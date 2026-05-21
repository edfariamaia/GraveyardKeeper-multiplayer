using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace GraveyardKeeperMultiplayer
{
    // Harmony prefix patch on SaveSlotsMenuGUI.OnSlotsLoaded.
    // OnSlotsLoaded is called by the game after it reads all save files from disk and
    // is about to render the save slot list. When the player entered the menu via
    // "Playing Online" (IsOnlineFlow == true), we intercept the full slot list, filter
    // it down to only saves that have a companion .gkmp file, and call RedrawSlots
    // ourselves with the filtered list — then return false to skip the original method.
    // In normal (local) play we return true so the original method runs unchanged.
    [HarmonyPatch(typeof(SaveSlotsMenuGUI), "OnSlotsLoaded")]
    public class PatchSaveSlots
    {
        [HarmonyPrefix]
        private static bool Prefix(SaveSlotsMenuGUI __instance, List<SaveSlotData> slots)
        {
            // Not in online flow — let the original method handle the slot list normally
            if (!OnlineLobbyMenu.IsOnlineFlow)
                return true;

            // Build a filtered list containing only online saves (.gkmp companion file present)
            List<SaveSlotData> onlineSlots = new List<SaveSlotData>();
            foreach (SaveSlotData slot in slots)
            {
                if (OnlineSaveManager.IsOnlineSave(slot))
                    onlineSlots.Add(slot);
            }

            // RedrawSlots is private — call it via reflection with the filtered list.
            // The second argument (bool) tells the method to also add a "New Save" entry.
            MethodInfo redraw = typeof(SaveSlotsMenuGUI)
                .GetMethod("RedrawSlots", BindingFlags.Instance | BindingFlags.NonPublic);
            redraw.Invoke(__instance, new object[] { onlineSlots, true });

            // Return false to suppress the original OnSlotsLoaded body
            return false;
        }
    }
}
