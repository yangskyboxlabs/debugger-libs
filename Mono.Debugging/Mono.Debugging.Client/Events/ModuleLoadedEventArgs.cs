using System;
using System.Reflection;

namespace Mono.Debugging.Client.Events
{
    public class ModuleLoadedEventArgs
    {
        public Guid Mvid { get; }

        public AssemblyName AssemblyName { get; }

        public string Location { get; }

        public string AppDomainName { get; }

        public ModuleLoadedEventArgs(
            AssemblyName assemblyName,
            string location,
            string appDomainName,
            Guid mvid)
        {
            this.AssemblyName = assemblyName;
            this.Location = location;
            this.AppDomainName = appDomainName;
            this.Mvid = mvid;
        }
    }
}