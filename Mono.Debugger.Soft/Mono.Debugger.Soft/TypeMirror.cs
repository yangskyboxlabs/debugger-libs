using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using C = Mono.Cecil;
using Mono.Cecil.Metadata;
using System.Threading.Tasks;
using System.Globalization;

namespace Mono.Debugger.Soft
{
	/*
	 * Represents a type in the remote virtual machine.
	 * It might be better to make this a subclass of Type, but that could be
	 * difficult as some of our methods like GetMethods () return Mirror objects.
	 */
	public class TypeMirror : System.Type, IMirrorWithId
	{
		VirtualMachine vm;
		long id;

		//MethodMirror[] methods;
		ImmutableArray<MethodMirror> methodImpls;
		MethodInfoMirror[] methods;
		ConstructorInfoMirror[] ctors;
		AssemblyMirror ass;
		ModuleMirror module;
		C.TypeDefinition meta;
		FieldInfoMirror[] fields;
		PropertyInfoMirror[] properties;
		TypeInfo info;
		TypeMirror base_type, element_type, gtd;
		TypeMirror[] nested;
		CustomAttributeDataMirror[] cattrs;
		TypeMirror[] ifaces;
		Dictionary<TypeMirror, InterfaceMapping> iface_map;
		TypeMirror[] type_args;
		bool cached_base_type;
		bool inited;

		internal const BindingFlags DefaultBindingFlags =
		BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;

		public VirtualMachine VirtualMachine => vm;
		public long Id => id;

		internal TypeMirror (VirtualMachine vm, long id)
		{
			this.vm = vm;
			this.id = id;
		}

		public override string AssemblyQualifiedName => throw new NotImplementedException ();
		public override Guid GUID => throw new NotImplementedException ();

		public override bool IsGenericType => GetInfo ().is_generic_type;

		public override bool IsGenericTypeDefinition => GetInfo ().is_gtd;

		public override string Name {
			get {
				return GetInfo ().name;
			}
		}

		public override string Namespace {
			get {
				return GetInfo ().ns;
			}
		}

		public override Assembly Assembly {
			get {
				if (ass == null) {
					ass = vm.GetAssembly (GetInfo ().assembly);
				}
				return ass;
			}
		}

		public override Module Module {
			get {
				if (module == null) {
					module = vm.GetModule (GetInfo ().module);
				}
				return module;
			}
		}

		public override int MetadataToken {
			get {
				return GetInfo ().token;
			}
		}

		public override Type BaseType {
			get {
				if (!cached_base_type) {
					base_type = vm.GetType (GetInfo ().base_type);
					cached_base_type = true;
				}
				return base_type;
			}
		}

		public override Type UnderlyingSystemType => Type.GetType (FullName);

		public override bool Equals(Type o)
		{
			var other = o as TypeMirror;
			if (other == null)
				return false;

			return vm == other.vm && id == other.id;
		}

		public override int GetArrayRank () {
			GetInfo ();
			if (info.rank == 0)
				throw new ArgumentException ("Type must be an array type.");
			return info.rank;
		}

		public override Type GetElementType () {
			GetInfo ();
			if (element_type == null && info.element_type != 0)
				element_type = vm.GetType (info.element_type);
			return element_type;
		}

		public override Type GetGenericTypeDefinition () {
			vm.CheckProtocolVersion (2, 12);
			GetInfo ();
			if (gtd == null) {
				if (info.gtd == 0)
					throw new InvalidOperationException ();
				gtd = vm.GetType (info.gtd);
			}
			return gtd;
		}

		// Since protocol version 2.15
		public override Type[] GetGenericArguments () {
			vm.CheckProtocolVersion (2, 15);
			if (type_args == null)
				type_args = vm.GetTypes (GetInfo ().type_args);
			return type_args;
		}

		public override Type MakeGenericType(params Type[] typeArguments)
		{
			throw new NotImplementedException("Cannot make generic type from only TypeMirror of generic definition. Construct using the SDB session instead.");
		}

		public override string FullName {
			get {
				return GetInfo ().full_name;
			}
		}

