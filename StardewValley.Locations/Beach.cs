using System;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewValley.BellsAndWhistles;
using StardewValley.Constants;
using StardewValley.Extensions;
using StardewValley.Menus;
using StardewValley.Network;
using xTile;
using xTile.Dimensions;

namespace StardewValley.Locations
{
	public class Beach : GameLocation
	{
		private NPC oldMariner;

		[XmlElement("bridgeFixed")]
		public readonly NetBool bridgeFixed = new NetBool();

		private bool hasShownCCUpgrade;

		public Beach()
		{
		}

		public Beach(string mapPath, string name)
			: base(mapPath, name)
		{
		}

		protected override void initNetFields()
		{
			base.initNetFields();
			base.NetFields.AddField(bridgeFixed, "bridgeFixed");
			bridgeFixed.fieldChangeEvent += delegate(NetBool f, bool oldValue, bool newValue)
			{
				if (newValue && mapPath.Value != null)
				{
					fixBridge(this);
				}
			};
		}

		public override void UpdateWhenCurrentLocation(GameTime time)
		{
			if (wasUpdated)
			{
				return;
			}
			base.UpdateWhenCurrentLocation(time);
			oldMariner?.update(time, this);
			if (Game1.eventUp || !(Game1.random.NextDouble() < 1E-06))
			{
				return;
			}
			Vector2 position = new Vector2(Game1.random.Next(15, 47) * 64, Game1.random.Next(29, 42) * 64);
			bool draw = true;
			for (float i = position.Y / 64f; i < (float)map.RequireLayer("Back").LayerHeight; i += 1f)
			{
				if (!isWaterTile((int)position.X / 64, (int)i) || !isWaterTile((int)position.X / 64 - 1, (int)i) || !isWaterTile((int)position.X / 64 + 1, (int)i))
				{
					draw = false;
					break;
				}
			}
			if (draw)
			{
				temporarySprites.Add(new SeaMonsterTemporarySprite(250f, 4, Game1.random.Next(7), position));
			}
		}

		public override void cleanupBeforePlayerExit()
		{
			base.cleanupBeforePlayerExit();
			oldMariner = null;
		}

		public override void DayUpdate(int dayOfMonth)
		{
			base.DayUpdate(dayOfMonth);
			Microsoft.Xna.Framework.Rectangle tidePools = new Microsoft.Xna.Framework.Rectangle(65, 11, 25, 12);
			float chance = 1f;
			while (Game1.random.NextDouble() < (double)chance)
			{
				string id = ((Game1.random.NextDouble() < 0.2) ? "(O)397" : "(O)393");
				Vector2 position = new Vector2(Game1.random.Next(tidePools.X, tidePools.Right), Game1.random.Next(tidePools.Y, tidePools.Bottom));
				if (CanItemBePlacedHere(position))
				{
					dropObject(ItemRegistry.Create<Object>(id), position * 64f, Game1.viewport, true);
				}
				chance /= 2f;
			}
			Microsoft.Xna.Framework.Rectangle seaweedShore = new Microsoft.Xna.Framework.Rectangle(66, 24, 19, 1);
			chance = 0.25f;
			while (Game1.random.NextDouble() < (double)chance)
			{
				if (Game1.random.NextDouble() < 0.1)
				{
					Vector2 position2 = new Vector2(Game1.random.Next(seaweedShore.X, seaweedShore.Right), Game1.random.Next(seaweedShore.Y, seaweedShore.Bottom));
					if (CanItemBePlacedHere(position2))
					{
						dropObject(ItemRegistry.Create<Object>("(O)152"), position2 * 64f, Game1.viewport, true);
					}
				}
				chance /= 2f;
			}
			if (IsSummerHere() && Game1.dayOfMonth >= 12 && Game1.dayOfMonth <= 14)
			{
				for (int j = 0; j < 5; j++)
				{
					spawnObjects();
				}
				chance = 1.5f;
				while (Game1.random.NextDouble() < (double)chance)
				{
					string id2 = ((Game1.random.NextDouble() < 0.2) ? "(O)397" : "(O)393");
					Vector2 position3 = getRandomTile();
					position3.Y /= 2f;
					string prop = doesTileHaveProperty((int)position3.X, (int)position3.Y, "Type", "Back");
					if (CanItemBePlacedHere(position3) && (prop == null || !prop.Equals("Wood")))
					{
						dropObject(ItemRegistry.Create<Object>(id2), position3 * 64f, Game1.viewport, true);
					}
					chance /= 1.1f;
				}
			}
			if (!Game1.IsWinter)
			{
				return;
			}
			for (int i = characters.Count - 1; i >= 0; i--)
			{
				if (characters[i].Name.Contains("derby_contestent"))
				{
					characters.RemoveAt(i);
				}
			}
		}

