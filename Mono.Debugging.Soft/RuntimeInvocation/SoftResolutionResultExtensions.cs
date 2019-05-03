using System;
using System.Linq;
using Mono.Debugging.Evaluation.OverloadResolution;

namespace Mono.Debugger.Soft.RuntimeInvocation
{
    public static class SoftResolutionResultExtensions
    {
        public static MethodMirror MakeGenericMethodIfNeeded(
            this ResolutionResult<TypeMirror, MethodMirror> result)
        {
            if (result.SelectedCandidate == null)
                throw new InvalidOperationException("resulutionResult must be success");

            MethodMirror method = result.SelectedCandidate.Method;
            if (!method.VirtualMachine.Version.AtLeast(2, 24))
                return method;

            TypeMirror[] methodTypeArguments = result.MethodTypeArguments;
            if (methodTypeArguments.Length == 0)
                return method;

            TypeMirror[] array = methodTypeArguments.ToArray();
            if (method.VirtualMachine.Version.AtLeast(2, 45))
            {
                AppDomainMirror domain1 = method.DeclaringType.Assembly.Domain;
                for (var index = 0; index < methodTypeArguments.Length; ++index)
                {
                    TypeMirror typeMirror = methodTypeArguments[index];
                    AppDomainMirror domain2 = typeMirror.Assembly.Domain;
                    array[index] = !typeMirror.IsPrimitive || domain1 == domain2 ? typeMirror : domain1.Corlib.GetType(typeMirror.FullName);
                }
            }

            return method.MakeGenericMethod(array);
        }
    }
}
