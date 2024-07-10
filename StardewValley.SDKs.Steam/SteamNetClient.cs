using System;
using System.Runtime.CompilerServices;
using Galaxy.Api;
using StardewValley.Logging;
using StardewValley.Network;
using StardewValley.SDKs.GogGalaxy.Listeners;
using StardewValley.SDKs.Steam.Internal;
using Steamworks;

namespace StardewValley.SDKs.Steam
{
	internal sealed class SteamNetClient : HookableClient
	{
		/// <summary>The max number of messages we can receive in a single frame.</summary>
		private const int ClientBufferSize = 256;

		/// <summary>The callback used to check the result of entering a Steam lobby.</summary>
		private CallResult<LobbyEnter_t> SteamLobbyEnterCallResult;

		/// <summary>The callback used to handle changes in the connection state (connecting, connected, disconnected, etc.).</summary>
		private readonly Callback<SteamNetConnectionStatusChangedCallback_t> SteamNetConnectionStatusChangedCallback;

		/// <summary>The callback used to check the result of retrieving Galaxy lobby data.</summary>
		private GalaxyLobbyDataRetrieveListener GalaxyLobbyDataRetrieveCallback;

		/// <summary>The pointers to received messages.</summary>
		private readonly IntPtr[] Messages = new IntPtr[256];

		/// <summary>The Galaxy lobby ID. If this is valid, we will fetch <see cref="F:StardewValley.SDKs.Steam.SteamNetClient.SteamLobby" /> by querying the <see cref="F:StardewValley.SDKs.GogGalaxy.GalaxySocket.SteamLobbyIdDataKey" /> lobby data.</summary>
		private GalaxyID GalaxyLobby;

		/// <summary>The Steam lobby ID. If this is valid, we will fetch <see cref="F:StardewValley.SDKs.Steam.SteamNetClient.HostId" /> by querying the lobby owner.</summary>
		private CSteamID SteamLobby;

		/// <summary>The Steam host ID that the client will connect to.</summary>
		private CSteamID HostId;

		/// <summary>The Steam display name of the hosting player.</summary>
		private string CachedHostName;

		/// <summary>The Steam Networking Socket connection between the client and server.</summary>
		private HSteamNetConnection Connection = HSteamNetConnection.Invalid;

		/// <summary>Constructs an instance that resolves the host from a Galaxy lobby.</summary>
		/// <param name="galaxyLobby">The Galaxy lobby that we will be querying for the Steam host ID.</param>
		public SteamNetClient(GalaxyID galaxyLobby)
		{
			SteamNetConnectionStatusChangedCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSteamNetConnectionStatusChanged);
			GalaxyLobby = galaxyLobby;
		}

