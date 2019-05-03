// 
// VariableValueReference.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2009 Novell, Inc (http://www.novell.com)
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

namespace Mono.Debugging.Soft
{
    public class VariableValueReference : ValueReference<TypeMirror, Value>
    {
        readonly LocalVariable variable;
        LocalVariableBatch batch;
        Value value;
        string name;

        public VariableValueReference(
            SoftDebuggerAdaptor adapter,
            EvaluationContext ctx,
            string name,
            LocalVariable variable,
            LocalVariableBatch batch)
            : this(adapter, ctx, name, variable)
        {
            this.batch = batch;
        }

        public VariableValueReference(
            SoftDebuggerAdaptor adapter,
            EvaluationContext ctx,
            string name,
            LocalVariable variable,
            Value value)
            : this(adapter, ctx, name, variable)
        {
            this.value = value;
        }

        public VariableValueReference(
            SoftDebuggerAdaptor adapter,
            EvaluationContext ctx,
            string name,
            LocalVariable variable)
            : base(adapter, ctx)
        {
            this.variable = variable;
            this.name = name;
        }

        public override ObjectValueFlags Flags => ObjectValueFlags.Variable;

        public override string Name => name;

        public override TypeMirror Type => variable.Type;

        Value NormalizeValue(EvaluationContext ctx, Value value)
        {
            if (variable.Type.IsPointer)
            {
                long addr = (long)((PrimitiveValue)value).Value;

                return new PointerValue(value.VirtualMachine, variable.Type, addr);
            }

            return Adaptor.IsNull(ctx, value) ? null : value;
        }

        public override Value Value
        {
            get
            {
                var ctx = (SoftEvaluationContext)Context;

                try
                {
                    if (value == null)
                        value = batch != null ? batch.GetValue(variable) : ctx.Frame.GetValue(variable);

                    return NormalizeValue(ctx, value);
                }
                catch (AbsentInformationException ex)
                {
                    throw new EvaluatorException(ex, "Value not available");
                }
                catch (ArgumentException ex)
                {
                    throw new EvaluatorException(ex.Message);
                }
            }
            set
            {
                ((SoftEvaluationContext)Context).Frame.SetValue(variable, value);
                this.value = value;
            }
        }

        internal string[] GetTupleElementNames(SoftEvaluationContext ctx)
        {
            return ctx.Session.GetPdbData(variable.Method)?.GetTupleElementNames(variable.Method, variable.Index);
        }
    }
}
