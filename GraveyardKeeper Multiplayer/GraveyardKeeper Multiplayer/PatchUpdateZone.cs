using System;
using HarmonyLib;

namespace GraveyardKeeperMultiplayer
{
    // Harmony postfix patch on PlayerComponent.UpdateZone.
    // UpdateZone is called by the game whenever the player moves between map zones
    // (e.g. overworld, graveyard, dungeon, church). We detect zone changes by comparing
    // with the last known zone ID and send a Zone packet to the remote player only when
    // the zone actually changes, avoiding redundant traffic.
    [HarmonyPatch(typeof(PlayerComponent), "UpdateZone")]
    public class PatchUpdateZone
    {
        [HarmonyPostfix]
        private static void Postfix(PlayerComponent __instance)
        {
            // Guard: zone may be null during loading transitions
            if (__instance.current_zone == null) return;

            string id = __instance.current_zone.id;

            // Only broadcast when the zone has actually changed
            if (id == _lastZone) return;

            _lastZone = id;
            Plugin.Log.LogInfo("Zona atual: " + id);
            NetworkManager.SendZone(id);
        }

        // Tracks the last zone the local player was in to detect transitions
        private static string _lastZone = "";
    }
}
