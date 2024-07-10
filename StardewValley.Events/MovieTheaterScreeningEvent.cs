using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using StardewValley.Extensions;
using StardewValley.GameData.Characters;
using StardewValley.GameData.Movies;
using StardewValley.Locations;
using StardewValley.TokenizableStrings;

namespace StardewValley.Events
{
	/// <summary>Generates the event that plays when watching a movie at the <see cref="T:StardewValley.Locations.MovieTheater" />.</summary>
	public class MovieTheaterScreeningEvent
	{
		public int currentResponse;

		public List<List<Character>> playerAndGuestAudienceGroups;

		public Dictionary<int, Character> _responseOrder = new Dictionary<int, Character>();

		protected Dictionary<Character, Character> _whiteListDependencyLookup;

		protected Dictionary<Character, string> _characterResponses;

		public MovieData movieData;

		protected List<Farmer> _farmers;

		protected Dictionary<Character, MovieConcession> _concessionsData;

		public Event getMovieEvent(string movieId, List<List<Character>> player_and_guest_audience_groups, List<List<Character>> npcOnlyAudienceGroups, Dictionary<Character, MovieConcession> concessions_data = null)
		{
			_concessionsData = concessions_data;
			_responseOrder = new Dictionary<int, Character>();
			_whiteListDependencyLookup = new Dictionary<Character, Character>();
			_characterResponses = new Dictionary<Character, string>();
			movieData = MovieTheater.GetMovieDataById()[movieId];
			playerAndGuestAudienceGroups = player_and_guest_audience_groups;
			currentResponse = 0;
			StringBuilder sb = new StringBuilder();
			Random theaterRandom = Utility.CreateDaySaveRandom();
			sb.Append("movieScreenAmbience/-2000 -2000/");
			string playerCharacterEventName = "farmer" + Utility.getFarmerNumberFromFarmer(Game1.player);
			string playerCharacterGuestName = "";
			bool hasPlayerGuest = false;
			foreach (List<Character> list in playerAndGuestAudienceGroups)
			{
				if (!list.Contains(Game1.player))
				{
					continue;
				}
				for (int i12 = 0; i12 < list.Count; i12++)
				{
					if (!(list[i12] is Farmer))
					{
						playerCharacterGuestName = list[i12].name;
						hasPlayerGuest = true;
						break;
					}
				}
			}
			_farmers = new List<Farmer>();
			foreach (List<Character> playerAndGuestAudienceGroup in playerAndGuestAudienceGroups)
			{
				foreach (Character item in playerAndGuestAudienceGroup)
				{
					Farmer player = item as Farmer;
					if (player != null && !_farmers.Contains(player))
					{
						_farmers.Add(player);
					}
				}
			}
			List<Character> allAudience = playerAndGuestAudienceGroups.SelectMany((List<Character> x) => x).ToList();
			if (allAudience.Count <= 12)
			{
				allAudience.AddRange(npcOnlyAudienceGroups.SelectMany((List<Character> x) => x).ToList());
			}
			bool first = true;
			foreach (Character c2 in allAudience)
			{
				if (c2 != null)
				{
					if (!first)
					{
						sb.Append(" ");
					}
					Farmer f = c2 as Farmer;
					if (f != null)
					{
						sb.Append("farmer" + Utility.getFarmerNumberFromFarmer(f));
					}
					else
					{
						sb.Append(c2.name);
					}
					sb.Append(" -1000 -1000 0");
					first = false;
				}
			}
			sb.Append("/changeToTemporaryMap MovieTheaterScreen false/specificTemporarySprite movieTheater_setup/ambientLight 0 0 0/");
			string[] backRow = new string[8];
			string[] midRow = new string[6];
			string[] frontRow = new string[4];
			playerAndGuestAudienceGroups = playerAndGuestAudienceGroups.OrderBy((List<Character> x) => theaterRandom.Next()).ToList();
			int startingSeat = theaterRandom.Next(8 - Math.Min(playerAndGuestAudienceGroups.SelectMany((List<Character> x) => x).Count(), 8) + 1);
			int whichGroup = 0;
			if (playerAndGuestAudienceGroups.Count > 0)
			{
				for (int i13 = 0; i13 < 8; i13++)
				{
					int seat13 = (i13 + startingSeat) % 8;
					if (playerAndGuestAudienceGroups[whichGroup].Count == 2 && (seat13 == 3 || seat13 == 7))
					{
						i13++;
						seat13++;
						seat13 %= 8;
					}
					for (int j7 = 0; j7 < playerAndGuestAudienceGroups[whichGroup].Count && seat13 + j7 < backRow.Length; j7++)
					{
						backRow[seat13 + j7] = ((playerAndGuestAudienceGroups[whichGroup][j7] is Farmer) ? ("farmer" + Utility.getFarmerNumberFromFarmer(playerAndGuestAudienceGroups[whichGroup][j7] as Farmer)) : ((string)playerAndGuestAudienceGroups[whichGroup][j7].name));
						if (j7 > 0)
						{
							i13++;
						}
					}
					whichGroup++;
					if (whichGroup >= playerAndGuestAudienceGroups.Count)
					{
						break;
					}
				}
			}
			else
			{
				Game1.log.Warn("The movie audience somehow has no players. This is likely a bug.");
			}
			bool usedMidRow = false;
			if (whichGroup < playerAndGuestAudienceGroups.Count)
			{
				startingSeat = 0;
				for (int i11 = 0; i11 < 4; i11++)
				{
					int seat12 = (i11 + startingSeat) % 4;
					for (int j6 = 0; j6 < playerAndGuestAudienceGroups[whichGroup].Count && seat12 + j6 < frontRow.Length; j6++)
					{
						frontRow[seat12 + j6] = ((playerAndGuestAudienceGroups[whichGroup][j6] is Farmer) ? ("farmer" + Utility.getFarmerNumberFromFarmer(playerAndGuestAudienceGroups[whichGroup][j6] as Farmer)) : ((string)playerAndGuestAudienceGroups[whichGroup][j6].name));
						if (j6 > 0)
						{
							i11++;
						}
					}
					whichGroup++;
					if (whichGroup >= playerAndGuestAudienceGroups.Count)
					{
						break;
					}
				}
				if (whichGroup < playerAndGuestAudienceGroups.Count)
				{
					usedMidRow = true;
					startingSeat = 0;
					for (int i10 = 0; i10 < 6; i10++)
					{
						int seat11 = (i10 + startingSeat) % 6;
						if (playerAndGuestAudienceGroups[whichGroup].Count == 2 && seat11 == 2)
						{
							i10++;
							seat11++;
							seat11 %= 8;
						}
						for (int j5 = 0; j5 < playerAndGuestAudienceGroups[whichGroup].Count && seat11 + j5 < midRow.Length; j5++)
						{
							midRow[seat11 + j5] = ((playerAndGuestAudienceGroups[whichGroup][j5] is Farmer) ? ("farmer" + Utility.getFarmerNumberFromFarmer(playerAndGuestAudienceGroups[whichGroup][j5] as Farmer)) : ((string)playerAndGuestAudienceGroups[whichGroup][j5].name));
							if (j5 > 0)
							{
								i10++;
							}
						}
						whichGroup++;
						if (whichGroup >= playerAndGuestAudienceGroups.Count)
						{
							break;
						}
					}
				}
			}
			if (!usedMidRow)
			{
				for (int j4 = 0; j4 < npcOnlyAudienceGroups.Count; j4++)
				{
					int seat10 = theaterRandom.Next(3 - npcOnlyAudienceGroups[j4].Count + 1) + j4 * 3;
					for (int i9 = 0; i9 < npcOnlyAudienceGroups[j4].Count; i9++)
					{
						midRow[seat10 + i9] = npcOnlyAudienceGroups[j4][i9].name;
					}
				}
			}
			int soFar = 0;
			int sittingTogetherCount = 0;
			for (int i8 = 0; i8 < backRow.Length; i8++)
			{
				if (backRow[i8] == null || !(backRow[i8] != "") || !(backRow[i8] != playerCharacterEventName) || !(backRow[i8] != playerCharacterGuestName))
				{
					continue;
				}
				soFar++;
				if (soFar < 2)
				{
					continue;
				}
				sittingTogetherCount++;
				Point seat = getBackRowSeatTileFromIndex(i8);
				sb.Append("warp ").Append(backRow[i8]).Append(" ")
					.Append(seat.X)
					.Append(" ")
					.Append(seat.Y)
					.Append("/positionOffset ")
					.Append(backRow[i8])
					.Append(" 0 -10/");
				if (sittingTogetherCount == 2)
				{
					sittingTogetherCount = 0;
					if (theaterRandom.NextBool() && backRow[i8] != playerCharacterGuestName && backRow[i8 - 1] != playerCharacterGuestName && backRow[i8 - 1] != null)
					{
						sb.Append("faceDirection " + backRow[i8] + " 3 true/");
						sb.Append("faceDirection " + backRow[i8 - 1] + " 1 true/");
					}
				}
			}
			soFar = 0;
			sittingTogetherCount = 0;
			for (int i7 = 0; i7 < midRow.Length; i7++)
			{
				if (midRow[i7] == null || !(midRow[i7] != "") || !(midRow[i7] != playerCharacterEventName) || !(midRow[i7] != playerCharacterGuestName))
				{
					continue;
				}
				soFar++;
				if (soFar < 2)
				{
					continue;
				}
				sittingTogetherCount++;
				Point seat2 = getMidRowSeatTileFromIndex(i7);
				sb.Append("warp ").Append(midRow[i7]).Append(" ")
					.Append(seat2.X)
					.Append(" ")
					.Append(seat2.Y)
					.Append("/positionOffset ")
					.Append(midRow[i7])
					.Append(" 0 -10/");
				if (sittingTogetherCount == 2)
				{
					sittingTogetherCount = 0;
					if (i7 != 3 && theaterRandom.NextBool() && midRow[i7 - 1] != null)
					{
						sb.Append("faceDirection " + midRow[i7] + " 3 true/");
						sb.Append("faceDirection " + midRow[i7 - 1] + " 1 true/");
					}
				}
			}
			soFar = 0;
			sittingTogetherCount = 0;
			for (int i6 = 0; i6 < frontRow.Length; i6++)
			{
				if (frontRow[i6] == null || !(frontRow[i6] != "") || !(frontRow[i6] != playerCharacterEventName) || !(frontRow[i6] != playerCharacterGuestName))
				{
					continue;
				}
				soFar++;
				if (soFar < 2)
				{
					continue;
				}
				sittingTogetherCount++;
				Point seat3 = getFrontRowSeatTileFromIndex(i6);
				sb.Append("warp ").Append(frontRow[i6]).Append(" ")
					.Append(seat3.X)
					.Append(" ")
					.Append(seat3.Y)
					.Append("/positionOffset ")
					.Append(frontRow[i6])
					.Append(" 0 -10/");
				if (sittingTogetherCount == 2)
				{
					sittingTogetherCount = 0;
					if (theaterRandom.NextBool() && frontRow[i6 - 1] != null)
					{
						sb.Append("faceDirection " + frontRow[i6] + " 3 true/");
						sb.Append("faceDirection " + frontRow[i6 - 1] + " 1 true/");
					}
				}
			}
			Point warpPoint = new Point(1, 15);
			soFar = 0;
			for (int i5 = 0; i5 < backRow.Length; i5++)
			{
				if (backRow[i5] != null && backRow[i5] != "" && backRow[i5] != playerCharacterEventName && backRow[i5] != playerCharacterGuestName)
				{
					Point seat9 = getBackRowSeatTileFromIndex(i5);
					if (soFar == 1)
					{
						sb.Append("warp ").Append(backRow[i5]).Append(" ")
							.Append(seat9.X - 1)
							.Append(" 10")
							.Append("/advancedMove ")
							.Append(backRow[i5])
							.Append(" false 1 " + 200 + " 1 0 4 1000/")
							.Append("positionOffset ")
							.Append(backRow[i5])
							.Append(" 0 -10/");
					}
					else
					{
						sb.Append("warp ").Append(backRow[i5]).Append(" 1 12")
							.Append("/advancedMove ")
							.Append(backRow[i5])
							.Append(" false 1 200 ")
							.Append("0 -2 ")
							.Append(seat9.X - 1)
							.Append(" 0 4 1000/")
							.Append("positionOffset ")
							.Append(backRow[i5])
							.Append(" 0 -10/");
					}
					soFar++;
				}
				if (soFar >= 2)
				{
					break;
				}
			}
			soFar = 0;
			for (int i4 = 0; i4 < midRow.Length; i4++)
			{
				if (midRow[i4] != null && midRow[i4] != "" && midRow[i4] != playerCharacterEventName && midRow[i4] != playerCharacterGuestName)
				{
					Point seat8 = getMidRowSeatTileFromIndex(i4);
					if (soFar == 1)
					{
						sb.Append("warp ").Append(midRow[i4]).Append(" ")
							.Append(seat8.X - 1)
							.Append(" 8")
							.Append("/advancedMove ")
							.Append(midRow[i4])
							.Append(" false 1 " + 400 + " 1 0 4 1000/");
					}
					else
					{
						sb.Append("warp ").Append(midRow[i4]).Append(" 2 9")
							.Append("/advancedMove ")
							.Append(midRow[i4])
							.Append(" false 1 300 ")
							.Append("0 -1 ")
							.Append(seat8.X - 2)
							.Append(" 0 4 1000/");
					}
					soFar++;
				}
				if (soFar >= 2)
				{
					break;
				}
			}
			soFar = 0;
			for (int i3 = 0; i3 < frontRow.Length; i3++)
			{
				if (frontRow[i3] != null && frontRow[i3] != "" && frontRow[i3] != playerCharacterEventName && frontRow[i3] != playerCharacterGuestName)
				{
					Point seat7 = getFrontRowSeatTileFromIndex(i3);
					if (soFar == 1)
					{
						sb.Append("warp ").Append(frontRow[i3]).Append(" ")
							.Append(seat7.X - 1)
							.Append(" 6")
							.Append("/advancedMove ")
							.Append(frontRow[i3])
							.Append(" false 1 " + 400 + " 1 0 4 1000/");
					}
					else
					{
						sb.Append("warp ").Append(frontRow[i3]).Append(" 3 7")
							.Append("/advancedMove ")
							.Append(frontRow[i3])
							.Append(" false 1 300 ")
							.Append("0 -1 ")
							.Append(seat7.X - 3)
							.Append(" 0 4 1000/");
					}
					soFar++;
				}
				if (soFar >= 2)
				{
					break;
				}
			}
			sb.Append("viewport 6 8 true/pause 500/");
			for (int i2 = 0; i2 < backRow.Length; i2++)
			{
				if (backRow[i2] != null && backRow[i2] != "")
				{
					Point seat4 = getBackRowSeatTileFromIndex(i2);
					if (backRow[i2] == playerCharacterEventName || backRow[i2] == playerCharacterGuestName)
					{
						sb.Append("warp ").Append(backRow[i2]).Append(" ")
							.Append(warpPoint.X)
							.Append(" ")
							.Append(warpPoint.Y)
							.Append("/advancedMove ")
							.Append(backRow[i2])
							.Append(" false 0 -5 ")
							.Append(seat4.X - warpPoint.X)
							.Append(" 0 4 1000/")
							.Append("pause ")
							.Append(1000)
							.Append("/");
					}
				}
			}
			for (int n = 0; n < midRow.Length; n++)
			{
				if (midRow[n] != null && midRow[n] != "")
				{
					Point seat5 = getMidRowSeatTileFromIndex(n);
					if (midRow[n] == playerCharacterEventName || midRow[n] == playerCharacterGuestName)
					{
						sb.Append("warp ").Append(midRow[n]).Append(" ")
							.Append(warpPoint.X)
							.Append(" ")
							.Append(warpPoint.Y)
							.Append("/advancedMove ")
							.Append(midRow[n])
							.Append(" false 0 -7 ")
							.Append(seat5.X - warpPoint.X)
							.Append(" 0 4 1000/")
							.Append("pause ")
							.Append(1000)
							.Append("/");
					}
				}
			}
			for (int m = 0; m < frontRow.Length; m++)
			{
				if (frontRow[m] != null && frontRow[m] != "")
				{
					Point seat6 = getFrontRowSeatTileFromIndex(m);
					if (frontRow[m] == playerCharacterEventName || frontRow[m] == playerCharacterGuestName)
					{
						sb.Append("warp ").Append(frontRow[m]).Append(" ")
							.Append(warpPoint.X)
							.Append(" ")
							.Append(warpPoint.Y)
							.Append("/advancedMove ")
							.Append(frontRow[m])
							.Append(" false 0 -7 1 0 0 -1 1 0 0 -1 ")
							.Append(seat6.X - 3)
							.Append(" 0 4 1000/")
							.Append("pause ")
							.Append(1000)
							.Append("/");
					}
				}
			}
			sb.Append("pause 3000");
			if (hasPlayerGuest)
			{
				sb.Append("/proceedPosition ").Append(playerCharacterGuestName);
			}
			sb.Append("/pause 1000");
			if (!hasPlayerGuest)
			{
				sb.Append("/proceedPosition farmer");
			}
			sb.Append("/waitForAllStationary/pause 100");
			foreach (Character c in allAudience)
			{
				string actorName = getEventName(c);
				if (actorName != playerCharacterEventName && actorName != playerCharacterGuestName)
				{
					if (c is Farmer)
					{
						sb.Append("/faceDirection ").Append(actorName).Append(" 0 true/positionOffset ")
							.Append(actorName)
							.Append(" 0 42 true");
					}
					else
					{
						sb.Append("/faceDirection ").Append(actorName).Append(" 0 true/positionOffset ")
							.Append(actorName)
							.Append(" 0 12 true");
					}
					if (theaterRandom.NextDouble() < 0.2)
					{
						sb.Append("/pause 100");
					}
				}
			}
			sb.Append("/positionOffset ").Append(playerCharacterEventName).Append(" 0 32");
			if (hasPlayerGuest)
			{
				sb.Append("/positionOffset ").Append(playerCharacterGuestName).Append(" 0 8");
			}
			sb.Append("/ambientLight 210 210 120 true/pause 500/viewport move 0 -1 4000/pause 5000");
			List<Character> responding_characters = new List<Character>();
			foreach (List<Character> playerAndGuestAudienceGroup2 in playerAndGuestAudienceGroups)
			{
				foreach (Character character2 in playerAndGuestAudienceGroup2)
				{
					if (!(character2 is Farmer) && !responding_characters.Contains(character2))
					{
						responding_characters.Add(character2);
					}
				}
			}
			for (int l = 0; l < responding_characters.Count; l++)
			{
				int index = theaterRandom.Next(responding_characters.Count);
				Character character = responding_characters[l];
				responding_characters[l] = responding_characters[index];
				responding_characters[index] = character;
			}
			int current_response_index = 0;
			foreach (MovieScene scene2 in movieData.Scenes)
			{
				if (scene2.ResponsePoint == null)
				{
					continue;
				}
				bool found_reaction = false;
				for (int k = 0; k < responding_characters.Count; k++)
				{
					MovieCharacterReaction reaction2 = MovieTheater.GetReactionsForCharacter(responding_characters[k] as NPC);
					if (reaction2 == null)
					{
						continue;
					}
					foreach (MovieReaction movie_reaction2 in reaction2.Reactions)
					{
						if (!movie_reaction2.ShouldApplyToMovie(movieData, MovieTheater.GetPatronNames(), MovieTheater.GetResponseForMovie(responding_characters[k] as NPC)) || movie_reaction2.SpecialResponses?.DuringMovie == null || (!(movie_reaction2.SpecialResponses.DuringMovie.ResponsePoint == scene2.ResponsePoint) && movie_reaction2.Whitelist.Count <= 0))
						{
							continue;
						}
						if (!_whiteListDependencyLookup.ContainsKey(responding_characters[k]))
						{
							_responseOrder[current_response_index] = responding_characters[k];
							if (movie_reaction2.Whitelist != null)
							{
								for (int j3 = 0; j3 < movie_reaction2.Whitelist.Count; j3++)
								{
									Character white_list_character2 = Game1.getCharacterFromName(movie_reaction2.Whitelist[j3]);
									if (white_list_character2 == null)
									{
										continue;
									}
									_whiteListDependencyLookup[white_list_character2] = responding_characters[k];
									foreach (int key2 in _responseOrder.Keys)
									{
										if (_responseOrder[key2] == white_list_character2)
										{
											_responseOrder.Remove(key2);
										}
									}
								}
							}
						}
						responding_characters.RemoveAt(k);
						k--;
						found_reaction = true;
						break;
					}
					if (found_reaction)
					{
						break;
					}
				}
				if (!found_reaction)
				{
					for (int j = 0; j < responding_characters.Count; j++)
					{
						MovieCharacterReaction reaction = MovieTheater.GetReactionsForCharacter(responding_characters[j] as NPC);
						if (reaction == null)
						{
							continue;
						}
						foreach (MovieReaction movie_reaction in reaction.Reactions)
						{
							if (!movie_reaction.ShouldApplyToMovie(movieData, MovieTheater.GetPatronNames(), MovieTheater.GetResponseForMovie(responding_characters[j] as NPC)) || movie_reaction.SpecialResponses?.DuringMovie == null || !(movie_reaction.SpecialResponses.DuringMovie.ResponsePoint == current_response_index.ToString()))
							{
								continue;
							}
							if (!_whiteListDependencyLookup.ContainsKey(responding_characters[j]))
							{
								_responseOrder[current_response_index] = responding_characters[j];
								if (movie_reaction.Whitelist != null)
								{
									for (int j2 = 0; j2 < movie_reaction.Whitelist.Count; j2++)
									{
										Character white_list_character = Game1.getCharacterFromName(movie_reaction.Whitelist[j2]);
										if (white_list_character == null)
										{
											continue;
										}
										_whiteListDependencyLookup[white_list_character] = responding_characters[j];
										foreach (int key in _responseOrder.Keys)
										{
											if (_responseOrder[key] == white_list_character)
											{
												_responseOrder.Remove(key);
											}
										}
									}
								}
							}
							responding_characters.RemoveAt(j);
							j--;
							found_reaction = true;
							break;
						}
						if (found_reaction)
						{
							break;
						}
					}
				}
				current_response_index++;
			}
			current_response_index = 0;
			for (int i = 0; i < responding_characters.Count; i++)
			{
				if (!_whiteListDependencyLookup.ContainsKey(responding_characters[i]))
				{
					for (; _responseOrder.ContainsKey(current_response_index); current_response_index++)
					{
					}
					_responseOrder[current_response_index] = responding_characters[i];
					current_response_index++;
				}
			}
			responding_characters = null;
			foreach (MovieScene scene in movieData.Scenes)
			{
				_ParseScene(sb, scene);
			}
			while (currentResponse < _responseOrder.Count)
			{
				_ParseResponse(sb);
			}
			sb.Append("/stopMusic");
			sb.Append("/fade/viewport -1000 -1000");
			sb.Append("/pause 500/message \"" + Game1.content.LoadString("Strings\\Locations:Theater_MovieEnd") + "\"/pause 500");
			sb.Append("/requestMovieEnd");
			return new Event(sb.ToString(), null, "MovieTheaterScreening");
		}

