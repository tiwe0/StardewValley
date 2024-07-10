using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Netcode;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Mods;
using StardewValley.Monsters;

namespace StardewValley.Quests
{
	[XmlInclude(typeof(CraftingQuest))]
	[XmlInclude(typeof(DescriptionElement))]
	[XmlInclude(typeof(FishingQuest))]
	[XmlInclude(typeof(GoSomewhereQuest))]
	[XmlInclude(typeof(ItemDeliveryQuest))]
	[XmlInclude(typeof(ItemHarvestQuest))]
	[XmlInclude(typeof(LostItemQuest))]
	[XmlInclude(typeof(ResourceCollectionQuest))]
	[XmlInclude(typeof(SecretLostItemQuest))]
	[XmlInclude(typeof(SlayMonsterQuest))]
	[XmlInclude(typeof(SocializeQuest))]
	public class Quest : INetObject<NetFields>, IQuest, IHaveModData
	{
		public const int type_basic = 1;

		public const int type_crafting = 2;

		public const int type_itemDelivery = 3;

		public const int type_monster = 4;

		public const int type_socialize = 5;

		public const int type_location = 6;

		public const int type_fishing = 7;

		public const int type_building = 8;

		public const int type_harvest = 9;

		public const int type_resource = 10;

		public const int type_weeding = 11;

		public string _currentObjective = "";

		public string _questDescription = "";

		public string _questTitle = "";

		[XmlElement("rewardDescription")]
		public readonly NetString rewardDescription = new NetString();

		[XmlElement("completionString")]
		public readonly NetString completionString = new NetString();

		protected Random random = Utility.CreateRandom(Game1.uniqueIDForThisGame, Game1.stats.DaysPlayed);

		[XmlElement("accepted")]
		public readonly NetBool accepted = new NetBool();

		[XmlElement("completed")]
		public readonly NetBool completed = new NetBool();

		[XmlElement("dailyQuest")]
		public readonly NetBool dailyQuest = new NetBool();

		[XmlElement("showNew")]
		public readonly NetBool showNew = new NetBool();

		[XmlElement("canBeCancelled")]
		public readonly NetBool canBeCancelled = new NetBool();

		[XmlElement("destroy")]
		public readonly NetBool destroy = new NetBool();

		[XmlElement("id")]
		public readonly NetString id = new NetString();

		[XmlElement("moneyReward")]
		public readonly NetInt moneyReward = new NetInt();

		[XmlElement("questType")]
		public readonly NetInt questType = new NetInt();

		[XmlElement("daysLeft")]
		public readonly NetInt daysLeft = new NetInt();

		[XmlElement("dayQuestAccepted")]
		public readonly NetInt dayQuestAccepted = new NetInt(-1);

		[XmlArrayItem("int")]
		public readonly NetStringList nextQuests = new NetStringList();

		private bool _loadedDescription;

		protected bool _loadedTitle;

		/// <inheritdoc />
		[XmlIgnore]
		public ModDataDictionary modData { get; } = new ModDataDictionary();


		/// <inheritdoc />
		[XmlElement("modData")]
		public ModDataDictionary modDataForSerialization
		{
			get
			{
				return modData.GetForSerialization();
			}
			set
			{
				modData.SetFromSerialization(value);
			}
		}

		public NetFields NetFields { get; }