		public string CSharpName {
			get {
				if (IsArray) {
					if (GetArrayRank () == 1)
						return ((TypeMirror)GetElementType ()).CSharpName + "[]";
					else {
						string ranks = "";
						for (int i = 0; i < GetArrayRank (); ++i)
							ranks += ',';
						return ((TypeMirror)GetElementType ()).CSharpName + "[" + ranks + "]";
					}
				}
				if (IsPrimitive) {
					switch (Name) {
					case "Byte":
						return "byte";
					case "Sbyte":
						return "sbyte";
					case "Char":
						return "char";
					case "UInt16":
						return "ushort";
					case "Int16":
						return "short";
					case "UInt32":
						return "uint";
					case "Int32":
						return "int";
					case "UInt64":
						return "ulong";
					case "Int64":
						return "long";
					case "Single":
						return "float";
					case "Double":
						return "double";
					case "Boolean":
						return "bool";
					default:
						return FullName;
					}
				}
				// FIXME: Only do this for real corlib types
				if (Namespace == "System") {
					string s = Name;
					switch (s) {
					case "Decimal":
						return "decimal";
					case "Object":
						return "object";
					case "String":
						return "string";
					default:
						return FullName;
					}
				} else {
					return FullName;
				}
			}
		}

		public override System.Reflection.MethodInfo[] GetMethods (BindingFlags bindingAttr)
		{
			return GetMethodMirrors (bindingAttr)
				.Select(m => new MethodInfoMirror (m))
				.ToArray();
		}

		public IEnumerable<MethodMirror> GetMethodMirrors (BindingFlags bindingAttr)
		{
			TryInitMethods ();

			var matched = methodImpls
				.Where(method => {
					if (method.IsStatic && !bindingAttr.HasFlag (BindingFlags.Static))
						return false;
					if (!method.IsStatic && !bindingAttr.HasFlag (BindingFlags.Instance))
						return false;
					if (method.IsPublic && !bindingAttr.HasFlag (BindingFlags.Public))
						return false;
					if (method.IsPrivate && !bindingAttr.HasFlag (BindingFlags.NonPublic))
						return false;

					return true;
				});

			if (BaseType != null && bindingAttr.HasFlag(BindingFlags.FlattenHierarchy))
				matched = matched.Concat (((TypeMirror) BaseType).GetMethodMirrors (bindingAttr));

			return matched;
		}

		private void TryInitMethods()
		{
			if (methodImpls != default)
				return;

			long[] ids = vm.conn.Type_GetMethods (id);

			methodImpls = ImmutableArray<MethodMirror>.Empty
				.AddRange (ids.Select (id => vm.GetMethod (id)).Where (m => m != null));

			methods = methodImpls
				.Where (m => !m.IsConstructor)
				.Select (m => new MethodInfoMirror (m))
				.ToArray();

			ctors = methodImpls
				.Where (m => m.IsConstructor)
				.Select (m => new ConstructorInfoMirror (m))
				.ToArray();
		}

		public override FieldInfo GetField(string name, BindingFlags bindingAttr)
		{
			InitFields ();

			FieldInfo field = fields.FirstOrDefault (m => m.Name == name);
			Console.WriteLine($"!!!  -> TypeMirror.GetField: trying '{Name}.{name}'; found '{field?.Name}'");

			if (field != null
				&& (field.IsStatic
					? bindingAttr.HasFlag(BindingFlags.Static)
					: bindingAttr.HasFlag(BindingFlags.Instance))
				&& (field.IsPublic
					? bindingAttr.HasFlag(BindingFlags.Public)
					: bindingAttr.HasFlag(BindingFlags.NonPublic)))
					return field;

			if (!bindingAttr.HasFlag(BindingFlags.DeclaredOnly)) {
				for (var type = (TypeMirror)BaseType; type != null; type = (TypeMirror)type.BaseType) {
					type.InitFields ();
					field = type.fields.FirstOrDefault (m => m.Name == name);

					if (field == null || field.IsPrivate)
						continue;

					if (field.IsStatic) {
						if (bindingAttr.HasFlag(BindingFlags.FlattenHierarchy | BindingFlags.Static)
							&& ((field.IsPublic && bindingAttr.HasFlag(BindingFlags.Public))
								|| (field.IsFamily && bindingAttr.HasFlag(BindingFlags.NonPublic))))
						return field;
					} else {
						if (bindingAttr.HasFlag(BindingFlags.Instance)
							&& ((field.IsPublic && bindingAttr.HasFlag(BindingFlags.Public))
								|| (field.IsFamily && bindingAttr.HasFlag(BindingFlags.NonPublic))))
						return field;
					}
				}
			}

			return null;
		}

