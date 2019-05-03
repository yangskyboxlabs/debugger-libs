//
// IEnumerableSource.cs
//
// Author:
//       David Karlaš <david.karlas@xamarin.com>
//
// Copyright (c) 2014 Xamarin, Inc (http://www.xamarin.com)
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

namespace Mono.Debugging.Evaluation
{
    class EnumerableSource<TType, TValue> : IObjectValueSource
        where TType : class
        where TValue : class
    {
        readonly TValue obj;
        readonly TType objType;
        readonly ObjectValueAdaptor<TType, TValue> adaptor;
        readonly EvaluationContext ctx;
        List<ObjectValue> elements;
        List<TValue> values;
        int currentIndex = 0;
        TValue enumerator;
        TType enumeratorType;

        public EnumerableSource(
            TValue source,
            TType type,
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx)
        {
            this.obj = source;
            this.objType = type;
            this.adaptor = adaptor;
            this.ctx = ctx;
        }

        public IDebuggerHierarchicalObject ParentSource { get; private set; }

        bool MoveNext()
        {
            return adaptor.Invocator.InvokeInstanceMethod(ctx, enumeratorType, enumerator, nameof(MoveNext), adaptor.EmptyTypeArray, adaptor.EmptyTypeArray)
                .Result
                .ToRawValue(adaptor, ctx)
                .ToPrimitive<bool>();
        }

        void Fetch(int maxIndex)
        {
            if (elements == null)
            {
                elements = new List<ObjectValue>();
                values = new List<TValue>();
                try
                {
                    enumerator = adaptor.Invocator.InvokeInstanceMethod(ctx, objType, obj, "GetEnumerator", adaptor.EmptyTypeArray, adaptor.EmptyTypeArray).Result;
                    enumeratorType = adaptor.GetImplementedInterfaces(ctx, adaptor.GetValueType(ctx, enumerator)).First(f => adaptor.GetTypeName(ctx, f) == "System.Collections.IEnumerator");
                }

//                catch (TargetInvokeDisabledException ex)
//                {
//                    this.elements.Add(ObjectValue.CreateTargetInvokeNotSupported((IObjectValueSource) this, ObjectPath.CreatePath((IDebuggerHierarchicalObject) this), "", ObjectValueFlags.NoRefresh, this.Context.Options, "Results"));
//                    return;
//                }
//                catch (CrossThreadDependencyRejectedException ex)
//                {
//                    this.elements.Add(ObjectValue.CreateCrossThreadDependencyRejected((IObjectValueSource) this, ObjectPath.CreatePath((IDebuggerHierarchicalObject) this), string.Empty, ObjectValueFlags.NoRefresh, "Results"));
//                    return;
//                }
//                catch (EvaluatorExceptionThrownExceptionBase ex)
//                {
//                    this.elements.Add(ObjectValue.CreateEvaluationException<TContext, TType, TValue>(this.adapter, ctx, (IObjectValueSource) this, new ObjectPath(new string[1]
//                    {
//                        "GetEnumerator()"
//                    }), ex, ObjectValueFlags.None));
//                    return;
//                }
                catch (EvaluatorAbortedException ex)
                {
                    elements.Add(ObjectValue.CreateFatalError("GetEnumerator", ex.Message, ObjectValueFlags.Error));
                    return;
                }
                catch (TimeOutException ex)
                {
                    elements.Add(ObjectValue.CreateFatalError("GetEnumerator", ex.Message, ObjectValueFlags.Error));
                    return;
                }
                catch (EvaluatorException ex)
                {
                    elements.Add(ObjectValue.CreateFatalError("GetEnumerator", ex.Message, ObjectValueFlags.Error));
                    return;
                }
            }

            while (maxIndex > elements.Count && !ctx.CancellationToken.IsCancellationRequested && MoveNext())
            {
                string name = "[" + currentIndex + "]";
                TValue val;
                ValueReference<TType, TValue> valCurrent;
                try
                {
                    valCurrent = adaptor.GetMember(ctx, null, enumeratorType, enumerator, "Current");
                    val = valCurrent.Value;
                }

//                catch (EvaluatorExceptionThrownExceptionBase ex)
//                {
//                    this.elements.Add(ObjectValue.CreateEvaluationException<TContext, TType, TValue>(this.adapter, ctx, (IObjectValueSource) this, new ObjectPath(new string[1]
//                    {
//                        name
//                    }), ex, ObjectValueFlags.None));
//                    ++this.currentIndex;
//                    break;
//                }
                catch (EvaluatorAbortedException ex)
                {
                    elements.Add(ObjectValue.CreateFatalError(name, ex.Message, ObjectValueFlags.Error));
                    break;
                }
                catch (TimeOutException ex)
                {
                    elements.Add(ObjectValue.CreateFatalError(name, ex.Message, ObjectValueFlags.Error));
                    ++currentIndex;
                    continue;
                }
                catch (EvaluatorException ex)
                {
                    elements.Add(ObjectValue.CreateFatalError("GetEnumerator", ex.Message, ObjectValueFlags.Error));
                    break;
                }

                values.Add(val);
                if (val != null)
                {
                    elements.Add(adaptor.CreateObjectValue(ctx, valCurrent, new ObjectPath(name), val, ObjectValueFlags.ReadOnly));
                }
                else
                {
                    elements.Add(ObjectValue.CreateNullObject(this, name, adaptor.GetDisplayTypeName(adaptor.GetTypeName(ctx, valCurrent.Type)), ObjectValueFlags.ReadOnly));
                }

                currentIndex++;
            }
        }

        public TValue GetElement(int idx)
        {
            return values[idx];
        }

        public ObjectValue[] GetChildren(ObjectPath path, int index, int count, EvaluationOptions options)
        {
            int idx;
            if (int.TryParse(path.LastName.Replace("[", "").Replace("]", ""), out idx))
            {
                return adaptor.GetObjectValueChildren(ctx, null, values[idx], -1, -1);
            }

            if (index < 0)
                index = 0;
            if (count == 0)
                return new ObjectValue[0];
            if (count == -1)
                count = int.MaxValue;
            Fetch(index + count);
            if (count < 0 || index + count > elements.Count)
            {
                return elements.Skip(index).ToArray();
            }
            else
            {
                if (index < elements.Count)
                {
                    return elements.Skip(index).Take(System.Math.Min(count, elements.Count - index)).ToArray();
                }
                else
                {
                    return new ObjectValue[0];
                }
            }
        }

        public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
        {
            throw new InvalidOperationException("Elements of IEnumerable can not be set");
        }

        public ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
        {
            int idx;
            if (int.TryParse(path.LastName.Replace("[", "").Replace("]", ""), out idx))
            {
                var element = elements[idx];
                element.Refresh(options);
                return element;
            }

            return null;
        }

        public IRawValue GetRawValue(ObjectPath path, EvaluationOptions options)
        {
            int idx = int.Parse(path.LastName.Replace("[", "").Replace("]", ""));
            EvaluationContext cctx = ctx.WithOptions(options);
            return adaptor.ToRawValue(cctx, new EnumerableObjectSource(this, idx), GetElement(idx));
        }

        public void SetRawValue(ObjectPath path, IRawValue value, EvaluationOptions options)
        {
            throw new InvalidOperationException("Elements of IEnumerable can not be set");
        }

        class EnumerableObjectSource : IObjectSource<TValue>
        {
            EnumerableSource<TType, TValue> enumerableSource;
            int idx;

            public EnumerableObjectSource(
                EnumerableSource<TType, TValue> enumerableSource,
                int idx)
            {
                this.enumerableSource = enumerableSource;
                this.idx = idx;
            }

            #region IObjectSource implementation

            public IDebuggerHierarchicalObject ParentSource => enumerableSource.ParentSource;

            public TValue Value
            {
                get => enumerableSource.GetElement(idx);
                set => throw new InvalidOperationException("Elements of IEnumerable can not be set");
            }

            public string Name => this.idx.ToString();

            #endregion
        }
    }
}
