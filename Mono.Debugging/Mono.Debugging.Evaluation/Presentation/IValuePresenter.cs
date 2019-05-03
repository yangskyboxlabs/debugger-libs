using System;
using Mono.Debugging.Backend;

namespace Mono.Debugging.Evaluation.Presentation
{
    public interface IValuePresenter<in TType, in TValue>
    {
        EvaluationResult TargetValueToPresentation(EvaluationContext ctx, TValue obj);

        string TargetValueToString(EvaluationContext ctx, TValue obj);
    }
}