		public override System.Reflection.FieldInfo[] GetFields (BindingFlags bindingAttr)
		{
			InitFields ();

			var matched = fields
				.Where(field => {
					if (field.IsStatic && !bindingAttr.HasFlag (BindingFlags.Static))
						return false;
					if (!field.IsStatic && !bindingAttr.HasFlag (BindingFlags.Instance))
						return false;

					if (bindingAttr.HasFlag (BindingFlags.Public) && field.IsPublic)
						return true;
					if (bindingAttr.HasFlag (BindingFlags.NonPublic)
						&& (field.IsPrivate || field.IsFamily))
						return true;

					return false;
				});

			if (!bindingAttr.HasFlag(BindingFlags.DeclaredOnly)) {
				for (var type = (TypeMirror)BaseType; type != null; type = (TypeMirror)type.BaseType) {
					type.InitFields ();

					foreach (var field in type.fields) {
						if (field.IsPrivate)
							continue;

						if (field.IsStatic) {
							if (bindingAttr.HasFlag(BindingFlags.FlattenHierarchy | BindingFlags.Static)
								&& ((field.IsPublic && bindingAttr.HasFlag(BindingFlags.Public))
									|| (field.IsFamily && bindingAttr.HasFlag(BindingFlags.NonPublic))))
							matched.Append(field);
						} else {
							if (bindingAttr.HasFlag(BindingFlags.Instance)
								&& ((field.IsPublic && bindingAttr.HasFlag(BindingFlags.Public))
									|| (field.IsFamily && bindingAttr.HasFlag(BindingFlags.NonPublic))))
							matched.Append(field);
						}
					}
				}
			}

			return matched.ToArray ();
		}

		void InitFields ()
		{
			if (fields != null)
				return;

			var ids = vm.conn.Type_GetFields (id, out var names, out var types, out var attrs);

			fields = new FieldInfoMirror [ids.Length];
			for (int i = 0; i < fields.Length; ++i)
				fields [i] = new FieldInfoMirror (this, ids [i], names [i], vm.GetType (types [i]), (FieldAttributes)attrs [i]);
		}

		public override int GetHashCode()
			=> (int) id;

		public override Type GetNestedType(string name, BindingFlags bindingAttr)
		{
			return GetNestedTypes (bindingAttr)
				.Where (t => t.Name == name)
				.SingleOrDefault ();
		}

		public override Type[] GetNestedTypes (BindingFlags bindingAttr) {
			if (nested == null) {
				GetInfo ();
				var arr = new TypeMirror [info.nested.Length];
				for (int i = 0; i < arr.Length; ++i)
					arr [i] = vm.GetType (info.nested [i]);
				nested = arr;
			}

			return nested
				.Where(nested => {
					if (!bindingAttr.HasFlag(BindingFlags.Public) && nested.IsPublic)
						return false;
					if (!bindingAttr.HasFlag(BindingFlags.NonPublic) && !nested.IsPublic)
						return false;

					return true;
				})
				.ToArray();
		}

		public override PropertyInfo[] GetProperties (BindingFlags bindingAttr)
		{
			InitProperties ();

			var matched = properties
				.Where(prop => {
					if (!prop.CanRead)
						return false;

					var getter = prop.GetMethod;

					if (getter.IsStatic && !bindingAttr.HasFlag (BindingFlags.Static))
						return false;
					if (!getter.IsStatic && !bindingAttr.HasFlag (BindingFlags.Instance))
						return false;

					if (bindingAttr.HasFlag (BindingFlags.Public) && getter.IsPublic)
						return true;
					if (bindingAttr.HasFlag (BindingFlags.NonPublic)
						&& (getter.IsPrivate || getter.IsFamily))
						return true;

					return false;
				});

			if (!bindingAttr.HasFlag(BindingFlags.DeclaredOnly)) {
				for (var type = (TypeMirror)BaseType; type != null; type = (TypeMirror)type.BaseType) {
					type.InitProperties ();

					foreach (var prop in type.properties) {
						var getter = prop.GetMethod;
						var setter = prop.SetMethod;
						var isPublic = (getter?.IsPublic ?? false) || (setter?.IsPublic ?? false);
						var isPrivate = (getter?.IsPrivate ?? true) && (setter?.IsPrivate ?? true);
						var isFamily = (getter?.IsFamily ?? false) || (setter?.IsFamily ?? false);
						var isStatic = (getter?.IsStatic ?? false) || (setter?.IsStatic ?? false);

						if (isPrivate)
							continue;

						if (isStatic) {
							if (bindingAttr.HasFlag(BindingFlags.FlattenHierarchy | BindingFlags.Static)
								&& (isPublic || (isFamily && bindingAttr.HasFlag(BindingFlags.NonPublic))))
							matched.Append(prop);
						} else {
							if (bindingAttr.HasFlag(BindingFlags.Instance)
								&& ((isPublic && bindingAttr.HasFlag(BindingFlags.Public))
									|| (isFamily && bindingAttr.HasFlag(BindingFlags.NonPublic))))
							matched.Append(prop);
						}
					}
				}
			}

			return matched.ToArray ();
		}

