using System;
using Mono.Debugger.Soft;
using Mono.Debugging.Client.Breakpoints;
using Mono.Debugging.Mono.Debugging.Utils;

namespace Mono.Debugging.Soft.Breakpoints
{
    public abstract class SoftDebuggerBindingRequestBase : IBindingBreakEvent<ModuleMirror>
    {
        readonly EventRequest myRequest;
        readonly SoftDebuggerSession mySession;
        IDebuggerBreakEvent myBreakEvent;

        IDebuggerBreakEvent IBindingBreakEvent<ModuleMirror>.DebuggerBreakEvent =>
            myBreakEvent ?? (myBreakEvent = new DebuggerBreakEvent<EventRequest>(myRequest));

        public bool Enabled
        {
            get => myRequest.Enabled;
            set
            {
                myRequest.Enabled = value;
                if (value)
                    return;
                mySession.RemoveQueuedBreakEvents(myRequest.WrapInArray());
            }
        }

        public abstract bool IsInternalBreakevent { get; }

        protected SoftDebuggerBindingRequestBase(EventRequest request, SoftDebuggerSession session)
        {
            myRequest = request;
            mySession = session;
        }

        public abstract bool Equals(IBindingBreakEvent<ModuleMirror> bindingBreakEvent);

        public abstract bool ModuleEquals(ModuleMirror module);
    }
}
