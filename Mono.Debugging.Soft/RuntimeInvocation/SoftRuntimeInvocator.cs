using System;
using System.Linq;
using System.Threading;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation;
using Mono.Debugging.Evaluation.Extension;
using Mono.Debugging.Evaluation.OverloadResolution;
using Mono.Debugging.Evaluation.RuntimeInvocation;
using Mono.Debugging.Soft;

namespace Mono.Debugger.Soft.RuntimeInvocation
{
    public class SoftRuntimeInvocator : RuntimeInvocator<TypeMirror, Value>
    {
        public SoftRuntimeInvocator(
            ObjectValueAdaptor<TypeMirror, Value> adapter)
            : base(adapter) { }

        public override InvocationResult<Value> RuntimeInvoke(EvaluationContext ctx, InvocationInfo<Value> invocationInfo)
        {
            var resolutionResult = (ResolutionResult<TypeMirror, MethodMirror>)invocationInfo.ResolutionResult;
            if (!resolutionResult.IsSuccess())
                throw new InvalidOperationException("Invalid resolution result");

            MethodMirror method = resolutionResult.MakeGenericMethodIfNeeded();
            return RuntimeInvoke(ctx, method, resolutionResult.SelectedCandidate.OwnerType, invocationInfo.This, invocationInfo.Arguments.ToArray());
        }

