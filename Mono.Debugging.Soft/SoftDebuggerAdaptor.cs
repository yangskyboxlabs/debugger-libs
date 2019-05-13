// 
// SoftDebuggerAdaptor.cs
//  
// Authors: Lluis Sanchez Gual <lluis@novell.com>
//          Jeffrey Stedfast <jeff@xamarin.com>
// 
// Copyright (c) 2009 Novell, Inc (http://www.novell.com)
// Copyright (c) 2011,2012 Xamain Inc. (http://www.xamarin.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Debugger.Soft;
using Mono.Debugger.Soft.RuntimeInvocation;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation;
using Mono.Debugging.Evaluation.Extension;
using Mono.Debugging.Evaluation.OverloadResolution;
using Mono.Debugging.Evaluation.RuntimeInvocation;

namespace Mono.Debugging.Soft
{
    public class SoftDebuggerAdaptor : ObjectValueAdaptor<TypeMirror, Value>
    {
        static readonly Dictionary<Type, OpCode> convertOps = new Dictionary<Type, OpCode>();

        delegate object TypeCastDelegate(object value);

        static SoftDebuggerAdaptor()
        {
            convertOps.Add(typeof(double), OpCodes.Conv_R8);
            convertOps.Add(typeof(float), OpCodes.Conv_R4);
            convertOps.Add(typeof(ulong), OpCodes.Conv_U8);
            convertOps.Add(typeof(uint), OpCodes.Conv_U4);
            convertOps.Add(typeof(ushort), OpCodes.Conv_U2);
            convertOps.Add(typeof(char), OpCodes.Conv_U2);
            convertOps.Add(typeof(byte), OpCodes.Conv_U1);
            convertOps.Add(typeof(long), OpCodes.Conv_I8);
            convertOps.Add(typeof(int), OpCodes.Conv_I4);
            convertOps.Add(typeof(short), OpCodes.Conv_I2);
            convertOps.Add(typeof(sbyte), OpCodes.Conv_I1);
        }

        public SoftDebuggerAdaptor(
            IMethodResolver<TypeMirror> methodResolver,
            ExpressionEvaluator<TypeMirror, Value> expressionEvaluator)
            : base(methodResolver, expressionEvaluator)
        {
            SpecialSymbolHelper = new SoftSpecialSymbolHelper();
        }

        public SoftDebuggerSession Session
        {
            get { return (SoftDebuggerSession)DebuggerSession; }
            set => DebuggerSession = value;
        }

        public SoftSpecialSymbolHelper SpecialSymbolHelper { get; }

        public SoftRuntimeInvocator Invocator => (SoftRuntimeInvocator)base.Invocator;

        string InvokeToString(SoftEvaluationContext ctx, MethodMirror method, Value obj)
        {
            try
            {
                if (Invocator.RuntimeInvoke(ctx, method, null, obj, new Value [0]).Result is StringMirror res)
                {
                    return MirrorStringToString(ctx, res);
                }

                return null;
            }
            catch
            {
                return GetDisplayTypeName(GetValueTypeName(ctx, obj));
            }
        }

        public override string CallToString(EvaluationContext ctx, Value obj)
        {
            if (obj == null)
                return null;

            var str = obj as StringMirror;
            if (str != null)
                return str.Value;

            var em = obj as EnumMirror;
            if (em != null)
                return em.StringValue;

            var primitive = obj as PrimitiveValue;
            if (primitive != null)
                return primitive.Value.ToString();

            var pointer = obj as PointerValue;
            if (pointer != null)
                return string.Format("0x{0:x}", pointer.Address);

            var cx = (SoftEvaluationContext)ctx;
            var sm = obj as StructMirror;
            var om = obj as ObjectMirror;

            if (sm != null && sm.Type.IsPrimitive)
            {
                // Boxed primitive
                if (sm.Fields.Length > 0 && (sm.Fields[0] is PrimitiveValue))
                    return ((PrimitiveValue)sm.Fields[0]).Value.ToString();
            }
            else if (om != null && cx.Options.AllowTargetInvoke)
            {
                var method = OverloadResolve(this, cx, om.Type, "ToString", null, new ArgumentType[0], true, false, false);
                if (method != null && method.DeclaringType.FullName != "System.Object")
                    return InvokeToString(cx, method, obj);
            }
            else if (sm != null && cx.Options.AllowTargetInvoke)
            {
                var method = OverloadResolve(this, cx, sm.Type, "ToString", null, new ArgumentType [0], true, false, false);
                if (method != null && method.DeclaringType.FullName != "System.ValueType")
                    return InvokeToString(cx, method, obj);
            }

            return GetDisplayTypeName(GetValueTypeName(ctx, obj));
        }

