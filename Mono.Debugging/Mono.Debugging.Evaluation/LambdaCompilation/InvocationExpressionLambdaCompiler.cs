using System;
using ICSharpCode.NRefactory.CSharp;

namespace Mono.Debugging.Evaluation.LambdaCompilation
{
    public class InvocationExpressionLambdaCompiler<TType, TValue> : LambdaCompiler<TType, TValue>
        where TType : class
        where TValue : class
    {
        readonly InvocationExpression m_InvocationExpression;

        public InvocationExpressionLambdaCompiler(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            NRefactoryExpressionEvaluatorVisitor<TType, TValue> visitor,
            InvocationExpression invocationExpression)
            : base(adaptor, ctx, visitor)
        {
            if (invocationExpression == null)
                throw new ArgumentNullException();
            m_InvocationExpression = invocationExpression;
        }

        public static CompilationResult<TType, TValue> Compile(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            NRefactoryExpressionEvaluatorVisitor<TType, TValue> visitor,
            InvocationExpression invocationExpression)
        {
            return new InvocationExpressionLambdaCompiler<TType, TValue>(adaptor, ctx, visitor, invocationExpression).Compile();
        }
    }
}