		public string questTitle
		{
			get
			{
				if (!_loadedTitle)
				{
					switch (questType.Value)
					{
					case 3:
					{
						ItemDeliveryQuest quest = this as ItemDeliveryQuest;
						if (quest != null && quest.target.Value != null)
						{
							_questTitle = Game1.content.LoadString("Strings\\1_6_Strings:ItemDeliveryQuestTitle", NPC.GetDisplayName(quest.target.Value));
						}
						else
						{
							_questTitle = Game1.content.LoadString("Strings\\StringsFromCSFiles:ItemDeliveryQuest.cs.13285");
						}
						break;
					}
					case 4:
					{
						SlayMonsterQuest quest2 = this as SlayMonsterQuest;
						if (quest2 != null && quest2.monsterName.Value != null)
						{
							_questTitle = Game1.content.LoadString("Strings\\1_6_Strings:MonsterQuestTitle", Monster.GetDisplayName(quest2.monsterName.Value));
						}
						else
						{
							_questTitle = Game1.content.LoadString("Strings\\StringsFromCSFiles:SlayMonsterQuest.cs.13696");
						}
						break;
					}
					case 5:
						_questTitle = Game1.content.LoadString("Strings\\StringsFromCSFiles:SocializeQuest.cs.13785");
						break;
					case 7:
					{
						FishingQuest quest3 = this as FishingQuest;
						if (quest3 != null && quest3.ItemId.Value != null)
						{
							string fishName = "???";
							ParsedItemData data = ItemRegistry.GetDataOrErrorItem(quest3.ItemId.Value);
							if (!data.IsErrorItem)
							{
								fishName = data.DisplayName;
							}
							_questTitle = Game1.content.LoadString("Strings\\1_6_Strings:FishingQuestTitle", fishName);
						}
						else
						{
							_questTitle = Game1.content.LoadString("Strings\\StringsFromCSFiles:FishingQuest.cs.13227");
						}
						break;
					}
					case 10:
					{
						ResourceCollectionQuest quest4 = this as ResourceCollectionQuest;
						if (quest4 != null && quest4.ItemId.Value != null)
						{
							string resourceName = "???";
							ParsedItemData data2 = ItemRegistry.GetDataOrErrorItem(quest4.ItemId.Value);
							if (!data2.IsErrorItem)
							{
								resourceName = data2.DisplayName;
							}
							_questTitle = Game1.content.LoadString("Strings\\1_6_Strings:ResourceQuestTitle", resourceName);
						}
						else
						{
							_questTitle = Game1.content.LoadString("Strings\\StringsFromCSFiles:ResourceCollectionQuest.cs.13640");
						}
						break;
					}
					}
					string[] fields = GetRawQuestFields(id.Value);
					_questTitle = ArgUtility.Get(fields, 1, _questTitle);
					_loadedTitle = true;
				}
				if (_questTitle == null)
				{
					_questTitle = "";
				}
				return _questTitle;
			}
			set
			{
				_questTitle = value;
			}
		}

		[XmlIgnore]
		public string questDescription
		{
			get
			{
				if (!_loadedDescription)
				{
					reloadDescription();
					string[] fields = GetRawQuestFields(id.Value);
					_questDescription = ArgUtility.Get(fields, 2, _questDescription);
					_loadedDescription = true;
				}
				if (_questDescription == null)
				{
					_questDescription = "";
				}
				return _questDescription;
			}
			set
			{
				_questDescription = value;
			}
		}

		[XmlIgnore]
		public string currentObjective
		{
			get
			{
				string[] fields = GetRawQuestFields(id.Value);
				_currentObjective = ArgUtility.Get(fields, 3, _currentObjective, false);
				reloadObjective();
				if (_currentObjective == null)
				{
					_currentObjective = "";
				}
				return _currentObjective;
			}
			set
			{
				_currentObjective = value;
			}
		}

		public Quest()
		{
			NetFields = new NetFields(NetFields.GetNameForInstance(this));
			initNetFields();
		}

		protected virtual void initNetFields()
		{
			NetFields.SetOwner(this).AddField(rewardDescription, "rewardDescription").AddField(completionString, "completionString")
				.AddField(accepted, "accepted")
				.AddField(completed, "completed")
				.AddField(dailyQuest, "dailyQuest")
				.AddField(showNew, "showNew")
				.AddField(canBeCancelled, "canBeCancelled")
				.AddField(destroy, "destroy")
				.AddField(id, "id")
				.AddField(moneyReward, "moneyReward")
				.AddField(questType, "questType")
				.AddField(daysLeft, "daysLeft")
				.AddField(nextQuests, "nextQuests")
				.AddField(dayQuestAccepted, "dayQuestAccepted")
				.AddField(modData, "modData");
		}

