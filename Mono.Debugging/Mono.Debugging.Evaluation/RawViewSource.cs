// RawViewSource.cs
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
using Mono.Debugging.Backend;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class RawViewSource<TType, TValue> : RemoteFrameObject, IObjectValueSource
        where TType : class
        where TValue : class
    {
        readonly TValue obj;
        readonly EvaluationContext ctx;
        readonly IObjectSource<TValue> objectSource;
        readonly ObjectValueAdaptor<TType, TValue> adaptor;

        public RawViewSource(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TValue obj)
        {
            this.adaptor = adaptor;
            this.ctx = ctx;
            this.obj = obj;
            this.objectSource = objectSource;
        }

        public ObjectValue[] GetChildren(ObjectPath path, int index, int count, EvaluationOptions options)
        {
            EvaluationContext cctx = ctx.WithOptions(options);
            return adaptor.GetObjectValueChildren(cctx, objectSource, adaptor.GetValueType(cctx, obj), obj, index, count, false);
        }

        public ObjectValue GetValue(ObjectPath path, EvaluationOptions options)
        {
            throw new NotSupportedException();
        }

        public EvaluationResult SetValue(ObjectPath path, string value, EvaluationOptions options)
        {
            throw new NotSupportedException();
        }

        public void SetRawValue(ObjectPath path, IRawValue value, EvaluationOptions options)
        {
            throw new NotImplementedException();
        }

        public IRawValue GetRawValue(ObjectPath path, EvaluationOptions options)
        {
            throw new NotImplementedException();
        }
    }

    public static class RawViewSource
    {
        public static ObjectValue CreateRawView<TType, TValue>(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            IObjectSource<TValue> objectSource,
            TValue obj)
            where TType : class
            where TValue : class
        {
            RawViewSource<TType, TValue> src = new RawViewSource<TType, TValue>(adaptor, ctx, objectSource, obj);
            src.Connect();
            ObjectValue val = ObjectValue.CreateObject(src, new ObjectPath("Raw View"), "", "", ObjectValueFlags.Group | ObjectValueFlags.ReadOnly | ObjectValueFlags.NoRefresh, null);
            val.ChildSelector = "";
            return val;
        }
    }
}
