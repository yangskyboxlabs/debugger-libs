using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class RoselynExpressionEvaluatorVisitor : CSharpSyntaxVisitor<ValueReference>
    {
        private class ResolutionContext
        {
            public bool WantTypeResult;
            public ValueReference Parent;
        }

        private Stack<ResolutionContext> ContextStack = new Stack<ResolutionContext>();

        private ResolutionContext LookupContext => this.ContextStack.Peek();

        public Dictionary<string, ValueReference> UserVariables { get; }
        public EvaluationContext Context { get; }
        public SemanticModel SemanticModel { get; }

        public TypeResolverHandler TypeResolver { get; }

        private ObjectValueAdaptor Adapter => this.Context.Adapter;

        public RoselynExpressionEvaluatorVisitor(EvaluationContext context, SemanticModel semanticModel, TypeResolverHandler typeResolver, Dictionary<string, ValueReference> userVariables)
        {
            this.Context = context;
            this.SemanticModel = semanticModel;
            this.TypeResolver = typeResolver;
            this.UserVariables = userVariables;
        }

        private object GetEnclosingType()
            => this.Context.Adapter.GetEnclosingType(this.Context);

        private bool TryGetMember(ValueReference targetRef, string name, out ValueReference memberReference)
        {
            switch (targetRef) {
                case TypeValueReference typeRef:
                    memberReference = this.Context.Adapter.GetMember(this.Context, null, typeRef.Type, null, name);
                    break;
                default:
                    memberReference = this.Context.Adapter.GetMember(this.Context, targetRef, targetRef.Type, targetRef.Value, name);
                    break;
            }

            return memberReference != null;
        }

        private bool TryGetMember(ValueReference targetRef, object targetType, string name, out ValueReference memberReference)
        {
            memberReference = this.Context.Adapter.GetMember(this.Context, targetRef, targetType, targetRef.Value, name);
            return memberReference != null;
        }

        private bool TryGetStaticMember(object targetType, string name, out ValueReference memberReference)
        {
            memberReference = this.Context.Adapter.GetMember(this.Context, null, targetType, null, name);
            return memberReference != null;
        }

        private bool TryGetThisReference(out ValueReference thisReference)
        {
            thisReference = this.Context.Adapter.GetThisReference(this.Context);
            return thisReference != null;
        }

        private bool TryGetType(string name, out object typeObject)
        {
            Console.WriteLine($"!! -> trying type {name}");
            typeObject = this.Context.Adapter.GetType(this.Context, name);
            if (typeObject != null) {
                this.Context.Adapter.ForceLoadType(this.Context, typeObject);
                return true;
            }
            return false;
        }
        private bool TryGetType(string name, object[] typeArguments, out object typeObject)
        {
            Console.WriteLine($"!! -> trying type {name} <..>");
            typeObject = this.Context.Adapter.GetType(this.Context, name, typeArguments);
            if (typeObject != null) {
                this.Context.Adapter.ForceLoadType(this.Context, typeObject);
                return true;
            }
            return false;
        }

        public object ResolveType(ExpressionSyntax node)
        {
            this.ContextStack.Push(new ResolutionContext {
                WantTypeResult = true,
            });

            Console.WriteLine($"!! -> new type resolution context");

            Console.WriteLine($"!!  -> {node.Kind()}");
            var r = node.Accept(this);

            this.ContextStack.Pop();

            Console.WriteLine($"!! <- end type resolution context");
            return r.Type;
        }

        public override ValueReference VisitArgument(ArgumentSyntax node)
        {
            return node.Expression.Accept(this);
        }

        public override ValueReference VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
        {
            var elementType = this.ResolveType(node.Type.ElementType);
            var rankSpec = node.Type.RankSpecifiers.First();
            var sizes = rankSpec.Sizes.Select(s => (int)s.Accept(this).ObjectValue).ToArray();

            var array = this.Adapter.CreateArray(this.Context, elementType, sizes);

            if (node.Initializer != null) {
                var arrayAdapter = this.Adapter.CreateArrayAdaptor(this.Context, array);
                var index = new int[rankSpec.Rank];

                void initializeArray(int rank, InitializerExpressionSyntax initializer)
                {
                    index[rank] = 0;
                    foreach (var el in initializer.Expressions) {
                        switch (el) {
                            case InitializerExpressionSyntax subInitializer:
                                initializeArray(rank + 1, subInitializer);
                                break;
                            default:
                                arrayAdapter.SetElement(index, el.Accept(this).Value);
                                break;
                        }
                        index[rank]++;
                    }
                }

                initializeArray(0, node.Initializer);
            }

            return LiteralValueReference.CreateTargetObjectLiteral(this.Context, node.ToString(), array);
        }

        public override ValueReference VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (!this.Context.Options.AllowMethodEvaluation) {
                throw new ImplicitEvaluationDisabledException();
            }

            var left = node.Left.Accept(this);
            var right = node.Right.Accept(this);

            if (left is UserVariableReference) {
                left.Value = right.Value;
            }
            else {
                var castedValue = this.Adapter.TryCast(this.Context, right.Value, left.Type);

                if (castedValue == null) {
                    Console.WriteLine($"!! could not cast: {right.Type} to {this.Adapter.GetValueType(this.Context, left.Value)}");
                }
                left.Value = castedValue;
            }

            return left;
        }

        public override ValueReference VisitBaseExpression(BaseExpressionSyntax node)
        {
            var self = this.Context.Adapter.GetThisReference(this.Context);
            if (self != null) {
                return LiteralValueReference.CreateTargetBaseObjectLiteral(this.Context, node.ToString(), self.Value);
            }

            throw new EvaluatorException("Unexpected 'base' reference in static method.");
        }

        public override ValueReference VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            ValueReference leftRef;
            object targetType;
            object result;
            switch (node.Kind()) {
                case SyntaxKind.AsExpression:
                    leftRef = node.Left.Accept(this);
                    targetType = this.ResolveType(node.Right);
                    result = this.Adapter.TryCast(this.Context, leftRef.Value, targetType);
                    return result != null
                        ? (ValueReference)LiteralValueReference.CreateTargetObjectLiteral(this.Context, node.ToString(), result, targetType)
                        : new NullValueReference(this.Context, targetType);

                case SyntaxKind.IsExpression:
                    leftRef = node.Left.Accept(this);
                    targetType = this.ResolveType(node.Right);
                    if (this.Context.Adapter.IsNullableType(this.Context, targetType)) {
                        targetType = this.Context.Adapter.GetGenericTypeArguments(this.Context, targetType).Single();
                    }

                    var valueIsPrimitive = this.Context.Adapter.IsPrimitive(this.Context, leftRef.Value);
                    var typeIsPrimitive = this.Context.Adapter.IsPrimitiveType(targetType);

                    if (valueIsPrimitive != typeIsPrimitive) {
                        return LiteralValueReference.CreateObjectLiteral(this.Context, node.ToString(), false);
                    }
                    if (typeIsPrimitive) {
                        return LiteralValueReference.CreateObjectLiteral(this.Context, node.ToString(),
                            this.Context.Adapter.GetTypeName(this.Context, targetType) == this.Adapter.GetValueTypeName(this.Context, leftRef.Value));
                    }

                    result = this.Adapter.TryCast(this.Context, leftRef.Value, targetType);

                    Console.WriteLine($"!!  -> cast result: {result}");

                    return LiteralValueReference.CreateObjectLiteral(this.Context, node.ToString(), result != null);
            }

            throw new EvaluatorException($"Binary operator '{node.OperatorToken}' is not supported");
        }

        public override ValueReference VisitCastExpression(CastExpressionSyntax node)
        {
            var targetType = this.ResolveType(node.Type);
            var value = node.Expression.Accept(this).Value;

            var castValue = this.Adapter.Cast(this.Context, value, targetType);

            if (castValue == null) {
                throw new EvaluatorException($"Could not cast {value} to {targetType}");
            }

            return LiteralValueReference.CreateTargetObjectLiteral(this.Context, "result", castValue, targetType);
        }

        public override ValueReference VisitCompilationUnit(CompilationUnitSyntax node)
        {
            ValueReference value = null;
            foreach (var s in node.ChildNodes()) {
                value = (s as CSharpSyntaxNode)?.Accept(this);
            }

            return value;
        }

        public override ValueReference VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            var condition = node.Condition.Accept(this);

            if (condition == null) {
                throw new EvaluatorException($"Could not evaluate expression: {node.Condition.ToString()}");
            }

            return (bool)condition.ObjectValue
                ? node.WhenTrue.Accept(this)
                : node.WhenFalse.Accept(this);
        }

        public override ValueReference VisitDefaultExpression(DefaultExpressionSyntax node)
        {
            var type = this.ResolveType(node.Type);

            if (type == null){
                throw new EvaluatorException($"Could not resolve type '{node.Type}'");
            }

            if (this.Context.Adapter.IsClass(this.Context, type)) {
                return LiteralValueReference.CreateTargetObjectLiteral(this.Context, node.ToString(), this.Context.Adapter.CreateNullValue(this.Context, type), type);
            }

            if (this.Context.Adapter.IsValueType(type)) {
                return LiteralValueReference.CreateTargetObjectLiteral(this.Context, node.ToString(), this.Context.Adapter.CreateValue(this.Context, type), type);
            }

            var expression = node.ToString();
            switch (this.Context.Adapter.GetTypeName(this.Context, type))
            {
                case "System.Boolean": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, false);
                case "System.Char": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, '\0');
                case "System.Byte": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (byte)0);
                case "System.SByte": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (sbyte)0);
                case "System.Int16": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (short)0);
                case "System.UInt16": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (ushort)0);
                case "System.Int32": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (int)0);
                case "System.UInt32": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (uint)0);
                case "System.Int64": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (long)0);
                case "System.UInt64": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (ulong)0);
                case "System.Decimal": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (decimal)0);
                case "System.Single": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (float)0);
                case "System.Double": return LiteralValueReference.CreateObjectLiteral(this.Context, expression, (double)0);
                default: throw new Exception($"Unexpected type {this.Context.Adapter.GetTypeName(this.Context, type)}");
            }
        }

        public override ValueReference VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.Declaration.Variables.Count > 1) {
                throw new NotSupportedExpressionException();
            }

            var v = node.Declaration.Variables.First();

            var name = v.Identifier.ValueText;
            var vref = new UserVariableReference(this.Context, name);

            if (v.Initializer != null) {
                vref.Value = v.Initializer.Value.Accept(this).Value;
            }

            this.UserVariables.Add(name, vref);

            return vref;
        }

        public override ValueReference VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            var target = node.Expression.Accept(this);
            var args = node.ArgumentList.Arguments;

            if (target is TypeValueReference) {
                throw new EvaluatorException($"Invalid element access on type object");
            }

            if (this.Adapter.IsArray(this.Context, target.Value))
            {
                var indexes = args
                    .Select(a => (int)Convert.ChangeType(a.Accept(this).ObjectValue, typeof(int)))
                    .ToArray();

                return new ArrayValueReference(this.Context, target.Value, indexes);
            }

            var indexArgs = args.Select(a => a.Accept(this).Value).ToArray();

            return this.Adapter.GetIndexerReference(this.Context, target.Value, target.Type, indexArgs)
                ?? throw new EvaluatorException($"Could not resolve element access");
        }

        public override ValueReference VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            Console.WriteLine($"!! -> {node.Expression.Kind()}");
            this.ContextStack.Push(new ResolutionContext());
            var r = node.Expression.Accept(this);
            this.ContextStack.Pop();
            return r;
        }

        public override ValueReference VisitGenericName(GenericNameSyntax node)
        {
            var parentRef = this.LookupContext.Parent;

            Console.WriteLine($"!! -> generic name: {node.Identifier.ValueText}");

            var typeArguments = node.TypeArgumentList.Arguments
                //.Select(ta => this.ResolveType(ta))
                .Select(ta => ta.Accept(this).Type)
                .ToArray();

            var typeName = $"{node.Identifier.ValueText}`{node.Arity}";

/*
            switch (parentRef) {
                case NamespaceValueReference namespacePrefix:
                    typeName = $"{namespacePrefix.CallToString()}.{typeName}";
                    break;
                case TypeValueReference parentTypeRef:
                    var parentType = parentTypeRef.Type;
                    if (this.Context.Adapter.IsGenericType(this.Context, parentTypeRef.Type)) {
                        var parentName = this.Context.Adapter.GetTypeName(this.Context, parentType);
                        parentName = parentName.Substring(0, parentName.IndexOf('['));
                        typeName = $"{parentName}.{typeName}";

                        var combinedTypeArguments = this.Context.Adapter
                            .GetTypeArgs(this.Context, parentTypeRef.Type)
                            .Concat(typeArguments)
                            .ToArray();

                        var aa = combinedTypeArguments
                            .Select(a => this.Context.Adapter.GetTypeName(this.Context, a));

                        Console.WriteLine($"!!   -> trying: {typeName} [{string.Join(",", aa)}]");

                        if (this.TryGetType(typeName, combinedTypeArguments, out var t)) {
                            return new TypeValueReference(this.Context, t);
                        }
                    }
                    else {
                        typeName = $"{parentTypeRef.CallToString()}.{typeName}";
                    }
                    break;
                case null:
                    // Try type resolution table
                    if (this.TryGetIdentifierFullName(typeName, out var fullName)) {
                        Console.WriteLine($"!! -> name in identifier table: {typeName} -> {fullName}");
                        typeName = $"{fullName}`{typeArguments.Length}";
                    }
                    break;
            }
            */

            if (this.TryGetType(typeName, typeArguments, out var type)) {
                return new TypeValueReference(this.Context, type);
            }

            // Try types in namespaces
            foreach (var ns in this.Context.Adapter.GetImportedNamespaces(this.Context)) {
                Console.WriteLine($"!! -> checking namespace {ns}");
                if (this.TryGetType($"{ns}.{typeName}", typeArguments, out var t)) {
                    return new TypeValueReference(this.Context, t);
                }
            }

            throw new EvaluatorException($"Could not resolve type '{typeName}'");
        }

        public override ValueReference VisitGlobalStatement(GlobalStatementSyntax node)
        {
            return node.Statement.Accept(this);
        }

        public override ValueReference VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            throw new EvaluatorException($"Simple lambda expression not supported.");
        }

        public override ValueReference VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            throw new EvaluatorException($"Parenthesized lambda expression not supported.");
        }

        public override ValueReference VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            switch (node.Kind()) {
                case SyntaxKind.NullLiteralExpression:
                    return new NullValueReference(this.Context, /*expectedType ?? */this.Context.Adapter.GetType(this.Context, "System.Object"));
                default:
                    return LiteralValueReference.CreateObjectLiteral(this.Context, node.ToFullString(), node.Token.Value);
            }
        }

        public override ValueReference VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.ValueText;
            string fullName;
            ValueReference memberRef;
            ValueReference resolved;
            object val;

            Console.WriteLine($"!! -> identifier: {node.ToString()}");

            var symbolInfo = this.SemanticModel.GetSymbolInfo(node);

            if (this.TryResolveAsSymbol(symbolInfo, node, out resolved)) {
                return resolved;
            }

