using System;
using System.Collections.Generic;
using System.Text;

namespace Mono.Debugging.Evaluation
{
    public class TypeNamingUtil
    {
        static readonly Dictionary<string, string> CSharpTypeNames = new Dictionary<string, string>();

        static TypeNamingUtil()
        {
            CSharpTypeNames["System.Void"] = "void";
            CSharpTypeNames["System.Object"] = "object";
            CSharpTypeNames["System.Boolean"] = "bool";
            CSharpTypeNames["System.Byte"] = "byte";
            CSharpTypeNames["System.SByte"] = "sbyte";
            CSharpTypeNames["System.Char"] = "char";
            CSharpTypeNames["System.Enum"] = "enum";
            CSharpTypeNames["System.Int16"] = "short";
            CSharpTypeNames["System.Int32"] = "int";
            CSharpTypeNames["System.Int64"] = "long";
            CSharpTypeNames["System.UInt16"] = "ushort";
            CSharpTypeNames["System.UInt32"] = "uint";
            CSharpTypeNames["System.UInt64"] = "ulong";
            CSharpTypeNames["System.Single"] = "float";
            CSharpTypeNames["System.Double"] = "double";
            CSharpTypeNames["System.Decimal"] = "decimal";
            CSharpTypeNames["System.String"] = "string";
        }

        public static string GetDisplayTypeName(
            string typeName,
            int startIndex,
            int endIndex)
        {
            // Note: '[' denotes the start of an array
            //       '`' denotes a generic type
            //       ',' denotes the start of the assembly name
            int tokenIndex = typeName.IndexOfAny(new[] { '[', '`', ',' }, startIndex, endIndex - startIndex);
            List<string> genericArgs = null;
            string array = string.Empty;
            int genericEndIndex = -1;
            int typeEndIndex;

            retry:
            if (tokenIndex == -1) // Simple type
                return GetShortTypeName(typeName.Substring(startIndex, endIndex - startIndex));

            if (typeName[tokenIndex] == ',') // Simple type with an assembly name
                return GetShortTypeName(typeName.Substring(startIndex, tokenIndex - startIndex));

            // save the index of the end of the type name
            typeEndIndex = tokenIndex;

            // decode generic args first, if this is a generic type
            if (typeName[tokenIndex] == '`')
            {
                genericEndIndex = typeName.IndexOf('[', tokenIndex, endIndex - tokenIndex);
                if (genericEndIndex == -1)
                {
                    // Mono's compiler seems to generate non-generic types with '`'s in the name
                    // e.g. __EventHandler`1_FileCopyEventArgs_DelegateFactory_2
                    tokenIndex = typeName.IndexOfAny(new[] { '[', ',' }, tokenIndex, endIndex - tokenIndex);
                    goto retry;
                }

                tokenIndex = genericEndIndex;
                genericArgs = GetGenericArguments(typeName, ref tokenIndex, endIndex);
            }

            // decode array rank info
            while (tokenIndex < endIndex && typeName[tokenIndex] == '[')
            {
                int arrayEndIndex = typeName.IndexOf(']', tokenIndex, endIndex - tokenIndex);
                if (arrayEndIndex == -1)
                    break;
                arrayEndIndex++;
                array += typeName.Substring(tokenIndex, arrayEndIndex - tokenIndex);
                tokenIndex = arrayEndIndex;
            }

            string name = typeName.Substring(startIndex, typeEndIndex - startIndex);

            if (genericArgs == null)
                return GetShortTypeName(name) + array;

            // Use the prettier name for nullable types
            if (name == "System.Nullable" && genericArgs.Count == 1)
                return genericArgs[0] + "?" + array;

            // Insert the generic arguments next to each type.
            // for example: Foo`1+Bar`1[System.Int32,System.String]
            // is converted to: Foo<int>.Bar<string>
            var builder = new StringBuilder(name);
            int i = typeEndIndex + 1;
            int genericIndex = 0;
            int argCount, next;

            while (i < genericEndIndex)
            {
                // decode the argument count
                argCount = 0;
                while (i < genericEndIndex && char.IsDigit(typeName[i]))
                {
                    argCount = (argCount * 10) + (typeName[i] - '0');
                    i++;
                }

                // insert the argument types
                builder.Append('<');
                while (argCount > 0 && genericIndex < genericArgs.Count)
                {
                    builder.Append(genericArgs[genericIndex++]);
                    if (--argCount > 0)
                        builder.Append(',');
                }

                builder.Append('>');

                // Find the end of the next generic type component
                if ((next = typeName.IndexOf('`', i, genericEndIndex - i)) == -1)
                    next = genericEndIndex;

                // Append the next generic type component
                builder.Append(typeName, i, next - i);

                i = next + 1;
            }

            return builder + array;
        }

        public static string GetShortTypeName(string typeName)
        {
            int star = typeName.IndexOf('*');
            string name, ptr, csharp;

            if (star != -1)
            {
                name = typeName.Substring(0, star);
                ptr = typeName.Substring(star);
            }
            else
            {
                ptr = string.Empty;
                name = typeName;
            }

            if (CSharpTypeNames.TryGetValue(name, out csharp))
                return csharp + ptr;

            return typeName;
        }

        static List<string> GetGenericArguments(string typeName, ref int i, int endIndex)
        {
            // Get a list of the generic arguments.
            // When returning, i points to the next char after the closing ']'
            var genericArgs = new List<string>();

            i++;

            while (i < endIndex && typeName[i] != ']')
            {
                int pend = FindTypeEnd(typeName, i, endIndex);
                bool escaped = typeName[i] == '[';

                genericArgs.Add(GetDisplayTypeName(typeName, escaped ? i + 1 : i, escaped ? pend - 1 : pend));
                i = pend;

                if (i < endIndex && typeName[i] == ',')
                    i++;
            }

            i++;

            return genericArgs;
        }

        static int FindTypeEnd(string typeName, int startIndex, int endIndex)
        {
            int i = startIndex;
            int brackets = 0;

            while (i < endIndex)
            {
                char c = typeName[i];

                if (c == '[')
                {
                    brackets++;
                }
                else if (c == ']')
                {
                    if (brackets <= 0)
                        return i;

                    brackets--;
                }
                else if (c == ',' && brackets == 0)
                {
                    return i;
                }

                i++;
            }

            return i;
        }
    }
}
