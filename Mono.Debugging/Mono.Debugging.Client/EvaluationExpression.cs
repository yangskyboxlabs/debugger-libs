using System;

namespace Mono.Debugging.Client
{
    public class EvaluationExpression
    {
        public string Expression { get; }

        public string[] ImportsList { get; }

        public EvaluationExpression(string expression, string[] importsList)
        {
            Expression = expression;
            ImportsList = importsList;
        }
    }
}
