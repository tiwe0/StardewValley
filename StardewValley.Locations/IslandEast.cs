using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewValley.BellsAndWhistles;
using xTile.Dimensions;

namespace StardewValley.Locations
{
	public class IslandEast : IslandForestLocation
	{
		protected PerchingBirds _parrots;

		protected Texture2D _parrotTextures;

		protected NetEvent0 bananaShrineEvent = new NetEvent0();

		public NetBool bananaShrineComplete = new NetBool();

		public NetBool bananaShrineNutAwarded = new NetBool();

		public IslandEast()
		{
		}

		public IslandEast(string map, string name)
			: base(map, name)
		{
		}

		protected override void initNetFields()
		{
			base.initNetFields();
			base.NetFields.AddField(bananaShrineEvent.NetFields, "bananaShrineEvent.NetFields").AddField(bananaShrineComplete, "bananaShrineComplete").AddField(bananaShrineNutAwarded, "bananaShrineNutAwarded");
			bananaShrineEvent.onEvent += OnBananaShrine;
		}

		public virtual void AddTorchLights()
		{
			removeTemporarySpritesWithIDLocal(6666);
			int torch_x = 1280;
			int torch_y = 704;
			temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1965, 8, 8), new Vector2(torch_x + 24, torch_y + 48), false, 0f, Color.White)
			{
				interval = 50f,
				totalNumberOfLoops = 99999,
				animationLength = 7,
				light = true,
				id = 6666,
				lightRadius = 1f,
				scale = 3f,
				layerDepth = (float)(torch_y + 48) / 10000f + 0.0001f,
				delayBeforeAnimationStart = 0
			});
			temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1984, 12, 12), new Vector2(torch_x + 16, torch_y + 28), false, 0f, Color.White)
			{
				interval = 50f,
				totalNumberOfLoops = 99999,
				animationLength = 4,
				light = true,
				id = 6666,
				lightRadius = 1f,
				scale = 3f,
				layerDepth = (float)(torch_y + 28) / 10000f + 0.0001f,
				delayBeforeAnimationStart = 0
			});
			torch_x = 1472;
			torch_y = 704;
			temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1965, 8, 8), new Vector2(torch_x + 24, torch_y + 48), false, 0f, Color.White)
			{
				interval = 50f,
				totalNumberOfLoops = 99999,
				animationLength = 7,
				light = true,
				id = 6666,
				lightRadius = 1f,
				scale = 3f,
				layerDepth = (float)(torch_y + 48) / 10000f + 0.0001f,
				delayBeforeAnimationStart = 0
			});
			temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1984, 12, 12), new Vector2(torch_x + 16, torch_y + 28), false, 0f, Color.White)
			{
				interval = 50f,
				totalNumberOfLoops = 99999,
				animationLength = 4,
				light = true,
				id = 6666,
				lightRadius = 1f,
				scale = 3f,
				layerDepth = (float)(torch_y + 28) / 10000f + 0.0001f,
				delayBeforeAnimationStart = 0
			});
		}

		protected override void resetLocalState()
		{
			_parrotTextures = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\parrots");
			base.resetLocalState();
			for (int j = 0; j < 5; j++)
			{
				Vector2 v = Utility.getRandomPositionInThisRectangle(new Microsoft.Xna.Framework.Rectangle(14, 3, 16, 12), Game1.random);
				critters.Add(new Firefly(v));
			}
			AddTorchLights();
			if (bananaShrineComplete.Value)
			{
				AddGorillaShrineTorches(0);
			}
			_parrots = new PerchingBirds(_parrotTextures, 3, 24, 24, new Vector2(12f, 19f), new Point[9]
			{
				new Point(18, 8),
				new Point(17, 9),
				new Point(20, 7),
				new Point(21, 8),
				new Point(22, 7),
				new Point(23, 8),
				new Point(18, 12),
				new Point(25, 11),
				new Point(27, 8)
			}, new Point[0]);
			_parrots.peckDuration = 0;
			for (int i = 0; i < 5; i++)
			{
				_parrots.AddBird(Game1.random.Next(0, 4));
			}
			if (bananaShrineComplete.Value && Utility.CreateRandom(Game1.uniqueIDForThisGame, Game1.stats.DaysPlayed, 1111.0).NextDouble() < 0.1)
			{
				temporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(32, 352, 32, 32), 500f, 2, 999, new Vector2(15.5f, 19f) * 64f, false, false, 0.1216f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 888,
					yStopCoordinate = 1497,
					motion = new Vector2(0f, 1f),
					reachedStopCoordinate = gorillaReachedShrineCosmetic,
					delayBeforeAnimationStart = 1000
				});
			}
			addOneTimeGiftBox(ItemRegistry.Create("(O)TentKit", 3), 30, 40, 4);
		}

		public override void cleanupBeforePlayerExit()
		{
			_parrots = null;
			_parrotTextures = null;
			base.cleanupBeforePlayerExit();
		}

		public override void UpdateWhenCurrentLocation(GameTime time)
		{
			base.UpdateWhenCurrentLocation(time);
			bananaShrineEvent.Poll();
			_parrots?.Update(time);
			if (bananaShrineComplete.Value && Game1.random.NextDouble() < 0.005)
			{
				TemporaryAnimatedSprite t = getTemporarySpriteByID(888);
				if (t != null && t.motion.Equals(Vector2.Zero))
				{
					temporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(128, 352, 32, 32), 200 + ((Game1.random.NextDouble() < 0.1) ? Game1.random.Next(1000, 3000) : 0), 1, 1, t.position, false, false, 0.12224f, 0f, Color.White, 4f, 0f, 0f, 0f));
				}
			}
		}

		public virtual void SpawnBananaNutReward()
		{
			if (!bananaShrineNutAwarded.Value && Game1.IsMasterGame)
			{
				Game1.player.team.MarkCollectedNut("BananaShrine");
				bananaShrineNutAwarded.Value = true;
				for (int i = 0; i < 3; i++)
				{
					Game1.createItemDebris(ItemRegistry.Create("(O)73"), new Vector2(16.5f, 25f) * 64f, 0, this, 1280);
				}
			}
		}

		public override void DayUpdate(int dayOfMonth)
		{
			if (Game1.IsMasterGame && bananaShrineComplete.Value && !bananaShrineNutAwarded.Value)
			{
				SpawnBananaNutReward();
			}
			base.DayUpdate(dayOfMonth);
			Microsoft.Xna.Framework.Rectangle parrot_platform_rect = new Microsoft.Xna.Framework.Rectangle(27, 27, 3, 3);
			for (int i = 0; i < 8; i++)
			{
				Vector2 v = getRandomTile();
				if (v.Y < 24f)
				{
					v.Y += 24f;
				}
				if (v.X > 4f && getTileIndexAt((int)v.X, (int)v.Y, "AlwaysFront") == -1 && CanItemBePlacedHere(v, false, CollisionMask.All, CollisionMask.None) && doesTileHavePropertyNoNull((int)v.X, (int)v.Y, "Type", "Back") == "Grass" && !IsNoSpawnTile(v) && doesTileHavePropertyNoNull((int)v.X + 1, (int)v.Y, "Type", "Back") != "Stone" && doesTileHavePropertyNoNull((int)v.X - 1, (int)v.Y, "Type", "Back") != "Stone" && doesTileHavePropertyNoNull((int)v.X, (int)v.Y + 1, "Type", "Back") != "Stone" && doesTileHavePropertyNoNull((int)v.X, (int)v.Y - 1, "Type", "Back") != "Stone" && !parrot_platform_rect.Contains((int)v.X, (int)v.Y))
				{
					if (Game1.random.NextDouble() < 0.04)
					{
						Object fiddlehead = ItemRegistry.Create<Object>("(O)259");
						fiddlehead.isSpawnedObject.Value = true;
						objects.Add(v, fiddlehead);
					}
					else
					{
						objects.Add(v, ItemRegistry.Create<Object>("(O)" + (882 + Game1.random.Next(3))));
					}
				}
			}
		}

		public override void drawAboveAlwaysFrontLayer(SpriteBatch b)
		{
			_parrots?.Draw(b);
			base.drawAboveAlwaysFrontLayer(b);
		}

		public virtual void AddGorillaShrineTorches(int delay)
		{
			if (getTemporarySpriteByID(12038) == null)
			{
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11), new Vector2(15f, 24f) * 64f + new Vector2(8f, -16f), false, 0f, Color.White)
				{
					interval = 50f,
					totalNumberOfLoops = 99999,
					animationLength = 4,
					light = true,
					lightRadius = 2f,
					delayBeforeAnimationStart = delay,
					scale = 4f,
					layerDepth = 0.16704f,
					id = 12038
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11), new Vector2(17f, 24f) * 64f + new Vector2(8f, -16f), false, 0f, Color.White)
				{
					interval = 50f,
					totalNumberOfLoops = 99999,
					animationLength = 4,
					light = true,
					lightRadius = 2f,
					delayBeforeAnimationStart = delay,
					scale = 4f,
					layerDepth = 0.16704f,
					id = 12097
				});
			}
		}

		public override void TransferDataFromSavedLocation(GameLocation l)
		{
			base.TransferDataFromSavedLocation(l);
			IslandEast location = l as IslandEast;
			if (location != null)
			{
				bananaShrineComplete.Value = location.bananaShrineComplete.Value;
				bananaShrineNutAwarded.Value = location.bananaShrineNutAwarded.Value;
			}
		}

		public virtual void OnBananaShrine()
		{
			Location tileLocation = new Location(16, 26);
			temporarySprites.Add(new TemporaryAnimatedSprite("Maps\\springobjects", new Microsoft.Xna.Framework.Rectangle(304, 48, 16, 16), new Vector2(16f, tileLocation.Y - 1) * 64f, false, 0f, Color.White)
			{
				id = 88976,
				scale = 4f,
				layerDepth = ((float)tileLocation.Y + 1.2f) * 64f / 10000f,
				dontClearOnAreaEntry = true
			});
			temporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(32, 352, 32, 32), 400f, 2, 999, new Vector2(15.5f, 19f) * 64f, false, false, 0.1216f, 0f, Color.White, 4f, 0f, 0f, 0f)
			{
				id = 777,
				yStopCoordinate = 1497,
				motion = new Vector2(0f, 2f),
				reachedStopCoordinate = gorillaReachedShrine,
				delayBeforeAnimationStart = 1000,
				dontClearOnAreaEntry = true
			});
			if (Game1.currentLocation == this)
			{
				Game1.playSound("coin");
				DelayedAction.playSoundAfterDelay("fireball", 800);
			}
			AddGorillaShrineTorches(800);
			if (Game1.currentLocation == this)
			{
				DelayedAction.playSoundAfterDelay("grassyStep", 1400);
				DelayedAction.playSoundAfterDelay("grassyStep", 1800);
				DelayedAction.playSoundAfterDelay("grassyStep", 2200);
				DelayedAction.playSoundAfterDelay("grassyStep", 2600);
				DelayedAction.playSoundAfterDelay("grassyStep", 3000);
				Game1.changeMusicTrack("none");
				DelayedAction.playSoundAfterDelay("gorilla_intro", 2000);
			}
		}

		/// <inheritdoc />
		public override bool performAction(string[] action, Farmer who, Location tileLocation)
		{
			if (ArgUtility.Get(action, 0) == "BananaShrine")
			{
				if (who.CurrentItem?.QualifiedItemId == "(O)91" && getTemporarySpriteByID(777) == null && !bananaShrineComplete.Value)
				{
					bananaShrineComplete.Value = true;
					who.reduceActiveItemByOne();
					bananaShrineEvent.Fire();
					return true;
				}
				if (getTemporarySpriteByID(777) == null && !bananaShrineComplete.Value)
				{
					who.doEmote(8);
				}
			}
			return base.performAction(action, who, tileLocation);
		}

		private void gorillaReachedShrine(int extra)
		{
			TemporaryAnimatedSprite temporarySpriteByID = getTemporarySpriteByID(777);
			temporarySpriteByID.sourceRect = new Microsoft.Xna.Framework.Rectangle(0, 352, 32, 32);
			temporarySpriteByID.sourceRectStartingPos = Utility.PointToVector2(temporarySpriteByID.sourceRect.Location);
			temporarySpriteByID.currentNumberOfLoops = 0;
			temporarySpriteByID.totalNumberOfLoops = 1;
			temporarySpriteByID.interval = 1000f;
			temporarySpriteByID.timer = 0f;
			temporarySpriteByID.motion = Vector2.Zero;
			temporarySpriteByID.animationLength = 1;
			temporarySpriteByID.endFunction = gorillaGrabBanana;
		}

		private void gorillaReachedShrineCosmetic(int extra)
		{
			TemporaryAnimatedSprite temporarySpriteByID = getTemporarySpriteByID(888);
			temporarySpriteByID.sourceRect = new Microsoft.Xna.Framework.Rectangle(192, 352, 32, 32);
			temporarySpriteByID.sourceRectStartingPos = Utility.PointToVector2(temporarySpriteByID.sourceRect.Location);
			temporarySpriteByID.currentNumberOfLoops = 0;
			temporarySpriteByID.totalNumberOfLoops = 999999;
			temporarySpriteByID.interval = 8000f;
			temporarySpriteByID.timer = 0f;
			temporarySpriteByID.motion = Vector2.Zero;
			temporarySpriteByID.animationLength = 1;
		}

		private void gorillaGrabBanana(int extra)
		{
			TemporaryAnimatedSprite gorilla = getTemporarySpriteByID(777);
			DelayedAction.functionAfterDelay(delegate
			{
				removeTemporarySpritesWithID(88976);
			}, 50);
			if (Game1.currentLocation == this)
			{
				Game1.playSound("slimeHit");
			}
			gorilla.sourceRect = new Microsoft.Xna.Framework.Rectangle(96, 352, 32, 32);
			gorilla.sourceRectStartingPos = Utility.PointToVector2(gorilla.sourceRect.Location);
			gorilla.currentNumberOfLoops = 0;
			gorilla.totalNumberOfLoops = 1;
			gorilla.interval = 1000f;
			gorilla.timer = 0f;
			gorilla.animationLength = 1;
			gorilla.endFunction = gorillaEatBanana;
			temporarySprites.Add(gorilla);
		}

		private void gorillaEatBanana(int extra)
		{
			TemporaryAnimatedSprite gorilla = getTemporarySpriteByID(777);
			gorilla.sourceRect = new Microsoft.Xna.Framework.Rectangle(128, 352, 32, 32);
			gorilla.sourceRectStartingPos = Utility.PointToVector2(gorilla.sourceRect.Location);
			gorilla.currentNumberOfLoops = 0;
			gorilla.totalNumberOfLoops = 5;
			gorilla.interval = 300f;
			gorilla.timer = 0f;
			gorilla.animationLength = 2;
			gorilla.endFunction = gorillaAfterEat;
			if (Game1.currentLocation == this)
			{
				Game1.playSound("eat");
				DelayedAction.playSoundAfterDelay("eat", 600);
				DelayedAction.playSoundAfterDelay("eat", 1200);
				DelayedAction.playSoundAfterDelay("eat", 1800);
				DelayedAction.playSoundAfterDelay("eat", 2400);
			}
			temporarySprites.Add(gorilla);
		}

		private void gorillaAfterEat(int extra)
		{
			TemporaryAnimatedSprite gorilla = getTemporarySpriteByID(777);
			gorilla.sourceRect = new Microsoft.Xna.Framework.Rectangle(0, 352, 32, 32);
			gorilla.sourceRectStartingPos = Utility.PointToVector2(gorilla.sourceRect.Location);
			gorilla.currentNumberOfLoops = 0;
			gorilla.totalNumberOfLoops = 1;
			gorilla.interval = 1000f;
			gorilla.timer = 0f;
			gorilla.motion = Vector2.Zero;
			gorilla.animationLength = 1;
			gorilla.endFunction = gorillaSpawnNut;
			gorilla.shakeIntensity = 1f;
			gorilla.shakeIntensityChange = -0.01f;
			temporarySprites.Add(gorilla);
		}

		private void gorillaSpawnNut(int extra)
		{
			TemporaryAnimatedSprite gorilla = getTemporarySpriteByID(777);
			gorilla.sourceRect = new Microsoft.Xna.Framework.Rectangle(0, 352, 32, 32);
			gorilla.sourceRectStartingPos = Utility.PointToVector2(gorilla.sourceRect.Location);
			gorilla.currentNumberOfLoops = 0;
			gorilla.totalNumberOfLoops = 1;
			gorilla.interval = 1000f;
			gorilla.shakeIntensity = 2f;
			gorilla.shakeIntensityChange = -0.01f;
			if (Game1.currentLocation == this)
			{
				Game1.playSound("grunt");
			}
			if (Game1.IsMasterGame)
			{
				SpawnBananaNutReward();
			}
			gorilla.timer = 0f;
			gorilla.motion = Vector2.Zero;
			gorilla.animationLength = 1;
			gorilla.endFunction = gorillaReturn;
			temporarySprites.Add(gorilla);
		}

		private void gorillaReturn(int extra)
		{
			TemporaryAnimatedSprite gorilla = getTemporarySpriteByID(777);
			gorilla.sourceRect = new Microsoft.Xna.Framework.Rectangle(32, 352, 32, 32);
			gorilla.sourceRectStartingPos = Utility.PointToVector2(gorilla.sourceRect.Location);
			gorilla.currentNumberOfLoops = 0;
			gorilla.totalNumberOfLoops = 6;
			gorilla.interval = 200f;
			gorilla.timer = 0f;
			gorilla.motion = new Vector2(0f, -3f);
			gorilla.animationLength = 2;
			gorilla.yStopCoordinate = 1280;
			gorilla.reachedStopCoordinate = delegate
			{
				removeTemporarySpritesWithID(777);
			};
			temporarySprites.Add(gorilla);
			if (Game1.currentLocation == this)
			{
				DelayedAction.functionAfterDelay(delegate
				{
					Game1.playMorningSong();
				}, 3000);
			}
		}
	}
}
