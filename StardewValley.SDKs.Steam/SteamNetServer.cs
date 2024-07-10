using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using StardewValley.Logging;
using StardewValley.Network;
using StardewValley.SDKs.GogGalaxy;
using StardewValley.SDKs.Steam.Internal;
using Steamworks;

namespace StardewValley.SDKs.Steam
{
	internal sealed class SteamNetServer : HookableServer
	{
		/// <summary>The max number of messages we can receive in a single frame.</summary>
		private const int ServerBufferSize = 256;

		/// <summary>The bit mask to check if a player entered the lobby.</summary>
		private const int FlagsLobbyEntered = 1;

		/// <summary>The bit mask to check if a player left the lobby for any reason.</summary>
		private const int FlagsLobbyLeft = 30;

		/// <summary>The callback used to check the result of creating a Steam lobby.</summary>
		private CallResult<LobbyCreated_t> LobbyCreatedCallResult;

		/// <summary>The callback used to handle changes in the connection state (connecting, connected, disconnected, etc).</summary>
		private Callback<SteamNetConnectionStatusChangedCallback_t> SteamNetConnectionStatusChangedCallback;

		/// <summary>The callback used to handle changes to chat room members (joined the lobby, left the lobby, etc).</summary>
		private Callback<LobbyChatUpdate_t> LobbyChatUpdateCallback;

		/// <summary>A local copy of the lobby data, in case the Steam lobby is not ready.</summary>
		private Dictionary<string, string> LobbyData;

		/// <summary>The connection data by Steam Networking Socket.</summary>
		private Dictionary<HSteamNetConnection, ConnectionData> ConnectionDataMap;

		/// <summary>The connection data by farmer ID.</summary>
		private Dictionary<long, ConnectionData> FarmerConnectionMap;

		/// <summary>The cached display names of Steam lobby members.</summary>
		private Dictionary<CSteamID, string> CachedDisplayNames;

		/// <summary>The connections that changed poll groups during a call to <see cref="M:StardewValley.SDKs.Steam.SteamNetServer.PollJoiningMessages" />.</summary>
		private HashSet<HSteamNetConnection> RecentlyJoined;

		/// <summary>The pointers to received messages.</summary>
		private readonly IntPtr[] Messages = new IntPtr[256];

		/// <summary>The Steam ID of the game server's lobby.</summary>
		private CSteamID Lobby;

		/// <summary>The Steam socket used to handle incoming connections.</summary>
		private HSteamListenSocket ListenSocket = HSteamListenSocket.Invalid;

		/// <summary>The poll group used for connections that have not selected a farmhand.</summary>
		private HSteamNetPollGroup JoiningGroup = HSteamNetPollGroup.Invalid;

		/// <summary>The poll group used for connections currently playing as a farmhand.</summary>
		private HSteamNetPollGroup FarmhandGroup = HSteamNetPollGroup.Invalid;

		/// <summary>The privacy setting for the server's lobby.</summary>
		private ServerPrivacy Privacy;

		public override int connectionsCount => ConnectionDataMap?.Count ?? 0;

		/// <summary>Creates an instance of the <see cref="T:StardewValley.SDKs.Steam.SteamNetServer" />.</summary>
		public SteamNetServer(IGameServer gameServer)
			: base(gameServer)
		{
		}

		/// <summary>Applies the privacy setting from <see cref="F:StardewValley.SDKs.Steam.SteamNetServer.Privacy" /> to the game server's lobby.</summary>
		private void UpdateLobbyPrivacy()
		{
			if (Lobby.IsValid())
			{
				ELobbyType lobbyType;
				switch (Privacy)
				{
				case ServerPrivacy.FriendsOnly:
					lobbyType = ELobbyType.k_ELobbyTypeFriendsOnly;
					break;
				case ServerPrivacy.Public:
					lobbyType = ELobbyType.k_ELobbyTypePublic;
					break;
				default:
					lobbyType = ELobbyType.k_ELobbyTypePrivate;
					break;
				}
				SteamMatchmaking.SetLobbyType(Lobby, lobbyType);
			}
		}

