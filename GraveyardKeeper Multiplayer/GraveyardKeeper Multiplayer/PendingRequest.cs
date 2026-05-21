using System;

namespace GraveyardKeeperMultiplayer
{
    // Plain data class representing a request that has been sent but not yet confirmed.
    // RequestManager keeps a dictionary of these, keyed by Id, and uses them to drive
    // the retry logic when the remote side does not respond within the timeout window.
    public class PendingRequest
    {
        // Unique ID assigned by RequestManager when the request is sent
        public uint Id;

        // The packet type of this request (used for logging and dispatching)
        public NetworkManager.PacketType Type;

        // Raw bytes of the request packet, kept so it can be resent on retry
        public byte[] Data;

        // Unity Time.time at which this request was (last) sent
        public float SentTime;

        // How many times this request has been retried so far
        public int RetryCount;
    }
}
