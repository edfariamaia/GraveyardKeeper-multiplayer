using HarmonyLib;
using System;
using System.IO;
using System.Reflection.Emit;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // Harmony postfix patch on MainMenuGUI.Open.
    // This patch runs every time the main menu is shown (game launch, return from save, etc.).
    // It does two things:
    //   1. Resets multiplayer connection state so a fresh session can begin.
    //   2. Injects two new buttons ("Invite Friend" and "Playing Online") into the main menu
    //      by cloning the existing Exit button and rewiring its label and press callback.
    [HarmonyPatch(typeof(MainMenuGUI), "Open")]
    public class PatchMainMenu
    {
        [HarmonyPostfix]
        private static void Postfix(MainMenuGUI __instance)
        {
            // Clean up the temporary host save file left over when a client disconnects
            string path = PlatformSpecific.GetSaveFolder() + "mp_host.dat";
            if (File.Exists(path))
            {
                File.Delete(path);
                Plugin.Log.LogInfo("Cleaned up mp_host.dat");
            }

            // Reset lobby reference only if no session is in progress
            if (!NetworkManager.IsHost && !NetworkManager.IsConnected)
                NetworkManager.ResetLobby();

            // Only reset IsConnected if no save transfer is pending (client joining mid-load)
            if (!SaveTransferManager.IsPendingTransfer)
                NetworkManager.IsConnected = false;

            Plugin.Log.LogInfo("Menu opened, connection state reset!");

            // Guard: only create buttons once — if the button GameObjects still exist, skip
            if (_inviteButton != null && _inviteButton.gameObject != null)
                return;

            _inviteButton = null;
            _onlineButton = null;

            // Destroy the avatar when returning to the main menu
            RemotePlayerAvatar.Instance?.DestroyAvatar();

            // --- "Invite Friend" button ---
            // Clone the Exit button to inherit its visual style and layout position
            GameObject inviteGO = UnityEngine.Object.Instantiate<GameObject>(
                __instance.mm_exit.gameObject, __instance.mm_exit.transform.parent);
            inviteGO.name = "mm_invite";

            // Remove the LocalizedLabel component so we can set text directly
            var loc1 = inviteGO.GetComponentInChildren<LocalizedLabel>(true);
            if (loc1 != null) UnityEngine.Object.DestroyImmediate(loc1);

            // Set the button label text
            var lbl1 = inviteGO.GetComponentInChildren<UILabel>(true);
            if (lbl1 != null) { lbl1.text = "Invite Friend"; lbl1.MarkAsChanged(); }

            // Wire the press callback: clicking opens the Steam invite overlay / creates lobby
            MenuItemGUI btn1 = inviteGO.GetComponent<MenuItemGUI>();
            btn1.on_pressed = new EventDelegate(() => NetworkManager.HostGame());
            btn1.Init(__instance.GetComponent<BaseMenuGUI>());
            _inviteButton = btn1;

            // --- "Playing Online" button ---
            GameObject onlineGO = UnityEngine.Object.Instantiate<GameObject>(
                __instance.mm_exit.gameObject, __instance.mm_exit.transform.parent);
            onlineGO.name = "mm_online";

            var loc2 = onlineGO.GetComponentInChildren<LocalizedLabel>(true);
            if (loc2 != null) UnityEngine.Object.DestroyImmediate(loc2);

            var lbl2 = onlineGO.GetComponentInChildren<UILabel>(true);
            if (lbl2 != null) { lbl2.text = "Playing Online"; lbl2.MarkAsChanged(); }

            // Wire the press callback: clicking opens the online save browser
            MenuItemGUI btn2 = onlineGO.GetComponent<MenuItemGUI>();
            btn2.on_pressed = new EventDelegate(() => OnlineLobbyMenu.Open(__instance));
            btn2.Init(__instance.GetComponent<BaseMenuGUI>());
            _onlineButton = btn2;

            // Refresh the menu layout to account for the new buttons
            __instance.buttons_table.Reposition();
            Plugin.Log.LogInfo("Online buttons added!");
        }

        // Cached references to the injected buttons to avoid re-creating them on every Open call
        private static MenuItemGUI _inviteButton;
        private static MenuItemGUI _onlineButton;
    }
}
