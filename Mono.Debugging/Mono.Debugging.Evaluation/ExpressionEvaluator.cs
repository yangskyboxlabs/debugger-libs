// IExpressionEvaluator.cs
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
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using Mono.Debugging.Backend;
using Mono.Debugging.Client;

namespace Mono.Debugging.Evaluation
{
    public abstract class ExpressionEvaluator<TType, TValue>
        where TType : class
        where TValue : class
    {
        protected ObjectValueAdaptor<TType, TValue> Adaptor { get; }

        public ExpressionEvaluator(ObjectValueAdaptor<TType, TValue> adaptor)
        {
            Adaptor = adaptor;
        }

        public ValueReference<TType, TValue> Evaluate(EvaluationContext ctx, string exp)
        {
            return Evaluate(ctx, exp, null);
        }

        public virtual ValueReference<TType, TValue> Evaluate(EvaluationContext ctx, string exp, TType expectedType)
        {
            foreach (ValueReference<TType, TValue> var in Adaptor.GetLocalVariables(ctx))
                if (var.Name == exp)
                    return var;

            foreach (ValueReference<TType, TValue> var in Adaptor.GetParameters(ctx))
                if (var.Name == exp)
                    return var;

            ValueReference<TType, TValue> thisVar = Adaptor.GetThisReference(ctx);
            if (thisVar != null)
            {
                if (thisVar.Name == exp)
                    return thisVar;
                foreach (ValueReference<TType, TValue> cv in thisVar.GetChildReferences(ctx.Options))
                    if (cv.Name == exp)
                        return cv;
            }

            throw new EvaluatorException("Invalid Expression: '{0}'", exp);
        }

        public virtual ValidationResult ValidateExpression(EvaluationContext ctx, string expression)
        {
            return new ValidationResult(true, null);
        }

        public string TargetObjectToString(EvaluationContext ctx, TValue obj)
        {
            object res = Adaptor.TargetObjectToObject(ctx, obj);
            if (res == null)
                return null;

            if (res is EvaluationResult)
                return ((EvaluationResult)res).DisplayValue ?? ((EvaluationResult)res).Value;
            else
                return res.ToString();
        }

        public EvaluationResult TargetObjectToEvaluationResult(EvaluationContext ctx, TValue obj)
        {
            return ToExpression(ctx, Adaptor.TargetObjectToObject(ctx, obj));
        }

        public virtual EvaluationResult ToExpression(EvaluationContext ctx, object obj)
        {
            if (obj == null)
                return new EvaluationResult("null", StringPresentationKind.Null);
            if (obj is IntPtr p)
            {
                return new EvaluationResult("0x" + p.ToInt64().ToString("x"));
            }

            if (obj is char c)
            {
                string str;
                if (c == '\'')
                    str = @"'\''";
                else if (c == '"')
                    str = "'\"'";
                else
                    str = EscapeString("'" + c + "'");
                return new EvaluationResult(str, (int)c + " " + str);
            }

            if (obj is string s)
                return new EvaluationResult("\"" + EscapeString(s) + "\"");
            if (obj is bool b)
                return new EvaluationResult(b ? "true" : "false");
            if (obj is decimal d)
                return new EvaluationResult(d.ToString(CultureInfo.InvariantCulture));
            if (obj is EvaluationResult evaluationResult)
                return evaluationResult;

            if (ctx.Options.IntegerDisplayFormat == IntegerDisplayFormat.Hexadecimal)
            {
                string hexadecimalRespresentation = GetIntHexadecimalRepresentation(obj);
                if (hexadecimalRespresentation != null)
                    return new EvaluationResult(obj.ToString(), null, StringPresentationKind.Raw, "0x" + hexadecimalRespresentation);
            }

            return new EvaluationResult(obj.ToString());
        }

        static string GetIntHexadecimalRepresentation(object obj)
        {
            if (obj == null)
            {
                return null;
            }

            switch (obj)
            {
                case sbyte sb:
                    return sb.ToString("X", CultureInfo.InvariantCulture);
                case int i:
                    return i.ToString("X", CultureInfo.InvariantCulture);
                case short s:
                    return s.ToString("X", CultureInfo.InvariantCulture);
                case long l:
                    return l.ToString("X", CultureInfo.InvariantCulture);
                case byte b:
                    return b.ToString("X", CultureInfo.InvariantCulture);
                case uint ui:
                    return ui.ToString("X", CultureInfo.InvariantCulture);
                case ushort us:
                    return us.ToString("X", CultureInfo.InvariantCulture);
                case ulong ul:
                    return ul.ToString("X", CultureInfo.InvariantCulture);
            }

            return null;
        }

        public static string EscapeString(string text)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                string txt;
                switch (c)
                {
                    case '"':
                        txt = "\\\"";
                        break;
                    case '\0':
                        txt = @"\0";
                        break;
                    case '\\':
                        txt = @"\\";
                        break;
                    case '\a':
                        txt = @"\a";
                        break;
                    case '\b':
                        txt = @"\b";
                        break;
                    case '\f':
                        txt = @"\f";
                        break;
                    case '\v':
                        txt = @"\v";
                        break;
                    case '\n':
                        txt = @"\n";
                        break;
                    case '\r':
                        txt = @"\r";
                        break;
                    case '\t':
                        txt = @"\t";
                        break;
                    default:
                        if (char.GetUnicodeCategory(c) == UnicodeCategory.OtherNotAssigned)
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        continue;
                }

                sb.Append(txt);
            }

            return sb.ToString();
        }

        public virtual bool CaseSensitive
        {
            get { return true; }
        }

        public virtual IEnumerable<ValueReference<TType, TValue>> GetLocalVariables(EvaluationContext ctx)
        {
            return Adaptor.GetLocalVariables(ctx);
        }

        public virtual ValueReference<TType, TValue> GetThisReference(EvaluationContext ctx)
        {
            return Adaptor.GetThisReference(ctx);
        }

        public virtual IEnumerable<ValueReference<TType, TValue>> GetParameters(EvaluationContext ctx)
        {
            return Adaptor.GetParameters(ctx);
        }

        public virtual ValueReference<TType, TValue> GetCurrentException(EvaluationContext ctx)
        {
            return Adaptor.GetCurrentException(ctx);
        }
    }

    [Serializable]
    public class EvaluatorException : Exception
    {
        protected EvaluatorException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }

        public EvaluatorException(string msg, params object[] args)
            : base(string.Format(msg, args)) { }

        public EvaluatorException(Exception innerException, string msg, params object[] args)
            : base(string.Format(msg, args), innerException) { }
    }

    [Serializable]
    public class EvaluatorAbortedException : EvaluatorException
    {
        protected EvaluatorAbortedException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }

        public EvaluatorAbortedException()
            : base("Aborted.") { }
    }

    [Serializable]
    public class NotSupportedExpressionException : EvaluatorException
    {
        protected NotSupportedExpressionException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }

        public NotSupportedExpressionException()
            : base("Expression not supported.") { }
    }

    [Serializable]
    public class ImplicitEvaluationDisabledException : EvaluatorException
    {
        protected ImplicitEvaluationDisabledException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }

        public ImplicitEvaluationDisabledException()
            : base("Implicit property and method evaluation is disabled.") { }
    }
}
