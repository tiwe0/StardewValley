using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewValley.BellsAndWhistles;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Delegates;
using StardewValley.Extensions;
using StardewValley.GameData;
using StardewValley.GameData.Characters;
using StardewValley.GameData.Movies;
using StardewValley.GameData.Pets;
using StardewValley.GameData.Shops;
using StardewValley.Internal;
using StardewValley.Locations;
using StardewValley.Logging;
using StardewValley.Menus;
using StardewValley.Minigames;
using StardewValley.Monsters;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.SpecialOrders;
using StardewValley.TerrainFeatures;
using StardewValley.TokenizableStrings;
using StardewValley.Tools;
using StardewValley.Triggers;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace StardewValley
{
	public class Event
	{
		/// <summary>The low-level event commands defined by the base game. Most code should use <see cref="T:StardewValley.Event" /> methods instead.</summary>
		/// <remarks>Every method within this class is an event command whose name matches the method name. All event commands must be static, public, and match <see cref="T:StardewValley.Delegates.EventCommandDelegate" />.</remarks>
		public static class DefaultCommands
		{
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void IgnoreEventTileOffset(Event @event, string[] args, EventContext context)
			{
				@event.ignoreTileOffsets = true;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Move(Event @event, string[] args, EventContext context)
			{
				bool? continueAfterMove = null;
				int fieldsAfterMoves = (args.Length - 1) % 4;
				if (fieldsAfterMoves == 1)
				{
					bool rawValue;
					string error2;
					if (!ArgUtility.TryGetOptionalBool(args, args.Length - 1, out rawValue, out error2))
					{
						context.LogErrorAndSkip(error2);
						return;
					}
					continueAfterMove = rawValue;
				}
				else if (fieldsAfterMoves > 1)
				{
					context.LogErrorAndSkip("invalid number of arguments, expected sets of [actor x y direction] fields plus an optional continue-after-move boolean field");
					return;
				}
				if (!continueAfterMove.HasValue || args.Length > 2)
				{
					for (int i = 1; i < args.Length && ArgUtility.HasIndex(args, i + 3); i += 4)
					{
						string actorName;
						string error;
						Point tile;
						int facingDirection;
						int farmerNumber;
						if (!ArgUtility.TryGet(args, i, out actorName, out error) || !ArgUtility.TryGetPoint(args, i + 1, out tile, out error) || !ArgUtility.TryGetDirection(args, i + 3, out facingDirection, out error))
						{
							context.LogError(error);
						}
						else if (@event.IsFarmerActorId(actorName, out farmerNumber))
						{
							if (!@event.actorPositionsAfterMove.ContainsKey(actorName))
							{
								Farmer farmer = @event.GetFarmerActor(farmerNumber);
								if (farmer != null)
								{
									farmer.canOnlyWalk = false;
									farmer.setRunning(false, true);
									farmer.canOnlyWalk = true;
									farmer.convertEventMotionCommandToMovement(Utility.PointToVector2(tile));
									@event.actorPositionsAfterMove.Add(actorName, @event.getPositionAfterMove(farmer, tile.X, tile.Y, facingDirection));
								}
							}
						}
						else
						{
							bool isOptionalNpc;
							NPC j = @event.getActorByName(actorName, out isOptionalNpc);
							if (j == null)
							{
								context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
								return;
							}
							if (!@event.actorPositionsAfterMove.ContainsKey(actorName))
							{
								j.convertEventMotionCommandToMovement(Utility.PointToVector2(tile));
								@event.actorPositionsAfterMove.Add(actorName, @event.getPositionAfterMove(j, tile.X, tile.Y, facingDirection));
							}
						}
					}
				}
				if (!continueAfterMove.HasValue)
				{
					return;
				}
				if (continueAfterMove.GetValueOrDefault())
				{
					@event.continueAfterMove = true;
					@event.CurrentCommand++;
					return;
				}
				@event.continueAfterMove = false;
				if (args.Length == 2 && @event.actorPositionsAfterMove.Count == 0)
				{
					@event.CurrentCommand++;
				}
			}

			/// <summary>Run an action string.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Action(Event @event, string[] args, EventContext context)
			{
				string action;
				string error;
				Exception ex;
				if (!ArgUtility.TryGetRemainder(args, 1, out action, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else if (!TriggerActionManager.TryRunAction(action, out error, out ex))
				{
					if (ex != null)
					{
						string text = error;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 1);
						defaultInterpolatedStringHandler.AppendLiteral("\n");
						defaultInterpolatedStringHandler.AppendFormatted(ex);
						error = text + defaultInterpolatedStringHandler.ToStringAndClear();
					}
					context.LogErrorAndSkip(error);
				}
				else
				{
					@event.CurrentCommand++;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Speak(Event @event, string[] args, EventContext context)
			{
				if (@event.skipped)
				{
					return;
				}
				string actorName;
				string error;
				string textOrTranslationKey;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGet(args, 2, out textOrTranslationKey, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else
				{
					if (Game1.dialogueUp)
					{
						return;
					}
					@event.timeAccumulator += context.Time.ElapsedGameTime.Milliseconds;
					if (@event.timeAccumulator < 500f)
					{
						return;
					}
					@event.timeAccumulator = 0f;
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc) ?? Game1.getCharacterFromName(actorName);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						Game1.eventFinished();
						return;
					}
					Game1.player.checkForQuestComplete(i, -1, -1, null, null, 5);
					if (Game1.NPCGiftTastes.ContainsKey(actorName) && !Game1.player.friendshipData.ContainsKey(actorName))
					{
						Game1.player.friendshipData.Add(actorName, new Friendship(0));
					}
					Dialogue dialogue = (Game1.content.IsValidTranslationKey(textOrTranslationKey) ? new Dialogue(i, textOrTranslationKey) : new Dialogue(i, null, textOrTranslationKey));
					i.CurrentDialogue.Push(dialogue);
					Game1.drawDialogue(i);
				}
			}

			/// <summary>Try to execute all commands in one tick until <see cref="M:StardewValley.Event.DefaultCommands.EndSimultaneousCommand(StardewValley.Event,System.String[],StardewValley.EventContext)" /> is called.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void BeginSimultaneousCommand(Event @event, string[] args, EventContext context)
			{
				@event.simultaneousCommand = true;
				@event.CurrentCommand++;
			}

			/// <summary>If commands are being executed in one tick due to <see cref="M:StardewValley.Event.DefaultCommands.BeginSimultaneousCommand(StardewValley.Event,System.String[],StardewValley.EventContext)" />, stop doing so for the remaining commands.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void EndSimultaneousCommand(Event @event, string[] args, EventContext context)
			{
				@event.simultaneousCommand = false;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void MineDeath(Event @event, string[] args, EventContext context)
			{
				if (!Game1.dialogueUp)
				{
					Random r = Utility.CreateDaySaveRandom(Game1.timeOfDay);
					int moneyToLose = r.Next(Game1.player.Money / 40, Game1.player.Money / 8);
					moneyToLose = Math.Min(moneyToLose, 15000);
					moneyToLose -= (int)((double)Game1.player.LuckLevel * 0.01 * (double)moneyToLose);
					moneyToLose -= moneyToLose % 100;
					int numberOfItemsLost = Game1.player.LoseItemsOnDeath(r);
					Game1.player.Stamina = Math.Min(Game1.player.Stamina, 2f);
					Game1.player.Money = Math.Max(0, Game1.player.Money - moneyToLose);
					Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1057") + " " + ((moneyToLose <= 0) ? "" : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1058", moneyToLose)) + ((numberOfItemsLost <= 0) ? ((moneyToLose <= 0) ? "" : ".") : ((moneyToLose <= 0) ? (Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1060") + ((numberOfItemsLost == 1) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1061") : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1062", numberOfItemsLost))) : (Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1063") + ((numberOfItemsLost == 1) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1061") : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1062", numberOfItemsLost))))));
					@event.InsertNextCommand("showItemsLost");
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void HospitalDeath(Event @event, string[] args, EventContext context)
			{
				if (!Game1.dialogueUp)
				{
					int numberOfItemsLost = Game1.player.LoseItemsOnDeath();
					Game1.player.Stamina = Math.Min(Game1.player.Stamina, 2f);
					int moneyToLose = Math.Min(1000, Game1.player.Money);
					Game1.player.Money -= moneyToLose;
					Game1.drawObjectDialogue(((moneyToLose > 0) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1068", moneyToLose) : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1070")) + ((numberOfItemsLost > 0) ? (Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1071") + ((numberOfItemsLost == 1) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1061") : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1062", numberOfItemsLost))) : ""));
					@event.InsertNextCommand("showItemsLost");
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ShowItemsLost(Event @event, string[] args, EventContext context)
			{
				if (Game1.activeClickableMenu == null)
				{
					Game1.activeClickableMenu = new ItemListMenu(Game1.content.LoadString("Strings\\UI:ItemList_ItemsLost"), Game1.player.itemsLostLastDeath.ToList());
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void End(Event @event, string[] args, EventContext context)
			{
				@event.endBehaviors(args, context.Location);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void LocationSpecificCommand(Event @event, string[] args, EventContext context)
			{
				string command;
				string error;
				if (!ArgUtility.TryGet(args, 1, out command, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				string[] commandArgs = args.Skip(2).ToArray();
				if (context.Location.RunLocationSpecificEventCommand(@event, command, !@event._repeatingLocationSpecificCommand, commandArgs))
				{
					@event._repeatingLocationSpecificCommand = false;
					@event.CurrentCommand++;
				}
				else
				{
					@event._repeatingLocationSpecificCommand = true;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Unskippable(Event @event, string[] args, EventContext context)
			{
				@event.skippable = false;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Skippable(Event @event, string[] args, EventContext context)
			{
				@event.skippable = true;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void SetSkipActions(Event @event, string[] args, EventContext context)
			{
				string skipActions;
				string error;
				if (!ArgUtility.TryGetRemainder(args, 1, out skipActions, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (string.IsNullOrWhiteSpace(skipActions))
				{
					@event.actionsOnSkip = null;
				}
				else
				{
					string[] actions = LegacyShims.SplitAndTrim(skipActions, '#');
					string[] array = actions;
					for (int i = 0; i < array.Length; i++)
					{
						if (!TriggerActionManager.TryValidateActionExists(array[i], out error))
						{
							context.LogErrorAndSkip(error);
							return;
						}
					}
					@event.actionsOnSkip = actions;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Emote(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				int emoteId;
				bool nextCommandImmediate;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetInt(args, 2, out emoteId, out error) || !ArgUtility.TryGetOptionalBool(args, 3, out nextCommandImmediate, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					@event.GetFarmerActor(farmerNumber)?.doEmote(emoteId, !nextCommandImmediate);
				}
				else
				{
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					if (!i.isEmoting)
					{
						i.doEmote(emoteId, !nextCommandImmediate);
					}
				}
				if (nextCommandImmediate)
				{
					@event.CurrentCommand++;
					@event.Update(context.Location, context.Time);
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StopMusic(Event @event, string[] args, EventContext context)
			{
				Game1.changeMusicTrack("none", false, MusicContext.Event);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void PlayPetSound(Event @event, string[] args, EventContext context)
			{
				string sound;
				string error;
				if (!ArgUtility.TryGet(args, 1, out sound, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Pet pet = null;
				foreach (NPC actor in @event.actors)
				{
					if (actor is Pet)
					{
						pet = actor as Pet;
						break;
					}
				}
				if (pet == null)
				{
					pet = Game1.player.getPet();
				}
				float pitch = 1200f;
				if (pet != null)
				{
					PetData pet_data = pet.GetPetData();
					PetBreed breed = pet_data?.GetBreedById(pet.whichBreed.Value);
					if (breed != null)
					{
						pitch *= breed.VoicePitch;
						if (sound == pet_data.BarkSound && breed.BarkOverride != null)
						{
							sound = breed.BarkOverride;
						}
					}
				}
				Game1.playSound(sound, (int)pitch);
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void PlaySound(Event @event, string[] args, EventContext context)
			{
				string soundId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out soundId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				ICue sound;
				Game1.playSound(soundId, out sound);
				@event.TrackSound(sound);
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StopSound(Event @event, string[] args, EventContext context)
			{
				string soundId;
				string error;
				bool immediate;
				if (!ArgUtility.TryGet(args, 1, out soundId, out error) || !ArgUtility.TryGetOptionalBool(args, 2, out immediate, out error, true))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				@event.StopTrackedSound(soundId, immediate);
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void TossConcession(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string concessionId;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGet(args, 2, out concessionId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
				if (actor == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				MovieConcession concession = MovieTheater.GetConcessionItem(concessionId);
				if (concession == null)
				{
					context.LogErrorAndSkip("no concession found with ID '" + concessionId + "'");
					return;
				}
				Texture2D texture = concession.GetTexture();
				int spriteIndex = concession.GetSpriteIndex();
				Game1.playSound("dwop");
				context.Location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = texture,
					sourceRect = Game1.getSourceRectForStandardTileSheet(texture, spriteIndex, 16, 16),
					animationLength = 1,
					totalNumberOfLoops = 1,
					motion = new Vector2(0f, -6f),
					acceleration = new Vector2(0f, 0.2f),
					interval = 1000f,
					scale = 4f,
					position = @event.OffsetPosition(new Vector2(actor.Position.X, actor.Position.Y - 96f)),
					layerDepth = (float)actor.StandingPixel.Y / 10000f
				});
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Pause(Event @event, string[] args, EventContext context)
			{
				int pauseTime;
				string error;
				if (!ArgUtility.TryGetInt(args, 1, out pauseTime, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else if (Game1.pauseTime <= 0f)
				{
					Game1.pauseTime = pauseTime;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void PrecisePause(Event @event, string[] args, EventContext context)
			{
				int pauseTime;
				string error;
				if (!ArgUtility.TryGetInt(args, 1, out pauseTime, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (@event.stopWatch == null)
				{
					@event.stopWatch = new Stopwatch();
				}
				if (!@event.stopWatch.IsRunning)
				{
					@event.stopWatch.Start();
				}
				if (@event.stopWatch.ElapsedMilliseconds >= pauseTime)
				{
					@event.stopWatch.Stop();
					@event.stopWatch.Reset();
					@event.CurrentCommand++;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ResetVariable(Event @event, string[] args, EventContext context)
			{
				@event.specialEventVariable1 = false;
				@event.currentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void FaceDirection(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				int faceDirection;
				bool continueImmediate;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetDirection(args, 2, out faceDirection, out error) || !ArgUtility.TryGetOptionalBool(args, 3, out continueImmediate, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					Farmer f = @event.GetFarmerActor(farmerNumber);
					if (f != null)
					{
						f.FarmerSprite.StopAnimation();
						f.completelyStopAnimatingOrDoingAction();
						f.faceDirection(faceDirection);
					}
				}
				else if (actorName.Contains("spouse"))
				{
					NPC spouse = @event.getActorByName(Game1.player.spouse);
					if (spouse != null && !Game1.player.hasRoommate())
					{
						spouse.faceDirection(faceDirection);
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					i.faceDirection(faceDirection);
				}
				if (continueImmediate)
				{
					@event.CurrentCommand++;
					@event.Update(context.Location, context.Time);
				}
				else if (Game1.pauseTime <= 0f)
				{
					Game1.pauseTime = 500f;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Warp(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				Vector2 tile;
				bool continueImmediate;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetVector2(args, 2, out tile, out error, true) || !ArgUtility.TryGetOptionalBool(args, 4, out continueImmediate, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					Farmer f = @event.GetFarmerActor(farmerNumber);
					if (f != null)
					{
						f.setTileLocation(@event.OffsetTile(tile));
						f.position.Y -= 16f;
						if (@event.farmerActors.Contains(f))
						{
							f.completelyStopAnimatingOrDoingAction();
						}
					}
				}
				else if (actorName.Contains("spouse"))
				{
					NPC spouse = @event.getActorByName(Game1.player.spouse);
					if (spouse != null && !Game1.player.hasRoommate())
					{
						if (@event.npcControllers != null)
						{
							for (int i = @event.npcControllers.Count - 1; i >= 0; i--)
							{
								if (@event.npcControllers[i].puppet.Name == Game1.player.spouse)
								{
									@event.npcControllers.RemoveAt(i);
								}
							}
						}
						spouse.Position = @event.OffsetPosition(tile * 64f);
						spouse.IsWalkingInSquare = false;
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC j = @event.getActorByName(actorName, out isOptionalNpc);
					if (j == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					j.position.X = @event.OffsetPositionX(tile.X * 64f + 4f);
					j.position.Y = @event.OffsetPositionY(tile.Y * 64f);
					j.IsWalkingInSquare = false;
				}
				@event.CurrentCommand++;
				if (continueImmediate)
				{
					@event.Update(context.Location, context.Time);
				}
			}

			/// <summary>Change the event position for all connected farmers.</summary>
			/// <remarks>This expects at least four fields:
			///   1. zero or more [x y direction] triplets (one per possible farmer);
			///   2. an offset direction (up/down/left/right), which sets where each subsequent farmer is set when using the default triplet;
			///   3. and a default [x y direction] triplet which applies to any unlisted farmer.</remarks>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void WarpFarmers(Event @event, string[] args, EventContext context)
			{
				int nonWarpFields = (args.Length - 1) % 3;
				if (args.Length < 5 || nonWarpFields != 1)
				{
					context.LogErrorAndSkip("invalid number of arguments; expected zero or more [x y direction] triplets, one offset direction (up/down/left/right), and one triplet which applies to any other farmer");
					return;
				}
				int defaultsIndex = args.Length - 4;
				int offsetDirection;
				string error;
				Point defaultPosition;
				int defaultFacingDirection;
				if (!ArgUtility.TryGetDirection(args, defaultsIndex, out offsetDirection, out error) || !ArgUtility.TryGetPoint(args, defaultsIndex + 1, out defaultPosition, out error) || !ArgUtility.TryGetDirection(args, defaultsIndex + 3, out defaultFacingDirection, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				List<Vector3> positions = new List<Vector3>();
				for (int j = 1; j < defaultsIndex; j += 3)
				{
					Point position;
					int facingDirection;
					if (!ArgUtility.TryGetPoint(args, j, out position, out error) || !ArgUtility.TryGetDirection(args, j + 2, out facingDirection, out error))
					{
						context.LogErrorAndSkip(error);
						return;
					}
					positions.Add(new Vector3(position.X, position.Y, facingDirection));
				}
				Point offset;
				switch (offsetDirection)
				{
				case 3:
					offset = new Point(-1, 0);
					break;
				case 1:
					offset = new Point(1, 0);
					break;
				case 0:
					offset = new Point(0, -1);
					break;
				case 2:
					offset = new Point(0, 1);
					break;
				default:
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(76, 1);
					defaultInterpolatedStringHandler.AppendLiteral("invalid offset direction '");
					defaultInterpolatedStringHandler.AppendFormatted(offsetDirection);
					defaultInterpolatedStringHandler.AppendLiteral("'; must be one of 'left', 'right', 'up', or 'down'");
					context.LogErrorAndSkip(defaultInterpolatedStringHandler.ToStringAndClear());
					return;
				}
				}
				int currentX = defaultPosition.X;
				int currentY = defaultPosition.Y;
				for (int i = 0; i < Game1.numberOfPlayers(); i++)
				{
					Farmer farmer = @event.GetFarmerActor(i + 1);
					float x;
					float y;
					int direction;
					if (i < positions.Count)
					{
						x = positions[i].X;
						y = positions[i].Y;
						direction = (int)positions[i].Z;
					}
					else
					{
						x = currentX;
						y = currentY;
						direction = defaultFacingDirection;
						currentX += offset.X;
						currentY += offset.Y;
						if (context.Location.map.GetLayer("Buildings")?.Tiles[currentX, currentY] != null && offset != Point.Zero)
						{
							currentX -= offset.X;
							currentY -= offset.Y;
							offset = Point.Zero;
						}
					}
					if (farmer != null)
					{
						farmer.setTileLocation(@event.OffsetTile(new Vector2(x, y)));
						farmer.faceDirection(direction);
						farmer.position.Y -= 16f;
						farmer.completelyStopAnimatingOrDoingAction();
					}
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Speed(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				int speed;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetInt(args, 2, out speed, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					if (@event.IsCurrentFarmerActorId(farmerNumber))
					{
						@event.farmerAddedSpeed = speed;
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
					if (actor == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					actor.speed = speed;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StopAdvancedMoves(Event @event, string[] args, EventContext context)
			{
				string option = ArgUtility.Get(args, 1);
				if (option != null)
				{
					if (!(option == "next"))
					{
						context.LogErrorAndSkip("unknown option " + option + ", must be 'next' or omitted");
						return;
					}
					foreach (NPCController npcController in @event.npcControllers)
					{
						npcController.destroyAtNextCrossroad();
					}
				}
				else
				{
					@event.npcControllers.Clear();
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void DoAction(Event @event, string[] args, EventContext context)
			{
				Point tile;
				string error;
				if (!ArgUtility.TryGetPoint(args, 1, out tile, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Location tileLocation = new Location(@event.OffsetTileX(tile.X), @event.OffsetTileY(tile.Y));
				Game1.hooks.OnGameLocation_CheckAction(context.Location, tileLocation, Game1.viewport, @event.farmer, () => context.Location.checkAction(tileLocation, Game1.viewport, @event.farmer));
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RemoveTile(Event @event, string[] args, EventContext context)
			{
				Point tile;
				string error;
				string layerId;
				if (!ArgUtility.TryGetPoint(args, 1, out tile, out error) || !ArgUtility.TryGet(args, 3, out layerId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				context.Location.removeTile(@event.OffsetTileX(tile.X), @event.OffsetTileY(tile.Y), layerId);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void TextAboveHead(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string text;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGet(args, 2, out text, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC i = @event.getActorByName(actorName, out isOptionalNpc);
				if (i == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				i.showTextAboveHead(text);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ShowFrame(Event @event, string[] args, EventContext context)
			{
				bool flip = false;
				string actorName;
				int frame;
				string error;
				if (args.Length == 2)
				{
					actorName = "farmer";
					string error2;
					if (!ArgUtility.TryGetInt(args, 1, out frame, out error2))
					{
						context.LogErrorAndSkip(error2);
						return;
					}
				}
				else if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetInt(args, 2, out frame, out error) || !ArgUtility.TryGetOptionalBool(args, 3, out flip, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (!@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					if (actorName == "spouse" && i.Gender == Gender.Male && frame >= 36 && frame <= 38)
					{
						frame += 12;
					}
					i.Sprite.CurrentFrame = frame;
				}
				else
				{
					Farmer f = @event.GetFarmerActor(farmerNumber);
					if (f != null)
					{
						f.FarmerSprite.setCurrentAnimation(new FarmerSprite.AnimationFrame[1]
						{
							new FarmerSprite.AnimationFrame(frame, 100, false, flip)
						});
						f.FarmerSprite.loop = true;
						f.FarmerSprite.loopThisAnimation = true;
						f.FarmerSprite.PauseForSingleAnimation = true;
						f.Sprite.currentFrame = frame;
					}
				}
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void FarmerAnimation(Event @event, string[] args, EventContext context)
			{
				int animationId;
				string error;
				if (!ArgUtility.TryGetInt(args, 1, out animationId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				@event.farmer.FarmerSprite.setCurrentSingleAnimation(animationId);
				@event.farmer.FarmerSprite.PauseForSingleAnimation = true;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void IgnoreMovementAnimation(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				bool ignore;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetOptionalBool(args, 2, out ignore, out error, true))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerId;
				if (@event.IsFarmerActorId(actorName, out farmerId))
				{
					Farmer f = @event.GetFarmerActor(farmerId);
					if (f != null)
					{
						f.ignoreMovementAnimation = ignore;
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc, true);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					i.ignoreMovementAnimation = ignore;
				}
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Animate(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				bool flip;
				bool loop;
				int frameDuration;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetBool(args, 2, out flip, out error) || !ArgUtility.TryGetBool(args, 3, out loop, out error) || !ArgUtility.TryGetInt(args, 4, out frameDuration, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				List<FarmerSprite.AnimationFrame> animationFrames = new List<FarmerSprite.AnimationFrame>();
				for (int i = 5; i < args.Length; i++)
				{
					int frame;
					if (!ArgUtility.TryGetInt(args, i, out frame, out error))
					{
						context.LogErrorAndSkip(error);
						return;
					}
					animationFrames.Add(new FarmerSprite.AnimationFrame(frame, frameDuration, false, flip));
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					Farmer f = @event.GetFarmerActor(farmerNumber);
					if (f != null)
					{
						f.FarmerSprite.setCurrentAnimation(animationFrames.ToArray());
						f.FarmerSprite.loop = true;
						f.FarmerSprite.loopThisAnimation = loop;
						f.FarmerSprite.PauseForSingleAnimation = true;
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC j = @event.getActorByName(actorName, out isOptionalNpc, true);
					if (j == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					j.Sprite.setCurrentAnimation(animationFrames);
					j.Sprite.loop = loop;
				}
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StopAnimation(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				int endFrame;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetOptionalInt(args, 2, out endFrame, out error, -1))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					Farmer f = @event.GetFarmerActor(farmerNumber);
					if (f != null)
					{
						f.completelyStopAnimatingOrDoingAction();
						f.Halt();
						f.FarmerSprite.CurrentAnimation = null;
						switch (f.FacingDirection)
						{
						case 0:
							f.FarmerSprite.setCurrentSingleFrame(12, 32000);
							break;
						case 1:
							f.FarmerSprite.setCurrentSingleFrame(6, 32000);
							break;
						case 2:
							f.FarmerSprite.setCurrentSingleFrame(0, 32000);
							break;
						case 3:
							f.FarmerSprite.setCurrentSingleFrame(6, 32000, false, true);
							break;
						}
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					i.Sprite.StopAnimation();
					if (endFrame > -1)
					{
						i.Sprite.currentFrame = endFrame;
						i.Sprite.UpdateSourceRect();
					}
				}
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ChangeLocation(Event @event, string[] args, EventContext context)
			{
				string locationName;
				string error;
				if (!ArgUtility.TryGet(args, 1, out locationName, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Point playerTile = @event.farmer.TilePoint;
				@event.changeLocation(locationName, playerTile.X, playerTile.Y, delegate
				{
					Game1.currentLocation.ResetForEvent(@event);
					@event.CurrentCommand++;
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Halt(Event @event, string[] args, EventContext context)
			{
				foreach (NPC actor in @event.actors)
				{
					actor.Halt();
				}
				@event.farmer.Halt();
				@event.CurrentCommand++;
				@event.continueAfterMove = false;
				@event.actorPositionsAfterMove.Clear();
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Message(Event @event, string[] args, EventContext context)
			{
				string dialogue;
				string error;
				if (!ArgUtility.TryGet(args, 1, out dialogue, out error))
				{
					context.LogError(error);
				}
				if (!Game1.dialogueUp && Game1.activeClickableMenu == null)
				{
					Game1.drawDialogueNoTyping((dialogue != null) ? Game1.parseText(dialogue) : "...");
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddCookingRecipe(Event @event, string[] args, EventContext context)
			{
				string recipeKey;
				string error;
				if (!ArgUtility.TryGetRemainder(args, 1, out recipeKey, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.cookingRecipes.TryAdd(recipeKey, 0);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ItemAboveHead(Event @event, string[] args, EventContext context)
			{
				string itemId = ArgUtility.Get(args, 1);
				switch (itemId?.ToLower())
				{
				case "pan":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(T)Pan"));
					break;
				case "hero":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(BC)116"));
					break;
				case "sculpture":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(F)1306"));
					break;
				case "samboombox":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(F)1309"));
					break;
				case "joja":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(BC)117"));
					break;
				case "slimeegg":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(O)680"));
					break;
				case "rod":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(T)BambooPole"));
					break;
				case "sword":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(W)0"));
					break;
				case "ore":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(O)334"), false);
					break;
				case "pot":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(BC)62"), false);
					break;
				case "jukebox":
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create("(BC)209"), false);
					break;
				case null:
					@event.farmer.holdUpItemThenMessage(null, false);
					break;
				default:
					@event.farmer.holdUpItemThenMessage(ItemRegistry.Create(itemId), false);
					break;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddCraftingRecipe(Event @event, string[] args, EventContext context)
			{
				string recipeKey;
				string error;
				if (!ArgUtility.TryGetRemainder(args, 1, out recipeKey, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.craftingRecipes.TryAdd(recipeKey, 0);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void HostMail(Event @event, string[] args, EventContext context)
			{
				string mailId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out mailId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (Game1.IsMasterGame && !Game1.player.hasOrWillReceiveMail(mailId))
				{
					Game1.addMailForTomorrow(mailId);
				}
				@event.CurrentCommand++;
			}

			/// <summary>Add a letter to the mailbox tomorrow.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Mail(Event @event, string[] args, EventContext context)
			{
				string mailId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out mailId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (!Game1.player.hasOrWillReceiveMail(mailId))
				{
					Game1.addMailForTomorrow(mailId);
				}
				@event.CurrentCommand++;
			}

			/// <summary>Add a letter to the mailbox immediately.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void MailToday(Event @event, string[] args, EventContext context)
			{
				string mailId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out mailId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (!Game1.player.hasOrWillReceiveMail(mailId))
				{
					Game1.addMail(mailId);
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Shake(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				int duration;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetInt(args, 2, out duration, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
				if (actor == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				actor.shake(duration);
				@event.CurrentCommand++;
			}

			/// <remarks>Main format: <c>temporaryAnimatedSprite texture rect_x rect_y rect_width rect_height animation_interval animation_length number_of_loops tile_x tile_y flicker flipped layer_depth alpha_fade scale scale_change rotation rotation_change</c>. This also supports a number of extended options (like <c>color Green</c>).</remarks>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void TemporaryAnimatedSprite(Event @event, string[] args, EventContext context)
			{
				string textureName;
				string error;
				Microsoft.Xna.Framework.Rectangle sourceRect;
				float animationInterval;
				int animationLength;
				int numberOfLoops;
				Vector2 tile;
				bool flicker;
				bool flip;
				float layerDepth;
				float alphaFade;
				int scale;
				float scaleChange;
				float rotation;
				float rotationChange;
				if (!ArgUtility.TryGet(args, 1, out textureName, out error) || !ArgUtility.TryGetRectangle(args, 2, out sourceRect, out error) || !ArgUtility.TryGetFloat(args, 6, out animationInterval, out error) || !ArgUtility.TryGetInt(args, 7, out animationLength, out error) || !ArgUtility.TryGetInt(args, 8, out numberOfLoops, out error) || !ArgUtility.TryGetVector2(args, 9, out tile, out error, true) || !ArgUtility.TryGetBool(args, 11, out flicker, out error) || !ArgUtility.TryGetBool(args, 12, out flip, out error) || !ArgUtility.TryGetFloat(args, 13, out layerDepth, out error) || !ArgUtility.TryGetFloat(args, 14, out alphaFade, out error) || !ArgUtility.TryGetInt(args, 15, out scale, out error) || !ArgUtility.TryGetFloat(args, 16, out scaleChange, out error) || !ArgUtility.TryGetFloat(args, 17, out rotation, out error) || !ArgUtility.TryGetFloat(args, 18, out rotationChange, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				TemporaryAnimatedSprite tempSprite = new TemporaryAnimatedSprite(textureName, sourceRect, animationInterval, animationLength, numberOfLoops, @event.OffsetPosition(tile * 64f), flicker, flip, @event.OffsetPosition(new Vector2(0f, layerDepth) * 64f).Y / 10000f, alphaFade, Color.White, 4 * scale, scaleChange, rotation, rotationChange);
				for (int i = 19; i < args.Length; i++)
				{
					switch (args[i])
					{
					case "color":
					{
						string rawColor;
						if (!ArgUtility.TryGet(args, i + 1, out rawColor, out error))
						{
							context.LogError(error);
							break;
						}
						Color? color = Utility.StringToColor(rawColor);
						if (color.HasValue)
						{
							tempSprite.color = color.Value;
						}
						else
						{
							DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(53, 2);
							defaultInterpolatedStringHandler.AppendLiteral("index ");
							defaultInterpolatedStringHandler.AppendFormatted(i + 1);
							defaultInterpolatedStringHandler.AppendLiteral(" has value '");
							defaultInterpolatedStringHandler.AppendFormatted(rawColor);
							defaultInterpolatedStringHandler.AppendLiteral("', which can't be parsed as a color");
							context.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
						}
						i++;
						break;
					}
					case "hold_last_frame":
						tempSprite.holdLastFrame = true;
						break;
					case "ping_pong":
						tempSprite.pingPong = true;
						break;
					case "motion":
					{
						Vector2 value;
						if (!ArgUtility.TryGetVector2(args, i + 1, out value, out error))
						{
							context.LogError(error);
							break;
						}
						tempSprite.motion = value;
						i += 2;
						break;
					}
					case "acceleration":
					{
						Vector2 value2;
						if (!ArgUtility.TryGetVector2(args, i + 1, out value2, out error))
						{
							context.LogError(error);
							break;
						}
						tempSprite.acceleration = value2;
						i += 2;
						break;
					}
					case "acceleration_change":
					{
						Vector2 value3;
						if (!ArgUtility.TryGetVector2(args, i + 1, out value3, out error))
						{
							context.LogError(error);
							break;
						}
						tempSprite.accelerationChange = value3;
						i += 2;
						break;
					}
					default:
						context.LogError("unknown option '" + args[i] + "'");
						break;
					}
				}
				context.Location.TemporarySprites.Add(tempSprite);
				@event.CurrentCommand++;
			}

			/// <remarks>Format: <c>temporarySprite xTile yTile rowInAnimationSheet animationLength animationInterval=300 flipped=false layerDepth=-1</c>.</remarks>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void TemporarySprite(Event @event, string[] args, EventContext context)
			{
				Vector2 tile;
				string error;
				int rowInAnimationSheet;
				int animationLength;
				float animationInterval;
				bool flipped;
				float layerDepth;
				if (!ArgUtility.TryGetVector2(args, 1, out tile, out error, true) || !ArgUtility.TryGetInt(args, 3, out rowInAnimationSheet, out error) || !ArgUtility.TryGetInt(args, 4, out animationLength, out error) || !ArgUtility.TryGetOptionalFloat(args, 5, out animationInterval, out error, 300f) || !ArgUtility.TryGetOptionalBool(args, 6, out flipped, out error) || !ArgUtility.TryGetOptionalFloat(args, 7, out layerDepth, out error, -1f))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				context.Location.TemporarySprites.Add(new TemporaryAnimatedSprite(rowInAnimationSheet, @event.OffsetPosition(tile * 64f), Color.White, animationLength, flipped, animationInterval, 0, 64, layerDepth));
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RemoveTemporarySprites(Event @event, string[] args, EventContext context)
			{
				context.Location.TemporarySprites.Clear();
				@event.CurrentCommand++;
			}

			/// <summary>A command that does nothing. Used just to wait for another event to finish.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Null(Event @event, string[] args, EventContext context)
			{
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void SpecificTemporarySprite(Event @event, string[] args, EventContext context)
			{
				string spriteId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out spriteId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				@event.addSpecificTemporarySprite(spriteId, context.Location, args);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void PlayMusic(Event @event, string[] args, EventContext context)
			{
				string musicId;
				string error;
				if (!ArgUtility.TryGetRemainder(args, 1, out musicId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (musicId == "samBand")
				{
					if (Game1.player.DialogueQuestionsAnswered.Contains("78"))
					{
						Game1.changeMusicTrack("shimmeringbastion", false, MusicContext.Event);
					}
					else if (Game1.player.DialogueQuestionsAnswered.Contains("79"))
					{
						Game1.changeMusicTrack("honkytonky", false, MusicContext.Event);
					}
					else if (Game1.player.DialogueQuestionsAnswered.Contains("77"))
					{
						Game1.changeMusicTrack("heavy", false, MusicContext.Event);
					}
					else
					{
						Game1.changeMusicTrack("poppy", false, MusicContext.Event);
					}
				}
				else if (Game1.options.musicVolumeLevel > 0f)
				{
					Game1.changeMusicTrack(musicId, false, MusicContext.Event);
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void MakeInvisible(Event @event, string[] args, EventContext context)
			{
				Point tile;
				string error;
				int width;
				int height;
				if (!ArgUtility.TryGetPoint(args, 1, out tile, out error) || !ArgUtility.TryGetOptionalInt(args, 3, out width, out error, 1) || !ArgUtility.TryGetOptionalInt(args, 4, out height, out error, 1))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				GameLocation location = context.Location;
				int originX = @event.OffsetTileX(tile.X);
				int originY = @event.OffsetTileY(tile.Y);
				for (int y = originY; y < originY + height; y++)
				{
					for (int x = originX; x < originX + width; x++)
					{
						Object o = location.getObjectAtTile(x, y);
						TerrainFeature terrainFeature;
						if (o != null)
						{
							BedFurniture bed = o as BedFurniture;
							if (bed != null && bed.GetBoundingBox().Contains(Utility.Vector2ToPoint(Game1.player.mostRecentBed)))
							{
								@event.CurrentCommand++;
								return;
							}
							o.isTemporarilyInvisible = true;
						}
						else if (location.terrainFeatures.TryGetValue(new Vector2(x, y), out terrainFeature))
						{
							terrainFeature.isTemporarilyInvisible = true;
						}
					}
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddObject(Event @event, string[] args, EventContext context)
			{
				Point tile;
				string error;
				string itemId;
				float layerDepth;
				if (!ArgUtility.TryGetPoint(args, 1, out tile, out error) || !ArgUtility.TryGet(args, 3, out itemId, out error) || !ArgUtility.TryGetOptionalFloat(args, 4, out layerDepth, out error, -1f))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Vector2 pixelPos = @event.OffsetPosition(new Vector2(tile.X * 64, tile.Y * 64));
				TemporaryAnimatedSprite sprite = new TemporaryAnimatedSprite(null, Microsoft.Xna.Framework.Rectangle.Empty, pixelPos, false, 0f, Color.White)
				{
					layerDepth = ((layerDepth >= 0f) ? layerDepth : ((float)(@event.OffsetTileY(tile.Y) * 64) / 10000f))
				};
				sprite.CopyAppearanceFromItemId(itemId);
				context.Location.TemporarySprites.Add(sprite);
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddBigProp(Event @event, string[] args, EventContext context)
			{
				Vector2 tile;
				string error;
				string itemId;
				if (!ArgUtility.TryGetVector2(args, 1, out tile, out error, true) || !ArgUtility.TryGet(args, 3, out itemId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Object prop = ItemRegistry.Create<Object>("(BC)" + itemId);
				prop.TileLocation = @event.OffsetTile(tile);
				@event.props.Add(prop);
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddFloorProp(Event @event, string[] args, EventContext context)
			{
				AddProp(@event, args, context);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddProp(Event @event, string[] args, EventContext context)
			{
				string commandName;
				string error;
				int index;
				Point tile;
				int drawWidth;
				int drawHeight;
				int boundingHeight;
				int tilesHorizontal;
				int tilesVertical;
				if (!ArgUtility.TryGet(args, 0, out commandName, out error) || !ArgUtility.TryGetInt(args, 1, out index, out error) || !ArgUtility.TryGetPoint(args, 2, out tile, out error) || !ArgUtility.TryGetOptionalInt(args, 4, out drawWidth, out error, 1) || !ArgUtility.TryGetOptionalInt(args, 5, out drawHeight, out error, 1) || !ArgUtility.TryGetOptionalInt(args, 6, out boundingHeight, out error, drawHeight) || !ArgUtility.TryGetOptionalInt(args, 7, out tilesHorizontal, out error) || !ArgUtility.TryGetOptionalInt(args, 8, out tilesVertical, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int tileX = @event.OffsetTileX(tile.X);
				int tileY = @event.OffsetTileY(tile.Y);
				bool solid = !commandName.Equals("AddFloorProp", StringComparison.OrdinalIgnoreCase);
				@event.festivalProps.Add(new Prop(@event.festivalTexture, index, drawWidth, boundingHeight, drawHeight, tileX, tileY, solid));
				if (tilesHorizontal != 0)
				{
					for (int x = tileX + tilesHorizontal; x != tileX; x -= Math.Sign(tilesHorizontal))
					{
						@event.festivalProps.Add(new Prop(@event.festivalTexture, index, drawWidth, boundingHeight, drawHeight, x, tileY, solid));
					}
				}
				if (tilesVertical != 0)
				{
					for (int y = tileY + tilesVertical; y != tileY; y -= Math.Sign(tilesVertical))
					{
						@event.festivalProps.Add(new Prop(@event.festivalTexture, index, drawWidth, boundingHeight, drawHeight, tileX, y, solid));
					}
				}
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RemoveObject(Event @event, string[] args, EventContext context)
			{
				Point tile;
				string error;
				if (!ArgUtility.TryGetPoint(args, 1, out tile, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				GameLocation location = context.Location;
				Vector2 position = @event.OffsetPosition(new Vector2(tile.X, tile.Y) * 64f);
				for (int i = location.temporarySprites.Count - 1; i >= 0; i--)
				{
					if (location.temporarySprites[i].position == position)
					{
						location.temporarySprites.RemoveAt(i);
						break;
					}
				}
				@event.CurrentCommand++;
				@event.Update(location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Glow(Event @event, string[] args, EventContext context)
			{
				int red;
				string error;
				int green;
				int blue;
				bool hold;
				if (!ArgUtility.TryGetInt(args, 1, out red, out error) || !ArgUtility.TryGetInt(args, 2, out green, out error) || !ArgUtility.TryGetInt(args, 3, out blue, out error) || !ArgUtility.TryGetOptionalBool(args, 4, out hold, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.screenGlowOnce(new Color(red, green, blue), hold);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StopGlowing(Event @event, string[] args, EventContext context)
			{
				Game1.screenGlowUp = false;
				Game1.screenGlowHold = false;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddQuest(Event @event, string[] args, EventContext context)
			{
				string questId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out questId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.addQuest(questId);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RemoveQuest(Event @event, string[] args, EventContext context)
			{
				string questId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out questId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.removeQuest(questId);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddSpecialOrder(Event @event, string[] args, EventContext context)
			{
				string orderId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out orderId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.team.AddSpecialOrder(orderId);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RemoveSpecialOrder(Event @event, string[] args, EventContext context)
			{
				string orderId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out orderId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				NetList<SpecialOrder, NetRef<SpecialOrder>> orders = Game1.player.team.specialOrders;
				for (int i = orders.Count - 1; i >= 0; i--)
				{
					if (orders[i].questKey.Value == orderId)
					{
						orders.RemoveAt(i);
					}
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddItem(Event @event, string[] args, EventContext context)
			{
				string itemId;
				string error;
				int count;
				int quality;
				if (!ArgUtility.TryGet(args, 1, out itemId, out error) || !ArgUtility.TryGetOptionalInt(args, 2, out count, out error, 1) || !ArgUtility.TryGetOptionalInt(args, 3, out quality, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Item i = ItemRegistry.Create(itemId, count, quality);
				if (i != null)
				{
					Game1.player.addItemByMenuIfNecessary(i);
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AwardFestivalPrize(Event @event, string[] args, EventContext context)
			{
				if (args.Length == 1)
				{
					string id = @event.id;
					if (id == "festival_spring13")
					{
						if (@event.festivalWinners.Contains(Game1.player.UniqueMultiplayerID))
						{
							if (Game1.player.mailReceived.Add("Egg Festival"))
							{
								if (Game1.activeClickableMenu == null)
								{
									Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(H)4"));
								}
								@event.CurrentCommand++;
								if (Game1.activeClickableMenu == null)
								{
									@event.CurrentCommand++;
								}
							}
							else
							{
								if (Game1.activeClickableMenu == null)
								{
									Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(O)PrizeTicket"));
								}
								@event.CurrentCommand++;
								if (Game1.activeClickableMenu == null)
								{
									@event.CurrentCommand++;
								}
							}
						}
						else
						{
							@event.CurrentCommand += 2;
						}
						return;
					}
					if (id == "festival_winter8")
					{
						if (@event.festivalWinners.Contains(Game1.player.UniqueMultiplayerID))
						{
							if (Game1.player.mailReceived.Add("Ice Festival"))
							{
								if (Game1.activeClickableMenu == null)
								{
									Game1.activeClickableMenu = new ItemGrabMenu(new Item[4]
									{
										ItemRegistry.Create("(H)17"),
										ItemRegistry.Create("(O)687"),
										ItemRegistry.Create("(O)691"),
										ItemRegistry.Create("(O)703")
									}, @event).setEssential(true);
								}
								@event.CurrentCommand++;
							}
							else
							{
								if (Game1.activeClickableMenu == null)
								{
									Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(O)PrizeTicket"));
								}
								@event.CurrentCommand++;
								if (Game1.activeClickableMenu == null)
								{
									@event.CurrentCommand++;
								}
							}
						}
						else
						{
							@event.CurrentCommand += 2;
						}
						return;
					}
				}
				string itemId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out itemId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				switch (itemId.ToLower())
				{
				case "meowmere":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(W)65"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "birdiereward":
					Game1.player.team.RequestLimitedNutDrops("Birdie", null, 0, 0, 5, 5);
					if (!Game1.MasterPlayer.hasOrWillReceiveMail("gotBirdieReward"))
					{
						Game1.addMailForTomorrow("gotBirdieReward", true, true);
					}
					@event.CurrentCommand++;
					@event.CurrentCommand++;
					break;
				case "memento":
				{
					Object o = ItemRegistry.Create<Object>("(O)864");
					o.specialItem = true;
					o.questItem.Value = true;
					Game1.player.addItemByMenuIfNecessary(o);
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				}
				case "emilyclothes":
				{
					Clothing pants = ItemRegistry.Create<Clothing>("(P)8");
					pants.Dye(new Color(0, 143, 239), 1f);
					Game1.player.addItemsByMenuIfNecessary(new List<Item>
					{
						ItemRegistry.Create("(B)804"),
						ItemRegistry.Create("(H)41"),
						ItemRegistry.Create("(S)1127"),
						pants
					});
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				}
				case "qimilk":
					if (Game1.player.mailReceived.Add("qiCave"))
					{
						Game1.player.maxHealth += 25;
					}
					@event.CurrentCommand++;
					break;
				case "pan":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(T)Pan"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "sculpture":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(F)1306"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "samboombox":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(F)1309"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "marniepainting":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(F)1802"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "rod":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(T)BambooPole"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "pot":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(BC)62"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "jukebox":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(BC)209"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "sword":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(W)0"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "hero":
					Game1.getSteamAchievement("Achievement_LocalLegend");
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(BC)116"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "joja":
					Game1.getSteamAchievement("Achievement_Joja");
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(BC)117"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				case "slimeegg":
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(O)680"));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				default:
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create(itemId));
					if (Game1.activeClickableMenu == null)
					{
						@event.CurrentCommand++;
					}
					@event.CurrentCommand++;
					break;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AttachCharacterToTempSprite(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				TemporaryAnimatedSprite t = context.Location.temporarySprites.Last();
				if (t != null)
				{
					t.attachedCharacter = @event.getActorByName(actorName);
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Fork(Event @event, string[] args, EventContext context)
			{
				string requiredId;
				string error;
				string newKey;
				bool isTranslationKey;
				if (!ArgUtility.TryGet(args, 1, out requiredId, out error) || !ArgUtility.TryGetOptional(args, 2, out newKey, out error) || !ArgUtility.TryGetOptionalBool(args, 3, out isTranslationKey, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (newKey == null)
				{
					newKey = requiredId;
					requiredId = null;
				}
				bool num;
				if (requiredId == null)
				{
					num = @event.specialEventVariable1;
				}
				else
				{
					if (Game1.player.mailReceived.Contains(requiredId))
					{
						goto IL_0080;
					}
					num = Game1.player.dialogueQuestionsAnswered.Contains(requiredId);
				}
				if (!num)
				{
					@event.CurrentCommand++;
					return;
				}
				goto IL_0080;
				IL_0080:
				string[] commands;
				if (isTranslationKey)
				{
					string raw3 = Game1.content.LoadStringReturnNullIfNotFound(newKey);
					if (raw3 == null)
					{
						context.LogErrorAndSkip("can't load new script from translation key '" + newKey + "' because that translation wasn't found");
						return;
					}
					commands = ParseCommands(raw3, context.Event.farmer);
				}
				else if (@event.isFestival)
				{
					string raw2;
					if (!@event.TryGetFestivalDataForYear(newKey, out raw2))
					{
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(91, 2);
						defaultInterpolatedStringHandler.AppendLiteral("can't load new script from festival field '");
						defaultInterpolatedStringHandler.AppendFormatted(newKey);
						defaultInterpolatedStringHandler.AppendLiteral("' because there's no such key in the '");
						defaultInterpolatedStringHandler.AppendFormatted(@event.id);
						defaultInterpolatedStringHandler.AppendLiteral("' festival");
						context.LogErrorAndSkip(defaultInterpolatedStringHandler.ToStringAndClear());
						return;
					}
					commands = ParseCommands(raw2, context.Event.farmer);
				}
				else
				{
					string assetName = "Data\\Events\\" + Game1.currentLocation.Name;
					if (!Game1.content.DoesAssetExist<Dictionary<string, string>>(assetName))
					{
						context.LogErrorAndSkip("can't load new script from event asset '" + assetName + "' because it doesn't exist");
						return;
					}
					string raw;
					if (!Game1.content.Load<Dictionary<string, string>>(assetName).TryGetValue(newKey, out raw))
					{
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(88, 2);
						defaultInterpolatedStringHandler.AppendLiteral("can't load new script from event asset '");
						defaultInterpolatedStringHandler.AppendFormatted(assetName);
						defaultInterpolatedStringHandler.AppendLiteral("' because it doesn't contain the required '");
						defaultInterpolatedStringHandler.AppendFormatted(newKey);
						defaultInterpolatedStringHandler.AppendLiteral("' key");
						context.LogErrorAndSkip(defaultInterpolatedStringHandler.ToStringAndClear());
						return;
					}
					commands = ParseCommands(raw, context.Event.farmer);
				}
				@event.ReplaceAllCommands(commands);
				@event.forked = true;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void SwitchEvent(Event @event, string[] args, EventContext context)
			{
				string newKey;
				string error;
				if (!ArgUtility.TryGet(args, 1, out newKey, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				string[] commands;
				if (@event.isFestival)
				{
					string raw2;
					if (!@event.TryGetFestivalDataForYear(newKey, out raw2))
					{
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(90, 2);
						defaultInterpolatedStringHandler.AppendLiteral("can't load new event from festival field '");
						defaultInterpolatedStringHandler.AppendFormatted(newKey);
						defaultInterpolatedStringHandler.AppendLiteral("' because there's no such key in the '");
						defaultInterpolatedStringHandler.AppendFormatted(@event.id);
						defaultInterpolatedStringHandler.AppendLiteral("' festival");
						context.LogErrorAndSkip(defaultInterpolatedStringHandler.ToStringAndClear());
						return;
					}
					commands = ParseCommands(raw2, context.Event.farmer);
				}
				else
				{
					string assetName = "Data\\Events\\" + Game1.currentLocation.Name;
					if (!Game1.content.DoesAssetExist<Dictionary<string, string>>(assetName))
					{
						context.LogErrorAndSkip("can't load new event from asset '" + assetName + "' because it doesn't exist");
						return;
					}
					string raw;
					if (!Game1.content.Load<Dictionary<string, string>>(assetName).TryGetValue(newKey, out raw))
					{
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(81, 2);
						defaultInterpolatedStringHandler.AppendLiteral("can't load new event from asset '");
						defaultInterpolatedStringHandler.AppendFormatted(assetName);
						defaultInterpolatedStringHandler.AppendLiteral("' because it doesn't contain the required '");
						defaultInterpolatedStringHandler.AppendFormatted(newKey);
						defaultInterpolatedStringHandler.AppendLiteral("' key");
						context.LogErrorAndSkip(defaultInterpolatedStringHandler.ToStringAndClear());
						return;
					}
					commands = ParseCommands(raw, context.Event.farmer);
				}
				@event.ReplaceAllCommands(commands);
				@event.eventSwitched = true;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void GlobalFade(Event @event, string[] args, EventContext context)
			{
				float fadeSpeed;
				string error;
				bool continueEventDuringFade;
				if (!ArgUtility.TryGetOptionalFloat(args, 1, out fadeSpeed, out error, 0.007f) || !ArgUtility.TryGetOptionalBool(args, 2, out continueEventDuringFade, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else if (!Game1.globalFade)
				{
					if (continueEventDuringFade)
					{
						Game1.globalFadeToBlack(null, fadeSpeed);
						@event.CurrentCommand++;
					}
					else
					{
						Game1.globalFadeToBlack(@event.incrementCommandAfterFade, fadeSpeed);
					}
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void GlobalFadeToClear(Event @event, string[] args, EventContext context)
			{
				float fadeSpeed;
				string error;
				bool continueEventDuringFade;
				if (!ArgUtility.TryGetOptionalFloat(args, 1, out fadeSpeed, out error, 0.007f) || !ArgUtility.TryGetOptionalBool(args, 2, out continueEventDuringFade, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else if (!Game1.globalFade)
				{
					if (continueEventDuringFade)
					{
						Game1.globalFadeToClear(null, fadeSpeed);
						@event.CurrentCommand++;
					}
					else
					{
						Game1.globalFadeToClear(@event.incrementCommandAfterFade, fadeSpeed);
					}
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Cutscene(Event @event, string[] args, EventContext context)
			{
				string cutsceneId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out cutsceneId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				GameLocation location = context.Location;
				GameTime time = context.Time;
				if (@event.currentCustomEventScript != null)
				{
					if (@event.currentCustomEventScript.update(time, @event))
					{
						@event.currentCustomEventScript = null;
						@event.CurrentCommand++;
					}
				}
				else
				{
					if (Game1.currentMinigame != null)
					{
						return;
					}
					switch (cutsceneId)
					{
					case "greenTea":
						@event.currentCustomEventScript = new EventScript_GreenTea(new Vector2(-64000f, -64000f), @event);
						break;
					case "linusMoneyGone":
						foreach (TemporaryAnimatedSprite temporarySprite in location.temporarySprites)
						{
							temporarySprite.alphaFade = 0.01f;
							temporarySprite.motion = new Vector2(0f, -1f);
						}
						@event.CurrentCommand++;
						return;
					case "marucomet":
						Game1.currentMinigame = new MaruComet();
						break;
					case "AbigailGame":
						Game1.currentMinigame = new AbigailGame(@event.getActorByName("Abigail") ?? Game1.RequireCharacter("Abigail"));
						break;
					case "robot":
						Game1.currentMinigame = new RobotBlastoff();
						break;
					case "haleyCows":
						Game1.currentMinigame = new HaleyCowPictures();
						break;
					case "boardGame":
						Game1.currentMinigame = new FantasyBoardGame();
						@event.CurrentCommand++;
						break;
					case "plane":
						Game1.currentMinigame = new PlaneFlyBy();
						break;
					case "balloonDepart":
					{
						TemporaryAnimatedSprite temporarySpriteByID = location.getTemporarySpriteByID(1);
						temporarySpriteByID.attachedCharacter = @event.farmer;
						temporarySpriteByID.motion = new Vector2(0f, -2f);
						TemporaryAnimatedSprite temporarySpriteByID2 = location.getTemporarySpriteByID(2);
						temporarySpriteByID2.attachedCharacter = @event.getActorByName("Harvey");
						temporarySpriteByID2.motion = new Vector2(0f, -2f);
						location.getTemporarySpriteByID(3).scaleChange = -0.01f;
						@event.CurrentCommand++;
						return;
					}
					case "clearTempSprites":
						location.temporarySprites.Clear();
						@event.CurrentCommand++;
						break;
					case "balloonChangeMap":
						@event.eventPositionTileOffset = Vector2.Zero;
						location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 1183, 84, 160), 10000f, 1, 99999, @event.OffsetPosition(new Vector2(22f, 36f) * 64f + new Vector2(-23f, 0f) * 4f), false, false, 2E-05f, 0f, Color.White, 4f, 0f, 0f, 0f)
						{
							motion = new Vector2(0f, -2f),
							yStopCoordinate = (int)@event.OffsetPositionY(576f),
							reachedStopCoordinate = @event.balloonInSky,
							attachedCharacter = @event.farmer,
							id = 1
						});
						location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(84, 1205, 38, 26), 10000f, 1, 99999, @event.OffsetPosition(new Vector2(22f, 36f) * 64f + new Vector2(0f, 134f) * 4f), false, false, 0.2625f, 0f, Color.White, 4f, 0f, 0f, 0f)
						{
							motion = new Vector2(0f, -2f),
							id = 2,
							attachedCharacter = @event.getActorByName("Harvey")
						});
						@event.CurrentCommand++;
						break;
					case "bandFork":
					{
						int whichBand = 76;
						if (Game1.player.dialogueQuestionsAnswered.Contains("77"))
						{
							whichBand = 77;
						}
						else if (Game1.player.dialogueQuestionsAnswered.Contains("78"))
						{
							whichBand = 78;
						}
						else if (Game1.player.dialogueQuestionsAnswered.Contains("79"))
						{
							whichBand = 79;
						}
						@event.answerDialogue("bandFork", whichBand);
						@event.CurrentCommand++;
						return;
					}
					case "eggHuntWinner":
						@event.eggHuntWinner();
						@event.CurrentCommand++;
						return;
					case "governorTaste":
						@event.governorTaste();
						@event.currentCommand++;
						return;
					case "addSecretSantaItem":
					{
						Item o = Utility.getGiftFromNPC(@event.mySecretSanta);
						Game1.player.addItemByMenuIfNecessaryElseHoldUp(o);
						@event.currentCommand++;
						return;
					}
					case "iceFishingWinner":
						@event.iceFishingWinner();
						@event.currentCommand++;
						return;
					case "iceFishingWinnerMP":
						@event.iceFishingWinnerMP();
						@event.currentCommand++;
						return;
					}
					Game1.globalFadeToClear(null, 0.01f);
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void WaitForTempSprite(Event @event, string[] args, EventContext context)
			{
				int spriteId;
				string error;
				if (!ArgUtility.TryGetInt(args, 1, out spriteId, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else if (Game1.currentLocation.getTemporarySpriteByID(spriteId) != null)
				{
					@event.CurrentCommand++;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Cave(Event @event, string[] args, EventContext context)
			{
				if (Game1.activeClickableMenu == null)
				{
					Response[] responses = new Response[2]
					{
						new Response("Mushrooms", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1220")),
						new Response("Bats", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1222"))
					};
					Game1.currentLocation.createQuestionDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1223"), responses, "cave");
					Game1.dialogueTyping = false;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void UpdateMinigame(Event @event, string[] args, EventContext context)
			{
				int eventData;
				string error;
				if (!ArgUtility.TryGetInt(args, 1, out eventData, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.currentMinigame?.receiveEventPoke(eventData);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StartJittering(Event @event, string[] args, EventContext context)
			{
				@event.farmer.jitterStrength = 1f;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Money(Event @event, string[] args, EventContext context)
			{
				int amount;
				string error;
				if (!ArgUtility.TryGetInt(args, 1, out amount, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				@event.farmer.Money += amount;
				if (@event.farmer.Money < 0)
				{
					@event.farmer.Money = 0;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StopJittering(Event @event, string[] args, EventContext context)
			{
				@event.farmer.stopJittering();
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddLantern(Event @event, string[] args, EventContext context)
			{
				int initialParentSheetIndex;
				string error;
				Vector2 tile;
				int lightRadius;
				if (!ArgUtility.TryGetInt(args, 1, out initialParentSheetIndex, out error) || !ArgUtility.TryGetVector2(args, 2, out tile, out error, true) || !ArgUtility.TryGetInt(args, 4, out lightRadius, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				context.Location.TemporarySprites.Add(new TemporaryAnimatedSprite(initialParentSheetIndex, 999999f, 1, 0, @event.OffsetPosition(tile * 64f), false, false)
				{
					light = true,
					lightRadius = lightRadius
				});
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RustyKey(Event @event, string[] args, EventContext context)
			{
				Game1.player.hasRustyKey = true;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Swimming(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					Farmer farmer = @event.GetFarmerActor(farmerNumber);
					if (farmer != null)
					{
						farmer.bathingClothes.Value = true;
						farmer.swimming.Value = true;
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
					if (actor == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					actor.swimming.Value = true;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StopSwimming(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					Farmer farmer = @event.GetFarmerActor(farmerNumber);
					if (farmer != null)
					{
						farmer.bathingClothes.Value = context.Location is BathHousePool;
						farmer.swimming.Value = false;
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
					if (actor == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					actor.swimming.Value = false;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void TutorialMenu(Event @event, string[] args, EventContext context)
			{
				if (Game1.activeClickableMenu == null)
				{
					Game1.activeClickableMenu = new TutorialMenu();
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AnimalNaming(Event @event, string[] args, EventContext context)
			{
				GameLocation currentLocation = Game1.currentLocation;
				AnimalHouse animalHouse = currentLocation as AnimalHouse;
				if (animalHouse == null)
				{
					context.LogErrorAndSkip("this command only works when run in an AnimalHouse location");
				}
				else if (Game1.activeClickableMenu == null)
				{
					Game1.activeClickableMenu = new NamingMenu(delegate(string animalName)
					{
						animalHouse.addNewHatchedAnimal(animalName);
						@event.CurrentCommand++;
					}, Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1236"));
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void SplitSpeak(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string dialogue;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGet(args, 2, out dialogue, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				string[] choiceAnswers = LegacyShims.SplitAndTrim(dialogue, '~');
				if (Game1.dialogueUp)
				{
					return;
				}
				@event.timeAccumulator += context.Time.ElapsedGameTime.Milliseconds;
				if (!(@event.timeAccumulator < 500f))
				{
					@event.timeAccumulator = 0f;
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc) ?? Game1.getCharacterFromName(actorName);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					if (!ArgUtility.HasIndex(choiceAnswers, @event.previousAnswerChoice))
					{
						@event.CurrentCommand++;
						return;
					}
					i.CurrentDialogue.Push(new Dialogue(i, null, choiceAnswers[@event.previousAnswerChoice]));
					Game1.drawDialogue(i);
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void CatQuestion(Event @event, string[] args, EventContext context)
			{
				if (!Game1.isQuestion && Game1.activeClickableMenu == null)
				{
					PetData data;
					string petType = (Pet.TryGetData(Game1.player.whichPetType, out data) ? (TokenParser.ParseText(data.DisplayName) ?? "pet") : "pet");
					Game1.currentLocation.createQuestionDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1241") + petType + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1244"), Game1.currentLocation.createYesNoResponses(), "pet");
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AmbientLight(Event @event, string[] args, EventContext context)
			{
				int red;
				string error;
				int green;
				int blue;
				if (!ArgUtility.TryGetInt(args, 1, out red, out error) || !ArgUtility.TryGetInt(args, 2, out green, out error) || !ArgUtility.TryGetInt(args, 3, out blue, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.ambientLight = new Color(red, green, blue);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void BgColor(Event @event, string[] args, EventContext context)
			{
				int red;
				string error;
				int green;
				int blue;
				if (!ArgUtility.TryGetInt(args, 1, out red, out error) || !ArgUtility.TryGetInt(args, 2, out green, out error) || !ArgUtility.TryGetInt(args, 3, out blue, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.setBGColor((byte)red, (byte)green, (byte)blue);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ElliottBookTalk(Event @event, string[] args, EventContext context)
			{
				if (!Game1.dialogueUp)
				{
					string speechKey = (Game1.player.dialogueQuestionsAnswered.Contains("958699") ? "Strings\\StringsFromCSFiles:Event.cs.1257" : (Game1.player.dialogueQuestionsAnswered.Contains("958700") ? "Strings\\StringsFromCSFiles:Event.cs.1258" : ((!Game1.player.dialogueQuestionsAnswered.Contains("9586701")) ? "Strings\\StringsFromCSFiles:Event.cs.1260" : "Strings\\StringsFromCSFiles:Event.cs.1259")));
					NPC i = @event.getActorByName("Elliott") ?? Game1.getCharacterFromName("Elliott");
					i.CurrentDialogue.Push(new Dialogue(i, speechKey));
					Game1.drawDialogue(i);
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RemoveItem(Event @event, string[] args, EventContext context)
			{
				string itemId;
				string error;
				int count;
				if (!ArgUtility.TryGet(args, 1, out itemId, out error) || !ArgUtility.TryGetOptionalInt(args, 2, out count, out error, 1))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.removeFirstOfThisItemFromInventory(itemId, count);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Friendship(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				int friendshipChange;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetInt(args, 2, out friendshipChange, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				NPC character = Game1.getCharacterFromName(actorName);
				if (character == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'");
					return;
				}
				Game1.player.changeFriendship(friendshipChange, character);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void SetRunning(Event @event, string[] args, EventContext context)
			{
				@event.farmer.setRunning(true);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ExtendSourceRect(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string rawOption;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGet(args, 2, out rawOption, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isReset = rawOption == "reset";
				int horizontal = -1;
				int vertical = -1;
				bool ignoreSourceRectUpdates = false;
				if (!isReset && (!ArgUtility.TryGetInt(args, 2, out horizontal, out error) || !ArgUtility.TryGetInt(args, 3, out vertical, out error) || !ArgUtility.TryGetOptionalBool(args, 4, out ignoreSourceRectUpdates, out error)))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
				if (actor == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				if (isReset)
				{
					actor.reloadSprite();
					actor.Sprite.SpriteWidth = 16;
					actor.Sprite.SpriteHeight = 32;
					actor.HideShadow = false;
				}
				else
				{
					actor.extendSourceRect(horizontal, vertical, ignoreSourceRectUpdates);
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void WaitForOtherPlayers(Event @event, string[] args, EventContext context)
			{
				string gateId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out gateId, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else if (Game1.IsMultiplayer)
				{
					Game1.netReady.SetLocalReady(gateId, true);
					if (Game1.netReady.IsReady(gateId))
					{
						if (Game1.activeClickableMenu is ReadyCheckDialog)
						{
							Game1.exitActiveMenu();
						}
						@event.CurrentCommand++;
					}
					else if (Game1.activeClickableMenu == null)
					{
						Game1.activeClickableMenu = new ReadyCheckDialog(gateId, false);
					}
				}
				else
				{
					@event.CurrentCommand++;
				}
			}

			/// <summary>Used in the movie theater, requests that the server end the movie.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RequestMovieEnd(Event @event, string[] args, EventContext context)
			{
				Game1.player.team.requestMovieEndEvent.Fire(Game1.player.UniqueMultiplayerID);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RestoreStashedItem(Event @event, string[] args, EventContext context)
			{
				Game1.player.TemporaryItem = null;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AdvancedMove(Event @event, string[] args, EventContext context)
			{
				@event.setUpAdvancedMove(args);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void StopRunning(Event @event, string[] args, EventContext context)
			{
				@event.farmer.setRunning(false);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Eyes(Event @event, string[] args, EventContext context)
			{
				int eyes;
				string error;
				int blinkTimer;
				if (!ArgUtility.TryGetInt(args, 1, out eyes, out error) || !ArgUtility.TryGetInt(args, 2, out blinkTimer, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				@event.farmer.currentEyes = eyes;
				@event.farmer.blinkTimer = blinkTimer;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			[OtherNames(new string[] { "mailReceived" })]
			public static void AddMailReceived(Event @event, string[] args, EventContext context)
			{
				string mailId;
				string error;
				bool add;
				if (!ArgUtility.TryGet(args, 1, out mailId, out error) || !ArgUtility.TryGetOptionalBool(args, 2, out add, out error, true))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.mailReceived.Toggle(mailId, add);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddWorldState(Event @event, string[] args, EventContext context)
			{
				string worldStateId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out worldStateId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.worldStateIDs.Add(worldStateId);
				Game1.netWorldState.Value.addWorldStateID(worldStateId);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Fade(Event @event, string[] args, EventContext context)
			{
				string option = ArgUtility.Get(args, 1);
				if (option == "unfade")
				{
					Game1.fadeIn = false;
					Game1.fadeToBlack = false;
					@event.CurrentCommand++;
					return;
				}
				Game1.fadeToBlack = true;
				Game1.fadeIn = true;
				if (Game1.fadeToBlackAlpha >= 0.97f)
				{
					if (option == null)
					{
						Game1.fadeIn = false;
					}
					@event.CurrentCommand++;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ChangeMapTile(Event @event, string[] args, EventContext context)
			{
				string layerId;
				string error;
				Point tilePos;
				int newTileIndex;
				if (!ArgUtility.TryGet(args, 1, out layerId, out error) || !ArgUtility.TryGetPoint(args, 2, out tilePos, out error) || !ArgUtility.TryGetInt(args, 4, out newTileIndex, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Layer layer = context.Location.map.GetLayer(layerId);
				if (layer == null)
				{
					context.LogErrorAndSkip("the '" + context.Location.NameOrUniqueName + "' location doesn't have required map layer " + layerId);
					return;
				}
				int tileX = @event.OffsetTileX(tilePos.X);
				int tileY = @event.OffsetTileY(tilePos.Y);
				Tile tile = layer.Tiles[tileX, tileY];
				if (tile == null)
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(47, 3);
					defaultInterpolatedStringHandler.AppendLiteral("the '");
					defaultInterpolatedStringHandler.AppendFormatted(context.Location.NameOrUniqueName);
					defaultInterpolatedStringHandler.AppendLiteral("' location doesn't have required tile (");
					defaultInterpolatedStringHandler.AppendFormatted(tilePos.X);
					defaultInterpolatedStringHandler.AppendLiteral(", ");
					defaultInterpolatedStringHandler.AppendFormatted(tilePos.Y);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					string text = defaultInterpolatedStringHandler.ToStringAndClear();
					object obj;
					if (tileX == tilePos.X && tileY == tilePos.Y)
					{
						obj = "";
					}
					else
					{
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(18, 2);
						defaultInterpolatedStringHandler.AppendLiteral(" (adjusted to (");
						defaultInterpolatedStringHandler.AppendFormatted(tileX);
						defaultInterpolatedStringHandler.AppendLiteral(", ");
						defaultInterpolatedStringHandler.AppendFormatted(tileY);
						defaultInterpolatedStringHandler.AppendLiteral(")");
						obj = defaultInterpolatedStringHandler.ToStringAndClear();
					}
					context.LogErrorAndSkip(text + (string)obj + " on layer " + layerId);
				}
				else
				{
					tile.TileIndex = newTileIndex;
					@event.CurrentCommand++;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ChangeSprite(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string spriteSuffix;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetOptional(args, 2, out spriteSuffix, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
				if (actor == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				if (spriteSuffix != null)
				{
					actor.spriteOverridden = true;
					actor.Sprite.LoadTexture("Characters\\" + NPC.getTextureNameForCharacter(actorName) + "_" + spriteSuffix);
				}
				else
				{
					actor.spriteOverridden = false;
					actor.ChooseAppearance();
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void WaitForAllStationary(Event @event, string[] args, EventContext context)
			{
				List<NPCController> npcControllers = @event.npcControllers;
				bool anyMoving = npcControllers != null && npcControllers.Count > 0;
				if (!anyMoving)
				{
					foreach (NPC actor in @event.actors)
					{
						if (actor.isMoving())
						{
							anyMoving = true;
							break;
						}
					}
				}
				if (!anyMoving)
				{
					foreach (Farmer farmerActor in @event.farmerActors)
					{
						if (farmerActor.isMoving())
						{
							anyMoving = true;
							break;
						}
					}
				}
				if (!anyMoving)
				{
					@event.CurrentCommand++;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ProceedPosition(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Character character = @event.getCharacterByName(actorName);
				if (character == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'");
					return;
				}
				@event.continueAfterMove = true;
				try
				{
					if (character.isMoving())
					{
						List<NPCController> npcControllers = @event.npcControllers;
						if (npcControllers == null || npcControllers.Count != 0)
						{
							return;
						}
					}
					character.Halt();
					@event.CurrentCommand++;
				}
				catch
				{
					@event.CurrentCommand++;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ChangePortrait(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string portraitSuffix;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetOptional(args, 2, out portraitSuffix, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC i = @event.getActorByName(actorName, out isOptionalNpc) ?? Game1.getCharacterFromName(actorName);
				if (i == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				if (portraitSuffix != null)
				{
					i.portraitOverridden = true;
					i.Portrait = Game1.content.Load<Texture2D>("Portraits\\" + NPC.getTextureNameForCharacter(actorName) + "_" + portraitSuffix);
				}
				else
				{
					i.portraitOverridden = false;
					i.ChooseAppearance();
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ChangeYSourceRectOffset(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				int ySourceRectOffset;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetInt(args, 2, out ySourceRectOffset, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC i = @event.getActorByName(actorName, out isOptionalNpc);
				if (i == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				i.ySourceRectOffset = ySourceRectOffset;
				@event.CurrentCommand++;
			}

			/// <summary>Set the display name for an event actor to an exact value.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ChangeName(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string newName;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGet(args, 2, out newName, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC i = @event.getActorByName(actorName, out isOptionalNpc);
				if (i == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				i.displayName = newName;
				@event.CurrentCommand++;
			}

			/// <summary>Set the display name for an event actor to a translation key.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void TranslateName(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string translationKey;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGet(args, 2, out translationKey, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC i = @event.getActorByName(actorName, out isOptionalNpc);
				if (i == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				i.displayName = Game1.content.LoadString(translationKey);
				@event.CurrentCommand++;
			}

			/// <summary>Replace an NPC in the event with a temporary copy that only exists for the duration of the event. This allows changing the NPC in the event (e.g. renaming them) without affecting the real NPC.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ReplaceWithClone(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
				if (actor == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				@event.actors.Remove(actor);
				@event.actors.Add(new NPC(actor.Sprite.Clone(), actor.Position, actor.FacingDirection, actor.Name)
				{
					Birthday_Day = actor.Birthday_Day,
					Birthday_Season = actor.Birthday_Season,
					Gender = actor.Gender,
					Portrait = actor.Portrait,
					EventActor = true,
					displayName = actor.displayName,
					drawOffset = actor.drawOffset,
					TemporaryDialogue = new Stack<Dialogue>(actor.CurrentDialogue.Select((Dialogue p) => new Dialogue(p)))
				});
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void PlayFramesAhead(Event @event, string[] args, EventContext context)
			{
				int framesToSkip;
				string error;
				if (!ArgUtility.TryGetInt(args, 1, out framesToSkip, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				@event.CurrentCommand++;
				for (int i = 0; i < framesToSkip; i++)
				{
					@event.Update(context.Location, context.Time);
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ShowKissFrame(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				bool flip;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetOptionalBool(args, 2, out flip, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
				if (actor == null)
				{
					context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				CharacterData data = actor.GetData();
				int spouseFrame = data?.KissSpriteIndex ?? 28;
				bool facingRight = data?.KissSpriteFacingRight ?? true;
				if (flip)
				{
					facingRight = !facingRight;
				}
				actor.Sprite.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
				{
					new FarmerSprite.AnimationFrame(spouseFrame, 1000, false, facingRight)
				});
				@event.CurrentCommand++;
			}

			/// <remarks>Format: <c>addTemporaryActor name spriteWidth spriteHeight xPosition yPosition facingDirection breather=true animal=false</c>.</remarks>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddTemporaryActor(Event @event, string[] args, EventContext context)
			{
				string spriteAssetName;
				string error;
				Point spriteSize;
				Vector2 tile;
				int facingDirection;
				bool isBreather;
				string typeOrDisplayName;
				string overrideName;
				if (!ArgUtility.TryGet(args, 1, out spriteAssetName, out error) || !ArgUtility.TryGetPoint(args, 2, out spriteSize, out error) || !ArgUtility.TryGetVector2(args, 4, out tile, out error) || !ArgUtility.TryGetDirection(args, 6, out facingDirection, out error) || !ArgUtility.TryGetOptionalBool(args, 7, out isBreather, out error, true) || !ArgUtility.TryGetOptional(args, 8, out typeOrDisplayName, out error) || !ArgUtility.TryGetOptional(args, 9, out overrideName, out error, null, false))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				string textureLocation = "Characters\\";
				bool hasValidTypeKey = true;
				switch (typeOrDisplayName?.ToLower())
				{
				case "animal":
					textureLocation = "Animals\\";
					break;
				case "monster":
					textureLocation = "Characters\\Monsters\\";
					break;
				default:
					hasValidTypeKey = false;
					break;
				case "character":
					break;
				}
				string fullSpriteAssetName = textureLocation + spriteAssetName;
				if (!Game1.content.DoesAssetExist<Texture2D>(fullSpriteAssetName))
				{
					string newSpriteAssetName = spriteAssetName.Replace('_', ' ');
					string newFullSpriteAssetName = textureLocation + newSpriteAssetName;
					if (newSpriteAssetName != spriteAssetName && Game1.content.DoesAssetExist<Texture2D>(newFullSpriteAssetName))
					{
						spriteAssetName = newSpriteAssetName;
						fullSpriteAssetName = newFullSpriteAssetName;
					}
				}
				NPC i = new NPC(new AnimatedSprite(@event.festivalContent, fullSpriteAssetName, 0, spriteSize.X, spriteSize.Y), @event.OffsetPosition(tile * 64f), facingDirection, spriteAssetName, @event.festivalContent);
				i.AllowDynamicAppearance = false;
				i.Breather = isBreather;
				i.HideShadow = i.Sprite.SpriteWidth >= 32;
				i.TemporaryDialogue = new Stack<Dialogue>();
				if (!hasValidTypeKey && typeOrDisplayName != null)
				{
					i.displayName = typeOrDisplayName;
				}
				Dialogue dialogue;
				if (@event.isFestival && @event.TryGetFestivalDialogueForYear(i, i.Name, out dialogue))
				{
					i.CurrentDialogue.Push(dialogue);
				}
				if (overrideName != null)
				{
					i.Name = overrideName;
				}
				i.EventActor = true;
				@event.actors.Add(i);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ChangeToTemporaryMap(Event @event, string[] args, EventContext context)
			{
				string mapName;
				string error;
				bool shouldPan;
				if (!ArgUtility.TryGet(args, 1, out mapName, out error) || !ArgUtility.TryGetOptionalBool(args, 2, out shouldPan, out error, true))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				@event.temporaryLocation = ((mapName == "Town") ? new Town("Maps\\Town", "Temp") : ((@event.isFestival && mapName.Contains("Town")) ? new Town("Maps\\" + mapName, "Temp") : new GameLocation("Maps\\" + mapName, "Temp")));
				@event.temporaryLocation.map.LoadTileSheets(Game1.mapDisplayDevice);
				Event e = Game1.currentLocation.currentEvent;
				Game1.currentLocation.cleanupBeforePlayerExit();
				Game1.currentLocation.currentEvent = null;
				Game1.currentLightSources.Clear();
				Game1.currentLocation = @event.temporaryLocation;
				Game1.currentLocation.resetForPlayerEntry();
				Game1.currentLocation.UpdateMapSeats();
				Game1.currentLocation.currentEvent = e;
				@event.CurrentCommand++;
				Game1.player.currentLocation = Game1.currentLocation;
				@event.farmer.currentLocation = Game1.currentLocation;
				Game1.currentLocation.ResetForEvent(@event);
				if (shouldPan)
				{
					Game1.panScreen(0, 0);
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void PositionOffset(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				Point offset;
				bool continueImmediately;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetPoint(args, 2, out offset, out error) || !ArgUtility.TryGetOptionalBool(args, 4, out continueImmediately, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					Farmer f = @event.GetFarmerActor(farmerNumber);
					if (f != null)
					{
						f.position.X += offset.X;
						f.position.Y += offset.Y;
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					i.position.X += offset.X;
					i.position.Y += offset.Y;
				}
				@event.CurrentCommand++;
				if (continueImmediately)
				{
					@event.Update(context.Location, context.Time);
				}
			}

			/// <remarks>Format: <c>question &lt;questionKey (forkN to make the nth answer fork)&gt; "question#answer1#answer2#...#answerN"</c>.</remarks>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Question(Event @event, string[] args, EventContext context)
			{
				string dialogueKey;
				string error;
				string rawQuestionsAndAnswers;
				if (!ArgUtility.TryGet(args, 1, out dialogueKey, out error) || !ArgUtility.TryGet(args, 2, out rawQuestionsAndAnswers, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else if (!Game1.isQuestion && Game1.activeClickableMenu == null)
				{
					string[] questionAndAnswers = LegacyShims.SplitAndTrim(rawQuestionsAndAnswers, '#');
					string question = questionAndAnswers[0];
					Response[] answers = new Response[questionAndAnswers.Length - 1];
					for (int i = 1; i < questionAndAnswers.Length; i++)
					{
						answers[i - 1] = new Response((i - 1).ToString(), questionAndAnswers[i]);
					}
					Game1.currentLocation.createQuestionDialogue(question, answers, dialogueKey);
				}
			}

			/// <remarks>Format: <c>quickQuestion question#answer1#answer2#...#answerN(break)answerLogic1(break)answerLogic2(break)...(break)answerLogicN</c>. Use <c>\</c> instead of <c>/</c> inside the <c>answerLogic</c> sections.</remarks>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void QuickQuestion(Event @event, string[] args, EventContext context)
			{
				if (!Game1.isQuestion && Game1.activeClickableMenu == null)
				{
					string currentCommand = @event.GetCurrentCommand();
					string[] questionAndAnswerSplit = LegacyShims.SplitAndTrim(LegacyShims.SplitAndTrim(currentCommand.Substring(currentCommand.IndexOf(' ') + 1), "(break)")[0], '#');
					string question = questionAndAnswerSplit[0];
					Response[] answers = new Response[questionAndAnswerSplit.Length - 1];
					for (int i = 1; i < questionAndAnswerSplit.Length; i++)
					{
						answers[i - 1] = new Response((i - 1).ToString(), questionAndAnswerSplit[i]);
					}
					Game1.currentLocation.createQuestionDialogue(question, answers, "quickQuestion");
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void DrawOffset(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				Vector2 offset;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetVector2(args, 2, out offset, out error, true))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc = false;
				int farmerNumber;
				Character character = (@event.IsFarmerActorId(actorName, out farmerNumber) ? ((Character)@event.GetFarmerActor(farmerNumber)) : ((Character)@event.getActorByName(actorName, out isOptionalNpc)));
				if (character == null)
				{
					context.LogErrorAndSkip("no actor found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				character.drawOffset = offset * 4f;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void HideShadow(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				bool hideShadow;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetBool(args, 2, out hideShadow, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				bool isOptionalNpc;
				NPC character = @event.getActorByName(actorName, out isOptionalNpc);
				if (character == null)
				{
					context.LogErrorAndSkip("no actor found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				character.HideShadow = hideShadow;
				@event.CurrentCommand++;
			}

			/// <summary>Animates properties of character "jumps". If any argument is set to "keep", it'll retain the current value.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AnimateHeight(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				string rawHeight;
				string rawGravity;
				string rawVelocity;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGet(args, 2, out rawHeight, out error) || !ArgUtility.TryGet(args, 3, out rawGravity, out error) || !ArgUtility.TryGet(args, 4, out rawVelocity, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int? height = null;
				float? jumpGravity = null;
				float? jumpVelocity = null;
				if (rawHeight != "keep")
				{
					int parsed3;
					if (!int.TryParse(rawHeight, out parsed3))
					{
						context.LogErrorAndSkip("required index 2 must be 'keep' or an integer height");
						return;
					}
					height = parsed3;
				}
				if (rawGravity != "keep")
				{
					float parsed2;
					if (!float.TryParse(rawGravity, out parsed2))
					{
						context.LogErrorAndSkip("required index 3 must be 'keep' or a float gravity value");
						return;
					}
					jumpGravity = parsed2;
				}
				if (rawVelocity != "keep")
				{
					float parsed;
					if (!float.TryParse(rawVelocity, out parsed))
					{
						context.LogErrorAndSkip("required index 4 must be 'keep' or a float velocity value");
						return;
					}
					jumpVelocity = parsed;
				}
				bool isOptionalNpc = false;
				int farmerNumber;
				Character character = (@event.IsFarmerActorId(actorName, out farmerNumber) ? ((Character)@event.GetFarmerActor(farmerNumber)) : ((Character)@event.getActorByName(actorName, out isOptionalNpc)));
				if (character == null)
				{
					context.LogErrorAndSkip("no actor found with name '" + actorName + "'", isOptionalNpc);
					return;
				}
				if (height.HasValue)
				{
					character.yJumpOffset = -height.Value;
				}
				if (jumpGravity.HasValue)
				{
					character.yJumpGravity = jumpGravity.Value;
				}
				if (jumpVelocity.HasValue)
				{
					character.yJumpVelocity = jumpVelocity.Value;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Jump(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				float jumpV;
				bool noSound;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetOptionalFloat(args, 2, out jumpV, out error, 8f) || !ArgUtility.TryGetOptionalBool(args, 3, out noSound, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					@event.GetFarmerActor(farmerNumber)?.jump(jumpV);
				}
				else
				{
					bool isOptionalNpc;
					NPC actor = @event.getActorByName(actorName, out isOptionalNpc);
					if (actor == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					if (noSound)
					{
						actor.jumpWithoutSound(jumpV);
					}
					else
					{
						actor.jump(jumpV);
					}
				}
				@event.CurrentCommand++;
				@event.Update(context.Location, context.Time);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void FarmerEat(Event @event, string[] args, EventContext context)
			{
				string itemId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out itemId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Object toEat = ItemRegistry.Create<Object>("(O)" + itemId);
				@event.farmer.eatObject(toEat, true);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void SpriteText(Event @event, string[] args, EventContext context)
			{
				int colorIndex;
				string error;
				string text;
				if (!ArgUtility.TryGetInt(args, 1, out colorIndex, out error) || !ArgUtility.TryGet(args, 2, out text, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				@event.int_useMeForAnything2 = colorIndex;
				@event.float_useMeForAnything += context.Time.ElapsedGameTime.Milliseconds;
				if (@event.float_useMeForAnything > 80f)
				{
					if (@event.int_useMeForAnything >= text.Length)
					{
						if (@event.float_useMeForAnything >= 2500f)
						{
							@event.int_useMeForAnything = 0;
							@event.float_useMeForAnything = 0f;
							@event.spriteTextToDraw = "";
							@event.CurrentCommand++;
						}
					}
					else
					{
						@event.int_useMeForAnything++;
						@event.float_useMeForAnything = 0f;
						Game1.playSound("dialogueCharacter");
					}
				}
				@event.spriteTextToDraw = text;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void IgnoreCollisions(Event @event, string[] args, EventContext context)
			{
				string actorName;
				string error;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				int farmerNumber;
				if (@event.IsFarmerActorId(actorName, out farmerNumber))
				{
					Farmer f = @event.GetFarmerActor(farmerNumber);
					if (f != null)
					{
						f.ignoreCollisions = true;
					}
				}
				else
				{
					bool isOptionalNpc;
					NPC i = @event.getActorByName(actorName, out isOptionalNpc);
					if (i == null)
					{
						context.LogErrorAndSkip("no NPC found with name '" + actorName + "'", isOptionalNpc);
						return;
					}
					i.isCharging = true;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void ScreenFlash(Event @event, string[] args, EventContext context)
			{
				float flashAlpha;
				string error;
				if (!ArgUtility.TryGetFloat(args, 1, out flashAlpha, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.flashAlpha = flashAlpha;
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void GrandpaCandles(Event @event, string[] args, EventContext context)
			{
				int candles = Utility.getGrandpaCandlesFromScore(Utility.getGrandpaScore());
				Game1.getFarm().grandpaScore.Value = candles;
				for (int i = 0; i < candles; i++)
				{
					DelayedAction.playSoundAfterDelay("fireball", 100 * i);
				}
				Game1.getFarm().addGrandpaCandles();
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void GrandpaEvaluation2(Event @event, string[] args, EventContext context)
			{
				switch (Utility.getGrandpaCandlesFromScore(Utility.getGrandpaScore()))
				{
				case 1:
					@event.ReplaceCurrentCommand("speak Grandpa \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1306") + "\"");
					break;
				case 2:
					@event.ReplaceCurrentCommand("speak Grandpa \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1307") + "\"");
					break;
				case 3:
					@event.ReplaceCurrentCommand("speak Grandpa \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1308") + "\"");
					break;
				case 4:
					@event.ReplaceCurrentCommand("speak Grandpa \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1309") + "\"");
					break;
				}
				Game1.player.eventsSeen.Remove("2146991");
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void GrandpaEvaluation(Event @event, string[] args, EventContext context)
			{
				switch (Utility.getGrandpaCandlesFromScore(Utility.getGrandpaScore()))
				{
				case 1:
					@event.ReplaceCurrentCommand("speak Grandpa \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1315") + "\"");
					break;
				case 2:
					@event.ReplaceCurrentCommand("speak Grandpa \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1316") + "\"");
					break;
				case 3:
					@event.ReplaceCurrentCommand("speak Grandpa \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1317") + "\"");
					break;
				case 4:
					@event.ReplaceCurrentCommand("speak Grandpa \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1318") + "\"");
					break;
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void LoadActors(Event @event, string[] args, EventContext context)
			{
				_003C_003Ec__DisplayClass129_0 _003C_003Ec__DisplayClass129_ = default(_003C_003Ec__DisplayClass129_0);
				_003C_003Ec__DisplayClass129_.@event = @event;
				string layerId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out layerId, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Layer layer = _003C_003Ec__DisplayClass129_.@event.temporaryLocation?.map.GetLayer(layerId);
				if (layer == null)
				{
					context.LogErrorAndSkip("the '" + context.Location.NameOrUniqueName + "' location doesn't have required map layer " + layerId);
					return;
				}
				_003C_003Ec__DisplayClass129_.@event.actors.Clear();
				_003C_003Ec__DisplayClass129_.@event.npcControllers?.Clear();
				Dictionary<int, string> actorNamesByIndex = new Dictionary<int, string>();
				foreach (KeyValuePair<string, CharacterData> entry in Game1.characterData)
				{
					int index = entry.Value.FestivalVanillaActorIndex;
					if (index >= 0 && !actorNamesByIndex.TryAdd(index, entry.Key))
					{
						IGameLogger log = Game1.log;
						DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(110, 2);
						defaultInterpolatedStringHandler.AppendLiteral("NPC '");
						defaultInterpolatedStringHandler.AppendFormatted(entry.Key);
						defaultInterpolatedStringHandler.AppendLiteral("' has the same festival actor index as '");
						defaultInterpolatedStringHandler.AppendFormatted(actorNamesByIndex[index]);
						defaultInterpolatedStringHandler.AppendLiteral("' in Data/Characters, so it'll be ignored for festival placement.");
						log.Warn(defaultInterpolatedStringHandler.ToStringAndClear());
					}
				}
				HashSet<string> npcNames = new HashSet<string>();
				for (int x = 0; x < layer.LayerWidth; x++)
				{
					for (int y = 0; y < layer.LayerHeight; y++)
					{
						if (layer.Tiles[x, y] != null)
						{
							int tileIndexAt = layer.GetTileIndexAt(x, y);
							int actorIndex = tileIndexAt / 4;
							int actorFacingDirection = tileIndexAt % 4;
							string actorName;
							if (actorNamesByIndex.TryGetValue(actorIndex, out actorName) && Game1.getCharacterFromName(actorName) != null && (!(actorName == "Leo") || Game1.MasterPlayer.mailReceived.Contains("leoMoved")))
							{
								_003C_003Ec__DisplayClass129_.@event.addActor(actorName, x, y, actorFacingDirection, _003C_003Ec__DisplayClass129_.@event.temporaryLocation);
								npcNames.Add(actorName);
							}
						}
					}
				}
				string data;
				string keyName;
				if (_003C_003Ec__DisplayClass129_.@event.festivalData != null && _003C_003Ec__DisplayClass129_.@event.TryGetFestivalDataForYear(layerId + "_additionalCharacters", out data, out keyName))
				{
					string[] array = ParseCommands(data, context.Event.farmer);
					for (int j = 0; j < array.Length; j++)
					{
						string[] curArgs = ArgUtility.SplitBySpaceQuoteAware(array[j]);
						string actorName2;
						Point tile;
						int direction;
						if (!ArgUtility.TryGet(curArgs, 0, out actorName2, out error) || !ArgUtility.TryGetPoint(curArgs, 1, out tile, out error) || !ArgUtility.TryGetDirection(curArgs, 3, out direction, out error))
						{
							DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(61, 3);
							defaultInterpolatedStringHandler.AppendLiteral("'");
							defaultInterpolatedStringHandler.AppendFormatted(keyName);
							defaultInterpolatedStringHandler.AppendLiteral("' festival field has invalid additional character entry '");
							defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", curArgs));
							defaultInterpolatedStringHandler.AppendLiteral("': ");
							defaultInterpolatedStringHandler.AppendFormatted(error);
							context.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
						}
						else if (Game1.getCharacterFromName(actorName2) != null)
						{
							if (!(actorName2 == "Leo") || Game1.MasterPlayer.mailReceived.Contains("leoMoved"))
							{
								_003C_003Ec__DisplayClass129_.@event.addActor(actorName2, tile.X, tile.Y, direction, _003C_003Ec__DisplayClass129_.@event.temporaryLocation);
								npcNames.Add(actorName2);
							}
						}
						else
						{
							DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(86, 3);
							defaultInterpolatedStringHandler.AppendLiteral("'");
							defaultInterpolatedStringHandler.AppendFormatted(keyName);
							defaultInterpolatedStringHandler.AppendLiteral("' festival field has invalid additional character entry '");
							defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", curArgs));
							defaultInterpolatedStringHandler.AppendLiteral("': no NPC found with name '");
							defaultInterpolatedStringHandler.AppendFormatted(actorName2);
							defaultInterpolatedStringHandler.AppendLiteral("'");
							context.LogError(defaultInterpolatedStringHandler.ToStringAndClear());
						}
					}
				}
				if (layerId == "Set-Up")
				{
					foreach (string npcName in npcNames)
					{
						NPC npc = Game1.getCharacterFromName(npcName);
						if (!npc.isMarried() || npc.getSpouse() == null || npc.getSpouse().getChildren().Count <= 0)
						{
							continue;
						}
						Farmer spouse = Game1.player;
						if (npc.getSpouse() != null)
						{
							spouse = npc.getSpouse();
						}
						List<Child> children = spouse.getChildren();
						npc = _003C_003Ec__DisplayClass129_.@event.getCharacterByName(npcName) as NPC;
						for (int childIndex = 0; childIndex < children.Count; childIndex++)
						{
							Child child = children[childIndex];
							if (child.Age < 3)
							{
								continue;
							}
							Child childActor = new Child(child.Name, child.Gender == Gender.Male, child.darkSkinned, spouse);
							childActor.NetFields.CopyFrom(child.NetFields);
							childActor.Halt();
							Point[] directionOffsets;
							switch (npc.FacingDirection)
							{
							case 0:
								directionOffsets = new Point[4]
								{
									new Point(0, 1),
									new Point(-1, 0),
									new Point(1, 0),
									new Point(0, -1)
								};
								break;
							case 2:
								directionOffsets = new Point[4]
								{
									new Point(0, -1),
									new Point(1, 0),
									new Point(-1, 0),
									new Point(0, 1)
								};
								break;
							case 3:
								directionOffsets = new Point[4]
								{
									new Point(1, 0),
									new Point(0, -1),
									new Point(0, 1),
									new Point(-1, 0)
								};
								break;
							case 1:
								directionOffsets = new Point[4]
								{
									new Point(-1, 0),
									new Point(0, 1),
									new Point(0, -1),
									new Point(1, 0)
								};
								break;
							default:
								directionOffsets = new Point[4]
								{
									new Point(-1, 0),
									new Point(1, 0),
									new Point(0, -1),
									new Point(0, 1)
								};
								break;
							}
							Point spawnPoint = npc.TilePoint;
							List<Point> pointsToCheck = new List<Point>();
							Point[] array2 = directionOffsets;
							for (int j = 0; j < array2.Length; j++)
							{
								Point offset = array2[j];
								pointsToCheck.Add(new Point(spawnPoint.X + offset.X, spawnPoint.Y + offset.Y));
							}
							bool foundSpawn = false;
							for (int iteration = 0; iteration < 5; iteration++)
							{
								if (foundSpawn)
								{
									break;
								}
								int currentCheckCount = pointsToCheck.Count;
								for (int i = 0; i < currentCheckCount; i++)
								{
									Point currentPoint = pointsToCheck[0];
									pointsToCheck.RemoveAt(0);
									if (_003CLoadActors_003Eg__IsWalkableTileCheck_007C129_0(currentPoint, ref _003C_003Ec__DisplayClass129_))
									{
										if (_003CLoadActors_003Eg__HasClearanceCheck_007C129_1(currentPoint, ref _003C_003Ec__DisplayClass129_))
										{
											foundSpawn = true;
											spawnPoint = currentPoint;
											break;
										}
										array2 = directionOffsets;
										for (int j = 0; j < array2.Length; j++)
										{
											Point offset2 = array2[j];
											pointsToCheck.Add(new Point(currentPoint.X + offset2.X, currentPoint.Y + offset2.Y));
										}
									}
								}
							}
							if (foundSpawn)
							{
								childActor.setTilePosition(spawnPoint.X, spawnPoint.Y);
								childActor.DefaultPosition = npc.DefaultPosition;
								childActor.faceDirection(npc.FacingDirection);
								childActor.EventActor = true;
								childActor.lastCrossroad = new Microsoft.Xna.Framework.Rectangle(spawnPoint.X * 64, spawnPoint.Y * 64, 64, 64);
								childActor.squareMovementFacingPreference = -1;
								childActor.walkInSquare(3, 3, 2000);
								childActor.controller = null;
								childActor.temporaryController = null;
								_003C_003Ec__DisplayClass129_.@event.actors.Add(childActor);
							}
						}
					}
				}
				_003C_003Ec__DisplayClass129_.@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void PlayerControl(Event @event, string[] args, EventContext context)
			{
				string sequenceId;
				string error;
				if (!ArgUtility.TryGet(args, 1, out sequenceId, out error))
				{
					context.LogErrorAndSkip(error);
				}
				else if (!@event.playerControlSequence)
				{
					@event.setUpPlayerControlSequence(sequenceId);
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void RemoveSprite(Event @event, string[] args, EventContext context)
			{
				Vector2 tile;
				string error;
				if (!ArgUtility.TryGetVector2(args, 1, out tile, out error, true))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Vector2 tilePixel = @event.OffsetPosition(tile * 64f);
				for (int i = Game1.currentLocation.temporarySprites.Count - 1; i >= 0; i--)
				{
					if (Game1.currentLocation.temporarySprites[i].position == tilePixel)
					{
						Game1.currentLocation.temporarySprites.RemoveAt(i);
					}
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Viewport(Event @event, string[] args, EventContext context)
			{
				if (ArgUtility.Get(args, 1) == "move")
				{
					Point direction;
					string error2;
					int duration;
					if (!ArgUtility.TryGetPoint(args, 2, out direction, out error2) || !ArgUtility.TryGetInt(args, 4, out duration, out error2))
					{
						context.LogErrorAndSkip(error2);
						return;
					}
					@event.viewportTarget = new Vector3(direction.X, direction.Y, duration);
				}
				else
				{
					Point position = Point.Zero;
					string action = null;
					bool shouldFade = false;
					string option = null;
					int test;
					string NPCTarget;
					string error3;
					string error;
					if (!int.TryParse(args[1], out test) && ArgUtility.TryGet(args, 1, out NPCTarget, out error3))
					{
						position = ((!(NPCTarget == "player")) ? @event.getActorByName(NPCTarget).TilePoint : Game1.MasterPlayer.TilePoint);
						if (!ArgUtility.TryGetOptional(args, 2, out action, out error3) || !ArgUtility.TryGetOptionalBool(args, (action == "clamp") ? 3 : 2, out shouldFade, out error3) || !ArgUtility.TryGetOptional(args, (action == "clamp") ? 4 : 2, out option, out error3))
						{
							context.LogErrorAndSkip(error3);
						}
					}
					else if (!ArgUtility.TryGetPoint(args, 1, out position, out error) || !ArgUtility.TryGetOptional(args, 3, out action, out error) || !ArgUtility.TryGetOptionalBool(args, (action == "clamp") ? 4 : 3, out shouldFade, out error) || !ArgUtility.TryGetOptional(args, (action == "clamp") ? 5 : 4, out option, out error))
					{
						context.LogErrorAndSkip(error);
						return;
					}
					if (@event.aboveMapSprites != null && position.X < 0)
					{
						@event.aboveMapSprites.Clear();
						@event.aboveMapSprites = null;
					}
					Game1.viewportFreeze = true;
					int targetTileX = @event.OffsetTileX(position.X);
					int targetTileY = @event.OffsetTileY(position.Y);
					if (@event.id == "2146991")
					{
						Point grandpaShrinePosition = Game1.getFarm().GetGrandpaShrinePosition();
						targetTileX = grandpaShrinePosition.X;
						targetTileY = grandpaShrinePosition.Y;
					}
					Game1.viewport.X = targetTileX * 64 + 32 - Game1.viewport.Width / 2;
					Game1.viewport.Y = targetTileY * 64 + 32 - Game1.viewport.Height / 2;
					if (Game1.viewport.X > 0 && Game1.viewport.Width > Game1.currentLocation.Map.DisplayWidth)
					{
						Game1.viewport.X = (Game1.currentLocation.Map.DisplayWidth - Game1.viewport.Width) / 2;
					}
					if (Game1.viewport.Y > 0 && Game1.viewport.Height > Game1.currentLocation.Map.DisplayHeight)
					{
						Game1.viewport.Y = (Game1.currentLocation.Map.DisplayHeight - Game1.viewport.Height) / 2;
					}
					if (action == "clamp")
					{
						if (Game1.currentLocation.map.DisplayWidth >= Game1.viewport.Width)
						{
							if (Game1.viewport.X + Game1.viewport.Width > Game1.currentLocation.Map.DisplayWidth)
							{
								Game1.viewport.X = Game1.currentLocation.Map.DisplayWidth - Game1.viewport.Width;
							}
							if (Game1.viewport.X < 0)
							{
								Game1.viewport.X = 0;
							}
						}
						else
						{
							Game1.viewport.X = Game1.currentLocation.Map.DisplayWidth / 2 - Game1.viewport.Width / 2;
						}
						if (Game1.currentLocation.map.DisplayHeight >= Game1.viewport.Height)
						{
							if (Game1.viewport.Y + Game1.viewport.Height > Game1.currentLocation.Map.DisplayHeight)
							{
								Game1.viewport.Y = Game1.currentLocation.Map.DisplayHeight - Game1.viewport.Height;
							}
						}
						else
						{
							Game1.viewport.Y = Game1.currentLocation.Map.DisplayHeight / 2 - Game1.viewport.Height / 2;
						}
						if (Game1.viewport.Y < 0)
						{
							Game1.viewport.Y = 0;
						}
					}
					if (shouldFade)
					{
						Game1.fadeScreenToBlack();
						Game1.fadeToBlackAlpha = 1f;
						Game1.nonWarpFade = true;
					}
					if (option == "unfreeze")
					{
						Game1.viewportFreeze = false;
					}
					if (Game1.gameMode == 2)
					{
						Game1.viewport.X = Game1.currentLocation.Map.DisplayWidth - Game1.viewport.Width;
					}
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void BroadcastEvent(Event @event, string[] args, EventContext context)
			{
				bool useLocalFarmer;
				string error;
				if (!ArgUtility.TryGetOptionalBool(args, 1, out useLocalFarmer, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (@event.farmer == Game1.player)
				{
					if (@event.id == "558291" || @event.id == "558292")
					{
						useLocalFarmer = true;
					}
					Game1.multiplayer.broadcastEvent(@event, Game1.currentLocation, Game1.player.positionBeforeEvent, useLocalFarmer);
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void AddConversationTopic(Event @event, string[] args, EventContext context)
			{
				string topicId;
				string error;
				int daysDuration;
				if (!ArgUtility.TryGet(args, 1, out topicId, out error) || !ArgUtility.TryGetOptionalInt(args, 2, out daysDuration, out error, 4))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (@event.isMemory)
				{
					@event.CurrentCommand++;
					return;
				}
				Game1.player.activeDialogueEvents.TryAdd(topicId, daysDuration);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void Dump(Event @event, string[] args, EventContext context)
			{
				string which;
				string error;
				if (!ArgUtility.TryGet(args, 1, out which, out error))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				if (!(which == "girls"))
				{
					if (!(which == "guys"))
					{
						context.LogErrorAndSkip("unknown ID '" + which + "', expected 'girls' or 'guys'");
						return;
					}
					Game1.player.activeDialogueEvents.Add("dumped_Guys", 7);
					Game1.player.activeDialogueEvents.Add("secondChance_Guys", 14);
				}
				else
				{
					Game1.player.activeDialogueEvents.Add("dumped_Girls", 7);
					Game1.player.activeDialogueEvents.Add("secondChance_Girls", 14);
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void EventSeen(Event @event, string[] args, EventContext context)
			{
				string eventId;
				string error;
				bool seen;
				if (!ArgUtility.TryGet(args, 1, out eventId, out error, false) || !ArgUtility.TryGetOptionalBool(args, 2, out seen, out error, true))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.eventsSeen.Toggle(eventId, seen);
				if (eventId == @event.id)
				{
					@event.markEventSeen = false;
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.DebugCommandHandlerDelegate" />
			public static void QuestionAnswered(Event @event, string[] args, EventContext context)
			{
				string questionId;
				string error;
				bool seen;
				if (!ArgUtility.TryGet(args, 1, out questionId, out error, false) || !ArgUtility.TryGetOptionalBool(args, 2, out seen, out error, true))
				{
					context.LogErrorAndSkip(error);
					return;
				}
				Game1.player.dialogueQuestionsAnswered.Toggle(questionId, seen);
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void GainSkill(Event @event, string[] args, EventContext context)
			{
				int whichSkill = Farmer.getSkillNumberFromName(args[1]);
				int level = Convert.ToInt32(args[2]);
				if (Game1.player.GetUnmodifiedSkillLevel(whichSkill) < level)
				{
					Game1.player.setSkillLevel(args[1], level);
				}
				@event.CurrentCommand++;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.EventCommandDelegate" />
			public static void MoveToSoup(Event @event, string[] args, EventContext context)
			{
				if (Game1.year % 2 == 1)
				{
					@event.setUpAdvancedMove(new string[9] { "", "Gus", "false", "0", "-1", "5", "0", "4", "1000" });
					@event.setUpAdvancedMove(new string[5] { "", "Jodi", "false", "0", "-2" });
					@event.setUpAdvancedMove(new string[11]
					{
						"", "Clint", "false", "0", "1", "-1", "0", "0", "3", "-2",
						"0"
					});
					@event.setUpAdvancedMove(new string[5] { "", "Emily", "false", "3", "0" });
					@event.setUpAdvancedMove(new string[7] { "", "Pam", "false", "0", "2", "7", "0" });
				}
				else
				{
					@event.setUpAdvancedMove(new string[5] { "", "Pierre", "false", "3", "0" });
					@event.setUpAdvancedMove(new string[9] { "", "Pam", "false", "0", "2", "-4", "0", "0", "1" });
					@event.setUpAdvancedMove(new string[9] { "", "Abigail", "false", "4", "0", "0", "-3", "1", "4000" });
					@event.setUpAdvancedMove(new string[9] { "", "Alex", "false", "-5", "0", "0", "-1", "3", "2000" });
					@event.setUpAdvancedMove(new string[5] { "", "Gus", "false", "0", "-1" });
				}
				@event.CurrentCommand++;
			}
		}

		/// <summary>The event commands indexed by name.</summary>
		/// <remarks>Command names are case-insensitive.</remarks>
		protected static readonly Dictionary<string, EventCommandDelegate> Commands = new Dictionary<string, EventCommandDelegate>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Alternate names for event commands.</summary>
		protected static readonly Dictionary<string, string> CommandAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>The event preconditions indexed by name.</summary>
		/// <remarks>Precondition names are case-<strong>sensitive</strong>.</remarks>
		protected static readonly Dictionary<string, EventPreconditionDelegate> Preconditions = new Dictionary<string, EventPreconditionDelegate>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Alternate names for event preconditions (e.g. shorthand or acronyms).</summary>
		/// <remarks>Aliases are case-sensitive for compatibility with older preconditions like 'h' vs 'H'.</remarks>
		private static readonly Dictionary<string, string> PreconditionAliases = new Dictionary<string, string>();

		private const float timeBetweenSpeech = 500f;

		public const string festivalTextureName = "Maps\\Festivals";

		private string festivalDataAssetName;

		/// <summary>
		///   The unique identifier for the event, if available. This may be...
		///   <list type="bullet">
		///     <item><description>for a regular event, the unique event ID from its data file (i.e. the first number in its entry key);</description></item>
		///     <item><description>for a generated event, an <see cref="T:StardewValley.Constants.EventIds" /> constant;</description></item>
		///     <item><description>for a festival, <c>festival_{asset name}</c> (like <c>festival_fall16</c>);</description></item>
		///     <item><description>else <see cref="F:StardewValley.Constants.EventIds.Unknown" />.</description></item>
		///   </list>
		/// </summary>
		public string id = "-1";

		/// <summary>The data asset name from which the event script was taken, or <c>null</c> for a generated event.</summary>
		public string fromAssetName;

		public bool isFestival;

		public bool isWedding;

		public bool isMemory;

		/// <summary>Whether the player can skip the rest of the event.</summary>
		public bool skippable;

		/// <summary>The actions to perform when the event is skipped, if any.</summary>
		public string[] actionsOnSkip;

		public bool skipped;

		public bool forked;

		public bool eventSwitched;

		private readonly LocalizedContentManager festivalContent = Game1.content.CreateTemporary();

		public string[] eventCommands;

		public int currentCommand;

		private Dictionary<string, Vector3> actorPositionsAfterMove;

		private float timeAccumulator;

		private Vector3 viewportTarget;

		private Color previousAmbientLight;

		private HashSet<long> festivalWinners = new HashSet<long>();

		private GameLocation temporaryLocation;

		private Dictionary<string, string> festivalData;

		private Texture2D _festivalTexture;

		private bool drawTool;

		private string hostMessageKey;

		private int previousFacingDirection = -1;

		private int previousAnswerChoice = -1;

		private bool startSecretSantaAfterDialogue;

		private List<Farmer> iceFishWinners;

		protected static LocalizedContentManager FestivalReadContentLoader;

		protected bool _playerControlSequence;

		protected bool _repeatingLocationSpecificCommand;

		[NonInstancedStatic]
		public static HashSet<string> invalidFestivals = new HashSet<string>();

		public List<NPC> actors = new List<NPC>();

		public List<Object> props = new List<Object>();

		public List<Prop> festivalProps = new List<Prop>();

		public List<Farmer> farmerActors = new List<Farmer>();

		public Dictionary<string, Dictionary<ISalable, ItemStockInformation>> festivalShops;

		public List<NPCController> npcControllers;

		private NPC festivalHost;

		public NPC secretSantaRecipient;

		public NPC mySecretSanta;

		public TemporaryAnimatedSpriteList underwaterSprites;

		public TemporaryAnimatedSpriteList aboveMapSprites;

		/// <summary>The custom sounds started during the event via <see cref="M:StardewValley.Event.DefaultCommands.PlaySound(StardewValley.Event,System.String[],StardewValley.EventContext)" />.</summary>
		public IDictionary<string, List<ICue>> CustomSounds = new Dictionary<string, List<ICue>>();

		public ICustomEventScript currentCustomEventScript;

		public bool simultaneousCommand;

		public int farmerAddedSpeed;

		public int int_useMeForAnything;

		public int int_useMeForAnything2;

		public float float_useMeForAnything;

		public string playerControlSequenceID;

		public string spriteTextToDraw;

		public bool showActiveObject;

		public bool continueAfterMove;

		public bool specialEventVariable1;

		public bool specialEventVariable2;

		public bool showGroundObjects = true;

		public bool doingSecretSanta;

		public bool showWorldCharacters;

		public bool ignoreObjectCollisions = true;

		public Point playerControlTargetTile;

		public List<Vector2> characterWalkLocations = new List<Vector2>();

		public Vector2 eventPositionTileOffset = Vector2.Zero;

		public int festivalTimer;

		public int grangeScore = -1000;

		public bool grangeJudged;

		/// <summary>Used to offset positions specified in events.</summary>
		public bool ignoreTileOffsets;

		private Stopwatch stopWatch;

		public LocationRequest exitLocation;

		public Action onEventFinished;

		/// <summary>Whether to add this event's ID to <see cref="F:StardewValley.Farmer.eventsSeen" /> when it ends, if it has a valid ID.</summary>
		/// <remarks>This has no effect on <see cref="F:StardewValley.Game1.eventsSeenSinceLastLocationChange" />, which is updated regardless (if it has a valid ID) to prevent event loops.</remarks>
		public bool markEventSeen = true;

		private bool eventFinished;

		private bool gotPet;

		public string FestivalName
		{
			get
			{
				string name;
				if (!TryGetFestivalDataForYear("name", out name))
				{
					return "";
				}
				return name;
			}
		}

		public bool playerControlSequence
		{
			get
			{
				return _playerControlSequence;
			}
			set
			{
				if (_playerControlSequence != value)
				{
					_playerControlSequence = value;
					if (!_playerControlSequence)
					{
						OnPlayerControlSequenceEnd(playerControlSequenceID);
					}
				}
			}
		}

		public Farmer farmer
		{
			get
			{
				if (farmerActors.Count <= 0)
				{
					return Game1.player;
				}
				return farmerActors[0];
			}
		}

		public Texture2D festivalTexture
		{
			get
			{
				if (_festivalTexture == null)
				{
					_festivalTexture = festivalContent.Load<Texture2D>("Maps\\Festivals");
				}
				return _festivalTexture;
			}
		}

		public int CurrentCommand
		{
			get
			{
				return currentCommand;
			}
			set
			{
				currentCommand = value;
			}
		}

		/// <summary>Register an event command.</summary>
		/// <param name="name">The command name that can be used in event scripts. This is case-insensitive.</param>
		/// <param name="action">The handler to call when the command is used.</param>
		public static void RegisterCommand(string name, EventCommandDelegate action)
		{
			SetupEventCommandsIfNeeded();
			if (Commands.ContainsKey(name))
			{
				Game1.log.Warn("Warning: event command " + name + " is already defined and will be overwritten.");
			}
			Commands[name] = action;
			Game1.log.Verbose("Registered event command: " + name);
		}

		/// <summary>Register an alternate name for an event command.</summary>
		/// <param name="alias">The alternate name. This is case-insensitive.</param>
		/// <param name="commandName">The original command name to alias. This is case-insensitive.</param>
		public static void RegisterCommandAlias(string alias, string commandName)
		{
			SetupEventCommandsIfNeeded();
			string conflictingName;
			if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(commandName))
			{
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(124, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Can't register event command alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' for '");
				defaultInterpolatedStringHandler.AppendFormatted(commandName);
				defaultInterpolatedStringHandler.AppendLiteral("' because the alias and command name must both be non-null and non-empty strings.");
				log.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (Commands.ContainsKey(alias))
			{
				IGameLogger log2 = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(95, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Can't register event command alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' for command '");
				defaultInterpolatedStringHandler.AppendFormatted(commandName);
				defaultInterpolatedStringHandler.AppendLiteral("', because there's a command with that name.");
				log2.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (CommandAliases.TryGetValue(alias, out conflictingName))
			{
				IGameLogger log3 = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(93, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Can't register event command alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' for command '");
				defaultInterpolatedStringHandler.AppendFormatted(commandName);
				defaultInterpolatedStringHandler.AppendLiteral("', because that's already an alias for '");
				defaultInterpolatedStringHandler.AppendFormatted(conflictingName);
				defaultInterpolatedStringHandler.AppendLiteral("'.");
				log3.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (!Commands.ContainsKey(commandName))
			{
				IGameLogger log4 = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(86, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Can't register event command alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' for command '");
				defaultInterpolatedStringHandler.AppendFormatted(commandName);
				defaultInterpolatedStringHandler.AppendLiteral("', because there's no such command.");
				log4.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else
			{
				CommandAliases[alias] = commandName;
			}
		}

		/// <summary>Register an event precondition.</summary>
		/// <param name="name">The precondition key that can be used in event precondition strings. This is case-insensitive.</param>
		/// <param name="action">The handler to call when the precondition is used.</param>
		public static void RegisterPrecondition(string name, EventPreconditionDelegate action)
		{
			SetupEventCommandsIfNeeded();
			if (Preconditions.ContainsKey(name))
			{
				Game1.log.Warn("Warning: event precondition " + name + " is already defined and will be overwritten.");
			}
			if (PreconditionAliases.Remove(name))
			{
				Game1.log.Warn("Warning: '" + name + "' was previously registered as a precondition alias. The alias was removed.");
			}
			Preconditions[name] = action;
			Game1.log.Verbose("Registered precondition: " + name);
		}

		/// <summary>Register an alternate name for an event precondition.</summary>
		/// <param name="alias">The alternate name. This is <strong>case-sensitive</strong> for legacy reasons.</param>
		/// <param name="preconditionName">The original precondition name to alias. This is case-insensitive.</param>
		public static void RegisterPreconditionAlias(string alias, string preconditionName)
		{
			SetupEventCommandsIfNeeded();
			string conflictingName;
			if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(preconditionName))
			{
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(134, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Can't register event precondition alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' for '");
				defaultInterpolatedStringHandler.AppendFormatted(preconditionName);
				defaultInterpolatedStringHandler.AppendLiteral("' because the alias and precondition name must both be non-null and non-empty strings.");
				log.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (Preconditions.ContainsKey(alias))
			{
				IGameLogger log2 = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(110, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Can't register event precondition alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' for precondition '");
				defaultInterpolatedStringHandler.AppendFormatted(preconditionName);
				defaultInterpolatedStringHandler.AppendLiteral("', because there's a precondition with that name.");
				log2.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (PreconditionAliases.TryGetValue(alias, out conflictingName))
			{
				IGameLogger log3 = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(103, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Can't register event precondition alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' for precondition '");
				defaultInterpolatedStringHandler.AppendFormatted(preconditionName);
				defaultInterpolatedStringHandler.AppendLiteral("', because that's already an alias for '");
				defaultInterpolatedStringHandler.AppendFormatted(conflictingName);
				defaultInterpolatedStringHandler.AppendLiteral("'.");
				log3.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else if (!Preconditions.ContainsKey(preconditionName))
			{
				IGameLogger log4 = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(101, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Can't register event precondition alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' for precondition '");
				defaultInterpolatedStringHandler.AppendFormatted(preconditionName);
				defaultInterpolatedStringHandler.AppendLiteral("', because there's no such precondition.");
				log4.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else
			{
				PreconditionAliases[alias] = preconditionName;
			}
		}

		/// <summary>Register the vanilla event commands and preconditions if they haven't already been registered.</summary>
		private static void SetupEventCommandsIfNeeded()
		{
			MethodInfo[] array;
			if (Commands.Count == 0)
			{
				MethodInfo[] methods2 = typeof(DefaultCommands).GetMethods(BindingFlags.Static | BindingFlags.Public);
				array = methods2;
				foreach (MethodInfo method3 in array)
				{
					EventCommandDelegate command = (EventCommandDelegate)Delegate.CreateDelegate(typeof(EventCommandDelegate), method3);
					Commands.Add(method3.Name, command);
				}
				array = methods2;
				foreach (MethodInfo method4 in array)
				{
					OtherNamesAttribute attribute2 = method4.GetCustomAttribute<OtherNamesAttribute>();
					if (attribute2 != null)
					{
						string[] aliases = attribute2.Aliases;
						for (int j = 0; j < aliases.Length; j++)
						{
							RegisterCommandAlias(aliases[j], method4.Name);
						}
					}
				}
			}
			if (Preconditions.Count != 0)
			{
				return;
			}
			MethodInfo[] methods = typeof(Preconditions).GetMethods(BindingFlags.Static | BindingFlags.Public);
			array = methods;
			foreach (MethodInfo method in array)
			{
				EventPreconditionDelegate preconditionDelegate = (EventPreconditionDelegate)Delegate.CreateDelegate(typeof(EventPreconditionDelegate), method);
				Preconditions[method.Name] = preconditionDelegate;
			}
			array = methods;
			foreach (MethodInfo method2 in array)
			{
				OtherNamesAttribute attribute = method2.GetCustomAttribute<OtherNamesAttribute>();
				if (attribute != null)
				{
					string[] aliases = attribute.Aliases;
					for (int j = 0; j < aliases.Length; j++)
					{
						RegisterPreconditionAlias(aliases[j], method2.Name);
					}
				}
			}
		}

		/// <summary>Get the handler for a precondition key, if any.</summary>
		/// <param name="key">The precondition key, which can be either the case-insensitive canonical name (like <c>DaysPlayed</c>) or case-sensitive alias (like <c>j</c>).</param>
		/// <param name="handler">The precondition handler, if found.</param>
		/// <returns>Returns whether a handler was found for the precondition key.</returns>
		public static bool TryGetPreconditionHandler(string key, out EventPreconditionDelegate handler)
		{
			SetupEventCommandsIfNeeded();
			string aliasTarget;
			if (PreconditionAliases.TryGetValue(key, out aliasTarget))
			{
				key = aliasTarget;
			}
			return Preconditions.TryGetValue(key, out handler);
		}

		/// <summary>Get whether an event precondition matches the current context.</summary>
		/// <param name="location">The location which is checking the event.</param>
		/// <param name="eventId">The unique ID for the event being checked.</param>
		/// <param name="precondition">The event precondition string, including the precondition name.</param>
		public static bool CheckPrecondition(GameLocation location, string eventId, string precondition)
		{
			string[] preconditionSplit = ArgUtility.SplitBySpaceQuoteAware(precondition);
			string key = preconditionSplit[0];
			bool match = true;
			if (key.StartsWith('!'))
			{
				key = key.Substring(1);
				match = false;
			}
			EventPreconditionDelegate handler;
			if (!TryGetPreconditionHandler(key, out handler))
			{
				Game1.log.Warn("Unknown precondition for event " + eventId + ": " + precondition);
				return false;
			}
			try
			{
				return handler(location, eventId, preconditionSplit) == match;
			}
			catch (Exception ex)
			{
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Failed checking precondition '");
				defaultInterpolatedStringHandler.AppendFormatted(precondition);
				defaultInterpolatedStringHandler.AppendLiteral("' for event ");
				defaultInterpolatedStringHandler.AppendFormatted(eventId);
				defaultInterpolatedStringHandler.AppendLiteral(".");
				log.Error(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
				return false;
			}
		}

		/// <summary>Try to run an event command for the current event.</summary>
		/// <param name="location">The location in which the event is running.</param>
		/// <param name="time">The current game execution time.</param>
		/// <param name="args">The space-delimited event command string, including the command name.</param>
		public virtual void tryEventCommand(GameLocation location, GameTime time, string[] args)
		{
			string commandName = ArgUtility.Get(args, 0);
			if (string.IsNullOrWhiteSpace(commandName))
			{
				LogCommandErrorAndSkip(args, "can't run an empty or null command");
				return;
			}
			string aliasTarget;
			if (CommandAliases.TryGetValue(commandName, out aliasTarget))
			{
				commandName = aliasTarget;
			}
			EventCommandDelegate command;
			if (!Commands.TryGetValue(commandName, out command))
			{
				LogCommandErrorAndSkip(args, "unknown command '" + commandName + "'");
				return;
			}
			try
			{
				EventContext context = new EventContext(this, location, time, args);
				command(this, args, context);
			}
			catch (Exception e)
			{
				LogErrorAndHalt(e);
			}
		}

		/// <summary>Construct an instance.</summary>
		/// <param name="eventString">The raw event script.</param>
		/// <param name="farmerActor">The player to add as an actor in the event script, or <c>null</c> to use <see cref="P:StardewValley.Game1.player" />.</param>
		public Event(string eventString, Farmer farmerActor = null)
			: this(eventString, null, "-1", farmerActor)
		{
		}

		/// <summary>Construct an instance.</summary>
		/// <param name="eventString">The raw event script.</param>
		/// <param name="fromAssetName">The data asset name from which the event script was taken, or <c>null</c> for a generated event.</param>
		/// <param name="eventID">The event's unique ID from the event data files, if known. This may be a number matching one of the <see cref="T:StardewValley.Event" /> constants in <see cref="T:StardewValley.Constants.EventIds" /> for a generated event.</param>
		/// <param name="farmerActor">The player to add as an actor in the event script, or <c>null</c> to use <see cref="P:StardewValley.Game1.player" />.</param>
		public Event(string eventString, string fromAssetName, string eventID, Farmer farmerActor = null)
			: this()
		{
			this.fromAssetName = fromAssetName;
			id = eventID;
			eventCommands = ParseCommands(eventString, farmerActor);
			actorPositionsAfterMove = new Dictionary<string, Vector3>();
			previousAmbientLight = Game1.ambientLight;
			if (farmerActor != null)
			{
				farmerActors.Add(farmerActor);
			}
			farmer.canOnlyWalk = true;
			farmer.showNotCarrying();
			drawTool = false;
			if (eventID == "-2")
			{
				isWedding = true;
			}
		}

		/// <summary>Construct an instance.</summary>
		public Event()
		{
			SetupEventCommandsIfNeeded();
		}

		public static void OnNewDay()
		{
			FestivalReadContentLoader?.Unload();
		}

		/// <summary>Load the raw data for a festival, if it exists and is valid.</summary>
		/// <param name="festival">The festival ID to load, matching the asset name under <c>Data/Festivals</c> (like <samp>spring13</samp>).</param>
		/// <param name="assetName">The asset name for the loaded festival data.</param>
		/// <param name="data">The loaded festival data.</param>
		/// <param name="locationName">The location name in which the festival takes place.</param>
		/// <param name="startTime">The time of day when the festival opens.</param>
		/// <param name="endTime">The time of day when the festival closes.</param>
		/// <returns>Returns whether the festival data was loaded successfully.</returns>
		public static bool tryToLoadFestivalData(string festival, out string assetName, out Dictionary<string, string> data, out string locationName, out int startTime, out int endTime)
		{
			assetName = "Data\\Festivals\\" + festival;
			data = null;
			locationName = null;
			startTime = 0;
			endTime = 0;
			if (invalidFestivals.Contains(festival))
			{
				return false;
			}
			if (FestivalReadContentLoader == null)
			{
				FestivalReadContentLoader = Game1.content.CreateTemporary();
			}
			try
			{
				if (!FestivalReadContentLoader.DoesAssetExist<Dictionary<string, string>>(assetName))
				{
					invalidFestivals.Add(festival);
					return false;
				}
				data = FestivalReadContentLoader.Load<Dictionary<string, string>>(assetName);
			}
			catch
			{
				invalidFestivals.Add(festival);
				return false;
			}
			string rawConditions;
			if (!data.TryGetValue("conditions", out rawConditions))
			{
				Game1.log.Error("Festival '" + festival + "' doesn't have the required 'conditions' data field.");
				return false;
			}
			string[] fields = LegacyShims.SplitAndTrim(rawConditions, '/');
			string error;
			string rawTimeSpan;
			if (!ArgUtility.TryGet(fields, 0, out locationName, out error, false) || !ArgUtility.TryGet(fields, 1, out rawTimeSpan, out error, false))
			{
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(60, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Festival '");
				defaultInterpolatedStringHandler.AppendFormatted(festival);
				defaultInterpolatedStringHandler.AppendLiteral("' has preconditions '");
				defaultInterpolatedStringHandler.AppendFormatted(rawConditions);
				defaultInterpolatedStringHandler.AppendLiteral("' which couldn't be parsed: ");
				defaultInterpolatedStringHandler.AppendFormatted(error);
				defaultInterpolatedStringHandler.AppendLiteral(".");
				log.Error(defaultInterpolatedStringHandler.ToStringAndClear());
				return false;
			}
			string[] timeParts = ArgUtility.SplitBySpace(rawTimeSpan);
			if (!ArgUtility.TryGetInt(timeParts, 0, out startTime, out error) || !ArgUtility.TryGetInt(timeParts, 1, out endTime, out error))
			{
				IGameLogger log2 = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(79, 4);
				defaultInterpolatedStringHandler.AppendLiteral("Festival '");
				defaultInterpolatedStringHandler.AppendFormatted(festival);
				defaultInterpolatedStringHandler.AppendLiteral("' has preconditions '");
				defaultInterpolatedStringHandler.AppendFormatted(rawConditions);
				defaultInterpolatedStringHandler.AppendLiteral("' with time range '");
				defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", timeParts));
				defaultInterpolatedStringHandler.AppendLiteral("' which couldn't be parsed: ");
				defaultInterpolatedStringHandler.AppendFormatted(error);
				defaultInterpolatedStringHandler.AppendLiteral(".");
				log2.Error(defaultInterpolatedStringHandler.ToStringAndClear());
				return false;
			}
			return true;
		}

		/// <summary>Load a festival if it exists and its preconditions match the current time and the local player's current location.</summary>
		/// <param name="festival">The festival ID to load, matching the asset name under <c>Data/Festivals</c> (like <samp>spring13</samp>).</param>
		/// <param name="ev">The loaded festival event, if it was loaded successfully.</param>
		/// <returns>Returns whether the festival was loaded successfully.</returns>
		public static bool tryToLoadFestival(string festival, out Event ev)
		{
			ev = null;
			string dataAssetName;
			Dictionary<string, string> data;
			string locationName;
			int startTime;
			int endTime;
			if (!tryToLoadFestivalData(festival, out dataAssetName, out data, out locationName, out startTime, out endTime))
			{
				return false;
			}
			if (locationName != Game1.currentLocation.Name || Game1.timeOfDay < startTime || Game1.timeOfDay >= endTime)
			{
				return false;
			}
			ev = new Event
			{
				id = "festival_" + festival,
				isFestival = true,
				festivalDataAssetName = dataAssetName,
				festivalData = data,
				actorPositionsAfterMove = new Dictionary<string, Vector3>(),
				previousAmbientLight = Game1.ambientLight
			};
			ev.festivalData["file"] = festival;
			string rawSetUp;
			if (!ev.TryGetFestivalDataForYear("set-up", out rawSetUp))
			{
				Game1.log.Error("Festival " + ev.id + " doesn't have the required 'set-up' data field.");
			}
			ev.eventCommands = ParseCommands(rawSetUp, ev.farmer);
			Game1.player.festivalScore = 0;
			Game1.setRichPresence("festival", festival);
			return true;
		}

		/// <summary>Try to get an NPC dialogue from the festival data, automatically adjusted to use the closest <c>{key}_y{year}</c> variant if any.</summary>
		/// <param name="npc">The NPC for which to get a dialogue.</param>
		/// <param name="key">The base field key for the dialogue text.</param>
		/// <param name="data">The resulting dialogue instance, or <c>null</c> if the key wasn't found.</param>
		/// <returns>Returns whether a matching dialogue was found.</returns>
		public bool TryGetFestivalDialogueForYear(NPC npc, string key, out Dialogue dialogue)
		{
			string text;
			string actualKey;
			if (TryGetFestivalDataForYear(key, out text, out actualKey))
			{
				dialogue = new Dialogue(npc, festivalDataAssetName + ":" + actualKey, text);
				return true;
			}
			dialogue = null;
			return false;
		}

		/// <summary>Try to get a value from the festival data, automatically adjusted to use the closest <c>{key}_y{year}</c> variant if any.</summary>
		/// <param name="key">The base field key.</param>
		/// <param name="data">The resolved data, or <c>null</c> if the key wasn't found.</param>
		/// <param name="actualKey">The resolved field key, including the variant suffix if applicable, or <c>null</c> if the key wasn't found.</param>
		/// <returns>Returns whether a matching field was found.</returns>
		public bool TryGetFestivalDataForYear(string key, out string data, out string actualKey)
		{
			if (festivalData == null)
			{
				data = null;
				actualKey = null;
				return false;
			}
			int years = 1;
			while (true)
			{
				Dictionary<string, string> dictionary = festivalData;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(2, 2);
				defaultInterpolatedStringHandler.AppendFormatted(key);
				defaultInterpolatedStringHandler.AppendLiteral("_y");
				defaultInterpolatedStringHandler.AppendFormatted(years + 1);
				if (!dictionary.ContainsKey(defaultInterpolatedStringHandler.ToStringAndClear()))
				{
					break;
				}
				years++;
			}
			int selected_year = Game1.year % years;
			if (selected_year == 0)
			{
				selected_year = years;
			}
			string text;
			if (selected_year <= 1)
			{
				text = key;
			}
			else
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(2, 2);
				defaultInterpolatedStringHandler.AppendFormatted(key);
				defaultInterpolatedStringHandler.AppendLiteral("_y");
				defaultInterpolatedStringHandler.AppendFormatted(selected_year);
				text = defaultInterpolatedStringHandler.ToStringAndClear();
			}
			actualKey = text;
			if (festivalData.TryGetValue(actualKey, out data))
			{
				return true;
			}
			actualKey = null;
			data = null;
			return false;
		}

		/// <summary>Get a value from the festival data, automatically adjusted to use the closest <c>{key}_y{year}</c> variant if any.</summary>
		/// <param name="key">The base field key.</param>
		/// <param name="data">The resolved data, or <c>null</c> if the key wasn't found.</param>
		/// <returns>Returns whether a matching field was found.</returns>
		public bool TryGetFestivalDataForYear(string key, out string data)
		{
			string actualKey;
			return TryGetFestivalDataForYear(key, out data, out actualKey);
		}

		/// <summary>Set the location and tile position at which to warp the player once the event ends.</summary>
		/// <param name="warp">The warp whose endpoint to use as the exit location.</param>
		public void setExitLocation(Warp warp)
		{
			setExitLocation(warp.TargetName, warp.TargetX, warp.TargetY);
		}

		/// <summary>Set the location and tile position at which to warp the player once the event ends.</summary>
		/// <param name="location">The location name.</param>
		/// <param name="x">The X tile position.</param>
		/// <param name="y">The Y tile position.</param>
		public void setExitLocation(string location, int x, int y)
		{
			if (Game1.player.locationBeforeForcedEvent.Value == null || Game1.player.locationBeforeForcedEvent.Value == "")
			{
				exitLocation = Game1.getLocationRequest(location);
				Game1.player.positionBeforeEvent = new Vector2(x, y);
			}
		}

		public void endBehaviors(GameLocation location = null)
		{
			endBehaviors(LegacyShims.EmptyArray<string>(), location ?? Game1.currentLocation);
		}

		public void endBehaviors(string[] args, GameLocation location)
		{
			if (Game1.getMusicTrackName().Contains(Game1.currentSeason) && ArgUtility.Get(eventCommands, 0) != "continue")
			{
				Game1.stopMusicTrack(MusicContext.Default);
			}
			switch (ArgUtility.Get(args, 1))
			{
			case "qiSummitCheat":
				Game1.playSound("death");
				Game1.player.health = -1;
				Game1.player.position.X = -99999f;
				Game1.background = null;
				Game1.viewport.X = -999999;
				Game1.viewport.Y = -999999;
				Game1.viewportHold = 6000;
				Game1.eventOver = true;
				CurrentCommand += 2;
				Game1.screenGlowHold = false;
				Game1.screenGlowOnce(Color.Black, true, 1f, 1f);
				break;
			case "Leo":
				if (!isMemory)
				{
					Game1.addMailForTomorrow("leoMoved", true, true);
					Game1.player.team.requestLeoMove.Fire();
				}
				break;
			case "bed":
				Game1.player.Position = Game1.player.mostRecentBed + new Vector2(0f, 64f);
				break;
			case "newDay":
				Game1.player.faceDirection(2);
				setExitLocation(Game1.player.homeLocation, (int)Game1.player.mostRecentBed.X / 64, (int)Game1.player.mostRecentBed.Y / 64);
				if (!Game1.IsMultiplayer)
				{
					exitLocation.OnWarp += delegate
					{
						Game1.NewDay(0f);
						Game1.player.currentLocation.lastTouchActionLocation = new Vector2((int)Game1.player.mostRecentBed.X / 64, (int)Game1.player.mostRecentBed.Y / 64);
					};
				}
				Game1.player.completelyStopAnimatingOrDoingAction();
				if ((bool)Game1.player.bathingClothes)
				{
					Game1.player.changeOutOfSwimSuit();
				}
				Game1.player.swimming.Value = false;
				Game1.player.CanMove = false;
				Game1.changeMusicTrack("none");
				break;
			case "invisibleWarpOut":
			{
				string npcName;
				string error;
				if (!ArgUtility.TryGet(args, 2, out npcName, out error, false))
				{
					LogCommandError(args, error);
					break;
				}
				NPC npc = Game1.getCharacterFromName(npcName);
				if (npc == null)
				{
					LogCommandError(args, "NPC '" + npcName + "' not found");
					break;
				}
				npc.IsInvisible = true;
				npc.daysUntilNotInvisible = 1;
				setExitLocation(location.GetFirstPlayerWarp());
				Game1.fadeScreenToBlack();
				Game1.eventOver = true;
				CurrentCommand += 2;
				Game1.screenGlowHold = false;
				break;
			}
			case "invisible":
			{
				string npcName2;
				string error2;
				if (!ArgUtility.TryGet(args, 2, out npcName2, out error2, false))
				{
					LogCommandError(args, error2);
				}
				else if (!isMemory)
				{
					NPC npc2 = Game1.getCharacterFromName(npcName2);
					if (npc2 == null)
					{
						LogCommandError(args, "NPC '" + npcName2 + "' not found");
						break;
					}
					npc2.IsInvisible = true;
					npc2.daysUntilNotInvisible = 1;
				}
				break;
			}
			case "warpOut":
				setExitLocation(location.GetFirstPlayerWarp());
				Game1.eventOver = true;
				CurrentCommand += 2;
				Game1.screenGlowHold = false;
				break;
			case "dialogueWarpOut":
			{
				string npcName3;
				string error3;
				string dialogue;
				if (!ArgUtility.TryGet(args, 2, out npcName3, out error3, false) || !ArgUtility.TryGet(args, 3, out dialogue, out error3))
				{
					LogCommandError(args, error3);
					break;
				}
				setExitLocation(location.GetFirstPlayerWarp());
				NPC i = Game1.getCharacterFromName(npcName3);
				if (i == null)
				{
					LogCommandError(args, "NPC '" + npcName3 + "' not found");
					break;
				}
				i.CurrentDialogue.Clear();
				i.CurrentDialogue.Push(new Dialogue(i, null, dialogue));
				Game1.eventOver = true;
				CurrentCommand += 2;
				Game1.screenGlowHold = false;
				break;
			}
			case "Maru1":
				(Game1.getCharacterFromName("Demetrius") ?? getActorByName("Demetrius"))?.setNewDialogue("Strings\\StringsFromCSFiles:Event.cs.1018");
				(Game1.getCharacterFromName("Maru") ?? getActorByName("Maru"))?.setNewDialogue("Strings\\StringsFromCSFiles:Event.cs.1020");
				setExitLocation(location.GetFirstPlayerWarp());
				Game1.fadeScreenToBlack();
				Game1.eventOver = true;
				CurrentCommand += 2;
				break;
			case "wedding":
			{
				Game1.RequireCharacter("Lewis").CurrentDialogue.Push(new Dialogue(Game1.getCharacterFromName("Lewis"), "Strings\\StringsFromCSFiles:Event.cs.1025"));
				FarmHouse homeOfFarmer = Utility.getHomeOfFarmer(Game1.player);
				Point porch = homeOfFarmer.getPorchStandingSpot();
				if (homeOfFarmer is Cabin)
				{
					setExitLocation("Farm", porch.X + 1, porch.Y);
				}
				else
				{
					setExitLocation("Farm", porch.X - 1, porch.Y);
				}
				if (!Game1.IsMasterGame)
				{
					break;
				}
				NPC spouse = Game1.getCharacterFromName(farmer.spouse);
				if (spouse != null)
				{
					spouse.ClearSchedule();
					spouse.ignoreScheduleToday = true;
					spouse.shouldPlaySpousePatioAnimation.Value = false;
					spouse.controller = null;
					spouse.temporaryController = null;
					spouse.currentMarriageDialogue.Clear();
					Game1.warpCharacter(spouse, "Farm", Utility.getHomeOfFarmer(farmer).getPorchStandingSpot());
					spouse.faceDirection(2);
					if (Game1.content.LoadStringReturnNullIfNotFound("Strings\\StringsFromCSFiles:" + spouse.Name + "_AfterWedding") != null)
					{
						spouse.addMarriageDialogue("Strings\\StringsFromCSFiles", spouse.Name + "_AfterWedding", false);
					}
					else
					{
						spouse.addMarriageDialogue("Strings\\StringsFromCSFiles", "Game1.cs.2782", false);
					}
				}
				break;
			}
			case "dialogue":
			{
				string npcName4;
				string error4;
				string dialogue2;
				if (!ArgUtility.TryGet(args, 2, out npcName4, out error4, false) || !ArgUtility.TryGet(args, 3, out dialogue2, out error4))
				{
					LogCommandError(args, error4);
					break;
				}
				NPC j = Game1.getCharacterFromName(npcName4);
				if (j == null)
				{
					LogCommandError(args, "NPC '" + npcName4 + "' not found");
					break;
				}
				j.shouldSayMarriageDialogue.Value = false;
				j.currentMarriageDialogue.Clear();
				j.CurrentDialogue.Clear();
				j.CurrentDialogue.Push(new Dialogue(j, null, dialogue2));
				break;
			}
			case "beginGame":
				Game1.gameMode = 3;
				setExitLocation("FarmHouse", 9, 9);
				Game1.NewDay(1000f);
				exitEvent();
				Game1.eventFinished();
				return;
			case "credits":
				Game1.debrisWeather.Clear();
				Game1.isDebrisWeather = false;
				Game1.changeMusicTrack("wedding", false, MusicContext.Event);
				Game1.gameMode = 10;
				CurrentCommand += 2;
				break;
			case "position":
			{
				Vector2 position;
				string error5;
				if (!ArgUtility.TryGetVector2(args, 2, out position, out error5, true))
				{
					LogCommandError(args, error5);
				}
				else if (Game1.player.locationBeforeForcedEvent.Value == null || Game1.player.locationBeforeForcedEvent.Value == "")
				{
					Game1.player.positionBeforeEvent = position;
				}
				break;
			}
			case "islandDepart":
			{
				Game1.player.orientationBeforeEvent = 2;
				string whereIsTodaysFest = Game1.whereIsTodaysFest;
				if (!(whereIsTodaysFest == "Beach"))
				{
					if (whereIsTodaysFest == "Town")
					{
						Game1.player.orientationBeforeEvent = 3;
						setExitLocation("BusStop", 43, 23);
					}
					else
					{
						setExitLocation("BoatTunnel", 6, 9);
					}
				}
				else
				{
					Game1.player.orientationBeforeEvent = 0;
					setExitLocation("Town", 54, 109);
				}
				GameLocation left_location = Game1.currentLocation;
				exitLocation.OnLoad += delegate
				{
					foreach (NPC actor in actors)
					{
						actor.shouldShadowBeOffset = true;
						actor.drawOffset.Y = 0f;
					}
					foreach (Farmer farmerActor in farmerActors)
					{
						farmerActor.shouldShadowBeOffset = true;
						farmerActor.drawOffset.Y = 0f;
					}
					Game1.player.drawOffset = Vector2.Zero;
					Game1.player.shouldShadowBeOffset = false;
					(left_location as IslandSouth)?.ResetBoat();
				};
				break;
			}
			case "tunnelDepart":
				if (Game1.player.hasOrWillReceiveMail("seenBoatJourney"))
				{
					Game1.warpFarmer("IslandSouth", 21, 43, 0);
				}
				break;
			}
			exitEvent();
		}

		public void exitEvent()
		{
			eventFinished = true;
			if (!string.IsNullOrEmpty(id) && id != "-1")
			{
				if (markEventSeen)
				{
					Game1.player.eventsSeen.Add(id);
				}
				Game1.eventsSeenSinceLastLocationChange.Add(id);
			}
			Game1.stopMusicTrack(MusicContext.Event);
			StopTrackedSounds();
			if (id == "1039573")
			{
				Game1.addMail("addedParrotBoy", true, true);
				Game1.player.team.requestAddCharacterEvent.Fire("Leo");
			}
			Game1.player.ignoreCollisions = false;
			Game1.player.canOnlyWalk = false;
			Game1.nonWarpFade = true;
			if (!Game1.fadeIn || Game1.fadeToBlackAlpha >= 1f)
			{
				Game1.fadeScreenToBlack();
			}
			Game1.eventOver = true;
			Game1.fadeToBlack = true;
			Game1.setBGColor(5, 3, 4);
			CurrentCommand += 2;
			Game1.screenGlowHold = false;
			if (isFestival)
			{
				Game1.timeOfDayAfterFade = 2200;
				if (festivalData != null && (isSpecificFestival("summer28") || isSpecificFestival("fall27")))
				{
					Game1.timeOfDayAfterFade = 2400;
				}
				int timePass = Utility.CalculateMinutesBetweenTimes(Game1.timeOfDay, Game1.timeOfDayAfterFade);
				if (Game1.IsMasterGame)
				{
					Point house_entry = Game1.getFarm().GetMainFarmHouseEntry();
					setExitLocation("Farm", house_entry.X, house_entry.Y);
				}
				else
				{
					Point porchSpot = Utility.getHomeOfFarmer(Game1.player).getPorchStandingSpot();
					setExitLocation("Farm", porchSpot.X, porchSpot.Y);
				}
				Game1.player.toolOverrideFunction = null;
				isFestival = false;
				foreach (NPC k in actors)
				{
					if (k != null)
					{
						resetDialogueIfNecessary(k);
					}
				}
				if (Game1.IsMasterGame)
				{
					foreach (NPC j in Utility.getAllVillagers())
					{
						if (j.getSpouse() != null)
						{
							Farmer spouse_farmer = j.getSpouse();
							if (spouse_farmer.isMarriedOrRoommates())
							{
								j.controller = null;
								j.temporaryController = null;
								FarmHouse home_location = Utility.getHomeOfFarmer(spouse_farmer);
								j.Halt();
								Game1.warpCharacter(j, home_location, Utility.PointToVector2(home_location.getSpouseBedSpot(spouse_farmer.spouse)));
								if (home_location.GetSpouseBed() != null)
								{
									FarmHouse.spouseSleepEndFunction(j, Utility.getHomeOfFarmer(spouse_farmer));
								}
								j.ignoreScheduleToday = true;
								if (Game1.timeOfDayAfterFade >= 1800)
								{
									j.currentMarriageDialogue.Clear();
									j.checkForMarriageDialogue(1800, Utility.getHomeOfFarmer(spouse_farmer));
								}
								else if (Game1.timeOfDayAfterFade >= 1100)
								{
									j.currentMarriageDialogue.Clear();
									j.checkForMarriageDialogue(1100, Utility.getHomeOfFarmer(spouse_farmer));
								}
								continue;
							}
						}
						if (j.currentLocation != null && j.defaultMap.Value != null)
						{
							j.doingEndOfRouteAnimation.Value = false;
							j.nextEndOfRouteMessage = null;
							j.endOfRouteMessage.Value = null;
							j.controller = null;
							j.temporaryController = null;
							j.Halt();
							Game1.warpCharacter(j, j.defaultMap, j.DefaultPosition / 64f);
							j.ignoreScheduleToday = true;
						}
					}
				}
				foreach (GameLocation i in Game1.locations)
				{
					foreach (Vector2 position in new List<Vector2>(i.objects.Keys))
					{
						if (i.objects[position].minutesElapsed(timePass))
						{
							i.objects.Remove(position);
						}
					}
					(i as Farm)?.timeUpdate(timePass);
				}
				Game1.player.freezePause = 1500;
			}
			else
			{
				Game1.player.forceCanMove();
			}
		}

		public void resetDialogueIfNecessary(NPC n)
		{
			if (!Game1.player.hasTalkedToFriendToday(n.Name))
			{
				n.resetCurrentDialogue();
			}
			else
			{
				n.CurrentDialogue?.Clear();
			}
		}

		public void incrementCommandAfterFade()
		{
			CurrentCommand++;
			Game1.globalFade = false;
		}

		public void cleanup()
		{
			Game1.ambientLight = previousAmbientLight;
			_festivalTexture = null;
			festivalContent.Unload();
		}

		private void changeLocation(string locationName, int x, int y, Action onComplete = null)
		{
			Event e = Game1.currentLocation.currentEvent;
			Game1.currentLocation.currentEvent = null;
			LocationRequest locationRequest = Game1.getLocationRequest(locationName);
			locationRequest.OnLoad += delegate
			{
				if (!e.isFestival)
				{
					Game1.currentLocation.currentEvent = e;
				}
				temporaryLocation = null;
				onComplete?.Invoke();
				locationRequest.Location.ResetForEvent(this);
			};
			locationRequest.OnWarp += delegate
			{
				farmer.currentLocation = Game1.currentLocation;
				if (e.isFestival)
				{
					Game1.currentLocation.currentEvent = e;
				}
			};
			Game1.warpFarmer(locationRequest, x, y, farmer.FacingDirection);
		}

		/// <summary>Log an error indicating that an event command format is invalid.</summary>
		/// <param name="args">The space-delimited event command string, including the command name.</param>
		/// <param name="error">The error to log.</param>
		/// <param name="willSkip">Whether the event command will be skipped entirely. If false, the event command will be applied without the argument(s) that failed. This only affects the wording of the message logged.</param>
		public void LogCommandError(string[] args, string error, bool willSkip = false)
		{
			IGameLogger log = Game1.log;
			string error2;
			if (!willSkip)
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(48, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Event '");
				defaultInterpolatedStringHandler.AppendFormatted(id);
				defaultInterpolatedStringHandler.AppendLiteral("' has command '");
				defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", args));
				defaultInterpolatedStringHandler.AppendLiteral("' which reported errors: ");
				defaultInterpolatedStringHandler.AppendFormatted(error);
				defaultInterpolatedStringHandler.AppendLiteral(".");
				error2 = defaultInterpolatedStringHandler.ToStringAndClear();
			}
			else
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(51, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Event '");
				defaultInterpolatedStringHandler.AppendFormatted(id);
				defaultInterpolatedStringHandler.AppendLiteral("' has command '");
				defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", args));
				defaultInterpolatedStringHandler.AppendLiteral("' which couldn't be parsed: ");
				defaultInterpolatedStringHandler.AppendFormatted(error);
				defaultInterpolatedStringHandler.AppendLiteral(".");
				error2 = defaultInterpolatedStringHandler.ToStringAndClear();
			}
			log.Error(error2);
		}

		/// <summary>Log an error indicating that a command format is invalid and skip the current command.</summary>
		/// <param name="args">The space-delimited event command string, including the command name.</param>
		/// <param name="error">The error to log.</param>
		/// <param name="hideError">Whether to skip without logging an error message.</param>
		public void LogCommandErrorAndSkip(string[] args, string error, bool hideError = false)
		{
			if (!hideError)
			{
				LogCommandError(args, error, true);
			}
			CurrentCommand++;
		}

		/// <summary>Log an error indicating that the entire event has failed, and immediately stop the event.</summary>
		/// <param name="error">An error message indicating why the event failed.</param>
		/// <param name="e">The exception which caused the error, if applicable.</param>
		public void LogErrorAndHalt(string error, Exception e = null)
		{
			string technicalError = "Error running event script " + fromAssetName + "#" + id;
			Game1.chatBox.addErrorMessage("Event script error: " + error);
			string commandText = GetCurrentCommand();
			if (commandText != null)
			{
				string text = technicalError;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(13, 2);
				defaultInterpolatedStringHandler.AppendLiteral(" on line #");
				defaultInterpolatedStringHandler.AppendFormatted(CurrentCommand);
				defaultInterpolatedStringHandler.AppendLiteral(" (");
				defaultInterpolatedStringHandler.AppendFormatted(commandText);
				defaultInterpolatedStringHandler.AppendLiteral(")");
				technicalError = text + defaultInterpolatedStringHandler.ToStringAndClear();
				ChatBox chatBox = Game1.chatBox;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(11, 2);
				defaultInterpolatedStringHandler.AppendLiteral("On line #");
				defaultInterpolatedStringHandler.AppendFormatted(CurrentCommand);
				defaultInterpolatedStringHandler.AppendLiteral(": ");
				defaultInterpolatedStringHandler.AppendFormatted(commandText);
				chatBox.addErrorMessage(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			Game1.log.Error(technicalError + ".", e);
			skipEvent();
		}

		/// <summary>Log an error indicating that the entire event has failed, and immediately stop the event.</summary>
		/// <param name="e">The exception which caused the error.</param>
		public void LogErrorAndHalt(Exception e)
		{
			LogErrorAndHalt(e?.Message ?? "An unknown error occurred.", e);
		}

		/// <summary>Log an error indicating that an event precondition is invalid.</summary>
		/// <param name="location">The location containing the event.</param>
		/// <param name="eventId">The unique event ID whose preconditions are being checked.</param>
		/// <param name="args">The precondition arguments, including the precondition key at the zeroth index.</param>
		/// <param name="error">The error phrase indicating why the precondition is invalid.</param>
		/// <returns>Returns false to simplify failing the precondition.</returns>
		public static bool LogPreconditionError(GameLocation location, string eventId, string[] args, string error)
		{
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(60, 4);
			defaultInterpolatedStringHandler.AppendLiteral("Event '");
			defaultInterpolatedStringHandler.AppendFormatted(eventId);
			defaultInterpolatedStringHandler.AppendLiteral("' in location '");
			defaultInterpolatedStringHandler.AppendFormatted(location.NameOrUniqueName);
			defaultInterpolatedStringHandler.AppendLiteral("' has invalid event precondition '");
			defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", args));
			defaultInterpolatedStringHandler.AppendLiteral("': ");
			defaultInterpolatedStringHandler.AppendFormatted(error);
			defaultInterpolatedStringHandler.AppendLiteral(".");
			log.Error(defaultInterpolatedStringHandler.ToStringAndClear());
			return false;
		}

		/// <summary>Update the event state.</summary>
		/// <param name="location">The location in which the event is running.</param>
		/// <param name="time">The current game execution time.</param>
		public void Update(GameLocation location, GameTime time)
		{
			try
			{
				if (eventFinished)
				{
					return;
				}
				int num;
				if (CurrentCommand == 0 && !forked)
				{
					num = ((!eventSwitched) ? 1 : 0);
					if (num != 0)
					{
						InitializeEvent(location, time);
					}
				}
				else
				{
					num = 0;
				}
				bool runNextCommand = UpdateBeforeNextCommand(location, time);
				if (num == 0 && runNextCommand)
				{
					CheckForNextCommand(location, time);
				}
			}
			catch (Exception e)
			{
				LogErrorAndHalt(e);
			}
		}

		/// <summary>Initialize the event when it first starts.</summary>
		/// <param name="location">The location in which the event is running.</param>
		/// <param name="time">The current game execution time.</param>
		protected void InitializeEvent(GameLocation location, GameTime time)
		{
			farmer.speed = 2;
			farmer.running = false;
			Game1.eventOver = false;
			string musicId;
			string error;
			string rawCameraPosition;
			string rawCharacterPositions;
			string rawOption;
			if (!ArgUtility.TryGet(eventCommands, 0, out musicId, out error) || !ArgUtility.TryGet(eventCommands, 1, out rawCameraPosition, out error, false) || !ArgUtility.TryGet(eventCommands, 2, out rawCharacterPositions, out error, false) || !ArgUtility.TryGetOptional(eventCommands, 3, out rawOption, out error))
			{
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(58, 3);
				defaultInterpolatedStringHandler.AppendLiteral("Event '");
				defaultInterpolatedStringHandler.AppendFormatted(id);
				defaultInterpolatedStringHandler.AppendLiteral("' has initial fields '");
				defaultInterpolatedStringHandler.AppendFormatted(string.Join("/", eventCommands.Take(3)));
				defaultInterpolatedStringHandler.AppendLiteral("' which couldn't be parsed: ");
				defaultInterpolatedStringHandler.AppendFormatted(error);
				defaultInterpolatedStringHandler.AppendLiteral(".");
				log.Error(defaultInterpolatedStringHandler.ToStringAndClear());
				LogErrorAndHalt("event script is invalid");
				return;
			}
			if (string.IsNullOrWhiteSpace(musicId))
			{
				musicId = "none";
			}
			Point cameraPosition;
			if (rawCameraPosition != "follow")
			{
				string[] cameraParts = ArgUtility.SplitBySpace(rawCameraPosition);
				if (!ArgUtility.TryGetPoint(cameraParts, 0, out cameraPosition, out error))
				{
					IGameLogger log2 = Game1.log;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(118, 4);
					defaultInterpolatedStringHandler.AppendLiteral("Event '");
					defaultInterpolatedStringHandler.AppendFormatted(id);
					defaultInterpolatedStringHandler.AppendLiteral("' has initial fields '");
					defaultInterpolatedStringHandler.AppendFormatted(string.Join("/", eventCommands.Take(3)));
					defaultInterpolatedStringHandler.AppendLiteral("' with camera value '");
					defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", cameraParts));
					defaultInterpolatedStringHandler.AppendLiteral("' which couldn't be parsed (must be 'follow' or tile coordinates): ");
					defaultInterpolatedStringHandler.AppendFormatted(error);
					defaultInterpolatedStringHandler.AppendLiteral(".");
					log2.Error(defaultInterpolatedStringHandler.ToStringAndClear());
					LogErrorAndHalt("event script is invalid");
					return;
				}
			}
			else
			{
				cameraPosition = new Point(-1000, -1000);
			}
			if (rawOption == "ignoreEventTileOffset")
			{
				ignoreTileOffsets = true;
			}
			if ((musicId != "none" || !Game1.isRaining) && musicId != "continue" && !musicId.Contains("pause"))
			{
				Game1.changeMusicTrack(musicId, false, MusicContext.Event);
			}
			if (location is Farm && cameraPosition.X >= -1000 && id != "-2" && !ignoreTileOffsets)
			{
				Point p = Farm.getFrontDoorPositionForFarmer(farmer);
				p.X *= 64;
				p.Y *= 64;
				Game1.viewport.X = (Game1.currentLocation.IsOutdoors ? Math.Max(0, Math.Min(p.X - Game1.graphics.GraphicsDevice.Viewport.Width / 2, Game1.currentLocation.Map.DisplayWidth - Game1.graphics.GraphicsDevice.Viewport.Width)) : (p.X - Game1.graphics.GraphicsDevice.Viewport.Width / 2));
				Game1.viewport.Y = (Game1.currentLocation.IsOutdoors ? Math.Max(0, Math.Min(p.Y - Game1.graphics.GraphicsDevice.Viewport.Height / 2, Game1.currentLocation.Map.DisplayHeight - Game1.graphics.GraphicsDevice.Viewport.Height)) : (p.Y - Game1.graphics.GraphicsDevice.Viewport.Height / 2));
			}
			else if (rawCameraPosition != "follow")
			{
				try
				{
					Game1.viewportFreeze = true;
					int centerX = OffsetTileX(cameraPosition.X) * 64 + 32;
					int centerY = OffsetTileY(cameraPosition.Y) * 64 + 32;
					if (centerX < 0)
					{
						Game1.viewport.X = centerX;
						Game1.viewport.Y = centerY;
					}
					else
					{
						Game1.viewport.X = (Game1.currentLocation.IsOutdoors ? Math.Max(0, Math.Min(centerX - Game1.viewport.Width / 2, Game1.currentLocation.Map.DisplayWidth - Game1.viewport.Width)) : (centerX - Game1.viewport.Width / 2));
						Game1.viewport.Y = (Game1.currentLocation.IsOutdoors ? Math.Max(0, Math.Min(centerY - Game1.viewport.Height / 2, Game1.currentLocation.Map.DisplayHeight - Game1.viewport.Height)) : (centerY - Game1.viewport.Height / 2));
					}
					if (centerX > 0 && Game1.graphics.GraphicsDevice.Viewport.Width > Game1.currentLocation.Map.DisplayWidth)
					{
						Game1.viewport.X = (Game1.currentLocation.Map.DisplayWidth - Game1.viewport.Width) / 2;
					}
					if (centerY > 0 && Game1.graphics.GraphicsDevice.Viewport.Height > Game1.currentLocation.Map.DisplayHeight)
					{
						Game1.viewport.Y = (Game1.currentLocation.Map.DisplayHeight - Game1.viewport.Height) / 2;
					}
				}
				catch (Exception)
				{
					forked = true;
					return;
				}
			}
			setUpCharacters(rawCharacterPositions, location);
			trySpecialSetUp(location);
			populateWalkLocationsList();
			CurrentCommand = 3;
		}

		/// <summary>Run any updates needed before checking for the next script command.</summary>
		/// <param name="location">The location in which the event is running.</param>
		/// <param name="time">The current game execution time.</param>
		/// <returns>Returns whether to run the next command.</returns>
		protected bool UpdateBeforeNextCommand(GameLocation location, GameTime time)
		{
			if (skipped || Game1.farmEvent != null)
			{
				return false;
			}
			foreach (NPC l in actors)
			{
				l.update(time, Game1.currentLocation);
				if (l.Sprite.CurrentAnimation != null)
				{
					l.Sprite.animateOnce(time);
				}
			}
			if (aboveMapSprites != null)
			{
				for (int j = aboveMapSprites.Count - 1; j >= 0; j--)
				{
					if (aboveMapSprites[j].update(time))
					{
						aboveMapSprites.RemoveAt(j);
					}
				}
			}
			if (underwaterSprites != null)
			{
				foreach (TemporaryAnimatedSprite underwaterSprite in underwaterSprites)
				{
					underwaterSprite.update(time);
				}
			}
			if (!playerControlSequence)
			{
				farmer.setRunning(false);
			}
			if (npcControllers != null)
			{
				for (int i = npcControllers.Count - 1; i >= 0; i--)
				{
					npcControllers[i].puppet.isCharging = !isFestival;
					if (npcControllers[i].update(time, location, npcControllers))
					{
						npcControllers.RemoveAt(i);
					}
				}
			}
			if (isFestival)
			{
				festivalUpdate(time);
			}
			if (temporaryLocation != null && !Game1.currentLocation.Equals(temporaryLocation))
			{
				temporaryLocation.updateEvenIfFarmerIsntHere(time, true);
			}
			if (!Game1.fadeToBlack || actorPositionsAfterMove.Count > 0 || CurrentCommand > 3 || forked)
			{
				if (eventCommands.Length <= CurrentCommand)
				{
					return false;
				}
				if (viewportTarget != Vector3.Zero)
				{
					int playerSpeed = farmer.speed;
					farmer.speed = (int)viewportTarget.X;
					int oldX = Game1.viewport.X;
					Game1.viewport.X += (int)viewportTarget.X;
					if (oldX > 0 && Game1.viewport.X <= 0 && location.IsOutdoors)
					{
						Game1.viewport.X = 0;
						viewportTarget.X = 0f;
					}
					else if (oldX < location.map.DisplayWidth - Game1.viewport.Width && Game1.viewport.X >= location.Map.DisplayWidth - Game1.viewport.Width)
					{
						Game1.viewport.X = location.Map.DisplayWidth - Game1.viewport.Width;
						viewportTarget.X = 0f;
					}
					if (viewportTarget.X != 0f)
					{
						Game1.updateRainDropPositionForPlayerMovement((!(viewportTarget.X < 0f)) ? 1 : 3, Math.Abs(viewportTarget.X + (float)((farmer.isMoving() && farmer.FacingDirection == 3) ? (-farmer.speed) : ((farmer.isMoving() && farmer.FacingDirection == 1) ? farmer.speed : 0))));
					}
					int oldY = Game1.viewport.Y;
					Game1.viewport.Y += (int)viewportTarget.Y;
					if (oldY > 0 && Game1.viewport.Y <= 0 && location.IsOutdoors)
					{
						Game1.viewport.Y = 0;
						viewportTarget.Y = 0f;
					}
					else if (oldY < location.map.DisplayHeight - Game1.viewport.Height && Game1.viewport.Y >= location.Map.DisplayHeight - Game1.viewport.Height)
					{
						Game1.viewport.Y = location.Map.DisplayHeight - Game1.viewport.Height;
						viewportTarget.Y = 0f;
					}
					farmer.speed = (int)viewportTarget.Y;
					if (viewportTarget.Y != 0f)
					{
						Game1.updateRainDropPositionForPlayerMovement((!(viewportTarget.Y < 0f)) ? 2 : 0, Math.Abs(viewportTarget.Y - (float)((farmer.isMoving() && farmer.FacingDirection == 0) ? (-farmer.speed) : ((farmer.isMoving() && farmer.FacingDirection == 2) ? farmer.speed : 0))));
					}
					farmer.speed = playerSpeed;
					viewportTarget.Z -= time.ElapsedGameTime.Milliseconds;
					if (viewportTarget.Z <= 0f)
					{
						viewportTarget = Vector3.Zero;
					}
				}
				if (actorPositionsAfterMove.Count > 0)
				{
					string[] array = actorPositionsAfterMove.Keys.ToArray();
					foreach (string s in array)
					{
						Microsoft.Xna.Framework.Rectangle targetTile = new Microsoft.Xna.Framework.Rectangle((int)actorPositionsAfterMove[s].X * 64, (int)actorPositionsAfterMove[s].Y * 64, 64, 64);
						targetTile.Inflate(-4, 0);
						NPC actor = getActorByName(s);
						if (actor != null)
						{
							Microsoft.Xna.Framework.Rectangle bounds3 = actor.GetBoundingBox();
							if (bounds3.Width > 64)
							{
								targetTile.Inflate(4, 0);
								targetTile.Width = bounds3.Width + 4;
								targetTile.Height = bounds3.Height + 4;
								targetTile.X += 8;
								targetTile.Y += 16;
							}
						}
						int farmerNumber;
						if (IsFarmerActorId(s, out farmerNumber))
						{
							Farmer f = GetFarmerActor(farmerNumber);
							if (f != null)
							{
								Microsoft.Xna.Framework.Rectangle bounds2 = f.GetBoundingBox();
								float moveSpeed = f.getMovementSpeed();
								if (targetTile.Contains(bounds2) && (((float)(bounds2.Y - targetTile.Top) <= 16f + moveSpeed && f.FacingDirection != 2) || ((float)(targetTile.Bottom - bounds2.Bottom) <= 16f + moveSpeed && f.FacingDirection == 2)))
								{
									f.showNotCarrying();
									f.Halt();
									f.faceDirection((int)actorPositionsAfterMove[s].Z);
									f.FarmerSprite.StopAnimation();
									f.Halt();
									actorPositionsAfterMove.Remove(s);
								}
								else if (f != null)
								{
									f.canOnlyWalk = false;
									f.setRunning(false, true);
									f.canOnlyWalk = true;
									f.lastPosition = farmer.Position;
									f.MovePosition(time, Game1.viewport, location);
								}
							}
							continue;
						}
						foreach (NPC k in actors)
						{
							Microsoft.Xna.Framework.Rectangle bounds = k.GetBoundingBox();
							if (k.Name.Equals(s) && targetTile.Contains(bounds) && bounds.Y - targetTile.Top <= 16)
							{
								k.Halt();
								k.faceDirection((int)actorPositionsAfterMove[s].Z);
								actorPositionsAfterMove.Remove(s);
								break;
							}
							if (k.Name.Equals(s))
							{
								if (k is Monster)
								{
									k.MovePosition(time, Game1.viewport, location);
								}
								else
								{
									k.MovePosition(time, Game1.viewport, null);
								}
								break;
							}
						}
					}
					if (actorPositionsAfterMove.Count == 0)
					{
						if (continueAfterMove)
						{
							continueAfterMove = false;
						}
						else
						{
							CurrentCommand++;
						}
					}
					if (!continueAfterMove)
					{
						return false;
					}
				}
			}
			return true;
		}

		protected void CheckForNextCommand(GameLocation location, GameTime time)
		{
			string[] args = ArgUtility.SplitBySpaceQuoteAware(eventCommands[Math.Min(eventCommands.Length - 1, CurrentCommand)]);
			bool num = ArgUtility.Get(args, 0)?.StartsWith("--") ?? false;
			if (temporaryLocation != null && !Game1.currentLocation.Equals(temporaryLocation))
			{
				temporaryLocation.updateEvenIfFarmerIsntHere(time, true);
			}
			if (num)
			{
				CurrentCommand++;
			}
			else
			{
				tryEventCommand(location, time, args);
			}
		}

		/// <summary>Get the text of the current event command being executed.</summary>
		public string GetCurrentCommand()
		{
			return ArgUtility.Get(eventCommands, currentCommand);
		}

		/// <summary>Replace the command at the current index.</summary>
		/// <param name="command">The new command text to parse.</param>
		public void ReplaceCurrentCommand(string command)
		{
			if (ArgUtility.HasIndex(eventCommands, currentCommand))
			{
				eventCommands[currentCommand] = command;
			}
		}

		/// <summary>Replace the entire list of commands with the given values.</summary>
		/// <param name="commands">The new commands to parse.</param>
		public void ReplaceAllCommands(params string[] commands)
		{
			eventCommands = commands;
			CurrentCommand = 0;
		}

		/// <summary>Add a new event command to run after the current one.</summary>
		/// <param name="command">The new command text to parse.</param>
		public void InsertNextCommand(string command)
		{
			int index = currentCommand + 1;
			List<string> commands = eventCommands.ToList();
			if (index <= commands.Count)
			{
				commands.Insert(index, command);
			}
			else
			{
				commands.Add(command);
			}
			eventCommands = commands.ToArray();
		}

		/// <summary>Register a sound cue to remove when the event ends.</summary>
		/// <param name="cue">The audio cue to register.</param>
		public void TrackSound(ICue cue)
		{
			if (cue != null)
			{
				List<ICue> sounds;
				if (!CustomSounds.TryGetValue(cue.Name, out sounds))
				{
					sounds = (CustomSounds[cue.Name] = new List<ICue>());
				}
				sounds.Add(cue);
			}
		}

		/// <summary>Stop a tracked sound registered via <see cref="M:StardewValley.Event.TrackSound(StardewValley.ICue)" />.</summary>
		/// <param name="cueId">The audio cue ID to stop.</param>
		/// <param name="immediate">Whether to stop the sound immediately, instead of letting it finish the current loop.</param>
		public void StopTrackedSound(string cueId, bool immediate)
		{
			List<ICue> sounds;
			if (cueId == null || !CustomSounds.TryGetValue(cueId, out sounds))
			{
				return;
			}
			foreach (ICue item in sounds)
			{
				item.Stop(immediate ? AudioStopOptions.Immediate : AudioStopOptions.AsAuthored);
			}
			if (immediate)
			{
				CustomSounds.Remove(cueId);
			}
		}

		/// <summary>Stop all tracked sounds registered via <see cref="M:StardewValley.Event.TrackSound(StardewValley.ICue)" />.</summary>
		public void StopTrackedSounds()
		{
			foreach (List<ICue> value in CustomSounds.Values)
			{
				foreach (ICue item in value)
				{
					item.Stop(AudioStopOptions.Immediate);
				}
			}
			CustomSounds.Clear();
		}

		public bool isTileWalkedOn(int x, int y)
		{
			return characterWalkLocations.Contains(new Vector2(x, y));
		}

		private void populateWalkLocationsList()
		{
			characterWalkLocations.Add(farmer.Tile);
			foreach (NPC j in actors)
			{
				characterWalkLocations.Add(j.Tile);
			}
			for (int i = 2; i < eventCommands.Length; i++)
			{
				string[] args = ArgUtility.SplitBySpace(eventCommands[i]);
				if (ArgUtility.Get(args, 0) != "move" || (ArgUtility.Get(args, 1) == "false" && args.Length == 2))
				{
					continue;
				}
				string actorName;
				string error;
				Point position;
				if (!ArgUtility.TryGet(args, 1, out actorName, out error) || !ArgUtility.TryGetPoint(args, 2, out position, out error))
				{
					LogCommandError(args, error);
					continue;
				}
				Character character = (IsCurrentFarmerActorId(actorName) ? ((Character)farmer) : ((Character)getActorByName(actorName)));
				if (character != null)
				{
					Vector2 pos = character.Tile;
					for (int x = 0; x < Math.Abs(position.X); x++)
					{
						pos.X += Math.Sign(position.X);
						characterWalkLocations.Add(pos);
					}
					for (int y = 0; y < Math.Abs(position.Y); y++)
					{
						pos.Y += Math.Sign(position.Y);
						characterWalkLocations.Add(pos);
					}
				}
			}
		}

		/// <summary>Get an NPC actor in the event by its name.</summary>
		/// <param name="name">The actor name.</param>
		/// <param name="legacyReplaceUnderscores">Whether to try replacing underscores with spaces in <paramref name="name" /> if an exact match wasn't found. This is only meant for backwards compatibility, for event commands which predate argument quoting.</param>
		/// <returns>Returns the matching actor, else <c>null</c>.</returns>
		public NPC getActorByName(string name, bool legacyReplaceUnderscores = false)
		{
			bool isOptionalNpc;
			return getActorByName(name, out isOptionalNpc, legacyReplaceUnderscores);
		}

		/// <summary>Get an NPC actor in the event by its name.</summary>
		/// <param name="name">The actor name.</param>
		/// <param name="isOptionalNpc">Whether the NPC is marked optional, so no error should be shown if they're missing.</param>
		/// <param name="legacyReplaceUnderscores">Whether to try replacing underscores with spaces in <paramref name="name" /> if an exact match wasn't found. This is only meant for backwards compatibility, for event commands which predate argument quoting.</param>
		/// <returns>Returns the matching actor, else <c>null</c>.</returns>
		public NPC getActorByName(string name, out bool isOptionalNpc, bool legacyReplaceUnderscores = false)
		{
			isOptionalNpc = name?.EndsWith('?') ?? false;
			if (isOptionalNpc)
			{
				name = name.Substring(0, name.Length - 1);
			}
			if (name != null)
			{
				if (name == "spouse")
				{
					name = farmer.spouse;
				}
				foreach (NPC j in actors)
				{
					if (j.Name == name)
					{
						return j;
					}
				}
				if (legacyReplaceUnderscores)
				{
					string newName = name.Replace('_', ' ');
					if (newName != name)
					{
						foreach (NPC i in actors)
						{
							if (i.Name == newName)
							{
								return i;
							}
						}
					}
				}
				return null;
			}
			return null;
		}

		private void addActor(string name, int x, int y, int facingDirection, GameLocation location)
		{
			bool isOptionalNpc;
			NPC duplicate = getActorByName(name, out isOptionalNpc);
			if (duplicate != null)
			{
				duplicate.Position = new Vector2(x * 64, y * 64);
				duplicate.FacingDirection = facingDirection;
				return;
			}
			if (isOptionalNpc)
			{
				name = name.Substring(0, name.Length - 1);
				CharacterData data;
				if (!NPC.TryGetData(name, out data) || !GameStateQuery.CheckConditions(data.UnlockConditions))
				{
					return;
				}
			}
			NPC i;
			try
			{
				string spriteName = NPC.getTextureNameForCharacter(name);
				Texture2D portrait = null;
				try
				{
					portrait = Game1.content.Load<Texture2D>("Portraits\\" + spriteName);
				}
				catch (Exception)
				{
				}
				int height = ((name.Contains("Dwarf") || name.Equals("Krobus")) ? 96 : 128);
				i = new NPC(new AnimatedSprite("Characters\\" + spriteName, 0, 16, height / 4), new Vector2(x * 64, y * 64), location.Name, facingDirection, name, portrait, true);
				i.EventActor = true;
				if (isFestival)
				{
					try
					{
						Dialogue dialogue;
						if (TryGetFestivalDialogueForYear(i, i.Name, out dialogue))
						{
							i.setNewDialogue(dialogue);
						}
					}
					catch (Exception)
					{
					}
				}
				if (i.name.Equals("MrQi"))
				{
					i.displayName = Game1.content.LoadString("Strings\\NPCNames:MisterQi");
				}
			}
			catch (Exception ex)
			{
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Event '");
				defaultInterpolatedStringHandler.AppendFormatted(id);
				defaultInterpolatedStringHandler.AppendLiteral("' has character '");
				defaultInterpolatedStringHandler.AppendFormatted(name);
				defaultInterpolatedStringHandler.AppendLiteral("' which couldn't be added.");
				log.Error(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
				return;
			}
			i.EventActor = true;
			actors.Add(i);
		}

		/// <summary>Get the player in the event matching a farmer number, if found.</summary>
		/// <param name="farmerNumber">The farmer number. This can be -1 (current player), 1 (main player), or higher numbers for farmhands.</param>
		/// <returns>Returns the matching event actor or real farmer, or <c>null</c> if neither was found.</returns>
		public Farmer GetFarmerActor(int farmerNumber)
		{
			Farmer player = ((farmerNumber < 1) ? farmer : Utility.getFarmerFromFarmerNumber(farmerNumber));
			if (player == null)
			{
				return null;
			}
			foreach (Farmer actor in farmerActors)
			{
				if (actor.UniqueMultiplayerID == player.UniqueMultiplayerID)
				{
					return actor;
				}
			}
			return player;
		}

		/// <summary>Get whether an actor ID is the current player.</summary>
		/// <param name="actor">The actor ID to check.</param>
		public bool IsCurrentFarmerActorId(string actor)
		{
			int farmerNumber;
			if (IsFarmerActorId(actor, out farmerNumber))
			{
				return IsCurrentFarmerActorId(farmerNumber);
			}
			return false;
		}

		/// <summary>Get whether an actor ID is the current player.</summary>
		/// <param name="farmerNumber">The farmer number to check.</param>
		public bool IsCurrentFarmerActorId(int farmerNumber)
		{
			if (farmerNumber >= 1)
			{
				return farmerNumber == Utility.getFarmerNumberFromFarmer(Game1.player);
			}
			return true;
		}

		/// <summary>Get whether an actor ID is a farmer like <c>farmer</c> (current player) or <samp>farmer3</samp> (player #3), regardless of whether that player is present.</summary>
		/// <param name="actor">The actor ID to check.</param>
		/// <param name="farmerNumber">The parsed farmer number, if applicable. This can be <samp>-1</samp> (current player), 1 (main player), or higher numbers for farmhands.</param>
		public bool IsFarmerActorId(string actor, out int farmerNumber)
		{
			if (actor == null || !actor.StartsWith("farmer"))
			{
				farmerNumber = -1;
				return false;
			}
			if (actor.Length == "farmer".Length)
			{
				farmerNumber = -1;
				return true;
			}
			return int.TryParse(actor.Substring("farmer".Length), out farmerNumber);
		}

		public Character getCharacterByName(string name)
		{
			int farmerNumber;
			if (IsFarmerActorId(name, out farmerNumber))
			{
				return GetFarmerActor(farmerNumber);
			}
			foreach (NPC i in actors)
			{
				if (i.Name.Equals(name))
				{
					return i;
				}
			}
			return null;
		}

		public Vector3 getPositionAfterMove(Character c, int xMove, int yMove, int facingDirection)
		{
			Vector2 tileLocation = c.Tile;
			return new Vector3(tileLocation.X + (float)xMove, tileLocation.Y + (float)yMove, facingDirection);
		}

		private void trySpecialSetUp(GameLocation location)
		{
			switch (id)
			{
			case "739330":
				if (!Game1.player.friendshipData.ContainsKey("Willy"))
				{
					Game1.player.friendshipData.Add("Willy", new Friendship(0));
				}
				Game1.player.checkForQuestComplete(Game1.getCharacterFromName("Willy"), -1, -1, null, null, 5);
				break;
			case "9333220":
			{
				FarmHouse house = location as FarmHouse;
				if (house != null && house.upgradeLevel == 1)
				{
					farmer.Position = new Vector2(1920f, 400f);
					getActorByName("Sebastian").setTilePosition(31, 6);
				}
				break;
			}
			case "4324303":
			{
				FarmHouse house3 = location as FarmHouse;
				if (house3 == null)
				{
					break;
				}
				Point bed_spot = house3.GetPlayerBedSpot();
				bed_spot.X--;
				farmer.Position = new Vector2(bed_spot.X * 64, bed_spot.Y * 64 + 16);
				getActorByName("Penny").setTilePosition(bed_spot.X - 1, bed_spot.Y);
				Microsoft.Xna.Framework.Rectangle room = new Microsoft.Xna.Framework.Rectangle(23, 12, 10, 10);
				if (house3.upgradeLevel == 1)
				{
					room = new Microsoft.Xna.Framework.Rectangle(20, 3, 8, 7);
				}
				Point room_center = room.Center;
				if (!room.Contains(Game1.player.TilePoint))
				{
					List<string> commands = new List<string>(eventCommands);
					int command_index = 56;
					commands.Insert(command_index, "globalFade 0.03");
					command_index++;
					commands.Insert(command_index, "beginSimultaneousCommand");
					command_index++;
					commands.Insert(command_index, "viewport " + room_center.X + " " + room_center.Y);
					command_index++;
					commands.Insert(command_index, "globalFadeToClear 0.03");
					command_index++;
					commands.Insert(command_index, "endSimultaneousCommand");
					command_index++;
					commands.Insert(command_index, "pause 2000");
					command_index++;
					commands.Insert(command_index, "globalFade 0.03");
					command_index++;
					commands.Insert(command_index, "beginSimultaneousCommand");
					command_index++;
					commands.Insert(command_index, "viewport " + Game1.player.TilePoint.X + " " + Game1.player.TilePoint.Y);
					command_index++;
					commands.Insert(command_index, "globalFadeToClear 0.03");
					command_index++;
					commands.Insert(command_index, "endSimultaneousCommand");
					command_index++;
					eventCommands = commands.ToArray();
				}
				for (int i = 0; i < eventCommands.Length; i++)
				{
					if (!eventCommands[i].StartsWith("makeInvisible"))
					{
						continue;
					}
					string[] args = ArgUtility.SplitBySpace(eventCommands[i]);
					Point tile;
					string error;
					if (!ArgUtility.TryGetPoint(args, 1, out tile, out error))
					{
						LogCommandError(args, error);
						continue;
					}
					args[1] = (tile.X - 26 + bed_spot.X).ToString() ?? "";
					args[2] = (tile.Y - 13 + bed_spot.Y).ToString() ?? "";
					if (location.getObjectAtTile(tile.X, tile.Y) == house3.GetPlayerBed())
					{
						eventCommands[i] = "makeInvisible -1000 -1000";
					}
					else
					{
						eventCommands[i] = string.Join(" ", args);
					}
				}
				break;
			}
			case "4325434":
			{
				FarmHouse house4 = location as FarmHouse;
				if (house4 != null && house4.upgradeLevel == 1)
				{
					farmer.Position = new Vector2(512f, 336f);
					getActorByName("Penny").setTilePosition(5, 5);
				}
				break;
			}
			case "3912132":
			{
				FarmHouse house7 = location as FarmHouse;
				if (house7 == null)
				{
					break;
				}
				Point bed_spot2 = house7.GetPlayerBedSpot();
				bed_spot2.X--;
				if (!location.CanItemBePlacedHere(Utility.PointToVector2(bed_spot2) + new Vector2(-2f, 0f)))
				{
					bed_spot2.X++;
				}
				farmer.setTileLocation(Utility.PointToVector2(bed_spot2));
				getActorByName("Elliott").setTileLocation(Utility.PointToVector2(bed_spot2) + new Vector2(-2f, 0f));
				for (int j = 0; j < eventCommands.Length; j++)
				{
					if (!eventCommands[j].StartsWith("makeInvisible"))
					{
						continue;
					}
					string[] args2 = ArgUtility.SplitBySpace(eventCommands[j]);
					Point tile2;
					string error2;
					if (!ArgUtility.TryGetPoint(args2, 1, out tile2, out error2))
					{
						LogCommandError(args2, error2);
						continue;
					}
					args2[1] = (tile2.X - 26 + bed_spot2.X).ToString() ?? "";
					args2[2] = (tile2.Y - 13 + bed_spot2.Y).ToString() ?? "";
					if (location.getObjectAtTile(tile2.X, tile2.Y) == house7.GetPlayerBed())
					{
						eventCommands[j] = "makeInvisible -1000 -1000";
					}
					else
					{
						eventCommands[j] = string.Join(" ", args2);
					}
				}
				break;
			}
			case "8675611":
			{
				FarmHouse house6 = location as FarmHouse;
				if (house6 != null && house6.upgradeLevel == 1)
				{
					getActorByName("Haley").setTilePosition(4, 5);
					farmer.Position = new Vector2(320f, 336f);
				}
				break;
			}
			case "3917601":
			{
				DecoratableLocation decoratableLocation = location as DecoratableLocation;
				if (decoratableLocation == null)
				{
					break;
				}
				foreach (Furniture f in decoratableLocation.furniture)
				{
					if ((int)f.furniture_type == 14 && !location.IsTileBlockedBy(f.TileLocation + new Vector2(0f, 1f), CollisionMask.All, CollisionMask.All) && !location.IsTileBlockedBy(f.TileLocation + new Vector2(1f, 1f), CollisionMask.All, CollisionMask.All))
					{
						getActorByName("Emily").setTilePosition((int)f.TileLocation.X, (int)f.TileLocation.Y + 1);
						farmer.Position = new Vector2((f.TileLocation.X + 1f) * 64f, (f.tileLocation.Y + 1f) * 64f + 16f);
						f.isOn.Value = true;
						f.setFireplace(false);
						return;
					}
				}
				FarmHouse house5 = location as FarmHouse;
				if (house5 != null && house5.upgradeLevel == 1)
				{
					getActorByName("Emily").setTilePosition(4, 5);
					farmer.Position = new Vector2(320f, 336f);
				}
				break;
			}
			case "3917666":
			{
				FarmHouse house2 = location as FarmHouse;
				if (house2 != null && house2.upgradeLevel == 1)
				{
					getActorByName("Maru").setTilePosition(4, 5);
					farmer.Position = new Vector2(320f, 336f);
				}
				break;
			}
			}
		}

		private void setUpCharacters(string description, GameLocation location)
		{
			farmer.Halt();
			if ((Game1.player.locationBeforeForcedEvent.Value == null || Game1.player.locationBeforeForcedEvent.Value == "") && !isMemory)
			{
				Game1.player.positionBeforeEvent = Game1.player.Tile;
				Game1.player.orientationBeforeEvent = Game1.player.FacingDirection;
			}
			string[] args = ArgUtility.SplitBySpace(description);
			for (int i = 0; i < args.Length; i += 4)
			{
				string actorName;
				string error;
				Point tile;
				int direction;
				if (!ArgUtility.TryGet(args, i, out actorName, out error) || !ArgUtility.TryGetPoint(args, i + 1, out tile, out error) || !ArgUtility.TryGetInt(args, i + 3, out direction, out error))
				{
					IGameLogger log = Game1.log;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(63, 3);
					defaultInterpolatedStringHandler.AppendLiteral("Event '");
					defaultInterpolatedStringHandler.AppendFormatted(id);
					defaultInterpolatedStringHandler.AppendLiteral("' has character positions '");
					defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", args));
					defaultInterpolatedStringHandler.AppendLiteral("' which couldn't be parsed: ");
					defaultInterpolatedStringHandler.AppendFormatted(error);
					defaultInterpolatedStringHandler.AppendLiteral(".");
					log.Error(defaultInterpolatedStringHandler.ToStringAndClear());
					continue;
				}
				int farmerNumber;
				bool isFarmerId = IsFarmerActorId(actorName, out farmerNumber);
				bool isCurrentFarmer = isFarmerId && IsCurrentFarmerActorId(farmerNumber);
				if (tile.X == -1 && !isCurrentFarmer)
				{
					foreach (NPC j in location.characters)
					{
						if (j.Name == actorName)
						{
							actors.Add(j);
						}
					}
				}
				else if (actorName != "farmer")
				{
					if (actorName == "otherFarmers")
					{
						int x2 = OffsetTileX(tile.X);
						int y2 = OffsetTileY(tile.Y);
						foreach (Farmer f2 in Game1.getOnlineFarmers())
						{
							if (f2.UniqueMultiplayerID != farmer.UniqueMultiplayerID)
							{
								Farmer fake2 = f2.CreateFakeEventFarmer();
								fake2.completelyStopAnimatingOrDoingAction();
								fake2.hidden.Value = false;
								fake2.faceDirection(direction);
								fake2.setTileLocation(new Vector2(x2, y2));
								fake2.currentLocation = Game1.currentLocation;
								x2++;
								farmerActors.Add(fake2);
							}
						}
						continue;
					}
					if (isFarmerId)
					{
						int x = OffsetTileX(tile.X);
						int y = OffsetTileY(tile.Y);
						Farmer f = GetFarmerActor(farmerNumber);
						if (f != null)
						{
							Farmer fake = f.CreateFakeEventFarmer();
							fake.completelyStopAnimatingOrDoingAction();
							fake.hidden.Value = false;
							fake.faceDirection(direction);
							fake.setTileLocation(new Vector2(x, y));
							fake.currentLocation = Game1.currentLocation;
							fake.isFakeEventActor = true;
							farmerActors.Add(fake);
						}
						continue;
					}
					string name = ((!(actorName == "spouse")) ? actorName : farmer.spouse);
					switch (actorName)
					{
					case "cat":
					{
						Pet cat = new Pet(OffsetTileX(tile.X), OffsetTileY(tile.Y), Game1.player.whichPetBreed, "Cat");
						cat.Name = "Cat";
						cat.position.X -= 32f;
						actors.Add(cat);
						continue;
					}
					case "dog":
					{
						Pet dog = new Pet(OffsetTileX(tile.X), OffsetTileY(tile.Y), Game1.player.whichPetBreed, "Dog");
						dog.Name = "Dog";
						dog.position.X -= 42f;
						actors.Add(dog);
						continue;
					}
					case "pet":
					{
						Pet pet = new Pet(OffsetTileX(tile.X), OffsetTileY(tile.Y), Game1.player.whichPetBreed, Game1.player.whichPetType);
						pet.Name = "PetActor";
						PetData data;
						if (Pet.TryGetData(Game1.player.whichPetType, out data))
						{
							pet.Position = new Vector2(pet.Position.X + (float)data.EventOffset.X, pet.Position.Y + (float)data.EventOffset.Y);
						}
						actors.Add(pet);
						continue;
					}
					case "golem":
					{
						NPC golem = new NPC(new AnimatedSprite("Characters\\Monsters\\Wilderness Golem", 0, 16, 24), OffsetPosition(new Vector2(tile.X, tile.Y) * 64f), 0, "Golem");
						golem.AllowDynamicAppearance = false;
						actors.Add(golem);
						continue;
					}
					case "Junimo":
						actors.Add(new Junimo(OffsetPosition(new Vector2(tile.X * 64, tile.Y * 64 - 32)), Game1.currentLocation.Name.Equals("AbandonedJojaMart") ? 6 : (-1))
						{
							Name = "Junimo",
							EventActor = true
						});
						continue;
					}
					int xPos = OffsetTileX(tile.X);
					int yPos = OffsetTileY(tile.Y);
					int facingDir = direction;
					if (location is Farm && id != "-2" && !ignoreTileOffsets)
					{
						xPos = Farm.getFrontDoorPositionForFarmer(farmer).X;
						yPos = Farm.getFrontDoorPositionForFarmer(farmer).Y + 2;
						facingDir = 0;
					}
					addActor(name, xPos, yPos, facingDir, location);
				}
				else if (tile.X != -1)
				{
					farmer.position.X = OffsetPositionX(tile.X * 64);
					farmer.position.Y = OffsetPositionY(tile.Y * 64 + 16);
					farmer.faceDirection(direction);
					if (location is Farm && id != "-2" && !ignoreTileOffsets)
					{
						farmer.position.X = Farm.getFrontDoorPositionForFarmer(farmer).X * 64;
						farmer.position.Y = (Farm.getFrontDoorPositionForFarmer(farmer).Y + 1) * 64;
						farmer.faceDirection(2);
					}
					farmer.FarmerSprite.StopAnimation();
				}
			}
		}

		private void beakerSmashEndFunction(int extraInfo)
		{
			Game1.playSound("breakingGlass");
			Game1.currentLocation.TemporarySprites.Add(new TemporaryAnimatedSprite(47, new Vector2(9f, 16f) * 64f, Color.LightBlue, 10));
			Game1.currentLocation.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(400, 3008, 64, 64), 99999f, 2, 0, new Vector2(9f, 16f) * 64f, false, false, 0.01f, 0f, Color.LightBlue, 1f, 0f, 0f, 0f)
			{
				delayBeforeAnimationStart = 700
			});
			Game1.currentLocation.TemporarySprites.Add(new TemporaryAnimatedSprite(46, new Vector2(9f, 16f) * 64f, Color.White * 0.75f, 10)
			{
				motion = new Vector2(0f, -1f)
			});
		}

		private void eggSmashEndFunction(int extraInfo)
		{
			Game1.playSound("slimedead");
			Game1.currentLocation.TemporarySprites.Add(new TemporaryAnimatedSprite(47, new Vector2(9f, 16f) * 64f, Color.White, 10));
			Game1.currentLocation.TemporarySprites.Add(new TemporaryAnimatedSprite(177, 99999f, 9999, 0, new Vector2(6f, 5f) * 64f, false, false)
			{
				layerDepth = 1E-06f
			});
		}

		private void balloonInSky(int extraInfo)
		{
			TemporaryAnimatedSprite t = Game1.currentLocation.getTemporarySpriteByID(2);
			if (t != null)
			{
				t.motion = Vector2.Zero;
			}
			t = Game1.currentLocation.getTemporarySpriteByID(1);
			if (t != null)
			{
				t.motion = Vector2.Zero;
			}
		}

		private void marcelloBalloonLand(int extraInfo)
		{
			Game1.playSound("thudStep");
			Game1.playSound("dirtyHit");
			TemporaryAnimatedSprite t = Game1.currentLocation.getTemporarySpriteByID(2);
			if (t != null)
			{
				t.motion = Vector2.Zero;
			}
			t = Game1.currentLocation.getTemporarySpriteByID(3);
			if (t != null)
			{
				t.scaleChange = 0f;
			}
			Game1.currentLocation.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(0, 2944, 64, 64), 120f, 8, 1, (new Vector2(25f, 39f) + eventPositionTileOffset) * 64f + new Vector2(-32f, 32f), false, true, 1f, 0f, Color.White, 1f, 0f, 0f, 0f));
			Game1.currentLocation.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(0, 2944, 64, 64), 120f, 8, 1, (new Vector2(27f, 39f) + eventPositionTileOffset) * 64f + new Vector2(0f, 48f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
			{
				delayBeforeAnimationStart = 300
			});
			CurrentCommand++;
		}

		private void samPreOllie(int extraInfo)
		{
			getActorByName("Sam").Sprite.currentFrame = 27;
			farmer.faceDirection(0);
			TemporaryAnimatedSprite temporarySpriteByID = Game1.currentLocation.getTemporarySpriteByID(92473);
			temporarySpriteByID.xStopCoordinate = 1408;
			temporarySpriteByID.reachedStopCoordinate = samOllie;
			temporarySpriteByID.motion = new Vector2(2f, 0f);
		}

		private void samOllie(int extraInfo)
		{
			Game1.playSound("crafting");
			getActorByName("Sam").Sprite.currentFrame = 26;
			TemporaryAnimatedSprite temporarySpriteByID = Game1.currentLocation.getTemporarySpriteByID(92473);
			temporarySpriteByID.currentNumberOfLoops = 0;
			temporarySpriteByID.totalNumberOfLoops = 1;
			temporarySpriteByID.motion.Y = -9f;
			temporarySpriteByID.motion.X = 2f;
			temporarySpriteByID.acceleration = new Vector2(0f, 0.4f);
			temporarySpriteByID.animationLength = 1;
			temporarySpriteByID.interval = 530f;
			temporarySpriteByID.timer = 0f;
			temporarySpriteByID.endFunction = samGrind;
			temporarySpriteByID.destroyable = false;
		}

		private void samGrind(int extraInfo)
		{
			Game1.playSound("hammer");
			getActorByName("Sam").Sprite.currentFrame = 28;
			TemporaryAnimatedSprite temporarySpriteByID = Game1.currentLocation.getTemporarySpriteByID(92473);
			temporarySpriteByID.currentNumberOfLoops = 0;
			temporarySpriteByID.totalNumberOfLoops = 9999;
			temporarySpriteByID.motion.Y = 0f;
			temporarySpriteByID.motion.X = 2f;
			temporarySpriteByID.acceleration = new Vector2(0f, 0f);
			temporarySpriteByID.animationLength = 1;
			temporarySpriteByID.interval = 99999f;
			temporarySpriteByID.timer = 0f;
			temporarySpriteByID.xStopCoordinate = 1664;
			temporarySpriteByID.yStopCoordinate = -1;
			temporarySpriteByID.reachedStopCoordinate = samDropOff;
		}

		private void samDropOff(int extraInfo)
		{
			NPC actorByName = getActorByName("Sam");
			actorByName.Sprite.currentFrame = 31;
			TemporaryAnimatedSprite temporarySpriteByID = Game1.currentLocation.getTemporarySpriteByID(92473);
			temporarySpriteByID.currentNumberOfLoops = 9999;
			temporarySpriteByID.totalNumberOfLoops = 0;
			temporarySpriteByID.motion.Y = 0f;
			temporarySpriteByID.motion.X = 2f;
			temporarySpriteByID.acceleration = new Vector2(0f, 0.4f);
			temporarySpriteByID.animationLength = 1;
			temporarySpriteByID.interval = 99999f;
			temporarySpriteByID.yStopCoordinate = 5760;
			temporarySpriteByID.reachedStopCoordinate = samGround;
			temporarySpriteByID.endFunction = null;
			actorByName.Sprite.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
			{
				new FarmerSprite.AnimationFrame(29, 100),
				new FarmerSprite.AnimationFrame(30, 100),
				new FarmerSprite.AnimationFrame(31, 100),
				new FarmerSprite.AnimationFrame(32, 100)
			});
			actorByName.Sprite.loop = false;
		}

		private void samGround(int extraInfo)
		{
			TemporaryAnimatedSprite temporarySpriteByID = Game1.currentLocation.getTemporarySpriteByID(92473);
			Game1.playSound("thudStep");
			temporarySpriteByID.attachedCharacter = null;
			temporarySpriteByID.reachedStopCoordinate = null;
			temporarySpriteByID.totalNumberOfLoops = -1;
			temporarySpriteByID.interval = 0f;
			temporarySpriteByID.destroyable = true;
			CurrentCommand++;
		}

		private void catchFootball(int extraInfo)
		{
			TemporaryAnimatedSprite temporarySpriteByID = Game1.currentLocation.getTemporarySpriteByID(56232);
			Game1.playSound("fishSlap");
			temporarySpriteByID.motion = new Vector2(2f, -8f);
			temporarySpriteByID.rotationChange = (float)Math.PI / 24f;
			temporarySpriteByID.reachedStopCoordinate = footballLand;
			temporarySpriteByID.yStopCoordinate = 1088;
			farmer.jump();
		}

		private void footballLand(int extraInfo)
		{
			TemporaryAnimatedSprite temporarySpriteByID = Game1.currentLocation.getTemporarySpriteByID(56232);
			Game1.playSound("sandyStep");
			temporarySpriteByID.motion = new Vector2(0f, 0f);
			temporarySpriteByID.rotationChange = 0f;
			temporarySpriteByID.reachedStopCoordinate = null;
			temporarySpriteByID.animationLength = 1;
			temporarySpriteByID.interval = 999999f;
			CurrentCommand++;
		}

		private void parrotSplat(int extraInfo)
		{
			Game1.playSound("drumkit0");
			DelayedAction.playSoundAfterDelay("drumkit5", 100);
			Game1.playSound("slimeHit");
			foreach (TemporaryAnimatedSprite aboveMapSprite in aboveMapSprites)
			{
				aboveMapSprite.alpha = 0f;
			}
			Game1.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(174, 168, 4, 11), 99999f, 1, 99999, new Vector2(1504f, 5568f), false, false, 0.02f, 0.01f, Color.White, 4f, 0f, (float)Math.PI / 2f, (float)Math.PI / 64f)
			{
				motion = new Vector2(2f, -2f),
				acceleration = new Vector2(0f, 0.1f)
			});
			Game1.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(174, 168, 4, 11), 99999f, 1, 99999, new Vector2(1504f, 5568f), false, false, 0.02f, 0.01f, Color.White, 4f, 0f, (float)Math.PI / 4f, (float)Math.PI / 64f)
			{
				motion = new Vector2(-2f, -1f),
				acceleration = new Vector2(0f, 0.1f)
			});
			Game1.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(174, 168, 4, 11), 99999f, 1, 99999, new Vector2(1504f, 5568f), false, false, 0.02f, 0.01f, Color.White, 4f, 0f, (float)Math.PI, (float)Math.PI / 64f)
			{
				motion = new Vector2(1f, 1f),
				acceleration = new Vector2(0f, 0.1f)
			});
			Game1.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(174, 168, 4, 11), 99999f, 1, 99999, new Vector2(1504f, 5568f), false, false, 0.02f, 0.01f, Color.White, 4f, 0f, 0f, (float)Math.PI / 64f)
			{
				motion = new Vector2(-2f, -2f),
				acceleration = new Vector2(0f, 0.1f)
			});
			Game1.currentLocation.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(148, 165, 25, 23), 99999f, 1, 99999, new Vector2(1504f, 5568f), false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
			{
				id = 666
			});
			CurrentCommand++;
		}

		public virtual Vector2 OffsetPosition(Vector2 original)
		{
			return new Vector2(OffsetPositionX(original.X), OffsetPositionY(original.Y));
		}

		public virtual Vector2 OffsetTile(Vector2 original)
		{
			return new Vector2(OffsetTileX((int)original.X), OffsetTileY((int)original.Y));
		}

		public virtual float OffsetPositionX(float original)
		{
			if (original < 0f || ignoreTileOffsets)
			{
				return original;
			}
			return original + eventPositionTileOffset.X * 64f;
		}

		public virtual float OffsetPositionY(float original)
		{
			if (original < 0f || ignoreTileOffsets)
			{
				return original;
			}
			return original + eventPositionTileOffset.Y * 64f;
		}

		public virtual int OffsetTileX(int original)
		{
			if (original < 0 || ignoreTileOffsets)
			{
				return original;
			}
			return (int)((float)original + eventPositionTileOffset.X);
		}

		public virtual int OffsetTileY(int original)
		{
			if (original < 0 || ignoreTileOffsets)
			{
				return original;
			}
			return (int)((float)original + eventPositionTileOffset.Y);
		}

		private void addSpecificTemporarySprite(string key, GameLocation location, string[] args)
		{
			switch (key)
			{
			case "raccoondance2":
			{
				location.removeTemporarySpritesWithIDLocal(9786);
				TemporaryAnimatedSprite temporarySpriteByID = location.getTemporarySpriteByID(9785);
				temporarySpriteByID.sourceRect.Y = 64;
				temporarySpriteByID.sourceRectStartingPos.Y = 64f;
				temporarySpriteByID.currentParentTileIndex = 0;
				temporarySpriteByID.motion.X = 0f;
				temporarySpriteByID.interval *= 2f;
				temporarySpriteByID.timer = 0f;
				temporarySpriteByID.sourceRect.X = 0;
				temporarySpriteByID.position.X -= 32f;
				temporarySpriteByID.position.Y += 8f;
				break;
			}
			case "raccoondance1":
			{
				TemporaryAnimatedSprite temporarySpriteByID2 = location.getTemporarySpriteByID(9786);
				TemporaryAnimatedSprite mrs_raccoon = location.getTemporarySpriteByID(9785);
				temporarySpriteByID2.sourceRect.Y = 96;
				temporarySpriteByID2.sourceRectStartingPos.Y = 96f;
				temporarySpriteByID2.currentParentTileIndex = 1;
				temporarySpriteByID2.motion.X = 0.07f;
				temporarySpriteByID2.timer = 0f;
				mrs_raccoon.sourceRect.Y = 32;
				mrs_raccoon.sourceRectStartingPos.Y = 32f;
				mrs_raccoon.currentParentTileIndex = 1;
				mrs_raccoon.motion.X = -0.07f;
				mrs_raccoon.timer = 0f;
				break;
			}
			case "raccoonbutterflies":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(128, 336, 16, 16), new Vector2(52.5f, 0f) * 64f - new Vector2(131.5f, -60f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 148f,
					animationLength = 4,
					pingPong = true,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f,
					xPeriodic = true,
					xPeriodicRange = 32f,
					xPeriodicLoopTime = 2800f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					yPeriodic = true,
					yPeriodicRange = 8f,
					yPeriodicLoopTime = 3800f,
					overrideLocationDestroy = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(192, 336, 16, 16), new Vector2(56.5f, 0f) * 64f - new Vector2(131.5f, 0f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 148f,
					animationLength = 4,
					pingPong = true,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f,
					xPeriodic = true,
					xPeriodicRange = 32f,
					xPeriodicLoopTime = 2600f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					yPeriodic = true,
					yPeriodicRange = 4f,
					yPeriodicLoopTime = 2900f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(128, 288, 16, 16), new Vector2(53.5f, 0f) * 64f + new Vector2(263f, 24f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 148f,
					animationLength = 4,
					pingPong = true,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f,
					xPeriodic = true,
					xPeriodicRange = 32f,
					xPeriodicLoopTime = 3000f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					yPeriodic = true,
					yPeriodicRange = 6f,
					yPeriodicLoopTime = 3100f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(192, 288, 16, 16), new Vector2(52.5f, 0f) * 64f + new Vector2(131.5f, 220f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 148f,
					animationLength = 4,
					pingPong = true,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f,
					xPeriodic = true,
					xPeriodicRange = 32f,
					xPeriodicLoopTime = 2400f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					yPeriodic = true,
					yPeriodicRange = 12f,
					yPeriodicLoopTime = 2800f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(64, 288, 16, 16), new Vector2(52.5f, 0f) * 64f + new Vector2(186.5f, 150f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 148f,
					animationLength = 4,
					pingPong = true,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f,
					xPeriodic = true,
					xPeriodicRange = 32f,
					xPeriodicLoopTime = 3400f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					yPeriodic = true,
					yPeriodicRange = 4f,
					yPeriodicLoopTime = 3200f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(128, 96, 16, 16), new Vector2(52.5f, 0f) * 64f + new Vector2(211.5f, 180f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 148f,
					animationLength = 4,
					pingPong = true,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f,
					xPeriodic = true,
					xPeriodicRange = 32f,
					xPeriodicLoopTime = 3500f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					yPeriodic = true,
					yPeriodicRange = 4f,
					yPeriodicLoopTime = 2700f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(192, 112, 16, 16), new Vector2(52.5f, 0f) * 64f - new Vector2(126.5f, -120f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 148f,
					animationLength = 4,
					pingPong = true,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f,
					xPeriodic = true,
					xPeriodicRange = 16f,
					xPeriodicLoopTime = 2500f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					yPeriodic = true,
					yPeriodicRange = 4f,
					yPeriodicLoopTime = 3300f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\critters", new Microsoft.Xna.Framework.Rectangle(128, 288, 16, 16), new Vector2(49.5f, 0f) * 64f - new Vector2(126.5f, -100f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 148f,
					animationLength = 4,
					pingPong = true,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f,
					xPeriodic = true,
					xPeriodicRange = 16f,
					xPeriodicLoopTime = 2200f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					yPeriodic = true,
					yPeriodicRange = 4f,
					yPeriodicLoopTime = 3400f
				});
				TemporaryAnimatedSprite temporarySpriteByID5 = location.getTemporarySpriteByID(9786);
				TemporaryAnimatedSprite mrs_raccoon2 = location.getTemporarySpriteByID(9785);
				temporarySpriteByID5.sourceRect.Y = 224;
				temporarySpriteByID5.sourceRectStartingPos.Y = 224f;
				temporarySpriteByID5.currentParentTileIndex = 3;
				temporarySpriteByID5.timer = 0f;
				temporarySpriteByID5.sourceRect.X = 96;
				mrs_raccoon2.sourceRect.Y = 224;
				mrs_raccoon2.sourceRectStartingPos.Y = 224f;
				mrs_raccoon2.currentParentTileIndex = 3;
				mrs_raccoon2.timer = 0f;
				mrs_raccoon2.sourceRect.X = 96;
				break;
			}
			case "raccoonCircle2":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\raccoon_circle_cutout", new Microsoft.Xna.Framework.Rectangle(0, 0, 263, 263), new Vector2(56.5f, 0f) * 64f - new Vector2(131.5f, 44f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					interval = 297f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					id = 997797,
					scale = 4f,
					alpha = 0.01f,
					alphaFade = -0.003f,
					layerDepth = 0.8f
				});
				break;
			case "raccoonCircle":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Characters\\raccoon", new Microsoft.Xna.Framework.Rectangle(0, 0, 32, 32), 148f, 8, 999, new Vector2(54.5f, 7f) * 64f, false, false)
				{
					scale = 4f,
					layerDepth = 0.051840004f,
					usePreciseTiming = true,
					id = 9786
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Characters\\mrs_raccoon", new Microsoft.Xna.Framework.Rectangle(0, 0, 32, 32), 148f, 8, 999, new Vector2(56.5f, 7f) * 64f, false, false)
				{
					scale = 4f,
					layerDepth = 0.0512f,
					usePreciseTiming = true,
					id = 9785
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\raccoon_circle_cutout", new Microsoft.Xna.Framework.Rectangle(0, 0, 1, 1), Vector2.Zero, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					vectorScale = new Vector2(3090f, 1052f),
					interval = 99999f,
					totalNumberOfLoops = 1,
					id = 997799
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\raccoon_circle_cutout", new Microsoft.Xna.Framework.Rectangle(0, 0, 1, 1), new Vector2(56.5f, 0f) * 64f + new Vector2(131.5f, 0f) * 4f, false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					vectorScale = new Vector2(5536f, 1052f),
					interval = 99999f,
					totalNumberOfLoops = 1,
					id = 997799
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\raccoon_circle_cutout", new Microsoft.Xna.Framework.Rectangle(0, 0, 1, 1), new Vector2(0f, 876f), false, 0f, Color.White)
				{
					drawAboveAlwaysFront = true,
					vectorScale = new Vector2(7552f, 7488f),
					interval = 99999f,
					totalNumberOfLoops = 1,
					id = 997799
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\raccoon_circle_cutout", new Microsoft.Xna.Framework.Rectangle(0, 0, 263, 263), new Vector2(56.5f, 0f) * 64f - new Vector2(131.5f, 44f) * 4f, false, 0f, Color.Black)
				{
					drawAboveAlwaysFront = true,
					interval = 297f,
					animationLength = 3,
					totalNumberOfLoops = 99999,
					id = 997799,
					scale = 4f
				});
				break;
			case "raccoonSong":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(279, 55, 12, 15), 297f, 8, 999, new Vector2(3706f, 340f) - new Vector2(6.5f, 12f) * 4f, false, false)
				{
					scale = 4f,
					layerDepth = 0.044809997f,
					usePreciseTiming = true
				});
				for (int i3 = 0; i3 < 8; i3++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(304, 397, 11, 11), 49f, 12, 1, new Vector2(3706f, 340f) + new Vector2(14f, -12f) * 4f, false, false)
					{
						scale = 4f,
						layerDepth = 0.05057f,
						delayBeforeAnimationStart = 2376 * i3,
						usePreciseTiming = true,
						motion = new Vector2(1f, 0f),
						acceleration = new Vector2(0f, 0.001f),
						color = new Color(255, 200, 200),
						rotationChange = (float)Game1.random.Next(-20, 20) / 1000f
					});
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(455, 414, 14, 17), 2376f, 1, 999, new Vector2(3706f, 340f) + new Vector2(7f, -12f) * 4f, false, false)
					{
						scale = 4f,
						layerDepth = 0.051209997f,
						delayBeforeAnimationStart = 2376 * i3,
						alphaFade = 0.02f,
						usePreciseTiming = true
					});
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(374, 55, 12, 15), 297f, 8, 999, new Vector2(54f, 4f) * 64f + new Vector2(0f, -16f), false, true)
				{
					scale = 4f,
					layerDepth = 0.044809997f,
					delayBeforeAnimationStart = 297,
					usePreciseTiming = true
				});
				for (int i2 = 0; i2 < 8; i2++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(385, 414, 14, 17), 2376f, 1, 999, new Vector2(54f, 4f) * 64f + new Vector2(16f, -8f) + new Vector2(-15f, -17f) * 4f, false, false)
					{
						scale = 4f,
						layerDepth = 0.051209997f,
						delayBeforeAnimationStart = 2376 * i2 + 297,
						alphaFade = 0.02f,
						usePreciseTiming = true
					});
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(279, 55, 12, 15), 297f, 8, 999, new Vector2(3462f, 433f) - new Vector2(6.5f, 12f) * 4f, false, false)
				{
					scale = 4f,
					layerDepth = 0.044809997f,
					delayBeforeAnimationStart = 594,
					usePreciseTiming = true
				});
				for (int n = 0; n < 8; n++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(304, 397, 11, 11), 49f, 12, 1, new Vector2(3462f, 433f) + new Vector2(-20f, -16f) + new Vector2(-15f, -17f) * 4f, false, false)
					{
						scale = 4f,
						layerDepth = 0.05057f,
						delayBeforeAnimationStart = 2376 * n + 594,
						usePreciseTiming = true,
						motion = new Vector2(-1f, -1f),
						acceleration = new Vector2(0f, 0.001f),
						color = new Color(180, 200, 255),
						rotationChange = (float)Game1.random.Next(-20, 20) / 1000f
					});
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(371, 414, 14, 17), 2376f, 1, 999, new Vector2(3462f, 433f) + new Vector2(-20f, -16f) + new Vector2(-15f, -17f) * 4f, false, false)
					{
						scale = 4f,
						layerDepth = 0.051209997f,
						delayBeforeAnimationStart = 2376 * n + 594,
						alphaFade = 0.013f,
						usePreciseTiming = true
					});
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(374, 55, 12, 15), 297f, 8, 999, new Vector2(58f, 4f) * 64f + new Vector2(0f, -24f), false, false)
				{
					scale = 4f,
					layerDepth = 0.044809997f,
					delayBeforeAnimationStart = 891,
					usePreciseTiming = true
				});
				for (int m = 0; m < 8; m++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(440, 415, 14, 15), 2376f, 1, 999, new Vector2(58f, 4f) * 64f + new Vector2(48f, -56f), false, false)
					{
						scale = 4f,
						layerDepth = 0.051209997f,
						delayBeforeAnimationStart = 2376 * m + 891,
						alphaFade = 0.02f,
						usePreciseTiming = true
					});
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(279, 55, 12, 15), 297f, 8, 999, new Vector2(3770f, 408f) - new Vector2(6.5f, 12f) * 4f, false, false)
				{
					scale = 4f,
					layerDepth = 0.044809997f,
					delayBeforeAnimationStart = 1188,
					usePreciseTiming = true
				});
				for (int l = 0; l < 8; l++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(469, 415, 14, 14), 2376f, 1, 999, new Vector2(3770f, 408f) + new Vector2(24f, -64f), false, false)
					{
						scale = 4f,
						layerDepth = 0.051209997f,
						delayBeforeAnimationStart = 2376 * l + 1188,
						alphaFade = 0.02f,
						usePreciseTiming = true
					});
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(279, 55, 12, 15), 297f, 8, 999, new Vector2(55f, 3f) * 64f + new Vector2(12f, 4f) - new Vector2(6.5f, 12f) * 4f, false, false)
				{
					scale = 4f,
					layerDepth = 0.044809997f,
					delayBeforeAnimationStart = 1485,
					usePreciseTiming = true
				});
				for (int k = 0; k < 8; k++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(400, 414, 12, 16), 2376f, 1, 999, new Vector2(55f, 3f) * 64f + new Vector2(-32f, -100f), false, false)
					{
						scale = 4f,
						layerDepth = 0.051209997f,
						delayBeforeAnimationStart = 2376 * k + 1485,
						alphaFade = 0.02f,
						usePreciseTiming = true
					});
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(279, 55, 12, 15), 297f, 8, 999, new Vector2(56f, 3f) * 64f + new Vector2(40f, -8f) - new Vector2(6.5f, 12f) * 4f, false, false)
				{
					scale = 4f,
					layerDepth = 0.044809997f,
					delayBeforeAnimationStart = 1782,
					usePreciseTiming = true
				});
				for (int j = 0; j < 8; j++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(304, 397, 11, 11), 49f, 12, 1, new Vector2(56f, 3f) * 64f + new Vector2(12f, -112f), false, false)
					{
						scale = 4f,
						layerDepth = 0.05057f,
						delayBeforeAnimationStart = 2376 * j + 1782,
						usePreciseTiming = true,
						motion = new Vector2(-0.25f, -1.5f),
						acceleration = new Vector2(0f, 0.001f),
						color = new Color(220, 255, 180),
						rotationChange = (float)Game1.random.Next(-20, 20) / 1000f
					});
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(414, 414, 12, 16), 2376f, 1, 999, new Vector2(56f, 3f) * 64f + new Vector2(12f, -112f), false, false)
					{
						scale = 4f,
						layerDepth = 0.051209997f,
						delayBeforeAnimationStart = 2376 * j + 1782,
						alphaFade = 0.013f,
						usePreciseTiming = true
					});
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(374, 55, 12, 15), 297f, 8, 999, new Vector2(58f, 3f) * 64f + new Vector2(-24f, -52f), false, false)
				{
					scale = 4f,
					layerDepth = 0.044809997f,
					delayBeforeAnimationStart = 2079,
					usePreciseTiming = true
				});
				for (int i = 0; i < 8; i++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors_1_6", new Microsoft.Xna.Framework.Rectangle(426, 414, 14, 15), 2376f, 1, 999, new Vector2(58f, 3f) * 64f + new Vector2(28f, -88f), false, false)
					{
						scale = 4f,
						layerDepth = 0.051209997f,
						delayBeforeAnimationStart = 2376 * i + 2079,
						alphaFade = 0.02f,
						usePreciseTiming = true
					});
				}
				break;
			}
			case "terraria_cat_leave":
			{
				TemporaryAnimatedSprite terraria_cat = location.getTemporarySpriteByID(777);
				if (terraria_cat == null)
				{
					break;
				}
				terraria_cat.sourceRect.Y = 0;
				terraria_cat.sourceRect.X = terraria_cat.currentParentTileIndex * 16;
				terraria_cat.paused = false;
				terraria_cat.motion = new Vector2(1f, 0f);
				terraria_cat.xStopCoordinate = 1152;
				terraria_cat.flipped = true;
				Microsoft.Xna.Framework.Rectangle warpRect2 = new Microsoft.Xna.Framework.Rectangle(1024, 120, 144, 272);
				terraria_cat.reachedStopCoordinate = delegate
				{
					terraria_cat.position.X = -4000f;
					location.removeTemporarySpritesWithID(888);
					Game1.playSound("terraria_warp");
					for (int num = 0; num < 80; num++)
					{
						Vector2 randomPositionInThisRectangle = Utility.getRandomPositionInThisRectangle(warpRect2, Game1.random);
						Vector2 vector = randomPositionInThisRectangle - Utility.PointToVector2(warpRect2.Center);
						vector.Normalize();
						vector *= (float)(Game1.random.Next(10, 21) / 10);
						location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\terraria_cat", new Microsoft.Xna.Framework.Rectangle(113 + Game1.random.Next(3) * 5, 123, 5, 5), 999f, 1, 9999, randomPositionInThisRectangle, false, false, 0.8f, 0.02f, Color.White, 4f, 0f, 0f, 0f)
						{
							layerDepth = 0.99f,
							rotationChange = (float)Game1.random.Next(-10, 10) / 100f,
							motion = vector,
							acceleration = -vector / 150f,
							scaleChange = (float)Game1.random.Next(-10, 0) / 500f,
							delayBeforeAnimationStart = num * 5
						});
					}
				};
				break;
			}
			case "terraria_warp_begin":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\terraria_cat", new Microsoft.Xna.Framework.Rectangle(0, 18, 36, 68), 90f, 3, 9999, new Vector2(16f, 5f) * 64f + new Vector2(0f, -50f) * 4f, false, false, 0.8f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					layerDepth = 0.8f,
					id = 888
				});
				TemporaryAnimatedSprite cat_sprite = new TemporaryAnimatedSprite("LooseSprites\\terraria_cat", new Microsoft.Xna.Framework.Rectangle(0, 0, 16, 16), 90f, 8, 9999, new Vector2(16f, 5f) * 64f + new Vector2(34f, -12f) * 4f, false, false, 0.8f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 777,
					layerDepth = 0.85f,
					motion = new Vector2(-1f, 0f),
					delayBeforeAnimationStart = 1000,
					xStopCoordinate = 960
				};
				cat_sprite.reachedStopCoordinate = delegate
				{
					cat_sprite.paused = true;
					cat_sprite.sourceRect = new Microsoft.Xna.Framework.Rectangle(112, 16, 16, 16);
					DelayedAction.functionAfterDelay(delegate
					{
						Game1.playSound("terraria_meowmere");
						cat_sprite.shakeIntensity = 1f;
						location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\weapons", new Microsoft.Xna.Framework.Rectangle(16, 128, 16, 16), 1000f, 1, 1, new Vector2(15f, 5f) * 64f, false, false, 0.8f, 0f, Color.White, 4f, 0f, 0f, 0f)
						{
							layerDepth = 0.86f,
							motion = new Vector2(-1f, -4f),
							acceleration = new Vector2(0f, 0.1f)
						});
					}, 1000);
					DelayedAction.functionAfterDelay(delegate
					{
						cat_sprite.shakeIntensity = 0f;
					}, 1300);
				};
				location.TemporarySprites.Add(cat_sprite);
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\terraria_cat", new Microsoft.Xna.Framework.Rectangle(4, 88, 19, 15), 90f, 3, 9999, new Vector2(16f, 5f) * 64f + new Vector2(31f, -10f) * 4f, false, false, 0.8f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					layerDepth = 0.9f,
					id = 888
				});
				Microsoft.Xna.Framework.Rectangle warpRect = new Microsoft.Xna.Framework.Rectangle(1024, 120, 144, 272);
				for (int i4 = 0; i4 < 80; i4++)
				{
					Vector2 warpSparklePos = Utility.getRandomPositionInThisRectangle(warpRect, Game1.random);
					Vector2 warpSkarleMotion = warpSparklePos - Utility.PointToVector2(warpRect.Center);
					warpSkarleMotion.Normalize();
					warpSkarleMotion *= (float)(Game1.random.Next(10, 21) / 10);
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\terraria_cat", new Microsoft.Xna.Framework.Rectangle(113 + Game1.random.Next(3) * 5, 123, 5, 5), 999f, 1, 9999, warpSparklePos, false, false, 0.8f, 0.02f, Color.White, 4f, 0f, 0f, 0f)
					{
						layerDepth = 0.99f,
						rotationChange = (float)Game1.random.Next(-10, 10) / 100f,
						motion = warpSkarleMotion,
						acceleration = -warpSkarleMotion / 150f,
						scaleChange = (float)Game1.random.Next(-10, 0) / 500f,
						delayBeforeAnimationStart = i4 * 5
					});
				}
				break;
			}
			case "LeoWillyFishing":
			{
				for (int i5 = 0; i5 < 20; i5++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite(0, new Vector2(42.5f, 38f) * 64f + new Vector2(Game1.random.Next(64), Game1.random.Next(64)), Color.White * 0.7f)
					{
						layerDepth = (float)(1280 + i5) / 10000f,
						delayBeforeAnimationStart = i5 * 150
					});
				}
				break;
			}
			case "LeoLinusCooking":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\springobjects", new Microsoft.Xna.Framework.Rectangle(240, 128, 16, 16), 9999f, 1, 1, new Vector2(29f, 8.5f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					layerDepth = 1f
				});
				for (int smokePuffs = 0; smokePuffs < 10; smokePuffs++)
				{
					Utility.addSmokePuff(location, new Vector2(29.5f, 8.6f) * 64f, smokePuffs * 500);
				}
				break;
			}
			case "BoatParrotLeave":
			{
				TemporaryAnimatedSprite temporaryAnimatedSprite2 = aboveMapSprites[0];
				temporaryAnimatedSprite2.motion = new Vector2(4f, -6f);
				temporaryAnimatedSprite2.sourceRect.X = 48;
				temporaryAnimatedSprite2.sourceRectStartingPos.X = 48f;
				temporaryAnimatedSprite2.animationLength = 3;
				temporaryAnimatedSprite2.pingPong = true;
				break;
			}
			case "BoatParrotSquawkStop":
			{
				TemporaryAnimatedSprite temporaryAnimatedSprite3 = aboveMapSprites[0];
				temporaryAnimatedSprite3.sourceRect.X = 0;
				temporaryAnimatedSprite3.sourceRectStartingPos.X = 0f;
				break;
			}
			case "BoatParrotSquawk":
			{
				TemporaryAnimatedSprite temporaryAnimatedSprite = aboveMapSprites[0];
				temporaryAnimatedSprite.sourceRect.X = 24;
				temporaryAnimatedSprite.sourceRectStartingPos.X = 24f;
				Game1.playSound("parrot_squawk");
				break;
			}
			case "BoatParrot":
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\parrots", new Microsoft.Xna.Framework.Rectangle(48, 0, 24, 24), 100f, 3, 99999, new Vector2(Game1.viewport.X - 64, 2112f), false, true, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999,
					motion = new Vector2(6f, 1f),
					delayBeforeAnimationStart = 0,
					pingPong = true,
					xStopCoordinate = 1040,
					reachedStopCoordinate = delegate
					{
						TemporaryAnimatedSprite temporaryAnimatedSprite5 = aboveMapSprites[0];
						if (temporaryAnimatedSprite5 != null)
						{
							temporaryAnimatedSprite5.motion = new Vector2(0f, 2f);
							temporaryAnimatedSprite5.yStopCoordinate = 2336;
							temporaryAnimatedSprite5.reachedStopCoordinate = delegate
							{
								TemporaryAnimatedSprite temporaryAnimatedSprite6 = aboveMapSprites[0];
								temporaryAnimatedSprite6.animationLength = 1;
								temporaryAnimatedSprite6.pingPong = false;
								temporaryAnimatedSprite6.sourceRect = new Microsoft.Xna.Framework.Rectangle(0, 0, 24, 24);
								temporaryAnimatedSprite6.sourceRectStartingPos = Vector2.Zero;
							};
						}
					}
				});
				break;
			case "islandFishSplash":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\springobjects", new Microsoft.Xna.Framework.Rectangle(336, 544, 16, 16), 100000f, 1, 1, new Vector2(81f, 92f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 9999,
					motion = new Vector2(-2f, -8f),
					acceleration = new Vector2(0f, 0.2f),
					flipped = true,
					rotationChange = -0.02f,
					yStopCoordinate = 5952,
					layerDepth = 0.99f,
					reachedStopCoordinate = delegate
					{
						location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\springobjects", new Microsoft.Xna.Framework.Rectangle(48, 16, 16, 16), 100f, 5, 1, location.getTemporarySpriteByID(9999).position, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
						{
							layerDepth = 1f
						});
						location.removeTemporarySpritesWithID(9999);
						Game1.playSound("waterSlosh");
					}
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\springobjects", new Microsoft.Xna.Framework.Rectangle(48, 16, 16, 16), 100f, 5, 1, new Vector2(81f, 92f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					layerDepth = 1f
				});
				break;
			case "georgeLeekGift":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(288, 1231, 16, 16), 100f, 6, 1, new Vector2(17f, 19f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999,
					paused = false,
					holdLastFrame = true
				});
				break;
			case "staticSprite":
			{
				string textureName;
				string error;
				Microsoft.Xna.Framework.Rectangle sourceRect;
				Vector2 tile;
				int id;
				float layerDepth;
				if (!ArgUtility.TryGet(args, 2, out textureName, out error) || !ArgUtility.TryGetRectangle(args, 3, out sourceRect, out error) || !ArgUtility.TryGetVector2(args, 7, out tile, out error) || !ArgUtility.TryGetOptionalInt(args, 9, out id, out error, 999) || !ArgUtility.TryGetOptionalFloat(args, 10, out layerDepth, out error, 1f))
				{
					LogCommandError(args, error);
					break;
				}
				location.temporarySprites.Add(new TemporaryAnimatedSprite(textureName, sourceRect, tile * 64f, false, 0f, Color.White)
				{
					animationLength = 1,
					interval = 999999f,
					scale = 4f,
					layerDepth = layerDepth,
					id = id
				});
				break;
			}
			case "WillyWad":
				location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\Cursors2"),
					sourceRect = new Microsoft.Xna.Framework.Rectangle(192, 61, 32, 32),
					sourceRectStartingPos = new Vector2(192f, 61f),
					animationLength = 2,
					totalNumberOfLoops = 99999,
					interval = 400f,
					scale = 4f,
					position = new Vector2(50f, 23f) * 64f,
					layerDepth = 0.1536f,
					id = 996
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(51, new Vector2(3328f, 1728f), Color.White, 10, false, 80f, 999999));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(51, new Vector2(3264f, 1792f), Color.White, 10, false, 70f, 999999));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(51, new Vector2(3392f, 1792f), Color.White, 10, false, 85f, 999999));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(160, 368, 16, 32), 500f, 3, 99999, new Vector2(53f, 24f) * 64f, false, false, 0.1984f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(160, 368, 16, 32), 510f, 3, 99999, new Vector2(54f, 23f) * 64f, false, false, 0.1984f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "parrotHutSquawk":
				(location as IslandHut).parrotUpgradePerches[0].timeUntilSqwawk = 1f;
				break;
			case "parrotPerchHut":
				location.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\parrots", new Microsoft.Xna.Framework.Rectangle(0, 0, 24, 24), new Vector2(7f, 4f) * 64f, false, 0f, Color.White)
				{
					animationLength = 1,
					interval = 999999f,
					scale = 4f,
					layerDepth = 1f,
					id = 999
				});
				break;
			case "trashBearTown":
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors2", new Microsoft.Xna.Framework.Rectangle(46, 80, 46, 56), new Vector2(43f, 64f) * 64f, false, 0f, Color.White)
				{
					animationLength = 1,
					interval = 999999f,
					motion = new Vector2(4f, 0f),
					scale = 4f,
					layerDepth = 1f,
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 32f,
					id = 777,
					xStopCoordinate = 3392,
					reachedStopCoordinate = delegate
					{
						aboveMapSprites[0].xStopCoordinate = -1;
						aboveMapSprites[0].motion = new Vector2(4f, 0f);
						location.ApplyMapOverride("Town-TrashGone", (Microsoft.Xna.Framework.Rectangle?)null, (Microsoft.Xna.Framework.Rectangle?)new Microsoft.Xna.Framework.Rectangle(57, 68, 17, 5));
						location.ApplyMapOverride("Town-DogHouse", (Microsoft.Xna.Framework.Rectangle?)null, (Microsoft.Xna.Framework.Rectangle?)new Microsoft.Xna.Framework.Rectangle(51, 65, 5, 6));
						Game1.flashAlpha = 0.75f;
						Game1.screenGlowOnce(Color.Lime, false, 0.25f, 1f);
						location.playSound("yoba");
						TemporaryAnimatedSprite temporaryAnimatedSprite4 = new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(497, 1918, 11, 11), new Vector2(3456f, 4160f), false, 0f, Color.White)
						{
							yStopCoordinate = 4372,
							motion = new Vector2(-0.5f, -10f),
							acceleration = new Vector2(0f, 0.25f),
							scale = 4f,
							alphaFade = 0f,
							extraInfoForEndBehavior = -777
						};
						temporaryAnimatedSprite4.reachedStopCoordinate = temporaryAnimatedSprite4.bounce;
						temporaryAnimatedSprite4.initialPosition.Y = 4372f;
						aboveMapSprites.Add(temporaryAnimatedSprite4);
						aboveMapSprites.AddRange(Utility.getStarsAndSpirals(location, 54, 69, 6, 5, 1000, 10, Color.Lime));
						location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(324, 1936, 12, 20), 80f, 4, 99999, new Vector2(53f, 67f) * 64f + new Vector2(3f, 3f) * 4f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
						{
							id = 1,
							delayBeforeAnimationStart = 3000,
							startSound = "dogWhining"
						});
					}
				});
				break;
			case "trashBearUmbrella1":
				location.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors2", new Microsoft.Xna.Framework.Rectangle(0, 80, 46, 56), new Vector2(102f, 94.5f) * 64f, false, 0f, Color.White)
				{
					animationLength = 1,
					interval = 999999f,
					motion = new Vector2(0f, -9f),
					acceleration = new Vector2(0f, 0.4f),
					scale = 4f,
					layerDepth = 1f,
					id = 777,
					yStopCoordinate = 6144,
					reachedStopCoordinate = delegate(int param)
					{
						location.getTemporarySpriteByID(777).yStopCoordinate = -1;
						location.getTemporarySpriteByID(777).motion = new Vector2(0f, (float)param * 0.75f);
						location.getTemporarySpriteByID(777).acceleration = new Vector2(0.04f, -0.19f);
						location.getTemporarySpriteByID(777).accelerationChange = new Vector2(0f, 0.0015f);
						location.getTemporarySpriteByID(777).sourceRect.X += 46;
						location.playSound("batFlap");
						location.playSound("tinyWhip");
					}
				});
				break;
			case "trashBearMagic":
				Utility.addStarsAndSpirals(location, 95, 103, 24, 12, 2000, 10, Color.Lime);
				(location as Forest).removeSewerTrash();
				Game1.flashAlpha = 0.75f;
				Game1.screenGlowOnce(Color.Lime, false, 0.25f, 1f);
				break;
			case "trashBearPrelude":
				Utility.addStarsAndSpirals(location, 95, 106, 23, 4, 10000, 275, Color.Lime);
				break;
			case "krobusBeach":
			{
				for (int i6 = 0; i6 < 8; i6++)
				{
					location.temporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(0, 0, 64, 64), 150f, 4, 0, new Vector2(84f + ((i6 % 2 == 0) ? 0.25f : (-0.05f)), 41f) * 64f, false, Game1.random.NextBool(), 0.001f, 0.02f, Color.White, 0.75f, 0.003f, 0f, 0f)
					{
						delayBeforeAnimationStart = 500 + i6 * 1000,
						startSound = "waterSlosh"
					});
				}
				underwaterSprites = new TemporaryAnimatedSpriteList();
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(82f, 52f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2688,
					delayBeforeAnimationStart = 0,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(82f, 52f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 3008,
					delayBeforeAnimationStart = 2000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(88f, 52f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2688,
					delayBeforeAnimationStart = 150,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(88f, 52f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 3008,
					delayBeforeAnimationStart = 2000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(90f, 52f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2816,
					delayBeforeAnimationStart = 300,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(79f, 52f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2816,
					delayBeforeAnimationStart = 1000,
					pingPong = true
				});
				break;
			}
			case "coldstarMiracle":
			{
				MovieData data;
				if (!MovieTheater.TryGetMovieData("winter_movie_0", out data))
				{
					Game1.log.Error("Can't find data for movie 'winter_movie_0'.");
					break;
				}
				Microsoft.Xna.Framework.Rectangle sourceRect2 = MovieTheater.GetSourceRectForScreen(data.SheetIndex, 9);
				location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>(data.Texture ?? "LooseSprites\\Movies"),
					sourceRect = sourceRect2,
					sourceRectStartingPos = new Vector2(sourceRect2.X, sourceRect2.Y),
					animationLength = 1,
					totalNumberOfLoops = 1,
					interval = 99999f,
					alpha = 0.01f,
					alphaFade = -0.01f,
					scale = 4f,
					position = new Vector2(4f, 1f) * 64f + new Vector2(3f, 7f) * 4f,
					layerDepth = 0.8535f,
					id = 989
				});
				break;
			}
			case "sunroom":
				location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1"),
					sourceRect = new Microsoft.Xna.Framework.Rectangle(304, 486, 24, 26),
					sourceRectStartingPos = new Vector2(304f, 486f),
					animationLength = 1,
					totalNumberOfLoops = 997,
					interval = 99999f,
					scale = 4f,
					position = new Vector2(4f, 8f) * 64f + new Vector2(8f, -8f) * 4f,
					layerDepth = 0.0512f,
					id = 996
				});
				location.addCritter(new Butterfly(location, location.getRandomTile()).setStayInbounds(true));
				while (Game1.random.NextBool())
				{
					location.addCritter(new Butterfly(location, location.getRandomTile()).setStayInbounds(true));
				}
				break;
			case "sauceGood":
				Utility.addSprinklesToLocation(location, OffsetTileX(64), OffsetTileY(16), 3, 1, 800, 200, Color.White);
				break;
			case "sauceFire":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.mouseCursors,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11),
					animationLength = 4,
					sourceRectStartingPos = new Vector2(276f, 1985f),
					interval = 100f,
					totalNumberOfLoops = 5,
					position = OffsetPosition(new Vector2(64f, 16f) * 64f + new Vector2(3f, -4f) * 4f),
					scale = 4f,
					layerDepth = 1f
				});
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				for (int i7 = 0; i7 < 8; i7++)
				{
					aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(372, 1956, 10, 10), OffsetPosition(new Vector2(64f, 16f) * 64f) + new Vector2(Game1.random.Next(-16, 32), 0f), false, 0.002f, Color.Gray)
					{
						alpha = 0.75f,
						motion = new Vector2(1f, -1f) + new Vector2((float)(Game1.random.Next(100) - 50) / 100f, (float)(Game1.random.Next(100) - 50) / 100f),
						interval = 99999f,
						layerDepth = 0.0384f + (float)Game1.random.Next(100) / 10000f,
						scale = 3f,
						scaleChange = 0.01f,
						rotationChange = (float)Game1.random.Next(-5, 6) * (float)Math.PI / 256f,
						delayBeforeAnimationStart = i7 * 25
					});
				}
				break;
			}
			case "evilRabbit":
				location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>("TileSheets\\critters"),
					sourceRect = new Microsoft.Xna.Framework.Rectangle(264, 209, 19, 16),
					sourceRectStartingPos = new Vector2(264f, 209f),
					animationLength = 1,
					totalNumberOfLoops = 999,
					interval = 999f,
					scale = 4f,
					position = new Vector2(4f, 1f) * 64f + new Vector2(38f, 23f) * 4f,
					layerDepth = 1f,
					motion = new Vector2(-2f, -2f),
					acceleration = new Vector2(0f, 0.1f),
					yStopCoordinate = 204,
					xStopCoordinate = 316,
					flipped = true,
					id = 778
				});
				break;
			case "shakeBushStop":
				location.getTemporarySpriteByID(777).shakeIntensity = 0f;
				break;
			case "shakeBush":
				location.getTemporarySpriteByID(777).shakeIntensity = 1f;
				break;
			case "movieBush":
				location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>("TileSheets\\bushes"),
					sourceRect = new Microsoft.Xna.Framework.Rectangle(65, 58, 30, 35),
					sourceRectStartingPos = new Vector2(65f, 58f),
					animationLength = 1,
					totalNumberOfLoops = 999,
					interval = 999f,
					scale = 4f,
					position = new Vector2(4f, 1f) * 64f + new Vector2(33f, 13f) * 4f,
					layerDepth = 0.99f,
					id = 777
				});
				break;
			case "woodswalker":
				location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1"),
					sourceRect = new Microsoft.Xna.Framework.Rectangle(448, 419, 16, 21),
					sourceRectStartingPos = new Vector2(448f, 419f),
					animationLength = 4,
					totalNumberOfLoops = 7,
					interval = 150f,
					scale = 4f,
					position = new Vector2(4f, 1f) * 64f + new Vector2(5f, 22f) * 4f,
					shakeIntensity = 1f,
					motion = new Vector2(1f, 0f),
					xStopCoordinate = 576,
					layerDepth = 1f,
					id = 996
				});
				break;
			case "movieFrame":
			{
				string error2;
				int frame;
				int duration;
				string movieId;
				if (!ArgUtility.TryGet(args, 2, out movieId, out error2) || !ArgUtility.TryGetInt(args, 3, out frame, out error2) || !ArgUtility.TryGetInt(args, 4, out duration, out error2))
				{
					LogCommandError(args, error2);
					break;
				}
				movieId = MovieTheater.GetMovieIdFromLegacyIndex(movieId);
				MovieData data2;
				if (!MovieTheater.TryGetMovieData(movieId, out data2))
				{
					LogCommandError(args, "no movie found with ID '" + movieId + "'");
					break;
				}
				Microsoft.Xna.Framework.Rectangle sourceRect3 = MovieTheater.GetSourceRectForScreen(data2.SheetIndex, frame);
				location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>(data2.Texture ?? "LooseSprites\\Movies"),
					sourceRect = sourceRect3,
					sourceRectStartingPos = new Vector2(sourceRect3.X, sourceRect3.Y),
					animationLength = 1,
					totalNumberOfLoops = 1,
					interval = duration,
					scale = 4f,
					position = new Vector2(4f, 1f) * 64f + new Vector2(3f, 7f) * 4f,
					shakeIntensity = 0.25f,
					layerDepth = 0.0192f,
					id = 997
				});
				break;
			}
			case "movieTheater_screen":
			{
				string error3;
				int screenIndex;
				bool shake;
				string movieId2;
				if (!ArgUtility.TryGet(args, 2, out movieId2, out error3) || !ArgUtility.TryGetInt(args, 3, out screenIndex, out error3) || !ArgUtility.TryGetBool(args, 4, out shake, out error3))
				{
					LogCommandError(args, error3);
					break;
				}
				movieId2 = MovieTheater.GetMovieIdFromLegacyIndex(movieId2);
				MovieData data3;
				if (!MovieTheater.TryGetMovieData(movieId2, out data3))
				{
					LogCommandError(args, "No movie found with ID '" + movieId2 + "'.");
					break;
				}
				Microsoft.Xna.Framework.Rectangle sourceRect4 = MovieTheater.GetSourceRectForScreen(data3.SheetIndex, screenIndex);
				location.removeTemporarySpritesWithIDLocal(998);
				if (screenIndex >= 0)
				{
					location.temporarySprites.Add(new TemporaryAnimatedSprite
					{
						texture = Game1.temporaryContent.Load<Texture2D>(data3.Texture ?? "LooseSprites\\Movies"),
						sourceRect = sourceRect4,
						sourceRectStartingPos = new Vector2(sourceRect4.X, sourceRect4.Y),
						animationLength = 1,
						totalNumberOfLoops = 9999,
						interval = 5000f,
						scale = 4f,
						position = new Vector2(4f, 1f) * 64f + new Vector2(3f, 7f) * 4f,
						shakeIntensity = (shake ? 1f : 0f),
						layerDepth = 0.0128f,
						id = 998
					});
				}
				break;
			}
			case "movieTheater_setup":
				Game1.currentLightSources.Add(new LightSource(7, new Vector2(192f, 64f) + new Vector2(64f, 80f) * 4f, 4f, LightSource.LightContext.None, 0L));
				location.temporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>("Maps\\MovieTheaterScreen_TileSheet"),
					sourceRect = new Microsoft.Xna.Framework.Rectangle(224, 0, 96, 112),
					sourceRectStartingPos = new Vector2(224f, 0f),
					animationLength = 1,
					interval = 5000f,
					totalNumberOfLoops = 9999,
					scale = 4f,
					position = new Vector2(4f, 4f) * 64f,
					layerDepth = 1f,
					id = 999,
					delayBeforeAnimationStart = 7950
				});
				break;
			case "junimoSpotlight":
				actors[0].drawOnTop = true;
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1"),
					sourceRect = new Microsoft.Xna.Framework.Rectangle(316, 123, 67, 43),
					sourceRectStartingPos = new Vector2(316f, 123f),
					animationLength = 1,
					interval = 5000f,
					totalNumberOfLoops = 9999,
					scale = 4f,
					position = Utility.getTopLeftPositionForCenteringOnScreen(Game1.viewport, 268, 172, 0, -20),
					layerDepth = 0.0001f,
					local = true,
					id = 999
				});
				break;
			case "missingJunimoStars":
			{
				location.removeTemporarySpritesWithID(999);
				Texture2D tempTxture11 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				for (int i8 = 0; i8 < 48; i8++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite
					{
						texture = tempTxture11,
						sourceRect = new Microsoft.Xna.Framework.Rectangle(477, 306, 28, 28),
						sourceRectStartingPos = new Vector2(477f, 306f),
						animationLength = 1,
						interval = 5000f,
						totalNumberOfLoops = 10,
						scale = Game1.random.Next(1, 5),
						position = Utility.getTopLeftPositionForCenteringOnScreen(Game1.viewport, 84, 84) + new Vector2(Game1.random.Next(-32, 32), Game1.random.Next(-32, 32)),
						rotationChange = (float)Math.PI / (float)Game1.random.Next(16, 128),
						motion = new Vector2((float)Game1.random.Next(-30, 40) / 10f, (float)Game1.random.Next(20, 90) * -0.1f),
						acceleration = new Vector2(0f, 0.05f),
						local = true,
						layerDepth = (float)i8 / 100f,
						color = (Game1.random.NextBool() ? Color.White : Utility.getRandomRainbowColor())
					});
				}
				break;
			}
			case "frogJump":
			{
				TemporaryAnimatedSprite temporarySpriteByID4 = location.getTemporarySpriteByID(777);
				temporarySpriteByID4.motion = new Vector2(-2f, 0f);
				temporarySpriteByID4.animationLength = 4;
				temporarySpriteByID4.interval = 150f;
				break;
			}
			case "sebastianFrogHouse":
			{
				Point frog_spot = (location as FarmHouse).GetSpouseRoomCorner();
				frog_spot.X++;
				frog_spot.Y += 6;
				Vector2 spot = Utility.PointToVector2(frog_spot);
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.mouseCursors,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(641, 1534, 48, 37),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(641f, 1534f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = spot * 64f + new Vector2(0f, -5f) * 4f,
					scale = 4f,
					layerDepth = (spot.Y + 2f + 0.1f) * 64f / 10000f
				});
				Texture2D crittersText2 = Game1.temporaryContent.Load<Texture2D>("TileSheets\\critters");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = crittersText2,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(0, 224, 16, 16),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(0f, 224f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = spot * 64f + new Vector2(25f, 2f) * 4f,
					scale = 4f,
					flipped = true,
					layerDepth = (spot.Y + 2f + 0.11f) * 64f / 10000f,
					id = 777
				});
				break;
			}
			case "sebastianFrog":
			{
				Texture2D crittersText = Game1.temporaryContent.Load<Texture2D>("TileSheets\\critters");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = crittersText,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(0, 224, 16, 16),
					animationLength = 4,
					sourceRectStartingPos = new Vector2(0f, 224f),
					interval = 120f,
					totalNumberOfLoops = 9999,
					position = new Vector2(45f, 36f) * 64f,
					scale = 4f,
					layerDepth = 0.00064f,
					motion = new Vector2(2f, 0f),
					xStopCoordinate = 3136,
					id = 777,
					reachedStopCoordinate = delegate
					{
						int num2 = CurrentCommand;
						CurrentCommand = num2 + 1;
						location.removeTemporarySpritesWithID(777);
					}
				});
				break;
			}
			case "haleyCakeWalk":
			{
				Texture2D tempTxture10 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture10,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(0, 400, 144, 112),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(0f, 400f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = new Vector2(26f, 65f) * 64f,
					scale = 4f,
					layerDepth = 0.00064f
				});
				break;
			}
			case "harveyDinnerSet":
			{
				Vector2 centerPoint = new Vector2(5f, 16f);
				DecoratableLocation decoratableLocation = location as DecoratableLocation;
				if (decoratableLocation != null)
				{
					foreach (Furniture f in decoratableLocation.furniture)
					{
						if ((int)f.furniture_type == 14 && location.getTileIndexAt((int)f.tileLocation.X, (int)f.tileLocation.Y + 1, "Buildings") == -1 && location.getTileIndexAt((int)f.tileLocation.X + 1, (int)f.tileLocation.Y + 1, "Buildings") == -1 && location.getTileIndexAt((int)f.tileLocation.X + 2, (int)f.tileLocation.Y + 1, "Buildings") == -1 && location.getTileIndexAt((int)f.tileLocation.X - 1, (int)f.tileLocation.Y + 1, "Buildings") == -1)
						{
							centerPoint = new Vector2((int)f.TileLocation.X, (int)f.TileLocation.Y + 1);
							f.isOn.Value = true;
							f.setFireplace(false);
							break;
						}
					}
				}
				location.TemporarySprites.Clear();
				getActorByName("Harvey").setTilePosition((int)centerPoint.X + 2, (int)centerPoint.Y);
				getActorByName("Harvey").Position = new Vector2(getActorByName("Harvey").Position.X - 32f, getActorByName("Harvey").Position.Y);
				farmer.Position = new Vector2(centerPoint.X * 64f - 32f, centerPoint.Y * 64f + 32f);
				Object o = location.getObjectAtTile((int)centerPoint.X, (int)centerPoint.Y);
				if (o != null)
				{
					o.isTemporarilyInvisible = true;
				}
				o = location.getObjectAtTile((int)centerPoint.X + 1, (int)centerPoint.Y);
				if (o != null)
				{
					o.isTemporarilyInvisible = true;
				}
				o = location.getObjectAtTile((int)centerPoint.X - 1, (int)centerPoint.Y);
				if (o != null)
				{
					o.isTemporarilyInvisible = true;
				}
				o = location.getObjectAtTile((int)centerPoint.X + 2, (int)centerPoint.Y);
				if (o != null)
				{
					o.isTemporarilyInvisible = true;
				}
				Texture2D tempTxture9 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture9,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(385, 423, 48, 32),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(385f, 423f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = centerPoint * 64f + new Vector2(-8f, -16f) * 4f,
					scale = 4f,
					layerDepth = (centerPoint.Y + 0.2f) * 64f / 10000f,
					light = true,
					lightRadius = 4f,
					lightcolor = Color.Black
				});
				List<string> tmp = eventCommands.ToList();
				tmp.Insert(CurrentCommand + 1, "viewport " + (int)centerPoint.X + " " + (int)centerPoint.Y + " true");
				eventCommands = tmp.ToArray();
				break;
			}
			case "harveyKitchenFlame":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.mouseCursors,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11),
					animationLength = 4,
					sourceRectStartingPos = new Vector2(276f, 1985f),
					interval = 100f,
					totalNumberOfLoops = 6,
					position = new Vector2(22f, 22f) * 64f + new Vector2(8f, 5f) * 4f,
					scale = 4f,
					layerDepth = 0.15584001f
				});
				break;
			case "harveyKitchenSetup":
			{
				Texture2D tempTxture8 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture8,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(379, 251, 31, 13),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(379f, 251f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = new Vector2(22f, 22f) * 64f + new Vector2(-2f, 6f) * 4f,
					scale = 4f,
					layerDepth = 0.15551999f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture8,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(391, 235, 5, 13),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(391f, 235f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = new Vector2(21f, 22f) * 64f + new Vector2(8f, 4f) * 4f,
					scale = 4f,
					layerDepth = 0.15551999f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture8,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(399, 229, 11, 21),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(399f, 229f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = new Vector2(19f, 22f) * 64f + new Vector2(8f, -5f) * 4f,
					scale = 4f,
					layerDepth = 0.15551999f
				});
				location.temporarySprites.Add(new TemporaryAnimatedSprite(27, new Vector2(21f, 22f) * 64f + new Vector2(0f, -5f) * 4f, Color.White, 10)
				{
					totalNumberOfLoops = 999,
					layerDepth = 0.15616f
				});
				location.temporarySprites.Add(new TemporaryAnimatedSprite(27, new Vector2(21f, 22f) * 64f + new Vector2(24f, -5f) * 4f, Color.White, 10)
				{
					totalNumberOfLoops = 999,
					flipped = true,
					delayBeforeAnimationStart = 400,
					layerDepth = 0.15616f
				});
				break;
			}
			case "golemDie":
			{
				location.temporarySprites.Add(new TemporaryAnimatedSprite(46, new Vector2(40f, 11f) * 64f, Color.DarkGray, 10));
				Utility.makeTemporarySpriteJuicier(new TemporaryAnimatedSprite(44, new Vector2(40f, 11f) * 64f, Color.LimeGreen, 10), location, 2);
				Texture2D tempTxture7 = Game1.temporaryContent.Load<Texture2D>("Characters\\Monsters\\Wilderness Golem");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture7,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(0, 0, 16, 24),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(0f, 0f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = new Vector2(40f, 11f) * 64f + new Vector2(2f, -8f) * 4f,
					scale = 4f,
					layerDepth = 0.01f,
					rotation = (float)Math.PI / 2f,
					motion = new Vector2(0f, 4f),
					yStopCoordinate = 832
				});
				break;
			}
			case "farmerHoldPainting":
			{
				Texture2D tempTxture6 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.getTemporarySpriteByID(888).sourceRect.X += 15;
				location.getTemporarySpriteByID(888).sourceRectStartingPos.X += 15f;
				location.removeTemporarySpritesWithID(444);
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture6,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(476, 394, 25, 22),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(476f, 394f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = new Vector2(75f, 40f) * 64f + new Vector2(-4f, -33f) * 4f,
					scale = 4f,
					layerDepth = 1f,
					id = 777
				});
				break;
			}
			case "leahStopHoldingPainting":
				location.getTemporarySpriteByID(999).sourceRect.X -= 15;
				location.getTemporarySpriteByID(999).sourceRectStartingPos.X -= 15f;
				location.removeTemporarySpritesWithIDLocal(777);
				Game1.playSound("thudStep");
				break;
			case "leahHoldPainting":
			{
				Texture2D tempTxture5 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.getTemporarySpriteByID(999).sourceRect.X += 15;
				location.getTemporarySpriteByID(999).sourceRectStartingPos.X += 15f;
				int whichPainting = ((!Game1.netWorldState.Value.hasWorldStateID("m_painting0")) ? (Game1.netWorldState.Value.hasWorldStateID("m_painting1") ? 1 : 2) : 0);
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture5,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(400 + whichPainting * 25, 394, 25, 23),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(400 + whichPainting * 25, 394f),
					interval = 5000f,
					totalNumberOfLoops = 9999,
					position = new Vector2(73f, 38f) * 64f + new Vector2(-2f, -16f) * 4f,
					scale = 4f,
					layerDepth = 1f,
					id = 777
				});
				break;
			}
			case "leahPaintingSetup":
			{
				Texture2D tempTxture4 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture4,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(368, 393, 15, 28),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(368f, 393f),
					interval = 5000f,
					totalNumberOfLoops = 99999,
					position = new Vector2(72f, 38f) * 64f + new Vector2(3f, -13f) * 4f,
					scale = 4f,
					layerDepth = 0.1f,
					id = 999
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture4,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(368, 393, 15, 28),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(368f, 393f),
					interval = 5000f,
					totalNumberOfLoops = 99999,
					position = new Vector2(74f, 40f) * 64f + new Vector2(3f, -17f) * 4f,
					scale = 4f,
					layerDepth = 0.1f,
					id = 888
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture4,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(369, 424, 11, 15),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(369f, 424f),
					interval = 9999f,
					totalNumberOfLoops = 99999,
					position = new Vector2(75f, 40f) * 64f + new Vector2(-2f, -11f) * 4f,
					scale = 4f,
					layerDepth = 0.01f,
					id = 444
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.mouseCursors,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(96, 1822, 32, 34),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(96f, 1822f),
					interval = 5000f,
					totalNumberOfLoops = 99999,
					position = new Vector2(79f, 36f) * 64f,
					scale = 4f,
					layerDepth = 0.1f
				});
				break;
			}
			case "junimoShow":
			{
				Texture2D tempTxture3 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture3,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(393, 350, 19, 14),
					animationLength = 6,
					sourceRectStartingPos = new Vector2(393f, 350f),
					interval = 90f,
					totalNumberOfLoops = 86,
					position = new Vector2(52f, 24f) * 64f + new Vector2(7f, -2f) * 4f,
					scale = 4f,
					layerDepth = 0.95f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture3,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(393, 364, 19, 14),
					animationLength = 4,
					sourceRectStartingPos = new Vector2(393f, 364f),
					interval = 90f,
					totalNumberOfLoops = 31,
					position = new Vector2(52f, 24f) * 64f + new Vector2(7f, -2f) * 4f,
					scale = 4f,
					layerDepth = 0.97f,
					delayBeforeAnimationStart = 11034
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture3,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(393, 378, 19, 14),
					animationLength = 6,
					sourceRectStartingPos = new Vector2(393f, 378f),
					interval = 90f,
					totalNumberOfLoops = 21,
					position = new Vector2(52f, 24f) * 64f + new Vector2(7f, -2f) * 4f,
					scale = 4f,
					layerDepth = 1f,
					delayBeforeAnimationStart = 22069
				});
				break;
			}
			case "samTV":
			{
				Texture2D tempTxture2 = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture2,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(368, 350, 25, 29),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(368f, 350f),
					interval = 5000f,
					totalNumberOfLoops = 99999,
					position = new Vector2(52f, 24f) * 64f + new Vector2(4f, -12f) * 4f,
					scale = 4f,
					layerDepth = 0.9f
				});
				break;
			}
			case "gridballGameTV":
			{
				Texture2D tempTxture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(368, 336, 19, 14),
					animationLength = 7,
					sourceRectStartingPos = new Vector2(368f, 336f),
					interval = 5000f,
					totalNumberOfLoops = 99999,
					position = new Vector2(34f, 3f) * 64f + new Vector2(7f, 13f) * 4f,
					scale = 4f,
					layerDepth = 1f
				});
				break;
			}
			case "shaneSaloonCola":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.mouseCursors,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(552, 1862, 31, 21),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(552f, 1862f),
					interval = 999999f,
					totalNumberOfLoops = 99999,
					position = new Vector2(32f, 17f) * 64f + new Vector2(10f, 3f) * 4f,
					scale = 4f,
					layerDepth = 1E-07f
				});
				break;
			case "luauShorts":
			{
				Vector2 shortsSpot = ((Game1.year % 2 == 0) ? new Vector2(24f, 10f) : new Vector2(35f, 10f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\springobjects", new Microsoft.Xna.Framework.Rectangle(336, 512, 16, 16), 9999f, 1, 99999, shortsSpot * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, -8f),
					acceleration = new Vector2(0f, 0.25f),
					yStopCoordinate = ((int)shortsSpot.Y + 1) * 64,
					xStopCoordinate = ((int)shortsSpot.X - 2) * 64
				});
				break;
			}
			case "qiCave":
			{
				Texture2D tempTxt = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(415, 216, 96, 89),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(415f, 216f),
					interval = 999999f,
					totalNumberOfLoops = 99999,
					position = new Vector2(2f, 2f) * 64f + new Vector2(112f, 25f) * 4f,
					scale = 4f,
					layerDepth = 1E-07f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(370, 272, 107, 64),
					animationLength = 1,
					sourceRectStartingPos = new Vector2(370f, 216f),
					interval = 999999f,
					totalNumberOfLoops = 99999,
					position = new Vector2(2f, 2f) * 64f + new Vector2(67f, 81f) * 4f,
					scale = 4f,
					layerDepth = 1.1E-07f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = Game1.objectSpriteSheet,
					sourceRect = Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, 803, 16, 16),
					sourceRectStartingPos = new Vector2(Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, 803, 16, 16).X, Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, 803, 16, 16).Y),
					animationLength = 1,
					interval = 999999f,
					id = 803,
					totalNumberOfLoops = 99999,
					position = new Vector2(13f, 7f) * 64f + new Vector2(1f, 9f) * 4f,
					scale = 4f,
					layerDepth = 2.1E-06f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(432, 171, 16, 30),
					animationLength = 5,
					sourceRectStartingPos = new Vector2(432f, 171f),
					pingPong = true,
					interval = 100f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(8f, 6f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(432, 171, 16, 30),
					animationLength = 5,
					sourceRectStartingPos = new Vector2(432f, 171f),
					pingPong = true,
					interval = 90f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(5f, 7f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(432, 171, 16, 30),
					animationLength = 5,
					sourceRectStartingPos = new Vector2(432f, 171f),
					pingPong = true,
					interval = 120f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(7f, 10f) * 64f,
					scale = 4f,
					layerDepth = 1f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(432, 171, 16, 30),
					animationLength = 5,
					sourceRectStartingPos = new Vector2(432f, 171f),
					pingPong = true,
					interval = 80f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(15f, 7f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(432, 171, 16, 30),
					animationLength = 5,
					sourceRectStartingPos = new Vector2(432f, 171f),
					pingPong = true,
					interval = 100f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(12f, 11f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(432, 171, 16, 30),
					animationLength = 5,
					sourceRectStartingPos = new Vector2(432f, 171f),
					pingPong = true,
					interval = 105f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(16f, 10f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTxt,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(432, 171, 16, 30),
					animationLength = 5,
					sourceRectStartingPos = new Vector2(432f, 171f),
					pingPong = true,
					interval = 85f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(3f, 9f) * 64f,
					scale = 4f
				});
				break;
			}
			case "removeSprite":
			{
				int spriteId;
				string error4;
				if (!ArgUtility.TryGetInt(args, 2, out spriteId, out error4))
				{
					LogCommandError(args, error4);
				}
				else
				{
					location.removeTemporarySpritesWithID(spriteId);
				}
				break;
			}
			case "willyCrabExperiment":
			{
				Texture2D tempTexture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 127, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 127f),
					pingPong = true,
					interval = 250f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(2f, 4f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 146, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 146f),
					pingPong = true,
					interval = 200f,
					totalNumberOfLoops = 99999,
					id = 1,
					initialPosition = new Vector2(2f, 6f) * 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 8000f,
					yPeriodicRange = 32f,
					position = new Vector2(2f, 6f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 127, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 127f),
					pingPong = true,
					interval = 100f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(1f, 5.75f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 127, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 127f),
					pingPong = true,
					interval = 100f,
					totalNumberOfLoops = 99999,
					id = 11,
					position = new Vector2(5f, 3f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 127, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 127f),
					pingPong = true,
					interval = 140f,
					totalNumberOfLoops = 99999,
					id = 22,
					position = new Vector2(4f, 6f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 127, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 127f),
					pingPong = true,
					interval = 140f,
					totalNumberOfLoops = 99999,
					id = 22,
					position = new Vector2(8.5f, 5f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 146, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 146f),
					pingPong = true,
					interval = 170f,
					totalNumberOfLoops = 99999,
					id = 222,
					position = new Vector2(6f, 3.25f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 146, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 146f),
					pingPong = true,
					interval = 190f,
					totalNumberOfLoops = 99999,
					id = 222,
					position = new Vector2(6f, 6f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 146, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 146f),
					pingPong = true,
					interval = 150f,
					totalNumberOfLoops = 99999,
					id = 222,
					position = new Vector2(7f, 4f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 146, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 146f),
					pingPong = true,
					interval = 200f,
					totalNumberOfLoops = 99999,
					id = 2,
					position = new Vector2(4f, 7f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 127, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 127f),
					pingPong = true,
					interval = 180f,
					totalNumberOfLoops = 99999,
					id = 3,
					position = new Vector2(8f, 6f) * 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 10000f,
					yPeriodicRange = 32f,
					initialPosition = new Vector2(8f, 6f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 146, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 146f),
					pingPong = true,
					interval = 220f,
					totalNumberOfLoops = 99999,
					id = 33,
					position = new Vector2(9f, 6f) * 64f,
					scale = 4f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTexture,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(259, 146, 18, 18),
					animationLength = 3,
					sourceRectStartingPos = new Vector2(259f, 146f),
					pingPong = true,
					interval = 150f,
					totalNumberOfLoops = 99999,
					id = 33,
					position = new Vector2(10f, 5f) * 64f,
					scale = 4f
				});
				break;
			}
			case "springOnionRemove":
				location.removeTemporarySpritesWithID(777);
				break;
			case "springOnionPeel":
			{
				TemporaryAnimatedSprite temporarySpriteByID6 = location.getTemporarySpriteByID(777);
				temporarySpriteByID6.sourceRectStartingPos = new Vector2(144f, 327f);
				temporarySpriteByID6.sourceRect = new Microsoft.Xna.Framework.Rectangle(144, 327, 112, 112);
				break;
			}
			case "springOnionDemo":
			{
				Texture2D tempTex = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\temporary_sprites_1");
				location.TemporarySprites.Add(new TemporaryAnimatedSprite
				{
					texture = tempTex,
					sourceRect = new Microsoft.Xna.Framework.Rectangle(144, 215, 112, 112),
					animationLength = 2,
					sourceRectStartingPos = new Vector2(144f, 215f),
					interval = 200f,
					totalNumberOfLoops = 99999,
					id = 777,
					position = new Vector2(Game1.graphics.GraphicsDevice.Viewport.Width / 2 - 264, Game1.graphics.GraphicsDevice.Viewport.Height / 3 - 264),
					local = true,
					scale = 4f,
					destroyable = false,
					overrideLocationDestroy = true
				});
				break;
			}
			case "springOnion":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(1, 129, 16, 16), 200f, 8, 999999, new Vector2(84f, 39f) * 64f, false, false, 0.4736f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				break;
			case "pamYobaStatue":
			{
				location.objects.Remove(new Vector2(26f, 9f));
				location.objects.Add(new Vector2(26f, 9f), ItemRegistry.Create<Object>("(BC)34"));
				GameLocation gameLocation = Game1.RequireLocation("Trailer_Big");
				gameLocation.objects.Remove(new Vector2(26f, 9f));
				gameLocation.objects.Add(new Vector2(26f, 9f), ItemRegistry.Create<Object>("(BC)34"));
				break;
			}
			case "arcaneBook":
			{
				for (int i10 = 0; i10 < 16; i10++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(536, 1945, 8, 8), new Vector2(128f, 792f) + new Vector2(Game1.random.Next(32), Game1.random.Next(32) - i10 * 4), false, 0f, Color.White)
					{
						interval = 50f,
						totalNumberOfLoops = 99999,
						animationLength = 7,
						layerDepth = 1f,
						scale = 4f,
						alphaFade = 0.008f,
						motion = new Vector2(0f, -0.5f)
					});
				}
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(325, 1977, 18, 18), new Vector2(160f, 800f), false, 0f, Color.White)
				{
					interval = 25f,
					totalNumberOfLoops = 99999,
					animationLength = 3,
					layerDepth = 1f,
					scale = 1f,
					scaleChange = 1f,
					scaleChangeChange = -0.05f,
					alpha = 0.65f,
					alphaFade = 0.005f,
					motion = new Vector2(-8f, -8f),
					acceleration = new Vector2(0.4f, 0.4f)
				});
				for (int i9 = 0; i9 < 16; i9++)
				{
					aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(372, 1956, 10, 10), new Vector2(2f, 12f) * 64f + new Vector2(Game1.random.Next(-32, 64), 0f), false, 0.002f, Color.Gray)
					{
						alpha = 0.75f,
						motion = new Vector2(1f, -1f) + new Vector2((float)(Game1.random.Next(100) - 50) / 100f, (float)(Game1.random.Next(100) - 50) / 100f),
						interval = 99999f,
						layerDepth = 0.0384f + (float)Game1.random.Next(100) / 10000f,
						scale = 3f,
						scaleChange = 0.01f,
						rotationChange = (float)Game1.random.Next(-5, 6) * (float)Math.PI / 256f,
						delayBeforeAnimationStart = i9 * 25
					});
				}
				location.setMapTileIndex(2, 12, 2143, "Front", 1);
				break;
			}
			case "stopShakeTent":
				location.getTemporarySpriteByID(999).shakeIntensity = 0f;
				break;
			case "shakeTent":
				location.getTemporarySpriteByID(999).shakeIntensity = 1f;
				break;
			case "EmilyCamping":
				showGroundObjects = false;
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(644, 1578, 59, 53), 999999f, 1, 99999, new Vector2(26f, 9f) * 64f + new Vector2(-16f, 0f), false, false, 0.0788f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(675, 1299, 29, 24), 999999f, 1, 99999, new Vector2(27f, 14f) * 64f, false, false, 0.001f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 99
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11), new Vector2(27f, 14f) * 64f + new Vector2(8f, 4f) * 4f, false, 0f, Color.White)
				{
					interval = 50f,
					totalNumberOfLoops = 99999,
					animationLength = 4,
					light = true,
					lightID = 666,
					id = 666,
					lightRadius = 2f,
					scale = 4f,
					layerDepth = 0.01f
				});
				Game1.currentLightSources.Add(new LightSource(4, new Vector2(27f, 14f) * 64f, 2f, LightSource.LightContext.None, 0L));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(585, 1846, 26, 22), 999999f, 1, 99999, new Vector2(25f, 12f) * 64f + new Vector2(-32f, 0f), false, false, 0.001f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 96
				});
				AmbientLocationSounds.addSound(new Vector2(27f, 14f), 1);
				break;
			case "curtainOpen":
				location.getTemporarySpriteByID(999).sourceRect.X = 672;
				Game1.playSound("shwip");
				break;
			case "curtainClose":
				location.getTemporarySpriteByID(999).sourceRect.X = 644;
				Game1.playSound("shwip");
				break;
			case "ClothingTherapy":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(644, 1405, 28, 46), 999999f, 1, 99999, new Vector2(5f, 6f) * 64f + new Vector2(-32f, -144f), false, false, 0.0424f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				break;
			case "EmilySongBackLights":
			{
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				for (int lightcolumns = 0; lightcolumns < 5; lightcolumns++)
				{
					for (int yPos = 0; yPos < Game1.graphics.GraphicsDevice.Viewport.Height + 48; yPos += 48)
					{
						aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(681, 1890, 18, 12), 42241f, 1, 1, new Vector2((lightcolumns + 1) * Game1.graphics.GraphicsDevice.Viewport.Width / 5 - Game1.graphics.GraphicsDevice.Viewport.Width / 7, -24 + yPos), false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
						{
							xPeriodic = true,
							xPeriodicLoopTime = 1760f,
							xPeriodicRange = 128 + yPos / 12 * 4,
							delayBeforeAnimationStart = lightcolumns * 100 + yPos / 4,
							local = true
						});
					}
				}
				for (int numFlyers = 0; numFlyers < 27; numFlyers++)
				{
					int flyerNumber = 0;
					int yPos2 = Game1.random.Next(64, Game1.graphics.GraphicsDevice.Viewport.Height - 64);
					int loopTime = Game1.random.Next(800, 2000);
					int loopRange = Game1.random.Next(32, 64);
					bool pulse = Game1.random.NextDouble() < 0.25;
					int speed = Game1.random.Next(-6, -3);
					for (int tails = 0; tails < 8; tails++)
					{
						aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(616 + flyerNumber * 10, 1891, 10, 10), 42241f, 1, 1, new Vector2(Game1.graphics.GraphicsDevice.Viewport.Width, yPos2), false, false, 0.01f, 0f, Color.White * (1f - (float)tails * 0.11f), 4f, 0f, 0f, 0f)
						{
							yPeriodic = true,
							motion = new Vector2(speed, 0f),
							yPeriodicLoopTime = loopTime,
							pulse = pulse,
							pulseTime = 440f,
							pulseAmount = 1.5f,
							yPeriodicRange = loopRange,
							delayBeforeAnimationStart = 14000 + numFlyers * 900 + tails * 100,
							local = true
						});
					}
				}
				for (int numRainbows2 = 0; numRainbows2 < 15; numRainbows2++)
				{
					int it = 0;
					int yPos3 = Game1.random.Next(Game1.graphics.GraphicsDevice.Viewport.Width - 128);
					for (int xPos = Game1.graphics.GraphicsDevice.Viewport.Height; xPos >= -64; xPos -= 48)
					{
						aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(597, 1888, 16, 16), 99999f, 1, 99999, new Vector2(yPos3, xPos), false, false, 1f, 0.02f, Color.White, 4f, 0f, -(float)Math.PI / 2f, 0f)
						{
							delayBeforeAnimationStart = 27500 + numRainbows2 * 880 + it * 25,
							local = true
						});
						it++;
					}
				}
				for (int numRainbows = 0; numRainbows < 120; numRainbows++)
				{
					aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(626 + numRainbows / 28 * 10, 1891, 10, 10), 2000f, 1, 1, new Vector2(Game1.random.Next(Game1.graphics.GraphicsDevice.Viewport.Width), Game1.random.Next(Game1.graphics.GraphicsDevice.Viewport.Height)), false, false, 0.01f, 0f, Color.White, 0.1f, 0f, 0f, 0f)
					{
						motion = new Vector2(0f, -2f),
						alphaFade = 0.002f,
						scaleChange = 0.5f,
						scaleChangeChange = -0.0085f,
						delayBeforeAnimationStart = 27500 + numRainbows * 110,
						local = true
					});
				}
				break;
			}
			case "EmilyBoomBoxStart":
				location.getTemporarySpriteByID(999).pulse = true;
				location.getTemporarySpriteByID(999).pulseTime = 420f;
				break;
			case "EmilyBoomBoxStop":
				location.getTemporarySpriteByID(999).pulse = false;
				location.getTemporarySpriteByID(999).scale = 4f;
				break;
			case "EmilyBoomBox":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(586, 1871, 24, 14), 99999f, 1, 99999, new Vector2(15f, 4f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				break;
			case "parrotGone":
				location.removeTemporarySpritesWithID(666);
				break;
			case "parrotSlide":
				location.getTemporarySpriteByID(666).yStopCoordinate = 5632;
				location.getTemporarySpriteByID(666).motion.X = 0f;
				location.getTemporarySpriteByID(666).motion.Y = 1f;
				break;
			case "parrotSplat":
				aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 165, 24, 22), 100f, 6, 9999, new Vector2(Game1.viewport.X + Game1.graphics.GraphicsDevice.Viewport.Width, Game1.viewport.Y + 64), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999,
					motion = new Vector2(-2f, 4f),
					acceleration = new Vector2(-0.1f, 0f),
					delayBeforeAnimationStart = 0,
					yStopCoordinate = 5568,
					xStopCoordinate = 1504,
					reachedStopCoordinate = parrotSplat
				});
				break;
			case "parrots1":
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 165, 24, 22), 100f, 6, 9999, new Vector2(Game1.graphics.GraphicsDevice.Viewport.Width, 256f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 32f,
					delayBeforeAnimationStart = 0,
					local = true
				});
				aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 165, 24, 22), 100f, 6, 9999, new Vector2(Game1.graphics.GraphicsDevice.Viewport.Width, 192f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 32f,
					delayBeforeAnimationStart = 600,
					local = true
				});
				aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 165, 24, 22), 100f, 6, 9999, new Vector2(Game1.graphics.GraphicsDevice.Viewport.Width, 320f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 32f,
					delayBeforeAnimationStart = 1200,
					local = true
				});
				break;
			case "EmilySign":
			{
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				for (int numRainbows3 = 0; numRainbows3 < 10; numRainbows3++)
				{
					int iter = 0;
					int yPos4 = Game1.random.Next(Game1.graphics.GraphicsDevice.Viewport.Height - 128);
					for (int xPos2 = Game1.graphics.GraphicsDevice.Viewport.Width; xPos2 >= -64; xPos2 -= 48)
					{
						aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(597, 1888, 16, 16), 99999f, 1, 99999, new Vector2(xPos2, yPos4), false, false, 1f, 0.02f, Color.White, 4f, 0f, 0f, 0f)
						{
							delayBeforeAnimationStart = numRainbows3 * 600 + iter * 25,
							startSound = ((iter == 0) ? "dwoop" : null),
							local = true
						});
						iter++;
					}
				}
				break;
			}
			case "EmilySleeping":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(574, 1892, 11, 11), 1000f, 2, 99999, new Vector2(20f, 3f) * 64f + new Vector2(8f, 32f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				break;
			case "shaneHospital":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(533, 1864, 19, 10), 99999f, 1, 99999, new Vector2(20f, 3f) * 64f + new Vector2(16f, 12f), false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				break;
			case "shaneCliffs":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(533, 1864, 19, 27), 99999f, 1, 99999, new Vector2(83f, 98f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(552, 1862, 31, 21), 99999f, 1, 99999, new Vector2(83f, 98f) * 64f + new Vector2(-16f, 0f), false, false, 0.0001f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(549, 1891, 19, 12), 99999f, 1, 99999, new Vector2(84f, 99f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(549, 1891, 19, 12), 99999f, 1, 99999, new Vector2(82f, 98f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(542, 1893, 4, 6), 99999f, 1, 99999, new Vector2(83f, 99f) * 64f + new Vector2(-8f, 4f) * 4f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "shaneCliffProps":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(549, 1891, 19, 12), 99999f, 1, 99999, new Vector2(104f, 96f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				break;
			case "shaneThrowCan":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(542, 1893, 4, 6), 99999f, 1, 99999, new Vector2(103f, 95f) * 64f + new Vector2(0f, 4f) * 4f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -4f),
					acceleration = new Vector2(0f, 0.25f),
					rotationChange = (float)Math.PI / 128f
				});
				Game1.playSound("shwip");
				break;
			case "jasGift":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(288, 1231, 16, 16), 100f, 6, 1, new Vector2(22f, 16f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999,
					paused = true,
					holdLastFrame = true
				});
				break;
			case "jasGiftOpen":
				location.getTemporarySpriteByID(999).paused = false;
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(537, 1850, 11, 10), 1500f, 1, 1, new Vector2(23f, 16f) * 64f + new Vector2(16f, -48f), false, false, 0.99f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -0.25f),
					delayBeforeAnimationStart = 500,
					yStopCoordinate = 928
				});
				location.temporarySprites.AddRange(Utility.sparkleWithinArea(new Microsoft.Xna.Framework.Rectangle(1440, 992, 128, 64), 5, Color.White, 300));
				break;
			case "waterShaneDone":
				farmer.completelyStopAnimatingOrDoingAction();
				farmer.TemporaryItem = null;
				drawTool = false;
				location.removeTemporarySpritesWithID(999);
				break;
			case "waterShane":
				drawTool = true;
				farmer.TemporaryItem = ItemRegistry.Create("(T)WateringCan");
				farmer.CurrentTool.Update(1, 0, farmer);
				farmer.FarmerSprite.animateOnce(new FarmerSprite.AnimationFrame[4]
				{
					new FarmerSprite.AnimationFrame(58, 0, false, false),
					new FarmerSprite.AnimationFrame(58, 75, false, false, Farmer.showToolSwipeEffect),
					new FarmerSprite.AnimationFrame(59, 100, false, false, Farmer.useTool, true),
					new FarmerSprite.AnimationFrame(45, 500, true, false, Farmer.canMoveNow, true)
				});
				break;
			case "shanePassedOut":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(533, 1864, 19, 27), 99999f, 1, 99999, new Vector2(25f, 7f) * 64f, false, false, 0.01f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 999
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(552, 1862, 31, 21), 99999f, 1, 99999, new Vector2(25f, 7f) * 64f + new Vector2(-16f, 0f), false, false, 0.0001f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "morrisFlying":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(105, 1318, 13, 31), 9999f, 1, 99999, new Vector2(32f, 13f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(4f, -8f),
					rotationChange = (float)Math.PI / 16f,
					shakeIntensity = 1f
				});
				break;
			case "grandpaNight":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 1453, 639, 176), 9999f, 1, 999999, new Vector2(0f, 1f) * 64f, false, false, 0.9f, 0f, Color.Cyan, 4f, 0f, 0f, 0f, true)
				{
					alpha = 0.01f,
					alphaFade = -0.002f,
					local = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 1453, 639, 176), 9999f, 1, 999999, new Vector2(0f, 768f), false, true, 0.9f, 0f, Color.Blue, 4f, 0f, 0f, 0f, true)
				{
					alpha = 0.01f,
					alphaFade = -0.002f,
					local = true
				});
				break;
			case "doneWithSlideShow":
				(location as Summit).isShowingEndSlideshow = false;
				break;
			case "getEndSlideshow":
			{
				Summit obj = location as Summit;
				string[] s = ParseCommands(obj.getEndSlideshow());
				List<string> commandsList = eventCommands.ToList();
				commandsList.InsertRange(CurrentCommand + 1, s);
				eventCommands = commandsList.ToArray();
				obj.isShowingEndSlideshow = true;
				break;
			}
			case "krobusraven":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Characters\\KrobusRaven", new Microsoft.Xna.Framework.Rectangle(0, 0, 32, 32), 100f, 5, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f), false, false, 0.9f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
				{
					pingPong = true,
					motion = new Vector2(-2f, 0f),
					yPeriodic = true,
					yPeriodicLoopTime = 3000f,
					yPeriodicRange = 16f,
					startSound = "shadowpeep"
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Characters\\KrobusRaven", new Microsoft.Xna.Framework.Rectangle(0, 32, 32, 32), 30f, 5, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f), false, false, 0.9f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
				{
					motion = new Vector2(-2.5f, 0f),
					yPeriodic = true,
					yPeriodicLoopTime = 2800f,
					yPeriodicRange = 16f,
					delayBeforeAnimationStart = 8000
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Characters\\KrobusRaven", new Microsoft.Xna.Framework.Rectangle(0, 64, 32, 39), 100f, 4, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f), false, false, 0.9f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
				{
					pingPong = true,
					motion = new Vector2(-3f, 0f),
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 16f,
					delayBeforeAnimationStart = 15000,
					startSound = "fireball"
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1886, 35, 29), 9999f, 1, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					yPeriodic = true,
					yPeriodicLoopTime = 2200f,
					yPeriodicRange = 32f,
					local = true,
					delayBeforeAnimationStart = 20000
				});
				for (int i12 = 0; i12 < 12; i12++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(16, 594, 16, 12), 100f, 2, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f + (float)Game1.random.Next(-128, 128)), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
					{
						motion = new Vector2(-2f, 0f),
						yPeriodic = true,
						yPeriodicLoopTime = Game1.random.Next(1500, 2000),
						yPeriodicRange = 32f,
						local = true,
						delayBeforeAnimationStart = 24000 + i12 * 200,
						startSound = ((i12 == 0) ? "yoba" : null)
					});
				}
				int whenToStart = 0;
				if (Game1.player.mailReceived.Contains("Capsule_Broken"))
				{
					for (int i11 = 0; i11 < 3; i11++)
					{
						location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(639, 785, 16, 16), 100f, 4, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f + (float)Game1.random.Next(-128, 128)), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
						{
							motion = new Vector2(-2f, 0f),
							yPeriodic = true,
							yPeriodicLoopTime = Game1.random.Next(1500, 2000),
							yPeriodicRange = 16f,
							local = true,
							delayBeforeAnimationStart = 30000 + i11 * 500,
							startSound = ((i11 == 0) ? "UFO" : null)
						});
					}
					whenToStart += 5000;
				}
				if (Game1.year <= 2)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors2", new Microsoft.Xna.Framework.Rectangle(150, 259, 9, 9), 10f, 4, 9999999, new Vector2(Game1.viewport.Width + 4, (float)Game1.viewport.Height * 0.33f + 44f), false, false, 0.9f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
					{
						motion = new Vector2(-2f, 0f),
						yPeriodic = true,
						yPeriodicLoopTime = 3000f,
						yPeriodicRange = 8f,
						delayBeforeAnimationStart = 30000 + whenToStart
					});
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("Characters\\KrobusRaven", new Microsoft.Xna.Framework.Rectangle(2, 129, 120, 27), 1090f, 1, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f), false, false, 0.9f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
					{
						motion = new Vector2(-2f, 0f),
						yPeriodic = true,
						yPeriodicLoopTime = 3000f,
						yPeriodicRange = 8f,
						startSound = "discoverMineral",
						delayBeforeAnimationStart = 30000 + whenToStart
					});
					whenToStart += 5000;
				}
				else if (Game1.year <= 3)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors2", new Microsoft.Xna.Framework.Rectangle(150, 259, 9, 9), 10f, 4, 9999999, new Vector2(Game1.viewport.Width + 4, (float)Game1.viewport.Height * 0.33f + 44f), false, false, 0.9f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
					{
						motion = new Vector2(-2f, 0f),
						yPeriodic = true,
						yPeriodicLoopTime = 3000f,
						yPeriodicRange = 8f,
						delayBeforeAnimationStart = 30000 + whenToStart
					});
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("Characters\\KrobusRaven", new Microsoft.Xna.Framework.Rectangle(1, 104, 100, 24), 1090f, 1, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f), false, false, 0.9f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
					{
						motion = new Vector2(-2f, 0f),
						yPeriodic = true,
						yPeriodicLoopTime = 3000f,
						yPeriodicRange = 8f,
						startSound = "newArtifact",
						delayBeforeAnimationStart = 30000 + whenToStart
					});
					whenToStart += 5000;
				}
				if (Game1.MasterPlayer.totalMoneyEarned >= 100000000)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("Characters\\KrobusRaven", new Microsoft.Xna.Framework.Rectangle(125, 108, 34, 50), 1090f, 1, 999999, new Vector2(Game1.viewport.Width, (float)Game1.viewport.Height * 0.33f), false, false, 0.9f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
					{
						motion = new Vector2(-2f, 0f),
						yPeriodic = true,
						yPeriodicLoopTime = 3000f,
						yPeriodicRange = 8f,
						startSound = "discoverMineral",
						delayBeforeAnimationStart = 30000 + whenToStart
					});
					whenToStart += 5000;
				}
				break;
			}
			case "grandpaThumbsUp":
			{
				TemporaryAnimatedSprite temporarySpriteByID3 = location.getTemporarySpriteByID(77777);
				temporarySpriteByID3.texture = Game1.mouseCursors2;
				temporarySpriteByID3.sourceRect = new Microsoft.Xna.Framework.Rectangle(186, 265, 22, 34);
				temporarySpriteByID3.sourceRectStartingPos = new Vector2(186f, 265f);
				temporarySpriteByID3.yPeriodic = true;
				temporarySpriteByID3.yPeriodicLoopTime = 1000f;
				temporarySpriteByID3.yPeriodicRange = 16f;
				temporarySpriteByID3.xPeriodicLoopTime = 2500f;
				temporarySpriteByID3.xPeriodicRange = 16f;
				temporarySpriteByID3.initialPosition = temporarySpriteByID3.position;
				break;
			}
			case "grandpaSpirit":
			{
				TemporaryAnimatedSprite p = new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(555, 1956, 18, 35), 9999f, 1, 99999, new Vector2(-1000f, -1010f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					yStopCoordinate = -64128,
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					motion = new Vector2(0f, 1f),
					overrideLocationDestroy = true,
					id = 77777
				};
				location.temporarySprites.Add(p);
				for (int i13 = 0; i13 < 19; i13++)
				{
					location.temporarySprites.Add(new TemporaryAnimatedSprite(10, new Vector2(32f, 32f), Color.White)
					{
						parentSprite = p,
						delayBeforeAnimationStart = (i13 + 1) * 500,
						overrideLocationDestroy = true,
						scale = 1f,
						alpha = 1f
					});
				}
				break;
			}
			case "farmerForestVision":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(393, 1973, 1, 1), 9999f, 1, 999999, new Vector2(0f, 0f) * 64f, false, false, 0.9f, 0f, Color.LimeGreen * 0.85f, Game1.viewport.Width * 2, 0f, 0f, 0f, true)
				{
					alpha = 0f,
					alphaFade = -0.002f,
					id = 1
				});
				Game1.player.mailReceived.Add("canReadJunimoText");
				int x = -64;
				int y = -64;
				int index = 0;
				int yIndex = 0;
				while (y < Game1.viewport.Height + 128)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(367 + ((index % 2 == 0) ? 8 : 0), 1969, 8, 8), 9999f, 1, 999999, new Vector2(x, y), false, false, 0.99f, 0f, Color.White, 4f, 0f, 0f, 0f, true)
					{
						alpha = 0f,
						alphaFade = -0.0015f,
						xPeriodic = true,
						xPeriodicLoopTime = 4000f,
						xPeriodicRange = 64f,
						yPeriodic = true,
						yPeriodicLoopTime = 5000f,
						yPeriodicRange = 96f,
						rotationChange = (float)Game1.random.Next(-1, 2) * (float)Math.PI / 256f,
						id = 1,
						delayBeforeAnimationStart = 20 * index
					});
					x += 128;
					if (x > Game1.viewport.Width + 64)
					{
						yIndex++;
						x = ((yIndex % 2 == 0) ? (-64) : 64);
						y += 128;
					}
					index++;
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(648, 895, 51, 101), 9999f, 1, 999999, new Vector2(Game1.viewport.Width / 2 - 100, Game1.viewport.Height / 2 - 240), false, false, 1f, 0f, Color.White, 3f, 0f, 0f, 0f, true)
				{
					alpha = 0f,
					alphaFade = -0.001f,
					id = 1,
					delayBeforeAnimationStart = 6000,
					scaleChange = 0.004f,
					xPeriodic = true,
					xPeriodicLoopTime = 4000f,
					xPeriodicRange = 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 5000f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(648, 895, 51, 101), 9999f, 1, 999999, new Vector2(Game1.viewport.Width / 4 - 100, Game1.viewport.Height / 4 - 120), false, false, 0.99f, 0f, Color.White, 3f, 0f, 0f, 0f, true)
				{
					alpha = 0f,
					alphaFade = -0.001f,
					id = 1,
					delayBeforeAnimationStart = 9000,
					scaleChange = 0.004f,
					xPeriodic = true,
					xPeriodicLoopTime = 4000f,
					xPeriodicRange = 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 5000f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(648, 895, 51, 101), 9999f, 1, 999999, new Vector2(Game1.viewport.Width * 3 / 4, Game1.viewport.Height / 3 - 120), false, false, 0.98f, 0f, Color.White, 3f, 0f, 0f, 0f, true)
				{
					alpha = 0f,
					alphaFade = -0.001f,
					id = 1,
					delayBeforeAnimationStart = 12000,
					scaleChange = 0.004f,
					xPeriodic = true,
					xPeriodicLoopTime = 4000f,
					xPeriodicRange = 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 5000f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(648, 895, 51, 101), 9999f, 1, 999999, new Vector2(Game1.viewport.Width / 3 - 60, Game1.viewport.Height * 3 / 4 - 120), false, false, 0.97f, 0f, Color.White, 3f, 0f, 0f, 0f, true)
				{
					alpha = 0f,
					alphaFade = -0.001f,
					id = 1,
					delayBeforeAnimationStart = 15000,
					scaleChange = 0.004f,
					xPeriodic = true,
					xPeriodicLoopTime = 4000f,
					xPeriodicRange = 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 5000f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(648, 895, 51, 101), 9999f, 1, 999999, new Vector2(Game1.viewport.Width * 2 / 3, Game1.viewport.Height * 2 / 3 - 120), false, false, 0.96f, 0f, Color.White, 3f, 0f, 0f, 0f, true)
				{
					alpha = 0f,
					alphaFade = -0.001f,
					id = 1,
					delayBeforeAnimationStart = 18000,
					scaleChange = 0.004f,
					xPeriodic = true,
					xPeriodicLoopTime = 4000f,
					xPeriodicRange = 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 5000f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(648, 895, 51, 101), 9999f, 1, 999999, new Vector2(Game1.viewport.Width / 8, Game1.viewport.Height / 5 - 120), false, false, 0.95f, 0f, Color.White, 3f, 0f, 0f, 0f, true)
				{
					alpha = 0f,
					alphaFade = -0.001f,
					id = 1,
					delayBeforeAnimationStart = 19500,
					scaleChange = 0.004f,
					xPeriodic = true,
					xPeriodicLoopTime = 4000f,
					xPeriodicRange = 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 5000f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(648, 895, 51, 101), 9999f, 1, 999999, new Vector2(Game1.viewport.Width * 2 / 3, Game1.viewport.Height / 5 - 120), false, false, 0.94f, 0f, Color.White, 3f, 0f, 0f, 0f, true)
				{
					alpha = 0f,
					alphaFade = -0.001f,
					id = 1,
					delayBeforeAnimationStart = 21000,
					scaleChange = 0.004f,
					xPeriodic = true,
					xPeriodicLoopTime = 4000f,
					xPeriodicRange = 64f,
					yPeriodic = true,
					yPeriodicLoopTime = 5000f,
					yPeriodicRange = 32f
				});
				break;
			}
			case "wizardWarp":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(387, 1965, 16, 31), 9999f, 1, 999999, new Vector2(8f, 16f) * 64f + new Vector2(0f, 4f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(2f, -2f),
					acceleration = new Vector2(0.1f, 0f),
					scaleChange = -0.02f,
					alphaFade = 0.001f
				});
				break;
			case "witchFlyby":
				Game1.screenOverlayTempSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1886, 35, 29), 9999f, 1, 999999, new Vector2(Game1.graphics.GraphicsDevice.Viewport.Width, 192f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-4f, 0f),
					acceleration = new Vector2(-0.025f, 0f),
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 64f,
					local = true
				});
				break;
			case "wizardWarp2":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(387, 1965, 16, 31), 9999f, 1, 999999, new Vector2(54f, 34f) * 64f + new Vector2(0f, 4f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-1f, 2f),
					acceleration = new Vector2(-0.1f, 0.2f),
					scaleChange = 0.03f,
					alphaFade = 0.001f
				});
				break;
			case "junimoCageGone":
				location.removeTemporarySpritesWithID(1);
				break;
			case "junimoCageGone2":
				location.removeTemporarySpritesWithID(1);
				Game1.viewportFreeze = true;
				Game1.viewport.X = -1000;
				Game1.viewport.Y = -1000;
				break;
			case "junimoCage":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(325, 1977, 18, 19), 60f, 3, 999999, new Vector2(10f, 17f) * 64f + new Vector2(0f, -4f), false, false, 0f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 1f,
					lightcolor = Color.Black,
					id = 1,
					shakeIntensity = 0f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(379, 1991, 5, 5), 9999f, 1, 999999, new Vector2(10f, 17f) * 64f + new Vector2(0f, -4f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 0.5f,
					lightcolor = Color.Black,
					id = 1,
					xPeriodic = true,
					xPeriodicLoopTime = 2000f,
					xPeriodicRange = 24f,
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 24f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(379, 1991, 5, 5), 9999f, 1, 999999, new Vector2(10f, 17f) * 64f + new Vector2(72f, -4f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 0.5f,
					lightcolor = Color.Black,
					id = 1,
					xPeriodic = true,
					xPeriodicLoopTime = 2000f,
					xPeriodicRange = -24f,
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 24f,
					delayBeforeAnimationStart = 250
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(379, 1991, 5, 5), 9999f, 1, 999999, new Vector2(10f, 17f) * 64f + new Vector2(0f, 52f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 0.5f,
					lightcolor = Color.Black,
					id = 1,
					xPeriodic = true,
					xPeriodicLoopTime = 2000f,
					xPeriodicRange = -24f,
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 24f,
					delayBeforeAnimationStart = 450
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(379, 1991, 5, 5), 9999f, 1, 999999, new Vector2(10f, 17f) * 64f + new Vector2(72f, 52f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 0.5f,
					lightcolor = Color.Black,
					id = 1,
					xPeriodic = true,
					xPeriodicLoopTime = 2000f,
					xPeriodicRange = 24f,
					yPeriodic = true,
					yPeriodicLoopTime = 2000f,
					yPeriodicRange = 24f,
					delayBeforeAnimationStart = 650
				});
				break;
			case "WizardPromise":
				Utility.addSprinklesToLocation(location, 16, 15, 9, 9, 2000, 50, Color.White);
				break;
			case "wizardSewerMagic":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11), 50f, 4, 20, new Vector2(15f, 13f) * 64f + new Vector2(8f, 0f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 1f,
					lightcolor = Color.Black,
					alphaFade = 0.005f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11), 50f, 4, 20, new Vector2(17f, 13f) * 64f + new Vector2(8f, 0f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 1f,
					lightcolor = Color.Black,
					alphaFade = 0.005f
				});
				break;
			case "linusCampfire":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1985, 12, 11), 50f, 4, 99999, new Vector2(29f, 9f) * 64f + new Vector2(8f, 0f), false, false, 0.0576f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 3f,
					lightcolor = Color.Black
				});
				break;
			case "linusLights":
				Game1.currentLightSources.Add(new LightSource(2, new Vector2(55f, 62f) * 64f, 2f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(2, new Vector2(60f, 62f) * 64f, 2f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(2, new Vector2(57f, 60f) * 64f, 3f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(2, new Vector2(57f, 60f) * 64f, 2f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(2, new Vector2(47f, 70f) * 64f, 2f, LightSource.LightContext.None, 0L));
				Game1.currentLightSources.Add(new LightSource(2, new Vector2(52f, 63f) * 64f, 2f, LightSource.LightContext.None, 0L));
				break;
			case "wed":
			{
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				Game1.flashAlpha = 1f;
				for (int i14 = 0; i14 < 150; i14++)
				{
					Vector2 position = new Vector2(Game1.random.Next(Game1.viewport.Width - 128), Game1.random.Next(Game1.viewport.Height));
					int scale = Game1.random.Next(2, 5);
					aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(424, 1266, 8, 8), 60f + (float)Game1.random.Next(-10, 10), 7, 999999, position, false, false, 0.99f, 0f, Color.White, scale, 0f, 0f, 0f)
					{
						local = true,
						motion = new Vector2(0.1625f, -0.25f) * scale
					});
				}
				Game1.changeMusicTrack("wedding", false, MusicContext.Event);
				Game1.musicPlayerVolume = 0f;
				break;
			}
			case "wedding":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(540, 1196, 98, 54), 99999f, 1, 99999, new Vector2(25f, 60f) * 64f + new Vector2(0f, -64f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(540, 1250, 98, 25), 99999f, 1, 99999, new Vector2(25f, 60f) * 64f + new Vector2(0f, 54f) * 4f + new Vector2(0f, -64f), false, false, 0f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(527, 1249, 12, 25), 99999f, 1, 99999, new Vector2(24f, 62f) * 64f, false, false, 0f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(527, 1249, 12, 25), 99999f, 1, 99999, new Vector2(32f, 62f) * 64f, false, false, 0f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(527, 1249, 12, 25), 99999f, 1, 99999, new Vector2(24f, 69f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(527, 1249, 12, 25), 99999f, 1, 99999, new Vector2(32f, 69f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "jojaCeremony":
			{
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				for (int i15 = 0; i15 < 16; i15++)
				{
					Vector2 position2 = new Vector2(Game1.random.Next(Game1.viewport.Width - 128), Game1.viewport.Height + i15 * 64);
					aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(534, 1413, 11, 16), 99999f, 1, 99999, position2, false, false, 0.99f, 0f, Color.DeepSkyBlue, 4f, 0f, 0f, 0f)
					{
						local = true,
						motion = new Vector2(0.25f, -1.5f),
						acceleration = new Vector2(0f, -0.001f),
						id = 79797 + i15
					});
					aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(545, 1413, 11, 34), 99999f, 1, 99999, position2 + new Vector2(0f, 0f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
					{
						local = true,
						motion = new Vector2(0.25f, -1.5f),
						acceleration = new Vector2(0f, -0.001f),
						id = 79797 + i15
					});
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 1363, 114, 58), 99999f, 1, 99999, new Vector2(50f, 20f) * 64f, false, false, 0.1472f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(595, 1387, 14, 34), 200f, 3, 99999, new Vector2(48f, 20f) * 64f, false, false, 0.15720001f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					pingPong = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(595, 1387, 14, 34), 200f, 3, 99999, new Vector2(49f, 20f) * 64f, false, false, 0.15720001f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					pingPong = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(595, 1387, 14, 34), 210f, 3, 99999, new Vector2(62f, 20f) * 64f, false, false, 0.15720001f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					pingPong = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(595, 1387, 14, 34), 190f, 3, 99999, new Vector2(60f, 20f) * 64f, false, false, 0.15720001f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					pingPong = true
				});
				break;
			}
			case "ccCelebration":
			{
				aboveMapSprites = new TemporaryAnimatedSpriteList();
				for (int i16 = 0; i16 < 32; i16++)
				{
					Vector2 position3 = new Vector2(Game1.random.Next(Game1.viewport.Width - 128), Game1.viewport.Height + i16 * 64);
					aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(534, 1413, 11, 16), 99999f, 1, 99999, position3, false, false, 1f, 0f, Utility.getRandomRainbowColor(), 4f, 0f, 0f, 0f)
					{
						local = true,
						motion = new Vector2(0.25f, -1.5f),
						acceleration = new Vector2(0f, -0.001f),
						id = 79797 + i16
					});
					aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(545, 1413, 11, 34), 99999f, 1, 99999, position3 + new Vector2(0f, 0f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
					{
						local = true,
						motion = new Vector2(0.25f, -1.5f),
						acceleration = new Vector2(0f, -0.001f),
						id = 79797 + i16
					});
				}
				if (Game1.IsWinter)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\marnie_winter_dance", new Microsoft.Xna.Framework.Rectangle(0, 0, 20, 26), 400f, 3, 99999, new Vector2(53f, 21f) * 64f, false, false, 0.5f, 0f, Color.White, 4f, 0f, 0f, 0f)
					{
						pingPong = true
					});
				}
				else
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(558, 1425, 20, 26), 400f, 3, 99999, new Vector2(53f, 21f) * 64f, false, false, 0.5f, 0f, Color.White, 4f, 0f, 0f, 0f)
					{
						pingPong = true
					});
				}
				break;
			}
			case "dickBag":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(528, 1435, 16, 16), 99999f, 1, 99999, new Vector2(48f, 7f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "dickGlitter":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(432, 1435, 16, 16), 100f, 6, 99999, new Vector2(47f, 8f) * 64f, false, false, 1f, 0f, Color.White, 2f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(432, 1435, 16, 16), 100f, 6, 99999, new Vector2(47f, 8f) * 64f + new Vector2(32f, 0f), false, false, 1f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					delayBeforeAnimationStart = 200
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(432, 1435, 16, 16), 100f, 6, 99999, new Vector2(47f, 8f) * 64f + new Vector2(32f, 32f), false, false, 1f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					delayBeforeAnimationStart = 300
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(432, 1435, 16, 16), 100f, 6, 99999, new Vector2(47f, 8f) * 64f + new Vector2(0f, 32f), false, false, 1f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					delayBeforeAnimationStart = 100
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(432, 1435, 16, 16), 100f, 6, 99999, new Vector2(47f, 8f) * 64f + new Vector2(16f, 16f), false, false, 1f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					delayBeforeAnimationStart = 400
				});
				break;
			case "iceFishingCatch":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(160, 368, 16, 32), 500f, 3, 99999, new Vector2(68f, 30f) * 64f, false, false, 0.1984f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(160, 368, 16, 32), 510f, 3, 99999, new Vector2(74f, 30f) * 64f, false, false, 0.1984f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(160, 368, 16, 32), 490f, 3, 99999, new Vector2(67f, 36f) * 64f, false, false, 0.2368f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(160, 368, 16, 32), 500f, 3, 99999, new Vector2(76f, 35f) * 64f, false, false, 0.2304f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "secretGift":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(288, 1231, 16, 16), new Vector2(30f, 70f) * 64f + new Vector2(0f, -21f), false, 0f, Color.White)
				{
					animationLength = 1,
					interval = 999999f,
					id = 666,
					scale = 4f
				});
				break;
			case "secretGiftOpen":
			{
				TemporaryAnimatedSprite t = location.getTemporarySpriteByID(666);
				if (t != null)
				{
					t.animationLength = 6;
					t.interval = 100f;
					t.totalNumberOfLoops = 1;
					t.timer = 0f;
					t.holdLastFrame = true;
				}
				break;
			}
			case "moonlightJellies":
				showGroundObjects = false;
				npcControllers?.Clear();
				underwaterSprites = new TemporaryAnimatedSpriteList();
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(26f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2560,
					delayBeforeAnimationStart = 10000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(29f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2560,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(31f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2624,
					delayBeforeAnimationStart = 12000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(20f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 1728,
					delayBeforeAnimationStart = 14000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(17f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 1856,
					delayBeforeAnimationStart = 19500,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(16f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2048,
					delayBeforeAnimationStart = 20300,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(17f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2496,
					delayBeforeAnimationStart = 21500,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(16f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2816,
					delayBeforeAnimationStart = 22400,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(12f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2688,
					delayBeforeAnimationStart = 23200,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(9f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2752,
					delayBeforeAnimationStart = 24000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(18f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 1920,
					delayBeforeAnimationStart = 24600,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(33f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2560,
					delayBeforeAnimationStart = 25600,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(36f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2496,
					delayBeforeAnimationStart = 26900,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(304, 16, 16, 16), 200f, 3, 9999, new Vector2(21f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 2500f,
					xPeriodicRange = 10f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2176,
					delayBeforeAnimationStart = 28000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(304, 16, 16, 16), 200f, 3, 9999, new Vector2(20f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 2500f,
					xPeriodicRange = 10f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2240,
					delayBeforeAnimationStart = 28500,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(304, 16, 16, 16), 200f, 3, 9999, new Vector2(22f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 2500f,
					xPeriodicRange = 10f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2304,
					delayBeforeAnimationStart = 28500,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(304, 16, 16, 16), 200f, 3, 9999, new Vector2(33f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 2500f,
					xPeriodicRange = 10f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2752,
					delayBeforeAnimationStart = 29000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(304, 16, 16, 16), 200f, 3, 9999, new Vector2(36f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 2500f,
					xPeriodicRange = 10f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2752,
					delayBeforeAnimationStart = 30000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 32, 16, 16), 250f, 3, 9999, new Vector2(28f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-0.5f, -0.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 4000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 2f,
					xStopCoordinate = 1216,
					yStopCoordinate = 2432,
					delayBeforeAnimationStart = 32000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(40f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2560,
					delayBeforeAnimationStart = 10000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(42f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2752,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(43f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2624,
					delayBeforeAnimationStart = 12000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(45f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2496,
					delayBeforeAnimationStart = 14000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(46f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 1856,
					delayBeforeAnimationStart = 19500,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(48f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2240,
					delayBeforeAnimationStart = 20300,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(49f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2560,
					delayBeforeAnimationStart = 21500,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(50f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 1920,
					delayBeforeAnimationStart = 22400,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(51f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2112,
					delayBeforeAnimationStart = 23200,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(52f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2432,
					delayBeforeAnimationStart = 24000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(53f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2240,
					delayBeforeAnimationStart = 24600,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(54f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 1920,
					delayBeforeAnimationStart = 25600,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(55f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2560,
					delayBeforeAnimationStart = 26900,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(4f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 1920,
					delayBeforeAnimationStart = 24000,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(5f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2560,
					delayBeforeAnimationStart = 24600,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(3f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2176,
					delayBeforeAnimationStart = 25600,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(6f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2368,
					delayBeforeAnimationStart = 26900,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(256, 16, 16, 16), 250f, 3, 9999, new Vector2(8f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1f),
					xPeriodic = true,
					xPeriodicLoopTime = 3000f,
					xPeriodicRange = 16f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2688,
					delayBeforeAnimationStart = 26900,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(304, 16, 16, 16), 200f, 3, 9999, new Vector2(50f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 2500f,
					xPeriodicRange = 10f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2688,
					delayBeforeAnimationStart = 28500,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(304, 16, 16, 16), 200f, 3, 9999, new Vector2(51f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 2500f,
					xPeriodicRange = 10f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2752,
					delayBeforeAnimationStart = 28500,
					pingPong = true
				});
				underwaterSprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(304, 16, 16, 16), 200f, 3, 9999, new Vector2(52f, 49f) * 64f, false, false, 0.1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -1.5f),
					xPeriodic = true,
					xPeriodicLoopTime = 2500f,
					xPeriodicRange = 10f,
					light = true,
					lightcolor = Color.Black,
					lightRadius = 1f,
					yStopCoordinate = 2816,
					delayBeforeAnimationStart = 29000,
					pingPong = true
				});
				break;
			case "candleBoatMove":
				showGroundObjects = false;
				location.getTemporarySpriteByID(1).motion = new Vector2(0f, 2f);
				break;
			case "candleBoat":
				showGroundObjects = false;
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(240, 112, 16, 32), 1000f, 2, 99999, new Vector2(22f, 36f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 1,
					light = true,
					lightRadius = 2f,
					lightcolor = Color.Black
				});
				break;
			case "linusMoney":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(397, 1941, 19, 20), 9999f, 1, 99999, new Vector2(-1002f, -1000f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					startSound = "money",
					delayBeforeAnimationStart = 10,
					overrideLocationDestroy = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(397, 1941, 19, 20), 9999f, 1, 99999, new Vector2(-1003f, -1002f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					startSound = "money",
					delayBeforeAnimationStart = 100,
					overrideLocationDestroy = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(397, 1941, 19, 20), 9999f, 1, 99999, new Vector2(-999f, -1000f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					startSound = "money",
					delayBeforeAnimationStart = 200,
					overrideLocationDestroy = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(397, 1941, 19, 20), 9999f, 1, 99999, new Vector2(-1004f, -1001f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					startSound = "money",
					delayBeforeAnimationStart = 300,
					overrideLocationDestroy = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(397, 1941, 19, 20), 9999f, 1, 99999, new Vector2(-1001f, -998f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					startSound = "money",
					delayBeforeAnimationStart = 400,
					overrideLocationDestroy = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(397, 1941, 19, 20), 9999f, 1, 99999, new Vector2(-998f, -999f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					startSound = "money",
					delayBeforeAnimationStart = 500,
					overrideLocationDestroy = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(397, 1941, 19, 20), 9999f, 1, 99999, new Vector2(-998f, -1002f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					startSound = "money",
					delayBeforeAnimationStart = 600,
					overrideLocationDestroy = true
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(397, 1941, 19, 20), 9999f, 1, 99999, new Vector2(-997f, -1001f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					startSound = "money",
					delayBeforeAnimationStart = 700,
					overrideLocationDestroy = true
				});
				break;
			case "joshDinner":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(649, 9999f, 1, 9999, new Vector2(6f, 4f) * 64f + new Vector2(8f, 32f), false, false)
				{
					layerDepth = 0.0256f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(664, 9999f, 1, 9999, new Vector2(8f, 4f) * 64f + new Vector2(-8f, 32f), false, false)
				{
					layerDepth = 0.0256f
				});
				break;
			case "alexDiningDog":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(324, 1936, 12, 20), 80f, 4, 99999, new Vector2(7f, 2f) * 64f + new Vector2(2f, -8f) * 4f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 1
				});
				break;
			case "JoshMom":
			{
				TemporaryAnimatedSprite parent = new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(416, 1931, 58, 65), 750f, 2, 99999, new Vector2(Game1.viewport.Width / 2, Game1.viewport.Height), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					alpha = 0.6f,
					local = true,
					xPeriodic = true,
					xPeriodicLoopTime = 2000f,
					xPeriodicRange = 32f,
					motion = new Vector2(0f, -1.25f),
					initialPosition = new Vector2(Game1.viewport.Width / 2, Game1.viewport.Height)
				};
				location.temporarySprites.Add(parent);
				for (int i17 = 0; i17 < 19; i17++)
				{
					location.temporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(516, 1916, 7, 10), 99999f, 1, 99999, new Vector2(64f, 32f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
					{
						alphaFade = 0.01f,
						local = true,
						motion = new Vector2(-1f, -1f),
						parentSprite = parent,
						delayBeforeAnimationStart = (i17 + 1) * 1000
					});
				}
				break;
			}
			case "joshSteak":
				location.temporarySprites.Clear();
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(324, 1936, 12, 20), 80f, 4, 99999, new Vector2(53f, 67f) * 64f + new Vector2(3f, 3f) * 4f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 1
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(497, 1918, 11, 11), 999f, 1, 9999, new Vector2(50f, 68f) * 64f + new Vector2(32f, -8f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "joshDog":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(324, 1916, 12, 20), 500f, 6, 9999, new Vector2(53f, 67f) * 64f + new Vector2(3f, 3f) * 4f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					id = 1
				});
				break;
			case "joshFootball":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(405, 1916, 14, 8), 40f, 6, 9999, new Vector2(25f, 16f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					rotation = -(float)Math.PI / 4f,
					rotationChange = (float)Math.PI / 200f,
					motion = new Vector2(6f, -4f),
					acceleration = new Vector2(0f, 0.2f),
					xStopCoordinate = 1856,
					reachedStopCoordinate = catchFootball,
					layerDepth = 1f,
					id = 56232
				});
				break;
			case "skateboardFly":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1875, 16, 6), 9999f, 1, 999, new Vector2(26f, 90f) * 64f, false, false, 1E-05f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					rotationChange = (float)Math.PI / 24f,
					motion = new Vector2(-8f, -10f),
					acceleration = new Vector2(0.02f, 0.3f),
					yStopCoordinate = 5824,
					xStopCoordinate = 1024,
					layerDepth = 1f
				});
				break;
			case "samSkate1":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 0, 0, 0), 9999f, 1, 999, new Vector2(12f, 90f) * 64f, false, false, 1E-05f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(4f, 0f),
					acceleration = new Vector2(-0.008f, 0f),
					xStopCoordinate = 1344,
					reachedStopCoordinate = samPreOllie,
					attachedCharacter = getActorByName("Sam"),
					id = 92473
				});
				break;
			case "beachStuff":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(324, 1887, 47, 29), 9999f, 1, 999, new Vector2(44f, 21f) * 64f, false, false, 1E-05f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "dropEgg":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(176, 800f, 1, 0, new Vector2(6f, 4f) * 64f + new Vector2(0f, 32f), false, false)
				{
					rotationChange = (float)Math.PI / 24f,
					motion = new Vector2(0f, -7f),
					acceleration = new Vector2(0f, 0.3f),
					endFunction = eggSmashEndFunction,
					layerDepth = 1f
				});
				break;
			case "balloonBirds":
			{
				int positionOffset;
				string error5;
				if (!ArgUtility.TryGetOptionalInt(args, 2, out positionOffset, out error5))
				{
					LogCommandError(args, error5);
					break;
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(48f, positionOffset + 12) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					delayBeforeAnimationStart = 1500
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(47f, positionOffset + 13) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					delayBeforeAnimationStart = 1250
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(46f, positionOffset + 14) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					delayBeforeAnimationStart = 1100
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(45f, positionOffset + 15) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					delayBeforeAnimationStart = 1000
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(46f, positionOffset + 16) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					delayBeforeAnimationStart = 1080
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(47f, positionOffset + 17) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					delayBeforeAnimationStart = 1300
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(48f, positionOffset + 18) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-3f, 0f),
					delayBeforeAnimationStart = 1450
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(46f, positionOffset + 15) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-4f, 0f),
					delayBeforeAnimationStart = 5450
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(48f, positionOffset + 10) * 64f, false, false, 0f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, 0f),
					delayBeforeAnimationStart = 500
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(47f, positionOffset + 11) * 64f, false, false, 0f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, 0f),
					delayBeforeAnimationStart = 250
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(46f, positionOffset + 12) * 64f, false, false, 0f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, 0f),
					delayBeforeAnimationStart = 100
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(45f, positionOffset + 13) * 64f, false, false, 0f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, 0f)
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(46f, positionOffset + 14) * 64f, false, false, 0f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, 0f),
					delayBeforeAnimationStart = 80
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(47f, positionOffset + 15) * 64f, false, false, 0f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, 0f),
					delayBeforeAnimationStart = 300
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(388, 1894, 24, 22), 100f, 6, 9999, new Vector2(48f, positionOffset + 16) * 64f, false, false, 0f, 0f, Color.White, 2f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, 0f),
					delayBeforeAnimationStart = 450
				});
				break;
			}
			case "marcelloLand":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 1183, 84, 160), 10000f, 1, 99999, (new Vector2(25f, 19f) + eventPositionTileOffset) * 64f + new Vector2(-23f, 0f) * 4f, false, false, 2E-05f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, 2f),
					yStopCoordinate = (41 + (int)eventPositionTileOffset.Y) * 64 - 640,
					reachedStopCoordinate = marcelloBalloonLand,
					attachedCharacter = getActorByName("Marcello"),
					id = 1
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(84, 1205, 38, 26), 10000f, 1, 99999, (new Vector2(25f, 19f) + eventPositionTileOffset) * 64f + new Vector2(0f, 134f) * 4f, false, false, (41f + eventPositionTileOffset.Y) * 64f / 10000f + 0.0001f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, 2f),
					id = 2
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(24, 1343, 36, 19), 7000f, 1, 99999, (new Vector2(25f, 40f) + eventPositionTileOffset) * 64f, false, false, 1E-05f, 0f, Color.White, 0f, 0f, 0f, 0f)
				{
					scaleChange = 0.01f,
					id = 3
				});
				break;
			case "elliottBoat":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(461, 1843, 32, 51), 1000f, 2, 9999, new Vector2(15f, 26f) * 64f + new Vector2(-28f, 0f), false, false, 0.1664f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "sebastianRide":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(405, 1843, 14, 9), 40f, 4, 999, new Vector2(19f, 8f) * 64f + new Vector2(0f, 28f), false, false, 0.1792f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(-2f, 0f)
				});
				break;
			case "sebastianOnBike":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(0, 1600, 64, 128), 80f, 8, 9999, new Vector2(19f, 27f) * 64f + new Vector2(32f, -16f), false, true, 0.1792f, 0f, Color.White, 1f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(405, 1854, 47, 33), 9999f, 1, 999, new Vector2(17f, 27f) * 64f + new Vector2(0f, -8f), false, false, 0.1792f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "umbrella":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(324, 1843, 27, 23), 80f, 3, 9999, new Vector2(12f, 39f) * 64f + new Vector2(-20f, -104f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "sebastianGarage":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(276, 1843, 48, 42), 9999f, 1, 999, new Vector2(17f, 23f) * 64f + new Vector2(0f, 8f), false, false, 0.1472f, 0f, Color.White, 4f, 0f, 0f, 0f));
				getActorByName("Sebastian").HideShadow = true;
				break;
			case "leahLaptop":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(130, 1849, 19, 19), 9999f, 1, 999, new Vector2(12f, 10f) * 64f + new Vector2(0f, 24f), false, false, 0.1856f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "leahPicnic":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(96, 1808, 32, 48), 9999f, 1, 999, new Vector2(75f, 37f) * 64f, false, false, 0.2496f, 0f, Color.White, 4f, 0f, 0f, 0f));
				NPC n2 = new NPC(new AnimatedSprite(festivalContent, "Characters\\" + (farmer.IsMale ? "LeahExMale" : "LeahExFemale"), 0, 16, 32), new Vector2(-100f, -100f) * 64f, 2, "LeahEx");
				n2.AllowDynamicAppearance = false;
				actors.Add(n2);
				break;
			}
			case "leahShow":
			{
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(144, 688, 16, 32), 9999f, 1, 999, new Vector2(29f, 59f) * 64f - new Vector2(0f, 16f), false, false, 0.37750003f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(112, 656, 16, 64), 9999f, 1, 999, new Vector2(29f, 56f) * 64f, false, false, 0.3776f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(144, 688, 16, 32), 9999f, 1, 999, new Vector2(33f, 59f) * 64f - new Vector2(0f, 16f), false, false, 0.37750003f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(128, 688, 16, 32), 9999f, 1, 999, new Vector2(33f, 58f) * 64f, false, false, 0.3776f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(160, 656, 32, 64), 9999f, 1, 999, new Vector2(29f, 60f) * 64f, false, false, 0.4032f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(144, 688, 16, 32), 9999f, 1, 999, new Vector2(34f, 63f) * 64f, false, false, 0.4031f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(113, 592, 16, 64), 100f, 4, 99999, new Vector2(34f, 60f) * 64f, false, false, 0.4032f, 0f, Color.White, 4f, 0f, 0f, 0f));
				NPC n2 = new NPC(new AnimatedSprite(festivalContent, "Characters\\" + (farmer.IsMale ? "LeahExMale" : "LeahExFemale"), 0, 16, 32), new Vector2(46f, 57f) * 64f, 2, "LeahEx");
				n2.AllowDynamicAppearance = false;
				actors.Add(n2);
				break;
			}
			case "leahTree":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(744, 999999f, 1, 0, new Vector2(42f, 8f) * 64f, false, false));
				break;
			case "haleyRoomDark":
				Game1.currentLightSources.Clear();
				Game1.ambientLight = new Color(200, 200, 100);
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(743, 999999f, 1, 0, new Vector2(4f, 1f) * 64f, false, false)
				{
					light = true,
					lightcolor = new Color(0, 255, 255),
					lightRadius = 2f
				});
				break;
			case "pennyCook":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(256, 1856, 64, 128), new Vector2(10f, 6f) * 64f, false, 0f, Color.White)
				{
					layerDepth = 1f,
					animationLength = 6,
					interval = 75f,
					motion = new Vector2(0f, -0.5f)
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(256, 1856, 64, 128), new Vector2(10f, 6f) * 64f + new Vector2(16f, 0f), false, 0f, Color.White)
				{
					layerDepth = 0.1f,
					animationLength = 6,
					interval = 75f,
					motion = new Vector2(0f, -0.5f),
					delayBeforeAnimationStart = 500
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(256, 1856, 64, 128), new Vector2(10f, 6f) * 64f + new Vector2(-16f, 0f), false, 0f, Color.White)
				{
					layerDepth = 1f,
					animationLength = 6,
					interval = 75f,
					motion = new Vector2(0f, -0.5f),
					delayBeforeAnimationStart = 750
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(256, 1856, 64, 128), new Vector2(10f, 6f) * 64f, false, 0f, Color.White)
				{
					layerDepth = 0.1f,
					animationLength = 6,
					interval = 75f,
					motion = new Vector2(0f, -0.5f),
					delayBeforeAnimationStart = 1000
				});
				break;
			case "pennyFieldTrip":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(0, 1813, 86, 54), 999999f, 1, 0, new Vector2(68f, 44f) * 64f, false, false, 0.0001f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "pennyMess":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(739, 999999f, 1, 0, new Vector2(10f, 5f) * 64f, false, false));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(740, 999999f, 1, 0, new Vector2(15f, 5f) * 64f, false, false));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(741, 999999f, 1, 0, new Vector2(16f, 6f) * 64f, false, false));
				break;
			case "heart":
			{
				Vector2 tile2;
				string error7;
				if (!ArgUtility.TryGetVector2(args, 2, out tile2, out error7, true))
				{
					LogCommandError(args, error7);
					break;
				}
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(211, 428, 7, 6), 2000f, 1, 0, OffsetPosition(tile2) * 64f + new Vector2(-16f, -16f), false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					motion = new Vector2(0f, -0.5f),
					alphaFade = 0.01f
				});
				break;
			}
			case "robot":
			{
				TemporaryAnimatedSprite parent2 = new TemporaryAnimatedSprite(getActorByName("robot").Sprite.textureName, new Microsoft.Xna.Framework.Rectangle(35, 42, 35, 42), 50f, 1, 9999, new Vector2(13f, 27f) * 64f - new Vector2(0f, 32f), false, false, 0.98f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					acceleration = new Vector2(0f, -0.01f),
					accelerationChange = new Vector2(0f, -0.0001f)
				};
				location.temporarySprites.Add(parent2);
				for (int i21 = 0; i21 < 420; i21++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(Game1.random.Next(4) * 64, 320, 64, 64), new Vector2(Game1.random.Next(96), 136f), false, 0.01f, Color.White * 0.75f)
					{
						layerDepth = 1f,
						delayBeforeAnimationStart = i21 * 10,
						animationLength = 1,
						currentNumberOfLoops = 0,
						interval = 9999f,
						motion = new Vector2(Game1.random.Next(-100, 100) / (i21 + 20), 0.25f + (float)i21 / 100f),
						parentSprite = parent2
					});
				}
				break;
			}
			case "maruTrapdoor":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(640, 1632, 16, 32), 150f, 4, 0, new Vector2(1f, 5f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(688, 1632, 16, 32), 99999f, 1, 0, new Vector2(1f, 5f) * 64f, false, false, 0.99f, 0f, Color.White, 4f, 0f, 0f, 0f)
				{
					delayBeforeAnimationStart = 500
				});
				break;
			case "maruElectrocution":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(432, 1664, 16, 32), 40f, 1, 20, new Vector2(7f, 5f) * 64f - new Vector2(-4f, 8f), true, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "maruTelescope":
			{
				for (int i20 = 0; i20 < 9; i20++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(256, 1680, 16, 16), 80f, 5, 0, new Vector2(Game1.random.Next(1, 28), Game1.random.Next(1, 20)) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 0f, 0f)
					{
						delayBeforeAnimationStart = 8000 + i20 * Game1.random.Next(2000),
						motion = new Vector2(4f, 4f)
					});
				}
				if (this.id == "5183338")
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(206, 1827, 15, 27), 80f, 4, 999, new Vector2(-2f, 13f) * 64f, false, false, 1f, 0f, Color.White, 4f, 0f, 1.2f, 0f)
					{
						delayBeforeAnimationStart = 7000,
						motion = new Vector2(2f, -0.5f),
						alpha = 0.01f,
						alphaFade = -0.005f
					});
				}
				break;
			}
			case "maruBeaker":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(738, 1380f, 1, 0, new Vector2(9f, 14f) * 64f + new Vector2(0f, 32f), false, false)
				{
					rotationChange = (float)Math.PI / 24f,
					motion = new Vector2(0f, -7f),
					acceleration = new Vector2(0f, 0.2f),
					endFunction = beakerSmashEndFunction,
					layerDepth = 1f
				});
				break;
			case "abbyOuijaCandles":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(737, 999999f, 1, 0, new Vector2(5f, 9f) * 64f, false, false)
				{
					light = true,
					lightRadius = 1f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(737, 999999f, 1, 0, new Vector2(7f, 8f) * 64f, false, false)
				{
					light = true,
					lightRadius = 1f
				});
				break;
			case "abbyManyBats":
			{
				for (int i18 = 0; i18 < 100; i18++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(640, 1664, 16, 16), 80f, 4, 9999, new Vector2(23f, 9f) * 64f, false, false, 1f, 0.003f, Color.White, 4f, 0f, 0f, 0f)
					{
						xPeriodic = true,
						xPeriodicLoopTime = Game1.random.Next(1500, 2500),
						xPeriodicRange = Game1.random.Next(64, 192),
						motion = new Vector2(Game1.random.Next(-2, 3), Game1.random.Next(-8, -4)),
						delayBeforeAnimationStart = i18 * 30,
						startSound = ((i18 % 10 == 0 || Game1.random.NextDouble() < 0.1) ? "batScreech" : null)
					});
				}
				for (int i19 = 0; i19 < 100; i19++)
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(640, 1664, 16, 16), 80f, 4, 9999, new Vector2(23f, 9f) * 64f, false, false, 1f, 0.003f, Color.White, 4f, 0f, 0f, 0f)
					{
						motion = new Vector2(Game1.random.Next(-4, 5), Game1.random.Next(-8, -4)),
						delayBeforeAnimationStart = 10 + i19 * 30
					});
				}
				break;
			}
			case "abbyOneBat":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(640, 1664, 16, 16), 80f, 4, 9999, new Vector2(23f, 9f) * 64f, false, false, 1f, 0.003f, Color.White, 4f, 0f, 0f, 0f)
				{
					xPeriodic = true,
					xPeriodicLoopTime = 2000f,
					xPeriodicRange = 128f,
					motion = new Vector2(0f, -8f)
				});
				break;
			case "swordswipe":
			{
				Vector2 position4;
				string error6;
				if (!ArgUtility.TryGetVector2(args, 2, out position4, out error6, true))
				{
					LogCommandError(args, error6);
				}
				else
				{
					location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(0, 960, 128, 128), 60f, 4, 0, position4 * 64f + new Vector2(0f, -32f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f));
				}
				break;
			}
			case "abbyOuija":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(0, 960, 128, 128), 60f, 4, 0, new Vector2(6f, 9f) * 64f, false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f));
				break;
			case "abbyvideoscreen":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(167, 1714, 19, 14), 100f, 3, 9999, new Vector2(2f, 3f) * 64f + new Vector2(7f, 12f) * 4f, false, false, 0.0002f, 0f, Color.White, 4f, 0f, 0f, 0f));
				break;
			case "abbyGraveyard":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(736, 999999f, 1, 0, new Vector2(48f, 86f) * 64f, false, false));
				break;
			case "abbyAtLake":
				location.TemporarySprites.Add(new TemporaryAnimatedSprite(735, 999999f, 1, 0, new Vector2(48f, 30f) * 64f, false, false)
				{
					light = true,
					lightRadius = 2f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(48f, 30f) * 64f + new Vector2(32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2000f,
					yPeriodicLoopTime = 1600f,
					xPeriodicRange = 32f,
					yPeriodicRange = 21f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(48f, 30f) * 64f + new Vector2(32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 1000f,
					yPeriodicLoopTime = 1600f,
					xPeriodicRange = 16f,
					yPeriodicRange = 21f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(48f, 30f) * 64f + new Vector2(32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2400f,
					yPeriodicLoopTime = 2800f,
					xPeriodicRange = 21f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(48f, 30f) * 64f + new Vector2(32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2000f,
					yPeriodicLoopTime = 2400f,
					xPeriodicRange = 16f,
					yPeriodicRange = 16f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(66f, 34f) * 64f + new Vector2(-32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2000f,
					yPeriodicLoopTime = 2600f,
					xPeriodicRange = 21f,
					yPeriodicRange = 48f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(66f, 34f) * 64f + new Vector2(32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2000f,
					yPeriodicLoopTime = 2600f,
					xPeriodicRange = 32f,
					yPeriodicRange = 21f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(66f, 34f) * 64f + new Vector2(32f, 32f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 4000f,
					yPeriodicLoopTime = 5000f,
					xPeriodicRange = 42f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(66f, 34f) * 64f + new Vector2(0f, -32f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 4000f,
					yPeriodicLoopTime = 5500f,
					xPeriodicRange = 32f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(69f, 28f) * 64f + new Vector2(-32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2400f,
					yPeriodicLoopTime = 3600f,
					xPeriodicRange = 32f,
					yPeriodicRange = 21f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(69f, 28f) * 64f + new Vector2(32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2500f,
					yPeriodicLoopTime = 3600f,
					xPeriodicRange = 42f,
					yPeriodicRange = 51f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(69f, 28f) * 64f + new Vector2(32f, 32f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 4500f,
					yPeriodicLoopTime = 3000f,
					xPeriodicRange = 21f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(69f, 28f) * 64f + new Vector2(0f, -32f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 5000f,
					yPeriodicLoopTime = 4500f,
					xPeriodicRange = 64f,
					yPeriodicRange = 48f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(72f, 33f) * 64f + new Vector2(-32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2000f,
					yPeriodicLoopTime = 3000f,
					xPeriodicRange = 32f,
					yPeriodicRange = 21f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(72f, 33f) * 64f + new Vector2(32f, 0f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 2900f,
					yPeriodicLoopTime = 3200f,
					xPeriodicRange = 21f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(72f, 33f) * 64f + new Vector2(32f, 32f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 4200f,
					yPeriodicLoopTime = 3300f,
					xPeriodicRange = 16f,
					yPeriodicRange = 32f
				});
				location.TemporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Microsoft.Xna.Framework.Rectangle(232, 328, 4, 4), 9999999f, 1, 0, new Vector2(72f, 33f) * 64f + new Vector2(0f, -32f), false, false, 1f, 0f, Color.White, 1f, 0f, 0f, 0f)
				{
					lightcolor = Color.Orange,
					light = true,
					lightRadius = 0.2f,
					xPeriodic = true,
					yPeriodic = true,
					xPeriodicLoopTime = 5100f,
					yPeriodicLoopTime = 4000f,
					xPeriodicRange = 32f,
					yPeriodicRange = 16f
				});
				break;
			}
		}

		private Microsoft.Xna.Framework.Rectangle skipBounds()
		{
			int scale = 4;
			int width = 22 * scale;
			Microsoft.Xna.Framework.Rectangle skipBounds = new Microsoft.Xna.Framework.Rectangle(Game1.viewport.Width - width - 8, Game1.viewport.Height - 64, width, 15 * scale);
			Utility.makeSafe(ref skipBounds);
			return skipBounds;
		}

		public void receiveMouseClick(int x, int y)
		{
			if (!Game1.options.SnappyMenus && !skipped && skippable && skipBounds().Contains(x, y))
			{
				skipped = true;
				skipEvent();
				Game1.freezeControls = false;
			}
			popBalloons(x, y);
		}

		public void skipEvent()
		{
			if (playerControlSequence)
			{
				EndPlayerControlSequence();
			}
			Game1.playSound("drumkit6");
			actorPositionsAfterMove.Clear();
			foreach (NPC i in actors)
			{
				bool ignore_stop_animation = i.Sprite.ignoreStopAnimation;
				i.Sprite.ignoreStopAnimation = true;
				i.Halt();
				i.Sprite.ignoreStopAnimation = ignore_stop_animation;
				resetDialogueIfNecessary(i);
			}
			farmer.Halt();
			farmer.ignoreCollisions = false;
			Game1.exitActiveMenu();
			Game1.fadeClear();
			Game1.dialogueUp = false;
			Game1.dialogueTyping = false;
			Game1.pauseTime = 0f;
			string[] array = actionsOnSkip;
			if (array != null && array.Length != 0)
			{
				string[] array2 = actionsOnSkip;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler;
				foreach (string action in array2)
				{
					string error;
					Exception ex;
					if (!TriggerActionManager.TryRunAction(action, out error, out ex))
					{
						IGameLogger log = Game1.log;
						defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(47, 3);
						defaultInterpolatedStringHandler.AppendLiteral("Event '");
						defaultInterpolatedStringHandler.AppendFormatted(id);
						defaultInterpolatedStringHandler.AppendLiteral("' failed applying post-skip action '");
						defaultInterpolatedStringHandler.AppendFormatted(action);
						defaultInterpolatedStringHandler.AppendLiteral("': ");
						defaultInterpolatedStringHandler.AppendFormatted(error);
						defaultInterpolatedStringHandler.AppendLiteral(".");
						log.Error(defaultInterpolatedStringHandler.ToStringAndClear(), ex);
					}
				}
				IGameLogger log2 = Game1.log;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(38, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Event '");
				defaultInterpolatedStringHandler.AppendFormatted(id);
				defaultInterpolatedStringHandler.AppendLiteral("' applied post-skip actions [");
				defaultInterpolatedStringHandler.AppendFormatted(string.Join(", ", actionsOnSkip));
				defaultInterpolatedStringHandler.AppendLiteral("].");
				log2.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			switch (id)
			{
			case "33":
				if (!Game1.player.craftingRecipes.ContainsKey("Drum Block"))
				{
					Game1.player.craftingRecipes.Add("Drum Block", 0);
				}
				if (!Game1.player.craftingRecipes.ContainsKey("Flute Block"))
				{
					Game1.player.craftingRecipes.Add("Flute Block", 0);
				}
				endBehaviors();
				break;
			case "897405":
			case "1590166":
				if (!gotPet)
				{
					string defaultName = ((!Game1.player.IsMale) ? Game1.content.LoadString((Game1.player.whichPetType == "Dog") ? "Strings\\StringsFromCSFiles:Event.cs.1797" : "Strings\\StringsFromCSFiles:Event.cs.1796") : Game1.content.LoadString(Game1.player.catPerson ? "Strings\\StringsFromCSFiles:Event.cs.1794" : "Strings\\StringsFromCSFiles:Event.cs.1795"));
					namePet(defaultName);
				}
				endBehaviors();
				break;
			case "980559":
				if (Game1.player.GetSkillLevel(1) < 1)
				{
					Game1.player.setSkillLevel("Fishing", 1);
				}
				if (!Game1.player.Items.ContainsId("(T)TrainingRod"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(T)TrainingRod"));
				}
				endBehaviors();
				break;
			case "-157039427":
				endBehaviors(new string[2] { "End", "islandDepart" }, Game1.currentLocation);
				break;
			case "-888999":
			{
				Object o = ItemRegistry.Create<Object>("(O)864");
				o.specialItem = true;
				o.questItem.Value = true;
				Game1.player.addItemByMenuIfNecessary(o);
				Game1.player.addQuest("130");
				endBehaviors();
				break;
			}
			case "-666777":
				if (!Game1.netWorldState.Value.ActivatedGoldenParrot)
				{
					Game1.player.team.RequestLimitedNutDrops("Birdie", null, 0, 0, 5, 5);
				}
				if (!Game1.MasterPlayer.hasOrWillReceiveMail("gotBirdieReward"))
				{
					Game1.addMailForTomorrow("gotBirdieReward", true, true);
				}
				Game1.player.craftingRecipes.TryAdd("Fairy Dust", 0);
				endBehaviors();
				break;
			case "6497428":
				endBehaviors(new string[2] { "End", "Leo" }, Game1.currentLocation);
				break;
			case "-78765":
				endBehaviors(new string[2] { "End", "tunnelDepart" }, Game1.currentLocation);
				break;
			case "690006":
				if (!Game1.player.Items.ContainsId("(O)680"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(O)680"));
				}
				endBehaviors();
				break;
			case "191393":
				if (!Game1.player.Items.ContainsId("(BC)116"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(BC)116"));
				}
				endBehaviors(new string[4] { "End", "position", "52", "20" }, Game1.currentLocation);
				break;
			case "2123343":
				endBehaviors(new string[2] { "End", "newDay" }, Game1.currentLocation);
				break;
			case "404798":
				if (!Game1.player.Items.ContainsId("(T)Pan"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(T)Pan"));
				}
				endBehaviors();
				break;
			case "26":
				Game1.player.craftingRecipes.TryAdd("Wild Bait", 0);
				endBehaviors();
				break;
			case "611173":
				if (!Game1.player.activeDialogueEvents.ContainsKey("pamHouseUpgrade") && !Game1.player.activeDialogueEvents.ContainsKey("pamHouseUpgradeAnonymous"))
				{
					Game1.player.activeDialogueEvents.Add("pamHouseUpgrade", 4);
				}
				endBehaviors();
				break;
			case "3091462":
				if (!Game1.player.Items.ContainsId("(F)1802"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(F)1802"));
				}
				endBehaviors();
				break;
			case "3918602":
				if (!Game1.player.Items.ContainsId("(F)1309"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(F)1309"));
				}
				endBehaviors();
				break;
			case "19":
				Game1.player.cookingRecipes.TryAdd("Cookies", 0);
				endBehaviors();
				break;
			case "992553":
				Game1.player.craftingRecipes.TryAdd("Furnace", 0);
				Game1.player.addQuest("11");
				endBehaviors();
				break;
			case "900553":
				Game1.player.craftingRecipes.TryAdd("Garden Pot", 0);
				if (!Game1.player.Items.ContainsId("(BC)62"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(BC)62"));
				}
				endBehaviors();
				break;
			case "980558":
				Game1.player.craftingRecipes.TryAdd("Mini-Jukebox", 0);
				if (!Game1.player.Items.ContainsId("(BC)209"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(BC)209"));
				}
				endBehaviors();
				break;
			case "60367":
				endBehaviors(new string[2] { "End", "beginGame" }, Game1.currentLocation);
				break;
			case "739330":
				if (!Game1.player.Items.ContainsId("(T)BambooPole"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(T)BambooPole"));
				}
				endBehaviors(new string[4] { "End", "position", "43", "36" }, Game1.currentLocation);
				break;
			case "112":
				endBehaviors();
				Game1.player.mailReceived.Add("canReadJunimoText");
				break;
			case "558292":
				Game1.player.eventsSeen.Remove("2146991");
				endBehaviors(new string[2] { "End", "bed" }, Game1.currentLocation);
				break;
			case "100162":
				if (!Game1.player.Items.ContainsId("(W)0"))
				{
					Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(W)0"));
				}
				Game1.player.Position = new Vector2(-9999f, -99999f);
				endBehaviors();
				break;
			default:
				endBehaviors();
				break;
			}
		}

		public void receiveActionPress(int xTile, int yTile)
		{
			if (xTile != playerControlTargetTile.X || yTile != playerControlTargetTile.Y)
			{
				return;
			}
			string text = playerControlSequenceID;
			if (!(text == "haleyBeach"))
			{
				if (text == "haleyBeach2")
				{
					EndPlayerControlSequence();
					CurrentCommand++;
				}
			}
			else
			{
				props.Clear();
				Game1.playSound("coin");
				playerControlTargetTile = new Point(35, 11);
				playerControlSequenceID = "haleyBeach2";
			}
		}

		public void startSecretSantaEvent()
		{
			playerControlSequence = false;
			playerControlSequenceID = null;
			string rawCommands;
			if (!TryGetFestivalDataForYear("secretSanta", out rawCommands))
			{
				Game1.log.Error("Festival " + id + " doesn't have the required 'secretSanta' data field.");
			}
			eventCommands = ParseCommands(rawCommands);
			doingSecretSanta = true;
			setUpSecretSantaCommands();
			currentCommand = 0;
		}

		public void festivalUpdate(GameTime time)
		{
			Game1.player.team.festivalScoreStatus.UpdateState(Game1.player.festivalScore.ToString() ?? "");
			if (festivalTimer > 0)
			{
				int oldTime = festivalTimer;
				festivalTimer -= time.ElapsedGameTime.Milliseconds;
				if (playerControlSequenceID == "iceFishing")
				{
					if (!Game1.player.UsingTool)
					{
						Game1.player.forceCanMove();
					}
					if (oldTime % 500 < festivalTimer % 500)
					{
						NPC temp = getActorByName("Pam");
						temp.Sprite.sourceRect.Offset(temp.Sprite.SourceRect.Width, 0);
						if (temp.Sprite.sourceRect.X >= temp.Sprite.Texture.Width)
						{
							temp.Sprite.sourceRect.Offset(-temp.Sprite.Texture.Width, 0);
						}
						temp = getActorByName("Elliott");
						temp.Sprite.sourceRect.Offset(temp.Sprite.SourceRect.Width, 0);
						if (temp.Sprite.sourceRect.X >= temp.Sprite.Texture.Width)
						{
							temp.Sprite.sourceRect.Offset(-temp.Sprite.Texture.Width, 0);
						}
						temp = getActorByName("Willy");
						temp.Sprite.sourceRect.Offset(temp.Sprite.SourceRect.Width, 0);
						if (temp.Sprite.sourceRect.X >= temp.Sprite.Texture.Width)
						{
							temp.Sprite.sourceRect.Offset(-temp.Sprite.Texture.Width, 0);
						}
					}
					if (oldTime % 29900 < festivalTimer % 29900)
					{
						getActorByName("Willy").shake(500);
						Game1.playSound("dwop");
						temporaryLocation.temporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(112, 432, 16, 16), getActorByName("Willy").Position + new Vector2(0f, -96f), false, 0.015f, Color.White)
						{
							layerDepth = 1f,
							scale = 4f,
							interval = 9999f,
							motion = new Vector2(0f, -1f)
						});
					}
					if (oldTime % 45900 < festivalTimer % 45900)
					{
						getActorByName("Pam").shake(500);
						Game1.playSound("dwop");
						temporaryLocation.temporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(112, 432, 16, 16), getActorByName("Pam").Position + new Vector2(0f, -96f), false, 0.015f, Color.White)
						{
							layerDepth = 1f,
							scale = 4f,
							interval = 9999f,
							motion = new Vector2(0f, -1f)
						});
					}
					if (oldTime % 59900 < festivalTimer % 59900)
					{
						getActorByName("Elliott").shake(500);
						Game1.playSound("dwop");
						temporaryLocation.temporarySprites.Add(new TemporaryAnimatedSprite("Maps\\Festivals", new Microsoft.Xna.Framework.Rectangle(112, 432, 16, 16), getActorByName("Elliott").Position + new Vector2(0f, -96f), false, 0.015f, Color.White)
						{
							layerDepth = 1f,
							scale = 4f,
							interval = 9999f,
							motion = new Vector2(0f, -1f)
						});
					}
				}
				if (festivalTimer <= 0)
				{
					Game1.player.Halt();
					string text = playerControlSequenceID;
					if (!(text == "eggHunt"))
					{
						if (text == "iceFishing")
						{
							EndPlayerControlSequence();
							string rawCommands2;
							if (!TryGetFestivalDataForYear("afterIceFishing", out rawCommands2))
							{
								Game1.log.Error("Festival " + id + " doesn't have the required 'afterIceFishing' data field.");
							}
							eventCommands = ParseCommands(rawCommands2);
							currentCommand = 0;
							if (Game1.activeClickableMenu != null)
							{
								Game1.activeClickableMenu.emergencyShutDown();
							}
							Game1.activeClickableMenu = null;
							if (Game1.player.UsingTool)
							{
								(Game1.player.CurrentTool as FishingRod)?.doneFishing(Game1.player);
							}
							Game1.screenOverlayTempSprites.Clear();
							Game1.player.forceCanMove();
						}
					}
					else
					{
						EndPlayerControlSequence();
						string rawCommands;
						if (!TryGetFestivalDataForYear("afterEggHunt", out rawCommands))
						{
							Game1.log.Error("Festival " + id + " doesn't have the required 'afterEggHunt' data field.");
						}
						eventCommands = ParseCommands(rawCommands);
						currentCommand = 0;
					}
				}
			}
			if (startSecretSantaAfterDialogue && !Game1.dialogueUp)
			{
				Game1.globalFadeToBlack(startSecretSantaEvent, 0.01f);
				startSecretSantaAfterDialogue = false;
			}
			Game1.player.festivalScore = Math.Min(Game1.player.festivalScore, 9999);
		}

		private void setUpSecretSantaCommands()
		{
			Point secretSantaTile;
			try
			{
				secretSantaTile = getActorByName(mySecretSanta.Name).TilePoint;
			}
			catch
			{
				mySecretSanta = getActorByName("Lewis");
				secretSantaTile = getActorByName(mySecretSanta.Name).TilePoint;
			}
			string beforeDialogue = mySecretSanta.Dialogue?.GetValueOrDefault("WinterStar_GiveGift_Before");
			string afterDialogue = mySecretSanta.Dialogue?.GetValueOrDefault("WinterStar_GiveGift_After");
			if (Game1.player.spouse == mySecretSanta.Name)
			{
				beforeDialogue = mySecretSanta.Dialogue?.GetValueOrDefault("WinterStar_GiveGift_Before_" + (Game1.player.isRoommate(mySecretSanta.Name) ? "Roommate" : "Spouse")) ?? beforeDialogue;
				afterDialogue = mySecretSanta.Dialogue?.GetValueOrDefault("WinterStar_GiveGift_After_" + (Game1.player.isRoommate(mySecretSanta.Name) ? "Roommate" : "Spouse")) ?? afterDialogue;
			}
			if (mySecretSanta.Age == 2)
			{
				if (beforeDialogue == null)
				{
					beforeDialogue = Game1.LoadStringByGender(mySecretSanta.Gender, "Strings\\StringsFromCSFiles:Event.cs.1497");
				}
				if (afterDialogue == null)
				{
					afterDialogue = Game1.LoadStringByGender(mySecretSanta.Gender, "Strings\\StringsFromCSFiles:Event.cs.1498");
				}
			}
			else if (mySecretSanta.Manners == 2)
			{
				if (beforeDialogue == null)
				{
					beforeDialogue = Game1.LoadStringByGender(mySecretSanta.Gender, "Strings\\StringsFromCSFiles:Event.cs.1501");
				}
				if (afterDialogue == null)
				{
					afterDialogue = Game1.LoadStringByGender(mySecretSanta.Gender, "Strings\\StringsFromCSFiles:Event.cs.1504");
				}
			}
			else
			{
				if (beforeDialogue == null)
				{
					beforeDialogue = Game1.LoadStringByGender(mySecretSanta.Gender, "Strings\\StringsFromCSFiles:Event.cs.1499");
				}
				if (afterDialogue == null)
				{
					afterDialogue = Game1.LoadStringByGender(mySecretSanta.Gender, "Strings\\StringsFromCSFiles:Event.cs.1500");
				}
			}
			for (int i = 0; i < eventCommands.Length; i++)
			{
				eventCommands[i] = eventCommands[i].Replace("secretSanta", mySecretSanta.Name);
				eventCommands[i] = eventCommands[i].Replace("warpX", secretSantaTile.X.ToString() ?? "");
				eventCommands[i] = eventCommands[i].Replace("warpY", secretSantaTile.Y.ToString() ?? "");
				eventCommands[i] = eventCommands[i].Replace("dialogue1", beforeDialogue);
				eventCommands[i] = eventCommands[i].Replace("dialogue2", afterDialogue);
			}
		}

		public void drawFarmers(SpriteBatch b)
		{
			foreach (Farmer farmerActor in farmerActors)
			{
				farmerActor.draw(b);
			}
		}

		public virtual bool ShouldHideCharacter(NPC n)
		{
			if (n is Child && doingSecretSanta)
			{
				return true;
			}
			return false;
		}

		public void draw(SpriteBatch b)
		{
			if (currentCustomEventScript != null)
			{
				currentCustomEventScript.draw(b);
				return;
			}
			foreach (NPC j in actors)
			{
				if (!ShouldHideCharacter(j))
				{
					j.Name.Equals("Marcello");
					if (j.ySourceRectOffset == 0)
					{
						j.draw(b);
					}
					else
					{
						j.draw(b, j.ySourceRectOffset);
					}
				}
			}
			foreach (Object prop in props)
			{
				prop.drawAsProp(b);
			}
			foreach (Prop festivalProp in festivalProps)
			{
				festivalProp.draw(b);
			}
			if (isSpecificFestival("fall16"))
			{
				Vector2 start = Game1.GlobalToLocal(Game1.viewport, new Vector2(37f, 56f) * 64f);
				start.X += 4f;
				int xCutoff = (int)start.X + 168;
				start.Y += 8f;
				for (int i = 0; i < Game1.player.team.grangeDisplay.Count; i++)
				{
					if (Game1.player.team.grangeDisplay[i] != null)
					{
						start.Y += 42f;
						start.X += 4f;
						b.Draw(Game1.shadowTexture, start, Game1.shadowTexture.Bounds, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.0001f);
						start.Y -= 42f;
						start.X -= 4f;
						Game1.player.team.grangeDisplay[i].drawInMenu(b, start, 1f, 1f, (float)i / 1000f + 0.001f, StackDrawType.Hide);
					}
					start.X += 60f;
					if (start.X >= (float)xCutoff)
					{
						start.X = xCutoff - 168;
						start.Y += 64f;
					}
				}
			}
			if (drawTool)
			{
				Game1.drawTool(farmer);
			}
		}

		public void drawUnderWater(SpriteBatch b)
		{
			if (underwaterSprites == null)
			{
				return;
			}
			foreach (TemporaryAnimatedSprite underwaterSprite in underwaterSprites)
			{
				underwaterSprite.draw(b);
			}
		}

		public void drawAfterMap(SpriteBatch b)
		{
			if (aboveMapSprites != null)
			{
				foreach (TemporaryAnimatedSprite aboveMapSprite in aboveMapSprites)
				{
					aboveMapSprite.draw(b);
				}
			}
			if (!Game1.game1.takingMapScreenshot && playerControlSequenceID != null)
			{
				switch (playerControlSequenceID)
				{
				case "eggHunt":
					b.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(32, 32, 224, 160), Color.Black * 0.5f);
					Game1.drawWithBorder(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1514", festivalTimer / 1000), Color.Black, Color.Yellow, new Vector2(64f, 64f), 0f, 1f, 1f, false);
					Game1.drawWithBorder(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1515", Game1.player.festivalScore), Color.Black, Color.Pink, new Vector2(64f, 128f), 0f, 1f, 1f, false);
					if (Game1.IsMultiplayer)
					{
						Game1.player.team.festivalScoreStatus.Draw(b, new Vector2(32f, Game1.viewport.Height - 32), 4f, 0.99f, PlayerStatusList.HorizontalAlignment.Left, PlayerStatusList.VerticalAlignment.Bottom);
					}
					break;
				case "fair":
					b.End();
					Game1.PushUIMode();
					b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
					b.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(16, 16, 128 + ((Game1.player.festivalScore > 999) ? 16 : 0), 64), Color.Black * 0.75f);
					b.Draw(Game1.mouseCursors, new Vector2(32f, 32f), new Microsoft.Xna.Framework.Rectangle(338, 400, 8, 8), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
					Game1.drawWithBorder(Game1.player.festivalScore.ToString() ?? "", Color.Black, Color.White, new Vector2(72f, 21 + ((LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.en) ? 8 : (LocalizedContentManager.CurrentLanguageLatin ? 16 : 8))), 0f, 1f, 1f, false);
					if (Game1.activeClickableMenu == null)
					{
						Game1.dayTimeMoneyBox.drawMoneyBox(b, Game1.dayTimeMoneyBox.xPositionOnScreen, 4);
					}
					b.End();
					Game1.PopUIMode();
					b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
					if (Game1.IsMultiplayer)
					{
						Game1.player.team.festivalScoreStatus.Draw(b, new Vector2(32f, Game1.viewport.Height - 32), 4f, 0.99f, PlayerStatusList.HorizontalAlignment.Left, PlayerStatusList.VerticalAlignment.Bottom);
					}
					break;
				case "iceFishing":
					b.End();
					b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
					b.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(16, 16, 128 + ((Game1.player.festivalScore > 999) ? 16 : 0), 128), Color.Black * 0.75f);
					b.Draw(festivalTexture, new Vector2(32f, 16f), new Microsoft.Xna.Framework.Rectangle(112, 432, 16, 16), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
					Game1.drawWithBorder(Game1.player.festivalScore.ToString() ?? "", Color.Black, Color.White, new Vector2(96f, 21 + ((LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.en) ? 8 : (LocalizedContentManager.CurrentLanguageLatin ? 16 : 8))), 0f, 1f, 1f, false);
					Game1.drawWithBorder(Utility.getMinutesSecondsStringFromMilliseconds(festivalTimer), Color.Black, Color.White, new Vector2(32f, 93f), 0f, 1f, 1f, false);
					b.End();
					b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
					if (Game1.IsMultiplayer)
					{
						Game1.player.team.festivalScoreStatus.Draw(b, new Vector2(32f, Game1.viewport.Height - 32), 4f, 0.99f, PlayerStatusList.HorizontalAlignment.Left, PlayerStatusList.VerticalAlignment.Bottom);
					}
					break;
				}
			}
			string text = spriteTextToDraw;
			if (text != null && text.Length > 0)
			{
				Color color = SpriteText.getColorFromIndex(int_useMeForAnything2);
				SpriteText.drawStringHorizontallyCenteredAt(b, spriteTextToDraw, Game1.graphics.GraphicsDevice.Viewport.Width / 2, Game1.graphics.GraphicsDevice.Viewport.Height - 192, int_useMeForAnything, -1, 999999, 1f, 1f, false, color);
			}
			foreach (NPC actor in actors)
			{
				actor.drawAboveAlwaysFrontLayer(b);
			}
			if (skippable && !Game1.options.SnappyMenus && !Game1.game1.takingMapScreenshot)
			{
				Microsoft.Xna.Framework.Rectangle skipBounds = this.skipBounds();
				Color renderCol = Color.White;
				if (skipBounds.Contains(Game1.getOldMouseX(), Game1.getOldMouseY()))
				{
					renderCol *= 0.5f;
				}
				b.Draw(sourceRectangle: new Microsoft.Xna.Framework.Rectangle(205, 406, 22, 15), texture: Game1.mouseCursors, position: Utility.PointToVector2(skipBounds.Location), color: renderCol, rotation: 0f, origin: Vector2.Zero, scale: 4f, effects: SpriteEffects.None, layerDepth: 0.92f);
			}
			currentCustomEventScript?.drawAboveAlwaysFront(b);
		}

		public void EndPlayerControlSequence()
		{
			playerControlSequence = false;
			playerControlSequenceID = null;
		}

		public void OnPlayerControlSequenceEnd(string id)
		{
			Game1.player.StopSitting();
			Game1.player.CanMove = false;
			Game1.player.Halt();
		}

		public void setUpPlayerControlSequence(string id)
		{
			playerControlSequenceID = id;
			playerControlSequence = true;
			Game1.player.CanMove = true;
			Game1.viewportFreeze = false;
			Game1.forceSnapOnNextViewportUpdate = true;
			Game1.globalFade = false;
			doingSecretSanta = false;
			switch (id)
			{
			case "haleyBeach":
			{
				Vector2 tile = new Vector2(53f, 8f);
				Object item = ItemRegistry.Create<Object>("(O)742");
				item.TileLocation = tile;
				item.Flipped = false;
				props.Add(item);
				playerControlTargetTile = new Point(53, 8);
				Game1.player.canOnlyWalk = false;
				break;
			}
			case "eggFestival":
				festivalHost = getActorByName("Lewis");
				hostMessageKey = "Strings\\StringsFromCSFiles:Event.cs.1521";
				break;
			case "flowerFestival":
				festivalHost = getActorByName("Lewis");
				hostMessageKey = "Strings\\StringsFromCSFiles:Event.cs.1524";
				if (NetWorldState.checkAnywhereForWorldStateID("trashBearDone"))
				{
					Game1.currentLocation.setMapTileIndex(62, 28, -1, "Buildings");
					Game1.currentLocation.setMapTileIndex(64, 28, -1, "Buildings");
					Game1.currentLocation.setMapTileIndex(73, 48, -1, "Buildings");
				}
				break;
			case "luau":
				festivalHost = getActorByName("Lewis");
				hostMessageKey = "Strings\\StringsFromCSFiles:Event.cs.1527";
				break;
			case "jellies":
				festivalHost = getActorByName("Lewis");
				hostMessageKey = "Strings\\StringsFromCSFiles:Event.cs.1531";
				break;
			case "boatRide":
				Game1.viewportFreeze = true;
				Game1.currentViewportTarget = Utility.PointToVector2(Game1.viewportCenter);
				currentCommand++;
				break;
			case "parrotRide":
				Game1.player.canOnlyWalk = false;
				currentCommand++;
				break;
			case "fair":
				festivalHost = getActorByName("Lewis");
				hostMessageKey = "Strings\\StringsFromCSFiles:Event.cs.1535";
				break;
			case "eggHunt":
			{
				Layer pathsLayer = Game1.currentLocation.map.RequireLayer("Paths");
				for (int x = 0; x < pathsLayer.LayerWidth; x++)
				{
					for (int y = 0; y < pathsLayer.LayerHeight; y++)
					{
						if (pathsLayer.Tiles[x, y] != null && pathsLayer.Tiles[x, y].TileSheet.Id.StartsWith("fest"))
						{
							festivalProps.Add(new Prop(festivalTexture, pathsLayer.GetTileIndexAt(x, y), 1, 1, 1, x, y));
						}
					}
				}
				festivalTimer = 52000;
				currentCommand++;
				break;
			}
			case "halloween":
				if (Game1.year % 2 == 0)
				{
					temporaryLocation.objects.Add(new Vector2(63f, 16f), new Chest(new List<Item> { ItemRegistry.Create("(O)PrizeTicket") }, new Vector2(63f, 16f)));
				}
				else
				{
					temporaryLocation.objects.Add(new Vector2(33f, 13f), new Chest(new List<Item> { ItemRegistry.Create("(O)373") }, new Vector2(33f, 13f)));
				}
				break;
			case "christmas":
				secretSantaRecipient = Utility.GetRandomWinterStarParticipant();
				mySecretSanta = Utility.GetRandomWinterStarParticipant((string name) => name == secretSantaRecipient.Name || NPC.IsDivorcedFrom(farmer, name)) ?? secretSantaRecipient;
				Game1.debugOutput = "Secret Santa Recipient: " + secretSantaRecipient.Name + "  My Secret Santa: " + mySecretSanta.Name;
				break;
			case "iceFestival":
			{
				festivalHost = getActorByName("Lewis");
				hostMessageKey = "Strings\\StringsFromCSFiles:Event.cs.1548";
				if (Game1.year % 2 == 0)
				{
					temporaryLocation.setFireplace(true, 46, 16, false, -28, 28);
					temporaryLocation.setFireplace(true, 61, 43, false, -28, 28);
				}
				else
				{
					temporaryLocation.setFireplace(true, 11, 44, false, -28, 28);
					temporaryLocation.setFireplace(true, 65, 45, false, -28, 28);
				}
				if (!Game1.MasterPlayer.mailReceived.Contains("raccoonTreeFallen"))
				{
					break;
				}
				for (int x2 = 52; x2 < 60; x2++)
				{
					for (int y2 = 0; y2 < 2; y2++)
					{
						temporaryLocation.removeTile(x2, y2, "AlwaysFront");
					}
				}
				if (!NetWorldState.checkAnywhereForWorldStateID("forestStumpFixed"))
				{
					temporaryLocation.ApplyMapOverride("Forest_RaccoonStump", (Microsoft.Xna.Framework.Rectangle?)null, (Microsoft.Xna.Framework.Rectangle?)new Microsoft.Xna.Framework.Rectangle(53, 2, 7, 6));
				}
				else
				{
					temporaryLocation.ApplyMapOverride("Forest_RaccoonHouse", (Microsoft.Xna.Framework.Rectangle?)null, (Microsoft.Xna.Framework.Rectangle?)new Microsoft.Xna.Framework.Rectangle(53, 2, 7, 6));
				}
				break;
			}
			case "iceFishing":
			{
				Tool rod = ItemRegistry.Create<Tool>("(T)BambooPole");
				rod.AttachmentSlotsCount = 2;
				rod.attachments[1] = ItemRegistry.Create<Object>("(O)687");
				festivalTimer = 120000;
				farmer.festivalScore = 0;
				farmer.CurrentToolIndex = 0;
				farmer.TemporaryItem = rod;
				farmer.CurrentToolIndex = 0;
				break;
			}
			}
		}

		public bool canMoveAfterDialogue()
		{
			if (playerControlSequenceID != null && playerControlSequenceID.Equals("eggHunt"))
			{
				Game1.player.canMove = true;
				CurrentCommand++;
			}
			return playerControlSequence;
		}

		public void forceFestivalContinue()
		{
			if (isSpecificFestival("fall16"))
			{
				initiateGrangeJudging();
				return;
			}
			Game1.dialogueUp = false;
			if (Game1.activeClickableMenu != null)
			{
				Game1.activeClickableMenu.emergencyShutDown();
			}
			Game1.exitActiveMenu();
			string rawCommands;
			if (!TryGetFestivalDataForYear("mainEvent", out rawCommands))
			{
				Game1.log.Error("Festival " + id + " doesn't have the required 'mainEvent' data field.");
			}
			string[] newCommands = (eventCommands = ParseCommands(rawCommands));
			CurrentCommand = 0;
			eventSwitched = true;
			playerControlSequence = false;
			setUpFestivalMainEvent();
			Game1.player.Halt();
		}

		/// <summary>Split an event's key into its ID and preconditions.</summary>
		/// <param name="rawScript">The event key to split.</param>
		public static string[] SplitPreconditions(string rawScript)
		{
			return ArgUtility.SplitQuoteAware(rawScript, '/', StringSplitOptions.RemoveEmptyEntries, true);
		}

		/// <summary>Split and preprocess a raw event script into its component commands.</summary>
		/// <param name="rawScript">The raw event script to split.</param>
		/// <param name="player">The player for which the event is being parsed.</param>
		public static string[] ParseCommands(string rawScript, Farmer player = null)
		{
			rawScript = Dialogue.applyGenderSwitchBlocks(((player != null) ? new Gender?(player.Gender) : Game1.player?.Gender).GetValueOrDefault(), rawScript);
			rawScript = TokenParser.ParseText(rawScript);
			return ArgUtility.SplitQuoteAware(rawScript, '/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, true);
		}

		public bool isSpecificFestival(string festivalId)
		{
			if (isFestival)
			{
				return id == "festival_" + festivalId;
			}
			return false;
		}

		public void setUpFestivalMainEvent()
		{
			if (!isSpecificFestival("spring24"))
			{
				return;
			}
			List<NetDancePartner> females = new List<NetDancePartner>();
			List<NetDancePartner> males = new List<NetDancePartner>();
			List<string> leftoverFemales = new List<string> { "Abigail", "Penny", "Leah", "Maru", "Haley", "Emily" };
			List<string> leftoverMales = new List<string> { "Sebastian", "Sam", "Elliott", "Harvey", "Alex", "Shane" };
			List<Farmer> farmers = (from f in Game1.getOnlineFarmers()
				orderby f.UniqueMultiplayerID
				select f).ToList();
			while (farmers.Count > 0)
			{
				Farmer f2 = farmers[0];
				farmers.RemoveAt(0);
				if (Game1.multiplayer.isDisconnecting(f2) || f2.dancePartner.Value == null)
				{
					continue;
				}
				if (f2.dancePartner.GetGender() == Gender.Female)
				{
					females.Add(f2.dancePartner);
					if (f2.dancePartner.IsVillager())
					{
						leftoverFemales.Remove(f2.dancePartner.TryGetVillager().Name);
					}
					males.Add(new NetDancePartner(f2));
				}
				else
				{
					males.Add(f2.dancePartner);
					if (f2.dancePartner.IsVillager())
					{
						leftoverMales.Remove(f2.dancePartner.TryGetVillager().Name);
					}
					females.Add(new NetDancePartner(f2));
				}
				if (f2.dancePartner.IsFarmer())
				{
					farmers.Remove(f2.dancePartner.TryGetFarmer());
				}
			}
			while (females.Count < 6)
			{
				string female = leftoverFemales.Last();
				if (leftoverMales.Contains(Utility.getLoveInterest(female)))
				{
					females.Add(new NetDancePartner(female));
					males.Add(new NetDancePartner(Utility.getLoveInterest(female)));
				}
				leftoverFemales.Remove(female);
			}
			string rawFestivalData;
			if (!TryGetFestivalDataForYear("mainEvent", out rawFestivalData))
			{
				rawFestivalData = string.Empty;
			}
			for (int l = 1; l <= 6; l++)
			{
				string female2 = ((!females[l - 1].IsVillager()) ? ("farmer" + Utility.getFarmerNumberFromFarmer(females[l - 1].TryGetFarmer())) : females[l - 1].TryGetVillager().Name);
				string male = ((!males[l - 1].IsVillager()) ? ("farmer" + Utility.getFarmerNumberFromFarmer(males[l - 1].TryGetFarmer())) : males[l - 1].TryGetVillager().Name);
				rawFestivalData = rawFestivalData.Replace("Girl" + l, female2);
				rawFestivalData = rawFestivalData.Replace("Guy" + l, male);
			}
			List<KeyValuePair<NetDancePartner, NetDancePartner>> pairsByInnermost = new List<KeyValuePair<NetDancePartner, NetDancePartner>>();
			List<KeyValuePair<NetDancePartner, NetDancePartner>> playerPairs = new List<KeyValuePair<NetDancePartner, NetDancePartner>>();
			for (int k = females.Count - 1; k >= 0; k--)
			{
				NetDancePartner female3 = females[k];
				NetDancePartner male2 = males[k];
				if (female3.IsFarmer() || male2.IsFarmer())
				{
					playerPairs.Add(new KeyValuePair<NetDancePartner, NetDancePartner>(female3, male2));
					females.RemoveAt(k);
					males.RemoveAt(k);
				}
			}
			pairsByInnermost.AddRange(playerPairs.OrderBy(delegate(KeyValuePair<NetDancePartner, NetDancePartner> pair)
			{
				int farmerNumberFromFarmer = Utility.getFarmerNumberFromFarmer(pair.Key.TryGetFarmer());
				int farmerNumberFromFarmer2 = Utility.getFarmerNumberFromFarmer(pair.Value.TryGetFarmer());
				if (farmerNumberFromFarmer > -1 && farmerNumberFromFarmer2 > -1)
				{
					return Math.Min(farmerNumberFromFarmer, farmerNumberFromFarmer2);
				}
				return (farmerNumberFromFarmer <= -1) ? farmerNumberFromFarmer2 : farmerNumberFromFarmer;
			}));
			for (int j = 0; j < females.Count; j++)
			{
				pairsByInnermost.Add(new KeyValuePair<NetDancePartner, NetDancePartner>(females[j], males[j]));
			}
			females.Clear();
			males.Clear();
			bool addLeft = true;
			foreach (KeyValuePair<NetDancePartner, NetDancePartner> pair2 in pairsByInnermost)
			{
				if (addLeft)
				{
					females.Insert(0, pair2.Key);
					males.Insert(0, pair2.Value);
				}
				else
				{
					females.Add(pair2.Key);
					males.Add(pair2.Value);
				}
				addLeft = !addLeft;
			}
			List<string> commandsToAdd = new List<string>(ParseCommands(rawFestivalData));
			for (int i = 0; i < commandsToAdd.Count; i++)
			{
				string command = commandsToAdd[i];
				List<NetDancePartner> dancers = null;
				string token = null;
				if (command.Contains("Girls"))
				{
					token = "Girls";
					dancers = females;
				}
				else if (command.Contains("Guys"))
				{
					token = "Guys";
					dancers = males;
				}
				if (dancers == null)
				{
					continue;
				}
				float spacing = 10f / (float)(dancers.Count - 1);
				if (spacing < 1f)
				{
					spacing = 1f;
				}
				for (int m = 0; m < dancers.Count; m++)
				{
					string name = (dancers[m].IsVillager() ? dancers[m].TryGetVillager().Name : ("farmer" + Utility.getFarmerNumberFromFarmer(dancers[m].TryGetFarmer())));
					string newCommand = command.Replace(token, name);
					if (newCommand.StartsWith("warp "))
					{
						string[] warp = ArgUtility.SplitBySpace(newCommand);
						int x = int.Parse(warp[2]);
						warp[2] = (x + (int)Math.Round((float)m * spacing)).ToString();
						newCommand = string.Join(" ", warp);
					}
					commandsToAdd.Insert(i + m, newCommand);
				}
				i += dancers.Count;
				commandsToAdd.RemoveAt(i);
				i--;
			}
			rawFestivalData = string.Join("/", commandsToAdd);
			Regex regex = new Regex("showFrame (?<farmerName>farmer\\d) 44");
			Regex showFrameGirl = new Regex("showFrame (?<farmerName>farmer\\d) 40");
			Regex animation1Guy = new Regex("animate (?<farmerName>farmer\\d) false true 600 44 45");
			Regex animation1Girl = new Regex("animate (?<farmerName>farmer\\d) false true 600 43 41 43 42");
			Regex animation2Guy = new Regex("animate (?<farmerName>farmer\\d) false true 300 46 47");
			Regex animation2Girl = new Regex("animate (?<farmerName>farmer\\d) false true 600 46 47");
			rawFestivalData = regex.Replace(rawFestivalData, "showFrame $1 12/faceDirection $1 0");
			rawFestivalData = showFrameGirl.Replace(rawFestivalData, "showFrame $1 0/faceDirection $1 2");
			rawFestivalData = animation1Guy.Replace(rawFestivalData, "animate $1 false true 600 12 13 12 14");
			rawFestivalData = animation1Girl.Replace(rawFestivalData, "animate $1 false true 596 4 0");
			rawFestivalData = animation2Guy.Replace(rawFestivalData, "animate $1 false true 150 12 13 12 14");
			rawFestivalData = animation2Girl.Replace(rawFestivalData, "animate $1 false true 600 0 3");
			eventCommands = ParseCommands(rawFestivalData);
		}

		private void judgeGrange()
		{
			int pointsEarned = 14;
			Dictionary<int, bool> categoriesRepresented = new Dictionary<int, bool>();
			int nullsCount = 0;
			bool purpleShorts = false;
			foreach (Item i in Game1.player.team.grangeDisplay)
			{
				Object obj = i as Object;
				if (obj != null)
				{
					if (IsItemMayorShorts(obj))
					{
						purpleShorts = true;
					}
					pointsEarned += obj.Quality + 1;
					int num = obj.sellToStorePrice(-1L);
					if (num >= 20)
					{
						pointsEarned++;
					}
					if (num >= 90)
					{
						pointsEarned++;
					}
					if (num >= 200)
					{
						pointsEarned++;
					}
					if (num >= 300 && obj.Quality < 2)
					{
						pointsEarned++;
					}
					if (num >= 400 && obj.Quality < 1)
					{
						pointsEarned++;
					}
					switch (obj.Category)
					{
					case -75:
						categoriesRepresented[-75] = true;
						break;
					case -79:
						categoriesRepresented[-79] = true;
						break;
					case -18:
					case -14:
					case -6:
					case -5:
						categoriesRepresented[-5] = true;
						break;
					case -12:
					case -2:
						categoriesRepresented[-12] = true;
						break;
					case -4:
						categoriesRepresented[-4] = true;
						break;
					case -81:
					case -80:
					case -27:
						categoriesRepresented[-81] = true;
						break;
					case -7:
						categoriesRepresented[-7] = true;
						break;
					case -26:
						categoriesRepresented[-26] = true;
						break;
					}
				}
				else if (i == null)
				{
					nullsCount++;
				}
			}
			pointsEarned += Math.Min(30, categoriesRepresented.Count * 5);
			int displayFilledPoints = 9 - 2 * nullsCount;
			pointsEarned = (grangeScore = pointsEarned + displayFilledPoints);
			if (purpleShorts)
			{
				grangeScore = -666;
			}
		}

		private void lewisDoneJudgingGrange()
		{
			if (Game1.activeClickableMenu == null)
			{
				Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1584")));
				Game1.player.Halt();
			}
			interpretGrangeResults();
		}

		public void interpretGrangeResults()
		{
			List<Character> winners = new List<Character>
			{
				getActorByName("Pierre"),
				getActorByName("Marnie"),
				getActorByName("Willy")
			};
			if (grangeScore >= 90)
			{
				winners.Insert(0, Game1.player);
			}
			else if (grangeScore >= 75)
			{
				winners.Insert(1, Game1.player);
			}
			else if (grangeScore >= 60)
			{
				winners.Insert(2, Game1.player);
			}
			else
			{
				winners.Add(Game1.player);
			}
			bool pierreWon = (winners[0] as NPC)?.Name == "Pierre";
			bool playerSkipped = Game1.player.team.grangeDisplay.Count == 0;
			bool usedPurpleShorts = grangeScore == -666;
			foreach (NPC actor in actors)
			{
				Dialogue dialogue = null;
				dialogue = ((!pierreWon) ? (actor.TryGetDialogue("Fair_Judged_PlayerWon") ?? actor.TryGetDialogue("Fair_Judged")) : ((usedPurpleShorts ? actor.TryGetDialogue("Fair_Judged_PlayerLost_PurpleShorts") : null) ?? (playerSkipped ? actor.TryGetDialogue("Fair_Judged_PlayerLost_Skipped") : null) ?? actor.TryGetDialogue("Fair_Judged_PlayerLost") ?? actor.TryGetDialogue("Fair_Judged")));
				if (dialogue != null)
				{
					actor.setNewDialogue(dialogue);
				}
			}
			grangeJudged = true;
			if (!(winners[0] is Farmer))
			{
				return;
			}
			foreach (Farmer allFarmer in Game1.getAllFarmers())
			{
				allFarmer.autoGenerateActiveDialogueEvent("wonGrange");
			}
		}

		private void initiateGrangeJudging()
		{
			judgeGrange();
			hostMessageKey = null;
			setUpAdvancedMove(ArgUtility.SplitBySpace("advancedMove Lewis False 2 0 0 7 8 0 4 3000 3 0 4 3000 3 0 4 3000 3 0 4 3000 -14 0 2 1000"), lewisDoneJudgingGrange);
			getActorByName("Lewis").CurrentDialogue.Clear();
			if (getActorByName("Marnie") != null)
			{
				for (int i = npcControllers.Count - 1; i >= 0; i--)
				{
					if (npcControllers[i].puppet.Name.Equals("Marnie"))
					{
						npcControllers.RemoveAt(i);
					}
				}
			}
			setUpAdvancedMove(ArgUtility.SplitBySpace("advancedMove Marnie False 0 1 4 1000"));
			foreach (NPC actor in actors)
			{
				Dialogue dialogue = actor.TryGetDialogue("Fair_Judging");
				if (dialogue != null)
				{
					actor.setNewDialogue(dialogue);
				}
			}
		}

		public void answerDialogueQuestion(NPC who, string answerKey)
		{
			if (!isFestival)
			{
				return;
			}
			switch (answerKey)
			{
			case "yes":
			{
				if (isSpecificFestival("fall16"))
				{
					initiateGrangeJudging();
					if (Game1.IsServer)
					{
						Game1.multiplayer.sendServerToClientsMessage("festivalEvent");
					}
					break;
				}
				string rawCommands;
				if (!TryGetFestivalDataForYear("mainEvent", out rawCommands))
				{
					Game1.log.Error("Festival " + id + " doesn't have the required 'mainEvent' data field.");
				}
				string[] newCommands = (eventCommands = ParseCommands(rawCommands));
				CurrentCommand = 0;
				eventSwitched = true;
				playerControlSequence = false;
				setUpFestivalMainEvent();
				if (Game1.IsServer)
				{
					Game1.multiplayer.sendServerToClientsMessage("festivalEvent");
				}
				break;
			}
			case "danceAsk":
				if (Game1.player.spouse != null && who.Name == Game1.player.spouse)
				{
					Game1.player.dancePartner.Value = who;
					who.setNewDialogue(who.TryGetDialogue("FlowerDance_Accept_" + (Game1.player.isRoommate(who.Name) ? "Roommate" : "Spouse")) ?? who.TryGetDialogue("FlowerDance_Accept") ?? new Dialogue(who, "Strings\\StringsFromCSFiles:Event.cs.1632"));
					foreach (NPC j in actors)
					{
						Stack<Dialogue> currentDialogue = j.CurrentDialogue;
						if (currentDialogue != null && currentDialogue.Count > 0 && j.CurrentDialogue.Peek().getCurrentDialogue().Equals("..."))
						{
							j.CurrentDialogue.Clear();
						}
					}
				}
				else if (!who.HasPartnerForDance && Game1.player.getFriendshipLevelForNPC(who.Name) >= 1000 && !who.isMarried())
				{
					try
					{
						Game1.player.changeFriendship(250, Game1.getCharacterFromName(who.Name));
					}
					catch
					{
					}
					Game1.player.dancePartner.Value = who;
					who.setNewDialogue(who.TryGetDialogue("FlowerDance_Accept") ?? ((who.Gender == Gender.Female) ? new Dialogue(who, "Strings\\StringsFromCSFiles:Event.cs.1634") : new Dialogue(who, "Strings\\StringsFromCSFiles:Event.cs.1633")));
					foreach (NPC i in actors)
					{
						Stack<Dialogue> currentDialogue2 = i.CurrentDialogue;
						if (currentDialogue2 != null && currentDialogue2.Count > 0 && i.CurrentDialogue.Peek().getCurrentDialogue().Equals("..."))
						{
							i.CurrentDialogue.Clear();
						}
					}
				}
				else if (who.HasPartnerForDance)
				{
					who.setNewDialogue("Strings\\StringsFromCSFiles:Event.cs.1635");
				}
				else
				{
					Dialogue dialogue = who.TryGetDialogue("FlowerDance_Decline") ?? who.TryGetDialogue("danceRejection");
					if (dialogue == null)
					{
						break;
					}
					who.setNewDialogue(dialogue);
				}
				Game1.drawDialogue(who);
				who.immediateSpeak = true;
				who.facePlayer(Game1.player);
				who.Halt();
				break;
			case "no":
				break;
			}
		}

		public void addItemToGrangeDisplay(Item i, int position, bool force)
		{
			while (Game1.player.team.grangeDisplay.Count < 9)
			{
				Game1.player.team.grangeDisplay.Add(null);
			}
			if (position >= 0 && position < Game1.player.team.grangeDisplay.Count && (Game1.player.team.grangeDisplay[position] == null || force))
			{
				Game1.player.team.grangeDisplay[position] = i;
			}
		}

		private bool onGrangeChange(Item i, int position, Item old, StorageContainer container, bool onRemoval)
		{
			if (!onRemoval)
			{
				if (i.Stack > 1 || (i.Stack == 1 && old != null && old.Stack == 1 && i.canStackWith(old)))
				{
					if (old != null && i != null && old.canStackWith(i))
					{
						container.ItemsToGrabMenu.actualInventory[position].Stack = 1;
						container.heldItem = old;
						return false;
					}
					if (old != null)
					{
						Utility.addItemToInventory(old, position, container.ItemsToGrabMenu.actualInventory);
						container.heldItem = i;
						return false;
					}
					int allButOne = i.Stack - 1;
					Item reject = i.getOne();
					reject.Stack = allButOne;
					container.heldItem = reject;
					i.Stack = 1;
				}
			}
			else if (old != null && old.Stack > 1 && !old.Equals(i))
			{
				return false;
			}
			addItemToGrangeDisplay((onRemoval && (old == null || old.Equals(i))) ? null : i, position, true);
			return true;
		}

		public bool canPlayerUseTool()
		{
			if (isSpecificFestival("winter8") && festivalTimer > 0 && !Game1.player.UsingTool)
			{
				previousFacingDirection = Game1.player.FacingDirection;
				return true;
			}
			return false;
		}

		public bool checkAction(Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who)
		{
			if (isFestival)
			{
				Object tempObj;
				if (temporaryLocation != null && temporaryLocation.objects.TryGetValue(new Vector2(tileLocation.X, tileLocation.Y), out tempObj))
				{
					tempObj.checkForAction(who);
				}
				GameLocation location = Game1.currentLocation;
				int tileIndex = location.getTileIndexAt(tileLocation.X, tileLocation.Y, "Buildings");
				string tileAction = location.doesTileHaveProperty(tileLocation.X, tileLocation.Y, "Action", "Buildings");
				string tileSheetID = location.getTileSheetIDAt(tileLocation.X, tileLocation.Y, "Buildings");
				bool isMainFestivalTilesheet = tileSheetID == "untitled tile sheet";
				if (Game1.season == Season.Winter && Game1.dayOfMonth == 8 && tileSheetID == "fest" && (tileIndex == 1009 || tileIndex == 1010 || tileIndex == 1012 || tileIndex == 1013))
				{
					Game1.playSound("pig");
					return true;
				}
				bool success = true;
				switch (tileIndex)
				{
				case 958:
					if (isSpecificFestival("fall27") && (tileLocation.X == 61 || tileLocation.X == 44) && (tileLocation.Y == 13 || tileLocation.Y == 9) && who.IsLocalPlayer)
					{
						location.createQuestionDialogue(Game1.content.LoadString("Strings\\1_6_Strings:SpiritsEveCart"), location.createYesNoResponses(), "spirits_eve_shortcut");
					}
					break;
				case 175:
				case 176:
					if (isSpecificFestival("fall16") && isMainFestivalTilesheet && who.IsLocalPlayer)
					{
						Game1.player.eatObject(ItemRegistry.Create<Object>("(O)241"), true);
					}
					break;
				case 308:
				case 309:
					if (isSpecificFestival("fall16") && isMainFestivalTilesheet)
					{
						Response[] colors = new Response[3]
						{
							new Response("Orange", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1645")),
							new Response("Green", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1647")),
							new Response("I", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1650"))
						};
						if (who.IsLocalPlayer && isSpecificFestival("fall16"))
						{
							location.createQuestionDialogue(Game1.parseText(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1652")), colors, "wheelBet");
						}
					}
					break;
				case 87:
				case 88:
					if (isSpecificFestival("fall16") && isMainFestivalTilesheet)
					{
						Response[] responses = new Response[2]
						{
							new Response("Buy", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1654")),
							new Response("Leave", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1656"))
						};
						if (who.IsLocalPlayer && isSpecificFestival("fall16"))
						{
							location.createQuestionDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1659"), responses, "StarTokenShop");
						}
					}
					break;
				case 501:
				case 502:
					if (isSpecificFestival("fall16") && isMainFestivalTilesheet)
					{
						Response[] responses4 = new Response[2]
						{
							new Response("Play", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1662")),
							new Response("Leave", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1663"))
						};
						if (who.IsLocalPlayer && isSpecificFestival("fall16"))
						{
							location.createQuestionDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1666"), responses4, "slingshotGame");
						}
					}
					break;
				case 510:
				case 511:
					if (isSpecificFestival("fall16") && isMainFestivalTilesheet && who.IsLocalPlayer && isSpecificFestival("fall16"))
					{
						location.createQuestionDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1672"), location.createYesNoResponses(), "starTokenShop");
					}
					break;
				case 349:
				case 350:
				case 351:
					if (!(isSpecificFestival("fall16") && isMainFestivalTilesheet))
					{
						break;
					}
					Game1.player.team.grangeMutex.RequestLock(delegate
					{
						while (Game1.player.team.grangeDisplay.Count < 9)
						{
							Game1.player.team.grangeDisplay.Add(null);
						}
						Game1.activeClickableMenu = new StorageContainer(Game1.player.team.grangeDisplay.ToList(), 9, 3, onGrangeChange, Utility.highlightSmallObjects);
					});
					break;
				case 503:
				case 504:
					if (isSpecificFestival("fall16") && isMainFestivalTilesheet)
					{
						Response[] responses3 = new Response[2]
						{
							new Response("Play", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1662")),
							new Response("Leave", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1663"))
						};
						if (who.IsLocalPlayer && isSpecificFestival("fall16"))
						{
							location.createQuestionDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1681"), responses3, "fishingGame");
						}
					}
					break;
				case 540:
					if (isSpecificFestival("fall16") && isMainFestivalTilesheet && who.IsLocalPlayer)
					{
						if (who.TilePoint.X == 29)
						{
							Game1.activeClickableMenu = new StrengthGame();
						}
						else
						{
							Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1684")));
						}
					}
					break;
				case 505:
				case 506:
					if (isSpecificFestival("fall16") && isMainFestivalTilesheet && who.IsLocalPlayer)
					{
						if (who.Money >= 100 && !who.mailReceived.Contains("fortuneTeller" + Game1.year))
						{
							Response[] responses2 = new Response[2]
							{
								new Response("Read", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1688")),
								new Response("No", Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1690"))
							};
							location.createQuestionDialogue(Game1.parseText(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1691")), responses2, "fortuneTeller");
						}
						else if (who.mailReceived.Contains("fortuneTeller" + Game1.year))
						{
							Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1694")));
						}
						else
						{
							Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1695")));
						}
						who.Halt();
					}
					break;
				default:
					success = false;
					break;
				}
				if (success)
				{
					return true;
				}
				if (tileAction != null)
				{
					try
					{
						string[] args = ArgUtility.SplitBySpace(tileAction);
						switch (ArgUtility.Get(args, 0))
						{
						case "OpenShop":
						case "Shop":
						{
							string shop_id;
							string error;
							if (!ArgUtility.TryGet(args, 1, out shop_id, out error))
							{
								location.LogTileActionError(args, tileLocation.X, tileLocation.Y, error);
								return false;
							}
							if (!who.IsLocalPlayer)
							{
								return false;
							}
							bool opened = false;
							if (shop_id == "shop" && isFestival)
							{
								switch (id)
								{
								case "festival_fall27":
									shop_id = "Festival_SpiritsEve_Pierre";
									break;
								case "festival_spring13":
									shop_id = "Festival_EggFestival_Pierre";
									break;
								case "festival_spring24":
									shop_id = "Festival_FlowerDance_Pierre";
									break;
								case "festival_summer11":
									shop_id = "Festival_Luau_Pierre";
									break;
								case "festival_summer28":
									shop_id = "Festival_DanceOfTheMoonlightJellies_Pierre";
									break;
								case "festival_winter8":
									shop_id = "Festival_FestivalOfIce_TravelingMerchant";
									break;
								case "festival_winter25":
									shop_id = "Festival_FeastOfTheWinterStar_Pierre";
									break;
								}
							}
							string legacyShopData;
							if (festivalData.TryGetValue(shop_id, out legacyShopData))
							{
								if (festivalShops == null)
								{
									festivalShops = new Dictionary<string, Dictionary<ISalable, ItemStockInformation>>();
								}
								Dictionary<ISalable, ItemStockInformation> stockList;
								if (!festivalShops.TryGetValue(shop_id, out stockList))
								{
									string[] inventoryList = ArgUtility.SplitBySpace(legacyShopData);
									stockList = new Dictionary<ISalable, ItemStockInformation>();
									for (int j = 0; j < inventoryList.Length; j += 4)
									{
										string type;
										string itemId;
										int price;
										int stock;
										if (!ArgUtility.TryGet(args, j, out type, out error) || !ArgUtility.TryGet(args, j + 1, out itemId, out error) || !ArgUtility.TryGetInt(args, j + 2, out price, out error) || !ArgUtility.TryGetInt(args, j + 3, out stock, out error))
										{
											IGameLogger log = Game1.log;
											DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(68, 3);
											defaultInterpolatedStringHandler.AppendLiteral("Festival '");
											defaultInterpolatedStringHandler.AppendFormatted(id);
											defaultInterpolatedStringHandler.AppendLiteral("' has legacy shop inventory '");
											defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", inventoryList));
											defaultInterpolatedStringHandler.AppendLiteral("' which couldn't be parsed: ");
											defaultInterpolatedStringHandler.AppendFormatted(error);
											defaultInterpolatedStringHandler.AppendLiteral(".");
											log.Error(defaultInterpolatedStringHandler.ToStringAndClear());
											break;
										}
										Item item = Utility.getItemFromStandardTextDescription(type, itemId, stock, who);
										if (item != null)
										{
											if (item.Category == -74)
											{
												price = (int)Math.Max(1f, (float)price * Game1.MasterPlayer.difficultyModifier);
											}
											if (!item.IsRecipe || !who.knowsRecipe(item.Name))
											{
												stockList.Add(item, new ItemStockInformation(price, (stock <= 0) ? int.MaxValue : stock, null, null, LimitedStockMode.Player));
											}
										}
									}
									festivalShops[shop_id] = stockList;
								}
								if (stockList != null && stockList.Count > 0)
								{
									who.team.synchronizedShopStock.UpdateLocalStockWithSyncedQuanitities(who.currentLocation.Name + shop_id, stockList);
									Game1.activeClickableMenu = new ShopMenu(id + "_" + shop_id, stockList);
									opened = true;
								}
							}
							bool showedClosedMessage = false;
							if (!opened && Utility.TryOpenShopMenu(shop_id, temporaryLocation, null, null, false, true, delegate(string message)
							{
								showedClosedMessage = true;
								Game1.drawObjectDialogue(message);
							}))
							{
								opened = true;
							}
							if (!opened && !showedClosedMessage)
							{
								Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1714"));
							}
							break;
						}
						case "Message":
						{
							string translationKey;
							string error2;
							if (!ArgUtility.TryGet(args, 1, out translationKey, out error2))
							{
								location.LogTileActionError(args, tileLocation.X, tileLocation.Y, error2);
								return false;
							}
							Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromMaps:" + translationKey.Replace("\"", "")));
							break;
						}
						case "Dialogue":
						{
							string dialogue3;
							string error3;
							if (!ArgUtility.TryGetRemainder(args, 1, out dialogue3, out error3))
							{
								location.LogTileActionError(args, tileLocation.X, tileLocation.Y, error3);
								return false;
							}
							Game1.drawObjectDialogue(dialogue3.Replace("#", " "));
							break;
						}
						case "LuauSoup":
							if (!specialEventVariable2)
							{
								Game1.activeClickableMenu = new ItemGrabMenu(null, true, false, Utility.highlightLuauSoupItems, clickToAddItemToLuauSoup, Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1719"), null, false, true, true, true, false, 0, null, -1, this);
							}
							break;
						}
					}
					catch (Exception)
					{
					}
				}
				else if (isFestival)
				{
					if (who.IsLocalPlayer && (!playerControlSequence || !playerControlSequenceID.Equals("iceFishing")))
					{
						foreach (NPC k in actors)
						{
							Point tile2 = k.TilePoint;
							Microsoft.Xna.Framework.Rectangle tileRect = new Microsoft.Xna.Framework.Rectangle(tileLocation.X * 64, tileLocation.Y * 64, 64, 64);
							if (tile2.X == tileLocation.X && tile2.Y == tileLocation.Y)
							{
								Child child = k as Child;
								if (child != null)
								{
									child.checkAction(who, temporaryLocation);
									return true;
								}
							}
							if (((tile2.X != tileLocation.X || (tile2.Y != tileLocation.Y && tile2.Y != tileLocation.Y + 1)) && !k.GetBoundingBox().Intersects(tileRect)) || (k.CurrentDialogue.Count < 1 && (k.CurrentDialogue.Count <= 0 || k.CurrentDialogue.Peek().isOnFinalDialogue()) && !k.Equals(festivalHost) && (!k.datable || !isSpecificFestival("spring24")) && (secretSantaRecipient == null || !k.Name.Equals(secretSantaRecipient.Name))))
							{
								continue;
							}
							Friendship friendship;
							bool divorced = who.friendshipData.TryGetValue(k.Name, out friendship) && friendship.IsDivorced();
							if ((grangeScore > -100 || grangeScore == -666) && k.Equals(festivalHost) && grangeJudged)
							{
								Dialogue message2;
								if (grangeScore >= 90)
								{
									Game1.playSound("reward");
									message2 = Dialogue.FromTranslation(k, "Strings\\StringsFromCSFiles:Event.cs.1723", grangeScore);
									Game1.player.festivalScore += 1000;
									Game1.getAchievement(37);
								}
								else if (grangeScore >= 75)
								{
									Game1.playSound("reward");
									message2 = Dialogue.FromTranslation(k, "Strings\\StringsFromCSFiles:Event.cs.1726", grangeScore);
									Game1.player.festivalScore += 500;
								}
								else if (grangeScore >= 60)
								{
									Game1.playSound("newArtifact");
									message2 = Dialogue.FromTranslation(k, "Strings\\StringsFromCSFiles:Event.cs.1729", grangeScore);
									Game1.player.festivalScore += 250;
								}
								else if (grangeScore == -666)
								{
									Game1.playSound("secret1");
									message2 = new Dialogue(k, "Strings\\StringsFromCSFiles:Event.cs.1730");
									Game1.player.festivalScore += 750;
								}
								else
								{
									Game1.playSound("newArtifact");
									message2 = Dialogue.FromTranslation(k, "Strings\\StringsFromCSFiles:Event.cs.1732", grangeScore);
									Game1.player.festivalScore += 50;
								}
								grangeScore = -100;
								k.setNewDialogue(message2);
							}
							else if ((Game1.serverHost == null || Game1.player.Equals(Game1.serverHost.Value)) && k.Equals(festivalHost) && (k.CurrentDialogue.Count == 0 || k.CurrentDialogue.Peek().isOnFinalDialogue()) && hostMessageKey != null)
							{
								k.setNewDialogue(hostMessageKey);
							}
							else if ((Game1.serverHost == null || Game1.player.Equals(Game1.serverHost.Value)) && k.Equals(festivalHost) && (k.CurrentDialogue.Count == 0 || k.CurrentDialogue.Peek().isOnFinalDialogue()) && hostMessageKey != null)
							{
								k.setNewDialogue(hostMessageKey);
							}
							if (isSpecificFestival("spring24") && !divorced)
							{
								bool? flag = k.GetData()?.FlowerDanceCanDance;
								bool num;
								if (!flag.HasValue)
								{
									if ((bool)k.datable)
									{
										goto IL_10c4;
									}
									num = k.Name == who.spouse;
								}
								else
								{
									num = flag.GetValueOrDefault();
								}
								if (num)
								{
									goto IL_10c4;
								}
							}
							goto IL_12de;
							IL_12de:
							if (!divorced && secretSantaRecipient != null && k.Name.Equals(secretSantaRecipient.Name))
							{
								k.grantConversationFriendship(who);
								location.createQuestionDialogue(Game1.parseText((secretSantaRecipient.Gender == Gender.Male) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1740", secretSantaRecipient.displayName) : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1741", secretSantaRecipient.displayName)), location.createYesNoResponses(), "secretSanta");
								who.Halt();
								return true;
							}
							if (k.CurrentDialogue.Count == 0)
							{
								return true;
							}
							if (who.spouse != null && k.Name == who.spouse && !isSpecificFestival("spring24"))
							{
								Dialogue dialogue = null;
								if (k.isRoommate())
								{
									TryGetFestivalDialogueForYear(k, k.Name + "_roommate", out dialogue);
								}
								if (dialogue == null)
								{
									TryGetFestivalDialogueForYear(k, k.Name + "_spouse", out dialogue);
								}
								if (dialogue != null && (k.CurrentDialogue.Count == 0 || !k.CurrentDialogue.Peek().TranslationKey.Equals(dialogue.TranslationKey)))
								{
									k.CurrentDialogue.Clear();
									k.CurrentDialogue.Push(dialogue);
								}
							}
							if (divorced)
							{
								k.CurrentDialogue.Clear();
								k.CurrentDialogue.Push(new Dialogue(k, "Characters\\Dialogue\\" + k.Name + ":divorced"));
							}
							k.grantConversationFriendship(who);
							if (k.CurrentDialogue == null || k.CurrentDialogue.Count == 0 || !k.CurrentDialogue.Peek().dontFaceFarmer)
							{
								k.faceTowardFarmerForPeriod(3000, 2, false, who);
							}
							Game1.drawDialogue(k);
							who.Halt();
							return true;
							IL_10c4:
							k.grantConversationFriendship(who);
							if (who.dancePartner.Value == null)
							{
								if (k.CurrentDialogue.Count > 0 && k.CurrentDialogue.Peek().getCurrentDialogue().Equals("..."))
								{
									k.CurrentDialogue.Clear();
								}
								if (k.CurrentDialogue.Count == 0)
								{
									k.CurrentDialogue.Push(new Dialogue(k, null, "..."));
									if (k.name == who.spouse)
									{
										k.setNewDialogue(Dialogue.FromTranslation(k, "Strings\\StringsFromCSFiles:Event.cs.1736", k.displayName), true);
									}
									else
									{
										k.setNewDialogue(Dialogue.FromTranslation(k, "Strings\\StringsFromCSFiles:Event.cs.1738", k.displayName), true);
									}
								}
								else if (k.CurrentDialogue.Peek().isOnFinalDialogue())
								{
									Dialogue d = k.CurrentDialogue.Peek();
									if (who.spouse != null && k.Name == who.spouse)
									{
										Dialogue dialogue2 = null;
										if (k.isRoommate())
										{
											TryGetFestivalDialogueForYear(k, k.Name + "_roommate", out dialogue2);
										}
										if (dialogue2 == null)
										{
											TryGetFestivalDialogueForYear(k, k.Name + "_spouse", out dialogue2);
										}
										if (dialogue2 != null)
										{
											k.CurrentDialogue.Clear();
											k.CurrentDialogue.Push(dialogue2);
											d = k.CurrentDialogue.Peek();
										}
									}
									Game1.drawDialogue(k);
									k.faceTowardFarmerForPeriod(3000, 2, false, who);
									who.Halt();
									k.CurrentDialogue = new Stack<Dialogue>();
									k.CurrentDialogue.Push(new Dialogue(k, null, "..."));
									k.CurrentDialogue.Push(d);
									return true;
								}
							}
							else if (k.CurrentDialogue.Count > 0 && k.CurrentDialogue.Peek().getCurrentDialogue().Equals("..."))
							{
								k.CurrentDialogue.Clear();
							}
							goto IL_12de;
						}
					}
					if (festivalData != null && isSpecificFestival("spring13"))
					{
						Microsoft.Xna.Framework.Rectangle tile = new Microsoft.Xna.Framework.Rectangle(tileLocation.X * 64, tileLocation.Y * 64, 64, 64);
						for (int i = festivalProps.Count - 1; i >= 0; i--)
						{
							if (festivalProps[i].isColliding(tile))
							{
								who.festivalScore++;
								festivalProps.RemoveAt(i);
								who.team.FestivalPropsRemoved(tile);
								if (who.IsLocalPlayer)
								{
									Game1.playSound("coin");
								}
								return true;
							}
						}
					}
					foreach (MapSeat seat in location.mapSeats)
					{
						if (seat.OccupiesTile(tileLocation.X, tileLocation.Y) && !seat.IsBlocked(location))
						{
							who.BeginSitting(seat);
							return true;
						}
					}
				}
			}
			return false;
		}

		public void removeFestivalProps(Microsoft.Xna.Framework.Rectangle rect)
		{
			for (int i = festivalProps.Count - 1; i >= 0; i--)
			{
				if (festivalProps[i].isColliding(rect))
				{
					festivalProps.RemoveAt(i);
				}
			}
		}

		public void checkForSpecialCharacterIconAtThisTile(Vector2 tileLocation)
		{
			if (isFestival && festivalHost != null && festivalHost.Tile == tileLocation)
			{
				Game1.mouseCursor = Game1.cursor_talk;
			}
		}

		public void forceEndFestival(Farmer who)
		{
			Game1.currentMinigame = null;
			Game1.exitActiveMenu();
			Game1.player.Halt();
			endBehaviors();
			if (Game1.IsServer)
			{
				Game1.multiplayer.sendServerToClientsMessage("endFest");
			}
			Game1.changeMusicTrack("none");
		}

		public bool checkForCollision(Microsoft.Xna.Framework.Rectangle position, Farmer who)
		{
			Microsoft.Xna.Framework.Rectangle playerBounds = who.GetBoundingBox();
			foreach (NPC i in actors)
			{
				Microsoft.Xna.Framework.Rectangle actorBounds = i.GetBoundingBox();
				if (actorBounds.Intersects(position) && !farmer.temporarilyInvincible && farmer.TemporaryPassableTiles.IsEmpty() && !i.IsInvisible && !playerBounds.Intersects(actorBounds) && !i.farmerPassesThrough)
				{
					return true;
				}
			}
			if (Game1.currentLocation.IsOutOfBounds(position))
			{
				TryStartEndFestivalDialogue(who);
				return true;
			}
			foreach (Object prop in props)
			{
				if (prop.GetBoundingBox().Intersects(position))
				{
					return true;
				}
			}
			if (temporaryLocation != null)
			{
				foreach (Object value in temporaryLocation.objects.Values)
				{
					if (value.GetBoundingBox().Intersects(position))
					{
						return true;
					}
				}
			}
			foreach (Prop festivalProp in festivalProps)
			{
				if (festivalProp.isColliding(position))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Show the dialogue to end the current festival when the player tries to leave the location.</summary>
		/// <param name="who">The local player instance.</param>
		/// <returns>Returns whether the dialogue was displayed.</returns>
		public bool TryStartEndFestivalDialogue(Farmer who)
		{
			if (!who.IsLocalPlayer || !isFestival)
			{
				return false;
			}
			who.Halt();
			who.Position = who.lastPosition;
			if (!Game1.IsMultiplayer && Game1.activeClickableMenu == null)
			{
				Game1.activeClickableMenu = new ConfirmationDialog(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1758", FestivalName), forceEndFestival);
			}
			else if (Game1.activeClickableMenu == null)
			{
				Game1.netReady.SetLocalReady("festivalEnd", true);
				Game1.activeClickableMenu = new ReadyCheckDialog("festivalEnd", true, forceEndFestival);
			}
			return true;
		}

		public void answerDialogue(string questionKey, int answerChoice)
		{
			previousAnswerChoice = answerChoice;
			if (questionKey.Contains("fork"))
			{
				int forkAnswer = Convert.ToInt32(questionKey.Replace("fork", ""));
				if (answerChoice == forkAnswer)
				{
					specialEventVariable1 = !specialEventVariable1;
				}
				return;
			}
			if (questionKey.Contains("quickQuestion"))
			{
				string obj = eventCommands[Math.Min(eventCommands.Length - 1, CurrentCommand)];
				string[] newCommands = obj.Substring(obj.IndexOf(' ') + 1).Split("(break)")[1 + answerChoice].Split('\\');
				List<string> tmp = eventCommands.ToList();
				tmp.InsertRange(CurrentCommand + 1, newCommands);
				eventCommands = tmp.ToArray();
				return;
			}
			switch (questionKey)
			{
			case "spirits_eve_shortcut":
				if (answerChoice == 0)
				{
					Game1.player.freezePause = 2000;
					Game1.globalFadeToBlack(delegate
					{
						Game1.player.Position = new Vector2(32f, 49f) * 64f;
						Game1.player.faceDirection(2);
						Game1.playSound("stairsdown");
						Game1.globalFadeToClear();
					});
				}
				break;
			case "shaneCliffs":
				switch (answerChoice)
				{
				case 0:
					eventCommands[currentCommand + 2] = "speak Shane \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1760") + "\"";
					break;
				case 1:
					eventCommands[currentCommand + 2] = "speak Shane \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1761") + "\"";
					break;
				case 2:
					eventCommands[currentCommand + 2] = "speak Shane \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1763") + "\"";
					break;
				case 3:
					eventCommands[currentCommand + 2] = "speak Shane \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1764") + "\"";
					break;
				}
				break;
			case "shaneLoan":
				if (answerChoice != 0)
				{
					int num = 1;
					break;
				}
				specialEventVariable1 = true;
				eventCommands[currentCommand + 1] = "fork giveShaneLoan";
				Game1.player.Money -= 3000;
				break;
			case "haleyDarkRoom":
				switch (answerChoice)
				{
				case 0:
					specialEventVariable1 = true;
					eventCommands[currentCommand + 1] = "fork decorate";
					break;
				case 1:
					specialEventVariable1 = true;
					eventCommands[currentCommand + 1] = "fork leave";
					break;
				case 2:
					break;
				}
				break;
			case "chooseCharacter":
				switch (answerChoice)
				{
				case 0:
					specialEventVariable1 = true;
					eventCommands[currentCommand + 1] = "fork warrior";
					break;
				case 1:
					specialEventVariable1 = true;
					eventCommands[currentCommand + 1] = "fork healer";
					break;
				case 2:
					break;
				}
				break;
			case "bandFork":
				switch (answerChoice)
				{
				case 76:
					specialEventVariable1 = true;
					eventCommands[currentCommand + 1] = "fork poppy";
					break;
				case 77:
					specialEventVariable1 = true;
					eventCommands[currentCommand + 1] = "fork heavy";
					break;
				case 78:
					specialEventVariable1 = true;
					eventCommands[currentCommand + 1] = "fork techno";
					break;
				case 79:
					specialEventVariable1 = true;
					eventCommands[currentCommand + 1] = "fork honkytonk";
					break;
				}
				break;
			case "StarTokenShop":
				if (answerChoice == 0)
				{
					Game1.activeClickableMenu = new NumberSelectionMenu(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1774"), buyStarTokens, 50, 0, 999);
				}
				break;
			case "wheelBet":
				specialEventVariable2 = answerChoice == 1;
				if (answerChoice != 2)
				{
					Game1.activeClickableMenu = new NumberSelectionMenu(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1776"), betStarTokens, -1, 1, Game1.player.festivalScore, Math.Min(1, Game1.player.festivalScore));
				}
				break;
			case "fortuneTeller":
				if (answerChoice == 0)
				{
					Game1.globalFadeToBlack(readFortune);
					Game1.player.Money -= 100;
					Game1.player.mailReceived.Add("fortuneTeller" + Game1.year);
				}
				break;
			case "slingshotGame":
				if (answerChoice == 0)
				{
					if (Game1.player.Money >= 50)
					{
						Game1.globalFadeToBlack(TargetGame.startMe, 0.01f);
						Game1.player.Money -= 50;
					}
					else
					{
						Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1780"));
					}
				}
				break;
			case "fishingGame":
				if (answerChoice == 0)
				{
					if (Game1.player.Money >= 50)
					{
						Game1.globalFadeToBlack(FishingGame.startMe, 0.01f);
						Game1.player.Money -= 50;
					}
					else
					{
						Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1780"));
					}
				}
				break;
			case "starTokenShop":
			{
				if (answerChoice != 0 || !Utility.TryOpenShopMenu("Festival_StardewValleyFair_StarTokens", temporaryLocation, null, null, false, false))
				{
					break;
				}
				ShopMenu shop = Game1.activeClickableMenu as ShopMenu;
				if (shop != null)
				{
					if (shop.IsOutOfStock())
					{
						shop.exitThisMenuNoSound();
						Game1.drawObjectDialogue(Game1.parseText(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1785")));
					}
					else
					{
						shop.PlayOpenSound();
					}
				}
				break;
			}
			case "secretSanta":
				if (answerChoice == 0)
				{
					Game1.activeClickableMenu = new ItemGrabMenu(null, true, false, Utility.highlightSantaObjects, chooseSecretSantaGift, Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1788", secretSantaRecipient.displayName), null, false, false, true, true, false, 0, null, -1, this);
				}
				break;
			case "cave":
				if (answerChoice == 0)
				{
					Game1.MasterPlayer.caveChoice.Value = 2;
					Game1.RequireLocation<FarmCave>("FarmCave").setUpMushroomHouse();
				}
				else
				{
					Game1.MasterPlayer.caveChoice.Value = 1;
				}
				break;
			case "pet":
				if (answerChoice == 0)
				{
					string title = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1236");
					Game1.activeClickableMenu = new NamingMenu(defaultName: (!Game1.player.IsMale) ? Game1.content.LoadString((Game1.player.whichPetType == "Dog") ? "Strings\\StringsFromCSFiles:Event.cs.1797" : "Strings\\StringsFromCSFiles:Event.cs.1796") : Game1.content.LoadString(Game1.player.catPerson ? "Strings\\StringsFromCSFiles:Event.cs.1794" : "Strings\\StringsFromCSFiles:Event.cs.1795"), b: namePet, title: title);
					break;
				}
				Game1.player.mailReceived.Add("rejectedPet");
				eventCommands = new string[2];
				eventCommands[1] = "end";
				eventCommands[0] = "speak Marnie \"" + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1798") + "\"";
				currentCommand = 0;
				eventSwitched = true;
				specialEventVariable1 = true;
				break;
			}
		}

		private void namePet(string name)
		{
			Pet p = new Pet(68, 13, Game1.player.whichPetBreed, Game1.player.whichPetType);
			gotPet = true;
			p.warpToFarmHouse(Game1.player);
			p.Name = name;
			p.displayName = p.name;
			foreach (Building building in Game1.getFarm().buildings)
			{
				PetBowl bowl = building as PetBowl;
				if (bowl != null && !bowl.HasPet())
				{
					bowl.AssignPet(p);
					break;
				}
			}
			foreach (Farmer allFarmer in Game1.getAllFarmers())
			{
				allFarmer.autoGenerateActiveDialogueEvent("gotPet");
			}
			Game1.exitActiveMenu();
			CurrentCommand++;
		}

		public void chooseSecretSantaGift(Item i, Farmer who)
		{
			if (i == null)
			{
				return;
			}
			Object obj = i as Object;
			if (obj != null)
			{
				if (obj.Stack > 1)
				{
					obj.Stack--;
					who.addItemToInventory(obj);
				}
				Game1.exitActiveMenu();
				NPC recipient = getActorByName(secretSantaRecipient.Name);
				recipient.faceTowardFarmerForPeriod(15000, 5, false, who);
				recipient.receiveGift(obj, who, false, 5f, false);
				recipient.CurrentDialogue.Clear();
				string article = Lexicon.getProperArticleForWord(obj.DisplayName);
				recipient.CurrentDialogue.Push(recipient.TryGetDialogue("WinterStar_ReceiveGift_" + obj.QualifiedItemId, obj.DisplayName, article) ?? (from tag in obj.GetContextTags()
					select recipient.TryGetDialogue("WinterStar_ReceiveGift_" + tag, obj.DisplayName, article)).FirstOrDefault((Dialogue p) => p != null) ?? recipient.TryGetDialogue("WinterStar_ReceiveGift", obj.DisplayName, article) ?? Dialogue.FromTranslation(recipient, "Strings\\StringsFromCSFiles:Event.cs.1801", obj.DisplayName, article));
				Game1.drawDialogue(recipient);
				secretSantaRecipient = null;
				startSecretSantaAfterDialogue = true;
				who.Halt();
				who.completelyStopAnimatingOrDoingAction();
				who.faceGeneralDirection(recipient.Position, 0, false, false);
			}
			else
			{
				Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1803"));
			}
		}

		public void perfectFishing()
		{
			if (isFestival)
			{
				FishingGame fishingGame = Game1.currentMinigame as FishingGame;
				if (fishingGame != null && isSpecificFestival("fall16"))
				{
					fishingGame.perfections++;
				}
			}
		}

		public void caughtFish(string itemId, int size, Farmer who)
		{
			if (itemId == null || !isFestival)
			{
				return;
			}
			FishingGame fishingGame = Game1.currentMinigame as FishingGame;
			if (fishingGame != null && isSpecificFestival("fall16"))
			{
				fishingGame.score += ((size <= 0) ? 1 : (size + 5));
				if (size > 0)
				{
					fishingGame.fishCaught++;
				}
				Game1.player.FarmerSprite.PauseForSingleAnimation = false;
				Game1.player.FarmerSprite.StopAnimation();
			}
			else if (isSpecificFestival("winter8"))
			{
				if (size > 0 && who.TilePoint.X < 79 && who.TilePoint.Y < 43)
				{
					who.festivalScore++;
					Game1.playSound("newArtifact");
				}
				who.forceCanMove();
				if (previousFacingDirection != -1)
				{
					who.faceDirection(previousFacingDirection);
				}
			}
		}

		public void readFortune()
		{
			Game1.globalFade = true;
			Game1.fadeToBlackAlpha = 1f;
			NPC topRomance = Utility.getTopRomanticInterest(Game1.player);
			NPC topFriend = Utility.getTopNonRomanticInterest(Game1.player);
			int topSkill = Utility.getHighestSkill(Game1.player);
			string[] fortune = new string[5];
			if (topFriend != null && Game1.player.getFriendshipLevelForNPC(topFriend.Name) > 100)
			{
				if (Utility.getNumberOfFriendsWithinThisRange(Game1.player, Game1.player.getFriendshipLevelForNPC(topFriend.Name) - 100, Game1.player.getFriendshipLevelForNPC(topFriend.Name)) > 3 && Game1.random.NextBool())
				{
					fortune[0] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1810");
				}
				else
				{
					switch (Game1.random.Next(4))
					{
					case 0:
						fortune[0] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1811", topFriend.displayName);
						break;
					case 1:
						fortune[0] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1813", topFriend.displayName) + ((topFriend.Gender == Gender.Male) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1815") : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1816"));
						break;
					case 2:
						fortune[0] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1818", topFriend.displayName);
						break;
					case 3:
						fortune[0] = ((topFriend.Gender == Gender.Male) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1820") : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1821")) + Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1823", topFriend.displayName);
						break;
					}
				}
			}
			else
			{
				fortune[0] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1825");
			}
			if (topRomance != null && Game1.player.getFriendshipLevelForNPC(topRomance.Name) > 250)
			{
				if (Utility.getNumberOfFriendsWithinThisRange(Game1.player, Game1.player.getFriendshipLevelForNPC(topRomance.Name) - 100, Game1.player.getFriendshipLevelForNPC(topRomance.Name), true) > 2)
				{
					fortune[1] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1826");
				}
				else
				{
					switch (Game1.random.Next(4))
					{
					case 0:
						fortune[1] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1827", topRomance.displayName);
						break;
					case 1:
						fortune[1] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1829", topRomance.displayName);
						break;
					case 2:
						fortune[1] = ((topRomance.Gender != 0) ? ((topRomance.SocialAnxiety == 1) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1833") : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1834")) : ((topRomance.SocialAnxiety == 1) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1831") : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1832"))) + " " + ((topRomance.Gender == Gender.Male) ? Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1837", topRomance.displayName[0]) : Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1838", topRomance.displayName[0]));
						break;
					case 3:
						fortune[1] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1843", topRomance.displayName);
						break;
					}
				}
			}
			else
			{
				fortune[1] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1845");
			}
			switch (topSkill)
			{
			case 0:
				fortune[2] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1846");
				break;
			case 3:
				fortune[2] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1847");
				break;
			case 4:
				fortune[2] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1848");
				break;
			case 1:
				fortune[2] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1849");
				break;
			case 2:
				fortune[2] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1850");
				break;
			case 5:
				fortune[2] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1851");
				break;
			}
			fortune[3] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1852");
			fortune[4] = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1853");
			Game1.multipleDialogues(fortune);
			Game1.afterDialogues = fadeClearAndviewportUnfreeze;
			Game1.viewportFreeze = true;
			Game1.viewport.X = -9999;
		}

		public void fadeClearAndviewportUnfreeze()
		{
			Game1.fadeClear();
			Game1.viewportFreeze = false;
		}

		public void betStarTokens(int value, int price, Farmer who)
		{
			if (value <= who.festivalScore)
			{
				Game1.playSound("smallSelect");
				Game1.activeClickableMenu = new WheelSpinGame(value);
			}
		}

		public void buyStarTokens(int value, int price, Farmer who)
		{
			if (value > 0 && value * price <= who.Money)
			{
				who.Money -= price * value;
				who.festivalScore += value;
				Game1.playSound("purchase");
				Game1.exitActiveMenu();
			}
		}

		public void clickToAddItemToLuauSoup(Item i, Farmer who)
		{
			addItemToLuauSoup(i, who);
		}

		public void setUpAdvancedMove(string[] args, NPCController.endBehavior endBehavior = null)
		{
			string actorName;
			string error;
			bool loop;
			if (!ArgUtility.TryGet(args, 1, out actorName, out error, false) || !ArgUtility.TryGetBool(args, 2, out loop, out error))
			{
				LogCommandError(args, error);
				return;
			}
			List<Vector2> path = new List<Vector2>();
			for (int i = 3; i < args.Length; i += 2)
			{
				Vector2 tile;
				if (ArgUtility.TryGetVector2(args, i, out tile, out error, true))
				{
					path.Add(tile);
				}
				else
				{
					LogCommandError(args, error);
				}
			}
			if (npcControllers == null)
			{
				npcControllers = new List<NPCController>();
			}
			int farmerNumber;
			if (IsFarmerActorId(actorName, out farmerNumber))
			{
				Farmer f = GetFarmerActor(farmerNumber);
				if (f != null)
				{
					npcControllers.Add(new NPCController(f, path, loop, endBehavior));
				}
			}
			else
			{
				NPC j = getActorByName(actorName, true);
				if (j != null)
				{
					npcControllers.Add(new NPCController(j, path, loop, endBehavior));
				}
			}
		}

		public static bool IsItemMayorShorts(Item i)
		{
			if (!(i?.QualifiedItemId == "(O)789"))
			{
				return i?.QualifiedItemId == "(O)71";
			}
			return true;
		}

		public void addItemToLuauSoup(Item i, Farmer who)
		{
			if (i == null)
			{
				return;
			}
			who.team.luauIngredients.Add(i.getOne());
			if (who.IsLocalPlayer)
			{
				specialEventVariable2 = true;
				bool is_shorts = IsItemMayorShorts(i);
				if (i != null && i.Stack > 1 && !is_shorts)
				{
					i.Stack--;
					who.addItemToInventory(i);
				}
				else if (is_shorts)
				{
					who.addItemToInventory(i);
				}
				Game1.exitActiveMenu();
				Game1.playSound("dropItemInWater");
				if (i != null)
				{
					Game1.drawObjectDialogue(Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1857", i.DisplayName));
				}
				string qualityString = "";
				switch (i.Quality)
				{
				case 1:
					qualityString = " ([51])";
					break;
				case 2:
					qualityString = " ([52])";
					break;
				case 4:
					qualityString = " ([53])";
					break;
				}
				if (!is_shorts)
				{
					Game1.multiplayer.globalChatInfoMessage("LuauSoup", Game1.player.Name, TokenStringBuilder.ItemName(i.QualifiedItemId) + qualityString);
				}
			}
		}

		private void governorTaste()
		{
			int likeLevel = 5;
			foreach (Item luauIngredient in Game1.player.team.luauIngredients)
			{
				Object o = luauIngredient as Object;
				int itemLevel = 5;
				if (IsItemMayorShorts(o))
				{
					likeLevel = 6;
					break;
				}
				if ((o.Quality >= 2 && (int)o.price >= 160) || (o.Quality == 1 && (int)o.price >= 300 && (int)o.edibility > 10))
				{
					itemLevel = 4;
					Utility.improveFriendshipWithEveryoneInRegion(Game1.player, 120, "Town");
				}
				else if ((int)o.edibility >= 20 || (int)o.price >= 100 || ((int)o.price >= 70 && o.Quality >= 1))
				{
					itemLevel = 3;
					Utility.improveFriendshipWithEveryoneInRegion(Game1.player, 60, "Town");
				}
				else if (((int)o.price > 20 && (int)o.edibility >= 10) || ((int)o.price >= 40 && (int)o.edibility >= 5))
				{
					itemLevel = 2;
				}
				else if ((int)o.edibility >= 0)
				{
					itemLevel = 1;
					Utility.improveFriendshipWithEveryoneInRegion(Game1.player, -50, "Town");
				}
				if ((int)o.edibility > -300 && (int)o.edibility < 0)
				{
					itemLevel = 0;
					Utility.improveFriendshipWithEveryoneInRegion(Game1.player, -100, "Town");
				}
				if (itemLevel < likeLevel)
				{
					likeLevel = itemLevel;
				}
			}
			if (likeLevel != 6 && Game1.player.team.luauIngredients.Count < Game1.numberOfPlayers())
			{
				likeLevel = 5;
			}
			eventCommands[CurrentCommand + 1] = "switchEvent governorReaction" + likeLevel;
			if (likeLevel == 4)
			{
				Game1.getAchievement(38);
			}
		}

		private void eggHuntWinner()
		{
			int numberOfEggsToWin;
			switch (Game1.numberOfPlayers())
			{
			case 1:
				numberOfEggsToWin = 9;
				break;
			case 2:
				numberOfEggsToWin = 6;
				break;
			case 3:
				numberOfEggsToWin = 5;
				break;
			default:
				numberOfEggsToWin = 4;
				break;
			}
			List<Farmer> winners = new List<Farmer>();
			int mostEggsScore = Game1.player.festivalScore;
			foreach (Farmer temp2 in Game1.getOnlineFarmers())
			{
				if (temp2.festivalScore > mostEggsScore)
				{
					mostEggsScore = temp2.festivalScore;
				}
			}
			foreach (Farmer temp in Game1.getOnlineFarmers())
			{
				if (temp.festivalScore == mostEggsScore)
				{
					winners.Add(temp);
					festivalWinners.Add(temp.UniqueMultiplayerID);
				}
			}
			string winnerDialogue = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1862");
			if (mostEggsScore >= numberOfEggsToWin)
			{
				foreach (Farmer item in winners)
				{
					item.autoGenerateActiveDialogueEvent("wonEggHunt");
				}
				if (winners.Count == 1)
				{
					winnerDialogue = ((LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.es) ? ("¡" + winners[0].displayName + "!") : (winners[0].displayName + "!"));
				}
				else
				{
					winnerDialogue = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1864");
					for (int i = 0; i < winners.Count; i++)
					{
						if (i == winners.Count - 1)
						{
							winnerDialogue += Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1865");
						}
						winnerDialogue = winnerDialogue + " " + winners[i].displayName;
						if (i < winners.Count - 1)
						{
							winnerDialogue += ",";
						}
					}
					winnerDialogue += "!";
				}
				specialEventVariable1 = false;
			}
			else
			{
				specialEventVariable1 = true;
			}
			NPC lewis = getActorByName("Lewis");
			lewis.CurrentDialogue.Push(new Dialogue(lewis, null, winnerDialogue));
			Game1.drawDialogue(lewis);
		}

		private void iceFishingWinner()
		{
			int numberOfFishToWin = 5;
			iceFishWinners = new List<Farmer>();
			int mostFishScore = Game1.player.festivalScore;
			for (int k = 1; k <= Game1.numberOfPlayers(); k++)
			{
				Farmer temp = GetFarmerActor(k);
				if (temp != null && temp.festivalScore > mostFishScore)
				{
					mostFishScore = temp.festivalScore;
				}
			}
			for (int j = 1; j <= Game1.numberOfPlayers(); j++)
			{
				Farmer temp2 = GetFarmerActor(j);
				if (temp2 != null && temp2.festivalScore == mostFishScore)
				{
					iceFishWinners.Add(temp2);
					festivalWinners.Add(temp2.UniqueMultiplayerID);
				}
			}
			string winnerDialogue = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1871");
			if (mostFishScore >= numberOfFishToWin)
			{
				foreach (Farmer iceFishWinner in iceFishWinners)
				{
					iceFishWinner.autoGenerateActiveDialogueEvent("wonIceFishing");
				}
				if (iceFishWinners.Count == 1)
				{
					winnerDialogue = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1872", iceFishWinners[0].displayName, iceFishWinners[0].festivalScore);
				}
				else
				{
					winnerDialogue = Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1864");
					for (int i = 0; i < iceFishWinners.Count; i++)
					{
						if (i == iceFishWinners.Count - 1)
						{
							winnerDialogue += Game1.content.LoadString("Strings\\StringsFromCSFiles:Event.cs.1865");
						}
						winnerDialogue = winnerDialogue + " " + iceFishWinners[i].displayName;
						if (i < iceFishWinners.Count - 1)
						{
							winnerDialogue += ",";
						}
					}
					winnerDialogue += "!";
				}
				specialEventVariable1 = false;
			}
			else
			{
				specialEventVariable1 = true;
			}
			NPC lewis = getActorByName("Lewis");
			lewis.CurrentDialogue.Push(new Dialogue(lewis, null, winnerDialogue));
			Game1.drawDialogue(lewis);
		}

		private void iceFishingWinnerMP()
		{
			specialEventVariable1 = !iceFishWinners.Contains(Game1.player);
		}

		public void popBalloons(int x, int y)
		{
			if ((!this.id.Equals("191393") && !this.id.Equals("502261")) || aboveMapSprites == null)
			{
				return;
			}
			List<int> idsToRemove = new List<int>();
			for (int j = aboveMapSprites.Count - 1; j >= 0; j--)
			{
				TemporaryAnimatedSprite t = aboveMapSprites[j];
				int width = t.sourceRect.Width * 4;
				int height = t.sourceRect.Height * 4;
				Microsoft.Xna.Framework.Rectangle r = new Microsoft.Xna.Framework.Rectangle((int)t.Position.X, (int)t.Position.Y, width, height);
				if (r.Contains(x, y))
				{
					idsToRemove.Add(t.id);
					if (t.sourceRect.Height <= 16)
					{
						for (int z = 0; z < 3; z++)
						{
							aboveMapSprites.Add(new TemporaryAnimatedSprite("LooseSprites\\Cursors", new Microsoft.Xna.Framework.Rectangle(280 + Game1.random.Choose(8, 0), 1954, 8, 8), 1000f, 1, 99, Utility.getRandomPositionInThisRectangle(r, Game1.random), false, false, 1f, 0f, t.color, 4f, 0f, 0f, (float)Game1.random.Next(-10, 11) / 100f)
							{
								motion = new Vector2(Game1.random.Next(-4, 5), -8f + (float)Game1.random.Next(-10, 1) / 100f),
								acceleration = new Vector2(0f, 0.3f),
								local = true
							});
						}
					}
				}
			}
			foreach (int id in idsToRemove)
			{
				for (int i = aboveMapSprites.Count - 1; i >= 0; i--)
				{
					if (aboveMapSprites[i].id == id || aboveMapSprites[i].id == 9988)
					{
						aboveMapSprites.RemoveAt(i);
					}
				}
			}
			if (idsToRemove.Count > 0)
			{
				int_useMeForAnything++;
				aboveMapSprites.Add(new TemporaryAnimatedSprite(null, Microsoft.Xna.Framework.Rectangle.Empty, new Vector2(16f, 16f), false, 0f, Color.White)
				{
					text = (int_useMeForAnything.ToString() ?? ""),
					layerDepth = 1f,
					animationLength = 1,
					totalNumberOfLoops = 10,
					interval = 300f,
					scale = 2f,
					local = true,
					id = 9988
				});
				Game1.playSound("coin");
			}
		}
	}
}