		public void doneWithBridgeFix()
		{
			Game1.globalFadeToClear();
			Game1.viewportFreeze = false;
			Game1.freezeControls = false;
		}

		public void fadedForBridgeFix()
		{
			Game1.freezeControls = true;
			DelayedAction.playSoundAfterDelay("crafting", 1000);
			DelayedAction.playSoundAfterDelay("crafting", 1500);
			DelayedAction.playSoundAfterDelay("crafting", 2000);
			DelayedAction.playSoundAfterDelay("crafting", 2500);
			DelayedAction.playSoundAfterDelay("axchop", 3000);
			DelayedAction.playSoundAfterDelay("Ship", 3200);
			Game1.viewportFreeze = true;
			Game1.viewport.X = -10000;
			bridgeFixed.Value = true;
			Game1.pauseThenDoFunction(4000, doneWithBridgeFix);
			fixBridge(this);
		}

		public override bool answerDialogueAction(string questionAndAnswer, string[] questionParams)
		{
			if (questionAndAnswer == "BeachBridge_Yes")
			{
				Game1.globalFadeToBlack(fadedForBridgeFix);
				Game1.player.Items.ReduceId("(O)388", 300);
				return true;
			}
			return base.answerDialogueAction(questionAndAnswer, questionParams);
		}

		public override bool checkAction(Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who)
		{
			switch (getTileIndexAt(tileLocation, "Buildings"))
			{
			case 284:
				if (who.Items.ContainsId("(O)388", 300))
				{
					createQuestionDialogue(Game1.content.LoadString("Strings\\Locations:Beach_FixBridge_Question"), createYesNoResponses(), "BeachBridge");
				}
				else
				{
					Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\Locations:Beach_FixBridge_Hint"));
				}
				break;
			case 496:
				if (!Game1.MasterPlayer.mailReceived.Contains("spring_2_1"))
				{
					Game1.drawLetterMessage(Game1.content.LoadString("Strings\\Locations:Beach_GoneFishingMessage").Replace('\n', '^'));
					return false;
				}
				break;
			}
			if (oldMariner != null && oldMariner.TilePoint.X == tileLocation.X && oldMariner.TilePoint.Y == tileLocation.Y)
			{
				string playerTerm = Game1.content.LoadString("Strings\\Locations:Beach_Mariner_Player_" + (who.IsMale ? "Male" : "Female"));
				if (!who.isMarriedOrRoommates() && who.specialItems.Contains("460") && !Utility.doesItemExistAnywhere("(O)460"))
				{
					for (int i = who.specialItems.Count - 1; i >= 0; i--)
					{
						if (who.specialItems[i] == "460")
						{
							who.specialItems.RemoveAt(i);
						}
					}
				}
				if (who.isMarriedOrRoommates())
				{
					Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\Locations:Beach_Mariner_PlayerMarried", playerTerm)));
				}
				else if (who.specialItems.Contains("460"))
				{
					Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\Locations:Beach_Mariner_PlayerHasItem", playerTerm)));
				}
				else if (who.hasAFriendWithHeartLevel(10, true) && (int)who.houseUpgradeLevel == 0)
				{
					Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\Locations:Beach_Mariner_PlayerNotUpgradedHouse", playerTerm)));
				}
				else if (who.hasAFriendWithHeartLevel(10, true))
				{
					Response[] answers = new Response[2]
					{
						new Response("Buy", Game1.content.LoadString("Strings\\Locations:Beach_Mariner_PlayerBuyItem_AnswerYes")),
						new Response("Not", Game1.content.LoadString("Strings\\Locations:Beach_Mariner_PlayerBuyItem_AnswerNo"))
					};
					createQuestionDialogue(Game1.parseText(Game1.content.LoadString("Strings\\Locations:Beach_Mariner_PlayerBuyItem_Question", playerTerm)), answers, "mariner");
				}
				else
				{
					Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\Locations:Beach_Mariner_PlayerNoRelationship", playerTerm)));
				}
				return true;
			}
			return base.checkAction(tileLocation, viewport, who);
		}

