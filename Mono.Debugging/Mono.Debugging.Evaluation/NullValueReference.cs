// NullValueReference.cs
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
using MD = Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class NullValueReference<TType, TValue> : ValueReference<TType, TValue>
        where TType : class
        where TValue : class
    {
        readonly TType type;
        TValue obj;
        bool valueCreated;

        public NullValueReference(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            TType type)
            : base(adaptor, ctx)
        {
            this.type = type;
        }

        public override TValue Value
        {
            get
            {
                if (!valueCreated)
                {
                    valueCreated = true;
                    obj = Adaptor.CreateNullValue(Context, type);
                }

                return obj;
            }
            set => throw new NotSupportedException();
        }

        public override TType Type => type;

        public override string Name => "null";

        public override MD.ObjectValueFlags Flags => MD.ObjectValueFlags.Literal;

        protected override MD.ObjectValue OnCreateObjectValue(MD.EvaluationOptions options)
        {
            string tn = Adaptor.GetTypeName(GetContext(options), Type);
            return MD.ObjectValue.CreateObject(null, new MD.ObjectPath(Name), tn, "null", Flags, null);
        }

        public override ValueReference<TType, TValue> GetChild(string name, MD.EvaluationOptions options)
        {
            return null;
        }

        public override MD.ObjectValue[] GetChildren(MD.ObjectPath path, int index, int count, MD.EvaluationOptions options)
        {
            return new MD.ObjectValue [0];
        }
    }
}
