using System;
using System.Text.RegularExpressions;

namespace Mono.Debugging.Evaluation
{
    public class ArrayValueUtil
    {
        static readonly Regex indexRegex = new Regex("^\\d+(\\s*,\\s*\\d+)*$", RegexOptions.Compiled);

        public static bool IsIndex(string index)
        {
            return indexRegex.IsMatch(index);
        }
    }
}
