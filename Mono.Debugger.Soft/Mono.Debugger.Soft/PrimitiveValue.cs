using System;
using System.Collections.Generic;

namespace Mono.Debugger.Soft
{
    /*
     * Represents a value of a primitive type in the debuggee
     */
    public class PrimitiveValue : Value
    {
        readonly object value;
        readonly TypeMirror type;

        public PrimitiveValue(object value, TypeMirror type)
            : base(type.VirtualMachine, 0)
        {
            this.value = value;
        }

        public PrimitiveValue(object value, AppDomainMirror appDomain)
            : this(value, value == null
                ? appDomain.Corlib.GetType("System.Object", false, false)
                : appDomain.GetCorrespondingType(value.GetType())) { }

        public object Value => value;
        public override TypeMirror Type => type;

        public override bool Equals(object obj)
        {
            if (value == obj)
                return true;

            if (obj is PrimitiveValue primitive)
                return value == primitive.Value;

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override string ToString()
        {
            object v = Value;

            return "PrimitiveValue<" + (v != null ? v.ToString() : "(null)") + ">";
        }

        public Value InvokeMethod(ThreadMirror thread, MethodMirror method, IList<Value> arguments)
        {
            return ObjectMirror.InvokeMethod(vm, thread, method, this, arguments, InvokeOptions.None);
        }

        public Value InvokeMethod(ThreadMirror thread, MethodMirror method, IList<Value> arguments, InvokeOptions options)
        {
            return ObjectMirror.InvokeMethod(vm, thread, method, this, arguments, options);
        }

        public IAsyncResult BeginInvokeMethod(ThreadMirror thread, MethodMirror method, IList<Value> arguments, InvokeOptions options, AsyncCallback callback, object state)
        {
            return ObjectMirror.BeginInvokeMethod(vm, thread, method, this, arguments, options, callback, state);
        }

        public Value EndInvokeMethod(IAsyncResult asyncResult)
        {
            return ObjectMirror.EndInvokeMethodInternal(asyncResult);
        }

        public InvokeResult EndInvokeMethodWithResult(IAsyncResult asyncResult)
        {
            return ObjectMirror.EndInvokeMethodInternalWithResult(asyncResult);
        }
    }
}