		public static string[] GetRawQuestFields(string id)
		{
			if (id == null)
			{
				return null;
			}
			Dictionary<string, string> questData = DataLoader.Quests(Game1.content);
			string rawData;
			if (questData == null || !questData.TryGetValue(id, out rawData))
			{
				return null;
			}
			return rawData.Split('/');
		}

		public static Quest getQuestFromId(string id)
		{
			string[] fields = GetRawQuestFields(id);
			if (fields == null)
			{
				return null;
			}
			string questType;
			string error;
			string title;
			string description;
			string objective;
			string rawNextQuests;
			int moneyReward;
			string rewardDescription;
			bool canBeCancelled;
			if (!ArgUtility.TryGet(fields, 0, out questType, out error, false) || !ArgUtility.TryGet(fields, 1, out title, out error, false) || !ArgUtility.TryGet(fields, 2, out description, out error, false) || !ArgUtility.TryGetOptional(fields, 3, out objective, out error, null, false) || !ArgUtility.TryGetOptional(fields, 5, out rawNextQuests, out error, null, false) || !ArgUtility.TryGetInt(fields, 6, out moneyReward, out error) || !ArgUtility.TryGetOptional(fields, 7, out rewardDescription, out error, null, false) || !ArgUtility.TryGetOptionalBool(fields, 8, out canBeCancelled, out error))
			{
				return LogParseError(id, error);
			}
			string[] nextQuests = ArgUtility.SplitBySpace(rawNextQuests);
			Quest q;
			switch (questType)
			{
			case "Crafting":
			{
				string[] conditions;
				if (!TryParseConditions(fields, out conditions, out error))
				{
					return LogParseError(id, error);
				}
				string itemId;
				if (!ArgUtility.TryGet(conditions, 0, out itemId, out error, false))
				{
					return LogConditionsParseError(id, error);
				}
				bool? isBigCraftable = null;
				if (ArgUtility.HasIndex(conditions, 1))
				{
					bool isBigCraftableValue;
					if (!ArgUtility.TryGetOptionalBool(conditions, 1, out isBigCraftableValue, out error))
					{
						return LogConditionsParseError(id, error);
					}
					isBigCraftable = isBigCraftableValue;
				}
				if (!ItemRegistry.IsQualifiedItemId(itemId))
				{
					itemId = ((!isBigCraftable.HasValue) ? (ItemRegistry.QualifyItemId(itemId) ?? itemId) : (isBigCraftable.Value ? ("(BC)" + itemId) : ("(O)" + itemId)));
				}
				q = new CraftingQuest(itemId);
				q.questType.Value = 2;
				break;
			}
			case "Location":
			{
				string[] conditions2;
				if (!TryParseConditions(fields, out conditions2, out error))
				{
					return LogParseError(id, error);
				}
				string locationName;
				if (!ArgUtility.TryGet(conditions2, 0, out locationName, out error, false))
				{
					return LogConditionsParseError(id, error);
				}
				q = new GoSomewhereQuest(locationName);
				q.questType.Value = 6;
				break;
			}
			case "Building":
			{
				string[] conditions3;
				if (!TryParseConditions(fields, out conditions3, out error))
				{
					return LogParseError(id, error);
				}
				string completionString;
				if (!ArgUtility.TryGet(conditions3, 0, out completionString, out error, false))
				{
					return LogConditionsParseError(id, error);
				}
				q = new Quest();
				q.questType.Value = 8;
				q.completionString.Value = completionString;
				break;
			}
			case "ItemDelivery":
			{
				string[] conditions4;
				string targetMessage;
				if (!TryParseConditions(fields, out conditions4, out error) || !ArgUtility.TryGet(fields, 9, out targetMessage, out error, false))
				{
					return LogParseError(id, error);
				}
				string npcName;
				string itemId2;
				int numberRequired;
				if (!ArgUtility.TryGet(conditions4, 0, out npcName, out error, false) || !ArgUtility.TryGet(conditions4, 1, out itemId2, out error, false) || !ArgUtility.TryGetOptionalInt(conditions4, 2, out numberRequired, out error, 1))
				{
					return LogConditionsParseError(id, error);
				}
				ItemDeliveryQuest itemDeliveryQuest = new ItemDeliveryQuest(npcName, itemId2);
				itemDeliveryQuest.targetMessage = targetMessage;
				itemDeliveryQuest.number.Value = numberRequired;
				itemDeliveryQuest.questType.Value = 3;
				q = itemDeliveryQuest;
				break;
			}
			case "Monster":
			{
				string[] conditions5;
				if (!TryParseConditions(fields, out conditions5, out error))
				{
					return LogParseError(id, error);
				}
				string monsterName;
				int numberToKill;
				string targetNpc;
				if (!ArgUtility.TryGet(conditions5, 0, out monsterName, out error, false) || !ArgUtility.TryGetInt(conditions5, 1, out numberToKill, out error) || !ArgUtility.TryGetOptional(conditions5, 2, out targetNpc, out error))
				{
					return LogConditionsParseError(id, error);
				}
				SlayMonsterQuest slayQuest = new SlayMonsterQuest();
				slayQuest.loadQuestInfo();
				slayQuest.monster.Value.Name = monsterName.Replace('_', ' ');
				slayQuest.monsterName.Value = slayQuest.monster.Value.Name;
				slayQuest.numberToKill.Value = numberToKill;
				slayQuest.target.Value = targetNpc ?? "null";
				slayQuest.questType.Value = 4;
				q = slayQuest;
				break;
			}
			case "Basic":
				q = new Quest();
				q.questType.Value = 1;
				break;
			case "Social":
			{
				SocializeQuest socializeQuest = new SocializeQuest();
				socializeQuest.loadQuestInfo();
				q = socializeQuest;
				break;
			}
			case "ItemHarvest":
			{
				string[] conditions6;
				if (!TryParseConditions(fields, out conditions6, out error))
				{
					return LogParseError(id, error);
				}
				string itemId3;
				int numberRequired2;
				if (!ArgUtility.TryGet(conditions6, 0, out itemId3, out error, false) || !ArgUtility.TryGetOptionalInt(conditions6, 1, out numberRequired2, out error, 1))
				{
					return LogConditionsParseError(id, error);
				}
				q = new ItemHarvestQuest(itemId3, numberRequired2);
				break;
			}
			case "LostItem":
			{
				string[] conditions7;
				if (!TryParseConditions(fields, out conditions7, out error))
				{
					return LogParseError(id, error);
				}
				string npcName2;
				string itemId4;
				string locationOfItem;
				int tileX;
				int tileY;
				if (!ArgUtility.TryGet(conditions7, 0, out npcName2, out error, false) || !ArgUtility.TryGet(conditions7, 1, out itemId4, out error, false) || !ArgUtility.TryGet(conditions7, 2, out locationOfItem, out error, false) || !ArgUtility.TryGetInt(conditions7, 3, out tileX, out error) || !ArgUtility.TryGetInt(conditions7, 4, out tileY, out error))
				{
					return LogConditionsParseError(id, error);
				}
				q = new LostItemQuest(npcName2, locationOfItem, itemId4, tileX, tileY);
				break;
			}
			case "SecretLostItem":
			{
				string[] conditions8;
				if (!TryParseConditions(fields, out conditions8, out error))
				{
					return LogParseError(id, error);
				}
				string npcName3;
				string itemId5;
				int friendshipReward;
				string exclusiveQuestId;
				if (!ArgUtility.TryGet(conditions8, 0, out npcName3, out error, false) || !ArgUtility.TryGet(conditions8, 1, out itemId5, out error, false) || !ArgUtility.TryGetInt(conditions8, 2, out friendshipReward, out error) || !ArgUtility.TryGetOptional(conditions8, 3, out exclusiveQuestId, out error, null, false))
				{
					return LogConditionsParseError(id, error);
				}
				q = new SecretLostItemQuest(npcName3, itemId5, friendshipReward, exclusiveQuestId);
				break;
			}
			default:
				return LogParseError(id, "quest type '" + questType + "' doesn't match a known type.");
			}
			q.id.Value = id;
			q.questTitle = title;
			q.questDescription = description;
			q.currentObjective = objective;
			string[] array = nextQuests;
			for (int i = 0; i < array.Length; i++)
			{
				string nextQuest = array[i];
				if (nextQuest.StartsWith('h'))
				{
					if (!Game1.IsMasterGame)
					{
						continue;
					}
					nextQuest = nextQuest.Substring(1);
				}
				q.nextQuests.Add(nextQuest);
			}
			q.showNew.Value = true;
			q.moneyReward.Value = moneyReward;
			q.rewardDescription.Value = ((moneyReward == -1) ? null : rewardDescription);
			q.canBeCancelled.Value = canBeCancelled;
			return q;
		}

