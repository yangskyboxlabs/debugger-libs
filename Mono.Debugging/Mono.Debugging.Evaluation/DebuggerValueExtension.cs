using System;
using Mono.Debugging.Backend;

namespace Mono.Debugging.Evaluation
{
    public static class DebuggerValueExtension
    {
        public static IRawValue<TValue> ToRawValue<TType, TValue>(
            this TValue debuggerValue,
            ObjectValueAdaptor<TType, TValue> adapter,
            EvaluationContext ctx)
            where TType : class
            where TValue : class
        {
            return adapter.ToRawValue(ctx, null, debuggerValue);
        }
    }
}
