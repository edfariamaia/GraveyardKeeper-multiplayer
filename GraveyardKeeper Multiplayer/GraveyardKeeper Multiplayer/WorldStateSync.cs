using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Handles initial world-state synchronisation when a client connects.
    //
    // When the host receives the first Connect packet from a client it calls
    // SendWorldState(), which serialises the current list of destroyed world objects
    // (plus game time and day) into a WorldStateResponse packet and sends it to the client.
    //
    // The client receives the packet, applies all the destructions locally (using the
    // IsRemoteDestruction flag to avoid echoing them back), and sets WorldStateReceived.
    //
    // WorldMap._objs is a private static field, so it is accessed via reflection.
    public class WorldStateSync : MonoBehaviour
    {
        // Creates the singleton MonoBehaviour. Called from PatchStartGame.
        public static void Create()
        {
            GameObject go = new GameObject("WorldStateSync");
            Instance = go.AddComponent<WorldStateSync>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // Lazily resolves and caches the private WorldMap._objs field via reflection.
        private static List<WorldGameObject> GetWorldObjects()
        {
            if (_objsField == null)
            {
                _objsField = typeof(WorldMap).GetField("_objs", BindingFlags.Static | BindingFlags.NonPublic);
                if (_objsField == null)
                {
                    Plugin.Log.LogError("WorldStateSync: could not find WorldMap._objs via reflection!");
                    return new List<WorldGameObject>();
                }
            }
            return (_objsField.GetValue(null) as List<WorldGameObject>) ?? new List<WorldGameObject>();
        }

        // Called by the host (via NetworkManager.ReceivePackets) immediately after a client
        // connects. Builds and sends a WorldStateResponse packet containing:
        //   - current game time (float)
        //   - current day (int)
        //   - host player position (Vector3)
        //   - list of destroyed object unique_ids (int count + n longs)
        public void SendWorldState()
        {
            if (!NetworkManager.IsHost) return;
            if (!MainGame.game_started) return;

            Plugin.Log.LogInfo("Sending world state to client...");

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(3); // PacketType.WorldStateResponse

                w.Write(MainGame.game_time);
                w.Write(MainGame.me.save.day);

                Vector3 pos = MainGame.me.player_pos;
                w.Write(pos.x);
                w.Write(pos.y);
                w.Write(pos.z);

                List<long> destroyed = GetDestroyedObjectIds();
                w.Write(destroyed.Count);
                foreach (long id in destroyed)
                    w.Write(id);

                Plugin.Log.LogInfo("Destroyed objects to sync: " + destroyed.Count);

                byte[] packet = ms.ToArray();
                NetworkManager.SendRaw(packet);
                Plugin.Log.LogInfo("World state sent. Size: " + packet.Length + " bytes");
            }
        }

        // Iterates all world objects and collects the unique_ids of those marked as removed.
        // Only objects with a valid (> 0) unique_id are included.
        private List<long> GetDestroyedObjectIds()
        {
            var ids = new List<long>();
            foreach (WorldGameObject obj in GetWorldObjects())
            {
                if (obj == null) continue;
                if (!obj.is_removed) continue;
                if (obj.unique_id <= 0L) continue;
                ids.Add(obj.unique_id);
            }
            return ids;
        }

        // Called on the client when a WorldStateResponse packet is received.
        // Parses the packet and calls DestroyMe on each destroyed object so the client
        // world matches the host's current state. Uses IsRemoteDestruction to prevent
        // PatchDestroyObject from echoing the destructions back to the host.
        public void ReceiveWorldState(byte[] data)
        {
            if (NetworkManager.IsHost) return;

            Plugin.Log.LogInfo("Receiving world state from host...");

            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var r = new BinaryReader(ms))
            {
                float gameTime = r.ReadSingle();
                Plugin.Log.LogInfo("Host game time: " + gameTime);

                int day = r.ReadInt32();
                Plugin.Log.LogInfo("Host day: " + day);

                float px = r.ReadSingle();
                float py = r.ReadSingle();
                float pz = r.ReadSingle();
                Plugin.Log.LogInfo("Host position: " + px + ", " + py);

                int count = r.ReadInt32();
                Plugin.Log.LogInfo("Destroyed objects to apply: " + count);

                int applied = 0, notFound = 0;
                for (int i = 0; i < count; i++)
                {
                    long uid = r.ReadInt64();
                    WorldGameObject obj = WorldMap.GetWorldGameObjectByUniqueId(uid, true);
                    if (obj != null && !obj.is_removed)
                    {
                        PatchDestroyObject.IsRemoteDestruction = true;
                        obj.DestroyMe();
                        PatchDestroyObject.IsRemoteDestruction = false;
                        applied++;
                    }
                    else
                    {
                        notFound++;
                    }
                }

                Plugin.Log.LogInfo("World state applied: " + applied + " destroyed, " + notFound + " not found");
            }

            WorldStateReceived = true;
            Plugin.Log.LogInfo("World state received and applied!");
        }

        public static WorldStateSync Instance;

        // Set to true on the client once the first WorldStateResponse has been processed
        public static bool WorldStateReceived;

        // Cached reflection reference to WorldMap._objs (private static field)
        private static FieldInfo _objsField;
    }
}