		protected void _ParseScene(StringBuilder sb, MovieScene scene)
		{
			if (!string.IsNullOrWhiteSpace(scene.Sound))
			{
				sb.Append("/playSound " + scene.Sound);
			}
			if (!string.IsNullOrWhiteSpace(scene.Music))
			{
				sb.Append("/playMusic " + scene.Music);
			}
			if (scene.MessageDelay > 0)
			{
				sb.Append("/pause " + scene.MessageDelay);
			}
			if (scene.Image >= 0)
			{
				sb.Append("/specificTemporarySprite movieTheater_screen " + movieData.Id + " " + scene.Image + " " + scene.Shake);
				if (movieData.Texture != null)
				{
					StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, sb);
					handler.AppendLiteral(" \"");
					handler.AppendFormatted(ArgUtility.EscapeQuotes(movieData.Texture));
					handler.AppendLiteral("\"");
					sb.Append(ref handler);
				}
			}
			if (!string.IsNullOrWhiteSpace(scene.Script))
			{
				sb.Append(TokenParser.ParseText(scene.Script));
			}
			if (!string.IsNullOrWhiteSpace(scene.Text))
			{
				sb.Append("/message \"" + TokenParser.ParseText(scene.Text) + "\"");
			}
			if (scene.ResponsePoint != null)
			{
				_ParseResponse(sb, scene);
			}
		}