		protected override PropertyInfo GetPropertyImpl(string name, BindingFlags bindingAttr, Binder binder, Type returnType, Type[] types, ParameterModifier[] modifiers)
		{
			InitProperties ();

			var matches = properties
				.Where (p => {
					if (p.Name != name)
						return false;
					var getter = p.GetMethod;
					var setter = p.SetMethod;

					var isStatic = getter?.IsStatic ?? setter?.IsStatic ?? false;
					if (!(isStatic
						? bindingAttr.HasFlag(BindingFlags.Static)
						: bindingAttr.HasFlag(BindingFlags.Instance)))
						return false;

					var isPublic = getter?.IsPublic ?? setter?.IsPublic ?? false;
					if (!(isPublic
						? bindingAttr.HasFlag(BindingFlags.Public)
						: bindingAttr.HasFlag(BindingFlags.NonPublic)))
						return false;

					return true;
				});

			if (!bindingAttr.HasFlag(BindingFlags.DeclaredOnly)) {
				for (var type = (TypeMirror) BaseType; type != null; type = (TypeMirror) type.BaseType) {
					type.InitProperties ();
					Console.WriteLine($"!!!   -> TypeMirror.GetProperty: trying base type {type.FullName}");

					var parentMatches = type.properties
						.Where (p => {
							if (p.Name != name)
								return false;

							var getter = p.GetMethod;
							var setter = p.SetMethod;

							if ((getter?.IsPrivate ?? true) && (setter?.IsPrivate ?? true))
								return false;

							var isStatic = getter?.IsStatic ?? setter?.IsStatic ?? false;
							if (isStatic && !bindingAttr.HasFlag(BindingFlags.Static | BindingFlags.FlattenHierarchy))
								return false;

							return true;
						});

					matches = matches.Concat(parentMatches);
				}
			}

			var results = matches.ToList();
			Console.WriteLine($"!!!  -> TypeMirror.GetPropertyImpl: trying {name}; found {results.Count} matches");

			if (results.Count == 0)
				return null;
			else if (results.Count == 1)
				return results[0];
			else {
				var mine = results.Where(r => r.DeclaringType.Equals(this)).ToList();
				if (mine.Count == 1)
					return mine[0];

				throw new AmbiguousMatchException();
			}
		}

		void InitProperties ()
		{
			if (properties != null)
				return;

			PropInfo[] info = vm.conn.Type_GetProperties (id);
			PropertyInfoMirror[] res = new PropertyInfoMirror [info.Length];
			for (int i = 0; i < res.Length; ++i)
				res [i] = new PropertyInfoMirror (this, info [i].id, info [i].name,
					(MethodMirror) vm.GetMethod (info [i].get_method),
					(MethodMirror) vm.GetMethod (info [i].set_method),
					(PropertyAttributes) info [i].attrs);

			properties = res;
		}

		public virtual bool IsAssignableFrom (TypeMirror c) {
			if (c == null)
				throw new ArgumentNullException ("c");

			this.AssertSameVm (c);

			// This is complex so do it in the debuggee
			return vm.conn.Type_IsAssignableFrom (id, c.Id);
		}

		public Value GetValue (FieldInfoMirror field) {
			return GetValues (new FieldInfoMirror [] { field }) [0];
		}

