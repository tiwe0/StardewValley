using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Netcode;
using StardewValley.BellsAndWhistles;
using StardewValley.Buildings;
using StardewValley.GameData.LocationContexts;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.TokenizableStrings;

namespace StardewValley
{
	public class Multiplayer
	{
		public enum PartyWideMessageQueue
		{
			MailForTomorrow,
			SeenMail
		}

		public enum DisconnectType
		{
			None,
			ClosedGame,
			ExitedToMainMenu,
			ExitedToMainMenu_FromFarmhandSelect,
			HostLeft,
			ServerOfflineMode,
			ServerFull,
			Kicked,
			AcceptedOtherInvite,
			ClientTimeout,
			LidgrenTimeout,
			GalaxyTimeout,
			Timeout_FarmhandSelection,
			LidgrenDisconnect_Unknown
		}

		public static readonly long AllPlayers = 0L;

		public const byte farmerDelta = 0;

		public const byte serverIntroduction = 1;

		public const byte playerIntroduction = 2;

		public const byte locationIntroduction = 3;

		public const byte forceEvent = 4;

		public const byte warpFarmer = 5;

		public const byte locationDelta = 6;

		public const byte locationSprites = 7;

		public const byte characterWarp = 8;

		public const byte availableFarmhands = 9;

		public const byte chatMessage = 10;

		public const byte connectionMessage = 11;

		public const byte worldDelta = 12;

		public const byte teamDelta = 13;

		public const byte newDaySync = 14;

		public const byte chatInfoMessage = 15;

		public const byte userNameUpdate = 16;

		public const byte farmerGainExperience = 17;

		public const byte serverToClientsMessage = 18;

		public const byte disconnecting = 19;

		public const byte sharedAchievement = 20;

		public const byte globalMessage = 21;

		public const byte partyWideMail = 22;

		public const byte forceKick = 23;

		public const byte removeLocationFromLookup = 24;

		public const byte farmerKilledMonster = 25;

		public const byte requestGrandpaReevaluation = 26;

		public const byte digBuriedNut = 27;

		public const byte requestPassout = 28;

		public const byte passout = 29;

		public const byte startNewDaySync = 30;

		public const byte readySync = 31;

		public const byte chestHitSync = 32;

		/// <summary>A compressed message, which must be decompressed to read the actual message.</summary>
		public const byte compressed = 127;

		/// <summary>A token prefix for messages sent via <see cref="M:StardewValley.Multiplayer.sendChatInfoMessage(System.String,System.String[])" /> that shows the result of <see cref="M:StardewValley.Utility.AOrAn(System.String)" /> for a tokenizable input.</summary>
		public const string chat_token_aOrAn = "aOrAn:";

		public int defaultInterpolationTicks = 15;

		public int farmerDeltaBroadcastPeriod = 3;

		public int locationDeltaBroadcastPeriod = 3;

		public int worldStateDeltaBroadcastPeriod = 3;

		public int playerLimit = 4;

		public static string kicked = "KICKED";

		/// <summary>The override value for <see cref="P:StardewValley.Multiplayer.protocolVersion" />, if set manually in the build settings.</summary>
		internal static string protocolVersionOverride;

		public readonly NetLogger logging = new NetLogger();

		protected List<long> disconnectingFarmers = new List<long>();

		public ulong latestID;

		public Dictionary<string, CachedMultiplayerMap> cachedMultiplayerMaps = new Dictionary<string, CachedMultiplayerMap>();

		protected HashSet<GameLocation> _updatedRoots = new HashSet<GameLocation>();

		public const string MSG_START_FESTIVAL_EVENT = "festivalEvent";

		public const string MSG_END_FESTIVAL = "endFest";

		public const string MSG_TRAIN_APPROACH = "trainApproach";

		/// <summary>A version string sent by the server to new connections. Clients disconnect with an error if it doesn't match their own protocol version, to prevent accidental connection of incompatible games.</summary>
		public static string protocolVersion
		{
			get
			{
				if (protocolVersionOverride != null)
				{
					return protocolVersionOverride;
				}
				return Game1.version + ((Game1.versionLabel != null) ? ("+" + new string(Game1.versionLabel.Where(char.IsLetterOrDigit).ToArray())) : "");
			}
		}

		public virtual int MaxPlayers
		{
			get
			{
				if (Game1.server == null)
				{
					return 1;
				}
				return playerLimit;
			}
		}

		public Multiplayer()
		{
			playerLimit = 8;
		}

		public virtual long getNewID()
		{
			ulong seqNum = ((latestID & 0xFF) + 1) & 0xFF;
			ulong nodeID = (ulong)Game1.player.UniqueMultiplayerID;
			nodeID = (nodeID >> 32) ^ (nodeID & 0xFFFFFFFFu);
			nodeID = ((nodeID >> 16) ^ (nodeID & 0xFFFF)) & 0xFFFF;
			ulong timestamp = (ulong)(DateTime.UtcNow.Ticks / 10000);
			latestID = (timestamp << 24) | (nodeID << 8) | seqNum;
			return (long)latestID;
		}

		public virtual bool isDisconnecting(Farmer farmer)
		{
			return isDisconnecting(farmer.UniqueMultiplayerID);
		}

		public virtual bool isDisconnecting(long uid)
		{
			return disconnectingFarmers.Contains(uid);
		}

		public virtual bool isClientBroadcastType(byte messageType)
		{
			switch (messageType)
			{
			case 0:
			case 2:
			case 4:
			case 6:
			case 7:
			case 12:
			case 13:
			case 14:
			case 15:
			case 19:
			case 20:
			case 21:
			case 22:
			case 24:
			case 26:
				return true;
			default:
				return false;
			}
		}

		public virtual bool allowSyncDelay()
		{
			return !Game1.newDaySync.hasInstance();
		}

		public virtual int interpolationTicks()
		{
			if (!allowSyncDelay())
			{
				return 0;
			}
			if (LocalMultiplayer.IsLocalMultiplayer(true))
			{
				return 4;
			}
			return defaultInterpolationTicks;
		}

		public virtual IEnumerable<NetFarmerRoot> farmerRoots()
		{
			if (Game1.serverHost != null)
			{
				yield return Game1.serverHost;
			}
			foreach (NetRoot<Farmer> farmerRoot in Game1.otherFarmers.Roots.Values)
			{
				if (Game1.serverHost == null || farmerRoot != Game1.serverHost)
				{
					yield return farmerRoot as NetFarmerRoot;
				}
			}
		}

		public virtual NetFarmerRoot farmerRoot(long id)
		{
			if (Game1.serverHost != null && id == Game1.serverHost.Value.UniqueMultiplayerID)
			{
				return Game1.serverHost;
			}
			NetRoot<Farmer> otherFarmer;
			if (Game1.otherFarmers.Roots.TryGetValue(id, out otherFarmer))
			{
				return otherFarmer as NetFarmerRoot;
			}
			return null;
		}

