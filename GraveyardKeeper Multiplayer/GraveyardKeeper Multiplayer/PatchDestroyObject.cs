using System;
using HarmonyLib;

namespace GraveyardKeeperMultiplayer
{
    // Harmony prefix patch on WorldGameObject.DestroyMe.
    // WorldGameObject.DestroyMe is the game's universal method for removing a world
    // object (trees, rocks, graves, etc.). We intercept it to broadcast the destruction
    // to the remote player so both worlds stay in sync.
    //
    // The IsRemoteDestruction flag prevents an infinite loop: when we receive a
    // WorldObjectDestroyed packet and call DestroyMe locally, we set the flag so
    // this patch knows not to send the packet back out again.
    [HarmonyPatch(typeof(WorldGameObject), "DestroyMe")]
    public class PatchDestroyObject
    {
        [HarmonyPrefix]
        private static void Prefix(WorldGameObject __instance)
        {
            // Skip if not in a multiplayer session
            if (!NetworkManager.IsConnected) return;

            // Skip if this destruction was triggered by a received network packet
            // (avoids echoing the event back to the sender)
            if (IsRemoteDestruction) return;

            // Skip objects already marked as removed (DestroyMe may be called twice)
            if (__instance.is_removed) return;

            // Only sync objects with a valid unique ID (dynamic/scripted objects have id <= 0)
            if (__instance.unique_id <= 0L) return;

            WorldSyncHelper.SendObjectDestroyed(__instance.unique_id);
        }

        // Set to true before calling DestroyMe on a remotely-destroyed object to
        // prevent this patch from sending a redundant outbound packet
        public static bool IsRemoteDestruction;
    }
}
