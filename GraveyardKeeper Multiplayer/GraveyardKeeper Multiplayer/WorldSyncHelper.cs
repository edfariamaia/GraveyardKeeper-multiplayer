using System;
using System.IO;

namespace GraveyardKeeperMultiplayer
{
    // Low-level serialisation helpers for world-object sync packets.
    // Keeps the binary format for each packet type in one place so that both the
    // sending side and the receiving side always agree on the layout.
    public static class WorldSyncHelper
    {
        // Builds and sends a WorldObjectDestroyed packet (type 21) containing the
        // unique_id of the object that was just destroyed on this client.
        // The receiver looks up the object by unique_id and calls DestroyMe locally.
        public static void SendObjectDestroyed(long uniqueId)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(21); // PacketType.WorldObjectDestroyed
                w.Write(uniqueId);
                NetworkManager.SendRaw(ms.ToArray());
            }
            Plugin.Log.LogInfo("Sent WorldObjectDestroyed: " + uniqueId);
        }

        // Parses a WorldObjectDestroyed packet and returns the encoded unique_id.
        // Skips byte[0] (the type discriminator) and reads the 8-byte long.
        public static long ReadObjectDestroyed(byte[] data)
        {
            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var r = new BinaryReader(ms))
                return r.ReadInt64();
        }
    }
}
