// EvaluationContext.cs
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
using System.Threading;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class EvaluationContext
    {
        public EvaluationOptions Options { get; set; }
        public SourceLocation SourceLocation { get; internal set; }

        public bool CaseSensitive { get; }

        public virtual void WriteDebuggerError(Exception ex) { }

        public virtual void WriteDebuggerOutput(string message, params object[] values) { }

        public void WaitRuntimeInvokes() { }

        public void AssertTargetInvokeAllowed()
        {
            if (!Options.AllowTargetInvoke)
                throw new ImplicitEvaluationDisabledException();
        }

        public void AssertMethodEvaluationAllowed()
        {
            if (!Options.AllowMethodEvaluation)
            {
                throw new ImplicitEvaluationDisabledException();
            }
        }

        public EvaluationContext(EvaluationOptions options)
        {
            Options = options;
        }

        public EvaluationContext Clone()
        {
            var clone = (EvaluationContext)MemberwiseClone();
            clone.CopyFrom(this);
            return clone;
        }

        public EvaluationContext WithOptions(EvaluationOptions options)
        {
            if (options == null || Options == options)
                return this;

            EvaluationContext clone = Clone();
            clone.Options = options;
            return clone;
        }

        public virtual void CopyFrom(EvaluationContext ctx)
        {
            Options = ctx.Options.Clone();
        }

        public virtual bool SupportIEnumerable => false;

        internal CancellationToken CancellationToken { get; set; }
    }

    class ExpressionValueSource<TType, TValue> : RemoteFrameObject, IObjectValueSource
        where TType : class
        where TValue : class
    {
        readonly EvaluationContext ctx;
        readonly ObjectValueAdaptor<TType, TValue> adapter;

        public ExpressionValueSource(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext ctx)
        {
            this.adapter = adapter;
            this.ctx = ctx;
            Connect();
        }

        public ObjectValue[] GetChildren(ObjectPath path, int index, int count, EvaluationOptions options)
        {
            throw new NotImplementedException();
        }

        public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
        {
            throw new NotImplementedException();
        }

        public ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
        {
            EvaluationContext c = ctx.WithOptions(options);
            var vals = adapter.GetExpressionValuesAsync(c, new string[] { path.LastName });
            return vals[0];
        }

        public IRawValue GetRawValue(ObjectPath path, EvaluationOptions options)
        {
            throw new NotImplementedException();
        }

        public void SetRawValue(ObjectPath path, IRawValue value, EvaluationOptions options)
        {
            throw new NotImplementedException();
        }
    }

    class ExpressionValueSource
    {
        public static ExpressionValueSource<TType, TValue> Create<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext context)
            where TType : class
            where TValue : class
        {
            return new ExpressionValueSource<TType, TValue>(adapter, context);
        }
    }
}
