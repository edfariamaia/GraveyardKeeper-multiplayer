using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Static utility class for serialising and deserialising network packets.
    // Every method pair (SerializeX / DeserializeX) handles one packet type.
    // The first byte written is always the PacketType discriminator so the receiver
    // can identify the packet before parsing the rest of the payload.
    //
    // Uses BinaryWriter/BinaryReader over a MemoryStream for portable, type-safe I/O.
    public static class PacketHelper
    {
        // --- Position (type 10) ---
        // Encodes a Vector3 as three consecutive floats (12 bytes of payload).
        public static byte[] SerializePosition(Vector3 pos)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(10); // PacketType.Position
                w.Write(pos.x);
                w.Write(pos.y);
                w.Write(pos.z);
                return ms.ToArray();
            }
        }

        // Reads three floats from bytes 1–12 and returns a Vector3.
        public static Vector3 DeserializePosition(byte[] data)
        {
            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var r = new BinaryReader(ms))
                return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }

        // --- Zone (type 11) ---
        // Encodes a zone ID string using BinaryWriter's length-prefixed UTF-8 format.
        public static byte[] SerializeZone(string zoneId)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(11); // PacketType.Zone
                w.Write(zoneId);
                return ms.ToArray();
            }
        }

        public static string DeserializeZone(byte[] data)
        {
            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var r = new BinaryReader(ms))
                return r.ReadString();
        }

        // --- WorldObjectChanged (type 20) ---
        // Encodes an object ID and its new serialised state string.
        // Used to sync state changes (e.g. a chest being opened) to the remote player.
        public static byte[] SerializeWorldObjectChanged(string objectId, string newState)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(20); // PacketType.WorldObjectChanged
                w.Write(objectId);
                w.Write(newState);
                return ms.ToArray();
            }
        }

        // Returns a (objectId, newState) tuple from the raw packet bytes.
        public static (string objectId, string newState) DeserializeWorldObjectChanged(byte[] data)
        {
            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var r = new BinaryReader(ms))
                return (r.ReadString(), r.ReadString());
        }

        // --- TimeSync (type 60) ---
        // Encodes the current in-game time as a single float.
        // Used by the host to keep both clients at the same time of day.
        public static byte[] SerializeTimeSync(float gameTime)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(60); // PacketType.TimeSync
                w.Write(gameTime);
                return ms.ToArray();
            }
        }

        public static float DeserializeTimeSync(byte[] data)
        {
            using (var ms = new MemoryStream(data, 1, data.Length - 1))
            using (var r = new BinaryReader(ms))
                return r.ReadSingle();
        }
    }
}
