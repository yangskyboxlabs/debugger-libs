using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace Mono.Debugging.Evaluation
{
    public interface IBindingInfo
    {
        object Target { get; }

        object Type { get; }

        string FullName { get; }

        string MethodName { get; }

        object[] TypeArguments { get; }

        object[] Arguments { get; }

        object[] ArgumentTypes { get; }

        ValueReference ToValueReference();
    }

    public class BindingInfoVisitor : CSharpSyntaxVisitor<IBindingInfo>, IBindingInfo
    {
        public object Target { get; private set; }
        public object Type { get; private set; }
        public string FullName { get; private set; }
        public string MethodName { get; private set; }
        public object[] TypeArguments { get; private set; }
        public object[] Arguments { get; private set; }
        public object[] ArgumentTypes { get; private set; }

        private ValueReference TargetReference;

        public RoselynExpressionEvaluatorVisitor Evaluator { get; }

        public Func<ValueReference, bool> Predicate {get;}

        public EvaluationContext Context => this.Evaluator.Context;

        public BindingFlags BindingFlags { get; }

        public BindingInfoVisitor(RoselynExpressionEvaluatorVisitor evaluator,
            BindingFlags bindingFlags,
            Func<ValueReference, bool> predicate = null)
        {
            this.Evaluator = evaluator;
            this.BindingFlags = bindingFlags;
            this.Predicate = predicate;
        }

        private BindingInfoVisitor ResolvedNamespace(string name)
        {
            this.Target = null;
            this.Type = null;
            this.FullName = name;
            return this;
        }

        private BindingInfoVisitor ResolvedType(object type)
        {
            this.TargetReference = null;
            this.Target = null;
            this.Type = type;
            return this;
        }

        private BindingInfoVisitor ResolvedValue(ValueReference valRef)
        {
            this.TargetReference = valRef;
            this.Target = valRef.Value;
            this.Type = valRef.Type;
            return this;
        }

        private BindingInfoVisitor ResolvedInstanceInvocation(ValueReference targetRef, string methodName)
        {
            this.TargetReference = targetRef;
            return this.ResolvedInstanceInvocation(targetRef.Value, targetRef.Type, methodName);
        }

        private BindingInfoVisitor ResolvedInstanceInvocation(object target, string methodName)
        {
            return this.ResolvedInstanceInvocation(target, null, methodName);
        }

        private BindingInfoVisitor ResolvedInstanceInvocation(object target, object type, string methodName)
        {
            this.Type = type;
            this.Target = target;
            this.MethodName = methodName;
            Console.WriteLine($"!!  -> this.{methodName}({string.Join(",", this.ArgumentTypes.Select(at => this.Context.Adapter.GetTypeName(this.Context, at)))})");
            return this;
        }

        private BindingInfoVisitor ResolvedStaticInvocation(object type, string methodName)
        {
            this.TargetReference = null;
            this.Type = type;
            this.Target = null;
            this.MethodName = methodName;

            Console.WriteLine($"!!  -> static {methodName}({string.Join(",", this.ArgumentTypes.Select(at => this.Context.Adapter.GetTypeName(this.Context, at)))})");
            return this;
        }

        public ValueReference ToValueReference() {
            if (this.TargetReference != null) {
                return this.TargetReference;
            }

            if (this.Type != null) {
                return new TypeValueReference(this.Context, this.Type);
            }

            if (this.FullName != null) {
                return new NamespaceValueReference(this.Context, this.FullName);
            }

            throw new InvalidOperationException();
        }

        public override IBindingInfo VisitArgumentList(ArgumentListSyntax node)
        {
            this.Arguments = node.Arguments.Select(a => a.Expression.Accept(this.Evaluator).Value).ToArray();
            this.ArgumentTypes = this.Arguments.Select(a => this.Context.Adapter.GetValueType(this.Context, a)).ToArray();
            return this;
        }

        public override IBindingInfo VisitGenericName(GenericNameSyntax node)
        {
            var name = node.Identifier.ValueText;
            this.TypeArguments = node.TypeArgumentList.Arguments.Select(ta => this.Evaluator.ResolveType(ta)).ToArray();

            Console.WriteLine($"!!  -> type arguments for {name}: {string.Join(",", this.TypeArguments.Select(ta => this.Context.Adapter.GetTypeName(this.Context, ta)))}");

            if (this.BindingFlags.HasFlag(BindingFlags.Instance)) {
                var self = this.Context.Adapter.GetThisReference(this.Context);

                if (this.BindingFlags.HasFlag(BindingFlags.InvokeMethod)) {
                    if (self != null && this.Context.Adapter.HasMethod(this.Context, self.Type, name, this.TypeArguments, this.ArgumentTypes, BindingFlags.Instance)) {
                        Console.WriteLine($"!!  -> found method on this");
                        return this.ResolvedInstanceInvocation(self, name);
                    }
                }
            }

            if (this.BindingFlags.HasFlag(BindingFlags.Static)) {
                if (this.BindingFlags.HasFlag(BindingFlags.InvokeMethod)) {
                    throw new NotSupportedException();
                }
                else {
                    var type = this.Context.Adapter.GetType(this.Context, $"{name}`{this.TypeArguments.Length}", this.TypeArguments);
                    if (type != null) {
                        return this.ResolvedType(type);
                    }

                    string fullName;
                    foreach (var ns in this.Context.Adapter.GetImportedNamespaces(this.Context).Concat(new[] { "System.Collections.Generic" })) {
                        fullName = $"{ns}.{name}";
                        Console.WriteLine($"!!  -> trying {fullName}...");
                        type = this.Context.Adapter.GetType(this.Context, $"{fullName}`{this.TypeArguments.Length}", this.TypeArguments);
                        if (type != null) {
                            Console.WriteLine($"!!  -> found type {fullName}<>");
                            return this.ResolvedType(type);
                        }
                    }
                }
            }

            Console.WriteLine($"!!  -> could not resolve {name}<>");
            throw new Exception($"Could not find {name}<> on this to invoke");
        }

        public override IBindingInfo VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.ValueText;
            Console.WriteLine($"!! -> Resolving '{name}'...");

            // If target reference exists while resolving invocation, only look for instance
            // methods on said target
            if (this.TargetReference != null && this.BindingFlags.HasFlag(BindingFlags.InvokeMethod)) {
                if (!this.Context.Adapter.HasMethod(this.Context, this.TargetReference.Type, name, this.BindingFlags)) {
                    throw new Exception($"Could not resolve {name} on type {this.TargetReference.Type}");
                }

                switch (this.TargetReference) {
                    case NamespaceValueReference namespaceRef:
                        throw new Exception($"Unexpected static method invocation with namespace target");
                    case TypeValueReference typeRef:
                        return this.ResolvedStaticInvocation(typeRef.Type, name);
                    default:
                        return this.ResolvedInstanceInvocation(this.TargetReference, name);
                }

            }

            // Try user defined variables
            if (this.Evaluator.UserVariables.TryGetValue(name, out var valueRef)) {
                return this.ResolvedValue(valueRef);
            }

            // Try local variables
            valueRef = this.Context.Adapter.GetLocalVariable(this.Context, name);
            if (valueRef != null) {
                return this.ResolvedValue(valueRef);
            }

            // Try parameters
            valueRef = this.Context.Adapter.GetParameter(this.Context, name);
            if (valueRef != null) {
                return this.ResolvedValue(valueRef);
            }

            if (this.BindingFlags.HasFlag(BindingFlags.Instance)) {
                var selfRef = this.Context.Adapter.GetThisReference(this.Context);

                if (selfRef != null) {
                    if (this.BindingFlags.HasFlag(BindingFlags.InvokeMethod)) {
                        // Try current type first
                        var enclosingType = this.Context.Adapter.GetEnclosingType(this.Context);
                        if (this.Context.Adapter.HasMethod(this.Context, enclosingType, name, this.TypeArguments, this.ArgumentTypes, BindingFlags.Instance)) {
                            return this.ResolvedInstanceInvocation(selfRef.Value, enclosingType, name);
                        }

                        // XXX: Bug? HasMethod finds static method when using BindingFlags.Instance. 
                        // XXX: Need to explicitly check if a static method with the same name exists
                        if (this.Context.Adapter.HasMethod(this.Context, selfRef.Type, name, BindingFlags.Static)) {
                            return this.ResolvedStaticInvocation(selfRef.Type, name);
                        }

                        if (this.Context.Adapter.HasMethod(this.Context, selfRef.Type, name, BindingFlags.Instance)) {
                            return this.ResolvedInstanceInvocation(selfRef, name);
                        }
                    }
                    else {
                        var memberRef = this.Context.Adapter.GetMember(this.Context, null, selfRef.Value, name);
                        if (memberRef != null) {
                            return this.ResolvedValue(memberRef);
                        }
                    }
                }
            }

            if (this.BindingFlags.HasFlag(BindingFlags.Static)) {
                if (this.BindingFlags.HasFlag(BindingFlags.InvokeMethod)) {
                    Console.WriteLine($"!!  -> trying to find {name} in enclosing context and parents");
                    var vtype = this.Context.Adapter.GetEnclosingType(this.Context);
                    while (vtype != null) {
                        if (this.Context.Adapter.HasMethod(this.Context,
                            vtype,
                            name,
                            this.TypeArguments,
                            this.ArgumentTypes,
                            BindingFlags.Static)) {
                                Console.WriteLine($"!!  -> found static {this.MethodName} in enclosing");
                                return this.ResolvedStaticInvocation(vtype, name);
                            }
                        vtype = this.Context.Adapter.GetParentType(this.Context, vtype);
                    }
                }
                else {
                    var enclosingType = this.Context.Adapter.GetEnclosingType(this.Context);
                    var memberRef = this.Context.Adapter.GetMember(this.Context, null, null, enclosingType, name);
                    if (memberRef != null) {
                        return this.ResolvedValue(memberRef);
                    }
                }

                var fullName = name;

                var type = this.Context.Adapter.GetType(this.Context, name);
                if (type != null) {
                    Console.WriteLine($"!!  -> resolved {name} to type");
                    return this.ResolvedType(type);
                }

                if (type == null) {
                    foreach (var ns in this.Context.Adapter.GetImportedNamespaces(this.Context)) {
                        fullName = $"{ns}.{name}";
                        type = this.Context.Adapter.GetType(this.Context, fullName);
                        if (type != null) {
                            Console.WriteLine($"!!  -> found type {fullName}");
                            return this.ResolvedType(type);
                        }

                        if (ns.Split('.').Contains(name)) {
                            return this.ResolvedNamespace(name);
                        }
                    }
                }
            }

            throw new Exception($"Could not resolve {name}");
        }

        public override IBindingInfo VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            node.ArgumentList.Accept(this);

            if (!this.BindingFlags.HasFlag(BindingFlags.InvokeMethod)) {
                throw new Exception("Saw invocation expression but InvokeMethod binding flag is not set");
            }

            switch (node.Expression) {
                case GenericNameSyntax genericName:
                    genericName.Accept(this);
                    break;
                case IdentifierNameSyntax identifier:
                    identifier.Accept(this);
                    break;
                case MemberAccessExpressionSyntax memberAccess:
                    this.TargetReference = memberAccess.Expression.Accept(this.Evaluator);
                    if (this.TargetReference is TypeValueReference || this.TargetReference is NamespaceValueReference) {
                        this.Type = this.TargetReference.Type;
                        this.Target = null;
                    }
                    memberAccess.Name.Accept(this);
                    break;
            }
            return this;
        }

        public override IBindingInfo VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            this.Target = node.Expression.Accept(this.Evaluator);
            this.MethodName = node.Name.Identifier.ValueText;
            return this;
        }

        public override IBindingInfo VisitPredefinedType(PredefinedTypeSyntax node)
        {
            var name = node.Keyword.ValueText;
            if (!PredefinedTypesMap.TryGetValue(node.Keyword.ValueText, out var fullName)) {
                var t = CSharpScript.EvaluateAsync<string>($"typeof({name}).FullName");
                t.Wait();
                fullName = t.Result;
                PredefinedTypesMap[node.Keyword.ValueText] = fullName;
            }

            this.Type = this.Context.Adapter.GetType(this.Context, fullName);
            this.FullName = fullName;
            return this;
        }

        private static Dictionary<string, string> PredefinedTypesMap = new Dictionary<string, string>();


        public override IBindingInfo VisitTypeArgumentList(TypeArgumentListSyntax node)
        {
            this.TypeArguments = node.Arguments.Select(ta => this.Evaluator.ResolveType(ta)).ToArray();
            return this;
        }
    }
}