		public Value[] GetValues (IList<FieldInfoMirror> fields, ThreadMirror thread) {
			if (fields == null)
				throw new ArgumentNullException ("fields");
			foreach (FieldInfoMirror f in fields) {
				if (f == null)
					throw new ArgumentNullException ("field");
				this.AssertSameVm (f);
			}
			long[] ids = new long [fields.Count];
			for (int i = 0; i < fields.Count; ++i)
				ids [i] = fields [i].Id;
			try {
				return vm.DecodeValues (vm.conn.Type_GetValues (id, ids, thread !=  null ? thread.Id : 0));
			} catch (CommandException ex) {
				if (ex.ErrorCode == ErrorCode.INVALID_FIELDID)
					throw new ArgumentException ("One of the fields is not valid for this type.", "fields");
				else
					throw;
			}
		}

		public Value[] GetValues (IList<FieldInfoMirror> fields) {
			return GetValues (fields, null);
		}

		/*
		 * Return the value of the [ThreadStatic] field FIELD on the thread THREAD.
		 */
		public Value GetValue (FieldInfoMirror field, ThreadMirror thread) {
			if (thread == null)
				throw new ArgumentNullException ("thread");
			this.AssertSameVm (thread);
			return GetValues (new FieldInfoMirror [] { field }, thread) [0];
		}

		public void SetValues (IList<FieldInfoMirror> fields, Value[] values) {
			if (fields == null)
				throw new ArgumentNullException ("fields");
			if (values == null)
				throw new ArgumentNullException ("values");
			foreach (FieldInfoMirror f in fields) {
				if (f == null)
					throw new ArgumentNullException ("field");
				this.AssertSameVm (f);
			}
			foreach (Value v in values) {
				if (v == null)
					throw new ArgumentNullException ("values");
				this.AssertSameVm (v);
			}
			long[] ids = new long [fields.Count];
			for (int i = 0; i < fields.Count; ++i)
				ids [i] = fields [i].Id;
			try {
				vm.conn.Type_SetValues (id, ids, vm.EncodeValues (values));
			} catch (CommandException ex) {
				if (ex.ErrorCode == ErrorCode.INVALID_FIELDID)
					throw new ArgumentException ("One of the fields is not valid for this type.", "fields");
				else
					throw;
			}
		}

		public void SetValue (FieldInfoMirror field, Value value) {
			SetValues (new FieldInfoMirror [] { field }, new Value [] { value });
		}

		public ObjectMirror GetTypeObject () {
			return vm.GetObject (vm.conn.Type_GetObject (id));
		}

		/*
		 * Return a list of source files without path info, where methods of
		 * this type are defined. Return an empty list if the information is not
		 * available.
		 * This can be used by a debugger to find out which types occur in a
		 * given source file, to filter the list of methods whose locations
		 * have to be checked when placing breakpoints.
		 */
		public string[] GetSourceFiles () {
			return GetSourceFiles (false);
		}

		string[] source_files;
		string[] source_files_full_path;
		public string[] GetSourceFiles (bool returnFullPaths) {
			string[] res = returnFullPaths ? source_files_full_path : source_files;
			if (res == null) {
				res = vm.conn.Type_GetSourceFiles (id, returnFullPaths);
				if (returnFullPaths)
					source_files_full_path = res;
				else
					source_files = res;
			}
			return res;
		}

		public C.TypeDefinition Metadata {
			get {
				if (meta == null) {
					if (((AssemblyMirror)Assembly).Metadata == null || MetadataToken == 0)
						return null;
					meta = (C.TypeDefinition)((AssemblyMirror)Assembly).Metadata.MainModule.LookupToken (MetadataToken);
				}
				return meta;
			}
		}

		TypeInfo GetInfo () {
			if (info == null)
				info = vm.conn.Type_GetInfo (id);
			return info;
		}

		protected override TypeAttributes GetAttributeFlagsImpl () {
			return (TypeAttributes)GetInfo ().attributes;
		}

		protected override bool HasElementTypeImpl () {
			return IsArray || IsByRef || IsPointer;
		}

		protected override bool IsArrayImpl () {
			return GetInfo ().rank > 0;
		}

		protected override bool IsByRefImpl () {
			return GetInfo ().is_byref;
		}

		protected override bool IsCOMObjectImpl () {
			return false;
		}

