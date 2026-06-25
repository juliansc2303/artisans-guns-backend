using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using ArtisansGuns.Auth;
using static ArtisansGuns.Managers.LocalizationManager;

namespace ArtisansGuns.UI
{
    public class AuthUIController : MonoBehaviour
    {
        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;

        [Header("Music")]
        [SerializeField] private AudioClip introMusic; // kept for backward compat, SoundManager owns playback

        private bool isMuted = false;
        private Button muteButton;
        private Label muteIcon;

        private VisualElement root;
        private VisualElement loginPanel;
        private VisualElement registerPanel;
        private VisualElement successPanel;
        private VisualElement loadingOverlay;
        private VisualElement loadingPanel;
        private VisualElement loadingSpinner;
        private Label loadingMessage;
        private Label loadingSubtext;
        private TextField usernameField;
        private TextField passwordField;
        private Button loginButton;
        private Button showRegisterButton;
        private Label errorText;
        private TextField registerUsernameField;
        private TextField registerPasswordField;
        private TextField repeatPasswordField;
        private TextField characterNameField;
        private Button createAccountButton;
        private Button backToLoginButton;
        private Label registerErrorText;
        private Label successTitle;
        private Label successMessage;
        private Label characterNameDisplay;
        private Button continueToLoginButton;

        [Header("Settings")]
        [SerializeField] private string lobbySceneName = "LobbyScene";

        private void Awake()
        {
            // BGM is owned by SoundManager (DontDestroyOnLoad).
            // Restore muted state in case we navigated back from Lobby.
            // NOTE: Use != null (Unity operator overload) instead of ?. which bypasses it.
            var sm = ArtisansGuns.Managers.SoundManager.Instance;
            if (sm != null)
            {
                isMuted = sm.IsMusicMuted;
            }
        }

        private void OnEnable()
        {
            root = uiDocument.rootVisualElement;
            loginPanel = root.Q<VisualElement>("login-panel");
            registerPanel = root.Q<VisualElement>("register-panel");
            successPanel = root.Q<VisualElement>("success-panel");
            loadingOverlay = root.Q<VisualElement>("loading-overlay");
            loadingPanel = root.Q<VisualElement>("loading-panel");
            loadingSpinner = root.Q<VisualElement>("loading-spinner");
            loadingMessage = root.Q<Label>("loading-message");
            loadingSubtext = root.Q<Label>("loading-subtext");
            usernameField = root.Q<TextField>("usernameField");
            passwordField = root.Q<TextField>("passwordField");
            loginButton = root.Q<Button>("loginButton");
            showRegisterButton = root.Q<Button>("registerButton");
            errorText = root.Q<Label>("errorText");
            registerUsernameField = root.Q<TextField>("registerUsernameField");
            registerPasswordField = root.Q<TextField>("registerPasswordField");
            repeatPasswordField = root.Q<TextField>("repeatPasswordField");
            characterNameField = root.Q<TextField>("characterNameField");
            createAccountButton = root.Q<Button>("createAccountButton");
            backToLoginButton = root.Q<Button>("backToLoginButton");
            registerErrorText = root.Q<Label>("registerErrorText");
            successTitle = root.Q<Label>("successTitle");
            successMessage = root.Q<Label>("successMessage");
            characterNameDisplay = root.Q<Label>("characterNameDisplay");
            continueToLoginButton = root.Q<Button>("continueToLoginButton");

            loginButton.clicked += OnLoginClicked;
            showRegisterButton.clicked += OnShowRegisterClicked;
            createAccountButton.clicked += OnCreateAccountClicked;
            backToLoginButton.clicked += OnBackToLoginClicked;
            continueToLoginButton.clicked += OnContinueToLoginClicked;

            // Mute button
            muteButton = root.Q<Button>("muteButton");
            muteIcon   = root.Q<Label>("muteIcon");
            if (muteButton != null)
                muteButton.clicked += ToggleMute;

            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnLoginSuccess += HandleLoginSuccess;
                AuthManager.Instance.OnLoginFailed += HandleLoginFailed;
                AuthManager.Instance.OnRegisterSuccess += HandleRegisterSuccess;
                AuthManager.Instance.OnRegisterFailed += HandleRegisterFailed;
                AuthManager.Instance.OnGuestReady += HandleGuestReady;
            }