		public virtual void reloadObjective()
		{
		}

		public virtual void reloadDescription()
		{
		}

		public virtual void adjustGameLocation(GameLocation location)
		{
		}

		public virtual void accept()
		{
			accepted.Value = true;
		}

		public virtual bool checkIfComplete(NPC n = null, int number1 = -1, int number2 = -2, Item item = null, string str = null, bool probe = false)
		{
			if (completionString.Value != null && str != null && str.Equals(completionString.Value))
			{
				if (!probe)
				{
					questComplete();
				}
				return true;
			}
			return false;
		}

		public bool hasReward()
		{
			if ((int)moneyReward <= 0)
			{
				string value = rewardDescription.Value;
				if (value == null)
				{
					return false;
				}
				return value.Length > 2;
			}
			return true;
		}

		public virtual bool isSecretQuest()
		{
			return false;
		}

		public virtual void questComplete()
		{
			if ((bool)completed)
			{
				return;
			}
			if ((bool)dailyQuest)
			{
				Game1.stats.Increment("BillboardQuestsDone");
				if (!Game1.player.mailReceived.Contains("completedFirstBillboardQuest"))
				{
					Game1.player.mailReceived.Add("completedFirstBillboardQuest");
				}
				if (Game1.stats.Get("BillboardQuestsDone") % 3u == 0)
				{
					if (!Game1.player.addItemToInventoryBool(ItemRegistry.Create("(O)PrizeTicket")))
					{
						Game1.createItemDebris(ItemRegistry.Create("(O)PrizeTicket"), Game1.player.getStandingPosition(), 2);
					}
					if (Game1.stats.Get("BillboardQuestsDone") >= 6 && !Game1.player.mailReceived.Contains("gotFirstBillboardPrizeTicket"))
					{
						Game1.player.mailReceived.Add("gotFirstBillboardPrizeTicket");
					}
				}
			}
			if ((bool)dailyQuest || (int)questType == 7)
			{
				Game1.stats.QuestsCompleted++;
			}
			completed.Value = true;
			Game1.player.currentLocation?.customQuestCompleteBehavior(id);
			if (nextQuests.Count > 0)
			{
				foreach (string i in nextQuests)
				{
					if (IsValidId(i))
					{
						Game1.player.addQuest(i);
					}
				}
				Game1.addHUDMessage(new HUDMessage(Game1.content.LoadString("Strings\\StringsFromCSFiles:Quest.cs.13636"), 2));
			}
			if ((int)moneyReward <= 0 && (rewardDescription.Value == null || rewardDescription.Value.Length <= 2))
			{
				Game1.player.questLog.Remove(this);
			}
			else
			{
				Game1.addHUDMessage(new HUDMessage(Game1.content.LoadString("Strings\\StringsFromCSFiles:Quest.cs.13636"), 2));
			}
			Game1.playSound("questcomplete");
			if (id.Value == "126")
			{
				Game1.player.mailReceived.Add("emilyFiber");
				Game1.player.activeDialogueEvents.Add("emilyFiber", 2);
			}
			Game1.dayTimeMoneyBox.questsDirty = true;
			Game1.player.autoGenerateActiveDialogueEvent("questComplete_" + id);
		}

