// 
// ObjectValueAdaptor.cs
//  
// Authors: Lluis Sanchez Gual <lluis@novell.com>
//          Jeffrey Stedfast <jeff@xamarin.com>
// 
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
// Copyright (c) 2012 Xamarin Inc. (http://www.xamarin.com)
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
using System.Text;
using System.Threading;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation.Extension;
using Mono.Debugging.Evaluation.Presentation;
using Mono.Debugging.Evaluation.RuntimeInvocation;

namespace Mono.Debugging.Evaluation
{
    public abstract class ObjectValueAdaptor<TType, TValue> : IDisposable
        where TType : class
        where TValue : class
    {
        static readonly TType[] emptyTypeArray = new TType[0];
        static readonly TValue[] emptyValueArray = new TValue[0];
        readonly Dictionary<string, TypeDisplayData> typeDisplayData = new Dictionary<string, TypeDisplayData>();
        readonly object cancellationTokensLock = new object();
        readonly List<CancellationTokenSource> cancellationTokenSources = new List<CancellationTokenSource>();
        public IMethodResolver<TType> MethodResolver { get; }

        // Time to wait while evaluating before switching to async mode
        public int DefaultEvaluationWaitTime { get; set; }

        public event EventHandler<BusyStateEventArgs> BusyStateChanged;

        public IDebuggerSessionInternal<TType, TValue> DebuggerSession { get; set; }

        readonly AsyncEvaluationTracker asyncEvaluationTracker = new AsyncEvaluationTracker();
        readonly AsyncOperationManager asyncOperationManager = new AsyncOperationManager();
        public IRuntimeInvocator<TType, TValue> Invocator { get; protected internal set; }
        public IValuePresenter<TType, TValue> ValuePresenter { get; protected internal set; }

        public TType[] EmptyTypeArray => emptyTypeArray;
        public TValue[] EmptyValueArray => emptyValueArray;

        public ExpressionEvaluator<TType, TValue> Evaluator { get; }

        protected ObjectValueAdaptor(
            IMethodResolver<TType> methodResolver,
            ExpressionEvaluator<TType, TValue> evaluator)
        {
            this.MethodResolver = methodResolver;
            Evaluator = evaluator;
        }

        public void Dispose()
        {
            asyncEvaluationTracker.Dispose();
            asyncOperationManager.Dispose();
        }

        public ObjectValue CreateObjectValue(
            EvaluationContext ctx,
            IObjectValueSource source,
            ObjectPath path,
            TValue obj,
            ObjectValueFlags flags)
        {
            try
            {
                return CreateObjectValueImpl(ctx, source, path, obj, flags);
            }
            catch (EvaluatorAbortedException ex)
            {
                return ObjectValue.CreateFatalError(path.LastName, ex.Message, flags);
            }
            catch (EvaluatorException ex)
            {
                return ObjectValue.CreateFatalError(path.LastName, ex.Message, flags);
            }
            catch (Exception ex)
            {
                ctx.WriteDebuggerError(ex);
                return ObjectValue.CreateFatalError(path.LastName, ex.Message, flags);
            }
        }

        public virtual string GetDisplayTypeName(string typeName)
        {
            return TypeNamingUtil.GetDisplayTypeName(typeName.Replace('+', '.'), 0, typeName.Length);
        }

        public virtual string GetDisplayTypeName(EvaluationContext ctx, TType type)
        {
            return GetDisplayTypeName(GetTypeName(ctx, type));
        }

        public virtual void OnBusyStateChanged(BusyStateEventArgs e)
        {
            EventHandler<BusyStateEventArgs> evnt = BusyStateChanged;
            if (evnt != null)
                evnt(this, e);
        }

        public abstract ICollectionAdaptor<TType, TValue> CreateArrayAdaptor(EvaluationContext ctx, TValue arr);
        public abstract IStringAdaptor CreateStringAdaptor(EvaluationContext ctx, TValue str);

        public abstract bool IsNull(EvaluationContext ctx, TValue val);
        public abstract bool IsPrimitive(EvaluationContext ctx, TValue val);
        public abstract bool IsPointer(EvaluationContext ctx, TValue val);
        public abstract bool IsString(EvaluationContext ctx, TValue val);
        public abstract bool IsArray(EvaluationContext ctx, TValue val);
        public abstract bool IsEnum(EvaluationContext ctx, TValue val);
        public abstract bool IsValueType(TType type);

        public virtual bool IsPrimitiveType(TType type)
        {
            throw new NotImplementedException();
        }

        public abstract bool IsClass(EvaluationContext ctx, TType type);
        public abstract TValue TryCast(EvaluationContext ctx, TValue val, TType type);

        public abstract TType GetValueType(EvaluationContext ctx, TValue val);
        public abstract string GetTypeName(EvaluationContext ctx, TType type);
        public abstract TType[] GetTypeArgs(EvaluationContext ctx, TType type);
        public abstract TType GetBaseType(EvaluationContext ctx, TType type);

        public virtual bool IsDelayedType(EvaluationContext ctx, TType type)
        {
            return false;
        }

        public virtual bool IsGenericType(EvaluationContext ctx, TType type)
        {
            return type != null && GetTypeName(ctx, type).IndexOf('`') != -1;
        }

        public virtual IEnumerable<TType> GetGenericTypeArguments(EvaluationContext ctx, TType type)
        {
            yield break;
        }

        public virtual bool IsNullableType(EvaluationContext ctx, TType type)
        {
            return type != null && GetTypeName(ctx, type).StartsWith("System.Nullable`1", StringComparison.Ordinal);
        }

        public virtual bool NullableHasValue(EvaluationContext ctx, TType type, TValue obj)
        {
            ValueReference<TType, TValue> hasValue = GetMember(ctx, type, obj, "HasValue");

            return hasValue.ObjectValue.ToPrimitive<bool>();
        }

        public virtual ValueReference<TType, TValue> NullableGetValue(EvaluationContext ctx, TType type, TValue obj)
        {
            return GetMember(ctx, type, obj, "Value");
        }

        public virtual bool IsFlagsEnumType(EvaluationContext ctx, TType type)
        {
            return true;
        }

        public virtual IEnumerable<EnumMember> GetEnumMembers(EvaluationContext ctx, TType type)
        {
            TType longType = GetType(ctx, "System.Int64");
            var tref = new TypeValueReference<TType, TValue>(this, ctx, type);

            foreach (var cr in tref.GetChildReferences(ctx.Options))
            {
                var c = TryCast(ctx, cr.Value, longType);
                if (c == null)
                    continue;

                long val = c.ToRawValue(this, ctx).ToPrimitive<long>();
                var em = new EnumMember { Name = cr.Name, Value = val };

                yield return em;
            }
        }

        public TType GetBaseType(EvaluationContext ctx, TType type, bool includeObjectClass)
        {
            TType bt = GetBaseType(ctx, type);
            string tn = bt != null ? GetTypeName(ctx, bt) : null;

            if (!includeObjectClass && bt != null && (tn == "System.Object" || tn == "System.ValueType"))
                return null;
            if (tn == "System.Enum")
                return GetMembers(ctx, type, null, BindingFlags.GetField | BindingFlags.Instance | BindingFlags.Public).FirstOrDefault()?.Type;

            return bt;
        }

        public virtual bool IsClassInstance(EvaluationContext ctx, TValue val)
        {
            return IsClass(ctx, GetValueType(ctx, val));
        }

        public virtual bool IsExternalType(EvaluationContext ctx, TType type)
        {
            return false;
        }

        public virtual bool IsPublic(EvaluationContext ctx, TType type)
        {
            return false;
        }

        public TType GetType(EvaluationContext ctx, string name)
        {
            return GetType(ctx, name, null);
        }

        public abstract TType GetType(EvaluationContext ctx, string name, TType[] typeArgs);

        public virtual string GetValueTypeName(EvaluationContext ctx, TValue val)
        {
            return GetTypeName(ctx, GetValueType(ctx, val));
        }

        public abstract TValue CreateTypeObject(EvaluationContext ctx, TType type);

        public virtual bool IsTypeLoaded(EvaluationContext ctx, string typeName)
        {
            var type = GetType(ctx, typeName);

            return type != null && IsTypeLoaded(ctx, type);
        }

        public virtual bool IsTypeLoaded(EvaluationContext ctx, TType type)
        {
            return true;
        }

        public virtual TType ForceLoadType(EvaluationContext ctx, string typeName)
        {
            var type = GetType(ctx, typeName);

            if (type == null || IsTypeLoaded(ctx, type))
                return type;

            return ForceLoadType(ctx, type) ? type : default;
        }

        public virtual bool ForceLoadType(EvaluationContext ctx, TType type)
        {
            return true;
        }

        public TValue CreateValue(EvaluationContext ctx, object value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value), "Use CreateNullValue instead");
            if (value is string)
                return this.CreateStringValue(ctx, (string)value);
            if (!(value is Decimal))
                return this.CreatePrimitiveValue(ctx, value);
            ctx = ctx.WithAllowedInvokes();
            int[] bits = Decimal.GetBits((Decimal)value);
            TType type = GetType(ctx, "System.Decimal");
            TValue obj1 = CreateValue(ctx, bits[0]);
            TValue obj2 = CreateValue(ctx, bits[1]);
            TValue obj3 = CreateValue(ctx, bits[2]);
            TValue obj4 = CreateValue(ctx, bits[3]);
            return CreateValue(ctx, type, obj1, obj2, obj3, obj4);
        }

        protected abstract TValue CreatePrimitiveValue(EvaluationContext ctx, object value);

        protected abstract TValue CreateStringValue(EvaluationContext ctx, string value);

        public abstract TValue CreateValue(EvaluationContext ctx, TType type, params TValue[] args);

        public abstract TValue CreateNullValue(EvaluationContext ctx, TType type);

        public abstract TValue CreateByteArray(EvaluationContext ctx, byte[] values);

        public virtual TValue GetBaseValue(EvaluationContext ctx, TValue val)
        {
            return val;
        }

        public virtual TValue CreateDelayedLambdaValue(EvaluationContext ctx, string expression, Tuple<string, TValue>[] localVariables)
        {
            return null;
        }

        public virtual string[] GetImportedNamespaces(EvaluationContext ctx)
        {
            return new string[0];
        }

        public TValue LoadAssembly(EvaluationContext ctx, byte[] assembly)
        {
            TValue byteArray = CreateByteArray(ctx, assembly);
            TType type = GetType(ctx, "System.Reflection.Assembly");
            return Invocator.InvokeStaticMethod(ctx, type, "Load", byteArray).Result;
        }

        public abstract void SetByteArray(EvaluationContext ctx, TValue array, byte[] values);

        public virtual void GetNamespaceContents(EvaluationContext ctx, string namspace, out string[] childNamespaces, out string[] childTypes)
        {
            childTypes = childNamespaces = new string[0];
        }

        protected virtual ObjectValue CreateObjectValueImpl(
            EvaluationContext ctx,
            IObjectValueSource source,
            ObjectPath path,
            TValue obj,
            ObjectValueFlags flags)
        {
            TType type = obj != null ? GetValueType(ctx, obj) : null;
            string typeName = type != null ? GetTypeName(ctx, type) : "";

            if (obj == null || IsNull(ctx, obj))
                return ObjectValue.CreateNullObject(source, path, GetDisplayTypeName(typeName), flags);

            if (this.IsPointer(ctx, obj))
                return ObjectValue.CreatePointer(source, path, GetDisplayTypeName(typeName), Evaluator.TargetObjectToEvaluationResult(ctx, obj), flags);

            if (IsPrimitive(ctx, obj) || IsEnum(ctx, obj))
                return ObjectValue.CreatePrimitive(source, path, GetDisplayTypeName(typeName), Evaluator.TargetObjectToEvaluationResult(ctx, obj), flags);

            if (IsArray(ctx, obj))
                return ObjectValue.CreateObject(source, path, GetDisplayTypeName(typeName), Evaluator.TargetObjectToEvaluationResult(ctx, obj), flags, null);

            EvaluationResult tvalue = null;
            TypeDisplayData typeDisplayData = null;
            string tname;

            if (IsNullableType(ctx, type))
            {
                if (NullableHasValue(ctx, type, obj))
                {
                    ValueReference<TType, TValue> value = NullableGetValue(ctx, type, obj);

                    typeDisplayData = GetTypeDisplayData(ctx, value.Type);
                    obj = value.Value;
                }
                else
                {
                    typeDisplayData = GetTypeDisplayData(ctx, type);
                    tvalue = new EvaluationResult("null");
                }

                tname = GetDisplayTypeName(typeName);
            }
            else
            {
                typeDisplayData = GetTypeDisplayData(ctx, type);

                if (!string.IsNullOrEmpty(typeDisplayData.TypeDisplayString) && ctx.Options.AllowDisplayStringEvaluation)
                {
                    try
                    {
                        tname = EvaluateDisplayString(ctx, obj, typeDisplayData.TypeDisplayString);
                    }
                    catch (MissingMemberException)
                    {
                        // missing property or otherwise malformed DebuggerDisplay string
                        tname = GetDisplayTypeName(typeName);
                    }
                }
                else
                {
                    tname = GetDisplayTypeName(typeName);
                }
            }

            if (tvalue == null)
            {
                if (!string.IsNullOrEmpty(typeDisplayData.ValueDisplayString) && ctx.Options.AllowDisplayStringEvaluation)
                {
                    try
                    {
                        tvalue = new EvaluationResult(EvaluateDisplayString(ctx, obj, typeDisplayData.ValueDisplayString));
                    }
                    catch (MissingMemberException)
                    {
                        // missing property or otherwise malformed DebuggerDisplay string
                        tvalue = Evaluator.TargetObjectToEvaluationResult(ctx, obj);
                    }
                }
                else
                {
                    tvalue = Evaluator.TargetObjectToEvaluationResult(ctx, obj);
                }
            }

            ObjectValue oval = ObjectValue.CreateObject(source, path, tname, tvalue, flags, null);
            if (!string.IsNullOrEmpty(typeDisplayData.NameDisplayString) && ctx.Options.AllowDisplayStringEvaluation)
            {
                try
                {
                    oval.Name = EvaluateDisplayString(ctx, obj, typeDisplayData.NameDisplayString);
                }
                catch (MissingMemberException)
                {
                    // missing property or otherwise malformed DebuggerDisplay string
                }
            }

            return oval;
        }

        public ObjectValue[] GetObjectValueChildren(
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TValue obj,
            int firstItemIndex,
            int count)
        {
            return GetObjectValueChildren(ctx, objectSource, GetValueType(ctx, obj), obj, firstItemIndex, count, true);
        }

        public virtual ObjectValue[] GetObjectValueChildren(
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue obj,
            int firstItemIndex,
            int count,
            bool dereferenceProxy)
        {
            if (obj is EvaluationResult)
                return new ObjectValue[0];

            if (IsArray(ctx, obj))
            {
                var agroup = new ArrayElementGroup<TType, TValue>(this, ctx, CreateArrayAdaptor(ctx, obj));
                return agroup.GetChildren(ctx.Options);
            }

            if (IsPrimitive(ctx, obj))
                return new ObjectValue[0];

            if (IsNullableType(ctx, type))
            {
                if (NullableHasValue(ctx, type, obj))
                {
                    ValueReference<TType, TValue> value = NullableGetValue(ctx, type, obj);

                    return GetObjectValueChildren(ctx, objectSource, value.Type, value.Value, firstItemIndex, count, dereferenceProxy);
                }

                return new ObjectValue[0];
            }

            bool showRawView = false;

            // If there is a proxy, it has to show the members of the proxy
            TValue proxy = obj;
            if (dereferenceProxy)
            {
                proxy = GetProxyObject(ctx, obj);
                if (proxy != obj)
                {
                    type = GetValueType(ctx, proxy);
                    showRawView = true;
                }
            }

            TypeDisplayData tdata = GetTypeDisplayData(ctx, type);
            bool groupPrivateMembers = ctx.Options.GroupPrivateMembers || IsExternalType(ctx, type);

            var values = new List<ObjectValue>();
            BindingFlags flattenFlag = ctx.Options.FlattenHierarchy ? (BindingFlags)0 : BindingFlags.DeclaredOnly;
            BindingFlags nonPublicFlag = !(groupPrivateMembers || showRawView) ? BindingFlags.NonPublic : (BindingFlags)0;
            BindingFlags staticFlag = ctx.Options.GroupStaticMembers ? (BindingFlags)0 : BindingFlags.Static;
            BindingFlags access = BindingFlags.Public | BindingFlags.Instance | flattenFlag | nonPublicFlag | staticFlag;

            // Load all members to a list before creating the object values,
            // to avoid problems with objects being invalidated due to evaluations in the target,
            var list = new List<ValueReference<TType, TValue>>();
            list.AddRange(GetMembersSorted(ctx, objectSource, type, proxy, access));

            // Some implementations of DebuggerProxies(showRawView==true) only have private members
            if (showRawView && list.Count == 0)
            {
                list.AddRange(GetMembersSorted(ctx, objectSource, type, proxy, access | BindingFlags.NonPublic));
            }

            var names = new ObjectValueNameTracker<TType, TValue>(this, ctx);
            TType tdataType = type;

            foreach (ValueReference<TType, TValue> val in list)
            {
                try
                {
                    TType decType = val.DeclaringType;
                    if (decType != null && decType != tdataType)
                    {
                        tdataType = decType;
                        tdata = GetTypeDisplayData(ctx, decType);
                    }

                    DebuggerBrowsableState state = tdata.GetMemberBrowsableState(val.Name);
                    if (state == DebuggerBrowsableState.Never)
                        continue;

                    if (state == DebuggerBrowsableState.RootHidden && dereferenceProxy)
                    {
                        TValue ob = val.Value;
                        if (ob != null)
                        {
                            values.Clear();
                            values.AddRange(GetObjectValueChildren(ctx, val, ob, -1, -1));
                            showRawView = true;
                            break;
                        }
                    }
                    else
                    {
                        ObjectValue oval = val.CreateObjectValue(true);
                        names.Disambiguate(val, oval);
                        values.Add(oval);
                    }
                }
                catch (Exception ex)
                {
                    ctx.WriteDebuggerError(ex);
                    values.Add(ObjectValue.CreateError(null, new ObjectPath(val.Name), GetDisplayTypeName(GetTypeName(ctx, val.Type)), ex.Message, val.Flags));
                }
            }

            if (showRawView)
            {
                values.Add(RawViewSource.CreateRawView(this, ctx, objectSource, obj));
            }
            else
            {
                if (IsArray(ctx, proxy))
                {
                    var col = CreateArrayAdaptor(ctx, proxy);
                    var agroup = new ArrayElementGroup<TType, TValue>(this, ctx, col);
                    var val = ObjectValue.CreateObject(null, new ObjectPath("Raw View"), "", "", ObjectValueFlags.ReadOnly, values.ToArray());

                    values = new List<ObjectValue>();
                    values.Add(val);
                    values.AddRange(agroup.GetChildren(ctx.Options));
                }
                else
                {
                    if (ctx.Options.GroupStaticMembers && HasMembers(ctx, type, proxy, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | flattenFlag))
                    {
                        access = BindingFlags.Static | BindingFlags.Public | flattenFlag | nonPublicFlag;
                        values.Add(FilteredMembersSource.CreateStaticsNode(this, ctx, objectSource, type, proxy, access));
                    }

                    if (groupPrivateMembers && HasMembers(ctx, type, proxy, BindingFlags.Instance | BindingFlags.NonPublic | flattenFlag | staticFlag))
                        values.Add(FilteredMembersSource.CreateNonPublicsNode(this, ctx, objectSource, type, proxy, BindingFlags.Instance | BindingFlags.NonPublic | flattenFlag | staticFlag));

                    if (!ctx.Options.FlattenHierarchy)
                    {
                        TType baseType = GetBaseType(ctx, type, false);
                        if (baseType != null)
                            values.Insert(0, BaseTypeViewSource.CreateBaseTypeView(this, ctx, objectSource, baseType, proxy));
                    }

                    if (ctx.SupportIEnumerable)
                    {
                        var iEnumerableType = GetImplementedInterfaces(ctx, type).FirstOrDefault((interfaceType) =>
                        {
                            string interfaceName = GetTypeName(ctx, interfaceType);
                            if (interfaceName == "System.Collections.IEnumerable")
                                return true;
                            if (interfaceName == "System.Collections.Generic.IEnumerable`1")
                                return true;
                            return false;
                        });
                        if (iEnumerableType != null)
                            values.Add(ObjectValue.CreatePrimitive(new EnumerableSource<TType, TValue>(proxy, iEnumerableType, this, ctx), new ObjectPath("IEnumerator"), "", new EvaluationResult(""), ObjectValueFlags.ReadOnly | ObjectValueFlags.Object | ObjectValueFlags.Group | ObjectValueFlags.IEnumerable));
                    }
                }
            }

            return values.ToArray();
        }

        public ObjectValue[] GetExpressionValuesAsync(EvaluationContext ctx, string[] expressions)
        {
            return PerformWithCancellationToken(token =>
            {
                var objectValueArray = new ObjectValue[expressions.Length];
                for (var index = 0; index < objectValueArray.Length && !token.IsCancellationRequested; ++index)
                {
                    string expression = expressions[index];
                    objectValueArray[index] = GetExpressionValue(ctx, expression);
                }

                return objectValueArray;
            });

//            var values = new ObjectValue[expressions.Length];
//
//            for (int n = 0; n < values.Length; n++)
//            {
//                string exp = expressions[n];
//
//                // This is a workaround to a bug in mono 2.0. That mono version fails to compile
//                // an anonymous method here
//                var edata = new ExpData(ctx, exp, this);
//                values[n] = asyncEvaluationTracker.Run(exp, ObjectValueFlags.Literal, edata.Run);
//            }
//
//            return values;
        }

        public T PerformWithCancellationToken<T>(Func<CancellationToken, T> func)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            lock (cancellationTokensLock)
            {
                cancellationTokenSources.Add(cancellationTokenSource);
            }

            try
            {
                return func(cancellationTokenSource.Token);
            }
            finally
            {
                lock (cancellationTokensLock)
                {
                    cancellationTokenSources.Remove(cancellationTokenSource);
                }
            }
        }

