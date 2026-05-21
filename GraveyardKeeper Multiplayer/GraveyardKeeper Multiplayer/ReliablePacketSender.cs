using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Low-level reliable packet delivery layer sitting directly above NetworkManager.SendRaw.
    //
    // Although Steam P2P's k_EP2PSendReliable already guarantees delivery, this class
    // provides an explicit application-level acknowledgement system for packets that are
    // critical enough to require confirmed delivery (e.g. world state changes that must
    // not be lost even during reconnects or Steam hiccups).
    //
    // Workflow:
    //   Sender calls SendReliable(data) → gets back a sequence number.
    //   Receiver processes the packet and calls SendAcknowledgement(seqNum) back.
    //   Sender receives the ack and calls Acknowledge(seqNum) to stop retrying.
    //   If no ack arrives within ResendInterval, the packet is resent up to MaxResendAttempts.
    public class ReliablePacketSender : MonoBehaviour
    {
        // Creates the singleton MonoBehaviour. Called from PatchStartGame.
        public static void Create()
        {
            GameObject go = new GameObject("ReliablePacketSender");
            Instance = go.AddComponent<ReliablePacketSender>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // Stamps data with a sequence number, registers it as unacknowledged, and sends it.
        // Returns the assigned sequence number so the caller can correlate acknowledgements.
        public uint SendReliable(byte[] data)
        {
            uint seq = _nextSequence++;

            // Build packet: [type (1)] [seqNum (4)] [original payload (rest)]
            byte[] stamped = new byte[data.Length + 4];
            stamped[0] = data[0];
            Buffer.BlockCopy(BitConverter.GetBytes(seq), 0, stamped, 1, 4);
            Buffer.BlockCopy(data, 1, stamped, 5, data.Length - 1);

            _unacknowledged[seq] = new TrackedPacket
            {
                SequenceNumber = seq,
                Data = stamped,
                LastSentTime = Time.time,
                Attempts = 1
            };

            NetworkManager.SendRaw(stamped);
            return seq;
        }

        // Called when the remote side sends back an acknowledgement for this sequence number.
        // Removes the tracked entry, stopping further resend attempts.
        public void Acknowledge(uint sequenceNumber)
        {
            if (_unacknowledged.ContainsKey(sequenceNumber))
            {
                _unacknowledged.Remove(sequenceNumber);
                Plugin.Log.LogInfo("Packet acknowledged: " + sequenceNumber);
            }
        }

        // Sends a small acknowledgement packet back to the remote sender.
        // Format: [0x00 (type byte)] [seqNum (4 bytes)]
        public void SendAcknowledgement(uint sequenceNumber)
        {
            byte[] ack = new byte[5];
            ack[0] = 0; // Reusing Connect (0) as ack discriminator — TODO: add a dedicated Ack type
            Buffer.BlockCopy(BitConverter.GetBytes(sequenceNumber), 0, ack, 1, 4);
            NetworkManager.SendRaw(ack);
        }

        // Clears all unacknowledged packets and resets the sequence counter.
        public void Reset()
        {
            _unacknowledged.Clear();
            _nextSequence = 1U;
        }

        // Per-frame resend loop. Any packet that hasn't been acknowledged within
        // ResendInterval is resent. After MaxResendAttempts the packet is dropped
        // and a warning is logged.
        private void Update()
        {
            var toRemove = new List<uint>();

            foreach (var kv in _unacknowledged)
            {
                TrackedPacket pkt = kv.Value;
                if (Time.time - pkt.LastSentTime < ResendInterval) continue;

                if (pkt.Attempts >= MaxResendAttempts)
                {
                    Plugin.Log.LogWarning("Packet dropped after max attempts: " + pkt.SequenceNumber);
                    toRemove.Add(pkt.SequenceNumber);
                }
                else
                {
                    pkt.Attempts++;
                    pkt.LastSentTime = Time.time;
                    NetworkManager.SendRaw(pkt.Data);
                    Plugin.Log.LogInfo("Resending packet: " + pkt.SequenceNumber + " attempt: " + pkt.Attempts);
                }
            }

            foreach (uint seq in toRemove)
                _unacknowledged.Remove(seq);
        }

        public static ReliablePacketSender Instance;

        // Seconds between resend attempts for an unacknowledged packet
        private const float ResendInterval = 1.5f;

        // Maximum times a packet is resent before being discarded
        private const int MaxResendAttempts = 5;

        // All packets sent but not yet acknowledged, keyed by sequence number
        private Dictionary<uint, TrackedPacket> _unacknowledged = new Dictionary<uint, TrackedPacket>();

        // Next sequence number to assign on SendReliable
        private uint _nextSequence = 1U;

        // Internal tracking record for a single unacknowledged packet
        private class TrackedPacket
        {
            public uint SequenceNumber;
            public byte[] Data;
            public float LastSentTime;
            public int Attempts;
        }
    }
}