		protected override bool IsPointerImpl () {
			return GetInfo ().is_pointer;
		}

		protected override bool IsPrimitiveImpl () {
			return GetInfo ().is_primitive;
		}

		protected override bool IsValueTypeImpl ()
		{
			return GetInfo ().is_valuetype;
		}

		protected override bool IsContextfulImpl ()
		{
			// FIXME:
			return false;
		}

		protected override bool IsMarshalByRefImpl ()
		{
			// FIXME:
			return false;
		}

		// Same as Enum.GetUnderlyingType ()
		public TypeMirror EnumUnderlyingType {
			get {
				if (!IsEnum)
					throw new ArgumentException ("Type is not an enum type.");
				foreach (FieldInfoMirror f in GetFields ()) {
					if (!f.IsStatic)
						return (TypeMirror)f.FieldType;
				}
				throw new NotImplementedException ();
			}
		}

		/*
		 * Creating the custom attributes themselves could modify the behavior of the
		 * debuggee, so we return objects similar to the CustomAttributeData objects
		 * used by the reflection-only functionality on .net.
		 */
		public override object[] GetCustomAttributes (bool inherit) {
			return GetCustomAttrs (null, inherit);
		}

		public override object[] GetCustomAttributes(Type attributeType, bool inherit)
		{
			if (attributeType == null)
				throw new ArgumentNullException (nameof (attributeType));
			return GetCustomAttrs ((TypeMirror) attributeType, inherit);
		}

		void AppendCustomAttrs (IList<CustomAttributeDataMirror> attrs, TypeMirror type, bool inherit)
		{
			if (cattrs == null && Metadata != null && !Metadata.HasCustomAttributes)
				cattrs = new CustomAttributeDataMirror [0];

			if (cattrs == null) {
				CattrInfo[] info = vm.conn.Type_GetCustomAttributes (id, 0, false);
				cattrs = CustomAttributeDataMirror.Create (vm, info);
			}

			foreach (var attr in cattrs) {
				if (type == null || attr.Constructor.DeclaringType == type)
					attrs.Add (attr);
			}

			if (inherit && BaseType != null)
				((TypeMirror)BaseType).AppendCustomAttrs (attrs, type, inherit);
		}

		CustomAttributeDataMirror[] GetCustomAttrs (TypeMirror type, bool inherit) {
			var attrs = new List<CustomAttributeDataMirror> ();
			AppendCustomAttrs (attrs, type, inherit);
			return attrs.ToArray ();
		}

		public MethodMirror[] GetMethodsByNameFlags (string name, BindingFlags flags, bool ignoreCase) {
			if (vm.conn.Version.AtLeast (2, 6)) {
				long[] ids = vm.conn.Type_GetMethodsByNameFlags (id, name, (int)flags, ignoreCase);
				MethodMirror[] m = new MethodMirror [ids.Length];
				for (int i = 0; i < ids.Length; ++i)
					m [i] = (MethodMirror) vm.GetMethod (ids [i]);
				return m;
			} else {
				if ((flags & BindingFlags.IgnoreCase) != 0) {
					flags &= ~BindingFlags.IgnoreCase;
					ignoreCase = true;
				}

				if (flags == BindingFlags.Default)
					flags = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;

				MethodAttributes access = (MethodAttributes) 0;
				bool matchInstance = false;
				bool matchStatic = false;

				if ((flags & BindingFlags.NonPublic) != 0) {
					access |= MethodAttributes.Private;
					flags &= ~BindingFlags.NonPublic;
				}
				if ((flags & BindingFlags.Public) != 0) {
					access |= MethodAttributes.Public;
					flags &= ~BindingFlags.Public;
				}
				if ((flags & BindingFlags.Instance) != 0) {
					flags &= ~BindingFlags.Instance;
					matchInstance = true;
				}
				if ((flags & BindingFlags.Static) != 0) {
					flags &= ~BindingFlags.Static;
					matchStatic = true;
				}

				if ((int) flags != 0)
					throw new NotImplementedException ();

				var res = new List<MethodMirror> ();
				foreach (var m in GetMethods ()) {
					if ((m.Attributes & access) == (MethodAttributes) 0)
						continue;

					if (!((matchStatic && m.IsStatic) || (matchInstance && !m.IsStatic)))
						continue;

					if ((!ignoreCase && m.Name == name) || (ignoreCase && m.Name.Equals (name, StringComparison.CurrentCultureIgnoreCase)))
						res.Add ((MethodInfoMirror) m);
				}
				return res.ToArray ();
			}
		}

