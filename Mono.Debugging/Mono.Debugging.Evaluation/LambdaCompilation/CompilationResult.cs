using System;
using System.Collections.Generic;
using ICSharpCode.NRefactory.CSharp;

namespace Mono.Debugging.Evaluation.LambdaCompilation
{
    public class CompilationResult<TType, TValue>
        where TType : class
        where TValue : class
    {
        public bool HasErrors { get; }
        public readonly string[] Errors;
        public readonly Dictionary<LambdaExpression, ValueReference<TType, TValue>> CalculatedLambdas;

        public CompilationResult(string[] errors)
        {
            Errors = errors;
            HasErrors = true;
        }

        public CompilationResult(Dictionary<LambdaExpression, ValueReference<TType, TValue>> calculatedLambdas)
        {
            HasErrors = false;
            CalculatedLambdas = calculatedLambdas;
        }
    }
}
