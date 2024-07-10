using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Netcode;
using StardewValley.Enchantments;
using StardewValley.Tools;

namespace StardewValley.Monsters
{
	public class Mummy : Monster
	{
		public NetInt reviveTimer = new NetInt(0);

		public const int revivalTime = 10000;

		protected int _damageToFarmer;

		private readonly NetEvent1Field<bool, NetBool> crumbleEvent = new NetEvent1Field<bool, NetBool>();

		public Mummy()
		{
		}

		public Mummy(Vector2 position)
			: base("Mummy", position)
		{
			Sprite.SpriteHeight = 32;
			Sprite.ignoreStopAnimation = true;
			Sprite.UpdateSourceRect();
			_damageToFarmer = damageToFarmer.Value;
		}

		protected override void initNetFields()
		{
			base.initNetFields();
			base.NetFields.AddField(crumbleEvent, "crumbleEvent").AddField(reviveTimer, "reviveTimer");
			crumbleEvent.onEvent += performCrumble;
			position.Field.AxisAlignedMovement = true;
		}

		/// <inheritdoc />
		public override void reloadSprite(bool onlyAppearance = false)
		{
			Sprite = new AnimatedSprite("Characters\\Monsters\\Mummy");
			Sprite.SpriteHeight = 32;
			Sprite.UpdateSourceRect();
			Sprite.ignoreStopAnimation = true;
		}

		public override int takeDamage(int damage, int xTrajectory, int yTrajectory, bool isBomb, double addedPrecision, Farmer who)
		{
			int actualDamage = Math.Max(1, damage - (int)resilience);
			if ((int)reviveTimer > 0)
			{
				if (isBomb)
				{
					base.Health = 0;
					Utility.makeTemporarySpriteJuicier(new TemporaryAnimatedSprite(44, base.Position, Color.BlueViolet, 10)
					{
						holdLastFrame = true,
						alphaFade = 0.01f,
						interval = 70f
					}, base.currentLocation);
					base.currentLocation.playSound("ghost");
					return 999;
				}
				return -1;
			}
			if (Game1.random.NextDouble() < missChance.Value - missChance.Value * addedPrecision)
			{
				actualDamage = -1;
			}
			else
			{
				base.Slipperiness = 2;
				base.Health -= actualDamage;
				setTrajectory(xTrajectory, yTrajectory);
				base.currentLocation.playSound("shadowHit");
				base.currentLocation.playSound("skeletonStep");
				base.IsWalkingTowardPlayer = true;
				if (base.Health <= 0)
				{
					if (!isBomb)
					{
						MeleeWeapon weapon = who.CurrentTool as MeleeWeapon;
						if (weapon != null && weapon.hasEnchantmentOfType<CrusaderEnchantment>())
						{
							Utility.makeTemporarySpriteJuicier(new TemporaryAnimatedSprite(44, base.Position, Color.BlueViolet, 10)
							{
								holdLastFrame = true,
								alphaFade = 0.01f,
								interval = 70f
							}, base.currentLocation);
							base.currentLocation.playSound("ghost");
							goto IL_0207;
						}
					}
					reviveTimer.Value = 10000;
					base.Health = base.MaxHealth;
					deathAnimation();
				}
			}
			goto IL_0207;
			IL_0207:
			return actualDamage;
		}

		public override void defaultMovementBehavior(GameTime time)
		{
			if ((int)reviveTimer <= 0)
			{
				base.defaultMovementBehavior(time);
			}
		}

		public override List<Item> getExtraDropItems()
		{
			List<Item> items = new List<Item>();
			if (Game1.random.NextDouble() < 0.002)
			{
				items.Add(ItemRegistry.Create("(O)485"));
			}
			return items;
		}

		protected override void sharedDeathAnimation()
		{
			Halt();
			crumble();
			collidesWithOtherCharacters.Value = false;
			base.IsWalkingTowardPlayer = false;
			moveTowardPlayerThreshold.Value = -1;
		}

		protected override void localDeathAnimation()
		{
		}

