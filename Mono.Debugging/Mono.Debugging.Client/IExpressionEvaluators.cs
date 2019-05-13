using System;
using Mono.Debugging.Evaluation;

namespace Mono.Debugging.Client
{
    public interface IExpressionEvaluators<TType, TValue>
        where TType : class
        where TValue : class
    {
        IExpressionEvaluator<TType, TValue> GetEvaluator(EvaluationContext context);
    }
}