        public InvocationResult<Value> RuntimeInvoke(
            SoftEvaluationContext context,
            MethodMirror method,
            TypeMirror targetType,
            Value targetObject,
            Value[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (method.VirtualMachine.Version.AtLeast(2, 45))
            {
                AppDomainMirror domain = method.DeclaringType.Assembly.Domain;
                targetObject = NormalizeAppDomain(targetObject, domain);
                for (int index = 0; index < values.Length; ++index)
                    values[index] = NormalizeAppDomain(values[index], domain);
            }

            // Some arguments may need to be boxed
            var mparams = method.GetParameters();
            if (mparams.Length != values.Length)
                throw new EvaluatorException("Invalid number of arguments when calling: " + method.Name);

            for (int index = 0; index < mparams.Length; ++index)
                values[index] = ConvertValueIfNeeded(context, values[index], mparams[index].ParameterType);

            if (!method.IsStatic && method.DeclaringType.IsClass && !IsValueTypeOrPrimitive(method.DeclaringType))
            {
                TypeMirror type = Adapter.GetValueType(context, targetObject);

                if (targetObject is StructMirror structMirror && structMirror.Type != method.DeclaringType
                    || IsValueTypeOrPrimitive(type) || IsValueTypeOrPrimitive(targetType))
                {
                    // A value type being assigned to a parameter which is not a value type. The value has to be boxed.
                    try
                    {
                        targetObject = context.Thread.Domain.CreateBoxedValue(targetObject);
                    }
                    catch (NotSupportedException)
                    {
                        // This runtime doesn't support creating boxed values
                        throw new EvaluatorException("This runtime does not support creating boxed values.");
                    }
                }
            }

//
            try
            {
                return new InvocationResult<Value>(method.Evaluate(targetObject, values));
            }
            catch (NotSupportedException)
            {
                context.AssertTargetInvokeAllowed();
                var threadState = context.Thread.ThreadState;
                if ((threadState & ThreadState.WaitSleepJoin) == ThreadState.WaitSleepJoin)
                {
                    DebuggerLoggingService.LogMessage("Thread state before evaluation is {0}", threadState);
                    throw new EvaluatorException("Evaluation is not allowed when the thread is in 'Wait' state");
                }

                var mc = new SoftMethodCall(this, method, targetObject, values, true);

                //Since runtime is returning NOT_SUSPENDED error if two methods invokes are executed
                //at same time we have to lock invoking to prevent this...
                lock (method.VirtualMachine)
                {
                    Adapter.AsyncExecute(mc, context.Options.EvaluationTimeout);

                    return mc.ReturnValue;
                }
            }
        }

        static bool IsValueTypeOrPrimitive(TypeMirror type)
        {
            if (type == null)
                return false;
            if (!type.IsValueType)
                return type.IsPrimitive;
            return true;
        }

        static Value NormalizeAppDomain(Value value, AppDomainMirror targetDomain)
        {
            if (!(value is PrimitiveValue))
                return value;
            PrimitiveValue primitiveValue = (PrimitiveValue)value;
            if (value.Type.Assembly.Domain != targetDomain)
                return new PrimitiveValue(primitiveValue.Value, targetDomain);
            return value;
        }

        public InvocationResult<Value> RuntimeInvoke(
            EvaluationContext context,
            MethodMirror method,
            TypeMirror targetType,
            Value targetObject,
            Value[] values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            if (method.VirtualMachine.Version.AtLeast(2, 45))
            {
                AppDomainMirror domain = method.DeclaringType.Assembly.Domain;
                targetObject = SoftRuntimeInvocator.NormalizeAppDomain(targetObject, domain);
                for (int index = 0; index < values.Length; ++index)
                    values[index] = SoftRuntimeInvocator.NormalizeAppDomain(values[index], domain);
            }

            ParameterInfoMirror[] parameters = method.GetParameters();
            if (parameters.Length != values.Length)
                throw new EvaluatorException("Invalid number of arguments when calling: " + method.Name, Array.Empty<object>());
            for (int index = 0; index < parameters.Length; ++index)
                values[index] = this.ConvertValueIfNeeded(context, values[index], parameters[index].ParameterType);
            if (!method.IsStatic && method.DeclaringType.IsClass && !SoftRuntimeInvocator.IsValueTypeOrPrimitive(method.DeclaringType))
            {
                TypeMirror valueType = this.Adapter.GetValueType(context, targetObject);
                if (targetObject is StructMirror && targetObject.Type != method.DeclaringType || SoftRuntimeInvocator.IsValueTypeOrPrimitive(valueType))
                {
                    try
                    {
                        targetObject = (Value)context.Thread.Domain.CreateBoxedValue(targetObject);
                    }
                    catch (NotSupportedException ex)
                    {
                        throw new EvaluatorException("This runtime does not support creating boxed values.", Array.Empty<object>());
                    }
                }
            }

            try
            {
                return new InvocationResult<Value>(method.Evaluate(targetObject, values));
            }
            catch (NotSupportedException ex)
            {
                context.AssertTargetInvokeAllowed();
                ThreadState threadState = context.Thread.ThreadState;
                if ((threadState & ThreadState.WaitSleepJoin) == ThreadState.WaitSleepJoin)
                {
                    this.Logger.Trace(string.Format("Thread state before evaluation is {0}", (object)threadState));
                    throw new EvaluatorException("Evaluation is not allowed when the thread is in 'Wait' state", Array.Empty<object>());
                }

                IInvocableMethodOwnerMirror methodOwnerMirror = targetObject as IInvocableMethodOwnerMirror;
                if (methodOwnerMirror == null && targetType == null)
                    throw new ArgumentException("Either targetObject or targetType have to be provided but targetType is null and targetObject isn't IInvocableMethodOwnerMirror or null");
                SoftMethodCall softMethodCall = new SoftMethodCall(this.Logger.GetSublogger("MethodCall"), context, method, methodOwnerMirror ?? (IInvocableMethodOwnerMirror)targetType, values, true);
                lock (method.VirtualMachine)
                {
                    SoftOperationResult softOperationResult = (SoftOperationResult)this.Adapter.InvokeSync((AsyncOperationBase<Value>)softMethodCall, context.Options.EvaluationTimeout).ThrowIfException<Value, SoftEvaluationContext, TypeMirror>((IObjectValueAdaptor<SoftEvaluationContext, TypeMirror, Value>)this.Adapter, context);
                    return new InvocationResult<Value>(softOperationResult.Result, softOperationResult.OutArgs);
                }
            }
        }

        Value ConvertValueIfNeeded(
            SoftEvaluationContext context,
            Value value,
            TypeMirror parameterType)
        {
            TypeMirror valueType = Adapter.GetValueType(context, value);
            if (parameterType.IsValueType || parameterType.IsPrimitive)
            {
                StructMirror structMirror = value as StructMirror;
                if (structMirror != null && valueType.IsPrimitive)
                    return structMirror.Fields.First();
                return value;
            }

            if (!valueType.IsValueType && (!valueType.IsPrimitive || !(value is PrimitiveValue)))
                return value;
            try
            {
                return context.Thread.Domain.CreateBoxedValue(value);
            }
            catch (NotSupportedException ex)
            {
                throw new EvaluatorException("This runtime does not support creating boxed values.", Array.Empty<object>());
            }
        }
    }
}