		public virtual void broadcastFarmerDeltas()
		{
			foreach (NetFarmerRoot farmerRoot in farmerRoots())
			{
				if (farmerRoot.Dirty && Game1.player.UniqueMultiplayerID == farmerRoot.Value.UniqueMultiplayerID)
				{
					broadcastFarmerDelta(farmerRoot.Value, writeObjectDeltaBytes(farmerRoot));
				}
			}
			if (Game1.player.teamRoot.Dirty)
			{
				broadcastTeamDelta(writeObjectDeltaBytes(Game1.player.teamRoot));
			}
		}

		protected virtual void broadcastTeamDelta(byte[] delta)
		{
			if (Game1.IsServer)
			{
				foreach (Farmer farmer in Game1.otherFarmers.Values)
				{
					if (farmer != Game1.player)
					{
						Game1.server.sendMessage(farmer.UniqueMultiplayerID, 13, Game1.player, delta);
					}
				}
				return;
			}
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(13, delta);
			}
		}

		protected virtual void broadcastFarmerDelta(Farmer farmer, byte[] delta)
		{
			foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
			{
				if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
				{
					v.Value.queueMessage(0, farmer, farmer.UniqueMultiplayerID, delta);
				}
			}
		}

		public void updateRoot<T>(T root) where T : INetRoot
		{
			foreach (long id in disconnectingFarmers)
			{
				root.Disconnect(id);
			}
			root.TickTree();
		}

		public virtual void updateRoots()
		{
			updateRoot(Game1.netWorldState);
			foreach (NetFarmerRoot farmerRoot in farmerRoots())
			{
				farmerRoot.Clock.InterpolationTicks = interpolationTicks();
				updateRoot(farmerRoot);
			}
			Game1.player.teamRoot.Clock.InterpolationTicks = interpolationTicks();
			updateRoot(Game1.player.teamRoot);
			if (Game1.IsClient)
			{
				foreach (GameLocation location2 in activeLocations())
				{
					if (location2.Root != null && _updatedRoots.Add(location2.Root.Value))
					{
						location2.Root.Clock.InterpolationTicks = interpolationTicks();
						updateRoot(location2.Root);
					}
				}
			}
			else
			{
				Utility.ForEachLocation(delegate(GameLocation location)
				{
					if (location.Root != null)
					{
						location.Root.Clock.InterpolationTicks = interpolationTicks();
						updateRoot(location.Root);
					}
					return true;
				}, false, true);
			}
			_updatedRoots.Clear();
		}

		public virtual void broadcastLocationDeltas()
		{
			if (Game1.IsClient)
			{
				foreach (GameLocation location2 in activeLocations())
				{
					if (!(location2.Root == null) && location2.Root.Dirty)
					{
						broadcastLocationDelta(location2);
					}
				}
				return;
			}
			Utility.ForEachLocation(delegate(GameLocation location)
			{
				if (location.Root != null && location.Root.Dirty)
				{
					broadcastLocationDelta(location);
				}
				return true;
			}, false, true);
		}

		public virtual void broadcastLocationDelta(GameLocation loc)
		{
			if (!(loc.Root == null) && loc.Root.Dirty)
			{
				byte[] delta = writeObjectDeltaBytes(loc.Root);
				broadcastLocationBytes(loc, 6, delta);
			}
		}

		protected virtual void broadcastLocationBytes(GameLocation loc, byte messageType, byte[] bytes)
		{
			OutgoingMessage message = new OutgoingMessage(messageType, Game1.player, loc.isStructure.Value, loc.NameOrUniqueName, bytes);
			broadcastLocationMessage(loc, message);
		}

