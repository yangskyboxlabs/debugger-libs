//
// IRawValue.cs
//
// Author:
//       Lluis Sanchez <lluis@xamarin.com>
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

namespace Mono.Debugging.Backend
{
    public interface IRawValue<out TValue> : IRawValue, IDebuggerBackendObject
    {
        TValue TargetObject { get; }
    }

    public interface IRawValue
    {
        bool IsNull { get; }
    }

    /// <summary>
    /// Represents primitive value that can be obtained as the value of this runtime
    /// Non-parametrized surface for <see cref="T:Mono.Debugging.Backend.IRawValuePrimitive`1" /> to expose into public API
    /// This interface should not be implemented directly. Implement <see cref="T:Mono.Debugging.Backend.IRawValuePrimitive`1" /> instead
    /// </summary>
    public interface IRawValuePrimitive : IRawValue
    {
        /// <summary>
        /// Returns primitive value of this runtime corresponding to
        /// </summary>
        ValueType Value { get; }
    }

    public static class IRawValueExtension
    {
        public static bool IsPrimitive(this IRawValue value)
        {
            return value is IRawValuePrimitive;
        }

        public static T ToPrimitive<T>(this IRawValue value) where T : struct
        {
            return (T)value.ToPrimitive();
        }

        public static ValueType ToPrimitive(this IRawValue value)
        {
            IRawValuePrimitive rawValuePrimitive = value as IRawValuePrimitive;
            if (rawValuePrimitive == null)
                throw new InvalidCastException("Cannot unwrap primitive because 'value' is not IRawValuePrimitive");
            return rawValuePrimitive.Value;
        }

        public static bool TryToPrimitive<T>(this IRawValue value, out T primitiveValue) where T : struct
        {
            primitiveValue = default;
            if (!(value is IRawValuePrimitive rawValuePrimitive) || !(rawValuePrimitive.Value is T))
                return false;
            primitiveValue = (T)rawValuePrimitive.Value;
            return true;
        }

        public static bool Is<T>(this IRawValue value) where T : struct
        {
            if (!(value is IRawValuePrimitive rawValuePrimitive))
                return false;
            return rawValuePrimitive.Value is T;
        }

        public static bool IsString(this IRawValue value)
        {
            return value is IRawValueString;
        }

        /// <summary>
        /// Returns null if <paramref name="value" /> is not a string or string value otherwise
        /// </summary>
        public static string TryToString(this IRawValue value)
        {
            return (value as IRawValueString)?.Value;
        }
    }
}
