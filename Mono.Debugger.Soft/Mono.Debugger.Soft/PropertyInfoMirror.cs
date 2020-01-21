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
	public class PropertyInfoMirror : System.Reflection.PropertyInfo, IMirrorWithId {
		VirtualMachine vm;
		long id;

		TypeMirror parent;
		string name;
		PropertyAttributes attrs;
		MethodInfoMirror get_method, set_method;
		CustomAttributeDataMirror[] cattrs;
		C.PropertyDefinition meta;

		System.Reflection.MethodInfo[] publicAccessors, allAccessors;

		public VirtualMachine VirtualMachine => vm;
		public long Id => id;

		public PropertyInfoMirror (TypeMirror parent, long id, string name, MethodMirror get_method, MethodMirror set_method, PropertyAttributes attrs)
		{
			vm = parent.VirtualMachine;
			this.id = id;
			this.parent = parent;
			this.name = name;
			this.attrs = attrs;
			if (get_method != null)
				this.get_method = new MethodInfoMirror (get_method);
			if (set_method != null)
				this.set_method = new MethodInfoMirror (set_method);
		}

		public override Type DeclaringType {
			get {
				return parent;
			}
		}

		public override string Name {
			get {
				return name;
			}
		}

		public override Type PropertyType {
			get {
				if (get_method != null)
					return get_method.ReturnType;
				else {
					var parameters = set_method.GetParameters ();

					return parameters [parameters.Length - 1].ParameterType;
				}
			}
		}

		public override PropertyAttributes Attributes => attrs;

		public override System.Reflection.MethodInfo GetGetMethod (bool nonPublic)
		{
			if (get_method != null && (nonPublic || get_method.IsPublic))
				return get_method;
			else
				return null;
		}

		public override System.Reflection.MethodInfo GetSetMethod (bool nonPublic)
		{
			if (set_method != null && (nonPublic || set_method.IsPublic))
				return set_method;
			else
				return null;
		}

		public override ParameterInfo[] GetIndexParameters()
		{
			if (get_method != null)
				return get_method.GetParameters ();
			return new ParameterInfoMirror [0];
		}

		public C.PropertyDefinition Metadata {
			get {
				if (parent.Metadata == null)
					return null;
				// FIXME: Speed this up
				foreach (var def in parent.Metadata.Properties) {
					if (def.Name == Name) {
						meta = def;
						break;
					}
				}
				if (meta == null)
					/* Shouldn't happen */
					throw new NotImplementedException ();
				return meta;
			}
		}

		public override bool CanRead => get_method != null;

		public override bool CanWrite => set_method != null;

		public override Type ReflectedType => parent;

		public override object[] GetCustomAttributes(bool inherit)
			=> GetCAttrs ();

		public override object[] GetCustomAttributes(Type type, bool inherit)
			=> GetCAttrs ()
				.Where (attr => attr.AttributeType.FullName == type.FullName)
				.ToArray ();

		CustomAttributeDataMirror[] GetCAttrs () {
			if (cattrs == null && Metadata != null && !Metadata.HasCustomAttributes)
				cattrs = new CustomAttributeDataMirror [0];

			if (cattrs == null) {
				CattrInfo[] info = vm.conn.Type_GetPropertyCustomAttributes (((TypeMirror)DeclaringType).Id, id, 0, false);
				cattrs = CustomAttributeDataMirror.Create (vm, info);
			}

			return cattrs;
		}

		public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture)
		{
			throw new NotImplementedException();
		}

		public override System.Reflection.MethodInfo[] GetAccessors(bool nonPublic)
		{
			if (nonPublic) {
				if (allAccessors == null) {
					if (get_method != null && set_method != null)
						allAccessors = new[] { get_method, set_method };
					else if (get_method != null || set_method != null)
						allAccessors = new[] { get_method ?? set_method };
					else
						allAccessors = Array.Empty<System.Reflection.MethodInfo>();
				}
				return allAccessors;
			} else {
				if (publicAccessors == null) {
					var hasPublicGet = get_method?.IsPublic ?? false;
					var hasPublicSet = set_method?.IsPublic ?? false;
					if (hasPublicGet && hasPublicSet)
						publicAccessors = new[] { get_method, set_method };
					else if (hasPublicGet || hasPublicSet)
						publicAccessors = new[] { hasPublicGet ? get_method : set_method };
					else
						publicAccessors = Array.Empty<System.Reflection.MethodInfo>();
				}
				return publicAccessors;
			}
		}

		public override object GetValue(object obj, BindingFlags invokeAttr, Binder binder, object[] index, CultureInfo culture)
		{
			throw new Exception("PropertyInfoMirror.GetValue is not implemented");
		}

		public override bool IsDefined(Type attributeType, bool inherit)
		{
			throw new NotImplementedException();
		}

		public override bool Equals(object obj)
		{
			if (obj is PropertyInfoMirror other)
				return vm == other.vm && id == other.id;
			else
				return false;
		}
	}
}

