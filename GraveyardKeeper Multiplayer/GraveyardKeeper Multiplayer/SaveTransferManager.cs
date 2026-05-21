using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Handles the full save-file transfer from host to client over Steam P2P.
    //
    // When a client connects, it requests the host's current save so both players start
    // from the same world state. Because a save file can be several megabytes and Steam
    // P2P packets are capped at ~1 MB, the transfer is split into chunks of ChunkSize
    // (65 536 bytes). After all chunks are sent, a SaveComplete packet is sent to signal
    // the end of the transfer.
    //
    // Transfer flow:
    //   Client sends SaveRequest → Host receives it → Host calls SendSave()
    //   Host sends N × SaveChunk packets → Host sends 1 × SaveComplete packet
    //   Client receives chunks (OnChunkReceived) → Client receives SaveComplete (OnTransferComplete)
    //   Client reassembles chunks → Client calls ApplySave()
    //   ApplySave writes the save to disk, loads it via the game's own StartPlayingGame flow.
    //
    // The transfer is deferred if the host's game hasn't fully started yet (game_started == false).
    public class SaveTransferManager : MonoBehaviour
    {
        // Creates the singleton MonoBehaviour. Called from PatchStartGame.
        public static void Create()
        {
            if (Instance != null) return;

            GameObject go = new GameObject("SaveTransferManager");
            Instance = go.AddComponent<SaveTransferManager>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // Per-frame check: if a send was requested before the game was ready, retry now.
        private void Update()
        {
            if (_pendingSendRequest && NetworkManager.IsHost && MainGame.game_started)
            {
                _pendingSendRequest = false;
                SendSave();
            }
        }

        // Called by NetworkManager when a SaveRequest packet arrives on the host.
        // If the game isn't started yet, defers the send to the next Update tick.
        public void OnSaveRequested()
        {
            if (!NetworkManager.IsHost) return;

            if (!MainGame.game_started)
            {
                Plugin.Log.LogInfo("Save requested but game not started yet, deferring...");
                _pendingSendRequest = true;
            }
            else
            {
                SendSave();
            }
        }

        // Serialises the current save to binary, splits it into ChunkSize chunks, and
        // sends each chunk as a SaveChunk packet, followed by a SaveComplete packet.
        // Also logs whether the talking_skull NPC is present in the world (debug check).
        private void SendSave()
        {
            Plugin.Log.LogInfo("Client requested save, serializing...");

            // Debug: check if talking_skull exists in the world object list before serialisation
            FieldInfo objsField = typeof(WorldMap).GetField("_objs", BindingFlags.Static | BindingFlags.NonPublic);
            var objs = objsField?.GetValue(null) as List<WorldGameObject>;
            if (objs != null)
            {
                foreach (WorldGameObject obj in objs)
                {
                    if (obj != null && obj.obj_id == "talking_skull")
                        Plugin.Log.LogInfo("talking_skull found: unique_id=" + obj.unique_id + " pos=" + obj.transform.position);
                }
            }

            // Serialise the entire save to a byte array
            byte[] saveBytes = MainGame.me.save.ToBinary();
            Plugin.Log.LogInfo("Save size: " + saveBytes.Length + " bytes");

            int chunkCount = Mathf.CeilToInt((float)saveBytes.Length / ChunkSize);
            Plugin.Log.LogInfo("Sending save in " + chunkCount + " chunks...");

            // Send one SaveChunk packet per chunk
            for (int i = 0; i < chunkCount; i++)
            {
                int offset    = i * ChunkSize;
                int chunkLen  = Mathf.Min(ChunkSize, saveBytes.Length - offset);

                using (var ms = new MemoryStream())
                using (var w  = new BinaryWriter(ms))
                {
                    w.Write(111);        // PacketType.SaveChunk
                    w.Write(chunkCount); // total number of chunks (so client can pre-allocate)
                    w.Write(saveBytes.Length); // total file size in bytes
                    w.Write(i);          // zero-based chunk index
                    w.Write(chunkLen);   // bytes in this chunk
                    w.Write(saveBytes, offset, chunkLen);
                    NetworkManager.SendRaw(ms.ToArray());
                }
                Plugin.Log.LogInfo("Sent chunk " + (i + 1) + "/" + chunkCount);
            }

            // Send the transfer-complete signal
            using (var ms = new MemoryStream())
            using (var w  = new BinaryWriter(ms))
            {
                w.Write(112);        // PacketType.SaveComplete
                w.Write(chunkCount);
                w.Write(saveBytes.Length);
                NetworkManager.SendRaw(ms.ToArray());
            }
            Plugin.Log.LogInfo("Save transfer complete.");
        }

        // Sends a SaveRequest packet to the host. Called by the client right after joining.
        public static void RequestSave()
        {
            Plugin.Log.LogInfo("Requesting save from host...");
            using (var ms = new MemoryStream())
            using (var w  = new BinaryWriter(ms))
            {
                w.Write(110); // PacketType.SaveRequest
                NetworkManager.SendRaw(ms.ToArray());
            }
        }

        // Called by NetworkManager each time a SaveChunk packet arrives on the client.
        // Reads the chunk metadata and stores the payload at its correct index in the buffer.
        public void OnChunkReceived(byte[] data)
        {
            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var r  = new BinaryReader(ms))
            {
                int totalChunks       = r.ReadInt32();
                int totalExpectedBytes = r.ReadInt32();
                int chunkIndex        = r.ReadInt32();
                int chunkLen          = r.ReadInt32();
                byte[] chunkData      = r.ReadBytes(chunkLen);

                // Initialise the receive buffer on the first chunk
                if (_expectedChunkCount < 0)
                {
                    _expectedChunkCount  = totalChunks;
                    _totalExpectedBytes  = totalExpectedBytes;
                    _receivedChunks      = new List<byte[]>(new byte[totalChunks][]);
                    Plugin.Log.LogInfo("Save transfer started: " + totalChunks + " chunks, " + totalExpectedBytes + " bytes");
                }

                _receivedChunks[chunkIndex] = chunkData;
                Plugin.Log.LogInfo("Received chunk " + (chunkIndex + 1) + "/" + _expectedChunkCount);
            }
        }

        // Called by NetworkManager when the SaveComplete packet arrives on the client.
        // Verifies that all chunks were received, then reassembles and applies the save.
        public void OnTransferComplete(byte[] data)
        {
            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var r  = new BinaryReader(ms))
            {
                int chunks = r.ReadInt32();
                int bytes  = r.ReadInt32();
                Plugin.Log.LogInfo("Save transfer complete signal received. Expected: " + chunks + " chunks, " + bytes + " bytes");
            }

            // Sanity check: every slot must have been filled
            for (int i = 0; i < _receivedChunks.Count; i++)
            {
                if (_receivedChunks[i] == null)
                {
                    Plugin.Log.LogError("Missing chunk " + i + " — save transfer incomplete!");
                    return;
                }
            }

            byte[] fullSave = ReassembleChunks();
            Plugin.Log.LogInfo("Save reassembled: " + fullSave.Length + " bytes");
            ApplySave(fullSave);
        }

        // Concatenates all received chunk payloads into a single byte array.
        private byte[] ReassembleChunks()
        {
            using (var ms = new MemoryStream(_totalExpectedBytes))
            {
                foreach (byte[] chunk in _receivedChunks)
                    ms.Write(chunk, 0, chunk.Length);
                return ms.ToArray();
            }
        }

        // Deserialises the received save, writes it to disk as "mp_host.dat",
        // then loads it by setting it on MainGame and triggering StartPlayingGame.
        // Also calls RestoreSceneToInitialState so the scene is clean before the
        // save's map data is applied.
        private void ApplySave(byte[] saveData)
        {
            try
            {
                GameSave gameSave = GameSave.FromBinary(saveData);
                if (gameSave == null)
                {
                    Plugin.Log.LogError("ApplySave: GameSave.FromBinary returned null!");
                    return;
                }

                // Write the raw bytes to disk so the game can reference a real file
                const string saveName = "mp_host";
                string savePath = PlatformSpecific.GetSaveFolder() + saveName + ".dat";
                File.WriteAllBytes(savePath, saveData);
                Plugin.Log.LogInfo("Save written to disk: " + savePath);

                // Build a SaveSlotData pointing to the temporary file.
                // filename_no_extension is private, so set it via reflection.
                var slot = new SaveSlotData();
                FieldInfo fnField = typeof(SaveSlotData).GetField(
                    "filename_no_extension",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                fnField?.SetValue(slot, saveName);
                slot.linked_save = gameSave;

                // Inject the save and slot into the running game
                MainGame.me.save      = gameSave;
                MainGame.me.save_slot = slot;

                // Clear the scene to its default state before applying the save's map
                MainGame.me.save.map.RestoreSceneToInitialState();

                IsPendingTransfer = false;
                Plugin.Log.LogInfo("Loading host save...");

                // Use the game's standard loading screen flow to transition into the game
                LoadingGUI.Show(delegate
                {
                    LoadingGUI.ShowBlackBackground(true, false);
                    GUIElements.me.saves.Hide(false);
                    GUIElements.me.main_menu.Hide(false);
                    GUIElements.me.hud.Hide();
                    GUIElements.me.saves.StartPlayingGame();
                });
                LoadingGUI.LinkAsyncProcess(null);
                LoadingGUI.ShowProgressBar();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ApplySave error: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        // Resets all transfer state (called on disconnect or menu return).
        public void Reset()
        {
            _pendingSendRequest  = false;
            _receivedChunks.Clear();
            _expectedChunkCount  = -1;
            _totalExpectedBytes  = -1;
        }

        public static SaveTransferManager Instance;

        // Maximum bytes per chunk (65 536 = 64 KB)
        private const int ChunkSize = 65536;

        // True when the client requested a save before the host's game was started
        private bool _pendingSendRequest = false;

        // True while a save transfer is in progress (prevents UI reset mid-load)
        public static bool IsPendingTransfer;

        // Buffer that receives chunks as they arrive; slots are filled by index
        private List<byte[]> _receivedChunks = new List<byte[]>();

        // Total number of chunks the host said it would send (-1 = not yet known)
        private int _expectedChunkCount = -1;

        // Total file size in bytes (-1 = not yet known)
        private int _totalExpectedBytes = -1;
    }
}
