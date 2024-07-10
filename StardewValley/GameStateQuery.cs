using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using StardewValley.Delegates;
using StardewValley.Extensions;
using StardewValley.GameData.Locations;
using StardewValley.Internal;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Logging;
using StardewValley.Network;

namespace StardewValley
{
	/// <summary>Resolves game state queries like <c>SEASON spring</c> in data assets.</summary>
	/// <summary>Resolves game state queries like <c>SEASON spring</c> in data assets.</summary>
	public class GameStateQuery
	{
		/// <summary>The helper methods which simplify implementing custom game state query resolvers.</summary>
		public static class Helpers
		{
			/// <summary>Get the location matching a given name.</summary>
			/// <param name="locationName">The location to check. This can be <c>Here</c> (current location), <c>Target</c> (contextual location), or a location name.</param>
			/// <param name="contextualLocation">The location for which the query is being checked.</param>
			public static GameLocation GetLocation(string locationName, GameLocation contextualLocation)
			{
				if (string.Equals(locationName, "Here", StringComparison.OrdinalIgnoreCase))
				{
					return Game1.currentLocation;
				}
				if (string.Equals(locationName, "Target", StringComparison.OrdinalIgnoreCase))
				{
					return contextualLocation ?? Game1.currentLocation;
				}
				return Game1.getLocationFromName(locationName);
			}

			/// <summary>Get the location matching a given name, or throw an exception if it's not found.</summary>
			/// <param name="locationName">The location to check. This can be <c>Here</c> (current location), <c>Target</c> (contextual location), or a location name.</param>
			/// <param name="contextualLocation">The location for which the query is being checked.</param>
			public static GameLocation RequireLocation(string locationName, GameLocation contextualLocation)
			{
				GameLocation location = GetLocation(locationName, contextualLocation);
				if (location == null)
				{
					throw new KeyNotFoundException("Required location '" + locationName + "' not found.");
				}
				return location;
			}

			/// <summary>Try to get a location matching a target location argument.</summary>
			/// <param name="query">The game state query split by space, including the query key.</param>
			/// <param name="index">The argument index to read.</param>
			/// <param name="error">An error phrase indicating why getting the argument failed (like 'required index X not found'), if applicable.</param>
			/// <param name="location">The contextual location instance, which will be updated if the argument is valid.</param>
			public static bool TryGetLocationArg(string[] query, int index, ref GameLocation location, out string error)
			{
				string locationTarget;
				if (!ArgUtility.TryGet(query, index, out locationTarget, out error))
				{
					location = null;
					return false;
				}
				GameLocation loaded = GetLocation(locationTarget, location);
				if (loaded == null)
				{
					error = "no location found matching '" + locationTarget + "'";
					return false;
				}
				location = loaded;
				return true;
			}

			/// <summary>Try to get an item matching an item type argument.</summary>
			/// <param name="query">The game state query split by space, including the query key.</param>
			/// <param name="index">The argument index to read.</param>
			/// <param name="targetItem">The target item (e.g. machine output or tree fruit), or <c>null</c> if not applicable.</param>
			/// <param name="inputItem">The input item (e.g. machine input), or <c>null</c> if not applicable.</param>
			/// <param name="error">An error phrase indicating why getting the argument failed (like 'required index X not found'), if applicable.</param>
			/// <param name="item">The item instance, if valid.</param>
			public static bool TryGetItemArg(string[] query, int index, Item targetItem, Item inputItem, out Item item, out string error)
			{
				string itemType;
				if (!ArgUtility.TryGet(query, index, out itemType, out error))
				{
					item = null;
					return false;
				}
				if (string.Equals(itemType, "Target", StringComparison.OrdinalIgnoreCase))
				{
					item = targetItem;
					return true;
				}
				if (string.Equals(itemType, "Input", StringComparison.OrdinalIgnoreCase))
				{
					item = inputItem;
					return true;
				}
				item = null;
				error = "invalid item type '" + itemType + "' (should be 'Input' or 'Target')";
				return false;
			}

			/// <summary>Get whether a check applies to the given player or players.</summary>
			/// <param name="contextualPlayer">The player for which the query is being checked.</param>
			/// <param name="playerKey">The players to check. This can be <c>Any</c> (at least one player matches), <c>All</c> (every player matches), <c>Current</c> (the current player), <c>Target</c> (the contextual player), <c>Host</c> (the main player), or a player ID.</param>
			/// <param name="check">The check to perform.</param>
			public static bool WithPlayer(Farmer contextualPlayer, string playerKey, Func<Farmer, bool> check)
			{
				if (string.Equals(playerKey, "Any", StringComparison.OrdinalIgnoreCase))
				{
					foreach (Farmer farmer2 in Game1.getAllFarmers())
					{
						if (check(farmer2))
						{
							return true;
						}
					}
					return false;
				}
				if (string.Equals(playerKey, "All", StringComparison.OrdinalIgnoreCase))
				{
					foreach (Farmer farmer in Game1.getAllFarmers())
					{
						if (!check(farmer))
						{
							return false;
						}
					}
					return true;
				}
				if (string.Equals(playerKey, "Current", StringComparison.OrdinalIgnoreCase))
				{
					return check(Game1.player);
				}
				if (string.Equals(playerKey, "Target", StringComparison.OrdinalIgnoreCase))
				{
					return check(contextualPlayer);
				}
				if (string.Equals(playerKey, "Host", StringComparison.OrdinalIgnoreCase))
				{
					return check(Game1.MasterPlayer);
				}
				long parsedId;
				if (long.TryParse(playerKey, out parsedId))
				{
					return check(Game1.getFarmerMaybeOffline(parsedId));
				}
				return false;
			}

			/// <summary>Get whether any query argument matches a condition.</summary>
			/// <param name="query">The game state query split by space, including the query key.</param>
			/// <param name="startAt">The index within <paramref name="query" /> to start iterating.</param>
			/// <param name="check">Check whether a query argument matches. This should return true (argument matches), false (argument doesn't match, but we can try the remaining arguments), or null (argument caused an error so we should stop iterating).</param>
			public static bool AnyArgMatches(string[] query, int startAt, Func<string, bool?> check)
			{
				for (int i = startAt; i < query.Length; i++)
				{
					bool? result = check(query[i]);
					if (result ?? false)
					{
						return true;
					}
					if (!result.HasValue)
					{
						return false;
					}
				}
				return false;
			}

			/// <summary>Log an error indicating that a query couldn't be parsed.</summary>
			/// <param name="query">The game state query split by space, including the query key.</param>
			/// <param name="reason">The human-readable reason why the query is invalid.</param>
			/// <param name="exception">The underlying exception, if applicable.</param>
			/// <returns>Returns false.</returns>
			public static bool ErrorResult(string[] query, string reason, Exception exception = null)
			{
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(30, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Failed parsing condition '");
				defaultInterpolatedStringHandler.AppendFormatted(string.Join(" ", query));
				defaultInterpolatedStringHandler.AppendLiteral("': ");
				defaultInterpolatedStringHandler.AppendFormatted(reason);
				defaultInterpolatedStringHandler.AppendLiteral(".");
				log.Error(defaultInterpolatedStringHandler.ToStringAndClear(), exception);
				return false;
			}

			/// <summary>The common implementation for <c>PLAYER_*_SKILL</c> game state queries.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PlayerSkillLevelImpl(string[] query, Farmer player, Func<Farmer, int> getLevel)
			{
				string playerKey;
				string error;
				int minLevel;
				int maxLevel;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetInt(query, 2, out minLevel, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxLevel, out error, int.MaxValue))
				{
					return ErrorResult(query, error);
				}
				return WithPlayer(player, playerKey, delegate(Farmer target)
				{
					int num = getLevel(target);
					return num >= minLevel && num <= maxLevel;
				});
			}

			/// <summary>The common implementation for most <c>RANDOM</c> game state queries.</summary>
			/// <param name="random">The random instance to use.</param>
			/// <param name="query">The condition arguments received by the query.</param>
			/// <param name="skipArguments">The number of arguments to skip. The next argument should be the chance value, followed by an optional <c>@addDailyLuck</c> argument.</param>
			public static bool RandomImpl(Random random, string[] query, int skipArguments)
			{
				float chance;
				string error;
				if (!ArgUtility.TryGetFloat(query, skipArguments, out chance, out error))
				{
					return ErrorResult(query, error);
				}
				bool addDailyLuck = false;
				for (int i = skipArguments + 1; i < query.Length; i++)
				{
					if (string.Equals(query[i], "@addDailyLuck", StringComparison.OrdinalIgnoreCase))
					{
						addDailyLuck = true;
					}
				}
				if (addDailyLuck)
				{
					chance += (float)Game1.player.DailyLuck;
				}
				return random.NextDouble() < (double)chance;
			}
		}

