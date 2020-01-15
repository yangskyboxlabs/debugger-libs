using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class RoselynExpressionResolverVisitor : CSharpSyntaxRewriter
    {
        public TypeResolverHandler TypeResolver { get; }

        public RoselynExpressionResolverVisitor(TypeResolverHandler typeResolver)
        {
            this.TypeResolver = typeResolver;
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            //Console.WriteLine($"!! resolve?: {node.Identifier.Text} has parent {node.Parent.Kind()}");

            if (node.Kind() == SyntaxKind.SimpleMemberAccessExpression) {
                return node;
            }

            var mappedType = this.TypeResolver.Invoke(node.Identifier.Text, null);

            if (mappedType == null || mappedType == node.Identifier.Text) {
                return node;
            }

            var statement = (GlobalStatementSyntax)CSharpSyntaxTree
                .ParseText(mappedType, new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetCompilationUnitRoot()
                .ChildNodes()
                .First();

            var expression = statement.Statement as ExpressionStatementSyntax;
            if (expression != null) {
                return (node as SyntaxNode).ReplaceNode(node, expression.Expression);
            }

            return node;
        }
    }
}