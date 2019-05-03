using System;
using Mono.Debugger.Soft;
using Mono.Debugging.Evaluation;

namespace Mono.Debugging.Soft
{
    public class SoftSpecialSymbolHelper : SpecialSymbolHelper<TypeMirror, FieldInfoMirror, LocalVariable>
    {
        public override string GetTypeName(TypeMirror type)
        {
            return type.Name;
        }
    }
}
