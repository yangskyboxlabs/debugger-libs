using System;
using System.Reflection;
using System.Globalization;
using System.Collections.Generic;

namespace Mono.Debugger.Soft {
    public sealed class ConstructorInfoMirror : ConstructorInfo, IMethodBaseMirror, IMirrorWithId
    {
		public VirtualMachine VirtualMachine => method.VirtualMachine;
		public long Id => method.Id;

		MethodMirror method;

        public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException ();

        public override MethodAttributes Attributes => method.Attributes;

        public override string Name => ConstructorInfo.ConstructorName;

        public override Type DeclaringType => method.DeclaringType;

        public override Type ReflectedType => method.ReflectedType;

        public override IEnumerable<CustomAttributeData> CustomAttributes => base.CustomAttributes;

        public override int MetadataToken => base.MetadataToken;

        public override Module Module => base.Module;

        public override MethodImplAttributes MethodImplementationFlags => base.MethodImplementationFlags;

        public override CallingConventions CallingConvention => base.CallingConvention;

        public override bool IsGenericMethodDefinition => base.IsGenericMethodDefinition;

        public override bool ContainsGenericParameters => base.ContainsGenericParameters;

        public override bool IsGenericMethod => base.IsGenericMethod;

        public override bool IsSecurityCritical => base.IsSecurityCritical;

        public override bool IsSecuritySafeCritical => base.IsSecuritySafeCritical;

        public override bool IsSecurityTransparent => base.IsSecurityTransparent;

        public override MemberTypes MemberType => base.MemberType;

        public ConstructorInfoMirror (MethodMirror method)
        {
			this.method = method;
        }

        public override object Invoke (BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
			=> Invoke (null, invokeAttr, binder, parameters, culture);

        public override ParameterInfo[] GetParameters ()
			=> method.GetParameters ();

        public override MethodImplAttributes GetMethodImplementationFlags ()
			=> method.GetMethodImplementationFlags ();

        public override object Invoke (object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            throw new NotSupportedException("Tried to Invoke() ctor");
        }

        public override object[] GetCustomAttributes (bool inherit)
			=> method.GetCustomAttributes (inherit);

        public override object[] GetCustomAttributes (Type attributeType, bool inherit)
			=> method.GetCustomAttributes ((TypeMirror) attributeType, inherit);

        public override bool IsDefined (Type attributeType, bool inherit)
			=> throw new NotImplementedException ();
			//=> method.IsDefined (attributeType, inherit);

        public override string ToString()
        {
            return method.ToString();
        }

        public override IList<CustomAttributeData> GetCustomAttributesData()
			=> throw new NotImplementedException ();
            //=> method.GetCustomAttributesData ();

        public override Type[] GetGenericArguments()
            => method.GetGenericArguments ();

        public override MethodBody GetMethodBody()
            => method.GetMethodBody ();

        public override bool Equals(object obj)
            => method.Equals(obj);

        public override int GetHashCode()
			=> method.GetHashCode ();

        public static implicit operator MethodMirror (ConstructorInfoMirror constructorInfo) => constructorInfo.method;
    }
}