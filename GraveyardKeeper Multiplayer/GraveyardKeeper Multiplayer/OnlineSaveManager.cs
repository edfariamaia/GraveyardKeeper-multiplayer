using System;
using System.IO;

namespace GraveyardKeeperMultiplayer
{
    // Manages the .gkmp companion files that store online session settings alongside
    // each online save file.
    //
    // Every online save has two files on disk:
    //   {name}.dat  — the normal game save (managed by the game engine)
    //   {name}.gkmp — JSON settings file written and read by this class
    //
    // The presence of a .gkmp file is the single source of truth for whether a save
    // is an online save (see IsOnlineSave). If the .gkmp file is absent the save is
    // treated as a local save and excluded from the "Playing Online" browser.
    public static class OnlineSaveManager
    {
        // The most recently loaded or saved settings for the current session
        public static OnlineSaveSettings CurrentSettings { get; private set; }

        // Returns the full path to the .gkmp companion file for the given save name.
        private static string GetCompanionPath(string filenameNoExtension)
        {
            return PlatformSpecific.GetSaveFolder() + filenameNoExtension + ".gkmp";
        }

        // Returns true when a .gkmp companion file exists for the given save slot.
        // Used by PatchSaveSlots to filter the save list.
        public static bool IsOnlineSave(SaveSlotData slot)
        {
            return File.Exists(GetCompanionPath(slot.filename_no_extension));
        }

        // Writes the session settings to the .gkmp file as a minimal JSON string.
        // Called by OnlineSettingsMenu.ConfirmSettings after a new online save is created.
        public static void SaveSettings(SaveSlotData slot, OnlineSaveSettings settings)
        {
            // Hand-roll the JSON to avoid a Newtonsoft dependency
            string json = string.Format(
                "{{\"isOnline\":true,\"sharedMoney\":{0},\"experienceMode\":{1},\"friendlyFire\":{2}}}",
                settings.sharedMoney.ToString().ToLower(),
                settings.experienceMode,
                settings.friendlyFire.ToString().ToLower());

            File.WriteAllText(GetCompanionPath(slot.filename_no_extension), json);
            CurrentSettings = settings;
            Plugin.Log.LogInfo("Online save settings saved for slot: " + slot.filename_no_extension);
        }

        // Reads the .gkmp file and returns a populated OnlineSaveSettings, or null if the
        // file does not exist. Uses simple string.Contains checks instead of a full JSON parser.
        public static OnlineSaveSettings LoadSettings(SaveSlotData slot)
        {
            string path = GetCompanionPath(slot.filename_no_extension);
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            var settings = new OnlineSaveSettings
            {
                sharedMoney  = json.Contains("\"sharedMoney\":true"),
                friendlyFire = json.Contains("\"friendlyFire\":true")
            };

            // Parse experienceMode: check for 2 first, then 1, otherwise default to 0
            if      (json.Contains("\"experienceMode\":2")) settings.experienceMode = 2;
            else if (json.Contains("\"experienceMode\":1")) settings.experienceMode = 1;
            else                                             settings.experienceMode = 0;

            CurrentSettings = settings;
            return settings;
        }

        // Deletes the .gkmp file for the given save slot.
        // Called by PatchDeleteSlot before the game deletes the .dat file.
        public static void DeleteSettings(SaveSlotData slot)
        {
            string path = GetCompanionPath(slot.filename_no_extension);
            if (File.Exists(path))
            {
                File.Delete(path);
                Plugin.Log.LogInfo("Online save settings deleted for slot: " + slot.filename_no_extension);
            }
        }

        // Applies the cached settings to the running game session (logging only for now).
        // Will be expanded in future phases to actually enforce shared money / XP rules.
        public static void ApplySettings()
        {
            if (CurrentSettings == null) return;

            string xpMode = CurrentSettings.experienceMode == 0 ? "individual"
                          : CurrentSettings.experienceMode == 1 ? "shared experience"
                          : "shared experience and skills";

            Plugin.Log.LogInfo(
                "Applying online settings: sharedMoney=" + CurrentSettings.sharedMoney +
                " experienceMode=" + xpMode +
                " friendlyFire=" + CurrentSettings.friendlyFire);
        }
    }
}
