using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Manages ordered packet queues for both outgoing and incoming traffic.
    //
    // Outgoing queue: packets are stamped with a monotonically increasing sequence
    // number and flushed at a rate of at most MaxPacketsPerFrame per Unity Update,
    // preventing Steam from being overwhelmed in a single frame.
    //
    // Incoming queue: uses a SortedDictionary keyed by sequence number so that
    // packets arriving out of order are held until all preceding packets have been
    // delivered, maintaining strict in-order processing.
    public class PacketQueue : MonoBehaviour
    {
        // Creates the singleton MonoBehaviour. Called from PatchStartGame.
        public static void Create()
        {
            GameObject go = new GameObject("PacketQueue");
            Instance = go.AddComponent<PacketQueue>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // Stamps outgoing data with the next sequence number and pushes it onto
        // the outgoing queue. The sequence number is injected at bytes 1–4 so
        // the receiver can reorder packets that arrive out of order.
        public void EnqueueOutgoing(NetworkManager.PacketType type, byte[] data)
        {
            // Build new packet: [type (1)] [seqNum (4)] [original payload (rest)]
            byte[] stamped = new byte[data.Length + 4];
            stamped[0] = data[0]; // preserve the type byte
            Buffer.BlockCopy(BitConverter.GetBytes(_nextSequenceNumber), 0, stamped, 1, 4);
            Buffer.BlockCopy(data, 1, stamped, 5, data.Length - 1);

            var entry = new QueuedPacket
            {
                SequenceNumber = _nextSequenceNumber++,
                Type = type,
                Data = stamped,
                QueuedTime = Time.time
            };
            _outgoingQueue.Enqueue(entry);
        }

        // Places an arriving packet into the incoming buffer at its sequence position.
        // Duplicates and already-processed packets (seq < expected) are discarded.
        public void EnqueueIncoming(uint sequenceNumber, NetworkManager.PacketType type, byte[] data)
        {
            if (sequenceNumber < _expectedSequenceNumber) return; // already processed
            if (_incomingQueue.ContainsKey(sequenceNumber)) return; // duplicate

            _incomingQueue[sequenceNumber] = new QueuedPacket
            {
                SequenceNumber = sequenceNumber,
                Type = type,
                Data = data,
                QueuedTime = Time.time
            };
        }

        // Returns and removes the next in-order incoming packet, or null if the next
        // expected sequence number hasn't arrived yet.
        public QueuedPacket DequeueIncoming()
        {
            if (!_incomingQueue.ContainsKey(_expectedSequenceNumber))
                return null;

            var pkt = _incomingQueue[_expectedSequenceNumber];
            _incomingQueue.Remove(_expectedSequenceNumber);
            _expectedSequenceNumber++;
            return pkt;
        }

        // Returns true when the next expected packet is already in the buffer.
        public bool HasIncoming()
        {
            return _incomingQueue.ContainsKey(_expectedSequenceNumber);
        }

        // Flushes up to MaxPacketsPerFrame outgoing packets per Unity Update tick.
        private void Update()
        {
            int sent = 0;
            while (_outgoingQueue.Count > 0 && sent < MaxPacketsPerFrame)
            {
                QueuedPacket pkt = _outgoingQueue.Dequeue();
                NetworkManager.SendRaw(pkt.Data);
                sent++;
            }
        }

        // Resets both queues and sequence counters (e.g. on disconnect).
        public void Reset()
        {
            _nextSequenceNumber = 1U;
            _expectedSequenceNumber = 1U;
            _incomingQueue.Clear();
            _outgoingQueue.Clear();
        }

        public static PacketQueue Instance;

        // Next sequence number to stamp on an outgoing packet
        private uint _nextSequenceNumber = 1U;

        // Holds received packets keyed by sequence number for in-order delivery
        private SortedDictionary<uint, QueuedPacket> _incomingQueue = new SortedDictionary<uint, QueuedPacket>();

        // Sequence number of the next packet we expect to deliver from the incoming buffer
        private uint _expectedSequenceNumber = 1U;

        // Queue of packets ready to be sent (flushed in Update)
        private Queue<QueuedPacket> _outgoingQueue = new Queue<QueuedPacket>();

        // Hard cap on how many packets are sent per Update frame
        private const int MaxPacketsPerFrame = 10;
    }
}