		public override bool isCollidingPosition(Microsoft.Xna.Framework.Rectangle position, xTile.Dimensions.Rectangle viewport, bool isFarmer, int damagesFarmer, bool glider, Character character)
		{
			if (oldMariner != null && position.Intersects(oldMariner.GetBoundingBox()))
			{
				return true;
			}
			return base.isCollidingPosition(position, viewport, isFarmer, damagesFarmer, glider, character);
		}

		/// <inheritdoc />
		public override void checkForMusic(GameTime time)
		{
			if (Game1.random.NextDouble() < 0.003 && Game1.timeOfDay < 1900)
			{
				localSound("seagulls");
			}
			base.checkForMusic(time);
		}

		protected override void resetSharedState()
		{
			base.resetSharedState();
			if (IsSummerHere() && Game1.dayOfMonth >= 12 && Game1.dayOfMonth <= 14)
			{
				waterColor.Value = new Color(0, 255, 0) * 0.4f;
			}
		}

		public override void performTenMinuteUpdate(int timeOfDay)
		{
			base.performTenMinuteUpdate(timeOfDay);
			if (!Game1.IsWinter || Game1.dayOfMonth < 12 || Game1.dayOfMonth > 13)
			{
				return;
			}
			Random r = Utility.CreateDaySaveRandom(Game1.timeOfDay * 20);
			NPC i = getCharacterFromName("winter_derby_contestent" + r.Next(10));
			if (i == null)
			{
				return;
			}
			i.shake(600);
			if (r.NextBool(0.25))
			{
				int whichSaying = r.Next(7);
				i.showTextAboveHead(Game1.content.LoadString("Strings\\1_6_Strings:FishingDerby_Exclamation" + whichSaying));
				if (whichSaying == 0 || whichSaying == 6)
				{
					temporarySprites.Add(new TemporaryAnimatedSprite(151, 1500f, 1, 1, i.Position, false, false, false, 0f)
					{
						motion = new Vector2((float)Game1.random.Next(-10, 10) / 10f, -7f),
						acceleration = new Vector2(0f, 0.1f),
						alphaFade = 0.001f,
						drawAboveAlwaysFront = true
					});
				}
				i.jump(4f);
			}
		}

