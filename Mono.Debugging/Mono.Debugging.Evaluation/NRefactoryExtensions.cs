//
// NRefactoryExtensions.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2013 Xamarin Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using ICSharpCode.NRefactory.CSharp;

namespace Mono.Debugging.Evaluation
{
    public static class NRefactoryExtensions
    {
        #region AstType

        public static TType Resolve<TType, TValue>(
            this AstType type,
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx)
            where TType : class
            where TValue : class
        {
            var args = new List<TType>();
            var name = type.Resolve(adaptor, ctx, args);

            //if (name.StartsWith ("global::", StringComparison.Ordinal))
            //	name = name.Substring ("global::".Length);

            if (string.IsNullOrEmpty(name))
                return null;

            if (args.Count > 0)
                return adaptor.GetType(ctx, name, args.ToArray());

            return adaptor.GetType(ctx, name);
        }

        static string Resolve<TType, TValue>(
            this AstType type,
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            List<TType> args)
            where TType : class
            where TValue : class
        {
            if (type is PrimitiveType)
                return Resolve((PrimitiveType)type, adaptor, ctx, args);
            else if (type is ComposedType)
                return Resolve((ComposedType)type, adaptor, ctx, args);
            else if (type is MemberType)
                return Resolve((MemberType)type, adaptor, ctx, args);
            else if (type is SimpleType)
                return Resolve((SimpleType)type, adaptor, ctx, args);

            return null;
        }

        #endregion AstType

        #region ComposedType

        static string Resolve<TType, TValue>(
            this ComposedType type,
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            List<TType> args)
            where TType : class
            where TValue : class
        {
            string name;

            if (type.HasNullableSpecifier)
            {
                args.Insert(0, type.BaseType.Resolve(adaptor, ctx));
                name = "System.Nullable`1";
            }
            else
            {
                name = type.BaseType.Resolve(adaptor, ctx, args);
            }

            if (type.PointerRank > 0)
                name += new string('*', type.PointerRank);

            if (type.ArraySpecifiers.Count > 0)
            {
                foreach (var spec in type.ArraySpecifiers)
                {
                    if (spec.Dimensions > 1)
                        name += "[" + new string(',', spec.Dimensions - 1) + "]";
                    else
                        name += "[]";
                }
            }

            return name;
        }

        #endregion ComposedType

        #region MemberType

        static string Resolve<TType, TValue>(
            this MemberType type,
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            List<TType> args)
            where TType : class
            where TValue : class
        {
            string name;

            if (!type.IsDoubleColon)
            {
                var parent = type.Target.Resolve(adaptor, ctx, args);
                name = parent + "." + type.MemberName;
            }
            else
            {
                name = type.MemberName;
            }

            if (type.TypeArguments.Count > 0)
            {
                name += "`" + type.TypeArguments.Count;
                foreach (var arg in type.TypeArguments)
                {
                    TType resolved;

                    if ((resolved = arg.Resolve(adaptor, ctx)) == null)
                        return null;

                    args.Add(resolved);
                }
            }

            return name;
        }

        #endregion MemberType

        #region PrimitiveType

        public static string Resolve(this PrimitiveType type)
        {
            switch (type.Keyword)
            {
                case "bool": return "System.Boolean";
                case "sbyte": return "System.SByte";
                case "byte": return "System.Byte";
                case "char": return "System.Char";
                case "short": return "System.Int16";
                case "ushort": return "System.UInt16";
                case "int": return "System.Int32";
                case "uint": return "System.UInt32";
                case "long": return "System.Int64";
                case "ulong": return "System.UInt64";
                case "float": return "System.Single";
                case "double": return "System.Double";
                case "decimal": return "System.Decimal";
                case "string": return "System.String";
                case "object": return "System.Object";
                case "void": return "System.Void";
                default: return null;
            }
        }

        static string Resolve<TType, TValue>(
            this PrimitiveType type,
            EvaluationContext ctx,
            List<TType> args)
            where TType : class
            where TValue : class
        {
            return Resolve(type);
        }

        #endregion PrimitiveType

        #region SimpleType

        static string Resolve<TType, TValue>(
            this SimpleType type,
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            List<TType> args)
            where TType : class
            where TValue : class
        {
            string name = type.Identifier;

            if (type.TypeArguments.Count > 0)
            {
                name += "`" + type.TypeArguments.Count;
                foreach (var arg in type.TypeArguments)
                {
                    TType resolved;

                    if ((resolved = arg.Resolve(adaptor, ctx)) == null)
                        return null;

                    args.Add(resolved);
                }
            }

            return name;
        }

        #endregion SimpleType
    }
}
