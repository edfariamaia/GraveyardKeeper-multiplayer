using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace GraveyardKeeperMultiplayer
{
    // BepInEx plugin entry point.
    // This class is loaded by BepInEx on game startup. It applies all Harmony patches
    // defined across the project and exposes a shared logger for the entire mod.
    [BepInPlugin("com.seunome.gkmultiplayer", "GK Multiplayer", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            // Store the logger so every other class can call Plugin.Log.LogInfo(...)
            Plugin.Log = base.Logger;

            // Scan all classes with [HarmonyPatch] attributes and apply their patches
            this._harmony.PatchAll();

            Plugin.Log.LogInfo("GK Multiplayer loaded!");
        }

        // Shared logger — used by all mod classes to write to the BepInEx console
        internal static ManualLogSource Log;

        // Harmony instance used to apply and optionally revert all patches
        private readonly Harmony _harmony = new Harmony("com.seunome.gkmultiplayer");
    }
}
