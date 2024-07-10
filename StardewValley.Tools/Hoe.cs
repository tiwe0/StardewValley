using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewValley.Extensions;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;
using xTile.Dimensions;

namespace StardewValley.Tools
{
	public class Hoe : Tool
	{
		public Hoe()
			: base("Hoe", 0, 21, 47, false)
		{
		}

		/// <inheritdoc />
		protected override void MigrateLegacyItemId()
		{
			switch (base.UpgradeLevel)
			{
			case 0:
				base.ItemId = "Hoe";
				break;
			case 1:
				base.ItemId = "CopperHoe";
				break;
			case 2:
				base.ItemId = "SteelHoe";
				break;
			case 3:
				base.ItemId = "GoldHoe";
				break;
			case 4:
				base.ItemId = "IridiumHoe";
				break;
			default:
				base.ItemId = "Hoe";
				break;
			}
		}

		/// <inheritdoc />
		protected override Item GetOneNew()
		{
			return new Hoe();
		}

		public override void DoFunction(GameLocation location, int x, int y, int power, Farmer who)
		{
			Vector2 initialTile = new Vector2(x / 64, y / 64);
			base.DoFunction(location, x, y, power, who);
			int level;
			if (MineShaft.IsGeneratedLevel(location, out level))
			{
				power = 1;
			}
			if (!isEfficient)
			{
				who.Stamina -= (float)(2 * power) - (float)who.FarmingLevel * 0.1f;
			}
			power = who.toolPower;
			who.stopJittering();
			location.playSound("woodyHit", initialTile);
			List<Vector2> tileLocations = tilesAffected(initialTile, power, who);
			foreach (Vector2 tileLocation in tileLocations)
			{
				TerrainFeature terrainFeature;
				if (location.terrainFeatures.TryGetValue(tileLocation, out terrainFeature))
				{
					if (terrainFeature.performToolAction(this, 0, tileLocation))
					{
						location.terrainFeatures.Remove(tileLocation);
					}
					continue;
				}
				Object obj;
				if (location.objects.TryGetValue(tileLocation, out obj) && obj.performToolAction(this))
				{
					if (obj.Type == "Crafting" && (int)obj.fragility != 2)
					{
						location.debris.Add(new Debris(obj.QualifiedItemId, who.GetToolLocation(), Utility.PointToVector2(who.StandingPixel)));
					}
					obj.performRemoveAction();
					location.Objects.Remove(tileLocation);
				}
				if (location.doesTileHaveProperty((int)tileLocation.X, (int)tileLocation.Y, "Diggable", "Back") == null)
				{
					continue;
				}
				MineShaft mine = location as MineShaft;
				if (mine != null && !location.IsTileOccupiedBy(tileLocation, CollisionMask.All, CollisionMask.None, true))
				{
					if (mine.getMineArea() != 77377)
					{
						location.makeHoeDirt(tileLocation);
						location.playSound("hoeHit", tileLocation);
						location.checkForBuriedItem((int)tileLocation.X, (int)tileLocation.Y, false, false, who);
						Game1.multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(12, new Vector2(initialTile.X * 64f, initialTile.Y * 64f), Color.White, 8, Game1.random.NextBool(), 50f));
						if (tileLocations.Count > 2)
						{
							Game1.multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(6, new Vector2(tileLocation.X * 64f, tileLocation.Y * 64f), Color.White, 8, Game1.random.NextBool(), Vector2.Distance(initialTile, tileLocation) * 30f));
						}
					}
				}
				else if (!location.IsTileOccupiedBy(tileLocation, ~(CollisionMask.Characters | CollisionMask.Farmers)) && location.isTilePassable(new Location((int)tileLocation.X, (int)tileLocation.Y), Game1.viewport))
				{
					location.makeHoeDirt(tileLocation);
					location.playSound("hoeHit", tileLocation);
					Game1.multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(12, new Vector2(tileLocation.X * 64f, tileLocation.Y * 64f), Color.White, 8, Game1.random.NextBool(), 50f));
					if (tileLocations.Count > 2)
					{
						Game1.multiplayer.broadcastSprites(location, new TemporaryAnimatedSprite(6, new Vector2(tileLocation.X * 64f, tileLocation.Y * 64f), Color.White, 8, Game1.random.NextBool(), Vector2.Distance(initialTile, tileLocation) * 30f));
					}
					location.checkForBuriedItem((int)tileLocation.X, (int)tileLocation.Y, false, false, who);
				}
				Game1.stats.DirtHoed++;
			}
		}
	}
}