		/// <summary>Converts a <see cref="T:StardewValley.SDKs.Steam.Internal.ConnectionData" /> to a connection string to be used in the Stardew API.</summary>
		/// <param name="connection">The connection data to convert to a unique connection string.</param>
		/// <returns>Returns a string that uniquely corresponds to the <see cref="T:StardewValley.SDKs.Steam.Internal.ConnectionData" />.</returns>
		private string ConnectionDataToId(ConnectionData connection)
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(4, 2);
			defaultInterpolatedStringHandler.AppendLiteral("SN_");
			defaultInterpolatedStringHandler.AppendFormatted(connection.SteamId.m_SteamID);
			defaultInterpolatedStringHandler.AppendLiteral("_");
			defaultInterpolatedStringHandler.AppendFormatted(connection.Connection.m_HSteamNetConnection);
			return defaultInterpolatedStringHandler.ToStringAndClear();
		}

		/// <summary>Gets the internal <see cref="T:StardewValley.SDKs.Steam.Internal.ConnectionData" /> for a corresponding connection string.</summary>
		/// <param name="connectionId">The unique connection string to fetch <see cref="T:StardewValley.SDKs.Steam.Internal.ConnectionData" /> for.</param>
		/// <returns>Returns the <see cref="T:StardewValley.SDKs.Steam.Internal.ConnectionData" /> bookkeeping object that corresponds to the <paramref name="connectionId" /> string, or <c>null</c> if not found.</returns>
		private ConnectionData IdToConnectionData(string connectionId)
		{
			if (connectionId.Length <= 3 || !connectionId.StartsWith("SN_"))
			{
				return null;
			}
			string steamConnectionString = connectionId.Substring(3);
			int underscoreIdx = steamConnectionString.IndexOf('_');
			if (underscoreIdx < 0)
			{
				return null;
			}
			ulong rawSteamId = default(CSteamID).m_SteamID;
			uint connectionRaw = HSteamNetConnection.Invalid.m_HSteamNetConnection;
			try
			{
				rawSteamId = Convert.ToUInt64(steamConnectionString.Substring(0, underscoreIdx));
				connectionRaw = Convert.ToUInt32(steamConnectionString.Substring(underscoreIdx + 1));
			}
			catch (Exception)
			{
			}
			if (!new CSteamID(rawSteamId).IsValid())
			{
				return null;
			}
			HSteamNetConnection connection = HSteamNetConnection.Invalid;
			connection.m_HSteamNetConnection = connectionRaw;
			ConnectionData connectionData;
			if (!ConnectionDataMap.TryGetValue(connection, out connectionData))
			{
				return null;
			}
			if (connectionData.SteamId.m_SteamID != rawSteamId)
			{
				return null;
			}
			return connectionData;
		}

		public override bool isConnectionActive(string connectionId)
		{
			return IdToConnectionData(connectionId) != null;
		}

		public override string getUserId(long farmerId)
		{
			ConnectionData connectionData;
			if (!FarmerConnectionMap.TryGetValue(farmerId, out connectionData))
			{
				return null;
			}
			return connectionData.SteamId.m_SteamID.ToString();
		}

		public override bool hasUserId(string userId)
		{
			CSteamID steamId = default(CSteamID);
			try
			{
				steamId = new CSteamID(Convert.ToUInt64(userId));
			}
			catch (Exception)
			{
			}
			if (!steamId.IsValid())
			{
				return false;
			}
			foreach (KeyValuePair<HSteamNetConnection, ConnectionData> item in ConnectionDataMap)
			{
				if (item.Value.SteamId.m_SteamID == steamId.m_SteamID)
				{
					return true;
				}
			}
			return false;
		}

		public override string getUserName(long farmerId)
		{
			ConnectionData connectionData;
			if (!FarmerConnectionMap.TryGetValue(farmerId, out connectionData))
			{
				return "";
			}
			string userName = SteamFriends.GetFriendPersonaName(connectionData.SteamId);
			if (string.IsNullOrWhiteSpace(userName) || userName == "[unknown]")
			{
				userName = connectionData.DisplayName;
			}
			connectionData.DisplayName = userName;
			return userName;
		}

		public override void setPrivacy(ServerPrivacy privacy)
		{
			Privacy = privacy;
			UpdateLobbyPrivacy();
		}

