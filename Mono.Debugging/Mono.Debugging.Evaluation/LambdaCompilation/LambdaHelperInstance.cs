using System;
using System.Linq;

namespace Mono.Debugging.Evaluation.LambdaCompilation
{
    public class LambdaHelperInstance<TType, TValue>
        where TType : class
        where TValue : class
    {
        readonly ObjectValueAdaptor<TType, TValue> adapter;
        readonly EvaluationContext ctx;
        readonly TValue helperInstance;

        public void LoadAssemblyAndPrepareLambdas(byte[] assembly)
        {
            adapter.Invocator.InvokeInstanceMethod(ctx, helperInstance, nameof(LoadAssemblyAndPrepareLambdas), adapter.CreateByteArray(ctx, assembly));
        }

        public void SetValues(string[] names, TValue[] values)
        {
            if (names.Length == 0)
                return;
            adapter.Invocator.InvokeInstanceMethod(
                ctx,
                helperInstance,
                nameof(SetValues),
                adapter.CreateArray(ctx, adapter.GetType(ctx, "System.String"), names.Select(x => adapter.CreateValue(ctx, x)).ToArray()),
                adapter.CreateArray(ctx, adapter.GetType(ctx, "System.Object"), values));
        }
    }
}
