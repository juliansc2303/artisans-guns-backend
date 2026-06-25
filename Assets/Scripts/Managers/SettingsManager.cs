using UnityEngine;
using UnityEngine.Rendering.Universal;
using ArtisansGuns.Auth;

namespace ArtisansGuns.Managers
{
    /// <summary>
    /// SettingsManager - Source of truth for in-memory sensitivity.
    /// On each query, syncs first from AuthManager (backend data) to stay always up to date.
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        private float mouseSensitivity = 6.0f;
        private const string SENSITIVITY_KEY = "player_sensitivity";
        private const float MIN_SENSITIVITY = 1.0f;
        private const float MAX_SENSITIVITY = 100.0f;
        private const float DEFAULT_SENSITIVITY = 6.0f;

        private bool renderShadows = true;
        private const string SHADOWS_KEY = "render_shadows";

        // ParrelSync-safe key prefix (mirrors AuthManager's K() helper).
        // In clone editors, prefixed with "clone_" so each instance has its own prefs.
        private static string _keyPrefix = "";
        private static bool _prefixInit = false;
        private static void InitPrefix()
        {
            if (_prefixInit) return;
            _prefixInit = true;
#if UNITY_EDITOR
            if (Application.dataPath.Contains("_clone"))
                _keyPrefix = "clone_";
#endif
        }
        private static string PK(string key) => _keyPrefix + key;