		protected void _ParseResponse(StringBuilder sb, MovieScene scene = null)
		{
			Character responding_character;
			if (_responseOrder.TryGetValue(currentResponse, out responding_character))
			{
				sb.Append("/pause 500");
				bool hadUniqueScript = false;
				if (!_whiteListDependencyLookup.ContainsKey(responding_character))
				{
					MovieCharacterReaction reaction = MovieTheater.GetReactionsForCharacter(responding_character as NPC);
					if (reaction != null)
					{
						foreach (MovieReaction movie_reaction in reaction.Reactions)
						{
							if (movie_reaction.ShouldApplyToMovie(movieData, MovieTheater.GetPatronNames(), MovieTheater.GetResponseForMovie(responding_character as NPC)) && movie_reaction.SpecialResponses?.DuringMovie != null && (string.IsNullOrEmpty(movie_reaction.SpecialResponses.DuringMovie.ResponsePoint) || (scene != null && movie_reaction.SpecialResponses.DuringMovie.ResponsePoint == scene.ResponsePoint) || movie_reaction.SpecialResponses.DuringMovie.ResponsePoint == currentResponse.ToString() || movie_reaction.Whitelist.Count > 0))
							{
								string script = TokenParser.ParseText(movie_reaction.SpecialResponses.DuringMovie.Script);
								string text = TokenParser.ParseText(movie_reaction.SpecialResponses.DuringMovie.Text);
								if (!string.IsNullOrWhiteSpace(script))
								{
									sb.Append(script);
									hadUniqueScript = true;
								}
								if (!string.IsNullOrWhiteSpace(text))
								{
									sb.Append(string.Concat("/speak ", responding_character.name, " \"", text, "\""));
								}
								break;
							}
						}
					}
				}
				_ParseCharacterResponse(sb, responding_character, hadUniqueScript);
				foreach (Character key in _whiteListDependencyLookup.Keys)
				{
					if (_whiteListDependencyLookup[key] == responding_character)
					{
						_ParseCharacterResponse(sb, key);
					}
				}
			}
			currentResponse++;
		}

