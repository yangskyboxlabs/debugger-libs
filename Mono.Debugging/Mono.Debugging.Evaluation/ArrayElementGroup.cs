// ArrayElementGroup.cs
//
// Authors: Lluis Sanchez Gual <lluis@novell.com>
//          Jeffrey Stedfast <jeff@xamarin.com>
// 
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
// Copyright (c) 2012 Xamarin Inc. (http://www.xamarin.com)
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
//
//

using System;
using System.Collections.Generic;
using System.Text;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class ArrayElementGroup<TType, TValue> : RemoteFrameObject, IObjectValueSource
        where TType : class
        where TValue : class
    {
        readonly ICollectionAdaptor<TType, TValue> array;
        readonly EvaluationContext ctx;
        int[] baseIndices;
        int[] dimensions;
        int firstIndex;
        int lastIndex;

        const int MaxChildCount = 150;

        public ArrayElementGroup(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            ICollectionAdaptor<TType, TValue> array)
            : this(adaptor, ctx, array, new int [0]) { }

        public ArrayElementGroup(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            ICollectionAdaptor<TType, TValue> array,
            int[] baseIndices)
            : this(adaptor, ctx, array, baseIndices, 0, -1) { }

        public ArrayElementGroup(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            ICollectionAdaptor<TType, TValue> array,
            int[] baseIndices,
            int firstIndex,
            int lastIndex)
        {
            Adaptor = adaptor;
            this.array = array;
            this.ctx = ctx;
            this.dimensions = array.GetDimensions();
            this.baseIndices = baseIndices;
            this.firstIndex = firstIndex;
            this.lastIndex = lastIndex;
        }

        public ObjectValueAdaptor<TType, TValue> Adaptor { get; }

        public IDebuggerHierarchicalObject ParentSource { get; }

        public bool IsRange
        {
            get { return lastIndex != -1; }
        }

        public ObjectValue CreateObjectValue()
        {
            Connect();

            var sb = new StringBuilder("[");

            for (int i = 0; i < baseIndices.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(baseIndices[i].ToString());
            }

            if (IsRange)
            {
                if (baseIndices.Length > 0)
                    sb.Append(", ");

                sb.Append(firstIndex.ToString()).Append("..").Append(lastIndex.ToString());
            }

            if (dimensions.Length > 1 && baseIndices.Length < dimensions.Length)
                sb.Append(", ...");

            sb.Append("]");

            ObjectValue res = ObjectValue.CreateObject(this, new ObjectPath(sb.ToString()), "", "", ObjectValueFlags.ArrayElement | ObjectValueFlags.ReadOnly | ObjectValueFlags.NoRefresh, null);
            res.ChildSelector = "";
            return res;
        }

        public ObjectValue[] GetChildren(EvaluationOptions options)
        {
            return GetChildren(new ObjectPath("this"), -1, -1, options);
        }

        public ObjectValue[] GetChildren(ObjectPath path, int firstItemIndex, int count, EvaluationOptions options)
        {
            EvaluationContext cctx = ctx.WithOptions(options);
            if (path.Length > 1)
            {
                // Looking for children of an array element
                string lastName = path.LastName;
                if (ArrayValueUtil.IsIndex(lastName))
                {
                    int[] idx = StringToIndices(path[1]);
                    TValue obj = array.GetElement(idx);
                    return Adaptor.GetObjectValueChildren(cctx, new ArrayObjectSource<TType, TValue>(array, path[1]), obj, firstItemIndex, count);
                }
            }

            int lowerBound;
            int upperBound;
            bool isLastDimension;

            if (dimensions.Length > 1)
            {
                int rank = baseIndices.Length;
                lowerBound = array.GetLowerBounds()[rank];
                upperBound = lowerBound + dimensions[rank] - 1;
                isLastDimension = rank == dimensions.Length - 1;
            }
            else
            {
                lowerBound = array.GetLowerBounds()[0];
                upperBound = lowerBound + dimensions[0] - 1;
                isLastDimension = true;
            }

            int len;
            int initalIndex;

            if (!IsRange)
            {
                initalIndex = lowerBound;
                len = upperBound + 1 - lowerBound;
            }
            else
            {
                initalIndex = firstIndex;
                len = lastIndex - firstIndex + 1;
            }

            if (firstItemIndex == -1)
            {
                firstItemIndex = 0;
                count = len;
            }

            // Make sure the group doesn't have too many elements. If so, divide
            int div = 1;
            while (len / div > MaxChildCount)
                div *= 10;

            if (div == 1 && isLastDimension)
            {
                // Return array elements

                ObjectValue[] values = new ObjectValue [count];
                ObjectPath newPath = new ObjectPath("this");

                int[] curIndex = new int [baseIndices.Length + 1];
                Array.Copy(baseIndices, curIndex, baseIndices.Length);
                string curIndexStr = IndicesToString(baseIndices);
                if (baseIndices.Length > 0)
                    curIndexStr += ",";
                curIndex[curIndex.Length - 1] = initalIndex + firstItemIndex;
                TValue[] elems = array.GetElements(curIndex, System.Math.Min(values.Length, upperBound - lowerBound + 1));

                for (int n = 0; n < values.Length; n++)
                {
                    int index = n + initalIndex + firstItemIndex;
                    string sidx = curIndexStr + index;
                    ObjectValue val;
                    string ename = "[" + sidx.Replace(",", ", ") + "]";
                    if (index > upperBound)
                        val = ObjectValue.CreateUnknown(sidx);
                    else
                    {
                        curIndex[curIndex.Length - 1] = index;
                        val = Adaptor.CreateObjectValue(cctx, this, newPath.Append(sidx), elems[n], ObjectValueFlags.ArrayElement);
                        if (elems.GetValue(n) != null && !Adaptor.IsNull(cctx, elems[n]))
                        {
                            TypeDisplayData tdata = Adaptor.GetTypeDisplayData(cctx, Adaptor.GetValueType(cctx, elems[n]));
                            if (!string.IsNullOrEmpty(tdata.NameDisplayString))
                            {
                                try
                                {
                                    ename = Adaptor.EvaluateDisplayString(cctx, elems[n], tdata.NameDisplayString);
                                }
                                catch (MissingMemberException)
                                {
                                    // missing property or otherwise malformed DebuggerDisplay string
                                }
                            }
                        }
                    }

                    val.Name = ename;
                    values[n] = val;
                }

                return values;
            }

            if (!isLastDimension && div == 1)
            {
                // Return an array element group for each index

                var list = new List<ObjectValue>();
                for (int i = 0; i < count; i++)
                {
                    int index = i + initalIndex + firstItemIndex;
                    ObjectValue val;

                    // This array must be created at every call to avoid sharing
                    // changes with all array groups
                    int[] curIndex = new int [baseIndices.Length + 1];
                    Array.Copy(baseIndices, curIndex, baseIndices.Length);
                    curIndex[curIndex.Length - 1] = index;

                    if (index > upperBound)
                        val = ObjectValue.CreateUnknown("");
                    else
                    {
                        ArrayElementGroup<TType, TValue> grp = new ArrayElementGroup<TType, TValue>(Adaptor, cctx, array, curIndex);
                        val = grp.CreateObjectValue();
                    }

                    list.Add(val);
                }

                return list.ToArray();
            }
            else
            {
                // Too many elements. Split the array.

                // Don't make divisions of 10 elements, min is 100
                if (div == 10)
                    div = 100;

                // Create the child groups
                int i = initalIndex + firstItemIndex;
                len += i;
                var list = new List<ObjectValue>();
                while (i < len)
                {
                    int end = i + div - 1;
                    if (end >= len)
                        end = len - 1;
                    ArrayElementGroup<TType, TValue> grp = new ArrayElementGroup<TType, TValue>(Adaptor, cctx, array, baseIndices, i, end);
                    list.Add(grp.CreateObjectValue());
                    i += div;
                }

                return list.ToArray();
            }
        }

        internal static string IndicesToString(int[] indices)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < indices.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(indices[i].ToString());
            }

            return sb.ToString();
        }

        internal static int[] StringToIndices(string str)
        {
            var sidx = str.Split(',');
            var idx = new int [sidx.Length];

            for (int i = 0; i < sidx.Length; i++)
                idx[i] = int.Parse(sidx[i]);

            return idx;
        }

        public static string GetArrayDescription(int[] bounds)
        {
            if (bounds.Length == 0)
                return "[...]";

            var sb = new StringBuilder("[");

            for (int i = 0; i < bounds.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(bounds[i].ToString());
            }

            sb.Append("]");

            return sb.ToString();
        }

        public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
        {
            if (path.Length != 2)
                throw new NotSupportedException();

            int[] idx = StringToIndices(path[1]);

            TValue val;
            try
            {
                EvaluationContext cctx = ctx.Clone();
                EvaluationOptions ops = options ?? cctx.Options;
                ops.AllowMethodEvaluation = true;
                ops.AllowTargetInvoke = true;
                cctx.Options = ops;
                ValueReference<TType, TValue> var = Adaptor.Evaluator.Evaluate(ctx, value, array.ElementType);
                val = var.Value;
                val = Adaptor.Convert(ctx, val, array.ElementType);
                array.SetElement(idx, val);
            }
            catch
            {
                val = array.GetElement(idx);
            }

            try
            {
                return Adaptor.Evaluator.TargetObjectToEvaluationResult(ctx, val);
            }
            catch (Exception ex)
            {
                ctx.WriteDebuggerError(ex);
                return new EvaluationResult("? (" + ex.Message + ")");
            }
        }

        public ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
        {
            if (path.Length != 2)
                throw new NotSupportedException();

            int[] idx = StringToIndices(path[1]);
            TValue elem = array.GetElement(idx);
            EvaluationContext cctx = ctx.WithOptions(options);
            ObjectValue val = Adaptor.CreateObjectValue(cctx, this, path, elem, ObjectValueFlags.ArrayElement);
            if (elem != null && !Adaptor.IsNull(cctx, elem))
            {
                TypeDisplayData tdata = Adaptor.GetTypeDisplayData(cctx, Adaptor.GetValueType(cctx, elem));
                if (!string.IsNullOrEmpty(tdata.NameDisplayString))
                {
                    try
                    {
                        val.Name = Adaptor.EvaluateDisplayString(cctx, elem, tdata.NameDisplayString);
                    }
                    catch (MissingMemberException)
                    {
                        // missing property or otherwise malformed DebuggerDisplay string
                    }
                }
            }

            return val;
        }

        public IRawValue GetRawValue(ObjectPath path, EvaluationOptions options)
        {
            if (path.Length != 2)
                throw new NotSupportedException();

            int[] idx = StringToIndices(path[1]);
            TValue elem = array.GetElement(idx);
            EvaluationContext cctx = ctx.WithOptions(options);
            return Adaptor.ToRawValue(cctx, new ArrayObjectSource<TType, TValue>(array, idx, ParentSource), elem);
        }

        public void SetRawValue(ObjectPath path, IRawValue value, EvaluationOptions options)
        {
            if (path.Length != 2)
                throw new NotSupportedException();

            int[] idx = StringToIndices(path[1]);
            ValueModification.ModifyValueFromRaw<TType, TValue>(value, val => array.SetElement(idx, val));
        }
    }

    class ArrayObjectSource<TType, TValue> : IObjectSource<TValue>
        where TType : class
        where TValue : class
    {
        readonly ICollectionAdaptor<TType, TValue> source;
        readonly string path;

        public ArrayObjectSource(ICollectionAdaptor<TType, TValue> source, string path)
        {
            this.source = source;
            this.path = path;
        }

        public ArrayObjectSource(
            ICollectionAdaptor<TType, TValue> source,
            int[] index,
            IDebuggerHierarchicalObject parentSource)
        {
            this.source = source;
            this.path = ArrayElementGroup<TType, TValue>.IndicesToString(index);
            ParentSource = parentSource;
        }

        public IDebuggerHierarchicalObject ParentSource { get; private set; }

        public TValue Value
        {
            get => source.GetElement(ArrayElementGroup<TType, TValue>.StringToIndices(path));
            set => source.SetElement(ArrayElementGroup<TType, TValue>.StringToIndices(path), value);
        }

        public string Name => path;
    }
}
