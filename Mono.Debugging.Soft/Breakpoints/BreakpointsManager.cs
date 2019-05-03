using System;
using Mono.Debugger.Soft;
using Mono.Debugging.Client;
using Mono.Debugging.Client.Breakpoints;

namespace Mono.Debugging.Soft.Breakpoints
{
    public class SoftDebuggerBreakpointsManager : BreakpointsManager<AppDomainMirror, AssemblyMirror, ModuleMirror>
    {
        public BreakEventInfo<ModuleMirror> GetBreakEventInfo(EventRequest request)
        {
            BreakEvent breakEvent = GetBreakEvent(request);
            if (breakEvent == null)
                return null;
            return GetBreakEventInfo(breakEvent);
        }

        public BreakEvent GetBreakEvent(EventRequest request)
        {
            return TryGetBreakEvent(new DebuggerBreakEvent<EventRequest>(request));
        }
    }
}