		public override void MakeMapModifications(bool force = false)
		{
			base.MakeMapModifications(force);
			if (force)
			{
				hasShownCCUpgrade = false;
			}
			if ((bool)bridgeFixed)
			{
				fixBridge(this);
			}
			if (Game1.MasterPlayer.mailReceived.Contains("communityUpgradeShortcuts"))
			{
				showCommunityUpgradeShortcuts(this, ref hasShownCCUpgrade);
			}
			if (Game1.IsWinter && Game1.dayOfMonth >= 9 && Game1.dayOfMonth <= 11)
			{
				ApplyMapOverride(Game1.game1.xTileContent.Load<Map>("Maps\\Forest_FishingDerbySign"), "Forest_FishingDerbySign", null, new Microsoft.Xna.Framework.Rectangle(15, 5, 2, 3), base.cleanUpTileForMapOverride);
			}
			else if (_appliedMapOverrides.Contains("Forest_FishingDerbySign"))
			{
				ApplyMapOverride("Beach_SquidFestSign_Revert", (Microsoft.Xna.Framework.Rectangle?)null, (Microsoft.Xna.Framework.Rectangle?)new Microsoft.Xna.Framework.Rectangle(15, 5, 2, 3));
				_appliedMapOverrides.Remove("Forest_FishingDerbySign");
				_appliedMapOverrides.Remove("Beach_SquidFestSign_Revert");
			}
			if (Game1.IsWinter && Game1.dayOfMonth >= 12 && Game1.dayOfMonth <= 13)
			{
				if (getCharacterFromName("winter_derby_contestent0") == null && (Game1.IsMasterGame || !Game1.player.sleptInTemporaryBed))
				{
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(15, 17))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 0, 16, 64), new Vector2(15f, 17f) * 64f, -1, "winter_derby_contestent0")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 96f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(30, 21))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 2, 16, 64), new Vector2(30f, 21f) * 64f, -1, "winter_derby_contestent1")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 96f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(13, 39))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 3, 16, 64), new Vector2(13f, 39f) * 64f, -1, "winter_derby_contestent2")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 96f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(42, 25))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 1, 16, 64), new Vector2(42f, 25f) * 64f, -1, "winter_derby_contestent3")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 96f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(50, 25) && checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(51, 25))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 2, 32, 64), new Vector2(50f, 25f) * 64f, -1, "winter_derby_contestent4")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 96f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(56, 19))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 8, 32, 32), new Vector2(56f, 19f) * 64f, -1, "winter_derby_contestent5")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 0f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(11, 28))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 9, 32, 32), new Vector2(10f, 28f) * 64f, -1, "winter_derby_contestent6")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 0f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(14, 39))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 10, 32, 32), new Vector2(14f, 39f) * 64f, -1, "winter_derby_contestent7")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 0f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(90, 40))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 11, 32, 32), new Vector2(90f, 40f) * 64f, -1, "winter_derby_contestent8")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 0f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(8, 12))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 12, 32, 32), new Vector2(7f, 12f) * 64f, -1, "winter_derby_contestent9")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 0f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(47, 21))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 6, 16, 64), new Vector2(47f, 21f) * 64f, -1, "winter_derby_contestent10")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 96f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
					if (checkForTerrainFeaturesAndObjectsButDestroyNonPlayerItems(22, 8))
					{
						characters.Add(new NPC(new AnimatedSprite("Characters\\Assorted_Fishermen_Winter", 7, 16, 64), new Vector2(22f, 8f) * 64f, -1, "winter_derby_contestent11")
						{
							Breather = false,
							HideShadow = true,
							drawOffset = new Vector2(0f, 96f),
							shouldShadowBeOffset = true,
							SimpleNonVillagerNPC = true
						});
					}
				}
				if (getCharacterFromName("winter_derby_contestent0") != null)
				{
					NPC npc12 = getCharacterFromName("winter_derby_contestent0");
					if (npc12.Sprite == null || npc12.Sprite.Texture == null)
					{
						npc12.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 0, 16, 64);
					}
					npc12.drawOffset = new Vector2(0f, 96f);
					npc12.shouldShadowBeOffset = true;
					npc12.SimpleNonVillagerNPC = true;
					npc12.HideShadow = true;
					npc12.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent1") != null)
				{
					NPC npc11 = getCharacterFromName("winter_derby_contestent1");
					if (npc11.Sprite == null || npc11.Sprite.Texture == null)
					{
						npc11.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 2, 16, 64);
					}
					npc11.Sprite.CurrentFrame = 2;
					npc11.drawOffset = new Vector2(0f, 96f);
					npc11.shouldShadowBeOffset = true;
					npc11.SimpleNonVillagerNPC = true;
					npc11.HideShadow = true;
					npc11.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent2") != null)
				{
					NPC npc10 = getCharacterFromName("winter_derby_contestent2");
					if (npc10.Sprite == null || npc10.Sprite.Texture == null)
					{
						npc10.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 3, 16, 64);
					}
					npc10.Sprite.CurrentFrame = 3;
					npc10.drawOffset = new Vector2(0f, 96f);
					npc10.shouldShadowBeOffset = true;
					npc10.SimpleNonVillagerNPC = true;
					npc10.HideShadow = true;
					npc10.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent3") != null)
				{
					NPC npc9 = getCharacterFromName("winter_derby_contestent3");
					if (npc9.Sprite == null || npc9.Sprite.Texture == null)
					{
						npc9.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 1, 16, 64);
					}
					npc9.Sprite.CurrentFrame = 1;
					npc9.drawOffset = new Vector2(0f, 96f);
					npc9.shouldShadowBeOffset = true;
					npc9.SimpleNonVillagerNPC = true;
					npc9.HideShadow = true;
					npc9.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent4") != null)
				{
					NPC npc8 = getCharacterFromName("winter_derby_contestent4");
					if (npc8.Sprite == null || npc8.Sprite.Texture == null)
					{
						npc8.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 2, 32, 64);
					}
					npc8.Sprite.CurrentFrame = 2;
					npc8.drawOffset = new Vector2(0f, 96f);
					npc8.shouldShadowBeOffset = true;
					npc8.SimpleNonVillagerNPC = true;
					npc8.HideShadow = true;
					npc8.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent5") != null)
				{
					NPC npc7 = getCharacterFromName("winter_derby_contestent5");
					if (npc7.Sprite == null || npc7.Sprite.Texture == null)
					{
						npc7.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 8, 32, 32);
					}
					npc7.Sprite.CurrentFrame = 8;
					npc7.shouldShadowBeOffset = true;
					npc7.SimpleNonVillagerNPC = true;
					npc7.HideShadow = true;
					npc7.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent6") != null)
				{
					NPC npc6 = getCharacterFromName("winter_derby_contestent6");
					if (npc6.Sprite == null || npc6.Sprite.Texture == null)
					{
						npc6.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 9, 32, 32);
					}
					npc6.Sprite.CurrentFrame = 9;
					npc6.shouldShadowBeOffset = true;
					npc6.SimpleNonVillagerNPC = true;
					npc6.HideShadow = true;
					npc6.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent7") != null)
				{
					NPC npc5 = getCharacterFromName("winter_derby_contestent7");
					if (npc5.Sprite == null || npc5.Sprite.Texture == null)
					{
						npc5.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 10, 32, 32);
					}
					npc5.Sprite.CurrentFrame = 10;
					npc5.shouldShadowBeOffset = true;
					npc5.SimpleNonVillagerNPC = true;
					npc5.HideShadow = true;
					npc5.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent8") != null)
				{
					NPC npc4 = getCharacterFromName("winter_derby_contestent8");
					if (npc4.Sprite == null || npc4.Sprite.Texture == null)
					{
						npc4.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 11, 32, 32);
					}
					npc4.Sprite.CurrentFrame = 11;
					npc4.shouldShadowBeOffset = true;
					npc4.SimpleNonVillagerNPC = true;
					npc4.HideShadow = true;
					npc4.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent9") != null)
				{
					NPC npc3 = getCharacterFromName("winter_derby_contestent9");
					if (npc3.Sprite == null || npc3.Sprite.Texture == null)
					{
						npc3.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 12, 32, 32);
					}
					npc3.Sprite.CurrentFrame = 12;
					npc3.shouldShadowBeOffset = true;
					npc3.SimpleNonVillagerNPC = true;
					npc3.HideShadow = true;
					npc3.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent10") != null)
				{
					NPC npc2 = getCharacterFromName("winter_derby_contestent10");
					if (npc2.Sprite == null || npc2.Sprite.Texture == null)
					{
						npc2.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 6, 16, 64);
					}
					npc2.Sprite.CurrentFrame = 6;
					npc2.drawOffset = new Vector2(0f, 96f);
					npc2.shouldShadowBeOffset = true;
					npc2.SimpleNonVillagerNPC = true;
					npc2.HideShadow = true;
					npc2.Breather = false;
				}
				if (getCharacterFromName("winter_derby_contestent11") != null)
				{
					NPC npc = getCharacterFromName("winter_derby_contestent11");
					if (npc.Sprite == null || npc.Sprite.Texture == null)
					{
						npc.Sprite = new AnimatedSprite("Characters\\Assorted_Fishermen", 7, 16, 64);
					}
					npc.Sprite.CurrentFrame = 7;
					npc.drawOffset = new Vector2(0f, 96f);
					npc.shouldShadowBeOffset = true;
					npc.SimpleNonVillagerNPC = true;
					npc.HideShadow = true;
					npc.Breather = false;
				}
				ApplyMapOverride(Game1.game1.xTileContent.Load<Map>("Maps\\Beach_SquidFest"), "Beach_SquidFest", null, new Microsoft.Xna.Framework.Rectangle(11, 3, 16, 5), base.cleanUpTileForMapOverride);
				if (Game1.dayOfMonth == 13)
				{
					setMapTileIndex(13, 6, 51, "Front");
					setMapTileIndex(13, 5, 43, "AlwaysFront");
				}
				setFireplace(true, 48, 20, false, 0, 64);
				Game1.currentLightSources.Add(new LightSource(1, new Vector2(732f, 480f), 4f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(1, new Vector2(1064f, 368f), 4f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(1, new Vector2(1692f, 476f), 4f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(1, new Vector2(1372f, 476f), 4f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(1, new Vector2(1532f, 380f), 4f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(1, new Vector2(15.5f, 17.5f) * 64f, 4f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(1, new Vector2(30.5f, 21f) * 64f, 4f, LightSource.LightContext.None, 0L));
			}
			else
			{
				if (!_appliedMapOverrides.Contains("Beach_SquidFest") && getTileIndexAt(11, 7, "Buildings") != 45)
				{
					return;
				}
				ApplyMapOverride("Beach_SquidFest_Revert", (Microsoft.Xna.Framework.Rectangle?)null, (Microsoft.Xna.Framework.Rectangle?)new Microsoft.Xna.Framework.Rectangle(11, 3, 16, 5));
				_appliedMapOverrides.Remove("Beach_SquidFest");
				_appliedMapOverrides.Remove("Beach_SquidFest_Revert");
				for (int i = characters.Count - 1; i >= 0; i--)
				{
					if (characters[i].Name.Contains("derby_contestent"))
					{
						characters.RemoveAt(i);
					}
				}
			}
		}

		public override void drawOverlays(SpriteBatch b)
		{
			if (Game1.IsWinter && Game1.dayOfMonth >= 12 && Game1.dayOfMonth <= 13)
			{
				SpecialCurrencyDisplay.Draw(b, new Vector2(16f, 0f), (int)Game1.stats.Get(StatKeys.SquidFestScore(Game1.dayOfMonth, Game1.year)), Game1.objectSpriteSheet, new Microsoft.Xna.Framework.Rectangle(112, 96, 16, 16));
			}
			base.drawOverlays(b);
		}

		protected override void resetLocalState()
		{
			base.resetLocalState();
			int numSeagulls = Game1.random.Next(6);
			foreach (Vector2 tile in Utility.getPositionsInClusterAroundThisTile(new Vector2(Game1.random.Next(map.DisplayWidth / 64), Game1.random.Next(12, map.DisplayHeight / 64)), numSeagulls))
			{
				if (!isTileOnMap(tile) || (!CanItemBePlacedHere(tile) && !isWaterTile((int)tile.X, (int)tile.Y)) || (!(tile.X < 23f) && !(tile.X > 46f)))
				{
					continue;
				}
				int state = 3;
				if (isWaterTile((int)tile.X, (int)tile.Y) && doesTileHaveProperty((int)tile.X, (int)tile.Y, "Passable", "Buildings") == null)
				{
					state = 2;
					if (Game1.random.NextBool())
					{
						continue;
					}
				}
				critters.Add(new Seagull(tile * 64f + new Vector2(32f, 32f), state));
			}
			tryAddPrismaticButterfly();
			if (IsRainingHere() && Game1.timeOfDay < 1900)
			{
				oldMariner = new NPC(new AnimatedSprite("Characters\\Mariner", 0, 16, 32), new Vector2(80f, 5f) * 64f, 2, "Old Mariner")
				{
					AllowDynamicAppearance = false
				};
			}
		}

		public static void showCommunityUpgradeShortcuts(GameLocation location, ref bool flag)
		{
			if (flag)
			{
				return;
			}
			flag = true;
			location.warps.Add(new Warp(-1, 4, "Forest", 119, 35, false));
			location.warps.Add(new Warp(-1, 5, "Forest", 119, 35, false));
			location.warps.Add(new Warp(-1, 6, "Forest", 119, 36, false));
			location.warps.Add(new Warp(-1, 7, "Forest", 119, 36, false));
			for (int x = 0; x < 5; x++)
			{
				for (int y = 4; y < 7; y++)
				{
					location.removeTile(x, y, "Buildings");
				}
			}
			location.removeTile(7, 6, "Buildings");
			location.removeTile(5, 6, "Buildings");
			location.removeTile(6, 6, "Buildings");
			location.setMapTileIndex(3, 7, 107, "Back");
			location.removeTile(67, 5, "Buildings");
			location.removeTile(67, 4, "Buildings");
			location.removeTile(67, 3, "Buildings");
			location.removeTile(67, 2, "Buildings");
			location.removeTile(67, 1, "Buildings");
			location.removeTile(67, 0, "Buildings");
			location.removeTile(66, 3, "Buildings");
			location.removeTile(68, 3, "Buildings");
		}

		public static void fixBridge(GameLocation location)
		{
			if (!NetWorldState.checkAnywhereForWorldStateID("beachBridgeFixed"))
			{
				NetWorldState.addWorldStateIDEverywhere("beachBridgeFixed");
			}
			location.updateMap();
			int whichTileSheet = ((!location.name.Value.Contains("Market")) ? 1 : 2);
			location.setMapTile(58, 13, 301, "Buildings", null, whichTileSheet);
			location.setMapTile(59, 13, 301, "Buildings", null, whichTileSheet);
			location.setMapTile(60, 13, 301, "Buildings", null, whichTileSheet);
			location.setMapTile(61, 13, 301, "Buildings", null, whichTileSheet);
			location.setMapTile(58, 14, 336, "Back", null, whichTileSheet);
			location.setMapTile(59, 14, 336, "Back", null, whichTileSheet);
			location.setMapTile(60, 14, 336, "Back", null, whichTileSheet);
			location.setMapTile(61, 14, 336, "Back", null, whichTileSheet);
		}

		public override void draw(SpriteBatch b)
		{
			oldMariner?.draw(b);
			base.draw(b);
			if (!bridgeFixed)
			{
				float yOffset = 4f * (float)Math.Round(Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 250.0), 2);
				b.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2(3704f, 720f + yOffset)), new Microsoft.Xna.Framework.Rectangle(141, 465, 20, 24), Color.White * 0.75f, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.095401f);
				b.Draw(Game1.mouseCursors, Game1.GlobalToLocal(Game1.viewport, new Vector2(3744f, 760f + yOffset)), new Microsoft.Xna.Framework.Rectangle(175, 425, 12, 12), Color.White * 0.75f, 0f, new Vector2(6f, 6f), 4f, SpriteEffects.None, 0.09541f);
			}
		}
	}
}
