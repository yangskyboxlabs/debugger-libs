using System;

namespace Mono.Debugging.Evaluation
{
    public abstract class SpecialSymbolHelper<TDebuggerType, TDebuggerField, TDebuggerLocal>
    {
        public virtual bool IsGeneratedType(TDebuggerType type)
        {
            return IsGeneratedTypeName(GetTypeName(type));
        }

        public static bool IsGeneratedTypeName(string typeName)
        {
            if (typeName.IndexOf(">c__", StringComparison.Ordinal) <= 0)
                return typeName.IndexOf(">d", StringComparison.Ordinal) > 0;
            return true;
        }

        public abstract string GetTypeName(TDebuggerType type);
    }
}
