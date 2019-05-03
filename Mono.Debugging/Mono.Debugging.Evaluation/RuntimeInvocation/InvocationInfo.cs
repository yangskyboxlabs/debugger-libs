using System;
using System.Collections.Generic;

namespace Mono.Debugging.Evaluation.RuntimeInvocation
{
    public class InvocationInfo<TValue> where TValue : class
    {
        public InvocationInfo(
            IResolutionResult resolutionResult,
            TValue @this,
            IEnumerable<TValue> arguments,
            bool paramsInvocation)
        {
            ResolutionResult = resolutionResult;
            Arguments = arguments;
            This = @this;
            ParamsInvocation = paramsInvocation;
        }

        public IResolutionResult ResolutionResult { get; }

        public IEnumerable<TValue> Arguments { get; }

        public TValue This { get; private set; }

        public bool ParamsInvocation { get; set; }
    }

    public interface IResolutionResult
    {
        ResolutionResultKind Kind { get; }

        bool ParamsInvocation { get; }
    }

    public enum ResolutionResultKind
    {
        Success,
        NoApplicableMethods,
        Ambigious,
    }
}
