using System;
using Microsoft.Samples.Debugging.CorDebug.NativeApi;

namespace Mono.Debugging.Win32
{
	/// <summary>
	/// Holder for <see cref="T:JetBrains.Debugger.CorApi.ComInterop.ICorDebugValue" />
	/// </summary>
	public interface ICorValue
	{
		ICorDebugValue Value { get; }
	}

	public static class CorValueExtensions
	{
		public static T GetRealObject<T>(
			this ICorDebugValue obj,
			CorEvaluationContext ctx,
			bool unboxPrimitive = true)
			where T : ICorDebugValue
		{
			if (obj == null)
				throw new ArgumentNullException(nameof(obj));
			try
			{
				if (obj is ICorDebugStringValue || obj is ICorDebugArrayValue)
					return obj.AssertType<T>();
				ICorDebugReferenceValue debugReferenceValue = obj as ICorDebugReferenceValue;
				if (debugReferenceValue != null)
				{
					if (debugReferenceValue.IsNull())
						return debugReferenceValue.AssertType<T>();
					return debugReferenceValue.Dereference().GetRealObject<T>(ctx, true);
				}

				ICorDebugBoxValue boxVal = obj as ICorDebugBoxValue;
				if (boxVal != null)
				{
					ICorDebugObjectValue objectValue = boxVal.Unbox(ctx);
					ICorDebugValue resultValue;
					if (unboxPrimitive && objectValue.UnboxPrimitive(ctx, out resultValue))
						return resultValue.GetRealObject<T>(ctx, true);
					return objectValue.AssertType<T>();
				}
			}
			catch (Exception ex)
			{
				CorValueUtil.Log.Warn(ex, "Error doing GetRealObject for an ICorDebugValue");
			}

			return obj.AssertType<T>();
		}

		static T AssertType<T>(this ICorDebugValue value) where T : ICorDebugValue
		{
			if (value is T debugValue)
				return debugValue;
			throw new InvalidCastException($"value is not of type {typeof(T).Name}");
		}
	}

	public static class ICorDebugReferenceValueEx
	{
		public static ICorDebugValue Dereference(this ICorDebugReferenceValue value)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			ICorDebugValue ppValue;
			value.Dereference(out ppValue);
			if (ppValue == null)
			{
				throw new Exception("Could not dereference a value");
			}

			return ppValue;
		}

		public static bool IsNull(this ICorDebugReferenceValue value)
		{
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			value.IsNull(out var num);
			if (num < 0)
			{
				throw new Exception("Could not get IsNull property of a value");
			}

			return (uint)num > 0U;
		}
	}
}
