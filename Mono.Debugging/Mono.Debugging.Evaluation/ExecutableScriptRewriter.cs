using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mono.Debugging.Evaluation
{
    public class ExecutableScriptRewriter : CSharpSyntaxRewriter
    {
        public EvaluationContext Context { get; }

        public SemanticModel SemanticModel { get; }

        public ExecutableScriptRewriter(EvaluationContext context, SemanticModel semanticModel)
        {
            this.Context = context;
            this.SemanticModel = semanticModel;
        }

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.Text;
            string fullName;

            var typeInfo = this.SemanticModel.GetTypeInfo(node);
            Console.WriteLine($"!! -> identifier {name} is type {typeInfo.Type?.Kind}");

            switch (typeInfo.Type) {
                case IErrorTypeSymbol error:
                    Console.WriteLine($"!!  -> could not resolve identifier {name}");
                    if (error.ContainingNamespace.IsGlobalNamespace) {
                        Console.WriteLine($"!!  -> {name} is at root");
                    }
                    break;
                case INamedTypeSymbol namedType:
                    Console.WriteLine($"!! -> identifier {name} is type: {namedType.Name}");
                    fullName = namedType.ContainingNamespace.IsGlobalNamespace
                        ? namedType.Name
                        : $"{namedType.ContainingNamespace.Name}.{namedType.Name}";
                    
                    return this.GetTypeReferenceSyntax(typeInfo.Type)
                        .WithAdditionalAnnotations(new SyntaxAnnotation("_orig", node.ToString()))
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia());
            }

/*
            var symbolInfo = this.SemanticModel.GetSymbolInfo(node);
            Console.WriteLine($"!! identifier {name} is symbol {symbolInfo.Symbol?.Kind}");

            switch (symbolInfo.Symbol) {
                case INamespaceSymbol nsSymbol:
                Console.WriteLine($"!! identifier {name} is namespace: {nsSymbol.Name}");
                    return new NamespaceValueReference(this.Context, nsSymbol.Name);
            }
            */

            return node;
        }

        public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var name = node.Name.Identifier.Text;
            string fullName;

            var typeInfo = this.SemanticModel.GetTypeInfo(node.Name);
            Console.WriteLine($"!! -> member access {name} is type {typeInfo.Type?.Kind}");

            switch (typeInfo.Type) {
                case IErrorTypeSymbol error:
                    Console.WriteLine($"!!  -> could not resolve identifier {name}");
                    return this.GetMemberReferenceSyntax(node.Expression, error);
                case INamedTypeSymbol namedType:
                    Console.WriteLine($"!! -> identifier {name} is type: {namedType.Name}");
                    fullName = namedType.ContainingNamespace.IsGlobalNamespace
                        ? namedType.Name
                        : $"{namedType.ContainingNamespace.Name}.{namedType.Name}";
                    
                    return this.GetTypeReferenceSyntax(typeInfo.Type)
                        .WithAdditionalAnnotations(new SyntaxAnnotation("_orig", node.ToString()))
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia());
            }

            return node;
        }

        private SyntaxNode GetTypeReferenceSyntax(ITypeSymbol typeSymbol)
        {
            var fullName = typeSymbol.ContainingNamespace.IsGlobalNamespace
                ? typeSymbol.Name
                : $"{typeSymbol.ContainingNamespace.Name}.{typeSymbol.Name}";

            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(nameof(SdbScriptExecutionContext._Type)),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(new[] {
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(fullName)))
                })));
        }

        private SyntaxNode GetMemberReferenceSyntax(ExpressionSyntax parent, ITypeSymbol typeSymbol)
        {
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                (ExpressionSyntax)parent.Accept(this),
                SyntaxFactory.IdentifierName(typeSymbol.Name));
        }
    }
}