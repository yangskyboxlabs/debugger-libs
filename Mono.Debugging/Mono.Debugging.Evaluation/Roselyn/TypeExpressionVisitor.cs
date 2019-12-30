using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation.Roselyn
{
	public class TypeExpressionVisitor : CSharpSyntaxVisitor<ValueReference>
	{
		public EvaluationContext Context { get; }
		public SemanticModel SemanticModel { get; }

		ValueReference parent;

		public TypeExpressionVisitor (EvaluationContext context, SemanticModel semanticModel)
		{
			this.Context = context;
			this.SemanticModel = semanticModel;
		}

		public override ValueReference VisitGenericName (GenericNameSyntax node)
			=> VisitGenericName (null, node);

		ValueReference VisitGenericName (ValueReference parent, GenericNameSyntax node)
		{
			Console.WriteLine($"!! -> generic type resolution: {(parent != null ? "?." : "global::")}{node}");

			var name = $"{node.Identifier.Text}`{node.Arity}";
			string fullName;
			object typeObj;

			var myTypeArgs = node.TypeArgumentList.Arguments
				.Select(a => a.Accept(this).Type);

			if (parent != null) {
				switch (parent) {
				case NamespaceValueReference nsParent:
					fullName = $"{nsParent.CallToString()}.{name}";
					if (Context.TryGetType (fullName, myTypeArgs.ToArray (), out typeObj))
						return new TypeValueReference (Context, typeObj);
					break;
				case TypeValueReference typeParent:
					var parentType = (Type)typeParent.Type;

					var definitionType = (parentType.IsGenericTypeDefinition
							? parentType
							: parentType.GetGenericTypeDefinition ())
						.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);

					if (definitionType == null) {
						break;
					}

					var typeParameters = parentType.IsGenericType
						? parentType.GetGenericArguments ().Concat (myTypeArgs.Cast<Type> ()).ToArray ()
						: myTypeArgs.Cast<Type> ().ToArray ();

					if (Context.TryGetType (definitionType.FullName, typeParameters, out typeObj))
						return new TypeValueReference (Context, typeObj);

					break;
				}
			} else {
				var typeArgs = myTypeArgs.ToArray ();
				Console.WriteLine($"!! -> verify generic semantic resolve");
				if (TrySemanticResolve (node, out fullName)) {
					Console.WriteLine($"!! -> semantically resolved: {fullName}");
					return new TypeValueReference (Context, Context.Adapter.GetType (Context, fullName, typeArgs));
				}

				if (Context.TryGetType (name, typeArgs, out typeObj))
					return new TypeValueReference (Context, typeObj);

                // Try subtype of enclosing type
				var enclosingType = Context.Adapter.GetEnclosingType (Context);
                fullName = $"{Context.Adapter.GetTypeName(Context, enclosingType)}.{name}";
                if (Context.TryGetType (fullName, out typeObj))
                    return new TypeValueReference(this.Context, typeObj);

                // Try as type in imported namespace
                foreach (var ns in this.Context.Adapter.GetImportedNamespaces (Context)) {
                    fullName = $"{ns}.{name}";
                    if (Context.TryGetType(fullName, out typeObj))
                        return new TypeValueReference(this.Context, typeObj);
                }
			}
			return null;
		}

		public override ValueReference VisitIdentifierName (IdentifierNameSyntax node)
			=> VisitIdentifierName (null, node);

		ValueReference VisitIdentifierName (ValueReference parent, SimpleNameSyntax node)
		{
			Console.WriteLine($"!! -> type resolution: ?.{node}");

			var name = node.Identifier.Text;
			string fullName;
			object typeObj;

			if (parent != null) {
				switch (parent) {
				case NamespaceValueReference nsParent:
					fullName = $"{nsParent.CallToString()}.{name}";
					if (Context.TryGetType (fullName, out typeObj))
						return new TypeValueReference (Context, typeObj);
					else
						return new NamespaceValueReference (Context, fullName);
				case TypeValueReference typeParent:
					if (Context.TryGetNestedClass (typeParent, name, out typeObj))
						return new TypeValueReference (Context, typeObj);
					break;
				}
			} else {
				if (TrySemanticResolve (node, out fullName))
					return new TypeValueReference (Context, Context.Adapter.GetType (Context, fullName));

				if (Context.TryGetType (name, out typeObj))
					return new TypeValueReference (Context, typeObj);

                // Try subtype of enclosing type
				var enclosingType = Context.Adapter.GetEnclosingType (Context);
                fullName = $"{Context.Adapter.GetTypeName(Context, enclosingType)}.{name}";
                if (Context.TryGetType (fullName, out typeObj))
                    return new TypeValueReference(this.Context, typeObj);

                // Try as namespace or type in imported namespace
                foreach (var ns in this.Context.Adapter.GetImportedNamespaces (Context)) {
                    fullName = $"{ns}.{name}";
                    if (Context.TryGetType(fullName, out typeObj))
                        return new TypeValueReference(this.Context, typeObj);

                    var nsParts = ns.Split ('.').ToList ();
                    var partIndex = nsParts.IndexOf(name);
                    if (partIndex >= 0) {
                        fullName = string.Join (".", nsParts.Take (partIndex + 1));
                        Console.WriteLine($"!!  -> found namespace: {fullName}");
                        return new NamespaceValueReference(this.Context, fullName);
                    }
                }

				return new NamespaceValueReference (Context, name);
			}
			return null;
		}

		public override ValueReference VisitMemberAccessExpression (MemberAccessExpressionSyntax node)
		{
			Console.WriteLine($"!! -> type resolution: {node}");

			if (TrySemanticResolve (node, out var fullName))
				return node.Name.Accept(this);

			switch (node.Name) {
			case IdentifierNameSyntax identifier:
				if (Context.TryGetType (node.ToString(), out var type))
					return new TypeValueReference (Context, type);
				return this.VisitIdentifierName (node.Expression.Accept (this), identifier);
			case GenericNameSyntax genericName:
				return this.VisitGenericName (node.Expression.Accept (this), genericName);
			}
			return null;
		}

		public override ValueReference VisitPredefinedType (PredefinedTypeSyntax node)
		{
			var typeInfo = SemanticModel.GetTypeInfo (node);
			return new TypeValueReference (Context,
				Context.Adapter.GetType (Context, typeInfo.Type.GetFullMetadataName ()));
		}

		public override ValueReference VisitQualifiedName (QualifiedNameSyntax node)
		{
			if (TrySemanticResolve (node, out string fullMetadataName)) {
				return new TypeValueReference (Context,
					Context.Adapter.GetType (Context, fullMetadataName));
			}

			return null;
		}

		public override ValueReference VisitNullableType(NullableTypeSyntax node)
		{
			Console.WriteLine($"!!type -> VisitTypeParameter {node}");

			var elementTypeRef = node.ElementType.Accept(this);
			if (elementTypeRef == null) {
				throw new EvaluatorException($"Unknown type '{node.ElementType}'");
			}

			var elementType = (Type)elementTypeRef.Type;

			return new TypeValueReference (Context,
				Context.Adapter.GetType (Context, "System.Nullable`1", new[] { elementType }));
		}

		bool TrySemanticResolve (SyntaxNode node, out string fullMetadataName)
		{
			var symbolInfo = SemanticModel.GetSymbolInfo (node);
			ISymbol symbol = null;

			if (symbolInfo.Symbol != null) {
				symbol = symbolInfo.Symbol;
			} else if (symbolInfo.CandidateReason == CandidateReason.NotAValue) {
				// We can accept candidates that were rejected for not being a value
				symbol = symbolInfo.CandidateSymbols [0];
			}

			fullMetadataName = (symbol as ITypeSymbol)?.GetFullMetadataName ();
			if (fullMetadataName != null)
				Console.WriteLine($"!!  -> semantically resolved: {node} => {fullMetadataName}");
			return fullMetadataName != null;
		}
	}
}