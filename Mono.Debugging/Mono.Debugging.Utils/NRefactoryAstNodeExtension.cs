using System;
using ICSharpCode.NRefactory.CSharp;

namespace Mono.Debugging.Mono.Debugging.Utils
{
    public static class NRefactoryAstNodeExtension
    {
        public static readonly CSharpFormattingOptions DefaultOptions = FormattingOptionsFactory.CreateMono();

        /// <summary>ToCSharpFormat returns the node in CSharp format</summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public static string ToCSharpFormat(this AstNode node)
        {
            return node.ToString(DefaultOptions);
        }

        /// <summary>ToCSharpFormat returns the node in CSharp format</summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public static string ToCSharpFormat(
            this AstNode node,
            CSharpFormattingOptions formattingOptions)
        {
            return node.ToString(formattingOptions);
        }
    }
}
