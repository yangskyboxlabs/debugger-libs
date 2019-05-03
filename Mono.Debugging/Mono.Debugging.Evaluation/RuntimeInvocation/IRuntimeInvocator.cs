using System;

namespace Mono.Debugging.Evaluation.RuntimeInvocation
{
    public interface IRuntimeInvocator<in TType, TValue>
        where TValue : class
        where TType : class
    {
        InvocationResult<TValue> RuntimeInvoke(
            EvaluationContext ctx,
            InvocationInfo<TValue> invocationInfo);

        InvocationResult<TValue> InvokeInstanceMethod(
            EvaluationContext ctx,
            TValue target,
            string methodName,
            params TValue[] argValues);

        InvocationResult<TValue> InvokeInstanceMethod(
            EvaluationContext ctx,
            TValue target,
            string methodName,
            TType[] invocationTypeArgs,
            params TValue[] argValues);

        InvocationResult<TValue> InvokeInstanceMethod(
            EvaluationContext ctx,
            TType targetTypeOverride,
            TValue target,
            string methodName,
            TType[] invocationTypeArgs,
            TType[] argTypesOverride,
            params TValue[] argValues);

        InvocationResult<TValue> InvokeStaticMethod(
            EvaluationContext ctx,
            TType targetType,
            string methodName,
            params TValue[] argValues);

        InvocationResult<TValue> InvokeStaticMethod(
            EvaluationContext ctx,
            TType targetType,
            string methodName,
            TType[] invocationTypeArgs,
            params TValue[] argValues);

        InvocationResult<TValue> InvokeStaticMethod(
            EvaluationContext ctx,
            TType targetType,
            string methodName,
            TType[] invocationTypeArgs,
            TType[] argTypesOverride,
            params TValue[] argValues);
    }
}
