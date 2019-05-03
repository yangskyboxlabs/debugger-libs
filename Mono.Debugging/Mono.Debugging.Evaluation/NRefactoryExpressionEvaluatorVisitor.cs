//
// NRefactoryExpressionEvaluatorVisitor.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2013 Xamarin Inc.
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
using System.Linq;
using System.Reflection;
using ICSharpCode.NRefactory.CSharp;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation.Extension;
using Mono.Debugging.Evaluation.LambdaCompilation;
using Mono.Debugging.Evaluation.RuntimeInvocation;

namespace Mono.Debugging.Evaluation
{
    public class NRefactoryExpressionEvaluatorVisitor<TType, TValue> : IAstVisitor<ValueReference<TType, TValue>>
        where TType : class
        where TValue : class
    {
        readonly Dictionary<Expression, ValueReference<TType, TValue>> cachedValues = new Dictionary<Expression, ValueReference<TType, TValue>>();
        readonly ObjectValueAdaptor<TType, TValue> adapter;
        readonly Dictionary<string, ValueReference<TType, TValue>> userVariables;
        readonly EvaluationOptions options;
        readonly EvaluationContext ctx;
        readonly TType expectedType;
        readonly string expression;

        public NRefactoryExpressionEvaluatorVisitor(EvaluationContext ctx, string expression, TType expectedType, Dictionary<string, ValueReference<TType, TValue>> userVariables)
        {
            this.ctx = ctx;
            this.expression = expression;
            this.expectedType = expectedType;
            this.userVariables = userVariables;
            this.options = ctx.Options;
        }

        static Exception ParseError(string message, params object[] args)
        {
            return new EvaluatorException(message, args);
        }

        static Exception NotSupported()
        {
            return new NotSupportedExpressionException();
        }

        static string ResolveTypeName(AstType type)
        {
            string name = type.ToString();
            if (name.StartsWith("global::", StringComparison.Ordinal))
                name = name.Substring("global::".Length);
            return name;
        }

        static long GetInteger(object val)
        {
            try
            {
                return Convert.ToInt64(val);
            }
            catch
            {
                throw ParseError("Expected integer value.");
            }
        }

        long ConvertToInt64(TValue val)
        {
            if (val is IntPtr)
                return Convert.ToInt64(val);

            if (adapter.IsEnum(ctx, val))
            {
                var type = adapter.GetType(ctx, "System.Int64");
                var result = adapter.Cast(ctx, val, type);

                return (long)adapter.TargetObjectToObject(ctx, result);
            }

            return Convert.ToInt64(val);
        }

        static Type GetCommonOperationType(object v1, object v2)
        {
            if (v1 is double || v2 is double)
                return typeof(double);

            if (v1 is float || v2 is float)
                return typeof(double);

            return typeof(long);
        }

        static Type GetCommonType(object v1, object v2)
        {
            var t1 = Type.GetTypeCode(v1.GetType());
            var t2 = Type.GetTypeCode(v2.GetType());
            if (t1 < TypeCode.Int32 && t2 < TypeCode.Int32)
                return typeof(int);
            else
                switch ((TypeCode)Math.Max((int)t1, (int)t2))
                {
                    case TypeCode.Byte: return typeof(byte);
                    case TypeCode.Decimal: return typeof(decimal);
                    case TypeCode.Double: return typeof(double);
                    case TypeCode.Int16: return typeof(short);
                    case TypeCode.Int32: return typeof(int);
                    case TypeCode.Int64: return typeof(long);
                    case TypeCode.SByte: return typeof(sbyte);
                    case TypeCode.Single: return typeof(float);
                    case TypeCode.UInt16: return typeof(ushort);
                    case TypeCode.UInt32: return typeof(uint);
                    case TypeCode.UInt64: return typeof(ulong);
                    default: throw new Exception(((TypeCode)Math.Max((int)t1, (int)t2)).ToString());
                }
        }

        static object EvaluateOperation(BinaryOperatorType op, double v1, double v2)
        {
            switch (op)
            {
                case BinaryOperatorType.Add: return v1 + v2;
                case BinaryOperatorType.Divide: return v1 / v2;
                case BinaryOperatorType.Multiply: return v1 * v2;
                case BinaryOperatorType.Subtract: return v1 - v2;
                case BinaryOperatorType.GreaterThan: return v1 > v2;
                case BinaryOperatorType.GreaterThanOrEqual: return v1 >= v2;
                case BinaryOperatorType.LessThan: return v1 < v2;
                case BinaryOperatorType.LessThanOrEqual: return v1 <= v2;
                case BinaryOperatorType.Equality: return v1 == v2;
                case BinaryOperatorType.InEquality: return v1 != v2;
                default: throw ParseError("Invalid binary operator.");
            }
        }

        static object EvaluateOperation(BinaryOperatorType op, long v1, long v2)
        {
            switch (op)
            {
                case BinaryOperatorType.Add: return v1 + v2;
                case BinaryOperatorType.BitwiseAnd: return v1 & v2;
                case BinaryOperatorType.BitwiseOr: return v1 | v2;
                case BinaryOperatorType.ExclusiveOr: return v1 ^ v2;
                case BinaryOperatorType.Divide: return v1 / v2;
                case BinaryOperatorType.Modulus: return v1 % v2;
                case BinaryOperatorType.Multiply: return v1 * v2;
                case BinaryOperatorType.ShiftLeft: return v1 << (int)v2;
                case BinaryOperatorType.ShiftRight: return v1 >> (int)v2;
                case BinaryOperatorType.Subtract: return v1 - v2;
                case BinaryOperatorType.GreaterThan: return v1 > v2;
                case BinaryOperatorType.GreaterThanOrEqual: return v1 >= v2;
                case BinaryOperatorType.LessThan: return v1 < v2;
                case BinaryOperatorType.LessThanOrEqual: return v1 <= v2;
                case BinaryOperatorType.Equality: return v1 == v2;
                case BinaryOperatorType.InEquality: return v1 != v2;
                default: throw ParseError("Invalid binary operator.");
            }
        }

        static bool CheckReferenceEquality(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext ctx,
            TValue v1,
            TValue v2)
        {
            if (v1 == null && v2 == null)
                return true;

            if (v1 == null || v2 == null)
                return false;

            TType objectType = adapter.GetType(ctx, "System.Object");
            TValue[] args = { v1, v2 };

            var result = adapter.Invocator.InvokeStaticMethod(ctx, objectType, "ReferenceEquals", args);
            return result.Result.ToRawValue(adapter, ctx).ToPrimitive<bool>();
        }

        static bool CheckEquality(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext ctx,
            bool negate,
            TType type1,
            TType type2,
            IRawValue<TValue> value1,
            IRawValue<TValue> value2)
        {
            if (value1 == null && value2 == null)
                return !negate;

            if (value1 == null || value2 == null)
                return negate;

            string method = negate ? "op_Inequality" : "op_Equality";
            TType[] argTypes = { type1, type2 };
            TValue[] objArray =
            {
                value1.TargetObject,
                value2.TargetObject
            };

            IResolutionResult resolutionResult = adapter.MethodResolver.ResolveOwnMethod(
                ctx,
                method,
                type1,
                adapter.EmptyTypeArray,
                argTypes,
                BindingFlags.Public | BindingFlags.Static);
            if (resolutionResult.IsSuccess())
            {
                negate = false;
            }
            else
            {
                resolutionResult = adapter.MethodResolver.ResolveOwnMethod(
                    ctx,
                    method,
                    type2,
                    adapter.EmptyTypeArray,
                    argTypes,
                    BindingFlags.Public | BindingFlags.Static);
                if (resolutionResult.IsSuccess())
                {
                    negate = false;
                }
                else
                {
                    method = adapter.IsValueType(type1) ? "Equals" : "ReferenceEquals";
                    TType type = adapter.GetType(ctx, "System.Object");
                    resolutionResult = adapter.MethodResolver.ResolveOwnMethod(ctx, method, type, adapter.EmptyTypeArray, argTypes, BindingFlags.Public | BindingFlags.Static);
                }
            }

            InvocationResult<TValue> invocationResult = adapter.Invocator.RuntimeInvoke(ctx, resolutionResult.ToStaticCallInfo(objArray));
            bool retval = invocationResult.Result.ToRawValue(adapter, ctx).ToPrimitive<bool>();
            return !negate ? retval : !retval;
        }

        static ValueReference<TType, TValue> EvaluateOverloadedOperator(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext ctx,
            string expression,
            BinaryOperatorType op,
            TType type1,
            TType type2,
            IRawValue<TValue> val1,
            IRawValue<TValue> val2)
        {
            TValue[] arguments = { val1.TargetObject, val2.TargetObject };
            TType[] argTypes = { type1, type2 };
            object targetType = null;
            string methodName = null;

            switch (op)
            {
                case BinaryOperatorType.BitwiseAnd:
                    methodName = "op_BitwiseAnd";
                    break;
                case BinaryOperatorType.BitwiseOr:
                    methodName = "op_BitwiseOr";
                    break;
                case BinaryOperatorType.ExclusiveOr:
                    methodName = "op_ExclusiveOr";
                    break;
                case BinaryOperatorType.GreaterThan:
                    methodName = "op_GreaterThan";
                    break;
                case BinaryOperatorType.GreaterThanOrEqual:
                    methodName = "op_GreaterThanOrEqual";
                    break;
                case BinaryOperatorType.Equality:
                    methodName = "op_Equality";
                    break;
                case BinaryOperatorType.InEquality:
                    methodName = "op_Inequality";
                    break;
                case BinaryOperatorType.LessThan:
                    methodName = "op_LessThan";
                    break;
                case BinaryOperatorType.LessThanOrEqual:
                    methodName = "op_LessThanOrEqual";
                    break;
                case BinaryOperatorType.Add:
                    methodName = "op_Addition";
                    break;
                case BinaryOperatorType.Subtract:
                    methodName = "op_Subtraction";
                    break;
                case BinaryOperatorType.Multiply:
                    methodName = "op_Multiply";
                    break;
                case BinaryOperatorType.Divide:
                    methodName = "op_Division";
                    break;
                case BinaryOperatorType.Modulus:
                    methodName = "op_Modulus";
                    break;
                case BinaryOperatorType.ShiftLeft:
                    methodName = "op_LeftShift";
                    break;
                case BinaryOperatorType.ShiftRight:
                    methodName = "op_RightShift";
                    break;
            }

            if (methodName == null)
                throw ParseError("Invalid operands in binary operator.");

            IResolutionResult resolutionResult = adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, type1, adapter.EmptyTypeArray, argTypes, BindingFlags.Public | BindingFlags.Static);
            if (resolutionResult.IsSuccess())
            {
                Need to check when everything works.resolutionResult = adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, type2, adapter.EmptyTypeArray, argTypes, BindingFlags.Public);
            }
            else
            {
                throw ParseError("Invalid operands in binary operator.");
            }

