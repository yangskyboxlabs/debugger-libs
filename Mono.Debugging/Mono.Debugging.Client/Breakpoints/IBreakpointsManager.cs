using System;
using System.Collections.Generic;

namespace Mono.Debugging.Client.Breakpoints
{
    public interface IBreakpointsManager
    {
        ICollection<BreakEvent> BreakEvents { get; }
    }
}
