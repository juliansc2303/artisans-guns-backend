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

            // Subscribe to SettingsManager changes
            if (SettingsManager.Instance != null)
                SettingsManager.Instance.OnSensitivityChanged += OnSettingsManagerSensitivityChanged;

            // Load current settings
            LoadSettings();

            // Show/hide logout button based on auth state
            UpdateLogoutButtonVisibility();
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
    }
}
