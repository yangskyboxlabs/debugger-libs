using System;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public static class EvaluationContextExtension
    {
        public static EvaluationContext WithModifiedOptions(
            this EvaluationContext context,
            Action<EvaluationOptions> optionsModificator)
        {
            if (context.Options == null)
                throw new InvalidOperationException("context.Options is null");
            return context.WithOptions(context.Options.CloneWith(optionsModificator));
        }

        public static EvaluationContext WithAllowedInvokes(this EvaluationContext context)
        {
            return context.WithModifiedOptions(options => options.AllowTargetInvoke = true);
        }

        public static EvaluationContext WithSourceLocation(
            this EvaluationContext context,
            SourceLocation sourceLocation)
        {
            context.SourceLocation = sourceLocation;
            return context;
        }
    }
}
