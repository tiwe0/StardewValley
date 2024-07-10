using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewValley.BellsAndWhistles;
using StardewValley.Minigames;
using StardewValley.Tools;
using xTile.Dimensions;

namespace StardewValley.Locations
{
	public class IslandSouthEastCave : IslandLocation
	{
		protected PerchingBirds _parrots;

		protected Texture2D _parrotTextures;

		public NetLongList drinksClaimed = new NetLongList();

		[XmlIgnore]
		public bool wasPirateCaveOnLoad;

		private float smokeTimer;

		public IslandSouthEastCave()
		{
		}

		public IslandSouthEastCave(string map, string name)
			: base(map, name)
		{
		}

		protected override void initNetFields()
		{
			base.initNetFields();
			base.NetFields.AddField(drinksClaimed, "drinksClaimed");
		}

		public override void updateMap()
		{
			if (isPirateNight())
			{
				mapPath.Value = "Maps\\IslandSouthEastCave_pirates";
			}
			else
			{
				mapPath.Value = "Maps\\IslandSouthEastCave";
			}
			base.updateMap();
		}

		public override void MakeMapModifications(bool force = false)
		{
			base.MakeMapModifications(force);
			if (isPirateNight())
			{
				setTileProperty(19, 9, "Buildings", "Action", "MessageSpeech Pirates1");
				setTileProperty(20, 9, "Buildings", "Action", "MessageSpeech Pirates2");
				setTileProperty(26, 17, "Buildings", "Action", "MessageSpeech Pirates3");
				setTileProperty(23, 8, "Buildings", "Action", "MessageSpeech Pirates4");
				setTileProperty(27, 5, "Buildings", "Action", "MessageSpeech Pirates5");
				setTileProperty(32, 6, "Buildings", "Action", "MessageSpeech Pirates6");
				setTileProperty(30, 8, "Buildings", "Action", "DartsGame");
				setTileProperty(33, 8, "Buildings", "Action", "Bartender");
			}
		}

		protected override void resetLocalState()
		{
			wasPirateCaveOnLoad = isPirateNight();
			base.resetLocalState();
			if (isPirateNight())
			{
				addFlame(new Vector2(25.6f, 5.7f), 0f);
				addFlame(new Vector2(18f, 11f) + new Vector2(0.2f, -0.05f));
				addFlame(new Vector2(22f, 11f) + new Vector2(0.2f, -0.05f));
				addFlame(new Vector2(23f, 16f) + new Vector2(0.2f, -0.05f));
				addFlame(new Vector2(19f, 27f) + new Vector2(0.2f, -0.05f));
				addFlame(new Vector2(33f, 10f) + new Vector2(0.2f, -0.05f));
				addFlame(new Vector2(21f, 22f) + new Vector2(0.2f, -0.05f));
				_parrotTextures = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\parrots");
				_parrots = new PerchingBirds(_parrotTextures, 3, 24, 24, new Vector2(12f, 19f), new Point[5]
				{
					new Point(12, 2),
					new Point(35, 6),
					new Point(25, 14),
					new Point(28, 1),
					new Point(27, 12)
				}, new Point[0]);
				_parrots.peckDuration = 0;
				for (int i = 0; i < 3; i++)
				{
					_parrots.AddBird(Game1.random.Next(0, 4));
				}
				Game1.changeMusicTrack("PIRATE_THEME", true);
			}
			if (AreMoonlightJelliesOut())
			{
				addMoonlightJellies(40, Utility.CreateRandom(Game1.stats.DaysPlayed, Game1.uniqueIDForThisGame, -24917.0), new Microsoft.Xna.Framework.Rectangle(0, 0, 30, 15));
			}
		}

		public static bool isWearingPirateClothes(Farmer who)
		{
			if (who.hat.Value != null)
			{
				switch (who.hat.Value.ItemId)
				{
				case "62":
				case "76":
				case "24":
					return true;
				}
			}
			if (who.hasTrinketWithID("ParrotEgg"))
			{
				return true;
			}
			return false;
		}

