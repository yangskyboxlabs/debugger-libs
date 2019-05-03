// LiteralValueReference.cs
//
// Author:
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
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
//
//

using System;
using Mono.Debugging.Backend;
using DC = Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class LiteralValueReference<TType, TValue> : ValueReference<TType, TValue>
        where TType : class
        where TValue : class
    {
        readonly bool isVoidReturn;
        readonly bool objLiteral;
        bool objCreated;
        readonly object objValue;
        TValue value;
        TType type;
        readonly string name;

        internal LiteralValueReference(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext ctx,
            string name,
            TValue value,
            TType type,
            object objValue = null,
            bool objCreated = false,
            bool isVoidReturn = false,
            bool objLiteral = false)
            : base(adapter, ctx)
        {
            this.name = name;
            this.value = value;
            this.type = type;
            this.objValue = objValue;
            this.objCreated = objCreated;
            this.isVoidReturn = isVoidReturn;
            this.objLiteral = objLiteral;
        }

        public LiteralValueReference(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext context,
            string name,
            object objValue,
            bool objLiteral)
            : base(adapter, context)
        {
            this.objLiteral = objLiteral;
            this.objValue = objValue;
            this.name = name;
        }

        void EnsureValueAndType()
        {
            if (!objCreated && objLiteral)
            {
                value = Adaptor.CreateValue(Context, objValue);
                type = Adaptor.GetValueType(Context, value);
                objCreated = true;
            }
        }

//        public override TValue ObjectValue
//        {
//            get { return objLiteral ? objValue : base.ObjectValue; }
//        }

        public override TValue Value
        {
            get
            {
                EnsureValueAndType();
                return value;
            }
            set { throw new NotSupportedException(); }
        }

        public override string Name
        {
            get { return name; }
        }

        public override TType Type
        {
            get
            {
                EnsureValueAndType();
                return type;
            }
        }

        public override DC.ObjectValueFlags Flags
        {
            get { return DC.ObjectValueFlags.Field | DC.ObjectValueFlags.ReadOnly; }
        }

        protected override DC.ObjectValue OnCreateObjectValue(DC.EvaluationOptions options)
        {
            if (ObjectValue is EvaluationResult)
            {
                EvaluationResult exp = (EvaluationResult)ObjectValue;
                return DC.ObjectValue.CreateObject(this, new DC.ObjectPath(Name), "", exp, Flags, null);
            }

            return base.OnCreateObjectValue(options);
        }

        public override ValueReference<TType, TValue> GetChild(
            string name,
            DC.EvaluationOptions options)
        {
            TValue obj = Value;

            if (obj == null)
                return null;

            if (name[0] == '[' && Adaptor.IsArray(Context, obj))
            {
                // Parse the array indices
                var tokens = name.Substring(1, name.Length - 2).Split(',');
                var indices = new int [tokens.Length];

                for (int n = 0; n < tokens.Length; n++)
                    indices[n] = int.Parse(tokens[n]);

                return new ArrayValueReference<TType, TValue>(Adaptor, Context, obj, indices);
            }

            if (Adaptor.IsClassInstance(Context, obj))
            {
                // Note: This is the only difference with the default ValueReference implementation.
                // We need this because the user may be requesting a base class's implementation, in
                // which case 'Type' will be the BaseType instead of the actual type of the variable.
                return Adaptor.GetMember(GetChildrenContext(options), this, Type, obj, name);
            }

            return null;
        }

        public override DC.ObjectValue[] GetChildren(DC.ObjectPath path, int index, int count, DC.EvaluationOptions options)
        {
            if (isVoidReturn)
                return new DC.ObjectValue[0];

            return base.GetChildren(path, index, count, options);
        }
    }

    public static class LiteralValueReference
    {
        public static LiteralValueReference<TType, TValue> CreateTargetBaseObjectLiteral<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            string name,
            TValue value)
            where TType : class
            where TValue : class
        {
            TType enclosingType = adaptor.GetEnclosingType(ctx);
            TType baseType = adaptor.GetBaseType(ctx, enclosingType);
            return new LiteralValueReference<TType, TValue>(adaptor, ctx, name, value, baseType, null, true);
        }

        public static LiteralValueReference<TType, TValue> CreateTargetObjectLiteral<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            string name,
            TValue value,
            TType type = null)
            where TType : class
            where TValue : class
        {
            ValueType local = true;
            type = type ?? adaptor.GetValueType(ctx, value);
            return new LiteralValueReference<TType, TValue>(adaptor, ctx, name, value, type, local, true);
        }

        public static LiteralValueReference<TType, TValue> CreateObjectLiteral<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            string name,
            TValue value)
            where TType : class
            where TValue : class
        {
            return new LiteralValueReference<TType, TValue>(adaptor, ctx, name, value, objLiteral: true);
        }

        public static LiteralValueReference<TType, TValue> CreateObjectLiteral<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext ctx,
            string name,
            object value)
            where TType : class
            where TValue : class
        {
            return new LiteralValueReference<TType, TValue>(adapter, ctx, name, value, true);
        }

        public static LiteralValueReference<TType, TValue> CreateVoidReturnLiteral<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            string name)
            where TType : class
            where TValue : class
        {
            return new LiteralValueReference<TType, TValue>(
                adaptor,
                ctx,
                name,
                default,
                default,
                "No return value.",
                objCreated: true,
                isVoidReturn: true,
                objLiteral: true);
        }
    }
}
