using System;
using System.Text;
using BepInEx.Logging;
using Steamworks;
using UnityEngine;
using GraveyardKeeperMultiplayer;

namespace GraveyardKeeperMultiplayer
{
    // Core multiplayer networking class built on Steam P2P (Steamworks.NET).
    // Responsibilities:
    //   - Creating / joining Steam lobbies
    //   - Sending and receiving raw byte packets over Steam P2P
    //   - Maintaining connection state (IsHost, IsConnected, RemoteID)
    //   - Dispatching received packets to the correct handler
    //
    // Architecture: host-authoritative. The host creates the lobby and the client joins.
    // All packets are sent as raw bytes where byte[0] is the PacketType discriminator.
    public class NetworkManager : MonoBehaviour
    {
        // --- Initialisation ---

        // Creates the singleton MonoBehaviour and grabs our own Steam ID.
        // Called from PatchStartGame once GeneralInit completes.
        public static void Init()
        {
            GameObject gameObject = new GameObject("NetworkManager");
            NetworkManager.Instance = gameObject.AddComponent<NetworkManager>();
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            NetworkManager.MyID = SteamUser.GetSteamID();
            ManualLogSource log = Plugin.Log;
            string str = "NetworkManager iniciado! SteamID: ";
            CSteamID myID = NetworkManager.MyID;
            log.LogInfo(str + myID.ToString());
        }

        // Sets up the three Steamworks callbacks used for lobby and session events.
        private void Start()
        {
            // Callback fired after CreateLobby completes (host side)
            _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);

            // Callback fired when the user accepts a Steam invite (client side)
            _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnLobbyJoinRequested);

