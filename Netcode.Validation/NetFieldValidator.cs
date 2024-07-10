using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Netcode.Validation
{
	/// <summary>A utility which auto-detects common net field issues.</summary>
	public static class NetFieldValidator
	{
		/// <summary>Detect and log warnings for common issues like net fields not added to the collection.</summary>
		/// <param name="owner">The object instance whose net fields to validate.</param>
		/// <param name="onError">The method to call when an error occurs.</param>
		public static void ValidateNetFields(INetObject<NetFields> owner, Action<string> onError)
		{
			string collectionName = owner.NetFields.Name;
			HashSet<INetSerializable> trackedFields = new HashSet<INetSerializable>(owner.NetFields.GetFields(), ReferenceEqualityComparer.Instance);
			List<NetFieldValidatorEntry> ownerFields = new List<NetFieldValidatorEntry>();
			FieldInfo[] fields = owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				NetFieldValidatorEntry netField;
				if (!NetFieldValidatorEntry.TryGetNetField(owner, fieldInfo, out netField))
				{
					continue;
				}
				if (netField.IsMarkedNotNetField())
				{
					if (!IsInCollection(trackedFields, netField))
					{
						continue;
					}
					onError(GetFieldError(collectionName, netField, "is marked [NotNetFieldAttribute] but still added to the collection"));
				}
				ownerFields.Add(netField);
			}
			foreach (NetFieldValidatorEntry entry in ownerFields)
			{
				if (entry.Value == null)
				{
					onError(GetFieldError(collectionName, entry, "is null"));
				}
				else if (string.IsNullOrWhiteSpace(entry.Name))
				{
					onError(GetFieldError(collectionName, entry, "has no name (and likely isn't in the collection)"));
				}
				else if (!IsInCollection(trackedFields, entry.Value))
				{
					onError(GetFieldError(collectionName, entry, "isn't in the collection"));
				}
			}
		}

		/// <summary>Get a human-readable error message for a field validation error.</summary>
		/// <param name="collectionName">The name of the net fields collection being validated.</param>
		/// <param name="entry">The validator entry for the net field being validated.</param>
		/// <param name="phrase">A short phrase which indicates why it failed validation, like <c>is null</c>.</param>
		private static string GetFieldError(string collectionName, NetFieldValidatorEntry entry, string phrase)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 4);
			defaultInterpolatedStringHandler.AppendLiteral("The owner of ");
			defaultInterpolatedStringHandler.AppendFormatted("NetFields");
			defaultInterpolatedStringHandler.AppendLiteral(" collection '");
			defaultInterpolatedStringHandler.AppendFormatted(collectionName);
			defaultInterpolatedStringHandler.AppendLiteral("' has field '");
			defaultInterpolatedStringHandler.AppendFormatted(entry.FromField.Name);
			defaultInterpolatedStringHandler.AppendLiteral("' which ");
			defaultInterpolatedStringHandler.AppendFormatted(phrase);
			defaultInterpolatedStringHandler.AppendLiteral(".");
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}

		/// <summary>Get whether the net field is in the owner's <see cref="P:Netcode.INetObject`1.NetFields" /> collection.</summary>
		/// <param name="trackedFields">The fields that are synced as part of the collection.</param>
		/// <param name="netField">The net field instance to find.</param>
		private static bool IsInCollection(HashSet<INetSerializable> trackedFields, object netField)
		{
			INetSerializable field = netField as INetSerializable;
			if (field != null)
			{
				return trackedFields.Contains(field);
			}
			INetObject<NetFields> container = netField as INetObject<NetFields>;
			if (container != null)
			{
				return trackedFields.Contains(container.NetFields);
			}
			return false;
		}
	}
}
