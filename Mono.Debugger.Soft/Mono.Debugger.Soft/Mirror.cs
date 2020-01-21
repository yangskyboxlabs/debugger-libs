using System;

namespace Mono.Debugger.Soft
{
	public abstract class Mirror : IMirror
	{
		protected VirtualMachine vm;
		protected long id; // The id used in the protocol

		internal Mirror (VirtualMachine vm, long id) {
			this.vm = vm;
			this.id = id;
		}

		internal Mirror () {
		}

		public VirtualMachine VirtualMachine {
			get {
				return vm;
			}
		}

		public long Id {
			get {
				return id;
			}
		}
	}

	public static class MirrorExtensions {
		public static void AssertSameVm (this IMirror self, IMirror other)
			=> self.VirtualMachine?.AssertSameVm (other);
	}
}
