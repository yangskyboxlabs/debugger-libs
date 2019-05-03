// ValueReference.cs
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
    public abstract class ValueReference<TType, TValue> : RemoteFrameObject, IObjectValueSource, IObjectSource<TValue>
        where TType : class
        where TValue : class
    {
        readonly DC.EvaluationOptions originalOptions;

        protected ValueReference(
            ObjectValueAdaptor<TType, TValue> adaptor,
            EvaluationContext ctx)
        {
            Adaptor = adaptor;
            originalOptions = ctx.Options;
            Context = ctx;
        }

        public ObjectValueAdaptor<TType, TValue> Adaptor { get; }

        public virtual IRawValue<TValue> ObjectValue => Adaptor.ToRawValue(Context, this, Value);

        public abstract TValue Value { get; set; }
        public abstract string Name { get; }
        public abstract TType Type { get; }
        public abstract DC.ObjectValueFlags Flags { get; }

        // For class members, the type declaring the member (null otherwise)
        public virtual TType DeclaringType
        {
            get { return null; }
        }

        public EvaluationContext Context { get; set; }

        public EvaluationContext GetContext(DC.EvaluationOptions options)
        {
            return Context.WithOptions(options);
        }

        public DC.ObjectValue CreateObjectValue(bool withTimeout)
        {
            return CreateObjectValue(withTimeout, Context.Options);
        }

        public DC.ObjectValue CreateObjectValue(bool withTimeout, DC.EvaluationOptions options)
        {
            if (!CanEvaluate(options))
                return DC.ObjectValue.CreateImplicitNotSupported(this, new DC.ObjectPath(Name), Adaptor.GetDisplayTypeName(GetContext(options), Type), Flags);

            if (withTimeout)
            {
                return Adaptor.CreateObjectValueAsync(Name, Flags, delegate
                {
                    return CreateObjectValue(options);
                });
            }

            return CreateObjectValue(options);
        }

        public DC.ObjectValue CreateObjectValue(DC.EvaluationOptions options)
        {
            if (!CanEvaluate(options))
            {
                if (options.AllowTargetInvoke) //If it can't evaluate and target invoke is allowed, mark it as not supported.
                    return DC.ObjectValue.CreateNotSupported(this, new DC.ObjectPath(Name), Adaptor.GetDisplayTypeName(GetContext(options), Type), "Can not evaluate", Flags);
                return DC.ObjectValue.CreateImplicitNotSupported(this, new DC.ObjectPath(Name), Adaptor.GetDisplayTypeName(GetContext(options), Type), Flags);
            }

            Connect();
            try
            {
                return OnCreateObjectValue(options);
            }
            catch (ImplicitEvaluationDisabledException)
            {
                return DC.ObjectValue.CreateImplicitNotSupported(this, new DC.ObjectPath(Name), Adaptor.GetDisplayTypeName(GetContext(options), Type), Flags);
            }
            catch (NotSupportedExpressionException ex)
            {
                return DC.ObjectValue.CreateNotSupported(this, new DC.ObjectPath(Name), Adaptor.GetDisplayTypeName(GetContext(options), Type), ex.Message, Flags);
            }
            catch (EvaluatorException ex)
            {
                return DC.ObjectValue.CreateError(this, new DC.ObjectPath(Name), Adaptor.GetDisplayTypeName(GetContext(options), Type), ex.Message, Flags);
            }
            catch (Exception ex)
            {
                Context.WriteDebuggerError(ex);
                return DC.ObjectValue.CreateUnknown(Name);
            }
        }

        protected virtual bool CanEvaluate(DC.EvaluationOptions options)
        {
            return true;
        }

        protected virtual DC.ObjectValue OnCreateObjectValue(DC.EvaluationOptions options)
        {
            string name = Name;
            if (string.IsNullOrEmpty(name))
                name = "?";

            var ctx = GetContext(options);
            TValue val = null;

            // Note: The Value property implementation may make use of the EvaluationOptions,
            // so we need to override our context temporarily to do the evaluation.
            val = GetValue(ctx);

            if (val != null && !Adaptor.IsNull(ctx, val))
                return Adaptor.CreateObjectValue(ctx, this, new DC.ObjectPath(name), val, Flags);

            return DC.ObjectValue.CreateNullObject(this, name, Adaptor.GetDisplayTypeName(Adaptor.GetTypeName(ctx, Type)), Flags);
        }

        DC.ObjectValue IObjectValueSource.GetValue(DC.ObjectPath path, DC.EvaluationOptions options)
        {
            return CreateObjectValue(true, options);
        }

        EvaluationResult IObjectValueSource.SetValue(
            DC.ObjectPath path,
            string value,
            DC.EvaluationOptions options)
        {
            var ctx = GetContext(options);
            return ValueModification.ModifyValue(Adaptor, Adaptor.DebuggerSession.Evaluators().GetEvaluator(ctx), ctx, value, Type, newVal => SetValue(ctx, newVal));

//            try
//            {
//                Context.WaitRuntimeInvokes();
//
//                var ctx = GetContext(options);
//                ctx.Options.AllowMethodEvaluation = true;
//                ctx.Options.AllowTargetInvoke = true;
//
//                var vref = Adaptor.Evaluator.Evaluate(ctx, value, Type);
//                var newValue = Adaptor.Convert(ctx, vref.Value, Type);
//                SetValue(ctx, newValue);
//            }
//            catch (Exception ex)
//            {
//                Context.WriteDebuggerError(ex);
//                Context.WriteDebuggerOutput("Value assignment failed: {0}: {1}\n", ex.GetType(), ex.Message);
//            }
//
//            try
//            {
//                return ValueModification.ModifyValue(Adaptor, Context.Evaluators.GetEvaluator(Context), Context, value, Type, newVal => SetValue(Context, newVal));
//            }
//            catch (Exception ex)
//            {
//                Context.WriteDebuggerError(ex);
//                Context.WriteDebuggerOutput("Value assignment failed: {0}: {1}\n", ex.GetType(), ex.Message);
//            }
//
//            return null;
        }

        IRawValue IObjectValueSource.GetRawValue(DC.ObjectPath path, DC.EvaluationOptions options)
        {
            var ctx = GetContext(options);

            return Adaptor.ToRawValue(ctx, this, GetValue(ctx));
        }

        void IObjectValueSource.SetRawValue(DC.ObjectPath path, IRawValue value, DC.EvaluationOptions options)
        {
            SetRawValue(path, value, options);
        }

        protected virtual void SetRawValue(DC.ObjectPath path, IRawValue value, DC.EvaluationOptions options)
        {
            var ctx = GetContext(options);

            ValueModification.ModifyValueFromRaw(Adaptor, ctx, value, val => SetValue(ctx, val));
        }

        DC.ObjectValue[] IObjectValueSource.GetChildren(DC.ObjectPath path, int index, int count, DC.EvaluationOptions options)
        {
            return GetChildren(path, index, count, options);
        }

        public virtual string CallToString()
        {
            return Adaptor.CallToString(Context, Value);
        }

        public virtual TValue GetValue(EvaluationContext ctx)
        {
            return Value;
        }

        public virtual void SetValue(EvaluationContext ctx, TValue value)
        {
            Value = value;
        }

        [Obsolete("Use GetValue(EvaluationContext) instead.")]
        protected virtual object GetValueExplicitly()
        {
            var options = Context.Options.Clone();
            options.AllowTargetInvoke = true;
            var ctx = GetContext(options);

            return GetValue(ctx);
        }

        public virtual DC.ObjectValue[] GetChildren(DC.ObjectPath path, int index, int count, DC.EvaluationOptions options)
        {
            try
            {
                var ctx = GetChildrenContext(options);

                return Adaptor.GetObjectValueChildren(ctx, this, GetValue(ctx), index, count);
            }
            catch (Exception ex)
            {
                return new[] { DC.ObjectValue.CreateFatalError("", ex.Message, DC.ObjectValueFlags.ReadOnly) };
            }
        }

        public virtual IEnumerable<ValueReference<TType, TValue>> GetChildReferences(DC.EvaluationOptions options)
        {
            try
            {
                TValue val = Value;
                if (Adaptor.IsClassInstance(Context, val))
                    return Adaptor.GetMembersSorted(GetChildrenContext(options), this, Type, val);
            }
            catch
            {
                // Ignore
            }

            return new ValueReference<TType, TValue> [0];
        }

        public IDebuggerHierarchicalObject ParentSource { get; set; }

        protected EvaluationContext GetChildrenContext(DC.EvaluationOptions options)
        {
            var ctx = Context.Clone();

            if (options != null)
                ctx.Options = options;

            ctx.Options.EvaluationTimeout = originalOptions.MemberEvaluationTimeout;

            return ctx;
        }

        public virtual ValueReference<TType, TValue> GetChild(DC.ObjectPath vpath, DC.EvaluationOptions options)
        {
            if (vpath.Length == 0)
                return this;

            var val = GetChild(vpath[0], options);

            return val != null ? val.GetChild(vpath.GetSubpath(1), options) : null;
        }

        public virtual ValueReference<TType, TValue> GetChild(string name, DC.EvaluationOptions options)
        {
            TValue obj = Value;

            if (obj == null)
                return null;

            if (name[0] == '[' && Adaptor.IsArray(Context, obj))
            {
                // Parse the array indices
                var tokens = name.Substring(1, name.Length - 2).Split(',');
                var indices = new int [tokens.Length];

                for (int n = 0; n < tokens.Length; n++)
                    indices[n] = int.Parse(tokens[n]);

                return new ArrayValueReference<TType, TValue>(Adaptor, Context, obj, indices);
            }

            if (Adaptor.IsClassInstance(Context, obj))
                return Adaptor.GetMember(GetChildrenContext(options), this, Type, obj, name);

            return null;
        }
    }
}