		public string GetName()
		{
			return questTitle;
		}

		public string GetDescription()
		{
			return questDescription;
		}

		public bool IsHidden()
		{
			return isSecretQuest();
		}

		public List<string> GetObjectiveDescriptions()
		{
			return new List<string> { currentObjective };
		}

		public bool CanBeCancelled()
		{
			return canBeCancelled.Value;
		}

		public bool HasReward()
		{
			if (!HasMoneyReward())
			{
				string value = rewardDescription.Value;
				if (value == null)
				{
					return false;
				}
				return value.Length > 2;
			}
			return true;
		}

		public bool HasMoneyReward()
		{
			if (completed.Value)
			{
				return moneyReward.Value > 0;
			}
			return false;
		}

		public void MarkAsViewed()
		{
			showNew.Value = false;
		}

		public bool ShouldDisplayAsNew()
		{
			return showNew.Value;
		}

		public bool ShouldDisplayAsComplete()
		{
			if (completed.Value)
			{
				return !IsHidden();
			}
			return false;
		}

		public bool IsTimedQuest()
		{
			if (!dailyQuest.Value)
			{
				return GetDaysLeft() > 0;
			}
			return true;
		}

		public int GetDaysLeft()
		{
			return daysLeft;
		}

		public int GetMoneyReward()
		{
			return moneyReward.Value;
		}

