using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Netcode.Validation;

namespace Netcode
{
	public class NetFields : AbstractNetSerializable
	{
		/// <summary>Whether to run detailed validation checks to detect possible bugs with net fields (e.g. fields which aren't added to the owner's <see cref="T:Netcode.NetFields" /> collection).</summary>
		/// <remarks>These validation checks are expensive and should normally be disabled.</remarks>
		public static bool ShouldValidateNetFields;

		/// <summary>The net fields within the collection to synchronize between players.</summary>
		private readonly List<INetSerializable> fields = new List<INetSerializable>();

		/// <summary>A name for this net field collection, used for troubleshooting network sync.</summary>
		public new string Name { get; }

		/// <summary>The object instance which owns this collection.</summary>
		/// <remarks>This is the instance which has the <see cref="T:Netcode.NetFields" /> property; see also <see cref="P:Netcode.AbstractNetSerializable.Parent" /> for the net field it's synced through (if any). For example, <see cref="P:StardewValley.Character.NetFields" />'s owner is a <see cref="T:StardewValley.Character" /> and its parent is another <see cref="T:Netcode.NetFields" />.</remarks>
		public INetObject<NetFields> Owner { get; private set; }

		/// <summary>Construct an instance.</summary>
		/// <param name="name">A name for this net field collection, used for troubleshooting network sync.</param>
		public NetFields(string name)
		{
			Name = name;
		}

		/// <summary>Set the object instance which owns this collection, used to enable validation and simplify troubleshooting.</summary>
		/// <param name="owner">The instance which owns this net field collection.</param>
		public NetFields SetOwner(INetObject<NetFields> owner)
		{
			Owner = owner;
			return this;
		}

		/// <summary>Get a suggested name for an instance's net field collection, for cases where it's useful to show the name of the subtype.</summary>
		/// <typeparam name="TBaseType">The base type which defines the net field collection.</typeparam>
		/// <param name="instance">The instance which inherits the net field collection.</param>
		public static string GetNameForInstance<TBaseType>(TBaseType instance)
		{
			Type baseType = typeof(TBaseType);
			Type instanceType = instance.GetType();
			if (!(baseType == instanceType))
			{
				return baseType.Name + " (" + instanceType.Name + ")";
			}
			return baseType.Name;
		}

		/// <summary>Get the fields that are in the collection.</summary>
		public IEnumerable<INetSerializable> GetFields()
		{
			return fields;
		}

		public void CancelInterpolation()
		{
			foreach (INetSerializable field in fields)
			{
				(field as InterpolationCancellable)?.CancelInterpolation();
			}
		}

