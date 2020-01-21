using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;

namespace Mono.Debugger.Soft
{
	public class ParameterInfoMirror : ParameterInfo, IMirror {
		VirtualMachine vm;

		MethodMirror method;
		TypeMirror type;
		string name;
		int pos;
		ParameterAttributes attrs;

		public VirtualMachine VirtualMachine => vm;

		internal ParameterInfoMirror (MethodMirror method, int pos, TypeMirror type, string name, ParameterAttributes attrs)
		{
			vm = method.VirtualMachine;
			this.method = method;
			this.pos = pos;
			this.type = type;
			this.name = name;
			this.attrs = attrs;
		}

		public override Type ParameterType {
			get {
				return type;
			}
		}

		public override bool HasDefaultValue => false;
		public override object DefaultValue => System.DBNull.Value;

		public MethodMirror Method {
			get {
				return method;
			}
		}

		public override string Name {
			get {
				return name;
			}
		}

		public override int Position {
			get {
				return pos;
			}
		}

		public override ParameterAttributes Attributes {
			get {
				return attrs;
			}
		}

		public override string ToString () {
			return String.Format ("ParameterInfo ({0})", Name);
		}
	}
}