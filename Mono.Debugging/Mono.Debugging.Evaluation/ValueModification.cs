using System;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class ValueModificationException : Exception
    {
        public ValueModificationException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    public static class ValueModification
    {
        internal static TValue ConvertRightHandValue<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext context,
            TValue value,
            TType expectedType)
            where TType : class
            where TValue : class
        {
            try
            {
                return adapter.Convert(context, value, expectedType);
            }
            catch (Exception ex)
            {
                throw new ValueModificationException($"Conversion error: {ex.Message}", ex);
            }
        }

        internal static ValueReference<TType, TValue> EvaluateRightHandValue<TType, TValue>(
            IExpressionEvaluator<TType, TValue> evaluator,
            EvaluationContext context,
            string value,
            TType expectedType)
            where TType : class
            where TValue : class
        {
            context = context.WithModifiedOptions(options =>
            {
                options.AllowMethodEvaluation = true;
                options.AllowTargetInvoke = true;
            });
            try
            {
                return evaluator.Evaluate(context, value, expectedType);
            }
            catch (Exception ex)
            {
                throw new ValueModificationException($"Cannot evaluate '{value}': {ex.Message}", ex);
            }
        }

        internal static void ModifyValueFromRaw<TType, TValue>(
            IRawValue rawValue,
            Action<TValue> valueSetter)
            where TType : class
            where TValue : class
        {
            ModifyValueFromRaw((IRawValue<TValue>)rawValue, valueSetter);
        }

        internal static void ModifyValueFromRaw<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext context,
            IRawValue rawValue,
            Action<TValue> valueSetter)
            where TType : class
            where TValue : class
        {
            ModifyValueFromRaw(adapter, context, (IRawValue<TValue>)rawValue, valueSetter);
        }

        static void ModifyValueFromRaw<TValue>(
            IRawValue<TValue> rawValue,
            Action<TValue> valueSetter) where TValue : class
        {
            ModifyValue(rawValue.TargetObject, valueSetter);
        }

        internal static EvaluationResult ModifyValue<TContext, TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adapter,
            IExpressionEvaluator<TType, TValue> evaluator,
            TContext context,
            string value,
            TType expectedType,
            Action<TValue> valueSetter)
            where TContext : EvaluationContext
            where TType : class
            where TValue : class
        {
            ValueReference<TType, TValue> rightHandValue = EvaluateRightHandValue(evaluator, context, value, expectedType);
            TValue rightHandObjectValue;
            try
            {
                rightHandObjectValue = rightHandValue.Value;
            }
            catch (Exception ex)
            {
                throw new ValueModificationException($"Cannot get real object of {value}", ex);
            }

            TValue convertRightHandValue = ConvertRightHandValue(adapter, context, rightHandObjectValue, expectedType);
            ModifyValue(convertRightHandValue, valueSetter);
            return adapter.ValuePresenter.TargetValueToPresentation(context, convertRightHandValue);
        }

        internal static void ModifyValue<TValue>(TValue value, Action<TValue> valueSetter)
        {
            try
            {
                valueSetter(value);
            }
            catch (Exception ex)
            {
                throw new ValueModificationException($"Error while assigning new value to object: {ex.Message}", ex);
            }
        }
    }
}
