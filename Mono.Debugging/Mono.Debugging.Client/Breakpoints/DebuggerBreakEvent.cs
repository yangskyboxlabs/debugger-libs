using System;
using System.Collections.Generic;

namespace Mono.Debugging.Client.Breakpoints
{
    public class DebuggerBreakEvent<T> : IDebuggerBreakEvent
    {
        readonly T myDebuggerBreakEvent;

        public DebuggerBreakEvent(T debuggerBreakEvent)
        {
            myDebuggerBreakEvent = debuggerBreakEvent;
        }

        public override bool Equals(object obj)
        {
            if (obj is DebuggerBreakEvent<T> debuggerBreakEvent)
                return DebuggerBreakEventEquals(debuggerBreakEvent.myDebuggerBreakEvent);
            return false;
        }

        public bool Equals(IDebuggerBreakEvent other)
        {
            return Equals((object)other);
        }

        bool DebuggerBreakEventEquals(T other)
        {
            return EqualityComparer<T>.Default.Equals(this.myDebuggerBreakEvent, other);
        }
    }
}
