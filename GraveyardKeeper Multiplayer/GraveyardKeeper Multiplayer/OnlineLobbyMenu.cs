using System;
using System.Collections.Generic;

namespace GraveyardKeeperMultiplayer
{
    // Controls the "Playing Online" save-browser flow.
    //
    // When the player clicks "Playing Online" in the main menu, Open() is called.
    // It sets IsOnlineFlow = true (which PatchSaveSlots uses to filter the save list)
    // and reads the available save slots so we can log how many online saves exist.
    //
    // IsOnlineFlow must be reset to false when the player leaves the save browser,
    // either by loading a save or cancelling — that reset is handled externally
    // (originally in PatchSaveSlotsClosed, now part of the closing callback).
    public static class OnlineLobbyMenu
    {
        // Opens the online save browser. Sets the flow flag so PatchSaveSlots knows to
        // filter the slot list to online-only saves (.gkmp companion file present).
        public static void Open(MainMenuGUI mainMenu)
        {
            IsOnlineFlow = true;
            Plugin.Log.LogInfo("Opening online lobby menu...");

            // Read all save slots asynchronously and log how many are online saves
            PlatformSpecific.ReadSaveSlots(slots =>
            {
                var onlineSlots = new List<SaveSlotData>();
                foreach (var slot in slots)
                {
                    if (OnlineSaveManager.IsOnlineSave(slot))
                        onlineSlots.Add(slot);
                }
                Plugin.Log.LogInfo("Found " + onlineSlots.Count + " online saves");
            });
        }

        // Resets the online flow flag. Call this when the player exits the save browser
        // without loading a game so that subsequent local-play saves are not filtered.
        public static void Close()
        {
            IsOnlineFlow = false;
        }

        // True while the player is inside the "Playing Online" save-selection flow.
        // Checked by PatchSaveSlots to decide whether to filter the slot list.
        public static bool IsOnlineFlow;
    }
}
