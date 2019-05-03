using System;
using System.Collections.Generic;
using Mono.Debugging.Evaluation.RuntimeInvocation;

namespace Mono.Debugging.Evaluation.OverloadResolution
{
    public class ResolutionResult<TType, TMethod> : IResolutionResult
    {
        public ResolutionResultKind Kind { get; private set; }

        public ResolutionMethodInfo<TType, TMethod> SelectedCandidate { get; private set; }

        public TType[] MethodTypeArguments { get; private set; }

        public bool ParamsInvocation { get; private set; }

        public IList<ResolutionMethodInfo<TType, TMethod>> AmbigiousCandidates { get; private set; }

        private ResolutionResult() { }

        public static ResolutionResult<TType, TMethod> Success(
            ResolutionMethodInfo<TType, TMethod> selectedCandidate,
            TType[] typeArguments,
            bool paramsInvocation)
        {
            return new ResolutionResult<TType, TMethod>()
            {
                Kind = ResolutionResultKind.Success,
                SelectedCandidate = selectedCandidate,
                MethodTypeArguments = typeArguments,
                ParamsInvocation = paramsInvocation
            };
        }

        public static ResolutionResult<TType, TMethod> NoApplicableCandidates()
        {
            return new ResolutionResult<TType, TMethod>()
            {
                Kind = ResolutionResultKind.NoApplicableMethods
            };
        }

        public static ResolutionResult<TType, TMethod> Ambigious(
            IList<ResolutionMethodInfo<TType, TMethod>> ambigiousCandidates)
        {
            return new ResolutionResult<TType, TMethod>()
            {
                Kind = ResolutionResultKind.Ambigious,
                AmbigiousCandidates = ambigiousCandidates
            };
        }
    }
}
