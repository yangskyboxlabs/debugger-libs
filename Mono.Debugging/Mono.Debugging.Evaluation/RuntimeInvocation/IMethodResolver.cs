using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Debugging.Evaluation.OverloadResolution;

namespace Mono.Debugging.Evaluation.RuntimeInvocation
{
    public interface IMethodResolver<in TType>
    {
        IResolutionResult ResolveStaticMethod(
            EvaluationContext ctx,
            string methodName,
            TType ownerType,
            TType[] invocationGenericTypeArgs,
            params TType[] argTypes);

        /// <summary>
        /// Looks for instance method with any visibility starting on type <paramref name="ownerType" /> and then going to its bases
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="methodName">method name to look for</param>
        /// <param name="ownerType">type in which search will be started</param>
        /// <param name="invocationGenericTypeArgs">generic type arguments for method invocation. If empty - type inference will be performed</param>
        /// <param name="argTypes">types of arguments</param>
        IResolutionResult ResolveInstanceMethod(
            EvaluationContext ctx,
            string methodName,
            TType ownerType,
            TType[] invocationGenericTypeArgs,
            params TType[] argTypes);

        /// <summary>
        /// Looks for instance/static method with visibility passed with <paramref name="flags" /> starting on type <paramref name="ownerType" /> and then going to its bases
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="methodName">method name to look for</param>
        /// <param name="ownerType">type in which search will be started</param>
        /// <param name="invocationGenericTypeArgs">generic type arguments for method invocation. If empty - type inference will be performed</param>
        /// <param name="argTypes">types of arguments</param>
        /// <param name="flags">visibility, staticness and so on</param>
        IResolutionResult ResolveOwnMethod(
            EvaluationContext ctx,
            string methodName,
            TType ownerType,
            TType[] invocationGenericTypeArgs,
            TType[] argTypes,
            BindingFlags flags);

        /// <summary>Looks for extension method</summary>
        /// <param name="ctx"></param>
        /// <param name="methodName"></param>
        /// <param name="receiverType"></param>
        /// <param name="invocationGenericTypeArgs"></param>
        /// <param name="argTypes"></param>
        IResolutionResult ResolveExtensionMethod(
            EvaluationContext ctx,
            string methodName,
            TType receiverType,
            TType[] invocationGenericTypeArgs,
            TType[] argTypes);

        /// <summary>
        /// Looks for user-defined conversion operator // TODO need to make different code for implicit and explicit operators
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="fromType">from type</param>
        /// <param name="toType">to type</param>
        IResolutionResult ResolveUserConversionOperator(
            EvaluationContext ctx,
            TType fromType,
            TType toType);

        /// <summary>
        /// Looks for a constructor of type <paramref name="type" />
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="type">type which to search</param>
        /// <param name="argTypes">argument types</param>
        IResolutionResult ResolveConstructor(
            EvaluationContext ctx,
            TType type,
            params TType[] argTypes);

        /// <summary>
        /// Looks for a static constructor of type <paramref name="type" />
        /// </summary>
        /// <param name="ctx">context</param>
        /// <param name="type">type which to search</param>
        IResolutionResult ResolveStaticConstructor(EvaluationContext ctx, TType type);
    }

    public interface IMethodResolver<in TType, TMetaType, TMetaMethod> : IMethodResolver<TType>
    {
        ResolutionResult<TMetaType, TMetaMethod> ResolveFromCandidateList(
            EvaluationContext ctx,
            TType[] invocationGenericTypeArgs,
            TType[] actualArgTypes,
            IList<ResolutionMethodInfo<TMetaType, TMetaMethod>> candidates);
    }
}
