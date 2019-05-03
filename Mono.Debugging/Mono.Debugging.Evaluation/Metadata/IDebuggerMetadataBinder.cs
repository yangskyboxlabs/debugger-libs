using System;

namespace Mono.Debugging.Evaluation.Metadata
{
    public interface IDebuggerMetadataBinder<TType, TMetaType>
        where TMetaType : class
    {
        TMetaType ToMetadataType(EvaluationContext context, TType type);

        TType ToDebuggerType(EvaluationContext context, TMetaType metadataType);
    }
}
