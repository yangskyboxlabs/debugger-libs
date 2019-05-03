// 
// RemoteRawValue.cs
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
using System.Linq;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;
using Mono.Debugging.Evaluation.RuntimeInvocation;

namespace Mono.Debugging.Evaluation
{
    internal class RemoteRawValueBase<TType, TValue> : IRawValue<TValue>
        where TType : class
        where TValue : class
    {
        public EvaluationContext Context { get; }

        public ObjectValueAdaptor<TType, TValue> Adaptor { get; }

        public TValue TargetObject { get; }

        public bool IsNull => Adaptor.IsNull(Context, TargetObject);

        public RemoteRawValueBase(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            TValue targetObject)
        {
            Adaptor = adaptor;
            Context = ctx;
            TargetObject = targetObject;
        }
    }

    public interface IRawValueArray<TValue> : IRawValueArray, IRawValue<TValue>
    {
        IRawValue<TValue> GetValue(int[] index);

        IRawValue<TValue>[] GetValues(int[] index, int count);

        void SetValue(int[] index, IRawValue<TValue> value);
    }

    class RemoteRawValueArray<TType, TValue> : RemoteRawValueBase<TType, TValue>, IRawValueArray<TValue>
        where TType : class
        where TValue : class
    {
        readonly ICollectionAdaptor<TType, TValue> targetArray;
        readonly EvaluationContext ctx;
        readonly IDebuggerHierarchicalObject source;

        public RemoteRawValueArray(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            IDebuggerHierarchicalObject source,
            ICollectionAdaptor<TType, TValue> targetArray)
            : base(adaptor, ctx, targetArray.CollectionObject)
        {
            this.ctx = ctx;
            this.targetArray = targetArray;
            this.source = source;
        }

        public IRawValue<TValue> GetValue(int[] index)
        {
            return Adaptor.ToRawValue(ctx, source, targetArray.GetElement(index));
        }

        public IRawValue<TValue>[] GetValues(int[] index, int count)
        {
            TValue[] values = targetArray.GetElements(index, count);
            var idx = new int[index.Length];
            var rawValues = new List<IRawValue<TValue>>();

            for (int i = 0; i < index.Length; i++)
                idx[i] = index[i];

//            Type commonType = null;
            for (int i = 0; i < count; i++)
            {
                var rv = Adaptor.ToRawValue(ctx, new ArrayObjectSource<TType, TValue>(targetArray, idx, source), values[i]);

//                if (commonType == null)
//                    commonType = rv.GetType();
//                else if (commonType != rv.GetType())
//                    commonType = typeof(void);
                rawValues.Add(rv);

                idx[idx.Length - 1]++;
            }

//            if (array.Count > 0 && commonType != typeof(void))
//                return array.ToArray(commonType);

            return rawValues.ToArray();
        }

        public void SetValue(int[] index, IRawValue<TValue> value)
        {
            targetArray.SetElement(index, value.TargetObject);
        }

        public int[] Dimensions => targetArray.GetDimensions();

        IRawValue[] IRawValueArray.GetValues(int[] index, int count)
        {
            return this.GetValues(index, count);
        }

        void IRawValueArray.SetValue(int[] index, IRawValue value)
        {
            SetValue(index, (IRawValue<TValue>)value);
        }

        IRawValue IRawValueArray.GetValue(int[] index)
        {
            return GetValue(index);
        }
    }

    class RemoteRawValueString<TType, TValue> : RemoteRawValueBase<TType, TValue>, IRawValueString
        where TType : class
        where TValue : class
    {
        private readonly IStringAdaptor targetString;

        public RemoteRawValueString(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext context,
            IStringAdaptor targetString,
            TValue targetObject)
            : base(adaptor, context, targetObject)
        {
            this.targetString = targetString;
        }

        public int Length
        {
            get { return this.targetString.Length; }
        }

        public string Value
        {
            get { return this.targetString.Value; }
        }

        public string Substring(int index, int length)
        {
            return this.targetString.Substring(index, length);
        }
    }

    internal class RemoteRawValuePrimitive<TValue> : RemoteFrameObject, IRawValue<TValue>
    {
        public RemoteRawValuePrimitive(ValueType value, TValue targetObject)
        {
            if (!value.GetType().IsPrimitive)
                throw new ArgumentException();
            this.Value = value;
            this.TargetObject = targetObject;
        }

        public ValueType Value { get; private set; }

