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
            this.TypeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        public override SyntaxNode VisitGenericName(GenericNameSyntax node)
        {
            return this.GetExpandedTypeNode(node);
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            return this.GetExpandedTypeNode(node);
        }

        public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (!(node.Expression is SimpleNameSyntax)) {
                return node;
            }

            return node.ReplaceNode(node.Expression, this.GetExpandedTypeNode(node.Expression as SimpleNameSyntax));
        }

        private SyntaxNode GetExpandedTypeNode(SimpleNameSyntax node)
        {
            var name = node.Identifier.Text;
            var isGeneric = node is GenericNameSyntax;
            var genericNode = node as GenericNameSyntax;

            if (isGeneric) {
                name = $"{name}`{((GenericNameSyntax)node).Arity}";
            }

            var mappedType = this.TypeResolver.Invoke(name, null);

            if (mappedType == null || mappedType == name) {
                return node;
            }

            if (isGeneric) {
                mappedType = $"{mappedType}<{string.Join(",", genericNode.TypeArgumentList.Arguments.Select(a => a.ToString()))}>";
            }

            Console.WriteLine($"!! r: {name} => {mappedType}");

            var statement = (GlobalStatementSyntax)CSharpSyntaxTree
                .ParseText(mappedType, new CSharpParseOptions(kind: SourceCodeKind.Script))
                .GetCompilationUnitRoot()
                .ChildNodes()
                .First();

            return ((ExpressionStatementSyntax)statement.Statement).Expression;
        }
    }
}