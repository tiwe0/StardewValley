using System;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Xna.Framework;
using Netcode;

namespace StardewValley.Network
{
	public struct OutgoingMessage
	{
		private byte messageType;

		private long farmerID;

		private object[] data;

		public byte MessageType => messageType;

		public long FarmerID => farmerID;

		public Farmer SourceFarmer => Game1.getFarmer(farmerID);

		public ReadOnlyCollection<object> Data => Array.AsReadOnly(data);

		public OutgoingMessage(byte messageType, long farmerID, params object[] data)
		{
			this.messageType = messageType;
			this.farmerID = farmerID;
			this.data = data;
		}

		public OutgoingMessage(byte messageType, Farmer sourceFarmer, params object[] data)
			: this(messageType, sourceFarmer.UniqueMultiplayerID, data)
		{
		}

		public OutgoingMessage(IncomingMessage message)
			: this(message.MessageType, message.FarmerID, message.Data)
		{
		}

		public void Write(BinaryWriter writer)
		{
			writer.Write(messageType);
			writer.Write(farmerID);
			object[] data = this.data;
			writer.WriteSkippable(delegate
			{
				object[] array = data;
				foreach (object obj in array)
				{
					if (obj is Vector2)
					{
						Vector2 vector = (Vector2)obj;
						writer.Write(vector.X);
						writer.Write(vector.Y);
					}
					else if (obj is Guid)
					{
						Guid guid = (Guid)obj;
						writer.Write(guid.ToByteArray());
					}
					else
					{
						byte[] array2 = obj as byte[];
						if (array2 == null)
						{
							if (obj is bool)
							{
								bool flag = (bool)obj;
								writer.Write((byte)(flag ? 1u : 0u));
							}
							else if (obj is byte)
							{
								byte value = (byte)obj;
								writer.Write(value);
							}
							else if (obj is int)
							{
								int value2 = (int)obj;
								writer.Write(value2);
							}
							else if (obj is short)
							{
								short value3 = (short)obj;
								writer.Write(value3);
							}
							else if (obj is float)
							{
								float value4 = (float)obj;
								writer.Write(value4);
							}
							else if (obj is long)
							{
								long value5 = (long)obj;
								writer.Write(value5);
							}
							else
							{
								string text = obj as string;
								if (text == null)
								{
									string[] array3 = obj as string[];
									if (array3 == null)
									{
										if (!(obj is IConvertible))
										{
											throw new InvalidDataException();
										}
										if (!obj.GetType().IsValueType)
										{
											throw new InvalidDataException();
										}
										writer.WriteEnum(obj);
									}
									else
									{
										writer.Write((byte)array3.Length);
										for (int j = 0; j < array3.Length; j++)
										{
											writer.Write(array3[j]);
										}
									}
								}
								else
								{
									writer.Write(text);
								}
							}
						}
						else
						{
							writer.Write(array2);
						}
					}
				}
			});
		}
	}
}