//
//        class ExpData
//        {
//            readonly ObjectValueAdaptor<TValue> adaptor;
//            readonly EvaluationContext ctx;
//            readonly string exp;
//
//            public ExpData(EvaluationContext ctx, string exp, ObjectValueAdaptor adaptor)
//            {
//                this.ctx = ctx;
//                this.exp = exp;
//                this.adaptor = adaptor;
//            }
//
//            public ObjectValue Run()
//            {
//                return adaptor.GetExpressionValue(ctx, exp);
//            }
//        }

        public virtual ValueReference<TType, TValue> GetIndexerReference(EvaluationContext ctx, TValue target, TValue[] indices)
        {
            return null;
        }

        public virtual ValueReference<TType, TValue> GetIndexerReference(EvaluationContext ctx, TValue target, TType type, TValue[] indices)
        {
            return GetIndexerReference(ctx, target, indices);
        }

        public ValueReference<TType, TValue> GetLocalVariable(EvaluationContext ctx, string name)
        {
            return OnGetLocalVariable(ctx, name);
        }

        protected virtual ValueReference<TType, TValue> OnGetLocalVariable(EvaluationContext ctx, string name)
        {
            ValueReference<TType, TValue> best = null;
            foreach (ValueReference<TType, TValue> var in GetLocalVariables(ctx))
            {
                if (var.Name == name)
                    return var;
                if (!Evaluator.CaseSensitive && var.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                    best = var;
            }

            return best;
        }

        public virtual ValueReference<TType, TValue> GetParameter(EvaluationContext ctx, string name)
        {
            return OnGetParameter(ctx, name);
        }

        protected virtual ValueReference<TType, TValue> OnGetParameter(EvaluationContext ctx, string name)
        {
            ValueReference<TType, TValue> best = null;
            foreach (ValueReference<TType, TValue> var in GetParameters(ctx))
            {
                if (var.Name == name)
                    return var;
                if (!Evaluator.CaseSensitive && var.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                    best = var;
            }

            return best;
        }

        public IEnumerable<ValueReference<TType, TValue>> GetLocalVariables(EvaluationContext ctx)
        {
            return OnGetLocalVariables(ctx);
        }

        public ValueReference<TType, TValue> GetThisReference(EvaluationContext ctx)
        {
            return OnGetThisReference(ctx);
        }

        public IEnumerable<ValueReference<TType, TValue>> GetParameters(EvaluationContext ctx)
        {
            return OnGetParameters(ctx);
        }

        protected virtual IEnumerable<ValueReference<TType, TValue>> OnGetLocalVariables(EvaluationContext ctx)
        {
            yield break;
        }

        protected virtual IEnumerable<ValueReference<TType, TValue>> OnGetParameters(EvaluationContext ctx)
        {
            yield break;
        }

        protected virtual ValueReference<TType, TValue> OnGetThisReference(EvaluationContext ctx)
        {
            return null;
        }

        public virtual ValueReference<TType, TValue> GetCurrentException(EvaluationContext ctx)
        {
            return null;
        }

        public virtual TType GetEnclosingType(EvaluationContext ctx)
        {
            return default;
        }

        protected virtual CompletionData GetMemberCompletionData(EvaluationContext ctx, ValueReference<TType, TValue> vr)
        {
            var data = new CompletionData();

            foreach (var cv in vr.GetChildReferences(ctx.Options))
                data.Items.Add(new CompletionItem(cv.Name, cv.Flags));

            data.ExpressionLength = 0;

            return data;
        }

        public virtual CompletionData GetExpressionCompletionData(EvaluationContext ctx, string expr)
        {
            if (expr == null)
                return null;

            int dot = expr.LastIndexOf('.');

            if (dot != -1)
            {
                try
                {
                    ValueReference<TType, TValue> vr = Evaluator.Evaluate(ctx, expr.Substring(0, dot), default);
                    if (vr != null)
                    {
                        var completionData = GetMemberCompletionData(ctx, vr);
                        completionData.ExpressionLength = expr.Length - dot - 1;
                        return completionData;
                    }

                    // FIXME: handle types and namespaces...
                }
                catch (EvaluatorException) { }
                catch (Exception ex)
                {
                    ctx.WriteDebuggerError(ex);
                }

                return null;
            }

            bool lastWastLetter = false;
            int i = expr.Length - 1;

            while (i >= 0)
            {
                char c = expr[i--];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    break;

                lastWastLetter = !char.IsDigit(c);
            }

            if (lastWastLetter || expr.Length == 0)
            {
                var data = new CompletionData();
                data.ExpressionLength = expr.Length - (i + 1);

                // Local variables

                foreach (var vc in GetLocalVariables(ctx))
                {
                    data.Items.Add(new CompletionItem(vc.Name, vc.Flags));
                }

                // Parameters

                foreach (var vc in GetParameters(ctx))
                {
                    data.Items.Add(new CompletionItem(vc.Name, vc.Flags));
                }

                // Members

                ValueReference<TType, TValue> thisobj = GetThisReference(ctx);

                if (thisobj != null)
                    data.Items.Add(new CompletionItem("this", ObjectValueFlags.Field | ObjectValueFlags.ReadOnly));

                TType type = GetEnclosingType(ctx);

                foreach (var vc in GetMembers(ctx, null, type, thisobj != null ? thisobj.Value : null))
                {
                    data.Items.Add(new CompletionItem(vc.Name, vc.Flags));
                }

                if (data.Items.Count > 0)
                    return data;
            }

            return null;
        }

        public IEnumerable<ValueReference<TType, TValue>> GetMembers(
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue co)
        {
            foreach (ValueReference<TType, TValue> val in GetMembers(ctx, objectSource, type, co, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                val.ParentSource = objectSource;
                yield return val;
            }
        }

        public ValueReference<TType, TValue> GetMember(
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TValue co,
            string name)
        {
            return GetMember(ctx, objectSource, GetValueType(ctx, co), co, name);
        }

        protected virtual ValueReference<TType, TValue> OnGetMember(
            EvaluationContext ctx,
            IDebuggerHierarchicalObject objectSource,
            TType type,
            TValue co,
            string name)
        {
            return GetMember(ctx, type, co, name);
        }

        public ValueReference<TType, TValue> GetMember(
            EvaluationContext ctx,
            IDebuggerHierarchicalObject objectSource,
            TType type,
            TValue co,
            string name)
        {
            ValueReference<TType, TValue> m = OnGetMember(ctx, objectSource, type, co, name);
            if (m != null)
                m.ParentSource = objectSource;
            return m;
        }

        protected virtual ValueReference<TType, TValue> GetMember(EvaluationContext ctx, TType type, TValue co, string name)
        {
            ValueReference<TType, TValue> best = null;
            foreach (ValueReference<TType, TValue> var in GetMembers(ctx, type, co, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (var.Name == name)
                    return var;
                if (!Evaluator.CaseSensitive && var.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                    best = var;
            }

            return best;
        }

        internal IEnumerable<ValueReference<TType, TValue>> GetMembersSorted(
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue co)
        {
            return GetMembersSorted(ctx, objectSource, type, co, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }

        internal IEnumerable<ValueReference<TType, TValue>> GetMembersSorted(
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue co,
            BindingFlags bindingFlags)
        {
            var list = new List<ValueReference<TType, TValue>>();

            foreach (var vr in GetMembers(ctx, objectSource, type, co, bindingFlags))
            {
                vr.ParentSource = objectSource;
                list.Add(vr);
            }

            list.Sort((v1, v2) => string.Compare(v1.Name, v2.Name, StringComparison.Ordinal));

            return list;
        }

        public bool HasMembers(EvaluationContext ctx, TType type, TValue co, BindingFlags bindingFlags)
        {
            return GetMembers(ctx, type, co, bindingFlags).Any();
        }

        public bool HasMember(EvaluationContext ctx, TType type, string memberName)
        {
            return HasMember(ctx, type, memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }

        public abstract bool HasMember(EvaluationContext ctx, TType type, string memberName, BindingFlags bindingFlags);

        /// <summary>
        /// Returns all members of a type. The following binding flags have to be honored:
        /// BindingFlags.Static, BindingFlags.Instance, BindingFlags.Public, BindingFlags.NonPublic, BindingFlags.DeclareOnly
        /// </summary>
        protected abstract IEnumerable<ValueReference<TType, TValue>> GetMembers(
            EvaluationContext ctx,
            TType type,
            TValue co,
            BindingFlags bindingFlags);

        /// <summary>
        /// Returns all members of a type. The following binding flags have to be honored:
        /// BindingFlags.Static, BindingFlags.Instance, BindingFlags.Public, BindingFlags.NonPublic, BindingFlags.DeclareOnly
        /// </summary>
        protected virtual IEnumerable<ValueReference<TType, TValue>> GetMembers(
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue co,
            BindingFlags bindingFlags)
        {
            return GetMembers(ctx, type, co, bindingFlags);
        }

        public virtual IEnumerable<TType> GetNestedTypes(EvaluationContext ctx, TType type)
        {
            yield break;
        }

        public virtual IEnumerable<TType> GetImplementedInterfaces(EvaluationContext ctx, TType type)
        {
            yield break;
        }

        public virtual TType GetParentType(EvaluationContext ctx, TType type)
        {
            return default;
        }

        public TValue CreateNewArray(EvaluationContext ctx, TType type, int size)
        {
            TType arrayType = GetType(ctx, "System.Array");
            TValue arrayObject = CreateTypeObject(ctx, type);
            TValue sizeObject = CreateValue(ctx, size);
            return Invocator.InvokeStaticMethod(ctx, arrayType, "CreateInstance", arrayObject, sizeObject).Result;
        }

        public abstract void SetArray(EvaluationContext ctx, TValue array, TValue[] values);

        public virtual TValue CreateArray(EvaluationContext ctx, TType type, TValue[] values)
        {
            TValue newArray = CreateNewArray(ctx, type, values.Length);
            SetArray(ctx, newArray, values);
            return newArray;
        }

//        public virtual TValue CreateArray(EvaluationContext ctx, TType type, int[] lengths)
//        {
//            if (lengths.Length > 3)
//            {
//                throw new NotSupportedException("Arrays with more than 3 demensions are not supported.");
//            }
//
//            var arrType = GetType(ctx, "System.Array");
//            var intType = GetType(ctx, "System.Int32");
//            var typeType = GetType(ctx, "System.Type");
//            var arguments = new object [lengths.Length + 1];
//            var argTypes = new object [lengths.Length + 1];
//            arguments[0] = CreateTypeObject(ctx, type);
//            argTypes[0] = typeType;
//            for (int i = 0; i < lengths.Length; i++)
//            {
//                arguments[i + 1] = FromRawValue(ctx, lengths[i]);
//                argTypes[i + 1] = intType;
//            }
//
//            return RuntimeInvoke(ctx, arrType, null, "CreateInstance", argTypes, arguments);
//        }

        public virtual IRawValue<TValue> ToRawValue(
            EvaluationContext ctx,
            IDebuggerHierarchicalObject source,
            TValue obj)
        {
//            if (IsEnum(ctx, obj))
//            {
//                var longType = GetType(ctx, "System.Int64");
//                var c = Cast(ctx, obj, longType);
//
//                return TargetObjectToObject(ctx, c);
//            }

            if (IsAtomicPrimitive(ctx, obj))
                return new RemoteRawValuePrimitive<TValue>(ToAtomicPrimitive(ctx, obj), obj);

            if (ctx.Options.ChunkRawStrings && IsString(ctx, obj))
            {
                var stringAdaptor = CreateStringAdaptor(ctx, obj);
                return new RemoteRawValueString<TType, TValue>(this, ctx, stringAdaptor, obj);
            }

            if (IsArray(ctx, obj))
            {
                var arrayAdaptor = CreateArrayAdaptor(ctx, obj);
                return new RemoteRawValueArray<TType, TValue>(this, ctx, source, arrayAdaptor);
            }

            return new RemoteRawValueObject<TType, TValue>(this, ctx, source, obj);
        }

        /// <summary>
        /// Check if the value is simple primitive type: Boolean, Byte, SByte, Int16, UInt16, Int32, UInt32, Int64, UInt64, IntPtr, UIntPtr, Char, Double, or Single
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public abstract bool IsAtomicPrimitive(EvaluationContext ctx, TValue value);

        /// <summary>
        /// Converts debugger value to an primitive object of this runtime
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public abstract ValueType ToAtomicPrimitive(EvaluationContext ctx, TValue value);

        public virtual object TargetObjectToObject(EvaluationContext ctx, TValue obj)
        {
            if (IsNull(ctx, obj))
                return null;

            if (IsArray(ctx, obj))
            {
                ICollectionAdaptor<TType, TValue> adaptor = CreateArrayAdaptor(ctx, obj);
                string ename = GetDisplayTypeName(GetTypeName(ctx, adaptor.ElementType));
                int[] dims = adaptor.GetDimensions();
                var tn = new StringBuilder("[");

                for (int n = 0; n < dims.Length; n++)
                {
                    if (n > 0)
                        tn.Append(',');
                    tn.Append(dims[n]);
                }

                tn.Append("]");

                int i = ename.LastIndexOf('>');
                if (i == -1)
                    i = 0;

                i = ename.IndexOf('[', i);

                if (i != -1)
                    return new EvaluationResult("{" + ename.Substring(0, i) + tn + ename.Substring(i) + "}");

                return new EvaluationResult("{" + ename + tn + "}");
            }

            TType type = GetValueType(ctx, obj);
            string typeName = GetTypeName(ctx, type);
            if (IsEnum(ctx, obj))
            {
                TType longType = GetType(ctx, "System.Int64");
                long val = Cast(ctx, obj, longType).ToRawValue(this, ctx).ToPrimitive<long>();
                long rest = val;
                string composed = string.Empty;
                string composedDisplay = string.Empty;

                foreach (var em in GetEnumMembers(ctx, type))
                {
                    if (em.Value == val)
                        return new EvaluationResult(typeName + "." + em.Name, em.Name);

                    if (em.Value != 0 && (rest & em.Value) == em.Value)
                    {
                        rest &= ~em.Value;
                        if (composed.Length > 0)
                        {
                            composed += " | ";
                            composedDisplay += " | ";
                        }

                        composed += typeName + "." + em.Name;
                        composedDisplay += em.Name;
                    }
                }

                if (IsFlagsEnumType(ctx, type) && rest == 0 && composed.Length > 0)
                    return new EvaluationResult(composed, composedDisplay);

                return new EvaluationResult(val.ToString());
            }

            if (typeName == "System.Decimal")
            {
                string res = CallToString(ctx, obj);

                // This returns the decimal formatted using the current culture. It has to be converted to invariant culture.
                decimal dec = decimal.Parse(res);
                res = dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new EvaluationResult(res);
            }

            if (typeName == "System.nfloat" || typeName == "System.nint")
            {
                return TargetObjectToObject(ctx, GetMembersSorted(ctx, null, type, obj, BindingFlags.Instance | BindingFlags.NonPublic).Single().Value);
            }

            if (IsClassInstance(ctx, obj))
            {
                TypeDisplayData tdata = GetTypeDisplayData(ctx, GetValueType(ctx, obj));
                if (!string.IsNullOrEmpty(tdata.ValueDisplayString) && ctx.Options.AllowDisplayStringEvaluation)
                {
                    try
                    {
                        return new EvaluationResult(EvaluateDisplayString(ctx, obj, tdata.ValueDisplayString));
                    }
                    catch (MissingMemberException)
                    {
                        // missing property or otherwise malformed DebuggerDisplay string
                    }
                }

                // Return the type name
                if (ctx.Options.AllowToStringCalls)
                {
                    try
                    {
                        return new EvaluationResult("{" + CallToString(ctx, obj) + "}");
                    }
                    catch (TimeOutException)
                    {
                        // ToString() timed out, fall back to default behavior.
                    }
                }

                if (!string.IsNullOrEmpty(tdata.TypeDisplayString) && ctx.Options.AllowDisplayStringEvaluation)
                {
                    try
                    {
                        return new EvaluationResult("{" + EvaluateDisplayString(ctx, obj, tdata.TypeDisplayString) + "}");
                    }
                    catch (MissingMemberException)
                    {
                        // missing property or otherwise malformed DebuggerDisplay string
                    }
                }

                return new EvaluationResult("{" + GetDisplayTypeName(GetValueTypeName(ctx, obj)) + "}");
            }

            return new EvaluationResult("{" + CallToString(ctx, obj) + "}");
        }

        public Decimal? ObjectToDecimal(EvaluationContext ctx, TValue obj)
        {
            TType valueType = GetValueType(ctx, obj);
            IResolutionResult resolutionResult = MethodResolver.ResolveStaticMethod(ctx, "GetBits", valueType, EmptyTypeArray, valueType);
            if (!resolutionResult.IsSuccess())
            {
                DebuggerLoggingService.LogMessage("Failed to resolve Decimal.GetBits()");
                return new Decimal?();
            }

            InvocationInfo<TValue> staticCallInfo = resolutionResult.ToStaticCallInfo(obj);
            try
            {
                InvocationResult<TValue> invocationResult = Invocator.RuntimeInvoke(ctx.WithAllowedInvokes(), staticCallInfo);
                int[] bits = new int[4];
                if (!(invocationResult.Result.ToRawValue(this, ctx) is IRawValueArray<TValue> rawValue)
                    || rawValue.Dimensions.Length != 1
                    || rawValue.Dimensions[0] != 4)
                {
                    DebuggerLoggingService.LogMessage("Decimal.GetBits() returned a value which is not 'int[4]' array");
                    return new Decimal?();
                }

                IRawValue<TValue>[] values = rawValue.GetValues(new int[1], 4);
                for (int index = 0; index < 4; ++index)
                {
                    if (!values[index].TryToPrimitive(out int primitiveValue))
                    {
                        DebuggerLoggingService.LogMessage("Decimal.GetBits() result element is not an 'int' value");
                        return new Decimal?();
                    }

                    bits[index] = primitiveValue;
                }

                return new decimal(bits);
            }
            catch (EvaluatorException ex)
            {
                DebuggerLoggingService.LogAndShowException("Failed to invoke Decimal.GetBits()", ex);
                return new Decimal?();
            }
        }

        public TValue Convert(EvaluationContext ctx, TValue obj, TType targetType)
        {
            if (obj == null)
                return null;

            TValue res = TryConvert(ctx, obj, targetType);
            if (res != null)
                return res;

            throw new EvaluatorException("Can't convert an object of type '{0}' to type '{1}'", GetValueTypeName(ctx, obj), GetTypeName(ctx, targetType));
        }

        public virtual TValue TryConvert(EvaluationContext ctx, TValue obj, TType targetType)
        {
            return TryCast(ctx, obj, targetType);
        }

        public virtual TValue Cast(EvaluationContext ctx, TValue obj, TType targetType)
        {
            if (obj == null)
                return null;

            TValue res = TryCast(ctx, obj, targetType);
            if (res != null)
                return res;

            throw new EvaluatorException("Can't cast an object of type '{0}' to type '{1}'", GetValueTypeName(ctx, obj), GetTypeName(ctx, targetType));
        }

        public virtual string CallToString(EvaluationContext ctx, TValue obj)
        {
            return GetValueTypeName(ctx, obj);
        }

        // FIXME: next time we can break ABI/API, make this abstract
        protected virtual TType GetBaseTypeWithAttribute(EvaluationContext ctx, TType type, TType attrType)
        {
            return default;
        }

        public TValue GetProxyObject(EvaluationContext ctx, TValue obj)
        {
            TypeDisplayData data = GetTypeDisplayData(ctx, GetValueType(ctx, obj));
            if (string.IsNullOrEmpty(data.ProxyType) || !ctx.Options.AllowDebuggerProxy)
                return obj;

            string proxyType = data.ProxyType;
            TType[] typeArgs = null;

            int index = proxyType.IndexOf('`');
            if (index != -1)
            {
                // The proxy type is an uninstantiated generic type.
                // The number of type args of the proxy must match the args of the target object
                int startIndex = index + 1;
                int endIndex = index + 1;

                while (endIndex < proxyType.Length && char.IsDigit(proxyType[endIndex]))
                    endIndex++;

                var attrType = GetType(ctx, "System.Diagnostics.DebuggerTypeProxyAttribute");
                int num = int.Parse(proxyType.Substring(startIndex, endIndex - startIndex));
                var proxiedType = GetBaseTypeWithAttribute(ctx, GetValueType(ctx, obj), attrType);

                if (proxiedType == null || !IsGenericType(ctx, proxiedType))
                    return obj;

                typeArgs = GetTypeArgs(ctx, proxiedType);
                if (typeArgs.Length != num)
                    return obj;

                if (endIndex < proxyType.Length)
                {
                    // chop off the []'d list of generic type arguments
                    proxyType = proxyType.Substring(0, endIndex);
                }
            }

            TType ttype = GetType(ctx, proxyType, typeArgs);
            if (ttype == null)
            {
                // the proxy type string might be in the form: "Namespace.TypeName, Assembly...", chop off the ", Assembly..." bit.
                if ((index = proxyType.IndexOf(',')) != -1)
                    ttype = GetType(ctx, proxyType.Substring(0, index).Trim(), typeArgs);
            }

            if (ttype == null)
                throw new EvaluatorException("Unknown type '{0}'", data.ProxyType);

            try
            {
                TValue val = CreateValue(ctx, ttype, obj);
                return val ?? obj;
            }
            catch (EvaluatorException)
            {
                // probably couldn't find the .ctor for the proxy type because the linker stripped it out
                return obj;
            }
            catch (Exception ex)
            {
                ctx.WriteDebuggerError(ex);
                return obj;
            }
        }

        public TypeDisplayData GetTypeDisplayData(EvaluationContext ctx, TType type)
        {
            if (!IsClass(ctx, type))
                return TypeDisplayData.Default;

            TypeDisplayData td;
            string tname = GetTypeName(ctx, type);
            if (typeDisplayData.TryGetValue(tname, out td))
                return td;

            try
            {
                td = OnGetTypeDisplayData(ctx, type);
            }
            catch (Exception ex)
            {
                ctx.WriteDebuggerError(ex);
            }

            if (td == null)
                typeDisplayData[tname] = td = TypeDisplayData.Default;
            else
                typeDisplayData[tname] = td;

            return td;
        }

        protected virtual TypeDisplayData OnGetTypeDisplayData(EvaluationContext ctx, TType type)
        {
            return null;
        }

        static bool IsQuoted(string str)
        {
            return str.Length >= 2 && str[0] == '"' && str[str.Length - 1] == '"';
        }

        public string EvaluateDisplayString(EvaluationContext ctx, TValue obj, string expr)
        {
            var display = new StringBuilder();
            int i = expr.IndexOf('{');
            int last = 0;

            while (i != -1 && i < expr.Length)
            {
                display.Append(expr, last, i - last);
                i++;

                int j = expr.IndexOf('}', i);
                if (j == -1)
                    return expr;

                string memberExpr = expr.Substring(i, j - i).Trim();
                if (memberExpr.Length == 0)
                    return expr;

                int comma = memberExpr.LastIndexOf(',');
                bool noquotes = false;
                if (comma != -1)
                {
                    var option = memberExpr.Substring(comma + 1).Trim();
                    memberExpr = memberExpr.Substring(0, comma).Trim();
                    noquotes |= option == "nq";
                }

                var props = memberExpr.Split('.');
                TValue val = obj;

                for (int k = 0; k < props.Length; k++)
                {
                    var member = GetMember(ctx, null, GetValueType(ctx, val), val, props[k]);
                    if (member != null)
                    {
                        val = member.Value;
                    }
                    else
                    {
                        var methodName = props[k].TrimEnd('(', ')', ' ');
                        IResolutionResult resolutionResult = MethodResolver.ResolveInstanceMethod(ctx, methodName, GetValueType(ctx, val), EmptyTypeArray);
                        if (resolutionResult.IsSuccess())
                        {
                            val = Invocator.RuntimeInvoke(ctx, resolutionResult.ToInstanceCallInfo<TValue>(val, Array.Empty<TValue>())).Result;
                        }
                        else
                        {
                            val = null;
                            break;
                        }
                    }
                }

                if (val != null)
                {
                    var str = Evaluator.TargetObjectToString(ctx, val);
                    if (str == null)
                        display.Append("null");
                    else if (noquotes && IsQuoted(str))
                        display.Append(str, 1, str.Length - 2);
                    else
                        display.Append(str);
                }
                else
                {
                    throw new MissingMemberException(GetValueTypeName(ctx, obj), memberExpr);
                }

                last = j + 1;
                i = expr.IndexOf('{', last);
            }

            display.Append(expr, last, expr.Length - last);

            return display.ToString();
        }

        public void AsyncExecute(AsyncOperation operation, int timeout)
        {
            asyncOperationManager.Invoke(operation, timeout);
        }

        public ObjectValue CreateObjectValueAsync(string name, ObjectValueFlags flags, ObjectEvaluatorDelegate evaluator)
        {
            return asyncEvaluationTracker.Run(name, flags, evaluator);
        }

        public bool IsEvaluating
        {
            get { return asyncEvaluationTracker.IsEvaluating; }
        }

        public void CancelAsyncOperations()
        {
            asyncEvaluationTracker.Stop();
            asyncOperationManager.AbortAll();
            asyncEvaluationTracker.WaitForStopped();
        }

        public ObjectValue GetExpressionValue(EvaluationContext ctx, string exp)
        {
            try
            {
                ValueReference<TType, TValue> var = Evaluator.Evaluate(ctx, exp);

                return var != null ? var.CreateObjectValue(ctx.Options) : ObjectValue.CreateUnknown(exp);
            }

//            catch (TargetInvokeDisabledException ex)
//            {
//                return ObjectValue.CreateTargetInvokeNotSupported((IObjectValueSource) ExpressionValueSource.Create<TContext, TType, TValue>((IObjectValueAdaptor<TContext, TType, TValue>) this, ctx), path, "", ObjectValueFlags.None, ctx.Options, (string) null);
//            }
//            catch (CrossThreadDependencyRejectedException ex)
//            {
//                return ObjectValue.CreateCrossThreadDependencyRejected((IObjectValueSource) ExpressionValueSource.Create<TContext, TType, TValue>((IObjectValueAdaptor<TContext, TType, TValue>) this, ctx), path, string.Empty, ObjectValueFlags.None, (string) null);
//            }
            catch (NotSupportedExpressionException ex)
            {
                return ObjectValue.CreateNotSupported(ExpressionValueSource.Create(this, ctx), new ObjectPath(exp), "", ex.Message, ObjectValueFlags.None);
            }

//            catch (EvaluatorExceptionThrownExceptionBase ex)
//            {
//                return ObjectValue.CreateEvaluationException<TContext, TType, TValue>((IObjectValueAdaptor<TContext, TType, TValue>) this, ctx, (IObjectValueSource) ExpressionValueSource.Create<TContext, TType, TValue>((IObjectValueAdaptor<TContext, TType, TValue>) this, ctx), path, ex, ObjectValueFlags.None);
//            }
            catch (EvaluatorException ex)
            {
                return ObjectValue.CreateError(ExpressionValueSource.Create(this, ctx), new ObjectPath(exp), "", ex.Message, ObjectValueFlags.None);
            }
            catch (Exception ex)
            {
                ctx.WriteDebuggerOutput("Exception in GetExpressionValue()");
                ctx.WriteDebuggerError(ex);
                return ObjectValue.CreateUnknown(exp, null);
            }
        }

        public virtual bool HasMethodWithParamLength(EvaluationContext ctx, TType targetType, string methodName, BindingFlags flags, int paramLength)
        {
            return false;
        }
    }

    public class TypeDisplayData
    {
        public string ProxyType { get; internal set; }
        public string ValueDisplayString { get; internal set; }
        public string TypeDisplayString { get; internal set; }
        public string NameDisplayString { get; internal set; }
        public bool IsCompilerGenerated { get; internal set; }

        public bool IsProxyType
        {
            get { return ProxyType != null; }
        }

        public static readonly TypeDisplayData Default = new TypeDisplayData(null, null, null, null, false, null);

        public Dictionary<string, DebuggerBrowsableState> MemberData { get; internal set; }

        public TypeDisplayData(string proxyType, string valueDisplayString, string typeDisplayString,
            string nameDisplayString, bool isCompilerGenerated, Dictionary<string, DebuggerBrowsableState> memberData)
        {
            ProxyType = proxyType;
            ValueDisplayString = valueDisplayString;
            TypeDisplayString = typeDisplayString;
            NameDisplayString = nameDisplayString;
            IsCompilerGenerated = isCompilerGenerated;
            MemberData = memberData;
        }

        public DebuggerBrowsableState GetMemberBrowsableState(string name)
        {
            if (MemberData == null)
                return DebuggerBrowsableState.Collapsed;

            DebuggerBrowsableState state;
            if (!MemberData.TryGetValue(name, out state))
                state = DebuggerBrowsableState.Collapsed;

            return state;
        }
    }

    class ObjectValueNameTracker<TType, TValue>
        where TType : class
        where TValue : class
    {
        readonly Dictionary<string, KeyValuePair<ObjectValue, ValueReference<TType, TValue>>> names = new Dictionary<string, KeyValuePair<ObjectValue, ValueReference<TType, TValue>>>();
        readonly ObjectValueAdaptor<TType, TValue> adapter;
        readonly EvaluationContext ctx;

        public ObjectValueNameTracker(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx)
        {
            this.adapter = adaptor;
            this.ctx = ctx;
        }

        /// <summary>
        /// Disambiguate the ObjectValue's name (in the case where the property name also exists in a base class).
        /// </summary>
        /// <param name='val'>
        /// The ValueReference.
        /// </param>
        /// <param name='oval'>
        /// The ObjectValue.
        /// </param>
        public void Disambiguate(ValueReference<TType, TValue> val, ObjectValue oval)
        {
            KeyValuePair<ObjectValue, ValueReference<TType, TValue>> other;

            if (names.TryGetValue(oval.Name, out other))
            {
                TType tn = val.DeclaringType;

                if (tn != null)
                    oval.Name += " (" + adapter.GetDisplayTypeName(ctx, tn) + ")";
                if (!other.Key.Name.EndsWith(")", StringComparison.Ordinal))
                {
                    tn = other.Value.DeclaringType;
                    if (tn != null)
                        other.Key.Name += " (" + adapter.GetDisplayTypeName(ctx, tn) + ")";
                }
            }

            names[oval.Name] = new KeyValuePair<ObjectValue, ValueReference<TType, TValue>>(oval, val);
        }
    }

    public struct EnumMember
    {
        public string Name { get; set; }
        public long Value { get; set; }
    }
}
