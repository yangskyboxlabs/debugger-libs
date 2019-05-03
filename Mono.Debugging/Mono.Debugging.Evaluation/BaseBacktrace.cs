// 
// Backtrace.cs
//  
// Author:
//       Lluis Sanchez Gual <lluis@novell.com>
// 
// Copyright (c) 2009 Novell, Inc (http://www.novell.com)
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
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public abstract class BaseBacktrace<TType, TValue> : RemoteFrameObject, IBacktrace
        where TType : class
        where TValue : class
    {
        readonly Dictionary<int, FrameInfo<TType, TValue>> frameInfo = new Dictionary<int, FrameInfo<TType, TValue>>();

        protected BaseBacktrace(ObjectValueAdaptor<TType, TValue> adaptor)
        {
            Adaptor = adaptor;
        }

        public abstract StackFrame[] GetStackFrames(int firstIndex, int lastIndex);

        public ObjectValueAdaptor<TType, TValue> Adaptor { get; set; }

        protected abstract EvaluationContext GetEvaluationContext(int frameIndex, EvaluationOptions options);

        public abstract int FrameCount { get; }

        public virtual ObjectValue[] GetLocalVariables(int frameIndex, EvaluationOptions options)
        {
            var frame = GetFrameInfo(frameIndex, options, false);
            var list = new List<ObjectValue>();

            if (frame == null)
            {
                var val = Adaptor.CreateObjectValueAsync("Local Variables", ObjectValueFlags.EvaluatingGroup, delegate
                {
                    frame = GetFrameInfo(frameIndex, options, true);
                    foreach (var local in frame.LocalVariables)
                        list.Add(local.CreateObjectValue(false, options));

                    return ObjectValue.CreateArray(null, new ObjectPath("Local Variables"), "", new[] { list.Count }, ObjectValueFlags.EvaluatingGroup, list.ToArray());
                });

                return new[] { val };
            }

            foreach (ValueReference<TType, TValue> local in frame.LocalVariables)
                list.Add(local.CreateObjectValue(true, options));

            return list.ToArray();
        }

        public virtual ObjectValue[] GetParameters(int frameIndex, EvaluationOptions options)
        {
            var frame = GetFrameInfo(frameIndex, options, false);
            var values = new List<ObjectValue>();

            if (frame == null)
            {
                var value = Adaptor.CreateObjectValueAsync("Parameters", ObjectValueFlags.EvaluatingGroup, delegate
                {
                    frame = GetFrameInfo(frameIndex, options, true);
                    foreach (var param in frame.Parameters)
                        values.Add(param.CreateObjectValue(false, options));

                    return ObjectValue.CreateArray(null, new ObjectPath("Parameters"), "", new[] { values.Count }, ObjectValueFlags.EvaluatingGroup, values.ToArray());
                });

                return new[] { value };
            }

            foreach (var param in frame.Parameters)
                values.Add(param.CreateObjectValue(true, options));

            return values.ToArray();
        }

        public virtual ObjectValue GetThisReference(int frameIndex, EvaluationOptions options)
        {
            var frame = GetFrameInfo(frameIndex, options, false);

            if (frame == null)
            {
                return Adaptor.CreateObjectValueAsync("this", ObjectValueFlags.EvaluatingGroup, delegate
                {
                    frame = GetFrameInfo(frameIndex, options, true);
                    ObjectValue[] values;

                    if (frame.This != null)
                        values = new[] { frame.This.CreateObjectValue(false, options) };
                    else
                        values = new ObjectValue [0];

                    return ObjectValue.CreateArray(null, new ObjectPath("this"), "", new[] { values.Length }, ObjectValueFlags.EvaluatingGroup, values);
                });
            }

            return frame.This != null ? frame.This.CreateObjectValue(true, options) : null;
        }

        public virtual ExceptionInfo GetException(int frameIndex, EvaluationOptions options)
        {
            var frame = GetFrameInfo(frameIndex, options, false);
            ObjectValue value;

            if (frame == null)
            {
                value = Adaptor.CreateObjectValueAsync(options.CurrentExceptionTag, ObjectValueFlags.EvaluatingGroup, delegate
                {
                    frame = GetFrameInfo(frameIndex, options, true);
                    ObjectValue[] values;

                    if (frame.Exception != null)
                        values = new[] { frame.Exception.CreateObjectValue(false, options) };
                    else
                        values = new ObjectValue [0];

                    return ObjectValue.CreateArray(null, new ObjectPath(options.CurrentExceptionTag), "", new[] { values.Length }, ObjectValueFlags.EvaluatingGroup, values);
                });
            }
            else if (frame.Exception != null)
            {
                value = frame.Exception.CreateObjectValue(true, options);
            }
            else
            {
                return null;
            }

            return new ExceptionInfo(value);
        }

        public virtual ObjectValue GetExceptionInstance(
            int frameIndex,
            EvaluationOptions options)
        {
            FrameInfo<TType, TValue> frame = GetFrameInfo(frameIndex, options, false);

            if (frame == null)
            {
                return Adaptor.CreateObjectValueAsync(options.CurrentExceptionTag, ObjectValueFlags.EvaluatingGroup, delegate
                {
                    frame = GetFrameInfo(frameIndex, options, true);
                    ObjectValue[] values;

                    if (frame.Exception != null)
                        values = new[] { frame.Exception.Exception.CreateObjectValue(false, options) };
                    else
                        values = new ObjectValue [0];

                    return ObjectValue.CreateArray(null, new ObjectPath(options.CurrentExceptionTag), "", new[] { values.Length }, ObjectValueFlags.EvaluatingGroup, values);
                });
            }

            return frame.Exception != null ? frame.Exception.Exception.CreateObjectValue(true, options) : null;
        }

        public virtual ObjectValue[] GetAllLocals(int frameIndex, EvaluationOptions options)
        {
            var locals = new List<ObjectValue>();

            var excObj = GetExceptionInstance(frameIndex, options);
            if (excObj != null)
                locals.Insert(0, excObj);

            locals.AddRange(GetLocalVariables(frameIndex, options));
            locals.AddRange(GetParameters(frameIndex, options));

            locals.Sort((v1, v2) => StringComparer.InvariantCulture.Compare(v1.Name, v2.Name));

            var thisObj = GetThisReference(frameIndex, options);
            if (thisObj != null)
                locals.Insert(0, thisObj);

            return locals.ToArray();
        }

        public virtual ObjectValue[] GetExpressionValues(
            int frameIndex,
            string[] expressions,
            EvaluationOptions options,
            SourceLocation location)
        {
            if (Adaptor.IsEvaluating)
            {
                var values = new List<ObjectValue>();

                foreach (string exp in expressions)
                {
                    string tmpExp = exp;
                    var value = Adaptor.CreateObjectValueAsync(tmpExp, ObjectValueFlags.Field, delegate
                    {
                        EvaluationContext cctx = GetEvaluationContext(frameIndex, options);
                        return Adaptor.GetExpressionValue(cctx, tmpExp);
                    });
                    values.Add(value);
                }

                return values.ToArray();
            }

            var ctx = GetEvaluationContext(frameIndex, options);

            return Adaptor.GetExpressionValuesAsync(ctx, expressions);
        }

        public virtual CompletionData GetExpressionCompletionData(int frameIndex, string exp)
        {
            var ctx = GetEvaluationContext(frameIndex, EvaluationOptions.DefaultOptions);
            return Adaptor.GetExpressionCompletionData(ctx, exp);
        }

        public virtual AssemblyLine[] Disassemble(int frameIndex, int firstLine, int count)
        {
            throw new NotImplementedException();
        }

        FrameInfo<TType, TValue> GetFrameInfo(int frameIndex, EvaluationOptions options, bool ignoreEvalStatus)
        {
            FrameInfo<TType, TValue> finfo;

            if (frameInfo.TryGetValue(frameIndex, out finfo))
                return finfo;

            if (!ignoreEvalStatus && Adaptor.IsEvaluating)
                return null;

            var ctx = GetEvaluationContext(frameIndex, options);
            if (ctx == null)
                return null;

            finfo = new FrameInfo<TType, TValue>();
            finfo.Context = ctx;

            //Don't try to optimize lines below with lazy loading, you won't gain anything(in communication with runtime)
            finfo.LocalVariables.AddRange(Adaptor.Evaluator.GetLocalVariables(ctx));
            finfo.Parameters.AddRange(Adaptor.Evaluator.GetParameters(ctx));
            finfo.This = Adaptor.Evaluator.GetThisReference(ctx);

            var exp = Adaptor.Evaluator.GetCurrentException(ctx);
            if (exp != null)
                finfo.Exception = new ExceptionInfoSource<TType, TValue>(ctx, exp);

            frameInfo[frameIndex] = finfo;

            return finfo;
        }
    }

    class FrameInfo<TType, TValue>
        where TType : class
        where TValue : class
    {
        public EvaluationContext Context;
        public List<ValueReference<TType, TValue>> LocalVariables = new List<ValueReference<TType, TValue>>();
        public List<ValueReference<TType, TValue>> Parameters = new List<ValueReference<TType, TValue>>();
        public ValueReference<TType, TValue> This;
        public ExceptionInfoSource<TType, TValue> Exception;
    }
}
