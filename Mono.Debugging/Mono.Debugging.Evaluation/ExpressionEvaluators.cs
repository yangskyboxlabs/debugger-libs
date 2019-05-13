using System;
using System.Collections.Generic;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class ExpressionEvaluators<TType, TValue> : IExpressionEvaluators<TType, TValue>
        where TType : class
        where TValue : class
    {
        
        protected IEnumerable<IExpressionEvaluator<TType, TValue>> AllEvaluators { get; }
        
        public IExpressionEvaluator<TType, TValue> GetEvaluator(EvaluationContext context)
        {
            foreach (IExpressionEvaluator<TType, TValue> allEvaluator in this.AllEvaluators)
            {
                if (allEvaluator.IsApplicable(context))
                {
//                    if (this.myLogger.IsTraceEnabled())
//                        this.myLogger.Trace(string.Format("Evaluator {0} is selected", (object) allEvaluator));
                    return allEvaluator;
                }
            }
            throw new InvalidOperationException("Can't find applicable expression evaluator");
        }
    }
}