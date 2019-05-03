using System;

namespace Mono.Debugging.Client.Breakpoints
{
    public interface IBindingBreakEvent<TModule>
    {
        IDebuggerBreakEvent DebuggerBreakEvent { get; }

        bool Enabled { get; set; }

        bool IsInternalBreakevent { get; }

        bool Equals(IBindingBreakEvent<TModule> bindingBreakEvent);

        bool ModuleEquals(TModule module);
    }
}
