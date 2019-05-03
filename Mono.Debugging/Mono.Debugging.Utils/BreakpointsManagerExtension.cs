using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Debugging.Client;
using Mono.Debugging.Client.Breakpoints;

namespace Mono.Debugging.Mono.Debugging.Utils
{
    public static class BreakpointsManagerExtension
    {
        public static IEnumerable<Catchpoint> GetCatchpoints(this IBreakpointsManager manager)
        {
            return manager.BreakEvents.OfType<Catchpoint>();
        }
    }
}
