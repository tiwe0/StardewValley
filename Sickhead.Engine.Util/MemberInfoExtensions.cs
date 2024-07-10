using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sickhead.Engine.Util
{
	/// <summary>
	/// Allows Set/GetValue of MemberInfo(s) so that code does not need to
	/// be written to work specifically on PropertyInfo or FieldInfo.
	/// </summary>
	public static class MemberInfoExtensions
	{
		public static Type GetDataType(this MemberInfo info)
		{
			PropertyInfo pi = info as PropertyInfo;
			if ((object)pi == null)
			{
				FieldInfo fi = info as FieldInfo;
				if ((object)fi != null)
				{
					return fi.FieldType;
				}
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 1);
				defaultInterpolatedStringHandler.AppendLiteral("MemberInfo.GetDataType is not possible for type=");
				defaultInterpolatedStringHandler.AppendFormatted(info.GetType());
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			return pi.PropertyType;
		}

		public static object GetValue(this MemberInfo info, object obj)
		{
			return info.GetValue(obj, null);
		}

		public static void SetValue(this MemberInfo info, object obj, object value)
		{
			info.SetValue(obj, value, null);
		}

		public static object GetValue(this MemberInfo info, object obj, object[] index)
		{
			PropertyInfo pi = info as PropertyInfo;
			if ((object)pi == null)
			{
				FieldInfo fi = info as FieldInfo;
				if ((object)fi != null)
				{
					return fi.GetValue(obj);
				}
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(45, 1);
				defaultInterpolatedStringHandler.AppendLiteral("MemberInfo.GetValue is not possible for type=");
				defaultInterpolatedStringHandler.AppendFormatted(info.GetType());
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			return pi.GetValue(obj, index);
		}

		public static void SetValue(this MemberInfo info, object obj, object value, object[] index)
		{
			PropertyInfo pi = info as PropertyInfo;
			if ((object)pi == null)
			{
				FieldInfo fi = info as FieldInfo;
				if ((object)fi == null)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(45, 1);
					defaultInterpolatedStringHandler.AppendLiteral("MemberInfo.SetValue is not possible for type=");
					defaultInterpolatedStringHandler.AppendFormatted(info.GetType());
					throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				fi.SetValue(obj, value);
			}
			else
			{
				pi.SetValue(obj, value, index);
			}
		}

		public static bool IsStatic(this MemberInfo info)
		{
			PropertyInfo pi = info as PropertyInfo;
			if ((object)pi == null)
			{
				FieldInfo fi = info as FieldInfo;
				if ((object)fi == null)
				{
					MethodInfo mi = info as MethodInfo;
					if ((object)mi != null)
					{
						return mi.IsStatic;
					}
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(45, 1);
					defaultInterpolatedStringHandler.AppendLiteral("MemberInfo.IsStatic is not possible for type=");
					defaultInterpolatedStringHandler.AppendFormatted(info.GetType());
					throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				return fi.IsStatic;
			}
			return pi.GetGetMethod(true).IsStatic;
		}

		/// <summary>
		/// Returns true if this is a property or field that is accessible to be set via reflection
		/// on all platforms. Note: windows phone can only set public or internal scope members.
		/// </summary>        
		public static bool CanBeSet(this MemberInfo info)
		{
			PropertyInfo pi = info as PropertyInfo;
			if ((object)pi == null)
			{
				FieldInfo fi = info as FieldInfo;
				if ((object)fi != null)
				{
					if (!fi.IsPrivate)
					{
						return !fi.IsFamily;
					}
					return false;
				}
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 1);
				defaultInterpolatedStringHandler.AppendLiteral("MemberInfo.CanSet is not possible for type=");
				defaultInterpolatedStringHandler.AppendFormatted(info.GetType());
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			MethodAttributes methodAtt = pi.GetSetMethod().Attributes;
			if (pi.CanWrite)
			{
				if ((methodAtt & MethodAttributes.Public) != MethodAttributes.Public)
				{
					return (methodAtt & MethodAttributes.Assembly) != MethodAttributes.Assembly;
				}
				return false;
			}
			return true;
		}

		/// <summary>
		/// In Win8 the static Delegate.Create was removed and added
		/// instead as an instance method on MethodInfo. Therefore it 
		/// is most portable if the new api is used and this extension
		/// translates it to the older API on those platforms.
		/// </summary>        
		public static Delegate CreateDelegate(this MethodInfo method, Type type, object target)
		{
			return Delegate.CreateDelegate(type, target, method);
		}

		public static Delegate CreateDelegate(this MethodInfo method, Type type)
		{
			return Delegate.CreateDelegate(type, method);
		}
	}
}
