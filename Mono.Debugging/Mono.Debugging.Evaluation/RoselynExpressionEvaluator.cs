using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class RoselynExpressionEvaluator : ExpressionEvaluator
    {
        public Dictionary<string, ValueReference> UserVariables { get; } = new Dictionary<string, ValueReference>();

        private RoselynExpressionEvaluatorVisitor Visitor;
        private TypeResolverHandler TypeResolver;

        public override ValueReference Evaluate(EvaluationContext context, string expression, object expectedType)
        {
            System.Console.WriteLine($"!! Evaluate ({expectedType?.ToString() ?? ""}): {expression} ");

            var parseOptions = new CSharpParseOptions(kind: Microsoft.CodeAnalysis.SourceCodeKind.Script);

            var ast = CSharpSyntaxTree.ParseText(expression, options: parseOptions);

            var mscorlib = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var compilation = CSharpCompilation.CreateScriptCompilation("eval", ast,
                references: new[] {
                    mscorlib,
                });
            var semanticModel = compilation.GetSemanticModel(ast);

            if (!ast.HasCompilationUnitRoot) {
                throw new EvaluatorException("Couldn't parse expression");
            }
            var visitor = new RoselynExpressionEvaluatorVisitor(context, semanticModel, this.TypeResolver, this.UserVariables);
            return ast.GetCompilationUnitRoot().Accept(visitor);
        }

        public override string Resolve(DebuggerSession session, SourceLocation location, string expression)
        {
            if (session.TypeResolverHandler == null) {
                return expression;
            }

            var visitor = new RoselynExpressionResolverVisitor(session.TypeResolverHandler);
            var tree = CSharpSyntaxTree.ParseText(expression, new CSharpParseOptions(kind: SourceCodeKind.Script));
            return tree.GetCompilationUnitRoot().Accept(visitor).ToFullString();
        }

        public void UseTypeResolver(TypeResolverHandler resolver)
        {
            this.TypeResolver = resolver;
        }
    }
}