		/// <summary>Add a net field to this collection.</summary>
		/// <param name="field">The field to sync as part of this collection.</param>
		/// <param name="name">A readable name for the field within the collection, used for troubleshooting network sync. This should usually be omitted so it's auto-generated from the expression passed to <paramref name="field" />.</param>
		/// <exception cref="T:System.InvalidOperationException">The field is already part of another collection, or this collection has already been fully initialized.</exception>
		/// <remarks><see cref="M:Netcode.NetFields.SetOwner(Netcode.INetObject{Netcode.NetFields})" /> should be called before any fields are added to enable readable error logs.</remarks>
		public NetFields AddField(INetSerializable field, [CallerArgumentExpression("field")] string name = null)
		{
			name = name ?? field.GetType().FullName;
			if (Owner == null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(59, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Field '");
				defaultInterpolatedStringHandler.AppendFormatted(name);
				defaultInterpolatedStringHandler.AppendLiteral("' was added to the '");
				defaultInterpolatedStringHandler.AppendFormatted(Name);
				defaultInterpolatedStringHandler.AppendLiteral("' net fields before ");
				defaultInterpolatedStringHandler.AppendFormatted("SetOwner");
				defaultInterpolatedStringHandler.AppendLiteral(" was called.");
				NetHelper.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			if (field.Parent != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(79, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Can't add field '");
				defaultInterpolatedStringHandler.AppendFormatted(name);
				defaultInterpolatedStringHandler.AppendLiteral("' to the '");
				defaultInterpolatedStringHandler.AppendFormatted(Name);
				defaultInterpolatedStringHandler.AppendLiteral("' net fields because it's already part of the ");
				defaultInterpolatedStringHandler.AppendFormatted(field.Parent.Name);
				defaultInterpolatedStringHandler.AppendLiteral(" tree.");
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			if (base.Parent != null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(86, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Can't add field '");
				defaultInterpolatedStringHandler.AppendFormatted(name);
				defaultInterpolatedStringHandler.AppendLiteral("' to the '");
				defaultInterpolatedStringHandler.AppendFormatted(Name);
				defaultInterpolatedStringHandler.AppendLiteral("' net fields, because they've already been added to a tree.");
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			if (ShouldValidateNetFields)
			{
				foreach (INetSerializable otherField in fields)
				{
					if (field == otherField)
					{
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(55, 2);
						defaultInterpolatedStringHandler.AppendLiteral("Field '");
						defaultInterpolatedStringHandler.AppendFormatted(name);
						defaultInterpolatedStringHandler.AppendLiteral("' was added to the '");
						defaultInterpolatedStringHandler.AppendFormatted(Name);
						defaultInterpolatedStringHandler.AppendLiteral("' net fields multiple times.");
						NetHelper.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
						break;
					}
				}
			}
			field.Name = Name + ": " + name;
			fields.Add(field);
			return this;
		}

		protected override void SetParent(INetSerializable parent)
		{
			base.SetParent(parent);
			ValidateNetFields();
		}

		/// <summary>Detect and log warnings for common issues like net fields not added to the collection.</summary>
		protected void ValidateNetFields()
		{
			if (Owner == null)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(74, 3);
				defaultInterpolatedStringHandler.AppendFormatted("NetFields");
				defaultInterpolatedStringHandler.AppendLiteral(" collection '");
				defaultInterpolatedStringHandler.AppendFormatted(Name);
				defaultInterpolatedStringHandler.AppendLiteral("' was initialized without calling ");
				defaultInterpolatedStringHandler.AppendFormatted("SetOwner");
				defaultInterpolatedStringHandler.AppendLiteral(", so it can't be validated.");
				NetHelper.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (this != Owner.NetFields)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(100, 4);
				defaultInterpolatedStringHandler.AppendFormatted("NetFields");
				defaultInterpolatedStringHandler.AppendLiteral(" collection '");
				defaultInterpolatedStringHandler.AppendFormatted(Name);
				defaultInterpolatedStringHandler.AppendLiteral("' has its own owner set to an ");
				defaultInterpolatedStringHandler.AppendFormatted(Owner?.GetType().FullName);
				defaultInterpolatedStringHandler.AppendLiteral(" instance whose ");
				defaultInterpolatedStringHandler.AppendFormatted("NetFields");
				defaultInterpolatedStringHandler.AppendLiteral(" field doesn't reference this collection.");
				NetHelper.LogWarning(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (ShouldValidateNetFields)
			{
				NetFieldValidator.ValidateNetFields(Owner, NetHelper.LogWarning);
			}
		}

		public override void Read(BinaryReader reader, NetVersion version)
		{
			BitArray dirtyBits = reader.ReadBitArray();
			if (fields.Count != dirtyBits.Length)
			{
				throw new InvalidOperationException();
			}
			for (int i = 0; i < fields.Count; i++)
			{
				if (dirtyBits[i])
				{
					INetSerializable field = fields[i];
					try
					{
						field.Read(reader, version);
					}
					catch (Exception ex)
					{
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 2);
						defaultInterpolatedStringHandler.AppendLiteral("Failed reading ");
						defaultInterpolatedStringHandler.AppendFormatted(Name);
						defaultInterpolatedStringHandler.AppendLiteral(" field '");
						defaultInterpolatedStringHandler.AppendFormatted(field.Name);
						defaultInterpolatedStringHandler.AppendLiteral("'");
						throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
					}
				}
			}
		}

		public override void Write(BinaryWriter writer)
		{
			BitArray dirtyBits = new BitArray(fields.Count);
			for (int j = 0; j < fields.Count; j++)
			{
				dirtyBits[j] = fields[j].Dirty;
			}
			writer.WriteBitArray(dirtyBits);
			for (int i = 0; i < fields.Count; i++)
			{
				if (dirtyBits[i])
				{
					INetSerializable field = fields[i];
					writer.Push(Convert.ToString(i));
					try
					{
						field.Write(writer);
					}
					catch (Exception ex)
					{
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 2);
						defaultInterpolatedStringHandler.AppendLiteral("Failed writing ");
						defaultInterpolatedStringHandler.AppendFormatted(Name);
						defaultInterpolatedStringHandler.AppendLiteral(" field '");
						defaultInterpolatedStringHandler.AppendFormatted(field.Name);
						defaultInterpolatedStringHandler.AppendLiteral("'");
						throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
					}
					writer.Pop();
				}
			}
		}

		public override void ReadFull(BinaryReader reader, NetVersion version)
		{
			foreach (INetSerializable field in fields)
			{
				try
				{
					field.ReadFull(reader, version);
				}
				catch (Exception ex)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 2);
					defaultInterpolatedStringHandler.AppendLiteral("Failed reading ");
					defaultInterpolatedStringHandler.AppendFormatted(Name);
					defaultInterpolatedStringHandler.AppendLiteral(" field '");
					defaultInterpolatedStringHandler.AppendFormatted(field.Name);
					defaultInterpolatedStringHandler.AppendLiteral("'");
					throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
				}
			}
		}

		public override void WriteFull(BinaryWriter writer)
		{
			for (int i = 0; i < fields.Count; i++)
			{
				INetSerializable field = fields[i];
				writer.Push(Convert.ToString(i));
				try
				{
					field.WriteFull(writer);
				}
				catch (Exception ex)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(24, 2);
					defaultInterpolatedStringHandler.AppendLiteral("Failed writing ");
					defaultInterpolatedStringHandler.AppendFormatted(Name);
					defaultInterpolatedStringHandler.AppendLiteral(" field '");
					defaultInterpolatedStringHandler.AppendFormatted(field.Name);
					defaultInterpolatedStringHandler.AppendLiteral("'");
					throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
				}
				writer.Pop();
			}
		}

		public virtual void CopyFrom(NetFields source)
		{
			try
			{
				using (MemoryStream stream = new MemoryStream())
				{
					using (BinaryWriter writer = new BinaryWriter(stream))
					{
						using (BinaryReader reader = new BinaryReader(stream))
						{
							source.WriteFull(writer);
							stream.Seek(0L, SeekOrigin.Begin);
							if (base.Root == null)
							{
								ReadFull(reader, new NetClock().netVersion);
							}
							else
							{
								ReadFull(reader, base.Root.Clock.netVersion);
							}
							MarkClean();
						}
					}
				}
			}
			catch (Exception ex)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(30, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Failed copying ");
				defaultInterpolatedStringHandler.AppendFormatted(Name);
				defaultInterpolatedStringHandler.AppendLiteral(" fields from '");
				defaultInterpolatedStringHandler.AppendFormatted(source.Name);
				defaultInterpolatedStringHandler.AppendLiteral("'");
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
			}
		}

		protected override void ForEachChild(Action<INetSerializable> childAction)
		{
			foreach (INetSerializable field in fields)
			{
				childAction(field);
			}
		}
	}
}