        public override Value TryConvert(
            EvaluationContext ctx,
            Value obj,
            TypeMirror targetType)
        {
            var res = TryCast(ctx, obj, targetType);

            if (res != null || obj == null)
                return res;

            try
            {
                if (obj is PrimitiveValue primitive)
                {
                    var tm = System.Convert.ChangeType(primitive.Value, Type.GetType(targetType.FullName, false));
                    return CreateValue(ctx, tm);
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        static Dictionary<string, TypeCastDelegate> typeCastDelegatesCache = new Dictionary<string, TypeCastDelegate>();

        static TypeCastDelegate GenerateTypeCastDelegate(string methodName, Type fromType, Type toType)
        {
            lock (typeCastDelegatesCache)
            {
                TypeCastDelegate cached;
                if (typeCastDelegatesCache.TryGetValue(methodName, out cached))
                    return cached;
                var argTypes = new[] { typeof(object) };
                var method = new DynamicMethod(methodName, typeof(object), argTypes, true);
                ILGenerator il = method.GetILGenerator();
                ConstructorInfo ctorInfo;
                MethodInfo methodInfo;
                OpCode conv;

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Unbox_Any, fromType);

                if (fromType.IsSubclassOf(typeof(Nullable)))
                {
                    PropertyInfo propInfo = fromType.GetProperty("Value");
                    methodInfo = propInfo.GetGetMethod();

                    il.Emit(OpCodes.Stloc_0);
                    il.Emit(OpCodes.Ldloca_S);
                    il.Emit(OpCodes.Call, methodInfo);

                    fromType = methodInfo.ReturnType;
                }

                if (!convertOps.TryGetValue(toType, out conv))
                {
                    argTypes = new[] { fromType };

                    if (toType == typeof(string))
                    {
                        methodInfo = fromType.GetMethod("ToString", new Type [0]);
                        il.Emit(OpCodes.Call, methodInfo);
                    }
                    else if ((methodInfo = toType.GetMethod("op_Explicit", argTypes)) != null)
                    {
                        il.Emit(OpCodes.Call, methodInfo);
                    }
                    else if ((methodInfo = toType.GetMethod("op_Implicit", argTypes)) != null)
                    {
                        il.Emit(OpCodes.Call, methodInfo);
                    }
                    else if ((ctorInfo = toType.GetConstructor(argTypes)) != null)
                    {
                        il.Emit(OpCodes.Call, ctorInfo);
                    }
                    else
                    {
                        // No idea what else to try...
                        throw new InvalidCastException();
                    }
                }
                else
                {
                    il.Emit(conv);
                }

                il.Emit(OpCodes.Box, toType);
                il.Emit(OpCodes.Ret);
                cached = (TypeCastDelegate)method.CreateDelegate(typeof(TypeCastDelegate));
                typeCastDelegatesCache[methodName] = cached;
                return cached;
            }
        }

        static object DynamicCast(object value, Type target)
        {
            var methodName = $"CastFrom{value.GetType().Name}To{target.Name}";
            return GenerateTypeCastDelegate(methodName, value.GetType(), target)(value);
        }

        static bool CanForceCast(
            SoftDebuggerAdaptor adaptor,
            EvaluationContext ctx,
            ArgumentType fromType,
            TypeMirror toType)
        {
            return adaptor.MethodResolver.ResolveUserConversionOperator(ctx, fromType, toType).IsSuccess();

//            var cx = (SoftEvaluationContext)ctx;
//            MethodMirror method;
//
//            if (CanCast(ctx, fromType, toType))
//                return true;
//
//            // check for explicit cast operators in the target type
//            method = OverloadResolve(cx, toType, "op_Explicit", null, new[] { fromType }, false, true, false, false);
//            if (method != null)
//                return true;
//
//            // check for explicit cast operators on the source type
//            method = OverloadResolve(cx, fromType.Type, "op_Explicit", null, toType, new[] { fromType }, false, true, false, false);
//            if (method != null)
//                return true;
//
//            method = OverloadResolve(cx, toType, ".ctor", null, new[] { fromType }, true, false, false, false);
//            if (method != null)
//                return true;
//
//            return false;
        }

        static bool CanCast(
            SoftDebuggerAdaptor adaptor,
            EvaluationContext ctx,
            ArgumentType fromType,
            TypeMirror toType)
        {
            var cx = (SoftEvaluationContext)ctx;
            MethodMirror method;

            // check for implicit cast operators in the target type
            method = OverloadResolve(adaptor, cx, toType, "op_Implicit", null, new[] { fromType }, false, true, false, false);
            if (method != null)
                return true;

            // check for implicit cast operators on the source type
            method = OverloadResolve(adaptor, cx, fromType.Type, "op_Implicit", null, toType, new[] { fromType }, false, true, false, false);
            if (method != null)
                return true;

            return false;
        }

        Value TryForceCast(EvaluationContext ctx, Value value, TypeMirror fromType, TypeMirror toType)
        {
            var resolutionResult = MethodResolver.ResolveUserConversionOperator(ctx, fromType, toType);
            if (!resolutionResult.IsSuccess())
                return null;

            InvocationInfo<Value> staticCallInfo = resolutionResult.ToStaticCallInfo(value);
            return Invocator.RuntimeInvoke(ctx, staticCallInfo).Result;
        }

        public override Value TryCast(EvaluationContext ctx, Value val, TypeMirror toType)
        {
            var cx = (SoftEvaluationContext)ctx;

            if (val == null || toType == null)
                return null;

            if (val is DelayedLambdaValue value)
                return CompileAndLoadLambdaValue(cx, value, toType, out _);

            var valueType = GetValueType(ctx, val);
            if (valueType == null)
            {
                return null;
            }

            // Try casting the primitive type of the enum
            if (val is EnumMirror enumMirror)
                return TryCast(ctx, CreateValue(ctx, enumMirror.Value), toType);

            if (toType.IsAssignableFrom(valueType))
                return val;

            var fromType = valueType;

//            // If we are trying to cast into non-primitive/enum value(e.g. System.nint)
//            // that class might have implicit operator and this must be handled via TypeMirrors
//            if (!toType.IsPrimitive && !toType.IsEnum)
//                fromType = valueType;

            if (val is PrimitiveValue primitiveValue1)
            {
                var obj = primitiveValue1.Value;
                if (obj == null)
                {
                    if (toType.IsValueType)
                        return null;
                    return new PrimitiveValue(null, toType);
                }

                if (toType.IsPrimitive)
                {
                    Type type = Type.GetType(toType.FullName, false);
                    if (!(type == null))
                        return new PrimitiveValue(DynamicCast(obj, type), toType);
                    DebuggerLoggingService.LogMessage("Can't get primitive type " + toType.FullName);
                    return null;
                }

                if (toType.IsEnum)
                {
                    if (TryCast(ctx, val, toType.EnumUnderlyingType) is PrimitiveValue primitiveValue2)
                        return cx.Session.VirtualMachine.CreateEnumMirror(toType, primitiveValue2);
                    return null;
                }
            }

            if (fromType.IsGenericType && fromType.FullName.StartsWith("System.Nullable`1", StringComparison.Ordinal))
            {
                var method = MethodResolver.ResolveInstanceMethod(cx, "get_Value", fromType, null, Array.Empty<TypeMirror>());
                if (method.IsSuccess())
                {
                    Value result = Invocator.RuntimeInvoke(ctx, method.ToInstanceCallInfo(val, EmptyValueArray)).Result;
                    return TryCast(ctx, result, toType);
                }
            }

            if (toType.IsGenericType && toType.FullName.StartsWith("System.Nullable`1", StringComparison.Ordinal))
            {
                if (val is PrimitiveValue primitiveVal && primitiveVal.Value == null)
                {
                    val = CreateValue(ctx, toType);
                }
                else
                {
                    val = CreateValue(ctx, toType, val);
                }

                return val;
            }

            return TryForceCast(ctx, val, fromType, toType);
        }

        public override IStringAdaptor CreateStringAdaptor(EvaluationContext ctx, Value str)
        {
            return new StringAdaptor((StringMirror)str);
        }

        public override ICollectionAdaptor<TypeMirror, Value> CreateArrayAdaptor(
            EvaluationContext ctx,
            Value arr)
        {
            return new ArrayAdaptor((ArrayMirror)arr);
        }

        public override Value CreateNullValue(EvaluationContext ctx, TypeMirror type)
        {
            return new PrimitiveValue(null, type);
        }

        public override Value CreateTypeObject(EvaluationContext ctx, TypeMirror type)
        {
            return type.GetTypeObject();
        }

        protected override Value CreatePrimitiveValue(EvaluationContext ctx, object value)
        {
            var cx = (SoftEvaluationContext)ctx;
            return cx.Session.VirtualMachine.CreateValue(value, cx.Domain);
        }

        protected override Value CreateStringValue(EvaluationContext ctx, string value)
        {
            throw new NotImplementedException();
        }

        public override Value CreateValue(
            EvaluationContext ctx,
            TypeMirror type,
            params Value[] argValues)
        {
            ctx.AssertTargetInvokeAllowed();

            var cx = (SoftEvaluationContext)ctx;
            var tm = type;

            var types = new ArgumentType [argValues.Length];
            var values = new Value[argValues.Length];

            for (int n = 0; n < argValues.Length; n++)
            {
                types[n] = GetValueType(ctx, argValues[n]);
            }

            var method = OverloadResolve(this, cx, tm, ".ctor", null, types, true, false, false);

            if (method != null)
            {
                var mparams = method.GetParameters();

                for (int n = 0; n < argValues.Length; n++)
                {
                    var param_type = mparams[n].ParameterType;

                    if (param_type.FullName != types[n].Type.FullName && !param_type.IsAssignableFrom(types[n].Type) && param_type.IsGenericType)
                    {
                        /* TODO: Add genericTypeArgs and handle this
                        bool throwCastException = true;

                        if (method.VirtualMachine.Version.AtLeast (2, 15)) {
                            var args = param_type.GetGenericArguments ();

                            if (args.Length == genericTypes.Length) {
                                var real_type = soft.Adapter.GetType (soft, param_type.GetGenericTypeDefinition ().FullName, genericTypes);

                                values [n] = (Value)TryCast (soft, (Value)argValues [n], real_type);
                                if (!(values [n] == null && argValues [n] != null && !soft.Adapter.IsNull (soft, argValues [n])))
                                    throwCastException = false;
                            }
                        }

                        if (throwCastException) {
                            string fromType = !IsGeneratedType (types [n]) ? soft.Adapter.GetDisplayTypeName (soft, types [n]) : types [n].FullName;
                            string toType = soft.Adapter.GetDisplayTypeName (soft, param_type);

                            throw new EvaluatorException ("Argument {0}: Cannot implicitly convert `{1}' to `{2}'", n, fromType, toType);
                        }*/
                    }
                    else if (param_type.FullName != types[n].Type.FullName && !param_type.IsAssignableFrom(types[n].Type) && CanForceCast(this, ctx, types[n], param_type))
                    {
                        values[n] = TryCast(ctx, argValues[n], param_type);
                    }
                    else if (param_type.FullName != types[n].Type.FullName && !param_type.IsAssignableFrom(types[n].Type) && CanDoPrimaryCast(types[n], param_type))
                    {
                        values[n] = TryConvert(ctx, argValues[n], param_type);
                    }
                    else
                    {
                        values[n] = argValues[n];
                    }
                }

                lock (method.VirtualMachine)
                {
                    return tm.NewInstance(cx.Thread, method, values);
                }
            }

            if (argValues.Length == 0 && tm.VirtualMachine.Version.AtLeast(2, 31))
                return tm.NewInstance();

            string typeName = GetDisplayTypeName(ctx, type);

            throw new EvaluatorException("Constructor not found for type `{0}'.", typeName);
        }

        public override Value CreateDelayedLambdaValue(EvaluationContext ctx, string expression, Tuple<string, Value>[] localVariables)
        {
            var soft = (SoftEvaluationContext)ctx;
            Tuple<string, Value>[] locals = null;

            if (localVariables != null)
            {
                locals = new Tuple<string, Value> [localVariables.Length];
                for (int i = 0; i < localVariables.Length; i++)
                {
                    var pair = localVariables[i];
                    var name = pair.Item1;
                    var val = (Value)pair.Item2;
                    locals[i] = Tuple.Create(name, val);
                }
            }

            return new DelayedLambdaValue(soft.Session.VirtualMachine, locals, expression);
        }

        public override Value CreateByteArray(EvaluationContext ctx, byte[] byts)
        {
            var byteType = GetType(ctx, "System.Byte");
            Value arr = CreateNewArray(ctx, byteType, byts.Length);
            SetByteArray(ctx, arr, byts);
            return arr;
        }

        public override void SetByteArray(EvaluationContext ctx, Value array, byte[] values)
        {
            if (values.Length == 0 || !(array is ArrayMirror arrayMirror))
                return;
            arrayMirror.SetByteValues(values);
        }

        private Value CompileAndLoadLambdaValue(SoftEvaluationContext ctx, DelayedLambdaValue val, TypeMirror toType, out string compileError)
        {
            if (!val.IsAcceptableType(toType))
            {
                compileError = null;
                return null;
            }

            string typeName = val.GetLiteralType(toType);
            byte[] bytes = CompileLambdaExpression(ctx, val.DelayedType, typeName, out compileError);

            return LoadLambdaValue(ctx, val.DelayedType, bytes);
        }

        private Compilation CreateLibraryCompilation(SoftEvaluationContext ctx, string assemblyName, bool enableOptimisations)
        {
            // Add references to assembly of debugee
            var assems = ctx.Domain.GetAssemblies();
            var references = new List<MetadataReference>();
            foreach (var assem in assems)
            {
                try
                {
                    var location = assem.Location;
                    if (System.IO.Path.IsPathRooted(location))
                    {
                        var meta = MetadataReference.CreateFromFile(location);
                        references.Add(meta);
                    }
                }
                catch (ArgumentException)
                {
                    // When assembly location path is invalid.
                    continue;
                }
            }

            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: enableOptimisations ? OptimizationLevel.Release : OptimizationLevel.Debug);

            return CSharpCompilation.Create(assemblyName, options: options)
                .AddReferences(references);
        }

        private byte[] CompileLambdaExpression(SoftEvaluationContext ctx, DelayedLambdaType val, string typeName, out string error)
        {
            string className = val.Name;
            string assemblyNamePrefix = "lambdaAssem";
            string assemblyName = assemblyNamePrefix + className;
            string lambdaExpression = val.Expression;

            int startOfExp, endOfExp;
            error = null;

            Tuple<string, Value>[] values = val.Locals;
            string[] paramLiterals = new string [values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                var v = values[i];
                var typ = GetValueType(ctx, v.Item2);
                var typ2 = ToTypeMirror(ctx, typ);
                var typ3 = GetDisplayTypeName(typ2.FullName);
                Console.WriteLine(typ3);
                paramLiterals[i] = typ3 + " " + v.Item1;
            }

            string paramLiteral = string.Join(",", paramLiterals);

            var sb = new System.Text.StringBuilder();
            sb.Append("public class ");
            sb.Append(className);
            sb.Append("{public static ");
            sb.Append(typeName);
            sb.Append(" injected_fn(" + paramLiteral + ") {");
            sb.Append("return (");
            startOfExp = sb.Length;
            sb.Append(lambdaExpression);
            endOfExp = startOfExp + lambdaExpression.Length;
            sb.Append(");");
            sb.Append("}}");

            var options = new CSharpParseOptions(kind: SourceCodeKind.Regular);
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(sb.ToString(), options);

            IEnumerable<SyntaxTree> trees = new[] { syntaxTree };
            Compilation compilation = CreateLibraryCompilation(ctx, assemblyName, true).AddSyntaxTrees(trees);

            var stream = new System.IO.MemoryStream();
            var compileResult = compilation.Emit(stream);
            if (compileResult.Success)
                return stream.ToArray();

            // Take an error message only if its error is due to lambda expression that is
            // inputted by user.
            var diagnostics = compileResult.Diagnostics;
            var dx = diagnostics.Length != 0 ? diagnostics[0] : null;

            if (dx != null && dx.Severity == DiagnosticSeverity.Error)
            {
                var location = dx.Location;
                var start = location.SourceSpan.Start;
                var end = location.SourceSpan.End;
                if (startOfExp <= start && end <= endOfExp)
                    error = dx.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        private Value LoadLambdaValue(SoftEvaluationContext ctx, DelayedLambdaType typ, byte[] bytes)
        {
            if (bytes == null)
                return null;

            var assemblyType = GetType(ctx, "System.Reflection.Assembly");
            var byteArrayType = GetType(ctx, "System.Byte[]");
            var byteArrayValue = CreateByteArray(ctx, bytes);
            var argTypes = new[] { byteArrayType };
            var argValues = new[] { byteArrayValue };
            var asm = Invocator.InvokeStaticMethod(ctx, assemblyType, "Load", argTypes, argValues).Result;

            var stringType = GetType(ctx, "System.String");
            var classNameValue = CreateValue(ctx, typ.Name);
            argTypes = new[] { stringType };
            argValues = new[] { classNameValue };
            var injectedType = Invocator.InvokeInstanceMethod(ctx, asm, "GetType", argTypes, argValues).Result;

            var typeType = GetType(ctx, "System.Type");
            var methodNameValue = CreateValue(ctx, "injected_fn");
            argTypes = new[] { stringType };
            argValues = new[] { methodNameValue };
            var injectedFun = Invocator.InvokeInstanceMethod(ctx, injectedType, "GetMethod", argTypes, argValues).Result;

            var methodInfoType = GetType(ctx, "System.Reflection.MethodInfo");
            var objectType = GetType(ctx, "System.Object");
            var objectArrayType = GetType(ctx, "System.Object[]");
            var nullValue = CreateValue(ctx, null);
            var paramValues = typ.GetLocalValues();
            var paramValuesArray = CreateArray(ctx, objectType, paramValues);
            argTypes = new[] { objectType, objectArrayType };
            argValues = new[] { nullValue, paramValuesArray };
            return Invocator.InvokeInstanceMethod(ctx, injectedFun, "Invoke", argTypes, argValues).Result;
        }

        public override Value GetBaseValue(EvaluationContext ctx, Value val)
        {
            return val;
        }

        public override bool NullableHasValue(EvaluationContext ctx, TypeMirror type, Value obj)
        {
            var hasValue = GetMember(ctx, type, obj, "hasValue") ?? GetMember(ctx, type, obj, "has_value");

            return hasValue.ObjectValue.ToPrimitive<bool>();
        }

        public override ValueReference<TypeMirror, Value> NullableGetValue(EvaluationContext ctx, TypeMirror type, Value obj)
        {
            return GetMember(ctx, type, obj, "value");
        }

        public override TypeMirror GetEnclosingType(EvaluationContext ctx)
        {
            return ((SoftEvaluationContext)ctx).Frame.Method.DeclaringType;
        }

        public override string[] GetImportedNamespaces(EvaluationContext ctx)
        {
            var namespaces = new HashSet<string>();
            var cx = (SoftEvaluationContext)ctx;

            foreach (TypeMirror type in cx.Session.GetAllTypes())
                namespaces.Add(type.Namespace);

            var nss = new string [namespaces.Count];
            namespaces.CopyTo(nss);

            return nss;
        }

        public override ValueReference<TypeMirror, Value> GetIndexerReference(
            EvaluationContext ctx,
            Value target,
            TypeMirror type,
            Value[] indices)
        {
            var values = new Value [indices.Length];
            var types = new TypeMirror [indices.Length];
            for (int n = 0; n < indices.Length; n++)
            {
                types[n] = GetValueType(ctx, indices[n]);
                values[n] = indices[n];
            }

            var candidates = new List<ResolutionMethodInfo<TypeMirror, MethodMirror>>();
            var props = new List<PropertyInfoMirror>();
            var mirType = type;
            while (mirType != null)
            {
                foreach (PropertyInfoMirror prop in mirType.GetProperties())
                {
                    MethodMirror met = prop.GetGetMethod(true);
                    if (met != null &&
                        !met.IsStatic &&
                        met.GetParameters().Length > 0 &&
                        !(met.IsPrivate && met.IsVirtual))
                    {
                        //Don't use explicit interface implementation
                        candidates.Add(new ResolutionMethodInfo<TypeMirror, MethodMirror>
                        {
                            Method = met,
                            OwnerType = mirType,
                        });
                        props.Add(prop);
                    }
                }

                mirType = mirType.BaseType;
            }

            ResolutionResult<TypeMirror, MethodMirror> resolutionResult = ((IMethodResolver<TypeMirror, TypeMirror, MethodMirror>)MethodResolver).ResolveFromCandidateList(ctx, EmptyTypeArray, types, candidates);
            if (!resolutionResult.IsSuccess())
            {
                return null;
            }

            int i = candidates.IndexOf(resolutionResult.SelectedCandidate);

            var getter = props[i].GetGetMethod(true);

            return getter != null ? new PropertyValueReference(this, ctx, props[i], target, null, getter, values) : null;
        }

        public override ValueReference<TypeMirror, Value> GetIndexerReference(
            EvaluationContext ctx,
            Value target,
            Value[] indices)
        {
            TypeMirror valueType = GetValueType(ctx, target);
            return GetIndexerReference(ctx, target, valueType, indices);
        }

        static bool InGeneratedClosureOrIteratorType(EvaluationContext ctx)
        {
            var cx = (SoftEvaluationContext)ctx;

            if (cx.Frame.Method.IsStatic)
                return false;

            var tm = cx.Frame.Method.DeclaringType;

            return IsGeneratedType(tm);
        }

        static bool IsLocalFunction(EvaluationContext ctx)
        {
            return ((SoftEvaluationContext)ctx).Frame.Method.Name.IndexOf(">g__", StringComparison.Ordinal) > 0;
        }

        internal static bool IsGeneratedType(TypeMirror tm)
        {
            //
            // This should cover all C# generated special containers
            // - anonymous methods
            // - lambdas
            // - iterators
            // - async methods
            //
            // which allow stepping into
            //

            // Note: mcs uses the form <${NAME}>c__${KIND}${NUMBER} where the leading '<' seems to have been dropped in 3.4.x
            //       csc uses the form <${NAME}>d__${NUMBER}
            //		 roslyn uses the form <${NAME}>d

            return tm.Name.IndexOf(">c__", StringComparison.Ordinal) > 0 || tm.Name.IndexOf(">d", StringComparison.Ordinal) > 0;
        }

        internal static string GetNameFromGeneratedType(TypeMirror tm)
        {
            return tm.Name.Substring(1, tm.Name.IndexOf('>') - 1);
        }

        static bool IsHoistedThisReference(FieldInfoMirror field)
        {
            // mcs is "<>f__this" or "$this" (if in an async compiler generated type)
            // csc is "<>4__this"
            return field.Name == "$this" ||
                (field.Name.StartsWith("<>", StringComparison.Ordinal) &&
                    field.Name.EndsWith("__this", StringComparison.Ordinal));
        }

        static bool IsClosureReferenceField(FieldInfoMirror field)
        {
            // mcs is "$locvar"
            // old mcs is "<>f__ref"
            // csc is "CS$<>"
            // roslyn is "<>8__"
            return field.Name.StartsWith("CS$<>", StringComparison.Ordinal) ||
                field.Name.StartsWith("<>f__ref", StringComparison.Ordinal) ||
                field.Name.StartsWith("$locvar", StringComparison.Ordinal) ||
                field.Name.StartsWith("<>8__", StringComparison.Ordinal);
        }

        static bool IsClosureReferenceLocal(LocalVariable local)
        {
            if (local.Name == null)
                return false;

            // mcs is "$locvar" or starts with '<'
            // csc is "CS$<>"
            return local.Name.Length == 0 || local.Name[0] == '<' || local.Name.StartsWith("$locvar", StringComparison.Ordinal) ||
                local.Name.StartsWith("CS$<>", StringComparison.Ordinal);
        }

        static bool IsGeneratedTemporaryLocal(LocalVariable local)
        {
            // csc uses CS$ prefix for temporary variables and <>t__ prefix for async task-related state variables
            return local.Name != null && (local.Name.StartsWith("CS$", StringComparison.Ordinal) || local.Name.StartsWith("<>t__", StringComparison.Ordinal));
        }

        Dictionary<MethodMirror, PortablePdbData.SoftScope[]> methodScopeCache = new Dictionary<MethodMirror, PortablePdbData.SoftScope[]>();

        string GetHoistedIteratorLocalName(FieldInfoMirror field, SoftEvaluationContext cx)
        {
            //mcs captured args, of form <$>name
            if (field.Name.StartsWith("<$>", StringComparison.Ordinal))
            {
                return field.Name.Substring(3);
            }

            // csc, mcs locals of form <name>__#, where # represents index of scope
            // roslyn locals of form <name>5__#, where # represents index of scope
            if (field.Name[0] == '<')
            {
                int suffixLength = 3;
                var i = field.Name.IndexOf(">__", StringComparison.Ordinal);
                if (i == -1)
                {
                    suffixLength = 4;
                    i = field.Name.IndexOf(">5__", StringComparison.Ordinal);
                }

                if (i != -1 && field.VirtualMachine.Version.AtLeast(2, 43))
                {
                    if (int.TryParse(field.Name.Substring(i + suffixLength), out var scopeIndex) && scopeIndex > 0)
                    {
                        //0 means whole method scope
                        scopeIndex--; //Scope index is 1 based(not zero)
                        PortablePdbData.SoftScope[] scopes;
                        if (!methodScopeCache.TryGetValue(cx.Frame.Method, out scopes))
                        {
                            scopes = cx.Session.GetPdbData(cx.Frame.Method)?.GetHoistedScopes(cx.Frame.Method);
                            if (scopes == null || scopes.Length == 0) // If hoisted scopes are empty use normal scopes
                                scopes = cx.Frame.Method.GetScopes().Select(s => new PortablePdbData.SoftScope() { LiveRangeStart = s.LiveRangeStart, LiveRangeEnd = s.LiveRangeEnd }).ToArray();
                            methodScopeCache[cx.Frame.Method] = scopes;
                        }

                        if (scopeIndex < scopes.Length)
                        {
                            var scope = scopes[scopeIndex];
                            if (scope.LiveRangeStart > cx.Frame.Location.ILOffset || scope.LiveRangeEnd < cx.Frame.Location.ILOffset)
                                return null;
                        }
                    }
                }

                i = field.Name.IndexOf('>');
                if (i > 1)
                {
                    return field.Name.Substring(1, i - 1);
                }
            }

            return null;
        }

        static IEnumerable<ValueReference<TypeMirror, Value>> GetHoistedLocalVariables(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext cx,
            ValueReference<TypeMirror, Value> vthis,
            HashSet<FieldInfoMirror> alreadyVisited = null)
        {
            if (vthis == null)
                return new ValueReference<TypeMirror, Value> [0];
            Value val;
            try
            {
                val = vthis.Value;
            }
            catch (AbsentInformationException)
            {
                return new ValueReference<TypeMirror, Value> [0];
            }
            catch (EvaluatorException ex) when (ex.InnerException is AbsentInformationException)
            {
                return new ValueReference<TypeMirror, Value> [0];
            }

            if (adaptor.IsNull(cx, val))
                return new ValueReference<TypeMirror, Value> [0];

            var tm = vthis.Type;
            var isIterator = IsGeneratedType(tm);

            var list = new List<ValueReference<TypeMirror, Value>>();
            var type = vthis.Type;

            foreach (var field in type.GetFields())
            {
                if (IsHoistedThisReference(field))
                    continue;

                if (IsClosureReferenceField(field))
                {
                    alreadyVisited = alreadyVisited ?? new HashSet<FieldInfoMirror>();
                    if (alreadyVisited.Contains(field))
                        continue;
                    alreadyVisited.Add(field);
                    list.AddRange(GetHoistedLocalVariables(adaptor, cx, new FieldValueReference(adaptor, cx, field, val, type), alreadyVisited));
                    continue;
                }

                if (field.Name[0] == '<')
                {
                    if (isIterator)
                    {
                        var name = adaptor.GetHoistedIteratorLocalName(field, cx);

                        if (!string.IsNullOrEmpty(name))
                            list.Add(new FieldValueReference(adaptor, cx, field, val, type, name, ObjectValueFlags.Variable)
                            {
                                ParentSource = vthis
                            });
                    }
                }
                else if (!field.Name.Contains("$"))
                {
                    list.Add(new FieldValueReference(adaptor, cx, field, val, type, field.Name, ObjectValueFlags.Variable)
                    {
                        ParentSource = vthis
                    });
                }
            }

            return list;
        }

        static ValueReference<TypeMirror, Value> GetHoistedThisReference(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext cx)
        {
            try
            {
                var val = cx.Frame.GetThis();
                var type = adaptor.GetValueType(cx, val);
                return GetHoistedThisReference(adaptor, cx, type, val);
            }
            catch (AbsentInformationException) { }

            return null;
        }

        static ValueReference<TypeMirror, Value> GetHoistedThisReference(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext cx,
            TypeMirror type,
            Value val,
            HashSet<FieldInfoMirror> alreadyVisited = null)
        {
            foreach (var field in type.GetFields())
            {
                if (IsHoistedThisReference(field))
                    return new FieldValueReference(adaptor, cx, field, val, type, "this", ObjectValueFlags.Literal);

                if (IsClosureReferenceField(field))
                {
                    alreadyVisited = alreadyVisited ?? new HashSet<FieldInfoMirror>();
                    if (alreadyVisited.Contains(field))
                        continue;
                    alreadyVisited.Add(field);
                    var fieldRef = new FieldValueReference(adaptor, cx, field, val, type);
                    var thisRef = GetHoistedThisReference(adaptor, cx, field.FieldType, fieldRef.Value, alreadyVisited);
                    if (thisRef != null)
                        return thisRef;
                }
            }

            return null;
        }

        // if the local does not have a name, constructs one from the index
        static string GetLocalName(SoftEvaluationContext cx, LocalVariable local)
        {
            if (!string.IsNullOrEmpty(local.Name) || cx.SourceCodeAvailable)
                return local.Name;

            return "loc" + local.Index;
        }

        protected override ValueReference<TypeMirror, Value> OnGetLocalVariable(EvaluationContext ctx, string name)
        {
            var cx = (SoftEvaluationContext)ctx;

            if (InGeneratedClosureOrIteratorType(cx) || IsLocalFunction(cx))
                return FindByName(OnGetLocalVariables(cx), v => v.Name, name, ctx.CaseSensitive);

            try
            {
                LocalVariable local = null;

                if (!cx.SourceCodeAvailable)
                {
                    if (name.StartsWith("loc", StringComparison.Ordinal))
                    {
                        int idx;

                        if (int.TryParse(name.Substring(3), out idx))
                            local = cx.Frame.Method.GetLocals().FirstOrDefault(loc => loc.Index == idx);
                    }
                }
                else
                {
                    local = ctx.CaseSensitive
                        ? cx.Frame.GetVisibleVariableByName(name)
                        : FindByName(cx.Frame.GetVisibleVariables(), v => v.Name, name, false);
                }

                if (local != null)
                    return new VariableValueReference(this, ctx, GetLocalName(cx, local), local);

                return FindByName(OnGetLocalVariables(ctx), v => v.Name, name, ctx.CaseSensitive);
            }
            catch (AbsentInformationException)
            {
                return null;
            }
        }

        protected override IEnumerable<ValueReference<TypeMirror, Value>> OnGetLocalVariables(EvaluationContext ctx)
        {
            var cx = (SoftEvaluationContext)ctx;

            if (InGeneratedClosureOrIteratorType(cx))
            {
                ValueReference<TypeMirror, Value> vthis = GetThisReference(this, cx);
                return GetHoistedLocalVariables(this, cx, vthis).Union(GetLocalVariables(this, cx));
            }

            if (IsLocalFunction(cx))
            {
                VariableValueReference vthis = GetClosureReference(this, cx);

                // if there's no closure reference then it didn't capture anything
                if (vthis != null)
                {
                    return GetHoistedLocalVariables(this, cx, vthis).Union(GetLocalVariables(this, cx));
                }
            }

            return GetLocalVariables(this, cx);
        }

        static VariableValueReference GetClosureReference(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext cx)
        {
            foreach (var local in cx.Frame.Method.GetLocals())
            {
                if (IsClosureReferenceLocal(local))
                {
                    return new VariableValueReference(adaptor, cx, local.Name, local);
                }
            }

            return null;
        }

        static IEnumerable<ValueReference<TypeMirror, Value>> GetLocalVariables(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext cx)
        {
            LocalVariable[] locals;

            try
            {
                locals = cx.Frame.GetVisibleVariables().Where(x => !x.IsArg && (IsClosureReferenceLocal(x) && IsGeneratedType(x.Type) || !IsGeneratedTemporaryLocal(x))).ToArray();
            }
            catch (AbsentInformationException)
            {
                yield break;
            }

            if (locals.Length == 0)
                yield break;

            var batch = new LocalVariableBatch(cx.Frame, locals);

            for (int i = 0; i < locals.Length; i++)
            {
                if (IsClosureReferenceLocal(locals[i]) && IsGeneratedType(locals[i].Type))
                {
                    foreach (var gv in GetHoistedLocalVariables(adaptor, cx, new VariableValueReference(adaptor, cx, locals[i].Name, locals[i], batch)))
                    {
                        yield return gv;
                    }
                }
                else if (!IsGeneratedTemporaryLocal(locals[i]))
                {
                    yield return new VariableValueReference(adaptor, cx, GetLocalName(cx, locals[i]), locals[i], batch);
                }
            }
        }

        public override bool HasMember(EvaluationContext ctx, TypeMirror type, string memberName, BindingFlags bindingFlags)
        {
            var tm = (TypeMirror)type;

            while (tm != null)
            {
                var field = FindByName(tm.GetFields(), f => f.Name, memberName, ctx.CaseSensitive);

                if (field != null)
                    return true;

                var prop = FindByName(tm.GetProperties(), p => p.Name, memberName, ctx.CaseSensitive);

                if (prop != null)
                {
                    var getter = prop.GetGetMethod(bindingFlags.HasFlag(BindingFlags.NonPublic));
                    if (getter != null)
                        return true;
                }

                if (bindingFlags.HasFlag(BindingFlags.DeclaredOnly))
                    break;

                tm = tm.BaseType;
            }

            return false;
        }

        static bool IsAnonymousType(TypeMirror type)
        {
            return type.Name.StartsWith("<>__AnonType", StringComparison.Ordinal);
        }

        protected override ValueReference<TypeMirror, Value> GetMember(EvaluationContext ctx, TypeMirror t, Value co, string name)
        {
            return OnGetMember(ctx, null, t, co, name);
        }

        protected override ValueReference<TypeMirror, Value> OnGetMember(
            EvaluationContext ctx,
            IDebuggerHierarchicalObject objectSource,
            TypeMirror type,
            Value co,
            string name)
        {
            var tupleNames = GetTupleElementNames(objectSource, ctx);
            while (type != null)
            {
                var field = FindByName(type.GetFields(), f => MapTupleName(f.Name, tupleNames), name, ctx.CaseSensitive);

                if (field != null && (field.IsStatic || co != null))
                    return new FieldValueReference(this, ctx, field, co, type);

                var prop = FindByName(type.GetProperties(), p => p.Name, name, ctx.CaseSensitive);

                if (prop != null && (IsStatic(prop) || co != null))
                {
                    var getter = prop.GetGetMethod(true);

                    // Optimization: if the property has a CompilerGenerated backing field, use that instead.
                    // This way we avoid overhead of invoking methods on the debugee when the value is requested.
                    //But also check that this method is not virtual, because in that case we need to call getter to invoke override
                    if (!getter.IsVirtual)
                    {
                        string cgFieldName = $"<{prop.Name}>{(IsAnonymousType(type) ? "" : "k__BackingField")}";
                        if ((field = FindByName(type.GetFields(), f => f.Name, cgFieldName, true)) != null && IsCompilerGenerated(field))
                            return new FieldValueReference(this, ctx, field, co, type, prop.Name, ObjectValueFlags.Property);
                    }

                    // Backing field not available, so do things the old fashioned way.
                    return new PropertyValueReference(this, ctx, prop, co, type, getter, null);
                }

                if (type.IsInterface)
                {
                    foreach (var inteface in type.GetInterfaces())
                    {
                        var result = GetMember(ctx, inteface, co, name);
                        if (result != null)
                            return result;
                    }

                    //foreach above recursively checked all "base" interfaces
                    //nothing was found, quit, otherwise we would loop forever
                    return null;
                }

                type = type.BaseType;
            }

            return null;
        }

        static string MapTupleName(string name, string[] tupleNames)
        {
            if (tupleNames != null &&
                name.Length > 4 &&
                name.StartsWith("Item", StringComparison.Ordinal) &&
                int.TryParse(name.Substring(4), out var tupleIndex) &&
                tupleNames.Length >= tupleIndex &&
                tupleNames[tupleIndex - 1] != null)
                return tupleNames[tupleIndex - 1];
            else
                return name;
        }

        static bool IsCompilerGenerated(FieldInfoMirror field)
        {
            var attrs = field.GetCustomAttributes(true);
            var generated = GetAttribute<CompilerGeneratedAttribute>(attrs);

            return generated != null;
        }

        static bool IsStatic(PropertyInfoMirror prop)
        {
            var met = prop.GetGetMethod(true) ?? prop.GetSetMethod(true);

            return met.IsStatic;
        }

        static T FindByName<T>(IEnumerable<T> items, Func<T, string> getName, string name, bool caseSensitive)
        {
            T best = default(T);

            foreach (T item in items)
            {
                string itemName = getName(item);

                if (itemName == name)
                    return item;

                if (!caseSensitive && itemName.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                    best = item;
            }

            return best;
        }

        protected override IEnumerable<ValueReference<TypeMirror, Value>> GetMembers(EvaluationContext ctx, TypeMirror t, Value co, BindingFlags bindingFlags)
        {
            return GetMembers(ctx, null, t, co, bindingFlags);
        }

        static string[] GetTupleElementNames(
            IDebuggerHierarchicalObject source,
            EvaluationContext ctx)
        {
            switch (source)
            {
                case FieldValueReference field:
                    if (field.Type?.Name?.StartsWith("ValueTuple`", StringComparison.Ordinal) != true)
                        return null;
                    return field.GetTupleElementNames();
                case PropertyValueReference prop:
                    if (prop.Type?.Name?.StartsWith("ValueTuple`", StringComparison.Ordinal) != true)
                        return null;
                    return prop.GetTupleElementNames();
                case VariableValueReference variable:
                    if (variable.Type?.Name?.StartsWith("ValueTuple`", StringComparison.Ordinal) != true)
                        return null;
                    return variable.GetTupleElementNames((SoftEvaluationContext)ctx);
                default:
                    return null;
            }
        }

        static bool IsIEnumerable(TypeMirror type)
        {
            if (!type.IsInterface)
                return false;

            if (type.Namespace == "System.Collections" && type.Name == "IEnumerable")
                return true;

            if (type.Namespace == "System.Collections.Generic" && type.Name == "IEnumerable`1")
                return true;

            return false;
        }

        static ObjectValueFlags GetFlags(MethodMirror method)
        {
            var flags = ObjectValueFlags.Method;

            if (method.IsStatic)
                flags |= ObjectValueFlags.Global;

            if (method.IsPublic)
                flags |= ObjectValueFlags.Public;
            else if (method.IsPrivate)
                flags |= ObjectValueFlags.Private;
            else if (method.IsFamily)
                flags |= ObjectValueFlags.Protected;
            else if (method.IsFamilyAndAssembly)
                flags |= ObjectValueFlags.Internal;
            else if (method.IsFamilyOrAssembly)
                flags |= ObjectValueFlags.InternalProtected;

            return flags;
        }

        protected override CompletionData GetMemberCompletionData(EvaluationContext ctx, ValueReference<TypeMirror, Value> vr)
        {
            var properties = new HashSet<string>();
            var methods = new HashSet<string>();
            var fields = new HashSet<string>();
            var data = new CompletionData();
            var type = vr.Type;
            bool isEnumerable = false;

            while (type != null)
            {
                if (!isEnumerable && IsIEnumerable(type))
                    isEnumerable = true;

                bool isExternal = Session.IsExternalCode(type);

                foreach (var field in type.GetFields())
                {
                    if (field.IsStatic || field.IsSpecialName || (isExternal && !field.IsPublic) ||
                        IsClosureReferenceField(field) || IsCompilerGenerated(field))
                        continue;

                    if (fields.Add(field.Name))
                        data.Items.Add(new CompletionItem(field.Name, FieldValueReference.GetFlags(field)));
                }

                foreach (var property in type.GetProperties())
                {
                    var getter = property.GetGetMethod(true);

                    if (getter == null || getter.IsStatic || (isExternal && !getter.IsPublic))
                        continue;

                    if (properties.Add(property.Name))
                        data.Items.Add(new CompletionItem(property.Name, PropertyValueReference.GetFlags(property, getter)));
                }

                foreach (var method in type.GetMethods())
                {
                    if (method.IsStatic || method.IsConstructor || method.IsSpecialName || (isExternal && !method.IsPublic))
                        continue;

                    if (methods.Add(method.Name))
                        data.Items.Add(new CompletionItem(method.Name, GetFlags(method)));
                }

                if (type.BaseType == null && type.FullName != "System.Object")
                    type = GetType(ctx, "System.Object");
                else
                    type = type.BaseType;
            }

            type = vr.Type;
            foreach (var iface in type.GetInterfaces())
            {
                if (!isEnumerable && IsIEnumerable(iface))
                    isEnumerable = true;
            }

            if (isEnumerable)
            {
                // Look for LINQ extension methods...
                if (GetType(ctx, "System.Linq.Enumerable") is TypeMirror linq)
                {
                    foreach (var method in linq.GetMethods())
                    {
                        if (!method.IsStatic || method.IsConstructor || method.IsSpecialName || !method.IsPublic)
                            continue;

                        if (methods.Add(method.Name))
                            data.Items.Add(new CompletionItem(method.Name, ObjectValueFlags.Method | ObjectValueFlags.Public));
                    }
                }
            }

            data.ExpressionLength = 0;

            return data;
        }

        public override void GetNamespaceContents(EvaluationContext ctx, string namspace, out string[] childNamespaces, out string[] childTypes)
        {
            var soft = (SoftEvaluationContext)ctx;
            var types = new HashSet<string>();
            var namespaces = new HashSet<string>();
            var namspacePrefix = namspace.Length > 0 ? namspace + "." : "";

            foreach (var type in soft.Session.GetAllTypes())
            {
                if (type.Namespace == namspace || type.Namespace.StartsWith(namspacePrefix, StringComparison.InvariantCulture))
                {
                    namespaces.Add(type.Namespace);
                    types.Add(type.FullName);
                }
            }

            childNamespaces = new string [namespaces.Count];
            namespaces.CopyTo(childNamespaces);

            childTypes = new string [types.Count];
            types.CopyTo(childTypes);
        }

        protected override ObjectValue CreateObjectValueImpl(EvaluationContext ctx, IObjectValueSource source, ObjectPath path, Value obj, ObjectValueFlags flags)
        {
            try
            {
                return base.CreateObjectValueImpl(ctx, source, path, obj, flags);
            }
            catch (NotSupportedException e)
            {
                throw new EvaluatorException("Evaluation failed: {0}", e.Message);
            }
        }

        protected override IEnumerable<ValueReference<TypeMirror, Value>> OnGetParameters(EvaluationContext ctx)
        {
            var soft = (SoftEvaluationContext)ctx;
            LocalVariable[] locals;

            try
            {
                locals = soft.Frame.Method.GetLocals().Where(x => x.IsArg && !IsClosureReferenceLocal(x)).ToArray();
            }
            catch (AbsentInformationException)
            {
                yield break;
            }

            if (locals.Length == 0)
                yield break;

            var batch = new LocalVariableBatch(soft.Frame, locals);

            for (int i = 0; i < locals.Length; i++)
            {
                string name = !string.IsNullOrEmpty(locals[i].Name) ? locals[i].Name : "arg" + locals[i].Index;
                yield return new VariableValueReference(this, ctx, name, locals[i], batch);
            }
        }

        protected override ValueReference<TypeMirror, Value> OnGetThisReference(EvaluationContext ctx)
        {
            var cx = (SoftEvaluationContext)ctx;

            if (InGeneratedClosureOrIteratorType(cx))
                return GetHoistedThisReference(this, cx);

            return GetThisReference(this, cx);
        }

        static ValueReference<TypeMirror, Value> GetThisReference(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext ctx)
        {
            return ctx.Frame.Method.IsStatic ? null : new ThisValueReference(adaptor, ctx, ctx.Frame);
        }

        public override ValueReference<TypeMirror, Value> GetCurrentException(EvaluationContext ctx)
        {
            try
            {
                var cx = (SoftEvaluationContext)ctx;
                var exc = cx.Session.GetExceptionObject(cx.Thread);

                return exc != null ? LiteralValueReference.CreateTargetObjectLiteral(this, ctx, ctx.Options.CurrentExceptionTag, exc) : null;
            }
            catch (AbsentInformationException)
            {
                return null;
            }
        }

        public override bool IsGenericType(EvaluationContext ctx, TypeMirror type)
        {
            return type != null && type.IsGenericType;
        }

        public override IEnumerable<TypeMirror> GetGenericTypeArguments(EvaluationContext ctx, TypeMirror type)
        {
            return type.GetGenericArguments();
        }

        public override TypeMirror[] GetTypeArgs(EvaluationContext ctx, TypeMirror typeMirror)
        {
            if (typeMirror.VirtualMachine.Version.AtLeast(2, 15))
                return typeMirror.GetGenericArguments();

            // fall back to parsing them from the from the FullName
            List<string> names = new List<string>();
            string s = typeMirror.FullName;
            int i = s.IndexOf('`');

            if (i != -1)
            {
                i = s.IndexOf('[', i);
                if (i == -1)
                    return new TypeMirror [0];
                int si = ++i;
                int nt = 0;
                for (; i < s.Length && (nt > 0 || s[i] != ']'); i++)
                {
                    if (s[i] == '[')
                        nt++;
                    else if (s[i] == ']')
                        nt--;
                    else if (s[i] == ',' && nt == 0)
                    {
                        names.Add(s.Substring(si, i - si));
                        si = i + 1;
                    }
                }

                names.Add(s.Substring(si, i - si));
                TypeMirror[] types = new TypeMirror [names.Count];
                for (int n = 0; n < names.Count; n++)
                {
                    string tn = names[n];
                    if (tn.StartsWith("[", StringComparison.Ordinal))
                        tn = tn.Substring(1, tn.Length - 2);
                    types[n] = GetType(ctx, tn);
                    if (types[n] == null)
                        return new TypeMirror [0];
                }

                return types;
            }

            return new TypeMirror [0];
        }

        public override TypeMirror GetType(EvaluationContext ctx, string name, TypeMirror[] typeArgs)
        {
            var cx = (SoftEvaluationContext)ctx;

            int i = name.IndexOf(',');
            if (i != -1)
            {
                // Find first comma outside brackets
                int nest = 0;
                for (int n = 0; n < name.Length; n++)
                {
                    char c = name[n];
                    if (c == '[')
                        nest++;
                    else if (c == ']')
                        nest--;
                    else if (c == ',' && nest == 0)
                    {
                        name = name.Substring(0, n).Trim();
                        break;
                    }
                }
            }

            if (typeArgs != null && typeArgs.Length > 0)
            {
                string args = "";

                foreach (var argType in typeArgs)
                {
                    if (args.Length > 0)
                        args += ",";

                    string tn = argType.FullName + ", " + argType.Assembly.GetName();

                    if (tn.IndexOf(',') != -1)
                        tn = "[" + tn + "]";

                    args += tn;
                }

                name += "[" + args + "]";
            }

            var tm = cx.Session.GetType(name);
            if (tm != null)
                return tm;

            foreach (var asm in cx.Domain.GetAssemblies())
            {
                tm = asm.GetType(name, false, false);
                if (tm != null)
                    return tm;
            }

            var method = cx.Frame.Method;
            if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
            {
                var definition = method.GetGenericMethodDefinition();
                var names = definition.GetGenericArguments();
                var types = method.GetGenericArguments();

                for (i = 0; i < names.Length; i++)
                {
                    if (names[i].FullName == name)
                        return types[i];
                }
            }

            var declaringType = method.DeclaringType;
            if (declaringType.IsGenericType && !declaringType.IsGenericTypeDefinition)
            {
                var definition = declaringType.GetGenericTypeDefinition();
                var types = declaringType.GetGenericArguments();
                var names = definition.GetGenericArguments();

                for (i = 0; i < names.Length; i++)
                {
                    if (names[i].FullName == name)
                        return types[i];
                }
            }

            return null;
        }

        protected override TypeMirror GetBaseTypeWithAttribute(EvaluationContext ctx, TypeMirror type, TypeMirror attrType)
        {
            var atm = attrType as TypeMirror;
            var tm = type as TypeMirror;

            while (tm != null)
            {
                if (tm.GetCustomAttributes(atm, false).Any())
                    return tm;

                tm = tm.BaseType;
            }

            return null;
        }

        public override TypeMirror GetParentType(EvaluationContext ctx, TypeMirror type)
        {
            int plus = type.FullName.LastIndexOf('+');
            return plus != -1 ? GetType(ctx, type.FullName.Substring(0, plus)) : null;
        }

        public override void SetArray(EvaluationContext ctx, Value array, Value[] values)
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<TypeMirror> GetNestedTypes(EvaluationContext ctx, TypeMirror type)
        {
            foreach (var nested in type.GetNestedTypes())
                if (!IsGeneratedType(nested))
                    yield return nested;
        }

        public override IEnumerable<TypeMirror> GetImplementedInterfaces(EvaluationContext ctx, TypeMirror type)
        {
            foreach (var nested in type.GetInterfaces())
                yield return nested;
        }

        public override string GetTypeName(EvaluationContext ctx, TypeMirror type)
        {
            if (IsGeneratedType(type))
            {
                // Return the name of the container-type.
                return type.FullName.Substring(0, type.FullName.LastIndexOf('+'));
            }

            return type.FullName;
        }

        public override TypeMirror GetValueType(EvaluationContext ctx, Value val)
        {
            if (val == null)
            {
                return GetType(ctx, "System.Object");
            }

            return val.Type;
        }

        public override TypeMirror GetBaseType(EvaluationContext ctx, TypeMirror type)
        {
            return type?.BaseType;
        }

        public override bool HasMethodWithParamLength(EvaluationContext ctx, TypeMirror targetType, string methodName, BindingFlags flags, int paramLength)
        {
            var soft = (SoftEvaluationContext)ctx;
            var currentType = ToTypeMirror(ctx, targetType);
            bool allowInstance = (flags & BindingFlags.Instance) != 0;
            bool allowStatic = (flags & BindingFlags.Static) != 0;
            bool onlyPublic = (flags & BindingFlags.Public) != 0;

            while (currentType != null)
            {
                var methods = GetMethodsByName(soft, currentType, methodName);

                foreach (var method in methods)
                {
                    var parms = method.GetParameters();
                    if (parms.Length == paramLength && ((method.IsStatic && allowStatic) || (!method.IsStatic && allowInstance)))
                    {
                        if (onlyPublic && !method.IsPublic)
                            continue;
                        return true;
                    }
                }

                if (methodName == ".ctor")
                    break;

                if (currentType.BaseType == null && currentType.FullName != "System.Object")
                    currentType = GetType(soft, "System.Object");
                else
                    currentType = currentType.BaseType;
            }

            return false;
        }

        // Returns the best method from `candidates' using lambda types in
        // `argTypes'. Non lambda types in argtypes will be ignored. We make sure
        // lamdas have its exected type by compiling them. If a matched method is
        // found, return an array of tuples of (argument index, resolved argument
        // type for lambda) through 'resolved'.
        MethodMirror FindMatchedLambdaType(SoftEvaluationContext soft, MethodMirror[] candidates, ArgumentType[] argTypes, out Tuple<int, object>[] resolved)
        {
            resolved = null;
            if (candidates == null || argTypes == null)
                return null;

            var maxMatchCount = 0;

            for (int k = 0; k < argTypes.Length; k++)
            {
                var paramType = argTypes[k];
                if (argTypes[k].IsDelayed)
                    maxMatchCount++;
            }

            Tuple<int, object>[] resolvedBuf = new Tuple<int, object> [maxMatchCount];

            for (int k = 0; k < candidates.Length; k++)
            {
                var method = candidates[k];
                var mparams = method.GetParameters();
                resolvedBuf = new Tuple<int, object>[maxMatchCount];

                int matchCount = 0;
                for (int i = 0; i < mparams.Length; i++)
                {
                    var argType = argTypes[i];
                    if (!argType.IsDelayed)
                        continue;

                    var paramType = mparams[i].ParameterType;
                    var lambdaType = argType.DelayedType;

                    if (lambdaType == null || !lambdaType.IsAcceptableType(paramType))
                        continue;

                    var lite = lambdaType.GetLiteralType(paramType);
                    var bytes = CompileLambdaExpression(soft, lambdaType, lite, out _);

                    if (bytes != null)
                    {
                        resolvedBuf[matchCount] = Tuple.Create(i, (object)paramType);
                        matchCount++;
                    }
                }

                if (matchCount == maxMatchCount)
                {
                    resolved = resolvedBuf;
                    return method;
                }
            }

            return null;
        }

        public override bool IsExternalType(EvaluationContext ctx, TypeMirror type)
        {
            return type == null || ((SoftEvaluationContext)ctx).Session.IsExternalCode(type);
        }

        public override bool IsString(EvaluationContext ctx, Value val)
        {
            return val is StringMirror;
        }

        public override bool IsArray(EvaluationContext ctx, Value val)
        {
            return val is ArrayMirror;
        }

        public override bool IsValueType(TypeMirror type)
        {
            return type != null && type.IsValueType;
        }

        public override bool IsPrimitiveType(TypeMirror type)
        {
            if (!(type is TypeMirror tm))
                return false;
            if (tm.IsPrimitive)
                return true;
            if (tm.IsValueType && tm.Namespace == "System" && (tm.Name == "nfloat" || tm.Name == "nint"))
                return true;
            return false;
        }

        public override bool IsClass(EvaluationContext ctx, TypeMirror type)
        {
            return type != null && (type.IsClass || type.IsValueType) && !type.IsPrimitive;
        }

        public override bool IsNull(EvaluationContext ctx, Value val)
        {
            return val == null
                || val is PrimitiveValue primitiveValue && primitiveValue.Value == null
                || val is PointerValue pointerValue && pointerValue.Address == 0;
        }

        public override bool IsPrimitive(EvaluationContext ctx, Value val)
        {
            if (val is PrimitiveValue || val is StringMirror || val is PointerValue)
                return true;
            if (!(val is StructMirror sm))
                return false;
            if (sm.Type.IsPrimitive)
                return true;
            if (sm.Type.Namespace == "System" && (sm.Type.Name == "nfloat" || sm.Type.Name == "nint"))
                return true;
            return false;
        }

        public override bool IsPointer(EvaluationContext ctx, Value val)
        {
            return val is PointerValue;
        }

        public override bool IsEnum(EvaluationContext ctx, Value val)
        {
            return val is EnumMirror;
        }

        public override bool IsDelayedType(EvaluationContext ctx, TypeMirror type)
        {
            return type is DelayedLambdaType;
        }

        public override bool IsPublic(EvaluationContext ctx, TypeMirror type)
        {
            var tm = ToTypeMirror(ctx, type);

            return tm != null && tm.IsPublic;
        }

        protected override TypeDisplayData OnGetTypeDisplayData(EvaluationContext ctx, TypeMirror type)
        {
            Dictionary<string, DebuggerBrowsableState> memberData = null;
            var soft = (SoftEvaluationContext)ctx;
            bool isCompilerGenerated = false;
            string displayValue = null;
            string displayName = null;
            string displayType = null;
            string proxyType = null;

            try
            {
                var tm = (TypeMirror)type;

                foreach (var attr in tm.GetCustomAttributes(true))
                {
                    var attrName = attr.Constructor.DeclaringType.FullName;
                    switch (attrName)
                    {
                        case "System.Diagnostics.DebuggerDisplayAttribute":
                            var display = BuildAttribute<DebuggerDisplayAttribute>(attr);
                            displayValue = display.Value;
                            displayName = display.Name;
                            displayType = display.Type;
                            break;
                        case "System.Diagnostics.DebuggerTypeProxyAttribute":
                            var proxy = BuildAttribute<DebuggerTypeProxyAttribute>(attr);
                            proxyType = proxy.ProxyTypeName;
                            if (!string.IsNullOrEmpty(proxyType))
                                ForceLoadType(soft, proxyType);
                            break;
                        case "System.Runtime.CompilerServices.CompilerGeneratedAttribute":
                            isCompilerGenerated = true;
                            break;
                    }
                }

                foreach (var field in tm.GetFields())
                {
                    var attrs = field.GetCustomAttributes(true);
                    var browsable = GetAttribute<DebuggerBrowsableAttribute>(attrs);

                    if (browsable == null)
                    {
                        var generated = GetAttribute<CompilerGeneratedAttribute>(attrs);
                        if (generated != null)
                            browsable = new DebuggerBrowsableAttribute(DebuggerBrowsableState.Never);
                    }

                    if (browsable != null)
                    {
                        if (memberData == null)
                            memberData = new Dictionary<string, DebuggerBrowsableState>();
                        memberData[field.Name] = browsable.State;
                    }
                }

                foreach (var property in tm.GetProperties())
                {
                    var browsable = GetAttribute<DebuggerBrowsableAttribute>(property.GetCustomAttributes(true));
                    if (browsable != null)
                    {
                        if (memberData == null)
                            memberData = new Dictionary<string, DebuggerBrowsableState>();
                        memberData[property.Name] = browsable.State;
                    }
                }
            }
            catch (Exception ex)
            {
                soft.Session.WriteDebuggerOutput(true, ex.ToString());
            }

            return new TypeDisplayData(proxyType, displayValue, displayType, displayName, isCompilerGenerated, memberData);
        }

        static T GetAttribute<T>(CustomAttributeDataMirror[] attrs)
        {
            foreach (var attr in attrs)
            {
                if (attr.Constructor.DeclaringType.FullName == typeof(T).FullName)
                    return BuildAttribute<T>(attr);
            }

            return default(T);
        }

        public override bool IsTypeLoaded(EvaluationContext ctx, string typeName)
        {
            var soft = (SoftEvaluationContext)ctx;

            return soft.Session.GetType(typeName) != null;
        }

        public override bool IsTypeLoaded(EvaluationContext ctx, TypeMirror type)
        {
            var tm = (TypeMirror)type;

            if (tm.VirtualMachine.Version.AtLeast(2, 23))
                return tm.IsInitialized;

            return IsTypeLoaded(ctx, tm.FullName);
        }

        public override bool ForceLoadType(EvaluationContext ctx, TypeMirror type)
        {
            var soft = (SoftEvaluationContext)ctx;

            if (!type.VirtualMachine.Version.AtLeast(2, 23))
                return IsTypeLoaded(ctx, type.FullName);

            if (type.IsInitialized)
                return true;

            if (!type.Attributes.HasFlag(TypeAttributes.BeforeFieldInit))
                return false;

            MethodMirror cctor = OverloadResolve(this, soft, type, ".cctor", null, new ArgumentType[0], false, true, false);
            if (cctor == null)
                return true;

            try
            {
                lock (cctor.VirtualMachine)
                {
                    type.InvokeMethod(soft.Thread, cctor, new Value [0], InvokeOptions.DisableBreakpoints | InvokeOptions.SingleThreaded);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                soft.Session.StackVersion++;
            }

            return true;
        }

        static T BuildAttribute<T>(CustomAttributeDataMirror attr)
        {
            var args = new List<object>();
            Type type = typeof(T);

            foreach (var arg in attr.ConstructorArguments)
            {
                object val = arg.Value;

                if (val is TypeMirror)
                {
                    // The debugger attributes that take a type as parameter of the constructor have
                    // a corresponding constructor overload that takes a type name. We'll use that
                    // constructor because we can't load target types in the debugger process.
                    // So what we do here is convert the Type to a String.
                    var tm = (TypeMirror)val;

                    // Workaround for older Mono runtime that doesn't support generics, simply use ICollection viewer instead generic version
                    if (!tm.VirtualMachine.Version.AtLeast(2, 15) && tm.FullName.StartsWith("System.Collections.Generic.CollectionDebuggerView`", StringComparison.Ordinal))
                        val = "System.Collections.CollectionDebuggerView, " + tm.Assembly.ManifestModule.Name;
                    else
                        val = tm.FullName + ", " + tm.Assembly.ManifestModule.Name;
                }
                else if (val is EnumMirror)
                {
                    var em = (EnumMirror)val;

                    if (type == typeof(DebuggerBrowsableAttribute))
                        val = (DebuggerBrowsableState)em.Value;
                    else
                        val = em.Value;
                }

                args.Add(val);
            }

            var attribute = Activator.CreateInstance(type, args.ToArray());

            foreach (var arg in attr.NamedArguments)
            {
                object val = arg.TypedValue.Value;
                string postFix = "";

                if (arg.TypedValue.ArgumentType == typeof(Type))
                    postFix = "TypeName";
                if (arg.Field != null)
                    type.GetField(arg.Field.Name + postFix).SetValue(attribute, val);
                else if (arg.Property != null)
                    type.GetProperty(arg.Property.Name + postFix).SetValue(attribute, val, null);
            }

            return (T)attribute;
        }

        public class ArgumentType
        {
            TypeMirror type;
            DelayedLambdaType delayedType;
            public bool RepresentsNull { get; internal set; }
            public bool IsDelayed { get; internal set; }

            public TypeMirror Type
            {
                get
                {
                    if (IsDelayed)
                        throw new NotSupportedException();
                    return type;
                }
                internal set { type = value; }
            }

            public DelayedLambdaType DelayedType
            {
                get
                {
                    if (!IsDelayed)
                        throw new NotSupportedException();
                    return delayedType;
                }
                internal set { delayedType = value; }
            }

            public static implicit operator TypeMirror(ArgumentType d)
            {
                return d.Type;
            }

            public static implicit operator ArgumentType(TypeMirror d)
            {
                return new ArgumentType() { Type = d };
            }
        }

        TypeMirror ToTypeMirror(EvaluationContext ctx, TypeMirror type)
        {
            return type ?? GetType(ctx, type.FullName);
        }

        static ArgumentType[] ResolveGenericTypeArguments(MethodMirror method, ArgumentType[] argTypes)
        {
            var genericArgs = method.GetGenericArguments();
            var types = new ArgumentType[genericArgs.Length];
            var parameters = method.GetParameters();
            var names = new List<string>();

            // map the generic argument type names
            foreach (var arg in genericArgs)
                names.Add(arg.Name);

            // map parameter types to generic argument types...
            for (int i = 0; i < argTypes.Length && i < parameters.Length; i++)
            {
                if (argTypes[i].IsDelayed)
                    continue;

                var paramType = parameters[i].ParameterType;
                var isArray = paramType.IsArray;
                if (isArray)
                    paramType = paramType.GetElementType();

                int index = names.IndexOf(paramType.Name);

                if (index != -1 && types[index] == null)
                {
                    var argType = argTypes[i].Type;
                    if (isArray && argType.IsArray)
                        argType = argType.GetElementType();

                    types[index] = argType;
                }
            }

            // make sure we have all the generic argument types...
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] == null)
                    return null;
            }

            return types;
        }

        static MethodMirror PickFirstCandidate(MethodMirror[] methods)
        {
            if (methods == null || methods.Length == 0)
            {
                return null;
            }
            else if (methods.Length == 1)
            {
                return methods[0];
            }
            else
            {
                // If there is an ambiguous match, just pick the first match. If the user was expecting
                // something else, he can provide more specific arguments

/*				if (!throwIfNotFound)
					return null;
				if (methodName != null)
					throw new EvaluatorException ("Ambiguous method `{0}'; need to use full name", methodName);
				else
					throw new EvaluatorException ("Ambiguous arguments for indexer.", methodName);
*/
                return methods[0];
            }
        }

        public static MethodMirror OverloadResolve(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext ctx,
            TypeMirror type,
            string methodName,
            ArgumentType[] genericTypeArgs,
            ArgumentType[] argTypes,
            bool allowInstance,
            bool allowStatic,
            bool throwIfNotFound,
            bool tryCasting = true)
        {
            return OverloadResolve(adaptor, ctx, type, methodName, genericTypeArgs, null, argTypes, allowInstance, allowStatic, throwIfNotFound, tryCasting);
        }

        public static MethodMirror OverloadResolve(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext ctx,
            TypeMirror type,
            string methodName,
            ArgumentType[] genericTypeArgs,
            TypeMirror returnType, ArgumentType[] argTypes, bool allowInstance, bool allowStatic, bool throwIfNotFound, bool tryCasting)
        {
            var results = OverloadResolveMulti(adaptor, ctx, type, methodName, genericTypeArgs, returnType, argTypes, allowInstance, allowStatic, throwIfNotFound, tryCasting: tryCasting);
            return PickFirstCandidate(results);
        }

        public static MethodMirror[] GetMethodsByName(SoftEvaluationContext ctx, TypeMirror type, string methodName)
        {
            const BindingFlags flag = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            MethodMirror[] methods = null;
            var cache = ctx.Session.OverloadResolveCache;

            if (ctx.CaseSensitive)
            {
                lock (cache)
                {
                    cache.TryGetValue(Tuple.Create(type, methodName), out methods);
                }
            }

            if (methods == null)
            {
                if (type.VirtualMachine.Version.AtLeast(2, 7))
                {
                    methods = type.GetMethodsByNameFlags(methodName, flag, !ctx.CaseSensitive);
                }
                else
                {
                    methods = type.GetMethods();
                }

                if (ctx.CaseSensitive)
                {
                    lock (cache)
                    {
                        cache[Tuple.Create(type, methodName)] = methods;
                    }
                }
            }

            return methods;
        }

        public static MethodMirror[] OverloadResolveMulti(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext ctx,
            TypeMirror type,
            string methodName,
            ArgumentType[] genericTypeArgs,
            TypeMirror returnType,
            ArgumentType[] argTypes,
            bool allowInstance,
            bool allowStatic,
            bool throwIfNotFound,
            bool tryCasting)
        {
            var candidates = new List<MethodMirror>();
            var currentType = type;

            while (currentType != null)
            {
                MethodMirror[] methods = GetMethodsByName(ctx, currentType, methodName);

                foreach (var method in methods)
                {
                    if (method.Name == methodName || !ctx.CaseSensitive && method.Name.Equals(methodName, StringComparison.CurrentCultureIgnoreCase))
                    {
                        MethodMirror actualMethod;

                        if (argTypes != null && method.VirtualMachine.Version.AtLeast(2, 24) && method.IsGenericMethod)
                        {
                            var generic = method.GetGenericMethodDefinition();
                            ArgumentType[] typeArgs;

                            //Console.WriteLine ("Attempting to resolve generic type args for: {0}", GetPrettyMethodName (ctx, generic));

                            if ((genericTypeArgs == null || genericTypeArgs.Length == 0))
                                typeArgs = ResolveGenericTypeArguments(generic, argTypes);
                            else
                                typeArgs = genericTypeArgs;

                            if (typeArgs == null || typeArgs.Length != generic.GetGenericArguments().Length)
                            {
                                //Console.WriteLine ("Failed to resolve generic method argument types...");
                                continue;
                            }

                            actualMethod = generic.MakeGenericMethod(typeArgs.Select(t => t.Type).ToArray());

                            //Console.WriteLine ("Resolve generic method to: {0}", GetPrettyMethodName (ctx, actualMethod));
                        }
                        else
                        {
                            actualMethod = method;
                        }

                        var parms = actualMethod.GetParameters();
                        if (argTypes == null || parms.Length == argTypes.Length && ((actualMethod.IsStatic && allowStatic) || (!actualMethod.IsStatic && allowInstance)))
                            candidates.Add(actualMethod);
                    }
                }

                if (argTypes == null && candidates.Count > 0)
                    break; // when argTypes is null, we are just looking for *any* match (not a specific match)

                if (methodName == ".ctor")
                    break; // Can't create objects using constructor from base classes

                // Make sure that we always pull in at least System.Object methods (this is mostly needed for cases where 'type' was an interface)
                if (currentType.BaseType == null && currentType.FullName != "System.Object")
                    currentType = adaptor.GetType(ctx, "System.Object");
                else
                    currentType = currentType.BaseType;
            }

            return OverloadResolveMulti(adaptor, ctx, type, methodName, genericTypeArgs, returnType, argTypes, candidates, throwIfNotFound, tryCasting);
        }

        static bool IsApplicable(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext ctx,
            MethodMirror method,
            ArgumentType[] genericTypeArgs,
            TypeMirror returnType,
            ArgumentType[] types,
            out string error,
            out int matchCount,
            bool tryCasting = true)
        {
            var mparams = method.GetParameters();
            matchCount = 0;

            for (int i = 0; i < types.Length; i++)
            {
                var param_type = mparams[i].ParameterType;

                if (types[i].RepresentsNull && !SoftEvaluationContext.IsValueTypeOrPrimitive(param_type))
                    continue;

                if (types[i].IsDelayed)
                {
                    var lambdaType = types[i].DelayedType;
                    if (lambdaType.IsAcceptableType(param_type))
                    {
                        matchCount++;
                    }

                    continue;
                }

                if (param_type.FullName == types[i].Type.FullName)
                {
                    matchCount++;
                    continue;
                }

                if (param_type.IsAssignableFrom(types[i].Type))
                    continue;

                if (param_type.IsGenericType)
                {
                    if (genericTypeArgs != null && method.VirtualMachine.Version.AtLeast(2, 12))
                    {
                        // FIXME: how can we make this more definitive?
                        if (param_type.GetGenericArguments().Length == genericTypeArgs.Length)
                            continue;
                    }
                    else
                    {
                        // no way to check... assume it'll work?
                        continue;
                    }
                }

                if (tryCasting && CanCast(adaptor, ctx, types[i], param_type))
                    continue;

                if (CanDoPrimaryCast(types[i].Type, param_type))
                    continue;

                string fromType = !IsGeneratedType(types[i].Type) ? adaptor.GetDisplayTypeName(ctx, types[i]) : types[i].Type.FullName;
                string toType = adaptor.GetDisplayTypeName(ctx, param_type);

                error = $"Argument {i}: Cannot implicitly convert `{fromType}' to `{toType}'";

                return false;
            }

            if (returnType != null && returnType != method.ReturnType)
            {
                string actual = adaptor.GetDisplayTypeName(ctx, method.ReturnType);
                string expected = adaptor.GetDisplayTypeName(ctx, returnType);

                error = $"Return types do not match: `{expected}' vs `{actual}'";

                return false;
            }

            error = null;

            return true;
        }

        static bool CanDoPrimaryCast(TypeMirror fromType, TypeMirror toType)
        {
            var name = toType.CSharpName;
            switch (fromType.CSharpName)
            {
                case "sbyte": return name == "short" || name == "int" || name == "long" || name == "float" || name == "double" || name == "decimal";
                case "byte": return name == "short" || name == "ushort" || name == "int" || name == "uint" || name == "long" || name == "ulong" || name == "float" || name == "double" || name == "decimal";
                case "short": return name == "int" || name == "long" || name == "float" || name == "double" || name == "decimal";
                case "ushort": return name == "int" || name == "uint" || name == "long" || name == "ulong" || name == "float" || name == "double" || name == "decimal";
                case "int": return name == "long" || name == "float" || name == "double" || name == "decimal";
                case "uint": return name == "long" || name == "ulong" || name == "float" || name == "double" || name == "decimal";
                case "long": return name == "float" || name == "double" || name == "decimal";
                case "char": return name == "ushort" || name == "int" || name == "uint" || name == "long" || name == "ulong" || name == "float" || name == "double" || name == "decimal";
                case "float": return name == "double";
                case "ulong": return name == "float" || name == "double" || name == "decimal";
            }

            return false;
        }

        static MethodMirror[] OverloadResolveMulti(
            SoftDebuggerAdaptor adaptor,
            SoftEvaluationContext ctx,
            TypeMirror type,
            string methodName,
            ArgumentType[] genericTypeArgs,
            TypeMirror returnType,
            ArgumentType[] argTypes,
            List<MethodMirror> candidates,
            bool throwIfNotFound,
            bool tryCasting = true)
        {
            if (candidates.Count == 0)
            {
                if (throwIfNotFound)
                {
                    string typeName = adaptor.GetDisplayTypeName(ctx, type);

                    if (methodName == null)
                        throw new EvaluatorException("Indexer not found in type `{0}'.", typeName);

                    if (genericTypeArgs != null && genericTypeArgs.Length > 0)
                    {
                        var types = string.Join(", ", genericTypeArgs.Select(t => adaptor.GetDisplayTypeName(ctx, t)));

                        throw new EvaluatorException("Method `{0}<{1}>' not found in type `{2}'.", methodName, types, typeName);
                    }

                    throw new EvaluatorException("Method `{0}' not found in type `{1}'.", methodName, typeName);
                }

                return null;
            }

            if (argTypes == null)
            {
                // This is just a probe to see if the type contains *any* methods of the given name
                return new[] { candidates[0] };
            }

            if (candidates.Count == 1)
            {
                string error;
                int matchCount;

                if (IsApplicable(adaptor, ctx, candidates[0], genericTypeArgs, returnType, argTypes, out error, out matchCount, tryCasting))
                    return new[] { candidates[0] };

                if (throwIfNotFound)
                    throw new EvaluatorException("Invalid arguments for method `{0}': {1}", methodName, error);

                return null;
            }

            // Ok, now we need to find exact matches.
            List<MethodMirror> bestCandidates = new List<MethodMirror>();
            int bestCount = -1;

            foreach (MethodMirror method in candidates)
            {
                string error;
                int matchCount;

                if (!IsApplicable(adaptor, ctx, method, genericTypeArgs, returnType, argTypes, out error, out matchCount, tryCasting))
                    continue;

                if (matchCount == bestCount)
                {
                    bestCandidates.Add(method);
                }
                else if (matchCount > bestCount)
                {
                    bestCandidates = new List<MethodMirror> { method };
                    bestCount = matchCount;
                }
            }

            if (bestCandidates.Count == 0)
            {
                if (!throwIfNotFound)
                    return null;

                if (methodName != null)
                    throw new EvaluatorException("Invalid arguments for method `{0}'.", methodName);

                throw new EvaluatorException("Invalid arguments for indexer.");
            }

            return bestCandidates.ToArray();
        }

        public override bool IsAtomicPrimitive(EvaluationContext ctx, Value value)
        {
            throw new NotImplementedException();
        }

        public override ValueType ToAtomicPrimitive(EvaluationContext ctx, Value value)
        {
            throw new NotImplementedException();
        }

        public override object TargetObjectToObject(EvaluationContext ctx, Value obj)
        {
            switch (obj)
            {
                case StringMirror stringMirror:
                    return MirrorStringToString(ctx, stringMirror);
                case PrimitiveValue primitiveValue:
                    return primitiveValue.Value;
                case PointerValue pointerValue:
                    return new EvaluationResult("0x" + pointerValue.Address.ToString("x"));
                case StructMirror sm when sm.Type.IsPrimitive:
                {
                    // Boxed primitive
                    if (sm.Type.FullName == "System.IntPtr")
                        return new EvaluationResult("0x" + ((long)((PrimitiveValue)sm.Fields[0]).Value).ToString("x"));
                    if (sm.Fields.Length > 0 && sm.Fields[0] is PrimitiveValue)
                        return ((PrimitiveValue)sm.Fields[0]).Value;
                    break;
                }

                case StructMirror sm:
                {
                    if (sm.Type.FullName == "System.Decimal")
                    {
                        Decimal? nullable = ObjectToDecimal(ctx, obj);
                        if (nullable.HasValue)
                            return nullable;
                    }

                    break;
                }
            }

            return base.TargetObjectToObject(ctx, obj);
        }

        static string MirrorStringToString(EvaluationContext ctx, StringMirror mirror)
        {
            string str;

            if (ctx.Options.EllipsizeStrings)
            {
                if (mirror.VirtualMachine.Version.AtLeast(2, 10))
                {
                    int length = mirror.Length;

                    if (length > ctx.Options.EllipsizedLength)
                        str = new string(mirror.GetChars(0, ctx.Options.EllipsizedLength)) + EvaluationOptions.Ellipsis;
                    else
                        str = mirror.Value;
                }
                else
                {
                    str = mirror.Value;
                    if (str.Length > ctx.Options.EllipsizedLength)
                        str = str.Substring(0, ctx.Options.EllipsizedLength) + EvaluationOptions.Ellipsis;
                }
            }
            else
            {
                str = mirror.Value;
            }

            return str;
        }
    }

//
//    class MethodCall : AsyncOperation
//    {
//        readonly InvokeOptions options = InvokeOptions.DisableBreakpoints | InvokeOptions.SingleThreaded;
//
//        readonly ManualResetEvent shutdownEvent = new ManualResetEvent(false);
//        readonly SoftEvaluationContext ctx;
//        readonly MethodMirror function;
//        readonly Value[] args;
//        readonly object obj;
//        IAsyncResult handle;
//        Exception exception;
//        InvokeResult result;
//
//        public MethodCall(SoftEvaluationContext ctx, MethodMirror function, object obj, Value[] args, bool enableOutArgs)
//        {
//            this.ctx = ctx;
//            this.function = function;
//            this.obj = obj;
//            this.args = args;
//            if (enableOutArgs)
//            {
//                this.options |= InvokeOptions.ReturnOutArgs;
//            }
//
//            if (function.VirtualMachine.Version.AtLeast(2, 40))
//            {
//                this.options |= InvokeOptions.Virtual;
//            }
//        }
//
//        public override string Description
//        {
//            get { return function.DeclaringType.FullName + "." + function.Name; }
//        }
//
//        public override void Invoke()
//        {
//            try
//            {
//                if (obj is ObjectMirror)
//                    handle = ((ObjectMirror)obj).BeginInvokeMethod(ctx.Thread, function, args, options, null, null);
//                else if (obj is TypeMirror)
//                    handle = ((TypeMirror)obj).BeginInvokeMethod(ctx.Thread, function, args, options, null, null);
//                else if (obj is StructMirror)
//                    handle = ((StructMirror)obj).BeginInvokeMethod(ctx.Thread, function, args, options | InvokeOptions.ReturnOutThis, null, null);
//                else if (obj is PrimitiveValue)
//                    handle = ((PrimitiveValue)obj).BeginInvokeMethod(ctx.Thread, function, args, options, null, null);
//                else
//                    throw new ArgumentException("Soft debugger method calls cannot be invoked on objects of type " + obj.GetType().Name);
//            }
//            catch (InvocationException ex)
//            {
//                ctx.Session.StackVersion++;
//                exception = ex;
//            }
//            catch (Exception ex)
//            {
//                ctx.Session.StackVersion++;
//                DebuggerLoggingService.LogError("Error in soft debugger method call thread on " + GetInfo(), ex);
//                exception = ex;
//            }
//        }
//
//        public override void Abort()
//        {
//            if (handle is IInvokeAsyncResult)
//            {
//                var info = GetInfo();
//                DebuggerLoggingService.LogMessage("Aborting invocation of " + info);
//                ((IInvokeAsyncResult)handle).Abort();
//
//                // Don't wait for the abort to finish. The engine will do it.
//            }
//            else
//            {
//                throw new NotSupportedException();
//            }
//        }
//
//        public override void Shutdown()
//        {
//            shutdownEvent.Set();
//        }
//
//        void EndInvoke()
//        {
//            try
//            {
//                if (obj is ObjectMirror)
//                    result = ((ObjectMirror)obj).EndInvokeMethodWithResult(handle);
//                else if (obj is TypeMirror)
//                    result = ((TypeMirror)obj).EndInvokeMethodWithResult(handle);
//                else if (obj is StructMirror)
//                    result = ((StructMirror)obj).EndInvokeMethodWithResult(handle);
//                else
//                    result = ((PrimitiveValue)obj).EndInvokeMethodWithResult(handle);
//            }
//            catch (InvocationException ex)
//            {
//                if (!Aborting && ex.Exception != null)
//                {
//                    string ename = GetValueTypeName(ctx, ex.Exception);
//                    var vref = GetMember(ctx, null, ex.Exception, "Message");
//
//                    exception = vref != null ? new Exception(ename + ": " + (string)vref.ObjectValue) : new Exception(ename);
//                    return;
//                }
//
//                exception = ex;
//            }
//            catch (Exception ex)
//            {
//                DebuggerLoggingService.LogError("Error in soft debugger method call thread on " + GetInfo(), ex);
//                exception = ex;
//            }
//            finally
//            {
//                ctx.Session.StackVersion++;
//            }
//        }
//
//        string GetInfo()
//        {
//            try
//            {
//                TypeMirror type = null;
//                if (obj is ObjectMirror)
//                    type = ((ObjectMirror)obj).Type;
//                else if (obj is TypeMirror)
//                    type = (TypeMirror)obj;
//                else if (obj is StructMirror)
//                    type = ((StructMirror)obj).Type;
//                return string.Format("method {0} on object {1}",
//                    function == null ? "[null]" : function.FullName,
//                    type == null ? "[null]" : type.FullName);
//            }
//            catch (Exception ex)
//            {
//                DebuggerLoggingService.LogError("Error getting info for SDB MethodCall", ex);
//                return "";
//            }
//        }
//
//        public override bool WaitForCompleted(int timeout)
//        {
//            if (handle == null)
//                return true;
//            int res = WaitHandle.WaitAny(new WaitHandle[] { handle.AsyncWaitHandle, shutdownEvent }, timeout);
//            if (res == 0)
//            {
//                EndInvoke();
//                return true;
//            }
//
//            // Return true if shut down.
//            return res == 1;
//        }
//
//        public Value ReturnValue
//        {
//            get
//            {
//                if (exception != null)
//                    throw new EvaluatorException(exception.Message);
//                return result.Result;
//            }
//        }
//
//        public Value[] OutArgs
//        {
//            get
//            {
//                if (exception != null)
//                    throw new EvaluatorException(exception.Message);
//                return result.OutArgs;
//            }
//        }
//    }
}
