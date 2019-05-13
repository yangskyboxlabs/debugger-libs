using System;
using Mono.Debugger.Soft;
using Mono.Debugging.Client;

namespace Mono.Debugging.Soft.util
{
    public static class ModuleMirrorExtension
    {
        public static Guid GetMvidSafe(this ModuleMirror module)
        {
            try
            {
                return module.ModuleVersionId;
            }
            catch (Exception ex)
            {
                DebuggerLoggingService.LogError("Failed to get mvid for module", ex);
                return Guid.Empty;
            }
        }
    }
}