		/// <summary>The cached metadata for a single raw game state query.</summary>
		[IsReadOnly]
		public struct ParsedGameStateQuery
		{
			/// <summary>Whether the result should be negated.</summary>
			public readonly bool Negated;

			/// <summary>The game state query split by space, including the query key.</summary>
			public readonly string[] Query;

			/// <summary>The resolver which handles the game state query.</summary>
			public readonly GameStateQueryDelegate Resolver;

			/// <summary>An error indicating why the query is invalid, if applicable.</summary>
			public readonly string Error;

			/// <summary>Construct an instance.</summary>
			/// <param name="negated">Whether the result should be negated.</param>
			/// <param name="query">The game state query split by space, including the query key.</param>
			/// <param name="resolver">The resolver which handles the game state query.</param>
			/// <param name="error">An error indicating why parsing the query failed, if applicable.</param>
			public ParsedGameStateQuery(bool negated, string[] query, GameStateQueryDelegate resolver, string error)
			{
				Negated = negated;
				Query = query;
				Resolver = resolver;
				Error = error;
			}
		}

		/// <summary>The resolvers for vanilla game state queries. Most code should call <see cref="M:StardewValley.GameStateQuery.CheckConditions(System.String,StardewValley.Delegates.GameStateQueryContext)" /> instead of using these directly.</summary>
		public static class DefaultResolvers
		{
			/// <summary>Get whether any of the given conditions match.</summary>
			/// <remarks>The query arguments must be passed as quoted arguments. For example, <c>ANY "SEASON Winter" "SEASON Spring, DAY_OF_WEEK Friday"</c> is true if (a) it's winter or (b) it's a spring Friday.</remarks>
			/// /// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ANY(string[] query, GameStateQueryContext context)
			{
				return Helpers.AnyArgMatches(query, 1, (string value) => CheckConditions(value, context));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool SEASON_DAY(string[] query, GameStateQueryContext context)
			{
				for (int i = 1; i < query.Length; i += 2)
				{
					Season season;
					string error;
					int day;
					if (!ArgUtility.TryGetEnum<Season>(query, i, out season, out error) || !ArgUtility.TryGetInt(query, i + 1, out day, out error))
					{
						return Helpers.ErrorResult(query, error);
					}
					if (Game1.season == season && Game1.dayOfMonth == day)
					{
						return true;
					}
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool DAY_OF_MONTH(string[] query, GameStateQueryContext context)
			{
				return Helpers.AnyArgMatches(query, 1, delegate(string rawDay)
				{
					int result;
					if (int.TryParse(rawDay, out result))
					{
						return Game1.dayOfMonth == result;
					}
					if (string.Equals(rawDay, "even", StringComparison.OrdinalIgnoreCase))
					{
						return Game1.dayOfMonth % 2 == 0;
					}
					if (string.Equals(rawDay, "odd", StringComparison.OrdinalIgnoreCase))
					{
						return Game1.dayOfMonth % 2 == 1;
					}
					Helpers.ErrorResult(query, "'" + rawDay + "' isn't a valid day of month");
					return null;
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool DAY_OF_WEEK(string[] query, GameStateQueryContext context)
			{
				return Helpers.AnyArgMatches(query, 1, delegate(string rawDay)
				{
					DayOfWeek dayOfWeek;
					if (!WorldDate.TryGetDayOfWeekFor(rawDay, out dayOfWeek))
					{
						Helpers.ErrorResult(query, "'" + rawDay + "' isn't a valid day of week");
						return null;
					}
					return (Game1.Date.DayOfWeek == dayOfWeek) ? new bool?(true) : new bool?(false);
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool DAYS_PLAYED(string[] query, GameStateQueryContext context)
			{
				int minDaysPlayed;
				string error;
				int maxDaysPlayed;
				if (!ArgUtility.TryGetInt(query, 1, out minDaysPlayed, out error) || !ArgUtility.TryGetOptionalInt(query, 2, out maxDaysPlayed, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				uint daysPlayed = Game1.stats.DaysPlayed;
				if (daysPlayed >= minDaysPlayed)
				{
					return daysPlayed <= maxDaysPlayed;
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_GREEN_RAIN_DAY(string[] query, GameStateQueryContext context)
			{
				WorldDate tomorrow = new WorldDate(Game1.Date);
				tomorrow.TotalDays++;
				return Utility.isGreenRainDay(tomorrow.DayOfMonth, tomorrow.Season);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_FESTIVAL_DAY(string[] query, GameStateQueryContext context)
			{
				string locationContextId;
				string error;
				int dayOffset;
				if (!ArgUtility.TryGetOptional(query, 1, out locationContextId, out error, "any", false) || !ArgUtility.TryGetOptionalInt(query, 2, out dayOffset, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				switch (locationContextId?.ToLower())
				{
				case null:
				case "any":
					locationContextId = null;
					break;
				case "here":
				case "target":
					locationContextId = Helpers.RequireLocation(locationContextId, context.Location).GetLocationContextId();
					break;
				}
				int num = (Game1.Date.TotalDays + dayOffset) % 112;
				return Utility.isFestivalDay(season: (Season)(num / 28), day: num % 28 + 1, locationContext: locationContextId);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_PASSIVE_FESTIVAL_OPEN(string[] query, GameStateQueryContext context)
			{
				string festivalId;
				string error;
				if (!ArgUtility.TryGet(query, 1, out festivalId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Utility.IsPassiveFestivalOpen(festivalId);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_PASSIVE_FESTIVAL_TODAY(string[] query, GameStateQueryContext context)
			{
				string festivalId;
				string error;
				if (!ArgUtility.TryGet(query, 1, out festivalId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Utility.IsPassiveFestivalDay(festivalId);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool SEASON(string[] query, GameStateQueryContext context)
			{
				for (int i = 1; i < query.Length; i++)
				{
					Season season;
					string error;
					if (!ArgUtility.TryGetEnum<Season>(query, i, out season, out error))
					{
						return Helpers.ErrorResult(query, error);
					}
					if (Game1.season == season)
					{
						return true;
					}
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool YEAR(string[] query, GameStateQueryContext context)
			{
				int minYear;
				string error;
				int maxYear;
				if (!ArgUtility.TryGetInt(query, 1, out minYear, out error) || !ArgUtility.TryGetOptionalInt(query, 2, out maxYear, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				int year = Game1.year;
				if (year >= minYear)
				{
					return year <= maxYear;
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool TIME(string[] query, GameStateQueryContext context)
			{
				int minTime;
				string error;
				int maxTime;
				if (!ArgUtility.TryGetInt(query, 1, out minTime, out error) || !ArgUtility.TryGetOptionalInt(query, 2, out maxTime, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				int time = Game1.timeOfDay;
				if (time >= minTime)
				{
					return time <= maxTime;
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			[OtherNames(new string[] { "EVENT_ID" })]
			public static bool IS_EVENT(string[] query, GameStateQueryContext context)
			{
				Event @event = Game1.CurrentEvent;
				if (@event != null)
				{
					if (query.Length != 1)
					{
						return Helpers.AnyArgMatches(query, 1, (string eventId) => eventId == @event.id);
					}
					return true;
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool CAN_BUILD_CABIN(string[] query, GameStateQueryContext context)
			{
				int totalCabins = Game1.GetNumberBuildingsConstructed("Cabin");
				if (Game1.IsMasterGame)
				{
					return totalCabins < Game1.CurrentPlayerLimit - 1;
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool CAN_BUILD_FOR_CABINS(string[] query, GameStateQueryContext context)
			{
				string buildingType;
				string error;
				if (!ArgUtility.TryGet(query, 1, out buildingType, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				int totalCabins = Game1.GetNumberBuildingsConstructed("Cabin");
				return Game1.GetNumberBuildingsConstructed(buildingType) < totalCabins + 1;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool BUILDINGS_CONSTRUCTED(string[] query, GameStateQueryContext context)
			{
				string locationFilter;
				string error;
				string buildingType;
				int minCount;
				int maxCount;
				bool includeUnderConstruction;
				if (!ArgUtility.TryGet(query, 1, out locationFilter, out error) || !ArgUtility.TryGetOptional(query, 2, out buildingType, out error, "All") || !ArgUtility.TryGetOptionalInt(query, 3, out minCount, out error, 1) || !ArgUtility.TryGetOptionalInt(query, 4, out maxCount, out error, int.MaxValue) || !ArgUtility.TryGetOptionalBool(query, 5, out includeUnderConstruction, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				bool allLocations = string.Equals(locationFilter, "All", StringComparison.OrdinalIgnoreCase);
				bool allBuildings = string.Equals(buildingType, "All", StringComparison.OrdinalIgnoreCase);
				GameLocation location = context.Location;
				if (!allLocations)
				{
					location = Helpers.GetLocation(locationFilter, location);
					if (location == null)
					{
						return Helpers.ErrorResult(query, "required index 2 has value '" + locationFilter + "', which doesn't match an existing location name or one of the special keys (All, Here, or Target)");
					}
				}
				int count = ((!allLocations) ? (allBuildings ? location.getNumberBuildingsConstructed(includeUnderConstruction) : location.getNumberBuildingsConstructed(buildingType, includeUnderConstruction)) : (allBuildings ? Game1.GetNumberBuildingsConstructed(includeUnderConstruction) : Game1.GetNumberBuildingsConstructed(buildingType, includeUnderConstruction)));
				if (count >= minCount)
				{
					return count <= maxCount;
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool FARM_CAVE(string[] query, GameStateQueryContext context)
			{
				string value;
				string error;
				if (!ArgUtility.TryGet(query, 1, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				string caveType;
				switch ((int)Game1.MasterPlayer.caveChoice)
				{
				case 1:
					caveType = "Bats";
					break;
				case 2:
					caveType = "Mushrooms";
					break;
				default:
					caveType = "None";
					break;
				}
				return Helpers.AnyArgMatches(query, 1, (string rawCaveType) => string.Equals(rawCaveType, caveType, StringComparison.OrdinalIgnoreCase));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool FARM_NAME(string[] query, GameStateQueryContext context)
			{
				string farmName;
				string error;
				if (!ArgUtility.TryGetRemainder(query, 1, out farmName, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return string.Equals(context.Player.farmName, farmName, StringComparison.OrdinalIgnoreCase);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool FARM_TYPE(string[] query, GameStateQueryContext context)
			{
				string value;
				string error;
				if (!ArgUtility.TryGet(query, 1, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				string farmTypeId = Game1.GetFarmTypeID();
				string farmTypeKey = Game1.GetFarmTypeKey();
				return Helpers.AnyArgMatches(query, 1, (string rawFarmType) => string.Equals(rawFarmType, farmTypeId, StringComparison.OrdinalIgnoreCase) || string.Equals(rawFarmType, farmTypeKey, StringComparison.OrdinalIgnoreCase));
			}

			/// <summary>Get whether all the Lost Books for the library have been found.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool FOUND_ALL_LOST_BOOKS(string[] query, GameStateQueryContext context)
			{
				return Game1.netWorldState.Value.LostBooksFound >= 21;
			}

			/// <summary>Get whether an explicit target location is set.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool HAS_TARGET_LOCATION(string[] query, GameStateQueryContext context)
			{
				return context.Location != null;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_COMMUNITY_CENTER_COMPLETE(string[] query, GameStateQueryContext context)
			{
				if (Game1.MasterPlayer.hasCompletedCommunityCenter())
				{
					return !Game1.MasterPlayer.mailReceived.Contains("JojaMember");
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_CUSTOM_FARM_TYPE(string[] query, GameStateQueryContext context)
			{
				return Game1.whichFarm == 7;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_HOST(string[] query, GameStateQueryContext context)
			{
				return Game1.IsMasterGame;
			}

			/// <summary>Get whether the <see cref="T:StardewValley.Locations.IslandNorth" /> bridge is fixed.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_ISLAND_NORTH_BRIDGE_FIXED(string[] query, GameStateQueryContext context)
			{
				return ((IslandNorth)Game1.getLocationFromName("IslandNorth"))?.bridgeFixed.Value ?? false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_JOJA_MART_COMPLETE(string[] query, GameStateQueryContext context)
			{
				return Utility.hasFinishedJojaRoute();
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_MULTIPLAYER(string[] query, GameStateQueryContext context)
			{
				return Game1.IsMultiplayer;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool IS_VISITING_ISLAND(string[] query, GameStateQueryContext context)
			{
				string npcName;
				string error;
				if (!ArgUtility.TryGet(query, 1, out npcName, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Game1.IsVisitingIslandToday(npcName);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_ACCESSIBLE(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Game1.isLocationAccessible(location.NameOrUniqueName);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_CONTEXT(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				string value;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				string contextId = location.GetLocationContextId();
				return Helpers.AnyArgMatches(query, 2, (string rawContextId) => string.Equals(rawContextId, contextId, StringComparison.OrdinalIgnoreCase));
			}

			/// <summary>Get whether a location has a given value in its <see cref="F:StardewValley.GameData.Locations.LocationData.CustomFields" /> data field. This expects <c>LOCATION_HAS_CUSTOM_FIELD &lt;location&gt; &lt;field key&gt; [expected value]</c>, where omitting the expected value will just check if the field is defined.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_HAS_CUSTOM_FIELD(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				string fieldKey;
				string value;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error) || !ArgUtility.TryGet(query, 2, out fieldKey, out error, false) || !ArgUtility.TryGetOptional(query, 3, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				bool checkValue = ArgUtility.HasIndex(query, 3);
				LocationData data = location?.GetData();
				string actualValue;
				if (data?.CustomFields != null && data.CustomFields.TryGetValue(fieldKey, out actualValue))
				{
					if (checkValue)
					{
						return actualValue == value;
					}
					return true;
				}
				return false;
			}

			/// <summary>Get whether a location is indoors.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_IS_INDOORS(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return (!(location?.IsOutdoors)) ?? false;
			}

			/// <summary>Get whether a location is outdoors.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_IS_OUTDOORS(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return location?.IsOutdoors ?? false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_IS_MINES(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return location is MineShaft;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_IS_SKULL_CAVE(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				MineShaft shaft = location as MineShaft;
				if (shaft != null && shaft.mineLevel >= 121)
				{
					return shaft.mineLevel != 77377;
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_NAME(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				string value;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (location != null)
				{
					return Helpers.AnyArgMatches(query, 2, (string rawName) => string.Equals(rawName, location.Name, StringComparison.OrdinalIgnoreCase));
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_UNIQUE_NAME(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				string value;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (location != null)
				{
					return Helpers.AnyArgMatches(query, 2, (string rawName) => string.Equals(rawName, location.NameOrUniqueName, StringComparison.OrdinalIgnoreCase));
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool LOCATION_SEASON(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				string season = Game1.GetSeasonKeyForLocation(location);
				return Helpers.AnyArgMatches(query, 2, (string rawSeason) => string.Equals(rawSeason, season, StringComparison.OrdinalIgnoreCase));
			}

			/// <summary>Get whether a given number of items have been donated to the museum, optionally filtered by type. Format: <c>MUSEUM_DONATIONS &lt;min count&gt; [max count] [object type]+</c> or <c>MUSEUM_DONATIONS &lt;min count&gt; &lt;max count&gt; [object type]+</c>.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool MUSEUM_DONATIONS(string[] query, GameStateQueryContext context)
			{
				int filterIndex = 3;
				int minCount;
				string error;
				if (!ArgUtility.TryGetInt(query, 1, out minCount, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				int maxCount;
				string error2;
				if (!ArgUtility.TryGetInt(query, 2, out maxCount, out error2))
				{
					filterIndex = 2;
					maxCount = int.MaxValue;
				}
				bool filtered = query.Length > filterIndex;
				int count = 0;
				foreach (string itemId in Game1.netWorldState.Value.MuseumPieces.Values)
				{
					if (filtered)
					{
						ParsedItemData data = ItemRegistry.GetDataOrErrorItem(itemId);
						if (data.ObjectType == null)
						{
							continue;
						}
						for (int i = filterIndex; i < query.Length; i++)
						{
							if (data.ObjectType == query[i])
							{
								count++;
								break;
							}
						}
					}
					else
					{
						count++;
					}
				}
				if (count >= minCount)
				{
					return count <= maxCount;
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool WEATHER(string[] query, GameStateQueryContext context)
			{
				GameLocation location = context.Location;
				string error;
				string value;
				if (!Helpers.TryGetLocationArg(query, 1, ref location, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (location != null)
				{
					string weather = location.GetWeather().Weather;
					return Helpers.AnyArgMatches(query, 2, (string rawWeather) => string.Equals(rawWeather, weather, StringComparison.OrdinalIgnoreCase));
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool WORLD_STATE_FIELD(string[] query, GameStateQueryContext context)
			{
				string name;
				string error;
				string expectedValue;
				int maxValue;
				if (!ArgUtility.TryGet(query, 1, out name, out error) || !ArgUtility.TryGet(query, 2, out expectedValue, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxValue, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				PropertyInfo property = typeof(NetWorldState).GetProperty(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
				if ((object)property == null)
				{
					return false;
				}
				object actualValue = property.GetValue(Game1.netWorldState.Value, null);
				if (actualValue != null)
				{
					if (actualValue is bool)
					{
						bool actual3 = (bool)actualValue;
						bool expectedBool;
						if (bool.TryParse(expectedValue, out expectedBool))
						{
							return actual3 == expectedBool;
						}
						return false;
					}
					if (actualValue is int)
					{
						int actual2 = (int)actualValue;
						int minValue;
						if (int.TryParse(expectedValue, out minValue) && actual2 >= minValue)
						{
							return actual2 <= maxValue;
						}
						return false;
					}
					string actual = actualValue as string;
					if (actual != null)
					{
						return string.Equals(actual, expectedValue, StringComparison.OrdinalIgnoreCase);
					}
					return string.Equals(actualValue.ToString(), expectedValue, StringComparison.OrdinalIgnoreCase);
				}
				return string.Equals(expectedValue, "null", StringComparison.OrdinalIgnoreCase);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool WORLD_STATE_ID(string[] query, GameStateQueryContext context)
			{
				string value;
				string error;
				if (!ArgUtility.TryGet(query, 1, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.AnyArgMatches(query, 1, (string worldStateId) => NetWorldState.checkAnywhereForWorldStateID(worldStateId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool MINE_LOWEST_LEVEL_REACHED(string[] query, GameStateQueryContext context)
			{
				int minLevel;
				string error;
				int maxLevel;
				if (!ArgUtility.TryGetInt(query, 1, out minLevel, out error) || !ArgUtility.TryGetOptionalInt(query, 2, out maxLevel, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				int level = MineShaft.lowestLevelReached;
				if (level >= minLevel)
				{
					return level <= maxLevel;
				}
				return false;
			}

			/// <summary>Get whether a player has the given combat level, including any buffs which increase it.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_COMBAT_LEVEL(string[] query, GameStateQueryContext context)
			{
				return Helpers.PlayerSkillLevelImpl(query, context.Player, (Farmer target) => target.CombatLevel);
			}

			/// <summary>Get whether a player has the given farming level, including any buffs which increase it.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_FARMING_LEVEL(string[] query, GameStateQueryContext context)
			{
				return Helpers.PlayerSkillLevelImpl(query, context.Player, (Farmer target) => target.FarmingLevel);
			}

			/// <summary>Get whether a player has the given fishing level, including any buffs which increase it.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_FISHING_LEVEL(string[] query, GameStateQueryContext context)
			{
				return Helpers.PlayerSkillLevelImpl(query, context.Player, (Farmer target) => target.FishingLevel);
			}

			/// <summary>Get whether a player has the given foraging level, including any buffs which increase it.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_FORAGING_LEVEL(string[] query, GameStateQueryContext context)
			{
				return Helpers.PlayerSkillLevelImpl(query, context.Player, (Farmer target) => target.ForagingLevel);
			}

			/// <summary>Get whether a player has the given luck level, including any buffs which increase it.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_LUCK_LEVEL(string[] query, GameStateQueryContext context)
			{
				return Helpers.PlayerSkillLevelImpl(query, context.Player, (Farmer target) => target.LuckLevel);
			}

			/// <summary>Get whether a player has the given mining level, including any buffs which increase it.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_MINING_LEVEL(string[] query, GameStateQueryContext context)
			{
				return Helpers.PlayerSkillLevelImpl(query, context.Player, (Farmer target) => target.MiningLevel);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_CURRENT_MONEY(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				int minAmount;
				int maxAmount;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetInt(query, 2, out minAmount, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxAmount, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					int money = target.Money;
					return money >= minAmount && money <= maxAmount;
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_FARMHOUSE_UPGRADE(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				int minUpgradeLevel;
				int maxUpgradeLevel;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetInt(query, 2, out minUpgradeLevel, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxUpgradeLevel, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					int houseUpgradeLevel = target.HouseUpgradeLevel;
					return houseUpgradeLevel >= minUpgradeLevel && houseUpgradeLevel <= maxUpgradeLevel;
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_GENDER(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string genderName;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out genderName, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				bool isMale = string.Equals(genderName, "Male", StringComparison.OrdinalIgnoreCase);
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.IsMale == isMale);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_ACHIEVEMENT(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				int achievementId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetInt(query, 2, out achievementId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.achievements.Contains(achievementId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_ALL_ACHIEVEMENTS(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				Dictionary<int, string> achievementData = DataLoader.Achievements(Game1.content);
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					foreach (int current in achievementData.Keys)
					{
						if (!target.achievements.Contains(current))
						{
							return false;
						}
					}
					return true;
				});
			}

			/// <summary>Get whether a player has a given buff currently active.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_BUFF(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string buffId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out buffId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.buffs.IsApplied(buffId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_CAUGHT_FISH(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string fishId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out fishId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				fishId = ItemRegistry.QualifyItemId(fishId);
				if (fishId != null)
				{
					return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.fishCaught.ContainsKey(fishId));
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_CONVERSATION_TOPIC(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string topic;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out topic, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.activeDialogueEvents.ContainsKey(topic));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_CRAFTING_RECIPE(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string recipeName;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetRemainder(query, 2, out recipeName, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.craftingRecipes.ContainsKey(recipeName));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_COOKING_RECIPE(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string recipeName;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetRemainder(query, 2, out recipeName, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.cookingRecipes.ContainsKey(recipeName));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_DIALOGUE_ANSWER(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string responseId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out responseId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.DialogueQuestionsAnswered.Contains(responseId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_HEARD_SONG(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string songId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out songId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.songsHeard.Contains(songId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_ITEM(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string itemId;
				int minCount;
				int maxCount;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out itemId, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out minCount, out error, 1) || !ArgUtility.TryGetOptionalInt(query, 4, out maxCount, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					switch (itemId)
					{
					case "73":
					case "(O)73":
					{
						int goldenWalnuts = Game1.netWorldState.Value.GoldenWalnuts;
						if (goldenWalnuts >= minCount)
						{
							return goldenWalnuts <= maxCount;
						}
						return false;
					}
					case "858":
					case "(O)858":
					{
						int qiGems = Game1.player.QiGems;
						if (qiGems >= minCount)
						{
							return qiGems <= maxCount;
						}
						return false;
					}
					default:
						if (maxCount != int.MaxValue)
						{
							int num = target.Items.CountId(itemId);
							if (num >= minCount)
							{
								return num <= maxCount;
							}
							return false;
						}
						return target.Items.ContainsId(itemId, minCount);
					}
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_MAIL(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string mailId;
				string rawType;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out mailId, out error) || !ArgUtility.TryGetOptional(query, 3, out rawType, out error, "any"))
				{
					return Helpers.ErrorResult(query, error);
				}
				string type = rawType?.ToLower();
				switch (type)
				{
				default:
					return Helpers.ErrorResult(query, "unknown mail type '" + type + "'; expected 'Mailbox', 'Tomorrow', 'Received', or 'Any'");
				case "mailbox":
				case "tomorrow":
				case "received":
				case "any":
					return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
					{
						switch (type)
						{
						case "mailbox":
							return target.mailbox.Contains(mailId);
						case "tomorrow":
							return target.mailForTomorrow.Contains(mailId);
						case "received":
							return target.mailReceived.Contains(mailId);
						default:
							return target.hasOrWillReceiveMail(mailId);
						}
					});
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_PROFESSION(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				int professionId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetInt(query, 2, out professionId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.professions.Contains(professionId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_RUN_TRIGGER_ACTION(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string actionId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out actionId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.triggerActionsRun.Contains(actionId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_SECRET_NOTE(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				int noteId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetInt(query, 2, out noteId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.secretNotesSeen.Contains(noteId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_SEEN_EVENT(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string eventId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out eventId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.eventsSeen.Contains(eventId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_TOWN_KEY(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.HasTownKey);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_TRASH_CAN_LEVEL(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				int minLevel;
				int maxLevel;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetInt(query, 2, out minLevel, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxLevel, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					int trashCanLevel = target.trashCanLevel;
					return trashCanLevel >= minLevel && trashCanLevel <= maxLevel;
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_LOCATION_CONTEXT(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string value;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					string contextId = target.currentLocation?.GetLocationContextId();
					return Helpers.AnyArgMatches(query, 2, (string rawContextId) => string.Equals(rawContextId, contextId, StringComparison.OrdinalIgnoreCase));
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_LOCATION_NAME(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string value;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => Helpers.AnyArgMatches(query, 2, (string rawName) => string.Equals(rawName, target.currentLocation?.Name, StringComparison.OrdinalIgnoreCase)));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_LOCATION_UNIQUE_NAME(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string value;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => Helpers.AnyArgMatches(query, 2, (string rawName) => string.Equals(rawName, target.currentLocation?.NameOrUniqueName, StringComparison.OrdinalIgnoreCase)));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_MOD_DATA(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string key;
				string value;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out key, out error) || !ArgUtility.TryGet(query, 3, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				string value2;
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.modData.TryGetValue(key, out value2) && string.Equals(value2, value, StringComparison.OrdinalIgnoreCase));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_MONEY_EARNED(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				int minAmount;
				int maxAmount;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetInt(query, 2, out minAmount, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxAmount, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					uint totalMoneyEarned = target.totalMoneyEarned;
					return totalMoneyEarned >= minAmount && totalMoneyEarned <= maxAmount;
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_SHIPPED_BASIC_ITEM(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string itemId;
				int minShipped;
				int maxShipped;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out itemId, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out minShipped, out error, 1) || !ArgUtility.TryGetOptionalInt(query, 4, out maxShipped, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (ItemRegistry.IsQualifiedItemId(itemId))
				{
					ItemMetadata metadata = ItemRegistry.GetMetadata(itemId);
					if (metadata?.TypeIdentifier != "(O)")
					{
						return false;
					}
					itemId = metadata.LocalItemId;
				}
				int value;
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.basicShipped.TryGetValue(itemId, out value) && value >= minShipped && value <= maxShipped);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_SPECIAL_ORDER_ACTIVE(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string orderId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out orderId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.team.SpecialOrderActive(orderId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_SPECIAL_ORDER_RULE_ACTIVE(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string ruleId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out ruleId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.team.SpecialOrderRuleActive(ruleId));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_SPECIAL_ORDER_COMPLETE(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string orderId;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out orderId, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.team.completedSpecialOrders.Contains(orderId));
			}

			/// <summary>Check the number of monsters killed by the player. Format: <c>PLAYER_KILLED_MONSTERS &lt;player&gt; &lt;monster name&gt;+ [min] [max]</c>.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_KILLED_MONSTERS(string[] query, GameStateQueryContext context)
			{
				List<string> monsterNames = new List<string>();
				int min = 1;
				string playerKey;
				string error;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				int argIndex = 2;
				while (argIndex < query.Length)
				{
					string name = query[argIndex];
					argIndex++;
					int rawMin;
					if (int.TryParse(name, out rawMin))
					{
						min = rawMin;
						break;
					}
					monsterNames.Add(name);
				}
				int max;
				if (!ArgUtility.TryGetOptionalInt(query, argIndex, out max, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (monsterNames.Count == 0)
				{
					return Helpers.ErrorResult(query, "must specify at least one monster name to count");
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					int num = 0;
					foreach (string current in monsterNames)
					{
						num += target.stats.getMonstersKilled(current);
					}
					return num >= min && num <= max;
				});
			}

			/// <summary>Get whether the given player has a minimum value for a <see cref="P:StardewValley.Game1.stats" /> field returned by <see cref="M:StardewValley.Stats.Get(System.String)" />.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_STAT(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string statName;
				int minValue;
				int maxValue;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out statName, out error) || !ArgUtility.TryGetInt(query, 3, out minValue, out error) || !ArgUtility.TryGetOptionalInt(query, 4, out maxValue, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					uint num = target.stats.Get(statName);
					return num >= minValue && num <= maxValue;
				});
			}

			/// <summary>Get whether the given player has ever visited a location name.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_VISITED_LOCATION(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string value;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => Helpers.AnyArgMatches(query, 2, (string locationName) => target.locationsVisited.Contains(locationName)));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_FRIENDSHIP_POINTS(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string npcName;
				int minPoints;
				int maxPoints;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out npcName, out error) || !ArgUtility.TryGetInt(query, 3, out minPoints, out error) || !ArgUtility.TryGetOptionalInt(query, 4, out maxPoints, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				bool isAny = string.Equals(npcName, "Any", StringComparison.OrdinalIgnoreCase);
				bool isAnyDateable = !isAny && string.Equals(npcName, "AnyDateable", StringComparison.OrdinalIgnoreCase);
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					if (isAny)
					{
						return target.hasAFriendWithFriendshipPoints(minPoints, false, maxPoints);
					}
					if (isAnyDateable)
					{
						return target.hasAFriendWithFriendshipPoints(minPoints, true, maxPoints);
					}
					int friendshipLevelForNPC = target.getFriendshipLevelForNPC(npcName);
					return friendshipLevelForNPC >= minPoints && friendshipLevelForNPC <= maxPoints;
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_CHILDREN(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				int minCount;
				int maxCount;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGetOptionalInt(query, 2, out minCount, out error, 1) || !ArgUtility.TryGetOptionalInt(query, 3, out maxCount, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					int childrenCount = target.getChildrenCount();
					return childrenCount >= minCount && childrenCount <= maxCount;
				});
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_PET(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => target.hasPet());
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HEARTS(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string npcName;
				int minHearts;
				int maxHearts;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out npcName, out error) || !ArgUtility.TryGetInt(query, 3, out minHearts, out error) || !ArgUtility.TryGetOptionalInt(query, 4, out maxHearts, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				bool isAny = string.Equals(npcName, "Any", StringComparison.OrdinalIgnoreCase);
				bool isAnyDateable = !isAny && string.Equals(npcName, "AnyDateable", StringComparison.OrdinalIgnoreCase);
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					if (isAny)
					{
						return target.hasAFriendWithHeartLevel(minHearts, false, maxHearts);
					}
					if (isAnyDateable)
					{
						return target.hasAFriendWithHeartLevel(minHearts, true, maxHearts);
					}
					int friendshipHeartLevelForNPC = target.getFriendshipHeartLevelForNPC(npcName);
					return friendshipHeartLevelForNPC >= minHearts && friendshipHeartLevelForNPC <= maxHearts;
				});
			}

			/// <summary>Get whether a player has ever talked to an NPC. Format: <c>PLAYER_HAS_MET &lt;player&gt; &lt;npc&gt;</c>.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_HAS_MET(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string value;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => Helpers.AnyArgMatches(query, 2, (string npcName) => target.friendshipData.ContainsKey(npcName)));
			}

			/// <summary>Get a player's relationship status with an NPC. Format: <c>PLAYER_NPC_RELATIONSHIP &lt;player&gt; &lt;npc&gt; &lt;type&gt;+</c>, where the type is any combination of 'Friendly', 'Dating', 'Engaged', 'Roommate', 'Married' or 'Divorced'.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_NPC_RELATIONSHIP(string[] query, GameStateQueryContext context)
			{
				_003C_003Ec__DisplayClass86_0 CS_0024_003C_003E8__locals0 = new _003C_003Ec__DisplayClass86_0();
				CS_0024_003C_003E8__locals0.query = query;
				string playerKey;
				string error;
				if (!ArgUtility.TryGet(CS_0024_003C_003E8__locals0.query, 1, out playerKey, out error, false) || !ArgUtility.TryGet(CS_0024_003C_003E8__locals0.query, 2, out CS_0024_003C_003E8__locals0.npcName, out error, false))
				{
					return Helpers.ErrorResult(CS_0024_003C_003E8__locals0.query, error);
				}
				CS_0024_003C_003E8__locals0.relationships = new string[CS_0024_003C_003E8__locals0.query.Length - 3];
				string type;
				for (int i = 3; i < CS_0024_003C_003E8__locals0.query.Length && ArgUtility.TryGet(CS_0024_003C_003E8__locals0.query, i, out type, out error, false); i++)
				{
					type = type.ToLower();
					CS_0024_003C_003E8__locals0.relationships[i - 3] = type;
					switch (type)
					{
					case "friendly":
					case "roommate":
					case "dating":
					case "engaged":
					case "married":
					case "divorced":
						continue;
					}
					return Helpers.ErrorResult(CS_0024_003C_003E8__locals0.query, "unknown relationship type '" + type + "'; expected one of Friendly, Roommate, Dating, Engaged, Married, or Divorced");
				}
				if (CS_0024_003C_003E8__locals0.relationships.Length == 0)
				{
					return Helpers.ErrorResult(CS_0024_003C_003E8__locals0.query, ArgUtility.GetMissingRequiredIndexError(CS_0024_003C_003E8__locals0.query, 3));
				}
				CS_0024_003C_003E8__locals0.anyNpc = string.Equals(CS_0024_003C_003E8__locals0.npcName, "Any", StringComparison.OrdinalIgnoreCase);
				return Helpers.WithPlayer(context.Player, playerKey, delegate(Farmer target)
				{
					Friendship value;
					if (CS_0024_003C_003E8__locals0.anyNpc)
					{
						foreach (Friendship current in target.friendshipData.Values)
						{
							if (CS_0024_003C_003E8__locals0._003CPLAYER_NPC_RELATIONSHIP_003Eg__IsMatch_007C0(current, CS_0024_003C_003E8__locals0.relationships))
							{
								return true;
							}
						}
					}
					else if (target.friendshipData.TryGetValue(CS_0024_003C_003E8__locals0.npcName, out value) && CS_0024_003C_003E8__locals0._003CPLAYER_NPC_RELATIONSHIP_003Eg__IsMatch_007C0(value, CS_0024_003C_003E8__locals0.relationships))
					{
						return true;
					}
					return false;
				});
			}

			/// <summary>Get a player's relationship status with another player. Format: <c>PLAYER_PLAYER_RELATIONSHIP &lt;player 1&gt; &lt;player 2&gt; &lt;type&gt;+</c>, where the type is 'Engaged' or 'Married'.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_PLAYER_RELATIONSHIP(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string targetPlayerKey;
				string type;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error, false) || !ArgUtility.TryGet(query, 2, out targetPlayerKey, out error, false) || !ArgUtility.TryGet(query, 3, out type, out error, false))
				{
					return Helpers.ErrorResult(query, error);
				}
				type = type.ToLower();
				switch (type)
				{
				default:
					return Helpers.ErrorResult(query, "unknown relationship type '" + type + "'; expected one of Friendly, Engaged, or Married");
				case "friendly":
				case "engaged":
				case "married":
					return Helpers.WithPlayer(context.Player, playerKey, (Farmer fromPlayer) => Helpers.WithPlayer(context.Player, targetPlayerKey, delegate(Farmer toPlayer)
					{
						FriendshipStatus status = fromPlayer.team.GetFriendship(fromPlayer.UniqueMultiplayerID, toPlayer.UniqueMultiplayerID).Status;
						switch (type)
						{
						case "friendly":
							if (status != FriendshipStatus.Engaged)
							{
								return status != FriendshipStatus.Married;
							}
							return false;
						case "engaged":
							return status == FriendshipStatus.Engaged;
						case "married":
							return status == FriendshipStatus.Married;
						default:
							return Helpers.ErrorResult(query, "unhandled relationship type '" + type + "'");
						}
					}));
				}
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool PLAYER_PREFERRED_PET(string[] query, GameStateQueryContext context)
			{
				string playerKey;
				string error;
				string value;
				if (!ArgUtility.TryGet(query, 1, out playerKey, out error) || !ArgUtility.TryGet(query, 2, out value, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.WithPlayer(context.Player, playerKey, (Farmer target) => Helpers.AnyArgMatches(query, 2, (string rawPetId) => string.Equals(rawPetId, target.whichPetType, StringComparison.OrdinalIgnoreCase)));
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool RANDOM(string[] query, GameStateQueryContext context)
			{
				return Helpers.RandomImpl(context.Random, query, 1);
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool SYNCED_CHOICE(string[] query, GameStateQueryContext context)
			{
				string interval;
				string error;
				string key;
				int min;
				int max;
				Random syncedRandom;
				if (!ArgUtility.TryGet(query, 1, out interval, out error) || !ArgUtility.TryGet(query, 2, out key, out error) || !ArgUtility.TryGetInt(query, 3, out min, out error) || !ArgUtility.TryGetInt(query, 4, out max, out error) || !Utility.TryCreateIntervalRandom(interval, key, out syncedRandom, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				string selected = syncedRandom.Next(min, max + 1).ToString();
				for (int i = 5; i < query.Length; i++)
				{
					if (query[i] == selected)
					{
						return true;
					}
				}
				return false;
			}

			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool SYNCED_RANDOM(string[] query, GameStateQueryContext context)
			{
				string interval;
				string error;
				string key;
				Random syncedRandom;
				if (!ArgUtility.TryGet(query, 1, out interval, out error) || !ArgUtility.TryGet(query, 2, out key, out error) || !Utility.TryCreateIntervalRandom(interval, key, out syncedRandom, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return Helpers.RandomImpl(syncedRandom, query, 3);
			}

			/// <summary>A custom variant of <see cref="M:StardewValley.GameStateQuery.DefaultResolvers.SYNCED_RANDOM(System.String[],StardewValley.Delegates.GameStateQueryContext)" /> with a set key and which applies a chance boost for each day after summer starts.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool SYNCED_SUMMER_RAIN_RANDOM(string[] query, GameStateQueryContext context)
			{
				Random random = Utility.CreateDaySaveRandom(Game1.hash.GetDeterministicHashCode("summer_rain_chance"));
				float chanceToRain = 0.12f + (float)Game1.dayOfMonth * 0.003f;
				return random.NextBool(chanceToRain);
			}

			/// <summary>Get whether the target item has all of the given context tags.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_CONTEXT_TAG(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (item == null)
				{
					return false;
				}
				for (int i = 2; i < query.Length; i++)
				{
					if (!item.HasContextTag(query[i]))
					{
						return false;
					}
				}
				return true;
			}

			/// <summary>Get whether the target item has one of the given categories.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_CATEGORY(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (item != null)
				{
					if (query.Length == 2)
					{
						return item.Category < -1;
					}
					for (int i = 2; i < query.Length; i++)
					{
						int category;
						if (!ArgUtility.TryGetInt(query, i, out category, out error))
						{
							return Helpers.ErrorResult(query, error);
						}
						if (item.Category == category)
						{
							return true;
						}
					}
				}
				return false;
			}

			/// <summary>Get whether the item has an explicit category set in <c>Data/Objects</c>, ignoring categories assigned dynamically in code (e.g. for rings). These are often (but not always) special items like Secret Note or unimplemented items like Lumber. This is somewhat specialized.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_HAS_EXPLICIT_OBJECT_CATEGORY(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				return ObjectDataDefinition.HasExplicitCategory(ItemRegistry.GetData(item?.QualifiedItemId));
			}

			/// <summary>Get whether the target item has the given qualified or unqualified item ID.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_ID(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (item != null)
				{
					return Helpers.AnyArgMatches(query, 2, (string rawItemId) => string.Equals(rawItemId, item.QualifiedItemId, StringComparison.OrdinalIgnoreCase) || string.Equals(rawItemId, item.ItemId, StringComparison.OrdinalIgnoreCase));
				}
				return false;
			}

			/// <summary>Get whether the target item's qualified or unqualified item ID starts with the given prefix.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_ID_PREFIX(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (item != null)
				{
					return Helpers.AnyArgMatches(query, 2, (string prefix) => item.ItemId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || item.QualifiedItemId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
				}
				return false;
			}

			/// <summary>Get whether the target item has a numeric item ID within the given range.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_NUMERIC_ID(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				int minId;
				int maxId;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error) || !ArgUtility.TryGetInt(query, 2, out minId, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxId, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				int id;
				if (int.TryParse(item?.ItemId, out id) && id >= minId)
				{
					return id <= maxId;
				}
				return false;
			}

			/// <summary>Get whether the target item has one of the given object types.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_OBJECT_TYPE(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				Object obj = item as Object;
				if (obj != null)
				{
					return Helpers.AnyArgMatches(query, 2, (string rawObjType) => string.Equals(rawObjType, obj.Type, StringComparison.OrdinalIgnoreCase));
				}
				return false;
			}

			/// <summary>Get whether the target item has a price within the given range.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_PRICE(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				int minPrice;
				int maxPrice;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error) || !ArgUtility.TryGetInt(query, 2, out minPrice, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxPrice, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				int? price = item?.salePrice();
				if (price >= minPrice)
				{
					return price <= maxPrice;
				}
				return false;
			}

			/// <summary>Get whether the target item has a min quality level.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_QUALITY(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				int minQuality;
				int maxQuality;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error) || !ArgUtility.TryGetInt(query, 2, out minQuality, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxQuality, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				int? quality = item?.Quality;
				if (quality >= minQuality)
				{
					return quality <= maxQuality;
				}
				return false;
			}

			/// <summary>Get whether the target item has a min stack size (ignoring other stacks in the inventory).</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_STACK(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				int minStack;
				int maxStack;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error) || !ArgUtility.TryGetInt(query, 2, out minStack, out error) || !ArgUtility.TryGetOptionalInt(query, 3, out maxStack, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				int? stack = item?.Stack;
				if (stack >= minStack)
				{
					return stack <= maxStack;
				}
				return false;
			}

			/// <summary>Get whether the target item has the given item type.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_TYPE(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error))
				{
					return Helpers.ErrorResult(query, error);
				}
				if (item != null)
				{
					return Helpers.AnyArgMatches(query, 2, (string rawItemType) => string.Equals(rawItemType, item.TypeDefinitionId, StringComparison.OrdinalIgnoreCase));
				}
				return false;
			}

			/// <summary>Get whether the target item has a min edibility level.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool ITEM_EDIBILITY(string[] query, GameStateQueryContext context)
			{
				Item item;
				string error;
				int minEdibility;
				int maxEdibility;
				if (!Helpers.TryGetItemArg(query, 1, context.TargetItem, context.InputItem, out item, out error) || !ArgUtility.TryGetOptionalInt(query, 2, out minEdibility, out error, -299) || !ArgUtility.TryGetOptionalInt(query, 3, out maxEdibility, out error, int.MaxValue))
				{
					return Helpers.ErrorResult(query, error);
				}
				Object obj = item as Object;
				if (obj != null && obj.Edibility >= minEdibility)
				{
					return obj.Edibility <= maxEdibility;
				}
				return false;
			}

			/// <summary>A condition that always passes.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool TRUE(string[] query, GameStateQueryContext context)
			{
				return true;
			}

			/// <summary>A condition that always fails.</summary>
			/// <inheritdoc cref="T:StardewValley.Delegates.GameStateQueryDelegate" />
			public static bool FALSE(string[] query, GameStateQueryContext context)
			{
				return false;
			}
		}

		/// <summary>The supported game state queries and their resolvers.</summary>
		private static readonly Dictionary<string, GameStateQueryDelegate> QueryTypeLookup;

		/// <summary>Alternate names for game state queries (e.g. shorthand or acronyms).</summary>
		private static readonly Dictionary<string, string> Aliases;

		/// <summary>The <see cref="F:StardewValley.Game1.ticks" /> value when the cache should be reset.</summary>
		private static int NextClearCacheTick;

		/// <summary>The cache of parsed game state queries.</summary>
		private static readonly Dictionary<string, ParsedGameStateQuery[]> ParseCache;

		/// <summary>The query keys which check the season, like <c>LOCATION_SEASON</c> or <c>SEASON</c>.</summary>
		public static HashSet<string> SeasonQueryKeys;

		/// <summary>The query keys which are ignored when catching fish with the Magic Bait equipped.</summary>
		public static HashSet<string> MagicBaitIgnoreQueryKeys;

		/// <summary>Register the default game state queries, defined as <see cref="T:StardewValley.GameStateQuery.DefaultResolvers" /> methods.</summary>
		static GameStateQuery()
		{
			QueryTypeLookup = new Dictionary<string, GameStateQueryDelegate>(StringComparer.OrdinalIgnoreCase);
			Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			ParseCache = new Dictionary<string, ParsedGameStateQuery[]>();
			SeasonQueryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LOCATION_SEASON", "SEASON" };
			MagicBaitIgnoreQueryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DAY_OF_MONTH", "DAY_OF_WEEK", "DAYS_PLAYED", "LOCATION_SEASON", "SEASON", "SEASON_DAY", "WEATHER", "TIME" };
			MethodInfo[] methods = typeof(DefaultResolvers).GetMethods(BindingFlags.Static | BindingFlags.Public);
			MethodInfo[] array = methods;
			foreach (MethodInfo method in array)
			{
				GameStateQueryDelegate queryDelegate = (GameStateQueryDelegate)Delegate.CreateDelegate(typeof(GameStateQueryDelegate), method);
				Register(method.Name, queryDelegate);
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
						RegisterAlias(aliases[j], method2.Name);
					}
				}
			}
		}

		/// <summary>Update the game state query tracking.</summary>
		internal static void Update()
		{
			if (Game1.ticks >= NextClearCacheTick)
			{
				if (ParseCache.Count > 50)
				{
					ParseCache.Clear();
				}
				NextClearCacheTick = Game1.ticks + 3600;
			}
		}

		/// <summary>Get whether a game state query exists.</summary>
		/// <param name="queryKey">The game state query key, like <c>SEASON</c>.</param>
		public static bool Exists(string queryKey)
		{
			if (queryKey == null)
			{
				return false;
			}
			if (!QueryTypeLookup.ContainsKey(queryKey))
			{
				return Aliases.ContainsKey(queryKey);
			}
			return true;
		}

		/// <summary>Register a game state query resolver.</summary>
		/// <param name="queryKey">The game state query key, like <c>SEASON</c>. This should only contain alphanumeric, underscore, and dot characters. For custom queries, this should be prefixed with your mod ID like <c>Example.ModId_QueryName</c>.</param>
		/// <param name="queryDelegate">The resolver which returns whether a given query matches in the current context.</param>
		/// <exception cref="T:System.ArgumentException">The <paramref name="queryKey" /> is null or whitespace-only.</exception>
		/// <exception cref="T:System.ArgumentNullException">The <paramref name="queryDelegate" /> is null.</exception>
		/// <exception cref="T:System.InvalidOperationException">The <paramref name="queryKey" /> is already registered.</exception>
		public static void Register(string queryKey, GameStateQueryDelegate queryDelegate)
		{
			queryKey = queryKey?.Trim();
			if (string.IsNullOrWhiteSpace(queryKey))
			{
				throw new ArgumentException("The query key can't be null or empty.", "queryKey");
			}
			if (QueryTypeLookup.ContainsKey(queryKey))
			{
				throw new InvalidOperationException("The query key '" + queryKey + "' is already registered.");
			}
			string aliasFor;
			if (Aliases.TryGetValue(queryKey, out aliasFor))
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(57, 2);
				defaultInterpolatedStringHandler.AppendLiteral("The query key '");
				defaultInterpolatedStringHandler.AppendFormatted(queryKey);
				defaultInterpolatedStringHandler.AppendLiteral("' is already registered as an alias of '");
				defaultInterpolatedStringHandler.AppendFormatted(aliasFor);
				defaultInterpolatedStringHandler.AppendLiteral("'.");
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			Dictionary<string, GameStateQueryDelegate> queryTypeLookup = QueryTypeLookup;
			string key = queryKey;
			if (queryDelegate == null)
			{
				throw new ArgumentNullException("queryDelegate");
			}
			queryTypeLookup[key] = queryDelegate;
		}

		/// <summary>Register an alternate name for a game state query.</summary>
		/// <param name="alias">The alias to register. This should only contain alphanumeric, underscore, and dot characters. For custom queries, this should be prefixed with your mod ID like <c>Example.ModId_QueryName</c>.</param>
		/// <param name="queryKey">The game state query key to map it to, like <c>SEASON</c>. This should already be registered (e.g. via <see cref="M:StardewValley.GameStateQuery.Register(System.String,StardewValley.Delegates.GameStateQueryDelegate)" />).</param>
		/// <exception cref="T:System.ArgumentException">The <paramref name="alias" /> or <paramref name="queryKey" /> is null or whitespace-only.</exception>
		/// <exception cref="T:System.InvalidOperationException">The <paramref name="queryKey" /> is already registered.</exception>
		public static void RegisterAlias(string alias, string queryKey)
		{
			alias = alias?.Trim();
			if (string.IsNullOrWhiteSpace(alias))
			{
				throw new ArgumentException("The alias can't be null or empty.", "alias");
			}
			if (QueryTypeLookup.ContainsKey(alias))
			{
				throw new InvalidOperationException("The alias '" + alias + "' is already registered as a game state query.");
			}
			string otherQuery;
			if (Aliases.TryGetValue(alias, out otherQuery))
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(42, 2);
				defaultInterpolatedStringHandler.AppendLiteral("The alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' is already registered for '");
				defaultInterpolatedStringHandler.AppendFormatted(otherQuery);
				defaultInterpolatedStringHandler.AppendLiteral("'.");
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			if (string.IsNullOrWhiteSpace(queryKey))
			{
				throw new ArgumentException("The query key can't be null or empty.", "alias");
			}
			if (!QueryTypeLookup.ContainsKey(queryKey))
			{
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(91, 2);
				defaultInterpolatedStringHandler.AppendLiteral("The alias '");
				defaultInterpolatedStringHandler.AppendFormatted(alias);
				defaultInterpolatedStringHandler.AppendLiteral("' can't be registered for '");
				defaultInterpolatedStringHandler.AppendFormatted(queryKey);
				defaultInterpolatedStringHandler.AppendLiteral("' because there's no game state query with that name.");
				throw new InvalidOperationException(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			Aliases[alias] = queryKey;
		}

		/// <summary>Get whether a set of game state queries matches in the current context.</summary>
		/// <param name="queryString">The game state queries to check as a comma-delimited string.</param>
		/// <param name="location">The location for which to check the query, or <c>null</c> to use the current location.</param>
		/// <param name="player">The player for which to check the query, or <c>null</c> to use the current player.</param>
		/// <param name="targetItem">The target item (e.g. machine output or tree fruit) for which to check queries, or <c>null</c> if not applicable.</param>
		/// <param name="inputItem">The input item (e.g. machine input) for which to check queries, or <c>null</c> if not applicable.</param>
		/// <param name="random">The RNG to use for randomization, or <c>null</c> to use <see cref="F:StardewValley.Game1.random" />.</param>
		/// <param name="ignoreQueryKeys">The query keys to ignore when checking conditions (like <c>LOCATION_SEASON</c>), or <c>null</c> to check all of them.</param>
		/// <returns>Returns whether the query matches.</returns>
		public static bool CheckConditions(string queryString, GameLocation location = null, Farmer player = null, Item targetItem = null, Item inputItem = null, Random random = null, HashSet<string> ignoreQueryKeys = null)
		{
			if (queryString != null && (queryString == null || queryString.Length != 0) && !(queryString == "TRUE"))
			{
				if (queryString == "FALSE")
				{
					return false;
				}
				GameStateQueryContext context = new GameStateQueryContext(location, player, targetItem, inputItem, random, ignoreQueryKeys);
				return CheckConditionsImpl(queryString, context);
			}
			return true;
		}

		/// <summary>Get whether a set of game state queries matches in the current context.</summary>
		/// <param name="queryString">The game state queries to check as a comma-delimited string.</param>
		/// <param name="context">The game state query context.</param>
		/// <returns>Returns whether the query matches.</returns>
		public static bool CheckConditions(string queryString, GameStateQueryContext context)
		{
			if (queryString != null && (queryString == null || queryString.Length != 0) && !(queryString == "TRUE"))
			{
				if (queryString == "FALSE")
				{
					return false;
				}
				return CheckConditionsImpl(queryString, context);
			}
			return true;
		}

		/// <summary>Get whether a game state query can never be true under any circumstance (e.g. <c>FALSE</c> or <c>!TRUE</c>).</summary>
		/// <param name="queryString">The game state queries to check as a comma-delimited string.</param>
		public static bool IsImmutablyFalse(string queryString)
		{
			if (queryString != null && (queryString == null || queryString.Length != 0) && !(queryString == "TRUE"))
			{
				if (queryString == "FALSE")
				{
					return true;
				}
				ParsedGameStateQuery[] array = Parse(queryString);
				for (int i = 0; i < array.Length; i++)
				{
					ParsedGameStateQuery query = array[i];
					if (query.Query.Length != 0)
					{
						string immutableFalseName = (query.Negated ? "TRUE" : "FALSE");
						if (string.Equals(query.Query[0], immutableFalseName, StringComparison.OrdinalIgnoreCase))
						{
							return true;
						}
					}
				}
				return false;
			}
			return false;
		}

		/// <summary>Get whether a game state query can never be false under any circumstance (e.g. <c>TRUE</c>, <c>!FALSE</c>, or empty).</summary>
		/// <param name="queryString">The game state queries to check as a comma-delimited string.</param>
		public static bool IsImmutablyTrue(string queryString)
		{
			if (queryString != null && (queryString == null || queryString.Length != 0) && !(queryString == "TRUE"))
			{
				if (queryString == "FALSE")
				{
					return false;
				}
				ParsedGameStateQuery[] array = Parse(queryString);
				for (int i = 0; i < array.Length; i++)
				{
					ParsedGameStateQuery query = array[i];
					if (query.Query.Length != 0)
					{
						string immutableTrueName = (query.Negated ? "FALSE" : "TRUE");
						if (!string.Equals(query.Query[0], immutableTrueName, StringComparison.OrdinalIgnoreCase))
						{
							return false;
						}
					}
				}
				return true;
			}
			return true;
		}

		/// <summary>Parse a raw query string into its component query data.</summary>
		/// <param name="queryString">The query string to parse.</param>
		/// <returns>Returns the parsed game state queries. This value is cached, so it should not be modified. If any part of the query string is invalid, this returns a single value containing the invalid query with the error property set.</returns>
		public static ParsedGameStateQuery[] Parse(string queryString)
		{
			ParsedGameStateQuery[] parsed;
			if (!ParseCache.TryGetValue(queryString, out parsed))
			{
				string[] rawQueries = SplitRaw(queryString);
				parsed = new ParsedGameStateQuery[rawQueries.Length];
				for (int i = 0; i < rawQueries.Length; i++)
				{
					string[] query = ArgUtility.SplitBySpaceQuoteAware(rawQueries[i]);
					string key = query[0];
					bool negated = key.StartsWith('!');
					if (negated)
					{
						key = (query[0] = key.Substring(1));
					}
					string aliasFor;
					if (Aliases.TryGetValue(key, out aliasFor))
					{
						key = aliasFor;
						query[0] = aliasFor;
					}
					GameStateQueryDelegate resolver;
					if (!QueryTypeLookup.TryGetValue(key, out resolver))
					{
						if (parsed.Length > 1)
						{
							parsed = new ParsedGameStateQuery[1];
						}
						parsed[0] = new ParsedGameStateQuery(false, query, null, "'" + key + "' isn't a known query or alias");
						break;
					}
					parsed[i] = new ParsedGameStateQuery(negated, query, resolver, null);
				}
				ParseCache[queryString] = parsed;
			}
			return parsed;
		}

		/// <summary>Split a query string into its top-level component queries without parsing them.</summary>
		/// <param name="queryString">The query string to split.</param>
		public static string[] SplitRaw(string queryString)
		{
			return ArgUtility.SplitQuoteAware(queryString, ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries, true);
		}

		/// <summary>Get whether a set of game state queries matches in the current context without short-circuiting immutable values like <c>TRUE</c>.</summary>
		/// <param name="queryString">The game state queries to check as a comma-delimited string.</param>
		/// <param name="context">The game state query context.</param>
		/// <returns>Returns whether the query matches.</returns>
		private static bool CheckConditionsImpl(string queryString, GameStateQueryContext context)
		{
			if (queryString == null)
			{
				return true;
			}
			ParsedGameStateQuery[] parsed = Parse(queryString);
			if (parsed.Length == 0)
			{
				return true;
			}
			if (parsed[0].Error != null)
			{
				return Helpers.ErrorResult(parsed[0].Query, parsed[0].Error);
			}
			ParsedGameStateQuery[] array = parsed;
			for (int i = 0; i < array.Length; i++)
			{
				ParsedGameStateQuery query = array[i];
				HashSet<string> ignoreQueryKeys = context.IgnoreQueryKeys;
				if (ignoreQueryKeys != null && ignoreQueryKeys.Contains(query.Query[0]))
				{
					continue;
				}
				try
				{
					if (query.Resolver(query.Query, context) == query.Negated)
					{
						return false;
					}
				}
				catch (Exception e)
				{
					return Helpers.ErrorResult(query.Query, "unhandled exception", e);
				}
			}
			return true;
		}
	}
}
