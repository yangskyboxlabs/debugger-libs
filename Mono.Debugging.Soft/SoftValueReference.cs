using System;
using Mono.Debugger.Soft;
using Mono.Debugger.Soft.RuntimeInvocation;
using Mono.Debugging.Evaluation;

namespace Mono.Debugging.Soft
{
    public abstract class SoftValueReference : ValueReference<TypeMirror, Value>
    {
        public SoftRuntimeInvocator Invocator { get; }

        public SoftValueReference(SoftDebuggerAdaptor adaptor, EvaluationContext ctx)
            : base(adaptor, ctx)
        {
            Invocator = adaptor.Invocator;
        }

        protected void EnsureContextHasDomain(AppDomainMirror domain)
        {
            var softEvaluationContext = (SoftEvaluationContext)Context;
            if (softEvaluationContext.Domain == domain)
                return;

            var clone = (SoftEvaluationContext)softEvaluationContext.Clone();
            clone.Domain = domain;
            Context = clone;
        }
    }
}
