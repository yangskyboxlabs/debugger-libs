using System;
using System.Reflection;
using Mono.Debugging.Evaluation.Extension;

namespace Mono.Debugging.Evaluation.RuntimeInvocation
{
    public abstract class RuntimeInvocator<TType, TValue> : IRuntimeInvocator<TType, TValue>
        where TValue : class
        where TType : class
    {
        protected ObjectValueAdaptor<TType, TValue> Adapter { get; }

        protected RuntimeInvocator(ObjectValueAdaptor<TType, TValue> adapter)
        {
            this.Adapter = adapter;
        }

        protected TType[] GetValuesTypes(EvaluationContext context, params TValue[] values)
        {
            TType[] typeArray = new TType[values.Length];
            for (int index = 0; index < values.Length; ++index)
                typeArray[index] = Adapter.GetValueType(context, values[index]);
            return typeArray;
        }

        public abstract InvocationResult<TValue> RuntimeInvoke(EvaluationContext ctx, InvocationInfo<TValue> invocationInfo);

        public InvocationResult<TValue> InvokeInstanceMethod(
            EvaluationContext ctx,
            TValue target,
            string methodName,
            params TValue[] argValues)
        {
            return InvokeInstanceMethod(ctx, target, methodName, Adapter.EmptyTypeArray, argValues);
        }

        public InvocationResult<TValue> InvokeInstanceMethod(
            EvaluationContext ctx,
            TValue target,
            string methodName,
            TType[] invocationTypeArgs,
            params TValue[] argValues)
        {
            TType[] valuesTypes = GetValuesTypes(ctx, argValues);
            TType valueType = Adapter.GetValueType(ctx, target);
            return InvokeInstanceMethod(ctx, valueType, target, methodName, invocationTypeArgs, valuesTypes, argValues);
        }

        public InvocationResult<TValue> InvokeInstanceMethod(
            EvaluationContext ctx,
            TType targetTypeOverride,
            TValue target,
            string methodName,
            TType[] invocationTypeArgs,
            TType[] argTypesOverride,
            params TValue[] argValues)
        {
            InvocationInfo<TValue> instanceCallInfo = Adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, targetTypeOverride, invocationTypeArgs, argTypesOverride, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).ThrowIfFailed().ToInstanceCallInfo(target, argValues);
            return RuntimeInvoke(ctx, instanceCallInfo);
        }

        public InvocationResult<TValue> InvokeStaticMethod(
            EvaluationContext ctx,
            TType targetType,
            string methodName,
            params TValue[] argValues)
        {
            return InvokeStaticMethod(ctx, targetType, methodName, this.Adapter.EmptyTypeArray, argValues);
        }

        public InvocationResult<TValue> InvokeStaticMethod(
            EvaluationContext ctx,
            TType targetType,
            string methodName,
            TType[] invocationTypeArgs,
            params TValue[] argValues)
        {
            TType[] valuesTypes = GetValuesTypes(ctx, argValues);
            return InvokeStaticMethod(ctx, targetType, methodName, invocationTypeArgs, valuesTypes, argValues);
        }

        public InvocationResult<TValue> InvokeStaticMethod(
            EvaluationContext ctx,
            TType targetType,
            string methodName,
            TType[] invocationTypeArgs,
            TType[] argTypesOverride,
            params TValue[] argValues)
        {
            InvocationInfo<TValue> staticCallInfo = Adapter.MethodResolver.ResolveOwnMethod(ctx, methodName, targetType, invocationTypeArgs, argTypesOverride, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).ThrowIfFailed<IResolutionResult>("").ToStaticCallInfo<TValue>(argValues);
            return RuntimeInvoke(ctx, staticCallInfo);
        }
    }
}