        public event System.Action<float> OnSensitivityChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitPrefix();
        }

        private void Start()
        {
            // Subscribe here (Start is safer than Awake for cross-singleton dependencies)
            if (AuthManager.Instance != null)
                AuthManager.Instance.OnLoginSuccess += OnUserLoggedIn;

            // Load best available value now that all singletons are initialized
            RefreshFromCurrentUser();
        }

        private void OnDestroy()
        {
            if (AuthManager.Instance != null)
                AuthManager.Instance.OnLoginSuccess -= OnUserLoggedIn;
        }

        private void OnUserLoggedIn(AuthManager.UserData userData)
        {
            // Backend login response arrived - use it as the authoritative value
            float backendValue = userData.sensitivity >= MIN_SENSITIVITY ? userData.sensitivity : DEFAULT_SENSITIVITY;
            mouseSensitivity = Mathf.Clamp(backendValue, MIN_SENSITIVITY, MAX_SENSITIVITY);
            PlayerPrefs.SetFloat(PK(SENSITIVITY_KEY), mouseSensitivity);
            PlayerPrefs.Save();
            OnSensitivityChanged?.Invoke(mouseSensitivity);
            // Apply immediately to any active PlayerController (e.g. lobby preview)
            ApplySensitivity();
        }

        /// <summary>
        /// Sync mouseSensitivity from the currently logged-in user (most authoritative).
        /// Called from Start, and also from UI when opening the settings panel.
        /// </summary>
        public void RefreshFromCurrentUser()
        {
            AuthManager authMgr = AuthManager.Instance;
            AuthManager.UserData user = (authMgr != null) ? authMgr.GetCurrentUser() : null;
            if (user != null && user.sensitivity >= MIN_SENSITIVITY)
            {
                mouseSensitivity = Mathf.Clamp(user.sensitivity, MIN_SENSITIVITY, MAX_SENSITIVITY);
            }
            else
            {
                // PlayerPrefs fallback — but ignore stale values below the current minimum
                float cached = PlayerPrefs.GetFloat(PK(SENSITIVITY_KEY), DEFAULT_SENSITIVITY);
                mouseSensitivity = cached >= MIN_SENSITIVITY
                    ? Mathf.Clamp(cached, MIN_SENSITIVITY, MAX_SENSITIVITY)
                    : DEFAULT_SENSITIVITY;
            }
            PlayerPrefs.SetFloat(PK(SENSITIVITY_KEY), mouseSensitivity);

            // Load shadow setting from prefs
            LoadShadowSetting();
        }

        private void SaveSettings()
        {
            // Keep BOTH keys in sync so AuthManager.LoadSavedToken never reads a stale value
            PlayerPrefs.SetFloat(PK(SENSITIVITY_KEY), mouseSensitivity);          // prefixed "player_sensitivity"
            PlayerPrefs.SetFloat(PK("user_sensitivity"), mouseSensitivity);        // prefixed AuthManager key
            PlayerPrefs.Save();

            // Also update the in-memory AuthManager user so RefreshFromCurrentUser stays consistent
            AuthManager authMgr = AuthManager.Instance;
            if (authMgr != null)
            {
                AuthManager.UserData user = authMgr.GetCurrentUser();
                if (user != null)
                    user.sensitivity = mouseSensitivity;
            }

            // Use explicit != null (Unity overloads == to detect destroyed objects;
            // C# ?. bypasses that overload and can call methods on destroyed MonoBehaviours)
            LoadoutManager lm = LoadoutManager.Instance;
            if (lm != null)
                lm.UpdateSensitivity(mouseSensitivity);
        }

        private void ApplySensitivity()
        {
            var playerControllers = FindObjectsOfType<ArtisansGuns.Game.PlayerController>(true);
            foreach (var pc in playerControllers)
            {
                if (pc.HasInputAuthority)
                    pc.SetLookSensitivity(mouseSensitivity);
            }
        }

        public void SetMouseSensitivity(float value)
        {
            mouseSensitivity = Mathf.Clamp(value, MIN_SENSITIVITY, MAX_SENSITIVITY);
            ApplySensitivity();
            SaveSettings();
            OnSensitivityChanged?.Invoke(mouseSensitivity);
        }

        /// <summary>Returns the current in-memory sensitivity (authoritative after login/change).</summary>
        public float GetMouseSensitivity() => mouseSensitivity;

        public float GetSensitivityNormalized()
            => (GetMouseSensitivity() - MIN_SENSITIVITY) / (MAX_SENSITIVITY - MIN_SENSITIVITY);

        public void SetSensitivityNormalized(float normalized)
        {
            float value = MIN_SENSITIVITY + (normalized * (MAX_SENSITIVITY - MIN_SENSITIVITY));
            SetMouseSensitivity(value);
        }

        // ─── Render Shadows ──────────────────────────────────────────────────

        public event System.Action<bool> OnRenderShadowsChanged;

        /// <summary>Returns whether shadows are enabled (default true).</summary>
        public bool GetRenderShadows() => renderShadows;

        public void SetRenderShadows(bool enabled)
        {
            renderShadows = enabled;
            PlayerPrefs.SetInt(PK(SHADOWS_KEY), enabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyShadows();
            OnRenderShadowsChanged?.Invoke(renderShadows);
        }

        /// <summary>Load shadow setting from PlayerPrefs and apply immediately.</summary>
        private void LoadShadowSetting()
        {
            renderShadows = PlayerPrefs.GetInt(PK(SHADOWS_KEY), 1) == 1;
            ApplyShadows();
        }

        /// <summary>
        /// Applies the shadow setting globally via the active URP pipeline asset.
        /// Toggling mainLightCastShadows on the asset skips shadow map generation
        /// entirely — immediate visual change and GPU savings on mobile.
        /// </summary>
        private void ApplyShadows()
        {
            var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null) return;

            // Cache the original shadow distance so we can restore it when re-enabling
            if (renderShadows)
            {
                // Restore: if shadow distance was zeroed, restore from PlayerPrefs cache
                float saved = PlayerPrefs.GetFloat(PK("shadow_distance_backup"), 69f);
                if (urpAsset.shadowDistance < 1f)
                    urpAsset.shadowDistance = saved;
            }
            else
            {
                // Backup current distance before zeroing
                if (urpAsset.shadowDistance > 0f)
                    PlayerPrefs.SetFloat(PK("shadow_distance_backup"), urpAsset.shadowDistance);
                urpAsset.shadowDistance = 0f;
            }
        }
    }
}
