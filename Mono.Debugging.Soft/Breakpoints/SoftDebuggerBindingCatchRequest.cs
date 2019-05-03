using System;
using Mono.Debugger.Soft;
using Mono.Debugging.Client.Breakpoints;

namespace Mono.Debugging.Soft.Breakpoints
{
    public class SoftDebuggerBindingCatchpointRequest : SoftDebuggerBindingRequestBase
    {
        public TypeMirror Type { get; }

        public override bool IsInternalBreakevent
        {
            get { return true; }
        }

        public SoftDebuggerBindingCatchpointRequest(
            EventRequest request,
            TypeMirror type,
            SoftDebuggerSession session)
            : base(request, session)
        {
            Type = type;
        }

        public override bool Equals(IBindingBreakEvent<ModuleMirror> bindingBreakEvent)
        {
            if (bindingBreakEvent is SoftDebuggerBindingCatchpointRequest catchpointRequest)
                return Type.Id == catchpointRequest.Type.Id;
            return false;
        }

        public override bool ModuleEquals(ModuleMirror module)
        {
            return module.Id == Type.Module.Id;
        }
    }
}
