using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Application-level request/confirmation system layered on top of raw packet sending.
    //
    // When an action requires a response from the remote side (e.g. asking the host to
    // confirm a world change), use SendRequest instead of NetworkManager.SendRaw. The
    // manager assigns a unique request ID, stores a PendingRequest entry, and retries
    // the packet every RetryInterval seconds up to MaxRetries times.
    //
    // The remote side must call ConfirmRequest(id) or RejectRequest(id, reason) to
    // remove the entry and stop the retry loop.
    public class RequestManager : MonoBehaviour
    {
        // Creates the singleton MonoBehaviour. Called from PatchStartGame.
        public static void Create()
        {
            GameObject go = new GameObject("RequestManager");
            Instance = go.AddComponent<RequestManager>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // Sends a request packet, registers it as pending, and returns the assigned ID.
        // The request ID is injected at bytes 1–4 so the remote side can include it in
        // the confirmation/rejection response.
        public uint SendRequest(NetworkManager.PacketType type, byte[] data)
        {
            uint id = _nextRequestId++;

            // Build packet: [type (1)] [requestId (4)] [original payload (rest)]
            byte[] stamped = new byte[data.Length + 4];
            stamped[0] = data[0];
            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, stamped, 1, 4);
            Buffer.BlockCopy(data, 1, stamped, 5, data.Length - 1);

            _pendingRequests[id] = new PendingRequest
            {
                Id = id,
                Type = type,
                Data = stamped,
                SentTime = Time.time,
                RetryCount = 0
            };

            NetworkManager.SendRaw(stamped);
            Plugin.Log.LogInfo("Request sent: " + type + " ID: " + id);
            return id;
        }

        // Call this when the remote side sends back a positive confirmation.
        // Removes the pending entry, stopping any further retries.
        public void ConfirmRequest(uint requestId)
        {
            if (_pendingRequests.ContainsKey(requestId))
            {
                Plugin.Log.LogInfo("Request confirmed: " + requestId);
                _pendingRequests.Remove(requestId);
            }
        }

        // Call this when the remote side explicitly rejects the request.
        // Logs the reason and removes the pending entry.
        public void RejectRequest(uint requestId, string reason)
        {
            if (_pendingRequests.ContainsKey(requestId))
            {
                Plugin.Log.LogInfo("Request rejected: " + requestId + " reason: " + reason);
                _pendingRequests.Remove(requestId);
            }
        }

        // Per-frame retry loop. Every RetryInterval seconds, resend any request that has
        // not yet been confirmed. After MaxRetries failed attempts, give up and log a warning.
        private void Update()
        {
            var toRemove = new List<uint>();

            foreach (var kv in _pendingRequests)
            {
                PendingRequest req = kv.Value;
                if (Time.time - req.SentTime < RetryInterval) continue;

                if (req.RetryCount >= MaxRetries)
                {
                    Plugin.Log.LogWarning("Request failed after max retries: " + req.Id);
                    toRemove.Add(req.Id);
                }
                else
                {
                    req.RetryCount++;
                    req.SentTime = Time.time;
                    NetworkManager.SendRaw(req.Data);
                    Plugin.Log.LogInfo("Retrying request: " + req.Id + " attempt: " + req.RetryCount);
                }
            }

            foreach (uint id in toRemove)
                _pendingRequests.Remove(id);
        }

        public static RequestManager Instance;

        // Maximum number of retry attempts before a request is abandoned
        private const int MaxRetries = 5;

        // How long (seconds) to wait between retry attempts
        private const float RetryInterval = 2f;

        // All requests awaiting confirmation, keyed by their assigned ID
        private Dictionary<uint, PendingRequest> _pendingRequests = new Dictionary<uint, PendingRequest>();

        // Counter used to assign unique IDs to outgoing requests
        private uint _nextRequestId = 1U;
    }
}