		public Value InvokeMethod (ThreadMirror thread, MethodMirror method, IList<Value> arguments) {
			return ObjectMirror.InvokeMethod (vm, thread, method, null, arguments, InvokeOptions.None);
		}

		public Value InvokeMethod (ThreadMirror thread, MethodMirror method, IList<Value> arguments, InvokeOptions options) {
			return ObjectMirror.InvokeMethod (vm, thread, method, null, arguments, options);
		}

		[Obsolete ("Use the overload without the 'vm' argument")]
		public IAsyncResult BeginInvokeMethod (VirtualMachine vm, ThreadMirror thread, MethodMirror method, IList<Value> arguments, InvokeOptions options, AsyncCallback callback, object state) {
			return ObjectMirror.BeginInvokeMethod (vm, thread, method, null, arguments, options, callback, state);
		}

		public IAsyncResult BeginInvokeMethod (ThreadMirror thread, MethodMirror method, IList<Value> arguments, InvokeOptions options, AsyncCallback callback, object state) {
			return ObjectMirror.BeginInvokeMethod (vm, thread, method, null, arguments, options, callback, state);
		}

		public Value EndInvokeMethod (IAsyncResult asyncResult) {
			return ObjectMirror.EndInvokeMethodInternal (asyncResult);
		}

		public InvokeResult EndInvokeMethodWithResult (IAsyncResult asyncResult) {
			return  ObjectMirror.EndInvokeMethodInternalWithResult (asyncResult);
		}

		public Task<Value> InvokeMethodAsync (ThreadMirror thread, MethodMirror method, IList<Value> arguments, InvokeOptions options = InvokeOptions.None) {
			var tcs = new TaskCompletionSource<Value> ();
			BeginInvokeMethod (thread, method, arguments, options, iar =>
					{
						try {
							tcs.SetResult (EndInvokeMethod (iar));
						} catch (OperationCanceledException) {
							tcs.TrySetCanceled ();
						} catch (Exception ex) {
							tcs.TrySetException (ex);
						}
					}, null);
			return tcs.Task;
		}

		public Value NewInstance (ThreadMirror thread, MethodMirror method, IList<Value> arguments) {
			return NewInstance (thread, method, arguments, InvokeOptions.None);
		}

		public Value NewInstance (ThreadMirror thread, MethodMirror method, IList<Value> arguments, InvokeOptions options) {
			if (method == null)
				throw new ArgumentNullException ("method");

			if (!method.IsConstructor)
				throw new ArgumentException ("The method must be a constructor.", "method");

			return ObjectMirror.InvokeMethod (vm, thread, method, null, arguments, options);
		}

		// Since protocol version 2.31
		public Value NewInstance () {
			return vm.GetObject (vm.conn.Type_CreateInstance (id));
		}

