using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Debugging.Evaluation.Extension;
using Mono.Debugging.Evaluation.Metadata;
using Mono.Debugging.Evaluation.OverloadResolution;
using Mono.Debugging.Mono.Debugging.Tools;

namespace Mono.Debugging.Evaluation.RuntimeInvocation
{
    public abstract class MethodResolver<TType, TMetaType, TMetaTypeInfo, TMetaMethod, TMetaParameter, TMetaGenericArgument> : IMethodResolver<TType>
        where TMetaType : class
    {
        public static readonly TType[] EmptyTypeArray = new TType[0];
        readonly IDebuggerMetadataBinder<TType, TMetaType> metadataBinder;
        readonly IOverloadResolutionEngine<TMetaType, TMetaMethod> overloadResolutionEngine;
        readonly IMetadataTools<TMetaType, TMetaMethod> tools;

        protected abstract IReadOnlyList<TMetaMethod> CollectExtensionMethods(
            EvaluationContext ctx,
            string methodName);

        List<ResolutionMethodInfo<TMetaType, TMetaMethod>> CollectOwnCandidates(
            EvaluationContext ctx,
            string methodName,
            TType type,
            BindingFlags flags)
        {
            List<ResolutionMethodInfo<TMetaType, TMetaMethod>> resolutionMethodInfoList = new List<ResolutionMethodInfo<TMetaType, TMetaMethod>>();
            for (TMetaType metaType = this.metadataBinder.ToMetadataType(ctx, type); (object)metaType != null; metaType = this.tools.GetBaseType(metaType))
            {
                foreach (TMetaMethod method in this.tools.GetMethods(metaType, methodName, ctx.CaseSensitive, flags))
                    resolutionMethodInfoList.Add(ResolutionMethodInfo.Create(method, metaType));
                if (flags.HasFlag(BindingFlags.DeclaredOnly))
                    break;
            }

            return resolutionMethodInfoList;
        }

        public ResolutionResult<TMetaType, TMetaMethod> ResolveFromCandidateList(
            EvaluationContext ctx,
            TType[] invocationGenericTypeArgs,
            TType[] actualArgTypes,
            IList<ResolutionMethodInfo<TMetaType, TMetaMethod>> candidates)
        {
            ResolutionArgumentInfo<TMetaType>[] array1 = actualArgTypes.Select(type => new ResolutionArgumentInfo<TMetaType>
            {
                Type = metadataBinder.ToMetadataType(ctx, type)
            }).ToArray();
            TMetaType[] array2 = invocationGenericTypeArgs.Select(type => metadataBinder.ToMetadataType(ctx, type)).ToArray();
            return overloadResolutionEngine.Resolve(candidates.ToArray(), array2, array1);
        }

        public ResolutionResult<TMetaType, TMetaMethod> ResolveStaticMethod(
            EvaluationContext ctx,
            string methodName,
            TType ownerType,
            TType[] invocationGenericTypeArgs,
            params TType[] argTypes)
        {
            return ResolveOwnMethod(ctx, methodName, ownerType, invocationGenericTypeArgs, argTypes, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        public IResolutionResult ResolveInstanceMethod(
            EvaluationContext ctx,
            string methodName,
            TType ownerType,
            TType[] invocationGenericTypeArgs,
            params TType[] argTypes)
        {
            return ResolveOwnMethod(ctx, methodName, ownerType, invocationGenericTypeArgs, argTypes, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        public ResolutionResult<TMetaType, TMetaMethod> ResolveOwnMethod(
            EvaluationContext ctx,
            string methodName,
            TType ownerType,
            TType[] invocationGenericTypeArgs,
            TType[] argTypes,
            BindingFlags flags)
        {
            List<ResolutionMethodInfo<TMetaType, TMetaMethod>> resolutionMethodInfoList = CollectOwnCandidates(ctx, methodName, ownerType, flags);
            if (argTypes != null)
                return ResolveFromCandidateList(ctx, invocationGenericTypeArgs, argTypes, resolutionMethodInfoList);

            if (resolutionMethodInfoList.Count <= 0)
                return ResolutionResult<TMetaType, TMetaMethod>.NoApplicableCandidates();

            return ResolutionResult<TMetaType, TMetaMethod>.Success(resolutionMethodInfoList[0], new TMetaType[0], false);
        }

        public IResolutionResult ResolveExtensionMethod(EvaluationContext ctx, string methodName, TType receiverType, TType[] invocationGenericTypeArgs, TType[] argTypes)
        {
            List<ResolutionMethodInfo<TMetaType, TMetaMethod>> list =
                CollectExtensionMethods(ctx, methodName)
                    .Select(method => ResolutionMethodInfo.Create(method, tools.GetDeclaringType(method)))
                    .ToList();

            if (argTypes == null)
            {
                if (list.Count <= 0)
                    return ResolutionResult<TMetaType, TMetaMethod>.NoApplicableCandidates();
                return ResolutionResult<TMetaType, TMetaMethod>.Success(list[0], new TMetaType[0], false);
            }

            TType[] actualArgTypes = new TType[argTypes.Length + 1];
            actualArgTypes[0] = receiverType;
            argTypes.CopyTo(actualArgTypes, 1);
            return ResolveFromCandidateList(ctx, invocationGenericTypeArgs, actualArgTypes, list);
        }

        public IResolutionResult ResolveUserConversionOperator(EvaluationContext ctx, TType fromType, TType toType)
        {
            var resolutionResult1 = TryResolveOperatorMatchingReturnType(ctx, toType, "op_Explicit", fromType, toType);
            if (resolutionResult1.IsSuccess())
                return resolutionResult1;
            var resolutionResult2 = TryResolveOperatorMatchingReturnType(ctx, toType, "op_Implicit", fromType, toType);
            if (resolutionResult2.IsSuccess())
                return resolutionResult2;
            var resolutionResult3 = TryResolveOperatorMatchingReturnType(ctx, fromType, "op_Explicit", fromType, toType);
            if (resolutionResult3.IsSuccess())
                return resolutionResult3;
            var resolutionResult4 = TryResolveOperatorMatchingReturnType(ctx, fromType, "op_Implicit", fromType, toType);
            resolutionResult4.IsSuccess();
            return resolutionResult4;
        }

        ResolutionResult<TMetaType, TMetaMethod> TryResolveOperatorMatchingReturnType(
            EvaluationContext ctx,
            TType typeToSearch,
            string operatorName,
            TType fromType,
            TType toType)
        {
            ResolutionResult<TMetaType, TMetaMethod> resolutionResult = this.ResolveStaticMethod(ctx, operatorName, typeToSearch, MethodResolver<TType, TMetaType, TMetaTypeInfo, TMetaMethod, TMetaParameter, TMetaGenericArgument>.EmptyTypeArray, fromType);
            TMetaType metadataType = this.metadataBinder.ToMetadataType(ctx, toType);
            if (resolutionResult.IsSuccess())
            {
                if (this.ReturnTypeMatches(resolutionResult.SelectedCandidate, metadataType))
                    return resolutionResult;
                return ResolutionResult<TMetaType, TMetaMethod>.NoApplicableCandidates();
            }

            if (resolutionResult.IsAmbigious() && resolutionResult.AmbigiousCandidates == null)
                throw new InvalidOperationException("AmbigiousCandidates must not be null");
            return resolutionResult;
        }

        bool ReturnTypeMatches(
            ResolutionMethodInfo<TMetaType, TMetaMethod> candidate,
            TMetaType toMetadataType)
        {
            return tools.TypeComparer.Equals(tools.GetReturnType(candidate.Method), toMetadataType);
        }

        public IResolutionResult ResolveConstructor(EvaluationContext ctx, TType type, params TType[] argTypes)
        {
            throw new NotImplementedException();
        }

        public IResolutionResult ResolveStaticConstructor(EvaluationContext ctx, TType type)
        {
            throw new NotImplementedException();
        }

        IResolutionResult IMethodResolver<TType>.ResolveOwnMethod(
            EvaluationContext ctx,
            string methodName,
            TType ownerType,
            TType[] invocationGenericTypeArgs,
            TType[] argTypes,
            BindingFlags flags)
        {
            return ResolveOwnMethod(ctx, methodName, ownerType, invocationGenericTypeArgs, argTypes, flags);
        }

        IResolutionResult IMethodResolver<TType>.ResolveStaticMethod(
            EvaluationContext ctx,
            string methodName,
            TType ownerType,
            TType[] invocationGenericTypeArgs,
            params TType[] argTypes)
        {
            return ResolveStaticMethod(ctx, methodName, ownerType, invocationGenericTypeArgs, argTypes);
        }
    }
}

namespace Mono.Debugging.Evaluation.OverloadResolution
{
    public class ResolutionMethodInfo<TType, TMethod>
    {
        public TMethod Method;
        public TType OwnerType;
    }

    public class ResolutionArgumentInfo<TType>
    {
        public TType Type;
    }

    public interface IOverloadResolutionEngine<TType, TMethod>
    {
        ResolutionResult<TType, TMethod> Resolve(
            ResolutionMethodInfo<TType, TMethod>[] candidates,
            TType[] typeArgs,
            ResolutionArgumentInfo<TType>[] argumentTypes);
    }

    public static class ResolutionMethodInfo
    {
        public static ResolutionMethodInfo<TType, TMethod> Create<TType, TMethod>(
            TMethod method,
            TType ownerType)
        {
            return new ResolutionMethodInfo<TType, TMethod>
            {
                Method = method,
                OwnerType = ownerType
            };
        }
    }
}