		protected void _ParseCharacterResponse(StringBuilder sb, Character responding_character, bool ignoreScript = false)
		{
			string response = MovieTheater.GetResponseForMovie(responding_character as NPC);
			Character requestingCharacter;
			if (_whiteListDependencyLookup.TryGetValue(responding_character, out requestingCharacter))
			{
				response = MovieTheater.GetResponseForMovie(requestingCharacter as NPC);
			}
			switch (response)
			{
			case "love":
				sb.Append("/friendship " + responding_character.Name + " " + 200);
				if (!ignoreScript)
				{
					sb.Append(string.Concat("/playSound reward/emote ", responding_character.name, " ", 20.ToString(), "/message \"", Game1.content.LoadString("Strings\\Characters:MovieTheater_LoveMovie", responding_character.displayName), "\""));
				}
				break;
			case "like":
				sb.Append("/friendship " + responding_character.Name + " " + 100);
				if (!ignoreScript)
				{
					sb.Append(string.Concat("/playSound give_gift/emote ", responding_character.name, " ", 56.ToString(), "/message \"", Game1.content.LoadString("Strings\\Characters:MovieTheater_LikeMovie", responding_character.displayName), "\""));
				}
				break;
			case "dislike":
				sb.Append("/friendship " + responding_character.Name + " " + 0);
				if (!ignoreScript)
				{
					sb.Append(string.Concat("/playSound newArtifact/emote ", responding_character.name, " ", 24.ToString(), "/message \"", Game1.content.LoadString("Strings\\Characters:MovieTheater_DislikeMovie", responding_character.displayName), "\""));
				}
				break;
			}
			MovieConcession concession;
			if (_concessionsData != null && _concessionsData.TryGetValue(responding_character, out concession))
			{
				string concession_response = MovieTheater.GetConcessionTasteForCharacter(responding_character, concession);
				string gender_tag = "";
				CharacterData npcData;
				if (NPC.TryGetData(responding_character.name, out npcData))
				{
					switch (npcData.Gender)
					{
					case Gender.Female:
						gender_tag = "_Female";
						break;
					case Gender.Male:
						gender_tag = "_Male";
						break;
					}
				}
				string sound = "eat";
				if (concession.Tags != null && concession.Tags.Contains("Drink"))
				{
					sound = "gulp";
				}
				switch (concession_response)
				{
				case "love":
					sb.Append("/friendship " + responding_character.Name + " " + 50);
					sb.Append("/tossConcession " + responding_character.Name + " " + concession.Id + "/pause 1000");
					sb.Append("/playSound " + sound + "/shake " + responding_character.Name + " 500/pause 1000");
					sb.Append(string.Concat("/playSound reward/emote ", responding_character.name, " ", 20.ToString(), "/message \"", Game1.content.LoadString("Strings\\Characters:MovieTheater_LoveConcession" + gender_tag, responding_character.displayName, concession.DisplayName), "\""));
					break;
				case "like":
					sb.Append("/friendship " + responding_character.Name + " " + 25);
					sb.Append("/tossConcession " + responding_character.Name + " " + concession.Id + "/pause 1000");
					sb.Append("/playSound " + sound + "/shake " + responding_character.Name + " 500/pause 1000");
					sb.Append(string.Concat("/playSound give_gift/emote ", responding_character.name, " ", 56.ToString(), "/message \"", Game1.content.LoadString("Strings\\Characters:MovieTheater_LikeConcession" + gender_tag, responding_character.displayName, concession.DisplayName), "\""));
					break;
				case "dislike":
					sb.Append("/friendship " + responding_character.Name + " " + 0);
					sb.Append("/playSound croak/pause 1000");
					sb.Append(string.Concat("/playSound newArtifact/emote ", responding_character.name, " ", 40.ToString(), "/message \"", Game1.content.LoadString("Strings\\Characters:MovieTheater_DislikeConcession" + gender_tag, responding_character.displayName, concession.DisplayName), "\""));
					break;
				}
			}
			_characterResponses[responding_character] = response;
		}

