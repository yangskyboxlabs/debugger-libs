//
// EvaluationContextExtensions.cs
//
// Authors: Yang Zhao <yang.zhao@skyboxlabs.com>
//
// Copyright (c) 2020 SkyBox Labs Inc. (https://skyboxlabs.com)
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
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mono.Debugging.Evaluation.Roselyn {
	public static class EvaluationContextExtensions {
		public static IEnumerable<(ValueReference, object)> GetAmbientValues (this EvaluationContext context)
		{
			// implicit this
			var thisRef = context.Adapter.GetThisReference (context);
			if (thisRef != null) {
				yield return (thisRef, context.Adapter.GetEnclosingType (context));
				yield return (thisRef, thisRef.Type);
			}

			// enclosing type and its parents
			var enclosingType = context.Adapter.GetEnclosingType (context);
            for (var vtype = enclosingType; vtype != null;) {
				yield return (null, vtype);

/*
                var nestedClasses = this.Context.Adapter.GetNestedTypes(this.Context, vtype);
                foreach (var nested in nestedClasses) {
                    Console.WriteLine($"!!  -> nested class: {this.Context.Adapter.GetTypeName(this.Context, nested)}");
                    if (this.Context.Adapter.GetTypeName(this.Context, nested).EndsWith($"+{name}")) {
                        return new TypeValueReference(this.Context, nested);
                    }
                }
				*/

                vtype = context.Adapter.GetParentType (context, vtype);
            }
		}

		public static bool TryGetNestedClass (this EvaluationContext context,
			TypeValueReference parent, string name,
			out object nestedType,
			IEnumerable<object> typeArgs = null)
		{
			var parentType = (Type) parent.Type;
			Console.WriteLine($"!! -> try nested type {name} of {parent.Name}");
			nestedType = parentType.GetNestedType (name, BindingFlags.Public | BindingFlags.NonPublic);
			return nestedType != null;
			/*
			var parentType = parent.Type;
			if (context.Adapter.IsGenericType(context, parentType)) {
				parentType = context.Adapter.GetType(context, "Thing`1");
				typeArgs = context.Adapter.GetGenericTypeArguments(context, parent.Type)
					.Concat(typeArgs ?? Enumerable.Empty<object> ());
			}

			var typeArgsArray = typeArgs?.ToArray();
			var nestedClasses = context.Adapter.GetNestedTypes(context, parentType);

			foreach (var nested in nestedClasses) {
				var fullName = context.Adapter.GetTypeName(context, nested);
				if (fullName.EndsWith($"+{name}")) {
					if (context.TryGetType(fullName, typeArgsArray, out nestedType))
						return true;
				}
			}
			nestedType = null;
			*/
			return false;
		}

		public static bool TryGetType (this EvaluationContext context,
			string name, out object typeObject)
		{
			typeObject = context.Adapter.GetType (context, name);
			if (typeObject != null) {
				context.Adapter.ForceLoadType (context, typeObject);
				return true;
			}
			return false;
		}

		public static bool TryGetType (this EvaluationContext context,
			string name, object[] typeArguments, out object typeObject)
		{
			typeObject = context.Adapter.GetType (context, name, typeArguments);
			if (typeObject != null) {
				context.Adapter.ForceLoadType(context, typeObject);
				return true;
			}
			return false;
		}
	}
}