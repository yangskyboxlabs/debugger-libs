using System;

namespace Mono.Debugging.Backend
{
    public interface IDebuggerHierarchicalObject
    {
        IDebuggerHierarchicalObject ParentSource { get; }

        string Name { get; }
    }
}
