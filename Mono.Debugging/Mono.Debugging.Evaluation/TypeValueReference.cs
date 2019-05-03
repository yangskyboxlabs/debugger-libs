// TypeValueReference.cs
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
using System.Diagnostics;
using System.Reflection;
using Mono.Debugging.Backend;
using MD = Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public class TypeValueReference<TType, TValue> : ValueReference<TType, TValue>
        where TType : class
        where TValue : class
    {
        readonly string fullName;
        readonly string name;
        readonly TType type;

        public TypeValueReference(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx, TType type)
            : base(adaptor, ctx)
        {
            this.type = type;
            fullName = adaptor.GetDisplayTypeName(ctx, type);
            name = GetTypeName(fullName);
        }

        internal static string GetTypeName(string tname)
        {
            tname = tname.Replace('+', '.');

            int sep1 = tname.IndexOf('<');
            int sep2 = tname.IndexOf('[');

            if (sep2 != -1 && (sep2 < sep1 || sep1 == -1))
                sep1 = sep2;

            if (sep1 == -1)
                sep1 = tname.Length - 1;

            int dot = tname.LastIndexOf('.', sep1);

            return dot != -1 ? tname.Substring(dot + 1) : tname;
        }

        public override TValue Value
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override TType Type
        {
            get { return type; }
        }

        public override IRawValue<TValue> ObjectValue
        {
            get { throw new NotSupportedException(); }
        }

        public override string Name
        {
            get { return name; }
        }

        public override MD.ObjectValueFlags Flags
        {
            get { return MD.ObjectValueFlags.Type; }
        }

        protected override MD.ObjectValue OnCreateObjectValue(MD.EvaluationOptions options)
        {
            return MD.ObjectValue.CreateObject(this, new MD.ObjectPath(Name), "<type>", fullName, Flags, null);
        }

        public override ValueReference<TType, TValue> GetChild(string name, MD.EvaluationOptions options)
        {
            var ctx = GetContext(options);

            foreach (var val in Adaptor.GetMembers(ctx, this, type, null))
            {
                if (val.Name == name)
                    return val;
            }

            foreach (var nestedType in Adaptor.GetNestedTypes(ctx, type))
            {
                string typeName = Adaptor.GetTypeName(ctx, nestedType);

                if (GetTypeName(typeName) == name)
                    return new TypeValueReference<TType, TValue>(Adaptor, ctx, nestedType);
            }

            return null;
        }

        public override MD.ObjectValue[] GetChildren(MD.ObjectPath path, int index, int count, MD.EvaluationOptions options)
        {
            var ctx = GetContext(options);

            try
            {
                BindingFlags flattenFlag = options.FlattenHierarchy ? (BindingFlags)0 : BindingFlags.DeclaredOnly;
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | flattenFlag;
                bool groupPrivateMembers = options.GroupPrivateMembers || Adaptor.IsExternalType(ctx, type);
                var list = new List<MD.ObjectValue>();

                if (!groupPrivateMembers)
                    flags |= BindingFlags.NonPublic;

                var tdata = Adaptor.GetTypeDisplayData(ctx, type);
                var tdataType = type;

                foreach (var val in Adaptor.GetMembersSorted(ctx, this, type, null, flags))
                {
                    var decType = val.DeclaringType;
                    if (decType != null && decType != tdataType)
                    {
                        tdataType = decType;
                        tdata = Adaptor.GetTypeDisplayData(ctx, decType);
                    }

                    var state = tdata.GetMemberBrowsableState(val.Name);
                    if (state == DebuggerBrowsableState.Never)
                        continue;

                    var oval = val.CreateObjectValue(options);
                    list.Add(oval);
                }

                var nestedTypes = new List<MD.ObjectValue>();
                foreach (var nestedType in Adaptor.GetNestedTypes(ctx, type))
                    nestedTypes.Add(new TypeValueReference<TType, TValue>(Adaptor, ctx, nestedType).CreateObjectValue(options));

                nestedTypes.Sort((v1, v2) => string.Compare(v1.Name, v2.Name, StringComparison.CurrentCulture));

                list.AddRange(nestedTypes);

                if (groupPrivateMembers)
                    list.Add(FilteredMembersSource.CreateNonPublicsNode(Adaptor, ctx, this, type, null, BindingFlags.NonPublic | BindingFlags.Static | flattenFlag));

                if (!options.FlattenHierarchy)
                {
                    TType baseType = Adaptor.GetBaseType(ctx, type, false);
                    if (baseType != null)
                    {
                        var baseRef = new TypeValueReference<TType, TValue>(Adaptor, ctx, baseType);
                        var baseVal = baseRef.CreateObjectValue(false);
                        baseVal.Name = "base";
                        list.Insert(0, baseVal);
                    }
                }

                return list.ToArray();
            }
            catch (Exception ex)
            {
                ctx.WriteDebuggerOutput(ex.Message);
                return new MD.ObjectValue [0];
            }
        }

        public override IEnumerable<ValueReference<TType, TValue>> GetChildReferences(MD.EvaluationOptions options)
        {
            var ctx = GetContext(options);

            try
            {
                var list = new List<ValueReference<TType, TValue>>();

                list.AddRange(Adaptor.GetMembersSorted(ctx, this, type, null, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));

                var nestedTypes = new List<ValueReference<TType, TValue>>();
                foreach (var nestedType in Adaptor.GetNestedTypes(ctx, type))
                    nestedTypes.Add(new TypeValueReference<TType, TValue>(Adaptor, ctx, nestedType));

                nestedTypes.Sort((v1, v2) => string.Compare(v1.Name, v2.Name, StringComparison.CurrentCulture));
                list.AddRange(nestedTypes);

                return list;
            }
            catch (Exception ex)
            {
                ctx.WriteDebuggerOutput(ex.Message);
                return new ValueReference<TType, TValue>[0];
            }
        }
    }
}
