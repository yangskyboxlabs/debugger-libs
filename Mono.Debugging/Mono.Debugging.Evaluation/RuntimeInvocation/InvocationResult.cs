using System;

namespace Mono.Debugging.Evaluation.RuntimeInvocation
{
    public class InvocationResult<TValue>
    {
        public InvocationResult(TValue result)
            : this(result, new TValue[0]) { }

        public InvocationResult(TValue result, TValue[] outArgs)
        {
            Result = result;
            OutArgs = outArgs;
        }

        public TValue Result { get; }

        public TValue[] OutArgs { get; }
    }
}