            TValue result = adapter.Invocator.RuntimeInvoke(ctx, resolutionResult.ToStaticCallInfo(arguments)).Result;
            return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, result);
        }

        ValueReference<TType, TValue> EvaluateBinaryOperatorExpression(
            BinaryOperatorType op,
            ValueReference<TType, TValue> left,
            Expression rightExp)
        {
            IRawValue<TValue> val = left.ObjectValue;
            switch (op)
            {
                case BinaryOperatorType.ConditionalAnd:
                {
                    if (!val.TryToPrimitive(out bool primitiveValue))
                        throw ParseError("Left operand of logical And must be a boolean.");

                    if (!primitiveValue)
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, false);

                    var valueReference = rightExp.AcceptVisitor(this);
                    if (valueReference == null || adapter.GetTypeName(ctx, valueReference.Type) != "System.Boolean")
                        throw ParseError("Right operand of logical And must be a boolean.");

                    return valueReference;
                }

                case BinaryOperatorType.ConditionalOr:
                {
                    if (!val.TryToPrimitive(out bool primitiveValue))
                        throw ParseError("Left operand of logical Or must be a boolean.");

                    if (primitiveValue)
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, true);

                    var vr = rightExp.AcceptVisitor(this);
                    if (vr == null || adapter.GetTypeName(ctx, vr.Type) != "System.Boolean")
                        throw ParseError("Right operand of logical Or must be a boolean.");

                    return vr;
                }
            }

            ValueReference<TType, TValue> right = rightExp.AcceptVisitor(this);
            TValue targetObject1 = left.ObjectValue.TargetObject;
            TValue targetObject2 = right.ObjectValue.TargetObject;
            TType type1 = adapter.GetValueType(ctx, targetObject1);
            TType type2 = adapter.GetValueType(ctx, targetObject2);
            IRawValue<TValue> val1 = left.ObjectValue;
            IRawValue<TValue> val2 = right.ObjectValue;
            object res = null;

            if (adapter.IsNullableType(ctx, type1) && adapter.NullableHasValue(ctx, type1, targetObject1))
            {
                if (val2.IsNull)
                {
                    if (op == BinaryOperatorType.Equality)
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, false);
                    if (op == BinaryOperatorType.InEquality)
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, true);
                }

                ValueReference<TType, TValue> nullable = adapter.NullableGetValue(ctx, type1, targetObject1);
                targetObject1 = nullable.Value;
                val1 = nullable.ObjectValue;
                type1 = nullable.Type;
            }

            if (adapter.IsNullableType(ctx, type2) && adapter.NullableHasValue(ctx, type2, targetObject2))
            {
                if (val1.IsNull)
                {
                    if (op == BinaryOperatorType.Equality)
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, false);
                    if (op == BinaryOperatorType.InEquality)
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, true);
                }

                ValueReference<TType, TValue> nullable = adapter.NullableGetValue(ctx, type2, targetObject2);
                targetObject2 = nullable.Value;
                val2 = nullable.ObjectValue;
                type2 = nullable.Type;
            }

            if (val1.IsString() || val2.IsString())
            {
                switch (op)
                {
                    case BinaryOperatorType.Add:
                        if (!val.IsNull && !val.IsNull)
                        {
                            res =
                                (val.TryToString() ?? adapter.CallToString(ctx, targetObject1))
                                + (val2.TryToString() ?? adapter.CallToString(ctx, targetObject2));
                        }
                        else if (!val1.IsNull)
                        {
                            res = val1.ToString();
                        }
                        else if (!val2.IsNull)
                        {
                            res = val2.ToString();
                        }

                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, res);
                    case BinaryOperatorType.Equality:
                        if ((val1.IsNull || val1.IsString()) && (val2.IsNull || val2.IsString()))
                            return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, val1.TryToString() == val2.TryToString());
                        break;
                    case BinaryOperatorType.InEquality:
                        if ((val1.IsNull || val1.IsString()) && (val2.IsNull || val2.IsString()))
                            return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, val1.TryToString() != val2.TryToString());
                        break;
                }
            }

            if (val1.IsNull || !adapter.IsPrimitive(ctx, targetObject1) && !adapter.IsEnum(ctx, targetObject1))
            {
                if (op == BinaryOperatorType.Equality)
                {
                    return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, CheckEquality(adapter, ctx, false, type1, type2, val1, val2));
                }

                if (op == BinaryOperatorType.InEquality)
                {
                    return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, CheckEquality(adapter, ctx, true, type1, type2, val1, val2));
                }

                if (!val1.IsNull && !val2.IsNull)
                    return EvaluateOverloadedOperator(adapter, ctx, expression, op, type1, type2, val1, val2);
            }

            if (val1.TryToPrimitive(out bool primitiveValue1) && val2.TryToPrimitive(out bool primitiveValue2))
            {
                switch (op)
                {
                    case BinaryOperatorType.ExclusiveOr:
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, primitiveValue1 ^ primitiveValue2);
                    case BinaryOperatorType.Equality:
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, primitiveValue1 == primitiveValue2);
                    case BinaryOperatorType.InEquality:
                        return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, primitiveValue1 != primitiveValue2);
                }
            }

            if (val1.IsNull || val2.IsNull || val1.Is<bool>() || val2.Is<bool>())
                throw ParseError("Invalid operands in binary operator.");