		/// <summary>Constructs an instance that resolves the host from a Steam lobby.</summary>
		/// <param name="steamLobby">The Steam lobby that we will be querying for the Steam host ID.</param>
		public SteamNetClient(CSteamID steamLobby)
		{
			SteamNetConnectionStatusChangedCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnSteamNetConnectionStatusChanged);
			GalaxyLobby = null;
			SteamLobby = steamLobby;
		}

		/// <summary>Cleans up the instance and its callbacks.</summary>
		~SteamNetClient()
		{
			CleanupLobbyDataRetrieve();
			SteamNetConnectionStatusChangedCallback.Unregister();
		}

		/// <summary>Handles disconnecting from the server, and cleans up the connection.</summary>
		/// <param name="connection">The connection to clean up.</param>
		private void OnDisconnected(HSteamNetConnection connection)
		{
			if (!(connection == HSteamNetConnection.Invalid) && !(connection != Connection))
			{
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(32, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Client disconnected from server ");
				defaultInterpolatedStringHandler.AppendFormatted(HostId.m_SteamID);
				log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
				timedOut = true;
				pendingDisconnect = Multiplayer.DisconnectType.HostLeft;
				SteamSocketUtils.CloseConnection(Connection);
				Connection = HSteamNetConnection.Invalid;
			}
		}

		/// <summary>Handles changes in the <see cref="F:StardewValley.SDKs.Steam.SteamNetClient.Connection" /> status.</summary>
		/// <param name="evt">The information about the connection and its new status.</param>
		private void OnSteamNetConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t evt)
		{
			if (!(evt.m_hConn != Connection))
			{
				switch (evt.m_info.m_eState)
				{
				case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
				{
					IGameLogger log2 = Game1.log;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(28, 1);
					defaultInterpolatedStringHandler.AppendLiteral("Client connecting to server ");
					defaultInterpolatedStringHandler.AppendFormatted(HostId.m_SteamID);
					log2.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
					break;
				}
				case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
				{
					IGameLogger log = Game1.log;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(27, 1);
					defaultInterpolatedStringHandler.AppendLiteral("Client connected to server ");
					defaultInterpolatedStringHandler.AppendFormatted(HostId.m_SteamID);
					log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
					break;
				}
				case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
				case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
					OnDisconnected(evt.m_hConn);
					break;
				case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_FindingRoute:
					break;
				}
			}
		}

		public override string getUserID()
		{
			return Program.sdk.Networking.GetUserID();
		}

		protected override string getHostUserName()
		{
			if (!HostId.IsValid())
			{
				return "???";
			}
			string userName = SteamFriends.GetFriendPersonaName(HostId);
			if (string.IsNullOrWhiteSpace(userName) || userName == "[unknown]")
			{
				userName = CachedHostName;
			}
			CachedHostName = userName;
			return userName;
		}

		/// <summary>Connects to the obtained host ID.</summary>
		private void ConnectToHost()
		{
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(17, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Found Steam host ");
			defaultInterpolatedStringHandler.AppendFormatted(HostId.m_SteamID);
			log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			SteamNetworkingIdentity identity = default(SteamNetworkingIdentity);
			identity.SetSteamID(HostId);
			SteamNetworkingConfigValue_t[] options = SteamSocketUtils.GetNetworkingOptions();
			Connection = SteamNetworkingSockets.ConnectP2P(ref identity, 0, options.Length, options);
		}

		/// <summary>Attempts to fetch the host data and connect from the Steam lobby.</summary>
		/// <param name="evt">The data for the lobby enter event.</param>
		/// <param name="ioFailure">Whether joining the lobby failed due to an I/O error.</param>
		/// <param name="errorTranslationKey">The translation key for the UI error message, if applicable.</param>
		/// <returns>Returns an error indicating why connection failed, if applicable.</returns>
		private string TryConnectSteam(LobbyEnter_t evt, bool ioFailure, out string errorTranslationKey)
		{
			SteamLobby.Clear();
			if (ioFailure)
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_Failed";
				return "IO Failure";
			}
			if (evt.m_EChatRoomEnterResponse != 1)
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_Failed";
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(16, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Failed to join: ");
				defaultInterpolatedStringHandler.AppendFormatted((EChatRoomEnterResponse)evt.m_EChatRoomEnterResponse);
				return defaultInterpolatedStringHandler.ToStringAndClear();
			}
			SteamLobby = new CSteamID(evt.m_ulSteamIDLobby);
			string protocolVersion = SteamMatchmaking.GetLobbyData(SteamLobby, "protocolVersion");
			if (protocolVersion != Multiplayer.protocolVersion)
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_FailedProtocolVersion";
				if (!(protocolVersion == ""))
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 2);
					defaultInterpolatedStringHandler.AppendLiteral("Protocol (");
					defaultInterpolatedStringHandler.AppendFormatted(protocolVersion);
					defaultInterpolatedStringHandler.AppendLiteral(") does not match our own (");
					defaultInterpolatedStringHandler.AppendFormatted(Multiplayer.protocolVersion);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					return defaultInterpolatedStringHandler.ToStringAndClear();
				}
				return "Missing protocol version data";
			}
			uint punGameServerIP;
			ushort punGameServerPort;
			CSteamID hostId;
			if (!SteamMatchmaking.GetLobbyGameServer(SteamLobby, out punGameServerIP, out punGameServerPort, out hostId))
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_Failed";
				return "Missing game server data";
			}
			if (!hostId.IsValid())
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_Failed";
				return "Invalid host ID";
			}
			CachedHostName = SteamFriends.GetFriendPersonaName(HostId);
			SteamFriends.RequestUserInformation(hostId, true);
			HostId = hostId;
			ConnectToHost();
			errorTranslationKey = null;
			return null;
		}

		/// <summary>Handles the result of joining a Steam lobby.</summary>
		/// <param name="evt">The data for the Lobby enter event.</param>
		/// <param name="ioFailure">Whether joining the lobby failed due to an I/O error.</param>
		private void OnLobbyEnter(LobbyEnter_t evt, bool ioFailure)
		{
			if (evt.m_ulSteamIDLobby == SteamLobby.m_SteamID)
			{
				string errorTranslationKey;
				string errorMsg = TryConnectSteam(evt, ioFailure, out errorTranslationKey);
				if (errorMsg != null)
				{
					connectionMessage = Game1.content.LoadString(errorTranslationKey);
					IGameLogger log = Game1.log;
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(33, 2);
					defaultInterpolatedStringHandler.AppendLiteral("Error joining via Steam lobby ");
					defaultInterpolatedStringHandler.AppendFormatted(evt.m_ulSteamIDLobby);
					defaultInterpolatedStringHandler.AppendLiteral(" (");
					defaultInterpolatedStringHandler.AppendFormatted(errorMsg);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
				}
				SteamLobbyEnterCallResult = null;
			}
		}

		/// <summary>Starts the client connection process via Steam lobby.</summary>
		private void ConnectImplSteam()
		{
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Resolving Steam host via Steam lobby ");
			defaultInterpolatedStringHandler.AppendFormatted(SteamLobby.m_SteamID);
			log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			SteamLobbyEnterCallResult = CallResult<LobbyEnter_t>.Create(OnLobbyEnter);
			SteamAPICall_t steamApiCall = SteamMatchmaking.JoinLobby(SteamLobby);
			SteamLobbyEnterCallResult.Set(steamApiCall);
		}

		/// <summary>Handles common cleanup tasks for <see cref="M:StardewValley.SDKs.Steam.SteamNetClient.OnLobbyDataRetrieveSuccess(Galaxy.Api.GalaxyID)" /> and <see cref="M:StardewValley.SDKs.Steam.SteamNetClient.OnLobbyDataRetrieveFailure(Galaxy.Api.GalaxyID,Galaxy.Api.ILobbyDataRetrieveListener.FailureReason)" />.</summary>
		private void CleanupLobbyDataRetrieve()
		{
			GalaxyLobbyDataRetrieveCallback?.Dispose();
			GalaxyLobbyDataRetrieveCallback = null;
		}

		/// <summary>Attempts to fetch the host data and connect from the Galaxy lobby.</summary>
		/// <param name="lobbyId">The Galaxy ID of the lobby to fetch host data from.</param>
		/// <param name="errorTranslationKey">The translation key for the UI error message, if applicable.</param>
		/// <returns>Returns an error indicating why connection failed, if applicable.</returns>
		private string TryConnectGalaxy(GalaxyID lobbyId, out string errorTranslationKey)
		{
			string steamLobbyIdString;
			try
			{
				steamLobbyIdString = GalaxyInstance.Matchmaking().GetLobbyData(lobbyId, "SteamLobbyId");
			}
			catch (Exception)
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_Failed";
				return "Failed to get Steam lobby ID";
			}
			if (string.IsNullOrEmpty(steamLobbyIdString))
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_Failed";
				return "Missing Steam lobby ID";
			}
			string protocolVersion;
			try
			{
				protocolVersion = GalaxyInstance.Matchmaking().GetLobbyData(lobbyId, "protocolVersion");
			}
			catch (Exception)
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_FailedProtocolVersion";
				return "Failed to get protocol version";
			}
			if (protocolVersion != Multiplayer.protocolVersion)
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_FailedProtocolVersion";
				if (!string.IsNullOrEmpty(protocolVersion))
				{
					DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 2);
					defaultInterpolatedStringHandler.AppendLiteral("Protocol (");
					defaultInterpolatedStringHandler.AppendFormatted(protocolVersion);
					defaultInterpolatedStringHandler.AppendLiteral(") does not match our own (");
					defaultInterpolatedStringHandler.AppendFormatted(Multiplayer.protocolVersion);
					defaultInterpolatedStringHandler.AppendLiteral(")");
					return defaultInterpolatedStringHandler.ToStringAndClear();
				}
				return "Missing protocol version data";
			}
			CSteamID steamLobbyId = default(CSteamID);
			try
			{
				steamLobbyId = new CSteamID(Convert.ToUInt64(steamLobbyIdString));
			}
			catch (Exception)
			{
			}
			if (!steamLobbyId.IsValid())
			{
				errorTranslationKey = "Strings\\UI:CoopMenu_Failed";
				return "Invalid lobby ID";
			}
			SteamLobby = steamLobbyId;
			GalaxyLobby = null;
			errorTranslationKey = null;
			ConnectImplSteam();
			return null;
		}

		/// <summary>Handles a successful retrieval of data from the Galaxy lobby.</summary>
		/// <param name="lobbyId">The Galaxy ID of the lobby we retrieved data from.</param>
		private void OnLobbyDataRetrieveSuccess(GalaxyID lobbyId)
		{
			if (lobbyId != null && lobbyId != GalaxyLobby)
			{
				return;
			}
			string errorTranslationKey;
			string errorMsg = TryConnectGalaxy(lobbyId, out errorTranslationKey);
			if (errorMsg != null)
			{
				connectionMessage = Game1.content.LoadString(errorTranslationKey);
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(34, 2);
				defaultInterpolatedStringHandler.AppendLiteral("Error joining via Galaxy lobby ");
				defaultInterpolatedStringHandler.AppendFormatted(lobbyId);
				defaultInterpolatedStringHandler.AppendLiteral(" (");
				defaultInterpolatedStringHandler.AppendFormatted(errorMsg);
				defaultInterpolatedStringHandler.AppendLiteral(")");
				log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			else
			{
				try
				{
					GalaxyInstance.Matchmaking().LeaveLobby(lobbyId);
				}
				catch (Exception)
				{
				}
			}
			CleanupLobbyDataRetrieve();
		}

		/// <summary>Handles a failure to retrieve data from the Galaxy lobby.</summary>
		/// <param name="lobbyId">The Galaxy ID of the lobby we failed to retrieve data from.</param>
		/// <param name="failureReason">The reason why we failed to retrieve data from the lobby.</param>
		private void OnLobbyDataRetrieveFailure(GalaxyID lobbyId, ILobbyDataRetrieveListener.FailureReason failureReason)
		{
			if (!(lobbyId != null) || !(lobbyId != GalaxyLobby))
			{
				connectionMessage = Game1.content.LoadString("Strings\\UI:CoopMenu_Failed");
				IGameLogger log = Game1.log;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(50, 1);
				defaultInterpolatedStringHandler.AppendLiteral("Steam client failed to get data from Galaxy lobby ");
				defaultInterpolatedStringHandler.AppendFormatted(lobbyId);
				log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
				CleanupLobbyDataRetrieve();
			}
		}

		/// <summary>Starts the client connection process via Galaxy lobby.</summary>
		private void ConnectImplGalaxy()
		{
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(39, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Resolving Steam lobby via Galaxy lobby ");
			defaultInterpolatedStringHandler.AppendFormatted(GalaxyLobby);
			log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			GalaxyLobbyDataRetrieveCallback = new GalaxyLobbyDataRetrieveListener(OnLobbyDataRetrieveSuccess, OnLobbyDataRetrieveFailure);
			try
			{
				GalaxyInstance.Matchmaking().RequestLobbyData(GalaxyLobby, GalaxyLobbyDataRetrieveCallback);
			}
			catch (Exception e)
			{
				connectionMessage = Game1.content.LoadString("Strings\\UI:CoopMenu_Failed");
				Game1.log.Error("Steam client Galaxy RequestLobbyData failed with an exception:", e);
				CleanupLobbyDataRetrieve();
			}
		}

		protected override void connectImpl()
		{
			if (GalaxyLobby == null)
			{
				ConnectImplSteam();
			}
			else
			{
				ConnectImplGalaxy();
			}
		}

		public override void disconnect(bool neatly = true)
		{
			if (SteamLobby.IsValid())
			{
				SteamMatchmaking.LeaveLobby(SteamLobby);
				SteamLobby.Clear();
			}
			IGameLogger log = Game1.log;
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(33, 1);
			defaultInterpolatedStringHandler.AppendLiteral("Client disconnecting from server ");
			defaultInterpolatedStringHandler.AppendFormatted(HostId.m_SteamID);
			log.Verbose(defaultInterpolatedStringHandler.ToStringAndClear());
			connectionMessage = null;
			ShutdownConnection();
		}

		protected override void receiveMessagesImpl()
		{
			if (Connection == HSteamNetConnection.Invalid)
			{
				return;
			}
			int messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(Connection, Messages, 256);
			for (int messageIndex = 0; messageIndex < messageCount; messageIndex++)
			{
				IncomingMessage message = new IncomingMessage();
				HSteamNetConnection messageConnection;
				SteamSocketUtils.ProcessSteamMessage(Messages[messageIndex], message, out messageConnection, bandwidthLogger);
				base.OnProcessingMessage(message, SendMessageImpl, delegate
				{
					processIncomingMessage(message);
				});
			}
			SteamNetworkingSockets.FlushMessagesOnConnection(Connection);
		}

		public override void sendMessage(OutgoingMessage message)
		{
			base.OnSendingMessage(message, SendMessageImpl, delegate
			{
				SendMessageImpl(message);
			});
		}

		public override float GetPingToHost()
		{
			if (Connection == HSteamNetConnection.Invalid)
			{
				return -1f;
			}
			SteamNetworkingQuickConnectionStatus status;
			SteamNetworkingSockets.GetQuickConnectionStatus(Connection, out status);
			return status.m_nPing;
		}

		/// <summary>Send a message to the server.</summary>
		/// <param name="message">The message to send.</param>
		private void SendMessageImpl(OutgoingMessage message)
		{
			if (!(Connection == HSteamNetConnection.Invalid))
			{
				SteamSocketUtils.SendMessage(Connection, message, bandwidthLogger, OnDisconnected);
			}
		}

		/// <summary>Closes the client connection and cleans up the bookkeeping data.</summary>
		/// <remarks>
		/// In most cases, this should be used instead of calling <see cref="M:StardewValley.SDKs.Steam.Internal.SteamSocketUtils.CloseConnection(Steamworks.HSteamNetConnection,System.Action{Steamworks.HSteamNetConnection})" /> directly,
		/// otherwise the <see cref="M:StardewValley.SDKs.Steam.SteamNetClient.OnDisconnected(Steamworks.HSteamNetConnection)" /> handler will not get called. However, the <see cref="M:StardewValley.SDKs.Steam.SteamNetClient.OnDisconnected(Steamworks.HSteamNetConnection)" />
		/// handler itself should not use this method.
		/// </remarks>
		private void ShutdownConnection()
		{
			SteamSocketUtils.CloseConnection(Connection, OnDisconnected);
		}
	}
}
