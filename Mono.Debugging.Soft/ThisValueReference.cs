//
// ThisValueReference.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2014 Xamarin Inc. (www.xamarin.com)
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
using Mono.Debugger.Soft;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation;
using StackFrame = Mono.Debugger.Soft.StackFrame;

namespace Mono.Debugging.Soft
{
    public class ThisValueReference : ValueReference<TypeMirror, Value>
    {
        readonly StackFrame frame;
        TypeMirror type;
        Value value;

        public ThisValueReference(
            SoftDebuggerAdaptor adaptor,
            EvaluationContext ctx,
            StackFrame frame)
            : base(adaptor, ctx)
        {
            this.frame = frame;
        }

        public override ObjectValueFlags Flags => ObjectValueFlags.Field | ObjectValueFlags.ReadOnly;

        public override string Name => "this";

        public override Value Value
        {
            get
            {
                if (value == null)
                    value = frame.GetThis();

                return value;
            }
            set
            {
                if (frame.VirtualMachine.Version.AtLeast(2, 44))
                {
                    this.value = value;
                    frame.SetThis(value);
                }
            }
        }

        public override TypeMirror Type
        {
            get
            {
                if (type == null)
                    type = Adaptor.GetValueType(Context, Value);

                return type;
            }
        }
    }
}