//            var commonType = GetCommonOperationType(val1, val2);
//
//            if (commonType == typeof(double))
//            {
//                double v1, v2;
//
//                try
//                {
//                    v1 = Convert.ToDouble(val1);
//                    v2 = Convert.ToDouble(val2);
//                }
//                catch
//                {
//                    throw ParseError("Invalid operands in binary operator.");
//                }
//
//                res = EvaluateOperation(op, v1, v2);
//            }
//            else
//            {
//                var v1 = ConvertToInt64(val1);
//                var v2 = ConvertToInt64(val2);
//
//                res = EvaluateOperation(op, v1, v2);
//            }

            TType longType = adapter.GetType(ctx, "System.Int64");
            if (adapter.IsEnum(ctx, targetObject1))
            {
                val1 = adapter.Cast(ctx, targetObject1, longType).ToRawValue(adapter, ctx);
            }

            if (adapter.IsEnum(ctx, targetObject2))
            {
                val2 = adapter.Cast(ctx, targetObject2, longType).ToRawValue(adapter, ctx);
            }

            var targetType = GetCommonType(val1, val2);

            if (targetType != typeof(IntPtr))
                res = Convert.ChangeType(res, targetType);
            else
                res = new IntPtr((long)res);

            return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, res);
        }

        static string ResolveType(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            TypeReferenceExpression mre,
            List<TType> args)
        {
            var memberType = mre.Type as MemberType;

            if (memberType != null)
            {
                var name = memberType.MemberName;

                if (memberType.TypeArguments.Count > 0)
                {
                    name += "`" + memberType.TypeArguments.Count;

                    foreach (var arg in memberType.TypeArguments)
                    {
                        var resolved = arg.Resolve(adaptor, ctx);

                        if (resolved == null)
                            return null;

                        args.Add(resolved);
                    }
                }

                return name;
            }

            return mre.ToString();
        }

        static string ResolveType(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            MemberReferenceExpression mre,
            List<TType> args)
        {
            string parent, name;

            if (mre.Target is MemberReferenceExpression)
            {
                parent = ResolveType(adaptor, ctx, (MemberReferenceExpression)mre.Target, args);
            }
            else if (mre.Target is TypeReferenceExpression)
            {
                parent = ResolveType(adaptor, ctx, (TypeReferenceExpression)mre.Target, args);
            }
            else if (mre.Target is IdentifierExpression)
            {
                parent = ((IdentifierExpression)mre.Target).Identifier;
            }
            else
            {
                return null;
            }

            name = parent + "." + mre.MemberName;
            if (mre.TypeArguments.Count > 0)
            {
                name += "`" + mre.TypeArguments.Count;

                foreach (var arg in mre.TypeArguments)
                {
                    var resolved = arg.Resolve(adaptor, ctx);

                    if (resolved == null)
                        return null;

                    args.Add(resolved);
                }
            }

            return name;
        }

        static TType ResolveType(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            MemberReferenceExpression mre)
        {
            var args = new List<TType>();
            var name = ResolveType(adaptor, ctx, mre, args);

            if (name == null)
                return null;

            if (args.Count > 0)
                return adaptor.GetType(ctx, name, args.ToArray());

            return adaptor.GetType(ctx, name);
        }

        ValueReference<TType, TValue> ResolveTypeValueReference(EvaluationContext ctx, MemberReferenceExpression mre)
        {
            TType resolved = ResolveType(adapter, ctx, mre);

            if (resolved != null)
            {
                adapter.ForceLoadType(ctx, resolved);

                return new TypeValueReference<TType, TValue>(adapter, ctx, resolved);
            }

            throw ParseError("Could not resolve type: {0}", mre);
        }

        ValueReference<TType, TValue> ResolveTypeValueReference(EvaluationContext ctx, AstType type)
        {
            TType resolved = type.Resolve(adapter, ctx);

            if (resolved != null)
            {
                adapter.ForceLoadType(ctx, resolved);

                return new TypeValueReference<TType, TValue>(adapter, ctx, resolved);
            }

            throw ParseError("Could not resolve type: {0}", ResolveTypeName(type));
        }

        public bool TryGetValueFromCache(
            Expression expr,
            out ValueReference<TType, TValue> value)
        {
            return cachedValues.TryGetValue(expr, out value);
        }

        public void AddOrUpdateValueToCache(
            Expression expr,
            ValueReference<TType, TValue> value)
        {
            cachedValues[expr] = value;
        }

        static TType[] UpdateDelayedTypes(TType[] types, Tuple<int, TType>[] updates, ref bool alreadyUpdated)
        {
            if (alreadyUpdated || types == null || updates == null || types.Length < updates.Length || updates.Length == 0)
                return types;

            for (int x = 0; x < updates.Length; x++)
            {
                int index = updates[x].Item1;
                types[index] = updates[x].Item2;
            }

            alreadyUpdated = true;
            return types;
        }

        #region IAstVisitor implementation

        public ValueReference<TType, TValue> VisitAnonymousMethodExpression(AnonymousMethodExpression anonymousMethodExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitUndocumentedExpression(UndocumentedExpression undocumentedExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitArrayCreateExpression(ArrayCreateExpression arrayCreateExpression)
        {
            if (!(arrayCreateExpression.Type.AcceptVisitor(this) is TypeValueReference<TType, TValue> type))
                throw ParseError("Invalid type in array creation.");
            var lengths = new TValue [arrayCreateExpression.Arguments.Count];
            for (int i = 0; i < lengths.Length; i++)
            {
                lengths[i] = arrayCreateExpression.Arguments.ElementAt(i).AcceptVisitor(this).ObjectValue.TargetObject;
            }

            var array = adapter.CreateArray(ctx, type.Type, lengths);
            if (arrayCreateExpression.Initializer.Elements.Any())
            {
                var arrayAdaptor = adapter.CreateArrayAdaptor(ctx, array);
                int index = 0;
                foreach (var el in LinearElements(arrayCreateExpression.Initializer.Elements))
                {
                    arrayAdaptor.SetElement(new[] { index++ }, el.AcceptVisitor(this).Value);
                }
            }

            return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, array);
        }

        IEnumerable<Expression> LinearElements(AstNodeCollection<Expression> elements)
        {
            foreach (var el in elements)
            {
                if (el is ArrayInitializerExpression)
                    foreach (var el2 in LinearElements(((ArrayInitializerExpression)el).Elements))
                    {
                        yield return el2;
                    }
                else
                    yield return el;
            }
        }

        public ValueReference<TType, TValue> VisitArrayInitializerExpression(ArrayInitializerExpression arrayInitializerExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitAsExpression(AsExpression asExpression)
        {
            if (!(asExpression.Type.AcceptVisitor(this) is TypeValueReference<TType, TValue> type))
                throw ParseError("Invalid type in cast.");

            var val = asExpression.Expression.AcceptVisitor(this);
            var result = adapter.TryCast(ctx, val.Value, type.Type);

            if (result == null)
                return new NullValueReference<TType, TValue>(adapter, ctx, type.Type);

            return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, result, type.Type);
        }

        public ValueReference<TType, TValue> VisitAssignmentExpression(AssignmentExpression assignmentExpression)
        {
            ctx.AssertMethodEvaluationAllowed();

            var left = assignmentExpression.Left.AcceptVisitor(this);

            if (assignmentExpression.Operator == AssignmentOperatorType.Assign)
            {
                var right = assignmentExpression.Right.AcceptVisitor(this);
                if (left is UserVariableReference<TType, TValue>)
                {
                    left.Value = right.Value;
                }
                else
                {
                    var castedValue = adapter.TryCast(ctx, right.Value, left.Type);
                    left.Value = castedValue;
                }
            }
            else
            {
                BinaryOperatorType op;

                switch (assignmentExpression.Operator)
                {
                    case AssignmentOperatorType.Add:
                        op = BinaryOperatorType.Add;
                        break;
                    case AssignmentOperatorType.Subtract:
                        op = BinaryOperatorType.Subtract;
                        break;
                    case AssignmentOperatorType.Multiply:
                        op = BinaryOperatorType.Multiply;
                        break;
                    case AssignmentOperatorType.Divide:
                        op = BinaryOperatorType.Divide;
                        break;
                    case AssignmentOperatorType.Modulus:
                        op = BinaryOperatorType.Modulus;
                        break;
                    case AssignmentOperatorType.ShiftLeft:
                        op = BinaryOperatorType.ShiftLeft;
                        break;
                    case AssignmentOperatorType.ShiftRight:
                        op = BinaryOperatorType.ShiftRight;
                        break;
                    case AssignmentOperatorType.BitwiseAnd:
                        op = BinaryOperatorType.BitwiseAnd;
                        break;
                    case AssignmentOperatorType.BitwiseOr:
                        op = BinaryOperatorType.BitwiseOr;
                        break;
                    case AssignmentOperatorType.ExclusiveOr:
                        op = BinaryOperatorType.ExclusiveOr;
                        break;
                    default: throw ParseError("Invalid operator in assignment.");
                }

                var result = EvaluateBinaryOperatorExpression(op, left, assignmentExpression.Right);
                left.Value = result.Value;
            }

            return left;
        }

        public ValueReference<TType, TValue> VisitBaseReferenceExpression(BaseReferenceExpression baseReferenceExpression)
        {
            var self = adapter.GetThisReference(ctx);

            if (self != null)
                return LiteralValueReference.CreateTargetBaseObjectLiteral(adapter, ctx, expression, self.Value);

            throw ParseError("'base' reference not available in static methods.");
        }

        public ValueReference<TType, TValue> VisitBinaryOperatorExpression(BinaryOperatorExpression binaryOperatorExpression)
        {
            var left = binaryOperatorExpression.Left.AcceptVisitor(this);

            return EvaluateBinaryOperatorExpression(binaryOperatorExpression.Operator, left, binaryOperatorExpression.Right);
        }

        public ValueReference<TType, TValue> VisitCastExpression(CastExpression castExpression)
        {
            if (!(castExpression.Type.AcceptVisitor(this) is TypeValueReference<TType, TValue> type))
                throw ParseError("Invalid type in cast.");

            var val = castExpression.Expression.AcceptVisitor(this);
            TValue result = adapter.TryCast(ctx, val.Value, type.Type);
            if (result == null)
                throw ParseError("Invalid cast.");

            return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, result, type.Type);
        }

        public ValueReference<TType, TValue> VisitCheckedExpression(CheckedExpression checkedExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitConditionalExpression(ConditionalExpression conditionalExpression)
        {
            ValueReference<TType, TValue> val = conditionalExpression.Condition.AcceptVisitor(this);
            if (val is TypeValueReference<TType, TValue>)
                throw NotSupported();

            if (val.ObjectValue.ToPrimitive<bool>())
                return conditionalExpression.TrueExpression.AcceptVisitor(this);

            return conditionalExpression.FalseExpression.AcceptVisitor(this);
        }

        public ValueReference<TType, TValue> VisitDefaultValueExpression(DefaultValueExpression defaultValueExpression)
        {
            if (!(defaultValueExpression.Type.AcceptVisitor(this) is TypeValueReference<TType, TValue> type))
                throw ParseError("Invalid type in 'default' expression.");
            if (adapter.IsClass(ctx, type.Type))
                return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, adapter.CreateNullValue(ctx, type.Type), type.Type);
            if (adapter.IsValueType(type.Type))
                return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, adapter.CreateValue(ctx, type.Type), type.Type);
            switch (adapter.GetTypeName(ctx, type.Type))
            {
                case "System.Boolean": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, false);
                case "System.Char": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, '\0');
                case "System.Byte": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (byte)0);
                case "System.SByte": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (sbyte)0);
                case "System.Int16": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (short)0);
                case "System.UInt16": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (ushort)0);
                case "System.Int32": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (int)0);
                case "System.UInt32": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (uint)0);
                case "System.Int64": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (long)0);
                case "System.UInt64": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (ulong)0);
                case "System.Decimal": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (decimal)0);
                case "System.Single": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (float)0);
                case "System.Double": return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, (double)0);
                default: throw new Exception($"Unexpected type {adapter.GetTypeName(ctx, type.Type)}");
            }
        }

        public ValueReference<TType, TValue> VisitDirectionExpression(DirectionExpression directionExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitIdentifierExpression(IdentifierExpression identifierExpression)
        {
            var name = identifierExpression.Identifier;

            if (name == "__EXCEPTION_OBJECT__")
                return adapter.GetCurrentException(ctx);

            // Look in user defined variables

            ValueReference<TType, TValue> userVar;
            if (userVariables.TryGetValue(name, out userVar))
                return userVar;

            // Look in variables

            ValueReference<TType, TValue> var = adapter.GetLocalVariable(ctx, name);
            if (var != null)
                return var;

            // Look in parameters

            var = adapter.GetParameter(ctx, name);
            if (var != null)
                return var;

            // Look in instance fields and properties

            ValueReference<TType, TValue> self = adapter.GetThisReference(ctx);

            if (self != null)
            {
                // check for fields and properties in this instance

                // first try if current type has field or property
                var = adapter.GetMember(ctx, self, adapter.GetEnclosingType(ctx), self.Value, name);
                if (var != null)
                    return var;

                var = adapter.GetMember(ctx, self, self.Type, self.Value, name);
                if (var != null)
                    return var;
            }

            // Look in static fields & properties of the enclosing type and all parent types

            TType type = adapter.GetEnclosingType(ctx);
            TType vtype = type;

            while (vtype != null)
            {
                // check for static fields and properties
                var = adapter.GetMember(ctx, null, vtype, null, name);
                if (var != null)
                    return var;

                vtype = adapter.GetParentType(ctx, vtype);
            }

            // Look in types

            vtype = adapter.GetType(ctx, name);
            if (vtype != null)
                return new TypeValueReference<TType, TValue>(adapter, ctx, vtype);

            if (self == null && adapter.HasMember(ctx, type, name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string message = string.Format("An object reference is required for the non-static field, method, or property '{0}.{1}'.",
                    adapter.GetDisplayTypeName(ctx, type), name);
                throw ParseError(message);
            }

            var namespaces = adapter.GetImportedNamespaces(ctx);
            if (namespaces.Any(x => x.Split('.').Contains(name)))
            {
                return new NamespaceValueReference<TType, TValue>(adapter, ctx, name);
            }

            throw ParseError("Unknown identifier: {0}", name);
        }

        public ValueReference<TType, TValue> VisitIndexerExpression(IndexerExpression indexerExpression)
        {
            int n = 0;

            var target = indexerExpression.Target.AcceptVisitor(this);
            if (target is TypeValueReference<TType, TValue>)
                throw NotSupported();

            if (adapter.IsArray(ctx, target.Value))
            {
                int[] indexes = new int [indexerExpression.Arguments.Count];

                foreach (var arg in indexerExpression.Arguments)
                {
                    var index = arg.AcceptVisitor(this);
                    indexes[n++] = (int)Convert.ChangeType(index.ObjectValue, typeof(int));
                }

                return new ArrayValueReference<TType, TValue>(adapter, ctx, target.Value, indexes);
            }

            TValue[] args = new TValue [indexerExpression.Arguments.Count];
            foreach (var arg in indexerExpression.Arguments)
                args[n++] = arg.AcceptVisitor(this).Value;

            var indexer = adapter.GetIndexerReference(ctx, target.Value, target.Type, args);
            if (indexer == null)
                throw NotSupported();

            return indexer;
        }

        string ResolveMethodName(MemberReferenceExpression method, out TType[] typeArgs)
        {
            if (method.TypeArguments.Count > 0)
            {
                var args = new List<TType>();

                foreach (var arg in method.TypeArguments)
                {
                    var type = arg.AcceptVisitor(this);
                    args.Add(type.Type);
                }

                typeArgs = args.ToArray();
            }
            else
            {
                typeArgs = null;
            }

            return method.MemberName;
        }

        string ResolveMethodName(IdentifierExpression method, out TType[] typeArgs)
        {
            if (method.TypeArguments.Count > 0)
            {
                var args = new List<TType>();

                foreach (var arg in method.TypeArguments)
                {
                    var type = arg.AcceptVisitor(this);
                    args.Add(type.Type);
                }

                typeArgs = args.ToArray();
            }
            else
            {
                typeArgs = null;
            }

            return method.Identifier;
        }

        static void ValidateLambdaCompilationResult(CompilationResult<TType, TValue> result)
        {
            if (result.HasErrors)
                throw new EvaluatorException("{0}", result.Errors.First());
        }

        void AddRangeToCache(Dictionary<LambdaExpression, ValueReference<TType, TValue>> calculatedLambdas)
        {
            foreach (var calculatedLambda in calculatedLambdas)
            {
                AddOrUpdateValueToCache(calculatedLambda.Key, calculatedLambda.Value);
            }
        }

        void ProcessLambdaCompilationResult(CompilationResult<TType, TValue> result)
        {
            ValidateLambdaCompilationResult(result);
            AddRangeToCache(result.CalculatedLambdas);
        }

        InvocationInfo<TValue> GetInvocationInfo(InvocationExpression invocationExpression)
        {
            var types = new TType [invocationExpression.Arguments.Count];
            var args = new TValue [invocationExpression.Arguments.Count];
            int n = 0;

            foreach (var arg in invocationExpression.Arguments)
            {
                var valueReference = arg.AcceptVisitor(this);
                args[n] = valueReference.Value;
                types[n] = adapter.GetValueType(ctx, args[n]);

//                if (adapter.IsDelayedType(ctx, types[n]))
//                    allArgTypesAreResolved = false;
                n++;
            }

            if (invocationExpression.Target is MemberReferenceExpression)
            {
                var field = (MemberReferenceExpression)invocationExpression.Target;
                ValueReference<TType, TValue> valueReference = field.Target.AcceptVisitorIfNeeded(this);
                string methodName = ResolveMethodName(field, out var typeArgs);
                if (valueReference is TypeValueReference<TType, TValue>)
                {
                    return adapter.MethodResolver.ResolveOwnMethod(
                            ctx,
                            methodName,
                            valueReference.Type,
                            typeArgs,
                            types,
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic)
                        .ThrowIfFailed()
                        .ToStaticCallInfo(args);
                }

                var resolutionResult = adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, valueReference.Type, typeArgs, types, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (resolutionResult.IsSuccess())
                {
                    return resolutionResult.ToInstanceCallInfo(valueReference.Value, args);
                }

                return adapter.MethodResolver.ResolveExtensionMethod(ctx, methodName, valueReference.Type, typeArgs, types)
                    .ThrowIfFailed().ToExtensionCallInfo(valueReference.Value, args);
            }

            if (invocationExpression.Target is IdentifierExpression)
            {
                var method = (IdentifierExpression)invocationExpression.Target;
                var vref = adapter.GetThisReference(ctx);

                string methodName = ResolveMethodName(method, out var typeArgs);

                if (vref == null)
                {
                    return adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, adapter.GetEnclosingType(ctx), typeArgs, types, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .ThrowIfFailed()
                        .ToStaticCallInfo(args);
                }

                TType vtype = adapter.GetEnclosingType(ctx);

                // There is an instance method for 'this', although it may not have an exact signature match. Check it now.
                IResolutionResult resolutionResult = adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, vtype, typeArgs, types, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (resolutionResult.IsSuccess())
                {
                    return resolutionResult.ToInstanceCallInfo(vref.Value, args);
                }

                resolutionResult = adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, vref.Type, typeArgs, types, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                // There isn't an instance method with exact signature match.
                // If there isn't a static method, then use the instance method,
                // which will report the signature match error when invoked
                if (resolutionResult.IsSuccess())
                {
                    resolutionResult.ToInstanceCallInfo(vref.Value, args);
                }

                return adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, adapter.GetEnclosingType(ctx), typeArgs, types, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .ThrowIfFailed()
                    .ToStaticCallInfo(args);
            }

            // TODO: Show detailed error message for why lambda types were not
            // resolved. Major causes are:
            // 1. there is no matched method
            // 2. matched method exists, but the lambda body has some invalid
            // expressions and does not compile
            throw NotSupported();

//            bool invokeBaseMethod = false;
//            bool allArgTypesAreResolved = true;
//            ValueReference target = null;
//            string methodName;
//
//            object[] typeArgs = null;
//
//
//            object vtype = null;
//            Tuple<int, object>[] resolvedLambdaTypes;
//
//            if (invocationExpression.Target is MemberReferenceExpression)
//            {
//                var field = (MemberReferenceExpression)invocationExpression.Target;
//                target = field.Target.AcceptVisitor<ValueReference<TType, TValue>>(this);
//                if (field.Target is BaseReferenceExpression)
//                    invokeBaseMethod = true;
//                methodName = ResolveMethodName(field, out typeArgs);
//            }
//            else if (invocationExpression.Target is IdentifierExpression)
//            {
//                var method = (IdentifierExpression)invocationExpression.Target;
//                var vref = adapter.GetThisReference(ctx);
//
//                methodName = ResolveMethodName(method, out typeArgs);
//
//                if (vref != null && adapter.HasMethod(ctx, vref.Type, methodName, typeArgs, types, BindingFlags.Instance))
//                {
//                    vtype = adapter.GetEnclosingType(ctx);
//
//                    // There is an instance method for 'this', although it may not have an exact signature match. Check it now.
//                    if (adapter.HasMethod(ctx, vref.Type, methodName, typeArgs, types, BindingFlags.Instance))
//                    {
//                        target = vref;
//                    }
//                    else
//                    {
//                        // There isn't an instance method with exact signature match.
//                        // If there isn't a static method, then use the instance method,
//                        // which will report the signature match error when invoked
//                        if (!adapter.HasMethod(ctx, vtype, methodName, typeArgs, types, BindingFlags.Static))
//                            target = vref;
//                    }
//                }
//                else
//                {
//                    if (adapter.HasMethod(ctx, adapter.GetEnclosingType(ctx), methodName, types, BindingFlags.Instance))
//                        throw new EvaluatorException("Cannot invoke an instance method from a static method.");
//                    target = null;
//                }
//            }
//            else
//            {
//                throw NotSupported();
//            }
//
//            if (vtype == null)
//                vtype = target != null ? target.Type : adapter.GetEnclosingType(ctx);
//            object vtarget = (target is TypeValueReference) || target == null ? null : target.Value;
//
//            var hasMethod = adapter.HasMethod(ctx, vtype, methodName, typeArgs, types, BindingFlags.Instance | BindingFlags.Static, out resolvedLambdaTypes);
//            if (hasMethod)
//                types = UpdateDelayedTypes(types, resolvedLambdaTypes, ref allArgTypesAreResolved);
//
//            if (invokeBaseMethod)
//            {
//                vtype = adapter.GetBaseType(ctx, vtype);
//            }
//            else if (target != null && !hasMethod)
//            {
//                // Look for LINQ extension methods...
//                var linq = adapter.GetType(ctx, "System.Linq.Enumerable");
//                if (linq != null)
//                {
//                    object[] xtypeArgs = typeArgs;
//
//                    if (xtypeArgs == null)
//                    {
//                        // try to infer the generic type arguments from the type of the object...
//                        object xtype = vtype;
//                        while (xtype != null && !adapter.IsGenericType(ctx, xtype))
//                            xtype = adapter.GetBaseType(ctx, xtype);
//
//                        if (xtype != null)
//                            xtypeArgs = adapter.GetTypeArgs(ctx, xtype);
//                    }
//
//                    if (xtypeArgs == null && adapter.IsArray(ctx, vtarget))
//                    {
//                        xtypeArgs = new object[] { adapter.CreateArrayAdaptor(ctx, vtarget).ElementType };
//                    }
//
//                    if (xtypeArgs != null)
//                    {
//                        var xtypes = new object[types.Length + 1];
//                        Array.Copy(types, 0, xtypes, 1, types.Length);
//                        xtypes[0] = vtype;
//
//                        var xargs = new object[args.Length + 1];
//                        Array.Copy(args, 0, xargs, 1, args.Length);
//                        xargs[0] = vtarget;
//
//                        if (adapter.HasMethod(ctx, linq, methodName, xtypeArgs, xtypes, BindingFlags.Static, out resolvedLambdaTypes))
//                        {
//                            vtarget = null;
//                            vtype = linq;
//
//                            typeArgs = xtypeArgs;
//                            types = UpdateDelayedTypes(xtypes, resolvedLambdaTypes, ref allArgTypesAreResolved);
//                            args = xargs;
//                        }
//                    }
//                }
//            }
//
//            if (!allArgTypesAreResolved)
//            {
//                // TODO: Show detailed error message for why lambda types were not
//                // resolved. Major causes are:
//                // 1. there is no matched method
//                // 2. matched method exists, but the lambda body has some invalid
//                // expressions and does not compile
//                throw NotSupported();
//            }
        }

        public ValueReference<TType, TValue> VisitInvocationExpression(InvocationExpression invocationExpression)
        {
            ctx.AssertMethodEvaluationAllowed();

            if (invocationExpression.Arguments.ContainsLambda())
            {
                ProcessLambdaCompilationResult(InvocationExpressionLambdaCompiler<TType, TValue>.Compile(adapter, ctx, this, invocationExpression));
            }

            TValue result = adapter.Invocator.RuntimeInvoke(ctx, GetInvocationInfo(invocationExpression)).Result;
            if (result != null)
                return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, result, default);

            return LiteralValueReference.CreateVoidReturnLiteral(adapter, ctx, expression);
        }

        public ValueReference<TType, TValue> VisitIsExpression(IsExpression isExpression)
        {
            var type = (isExpression.Type.AcceptVisitor(this) as TypeValueReference<TType, TValue>)?.Type;
            if (type == null)
                throw ParseError("Invalid type in 'is' expression.");
            if (adapter.IsNullableType(ctx, type))
                type = adapter.GetGenericTypeArguments(ctx, type).Single();
            var val = isExpression.Expression.AcceptVisitor(this).Value;
            if (adapter.IsNull(ctx, val))
                return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, false);
            var valueIsPrimitive = adapter.IsPrimitive(ctx, val);
            var typeIsPrimitive = adapter.IsPrimitiveType(type);
            if (valueIsPrimitive != typeIsPrimitive)
                return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, false);
            if (typeIsPrimitive)
                return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, adapter.GetTypeName(ctx, type) == adapter.GetValueTypeName(ctx, val));
            return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, adapter.TryCast(ctx, val, type) != null);
        }

        public ValueReference<TType, TValue> VisitLambdaExpression(LambdaExpression lambdaExpression)
        {
            if (lambdaExpression.IsAsync)
                throw NotSupported();

            AstNode parent = lambdaExpression.Parent;
            while (parent != null && parent is ParenthesizedExpression)
                parent = parent.Parent;

            if (parent is InvocationExpression || parent is CastExpression)
            {
                var writer = new System.IO.StringWriter();
                var visitor = new LambdaBodyOutputVisitor<TType, TValue>(adapter, ctx, userVariables, writer);

                lambdaExpression.AcceptVisitor(visitor);
                var body = writer.ToString();
                var values = visitor.GetLocalValues();
                TValue val = adapter.CreateDelayedLambdaValue(ctx, body, values);
                if (val != null)
                    return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, val);
            }

            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitMemberReferenceExpression(MemberReferenceExpression memberReferenceExpression)
        {
            if (memberReferenceExpression.TypeArguments.Count > 0)
                return ResolveTypeValueReference(ctx, memberReferenceExpression);

            var target = memberReferenceExpression.Target.AcceptVisitor(this);
            var member = target.GetChild(memberReferenceExpression.MemberName, ctx.Options);

            if (member == null)
            {
                if (!(target is TypeValueReference<TType, TValue>))
                {
                    if (adapter.IsNull(ctx, target.Value))
                        throw new EvaluatorException("{0} is null", target.Name);
                }

                throw ParseError("Unknown member: {0}", memberReferenceExpression.MemberName);
            }

            return member;
        }

        public ValueReference<TType, TValue> VisitNamedArgumentExpression(NamedArgumentExpression namedArgumentExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitNamedExpression(NamedExpression namedExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitNullReferenceExpression(NullReferenceExpression nullReferenceExpression)
        {
            return new NullValueReference<TType, TValue>(adapter, ctx, adapter.GetType(ctx, "System.Object"));
        }

        public ValueReference<TType, TValue> VisitObjectCreateExpression(ObjectCreateExpression objectCreateExpression)
        {
            var type = objectCreateExpression.Type.AcceptVisitor(this) as TypeValueReference<TType, TValue>;
            var args = new List<TValue>();

            foreach (var arg in objectCreateExpression.Arguments)
            {
                var val = arg.AcceptVisitor(this);
                args.Add(val != null ? val.Value : null);
            }

            return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, expression, adapter.CreateValue(ctx, type.Type, args.ToArray()));
        }

        public ValueReference<TType, TValue> VisitAnonymousTypeCreateExpression(AnonymousTypeCreateExpression anonymousTypeCreateExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitParenthesizedExpression(ParenthesizedExpression parenthesizedExpression)
        {
            return parenthesizedExpression.Expression.AcceptVisitor(this);
        }

        public ValueReference<TType, TValue> VisitPointerReferenceExpression(PointerReferenceExpression pointerReferenceExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitPrimitiveExpression(PrimitiveExpression primitiveExpression)
        {
            if (primitiveExpression.Value != null)
                return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, primitiveExpression.Value);

            if (expectedType != null)
                return new NullValueReference<TType, TValue>(adapter, ctx, expectedType);

            return new NullValueReference<TType, TValue>(adapter, ctx, adapter.GetType(ctx, "System.Object"));
        }

        public ValueReference<TType, TValue> VisitSizeOfExpression(SizeOfExpression sizeOfExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitStackAllocExpression(StackAllocExpression stackAllocExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitThisReferenceExpression(ThisReferenceExpression thisReferenceExpression)
        {
            var self = adapter.GetThisReference(ctx);

            if (self == null)
                throw ParseError("'this' reference not available in the current evaluation context.");

            return self;
        }

        public ValueReference<TType, TValue> VisitTypeOfExpression(TypeOfExpression typeOfExpression)
        {
            var name = ResolveTypeName(typeOfExpression.Type);
            var type = typeOfExpression.Type.Resolve(adapter, ctx);

            if (type == null)
                throw ParseError("Could not load type: {0}", name);

            TValue result = adapter.CreateTypeObject(ctx, type);
            if (result == null)
                throw NotSupported();

            return LiteralValueReference.CreateTargetObjectLiteral(adapter, ctx, name, result);
        }

        public ValueReference<TType, TValue> VisitTypeReferenceExpression(TypeReferenceExpression typeReferenceExpression)
        {
            var type = typeReferenceExpression.Type.Resolve(adapter, ctx);

            if (type != null)
            {
                adapter.ForceLoadType(ctx, type);

                return new TypeValueReference<TType, TValue>(adapter, ctx, type);
            }

            var name = ResolveTypeName(typeReferenceExpression.Type);

            // Assume it is a namespace.
            return new NamespaceValueReference<TType, TValue>(adapter, ctx, name);
        }

        public ValueReference<TType, TValue> VisitUnaryOperatorExpression(UnaryOperatorExpression unaryOperatorExpression)
        {
            var vref = unaryOperatorExpression.Expression.AcceptVisitor(this);
            IRawValue<TValue> val = vref.ObjectValue;

            if (!val.IsPrimitive())
            {
                throw ParseError("Cannot apply unary operator to non-primitive value");
            }

            ValueType primitive = val.ToPrimitive();
            long num;
            object originChangedValue = null;

            switch (unaryOperatorExpression.Operator)
            {
                case UnaryOperatorType.BitNot:
                    num = ~GetInteger(val);
                    originChangedValue = Convert.ChangeType(num, primitive.GetType());
                    break;
                case UnaryOperatorType.Minus:
                    if (val.Is<decimal>())
                    {
                        originChangedValue = -val.ToPrimitive<decimal>();
                    }
                    else if (val.Is<double>())
                    {
                        originChangedValue = -val.ToPrimitive<double>();
                    }
                    else if (val.Is<float>())
                    {
                        originChangedValue = -val.ToPrimitive<float>();
                    }
                    else
                    {
                        num = -GetInteger(primitive);
                        originChangedValue = Convert.ChangeType(num, primitive.GetType());
                    }

                    break;
                case UnaryOperatorType.Not:
                    if (!val.Is<bool>())
                        throw ParseError("Expected boolean type in Not operator.");

                    originChangedValue = !val.ToPrimitive<bool>();
                    break;
                case UnaryOperatorType.PostDecrement:
                    if (val.Is<decimal>())
                    {
                        originChangedValue = val.ToPrimitive<decimal>() - 1;
                    }
                    else if (val.Is<double>())
                    {
                        originChangedValue = val.ToPrimitive<double>() - 1;
                    }
                    else if (val.Is<float>())
                    {
                        originChangedValue = val.ToPrimitive<float>() - 1;
                    }
                    else
                    {
                        num = GetInteger(val) - 1;
                        originChangedValue = Convert.ChangeType(num, val.GetType());
                    }

                    vref.Value = adapter.CreateValue(ctx, originChangedValue);
                    break;
                case UnaryOperatorType.Decrement:
                    if (val.Is<decimal>())
                    {
                        originChangedValue = val.ToPrimitive<decimal>() - 1;
                    }
                    else if (val.Is<double>())
                    {
                        originChangedValue = val.ToPrimitive<double>() - 1;
                    }
                    else if (val.Is<float>())
                    {
                        originChangedValue = val.ToPrimitive<float>() - 1;
                    }
                    else
                    {
                        num = GetInteger(val) - 1;
                        originChangedValue = Convert.ChangeType(num, val.GetType());
                    }

                    vref.Value = adapter.CreateValue(ctx, val);
                    break;
                case UnaryOperatorType.PostIncrement:
                    if (val.Is<decimal>())
                    {
                        originChangedValue = val.ToPrimitive<decimal>() + 1;
                    }
                    else if (val.Is<double>())
                    {
                        originChangedValue = val.ToPrimitive<double>() + 1;
                    }
                    else if (val.Is<float>())
                    {
                        originChangedValue = val.ToPrimitive<float>() + 1;
                    }
                    else
                    {
                        num = GetInteger(val) + 1;
                        originChangedValue = Convert.ChangeType(num, val.GetType());
                    }

                    vref.Value = adapter.CreateValue(ctx, originChangedValue);
                    break;
                case UnaryOperatorType.Increment:
                    if (val.Is<decimal>())
                    {
                        originChangedValue = val.ToPrimitive<decimal>() + 1;
                    }
                    else if (val.Is<double>())
                    {
                        originChangedValue = val.ToPrimitive<double>() + 1;
                    }
                    else if (val.Is<float>())
                    {
                        originChangedValue = val.ToPrimitive<float>() + 1;
                    }
                    else
                    {
                        num = GetInteger(val) + 1;
                        originChangedValue = Convert.ChangeType(num, val.GetType());
                    }

                    vref.Value = adapter.CreateValue(ctx, val);
                    break;
                case UnaryOperatorType.Plus:
                    break;
                default:
                    throw NotSupported();
            }

            return LiteralValueReference.CreateObjectLiteral(adapter, ctx, expression, originChangedValue);
        }

        public ValueReference<TType, TValue> VisitUncheckedExpression(UncheckedExpression uncheckedExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitEmptyExpression(EmptyExpression emptyExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryExpression(QueryExpression queryExpression)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryContinuationClause(QueryContinuationClause queryContinuationClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryFromClause(QueryFromClause queryFromClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryLetClause(QueryLetClause queryLetClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryWhereClause(QueryWhereClause queryWhereClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryJoinClause(QueryJoinClause queryJoinClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryOrderClause(QueryOrderClause queryOrderClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryOrdering(QueryOrdering queryOrdering)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQuerySelectClause(QuerySelectClause querySelectClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitQueryGroupClause(QueryGroupClause queryGroupClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitAttribute(ICSharpCode.NRefactory.CSharp.Attribute attribute)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitAttributeSection(AttributeSection attributeSection)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitDelegateDeclaration(DelegateDeclaration delegateDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitTypeDeclaration(TypeDeclaration typeDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitUsingAliasDeclaration(UsingAliasDeclaration usingAliasDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitUsingDeclaration(UsingDeclaration usingDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitExternAliasDeclaration(ExternAliasDeclaration externAliasDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitBlockStatement(BlockStatement blockStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitBreakStatement(BreakStatement breakStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitCheckedStatement(CheckedStatement checkedStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitContinueStatement(ContinueStatement continueStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitDoWhileStatement(DoWhileStatement doWhileStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitEmptyStatement(EmptyStatement emptyStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitExpressionStatement(ExpressionStatement expressionStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitFixedStatement(FixedStatement fixedStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitForeachStatement(ForeachStatement foreachStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitForStatement(ForStatement forStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitGotoCaseStatement(GotoCaseStatement gotoCaseStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitGotoDefaultStatement(GotoDefaultStatement gotoDefaultStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitGotoStatement(GotoStatement gotoStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitIfElseStatement(IfElseStatement ifElseStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitLabelStatement(LabelStatement labelStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitLockStatement(LockStatement lockStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitReturnStatement(ReturnStatement returnStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitSwitchStatement(SwitchStatement switchStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitSwitchSection(SwitchSection switchSection)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitCaseLabel(CaseLabel caseLabel)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitThrowStatement(ThrowStatement throwStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitTryCatchStatement(TryCatchStatement tryCatchStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitCatchClause(CatchClause catchClause)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitUncheckedStatement(UncheckedStatement uncheckedStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitUnsafeStatement(UnsafeStatement unsafeStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitUsingStatement(UsingStatement usingStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitVariableDeclarationStatement(VariableDeclarationStatement variableDeclarationStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitWhileStatement(WhileStatement whileStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitYieldBreakStatement(YieldBreakStatement yieldBreakStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitYieldReturnStatement(YieldReturnStatement yieldReturnStatement)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitAccessor(Accessor accessor)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitConstructorDeclaration(ConstructorDeclaration constructorDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitConstructorInitializer(ConstructorInitializer constructorInitializer)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitDestructorDeclaration(DestructorDeclaration destructorDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitEnumMemberDeclaration(EnumMemberDeclaration enumMemberDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitEventDeclaration(EventDeclaration eventDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitCustomEventDeclaration(CustomEventDeclaration customEventDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitFieldDeclaration(FieldDeclaration fieldDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitIndexerDeclaration(IndexerDeclaration indexerDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitMethodDeclaration(MethodDeclaration methodDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitOperatorDeclaration(OperatorDeclaration operatorDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitParameterDeclaration(ParameterDeclaration parameterDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitPropertyDeclaration(PropertyDeclaration propertyDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitVariableInitializer(VariableInitializer variableInitializer)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitFixedFieldDeclaration(FixedFieldDeclaration fixedFieldDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitFixedVariableInitializer(FixedVariableInitializer fixedVariableInitializer)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitSyntaxTree(SyntaxTree syntaxTree)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitSimpleType(SimpleType simpleType)
        {
            return ResolveTypeValueReference(ctx, simpleType);
        }

        public ValueReference<TType, TValue> VisitMemberType(MemberType memberType)
        {
            return ResolveTypeValueReference(ctx, memberType);
        }

        public ValueReference<TType, TValue> VisitComposedType(ComposedType composedType)
        {
            return ResolveTypeValueReference(ctx, composedType);
        }

        public ValueReference<TType, TValue> VisitArraySpecifier(ArraySpecifier arraySpecifier)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitPrimitiveType(PrimitiveType primitiveType)
        {
            return ResolveTypeValueReference(ctx, primitiveType);
        }

        public ValueReference<TType, TValue> VisitComment(Comment comment)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitWhitespace(WhitespaceNode whitespaceNode)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitText(TextNode textNode)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitNewLine(NewLineNode newLineNode)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitPreProcessorDirective(PreProcessorDirective preProcessorDirective)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitDocumentationReference(DocumentationReference documentationReference)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitTypeParameterDeclaration(TypeParameterDeclaration typeParameterDeclaration)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitConstraint(Constraint constraint)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitCSharpTokenNode(CSharpTokenNode cSharpTokenNode)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitIdentifier(Identifier identifier)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitPatternPlaceholder(AstNode placeholder, ICSharpCode.NRefactory.PatternMatching.Pattern pattern)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitNullNode(AstNode nullNode)
        {
            throw NotSupported();
        }

        public ValueReference<TType, TValue> VisitErrorNode(AstNode errorNode)
        {
            throw NotSupported();
        }

        #endregion
    }
}