            // .IO style: auto-guest → lobby. No login form shown by default.
            // AuthManager.Start() will create/restore guest → OnGuestReady fires → auto-navigate.
            // If AuthManager already has a session (DontDestroyOnLoad), navigate immediately.
            if (AuthManager.Instance != null && AuthManager.Instance.HasBackendToken())
            {
                SceneManager.LoadScene(lobbySceneName);
                return;
            }

            // Hide login/register panels, show loading while guest session initializes
            loginPanel?.AddToClassList("hidden");
            registerPanel?.AddToClassList("hidden");
            successPanel?.AddToClassList("hidden");
            ShowLoading(T("CONNECTING..."), T("SETTING UP SESSION"));
        }

        private void Start()
        {
            // Register click sounds in Start() to ensure SoundManager.Instance has finished its Awake() creation
            ArtisansGuns.Managers.SoundManager.Instance?.RegisterGlobalClickSounds(root);
        }

        private void OnDisable()
        {
            StopSpinnerAnimation();
            if (loginButton != null) loginButton.clicked -= OnLoginClicked;
            if (showRegisterButton != null) showRegisterButton.clicked -= OnShowRegisterClicked;
            if (createAccountButton != null) createAccountButton.clicked -= OnCreateAccountClicked;
            if (backToLoginButton != null) backToLoginButton.clicked -= OnBackToLoginClicked;
            if (continueToLoginButton != null) continueToLoginButton.clicked -= OnContinueToLoginClicked;
            if (muteButton != null) muteButton.clicked -= ToggleMute;

            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnLoginSuccess -= HandleLoginSuccess;
                AuthManager.Instance.OnLoginFailed -= HandleLoginFailed;
                AuthManager.Instance.OnRegisterSuccess -= HandleRegisterSuccess;
                AuthManager.Instance.OnRegisterFailed -= HandleRegisterFailed;
                AuthManager.Instance.OnGuestReady -= HandleGuestReady;
            }
        }

        private void ToggleMute()
        {
            isMuted = !isMuted;
            ArtisansGuns.Managers.SoundManager.Instance?.SetMusicMuted(isMuted);

            if (muteIcon != null)
                muteIcon.text = isMuted ? "\u2715" : "\u266A"; // ✕ or ♪

            if (muteButton != null)
            {
                if (isMuted) muteButton.AddToClassList("muted");
                else         muteButton.RemoveFromClassList("muted");
            }
        }

        private void CheckExistingSession()
        {
            // Kept for backward-compat; no longer called in .IO auto-guest flow.
            // Login/register is now accessible from lobby settings instead.
            ShowLogin();
        }

        private void ShowLogin()
        {
            loginPanel.RemoveFromClassList("hidden");
            registerPanel.AddToClassList("hidden");
            successPanel.AddToClassList("hidden");
            ClearLoginFields();
            errorText.text = "Login";
            errorText.RemoveFromClassList("status-error");
        }

        private void ShowRegister()
        {
            loginPanel.AddToClassList("hidden");
            registerPanel.RemoveFromClassList("hidden");
            successPanel.AddToClassList("hidden");
            ClearRegisterFields();
            registerErrorText.text = "Register";
            registerErrorText.RemoveFromClassList("status-error");
        }

        private void ShowSuccess(string characterName)
        {
            loginPanel.AddToClassList("hidden");
            registerPanel.AddToClassList("hidden");
            successPanel.RemoveFromClassList("hidden");
            characterNameDisplay.text = characterName;
        }

        private void ShowLoading(string message = null, string subtext = null)
        {
            if (loadingMessage != null) loadingMessage.text = message ?? T("INITIATING SESSION...");
            if (loadingSubtext != null) loadingSubtext.text = subtext ?? T("CONNECTING TO SERVER");
            loadingOverlay?.RemoveFromClassList("hidden");
            StartSpinnerAnimation();
        }

        private void HideLoading()
        {
            loadingOverlay?.AddToClassList("hidden");
            StopSpinnerAnimation();
        }

        private System.Collections.IEnumerator spinnerCoroutine;

        private void StartSpinnerAnimation()
        {
            if (spinnerCoroutine != null) StopCoroutine(spinnerCoroutine);
            spinnerCoroutine = SpinnerAnimation();
            StartCoroutine(spinnerCoroutine);
        }

        private void StopSpinnerAnimation()
        {
            if (spinnerCoroutine != null)
            {
                StopCoroutine(spinnerCoroutine);
                spinnerCoroutine = null;
            }
        }

        private System.Collections.IEnumerator SpinnerAnimation()
        {
            if (loadingSpinner == null) yield break;
            float rotation = 0f;
            while (true)
            {
                rotation += 360f * Time.deltaTime;
                if (rotation >= 360f) rotation -= 360f;
                loadingSpinner.style.rotate = new Rotate(rotation);
                yield return null;
            }
        }

        private void OnLoginClicked()
        {
            string username = usernameField.value?.Trim();
            string password = passwordField.value;

            if (string.IsNullOrEmpty(username))
            {
                ShowLoginError(T("Username is required"));
                return;
            }
            if (string.IsNullOrEmpty(password))
            {
                ShowLoginError(T("Password is required"));
                return;
            }

            ShowLoading(T("INITIATING SESSION..."), T("CONNECTING TO SERVER (MAY TAKE UP TO 120S)"));
            AuthManager.Instance.Login(username, password);
        }

        private void OnShowRegisterClicked() => ShowRegister();
        private void OnBackToLoginClicked() => ShowLogin();
        private void OnContinueToLoginClicked() => ShowLogin();

        private void OnCreateAccountClicked()
        {
            string username = registerUsernameField.value?.Trim();
            string password = registerPasswordField.value;
            string repeatPassword = repeatPasswordField.value;
            string characterName = characterNameField.value?.Trim();

            if (string.IsNullOrEmpty(username) || username.Length < 3 || username.Length > 50)
            {
                ShowRegisterError(T("Username must be between 3 and 50 characters"));
                return;
            }
            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                ShowRegisterError(T("Password must be at least 6 characters"));
                return;
            }
            if (password != repeatPassword)
            {
                ShowRegisterError(T("Passwords do not match"));
                return;
            }
            if (string.IsNullOrEmpty(characterName) || characterName.Length < 3 || characterName.Length > 20)
            {
                ShowRegisterError(T("Character name must be between 3 and 20 characters"));
                return;
            }
            if (string.IsNullOrWhiteSpace(characterName))
            {
                ShowRegisterError(T("Character name cannot be only spaces"));
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(characterName, @"^[a-zA-Z0-9\s]+$"))
            {
                ShowRegisterError(T("Character name can only contain letters, numbers and spaces"));
                return;
            }

            createAccountButton.SetEnabled(false);
            ShowLoading(T("CREATING ACCOUNT..."), T("SETTING UP USER"));
            AuthManager.Instance.Register(username, password, characterName);
        }

        private void HandleLoginSuccess(AuthManager.UserData user)
        {
            HideLoading();
            SceneManager.LoadScene(lobbySceneName);
        }

        /// <summary>
        /// .IO style: guest session ready → auto-navigate to lobby.
        /// </summary>
        private void HandleGuestReady(AuthManager.UserData user)
        {
            HideLoading();
            Debug.Log($"[AuthUI] Guest ready ({user?.characterName}), auto-navigating to lobby");
            SceneManager.LoadScene(lobbySceneName);
        }

        private void HandleLoginFailed(string error)
        {
            HideLoading();
            ShowLoginError(error);
        }

        private void HandleRegisterSuccess(AuthManager.UserData user)
        {
            HideLoading();
            createAccountButton.SetEnabled(true);
            ShowSuccess(user.characterName);
        }

        private void HandleRegisterFailed(string error)
        {
            HideLoading();
            createAccountButton.SetEnabled(true);
            ShowRegisterError(error);
        }

        private void ShowLoginError(string message)
        {
            errorText.text = message;
            errorText.AddToClassList("status-error");
        }

        private void ShowRegisterError(string message)
        {
            registerErrorText.text = message;
            registerErrorText.AddToClassList("status-error");
        }

        private void ClearLoginFields()
        {
            if (usernameField != null) usernameField.value = "";
            if (passwordField != null) passwordField.value = "";
        }

        private void ClearRegisterFields()
        {
            if (registerUsernameField != null) registerUsernameField.value = "";
            if (registerPasswordField != null) registerPasswordField.value = "";
            if (repeatPasswordField != null) repeatPasswordField.value = "";
            if (characterNameField != null) characterNameField.value = "";
        }
    }
}