		protected virtual void broadcastLocationMessage(GameLocation loc, OutgoingMessage message)
		{
			_003C_003Ec__DisplayClass70_0 _003C_003Ec__DisplayClass70_ = default(_003C_003Ec__DisplayClass70_0);
			_003C_003Ec__DisplayClass70_.message = message;
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(_003C_003Ec__DisplayClass70_.message);
				return;
			}
			if (isAlwaysActiveLocation(loc))
			{
				foreach (Farmer value in Game1.otherFarmers.Values)
				{
					_003CbroadcastLocationMessage_003Eg__TellFarmer_007C70_0(value, ref _003C_003Ec__DisplayClass70_);
				}
				return;
			}
			foreach (Farmer farmer in loc.farmers)
			{
				_003CbroadcastLocationMessage_003Eg__TellFarmer_007C70_0(farmer, ref _003C_003Ec__DisplayClass70_);
			}
			foreach (Building building in loc.buildings)
			{
				GameLocation indoors = building.GetIndoors();
				if (indoors == null)
				{
					continue;
				}
				foreach (Farmer farmer2 in indoors.farmers)
				{
					_003CbroadcastLocationMessage_003Eg__TellFarmer_007C70_0(farmer2, ref _003C_003Ec__DisplayClass70_);
				}
			}
		}

		public virtual void broadcastSprites(GameLocation location, TemporaryAnimatedSpriteList sprites)
		{
			broadcastSprites(location, sprites.ToArray());
		}

		public virtual void broadcastSprites(GameLocation location, params TemporaryAnimatedSprite[] sprites)
		{
			location.temporarySprites.AddRange(sprites);
			if (sprites.Length == 0 || !Game1.IsMultiplayer)
			{
				return;
			}
			using (MemoryStream stream = new MemoryStream())
			{
				using (BinaryWriter writer = createWriter(stream))
				{
					writer.Push("TemporaryAnimatedSprites");
					writer.Write(sprites.Length);
					for (int i = 0; i < sprites.Length; i++)
					{
						sprites[i].Write(writer, location);
					}
					writer.Pop();
				}
				broadcastLocationBytes(location, 7, stream.ToArray());
			}
		}

		public virtual void broadcastWorldStateDeltas()
		{
			if (!Game1.netWorldState.Dirty)
			{
				return;
			}
			byte[] delta = writeObjectDeltaBytes(Game1.netWorldState);
			foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
			{
				if (v.Value != Game1.player)
				{
					v.Value.queueMessage(12, Game1.player, delta);
				}
			}
		}

		public virtual void receiveWorldState(BinaryReader msg)
		{
			Game1.netWorldState.Clock.InterpolationTicks = 0;
			readObjectDelta(msg, Game1.netWorldState);
			Game1.netWorldState.TickTree();
			int origTime = Game1.timeOfDay;
			Game1.netWorldState.Value.WriteToGame1();
			if (!Game1.IsServer && origTime != Game1.timeOfDay && Game1.currentLocation != null && !Game1.newDaySync.hasInstance())
			{
				Game1.performTenMinuteClockUpdate();
			}
		}

		public virtual void requestCharacterWarp(NPC character, GameLocation targetLocation, Vector2 position)
		{
			if (Game1.IsClient)
			{
				GameLocation loc = character.currentLocation;
				if (loc == null)
				{
					throw new ArgumentException("In warpCharacter, the character's currentLocation must not be null");
				}
				Guid characterGuid = loc.characters.GuidOf(character);
				if (characterGuid == Guid.Empty)
				{
					throw new ArgumentException("In warpCharacter, the character must be in its currentLocation");
				}
				OutgoingMessage message = new OutgoingMessage(8, Game1.player, loc.isStructure.Value, loc.NameOrUniqueName, characterGuid, targetLocation.isStructure.Value, targetLocation.NameOrUniqueName, position);
				Game1.serverHost.Value.queueMessage(message);
			}
		}

		public virtual NetRoot<GameLocation> locationRoot(GameLocation location)
		{
			if (location.Root == null && Game1.IsMasterGame)
			{
				new NetRoot<GameLocation>().Set(location);
				location.Root.Clock.InterpolationTicks = interpolationTicks();
				location.Root.MarkClean();
			}
			return location.Root;
		}

		public virtual void sendPassoutRequest()
		{
			object[] message = new object[1] { Game1.player.UniqueMultiplayerID };
			if (Game1.IsMasterGame)
			{
				_receivePassoutRequest(Game1.player);
			}
			else
			{
				Game1.client.sendMessage(28, message);
			}
		}

		public virtual void receivePassoutRequest(IncomingMessage msg)
		{
			if (Game1.IsServer)
			{
				Farmer farmer = Game1.getFarmer(msg.Reader.ReadInt64());
				if (farmer != null)
				{
					_receivePassoutRequest(farmer);
				}
			}
		}

		protected virtual void _receivePassoutRequest(Farmer farmer)
		{
			if (!Game1.IsMasterGame)
			{
				return;
			}
			GameLocation lastSleepLocation = ((farmer.lastSleepLocation.Value != null && Game1.isLocationAccessible(farmer.lastSleepLocation)) ? Game1.getLocationFromName(farmer.lastSleepLocation) : null);
			if (lastSleepLocation != null && lastSleepLocation.GetLocationContextId() == farmer.currentLocation.GetLocationContextId() && BedFurniture.IsBedHere(lastSleepLocation, farmer.lastSleepPoint.Value.X, farmer.lastSleepPoint.Value.Y))
			{
				if (Game1.IsServer && farmer != Game1.player)
				{
					object[] message2 = new object[4]
					{
						farmer.lastSleepLocation.Value,
						farmer.lastSleepPoint.X,
						farmer.lastSleepPoint.Y,
						true
					};
					Game1.server.sendMessage(farmer.UniqueMultiplayerID, 29, Game1.player, message2.ToArray());
				}
				else
				{
					Farmer.performPassoutWarp(farmer, farmer.lastSleepLocation, farmer.lastSleepPoint.Value, true);
				}
				return;
			}
			FarmHouse homeOfFarmer = Utility.getHomeOfFarmer(farmer);
			string wakeup_location = homeOfFarmer.NameOrUniqueName;
			Point wakeup_point = homeOfFarmer.GetPlayerBedSpot();
			bool has_bed = homeOfFarmer.GetPlayerBed() != null;
			List<ReviveLocation> wakeUpLocations = farmer.currentLocation.GetLocationContext().PassOutLocations ?? LocationContexts.Default.PassOutLocations;
			if (wakeUpLocations != null)
			{
				foreach (ReviveLocation wakeUpLocation in wakeUpLocations)
				{
					if (!GameStateQuery.CheckConditions(wakeUpLocation.Condition, farmer.currentLocation, farmer))
					{
						continue;
					}
					GameLocation location = Game1.getLocationFromName(wakeUpLocation.Location);
					if (location == null)
					{
						break;
					}
					wakeup_location = wakeUpLocation.Location;
					wakeup_point = wakeUpLocation.Position;
					has_bed = false;
					foreach (Furniture item in location.furniture)
					{
						BedFurniture bed = item as BedFurniture;
						if (bed != null && bed.bedType != BedFurniture.BedType.Child)
						{
							wakeup_point = bed.GetBedSpot();
							has_bed = true;
							break;
						}
					}
					break;
				}
			}
			if (Game1.IsServer && farmer != Game1.player)
			{
				object[] message = new object[4] { wakeup_location, wakeup_point.X, wakeup_point.Y, has_bed };
				Game1.server.sendMessage(farmer.UniqueMultiplayerID, 29, Game1.player, message.ToArray());
			}
			else
			{
				Farmer.performPassoutWarp(farmer, wakeup_location, wakeup_point, has_bed);
			}
		}

		public virtual void receivePassout(IncomingMessage msg)
		{
			if (msg.SourceFarmer == Game1.serverHost.Value)
			{
				string wakeup_location = msg.Reader.ReadString();
				Point wakeup_point = new Point(msg.Reader.ReadInt32(), msg.Reader.ReadInt32());
				bool has_bed = msg.Reader.ReadBoolean();
				Farmer.performPassoutWarp(Game1.player, wakeup_location, wakeup_point, has_bed);
			}
		}

		public virtual void broadcastEvent(Event evt, GameLocation location, Vector2 positionBeforeEvent, bool use_local_farmer = true)
		{
			if (string.IsNullOrEmpty(evt.id) || evt.id == "-1")
			{
				return;
			}
			object[] message = new object[6]
			{
				evt.id,
				use_local_farmer,
				(int)positionBeforeEvent.X,
				(int)positionBeforeEvent.Y,
				(byte)(location.isStructure ? 1u : 0u),
				location.NameOrUniqueName
			};
			if (Game1.IsServer)
			{
				foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
				{
					if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
					{
						Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 4, Game1.player, message);
					}
				}
				return;
			}
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(4, message);
			}
		}

		protected virtual void receiveRequestGrandpaReevaluation(IncomingMessage msg)
		{
			Game1.getFarm()?.requestGrandpaReevaluation();
		}

		protected virtual void receiveFarmerKilledMonster(IncomingMessage msg)
		{
			if (msg.SourceFarmer == Game1.serverHost.Value)
			{
				string which = msg.Reader.ReadString();
				if (which != null)
				{
					Game1.stats.monsterKilled(which);
				}
			}
		}

		public virtual void broadcastRemoveLocationFromLookup(GameLocation location)
		{
			List<object> message = new List<object>();
			message.Add(location.NameOrUniqueName);
			if (Game1.IsServer)
			{
				foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
				{
					if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
					{
						Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 24, Game1.player, message.ToArray());
					}
				}
				return;
			}
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(24, message.ToArray());
			}
		}

		public virtual void broadcastNutDig(GameLocation location, Point point)
		{
			if (Game1.IsMasterGame)
			{
				_performNutDig(location, point);
				return;
			}
			List<object> message = new List<object>();
			message.Add(location.NameOrUniqueName);
			message.Add(point.X);
			message.Add(point.Y);
			Game1.client.sendMessage(27, message.ToArray());
		}

		protected virtual void receiveNutDig(IncomingMessage msg)
		{
			if (Game1.IsMasterGame)
			{
				string name = msg.Reader.ReadString();
				Point point = new Point(msg.Reader.ReadInt32(), msg.Reader.ReadInt32());
				GameLocation location = Game1.getLocationFromName(name);
				_performNutDig(location, point);
			}
		}

		protected virtual void _performNutDig(GameLocation location, Point point)
		{
			IslandLocation island_location = location as IslandLocation;
			if (island_location != null && island_location.IsBuriedNutLocation(point))
			{
				string key = location.NameOrUniqueName + "_" + point.X + "_" + point.Y;
				if (Game1.netWorldState.Value.FoundBuriedNuts.Add(key))
				{
					Game1.createItemDebris(ItemRegistry.Create("(O)73"), new Vector2(point.X, point.Y) * 64f, -1, island_location);
				}
			}
		}

		public virtual void broadcastPartyWideMail(string mail_key, PartyWideMessageQueue message_queue = PartyWideMessageQueue.MailForTomorrow, bool no_letter = false)
		{
			mail_key = mail_key.Trim();
			mail_key = mail_key.Replace(Environment.NewLine, "");
			List<object> message = new List<object>();
			message.Add(mail_key);
			message.Add((int)message_queue);
			message.Add(no_letter);
			_performPartyWideMail(mail_key, message_queue, no_letter);
			if (Game1.IsServer)
			{
				foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
				{
					if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
					{
						Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 22, Game1.player, message.ToArray());
					}
				}
				return;
			}
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(22, message.ToArray());
			}
		}

		public virtual void broadcastGrandpaReevaluation()
		{
			Game1.getFarm().requestGrandpaReevaluation();
			if (Game1.IsServer)
			{
				foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
				{
					if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
					{
						Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 26, Game1.player);
					}
				}
				return;
			}
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(26);
			}
		}

		/// <summary>Broadcast a global popup message.</summary>
		/// <param name="translationKey">The translation key for the message text.</param>
		/// <param name="onlyShowIfEmpty">Whether to show the message only when no other messages are showing.</param>
		/// <param name="location">The location where players will see the message, or <see langword="null" /> to show it everywhere.</param>
		/// <param name="substitutions">The token substitutions for placeholders in the translation text, if any.</param>
		public virtual void broadcastGlobalMessage(string translationKey, bool onlyShowIfEmpty = false, GameLocation location = null, params string[] substitutions)
		{
			if ((!onlyShowIfEmpty || Game1.hudMessages.Count == 0) && (location == null || location.NameOrUniqueName == Game1.player.currentLocation?.NameOrUniqueName))
			{
				if (translationKey.StartsWith("Strings\\1_6_Strings:FishFrenzy") && Game1.content.GetCurrentLanguage() == LocalizedContentManager.LanguageCode.en && substitutions.Count() > 0)
				{
					LocalizedContentManager content = Game1.content;
					object[] substitutions2 = new string[1] { Lexicon.capitalize(Lexicon.getProperArticleForWord(substitutions[0])) + " " + substitutions[0] };
					Game1.showGlobalMessage(content.LoadString(translationKey, substitutions2));
				}
				else
				{
					LocalizedContentManager content2 = Game1.content;
					object[] substitutions2 = substitutions;
					Game1.showGlobalMessage(content2.LoadString(translationKey, substitutions2));
				}
			}
			List<object> message = new List<object>
			{
				translationKey,
				onlyShowIfEmpty,
				location?.NameOrUniqueName ?? "",
				substitutions.Length
			};
			message.AddRange(substitutions);
			if (Game1.IsServer)
			{
				foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
				{
					if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
					{
						Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 21, Game1.player, message.ToArray());
					}
				}
				return;
			}
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(21, message.ToArray());
			}
		}

		public virtual NetRoot<T> readObjectFull<T>(BinaryReader reader) where T : class, INetObject<INetSerializable>
		{
			NetRoot<T> netRoot = NetRoot<T>.Connect(reader);
			netRoot.Clock.InterpolationTicks = defaultInterpolationTicks;
			return netRoot;
		}

		protected virtual BinaryWriter createWriter(Stream stream)
		{
			BinaryWriter writer = new BinaryWriter(stream);
			if (logging.IsLogging)
			{
				writer = new LoggingBinaryWriter(writer);
			}
			return writer;
		}

		public virtual void writeObjectFull<T>(BinaryWriter writer, NetRoot<T> root, long? peer) where T : class, INetObject<INetSerializable>
		{
			root.CreateConnectionPacket(writer, peer);
		}

		public virtual byte[] writeObjectFullBytes<T>(NetRoot<T> root, long? peer) where T : class, INetObject<INetSerializable>
		{
			using (MemoryStream stream = new MemoryStream())
			{
				using (BinaryWriter writer = createWriter(stream))
				{
					root.CreateConnectionPacket(writer, peer);
					return stream.ToArray();
				}
			}
		}

		public virtual void readObjectDelta<T>(BinaryReader reader, NetRoot<T> root) where T : class, INetObject<INetSerializable>
		{
			root.Read(reader);
		}

		public virtual void writeObjectDelta<T>(BinaryWriter writer, NetRoot<T> root) where T : class, INetObject<INetSerializable>
		{
			root.Write(writer);
		}

		public virtual byte[] writeObjectDeltaBytes<T>(NetRoot<T> root) where T : class, INetObject<INetSerializable>
		{
			using (MemoryStream stream = new MemoryStream())
			{
				using (BinaryWriter writer = createWriter(stream))
				{
					root.Write(writer);
					return stream.ToArray();
				}
			}
		}

		public virtual NetFarmerRoot readFarmer(BinaryReader reader)
		{
			NetFarmerRoot netFarmerRoot = new NetFarmerRoot();
			netFarmerRoot.ReadConnectionPacket(reader);
			netFarmerRoot.Clock.InterpolationTicks = defaultInterpolationTicks;
			return netFarmerRoot;
		}

		public virtual void addPlayer(NetFarmerRoot f)
		{
			long id = f.Value.UniqueMultiplayerID;
			f.Value.teamRoot = Game1.player.teamRoot;
			Game1.otherFarmers.Roots[id] = f;
			disconnectingFarmers.Remove(id);
			if (Game1.chatBox != null)
			{
				string farmerName = ChatBox.formattedUserNameLong(f.Value);
				Game1.chatBox.addInfoMessage(Game1.content.LoadString("Strings\\UI:Chat_PlayerJoined", farmerName));
			}
		}

		public virtual void receivePlayerIntroduction(BinaryReader reader)
		{
			addPlayer(readFarmer(reader));
		}

		public virtual void broadcastPlayerIntroduction(NetFarmerRoot farmerRoot)
		{
			if (Game1.server == null)
			{
				return;
			}
			foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
			{
				if (farmerRoot.Value.UniqueMultiplayerID != v.Value.UniqueMultiplayerID)
				{
					Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 2, farmerRoot.Value, Game1.server.getUserName(farmerRoot.Value.UniqueMultiplayerID), writeObjectFullBytes(farmerRoot, v.Value.UniqueMultiplayerID));
				}
			}
		}

		public virtual void broadcastUserName(long farmerId, string userName)
		{
			if (Game1.server != null)
			{
				return;
			}
			foreach (KeyValuePair<long, Farmer> otherFarmer in Game1.otherFarmers)
			{
				Farmer farmer = otherFarmer.Value;
				if (farmer.UniqueMultiplayerID != farmerId)
				{
					Game1.server.sendMessage(farmer.UniqueMultiplayerID, 16, Game1.serverHost.Value, farmerId, userName);
				}
			}
		}

		public virtual string getUserName(long id)
		{
			if (id == Game1.player.UniqueMultiplayerID)
			{
				return Game1.content.LoadString("Strings\\UI:Chat_SelfPlayerID");
			}
			if (Game1.server != null)
			{
				return Game1.server.getUserName(id);
			}
			if (Game1.client != null)
			{
				return Game1.client.getUserName(id);
			}
			return "?";
		}

		public virtual void playerDisconnected(long id)
		{
			NetRoot<Farmer> otherFarmer;
			if (Game1.otherFarmers.Roots.TryGetValue(id, out otherFarmer) && !disconnectingFarmers.Contains(id))
			{
				NetFarmerRoot farmhand = otherFarmer as NetFarmerRoot;
				if (farmhand.Value.mount != null && Game1.IsMasterGame)
				{
					farmhand.Value.mount.dismount();
				}
				if (Game1.IsMasterGame)
				{
					farmhand.TargetValue.handleDisconnect();
					farmhand.TargetValue.companions.Clear();
					saveFarmhand(farmhand);
					farmhand.Value.handleDisconnect();
				}
				if (Game1.player.dancePartner.Value is Farmer && ((Farmer)Game1.player.dancePartner.Value).UniqueMultiplayerID == farmhand.Value.UniqueMultiplayerID)
				{
					Game1.player.dancePartner.Value = null;
				}
				if (Game1.chatBox != null)
				{
					Game1.chatBox.addInfoMessage(Game1.content.LoadString("Strings\\UI:Chat_PlayerLeft", ChatBox.formattedUserNameLong(Game1.otherFarmers[id])));
				}
				disconnectingFarmers.Add(id);
			}
		}

		protected virtual void removeDisconnectedFarmers()
		{
			foreach (long id in disconnectingFarmers)
			{
				Game1.otherFarmers.Remove(id);
			}
			disconnectingFarmers.Clear();
		}

		public virtual void sendFarmhand()
		{
			(Game1.player.NetFields.Root as NetFarmerRoot).MarkReassigned();
		}

		protected virtual void saveFarmhand(NetFarmerRoot farmhand)
		{
			Game1.netWorldState.Value.SaveFarmhand(farmhand);
		}

		public virtual void saveFarmhands()
		{
			if (!Game1.IsMasterGame)
			{
				return;
			}
			foreach (NetRoot<Farmer> farmer in Game1.otherFarmers.Roots.Values)
			{
				saveFarmhand(farmer as NetFarmerRoot);
			}
		}

		public virtual void clientRemotelyDisconnected(DisconnectType disconnectType)
		{
			LogDisconnect(disconnectType);
			returnToMainMenu();
		}

		private void returnToMainMenu()
		{
			if (!Game1.game1.IsMainInstance)
			{
				GameRunner.instance.RemoveGameInstance(Game1.game1);
				return;
			}
			Game1.ExitToTitle(delegate
			{
				(Game1.activeClickableMenu as TitleMenu).skipToTitleButtons();
				TitleMenu.subMenu = new ConfirmationDialog(Game1.content.LoadString("Strings\\UI:Client_RemotelyDisconnected"), null)
				{
					okButton = 
					{
						visible = false
					}
				};
			});
		}

		public static bool ShouldLogDisconnect(DisconnectType disconnectType)
		{
			switch (disconnectType)
			{
			case DisconnectType.ClosedGame:
			case DisconnectType.ExitedToMainMenu:
			case DisconnectType.ExitedToMainMenu_FromFarmhandSelect:
			case DisconnectType.ServerOfflineMode:
			case DisconnectType.ServerFull:
			case DisconnectType.AcceptedOtherInvite:
				return false;
			default:
				return true;
			}
		}

		public static bool IsTimeout(DisconnectType disconnectType)
		{
			if ((uint)(disconnectType - 9) <= 2u)
			{
				return true;
			}
			return false;
		}

		public static void LogDisconnect(DisconnectType disconnectType)
		{
			if (ShouldLogDisconnect(disconnectType))
			{
				string message = "Disconnected at : " + DateTime.Now.ToLongTimeString() + " - " + disconnectType;
				if (Game1.client != null)
				{
					message = message + " Ping: " + Game1.client.GetPingToHost().ToString("0.#");
					message += ((Game1.client is LidgrenClient) ? " ip" : " friend/invite");
				}
				Program.WriteLog(Program.LogType.Disconnect, message, true);
			}
			Game1.log.Verbose("Disconnected: " + disconnectType);
		}

		public virtual void sendSharedAchievementMessage(int achievement)
		{
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(20, achievement);
			}
			else
			{
				if (!Game1.IsServer)
				{
					return;
				}
				foreach (long id in Game1.otherFarmers.Keys)
				{
					Game1.server.sendMessage(id, 20, Game1.player, achievement);
				}
			}
		}

		public virtual void sendServerToClientsMessage(string message)
		{
			if (!Game1.IsServer)
			{
				return;
			}
			foreach (KeyValuePair<long, Farmer> otherFarmer in Game1.otherFarmers)
			{
				otherFarmer.Value.queueMessage(18, Game1.player, message);
			}
		}

		public virtual void sendChatMessage(LocalizedContentManager.LanguageCode language, string message, long recipientID)
		{
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(10, recipientID, language, message);
			}
			else
			{
				if (!Game1.IsServer)
				{
					return;
				}
				if (recipientID == AllPlayers)
				{
					foreach (long id in Game1.otherFarmers.Keys)
					{
						Game1.server.sendMessage(id, 10, Game1.player, recipientID, language, message);
					}
					return;
				}
				Game1.server.sendMessage(recipientID, 10, Game1.player, recipientID, language, message);
			}
		}

		public virtual void receiveChatMessage(Farmer sourceFarmer, long recipientID, LocalizedContentManager.LanguageCode language, string message)
		{
			if (Game1.chatBox != null)
			{
				int messageType = 0;
				message = Program.sdk.FilterDirtyWords(message);
				if (recipientID != AllPlayers)
				{
					messageType = 3;
				}
				Game1.chatBox.receiveChatMessage(sourceFarmer.UniqueMultiplayerID, messageType, language, message);
			}
		}

		/// <summary>In multiplayer, send a chat messages to all connected players including the current player. In single-player, do nothing.</summary>
		/// <inheritdoc cref="M:StardewValley.Multiplayer.receiveChatInfoMessage(StardewValley.Farmer,System.String,System.String[])" />
		public virtual void globalChatInfoMessage(string messageKey, params string[] args)
		{
			if (Game1.IsMultiplayer || Game1.multiplayerMode != 0)
			{
				receiveChatInfoMessage(Game1.player, messageKey, args);
				sendChatInfoMessage(messageKey, args);
			}
		}

		/// <summary>Send a chat messages to all connected players including the current player.</summary>
		/// <inheritdoc cref="M:StardewValley.Multiplayer.receiveChatInfoMessage(StardewValley.Farmer,System.String,System.String[])" />
		public void globalChatInfoMessageEvenInSinglePlayer(string messageKey, params string[] args)
		{
			receiveChatInfoMessage(Game1.player, messageKey, args);
			sendChatInfoMessage(messageKey, args);
		}

		/// <summary>Send a chat messages to all connected players, excluding the current player.</summary>
		/// <inheritdoc cref="M:StardewValley.Multiplayer.receiveChatInfoMessage(StardewValley.Farmer,System.String,System.String[])" />
		protected virtual void sendChatInfoMessage(string messageKey, params string[] args)
		{
			if (Game1.IsClient)
			{
				Game1.client.sendMessage(15, messageKey, args);
			}
			else
			{
				if (!Game1.IsServer)
				{
					return;
				}
				foreach (long id in Game1.otherFarmers.Keys)
				{
					Game1.server.sendMessage(id, 15, Game1.player, messageKey, args);
				}
			}
		}

		/// <summary>Receive a chat message sent via a method like <see cref="M:StardewValley.Multiplayer.globalChatInfoMessage(System.String,System.String[])" /> or <see cref="M:StardewValley.Multiplayer.sendChatInfoMessage(System.String,System.String[])" />.</summary>
		/// <param name="sourceFarmer">The player who sent the message.</param>
		/// <param name="messageKey">The translation key to show. This is prefixed with <c>Strings\UI:Chat_</c> automatically.</param>
		/// <param name="args">The values with which to replace placeholders in the translation text. Localizable values should be <see cref="T:StardewValley.TokenizableStrings.TokenParser">tokenized strings</see> or special tokens like <see cref="F:StardewValley.Multiplayer.chat_token_aOrAn" />, since other players may not be playing in the same language.</param>
		protected virtual void receiveChatInfoMessage(Farmer sourceFarmer, string messageKey, string[] args)
		{
			if (Game1.chatBox == null)
			{
				return;
			}
			try
			{
				string[] processedArgs = args.Select((string arg) => arg.StartsWith("aOrAn:") ? Utility.AOrAn(TokenParser.ParseText(arg.Substring("aOrAn:".Length))) : TokenParser.ParseText(arg)).ToArray();
				ChatBox chatBox = Game1.chatBox;
				LocalizedContentManager content = Game1.content;
				string path = "Strings\\UI:Chat_" + messageKey;
				object[] substitutions = processedArgs;
				chatBox.addInfoMessage(content.LoadString(path, substitutions));
			}
			catch (ContentLoadException)
			{
			}
			catch (FormatException)
			{
			}
			catch (OverflowException)
			{
			}
			catch (KeyNotFoundException)
			{
			}
		}

		public virtual void parseServerToClientsMessage(string message)
		{
			if (!Game1.IsClient)
			{
				return;
			}
			switch (message)
			{
			case "festivalEvent":
				if (Game1.currentLocation.currentEvent != null)
				{
					Game1.currentLocation.currentEvent.forceFestivalContinue();
				}
				break;
			case "endFest":
				if (Game1.CurrentEvent != null)
				{
					Game1.CurrentEvent.forceEndFestival(Game1.player);
				}
				break;
			case "trainApproach":
				(Game1.getLocationFromName("Railroad") as Railroad)?.PlayTrainApproach();
				break;
			}
		}

		public virtual IEnumerable<GameLocation> activeLocations()
		{
			if (Game1.currentLocation != null)
			{
				yield return Game1.currentLocation;
			}
			foreach (GameLocation location in Game1.locations)
			{
				if (!isAlwaysActiveLocation(location))
				{
					continue;
				}
				foreach (GameLocation item in _GetActiveLocationsHere(location))
				{
					yield return item;
				}
			}
		}

		protected virtual IEnumerable<GameLocation> _GetActiveLocationsHere(GameLocation location)
		{
			if (location != Game1.currentLocation)
			{
				yield return location;
			}
			foreach (Building building in location.buildings)
			{
				GameLocation indoors = building.GetIndoors();
				if (indoors == null || (indoors.isAlwaysActive.Value && building.GetIndoorsType() == IndoorsType.Global))
				{
					continue;
				}
				foreach (GameLocation item in _GetActiveLocationsHere(indoors))
				{
					yield return item;
				}
			}
		}

		public virtual bool isAlwaysActiveLocation(GameLocation location)
		{
			if (location.Root != null && location.Root.Value != location && isAlwaysActiveLocation(location.Root.Value))
			{
				return true;
			}
			return location.isAlwaysActive.Value;
		}

		protected virtual void readActiveLocation(IncomingMessage msg)
		{
			bool force_current_location = msg.Reader.ReadBoolean();
			NetRoot<GameLocation> root = readObjectFull<GameLocation>(msg.Reader);
			if (isAlwaysActiveLocation(root.Value))
			{
				for (int i = 0; i < Game1.locations.Count; i++)
				{
					GameLocation local = Game1.locations[i];
					if (!local.Equals(root.Value))
					{
						continue;
					}
					if (local == root.Value)
					{
						break;
					}
					if (local != null)
					{
						if (Game1.currentLocation == local)
						{
							Game1.currentLocation = root.Value;
						}
						if (Game1.player.currentLocation == local)
						{
							Game1.player.currentLocation = root.Value;
						}
						Game1.removeLocationFromLocationLookup(local);
					}
					Game1.locations[i] = root.Value;
					break;
				}
			}
			if (Game1.locationRequest != null || force_current_location)
			{
				if (Game1.locationRequest != null)
				{
					Game1.currentLocation = Game1.findStructure(root.Value, Game1.locationRequest.Name) ?? root.Value;
				}
				else if (force_current_location)
				{
					Game1.currentLocation = root.Value;
				}
				if (Game1.locationRequest != null)
				{
					Game1.locationRequest.Location = Game1.currentLocation;
					Game1.locationRequest.Loaded(Game1.currentLocation);
				}
				if (Game1.client != null || !(Game1.activeClickableMenu is TitleMenu) || (TitleMenu.subMenu as FarmhandMenu)?.client == null)
				{
					Game1.currentLocation.resetForPlayerEntry();
				}
				Game1.player.currentLocation = Game1.currentLocation;
				Game1.locationRequest?.Warped(Game1.currentLocation);
				Game1.currentLocation.updateSeasonalTileSheets();
				Game1.locationRequest = null;
			}
		}

		public virtual bool isActiveLocation(GameLocation location)
		{
			if (Game1.IsMasterGame)
			{
				return true;
			}
			if ((object)location?.Root == null)
			{
				return false;
			}
			if (Game1.currentLocation != null && Game1.currentLocation.Root != null && Game1.currentLocation.Root.Value == location.Root.Value)
			{
				return true;
			}
			return isAlwaysActiveLocation(location);
		}

		protected virtual GameLocation readLocation(BinaryReader reader)
		{
			bool structure = reader.ReadByte() != 0;
			GameLocation location = Game1.getLocationFromName(reader.ReadString(), structure);
			if (location == null || locationRoot(location) == null)
			{
				return null;
			}
			if (!isActiveLocation(location))
			{
				return null;
			}
			return location;
		}

		protected virtual LocationRequest readLocationRequest(BinaryReader reader)
		{
			bool structure = reader.ReadByte() != 0;
			return Game1.getLocationRequest(reader.ReadString(), structure);
		}

		protected virtual NPC readNPC(BinaryReader reader)
		{
			GameLocation gameLocation = readLocation(reader);
			Guid guid = reader.ReadGuid();
			NPC npc;
			if (!gameLocation.characters.TryGetValue(guid, out npc))
			{
				return null;
			}
			return npc;
		}

		public virtual void readSprites(BinaryReader reader, GameLocation location, Action<TemporaryAnimatedSprite> assignSprite)
		{
			int count = reader.ReadInt32();
			TemporaryAnimatedSprite[] result = new TemporaryAnimatedSprite[count];
			for (int i = 0; i < count; i++)
			{
				TemporaryAnimatedSprite sprite = TemporaryAnimatedSprite.GetTemporaryAnimatedSprite();
				sprite.Read(reader, location);
				sprite.ticksBeforeAnimationStart += interpolationTicks();
				result[i] = sprite;
				assignSprite(sprite);
			}
		}

		protected virtual void receiveTeamDelta(BinaryReader msg)
		{
			readObjectDelta(msg, Game1.player.teamRoot);
		}

		protected virtual void receiveNewDaySync(IncomingMessage msg)
		{
			if (!Game1.newDaySync.hasInstance() && msg.SourceFarmer == Game1.serverHost.Value)
			{
				Game1.NewDay(0f);
			}
			if (Game1.newDaySync.hasInstance())
			{
				Game1.newDaySync.receiveMessage(msg);
			}
		}

		protected virtual void receiveFarmerGainExperience(IncomingMessage msg)
		{
			if (msg.SourceFarmer == Game1.serverHost.Value)
			{
				int which = msg.Reader.ReadInt32();
				int howMuch = msg.Reader.ReadInt32();
				Game1.player.gainExperience(which, howMuch);
			}
		}

		protected virtual void receiveSharedAchievement(IncomingMessage msg)
		{
			Game1.getAchievement(msg.Reader.ReadInt32(), false);
		}

		protected virtual void receiveRemoveLocationFromLookup(IncomingMessage msg)
		{
			Game1.removeLocationFromLocationLookup(msg.Reader.ReadString());
		}

		protected virtual void receivePartyWideMail(IncomingMessage msg)
		{
			string mail_key = msg.Reader.ReadString();
			PartyWideMessageQueue message_queue = (PartyWideMessageQueue)msg.Reader.ReadInt32();
			bool no_letter = msg.Reader.ReadBoolean();
			_performPartyWideMail(mail_key, message_queue, no_letter);
		}

		protected void _performPartyWideMail(string mail_key, PartyWideMessageQueue message_queue, bool no_letter)
		{
			switch (message_queue)
			{
			case PartyWideMessageQueue.MailForTomorrow:
				Game1.addMailForTomorrow(mail_key, no_letter);
				break;
			case PartyWideMessageQueue.SeenMail:
				Game1.addMail(mail_key, no_letter);
				break;
			}
			if (no_letter)
			{
				mail_key += "%&NL&%";
			}
			switch (message_queue)
			{
			case PartyWideMessageQueue.MailForTomorrow:
				mail_key = "%&MFT&%" + mail_key;
				break;
			case PartyWideMessageQueue.SeenMail:
				mail_key = "%&SM&%" + mail_key;
				break;
			}
			if (Game1.IsMasterGame && !Game1.player.team.broadcastedMail.Contains(mail_key))
			{
				Game1.player.team.broadcastedMail.Add(mail_key);
			}
		}

		protected void receiveForceKick()
		{
			if (!Game1.IsServer)
			{
				Disconnect(DisconnectType.Kicked);
				returnToMainMenu();
			}
		}

		protected virtual void receiveGlobalMessage(IncomingMessage msg)
		{
			string translationKey = msg.Reader.ReadString();
			bool num = msg.Reader.ReadBoolean();
			string locationName = msg.Reader.ReadString();
			if ((!num || Game1.hudMessages.Count <= 0) && (string.IsNullOrEmpty(locationName) || !(locationName != Game1.player.currentLocation?.NameOrUniqueName)))
			{
				int count = msg.Reader.ReadInt32();
				object[] substitutions = new object[count];
				for (int i = 0; i < count; i++)
				{
					substitutions[i] = msg.Reader.ReadString();
				}
				if (translationKey.StartsWith("Strings\\1_6_Strings:FishFrenzy") && Game1.content.GetCurrentLanguage() == LocalizedContentManager.LanguageCode.en && substitutions.Count() > 0)
				{
					substitutions[0] = Lexicon.capitalize(Lexicon.getProperArticleForWord((string)substitutions[0])) + " " + substitutions[0];
				}
				Game1.showGlobalMessage(Game1.content.LoadString(translationKey, substitutions));
			}
		}

		protected void receiveStartNewDaySync()
		{
			Game1.newDaySync.flagServerReady();
		}

		protected void receiveReadySync(IncomingMessage msg)
		{
			Game1.netReady.ProcessMessage(msg);
		}

		protected void receiveChestHitSync(IncomingMessage msg)
		{
			Game1.player.team.chestHit.ProcessMessage(msg);
		}

		public virtual void processIncomingMessage(IncomingMessage msg)
		{
			_003C_003Ec__DisplayClass147_1 CS_0024_003C_003E8__locals1 = new _003C_003Ec__DisplayClass147_1();
			GameLocation location;
			switch (msg.MessageType)
			{
			case 0:
			{
				long f = msg.Reader.ReadInt64();
				NetFarmerRoot farmer = farmerRoot(f);
				if (farmer != null)
				{
					readObjectDelta(msg.Reader, farmer);
				}
				break;
			}
			case 3:
				readActiveLocation(msg);
				break;
			case 6:
				location = readLocation(msg.Reader);
				if (location != null)
				{
					readObjectDelta(msg.Reader, location.Root);
				}
				break;
			case 7:
				location = readLocation(msg.Reader);
				if (location != null)
				{
					readSprites(msg.Reader, location, delegate(TemporaryAnimatedSprite sprite)
					{
						location.temporarySprites.Add(sprite);
					});
				}
				break;
			case 8:
			{
				NPC character = readNPC(msg.Reader);
				location = readLocation(msg.Reader);
				if (character != null && location != null)
				{
					Game1.warpCharacter(character, location, msg.Reader.ReadVector2());
				}
				break;
			}
			case 4:
			{
				CS_0024_003C_003E8__locals1.eventId = msg.Reader.ReadString();
				bool use_local_farmer = msg.Reader.ReadBoolean();
				CS_0024_003C_003E8__locals1.tileX = msg.Reader.ReadInt32();
				CS_0024_003C_003E8__locals1.tileY = msg.Reader.ReadInt32();
				CS_0024_003C_003E8__locals1.request = readLocationRequest(msg.Reader);
				if (Game1.getLocationFromName(CS_0024_003C_003E8__locals1.request.Name)?.findEventById(CS_0024_003C_003E8__locals1.eventId) == null)
				{
					Game1.log.Warn("Couldn't find event " + CS_0024_003C_003E8__locals1.eventId + " for broadcast event!");
					break;
				}
				CS_0024_003C_003E8__locals1.farmerActor = (use_local_farmer ? (Game1.player.NetFields.Root as NetRoot<Farmer>).Clone().Value : (msg.SourceFarmer.NetFields.Root as NetRoot<Farmer>).Clone().Value);
				CS_0024_003C_003E8__locals1.oldTile = Game1.player.TilePoint;
				CS_0024_003C_003E8__locals1.oldLocation = Game1.player.currentLocation.NameOrUniqueName;
				CS_0024_003C_003E8__locals1.direction = Game1.player.facingDirection.Value;
				Game1.player.locationBeforeForcedEvent.Value = CS_0024_003C_003E8__locals1.oldLocation;
				CS_0024_003C_003E8__locals1.request.OnWarp += delegate
				{
					CS_0024_003C_003E8__locals1.farmerActor.currentLocation = Game1.currentLocation;
					CS_0024_003C_003E8__locals1.farmerActor.completelyStopAnimatingOrDoingAction();
					CS_0024_003C_003E8__locals1.farmerActor.UsingTool = false;
					CS_0024_003C_003E8__locals1.farmerActor.Items.Clear();
					CS_0024_003C_003E8__locals1.farmerActor.hidden.Value = false;
					Event @event = Game1.currentLocation.findEventById(CS_0024_003C_003E8__locals1.eventId, CS_0024_003C_003E8__locals1.farmerActor);
					Game1.currentLocation.startEvent(@event);
					CS_0024_003C_003E8__locals1.farmerActor.Position = Game1.player.Position;
					Game1.warpingForForcedRemoteEvent = false;
					string value = Game1.player.locationBeforeForcedEvent.Value;
					Game1.player.locationBeforeForcedEvent.Value = null;
					@event.setExitLocation(CS_0024_003C_003E8__locals1.oldLocation, CS_0024_003C_003E8__locals1.oldTile.X, CS_0024_003C_003E8__locals1.oldTile.Y);
					Game1.player.locationBeforeForcedEvent.Value = value;
					Game1.player.orientationBeforeEvent = CS_0024_003C_003E8__locals1.direction;
				};
				Game1.remoteEventQueue.Add(CS_0024_003C_003E8__locals1._003CprocessIncomingMessage_003Eg__PerformForcedEvent_007C2);
				break;
			}
			case 10:
			{
				long recipientId = msg.Reader.ReadInt64();
				LocalizedContentManager.LanguageCode langCode = msg.Reader.ReadEnum<LocalizedContentManager.LanguageCode>();
				string message = msg.Reader.ReadString();
				receiveChatMessage(msg.SourceFarmer, recipientId, langCode, message);
				break;
			}
			case 15:
			{
				string messageKey = msg.Reader.ReadString();
				string[] args = new string[msg.Reader.ReadByte()];
				for (int i = 0; i < args.Length; i++)
				{
					args[i] = msg.Reader.ReadString();
				}
				receiveChatInfoMessage(msg.SourceFarmer, messageKey, args);
				break;
			}
			case 2:
				receivePlayerIntroduction(msg.Reader);
				break;
			case 12:
				receiveWorldState(msg.Reader);
				break;
			case 13:
				receiveTeamDelta(msg.Reader);
				break;
			case 14:
				receiveNewDaySync(msg);
				break;
			case 18:
				parseServerToClientsMessage(msg.Reader.ReadString());
				break;
			case 19:
				playerDisconnected(msg.SourceFarmer.UniqueMultiplayerID);
				break;
			case 17:
				receiveFarmerGainExperience(msg);
				break;
			case 25:
				receiveFarmerKilledMonster(msg);
				break;
			case 20:
				receiveSharedAchievement(msg);
				break;
			case 21:
				receiveGlobalMessage(msg);
				break;
			case 22:
				receivePartyWideMail(msg);
				break;
			case 27:
				receiveNutDig(msg);
				break;
			case 23:
				receiveForceKick();
				break;
			case 24:
				receiveRemoveLocationFromLookup(msg);
				break;
			case 26:
				receiveRequestGrandpaReevaluation(msg);
				break;
			case 28:
				receivePassoutRequest(msg);
				break;
			case 29:
				receivePassout(msg);
				break;
			case 30:
				receiveStartNewDaySync();
				break;
			case 31:
				receiveReadySync(msg);
				break;
			case 32:
				receiveChestHitSync(msg);
				break;
			case 127:
				Game1.log.Warn("Unexpectedly received a compressed multiplayer message that wasn't decompressed by the net client.");
				break;
			}
		}

		public virtual void StartLocalMultiplayerServer()
		{
			Game1.server = new GameServer(true);
			Game1.server.startServer();
		}

		public virtual void StartServer()
		{
			Game1.server = new GameServer();
			Game1.server.startServer();
		}

		public virtual void Disconnect(DisconnectType disconnectType)
		{
			if (Game1.server != null)
			{
				Game1.server.stopServer();
				Game1.server = null;
				foreach (long id in Game1.otherFarmers.Keys)
				{
					playerDisconnected(id);
				}
			}
			if (Game1.client != null)
			{
				sendFarmhand();
				UpdateLate(true);
				Game1.client.disconnect();
				Game1.client = null;
			}
			Game1.otherFarmers.Clear();
			LogDisconnect(disconnectType);
		}

		protected virtual void updatePendingConnections()
		{
			switch (Game1.multiplayerMode)
			{
			case 2:
				if (Game1.server == null && Game1.options.enableServer)
				{
					StartServer();
				}
				break;
			case 1:
				if (Game1.client != null && !Game1.client.readyToPlay)
				{
					Game1.client.receiveMessages();
				}
				break;
			}
		}

		public void UpdateLoading()
		{
			updatePendingConnections();
			if (Game1.server != null)
			{
				Game1.server.receiveMessages();
			}
		}

		public virtual void UpdateEarly()
		{
			updatePendingConnections();
			if (Game1.multiplayerMode == 2 && Game1.serverHost == null && Game1.options.enableServer)
			{
				Game1.server.initializeHost();
			}
			if (Game1.server != null)
			{
				Game1.server.receiveMessages();
			}
			else if (Game1.client != null)
			{
				Game1.client.receiveMessages();
			}
			updateRoots();
			if (Game1.CurrentEvent == null)
			{
				removeDisconnectedFarmers();
			}
		}

		public virtual void UpdateLate(bool forceSync = false)
		{
			if (Game1.multiplayerMode != 0)
			{
				if (!allowSyncDelay() || forceSync || Game1.ticks % farmerDeltaBroadcastPeriod == 0)
				{
					broadcastFarmerDeltas();
				}
				if (!allowSyncDelay() || forceSync || Game1.ticks % locationDeltaBroadcastPeriod == 0)
				{
					broadcastLocationDeltas();
				}
				if (!allowSyncDelay() || forceSync || Game1.ticks % worldStateDeltaBroadcastPeriod == 0)
				{
					broadcastWorldStateDeltas();
				}
			}
			if (Game1.server != null)
			{
				Game1.server.sendMessages();
			}
			if (Game1.client != null)
			{
				Game1.client.sendMessages();
			}
		}

		public virtual void inviteAccepted()
		{
			TitleMenu title = Game1.activeClickableMenu as TitleMenu;
			if (title == null)
			{
				return;
			}
			IClickableMenu subMenu = TitleMenu.subMenu;
			if (subMenu != null)
			{
				if (subMenu is FarmhandMenu || subMenu is CoopMenu)
				{
					TitleMenu.subMenu = new FarmhandMenu();
				}
			}
			else
			{
				title.performButtonAction("Invite");
			}
		}

		public virtual Client InitClient(Client client)
		{
			return client;
		}

		public virtual Server InitServer(Server server)
		{
			return server;
		}
	}
}