/*
            if (this.TryResolveAsType(node, out resolved)) {
                return resolved;
            }
            */

/*
            var symbolInfo = this.SemanticModel.GetSymbolInfo(node);
            Console.WriteLine($"!! identifier {name} is symbol {symbolInfo.Symbol?.Kind}");

            switch (symbolInfo.Symbol) {
                case INamespaceSymbol nsSymbol:
                Console.WriteLine($"!! identifier {name} is namespace: {nsSymbol.Name}");
                    return new NamespaceValueReference(this.Context, nsSymbol.Name);
            }
            */

            if (false/*this.LookupContext.Parent != null*/) {
                var parent = this.LookupContext.Parent;
                switch (parent) {
                    case NamespaceValueReference namespaceRef:
                        var parentNamespace = namespaceRef.CallToString();
                        if (this.TryGetType($"{parentNamespace}.{name}", out var type)) {
                            return new TypeValueReference(this.Context, type);
                        }
                        // Assume it's a namespace
                        return new NamespaceValueReference(this.Context, $"{parentNamespace}.{name}");
                    case TypeValueReference typeRef:
                        if (this.TryGetMember(typeRef, name, out memberRef)) {
                            return memberRef;
                        }
                        // Try nested classes
                        var nestedClasses = this.Context.Adapter.GetNestedTypes(this.Context, typeRef.Type);
                        foreach (var nested in nestedClasses) {
                            if (this.Context.Adapter.GetTypeName(this.Context, nested).EndsWith($"+{name}")) {
                                return new TypeValueReference(this.Context, nested);
                            }
                        }
                        break;
                    default:
                        if (this.TryGetMember(parent, name, out memberRef)) {
                            return memberRef;
                        }
                        break;
                }
                return null;
            }
            else {
                // Try user defined variables
                if (this.UserVariables.TryGetValue(name, out var valueRef)) {
                    return valueRef;
                }

                // Try local variables
                valueRef = this.Context.Adapter.GetLocalVariable(this.Context, name);
                if (valueRef != null) {
                    return valueRef;
                }

                // Try parameters
                valueRef = this.Context.Adapter.GetParameter(this.Context, name);
                if (valueRef != null) {
                    return valueRef;
                }

                // Try implicit `this`
                if (this.TryGetThisReference(out var thisRef)) {
                    // First try enclosing type
                    if (this.TryGetMember(thisRef, this.GetEnclosingType(), name, out var member)) {
                        return member;
                    }

                    if (this.TryGetMember(thisRef, name, out member)) {
                        return member;
                    }
                }

                var enclosingType = this.GetEnclosingType();

                for (var vtype = enclosingType; vtype != null;) {
                    Console.WriteLine($"!!  -> checking in {this.Context.Adapter.GetTypeName(this.Context, vtype)}");
                    if (this.TryGetStaticMember(vtype, name, out var member)) {
                        return member;
                    }

                    var nestedClasses = this.Context.Adapter.GetNestedTypes(this.Context, vtype);
                    foreach (var nested in nestedClasses) {
                        Console.WriteLine($"!!  -> nested class: {this.Context.Adapter.GetTypeName(this.Context, nested)}");
                        if (this.Context.Adapter.GetTypeName(this.Context, nested).EndsWith($"+{name}")) {
                            return new TypeValueReference(this.Context, nested);
                        }
                    }

                    vtype = this.Context.Adapter.GetParentType(this.Context, vtype);
                }

                for (var vtype = this.Context.Adapter.GetBaseType(this.Context, enclosingType); vtype != null;) {
                    Console.WriteLine($"!!  -> checking in {this.Context.Adapter.GetTypeName(this.Context, vtype)}");
                    if (this.TryGetStaticMember(vtype, name, out var member)) {
                        return member;
                    }

                    var nestedClasses = this.Context.Adapter.GetNestedTypes(this.Context, vtype);
                    foreach (var nested in nestedClasses) {
                        Console.WriteLine($"!!  -> nested class: {this.Context.Adapter.GetTypeName(this.Context, nested)}");
                        if (this.Context.Adapter.GetTypeName(this.Context, nested).EndsWith($"+{name}")) {
                            return new TypeValueReference(this.Context, nested);
                        }
                    }

                    vtype = this.Context.Adapter.GetBaseType(this.Context, vtype);
                }

                object typeObject;
                fullName = name;
                
                // Try as type
                if (this.TryGetType(name, out typeObject)) {
                    return new TypeValueReference(this.Context, typeObject);
                }

                // Try subtype of enclosing type
                fullName = $"{this.Context.Adapter.GetTypeName(this.Context, enclosingType)}.{name}";
                if (this.TryGetType(fullName, out typeObject)) {
                    return new TypeValueReference(this.Context, typeObject);
                }

                // Try as namespace or type in imported namespace
                foreach (var ns in this.Context.Adapter.GetImportedNamespaces(this.Context)) {
                    fullName = $"{ns}.{name}";
                    if (this.TryGetType(fullName, out typeObject)) {
                        return new TypeValueReference(this.Context, typeObject);
                    }

                    var nsParts = ns.Split('.').ToList();
                    var partIndex = nsParts.IndexOf(name);
                    if (partIndex >= 0) {
                        fullName = string.Join(".", nsParts.Take(partIndex + 1));
                        Console.WriteLine($"!!  -> found namespace: {fullName}");
                        return new NamespaceValueReference(this.Context, fullName);
                    }
                }
            }

            throw new EvaluatorException($"Could not resolve identifier {name}");
        }

        public override ValueReference VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (!this.Context.Options.AllowMethodEvaluation) {
                throw new ImplicitEvaluationDisabledException();
            }

            var info = node.Accept(new BindingInfoVisitor(this,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.InvokeMethod,
                null));

            Console.WriteLine($"!! invoking ({this.Context.Adapter.GetTypeName(this.Context, info.Type)}).{info.MethodName}<{string.Join(",", info.TypeArguments?.Select(t => this.Context.Adapter.GetTypeName(this.Context, t)) ?? Enumerable.Empty<string>())}>({string.Join(",", info.Arguments.Select(a => this.Context.Adapter.GetValueTypeName(this.Context, a)))})");

            var ret = this.Adapter.RuntimeInvoke(
                this.Context,
                info.Type,
                info.Target,
                info.MethodName,
                info.TypeArguments,
                info.ArgumentTypes,
                info.Arguments);

            return LiteralValueReference.CreateTargetObjectLiteral(this.Context, "result", ret);
        }

        public override ValueReference VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            Console.WriteLine($"!! -> VisitMemberAccess... {node.Name}");
            ValueReference resolved;
            var symbolInfo = this.SemanticModel.GetSymbolInfo(node);

            Console.WriteLine($"!!  -> symbol: {symbolInfo.Symbol?.Kind}");

            if ((symbolInfo.Symbol is ITypeSymbol) && this.TryResolveAsSymbol(symbolInfo, node.Name, out resolved)) {
                return resolved;
            }

            Console.WriteLine($"!!  -> brute-force...");
            var parent = node.Expression.Accept(this);

            if (this.TryResolveAsSymbol(symbolInfo, node.Name, parent, out resolved)) {
                return resolved;
            }

            if (parent ==  null) {
                throw new EvaluatorException($"Could not resolve parent of '{node}'");
            }

            if (parent is NamespaceValueReference) {
                if (this.TryResolveAsType(node.Expression.ToString(), node.Name, out resolved)) {
                    return resolved;
                }
                else {
                    return new NamespaceValueReference(this.Context, node.ToString());
                }
            }

            this.LookupContext.Parent = parent;

            Console.WriteLine($"!!  -> parent: {parent.GetType()}");
            switch (parent) {
                case TypeValueReference typeParent:
                    // Nested type?
                    Console.WriteLine($"!!  -> trying nested type of {this.Context.Adapter.GetTypeName(this.Context, parent.Type)}");
                    if (this.TryResolveAsNestedType(typeParent, node.Name, out resolved)) {
                        return resolved;
                    }
                    break;
                default:
                    if (this.TryGetMember(parent, node.Name.Identifier.Text, out resolved)) {
                        return resolved;
                    }
                    break;
            }

            return node.Name.Accept(this);
        }

        public override ValueReference VisitNullableType(NullableTypeSyntax node)
        {
            var innerType = this.ResolveType(node.ElementType);
            if (this.TryGetType("System.Nullable`1", new[] { innerType }, out var type)) {
                return new TypeValueReference(this.Context, type);
            }

            throw new EvaluatorException($"Could not resolve type {node.ToString()}");
        }

        public override ValueReference VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var type = this.ResolveType(node.Type);
            var args = node.ArgumentList.Arguments
                .Select(a => a.Accept(this)?.Value)
                .ToArray();

            return LiteralValueReference.CreateTargetObjectLiteral(this.Context,
                node.ToString(),
                this.Adapter.CreateValue(this.Context, type, args));
        }

        public override ValueReference VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            return node.Expression.Accept(this);
        }

        public override ValueReference VisitPredefinedType(PredefinedTypeSyntax node)
        {
            var typeInfo = this.SemanticModel.GetTypeInfo(node);
            var fullName = typeInfo.Type.GetFullMetadataName();
            return new TypeValueReference(this.Context, this.Context.Adapter.GetType(this.Context, fullName));
        }

        public override ValueReference VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            throw new EvaluatorException("postfix unary not supported");
        }

        public override ValueReference VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            var vref = node.Operand.Accept(this);
            var val = vref.ObjectValue;
            var operand = node.Operand.Accept(this).ObjectValue;
            var kind = node.Kind();

            object newVal = null;

            switch (node.Kind()) {
                case SyntaxKind.BitwiseNotExpression:
                    switch (val) {
                        case sbyte b: newVal = ~b; break;
                        case char c: newVal = ~c; break;
                        case short s: newVal = ~s; break;
                        case ushort us: newVal = ~us; break;
                        case int i: newVal = ~i; break;
                        case uint ui: newVal = ~ui; break;
                        case long l: newVal = ~l; break;
                        case ulong ul: newVal = ~ul; break;
                    }
                    break;
                case SyntaxKind.LogicalNotExpression:
                    switch (val) { case bool b: newVal = !b; break; }
                    break;
                case SyntaxKind.UnaryMinusExpression:
                    switch (val) {
                        case sbyte b: newVal = -b; break;
                        case char c: newVal = -c; break;
                        case short s: newVal = -s; break;
                        case ushort us: newVal = -us; break;
                        case int i: newVal = -i; break;
                        case uint ui: newVal = -ui; break;
                        case long l: newVal = -l; break;
                        // TODO: float, double
                    }
                    break;
                case SyntaxKind.UnaryPlusExpression:
                    newVal = val;
                    break;
            }

            if (newVal == null) {
                throw new NotSupportedException($"Unsupported unary expression {node.Kind()} on {val.GetType()}.");
            }

            return LiteralValueReference.CreateObjectLiteral(this.Context, node.ToString(), newVal);
        }

        public override ValueReference VisitQualifiedName(QualifiedNameSyntax node)
        {
            this.LookupContext.Parent = node.Left.Accept(this);
            return node.Right.Accept(this);
        }

        public override ValueReference VisitThisExpression(ThisExpressionSyntax node)
        {
            return this.Context.Adapter.GetThisReference(this.Context);
        }

        public override ValueReference VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            var typeObj = this.ResolveType(node.Type);
            if (typeObj != null) {
                return LiteralValueReference.CreateTargetObjectLiteral(this.Context,
                    node.Type.ToString(),
                    this.Context.Adapter.CreateTypeObject(this.Context, typeObj));
            }

            throw new EvaluatorException($"Could not resolve '{node.Type.ToString()}' to a known type");
        }

        private bool TryResolveAsNestedType(TypeValueReference parent, SimpleNameSyntax nameNode, out ValueReference resolved)
        {
            var name = nameNode.Identifier.Text;
            var genericName = nameNode as GenericNameSyntax;

            if (genericName != null) {
                name = $"{name}`{genericName.Arity}";
            }

            var nestedTypes = this.Context.Adapter
                .GetNestedTypes(this.Context, parent.Type)
                .ToList();

            Console.WriteLine($"!!  -> nested types: {nestedTypes.Count}");
            foreach (var nested in nestedTypes) {
                var nestedName = this.Context.Adapter.GetTypeName(this.Context, nested);
                Console.WriteLine($"!!  -> nested type: {nestedName}");

                if (nestedName.EndsWith($"+{name}")) {
                    if (genericName != null) {
                        var typeArguments = this.Context.Adapter
                            .GetGenericTypeArguments(this.Context, parent.Type)
                            .Concat(genericName.TypeArgumentList.Arguments.Select(ta => ta.Accept(this).Type))
                            .ToArray();

                        if (this.TryGetType(nestedName, typeArguments, out var typeObj)) {
                            resolved = new TypeValueReference(this.Context, nested);
                        }
                        else {
                            Console.Write($"!!  -> ???");
                            resolved = null;
                            return false;
                        }
                    }
                    else {
                        resolved = new TypeValueReference(this.Context, nested);
                        return true;
                    }
                }
            }

            // Brute-force nested type
            var forcedName = $"Thing`1+{name}";
            Console.WriteLine($"!!  -> try forcing nested name {forcedName}");
            var tas = this.Context.Adapter
                .GetGenericTypeArguments(this.Context, parent.Type)
                .Concat(genericName.TypeArgumentList.Arguments.Select(ta => ta.Accept(this).Type))
                .ToArray();

            Console.WriteLine($"!!  -> type args: {string.Join(",",tas.Select(t => this.Context.Adapter.GetTypeName(this.Context, t)))}");

            if (this.TryGetType(forcedName, tas, out var o)) {
                resolved = new TypeValueReference(this.Context, o);
                return true;
            }

            resolved = null;
            return false;
        }

        private bool TryResolveAsSymbol(SymbolInfo symbolInfo, SimpleNameSyntax nameNode, out ValueReference resolved)
            => this.TryResolveAsSymbol(symbolInfo, nameNode, null, out resolved);

        private bool TryResolveAsSymbol(SymbolInfo symbolInfo, SimpleNameSyntax nameNode, ValueReference parent, out ValueReference resolved)
        {
            ISymbol symbol = symbolInfo.Symbol;
            switch (symbol) {
                case IPropertySymbol propertySymbol:
                case IFieldSymbol fieldSymbol:
                    Console.WriteLine($"!!  -> member symbol: {symbol.Name}");
                    return this.TryGetMember(parent, symbol.Name, out resolved);
            }

            if (symbolInfo.CandidateReason != CandidateReason.None) {
                var candidates = symbolInfo.CandidateSymbols;
                Console.WriteLine($"!!  -> {candidates.Length} candidates ({symbolInfo.CandidateReason})");

                // If the only candidate was rejected because it's not a value, we can still resolve to it
                if (candidates.Length == 1 && symbolInfo.CandidateReason == CandidateReason.NotAValue) {
                    symbol = candidates[0];
                    switch (symbol) {
                        case ITypeSymbol typeSymbol:
                            resolved = this.GetTypeValueReference(
                                typeSymbol,
                                (nameNode as GenericNameSyntax)?.TypeArgumentList.Arguments);
                            return true;
                    }
                }
                else {
                    throw new EvaluatorException($"Unresolvable symbol: {nameNode}");
                }
            }

            resolved = null;
            return false;
        }

        private bool TryResolveAsType(SimpleNameSyntax nameNode, out ValueReference resolved)
            => this.TryResolveAsType(null, nameNode, out resolved);

        private bool TryResolveAsType(string parent, SimpleNameSyntax nameNode, out ValueReference resolved)
        {
            Console.WriteLine($"!!  -> TryResolveAsType");
            var fullName = parent != null
                ? $"{parent}.{nameNode.Identifier.Text}"
                : nameNode.Identifier.Text;

            var typeArguments = (nameNode as GenericNameSyntax)?.TypeArgumentList.Arguments
                .Select(t => t.Accept(this).Type)
                .ToArray();

            if (typeArguments != null) {
                if (this.TryGetType(fullName, typeArguments, out var typeObj)) {
                    resolved = new TypeValueReference(this.Context, typeObj);
                    return true;
                }
            }
            else {
                if (this.TryGetType(fullName, out var typeObj)) {
                    resolved = new TypeValueReference(this.Context, typeObj);
                    return true;
                }
            }

            // TODO: Generic

            resolved = null;
            return false;
        }

        private TypeValueReference GetTypeValueReference(ITypeSymbol symbol, IEnumerable<TypeSyntax> typeArguments = null)
        {
            var fullName = symbol.GetFullMetadataName();
            object typeObject;

            if (typeArguments == null) {
                if (!this.TryGetType(fullName, out typeObject)) {
                    throw new EvaluatorException($"Unresolvable type: {fullName}");
                }
            }
            else {
                var types = typeArguments
                    .Select(t => t.Accept(this).Type)
                    .ToArray();

                //Console.WriteLine($"!!  -> types: {string.Join(",",types.Select(t => this.Context.Adapter.GetTypeName(this.Context, t)))}");

                if (!this.TryGetType(fullName, types, out typeObject)) {
                    throw new EvaluatorException($"Unresolvable type: {fullName}");
                }
            }

            return new TypeValueReference(this.Context, typeObject);
        }
    }

    internal static class SymbolExtensions
    {
        public static string GetFullMetadataName(this ISymbol symbol)
            => symbol.ContainingNamespace.IsGlobalNamespace
                ? symbol.MetadataName
                : $"{symbol.ContainingNamespace.GetFullMetadataName()}.{symbol.MetadataName}";
    }
}