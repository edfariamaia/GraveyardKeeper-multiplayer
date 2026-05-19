using System;
using System.Text;
using BepInEx.Logging;
using Steamworks;
using UnityEngine;
using GraveyardKeeperMultiplayer;

namespace GraveyardKeeperMultiplayer
{
    // Token: 0x02000003 RID: 3
    public class NetworkManager : MonoBehaviour
    {
        // Token: 0x06000003 RID: 3 RVA: 0x00002074 File Offset: 0x00000274
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

        // Token: 0x06000004 RID: 4 RVA: 0x000020D4 File Offset: 0x000002D4
        private void Start()
        {
            this._lobbyCreated = CallResult<LobbyCreated_t>.Create(new CallResult<LobbyCreated_t>.APIDispatchDelegate(this.OnLobbyCreated));
            this._lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(new Callback<GameLobbyJoinRequested_t>.DispatchDelegate(this.OnLobbyJoinRequested));
            this._lobbyEntered = CallResult<LobbyEnter_t>.Create(new CallResult<LobbyEnter_t>.APIDispatchDelegate(this.OnLobbyEntered));
        }

        // Token: 0x06000005 RID: 5 RVA: 0x00002128 File Offset: 0x00000328
        public static void HostGame()
        {
            bool isConnected = NetworkManager.IsConnected;
            if (!isConnected)
            {
                bool flag = NetworkManager.IsHost && NetworkManager._currentLobbyID != CSteamID.Nil;
                if (flag)
                {
                    Plugin.Log.LogInfo("Lobby já existe, abrindo painel...");
                    SteamFriends.ActivateGameOverlayInviteDialog(NetworkManager._currentLobbyID);
                }
                else
                {
                    NetworkManager.IsHost = true;
                    Plugin.Log.LogInfo("Criando lobby...");
                    SteamAPICall_t hAPICall = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2);
                    NetworkManager.Instance._lobbyCreated.Set(hAPICall, null);
                }
            }
        }

        // Token: 0x06000006 RID: 6 RVA: 0x000021AC File Offset: 0x000003AC
        private void OnLobbyCreated(LobbyCreated_t result, bool failure)
        {
            bool flag = failure || result.m_eResult != EResult.k_EResultOK;
            if (flag)
            {
                Plugin.Log.LogError("Falha ao criar lobby!");
                NetworkManager.IsHost = false;
            }
            else
            {
                NetworkManager._currentLobbyID = new CSteamID(result.m_ulSteamIDLobby);
                ManualLogSource log = Plugin.Log;
                string str = "Lobby criado! ID: ";
                CSteamID currentLobbyID = NetworkManager._currentLobbyID;
                log.LogInfo(str + currentLobbyID.ToString());
                SteamMatchmaking.SetLobbyData(NetworkManager._currentLobbyID, "host", NetworkManager.MyID.ToString());
                SteamFriends.ActivateGameOverlayInviteDialog(NetworkManager._currentLobbyID);
                this._p2PSessionRequest = Callback<P2PSessionRequest_t>.Create(new Callback<P2PSessionRequest_t>.DispatchDelegate(this.OnP2PSessionRequest));
            }
        }

