using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.NRefactory.CSharp;

namespace Mono.Debugging.Evaluation.LambdaCompilation
{
    internal static class ExpressionHelper
    {
        public static LambdaExpression AsLambda(this Expression verifiableExpression)
        {
            LambdaExpression lambdaExpression;
            while ((lambdaExpression = verifiableExpression as LambdaExpression) == null)
            {
                if (!(verifiableExpression is ParenthesizedExpression parenthesizedExpression))
                {
                    return null;
                }

                verifiableExpression = parenthesizedExpression.Expression;
            }

            return lambdaExpression;
        }

        public static bool ContainsLambda(this IEnumerable<Expression> expressions)
        {
            return expressions.Any(IsLambda);
        }

        static bool IsLambda(this Expression verifiableExpression)
        {
            return verifiableExpression.AsLambda() != null;
        }

        public static ValueReference<TType, TValue> AcceptVisitorIfNeeded<TType, TValue>(
            this Expression expression,
            NRefactoryExpressionEvaluatorVisitor<TType, TValue> visitor)
            where TType : class
            where TValue : class
        {
            if (visitor.TryGetValueFromCache(expression, out var valueReference1))
                return valueReference1;
            ValueReference<TType, TValue> valueReference2 = expression.AcceptVisitor(visitor);
            visitor.AddOrUpdateValueToCache(expression, valueReference2);
            return valueReference2;
        }
    }
}
