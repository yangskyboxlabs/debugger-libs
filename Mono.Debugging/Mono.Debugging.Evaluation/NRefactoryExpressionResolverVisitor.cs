//
// NRefactoryExpressionResolverVisitor.cs
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
using System.Text;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.CSharp;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation.TypeResolution;
using Mono.Debugging.Mono.Debugging.Utils;

namespace Mono.Debugging.Evaluation
{
    // FIXME: if we passed the DebuggerSession and SourceLocation into the NRefactoryExpressionEvaluatorVisitor,
    // we wouldn't need to do this resolve step.
    public class NRefactoryExpressionResolverVisitor : DepthFirstAstVisitor
    {
        readonly List<Replacement> replacements = new List<Replacement>();
        readonly List<int> lineStartOffsets = new List<int>();
        readonly SourceLocation location;
        readonly EvaluationContext myCtx;
        readonly ITypeResolver m_TypeResolverHandler;
        readonly string[] namespaceImports;
        readonly string expression;
        string parentType;

        class Replacement
        {
            public string NewText;
            public int Offset;
            public int Length;
        }

        public NRefactoryExpressionResolverVisitor(
            EvaluationContext ctx,
            ITypeResolver typeResolver,
            SourceLocation location,
            string expression)
        {
            this.expression = expression;
            this.namespaceImports = ctx.Options.NamespaceImports;
            this.myCtx = ctx;
            m_TypeResolverHandler = typeResolver;
            this.location = location;
            ComputeLineStartOffsets();
        }

        void ComputeLineStartOffsets()
        {
            int startIndex = 0;
            while (startIndex >= 0)
            {
                lineStartOffsets.Add(startIndex);
                startIndex = expression.IndexOf("\n", startIndex, StringComparison.Ordinal);
                if (startIndex >= 0)
                    ++startIndex;
            }
        }

        internal string GetResolvedExpression()
        {
            if (replacements.Count == 0)
                return expression;

            replacements.Sort((r1, r2) => r1.Offset.CompareTo(r2.Offset));
            var resolved = new StringBuilder();
            int i = 0;

            foreach (var replacement in replacements)
            {
                resolved.Append(expression, i, replacement.Offset - i);
                resolved.Append(replacement.NewText);
                i = replacement.Offset + replacement.Length;
            }

            var last = replacements[replacements.Count - 1];
            resolved.Append(expression, last.Offset + last.Length, expression.Length - (last.Offset + last.Length));

            return resolved.ToString();
        }

        string GenerateGenericArgs(int genericArgs)
        {
            if (genericArgs == 0)
                return "";

            string result = "<";
            for (int i = 0; i < genericArgs; i++)
                result += "int,";

            return result.Remove(result.Length - 1) + ">";
        }

        void ReplaceType(string name, int genericArgs, int offset, int length, bool memberType = false)
        {
            string type;

            if (genericArgs == 0)
                type = ResolveIdentifierAsType(myCtx, name);
            else
                type = ResolveIdentifierAsType(myCtx, name + "`" + genericArgs);

            if (string.IsNullOrEmpty(type))
            {
                parentType = null;
            }
            else
            {
                if (memberType)
                {
                    type = type.Substring(type.LastIndexOf('.') + 1);
                }
                else
                {
                    type = "global::" + type;
                }

                parentType = type + GenerateGenericArgs(genericArgs);
                var replacement = new Replacement { Offset = offset, Length = length, NewText = type };
                replacements.Add(replacement);
            }
        }

        protected internal string ResolveIdentifierAsType(EvaluationContext ctx, string identifier)
        {
            string name1 = m_TypeResolverHandler.Resolve(ctx, identifier, location);
            if (name1 != null)
                return CropAndReplace(name1);
            foreach (string namespaceImport in namespaceImports)
            {
                string name2 = m_TypeResolverHandler.Resolve(ctx, namespaceImport + "." + identifier, location);
                if (name2 != null)
                    return CropAndReplace(name2);
            }

            return null;

            string CropAndReplace(string name)
            {
                int length = name.LastIndexOf('`');
                name = length == -1 ? name : name.Substring(0, length);
                return name.Replace('+', '.');
            }
        }

        void ReplaceType(AstType type)
        {
            int length = GetOffsetByTextLocation(type.EndLocation) - GetOffsetByTextLocation(type.StartLocation);
            int offsetByTextLocation = GetOffsetByTextLocation(type.StartLocation);
            ReplaceType(type.ToCSharpFormat(), 0, offsetByTextLocation, length);
        }

        int GetOffsetByTextLocation(TextLocation textLocation)
        {
            int index = textLocation.Line - 1;
            if (index >= lineStartOffsets.Count)
                throw new ArgumentException($"Text Location is out of expression range, get line {index}, max line {lineStartOffsets.Count - 1}");
            return lineStartOffsets[index] + textLocation.Column - 1;
        }

        public override void VisitIdentifierExpression(IdentifierExpression identifierExpression)
        {
            base.VisitIdentifierExpression(identifierExpression);

            int length = identifierExpression.IdentifierToken.EndLocation.Column - identifierExpression.IdentifierToken.StartLocation.Column;
            int offset = identifierExpression.IdentifierToken.StartLocation.Column - 1;

            ReplaceType(identifierExpression.Identifier, identifierExpression.TypeArguments.Count, offset, length);
        }

        public override void VisitTypeReferenceExpression(TypeReferenceExpression typeReferenceExpression)
        {
            ReplaceType(typeReferenceExpression.Type);
        }

        public override void VisitComposedType(ComposedType composedType)
        {
            // Note: we specifically do not handle this case because the 'base' implementation will eventually
            // call VisitMemberType() or VisitSimpleType() on the ComposedType.BaseType which is all we really
            // care to resolve.
            base.VisitComposedType(composedType);
        }

        public override void VisitMemberType(MemberType memberType)
        {
            base.VisitMemberType(memberType);
            if (parentType == null)
                return;
            int length = memberType.MemberNameToken.EndLocation.Column - memberType.MemberNameToken.StartLocation.Column;
            int offset = memberType.MemberNameToken.StartLocation.Column - 1;
            ReplaceType(parentType + "." + memberType.MemberName, memberType.TypeArguments.Count, offset, length, true);
        }

        public override void VisitSimpleType(SimpleType simpleType)
        {
            base.VisitSimpleType(simpleType);

            int length = simpleType.IdentifierToken.EndLocation.Column - simpleType.IdentifierToken.StartLocation.Column;
            int offset = simpleType.IdentifierToken.StartLocation.Column - 1;

            ReplaceType(simpleType.Identifier, simpleType.TypeArguments.Count, offset, length);
        }
    }
}
