using System;

namespace GraveyardKeeperMultiplayer
{
    // Plain data class representing a packet that has been placed in the PacketQueue.
    // Both the outgoing queue (packets we need to send) and the incoming queue
    // (packets waiting to be processed in order) use this type.
    public class QueuedPacket
    {
        // Monotonically increasing number used to enforce packet ordering
        public uint SequenceNumber;

        // Identifies what kind of data this packet carries
        public NetworkManager.PacketType Type;

        // Raw bytes of the packet, including the type discriminator in byte[0]
        public byte[] Data;

        // Unity Time.time at which the packet was enqueued — used to detect stale packets
        public float QueuedTime;
    }
}
