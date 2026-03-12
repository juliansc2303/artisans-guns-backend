using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ArtisansGuns.Auth
{
    /// <summary>
    /// AuthManager - .IO-style auth: Guest backed by real DB row, optional upgrade to full account.
    /// Backend: Node.js + PostgreSQL on Render.com
    /// </summary>
    public class AuthManager : MonoBehaviour
    {
        public static AuthManager Instance { get; private set; }

        /// <summary>Guest has DB row but no password. LoggedIn has full credentials.</summary>
        public enum AuthMode { Guest, LoggedIn }

        // Backend URL
        private const string BASE_URL = "https://ryvalen.onrender.com/api";
        private const int REQUEST_TIMEOUT = 120;
        
        [Header("Security")]
        [SerializeField] private string encryptionKey = "ArtisansGunsKey2026!SecureToken#";

        // Auth state
        public AuthMode CurrentAuthMode { get; private set; } = AuthMode.Guest;
        
        // Current user data
        private string currentToken;
        private UserData currentUser;
        private bool isGuestSession;

        // Events
        public event Action<UserData> OnLoginSuccess;
        public event Action<string> OnLoginFailed;
        public event Action<UserData> OnRegisterSuccess;
        public event Action<string> OnRegisterFailed;
        public event Action OnTokenExpired;
        /// <summary>Fired after guest mode is initialized (now backed by DB).</summary>
        public event Action<UserData> OnGuestReady;
        /// <summary>Fired when guest account is upgraded to full account.</summary>
        public event Action<UserData> OnUpgradeSuccess;
        public event Action<string> OnUpgradeFailed;
        /// <summary>Fired when backend is unreachable and no session can be created.</summary>
        public event Action<string> OnConnectionFailed;

        // --- ParrelSync-safe PlayerPrefs key prefix ---
        // In Editor clones the prefix is "clone_" so each editor instance
        // gets its own guest identity / token / user data.
        private static string _keyPrefix = "";

        private static void InitKeyPrefix()
        {
#if UNITY_EDITOR
            if (Application.dataPath.Contains("_clone"))
                _keyPrefix = "clone_";
#endif
        }

        /// <summary>Namespace a PlayerPrefs key. No-op in builds; adds "clone_" in ParrelSync clones.</summary>
        private static string K(string key) => _keyPrefix + key;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitKeyPrefix();
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            // .IO style: Check for saved token (could be guest or full account).
            // If valid, restore session. Otherwise create/retrieve backend guest.
            if (PlayerPrefs.HasKey(K("auth_token")))
            {
                LoadSavedToken();
                VerifyToken(valid =>
                {
                    if (valid)
                    {
                        if (isGuestSession)
                        {
                            CurrentAuthMode = AuthMode.Guest;
                            Debug.Log($"[AuthManager] Restored guest session: {currentUser?.characterName}");
                            OnGuestReady?.Invoke(currentUser);
                        }
                        else
                        {
                            CurrentAuthMode = AuthMode.LoggedIn;
                            Debug.Log($"[AuthManager] Restored session: {currentUser?.username}");
                            OnLoginSuccess?.Invoke(currentUser);
                        }
                    }
                    else
                    {
                        // Token expired or invalid, create fresh guest via backend
                        InitializeGuestFromBackend();
                    }
                });
            }
            else
            {
                // First launch or after logout, create guest via backend
                InitializeGuestFromBackend();
            }
        }

        // ===================================
        // GUEST MODE (Backend-backed)
        // ===================================

        /// <summary>
        /// Initialize guest by calling the backend. Guest gets a real DB row and JWT.
        /// Falls back to local-only guest if backend is unreachable.
        /// </summary>
        public void InitializeGuestFromBackend()
        {
            string guestId = PlayerPrefs.GetString(K("guest_id"), "");
            if (string.IsNullOrEmpty(guestId))
            {
                guestId = Guid.NewGuid().ToString("N").Substring(0, 8);
                PlayerPrefs.SetString(K("guest_id"), guestId);
                PlayerPrefs.Save();
            }

            StartCoroutine(GuestLoginCoroutine(guestId));
        }

        private IEnumerator GuestLoginCoroutine(string guestUuid)
        {
            var requestBody = new GuestLoginRequest { guestUuid = guestUuid };
            string json = JsonUtility.ToJson(requestBody);

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/auth/guest", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    GuestLoginResponse response = JsonUtility.FromJson<GuestLoginResponse>(request.downloadHandler.text);

                    if (response.success && response.user != null)
                    {
                        currentToken = response.token;
                        currentUser = response.user;
                        CurrentAuthMode = AuthMode.Guest;
                        isGuestSession = true;

                        SaveToken(currentToken);
                        SaveUserData(currentUser, true);

                        Debug.Log($"[AuthManager] Guest ready (DB-backed): {currentUser.characterName} (id={currentUser.id})");
                        OnGuestReady?.Invoke(currentUser);
                    }
                    else
                    {
                        Debug.LogError($"[AuthManager] Guest backend error: {response.error}");
                        OnConnectionFailed?.Invoke($"Server error: {response.error}");
                    }
                }
                else
                {
                    Debug.LogError($"[AuthManager] Backend unreachable: {request.error}");
                    OnConnectionFailed?.Invoke("Cannot connect to server. Make sure the backend is running.");
                }
            }
        }

        /// <summary>
        /// Fallback: create a local-only guest when backend is unreachable.
        /// </summary>
        private void InitializeLocalGuest(string guestId)
        {
            CurrentAuthMode = AuthMode.Guest;
            isGuestSession = true;
            currentToken = null;

            string guestName = $"Guest_{guestId.Substring(0, 4).ToUpper()}";

            currentUser = new UserData
            {
                id = -1,
                username = guestName,
                characterName = guestName,
                selectedCharacter = PlayerPrefs.GetString(K("guest_character"), "crimson"),
                level = 1,
                primaryWeapon = new WeaponData { weaponId = "talon_ar", skinId = "default" },
                secondaryWeapon = new WeaponData { weaponId = "bolt", skinId = "default" },
                knifeSkin = new WeaponData { weaponId = "knife", skinId = "default" },
                unlockedCharacters = new[] { "crimson", "vibe", "sight", "pato" },
                unlockedWeaponSkins = new UnlockedWeaponSkins
                {
                    rifle_phantom = new[] { "default" },
                    rifle_vandal = new[] { "default" },
                    shotgun_bucky = new[] { "default" },
                    smg_stinger = new[] { "default" },
                    pistol_ghost = new[] { "default" },
                    knife = new[] { "default" }
                },
                sensitivity = PlayerPrefs.GetFloat("player_sensitivity", 6.0f)
            };

            Debug.Log($"[AuthManager] Local guest fallback: {guestName}");
            OnGuestReady?.Invoke(currentUser);
        }

        /// <summary>
        /// Retry connecting to the backend. Called from UI retry button.
        /// </summary>
        public void RetryConnection()
        {
            Debug.Log("[AuthManager] Retrying backend connection...");
            InitializeGuestFromBackend();
        }

        /// <summary>True when operating in guest mode.</summary>
        public bool IsGuest => CurrentAuthMode == AuthMode.Guest;

        /// <summary>Save guest character selection locally (both cache keys).</summary>
        public void SetGuestCharacter(string characterId)
        {
            if (currentUser != null)
                currentUser.selectedCharacter = characterId;
            // Update both keys so LoadSavedToken and InitializeLocalGuest stay in sync
            PlayerPrefs.SetString(K("guest_character"), characterId);
            PlayerPrefs.SetString(K("user_selected_character"), characterId);
            PlayerPrefs.Save();
        }

        // ===================================
        // UPGRADE GUEST (Save Progress)
        // ===================================

        #region Upgrade Guest

        /// <summary>
        /// Upgrade the current guest account to a full account.
        /// Keeps all progress (level, loadout, currency).
        /// </summary>
        public void UpgradeGuest(string username, string password, string characterName)
        {
            if (!IsGuest || string.IsNullOrEmpty(currentToken))
            {
                OnUpgradeFailed?.Invoke("No active guest session to upgrade.");
                return;
            }
            StartCoroutine(UpgradeGuestCoroutine(username, password, characterName));
        }

        private IEnumerator UpgradeGuestCoroutine(string username, string password, string characterName)
        {
            var requestBody = new UpgradeRequest
            {
                username = username,
                password = password,
                characterName = characterName
            };
            string json = JsonUtility.ToJson(requestBody);

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/auth/upgrade", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {currentToken}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    UpgradeResponse response = JsonUtility.FromJson<UpgradeResponse>(request.downloadHandler.text);

                    if (response.success)
                    {
                        currentToken = response.token;
                        currentUser = response.user;
                        CurrentAuthMode = AuthMode.LoggedIn;
                        isGuestSession = false;

                        SaveToken(currentToken);
                        SaveUserData(currentUser, false);

                        // Clear guest UUID since account is now full
                        PlayerPrefs.DeleteKey(K("guest_id"));
                        PlayerPrefs.Save();

                        Debug.Log($"[AuthManager] Guest upgraded to: {currentUser.username}");
                        OnUpgradeSuccess?.Invoke(currentUser);
                    }
                    else
                    {
                        OnUpgradeFailed?.Invoke(response.error ?? "Upgrade failed");
                    }
                }
                else
                {
                    string errorMessage = "Network error during upgrade.";
                    try
                    {
                        ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler.text);
                        errorMessage = error.error ?? errorMessage;
                    }
                    catch { }
                    OnUpgradeFailed?.Invoke(errorMessage);
                }
            }
        }

        #endregion

        // ===================================
        // GOOGLE SIGN-IN (Link / Login)
        // ===================================

        #region Google Auth

        public event Action<UserData> OnGoogleLinkSuccess;
        public event Action<string> OnGoogleLinkFailed;

        /// <summary>Bonus blue_points awarded on the last successful Google link (0 if none).</summary>
        public int LastGoogleLinkBonus { get; private set; }
        public event Action<UserData> OnGoogleLoginSuccess;
        public event Action<string> OnGoogleLoginFailed;

        /// <summary>
        /// Link a Google account to the current guest.
        /// Sends the Google ID token to the backend for verification.
        /// </summary>
        public void GoogleLink(string googleIdToken, string characterName)
        {
            if (!IsGuest)
            {
                OnGoogleLinkFailed?.Invoke("This account is already linked. Use Sign In to restore it.");
                return;
            }
            if (string.IsNullOrEmpty(currentToken))
            {
                OnGoogleLinkFailed?.Invoke("No active session. Use Sign In to restore your linked account.");
                return;
            }
            StartCoroutine(GoogleLinkCoroutine(googleIdToken, characterName));
        }

        private IEnumerator GoogleLinkCoroutine(string googleIdToken, string characterName)
        {
            var requestBody = new GoogleLinkRequest
            {
                googleIdToken = googleIdToken,
                characterName = characterName
            };
            string json = JsonUtility.ToJson(requestBody);

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/auth/google-link", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {currentToken}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    GoogleLinkResponse response = JsonUtility.FromJson<GoogleLinkResponse>(request.downloadHandler.text);

                    if (response.success)
                    {
                        currentToken = response.token;
                        currentUser = response.user;
                        CurrentAuthMode = AuthMode.LoggedIn;
                        isGuestSession = false;
                        LastGoogleLinkBonus = response.bonusAwarded;

                        SaveToken(currentToken);
                        SaveUserData(currentUser, false);

                        PlayerPrefs.DeleteKey(K("guest_id"));
                        PlayerPrefs.Save();

                        Debug.Log($"[AuthManager] Guest linked to Google: {currentUser.characterName} (bonus: {LastGoogleLinkBonus})");
                        OnGoogleLinkSuccess?.Invoke(currentUser);
                        OnLoginSuccess?.Invoke(currentUser);
                    }
                    else
                    {
                        OnGoogleLinkFailed?.Invoke(response.error ?? "Google link failed");
                    }
                }
                else
                {
                    string errorMessage = "Network error during Google link.";
                    try
                    {
                        ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler.text);
                        errorMessage = error.error ?? errorMessage;
                    }
                    catch { }
                    OnGoogleLinkFailed?.Invoke(errorMessage);
                }
            }
        }

        /// <summary>
        /// Login with a Google account. Sends the Google ID token to backend for verification.
        /// </summary>
        public void GoogleLogin(string googleIdToken)
        {
            StartCoroutine(GoogleLoginCoroutine(googleIdToken));
        }

        private IEnumerator GoogleLoginCoroutine(string googleIdToken)
        {
            var requestBody = new GoogleLoginRequest { googleIdToken = googleIdToken };
            string json = JsonUtility.ToJson(requestBody);

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/auth/google-login", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    LoginResponse response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);

                    if (response.success)
                    {
                        currentToken = response.token;
                        currentUser = response.user;
                        CurrentAuthMode = AuthMode.LoggedIn;
                        isGuestSession = false;
                        SaveToken(currentToken);
                        SaveUserData(currentUser, false);

                        PlayerPrefs.DeleteKey(K("guest_id"));
                        PlayerPrefs.Save();

                        Debug.Log($"[AuthManager] Google login: {currentUser.characterName}");
                        OnGoogleLoginSuccess?.Invoke(currentUser);
                        OnLoginSuccess?.Invoke(currentUser);
                    }
                    else
                    {
                        OnGoogleLoginFailed?.Invoke(response.error ?? "Google login failed");
                    }
                }
                else
                {
                    string errorMessage = "Network error during Google login.";
                    try
                    {
                        ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler.text);
                        errorMessage = error.error ?? errorMessage;
                    }
                    catch { }
                    OnGoogleLoginFailed?.Invoke(errorMessage);
                }
            }
        }

        #endregion

        // ===================================
        // REGISTER (Create fresh account)
        // ===================================

        #region Register

        public void Register(string username, string password, string characterName)
        {
            StartCoroutine(RegisterCoroutine(username, password, characterName));
        }

        private IEnumerator RegisterCoroutine(string username, string password, string characterName)
        {
            var requestBody = new RegisterRequest
            {
                username = username,
                password = password,
                characterName = characterName
            };

            string json = JsonUtility.ToJson(requestBody);

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/auth/register", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    RegisterResponse response = JsonUtility.FromJson<RegisterResponse>(request.downloadHandler.text);

                    if (response.success)
                    {
                        OnRegisterSuccess?.Invoke(response.user);
                    }
                    else
                    {
                        OnRegisterFailed?.Invoke(response.error);
                    }
                }
                else
                {
                    string errorMessage = "Network error. Please check your connection.";
                    try
                    {
                        ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler.text);
                        errorMessage = error.error ?? errorMessage;
                    }
                    catch { }
                    OnRegisterFailed?.Invoke(errorMessage);
                }
            }
        }

        #endregion

        #region Login

        public void Login(string username, string password)
        {
            StartCoroutine(LoginCoroutine(username, password));
        }

        private IEnumerator LoginCoroutine(string username, string password)
        {
            var requestBody = new LoginRequest
            {
                username = username,
                password = password
            };

            string json = JsonUtility.ToJson(requestBody);

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/auth/login", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    LoginResponse response = JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);

                    if (response.success)
                    {
                        currentToken = response.token;
                        currentUser = response.user;
                        CurrentAuthMode = AuthMode.LoggedIn;
                        isGuestSession = false;
                        SaveToken(currentToken);
                        SaveUserData(currentUser, false);

                        OnLoginSuccess?.Invoke(response.user);
                    }
                    else
                    {
                        OnLoginFailed?.Invoke(response.error);
                    }
                }
                else
                {
                    string errorMessage = $"Network error: {request.error}";
                    
                    if (request.result == UnityWebRequest.Result.ConnectionError)
                    {
                        errorMessage = "Cannot connect to server. Server may be waking up (wait 120s) or offline.";
                    }
                    else if (request.result == UnityWebRequest.Result.ProtocolError)
                    {
                        errorMessage = $"Server error: {request.responseCode}";
                    }
                    else if (request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        errorMessage = "Network timeout. Server is taking too long to respond.";
                    }
                    
                    try
                    {
                        ErrorResponse error = JsonUtility.FromJson<ErrorResponse>(request.downloadHandler.text);
                        errorMessage = error.error ?? errorMessage;
                    }
                    catch { }

                    OnLoginFailed?.Invoke(errorMessage);
                }
            }
        }

        #endregion

        #region Token Management

        private void SaveToken(string token)
        {
            string encrypted = SimpleEncrypt(token);
            PlayerPrefs.SetString(K("auth_token"), encrypted);
            PlayerPrefs.Save();
        }

        private void SaveUserData(UserData user, bool isGuest)
        {
            PlayerPrefs.SetInt(K("user_id"), user.id);
            PlayerPrefs.SetString(K("user_username"), user.username ?? "");
            PlayerPrefs.SetString(K("user_character_name"), user.characterName ?? "");
            PlayerPrefs.SetString(K("user_selected_character"), user.selectedCharacter ?? "");
            PlayerPrefs.SetInt(K("user_level"), user.level);
            PlayerPrefs.SetInt(K("user_is_guest"), isGuest ? 1 : 0);
            
            if (user.primaryWeapon != null)
                PlayerPrefs.SetString(K("user_primary_weapon"), JsonUtility.ToJson(user.primaryWeapon));
            if (user.secondaryWeapon != null)
                PlayerPrefs.SetString(K("user_secondary_weapon"), JsonUtility.ToJson(user.secondaryWeapon));
            if (user.knifeSkin != null)
                PlayerPrefs.SetString(K("user_knife_skin"), JsonUtility.ToJson(user.knifeSkin));
            if (user.unlockedCharacters != null)
                PlayerPrefs.SetString(K("user_unlocked_characters"), JsonUtility.ToJson(new ArrayWrapper<string> { items = user.unlockedCharacters }));
            if (user.unlockedWeaponSkins != null)
                PlayerPrefs.SetString(K("user_unlocked_skins"), JsonUtility.ToJson(user.unlockedWeaponSkins));
            
            PlayerPrefs.SetFloat(K("user_sensitivity"), user.sensitivity);
            PlayerPrefs.Save();
        }

        private void LoadSavedToken()
        {
            if (PlayerPrefs.HasKey(K("auth_token")))
            {
                string encrypted = PlayerPrefs.GetString(K("auth_token"));
                currentToken = SimpleDecrypt(encrypted);
                isGuestSession = PlayerPrefs.GetInt(K("user_is_guest"), 0) == 1;
                // Set auth mode immediately so UI checks (IsGuest) are correct
                // before VerifyToken completes asynchronously
                CurrentAuthMode = isGuestSession ? AuthMode.Guest : AuthMode.LoggedIn;
                
                if (PlayerPrefs.HasKey(K("user_id")))
                {
                    currentUser = new UserData
                    {
                        id = PlayerPrefs.GetInt(K("user_id")),
                        username = PlayerPrefs.GetString(K("user_username")),
                        characterName = PlayerPrefs.GetString(K("user_character_name")),
                        selectedCharacter = PlayerPrefs.GetString(K("user_selected_character"), ""),
                        level = PlayerPrefs.GetInt(K("user_level"), 1)
                    };
                    
                    if (PlayerPrefs.HasKey(K("user_primary_weapon")))
                        currentUser.primaryWeapon = JsonUtility.FromJson<WeaponData>(PlayerPrefs.GetString(K("user_primary_weapon")));
                    if (PlayerPrefs.HasKey(K("user_secondary_weapon")))
                        currentUser.secondaryWeapon = JsonUtility.FromJson<WeaponData>(PlayerPrefs.GetString(K("user_secondary_weapon")));
                    if (PlayerPrefs.HasKey(K("user_knife_skin")))
                        currentUser.knifeSkin = JsonUtility.FromJson<WeaponData>(PlayerPrefs.GetString(K("user_knife_skin")));
                    else
                        currentUser.knifeSkin = new WeaponData { weaponId = "knife", skinId = "default" };
                    if (PlayerPrefs.HasKey(K("user_unlocked_characters")))
                    {
                        var wrapper = JsonUtility.FromJson<ArrayWrapper<string>>(PlayerPrefs.GetString(K("user_unlocked_characters")));
                        currentUser.unlockedCharacters = wrapper.items;
                    }
                    if (PlayerPrefs.HasKey(K("user_unlocked_skins")))
                        currentUser.unlockedWeaponSkins = JsonUtility.FromJson<UnlockedWeaponSkins>(PlayerPrefs.GetString(K("user_unlocked_skins")));
                    
                    currentUser.sensitivity = PlayerPrefs.GetFloat(K("user_sensitivity"),
                        PlayerPrefs.GetFloat("player_sensitivity", 6.0f));
                }
            }
        }

        public void VerifyToken(Action<bool> callback)
        {
            StartCoroutine(VerifyTokenCoroutine(callback));
        }

        private IEnumerator VerifyTokenCoroutine(Action<bool> callback)
        {
            if (string.IsNullOrEmpty(currentToken))
            {
                callback?.Invoke(false);
                yield break;
            }

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/auth/verify", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                request.uploadHandler = new UploadHandlerRaw(new byte[0]);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {currentToken}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<TokenVerifyResponse>(request.downloadHandler.text);
                    callback?.Invoke(response.valid);
                    
                    if (!response.valid)
                    {
                        ClearSavedAuth();
                        OnTokenExpired?.Invoke();
                    }
                }
                else
                {
                    // Backend unreachable: require backend connection
                    Debug.LogError("[AuthManager] Verify failed - backend unreachable. Cannot proceed without backend.");
                    callback?.Invoke(false);
                    ClearSavedAuth();
                    OnConnectionFailed?.Invoke("Cannot connect to server. Make sure the backend is running.");
                }
            }
        }

        /// <summary>
        /// Log out and revert to guest mode.
        /// Does NOT create a new backend guest — stays in a local-only guest
        /// state so the user can log back in with Google and recover the
        /// same account (useful for testing save / load progress).
        /// </summary>
        public void Logout()
        {
            // Sign out of Google so re-login works without app restart.
            // Only SignOut() — Disconnect() is too aggressive and can break
            // subsequent SignIn() calls by putting the SDK in an invalid state.
            if (GoogleAuthService.Instance != null)
            {
                GoogleAuthService.Instance.SignOut();
            }

            ClearSavedAuth();

            // Stay as a local-only guest without hitting backend.
            // The user can re-login with Google to restore their account.
            string tempId = Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper();
            CurrentAuthMode = AuthMode.Guest;
            isGuestSession = true;
            currentToken = null;
            currentUser = new UserData
            {
                id = 0,
                username = $"Guest_{tempId}",
                characterName = $"Guest_{tempId}",
                selectedCharacter = PlayerPrefs.GetString(K("guest_character"), ""),
                level = 1
            };

            Debug.Log($"[AuthManager] Logged out → local guest: {currentUser.characterName}");
            OnGuestReady?.Invoke(currentUser);
        }

        /// <summary>
        /// Clear all saved auth data from PlayerPrefs.
        /// </summary>
        private void ClearSavedAuth()
        {
            currentToken = null;
            currentUser = null;
            CurrentAuthMode = AuthMode.Guest;
            isGuestSession = false;

            PlayerPrefs.DeleteKey(K("auth_token"));
            PlayerPrefs.DeleteKey(K("user_id"));
            PlayerPrefs.DeleteKey(K("user_username"));
            PlayerPrefs.DeleteKey(K("user_character_name"));
            PlayerPrefs.DeleteKey(K("user_selected_character"));
            PlayerPrefs.DeleteKey(K("user_level"));
            PlayerPrefs.DeleteKey(K("user_is_guest"));
            PlayerPrefs.DeleteKey(K("user_primary_weapon"));
            PlayerPrefs.DeleteKey(K("user_secondary_weapon"));
            PlayerPrefs.DeleteKey(K("user_unlocked_characters"));
            PlayerPrefs.DeleteKey(K("user_unlocked_skins"));
            PlayerPrefs.DeleteKey(K("guest_id"));
            PlayerPrefs.Save();
        }

        public bool IsLoggedIn()
        {
            return CurrentAuthMode == AuthMode.LoggedIn && !string.IsNullOrEmpty(currentToken) && currentUser != null;
        }

        public UserData GetCurrentUser()
        {
            return currentUser;
        }
        
        public string GetCurrentToken()
        {
            return currentToken;
        }

        /// <summary>Whether the current session has a valid backend token (guest or full).</summary>
        public bool HasBackendToken()
        {
            return !string.IsNullOrEmpty(currentToken) && currentUser != null && currentUser.id > 0;
        }

        private string SimpleEncrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            
            StringBuilder encrypted = new StringBuilder();
            for (int i = 0; i < plainText.Length; i++)
            {
                encrypted.Append((char)(plainText[i] ^ encryptionKey[i % encryptionKey.Length]));
            }
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(encrypted.ToString()));
        }

        private string SimpleDecrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return "";
            
            try
            {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encryptedText));
                StringBuilder decrypted = new StringBuilder();
                for (int i = 0; i < decoded.Length; i++)
                {
                    decrypted.Append((char)(decoded[i] ^ encryptionKey[i % encryptionKey.Length]));
                }
                return decrypted.ToString();
            }
            catch
            {
                return "";
            }
        }

        #endregion

        #region Data Models
        
        [Serializable]
        private class ArrayWrapper<T>
        {
            public T[] items;
        }

        [Serializable]
        public class GuestLoginRequest
        {
            public string guestUuid;
        }

        [Serializable]
        public class GuestLoginResponse
        {
            public bool success;
            public string error;
            public string token;
            public UserData user;
        }

        [Serializable]
        public class UpgradeRequest
        {
            public string username;
            public string password;
            public string characterName;
        }

        [Serializable]
        public class UpgradeResponse
        {
            public bool success;
            public string error;
            public string token;
            public UserData user;
        }

        [Serializable]
        public class GoogleLinkResponse
        {
            public bool success;
            public string error;
            public string token;
            public int bonusAwarded;
            public UserData user;
        }

        [Serializable]
        public class GoogleLinkRequest
        {
            public string googleIdToken;
            public string characterName;
        }

        [Serializable]
        public class GoogleLoginRequest
        {
            public string googleIdToken;
        }

        [Serializable]
        public class RegisterRequest
        {
            public string username;
            public string password;
            public string characterName;
        }

        [Serializable]
        public class LoginRequest
        {
            public string username;
            public string password;
        }

        [Serializable]
        public class RegisterResponse
        {
            public bool success;
            public string error;
            public UserData user;
        }

        [Serializable]
        public class LoginResponse
        {
            public bool success;
            public string error;
            public string token;
            public UserData user;
        }

        [Serializable]
        public class ErrorResponse
        {
            public bool success;
            public string error;
        }

        [Serializable]
        public class TokenVerifyResponse
        {
            public bool valid;
            public UserData user;
        }

        [Serializable]
        public class UserData
        {
            public int id;
            public string username;
            public string characterName;
            public string selectedCharacter;
            public int level;
            public WeaponData primaryWeapon;
            public WeaponData secondaryWeapon;
            public WeaponData knifeSkin;
            public string[] unlockedCharacters;
            public UnlockedWeaponSkins unlockedWeaponSkins;
            public int bluePoints;
            public int rivalCoins;
            public int xp;
            public float sensitivity = 6.0f;
            public string selectedHat;
            public string[] unlockedHats;
        }
        
        [Serializable]
        public class WeaponData
        {
            public string weaponId;
            public string skinId;
        }
        
        [Serializable]
        public class UnlockedWeaponSkins
        {
            public string[] talon_ar;
            public string[] bolt;
            public string[] rifle_phantom;
            public string[] rifle_vandal;
            public string[] shotgun_bucky;
            public string[] smg_stinger;
            public string[] pistol_ghost;
            public string[] knife;
        }

        #endregion
    }
}
