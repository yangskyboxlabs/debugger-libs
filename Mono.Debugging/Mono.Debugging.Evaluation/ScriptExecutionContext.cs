using System;
using Microsoft.CodeAnalysis;

namespace Mono.Debugging.Evaluation
{
    public class SdbScriptExecutionContext
    {
        public EvaluationContext Context { get; }

        public SdbScriptExecutionContext(EvaluationContext context)
        {
            this.Context = context;
        }

        public ValueReference _Type(string fullName)
        {
            var typeObject = this.Context.Adapter.GetType(this.Context, fullName);
            return new TypeValueReference(this.Context, typeObject);
        }

        public ValueReference _Ns(string name)
            => new NamespaceValueReference(this.Context, name);

        public ValueReference _Member(ValueReference parent, string name)
        {
            switch (parent) {
                case NamespaceValueReference nsRef:
                    return new NamespaceValueReference(this.Context, $"{nsRef.CallToString()}.{name}");
            }

            throw new InvalidOperationException();
        }
    }
}