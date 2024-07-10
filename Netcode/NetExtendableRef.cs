using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace Netcode
{
	public class NetExtendableRef<T, TSelf> : NetRefBase<T, TSelf> where T : class, INetObject<INetSerializable> where TSelf : NetExtendableRef<T, TSelf>
	{
		public NetExtendableRef()
		{
			base.notifyOnTargetValueChange = true;
		}

		public NetExtendableRef(T value)
			: this()
		{
			cleanSet(value);
		}

		protected override void ForEachChild(Action<INetSerializable> childAction)
		{
			if (targetValue != null)
			{
				childAction(targetValue.NetFields);
			}
		}

		protected override void ReadValueFull(T value, BinaryReader reader, NetVersion version)
		{
			value.NetFields.ReadFull(reader, version);
		}

		protected override void ReadValueDelta(BinaryReader reader, NetVersion version)
		{
			targetValue.NetFields.Read(reader, version);
		}

		private void clearValueParent(T targetValue)
		{
			if (targetValue.NetFields.Parent == this)
			{
				targetValue.NetFields.Parent = null;
			}
		}

		private void setValueParent(T targetValue)
		{
			if (targetValue?.NetFields == null)
			{
				string message;
				if (targetValue != null)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(54, 3);
					defaultInterpolatedStringHandler.AppendLiteral("Can't change net field parent for ");
					defaultInterpolatedStringHandler.AppendFormatted(targetValue.GetType().FullName);
					defaultInterpolatedStringHandler.AppendLiteral(" type's null ");
					defaultInterpolatedStringHandler.AppendFormatted("NetFields");
					defaultInterpolatedStringHandler.AppendLiteral(" to '");
					defaultInterpolatedStringHandler.AppendFormatted(base.Name);
					defaultInterpolatedStringHandler.AppendLiteral("'.");
					message = defaultInterpolatedStringHandler.ToStringAndClear();
				}
				else
				{
					message = "Can't change net field parent for null target to '" + base.Name + ".";
				}
				NetHelper.LogWarning(message);
				NetHelper.LogVerbose(new StackTrace().ToString());
				return;
			}
			if (base.Parent != null || base.Root == this)
			{
				if (targetValue.NetFields.Parent != null && targetValue.NetFields.Parent != this)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(58, 3);
					defaultInterpolatedStringHandler.AppendLiteral("Changing net field parent for '");
					defaultInterpolatedStringHandler.AppendFormatted(targetValue.NetFields.Name);
					defaultInterpolatedStringHandler.AppendLiteral("' collection from '");
					defaultInterpolatedStringHandler.AppendFormatted(targetValue.NetFields.Parent.Name);
					defaultInterpolatedStringHandler.AppendLiteral("' to '");
					defaultInterpolatedStringHandler.AppendFormatted(base.Name);
					defaultInterpolatedStringHandler.AppendLiteral("'.");
					NetHelper.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
					NetHelper.LogVerbose(new StackTrace().ToString());
				}
				targetValue.NetFields.Parent = this;
			}
			targetValue.NetFields.MarkClean();
		}

		protected override void targetValueChanged(T oldValue, T newValue)
		{
			base.targetValueChanged(oldValue, newValue);
			if (oldValue != null)
			{
				clearValueParent(oldValue);
			}
			if (newValue != null)
			{
				setValueParent(newValue);
			}
		}

		protected override void WriteValueFull(BinaryWriter writer)
		{
			targetValue.NetFields.WriteFull(writer);
		}

		protected override void WriteValueDelta(BinaryWriter writer)
		{
			targetValue.NetFields.Write(writer);
		}
	}
}
