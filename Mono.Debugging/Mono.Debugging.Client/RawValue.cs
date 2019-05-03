// 
// RawValue.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
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
using Mono.Debugging.Backend;
using Mono.Debugging.Evaluation;
using Mono.Debugging.Evaluation.RuntimeInvocation;

namespace Mono.Debugging.Client
{
    /// <summary>
    /// Represents an object in the process being debugged
    /// </summary>
    [Serializable]
    public class RawValue<TType, TValue> : IRawObject
        where TType : class
        where TValue : class
    {
        readonly TValue targetObject;
        EvaluationOptions options;
        readonly IDebuggerHierarchicalObject source;
        EvaluationContext Context { get; set; }
        ObjectValueAdaptor<TType, TValue> Adaptor { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Mono.Debugging.Client.RawValue"/> class.
        /// </summary>
        /// <param name='source'>
        /// Value source
        /// </param>
        public RawValue(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext context,
            IDebuggerHierarchicalObject source,
            TValue targetObject)
        {
            Adaptor = adaptor;
            this.targetObject = targetObject;
            this.source = source;
            Context = context;
        }

        /// <summary>
        /// Full name of the type of the object
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// Invokes a method on the object
        /// </summary>
        /// <returns>
        /// The result of the invocation
        /// </returns>
        /// <param name='methodName'>
        /// The name of the method
        /// </param>
        /// <param name='parameters'>
        /// The parameters (primitive type values, RawValue instances or RawValueArray instances)
        /// </param>
        public IRawValue<TValue> CallMethod(
            string methodName,
            params IRawValue<TValue>[] parameters)
        {
            return CallMethod(methodName, parameters, false, out var outArgs);
        }

        public IRawValue<TValue> CallMethod(string methodName, out IRawValue<TValue>[] outArgs, params IRawValue<TValue>[] parameters)
        {
            return CallMethod(methodName, parameters, true, out outArgs);
        }

        IRawValue<TValue> CallMethod(
            string methodName,
            IRawValue<TValue>[] parameters,
            bool enableOutArgs,
            out IRawValue<TValue>[] outArgs)
        {
            EvaluationContext ctx = Context.WithOptions(options);
            TValue[] objArray = new TValue[parameters.Length];
            TType[] typeArray = new TType[parameters.Length];
            for (int index = 0; index < objArray.Length; ++index)
            {
                objArray[index] = parameters[index].TargetObject;
                typeArray[index] = Adaptor.GetValueType(ctx, objArray[index]);
            }

            InvocationResult<TValue> invocationResult = Adaptor.Invocator.InvokeInstanceMethod(ctx, targetObject, methodName, objArray);
            if (enableOutArgs)
            {
                // TODO: Support this
                throw new NotSupportedException("Does not support out args");
            }
            else
            {
                outArgs = null;
            }

            return Adaptor.ToRawValue(ctx, source, invocationResult.Result);
        }

        /// <summary>
        /// Gets the value of a field or property
        /// </summary>
        /// <returns>
        /// The value (a primitive type value, a RawValue instance or a RawValueArray instance)
        /// </returns>
        /// <param name='name'>
        /// Name of the field or property
        /// </param>
        public IRawValue<TValue> GetMemberValue(string name)
        {
            EvaluationContext ctx = Context.WithOptions(options);
            TType valueType = Adaptor.GetValueType(ctx, targetObject);
            ValueReference<TType, TValue> member = Adaptor.GetMember(ctx, source, valueType, targetObject, name);
            if (member == null)
                throw new EvaluatorException("Member '{0}' not found", (object)name);
            return Adaptor.ToRawValue(ctx, this.source, member.Value);
        }

        /// <summary>
        /// Sets the value of a field or property
        /// </summary>
        /// <param name='name'>
        /// Name of the field or property
        /// </param>
        /// <param name='value'>
        /// The value (a primitive type value, a RawValue instance or a RawValueArray instance)
        /// </param>
        public void SetMemberValue(string name, IRawValue<TValue> value)
        {
            EvaluationContext ctx = Context.WithOptions(options);
            TType valueType = Adaptor.GetValueType(ctx, targetObject);
            ValueReference<TType, TValue> member = Adaptor.GetMember(ctx, source, valueType, targetObject, name);
            if (member == null)
                throw new EvaluatorException("Member '{0}' not found", (object)name);
            member.Value = value.TargetObject;
        }
    }

    /// <summary>
    /// Represents an array of objects in the process being debugged
    /// </summary>
    [Serializable]
    internal class RawValueArray<TType, TValue> : RemoteRawValueBase<TType, TValue>, IRawValueArray<TValue>, IRawObject
        where TType : class
        where TValue : class
    {
        readonly ICollectionAdaptor<TType, TValue> targetArray;
        readonly IDebuggerHierarchicalObject source;
        EvaluationOptions options;

        public int[] Dimensions => targetArray.GetDimensions();

        /// <summary>
        /// Initializes a new instance of the <see cref="Mono.Debugging.Client.RawValueArray"/> class.
        /// </summary>
        /// <param name='source'>
        /// Value source.
        /// </param>
        public RawValueArray(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext context,
            IDebuggerHierarchicalObject source,
            ICollectionAdaptor<TType, TValue> targetArray)
            : base(adaptor, context, targetArray.CollectionObject)
        {
            this.targetArray = targetArray;
            this.source = source;
        }

        public IRawValue<TValue> GetValue(int[] index)
        {
            return Adaptor.ToRawValue(Context, source, targetArray.GetElement(index));
        }

        IRawValue[] IRawValueArray.GetValues(int[] index, int count)
        {
            return GetValues(index, count);
        }

        IRawValue IRawValueArray.GetValue(int[] index)
        {
            return GetValue(index);
        }

        /// <summary>
        /// Gets the values.
        /// </summary>
        /// <returns>The items.</returns>
        /// <param name="index">The index.</param>
        /// <param name="count">The number of items to get.</param>
        /// <remarks>
        /// This method is useful for incrementally fetching an array in order to avoid
        /// long waiting periods when the array is too large for ToArray().
        /// </remarks>
        public IRawValue<TValue>[] GetValues(int[] index, int count)
        {
            TValue[] elements = targetArray.GetElements(index, count);
            int[] indices = new int[index.Length];
            for (int i = 0; i < index.Length; i++)
            {
                indices[i] = index[i];
            }

            var rawValues = new List<IRawValue<TValue>>();
            for (int i = 0; i < count; i++)
            {
                IRawValue<TValue> rawValue = Adaptor.ToRawValue(Context, new ArrayObjectSource<TType, TValue>(targetArray, indices, source), elements[i]);
                rawValues.Add(rawValue);
                ++indices[indices.Length - 1];
            }

            return rawValues.ToArray();
        }

        public void SetValue(int[] index, IRawValue<TValue> value)
        {
            targetArray.SetElement(index, value.TargetObject);
        }

        void IRawValueArray.SetValue(int[] index, IRawValue value)
        {
            SetValue(index, (IRawValue<TValue>)value);
        }
    }

    /// <summary>
    /// Represents a string object in the process being debugged
    /// </summary>
    [Serializable]
    public class RawValueString : IRawObject
    {
        IRawValueString source;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mono.Debugging.Client.RawValueString"/> class.
        /// </summary>
        /// <param name='source'>
        /// Value source.
        /// </param>
        public RawValueString(IRawValueString source)
        {
            this.source = source;
        }

        internal IRawValueString Source
        {
            get { return source; }
        }

        /// <summary>
        /// Gets the length of the string
        /// </summary>
        public int Length
        {
            get { return source.Length; }
        }

        /// <summary>
        /// Gets a substring of the string
        /// </summary>
        /// <param name='index'>
        /// The starting index of the requested substring.
        /// </param>
        /// <param name='length'>
        /// The length of the requested substring.
        /// </param>
        public string Substring(int index, int length)
        {
            return source.Substring(index, length);
        }

        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <value>
        /// The value.
        /// </value>
        public string Value
        {
            get { return source.Value; }
        }
    }

    interface IRawObject { }
}