		public void OnMoneyRewardClaimed()
		{
			moneyReward.Value = 0;
			destroy.Value = true;
		}

		public bool OnLeaveQuestPage()
		{
			if ((bool)completed && (int)moneyReward <= 0)
			{
				destroy.Value = true;
			}
			if (destroy.Value)
			{
				Game1.player.questLog.Remove(this);
				return true;
			}
			return false;
		}

		/// <summary>Get whether the <see cref="F:StardewValley.Quests.Quest.id" /> is set to a valid value.</summary>
		protected bool HasId()
		{
			return IsValidId(id.Value);
		}

		/// <summary>Get whether the given quest ID is valid.</summary>
		/// <param name="id">The quest ID to check.</param>
		protected bool IsValidId(string id)
		{
			switch (id)
			{
			case "7":
				return Game1.whichModFarm?.Id != "MeadowlandsFarm";
			case null:
			case "-1":
			case "0":
				return false;
			default:
				return true;
			}
		}

		/// <summary>Get the split quest conditions from raw quest fields, if it's found and valid.</summary>
		/// <param name="questFields">The raw quest fields.</param>
		/// <param name="conditions">The parsed conditions.</param>
		/// <param name="error">The error message indicating why parsing failed.</param>
		/// <param name="allowBlank">Whether to match the argument even if it's null or whitespace. If false, it will be treated as invalid in that case.</param>
		/// <returns>Returns whether the conditions field was found and valid.</returns>
		protected static bool TryParseConditions(string[] questFields, out string[] conditions, out string error, bool allowBlank = false)
		{
			string rawConditions;
			if (!ArgUtility.TryGet(questFields, 4, out rawConditions, out error, allowBlank))
			{
				conditions = null;
				return false;
			}
			conditions = ArgUtility.SplitBySpace(rawConditions);
			error = null;
			return true;
		}

		/// <summary>Log an error message indicating that the quest data couldn't be parsed.</summary>
		/// <param name="id">The quest ID being parsed.</param>
		/// <param name="error">The error message indicating why parsing failed.</param>
		/// <returns>Returns a null quest for convenience.</returns>
		protected static Quest LogParseError(string id, string error)
		{
			Game1.log.Error("Failed to parse data for quest '" + id + "': " + error);
			return null;
		}

		/// <summary>Log an error message indicating that the conditions field in the quest data couldn't be parsed.</summary>
		/// <param name="id">The quest ID being parsed.</param>
		/// <param name="error">The error message indicating why parsing failed.</param>
		/// <returns>Returns a null quest for convenience.</returns>
		protected static Quest LogConditionsParseError(string id, string error)
		{
			Game1.log.Error("Failed to parse for quest '" + id + "': conditions field (index 4) is invalid: " + error);
			return null;
		}
	}
}
