using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewValley.Characters;
using StardewValley.Extensions;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using xTile;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace StardewValley.Locations
{
	public class CommunityCenter : GameLocation
	{
		public const int AREA_Pantry = 0;

		public const int AREA_FishTank = 2;

		public const int AREA_CraftsRoom = 1;

		public const int AREA_BoilerRoom = 3;

		public const int AREA_Vault = 4;

		public const int AREA_Bulletin = 5;

		public const int AREA_AbandonedJojaMart = 6;

		public const int AREA_Bulletin2 = 7;

		public const int AREA_JunimoHut = 8;

		[XmlElement("warehouse")]
		private readonly NetBool warehouse = new NetBool();

		[XmlIgnore]
		public List<NetMutex> bundleMutexes = new List<NetMutex>();

		public readonly NetArray<bool, NetBool> areasComplete = new NetArray<bool, NetBool>(6);

		[XmlElement("numberOfStarsOnPlaque")]
		public readonly NetInt numberOfStarsOnPlaque = new NetInt();

		[XmlIgnore]
		private readonly NetEvent0 newJunimoNoteCheckEvent = new NetEvent0();

		[XmlIgnore]
		private readonly NetEvent1Field<int, NetInt> restoreAreaCutsceneEvent = new NetEvent1Field<int, NetInt>();

		[XmlIgnore]
		private readonly NetEvent1Field<int, NetInt> areaCompleteRewardEvent = new NetEvent1Field<int, NetInt>();

		private float messageAlpha;

		private List<int> junimoNotesViewportTargets;

		private Dictionary<int, List<int>> areaToBundleDictionary;

		private Dictionary<int, int> bundleToAreaDictionary;

		private Dictionary<string, List<List<int>>> bundlesIngredientsInfo;

		private bool _isWatchingJunimoGoodbye;

		private Vector2 missedRewardsChestTile = new Vector2(22f, 10f);

		private int missedRewardsTileSheet = 1;

		[XmlIgnore]
		public readonly NetRef<Chest> missedRewardsChest = new NetRef<Chest>(new Chest(true));

		[XmlIgnore]
		public readonly NetBool missedRewardsChestVisible = new NetBool(false);

		[XmlIgnore]
		public readonly NetEvent1Field<bool, NetBool> showMissedRewardsChestEvent = new NetEvent1Field<bool, NetBool>();

		public const int PHASE_firstPause = 0;

		public const int PHASE_junimoAppear = 1;

		public const int PHASE_junimoDance = 2;

		public const int PHASE_restore = 3;

		private int restoreAreaTimer;

		private int restoreAreaPhase;

		private int restoreAreaIndex;

		private ICue buildUpSound;

		[XmlElement("bundles")]
		public NetBundles bundles => Game1.netWorldState.Value.Bundles;

		[XmlElement("bundleRewards")]
		public NetIntDictionary<bool, NetBool> bundleRewards => Game1.netWorldState.Value.BundleRewards;

		public CommunityCenter()
		{
			initAreaBundleConversions();
			refreshBundlesIngredientsInfo();
		}

		public CommunityCenter(string map_path, string name)
			: base(map_path, name)
		{
			initAreaBundleConversions();
			refreshBundlesIngredientsInfo();
		}

		public CommunityCenter(string name)
			: base("Maps\\CommunityCenter_Ruins", name)
		{
			initAreaBundleConversions();
			refreshBundlesIngredientsInfo();
		}

		public void refreshBundlesIngredientsInfo()
		{
			bundlesIngredientsInfo = new Dictionary<string, List<List<int>>>();
			Dictionary<string, string> bundleData = Game1.netWorldState.Value.BundleData;
			Dictionary<int, bool[]> bundlesD = bundlesDict();
			foreach (KeyValuePair<string, string> v in bundleData)
			{
				string[] array = v.Key.Split('/');
				int bundleIndex = Convert.ToInt32(array[1]);
				string areaName = array[0];
				string[] ingredientSplit = ArgUtility.SplitBySpace(v.Value.Split('/')[2]);
				if (!shouldNoteAppearInArea(getAreaNumberFromName(areaName)))
				{
					continue;
				}
				for (int i = 0; i < ingredientSplit.Length; i += 3)
				{
					if (bundlesD.ContainsKey(bundleIndex) && !bundlesD[bundleIndex][i / 3])
					{
						int categoryOrId;
						string key;
						if (int.TryParse(ingredientSplit[i], out categoryOrId) && categoryOrId < 0)
						{
							key = categoryOrId.ToString();
						}
						else
						{
							ParsedItemData data = ItemRegistry.GetData(ingredientSplit[i]);
							key = ((data != null) ? data.QualifiedItemId : ("(O)" + ingredientSplit[i]));
						}
						int itemStack = Convert.ToInt32(ingredientSplit[i + 1]);
						int itemQuality = Convert.ToInt32(ingredientSplit[i + 2]);
						List<List<int>> ingredients;
						if (!bundlesIngredientsInfo.TryGetValue(key, out ingredients))
						{
							ingredients = (bundlesIngredientsInfo[key] = new List<List<int>>());
						}
						ingredients.Add(new List<int> { bundleIndex, itemStack, itemQuality });
					}
				}
			}
		}

		protected override void initNetFields()
		{
			base.initNetFields();
			base.NetFields.AddField(warehouse, "warehouse").AddField(areasComplete, "areasComplete").AddField(numberOfStarsOnPlaque, "numberOfStarsOnPlaque")
				.AddField(newJunimoNoteCheckEvent, "newJunimoNoteCheckEvent")
				.AddField(restoreAreaCutsceneEvent, "restoreAreaCutsceneEvent")
				.AddField(areaCompleteRewardEvent, "areaCompleteRewardEvent")
				.AddField(missedRewardsChest, "missedRewardsChest")
				.AddField(showMissedRewardsChestEvent, "showMissedRewardsChestEvent")
				.AddField(missedRewardsChestVisible, "missedRewardsChestVisible");
			newJunimoNoteCheckEvent.onEvent += doCheckForNewJunimoNotes;
			restoreAreaCutsceneEvent.onEvent += doRestoreAreaCutscene;
			areaCompleteRewardEvent.onEvent += doAreaCompleteReward;
			showMissedRewardsChestEvent.onEvent += doShowMissedRewardsChest;
		}

		private void initAreaBundleConversions()
		{
			areaToBundleDictionary = new Dictionary<int, List<int>>();
			bundleToAreaDictionary = new Dictionary<int, int>();
			for (int i = 0; i < 7; i++)
			{
				areaToBundleDictionary.Add(i, new List<int>());
				NetMutex j = new NetMutex();
				bundleMutexes.Add(j);
				base.NetFields.AddField(j.NetFields, "m.NetFields");
			}
			foreach (KeyValuePair<string, string> v in Game1.netWorldState.Value.BundleData)
			{
				int bundleIndex = Convert.ToInt32(v.Key.Split('/')[1]);
				areaToBundleDictionary[getAreaNumberFromName(v.Key.Split('/')[0])].Add(bundleIndex);
				bundleToAreaDictionary.Add(bundleIndex, getAreaNumberFromName(v.Key.Split('/')[0]));
			}
		}

		public static int getAreaNumberFromName(string name)
		{
			switch (name)
			{
			case "Pantry":
				return 0;
			case "Crafts Room":
			case "CraftsRoom":
				return 1;
			case "Fish Tank":
			case "FishTank":
				return 2;
			case "Boiler Room":
			case "BoilerRoom":
				return 3;
			case "Vault":
				return 4;
			case "BulletinBoard":
			case "Bulletin Board":
			case "Bulletin":
				return 5;
			case "Abandoned Joja Mart":
				return 6;
			default:
				return -1;
			}
		}

		private Point getNotePosition(int area)
		{
			switch (area)
			{
			case 0:
				return new Point(14, 5);
			case 2:
				return new Point(40, 10);
			case 1:
				return new Point(14, 23);
			case 3:
				return new Point(63, 14);
			case 4:
				return new Point(55, 6);
			case 5:
				return new Point(46, 11);
			default:
				return Point.Zero;
			}
		}

		public void addJunimoNote(int area)
		{
			Point position = getNotePosition(area);
			if (!position.Equals(Vector2.Zero))
			{
				StaticTile[] tileFrames = getJunimoNoteTileFrames(area, map);
				string layer = ((area == 5) ? "Front" : "Buildings");
				map.RequireLayer(layer).Tiles[position.X, position.Y] = new AnimatedTile(map.RequireLayer(layer), tileFrames, 70L);
				Game1.currentLightSources.Add(new LightSource(4, new Vector2(position.X * 64, position.Y * 64), 1f, LightSource.LightContext.None, 0L));
				temporarySprites.Add(new TemporaryAnimatedSprite(6, new Vector2(position.X * 64, position.Y * 64), Color.White)
				{
					layerDepth = 1f,
					interval = 50f,
					motion = new Vector2(1f, 0f),
					acceleration = new Vector2(-0.005f, 0f)
				});
				temporarySprites.Add(new TemporaryAnimatedSprite(6, new Vector2(position.X * 64 - 12, position.Y * 64 - 12), Color.White)
				{
					scale = 0.75f,
					layerDepth = 1f,
					interval = 50f,
					motion = new Vector2(1f, 0f),
					acceleration = new Vector2(-0.005f, 0f),
					delayBeforeAnimationStart = 50
				});
				temporarySprites.Add(new TemporaryAnimatedSprite(6, new Vector2(position.X * 64 - 12, position.Y * 64 + 12), Color.White)
				{
					layerDepth = 1f,
					interval = 50f,
					motion = new Vector2(1f, 0f),
					acceleration = new Vector2(-0.005f, 0f),
					delayBeforeAnimationStart = 100
				});
				temporarySprites.Add(new TemporaryAnimatedSprite(6, new Vector2(position.X * 64, position.Y * 64), Color.White)
				{
					layerDepth = 1f,
					scale = 0.75f,
					interval = 50f,
					motion = new Vector2(1f, 0f),
					acceleration = new Vector2(-0.005f, 0f),
					delayBeforeAnimationStart = 150
				});
			}
		}

		public int numberOfCompleteBundles()
		{
			int number = 0;
			foreach (KeyValuePair<int, bool[]> v in bundles.Pairs)
			{
				number++;
				for (int i = 0; i < v.Value.Length; i++)
				{
					if (!v.Value[i])
					{
						number--;
						break;
					}
				}
			}
			return number;
		}

		public void addStarToPlaque()
		{
			numberOfStarsOnPlaque.Value++;
		}

		private string getMessageForAreaCompletion()
		{
			int areasComplete = getNumberOfAreasComplete();
			if (areasComplete >= 1 && areasComplete <= 6)
			{
				return Game1.content.LoadString("Strings\\Locations:CommunityCenter_AreaCompletion" + areasComplete, Game1.player.Name);
			}
			return "";
		}

		private int getNumberOfAreasComplete()
		{
			int complete = 0;
			for (int i = 0; i < areasComplete.Count; i++)
			{
				if (areasComplete[i])
				{
					complete++;
				}
			}
			return complete;
		}

		public Dictionary<int, bool[]> bundlesDict()
		{
			return bundles.Pairs.Select((KeyValuePair<int, bool[]> kvp) => new KeyValuePair<int, bool[]>(kvp.Key, kvp.Value.ToArray())).ToDictionary((KeyValuePair<int, bool[]> x) => x.Key, (KeyValuePair<int, bool[]> y) => y.Value);
		}

		/// <inheritdoc />
		public override bool performAction(string[] action, Farmer who, Location tileLocation)
		{
			if (who.IsLocalPlayer && ArgUtility.Get(action, 0) == "MissedRewards")
			{
				missedRewardsChest.Value.mutex.RequestLock(delegate
				{
					Game1.activeClickableMenu = new ItemGrabMenu(missedRewardsChest.Value.Items, false, true, null, null, null, rewardGrabbed, false, true, true, true, false, 0, null, -1, this);
					Game1.activeClickableMenu.exitFunction = delegate
					{
						missedRewardsChest.Value.mutex.ReleaseLock();
						checkForMissedRewards();
					};
				});
				return true;
			}
			return base.performAction(action, who, tileLocation);
		}

		private void rewardGrabbed(Item item, Farmer who)
		{
			bundleRewards[item.SpecialVariable] = false;
		}

		public override bool checkAction(Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who)
		{
			switch (getTileIndexAt(tileLocation, "Buildings"))
			{
			case 1799:
				if (numberOfCompleteBundles() > 2)
				{
					checkBundle(5);
				}
				return true;
			case 1824:
			case 1825:
			case 1826:
			case 1827:
			case 1828:
			case 1829:
			case 1830:
			case 1831:
			case 1832:
			case 1833:
				checkBundle(getAreaNumberFromLocation(who.Tile));
				return true;
			default:
				return base.checkAction(tileLocation, viewport, who);
			}
		}

		public void checkBundle(int area)
		{
			bundleMutexes[area].RequestLock(delegate
			{
				Game1.activeClickableMenu = new JunimoNoteMenu(area, bundlesDict());
			});
		}

		public void addJunimoNoteViewportTarget(int area)
		{
			if (junimoNotesViewportTargets == null)
			{
				junimoNotesViewportTargets = new List<int>();
			}
			if (!junimoNotesViewportTargets.Contains(area))
			{
				junimoNotesViewportTargets.Add(area);
			}
		}

		public void checkForNewJunimoNotes()
		{
			newJunimoNoteCheckEvent.Fire();
		}

		private void doCheckForNewJunimoNotes()
		{
			if (Game1.currentLocation != this)
			{
				return;
			}
			for (int i = 0; i < areasComplete.Count; i++)
			{
				if (!isJunimoNoteAtArea(i) && shouldNoteAppearInArea(i))
				{
					addJunimoNoteViewportTarget(i);
				}
			}
		}

		public bool isJunimoNoteAtArea(int area)
		{
			Point p = getNotePosition(area);
			if (area == 5)
			{
				return map.RequireLayer("Front").Tiles[p.X, p.Y] != null;
			}
			return map.RequireLayer("Buildings").Tiles[p.X, p.Y] != null;
		}

		public bool shouldNoteAppearInArea(int area)
		{
			bool isAreaComplete = true;
			for (int i = 0; i < areaToBundleDictionary[area].Count; i++)
			{
				foreach (int bundleIndex in areaToBundleDictionary[area])
				{
					bool[] bundleEntries;
					if (bundles.TryGetValue(bundleIndex, out bundleEntries))
					{
						int bundleLength = bundleEntries.Length / 3;
						for (int j = 0; j < bundleLength; j++)
						{
							if (!bundleEntries[j])
							{
								isAreaComplete = false;
								break;
							}
						}
					}
					if (!isAreaComplete)
					{
						break;
					}
				}
			}
			if (area >= 0 && !isAreaComplete)
			{
				switch (area)
				{
				case 1:
					return true;
				case 0:
				case 2:
					if (numberOfCompleteBundles() > 0)
					{
						return true;
					}
					break;
				case 3:
					if (numberOfCompleteBundles() > 1)
					{
						return true;
					}
					break;
				case 5:
					if (numberOfCompleteBundles() > 2)
					{
						return true;
					}
					break;
				case 4:
					if (numberOfCompleteBundles() > 3)
					{
						return true;
					}
					break;
				case 6:
					if (Utility.HasAnyPlayerSeenEvent("191393"))
					{
						return true;
					}
					break;
				}
			}
			return false;
		}

		public override void updateMap()
		{
			if (Game1.MasterPlayer.mailReceived.Contains("JojaMember"))
			{
				warehouse.Value = true;
				mapPath.Value = "Maps\\CommunityCenter_Joja";
			}
			base.updateMap();
		}

		public override void TransferDataFromSavedLocation(GameLocation l)
		{
			if (areAllAreasComplete())
			{
				mapPath.Value = "Maps\\CommunityCenter_Refurbished";
				updateMap();
			}
			base.TransferDataFromSavedLocation(l);
		}

		protected override void resetSharedState()
		{
			base.resetSharedState();
			if (areAllAreasComplete())
			{
				mapPath.Value = "Maps\\CommunityCenter_Refurbished";
				addFishTank();
			}
			_isWatchingJunimoGoodbye = false;
			if (!Game1.MasterPlayer.mailReceived.Contains("JojaMember") && !areAllAreasComplete())
			{
				for (int i = 0; i < areasComplete.Count; i++)
				{
					if (shouldNoteAppearInArea(i))
					{
						characters.Add(new Junimo(new Vector2(getNotePosition(i).X, getNotePosition(i).Y + 2) * 64f, i));
					}
				}
			}
			numberOfStarsOnPlaque.Value = 0;
			for (int j = 0; j < areasComplete.Count; j++)
			{
				if (areasComplete[j])
				{
					numberOfStarsOnPlaque.Value++;
				}
			}
			checkForMissedRewards();
		}

		private void doShowMissedRewardsChest(bool isVisible)
		{
			int tileX = (int)missedRewardsChestTile.X;
			int tileY = (int)missedRewardsChestTile.Y;
			if (isVisible)
			{
				setMapTileIndex(tileX, tileY, -1, "Buildings");
				setMapTileIndex(tileX, tileY, 5, "Buildings", missedRewardsTileSheet);
				setTileProperty(tileX, tileY, "Buildings", "Action", "MissedRewards");
			}
			else
			{
				setMapTileIndex(tileX, tileY, -1, "Buildings");
				removeTileProperty(tileX, tileY, "Buildings", "Action");
			}
		}

		private void checkForMissedRewards()
		{
			HashSet<int> visited_areas = new HashSet<int>();
			bool hasUnclaimedRewards = false;
			missedRewardsChest.Value.Items.Clear();
			List<Item> rewards = new List<Item>();
			foreach (int key in bundleRewards.Keys)
			{
				int area = bundleToAreaDictionary[key];
				if (!bundleRewards[key] || areasComplete.Count <= area || !areasComplete[area] || visited_areas.Contains(area))
				{
					continue;
				}
				visited_areas.Add(area);
				hasUnclaimedRewards = true;
				rewards.Clear();
				JunimoNoteMenu.GetBundleRewards(area, rewards);
				foreach (Item item in rewards)
				{
					missedRewardsChest.Value.addItem(item);
				}
			}
			if (hasUnclaimedRewards != missedRewardsChestVisible.Value)
			{
				showMissedRewardsChestEvent.Fire(hasUnclaimedRewards);
				Game1.multiplayer.broadcastSprites(this, new TemporaryAnimatedSprite(Game1.random.Choose(5, 46), missedRewardsChestTile * 64f + new Vector2(16f, 16f), Color.White)
				{
					layerDepth = 1f
				});
				missedRewardsChestVisible.Value = hasUnclaimedRewards;
			}
		}

		public override void MakeMapModifications(bool force = false)
		{
			base.MakeMapModifications(force);
			if (!Game1.MasterPlayer.mailReceived.Contains("JojaMember") && !areAllAreasComplete())
			{
				for (int i = 0; i < areasComplete.Count; i++)
				{
					if (areasComplete[i])
					{
						loadArea(i, false);
					}
					else if (shouldNoteAppearInArea(i))
					{
						addJunimoNote(i);
					}
				}
			}
			doShowMissedRewardsChest(missedRewardsChestVisible);
		}

		protected override void resetLocalState()
		{
			base.resetLocalState();
			if (!Game1.eventUp && !areAllAreasComplete())
			{
				Game1.changeMusicTrack("communityCenter");
			}
		}

		private int getAreaNumberFromLocation(Vector2 tileLocation)
		{
			for (int i = 0; i < areasComplete.Count; i++)
			{
				if (getAreaBounds(i).Contains((int)tileLocation.X, (int)tileLocation.Y))
				{
					return i;
				}
			}
			return -1;
		}

		private Microsoft.Xna.Framework.Rectangle getAreaBounds(int area)
		{
			switch (area)
			{
			case 1:
				return new Microsoft.Xna.Framework.Rectangle(0, 12, 21, 17);
			case 0:
				return new Microsoft.Xna.Framework.Rectangle(0, 0, 22, 11);
			case 2:
				return new Microsoft.Xna.Framework.Rectangle(35, 4, 9, 9);
			case 3:
				return new Microsoft.Xna.Framework.Rectangle(52, 9, 16, 12);
			case 5:
				return new Microsoft.Xna.Framework.Rectangle(22, 13, 28, 9);
			case 4:
				return new Microsoft.Xna.Framework.Rectangle(45, 0, 15, 9);
			case 7:
				return new Microsoft.Xna.Framework.Rectangle(44, 10, 6, 3);
			case 8:
				return new Microsoft.Xna.Framework.Rectangle(22, 4, 13, 9);
			default:
				return Microsoft.Xna.Framework.Rectangle.Empty;
			}
		}

		protected void removeJunimo()
		{
			for (int i = characters.Count - 1; i >= 0; i--)
			{
				if (characters[i] is Junimo)
				{
					characters.RemoveAt(i);
				}
			}
		}

		public override void cleanupBeforeSave()
		{
			removeJunimo();
		}

		public override void cleanupBeforePlayerExit()
		{
			base.cleanupBeforePlayerExit();
			if (farmers.Count <= 1)
			{
				removeJunimo();
			}
		}

		public bool isBundleComplete(int bundleIndex)
		{
			for (int i = 0; i < bundles[bundleIndex].Length; i++)
			{
				if (!bundles[bundleIndex][i])
				{
					return false;
				}
			}
			return true;
		}

		public bool couldThisIngredienteBeUsedInABundle(Object o)
		{
			if (!o.bigCraftable)
			{
				List<List<int>> ingredientsById;
				if (bundlesIngredientsInfo.TryGetValue(o.QualifiedItemId, out ingredientsById))
				{
					foreach (List<int> j in ingredientsById)
					{
						if (o.Quality >= j[2])
						{
							return true;
						}
					}
				}
				List<List<int>> ingredientsByCategory;
				if (o.Category < 0 && bundlesIngredientsInfo.TryGetValue(o.Category.ToString(), out ingredientsByCategory))
				{
					foreach (List<int> i in ingredientsByCategory)
					{
						if (o.Quality >= i[2])
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		public void areaCompleteReward(int whichArea)
		{
			areaCompleteRewardEvent.Fire(whichArea);
		}

		private void doAreaCompleteReward(int whichArea)
		{
			string mailReceivedID = "";
			switch (whichArea)
			{
			case 3:
				mailReceivedID = "ccBoilerRoom";
				break;
			case 0:
				mailReceivedID = "ccPantry";
				break;
			case 2:
				mailReceivedID = "ccFishTank";
				break;
			case 4:
				mailReceivedID = "ccVault";
				break;
			case 5:
				mailReceivedID = "ccBulletin";
				Game1.addMailForTomorrow("ccBulletinThankYou");
				break;
			case 1:
				mailReceivedID = "ccCraftsRoom";
				break;
			}
			if (mailReceivedID.Length > 0 && !Game1.player.mailReceived.Contains(mailReceivedID))
			{
				Game1.player.mailForTomorrow.Add(mailReceivedID + "%&NL&%");
			}
		}

		public void loadArea(int area, bool showEffects = true)
		{
			Microsoft.Xna.Framework.Rectangle areaBounds = getAreaBounds(area);
			Map obj = Game1.game1.xTileContent.Load<Map>("Maps\\CommunityCenter_Refurbished");
			Layer mapBackLayer = map.RequireLayer("Back");
			Layer mapBuildingsLayer = map.RequireLayer("Buildings");
			Layer mapFrontLayer = map.RequireLayer("Front");
			Layer refurbishedBackLayer = obj.RequireLayer("Back");
			Layer refurbishedBuildingsLayer = obj.RequireLayer("Buildings");
			Layer refurbishedFrontLayer = obj.RequireLayer("Front");
			Layer refurbishedPathsLayer = obj.RequireLayer("Paths");
			foreach (Point point in areaBounds.GetPoints())
			{
				int x = point.X;
				int y = point.Y;
				int tileIndex = refurbishedBackLayer.GetTileIndexAt(x, y);
				if (tileIndex != -1)
				{
					mapBackLayer.Tiles[x, y].TileIndex = tileIndex;
				}
				tileIndex = refurbishedBuildingsLayer.GetTileIndexAt(x, y);
				if (tileIndex != -1)
				{
					mapBuildingsLayer.Tiles[x, y] = new StaticTile(mapBuildingsLayer, map.TileSheets[0], BlendMode.Alpha, tileIndex);
					adjustMapLightPropertiesForLamp(tileIndex, x, y, "Buildings");
					if (Game1.player.currentLocation == this && Game1.player.TilePoint.X == x && Game1.player.TilePoint.Y == y)
					{
						Game1.player.Position = new Vector2(2080f, 576f);
					}
				}
				else
				{
					mapBuildingsLayer.Tiles[x, y] = null;
				}
				tileIndex = refurbishedFrontLayer.GetTileIndexAt(x, y);
				if (tileIndex != -1)
				{
					mapFrontLayer.Tiles[x, y] = new StaticTile(mapFrontLayer, map.TileSheets[0], BlendMode.Alpha, tileIndex);
					adjustMapLightPropertiesForLamp(tileIndex, x, y, "Front");
				}
				else
				{
					mapFrontLayer.Tiles[x, y] = null;
				}
				tileIndex = refurbishedPathsLayer.GetTileIndexAt(x, y);
				if (tileIndex == 8)
				{
					Game1.currentLightSources.Add(new LightSource(4, new Vector2(x * 64, y * 64), 2f, LightSource.LightContext.None, 0L));
				}
				if (showEffects && Game1.random.NextDouble() < 0.58 && refurbishedBuildingsLayer.Tiles[x, y] == null)
				{
					temporarySprites.Add(new TemporaryAnimatedSprite(6, new Vector2(x * 64, y * 64), Color.White)
					{
						layerDepth = 1f,
						interval = 50f,
						motion = new Vector2((float)Game1.random.Next(17) / 10f, 0f),
						acceleration = new Vector2(-0.005f, 0f),
						delayBeforeAnimationStart = Game1.random.Next(500)
					});
				}
			}
			if ((area == 5 || area == 8) && (bool)missedRewardsChestVisible)
			{
				doShowMissedRewardsChest(true);
			}
			if (area == 5)
			{
				loadArea(7);
			}
			if (area == 2)
			{
				addFishTank();
			}
			addLightGlows();
		}

		public void addFishTank()
		{
			bool found = false;
			foreach (Furniture f2 in furniture)
			{
				if (f2.QualifiedItemId == "(F)CCFishTank")
				{
					f2.AllowLocalRemoval = false;
					found = true;
					break;
				}
			}
			if (!found)
			{
				FishTankFurniture f = new FishTankFurniture("CCFishTank", new Vector2(38f, 9f));
				f.CanBeGrabbed = false;
				f.AllowLocalRemoval = false;
				f.Fragility = 2;
				f.heldItems.Add(ItemRegistry.Create("(O)143"));
				f.heldItems.Add(ItemRegistry.Create("(O)145"));
				f.heldItems.Add(ItemRegistry.Create("(O)721"));
				furniture.Add(f);
			}
		}

		public void restoreAreaCutscene(int whichArea)
		{
			restoreAreaCutsceneEvent.Fire(whichArea);
		}

		public void markAreaAsComplete(int area)
		{
			if (Game1.currentLocation == this)
			{
				areasComplete[area] = true;
			}
			if (areAllAreasComplete() && Game1.currentLocation == this)
			{
				_isWatchingJunimoGoodbye = true;
			}
		}

		private void doRestoreAreaCutscene(int whichArea)
		{
			markAreaAsComplete(whichArea);
			restoreAreaIndex = whichArea;
			restoreAreaPhase = 0;
			restoreAreaTimer = 1000;
			if (Game1.player.currentLocation == this)
			{
				Game1.freezeControls = true;
				Game1.changeMusicTrack("none");
			}
			checkForMissedRewards();
		}

		public override void updateEvenIfFarmerIsntHere(GameTime time, bool ignoreWasUpdatedFlush = false)
		{
			base.updateEvenIfFarmerIsntHere(time, ignoreWasUpdatedFlush);
			restoreAreaCutsceneEvent.Poll();
			newJunimoNoteCheckEvent.Poll();
			areaCompleteRewardEvent.Poll();
			showMissedRewardsChestEvent.Poll();
			foreach (NetMutex i in bundleMutexes)
			{
				i.Update(this);
				if (i.IsLockHeld() && Game1.activeClickableMenu == null)
				{
					i.ReleaseLock();
				}
			}
		}

		public override void UpdateWhenCurrentLocation(GameTime time)
		{
			base.UpdateWhenCurrentLocation(time);
			missedRewardsChest.Value.updateWhenCurrentLocation(time);
			if (restoreAreaTimer > 0)
			{
				int old = restoreAreaTimer;
				restoreAreaTimer -= time.ElapsedGameTime.Milliseconds;
				switch (restoreAreaPhase)
				{
				case 0:
					if (restoreAreaTimer <= 0)
					{
						restoreAreaTimer = 3000;
						restoreAreaPhase = 1;
						if (Game1.player.currentLocation == this)
						{
							Game1.player.faceDirection(2);
							Game1.player.jump();
							Game1.player.jitterStrength = 1f;
							Game1.player.showFrame(94);
						}
					}
					break;
				case 1:
					if (Game1.IsMasterGame && Game1.random.NextDouble() < 0.4)
					{
						Vector2 v = Utility.getRandomPositionInThisRectangle(getAreaBounds(restoreAreaIndex), Game1.random);
						Junimo j = new Junimo(v * 64f, restoreAreaIndex, true);
						if (!isCollidingPosition(j.GetBoundingBox(), Game1.viewport, j))
						{
							characters.Add(j);
							Game1.multiplayer.broadcastSprites(this, new TemporaryAnimatedSprite(Game1.random.Choose(5, 46), v * 64f + new Vector2(16f, 16f), Color.White)
							{
								layerDepth = 1f
							});
							localSound("tinyWhip");
						}
					}
					if (restoreAreaTimer <= 0)
					{
						restoreAreaTimer = 999999;
						restoreAreaPhase = 2;
						if (Game1.player.currentLocation != this)
						{
							break;
						}
						Game1.screenGlowOnce(Color.White, true, 0.005f, 1f);
						Game1.playSound("wind", out buildUpSound);
						buildUpSound.SetVariable("Volume", 0f);
						buildUpSound.SetVariable("Frequency", 0f);
						Game1.player.jitterStrength = 2f;
						Game1.player.stopShowingFrame();
					}
					Game1.drawLighting = false;
					break;
				case 2:
					if (buildUpSound != null)
					{
						buildUpSound.SetVariable("Volume", Game1.screenGlowAlpha * 150f);
						buildUpSound.SetVariable("Frequency", Game1.screenGlowAlpha * 150f);
					}
					if (Game1.screenGlowAlpha >= Game1.screenGlowMax)
					{
						messageAlpha += 0.008f;
						messageAlpha = Math.Min(messageAlpha, 1f);
					}
					if ((Game1.screenGlowAlpha == Game1.screenGlowMax || Game1.currentLocation != this) && restoreAreaTimer > 5200)
					{
						restoreAreaTimer = 5200;
					}
					if (restoreAreaTimer < 5200 && Game1.random.NextDouble() < (double)((float)(5200 - restoreAreaTimer) / 10000f))
					{
						localSound(Game1.random.Choose("dustMeep", "junimoMeep1"));
					}
					if (restoreAreaTimer > 0)
					{
						break;
					}
					restoreAreaTimer = 2000;
					messageAlpha = 0f;
					restoreAreaPhase = 3;
					if (Game1.IsMasterGame)
					{
						for (int i = characters.Count - 1; i >= 0; i--)
						{
							Junimo junimo = characters[i] as Junimo;
							if (junimo != null && (bool)junimo.temporaryJunimo)
							{
								characters.RemoveAt(i);
							}
						}
					}
					if (Game1.player.currentLocation != this)
					{
						if (Game1.IsMasterGame)
						{
							loadArea(restoreAreaIndex);
							_mapSeatsDirty = true;
						}
						break;
					}
					Game1.screenGlowHold = false;
					loadArea(restoreAreaIndex);
					if (Game1.IsMasterGame)
					{
						_mapSeatsDirty = true;
					}
					buildUpSound?.Stop(AudioStopOptions.Immediate);
					localSound("wand");
					Game1.changeMusicTrack("junimoStarSong");
					localSound("woodyHit");
					Game1.flashAlpha = 1f;
					Game1.player.stopJittering();
					Game1.drawLighting = true;
					break;
				case 3:
					if (old > 1000 && restoreAreaTimer <= 1000)
					{
						Junimo k = getJunimoForArea(restoreAreaIndex);
						if (k != null && Game1.IsMasterGame)
						{
							if (!k.holdingBundle)
							{
								k.Position = Utility.getRandomAdjacentOpenTile(Utility.PointToVector2(getNotePosition(restoreAreaIndex)), this) * 64f;
								int iter = 0;
								while (isCollidingPosition(k.GetBoundingBox(), Game1.viewport, k) && iter < 20)
								{
									Microsoft.Xna.Framework.Rectangle area_bounds = getAreaBounds(restoreAreaIndex);
									if (restoreAreaIndex == 5)
									{
										area_bounds = new Microsoft.Xna.Framework.Rectangle(44, 13, 6, 2);
									}
									k.Position = Utility.getRandomPositionInThisRectangle(area_bounds, Game1.random) * 64f;
									iter++;
								}
								if (iter < 20)
								{
									k.fadeBack();
								}
							}
							k.returnToJunimoHutToFetchStar(this);
						}
					}
					if (restoreAreaTimer <= 0 && !_isWatchingJunimoGoodbye)
					{
						Game1.freezeControls = false;
					}
					break;
				}
			}
			else if (Game1.activeClickableMenu == null)
			{
				List<int> list = junimoNotesViewportTargets;
				if (list != null && list.Count > 0 && !Game1.isViewportOnCustomPath())
				{
					setViewportToNextJunimoNoteTarget();
				}
			}
		}

		private void setViewportToNextJunimoNoteTarget()
		{
			if (junimoNotesViewportTargets.Count > 0)
			{
				Game1.freezeControls = true;
				int area = junimoNotesViewportTargets[0];
				Point p = getNotePosition(area);
				Game1.moveViewportTo(new Vector2(p.X, p.Y) * 64f, 5f, 2000, afterViewportGetsToJunimoNotePosition, setViewportToNextJunimoNoteTarget);
			}
			else
			{
				Game1.viewportFreeze = true;
				Game1.viewportHold = 10000;
				Game1.globalFadeToBlack(Game1.afterFadeReturnViewportToPlayer);
				Game1.freezeControls = false;
				Game1.afterViewport = null;
			}
		}

		private void afterViewportGetsToJunimoNotePosition()
		{
			int area = junimoNotesViewportTargets[0];
			junimoNotesViewportTargets.RemoveAt(0);
			addJunimoNote(area);
			localSound("reward");
		}

		public Junimo getJunimoForArea(int whichArea)
		{
			foreach (NPC character in characters)
			{
				Junimo junimo = character as Junimo;
				if (junimo != null && (int)junimo.whichArea == whichArea)
				{
					return junimo;
				}
			}
			Junimo i = new Junimo(Vector2.Zero, whichArea);
			addCharacter(i);
			return i;
		}

		public bool areAllAreasComplete()
		{
			foreach (bool item in areasComplete)
			{
				if (!item)
				{
					return false;
				}
			}
			return true;
		}

		public void junimoGoodbyeDance()
		{
			getJunimoForArea(0).Position = new Vector2(23f, 11f) * 64f;
			getJunimoForArea(1).Position = new Vector2(27f, 11f) * 64f;
			getJunimoForArea(2).Position = new Vector2(24f, 12f) * 64f;
			getJunimoForArea(4).Position = new Vector2(26f, 12f) * 64f;
			getJunimoForArea(3).Position = new Vector2(28f, 12f) * 64f;
			getJunimoForArea(5).Position = new Vector2(25f, 11f) * 64f;
			for (int i = 0; i < areasComplete.Count; i++)
			{
				getJunimoForArea(i).stayStill();
				getJunimoForArea(i).faceDirection(1);
				getJunimoForArea(i).fadeBack();
				getJunimoForArea(i).IsInvisible = false;
				getJunimoForArea(i).setAlpha(1f);
			}
			Point playerPixel = Game1.player.StandingPixel;
			Game1.moveViewportTo(new Vector2(playerPixel.X, playerPixel.Y), 2f, 5000, startGoodbyeDance, endGoodbyeDance);
			Game1.viewportFreeze = false;
			Game1.freezeControls = true;
		}

		public void prepareForJunimoDance()
		{
			for (int j = 0; j < areasComplete.Count; j++)
			{
				Junimo junimoForArea = getJunimoForArea(j);
				junimoForArea.holdingBundle.Value = false;
				junimoForArea.holdingStar.Value = false;
				junimoForArea.controller = null;
				junimoForArea.Halt();
				junimoForArea.IsInvisible = true;
			}
			numberOfStarsOnPlaque.Value = 0;
			for (int i = 0; i < areasComplete.Count; i++)
			{
				if (areasComplete[i])
				{
					numberOfStarsOnPlaque.Value++;
				}
			}
		}

		private void startGoodbyeDance()
		{
			Game1.freezeControls = true;
			getJunimoForArea(0).Position = new Vector2(23f, 11f) * 64f;
			getJunimoForArea(1).Position = new Vector2(27f, 11f) * 64f;
			getJunimoForArea(2).Position = new Vector2(24f, 12f) * 64f;
			getJunimoForArea(4).Position = new Vector2(26f, 12f) * 64f;
			getJunimoForArea(3).Position = new Vector2(28f, 12f) * 64f;
			getJunimoForArea(5).Position = new Vector2(25f, 11f) * 64f;
			for (int i = 0; i < areasComplete.Count; i++)
			{
				getJunimoForArea(i).stayStill();
				getJunimoForArea(i).faceDirection(1);
				getJunimoForArea(i).fadeBack();
				getJunimoForArea(i).IsInvisible = false;
				getJunimoForArea(i).setAlpha(1f);
				getJunimoForArea(i).sayGoodbye();
			}
		}

		private void endGoodbyeDance()
		{
			for (int i = 0; i < areasComplete.Count; i++)
			{
				getJunimoForArea(i).fadeAway();
			}
			Game1.pauseThenDoFunction(3600, loadJunimoHut);
			Game1.freezeControls = true;
		}

		private void loadJunimoHut()
		{
			for (int i = 0; i < areasComplete.Count; i++)
			{
				getJunimoForArea(i).clearTextAboveHead();
			}
			loadArea(8);
			Game1.flashAlpha = 1f;
			localSound("wand");
			Game1.freezeControls = false;
			Game1.showGlobalMessage(Game1.content.LoadString("Strings\\Locations:CommunityCenter_JunimosReturned"));
		}

		public override void draw(SpriteBatch b)
		{
			base.draw(b);
			for (int i = 0; i < (int)numberOfStarsOnPlaque; i++)
			{
				switch (i)
				{
				case 0:
					b.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2(2136f, 324f)), new Microsoft.Xna.Framework.Rectangle(354, 401, 7, 7), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.8f);
					break;
				case 1:
					b.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2(2136f, 364f)), new Microsoft.Xna.Framework.Rectangle(354, 401, 7, 7), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.8f);
					break;
				case 2:
					b.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2(2096f, 384f)), new Microsoft.Xna.Framework.Rectangle(354, 401, 7, 7), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.8f);
					break;
				case 3:
					b.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2(2056f, 364f)), new Microsoft.Xna.Framework.Rectangle(354, 401, 7, 7), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.8f);
					break;
				case 4:
					b.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2(2056f, 324f)), new Microsoft.Xna.Framework.Rectangle(354, 401, 7, 7), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.8f);
					break;
				case 5:
					b.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2(2096f, 308f)), new Microsoft.Xna.Framework.Rectangle(354, 401, 7, 7), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.8f);
					break;
				}
			}
			if (!Game1.eventUp)
			{
				return;
			}
			Furniture.isDrawingLocationFurniture = true;
			foreach (Furniture f in furniture)
			{
				if (f.QualifiedItemId == "(F)CCFishTank")
				{
					f.draw(b, -1, -1);
				}
			}
			Furniture.isDrawingLocationFurniture = false;
		}

		public override void drawAboveAlwaysFrontLayer(SpriteBatch b)
		{
			base.drawAboveAlwaysFrontLayer(b);
			if (messageAlpha > 0f)
			{
				Junimo i = getJunimoForArea(0);
				if (i != null)
				{
					b.Draw(i.Sprite.Texture, new Vector2(Game1.viewport.Width / 2 - 32, (float)(Game1.viewport.Height * 2) / 3f - 64f), new Microsoft.Xna.Framework.Rectangle((int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds % 800.0) / 100 * 16, 0, 16, 16), Color.Lime * messageAlpha, 0f, new Vector2(i.Sprite.SpriteWidth * 4 / 2, (float)(i.Sprite.SpriteHeight * 4) * 3f / 4f) / 4f, Math.Max(0.2f, 1f) * 4f, i.flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 1f);
				}
				b.DrawString(Game1.dialogueFont, "\"" + Game1.parseText(getMessageForAreaCompletion() + "\"", Game1.dialogueFont, 640), new Vector2(Game1.viewport.Width / 2 - 320, (float)(Game1.viewport.Height * 2) / 3f), Game1.textColor * messageAlpha * 0.6f);
			}
		}

		public static string getAreaNameFromNumber(int areaNumber)
		{
			switch (areaNumber)
			{
			case 3:
				return "Boiler Room";
			case 5:
				return "Bulletin Board";
			case 1:
				return "Crafts Room";
			case 2:
				return "Fish Tank";
			case 0:
				return "Pantry";
			case 4:
				return "Vault";
			case 6:
				return "Abandoned Joja Mart";
			default:
				return "";
			}
		}

		public static string getAreaEnglishDisplayNameFromNumber(int areaNumber)
		{
			return Game1.content.LoadBaseString("Strings\\Locations:CommunityCenter_AreaName_" + getAreaNameFromNumber(areaNumber).Replace(" ", ""));
		}

		public static string getAreaDisplayNameFromNumber(int areaNumber)
		{
			return Game1.content.LoadString("Strings\\Locations:CommunityCenter_AreaName_" + getAreaNameFromNumber(areaNumber).Replace(" ", ""));
		}

		public static StaticTile[] getJunimoNoteTileFrames(int area, Map map)
		{
			if (area != 5)
			{
				return new StaticTile[20]
				{
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1832),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1824),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1825),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1826),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1827),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1828),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1829),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1830),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1831),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1832),
					new StaticTile(map.RequireLayer("Buildings"), map.TileSheets[0], BlendMode.Alpha, 1833)
				};
			}
			return new StaticTile[13]
			{
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1741),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1773),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1805),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1805),
				new StaticTile(map.RequireLayer("Front"), map.TileSheets[0], BlendMode.Alpha, 1773)
			};
		}
	}
}
