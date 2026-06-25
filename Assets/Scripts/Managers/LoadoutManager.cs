using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ArtisansGuns.Auth;

namespace ArtisansGuns.Managers
{
    /// <summary>
    /// LoadoutManager - Manages player loadout (character and weapons)
    /// Communicates with backend API for loadout CRUD operations
    /// Stores current loadout state from AuthManager login
    /// </summary>
    public class LoadoutManager : MonoBehaviour
    {
        public static LoadoutManager Instance { get; private set; }

        // Backend URL - Render production
        private const string BASE_URL = "https://ryvalen.onrender.com/api";
        private const int REQUEST_TIMEOUT = 120; // 2 minutos para cold start

        // Current loadout state (populated from login)
        private LoadoutData currentLoadout;

        // Events for UI updates
        public event Action<LoadoutData> OnLoadoutUpdated;
        public event Action<string> OnLoadoutError;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            // Subscribe in Start() so AuthManager.Instance is guaranteed to exist
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnLoginSuccess += InitializeLoadoutFromAuth;
                AuthManager.Instance.OnGuestReady += InitializeLoadoutFromAuth;
                
                // Do NOT eagerly init from cached user data here.
                // Wait for OnGuestReady / OnLoginSuccess which only fire
                // after the backend has confirmed the session.
            }
        }

        private void OnDestroy()
        {
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnLoginSuccess -= InitializeLoadoutFromAuth;
                AuthManager.Instance.OnGuestReady -= InitializeLoadoutFromAuth;
            }
        }

        /// <summary>
        /// Initialize loadout from AuthManager after successful login
        /// </summary>
        private void InitializeLoadoutFromAuth(AuthManager.UserData userData)
        {
            currentLoadout = new LoadoutData
            {
                userId = userData.id,
                username = userData.username,
                characterName = userData.characterName,
                selectedCharacter = userData.selectedCharacter,
                level = userData.level,
                primaryWeapon = userData.primaryWeapon,
                secondaryWeapon = userData.secondaryWeapon,
                knifeSkin = userData.knifeSkin ?? new AuthManager.WeaponData { weaponId = "knife", skinId = "default" },
                unlockedCharacters = userData.unlockedCharacters,
                unlockedWeaponSkins = userData.unlockedWeaponSkins,
                bluePoints = userData.bluePoints,
                rivalCoins = userData.rivalCoins,
                xp = userData.xp,
                sensitivity = userData.sensitivity > 0 ? userData.sensitivity : 6.0f,
                selectedHat = userData.selectedHat ?? "none",
                unlockedHats = userData.unlockedHats ?? new string[] { "none" },
                // Restore cached ability loadout (overridden by RefreshLoadout if backend is available)
                ability1 = PlayerPrefs.GetString("loadout_ability1", "smoke_grenade"),
                ability2 = PlayerPrefs.GetString("loadout_ability2", "dash"),
                ultimate = PlayerPrefs.GetString("loadout_ultimate", "crimson_ultimate")
            };

            // Debug.Log($"âœ… [LoadoutManager] Loadout initialized for {userData.username}");
            // Debug.Log($"   Character: {currentLoadout.selectedCharacter} (Level {currentLoadout.level})");
            // Debug.Log($"   Primary: {currentLoadout.primaryWeapon?.weaponId} - {currentLoadout.primaryWeapon?.skinId}");
            // Debug.Log($"   Secondary: {currentLoadout.secondaryWeapon?.weaponId} - {currentLoadout.secondaryWeapon?.skinId}");
            // Debug.Log($"   Unlocked Characters: {(currentLoadout.unlockedCharacters != null ? string.Join(", ", currentLoadout.unlockedCharacters) : "None")}");
            // Debug.Log($"   ðŸ’° Currency: Blue Points={currentLoadout.bluePoints}, Rival Coins={currentLoadout.rivalCoins}");

            OnLoadoutUpdated?.Invoke(currentLoadout);

            // If we have a backend connection, fetch fresh loadout from DB
            // This ensures we always have the latest data (not stale PlayerPrefs cache)
            if (AuthManager.Instance != null && AuthManager.Instance.HasBackendToken())
            {
                RefreshLoadout(success =>
                {
                    if (success)
                        Debug.Log($"[LoadoutManager] Loadout refreshed from backend: character={currentLoadout.selectedCharacter}");
                    else
                        Debug.LogWarning("[LoadoutManager] Could not refresh loadout from backend, using cached data");
                });
            }
        }

        /// <summary>
        /// Get current loadout data
        /// </summary>
        public LoadoutData GetLoadout()
        {
            return currentLoadout;
        }

        /// <summary>
        /// Check if loadout is initialized
        /// </summary>
        public bool IsInitialized()
        {
            return currentLoadout != null;
        }

        #region Character Management

        /// <summary>
        /// True when the guest has no backend connection (local-only fallback).
        /// DB-backed guests (with JWT + id > 0) should use the API like normal users.
        /// </summary>
        private bool IsLocalOnlyGuest
        {
            get
            {
                var auth = AuthManager.Instance;
                return auth != null && auth.IsGuest && !auth.HasBackendToken();
            }
        }

        /// <summary>
        /// Update selected character (must be unlocked)
        /// </summary>
        public void UpdateCharacter(string characterId, Action<bool> callback = null)
        {
            if (!IsCharacterUnlocked(characterId))
            {
                OnLoadoutError?.Invoke($"Character {characterId} is locked");
                callback?.Invoke(false);
                return;
            }

            // Local-only fallback guest (backend unreachable): save locally
            if (IsLocalOnlyGuest)
            {
                currentLoadout.selectedCharacter = characterId;
                AuthManager.Instance.SetGuestCharacter(characterId);
                OnLoadoutUpdated?.Invoke(currentLoadout);
                callback?.Invoke(true);
                return;
            }

            // DB-backed guest or logged-in user: persist to backend
            StartCoroutine(UpdateLoadoutCoroutine(new LoadoutUpdateRequest
            {
                selectedCharacter = characterId,
                primaryWeapon = currentLoadout.primaryWeapon,
                secondaryWeapon = currentLoadout.secondaryWeapon
            }, callback));
        }

        /// <summary>
        /// Check if a character is unlocked
        /// </summary>
        public bool IsCharacterUnlocked(string characterId)
        {
            if (currentLoadout?.unlockedCharacters == null) return false;
            return Array.Exists(currentLoadout.unlockedCharacters,
                c => string.Equals(c, characterId, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Weapon Management

        /// <summary>
        /// Update primary weapon (must be unlocked)
        /// </summary>
        public void UpdatePrimaryWeapon(string weaponId, string skinId, Action<bool> callback = null)
        {
            if (!IsSkinUnlocked(weaponId, skinId))
            {
                OnLoadoutError?.Invoke($"Skin {skinId} is locked");
                callback?.Invoke(false);
                return;
            }

            // Local-only fallback guest: save locally
            if (IsLocalOnlyGuest)
            {
                currentLoadout.primaryWeapon = new AuthManager.WeaponData { weaponId = weaponId, skinId = skinId };
                OnLoadoutUpdated?.Invoke(currentLoadout);
                callback?.Invoke(true);
                return;
            }

            // Optimistic local update so GetLoadout() is immediate
            currentLoadout.primaryWeapon = new AuthManager.WeaponData { weaponId = weaponId, skinId = skinId };

            StartCoroutine(UpdateLoadoutCoroutine(new LoadoutUpdateRequest
            {
                selectedCharacter = currentLoadout?.selectedCharacter ?? "crimson",
                primaryWeapon = new AuthManager.WeaponData
                {
                    weaponId = weaponId,
                    skinId = skinId
                },
                secondaryWeapon = currentLoadout?.secondaryWeapon ?? new AuthManager.WeaponData { weaponId = "pistol_ghost", skinId = "default" }
            }, callback));
        }

        /// <summary>
        /// Update secondary weapon (must be unlocked)
        /// </summary>
        public void UpdateSecondaryWeapon(string weaponId, string skinId, Action<bool> callback = null)
        {
            if (!IsSkinUnlocked(weaponId, skinId))
            {
                OnLoadoutError?.Invoke($"Skin {skinId} is locked");
                callback?.Invoke(false);
                return;
            }

            // Local-only fallback guest: save locally
            if (IsLocalOnlyGuest)
            {
                currentLoadout.secondaryWeapon = new AuthManager.WeaponData { weaponId = weaponId, skinId = skinId };
                OnLoadoutUpdated?.Invoke(currentLoadout);
                callback?.Invoke(true);
                return;
            }

            // Optimistic local update so GetLoadout() is immediate
            currentLoadout.secondaryWeapon = new AuthManager.WeaponData { weaponId = weaponId, skinId = skinId };

            StartCoroutine(UpdateLoadoutCoroutine(new LoadoutUpdateRequest
            {
                selectedCharacter = currentLoadout?.selectedCharacter ?? "crimson",
                primaryWeapon = currentLoadout?.primaryWeapon ?? new AuthManager.WeaponData { weaponId = "rifle_phantom", skinId = "default" },
                secondaryWeapon = new AuthManager.WeaponData
                {
                    weaponId = weaponId,
                    skinId = skinId
                }
            }, callback));
        }

        /// <summary>
        /// Update knife skin (must be unlocked)
        /// </summary>
        public void UpdateKnifeSkin(string skinId, Action<bool> callback = null)
        {
            if (!IsSkinUnlocked("knife", skinId))
            {
                OnLoadoutError?.Invoke($"Knife skin {skinId} is locked");
                callback?.Invoke(false);
                return;
            }

            // Local-only fallback guest: save locally
            if (IsLocalOnlyGuest)
            {
                currentLoadout.knifeSkin = new AuthManager.WeaponData { weaponId = "knife", skinId = skinId };
                OnLoadoutUpdated?.Invoke(currentLoadout);
                callback?.Invoke(true);
                return;
            }

            // Optimistic local update so GetLoadout() is immediate
            currentLoadout.knifeSkin = new AuthManager.WeaponData { weaponId = "knife", skinId = skinId };

            StartCoroutine(UpdateLoadoutCoroutine(new LoadoutUpdateRequest
            {
                selectedCharacter = currentLoadout?.selectedCharacter ?? "crimson",
                primaryWeapon = currentLoadout?.primaryWeapon ?? new AuthManager.WeaponData { weaponId = "rifle_phantom", skinId = "default" },
                secondaryWeapon = currentLoadout?.secondaryWeapon ?? new AuthManager.WeaponData { weaponId = "pistol_ghost", skinId = "default" },
                knifeSkin = new AuthManager.WeaponData
                {
                    weaponId = "knife",
                    skinId = skinId
                }
            }, callback));
        }

        /// <summary>
        /// Update selected agent/character
        /// </summary>
        public void UpdateAgent(string agentId, Action<bool> callback = null)
        {
            // Local-only fallback guest: save locally
            if (IsLocalOnlyGuest)
            {
                currentLoadout.selectedCharacter = agentId;
                AuthManager.Instance.SetGuestCharacter(agentId);
                OnLoadoutUpdated?.Invoke(currentLoadout);
                callback?.Invoke(true);
                return;
            }

            // Optimistic local update so GetLoadout() is immediate
            currentLoadout.selectedCharacter = agentId;

            // Include current weapons to avoid 400 error from backend
            StartCoroutine(UpdateLoadoutCoroutine(new LoadoutUpdateRequest
            {
                selectedCharacter = agentId,
                primaryWeapon = currentLoadout.primaryWeapon ?? new AuthManager.WeaponData { weaponId = "rifle_phantom", skinId = "default" },
                secondaryWeapon = currentLoadout.secondaryWeapon ?? new AuthManager.WeaponData { weaponId = "pistol_ghost", skinId = "default" }
            }, callback));
        }

        /// <summary>
        /// Check if a weapon skin is unlocked
        /// </summary>
        public bool IsSkinUnlocked(string weaponId, string skinId)
        {
            // Default skins are always unlocked
            if (skinId == "default")
            {
                return true;
            }

            if (currentLoadout?.unlockedWeaponSkins == null) return false;

            string[] unlockedSkins = weaponId switch
            {
                "talon_ar" => currentLoadout.unlockedWeaponSkins.talon_ar,
                "bolt" => currentLoadout.unlockedWeaponSkins.bolt,
                "rifle_phantom" => currentLoadout.unlockedWeaponSkins.rifle_phantom,
                "rifle_vandal" => currentLoadout.unlockedWeaponSkins.rifle_vandal,
                "shotgun_bucky" => currentLoadout.unlockedWeaponSkins.shotgun_bucky,
                "smg_stinger" => currentLoadout.unlockedWeaponSkins.smg_stinger,
                "pistol_ghost" => currentLoadout.unlockedWeaponSkins.pistol_ghost,
                "knife" => currentLoadout.unlockedWeaponSkins.knife,
                _ => null
            };

            if (unlockedSkins == null) return false;
            return Array.Exists(unlockedSkins, s => s == skinId);
        }

        /// <summary>
        /// Get all unlocked skins for a specific weapon
        /// </summary>
        public string[] GetUnlockedSkinsForWeapon(string weaponId)
        {
            if (currentLoadout?.unlockedWeaponSkins == null) return new string[0];

            return weaponId switch
            {
                "talon_ar" => currentLoadout.unlockedWeaponSkins.talon_ar ?? new string[0],
                "bolt" => currentLoadout.unlockedWeaponSkins.bolt ?? new string[0],
                "rifle_phantom" => currentLoadout.unlockedWeaponSkins.rifle_phantom ?? new string[0],
                "rifle_vandal" => currentLoadout.unlockedWeaponSkins.rifle_vandal ?? new string[0],
                "shotgun_bucky" => currentLoadout.unlockedWeaponSkins.shotgun_bucky ?? new string[0],
                "smg_stinger" => currentLoadout.unlockedWeaponSkins.smg_stinger ?? new string[0],
                "pistol_ghost" => currentLoadout.unlockedWeaponSkins.pistol_ghost ?? new string[0],
                _ => new string[0]
            };
        }

        #endregion

        #region Hats

        public bool IsHatUnlocked(string hatId)
        {
            if (currentLoadout?.unlockedHats == null) return false;
            return Array.Exists(currentLoadout.unlockedHats, h => h == hatId);
        }

        public void UpdateHat(string hatId, Action<bool> callback = null)
        {
            if (this == null) { callback?.Invoke(false); return; }
            if (currentLoadout == null) { callback?.Invoke(false); return; }

            currentLoadout.selectedHat = hatId;
            OnLoadoutUpdated?.Invoke(currentLoadout);

            var updateData = new LoadoutUpdateRequest
            {
                selectedCharacter = currentLoadout.selectedCharacter,
                primaryWeapon = currentLoadout.primaryWeapon,
                secondaryWeapon = currentLoadout.secondaryWeapon,
                knifeSkin = currentLoadout.knifeSkin,
                sensitivity = currentLoadout.sensitivity,
                selectedHat = hatId,
                ability1 = currentLoadout.ability1,
                ability2 = currentLoadout.ability2,
                ultimate = currentLoadout.ultimate
            };
            StartCoroutine(UpdateLoadoutCoroutine(updateData, callback));
        }

        /// <summary>
        /// Update the ability loadout (ability1, ability2, ultimate) and sync to backend.
        /// </summary>
        public void UpdateAbilities(string ability1Id, string ability2Id, string ultimateId, Action<bool> callback = null)
        {
            if (this == null) { callback?.Invoke(false); return; }
            if (currentLoadout == null) { callback?.Invoke(false); return; }

            currentLoadout.ability1 = ability1Id;
            currentLoadout.ability2 = ability2Id;
            currentLoadout.ultimate = ultimateId;
            OnLoadoutUpdated?.Invoke(currentLoadout);

            // Always cache to PlayerPrefs so next session starts with correct abilities
            PlayerPrefs.SetString("loadout_ability1", ability1Id);
            PlayerPrefs.SetString("loadout_ability2", ability2Id);
            PlayerPrefs.SetString("loadout_ultimate", ultimateId);
            PlayerPrefs.Save();

            // Local-only fallback guest: don't call backend
            if (IsLocalOnlyGuest)
            {
                callback?.Invoke(true);
                return;
            }

            var updateData = new LoadoutUpdateRequest
            {
                selectedCharacter = currentLoadout.selectedCharacter,
                primaryWeapon = currentLoadout.primaryWeapon,
                secondaryWeapon = currentLoadout.secondaryWeapon,
                knifeSkin = currentLoadout.knifeSkin,
                sensitivity = currentLoadout.sensitivity,
                selectedHat = currentLoadout.selectedHat,
                ability1 = ability1Id,
                ability2 = ability2Id,
                ultimate = ultimateId
            };
            StartCoroutine(UpdateLoadoutCoroutine(updateData, callback));
        }

        #endregion

        #region Settings

        /// <summary>
        /// Update mouse sensitivity - saves to backend and applies locally
        /// </summary>
        public void UpdateSensitivity(float value, Action<bool> callback = null)
        {
            // Guard against destroyed MonoBehaviour
            if (this == null) { callback?.Invoke(false); return; }

            // Recover currentLoadout from AuthManager if not yet initialized
            if (currentLoadout == null)
            {
                AuthManager authMgr = AuthManager.Instance;
                if (authMgr != null)
                {
                    AuthManager.UserData u = authMgr.GetCurrentUser();
                    if (u != null) InitializeLoadoutFromAuth(u);
                }
            }
            if (currentLoadout == null) { callback?.Invoke(false); return; }

            currentLoadout.sensitivity = value;

            // Local-only fallback guest: save locally only
            if (IsLocalOnlyGuest)
            {
                PlayerPrefs.SetFloat("player_sensitivity", value);
                PlayerPrefs.Save();
                callback?.Invoke(true);
                return;
            }

            StartCoroutine(UpdateSensitivityCoroutine(value, callback));
        }

        private IEnumerator UpdateSensitivityCoroutine(float value, Action<bool> callback)
        {
            AuthManager authMgr = AuthManager.Instance;
            string token = (authMgr != null) ? authMgr.GetCurrentToken() : null;
            if (string.IsNullOrEmpty(token))
            {
                callback?.Invoke(false);
                yield break;
            }

            // Only serialize sensitivity - avoids weapon validation on the backend
            string json = JsonUtility.ToJson(new SensitivityUpdateRequest { sensitivity = value });

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/loadout", "PUT"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<LoadoutResponse>(request.downloadHandler.text);
                    if (response.success)
                    {
                        if (response.loadout != null && response.loadout.sensitivity > 0)
                            currentLoadout.sensitivity = response.loadout.sensitivity;
                        callback?.Invoke(true);
                    }
                    else
                    {
                        OnLoadoutError?.Invoke(response.error);
                        callback?.Invoke(false);
                    }
                }
                else
                {
                    OnLoadoutError?.Invoke(request.error);
                    callback?.Invoke(false);
                }
            }
        }

        #endregion

        #region API Communication

        /// <summary>
        /// Fetch latest loadout from backend
        /// </summary>
        public void RefreshLoadout(Action<bool> callback = null)
        {
            // Local-only fallback guest: nothing to refresh from backend
            if (IsLocalOnlyGuest)
            {
                callback?.Invoke(true);
                return;
            }
            StartCoroutine(GetLoadoutCoroutine(callback));
        }

        private IEnumerator GetLoadoutCoroutine(Action<bool> callback)
        {
            string token = AuthManager.Instance.GetCurrentToken();
            if (string.IsNullOrEmpty(token))
            {
                // Debug.LogError("âŒ [LoadoutManager] No auth token available");
                OnLoadoutError?.Invoke("Not authenticated");
                callback?.Invoke(false);
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequest.Get($"{BASE_URL}/loadout"))
            {
                request.timeout = REQUEST_TIMEOUT;
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<LoadoutResponse>(request.downloadHandler.text);
                    if (response.success && response.loadout != null)
                    {
                        // Update current loadout with fresh data
                        currentLoadout.selectedCharacter = response.loadout.selectedCharacter;
                        currentLoadout.level = response.loadout.level;
                        currentLoadout.primaryWeapon = response.loadout.primaryWeapon;
                        currentLoadout.secondaryWeapon = response.loadout.secondaryWeapon;
                        if (response.loadout.knifeSkin != null)
                            currentLoadout.knifeSkin = response.loadout.knifeSkin;
                        currentLoadout.unlockedCharacters = response.loadout.unlockedCharacters;
                        currentLoadout.unlockedWeaponSkins = response.loadout.unlockedWeaponSkins;
                        currentLoadout.bluePoints = response.loadout.bluePoints;
                        currentLoadout.rivalCoins = response.loadout.rivalCoins;
                        currentLoadout.xp = response.loadout.xp;
                        if (response.loadout.sensitivity > 0) currentLoadout.sensitivity = response.loadout.sensitivity;
                        currentLoadout.selectedHat = response.loadout.selectedHat ?? "none";
                        currentLoadout.unlockedHats = response.loadout.unlockedHats ?? new string[] { "none" };                        if (!string.IsNullOrEmpty(response.loadout.ability1))
                            currentLoadout.ability1 = response.loadout.ability1;
                        if (!string.IsNullOrEmpty(response.loadout.ability2))
                            currentLoadout.ability2 = response.loadout.ability2;
                        if (!string.IsNullOrEmpty(response.loadout.ultimate))
                            currentLoadout.ultimate = response.loadout.ultimate;

                        // Keep PlayerPrefs cache in sync with backend
                        PlayerPrefs.SetString("loadout_ability1", currentLoadout.ability1);
                        PlayerPrefs.SetString("loadout_ability2", currentLoadout.ability2);
                        PlayerPrefs.SetString("loadout_ultimate", currentLoadout.ultimate);
                        PlayerPrefs.Save();

                        // Debug.Log("✅ [LoadoutManager] Loadout refreshed from backend");
                        OnLoadoutUpdated?.Invoke(currentLoadout);
                        callback?.Invoke(true);
                    }
                    else
                    {
                        // Debug.LogError($"❌ [LoadoutManager] Failed to get loadout: {response.error}");
                        OnLoadoutError?.Invoke(response.error);
                        callback?.Invoke(false);
                    }
                }
                else
                {
                    // Debug.LogError($"âŒ [LoadoutManager] Request failed: {request.error}");
                    OnLoadoutError?.Invoke(request.error);
                    callback?.Invoke(false);
                }
            }
        }

        private IEnumerator UpdateLoadoutCoroutine(LoadoutUpdateRequest updateData, Action<bool> callback)
        {
            string token = AuthManager.Instance.GetCurrentToken();
            if (string.IsNullOrEmpty(token))
            {
                // Debug.LogError("âŒ [LoadoutManager] No auth token available");
                OnLoadoutError?.Invoke("Not authenticated");
                callback?.Invoke(false);
                yield break;
            }

            string json = JsonUtility.ToJson(updateData);
            // Debug.Log($"ðŸ“¤ [LoadoutManager] Updating loadout: {json}");

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/loadout", "PUT"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<LoadoutResponse>(request.downloadHandler.text);
                    if (response.success && response.loadout != null)
                    {
                        // Update local loadout with response
                        currentLoadout.selectedCharacter = response.loadout.selectedCharacter;
                        currentLoadout.level = response.loadout.level;
                        currentLoadout.primaryWeapon = response.loadout.primaryWeapon;
                        currentLoadout.secondaryWeapon = response.loadout.secondaryWeapon;
                        // Guard: only overwrite inventory fields if the backend returned them
                        if (response.loadout.unlockedCharacters != null)
                            currentLoadout.unlockedCharacters = response.loadout.unlockedCharacters;
                        if (response.loadout.unlockedWeaponSkins != null)
                            currentLoadout.unlockedWeaponSkins = response.loadout.unlockedWeaponSkins;
                        if (response.loadout.bluePoints > 0 || response.loadout.rivalCoins > 0)
                        {
                            currentLoadout.bluePoints = response.loadout.bluePoints;
                            currentLoadout.rivalCoins = response.loadout.rivalCoins;
                        }
                        if (response.loadout.sensitivity > 0) currentLoadout.sensitivity = response.loadout.sensitivity;
                        currentLoadout.selectedHat = response.loadout.selectedHat ?? currentLoadout.selectedHat;
                        if (response.loadout.unlockedHats != null)
                            currentLoadout.unlockedHats = response.loadout.unlockedHats;
                        if (!string.IsNullOrEmpty(response.loadout.ability1))
                            currentLoadout.ability1 = response.loadout.ability1;
                        if (!string.IsNullOrEmpty(response.loadout.ability2))
                            currentLoadout.ability2 = response.loadout.ability2;
                        if (!string.IsNullOrEmpty(response.loadout.ultimate))
                            currentLoadout.ultimate = response.loadout.ultimate;
                        // Keep PlayerPrefs cache in sync with backend
                        PlayerPrefs.SetString("loadout_ability1", currentLoadout.ability1);
                        PlayerPrefs.SetString("loadout_ability2", currentLoadout.ability2);
                        PlayerPrefs.SetString("loadout_ultimate", currentLoadout.ultimate);
                        PlayerPrefs.Save();
                        // Debug.Log("âœ… [LoadoutManager] Loadout updated successfully");
                        OnLoadoutUpdated?.Invoke(currentLoadout);
                        callback?.Invoke(true);
                    }
                    else
                    {
                        // Debug.LogError($"âŒ [LoadoutManager] Update failed: {response.error}");
                        OnLoadoutError?.Invoke(response.error);
                        callback?.Invoke(false);
                    }
                }
                else
                {
                    // Debug.LogError($"âŒ [LoadoutManager] Request failed: {request.error}");
                    OnLoadoutError?.Invoke(request.error);
                    callback?.Invoke(false);
                }
            }
        }

        #endregion

        #region Data Models

        [Serializable]
        public class LoadoutData
        {
            public int userId;
            public string username;
            public string characterName;
            public string selectedCharacter;
            public int level;
            public AuthManager.WeaponData primaryWeapon;
            public AuthManager.WeaponData secondaryWeapon;
            public AuthManager.WeaponData knifeSkin; // Using WeaponData structure for knife too
            public string[] unlockedCharacters;
            public AuthManager.UnlockedWeaponSkins unlockedWeaponSkins;
            public int bluePoints;
            public int rivalCoins;
            public int xp;
            public float sensitivity;
            public string selectedHat;
            public string[] unlockedHats;
            // Ability loadout
            public string ability1 = "smoke_grenade";
            public string ability2 = "dash";
            public string ultimate = "crimson_ultimate";
        }

        [Serializable]
        private class SensitivityUpdateRequest
        {
            public float sensitivity;
        }

        [Serializable]
        private class LoadoutUpdateRequest
        {
            public string selectedCharacter;
            public AuthManager.WeaponData primaryWeapon;
            public AuthManager.WeaponData secondaryWeapon;
            public AuthManager.WeaponData knifeSkin;
            public float sensitivity;
            public string selectedHat;
            public string ability1;
            public string ability2;
            public string ultimate;
        }

        [Serializable]
        private class LoadoutResponse
        {
            public bool success;
            public string error;
            public LoadoutDataResponse loadout;
        }

        [Serializable]
        private class LoadoutDataResponse
        {
            public string selectedCharacter;
            public int level;
            public AuthManager.WeaponData primaryWeapon;
            public AuthManager.WeaponData secondaryWeapon;
            public AuthManager.WeaponData knifeSkin;
            public string[] unlockedCharacters;
            public AuthManager.UnlockedWeaponSkins unlockedWeaponSkins;
            public int bluePoints;
            public int rivalCoins;
            public int xp;
            public float sensitivity;
            public string selectedHat;
            public string[] unlockedHats;
            public string ability1;
            public string ability2;
            public string ultimate;
        }

        #endregion
    }
}