		public override bool connected()
		{
			if (Lobby.IsValid() && Lobby.IsLobby() && ListenSocket != HSteamListenSocket.Invalid && JoiningGroup != HSteamNetPollGroup.Invalid)
			{
				return FarmhandGroup != HSteamNetPollGroup.Invalid;
			}
			return false;
		}

		/// <summary>Handles new incoming connections, and rejects users that are banned.</summary>
		/// <param name="evt">The data about the incoming client connection.</param>
		/// <param name="steamId">The Steam ID of the connecting client.</param>
		private void OnConnecting(SteamNetConnectionStatusChangedCallback_t evt, CSteamID steamId)
		{
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(21, 1);
			defaultInterpolatedStringHandler.AppendFormatted(steamId.m_SteamID);
			defaultInterpolatedStringHandler.AppendLiteral(" connecting to server");
			log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			if (gameServer.isUserBanned(steamId.m_SteamID.ToString()))
			{
				IGameLogger log2 = Game1.log;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(10, 1);
				defaultInterpolatedStringHandler.AppendFormatted(steamId.m_SteamID);
				defaultInterpolatedStringHandler.AppendLiteral(" is banned");
				log2.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
				ShutdownConnection(evt.m_hConn);
			}
			else
			{
				SteamFriends.RequestUserInformation(steamId, true);
				SteamNetworkingSockets.AcceptConnection(evt.m_hConn);
			}
		}

		/// <summary>Handles newly connected clients, and creates internal bookkeeping structures for the connection.</summary>
		/// <param name="evt">A structure containing data about the newly connected client.</param>
		/// <param name="steamId">The Steam ID of the connected client.</param>
		private void OnConnected(SteamNetConnectionStatusChangedCallback_t evt, CSteamID steamId)
		{
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(20, 1);
			defaultInterpolatedStringHandler.AppendFormatted(steamId.m_SteamID);
			defaultInterpolatedStringHandler.AppendLiteral(" connected to server");
			log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			string cachedName;
			string displayName = (CachedDisplayNames.TryGetValue(steamId, out cachedName) ? cachedName : null);
			ConnectionData connectionData = new ConnectionData(evt.m_hConn, steamId, displayName);
			ConnectionDataMap[evt.m_hConn] = connectionData;
			SteamNetworkingSockets.SetConnectionPollGroup(evt.m_hConn, JoiningGroup);
			string connectionId = ConnectionDataToId(connectionData);
			onConnect(connectionId);
			gameServer.sendAvailableFarmhands("", connectionId, delegate(OutgoingMessage outgoing)
			{
				SendMessageToConnection(evt.m_hConn, outgoing);
			});
		}

		/// <summary>Handles client disconnects, and cleans up all bookkeeping data about the connection.</summary>
		/// <param name="evt">The data about the disconnected client.</param>
		/// <param name="steamId">The Steam ID of the disconnected client.</param>
		private void OnDisconnected(SteamNetConnectionStatusChangedCallback_t evt, CSteamID steamId)
		{
			if (!steamId.IsValid())
			{
				return;
			}
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(25, 1);
			defaultInterpolatedStringHandler.AppendFormatted(steamId.m_SteamID);
			defaultInterpolatedStringHandler.AppendLiteral(" disconnected from server");
			log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			ConnectionData connectionData;
			if (!ConnectionDataMap.TryGetValue(evt.m_hConn, out connectionData))
			{
				SteamSocketUtils.CloseConnection(evt.m_hConn);
				return;
			}
			onDisconnect(ConnectionDataToId(connectionData));
			if (connectionData.Online)
			{
				playerDisconnected(connectionData.FarmerId);
			}
			ConnectionDataMap.Remove(evt.m_hConn);
			SteamSocketUtils.CloseConnection(evt.m_hConn);
		}

