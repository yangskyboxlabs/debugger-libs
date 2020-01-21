using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using C = Mono.Cecil;
using Mono.Cecil.Metadata;
using System.Globalization;

namespace Mono.Debugger.Soft
{
	public class FieldInfoMirror : System.Reflection.FieldInfo, IMirror {
		VirtualMachine vm;
		long id;

		TypeMirror parent;
		string name;
		TypeMirror type;
		FieldAttributes attrs;
		CustomAttributeDataMirror[] cattrs;
		C.FieldDefinition meta;
		bool inited;

		public VirtualMachine VirtualMachine => vm;
		public long Id => id;

		public FieldInfoMirror (TypeMirror parent, long id, string name, TypeMirror type, FieldAttributes attrs) : this (parent.VirtualMachine, id) {
			this.parent = parent;
			this.name = name;
			this.type = type;
			this.attrs = attrs;
			inited = true;
		}

		public FieldInfoMirror (VirtualMachine vm, long id)
		{
			this.vm = vm;
			this.id = id;
		}

		public override Type DeclaringType {
			get {
				if (!inited)
					GetInfo ();
				return parent;
			}
		}

		public override string Name {
			get {
				if (!inited)
					GetInfo ();
				return name;
			}
		}

		public override Type FieldType {
			get {
				if (!inited)
					GetInfo ();
				return type;
			}
		}

		public override FieldAttributes Attributes {
			get {
				if (!inited)
					GetInfo ();
				return attrs;
			}
		}

		void GetInfo () {
			if (inited)
				return;
			var info = vm.conn.Field_GetInfo (id);
			name = info.Name;
			parent = vm.GetType (info.Parent);
			type = vm.GetType (info.TypeId);
			attrs = (FieldAttributes)info.Attrs;
			inited = true;
		}

		public override object[] GetCustomAttributes (bool inherit)
		{
			return GetCAttrs (null, inherit);
		}

		public override object[] GetCustomAttributes(Type attributeType, bool inherit)
		{
			if (attributeType is TypeMirror t)
				return GetCAttrs (t, inherit);

			throw new ArgumentException("Type argument is not a TypeMirror", nameof(attributeType));
		}

/*
		public CustomAttributeDataMirror[] GetCustomAttributes (TypeMirror attributeType, bool inherit) {
			if (attributeType == null)
				throw new ArgumentNullException ("attributeType");
			return GetCAttrs (attributeType, inherit);
		}
		*/

		public C.FieldDefinition Metadata {
			get {
				if (parent.Metadata == null)
					return null;
				// FIXME: Speed this up
				foreach (var fd in parent.Metadata.Fields) {
					if (fd.Name == Name) {
						meta = fd;
						break;
					}
				}
				if (meta == null)
					/* Shouldn't happen */
					throw new NotImplementedException ();
				return meta;
			}
		}

		CustomAttributeDataMirror[] GetCAttrs (TypeMirror type, bool inherit) {
			if (cattrs == null && Metadata != null && !Metadata.HasCustomAttributes)
				cattrs = new CustomAttributeDataMirror [0];

			// FIXME: Handle inherit
			if (cattrs == null) {
				CattrInfo[] info = vm.conn.Type_GetFieldCustomAttributes (((TypeMirror)DeclaringType).Id, id, 0, false);
				cattrs = CustomAttributeDataMirror.Create (vm, info);
			}
			var res = new List<CustomAttributeDataMirror> ();
			foreach (var attr in cattrs)
				if (type == null || attr.Constructor.DeclaringType == type)
					res.Add (attr);
			return res.ToArray ();
		}

		public override object GetValue(object obj)
		{
			throw new Exception("FieldInfoMirror.GetValue is not implemented");
		}

		public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, CultureInfo culture)
		{
			throw new Exception("FieldInfoMirror.SetValue is not implemented");
		}

		public override bool IsDefined(Type attributeType, bool inherit)
		{
			throw new NotImplementedException();
		}

		public string FullName {
			get {
				string type_namespace = DeclaringType.Namespace;
				string type_name = DeclaringType.Name;
				StringBuilder sb = new StringBuilder ();
				if (type_namespace != String.Empty)
					sb.Append (type_namespace).Append (".");
				sb.Append (type_name);
				sb.Append (":");
				sb.Append (Name);
				return sb.ToString ();
			}
		}

		public override RuntimeFieldHandle FieldHandle => throw new NotImplementedException();

		public override Type ReflectedType => throw new NotSupportedException ();
	}
}