		public Dictionary<Character, string> GetCharacterResponses()
		{
			return _characterResponses;
		}

		private static string getEventName(Character c)
		{
			Farmer player = c as Farmer;
			if (player != null)
			{
				return "farmer" + Utility.getFarmerNumberFromFarmer(player);
			}
			return c.name;
		}

		private Point getBackRowSeatTileFromIndex(int index)
		{
			switch (index)
			{
			case 0:
				return new Point(2, 10);
			case 1:
				return new Point(3, 10);
			case 2:
				return new Point(4, 10);
			case 3:
				return new Point(5, 10);
			case 4:
				return new Point(8, 10);
			case 5:
				return new Point(9, 10);
			case 6:
				return new Point(10, 10);
			case 7:
				return new Point(11, 10);
			default:
				return new Point(4, 12);
			}
		}

		private Point getMidRowSeatTileFromIndex(int index)
		{
			switch (index)
			{
			case 0:
				return new Point(3, 8);
			case 1:
				return new Point(4, 8);
			case 2:
				return new Point(5, 8);
			case 3:
				return new Point(8, 8);
			case 4:
				return new Point(9, 8);
			case 5:
				return new Point(10, 8);
			default:
				return new Point(4, 12);
			}
		}

		private Point getFrontRowSeatTileFromIndex(int index)
		{
			switch (index)
			{
			case 0:
				return new Point(4, 6);
			case 1:
				return new Point(5, 6);
			case 2:
				return new Point(8, 6);
			case 3:
				return new Point(9, 6);
			default:
				return new Point(4, 12);
			}
		}
	}
}
