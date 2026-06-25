using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using ArtisansGuns.Auth;
using ArtisansGuns.Managers;
using ArtisansGuns.Networking;
using UnityEngine.SceneManagement;
using System;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// SettingsUIController - Manages the settings panel UI (shared between Lobby and Game scenes)
    /// Handles sensitivity and other player settings with persistent storage via SettingsManager
    /// </summary>
    public class SettingsUIController : MonoBehaviour
    {
        [Header("UI Document - Optional (if settings panel is separate)")]
        [SerializeField] private UIDocument settingsUIDocument;

        // Events
        public event Action OnSettingsPanelClosed;
        public event Action OnLogoutPerformed;

        // UI Elements
        private VisualElement settingsPanel;
        private Button closeSettingsButton;
        private Slider sensitivitySlider;
        private Label sensitivityValueLabel;
        private Slider musicVolumeSlider;
        private Label musicVolumeValueLabel;
        private Button logoutButton;
        private Button exitGameButton;
        private VisualElement exitConfirmOverlay;
        private Button exitConfirmYes;
        private Button exitConfirmNo;
        private VisualElement roomCodeSection;
        private Label settingsRoomCodeLabel;
        private Button fireButtonLeftBtn;
        private Button fireButtonRightBtn;
        private Button langEnBtn;
        private Button langEsBtn;
        private Label languageSectionTitle;
        private Label languageSettingLabel;
        private Button shadowsOnBtn;
        private Button shadowsOffBtn;
        private Label renderShadowsLabel;

        // Cached labels for re-localization
        private Label settingsTitleLabel;
        private Label generalSectionLabel;
        private Label sensitivityLabel;
        private Label audioSectionLabel;
        private Label musicVolumeLabel;
        private Label controlsSectionLabel;
        private Label fireButtonSideLabel;
        private Label exitMatchTitleLabel;
        private Label exitMatchMessageLabel;
        private Button exitConfirmYesBtn;
        private Button exitConfirmNoBtn;
        private Label roomCodeLabel;

        private void OnEnable()
        {
            // Try to find the settings panel
            FindSettingsPanelElements();
        }

        private void OnDisable()
        {
            // Unsubscribe from events
            if (closeSettingsButton != null)
                closeSettingsButton.clicked -= HideSettings;
            if (sensitivitySlider != null)
                sensitivitySlider.UnregisterValueChangedCallback(OnSensitivityChanged);
            if (musicVolumeSlider != null)
                musicVolumeSlider.UnregisterValueChangedCallback(OnMusicVolumeChanged);
            if (logoutButton != null)
                logoutButton.clicked -= OnLogoutClicked;
            if (exitGameButton != null)
                exitGameButton.clicked -= OnExitGameClicked;
            if (exitConfirmYes != null)
                exitConfirmYes.clicked -= OnExitConfirmYes;
            if (exitConfirmNo != null)
                exitConfirmNo.clicked -= OnExitConfirmNo;

            // Unsubscribe from SettingsManager
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.OnSensitivityChanged -= OnSettingsManagerSensitivityChanged;
        }

        /// <summary>
        /// Find settings panel UI elements
        /// Can be called from external controllers (LobbyUIController, GameplayHUDController)
        /// </summary>
        public void FindSettingsPanelElements(VisualElement root = null)
        {
            // If root not provided, try to get from UIDocument
            if (root == null)
            {
                if (settingsUIDocument != null)
                    root = settingsUIDocument.rootVisualElement;
                else
                    return;
            }

            // Find settings panel
            settingsPanel = root.Q<VisualElement>("SettingsPanel");
            closeSettingsButton = root.Q<Button>("CloseSettingsButton");
            sensitivitySlider = root.Q<Slider>("SensitivitySlider");
            sensitivityValueLabel = root.Q<Label>("SensitivityValueLabel");
            musicVolumeSlider = root.Q<Slider>("MusicVolumeSlider");
            musicVolumeValueLabel = root.Q<Label>("MusicVolumeValueLabel");
            logoutButton = root.Q<Button>("LogoutButton");
            exitGameButton = root.Q<Button>("ExitGameButton");
            exitConfirmOverlay = root.Q<VisualElement>("ExitConfirmOverlay");
            exitConfirmYes = root.Q<Button>("ExitConfirmYes");
            exitConfirmNo = root.Q<Button>("ExitConfirmNo");
            roomCodeSection = root.Q<VisualElement>("RoomCodeSection");
            settingsRoomCodeLabel = root.Q<Label>("SettingsRoomCodeLabel");
            fireButtonLeftBtn = root.Q<Button>("FireButtonLeftBtn");
            langEnBtn = root.Q<Button>("LangEnBtn");
            langEsBtn = root.Q<Button>("LangEsBtn");
            languageSectionTitle = root.Q<Label>("LanguageSectionTitle");
            languageSettingLabel = root.Q<Label>("LanguageSettingLabel");
            shadowsOnBtn = root.Q<Button>("ShadowsOnBtn");
            shadowsOffBtn = root.Q<Button>("ShadowsOffBtn");
            renderShadowsLabel = root.Q<Label>("RenderShadowsLabel");
            // Cache labels for re-localization
            var settingsHeader = root.Q<VisualElement>("SettingsPanel");
            settingsTitleLabel = settingsHeader?.Q<Label>(className: "settings-title");
            var sections = root.Query<Label>(className: "settings-section-title").ToList();
            generalSectionLabel = sections.Count > 0 ? sections[0] : null;
            audioSectionLabel = sections.Find(l => l.text == "AUDIO" || l.text == LocalizationManager.T("AUDIO"));
            controlsSectionLabel = sections.Find(l => l.text == "CONTROLS" || l.text == LocalizationManager.T("CONTROLS"));
            sensitivityLabel = root.Q<Slider>("SensitivitySlider")?.parent?.parent?.Q<Label>(className: "settings-label");
            musicVolumeLabel = root.Q<Slider>("MusicVolumeSlider")?.parent?.parent?.Q<Label>(className: "settings-label");
            fireButtonSideLabel = root.Q<Button>("FireButtonLeftBtn")?.parent?.parent?.Q<Label>(className: "settings-label");
            exitMatchTitleLabel = root.Q<VisualElement>("ExitConfirmOverlay")?.Q<Label>(className: "exit-confirm-title");
            exitMatchMessageLabel = root.Q<VisualElement>("ExitConfirmOverlay")?.Q<Label>(className: "exit-confirm-message");
            exitConfirmYesBtn = root.Q<Button>("ExitConfirmYes");
            exitConfirmNoBtn = root.Q<Button>("ExitConfirmNo");
            roomCodeLabel = root.Q<VisualElement>("RoomCodeSection")?.Q<Label>(className: "room-code-label-sm");
            fireButtonRightBtn = root.Q<Button>("FireButtonRightBtn");

            // Register callbacks
            if (closeSettingsButton != null)
                closeSettingsButton.clicked += HideSettings;
            if (sensitivitySlider != null)
                sensitivitySlider.RegisterValueChangedCallback(OnSensitivityChanged);
            if (musicVolumeSlider != null)
                musicVolumeSlider.RegisterValueChangedCallback(OnMusicVolumeChanged);
            if (logoutButton != null)
                logoutButton.clicked += OnLogoutClicked;
            if (exitGameButton != null)
                exitGameButton.clicked += OnExitGameClicked;
            if (exitConfirmYes != null)
                exitConfirmYes.clicked += OnExitConfirmYes;
            if (exitConfirmNo != null)
                exitConfirmNo.clicked += OnExitConfirmNo;
            if (fireButtonLeftBtn != null)
                fireButtonLeftBtn.clicked += () => SetFireButtonSide("left");
            if (fireButtonRightBtn != null)
                fireButtonRightBtn.clicked += () => SetFireButtonSide("right");
            if (langEnBtn != null)
                langEnBtn.clicked += () => SetLanguage(LocalizationManager.Language.EN);
            if (langEsBtn != null)
                langEsBtn.clicked += () => SetLanguage(LocalizationManager.Language.ES);
            if (shadowsOnBtn != null)
                shadowsOnBtn.clicked += () => SetRenderShadows(true);
            if (shadowsOffBtn != null)
                shadowsOffBtn.clicked += () => SetRenderShadows(false);

            // Subscribe to SettingsManager changes
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.OnSensitivityChanged += OnSettingsManagerSensitivityChanged;

            // Load current settings
            LoadSettings();

            // Show/hide logout button based on auth state
            UpdateLogoutButtonVisibility();

            // Apply current language to settings panel labels
            UpdateLanguageToggleVisual();
            LocalizeSettingsLabels();
        }

        /// <summary>
        /// Load current settings from SettingsManager (always syncs from AuthManager first)
        /// </summary>
        private void LoadSettings()
        {
            if (SettingsManager.Instance == null)
                return;

            // Always sync from the logged-in user's backend data before reading
            SettingsManager.Instance.RefreshFromCurrentUser();
            float sensitivity = SettingsManager.Instance.GetMouseSensitivity();

            // SetValueWithoutNotify avoids firing OnSensitivityChanged (which would call SaveSettings
            // on a potentially destroyed LoadoutManager during scene transitions / login).
            if (sensitivitySlider != null)
                sensitivitySlider.SetValueWithoutNotify(sensitivity);

            UpdateSensitivityLabel(sensitivity);

            // Music volume
            float musicVol = SoundManager.Instance != null
                ? SoundManager.Instance.GetMusicVolume()
                : SoundManager.DEFAULT_MUSIC_VOLUME;
            if (musicVolumeSlider != null)
                musicVolumeSlider.SetValueWithoutNotify(musicVol);
            UpdateMusicVolumeLabel(musicVol);

            // Fire button side
            string fireSide = PlayerPrefs.GetString("fire_button_side", "left");
            UpdateFireButtonToggleVisual(fireSide);

            // Render shadows
            bool shadows = SettingsManager.Instance.GetRenderShadows();
            UpdateShadowsToggleVisual(shadows);
        }

        /// <summary>
        /// Handle slider value change
        /// </summary>
        private void OnSensitivityChanged(ChangeEvent<float> evt)
        {
            float value = evt.newValue;
            
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.SetMouseSensitivity(value);
            }
            
            UpdateSensitivityLabel(value);
        }

        /// <summary>
        /// Handle SettingsManager sensitivity change (from other sources)
        /// </summary>
        private void OnSettingsManagerSensitivityChanged(float value)
        {
            if (sensitivitySlider != null && Mathf.Abs(sensitivitySlider.value - value) > 0.01f)
            {
                sensitivitySlider.value = value;
            }
            UpdateSensitivityLabel(value);
        }

        /// <summary>
        /// Update sensitivity label display
        /// </summary>
        private void UpdateSensitivityLabel(float value)
        {
            if (sensitivityValueLabel != null)
                sensitivityValueLabel.text = value.ToString("F1");
        }

        private void OnMusicVolumeChanged(ChangeEvent<float> evt)
        {
            float value = evt.newValue;
            if (SoundManager.Instance != null)
                SoundManager.Instance.SetMusicVolume(value);
            UpdateMusicVolumeLabel(value);
        }

        private void UpdateMusicVolumeLabel(float value)
        {
            if (musicVolumeValueLabel != null)
                musicVolumeValueLabel.text = Mathf.RoundToInt(value * 100f) + "%";
        }

        private void SetFireButtonSide(string side)
        {
            PlayerPrefs.SetString("fire_button_side", side);
            PlayerPrefs.Save();
            UpdateFireButtonToggleVisual(side);

            // Apply immediately if MobileControlsController is active
            var ctrl = MobileControlsController.Instance;
            if (ctrl != null)
                ctrl.ApplyFireButtonSide(side);
        }

        private void UpdateFireButtonToggleVisual(string side)
        {
            if (fireButtonLeftBtn != null)
            {
                if (side == "left")
                    fireButtonLeftBtn.AddToClassList("settings-toggle-active");
                else
                    fireButtonLeftBtn.RemoveFromClassList("settings-toggle-active");
            }
            if (fireButtonRightBtn != null)
            {
                if (side == "right")
                    fireButtonRightBtn.AddToClassList("settings-toggle-active");
                else
                    fireButtonRightBtn.RemoveFromClassList("settings-toggle-active");
            }
        }

        private void SetRenderShadows(bool enabled)
        {
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.SetRenderShadows(enabled);
            UpdateShadowsToggleVisual(enabled);
        }

        private void UpdateShadowsToggleVisual(bool enabled)
        {
            if (shadowsOnBtn != null)
            {
                if (enabled) shadowsOnBtn.AddToClassList("settings-toggle-active");
                else shadowsOnBtn.RemoveFromClassList("settings-toggle-active");
            }
            if (shadowsOffBtn != null)
            {
                if (!enabled) shadowsOffBtn.AddToClassList("settings-toggle-active");
                else shadowsOffBtn.RemoveFromClassList("settings-toggle-active");
            }
        }

        /// <summary>
        /// Show settings panel (refreshes values from backend before showing)
        /// </summary>
        public void ShowSettings()
        {
            if (settingsPanel != null)
            {
                settingsPanel.RemoveFromClassList("hidden");
                settingsPanel.AddToClassList("visible");
            }
            // Refresh slider with latest backend value every time panel opens
            LoadSettings();
            // Show/hide logout based on current auth state
            UpdateLogoutButtonVisibility();
            // Show room code if in a session
            UpdateRoomCodeDisplay();
        }

        /// <summary>
        /// Hide settings panel
        /// </summary>
        public void HideSettings()
        {
            if (settingsPanel != null)
            {
                settingsPanel.RemoveFromClassList("visible");
                settingsPanel.AddToClassList("hidden");
            }
            
            // Notify listeners that settings panel closed
            OnSettingsPanelClosed?.Invoke();
        }

        /// <summary>
        /// Show/hide the room code section based on current session state
        /// </summary>
        private void UpdateRoomCodeDisplay()
        {
            string code = NetworkManager.Instance != null ? NetworkManager.Instance.CurrentRoomCode : null;
            if (!string.IsNullOrEmpty(code))
            {
                if (roomCodeSection != null) roomCodeSection.RemoveFromClassList("hidden");
                if (settingsRoomCodeLabel != null) settingsRoomCodeLabel.text = code;
            }
            else
            {
                if (roomCodeSection != null) roomCodeSection.AddToClassList("hidden");
            }
        }

        /// <summary>
        /// Handle logout button click
        /// </summary>
        private void OnLogoutClicked()
        {
            HideSettings();

            // Use AuthManager logout if available (it will re-init as guest)
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.Logout();
            }
            else
            {
                // Fallback: Clear auth data manually
                PlayerPrefs.DeleteKey("auth_token");
                PlayerPrefs.DeleteKey("user_data");
                PlayerPrefs.Save();
            }

            // .IO style: stay in lobby as guest, don't go to LoginScene
            // The lobby UI will refresh to show guest state
            OnLogoutPerformed?.Invoke();
        }

        // ─── Exit Game (in-match only) ───────────────────────────────────────

        private void OnExitGameClicked()
        {
            // Show confirm dialog
            if (exitConfirmOverlay != null)
                exitConfirmOverlay.RemoveFromClassList("hidden");
        }

        private void OnExitConfirmYes()
        {
            if (exitConfirmOverlay != null)
                exitConfirmOverlay.AddToClassList("hidden");
            HideSettings();

            // Exit game — despawn player and load lobby
            Time.timeScale = 1f;
            if (NetworkManager.Instance != null && NetworkManager.Instance.Runner != null)
            {
                var runner = NetworkManager.Instance.Runner;
                var ourController = UnityEngine.Object.FindObjectsOfType<ArtisansGuns.Game.PlayerController>()
                    .FirstOrDefault(pc => pc.Object != null && pc.Object.HasInputAuthority);
                if (ourController != null && ourController.Object != null)
                    runner.Despawn(ourController.Object);
                SceneManager.LoadScene("LobbyScene");
            }
            else
            {
                SceneManager.LoadScene("LobbyScene");
            }
        }

        private void OnExitConfirmNo()
        {
            if (exitConfirmOverlay != null)
                exitConfirmOverlay.AddToClassList("hidden");
        }

        /// <summary>
        /// Show logout button only when user has a full account (not a guest).
        /// </summary>
        public void UpdateLogoutButtonVisibility()
        {
            if (logoutButton == null) return;
            bool isGuest = AuthManager.Instance == null || AuthManager.Instance.IsGuest;
            if (isGuest)
                logoutButton.AddToClassList("hidden");
            else
                logoutButton.RemoveFromClassList("hidden");
        }

        // ─── Language Toggle ─────────────────────────────────────────────────

        private void SetLanguage(LocalizationManager.Language lang)
        {
            LocalizationManager.SetLanguage(lang);
            UpdateLanguageToggleVisual();
            LocalizeSettingsLabels();
            // OnLanguageChanged event handles re-rendering all other UI controllers
        }

        private void UpdateLanguageToggleVisual()
        {
            bool isEn = LocalizationManager.CurrentLanguage == LocalizationManager.Language.EN;
            if (langEnBtn != null)
            {
                if (isEn) langEnBtn.AddToClassList("settings-toggle-active");
                else langEnBtn.RemoveFromClassList("settings-toggle-active");
            }
            if (langEsBtn != null)
            {
                if (!isEn) langEsBtn.AddToClassList("settings-toggle-active");
                else langEsBtn.RemoveFromClassList("settings-toggle-active");
            }
        }

        /// <summary>Re-apply localized text to all settings panel labels.</summary>
        public void LocalizeSettingsLabels()
        {
            if (settingsTitleLabel != null) settingsTitleLabel.text = LocalizationManager.T("SETTINGS");
            if (generalSectionLabel != null) generalSectionLabel.text = LocalizationManager.T("GENERAL");
            if (sensitivityLabel != null) sensitivityLabel.text = LocalizationManager.T("Sensitivity");
            if (renderShadowsLabel != null) renderShadowsLabel.text = LocalizationManager.T("Render Shadows");
            if (audioSectionLabel != null) audioSectionLabel.text = LocalizationManager.T("AUDIO");
            if (musicVolumeLabel != null) musicVolumeLabel.text = LocalizationManager.T("Music Volume");
            if (controlsSectionLabel != null) controlsSectionLabel.text = LocalizationManager.T("CONTROLS");
            if (fireButtonSideLabel != null) fireButtonSideLabel.text = LocalizationManager.T("Fire Button Side");
            if (languageSectionTitle != null) languageSectionTitle.text = LocalizationManager.T("LANGUAGE");
            if (languageSettingLabel != null) languageSettingLabel.text = LocalizationManager.T("Language");
            if (logoutButton != null) logoutButton.text = LocalizationManager.T("LOGOUT");
            if (exitGameButton != null) exitGameButton.text = LocalizationManager.T("EXIT");
            if (exitMatchTitleLabel != null) exitMatchTitleLabel.text = LocalizationManager.T("EXIT MATCH?");
            if (exitMatchMessageLabel != null) exitMatchMessageLabel.text = LocalizationManager.T("You won't receive rewards if you leave now.");
            if (exitConfirmYesBtn != null) exitConfirmYesBtn.text = LocalizationManager.T("LEAVE");
            if (exitConfirmNoBtn != null) exitConfirmNoBtn.text = LocalizationManager.T("STAY");
            if (roomCodeLabel != null) roomCodeLabel.text = LocalizationManager.T("ROOM CODE");
            if (fireButtonLeftBtn != null) fireButtonLeftBtn.text = LocalizationManager.T("LEFT");
            if (fireButtonRightBtn != null) fireButtonRightBtn.text = LocalizationManager.T("RIGHT");
        }
    }
}
