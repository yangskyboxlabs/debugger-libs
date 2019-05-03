using System;
using System.Collections.Generic;
using Mono.Debugging.Evaluation.RuntimeInvocation;

namespace Mono.Debugging.Evaluation.Extension
{
    public static class ResolutionResultExtensions
    {
        public static bool IsSuccess(this IResolutionResult resolutionResult)
        {
            return resolutionResult.Kind == ResolutionResultKind.Success;
        }

        public static bool IsAmbigious(this IResolutionResult resolutionResult)
        {
            return resolutionResult.Kind == ResolutionResultKind.Ambigious;
        }

        public static InvocationInfo<TValue> ToStaticCallInfo<TValue>(
            this IResolutionResult resolutionResult,
            params TValue[] arguments)
            where TValue : class
        {
            return new InvocationInfo<TValue>(resolutionResult, default, arguments, resolutionResult.ParamsInvocation);
        }

        public static InvocationInfo<TValue> ToInstanceCallInfo<TValue>(
            this IResolutionResult resolutionResult,
            TValue thisObj,
            params TValue[] arguments)
            where TValue : class
        {
            return new InvocationInfo<TValue>(resolutionResult, thisObj, arguments, resolutionResult.ParamsInvocation);
        }

        public static InvocationInfo<TValue> ToExtensionCallInfo<TValue>(
            this IResolutionResult resolutionResult,
            TValue thisObj,
            params TValue[] arguments)
            where TValue : class
        {
            var objList = new List<TValue> { thisObj };
            objList.AddRange(arguments);
            return new InvocationInfo<TValue>(resolutionResult, default, objList, resolutionResult.ParamsInvocation);
        }

        public static TResult ThrowIfFailed<TResult>(
            this TResult resolutionResult,
            string prefixMessage = "")
            where TResult : IResolutionResult
        {
            switch (resolutionResult.Kind)
            {
                case ResolutionResultKind.Success:
                    return resolutionResult;
                case ResolutionResultKind.NoApplicableMethods:
                    throw new EvaluatorException(prefixMessage + "No applicable methods found", Array.Empty<object>());
                case ResolutionResultKind.Ambigious:
                    throw new EvaluatorException(prefixMessage + "Ambigious invocation", Array.Empty<object>());
                default:
                    throw new InvalidOperationException($"Unsupported resolution result {resolutionResult.Kind}");
            }
        }
    }
}
