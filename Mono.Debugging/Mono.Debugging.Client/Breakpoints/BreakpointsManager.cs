using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Util.Concurrency;

namespace Mono.Debugging.Client.Breakpoints
{
    public abstract class BreakpointsManager<TAppDomain, TAssembly, TModule> : IBreakpointsManager
    {
        readonly ReaderWriterLockSlim myBreakEventsLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        readonly Dictionary<IDebuggerBreakEvent, BreakEvent> myDebuggerBreakEvents = new Dictionary<IDebuggerBreakEvent, BreakEvent>();
        readonly Dictionary<BreakEvent, BreakEventInfo<TModule>> myBreakEvents = new Dictionary<BreakEvent, BreakEventInfo<TModule>>();

        public ICollection<BreakEvent> BreakEvents
        {
            get
            {
                using (myBreakEventsLock.UsingReadLock())
                    return myBreakEvents.Keys.ToArray();
            }
        }

        public BreakEventInfo<TModule> GetBreakEventInfo(BreakEvent breakEvent)
        {
            myBreakEventsLock.EnterReadLock();
            using (new DisposableReadLock(myBreakEventsLock))
            {
                return myBreakEvents.TryGetValue(breakEvent, out var breakEventInfo) ? breakEventInfo : null;
            }
        }

        protected BreakEvent TryGetBreakEvent(IDebuggerBreakEvent debuggerBreakEvent)
        {
            using (myBreakEventsLock.UsingReadLock())
            {
                BreakEvent breakEvent;
                return this.myDebuggerBreakEvents.TryGetValue(debuggerBreakEvent, out breakEvent) ? breakEvent : (BreakEvent)null;
            }
        }
    }
}
