using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation.Roselyn;

namespace Mono.Debugging.Evaluation
{
    public class RoselynExpressionEvaluatorVisitor : CSharpSyntaxVisitor<ValueReference>
    {
        private class ResolutionContext
        {
            public bool WantTypeResult;
            public ValueReference Parent;
        }

        TypeExpressionVisitor typeVisitor;

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

            typeVisitor = new TypeExpressionVisitor (context, semanticModel);
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
            => node.Accept(typeVisitor)?.Type;

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
            //Console.WriteLine($"!! -> {node.Expression.Kind()}");
            //this.ContextStack.Push(new ResolutionContext());
            var r = node.Expression.Accept(this);

            // If regular evaluation returns null, try as type expression
            if (r == null) {
                switch (node.Expression) {
                case MemberAccessExpressionSyntax memberAccess:
                case SimpleNameSyntax name:
                    r = node.Expression.Accept (typeVisitor);
                    break;
                }
            }
            //this.ContextStack.Pop();
            return r;
        }

        public override ValueReference VisitGenericName(GenericNameSyntax node)
        {
            Console.WriteLine($"!! -> generic name: {node.Identifier.ValueText}");

            var typeArguments = node.TypeArgumentList.Arguments
                //.Select(ta => this.ResolveType(ta))
                .Select(ta => ta.Accept (typeVisitor).Type)
                .ToArray();

            var typeName = $"{node.Identifier.ValueText}`{node.Arity}";

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

            return null;
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
            ValueReference resolved;

            Console.WriteLine($"!! -> identifier: {node.ToString()}");

            var symbolInfo = this.SemanticModel.GetSymbolInfo(node);

            if (this.TryResolveAsSymbol(symbolInfo, node, out resolved)) {
                return resolved;
            }

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

            return null;
        }

        public override ValueReference VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (!this.Context.Options.AllowMethodEvaluation) {
                throw new ImplicitEvaluationDisabledException();
            }

            var symbolInfo = SemanticModel.GetSymbolInfo(node);
            Console.WriteLine($"!! -> invocation symbol: {symbolInfo.Symbol?.Kind}");

            object receiver = null;
            object containingType = null;

            var argRefs = node.ArgumentList.Arguments.Select (a => a.Accept (this)).ToList ();
            var argVals = argRefs.Select (a => a.Value).ToArray ();
            var argTypes = argRefs.Select (a => a.Type).ToArray ();

            if (symbolInfo.Symbol?.Kind == SymbolKind.Method) {
                var methodSymbol = (IMethodSymbol)symbolInfo.Symbol;

                Console.WriteLine($"!!  -> static: {methodSymbol.IsStatic}");
                
                if (methodSymbol.IsStatic) {
                    receiver = null;
                    if (methodSymbol.ContainingType != null) {
                        containingType = Context.Adapter.GetType (Context, methodSymbol.ContainingType?.GetFullMetadataName());
                    }
                }
                else {
                    receiver = (node.Expression as MemberAccessExpressionSyntax).Expression
                        .Accept (this)
                        .Value;
                }

                Console.WriteLine($"!!  -> receiver type: {methodSymbol.ReceiverType?.Kind}");

                var receiverType = Context.Adapter.GetType (Context, methodSymbol.ReceiverType.GetFullMetadataName());

                return LiteralValueReference.CreateTargetObjectLiteral(this.Context, "result",
                    Adapter.RuntimeInvoke (Context,
                        receiverType,
                        receiver,
                        methodSymbol.MetadataName,
                        methodSymbol.Parameters.Select(p => Context.Adapter.GetType (Context, p.Type.GetFullMetadataName())).ToArray(),
                        argVals));
            }

            string methodName;
            IEnumerable<object> typeArguments;
            ValueReference parentRef = null;

            switch (node.Expression) {
            case NameSyntax name:
                (methodName, typeArguments) = GetNameInfo (name);
                break;
            case MemberAccessExpressionSyntax memberAccess:
                parentRef = memberAccess.Expression.Accept (this)
                    ?? memberAccess.Expression.Accept (typeVisitor);
                (methodName, typeArguments) = GetNameInfo (memberAccess.Name);
                break;
            default:
                throw new EvaluatorException($"Unexpected invocation on {node} ({node.Expression.Kind()})");
            }

            // Find the method
            var typeArgs = typeArguments?.ToArray ();

