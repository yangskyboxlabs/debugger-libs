using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Mono.Debugger.Soft {

	public sealed class CustomAttributeDataMirror : CustomAttributeData {
		ConstructorInfoMirror ctorInfo;
		IList<CustomAttributeTypedArgument> typedArguments;
		IList<CustomAttributeNamedArgument> namedArguments;

		internal CustomAttributeDataMirror (ConstructorInfoMirror ctorInfo, ImmutableList<CustomAttributeTypedArgument> ctorArgs, ImmutableList<CustomAttributeNamedArgument> namedArgs) : base()
		{
			this.ctorInfo = ctorInfo;
			typedArguments = ctorArgs;
			namedArguments = namedArgs;
		}

		public override ConstructorInfo Constructor => ctorInfo;

		public override IList<CustomAttributeTypedArgument> ConstructorArguments => typedArguments;

		public override IList<CustomAttributeNamedArgument> NamedArguments => namedArguments;

		/* 
		 * Construct a normal object from the value, so accessing the cattr doesn't 
		 * require remoting calls.
		 */
		static CustomAttributeTypedArgument CreateArg (VirtualMachine vm, ValueImpl vi) {
			object val;

			/* Instead of receiving a mirror of the Type object, we receive the id of the type */
			if (vi.Type == (ElementType)ValueTypeId.VALUE_TYPE_ID_TYPE)
				val = vm.GetType (vi.Id);
			else {
				Value v = vm.DecodeValue (vi);
				if (v is PrimitiveValue)
					val = (v as PrimitiveValue).Value;
				else if (v is StringMirror)
					val = (v as StringMirror).Value;
				else
					// FIXME:
					val = v;
			}
			return new CustomAttributeTypedArgument (val);
		}

		internal static CustomAttributeDataMirror[] Create (VirtualMachine vm, CattrInfo[] info) {
			var res = new CustomAttributeDataMirror [info.Length];
			for (int i = 0; i < info.Length; ++i) {
				CattrInfo attr = info [i];
				var ctor = new ConstructorInfoMirror (vm.GetMethod (attr.ctor_id));
				var typedArgs = ImmutableList<CustomAttributeTypedArgument>.Empty
					.AddRange (attr.ctor_args.Select (a => CreateArg (vm, a)));

				var namedArgs = ImmutableList<CustomAttributeNamedArgument>.Empty
					.AddRange (attr.named_args.Select (arg => {
						var val = CreateArg (vm, arg.value);

						var matchingMembers = ctor.DeclaringType.FindMembers (
							MemberTypes.Field | MemberTypes.Property,
							BindingFlags.Instance | BindingFlags.Public,
							(member, id) => (member as IMirrorWithId)?.Id == (long)id,
							arg.id);
						return new CustomAttributeNamedArgument (matchingMembers.First(), val);
					}));

				res [i] = new CustomAttributeDataMirror (ctor, typedArgs, namedArgs);
			}

			return res;
		}
	}

}