        public TValue TargetObject { get; private set; }

        public bool IsNull
        {
            get { return false; }
        }
    }

    internal class RemoteRawValueObject<TType, TValue> : RemoteRawValueBase<TType, TValue>
        where TType : class
        where TValue : class
    {
        private readonly IDebuggerHierarchicalObject source;
        private readonly TValue targetObject;

        public RemoteRawValueObject(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext gctx,
            IDebuggerHierarchicalObject source,
            TValue targetObject)
            : base(adaptor, gctx.WithModifiedOptions(options =>
            {
                options.AllowTargetInvoke = true;
                options.AllowMethodEvaluation = true;
            }), targetObject)
        {
            this.targetObject = targetObject;
            this.source = source;
        }

        public IRawValue<TValue> CallMethod(
            string name,
            IRawValue<TValue>[] parameters,
            EvaluationOptions options)
        {
            IRawValue<TValue>[] outArgs;
            return this.CallMethod(name, parameters, false, out outArgs, options);
        }

        public IRawValue<TValue> CallMethod(
            string name,
            IRawValue<TValue>[] parameters,
            out IRawValue<TValue>[] outArgs,
            EvaluationOptions options)
        {
            return this.CallMethod(name, parameters, true, out outArgs, options);
        }

        private IRawValue<TValue> CallMethod(
            string name,
            IRawValue<TValue>[] parameters,
            bool enableOutArgs,
            out IRawValue<TValue>[] outArgs,
            EvaluationOptions options)
        {
            EvaluationContext ctx = Context.WithOptions(options);
            TValue[] objArray = new TValue[parameters.Length];
            TType[] typeArray = new TType[parameters.Length];
            for (int index = 0; index < objArray.Length; ++index)
            {
                objArray[index] = parameters[index].TargetObject;
                typeArray[index] = Adaptor.GetValueType(ctx, objArray[index]);
            }

            InvocationResult<TValue> invocationResult = Adaptor.Invocator.InvokeInstanceMethod(ctx, this.targetObject, name, objArray);
            if (enableOutArgs)
            {
                outArgs = new IRawValue<TValue>[invocationResult.OutArgs.Length];
                for (int index = 0; index < outArgs.Length; ++index)
                    outArgs[index] = Adaptor.ToRawValue(ctx, this.source, invocationResult.OutArgs[index]);
            }
            else
                outArgs = (IRawValue<TValue>[])null;

            return Adaptor.ToRawValue(ctx, this.source, invocationResult.Result);
        }

        public IRawValue<TValue> GetMemberValue(string name, EvaluationOptions options)
        {
            EvaluationContext ctx = this.Context.WithOptions(options);
            TType valueType = Adaptor.GetValueType(ctx, targetObject);
            ValueReference<TType, TValue> member = Adaptor.GetMember(ctx, source, valueType, targetObject, name);
            if (member == null)
                throw new EvaluatorException("Member '{0}' not found", name);
            return Adaptor.ToRawValue(ctx, source, member.Value);
        }

        public void SetMemberValue(string name, IRawValue<TValue> value, EvaluationOptions options)
        {
            EvaluationContext ctx = Context.WithOptions(options);
            TType valueType = Adaptor.GetValueType(ctx, targetObject);
            ValueReference<TType, TValue> member = Adaptor.GetMember(ctx, source, valueType, targetObject, name);
            if (member == null)
                throw new EvaluatorException("Member '{0}' not found", name);
            member.Value = member.Value;
        }

        IRawValue CallMethod(
            string name,
            IRawValue[] parameters,
            EvaluationOptions options)
        {
            return CallMethod(name, parameters.Cast<IRawValue<TValue>>().ToArray(), options);
        }

        IRawValue CallMethod(
            string name,
            IRawValue[] parameters,
            out IRawValue[] outArgs,
            EvaluationOptions options)
        {
            IRawValue<TValue>[] outArgs1;
            IRawValue<TValue> rawValue = this.CallMethod(name, parameters.Cast<IRawValue<TValue>>().ToArray<IRawValue<TValue>>(), out outArgs1, options);
            outArgs = (IRawValue[])outArgs1;
            return (IRawValue)rawValue;
        }

        void SetMemberValue(
            string name,
            IRawValue value,
            EvaluationOptions options)
        {
            this.SetMemberValue(name, (IRawValue<TValue>)value, options);
        }
    }
}