            // Callback fired after JoinLobby completes (client side)
            _lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
        }

        // --- Lobby management ---

        // Called when the host clicks "Invite Friend". If a lobby already exists, reopens
        // the Steam invite overlay; otherwise creates a new friends-only lobby for 2 players.
        public static void HostGame()
        {
            if (IsConnected) return;

            if (IsHost && _currentLobbyID != CSteamID.Nil)
            {
                Plugin.Log.LogInfo("Lobby já existe, abrindo painel...");
                SteamFriends.ActivateGameOverlayInviteDialog(_currentLobbyID);
            }
            else
            {
                IsHost = true;
                Plugin.Log.LogInfo("Criando lobby...");
                SteamAPICall_t hAPICall = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2);
                Instance._lobbyCreated.Set(hAPICall, null);
            }
        }

        // Fired on the host when the lobby is successfully created.
        // Stores the lobby ID, advertises the host's Steam ID as lobby metadata, and
        // opens the invite overlay. Also registers the P2P session-request callback —
        // without this Steam silently drops all incoming P2P packets.
        private void OnLobbyCreated(LobbyCreated_t result, bool failure)
        {
            if (failure || result.m_eResult != EResult.k_EResultOK)
            {
                Plugin.Log.LogError("Falha ao criar lobby!");
                IsHost = false;
                return;
            }

            _currentLobbyID = new CSteamID(result.m_ulSteamIDLobby);
            Plugin.Log.LogInfo("Lobby criado! ID: " + _currentLobbyID.ToString());

            // Store host ID in lobby data so the client can read it on join
            SteamMatchmaking.SetLobbyData(_currentLobbyID, "host", MyID.ToString());
            SteamFriends.ActivateGameOverlayInviteDialog(_currentLobbyID);

            // CRITICAL: must accept P2P session requests or Steam drops all packets
            _p2PSessionRequest = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
        }

        // Fired when a remote player requests a P2P session. Always accept so packets flow.
        private void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            Plugin.Log.LogInfo("P2P session request de: " + request.m_steamIDRemote.ToString());
            SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
        }

        // Fired on the client when the user accepts a Steam invite.
        // Kicks off the JoinLobby call.
        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t result)
        {
            Plugin.Log.LogInfo("Entrando no lobby: " + result.m_steamIDLobby.ToString());
            SteamAPICall_t hAPICall = SteamMatchmaking.JoinLobby(result.m_steamIDLobby);
            _lobbyEntered.Set(hAPICall, null);
        }

        // Fired on the client when the lobby join completes.
        // Reads the host Steam ID from lobby metadata, marks us as connected, and
        // sends an initial Connect packet + save request to the host.
        private void OnLobbyEntered(LobbyEnter_t result, bool failure)
        {
            if (failure)
            {
                Plugin.Log.LogError("Falha ao entrar no lobby!");
                return;
            }

            CSteamID lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            string hostIdStr = SteamMatchmaking.GetLobbyData(lobbyId, "host");
            RemoteID = new CSteamID(ulong.Parse(hostIdStr));
            IsHost = false;
            IsConnected = true;
            Plugin.Log.LogInfo("Entrou no lobby! Host: " + RemoteID.ToString());

            // Flag the save transfer as pending so the UI loading screen stays up
            SaveTransferManager.IsPendingTransfer = true;

            // Tell the host we're here and request a copy of the save
            SendPacket(PacketType.Connect, new byte[0]);
            SaveTransferManager.RequestSave();
        }

        // --- Per-frame logic ---

        private void Update()
        {
            // Poll for incoming packets whenever we are part of a session
            if (IsHost || IsConnected)
                ReceivePackets();

            // Client-side reconnect heartbeat: resend Connect every 2 s until the host confirms
            if (!IsHost && IsConnected && RemoteID != CSteamID.Nil)
            {
                _reconnectTimer += Time.deltaTime;
                if (_reconnectTimer >= 2f)
                {
                    _reconnectTimer = 0f;
                    SendPacket(PacketType.Connect, new byte[0]);
                    Plugin.Log.LogInfo("Reenviando Connect...");
                }
            }

            // Try to initialise the remote avatar every frame until the game has loaded
            if (IsConnected && MainGame.game_started)
                RemotePlayerAvatar.Instance?.InitAvatar();
        }

        // --- Packet sending ---

        // Sends a position update (10 bytes: type byte + 3 floats).
        public static void SendPosition(Vector3 pos)
        {
            if (!IsConnected) return;

            byte[] data = new byte[13];
            data[0] = 10; // PacketType.Position
            Buffer.BlockCopy(BitConverter.GetBytes(pos.x), 0, data, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(pos.y), 0, data, 5, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(pos.z), 0, data, 9, 4);
            SendRaw(data);
        }

        // Sends a zone change notification (type byte + UTF-8 zone ID string).
        public static void SendZone(string zoneId)
        {
            if (!IsConnected) return;

            byte[] strBytes = Encoding.UTF8.GetBytes(zoneId);
            byte[] data = new byte[1 + strBytes.Length];
            data[0] = 11; // PacketType.Zone
            Buffer.BlockCopy(strBytes, 0, data, 1, strBytes.Length);
            SendRaw(data);
        }

        // Prepends the packet type byte to a payload and sends it.
        private static void SendPacket(PacketType type, byte[] payload)
        {
            byte[] data = new byte[1 + payload.Length];
            data[0] = (byte)type;
            Buffer.BlockCopy(payload, 0, data, 1, payload.Length);
            SendRaw(data);
        }

        // Lowest-level send: delivers raw bytes to RemoteID via Steam P2P (reliable channel 0).
        public static void SendRaw(byte[] data)
        {
            SteamNetworking.SendP2PPacket(RemoteID, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable, 0);
        }

        // Clears the stored lobby ID (used when returning to the main menu).
        public static void ResetLobby()
        {
            _currentLobbyID = CSteamID.Nil;
        }

        // --- Packet receiving ---

        // Drains the Steam P2P receive queue and dispatches each packet to the right handler.
        private void ReceivePackets()
        {
            uint msgSize;
            while (SteamNetworking.IsP2PPacketAvailable(out msgSize, 0))
            {
                byte[] buffer = new byte[msgSize];
                CSteamID senderId;
                if (!SteamNetworking.ReadP2PPacket(buffer, msgSize, out msgSize, out senderId, 0))
                    continue;

                // Always accept the sender's P2P session to keep the channel open
                SteamNetworking.AcceptP2PSessionWithUser(senderId);

                // First packet from a new client: mark them as connected and sync world state
                if (IsHost && !IsConnected)
                {
                    IsConnected = true;
                    RemoteID = senderId;
                    Plugin.Log.LogInfo("Player conected: " + senderId.ToString());
                    SendPacket(PacketType.Connect, new byte[0]);
                    WorldStateSync.Instance?.SendWorldState();
                }

                // Dispatch based on the packet type discriminator in byte[0]
                PacketType type = (PacketType)buffer[0];

                if (type == PacketType.Connect)
                {
                    Plugin.Log.LogInfo("Connect package recived from: " + senderId.ToString());

                    // Host: initialise avatar now that the client confirmed the connection
                    if (IsHost && MainGame.game_started)
                        RemotePlayerAvatar.Instance?.InitAvatar();
                    // Client: host confirmed — stop the reconnect heartbeat
                    else if (!IsHost)
                    {
                        Plugin.Log.LogInfo("Host confirmed conection!");
                        _reconnectTimer = -999f; // Effectively disable the heartbeat
                        if (MainGame.game_started)
                            RemotePlayerAvatar.Instance?.InitAvatar();
                    }
                }
                else if (type == PacketType.Position)
                {
                    // Parse 3 floats starting at byte 1 and forward to the avatar
                    float x = BitConverter.ToSingle(buffer, 1);
                    float y = BitConverter.ToSingle(buffer, 5);
                    float z = BitConverter.ToSingle(buffer, 9);
                    RemotePlayerAvatar.Instance?.UpdatePosition(new Vector3(x, y, z));
                }
                else if (type == PacketType.Zone)
                {
                    string zone = Encoding.UTF8.GetString(buffer, 1, buffer.Length - 1);
                    Plugin.Log.LogInfo("Zone: " + zone);
                }
                else if (type == PacketType.WorldStateResponse)
                {
                    WorldStateSync.Instance?.ReceiveWorldState(buffer);
                }
                else if (type == PacketType.WorldObjectDestroyed)
                {
                    // Find the object by its unique_id and destroy it locally
                    long instanceId = WorldSyncHelper.ReadObjectDestroyed(buffer);
                    Plugin.Log.LogInfo("Received WorldObjectDestroyed: " + instanceId);
                    WorldGameObject obj = WorldMap.GetWorldGameObjectByUniqueId(instanceId, false);
                    if (obj != null)
                    {
                        PatchDestroyObject.IsRemoteDestruction = true;
                        obj.DestroyMe();
                        PatchDestroyObject.IsRemoteDestruction = false;
                    }
                    else
                    {
                        Plugin.Log.LogWarning("WorldObjectDestroyed: object not found, id=" + instanceId);
                    }
                }
                else if (type == PacketType.SaveRequest)
                {
                    SaveTransferManager.Instance?.OnSaveRequested();
                }
                else if (type == PacketType.SaveChunk)
                {
                    SaveTransferManager.Instance?.OnChunkReceived(buffer);
                }
                else if (type == PacketType.SaveComplete)
                {
                    SaveTransferManager.Instance?.OnTransferComplete(buffer);
                }
            }
        }

        // --- Singleton & state ---

        public static NetworkManager Instance;

        // This player's own Steam ID
        public static CSteamID MyID;

        // The Steam ID of the connected remote player
        public static CSteamID RemoteID;

        // True when this instance is the session host (lobby creator)
        public static bool IsHost = false;

        // True when a P2P session with the remote player is established
        public static bool IsConnected = false;

        // Steamworks callback handles (must be stored as fields to avoid GC collection)
        private CallResult<LobbyCreated_t> _lobbyCreated;
        private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
        private CallResult<LobbyEnter_t> _lobbyEntered;

        private static CSteamID _currentLobbyID = CSteamID.Nil;

        // Registered after lobby creation; required to accept P2P session requests
        private Callback<P2PSessionRequest_t> _p2PSessionRequest;

        // Elapsed time used by the client reconnect heartbeat
        private float _reconnectTimer = 0f;

        // --- Packet type definitions ---
        // The first byte of every packet identifies its type.
        // Values match those used across PacketHelper, WorldSyncHelper, and SaveTransferManager.
        public enum PacketType : byte
        {
            Connect = 0,
            Disconnect = 1,
            WorldStateRequest = 2,
            WorldStateResponse = 3,

            Position = 10,
            Zone = 11,

            WorldObjectChanged = 20,
            WorldObjectDestroyed = 21,
            WorldObjectCreated = 22,

            InventoryChanged = 30,
            ItemPickup = 31,
            ItemDrop = 32,

            CraftingStarted = 40,
            CraftingFinished = 41,
            CraftingCancelled = 42,

            MoneyChanged = 50,
            VendorPurchase = 51,

            TimeSync = 60,
            SleepRequest = 61,
            SleepConfirm = 62,

            DungeonFloorChanged = 70,
            DungeonObjectChanged = 71,
            DungeonEnemyState = 72,

            QuestUpdated = 80,

            LobbySettings = 90,

            CombatAction = 100,
            DamageDealt = 101,

            SaveRequest = 110,
            SaveChunk = 111,
            SaveComplete = 112
        }
    }
}
