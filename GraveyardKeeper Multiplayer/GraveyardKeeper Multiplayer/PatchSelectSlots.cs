using System;
using HarmonyLib;

namespace GraveyardKeeperMultiplayer
{
    // Harmony prefix patch on SaveSlotsMenuGUI.OnSelectSlotPressed.
    // OnSelectSlotPressed is called when the player clicks a save slot or the "New Save"
    // entry. In normal flow we let it run as-is. In online flow (IsOnlineFlow == true),
    // clicking an existing save still proceeds normally (slot != null), but clicking
    // "New Save" (slot == null) is intercepted: instead of creating a blank save we open
    // the OnlineSettingsMenu so the host can configure the session before the save is made.
    [HarmonyPatch(typeof(SaveSlotsMenuGUI), "OnSelectSlotPressed")]
    public class PatchSelectSlot
    {
        [HarmonyPrefix]
        private static bool Prefix(SaveSlotData slot)
        {
            // Not in online flow — allow default behaviour
            if (!OnlineLobbyMenu.IsOnlineFlow)
                return true;

            if (slot != null)
            {
                // Existing online save selected — load it normally
                return true;
            }
            else
            {
                // "New Save" selected in online flow — open the settings panel instead
                OnlineSettingsMenu.Open(null);
                return false; // Suppress the original new-save logic
            }
        }
    }
}
