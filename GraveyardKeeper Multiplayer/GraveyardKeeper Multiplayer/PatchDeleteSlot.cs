using System;
using HarmonyLib;

namespace GraveyardKeeperMultiplayer
{
    // Harmony prefix patch on PlatformSpecific.DeleteSlot.
    // DeleteSlot is called whenever the player deletes a save file through the game's UI.
    // We hook it to also delete the companion .gkmp file that stores online session
    // settings for that save, keeping the save folder clean.
    [HarmonyPatch(typeof(PlatformSpecific), "DeleteSlot")]
    public class PatchDeleteSlot
    {
        [HarmonyPrefix]
        private static void Prefix(SaveSlotData slot)
        {
            // Delete the .gkmp companion file (if present) before the game deletes the .dat
            OnlineSaveManager.DeleteSettings(slot);
        }
    }
}
