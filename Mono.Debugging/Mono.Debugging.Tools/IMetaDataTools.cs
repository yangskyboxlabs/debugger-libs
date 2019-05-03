using System;
using System.Collections.Generic;
using System.Reflection;

namespace Mono.Debugging.Mono.Debugging.Tools
{
    public interface IMetadataTools<TMetaType, TMetaMethod>
    {
        IEqualityComparer<TMetaType> TypeComparer { get; }

        TMetaType GetBaseType(TMetaType type);

        TMetaMethod[] GetMethods(TMetaType typeInfo, BindingFlags flags = BindingFlags.Default);

        TMetaMethod[] GetMethods(
            TMetaType typeInfo,
            string name,
            bool caseSensitive = true,
            BindingFlags flags = BindingFlags.Default);

        TMetaType GetDeclaringType(TMetaMethod method);

        TMetaType GetDeclaringType(TMetaType type);

        TMetaType[] GetNestedTypes(TMetaType type);

        string GetFullyQualifiedName(TMetaType type);

        string GetShortName(TMetaType type);

        bool IsStatic(TMetaMethod method);

        bool TypeIsAvailable(string typeName, TMetaMethod metaMethod);

        TMetaType GetReturnType(TMetaMethod method);
    }
}
