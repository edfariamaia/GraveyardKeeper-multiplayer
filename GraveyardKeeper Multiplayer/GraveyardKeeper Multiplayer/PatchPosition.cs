using System;
using HarmonyLib;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Harmony postfix patch on PlayerComponent.Update.
    // PlayerComponent.Update runs every frame for every player entity (local and remote).
    // We use a timer to throttle position broadcasts to ~10 times per second (every 0.1 s),
    // sending only for the local player while a multiplayer connection is active.
    [HarmonyPatch(typeof(PlayerComponent), "Update")]
    public class PatchPosition
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerComponent __instance)
        {
            // Skip remote players — we only send our own position
            if (!__instance.is_local_player) return;

            // Skip if not connected to a multiplayer session
            if (!NetworkManager.IsConnected) return;

            // Accumulate time and send once per SEND_INTERVAL (0.1 s = 10 Hz)
            _timer += Time.deltaTime;
            if (_timer < SEND_INTERVAL) return;

            _timer = 0f;
            NetworkManager.SendPosition(__instance.transform.position);
        }

        // Elapsed time since the last position packet was sent
        private static float _timer;

        // How often (in seconds) to broadcast the player's position
        private const float SEND_INTERVAL = 0.1f;
    }
}