        // Token: 0x06000007 RID: 7 RVA: 0x00002264 File Offset: 0x00000464
        private void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            ManualLogSource log = Plugin.Log;
            string str = "P2P session request de: ";
            CSteamID steamIDRemote = request.m_steamIDRemote;
            log.LogInfo(str + steamIDRemote.ToString());
            SteamNetworking.AcceptP2PSessionWithUser(request.m_steamIDRemote);
        }

        // Token: 0x06000008 RID: 8 RVA: 0x000022A8 File Offset: 0x000004A8
        private void OnLobbyJoinRequested(GameLobbyJoinRequested_t result)
        {
            ManualLogSource log = Plugin.Log;
            string str = "Entrando no lobby: ";
            CSteamID steamIDLobby = result.m_steamIDLobby;
            log.LogInfo(str + steamIDLobby.ToString());
            SteamAPICall_t hAPICall = SteamMatchmaking.JoinLobby(result.m_steamIDLobby);
            this._lobbyEntered.Set(hAPICall, null);
        }

        // Token: 0x06000009 RID: 9 RVA: 0x000022FC File Offset: 0x000004FC
        private void OnLobbyEntered(LobbyEnter_t result, bool failure)
        {
            if (failure)
            {
                Plugin.Log.LogError("Falha ao entrar no lobby!");
            }
            else
            {
                CSteamID steamIDLobby = new CSteamID(result.m_ulSteamIDLobby);
                string lobbyData = SteamMatchmaking.GetLobbyData(steamIDLobby, "host");
                NetworkManager.RemoteID = new CSteamID(ulong.Parse(lobbyData));
                NetworkManager.IsHost = false;
                NetworkManager.IsConnected = true;
                ManualLogSource log = Plugin.Log;
                string str = "Entrou no lobby! Host: ";
                CSteamID remoteID = NetworkManager.RemoteID;
                log.LogInfo(str + remoteID.ToString());
                SaveTransferManager.IsPendingTransfer = true;
                NetworkManager.SendPacket(NetworkManager.PacketType.Connect, new byte[0]);
                SaveTransferManager.RequestSave();
            }
        }

        // Token: 0x0600000A RID: 10 RVA: 0x00002398 File Offset: 0x00000598
        private void Update()
        {
            bool flag = NetworkManager.IsHost || NetworkManager.IsConnected;
            if (flag)
            {
                this.ReceivePackets();
            }
            bool flag2 = !NetworkManager.IsHost && NetworkManager.IsConnected && NetworkManager.RemoteID != CSteamID.Nil;
            if (flag2)
            {
                this._reconnectTimer += Time.deltaTime;
                bool flag3 = this._reconnectTimer >= 2f;
                if (flag3)
                {
                    this._reconnectTimer = 0f;
                    NetworkManager.SendPacket(NetworkManager.PacketType.Connect, new byte[0]);
                    Plugin.Log.LogInfo("Reenviando Connect...");
                }
            }
            bool flag4 = NetworkManager.IsConnected && MainGame.game_started;
            if (flag4)
            {
                RemotePlayerAvatar instance = RemotePlayerAvatar.Instance;
                if (instance != null)
                {
                    instance.InitAvatar();
                }
            }
        }

        // Token: 0x0600000B RID: 11 RVA: 0x0000245C File Offset: 0x0000065C
        public static void SendPosition(Vector3 pos)
        {
            bool flag = !NetworkManager.IsConnected;
            if (!flag)
            {
                byte[] array = new byte[13];
                array[0] = 10;
                Buffer.BlockCopy(BitConverter.GetBytes(pos.x), 0, array, 1, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pos.y), 0, array, 5, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(pos.z), 0, array, 9, 4);
                NetworkManager.SendRaw(array);
            }
        }

        // Token: 0x0600000C RID: 12 RVA: 0x000024CC File Offset: 0x000006CC
        public static void SendZone(string zoneId)
        {
            bool flag = !NetworkManager.IsConnected;
            if (!flag)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(zoneId);
                byte[] array = new byte[1 + bytes.Length];
                array[0] = 11;
                Buffer.BlockCopy(bytes, 0, array, 1, bytes.Length);
                NetworkManager.SendRaw(array);
            }
        }

        // Token: 0x0600000D RID: 13 RVA: 0x00002518 File Offset: 0x00000718
        private static void SendPacket(NetworkManager.PacketType type, byte[] payload)
        {
            byte[] array = new byte[1 + payload.Length];
            array[0] = (byte)type;
            Buffer.BlockCopy(payload, 0, array, 1, payload.Length);
            NetworkManager.SendRaw(array);
        }

        // Token: 0x0600000E RID: 14 RVA: 0x00002549 File Offset: 0x00000749
        public static void SendRaw(byte[] data)
        {
            SteamNetworking.SendP2PPacket(NetworkManager.RemoteID, data, (uint)data.Length, EP2PSend.k_EP2PSendReliable, 0);
        }

        // Token: 0x0600000F RID: 15 RVA: 0x0000255D File Offset: 0x0000075D
        public static void ResetLobby()
        {
            NetworkManager._currentLobbyID = CSteamID.Nil;
        }

        // Token: 0x06000010 RID: 16 RVA: 0x0000256C File Offset: 0x0000076C
        private void ReceivePackets()
        {
            uint num;
            while (SteamNetworking.IsP2PPacketAvailable(out num, 0))
            {
                byte[] array = new byte[num];
                CSteamID csteamID;
                bool flag = !SteamNetworking.ReadP2PPacket(array, num, out num, out csteamID, 0);
                if (!flag)
                {
                    SteamNetworking.AcceptP2PSessionWithUser(csteamID);
                    bool flag2 = NetworkManager.IsHost && !NetworkManager.IsConnected;
                    if (flag2)
                    {
                        NetworkManager.IsConnected = true;
                        NetworkManager.RemoteID = csteamID;
                        ManualLogSource log = Plugin.Log;
                        string str = "Player conected: ";
                        CSteamID csteamID2 = csteamID;
                        log.LogInfo(str + csteamID2.ToString());
                        NetworkManager.SendPacket(NetworkManager.PacketType.Connect, new byte[0]);
                        WorldStateSync instance = WorldStateSync.Instance;
                        if (instance != null)
                        {
                            instance.SendWorldState();
                        }
                    }
                    NetworkManager.PacketType packetType = (NetworkManager.PacketType)array[0];
                    bool flag3 = packetType == NetworkManager.PacketType.Connect;
                    if (flag3)
                    {
                        ManualLogSource log2 = Plugin.Log;
                        string str2 = "Connect package recived from: ";
                        CSteamID csteamID2 = csteamID;
                        log2.LogInfo(str2 + csteamID2.ToString());
                        bool flag4 = NetworkManager.IsHost && MainGame.game_started;
                        if (flag4)
                        {
                            RemotePlayerAvatar instance2 = RemotePlayerAvatar.Instance;
                            if (instance2 != null)
                            {
                                instance2.InitAvatar();
                            }
                        }
                        else
                        {
                            bool flag5 = !NetworkManager.IsHost;
                            if (flag5)
                            {
                                Plugin.Log.LogInfo("Host confirmed conection!");
                                this._reconnectTimer = -999f;
                                bool game_started = MainGame.game_started;
                                if (game_started)
                                {
                                    RemotePlayerAvatar instance3 = RemotePlayerAvatar.Instance;
                                    if (instance3 != null)
                                    {
                                        instance3.InitAvatar();
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        bool flag6 = packetType == NetworkManager.PacketType.Position;
                        if (flag6)
                        {
                            float x = BitConverter.ToSingle(array, 1);
                            float y = BitConverter.ToSingle(array, 5);
                            float z = BitConverter.ToSingle(array, 9);
                            RemotePlayerAvatar instance4 = RemotePlayerAvatar.Instance;
                            if (instance4 != null)
                            {
                                instance4.UpdatePosition(new Vector3(x, y, z));
                            }
                        }
                        else
                        {
                            bool flag7 = packetType == NetworkManager.PacketType.Zone;
                            if (flag7)
                            {
                                string @string = Encoding.UTF8.GetString(array, 1, array.Length - 1);
                                Plugin.Log.LogInfo("Zone: " + @string);
                            }
                            else
                            {
                                bool flag8 = packetType == NetworkManager.PacketType.WorldStateResponse;
                                if (flag8)
                                {
                                    WorldStateSync instance5 = WorldStateSync.Instance;
                                    if (instance5 != null)
                                    {
                                        instance5.ReceiveWorldState(array);
                                    }
                                }
                                else
                                {
                                    bool flag9 = packetType == NetworkManager.PacketType.WorldObjectDestroyed;
                                    if (flag9)
                                    {
                                        long instance_id = WorldSyncHelper.ReadObjectDestroyed(array);
                                        Plugin.Log.LogInfo("Received WorldObjectDestroyed: " + instance_id.ToString());
                                        WorldGameObject worldGameObjectByUniqueId = WorldMap.GetWorldGameObjectByUniqueId(instance_id, false);
                                        bool flag10 = worldGameObjectByUniqueId != null;
                                        if (flag10)
                                        {
                                            PatchDestroyObject.IsRemoteDestruction = true;
                                            worldGameObjectByUniqueId.DestroyMe();
                                            PatchDestroyObject.IsRemoteDestruction = false;
                                        }
                                        else
                                        {
                                            Plugin.Log.LogWarning("WorldObjectDestroyed: object not found, id=" + instance_id.ToString());
                                        }
                                    }
                                    else
                                    {
                                        bool flag11 = packetType == NetworkManager.PacketType.SaveRequest;
                                        if (flag11)
                                        {
                                            SaveTransferManager instance6 = SaveTransferManager.Instance;
                                            if (instance6 != null)
                                            {
                                                instance6.OnSaveRequested();
                                            }
                                        }
                                        else
                                        {
                                            bool flag12 = packetType == NetworkManager.PacketType.SaveChunk;
                                            if (flag12)
                                            {
                                                SaveTransferManager instance7 = SaveTransferManager.Instance;
                                                if (instance7 != null)
                                                {
                                                    instance7.OnChunkReceived(array);
                                                }
                                            }
                                            else
                                            {
                                                bool flag13 = packetType == NetworkManager.PacketType.SaveComplete;
                                                if (flag13)
                                                {
                                                    SaveTransferManager instance8 = SaveTransferManager.Instance;
                                                    if (instance8 != null)
                                                    {
                                                        instance8.OnTransferComplete(array);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Token: 0x04000001 RID: 1
        public static NetworkManager Instance;

        // Token: 0x04000002 RID: 2
        public static CSteamID MyID;

        // Token: 0x04000003 RID: 3
        public static CSteamID RemoteID;

        // Token: 0x04000004 RID: 4
        public static bool IsHost = false;

        // Token: 0x04000005 RID: 5
        public static bool IsConnected = false;

        // Token: 0x04000006 RID: 6
        private CallResult<LobbyCreated_t> _lobbyCreated;

        // Token: 0x04000007 RID: 7
        private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;

        // Token: 0x04000008 RID: 8
        private CallResult<LobbyEnter_t> _lobbyEntered;

        // Token: 0x04000009 RID: 9
        private static CSteamID _currentLobbyID = CSteamID.Nil;

        // Token: 0x0400000A RID: 10
        private Callback<P2PSessionRequest_t> _p2PSessionRequest;

        // Token: 0x0400000B RID: 11
        private float _reconnectTimer = 0f;

        // Token: 0x0200001B RID: 27
        public enum PacketType : byte
        {
            // Token: 0x0400004F RID: 79
            Connect,
            // Token: 0x04000050 RID: 80
            Disconnect,
            // Token: 0x04000051 RID: 81
            WorldStateRequest,
            // Token: 0x04000052 RID: 82
            WorldStateResponse,
            // Token: 0x04000053 RID: 83
            Position = 10,
            // Token: 0x04000054 RID: 84
            Zone,
            // Token: 0x04000055 RID: 85
            WorldObjectChanged = 20,
            // Token: 0x04000056 RID: 86
            WorldObjectDestroyed,
            // Token: 0x04000057 RID: 87
            WorldObjectCreated,
            // Token: 0x04000058 RID: 88
            InventoryChanged = 30,
            // Token: 0x04000059 RID: 89
            ItemPickup,
            // Token: 0x0400005A RID: 90
            ItemDrop,
            // Token: 0x0400005B RID: 91
            CraftingStarted = 40,
            // Token: 0x0400005C RID: 92
            CraftingFinished,
            // Token: 0x0400005D RID: 93
            CraftingCancelled,
            // Token: 0x0400005E RID: 94
            MoneyChanged = 50,
            // Token: 0x0400005F RID: 95
            VendorPurchase,
            // Token: 0x04000060 RID: 96
            TimeSync = 60,
            // Token: 0x04000061 RID: 97
            SleepRequest,
            // Token: 0x04000062 RID: 98
            SleepConfirm,
            // Token: 0x04000063 RID: 99
            DungeonFloorChanged = 70,
            // Token: 0x04000064 RID: 100
            DungeonObjectChanged,
            // Token: 0x04000065 RID: 101
            DungeonEnemyState,
            // Token: 0x04000066 RID: 102
            QuestUpdated = 80,
            // Token: 0x04000067 RID: 103
            LobbySettings = 90,
            // Token: 0x04000068 RID: 104
            CombatAction = 100,
            // Token: 0x04000069 RID: 105
            DamageDealt,
            // Token: 0x0400006A RID: 106
            SaveRequest = 110,
            // Token: 0x0400006B RID: 107
            SaveChunk,
            // Token: 0x0400006C RID: 108
            SaveComplete
        }
    }
}