		/// <inheritdoc />
		public override bool performAction(string[] action, Farmer who, Location tileLocation)
		{
			if (who.IsLocalPlayer)
			{
				string text = ArgUtility.Get(action, 0);
				if (!(text == "Bartender"))
				{
					if (text == "DartsGame")
					{
						string question_prompt;
						switch (Game1.player.team.GetDroppedLimitedNutCount("Darts"))
						{
						case 0:
							question_prompt = Game1.content.LoadString("Strings\\StringsFromMaps:Pirates7_0");
							break;
						case 1:
							question_prompt = Game1.content.LoadString("Strings\\StringsFromMaps:Pirates7_1");
							break;
						case 2:
							question_prompt = Game1.content.LoadString("Strings\\StringsFromMaps:Pirates7_2");
							break;
						default:
							question_prompt = Game1.content.LoadString("Strings\\StringsFromMaps:Pirates7_3");
							break;
						}
						createQuestionDialogue(question_prompt, createYesNoResponses(), "DartsGame");
					}
				}
				else if (isWearingPirateClothes(who))
				{
					if (drinksClaimed.Contains(Game1.player.UniqueMultiplayerID))
					{
						Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromMaps:PirateBartender_PirateClothes_NoMore"));
					}
					else
					{
						Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromMaps:PirateBartender_PirateClothes"));
						Game1.afterDialogues = delegate
						{
							who.addItemByMenuIfNecessary(ItemRegistry.Create("(O)459"), delegate
							{
								drinksClaimed.Add(Game1.player.UniqueMultiplayerID);
							});
						};
					}
				}
				else
				{
					Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromMaps:Pirates8"));
				}
			}
			return base.performAction(action, who, tileLocation);
		}

		public override bool answerDialogueAction(string questionAndAnswer, string[] questionParams)
		{
			if (questionAndAnswer == null)
			{
				return false;
			}
			if (questionAndAnswer == "DartsGame_Yes")
			{
				int darts;
				switch (Game1.player.team.GetDroppedLimitedNutCount("Darts"))
				{
				case 1:
					darts = 15;
					break;
				case 2:
					darts = 10;
					break;
				default:
					darts = 20;
					break;
				}
				Game1.currentMinigame = new Darts(darts);
				return true;
			}
			return base.answerDialogueAction(questionAndAnswer, questionParams);
		}

		public override void cleanupBeforePlayerExit()
		{
			_parrots = null;
			_parrotTextures = null;
			base.cleanupBeforePlayerExit();
		}

		private void addFlame(Vector2 tileLocation, float sort_offset_tiles = 2.25f)
		{
			temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11), tileLocation * 64f, false, 0f, Color.White)
			{
				interval = 50f,
				totalNumberOfLoops = 99999,
				animationLength = 4,
				light = true,
				lightRadius = 2f,
				scale = 4f,
				layerDepth = (tileLocation.Y + sort_offset_tiles) * 64f / 10000f
			});
		}

		public override void drawAboveAlwaysFrontLayer(SpriteBatch b)
		{
			_parrots?.Draw(b);
			base.drawAboveAlwaysFrontLayer(b);
		}

		public override void DayUpdate(int dayOfMonth)
		{
			drinksClaimed.Clear();
			base.DayUpdate(dayOfMonth);
		}

		public override void SetBuriedNutLocations()
		{
			base.SetBuriedNutLocations();
			buriedNutPoints.Add(new Point(36, 26));
		}

		public override void UpdateWhenCurrentLocation(GameTime time)
		{
			base.UpdateWhenCurrentLocation(time);
			if (!isPirateNight())
			{
				return;
			}
			if (Game1.currentLocation == this && !wasPirateCaveOnLoad && Game1.locationRequest == null && Game1.activeClickableMenu == null && Game1.currentMinigame == null && Game1.CurrentEvent == null)
			{
				if (Game1.player.CurrentTool != null)
				{
					FishingRod rod = Game1.player.CurrentTool as FishingRod;
					if (rod != null && (rod.pullingOutOfWater || rod.fishCaught || rod.showingTreasure))
					{
						return;
					}
				}
				Game1.player.completelyStopAnimatingOrDoingAction();
				Game1.warpFarmer("IslandSouthEast", 29, 19, 1);
			}
			_parrots?.Update(time);
			smokeTimer -= (float)time.ElapsedGameTime.TotalMilliseconds;
			if (smokeTimer <= 0f)
			{
				Utility.addSmokePuff(this, new Vector2(25.6f, 5.7f) * 64f);
				Utility.addSmokePuff(this, new Vector2(34f, 7.2f) * 64f);
				smokeTimer = 1000f;
			}
		}

		public static bool isPirateNight()
		{
			if (!Game1.IsRainingHere() && Game1.timeOfDay >= 2000)
			{
				return Game1.dayOfMonth % 2 == 0;
			}
			return false;
		}

		public override void TransferDataFromSavedLocation(GameLocation l)
		{
			base.TransferDataFromSavedLocation(l);
			IslandSouthEastCave cave = l as IslandSouthEastCave;
			if (cave == null)
			{
				return;
			}
			drinksClaimed.Clear();
			foreach (long id in cave.drinksClaimed)
			{
				drinksClaimed.Add(id);
			}
		}
	}
}
