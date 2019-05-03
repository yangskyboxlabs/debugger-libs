using System;

namespace Mono.Debugging.Mono.Debugging.Utils
{
    public static class CollectionUtil
    {
        public static T[] WrapInArray<T>(this T val)
        {
            return new T[1] { val };
        }
    }
}
