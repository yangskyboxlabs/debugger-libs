using System;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation.TypeResolution
{
    public interface ITypeResolver
    {
        string Resolve(EvaluationContext ctx, string identifier, SourceLocation location);
    }
}