		public override Type GetInterface(string name, bool ignoreCase)
		{
			if (ifaces == null)
				ifaces = vm.GetTypes (vm.conn.Type_GetInterfaces (id));

			return ifaces.FirstOrDefault(i => i.Name.Equals(name, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
		}


		// Since protocol version 2.11
		public override Type[] GetInterfaces () {
			if (ifaces == null)
				ifaces = vm.GetTypes (vm.conn.Type_GetInterfaces (id));
			return ifaces;
		}

		// Since protocol version 2.11
		public override InterfaceMapping GetInterfaceMap (Type interfaceType) {
			if (interfaceType == null)
				throw new ArgumentNullException (nameof(interfaceType));
			if (!interfaceType.IsInterface)
				throw new ArgumentException ("Argument must be an interface.", nameof(interfaceType));
			if (IsInterface)
				throw new ArgumentException ("'this' type cannot be an interface itself");

			if (iface_map == null) {
				// Query the info in bulk
				GetInterfaces ();
				var ids = new long [ifaces.Length];
				for (int i = 0; i < ifaces.Length; ++i)
					ids [i] = ifaces [i].Id;

				var ifacemap = vm.conn.Type_GetInterfaceMap (id, ids);

				var imap = new Dictionary<TypeMirror, InterfaceMapping> ();
				for (int i = 0; i < ifacemap.Length; ++i) {
					IfaceMapInfo info = ifacemap [i];

					var imethods = new MethodInfoMirror [info.iface_methods.Length];
					for (int j = 0; j < info.iface_methods.Length; ++j)
						imethods [j] = new MethodInfoMirror (vm.GetMethod (info.iface_methods [j]));

					var tmethods = new MethodInfoMirror [info.iface_methods.Length];
					for (int j = 0; j < info.target_methods.Length; ++j)
						tmethods [j] = new MethodInfoMirror (vm.GetMethod (info.target_methods [j]));

					var itype = vm.GetType (info.iface_id);
					imap [itype] = new InterfaceMapping {
						InterfaceMethods = imethods,
						InterfaceType = itype,
						TargetMethods = tmethods,
						TargetType = this,
					};
				}

				iface_map = imap;
			}

			if (iface_map.TryGetValue ((TypeMirror) interfaceType, out var interfaceMapping))
				return interfaceMapping;

			throw new ArgumentException ("Interface not found", nameof(interfaceType));
		}

		public override object InvokeMember(string name, BindingFlags invokeAttr, Binder binder, object target, object[] args, ParameterModifier[] modifiers, CultureInfo culture, string[] namedParameters)
		{
			throw new Exception("TypeMirror.InvokeMember not implemented");
		}

		protected override ConstructorInfo GetConstructorImpl(BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers)
		{
			throw new NotImplementedException();
		}

		public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr)
		{
			TryInitMethods ();
			return ctors
				.Where(m => (bindingAttr.HasFlag(BindingFlags.Public) && m.IsPublic)
					|| bindingAttr.HasFlag(BindingFlags.NonPublic))
				.Select(m => new ConstructorInfoMirror (m))
				.ToArray();
		}

		protected override System.Reflection.MethodInfo GetMethodImpl(string name, BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers)
		{
			TryInitMethods ();
			var match = methods.Where (m => m.Name == name).ToArray ();
			if (match.Length == 0)
				return null;

			return (System.Reflection.MethodInfo) (binder ?? DefaultBinder).SelectMethod(
				bindingAttr,
				match,
				types ?? Array.Empty<Type> (),
				modifiers);
		}

		public override System.Reflection.EventInfo GetEvent(string name, BindingFlags bindingAttr)
		{
			throw new Exception("TypeMirror.GetEvent is not implemented");
		}

		public override System.Reflection.EventInfo[] GetEvents(BindingFlags bindingAttr)
		{
			throw new Exception("TypeMirror.GetEvents is not implemented");
		}

		public override MemberInfo[] GetMember(string name, MemberTypes type, BindingFlags bindingAttr)
		{
			Console.WriteLine($"!!! -> TypeMirror.GetMember({name}, ({type}), ({bindingAttr})");
			var members = new List<MemberInfo>();
			if (type.HasFlag(MemberTypes.Field)) {
				var field = GetField (name, bindingAttr);
				if (field != null)
					members.Add (field);
			}

			if (type.HasFlag (MemberTypes.Property)) {
				var prop = GetProperty (name, bindingAttr);
				if (prop != null)
					members.Add (prop);
			}

			if (type.HasFlag (MemberTypes.Method)) {
				var method = GetMethod (name, bindingAttr);
				if (method != null)
					members.Add (method);
			}

			Console.WriteLine($"!!!  -> found {members.Count} members matching '{name}' in {this.Name}");

			return members.ToArray ();
		}

		public override MemberInfo[] GetMembers(BindingFlags bindingAttr)
		{
			return GetProperties (bindingAttr)
				.Cast<MemberInfo> ()
				.Concat (GetFields (bindingAttr))
				.Concat (GetMethods (bindingAttr))
				.ToArray ();
		}

		public override bool IsDefined(Type attributeType, bool inherit)
		{
			throw new Exception("TypeMirror.IsDefined is not implemented");
		}

        // Return whenever the type initializer of this type has ran
        // Since protocol version 2.23
        public bool IsInitialized {
			get {
				vm.CheckProtocolVersion (2, 23, "TYPE_IS_INITIALIZED");
				if (!inited)
					inited = vm.conn.Type_IsInitialized (id);
				return inited;
			}
		}
    }
}