		public override void update(GameTime time, GameLocation location)
		{
			crumbleEvent.Poll();
			if ((int)reviveTimer > 0 && Sprite.CurrentAnimation == null && Sprite.currentFrame != 19)
			{
				Sprite.currentFrame = 19;
			}
			base.update(time, location);
		}

		private void crumble(bool reverse = false)
		{
			crumbleEvent.Fire(reverse);
		}

		private void performCrumble(bool reverse)
		{
			Sprite.setCurrentAnimation(getCrumbleAnimation(reverse));
			if (!reverse)
			{
				if (Game1.IsMasterGame)
				{
					damageToFarmer.Value = 0;
				}
				reviveTimer.Value = 10000;
				base.currentLocation.localSound("monsterdead");
			}
			else
			{
				if (Game1.IsMasterGame)
				{
					damageToFarmer.Value = _damageToFarmer;
				}
				reviveTimer.Value = 0;
				base.currentLocation.localSound("skeletonDie");
			}
		}

		private List<FarmerSprite.AnimationFrame> getCrumbleAnimation(bool reverse = false)
		{
			List<FarmerSprite.AnimationFrame> animation = new List<FarmerSprite.AnimationFrame>();
			if (!reverse)
			{
				animation.Add(new FarmerSprite.AnimationFrame(16, 100, 0, false, false));
			}
			else
			{
				animation.Add(new FarmerSprite.AnimationFrame(16, 100, 0, false, false, behaviorAfterRevival, true));
			}
			animation.Add(new FarmerSprite.AnimationFrame(17, 100, 0, false, false));
			animation.Add(new FarmerSprite.AnimationFrame(18, 100, 0, false, false));
			if (!reverse)
			{
				animation.Add(new FarmerSprite.AnimationFrame(19, 100, 0, false, false, behaviorAfterCrumble));
			}
			else
			{
				animation.Add(new FarmerSprite.AnimationFrame(19, 100, 0, false, false));
			}
			if (reverse)
			{
				animation.Reverse();
			}
			return animation;
		}

		public override void behaviorAtGameTick(GameTime time)
		{
			if ((int)reviveTimer <= 0 && withinPlayerThreshold())
			{
				base.IsWalkingTowardPlayer = true;
			}
			base.behaviorAtGameTick(time);
		}

		protected override void updateAnimation(GameTime time)
		{
			if (Sprite.CurrentAnimation != null)
			{
				if (Sprite.animateOnce(time))
				{
					Sprite.CurrentAnimation = null;
				}
			}
			else if ((int)reviveTimer > 0)
			{
				reviveTimer.Value -= time.ElapsedGameTime.Milliseconds;
				if ((int)reviveTimer < 2000)
				{
					shake(reviveTimer);
				}
				if ((int)reviveTimer <= 0)
				{
					if (Game1.IsMasterGame)
					{
						crumble(true);
						base.IsWalkingTowardPlayer = true;
					}
					else
					{
						reviveTimer.Value = 1;
					}
				}
			}
			else if (!Game1.IsMasterGame)
			{
				if (isMoving())
				{
					switch (FacingDirection)
					{
					case 0:
						Sprite.AnimateUp(time);
						break;
					case 3:
						Sprite.AnimateLeft(time);
						break;
					case 1:
						Sprite.AnimateRight(time);
						break;
					case 2:
						Sprite.AnimateDown(time);
						break;
					}
				}
				else
				{
					Sprite.StopAnimation();
				}
			}
			resetAnimationSpeed();
		}

		private void behaviorAfterCrumble(Farmer who)
		{
			Halt();
			Sprite.currentFrame = 19;
			Sprite.CurrentAnimation = null;
		}

		private void behaviorAfterRevival(Farmer who)
		{
			base.IsWalkingTowardPlayer = true;
			collidesWithOtherCharacters.Value = true;
			Sprite.currentFrame = 0;
			Sprite.oldFrame = 0;
			moveTowardPlayerThreshold.Value = 8;
			Sprite.CurrentAnimation = null;
		}
	}
}
