using System;
using UnityEngine;

namespace GraveyardKeeperMultiplayer
{
    // IMGUI settings panel shown before an online save is created.
    //
    // The host sees this panel after clicking "New Save" in the "Playing Online" browser.
    // It renders a centred overlay window using Unity's immediate-mode GUI (IMGUI) with
    // three configurable options:
    //   - Shared money (toggle)
    //   - Friendly fire (toggle)
    //   - Experience mode (radio group: individual / shared XP / shared XP + skills)
    //
    // On "Confirm", ConfirmSettings() creates the actual save file and writes the .gkmp
    // companion file via OnlineSaveManager. On "Cancel", the panel is hidden with no effect.
    //
    // Requires UnityEngine.IMGUIModule.dll and UnityEngine.TextRenderingModule.dll.
    public class OnlineSettingsMenu : MonoBehaviour
    {
        // Creates the singleton MonoBehaviour. Called from PatchStartGame.
        public static void Create()
        {
            if (Instance != null) return;

            GameObject go = new GameObject("OnlineSettingsMenu");
            Instance = go.AddComponent<OnlineSettingsMenu>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        // Opens the settings panel with a fresh default settings object.
        // Called from PatchSelectSlot when "New Save" is pressed in online flow.
        public static void Open(MainMenuGUI mainMenu)
        {
            if (Instance == null) Create();

            PendingSettings = new OnlineSaveSettings();
            Instance._mainMenu = mainMenu;
            Instance._visible = true;
            Plugin.Log.LogInfo("Online settings menu opened.");
        }

        // Hides the panel without saving (used when the player clicks Cancel).
        public static void Hide()
        {
            if (Instance != null)
                Instance._visible = false;
        }

        // Builds all GUIStyle objects once and caches them. Called lazily on first OnGUI.
        // Doing this in OnGUI (rather than Start) ensures the GUI skin is ready.
        private void BuildStyles()
        {
            if (_stylesBuilt) return;
            _stylesBuilt = true;

            // Dark semi-transparent background for the panel box
            Texture2D panelBg = MakeTex(1, 1, new Color(0.08f, 0.06f, 0.04f, 0.96f));
            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal  = { background = panelBg },
                border  = new RectOffset(4, 4, 4, 4),
                padding = new RectOffset(20, 20, 16, 16)
            };

            // Gold-tinted title text
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.92f, 0.8f, 0.55f) },
                margin    = new RectOffset(0, 0, 0, 12)
            };

            // Parchment-coloured body label text
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal   = { textColor = new Color(0.85f, 0.78f, 0.65f) },
                margin   = new RectOffset(0, 0, 4, 4)
            };

            // Toggle style matching the body label colour
            _toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 15,
                normal   = { textColor = new Color(0.85f, 0.78f, 0.65f) },
                active   = { textColor = new Color(0.92f, 0.8f, 0.55f) },
                focused  = { textColor = new Color(0.92f, 0.8f, 0.55f) },
                margin   = new RectOffset(0, 0, 6, 6)
            };

            // Green confirm button
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 15,
                fontStyle = FontStyle.Bold,
                normal    = { background = MakeTex(1, 1, new Color(0.2f,  0.38f, 0.18f, 1f)), textColor = new Color(0.9f,  0.95f, 0.8f) },
                hover     = { background = MakeTex(1, 1, new Color(0.28f, 0.5f,  0.24f, 1f)), textColor = Color.white },
                active    = { background = MakeTex(1, 1, new Color(0.14f, 0.28f, 0.12f, 1f)), textColor = Color.white },
                padding   = new RectOffset(12, 12, 8, 8),
                margin    = new RectOffset(0, 8, 0, 0)
            };

            // Red cancel button
            _btnCancelStyle = new GUIStyle(_btnStyle)
            {
                normal = { background = MakeTex(1, 1, new Color(0.38f, 0.14f, 0.12f, 1f)), textColor = new Color(0.95f, 0.8f, 0.78f) },
                hover  = { background = MakeTex(1, 1, new Color(0.52f, 0.2f,  0.16f, 1f)), textColor = Color.white },
                active = { background = MakeTex(1, 1, new Color(0.28f, 0.1f,  0.08f, 1f)), textColor = Color.white }
            };
        }

        // IMGUI rendering callback — called every frame by Unity when the panel is visible.
        private void OnGUI()
        {
            if (!_visible) return;

            BuildStyles();

            // Centre the panel on screen
            float x = ((float)Screen.width  - PanelWidth)  / 2f;
            float y = ((float)Screen.height - PanelHeight) / 2f;
            Rect panelRect = new Rect(x, y, PanelWidth, PanelHeight);

            // Draw a semi-transparent black overlay behind the panel to dim the game
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(panelRect, _panelStyle);

            GUILayout.Label("Online Server Settings", _titleStyle);
            GUILayout.Space(4f);

            // --- Toggles ---
            PendingSettings.sharedMoney  = GUILayout.Toggle(PendingSettings.sharedMoney,  "  Shared money",   _toggleStyle);
            PendingSettings.friendlyFire = GUILayout.Toggle(PendingSettings.friendlyFire, "  Friendly fire",  _toggleStyle);
            GUILayout.Space(8f);

            // --- Experience mode radio group ---
            GUILayout.Label("Experience mode:", _labelStyle);
            DrawRadio(0, "Individual (default)");
            DrawRadio(1, "Shared experience");
            DrawRadio(2, "Shared experience & skills");
            GUILayout.Space(16f);

            // --- Action buttons ---
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm", _btnStyle, GUILayout.Height(36f)))
            {
                _visible = false;
                ConfirmSettings();
            }
            if (GUILayout.Button("Cancel", _btnCancelStyle, GUILayout.Height(36f)))
            {
                _visible = false;
                Plugin.Log.LogInfo("Online settings cancelled.");
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        // Renders a single radio-button toggle for the experience mode selection.
        // Only updates PendingSettings.experienceMode when the option transitions
        // from unchecked to checked (avoids clearing the selection immediately).
        private void DrawRadio(int value, string label)
        {
            bool wasSelected = PendingSettings.experienceMode == value;
            bool isSelected  = GUILayout.Toggle(wasSelected, "  " + label, _toggleStyle);
            if (isSelected && !wasSelected)
                PendingSettings.experienceMode = value;
        }

        // Called when the host clicks "Confirm". Uses the game's own save flow to create
        // a new save file, then writes the .gkmp companion file with the pending settings.
        // The LoadingGUI callbacks follow the same pattern used by the game's own menus.
        private static void ConfirmSettings()
        {
            Plugin.Log.LogInfo("Settings confirmed, creating online save...");

            LoadingGUI.Show(delegate
            {
                LoadingGUI.ShowBlackBackground(true, false);

                // Hide all existing UI panels before the loading screen takes over
                GUIElements.me.saves.Hide(false);
                GUIElements.me.main_menu.Hide(false);
                GUIElements.me.hud.Hide();

                // Ask the game engine to create a new blank save, then save it to disk
                GameSave.CreateNewSave(delegate
                {
                    MainGame.me.player = null;
                    Intro.need_show_first_intro = true;

                    PlatformSpecific.SaveGame(null, MainGame.me.save, delegate(SaveSlotData slot)
                    {
                        Plugin.Log.LogInfo("SaveGame callback! slot=" + (slot == null ? "null" : slot.filename_no_extension));

                        if (slot != null)
                        {
                            MainGame.me.save_slot = slot;

                            // Write the .gkmp companion file with the chosen settings
                            OnlineSaveManager.SaveSettings(slot, PendingSettings);
                            Plugin.Log.LogInfo("Online save created: " + slot.filename_no_extension);
                        }

                        GUIElements.me.hud.Hide();

                        // Transition into the game using the new save
                        GUIElements.me.saves.StartPlayingGame();
                    });
                });
            });

            LoadingGUI.LinkAsyncProcess(null);
            LoadingGUI.ShowProgressBar();
        }

        // Creates a 1×1 Texture2D of the given colour, used to build GUIStyle backgrounds.
        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }

        public static OnlineSettingsMenu Instance;

        // Settings object that is updated live as the host interacts with the panel
        public static OnlineSaveSettings PendingSettings = new OnlineSaveSettings();

        // Whether the overlay is currently visible
        private bool _visible = false;

        // Reference to the calling main menu (currently unused but kept for future use)
        private MainMenuGUI _mainMenu;

        // Panel dimensions in pixels
        private const float PanelWidth  = 420f;
        private const float PanelHeight = 290f;

        // Cached GUIStyle objects (built once on first OnGUI call)
        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _toggleStyle;
        private GUIStyle _btnStyle;
        private GUIStyle _btnCancelStyle;
        private bool _stylesBuilt = false;
    }
}
