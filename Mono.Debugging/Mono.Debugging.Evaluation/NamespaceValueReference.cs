// NamespaceValueReference.cs
//
// Author:
//   Lluis Sanchez Gual <lluis@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
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
using Mono.Debugging.Backend;
using DC = Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class NamespaceValueReference<TType, TValue> : ValueReference<TType, TValue>
        where TType : class
        where TValue : class
    {
        readonly string namspace;
        readonly string name;

        public NamespaceValueReference(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx,
            string name)
            : base(adaptor, ctx)
        {
            namspace = name;

            int i = namspace.LastIndexOf('.');
            if (i != -1)
                this.name = namspace.Substring(i + 1);
            else
                this.name = namspace;
        }

        public override TValue Value
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override TType Type
        {
            get { throw new NotSupportedException(); }
        }

        public override IRawValue<TValue> ObjectValue
        {
            get { throw new NotSupportedException(); }
        }

        public override string Name
        {
            get { return name; }
        }

        public override DC.ObjectValueFlags Flags
        {
            get { return DC.ObjectValueFlags.Namespace; }
        }

        public override ValueReference<TType, TValue> GetChild(string name, DC.EvaluationOptions options)
        {
            string newNs = namspace + "." + name;

            var ctx = GetContext(options);
            var type = Adaptor.GetType(ctx, newNs);

            if (type != null)
                return new TypeValueReference<TType, TValue>(Adaptor, ctx, type);

            return new NamespaceValueReference<TType, TValue>(Adaptor, ctx, newNs);
        }

        public override DC.ObjectValue[] GetChildren(DC.ObjectPath path, int index, int count, DC.EvaluationOptions options)
        {
            var children = new List<DC.ObjectValue>();

            foreach (var val in GetChildReferences(options))
                children.Add(val.CreateObjectValue(options));

            return children.ToArray();
        }

        public override IEnumerable<ValueReference<TType, TValue>> GetChildReferences(DC.EvaluationOptions options)
        {
            // Child types
            string[] childNamespaces;
            string[] childTypes;

            var ctx = GetContext(options);
            Adaptor.GetNamespaceContents(ctx, namspace, out childNamespaces, out childTypes);

            var list = new List<ValueReference<TType, TValue>>();
            foreach (string typeName in childTypes)
            {
                TType tt = Adaptor.GetType(ctx, typeName);
                if (tt != null)
                    list.Add(new TypeValueReference<TType, TValue>(Adaptor, ctx, tt));
            }

            list.Sort((v1, v2) => string.Compare(v1.Name, v2.Name, StringComparison.CurrentCulture));

            // Child namespaces
            var listNs = new List<ValueReference<TType, TValue>>();
            foreach (string ns in childNamespaces)
                listNs.Add(new NamespaceValueReference<TType, TValue>(Adaptor, ctx, ns));

            listNs.Sort((v1, v2) => string.Compare(v1.Name, v2.Name, StringComparison.CurrentCulture));

            list.AddRange(listNs);

            return list;
        }

        protected override DC.ObjectValue OnCreateObjectValue(DC.EvaluationOptions options)
        {
            return DC.ObjectValue.CreateObject(this, new DC.ObjectPath(Name), "<namespace>", namspace, Flags, null);
        }

        public override string CallToString()
        {
            return namspace;
        }
    }
}
