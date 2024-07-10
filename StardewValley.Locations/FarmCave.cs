using Microsoft.Xna.Framework;
using StardewValley.Extensions;

namespace StardewValley.Locations
{
	public class FarmCave : GameLocation
	{
		public FarmCave()
		{
		}

		public FarmCave(string map, string name)
			: base(map, name)
		{
		}

		protected override void resetLocalState()
		{
			base.resetLocalState();
			if ((int)Game1.MasterPlayer.caveChoice == 1)
			{
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(0f, 0f), false, 0f, Color.White)
				{
					interval = 3000f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					layerDepth = 1f,
					light = true,
					lightRadius = 0.5f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(8f, 0f), false, 0f, Color.White)
				{
					interval = 3000f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					layerDepth = 1f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(320f, -64f), false, 0f, Color.White)
				{
					interval = 2000f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 500,
					layerDepth = 1f,
					light = true,
					lightRadius = 0.5f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(328f, -64f), false, 0f, Color.White)
				{
					interval = 2000f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 500,
					layerDepth = 1f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(128f, map.Layers[0].LayerHeight * 64 - 64), false, 0f, Color.White)
				{
					interval = 1600f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 250,
					layerDepth = 1f,
					light = true,
					lightRadius = 0.5f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(136f, map.Layers[0].LayerHeight * 64 - 64), false, 0f, Color.White)
				{
					interval = 1600f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 250,
					layerDepth = 1f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2((map.Layers[0].LayerWidth + 1) * 64 + 4, 192f), false, 0f, Color.White)
				{
					interval = 2800f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 750,
					layerDepth = 1f,
					light = true,
					lightRadius = 0.5f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2((map.Layers[0].LayerWidth + 1) * 64 + 12, 192f), false, 0f, Color.White)
				{
					interval = 2800f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 750,
					layerDepth = 1f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2((map.Layers[0].LayerWidth + 1) * 64 + 4, 576f), false, 0f, Color.White)
				{
					interval = 2200f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 750,
					layerDepth = 1f,
					light = true,
					lightRadius = 0.5f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2((map.Layers[0].LayerWidth + 1) * 64 + 12, 576f), false, 0f, Color.White)
				{
					interval = 2200f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 750,
					layerDepth = 1f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(-60f, 128f), false, 0f, Color.White)
				{
					interval = 2600f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 750,
					layerDepth = 1f,
					light = true,
					lightRadius = 0.5f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(-52f, 128f), false, 0f, Color.White)
				{
					interval = 2600f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 750,
					layerDepth = 1f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(-64f, 384f), false, 0f, Color.White)
				{
					interval = 3400f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 650,
					layerDepth = 1f,
					light = true,
					lightRadius = 0.5f
				});
				temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(374, 358, 1, 1), new Vector2(-52f, 384f), false, 0f, Color.White)
				{
					interval = 3400f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					scale = 4f,
					delayBeforeAnimationStart = 650,
					layerDepth = 1f
				});
				Game1.ambientLight = new Color(70, 90, 0);
			}
		}

		public override void UpdateWhenCurrentLocation(GameTime time)
		{
			base.UpdateWhenCurrentLocation(time);
			if ((int)Game1.MasterPlayer.caveChoice == 1 && Game1.random.NextDouble() < 0.002 && Game1.currentLocation == this)
			{
				base.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Rectangle(640, 1664, 16, 16), 80f, 4, 9999, new Vector2(Game1.random.Next(map.Layers[0].LayerWidth), map.Layers[0].LayerHeight) * 64f, false, false, 1f, 0f, Color.Black, 4f, 0f, 0f, 0f)
				{
					xPeriodic = true,
					xPeriodicLoopTime = 2000f,
					xPeriodicRange = 64f,
					motion = new Vector2(0f, -8f)
				});
				if (Game1.random.NextDouble() < 0.15 && Game1.currentLocation == this)
				{
					localSound("batScreech");
				}
				for (int i = 1; i < 5; i++)
				{
					DelayedAction.playSoundAfterDelay("batFlap", 320 * i - 80);
				}
			}
			else if ((int)Game1.MasterPlayer.caveChoice == 1 && Game1.random.NextDouble() < 0.005)
			{
				temporarySprites.Add(new BatTemporarySprite(new Vector2((!Game1.random.NextBool()) ? (map.DisplayWidth - 64) : 0, map.DisplayHeight - 64)));
			}
		}

		/// <inheritdoc />
		public override void checkForMusic(GameTime time)
		{
		}

		public override void performTenMinuteUpdate(int timeOfDay)
		{
			if (Game1.currentLocation == this)
			{
				UpdateReadyFlag();
			}
			base.performTenMinuteUpdate(timeOfDay);
		}

		public override void cleanupBeforePlayerExit()
		{
			base.cleanupBeforePlayerExit();
			UpdateReadyFlag();
		}

		public override void DayUpdate(int dayOfMonth)
		{
			base.DayUpdate(dayOfMonth);
			if ((int)Game1.MasterPlayer.caveChoice == 1)
			{
				while (Game1.random.NextDouble() < 0.66)
				{
					string fruitId;
					switch (Game1.random.Next(5))
					{
					case 0:
						fruitId = "296";
						break;
					case 1:
						fruitId = "396";
						break;
					case 2:
						fruitId = "406";
						break;
					case 3:
						fruitId = "410";
						break;
					default:
						fruitId = ((Game1.random.NextDouble() < 0.1) ? "613" : Game1.random.Next(634, 639).ToString());
						break;
					}
					Vector2 v = new Vector2(Game1.random.Next(1, map.Layers[0].LayerWidth - 1), Game1.random.Next(1, map.Layers[0].LayerHeight - 4));
					Object fruit = ItemRegistry.Create<Object>("(O)" + fruitId);
					fruit.IsSpawnedObject = true;
					if (CanItemBePlacedHere(v))
					{
						setObject(v, fruit);
					}
				}
			}
			UpdateReadyFlag();
		}

		public virtual void UpdateReadyFlag()
		{
			bool flag_value = false;
			foreach (Object o in objects.Values)
			{
				if ((bool)o.isSpawnedObject)
				{
					flag_value = true;
					break;
				}
				if ((bool)o.bigCraftable && o.heldObject.Value != null && o.MinutesUntilReady <= 0 && o.QualifiedItemId == "(BC)128")
				{
					flag_value = true;
					break;
				}
			}
			Game1.getFarm().farmCaveReady.Value = flag_value;
		}

		public void setUpMushroomHouse()
		{
			int[] array = new int[2] { 5, 7 };
			foreach (int y in array)
			{
				int[] array2 = new int[3] { 4, 6, 8 };
				foreach (int x in array2)
				{
					Object mushroomBox = ItemRegistry.Create<Object>("(BC)128");
					mushroomBox.fragility.Value = 2;
					setObject(new Vector2(x, y), mushroomBox);
				}
			}
			setObject(new Vector2(10f, 5f), ItemRegistry.Create<Object>("(BC)Dehydrator"));
		}
	}
}