            if (parentRef == null) {
                foreach (var (ambientValue, ambientType) in Context.GetAmbientValues ()) {
                    BindingFlags bindingFlags = ambientValue != null ? BindingFlags.Instance : BindingFlags.Static;
                    bindingFlags |= BindingFlags.Public | BindingFlags.NonPublic;

                    // Check for method with matching argument types
                    if (Context.Adapter.HasMethod (Context, ambientType, methodName, typeArgs, argTypes, bindingFlags)) {
                        receiver = ambientValue?.Value;
                        containingType = ambientType;
                        break;
                    }

                    // Find method by member?
                    foreach (var member in Context.Adapter.GetMembers(Context, ambientValue, ambientType, ambientValue?.Value)) {
                        Console.WriteLine($"!!  -> member of ambient: {member}");
                    }

                    // Check for method with matching parameter length
                    /*
                    if (Context.Adapter.HasMethodWithParamLength (Context, ambientType, methodName, bindingFlags, argTypes.Length)) {
                        receiver = ambientValue?.Value;
                        containingType = ambientType;
                        argTypes = null;
                        break;
                    }
                    */
                }
            } else if (parentRef is TypeValueReference) {
                var parentType = parentRef.Type;
                // static method on type

                if (!Context.Adapter.HasMethod (Context, parentType, methodName, typeArgs, argTypes, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                    throw new EvaluatorException($"Could  not find method {methodName} on {Context.Adapter.GetTypeName (Context, parentType)}");
                }
                containingType = parentType;
                receiver = null;
            } else if (parentRef is NamespaceValueReference) {
                throw new EvaluatorException("Could not find class with given method");
            } else {
                receiver = parentRef.Value;
                containingType = parentRef.Type;
            }

            if (containingType == null)
                throw new Exception("Did not resolve containgType");

            Console.WriteLine($"!! invoking ({this.Context.Adapter.GetTypeName(this.Context, containingType)}).{methodName}<{string.Join(",", typeArgs?.Select(t => this.Context.Adapter.GetTypeName(this.Context, t)) ?? Enumerable.Empty<string>())}>({string.Join(",", argVals.Select(a => this.Context.Adapter.GetValueTypeName(this.Context, a)))})");

            var ret = this.Adapter.RuntimeInvoke (Context,
                containingType,
                receiver,
                methodName,
                typeArgs,
                argTypes,
                argVals);

            if (ret == null)
                throw new EvaluatorException($"Could not evaluate {node}");

            return LiteralValueReference.CreateTargetObjectLiteral(this.Context, "result", ret);
        }

        public override ValueReference VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            Console.WriteLine($"!! -> VisitMemberAccess... {node.Name}");
            ValueReference resolved;
            var symbolInfo = this.SemanticModel.GetSymbolInfo(node);

            Console.WriteLine($"!!  -> symbol: {symbolInfo.Symbol?.Kind}");

            var parent = node.Expression.Accept(this);

            if (parent == null) {
                parent = node.Expression.Accept (typeVisitor);

                // Abort here if parent does not resolve to a non-namespace value
                // It will be handled further up the tree as this rewinds
                if (parent == null || parent is NamespaceValueReference)
                    return null;
            }

            Console.WriteLine($"!!  -> parent: {parent.GetType()}");
            switch (parent) {
                case TypeValueReference typeParent:
                    // Static member?
                    if (TryGetMember (parent, node.Name.Identifier.Text, out resolved))
                        return resolved;
                    break;
                default:
                    if (this.TryGetMember(parent, node.Name.Identifier.Text, out resolved)) {
                        return resolved;
                    }
                    break;
            }

            return null;
            //return node.Name.Accept(this);
        }

        public override ValueReference VisitNullableType(NullableTypeSyntax node)
        {
            var innerType = node.ElementType.Accept (typeVisitor);
            if (this.TryGetType("System.Nullable`1", new[] { innerType }, out var type)) {
                return new TypeValueReference(this.Context, type);
            }

            throw new EvaluatorException($"Could not resolve type {node.ToString()}");
        }

        public override ValueReference VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            //var type = this.ResolveType(node.Type);
            var type = node.Type.Accept (typeVisitor).Type;
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
            return node.Accept (typeVisitor);
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
                        case float f: newVal = -f; break;
                        case double d: newVal = -d; break;
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

        public override ValueReference VisitThisExpression(ThisExpressionSyntax node)
        {
            return this.Context.Adapter.GetThisReference(this.Context);
        }

        public override ValueReference VisitTypeOfExpression(TypeOfExpressionSyntax node)
        {
            var typeObj = node.Type.Accept (typeVisitor)?.Type;

            if (typeObj == null)
                throw new EvaluatorException($"Could not resolve '{node.Type.ToString()}' to a known type");

            return LiteralValueReference.CreateTargetObjectLiteral(this.Context,
                node.Type.ToString(),
                this.Context.Adapter.CreateTypeObject(this.Context, typeObj));
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

        private (string name, IEnumerable<object> typeArguments) GetNameInfo (NameSyntax node)
        {
			switch (node) {
			case GenericNameSyntax genericName:
				return GetNameInfo (genericName);
			case IdentifierNameSyntax identifierName:
                return (identifierName.Identifier.Text, null);
			default:
				throw new Exception($"TryResolveName doesn't support {node.Kind()}");
			}
        }
        private (string name, IEnumerable<object> typeArguments) GetNameInfo (GenericNameSyntax node)
        {
			return ($"{node.Identifier.Text}`{node.Arity}",
                node.TypeArgumentList.Arguments.Select (a => a.Accept (typeVisitor).Type));
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