		/// <summary>Handles clients disconnected via <see cref="M:Steamworks.SteamNetworkingSockets.CloseConnection(Steamworks.HSteamNetConnection,System.Int32,System.String,System.Boolean)" />, and cleans up all bookkeeping data about the connection.</summary>
		/// <param name="connection">The connection to clean up.</param>
		private void OnDisconnected(HSteamNetConnection connection)
		{
			SteamNetConnectionStatusChangedCallback_t steamNetConnectionStatusChangedCallback_t = default(SteamNetConnectionStatusChangedCallback_t);
			steamNetConnectionStatusChangedCallback_t.m_hConn = connection;
			steamNetConnectionStatusChangedCallback_t.m_eOldState = ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected;
			SteamNetConnectionStatusChangedCallback_t fakeStatusChange = steamNetConnectionStatusChangedCallback_t;
			SteamNetworkingSockets.GetConnectionInfo(connection, out fakeStatusChange.m_info);
			OnDisconnected(fakeStatusChange, fakeStatusChange.m_info.m_identityRemote.GetSteamID());
		}

		/// <summary>Handles all changes in client connection status, and invokes the corresponding handler.</summary>
		/// <param name="evt">A structure containing data about the client whose connection status changed.</param>
		private void OnSteamNetConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t evt)
		{
			switch (evt.m_info.m_eState)
			{
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
				OnConnecting(evt, evt.m_info.m_identityRemote.GetSteamID());
				break;
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
				OnConnected(evt, evt.m_info.m_identityRemote.GetSteamID());
				break;
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
				OnDisconnected(evt, evt.m_info.m_identityRemote.GetSteamID());
				break;
			case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute:
				break;
			}
		}

		/// <summary>Handles all changes in lobby member status.</summary>
		/// <param name="evt">A structure containing data about the changes to a lobby member.</param>
		private void OnLobbyChatUpdate(LobbyChatUpdate_t evt)
		{
			if (evt.m_ulSteamIDLobby == Lobby.m_SteamID)
			{
				CSteamID memberId = new CSteamID(evt.m_ulSteamIDUserChanged);
				if ((evt.m_rgfChatMemberStateChange & (true ? 1u : 0u)) != 0)
				{
					CachedDisplayNames[memberId] = SteamFriends.GetFriendPersonaName(memberId);
				}
				else if ((evt.m_rgfChatMemberStateChange & 0x1Eu) != 0)
				{
					CachedDisplayNames.Remove(memberId);
				}
			}
		}

		/// <summary>Handles the result of Steam lobby creation.</summary>
		/// <param name="evt">The data for the Lobby creation event.</param>
		/// <param name="ioFailure">Whether creating the lobby failed due to an I/O error.</param>
		/// <returns>Returns an error indicating why creation failed, if applicable.</returns>
		private string OnLobbyCreatedHelper(LobbyCreated_t evt, bool ioFailure)
		{
			if (ioFailure)
			{
				return "IO Failure";
			}
			switch (evt.m_eResult)
			{
			case EResult.k_EResultOK:
				Lobby = new CSteamID(evt.m_ulSteamIDLobby);
				return null;
			case EResult.k_EResultTimeout:
				return "Steam timed out";
			case EResult.k_EResultLimitExceeded:
				return "Too many Steam lobbies created";
			case EResult.k_EResultAccessDenied:
				return "Steam denied access";
			case EResult.k_EResultNoConnection:
				return "No connection to Steam";
			default:
				return "Unknown Steam failure";
			}
		}

		/// <summary>Handles the result of Steam lobby creation.</summary>
		/// <param name="evt">The data for the Lobby creation event.</param>
		/// <param name="ioFailure">Whether creating the lobby failed due to an I/O error.</param>
		private void OnLobbyCreated(LobbyCreated_t evt, bool ioFailure)
		{
			string lobbyError = OnLobbyCreatedHelper(evt, ioFailure);
			if (lobbyError == null)
			{
				SteamNetworkingConfigValue_t[] options = SteamSocketUtils.GetNetworkingOptions();
				ListenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
				JoiningGroup = SteamNetworkingSockets.CreatePollGroup();
				FarmhandGroup = SteamNetworkingSockets.CreatePollGroup();
				SteamMatchmaking.SetLobbyGameServer(Lobby, 0u, 0, SteamUser.GetSteamID());
				foreach (KeyValuePair<string, string> data in LobbyData)
				{
					SteamMatchmaking.SetLobbyData(Lobby, data.Key, data.Value);
				}
				SteamMatchmaking.SetLobbyJoinable(Lobby, true);
				UpdateLobbyPrivacy();
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(40, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Steam server successfully created lobby ");
				defaultInterpolatedStringHandler.AppendFormatted(Lobby.m_SteamID);
				log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
				StardewValley.Network.GameServer gameServerImpl = gameServer as StardewValley.Network.GameServer;
				if (gameServerImpl == null)
				{
					return;
				}
				{
					foreach (Server server in gameServerImpl.servers)
					{
						GalaxyNetServer galaxyServer = server as GalaxyNetServer;
						if (galaxyServer != null)
						{
							galaxyServer.setLobbyData("SteamLobbyId", Lobby.m_SteamID.ToString());
							Game1.log.Verbose("Updated Galaxy server with Steam lobby info");
							break;
						}
					}
					return;
				}
			}
			Game1.log.Verbose("Server failed to create lobby (" + lobbyError + ")");
		}

		public override void initialize()
		{
			Game1.log.Verbose("Starting Steam server");
			LobbyCreatedCallResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
			SteamNetConnectionStatusChangedCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSteamNetConnectionStatusChanged);
			LobbyChatUpdateCallback = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
			LobbyData = new Dictionary<string, string>();
			ConnectionDataMap = new Dictionary<HSteamNetConnection, ConnectionData>();
			FarmerConnectionMap = new Dictionary<long, ConnectionData>();
			CachedDisplayNames = new Dictionary<CSteamID, string>();
			RecentlyJoined = new HashSet<HSteamNetConnection>();
			LobbyData["protocolVersion"] = Multiplayer.protocolVersion;
			Lobby.Clear();
			ListenSocket = HSteamListenSocket.Invalid;
			JoiningGroup = HSteamNetPollGroup.Invalid;
			FarmhandGroup = HSteamNetPollGroup.Invalid;
			Privacy = Game1.options.serverPrivacy;
			SteamAPICall_t steamApiCall = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, Game1.multiplayer.playerLimit * 2);
			LobbyCreatedCallResult.Set(steamApiCall);
		}

		public override void stopServer()
		{
			Game1.log.Verbose("Stopping Steam server");
			foreach (KeyValuePair<HSteamNetConnection, ConnectionData> item in ConnectionDataMap)
			{
				ShutdownConnection(item.Value.Connection);
			}
			if (Lobby.IsValid())
			{
				SteamMatchmaking.LeaveLobby(Lobby);
			}
			if (ListenSocket != HSteamListenSocket.Invalid)
			{
				SteamNetworkingSockets.CloseListenSocket(ListenSocket);
				ListenSocket = HSteamListenSocket.Invalid;
			}
			if (JoiningGroup != HSteamNetPollGroup.Invalid)
			{
				SteamNetworkingSockets.DestroyPollGroup(JoiningGroup);
				JoiningGroup = HSteamNetPollGroup.Invalid;
			}
			if (FarmhandGroup != HSteamNetPollGroup.Invalid)
			{
				SteamNetworkingSockets.DestroyPollGroup(FarmhandGroup);
				FarmhandGroup = HSteamNetPollGroup.Invalid;
			}
			SteamNetConnectionStatusChangedCallback?.Unregister();
			LobbyChatUpdateCallback?.Unregister();
		}

		/// <summary>Handles an incoming <see cref="F:StardewValley.Multiplayer.playerIntroduction" /> message.</summary>
		/// <param name="message">The incoming <see cref="F:StardewValley.Multiplayer.playerIntroduction" /> message containing information about the requested farmhand.</param>
		/// <param name="connectionData">The connection data for the player who sent the <paramref name="message" />.</param>
		private void HandleFarmhandRequest(IncomingMessage message, ConnectionData connectionData)
		{
			NetFarmerRoot farmer = Game1.multiplayer.readFarmer(message.Reader);
			long farmerId = farmer.Value.UniqueMultiplayerID;
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(43, 2);
			defaultInterpolatedStringHandler.AppendLiteral("Server received farmhand request from ");
			defaultInterpolatedStringHandler.AppendFormatted(connectionData.SteamId.m_SteamID);
			defaultInterpolatedStringHandler.AppendLiteral(" for ");
			defaultInterpolatedStringHandler.AppendFormatted(farmerId);
			log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			gameServer.checkFarmhandRequest("", ConnectionDataToId(connectionData), farmer, delegate(OutgoingMessage outgoing)
			{
				SendMessageToConnection(connectionData.Connection, outgoing);
			}, delegate
			{
				IGameLogger log2 = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler2 = new DefaultInterpolatedStringHandler(29, 2);
				defaultInterpolatedStringHandler2.AppendLiteral("Server accepted ");
				defaultInterpolatedStringHandler2.AppendFormatted(connectionData.SteamId.m_SteamID);
				defaultInterpolatedStringHandler2.AppendLiteral(" as farmhand ");
				defaultInterpolatedStringHandler2.AppendFormatted(farmerId);
				log2.Verbose(defaultInterpolatedStringHandler2.ToStringAndClear());
				SteamNetworkingSockets.SetConnectionUserData(connectionData.Connection, farmerId);
				SteamNetworkingSockets.SetConnectionPollGroup(connectionData.Connection, FarmhandGroup);
				RecentlyJoined.Add(connectionData.Connection);
				connectionData.FarmerId = farmerId;
				connectionData.Online = true;
				FarmerConnectionMap[farmerId] = connectionData;
			});
		}

		/// <summary>Receives messages from the <see cref="F:StardewValley.SDKs.Steam.SteamNetServer.JoiningGroup" /> poll group, where all clients without farmhands should be.</summary>
		private void PollJoiningMessages()
		{
			RecentlyJoined.Clear();
			int messageCount = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(JoiningGroup, Messages, 256);
			for (int messageIndex = 0; messageIndex < messageCount; messageIndex++)
			{
				IncomingMessage message = new IncomingMessage();
				HSteamNetConnection messageConnection;
				SteamSocketUtils.ProcessSteamMessage(Messages[messageIndex], message, out messageConnection, bandwidthLogger);
				ConnectionData connectionData;
				if (!ConnectionDataMap.TryGetValue(messageConnection, out connectionData))
				{
					Game1.log.Warn("Tried to process multiplayer message from an invalid connection.");
					ShutdownConnection(messageConnection);
					continue;
				}
				bool isRecentlyJoined = RecentlyJoined.Contains(messageConnection);
				if (connectionData.Online && !isRecentlyJoined)
				{
					IGameLogger log = Game1.log;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(70, 1);
					defaultInterpolatedStringHandler.AppendLiteral("Online farmhand ");
					defaultInterpolatedStringHandler.AppendFormatted(connectionData.FarmerId);
					defaultInterpolatedStringHandler.AppendLiteral(" is in the wrong poll group. Closing their connection.");
					log.Warn(defaultInterpolatedStringHandler.ToStringAndClear());
					ShutdownConnection(messageConnection);
					continue;
				}
				base.OnProcessingMessage(message, delegate(OutgoingMessage outgoing)
				{
					SendMessageToConnection(messageConnection, outgoing);
				}, delegate
				{
					if (isRecentlyJoined)
					{
						gameServer.processIncomingMessage(message);
					}
					else if (message.MessageType == 2)
					{
						HandleFarmhandRequest(message, connectionData);
					}
				});
			}
		}

		/// <summary>Receives messages from the <see cref="F:StardewValley.SDKs.Steam.SteamNetServer.FarmhandGroup" /> poll group, where all clients with actively playing farmhands should be.</summary>
		private void PollFarmhandMessages()
		{
			int messageCount = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(FarmhandGroup, Messages, 256);
			for (int messageIndex = 0; messageIndex < messageCount; messageIndex++)
			{
				IncomingMessage message = new IncomingMessage();
				HSteamNetConnection messageConnection;
				SteamSocketUtils.ProcessSteamMessage(Messages[messageIndex], message, out messageConnection, bandwidthLogger);
				if (message.MessageType == 2)
				{
					Game1.log.Warn("Received farmhand request in the wrong poll group. Closing their connection.");
					ShutdownConnection(messageConnection);
					continue;
				}
				ConnectionData connectionData;
				if (!ConnectionDataMap.TryGetValue(messageConnection, out connectionData))
				{
					Game1.log.Warn("Tried to process multiplayer message from an invalid connection.");
					ShutdownConnection(messageConnection);
					continue;
				}
				if (!connectionData.Online)
				{
					Game1.log.Warn("A non-farmhand connection is in the wrong poll group. Closing their connection.");
					ShutdownConnection(messageConnection);
					continue;
				}
				base.OnProcessingMessage(message, delegate(OutgoingMessage outgoing)
				{
					SendMessageToConnection(messageConnection, outgoing);
				}, delegate
				{
					gameServer.processIncomingMessage(message);
				});
			}
		}

		public override void receiveMessages()
		{
			if (!connected())
			{
				return;
			}
			PollJoiningMessages();
			PollFarmhandMessages();
			foreach (KeyValuePair<HSteamNetConnection, ConnectionData> item in ConnectionDataMap)
			{
				SteamNetworkingSockets.FlushMessagesOnConnection(item.Value.Connection);
			}
		}

		private void SendMessageToConnection(HSteamNetConnection connection, OutgoingMessage message)
		{
			SteamSocketUtils.SendMessage(connection, message, bandwidthLogger, OnDisconnected);
		}

		public override void sendMessage(long peerId, OutgoingMessage message)
		{
			ConnectionData connectionData;
			if (connected() && FarmerConnectionMap.TryGetValue(peerId, out connectionData) && !(connectionData.Connection == HSteamNetConnection.Invalid))
			{
				SendMessageToConnection(connectionData.Connection, message);
			}
		}

		public override void setLobbyData(string key, string value)
		{
			if (LobbyData != null)
			{
				LobbyData[key] = value;
				if (Lobby.IsValid())
				{
					SteamMatchmaking.SetLobbyData(Lobby, key, value);
				}
			}
		}

		public override void kick(long disconnectee)
		{
			base.kick(disconnectee);
			sendMessage(disconnectee, new OutgoingMessage(23, Game1.player));
			ConnectionData connectionData;
			if (FarmerConnectionMap.TryGetValue(disconnectee, out connectionData))
			{
				ShutdownConnection(connectionData.Connection);
			}
		}

		public override void playerDisconnected(long disconnectee)
		{
			ConnectionData connectionData;
			if (FarmerConnectionMap.TryGetValue(disconnectee, out connectionData))
			{
				base.playerDisconnected(disconnectee);
				FarmerConnectionMap.Remove(disconnectee);
			}
		}

		public override float getPingToClient(long farmerId)
		{
			ConnectionData connectionData;
			if (!FarmerConnectionMap.TryGetValue(farmerId, out connectionData))
			{
				return -1f;
			}
			SteamNetworkingQuickConnectionStatus status;
			SteamNetworkingSockets.GetQuickConnectionStatus(connectionData.Connection, out status);
			return status.m_nPing;
		}

		public override bool canOfferInvite()
		{
			return connected();
		}

		public override void offerInvite()
		{
			if (connected())
			{
				Program.sdk.Networking.ShowInviteDialog(Lobby);
			}
		}

		/// <summary>Closes a connection and cleans up the corresponding bookkeeping data.</summary>
		/// <param name="connection">The connection to close and clean up.</param>
		/// <remarks>
		/// In most cases, this should be used instead of calling <see cref="M:StardewValley.SDKs.Steam.Internal.SteamSocketUtils.CloseConnection(Steamworks.HSteamNetConnection,System.Action{Steamworks.HSteamNetConnection})" /> directly,
		/// otherwise the <see cref="M:StardewValley.SDKs.Steam.SteamNetServer.OnDisconnected(Steamworks.SteamNetConnectionStatusChangedCallback_t,Steamworks.CSteamID)" /> handler will not get called. However, the  <see cref="M:StardewValley.SDKs.Steam.SteamNetServer.OnDisconnected(Steamworks.SteamNetConnectionStatusChangedCallback_t,Steamworks.CSteamID)" />
		/// handler itself should not use this method.
		/// </remarks>
		private void ShutdownConnection(HSteamNetConnection connection)
		{
			SteamSocketUtils.CloseConnection(connection, OnDisconnected);
		}
	}
}
