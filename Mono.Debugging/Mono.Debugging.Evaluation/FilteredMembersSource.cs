// 
// FilteredMembersSource.cs
//  
// Authors: Lluis Sanchez Gual <lluis@novell.com>
//          Jeffrey Stedfast <jeff@xamarin.com>
// 
// Copyright (c) 2009 Novell, Inc (http://www.novell.com)
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class FilteredMembersSource<TType, TValue> : RemoteFrameObject, IObjectValueSource
        where TType : class
        where TValue : class
    {
        readonly TValue obj;
        readonly TType type;
        readonly EvaluationContext ctx;
        readonly BindingFlags bindingFlags;
        readonly IObjectSource<TValue> objectSource;
        readonly ObjectValueAdaptor<TType, TValue> adaptor;

        public FilteredMembersSource(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue obj,
            BindingFlags bindingFlags)
        {
            this.adaptor = adaptor;
            this.ctx = ctx;
            this.obj = obj;
            this.type = type;
            this.bindingFlags = bindingFlags;
            this.objectSource = objectSource;
        }

        public ObjectValue[] GetChildren(
            ObjectPath path,
            int index,
            int count,
            EvaluationOptions options)
        {
            EvaluationContext cctx = ctx.WithOptions(options);
            var names = new ObjectValueNameTracker<TType, TValue>(adaptor, cctx);
            TType tdataType = null;
            TypeDisplayData tdata = null;
            List<ObjectValue> list = new List<ObjectValue>();
            foreach (ValueReference<TType, TValue> val in adaptor.GetMembersSorted(cctx, objectSource, type, obj, bindingFlags))
            {
                TType decType = val.DeclaringType;
                if (decType != null && decType != tdataType)
                {
                    tdataType = decType;
                    tdata = adaptor.GetTypeDisplayData(cctx, decType);
                }

                DebuggerBrowsableState state = tdata.GetMemberBrowsableState(val.Name);
                if (state == DebuggerBrowsableState.Never)
                    continue;
                ObjectValue oval = val.CreateObjectValue(options);
                names.Disambiguate(val, oval);
                list.Add(oval);
            }

            if ((bindingFlags & BindingFlags.NonPublic) == 0)
            {
                BindingFlags newFlags = bindingFlags | BindingFlags.NonPublic;
                newFlags &= ~BindingFlags.Public;
                list.Add(FilteredMembersSource.CreateNonPublicsNode(adaptor, cctx, objectSource, type, obj, newFlags));
            }

            return list.ToArray();
        }

        public ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
        {
            throw new NotSupportedException();
        }

        public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
        {
            throw new NotSupportedException();
        }

        public IRawValue GetRawValue(ObjectPath path, EvaluationOptions options)
        {
            throw new NotImplementedException();
        }

        public void SetRawValue(ObjectPath path, IRawValue value, EvaluationOptions options)
        {
            throw new NotImplementedException();
        }
    }

    public static class FilteredMembersSource
    {
        public static ObjectValue CreateNonPublicsNode<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue obj,
            BindingFlags bindingFlags)
            where TType : class
            where TValue : class
        {
            return CreateNode(adaptor, ctx, objectSource, type, obj, bindingFlags, "Non-public members");
        }

        public static ObjectValue CreateStaticsNode<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue obj,
            BindingFlags bindingFlags)
            where TType : class
            where TValue : class
        {
            return CreateNode(adaptor, ctx, objectSource, type, obj, bindingFlags, "Static members");
        }

        static ObjectValue CreateNode<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TType type,
            TValue obj,
            BindingFlags bindingFlags,
            string label)
            where TType : class
            where TValue : class
        {
            FilteredMembersSource<TType, TValue> src = new FilteredMembersSource<TType, TValue>(adaptor, ctx, objectSource, type, obj, bindingFlags);
            src.Connect();
            ObjectValue val = ObjectValue.CreateObject(src, new ObjectPath(label), "", "", ObjectValueFlags.Group | ObjectValueFlags.ReadOnly | ObjectValueFlags.NoRefresh, null);
            val.ChildSelector = "";
            return val;
        }
    }
}
