using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Mono.Debugger.Soft {
    public class MethodInfoMirror : System.Reflection.MethodInfo, IMethodBaseMirror {

        MethodMirror method;

        public long Id => method.Id;

        public VirtualMachine VirtualMachine => method.VirtualMachine;

        public override string Name => method.Name;

        public override Type DeclaringType => method.DeclaringType;

        public override Type ReflectedType => method.ReflectedType;

        public override IEnumerable<CustomAttributeData> CustomAttributes => throw new NotImplementedException();

        public override int MetadataToken => method.MetadataToken;

        public override Module Module => throw new NotImplementedException();

        public override MethodImplAttributes MethodImplementationFlags => method.GetMethodImplementationFlags ();

        public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException ();

        public override MethodAttributes Attributes => method.Attributes;

        public override bool IsGenericMethodDefinition => method.IsGenericMethod;

        public override bool IsGenericMethod => method.IsGenericMethod;

        public override Type ReturnType => method.ReturnType;

        public override ParameterInfo ReturnParameter => method.ReturnParameter;

        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();

		public IList<Location> Locations => method.Locations;

		public string SourceFile => method.SourceFile;

		public string FullName => method.FullName;

		public Location LocationAtILOffset (int il_offset) => method.LocationAtILOffset (il_offset);

        public MethodInfoMirror (MethodMirror method)
        {
            this.method = method;
        }

        public override Delegate CreateDelegate(Type delegateType)
			=> throw new NotImplementedException ();
            //=> method.CreateDelegate (delegateType);

        public override Delegate CreateDelegate(Type delegateType, object target)
			=> throw new NotImplementedException ();
            //=> method.CreateDelegate (delegateType, target);

        public override bool Equals(object obj)
            => obj is MethodInfoMirror other
				? method.Equals (other.method)
				: false;

        public override System.Reflection.MethodInfo GetBaseDefinition()
            //=> method.GetBaseDefinition ();
			=> throw new NotImplementedException ();

        public override object[] GetCustomAttributes(bool inherit)
            => method.GetCustomAttributes(inherit);

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
			=> throw new NotImplementedException ();
            //=> method.GetCustomAttributes (attributeType, inherit);

        public override IList<CustomAttributeData> GetCustomAttributesData()
			=> throw new NotImplementedException ();
            //=> method.GetCustomAttributesData ();

        public override Type[] GetGenericArguments()
            => method.GetGenericArguments ();

        public override System.Reflection.MethodInfo GetGenericMethodDefinition()
			=> throw new NotImplementedException();
            //=> method.GetGenericMethodDefinition ();

        public override int GetHashCode()
            => method.GetHashCode();

        public override MethodBody GetMethodBody()
            => method.GetMethodBody ();

        public override MethodImplAttributes GetMethodImplementationFlags()
            => method.GetMethodImplementationFlags ();

        public override ParameterInfo[] GetParameters()
            => method.GetParameters ();

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
			=> throw new NotImplementedException ();
            //=> method.Invoke (obj, invokeAttr, binder, parameters, culture);

        public override bool IsDefined(Type attributeType, bool inherit)
			=> throw new NotImplementedException ();
            //=> method.IsDefined (attributeType, inherit);

        public override System.Reflection.MethodInfo MakeGenericMethod(params Type[] typeArguments)
			=> throw new NotImplementedException ();
            //=> method.MakeGenericMethod (typeArguments);

        public override string ToString()
            => method.ToString ();

        public static implicit operator MethodMirror (MethodInfoMirror methodInfo) => methodInfo.method;
    }
}