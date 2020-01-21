using System;
using Mono.Debugger;

namespace Mono.Debugger.Soft
{
	public class ModuleMirror : System.Reflection.Module, IMirror
	{
		VirtualMachine vm;
		long id;

		ModuleInfo info;
		Guid guid;
		AssemblyMirror assembly;

		internal ModuleMirror (VirtualMachine vm, long id)
		{
			this.vm = vm;
			this.id = id;
		}

		public VirtualMachine VirtualMachine => vm;
		public long Id => id;

		void ReadInfo ()
		{
			if (info == null)
				info = vm.conn.Module_GetInfo (id);
		}

		public override string Name {
			get {
				ReadInfo ();
				return info.Name;
			}
		}

		public override string ScopeName {
			get {
				ReadInfo ();
				return info.ScopeName;
			}
		}

		public override string FullyQualifiedName {
			get {
				ReadInfo ();
				return info.FQName;
			}
		}

		public override Guid ModuleVersionId {
			get {
				if (guid == Guid.Empty) {
					ReadInfo ();
					guid = new Guid (info.Guid);
				}
				return guid;
			}
		}

		public override System.Reflection.Assembly Assembly {
			get {
				if (assembly == null) {
					ReadInfo ();
					if (info.Assembly == 0)
						return null;
					assembly = vm.GetAssembly (info.Assembly);
				}
				return assembly;
			}
		}

		// FIXME: Add function to query the guid, check in Metadata

		// Since protocol version 2.48
		public string SourceLink {
			get {
				vm.CheckProtocolVersion (2, 48);
				ReadInfo ();
				return info.SourceLink;
			}
		}
	}
}