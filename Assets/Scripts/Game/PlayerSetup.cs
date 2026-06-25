using UnityEngine;
using UnityEngine.Animations.Rigging;
using Fusion;
using ArtisansGuns.Weapons;
using ArtisansGuns.Managers;
using ArtisansGuns.Networking;
using ArtisansGuns.Abilities;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// PlayerSetup - Handles weapon instantiation based on loadout
    /// Connects backend loadout data with in-game weapon prefabs
    /// </summary>
    public class PlayerSetup : NetworkBehaviour
    {
        [Header("Weapon References")]
        [Tooltip("Primary weapon config (assigned from loadout)")]
        public WeaponConfig primaryWeaponConfig;
        
        [Tooltip("Secondary weapon config (assigned from loadout)")]
        public WeaponConfig secondaryWeaponConfig;
        
        [Tooltip("Knife weapon config (assigned from loadout)")]
        public WeaponConfig knifeWeaponConfig;
        
        [Header("Weapon Attachment Points")]
        [Tooltip("Transform where weapon will be instantiated")]
        public Transform weaponHolder;
        
        [Header("Hand IK Constraints")]
        [Tooltip("TwoBoneIKConstraint for right hand on player rig")]
        public TwoBoneIKConstraint rightHandIKConstraint;
        
        [Tooltip("TwoBoneIKConstraint for left hand on player rig")]
        public TwoBoneIKConstraint leftHandIKConstraint;
        
        [Tooltip("RigBuilder component for rebuilding rig after weapon spawn")]
        public RigBuilder rigBuilder;
        
        [Tooltip("Animator component for character hands (mixamorig:Spine2)")]
        public Animator handsAnimator;
        
        [Header("Third Person View (TPV) References")]
        [Tooltip("PlayerTPVController component for managing third-person model")]
        public PlayerTPVController tpvController;
        
        [Tooltip("TwoBoneIKConstraint for right hand on TPV rig (mixamorig:Spine2)")]
        public TwoBoneIKConstraint tpvRightHandIKConstraint;
        
        [Tooltip("TwoBoneIKConstraint for left hand on TPV rig (mixamorig:Spine2)")]
        public TwoBoneIKConstraint tpvLeftHandIKConstraint;
        
        [Tooltip("RigBuilder component on TPV model for rebuilding rig after weapon spawn")]
        public RigBuilder tpvRigBuilder;

        [Header("Character Mesh (swapped by CharacterSetupHandler)")]
        [Tooltip("SkinnedMeshRenderer on the PlayerTPV object â€” mesh is replaced per character")]
        public SkinnedMeshRenderer tpvSkinnedMeshRenderer;

        [Tooltip("SkinnedMeshRenderer on the ARMS (FPV) object â€” mesh is replaced per character")]
        public SkinnedMeshRenderer armsSkinnedMeshRenderer;

        [Header("Ability Spawner")]
        [Tooltip("Transform inside PlayerCamera where ability projectiles are instantiated")]
        public Transform abilitySpawner;
        
        [Header("Runtime References")]
        private GameObject currentWeaponInstance;
        private FireWeapon currentFireWeapon;
        private GameObject currentTPVWeaponInstance; // Third-person weapon instance (visible to others)
        private GameUIManager gameUIManager;
        private PlayerController playerController;

        // Event handler delegates stored so we can unsubscribe in Despawned()
        private System.Action _onPrimarySelectHandler;
        private System.Action _onSecondarySelectHandler;
        
        [Header("Weapon State")]
        private bool isPrimaryEquipped = true;
        private bool isKnifeEquipped = false; // True when knife is currently equipped
        private int primaryAmmo = -1;   // Saved ammo for primary weapon (-1 = not initialized yet)
        private int secondaryAmmo = -1; // Saved ammo for secondary weapon (-1 = not initialized yet)
        private Vector3 weaponHolderOriginalPosition;    // Original position from prefab
        private Quaternion weaponHolderOriginalRotation; // Original rotation from prefab
        private bool hasSpawned = false; // Prevent multiple Spawned() calls

        // Ability item state (grenade FPV etc.)
        private bool isAbilityEquipped = false;
        private bool restoredPrimaryAfterAbility = true; // which slot to restore after ability ends
        private bool _tpvAbilityActive   = false;        // true while TPV grenade is shown

        // Weapon pick/drop SFX
        private static AudioClip _pickSound;
        private static AudioClip _dropSound;

        /// <summary>True when player was using primary before last grenade equip. Read by AbilitySystem.</summary>
        public bool WasUsingPrimaryBeforeAbility => restoredPrimaryAfterAbility;

        // Network sync: which weapon slot is currently active (0=primary, 1=secondary, 2=knife)
        [Networked] public int ActiveWeaponSlot { get; set; }
        private int lastActiveWeaponSlot = -1;

        // Permanent safe transform used as IK target while no weapon is equipped.
        // Burst IK jobs must always read a valid Transform â€” we park targets here
        // before destroying the old weapon so in-flight jobs don't crash.
        private Transform safeIKTarget;

        // Coroutine handles so we can stop stale IK-connect coroutines when the
        // player switches weapons before the previous one finished rebuilding.
        private System.Collections.IEnumerator fpvIKCoroutine;
        private System.Collections.IEnumerator tpvIKCoroutine;
        
        public override void Spawned()
        {
            // Log FIRST to track timing issues, but check hasSpawned to prevent duplicate execution
            if (hasSpawned)
            {
                Debug.LogWarning($"âš ï¸ [PlayerSetup] Spawned() called multiple times on NetworkObject [Id:{Object.Id}] - ignoring (hasSpawned already true)");
                return;
            }
            hasSpawned = true;

            // Create a permanent safe transform used to park IK targets before
            // destroying the old weapon. Burst IK jobs must always read a valid
            // Transform, so we redirect all targets here instead of leaving them
            // pointing at a destroyed object.
            if (safeIKTarget == null)
            {
                GameObject safeGO = new GameObject("_IKSafeTarget");
                safeGO.transform.SetParent(transform, false);
                safeIKTarget = safeGO.transform;
            }

            // Check current scene - disable visuals if in lobby
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isInLobby = currentScene == "LobbyScene";
            
            if (isInLobby)
            {
                // Disable visual components in lobby (lightweight mode)
                DisableVisualsForLobby();
                
                // Load weapon configs but don't spawn weapons yet
                LoadWeaponsFromLoadout();
                return; // Don't setup weapons/UI in lobby
            }
            
            // Re-enable visuals if they were disabled
            EnableVisualsForGame();
            
            // Load weapon configs from loadout (needed for both local and remote)
            LoadWeaponsFromLoadout();
            
            // Setup based on authority
            var pc = GetComponent<PlayerController>();
            if (pc != null && pc.IsBotControlled)
            {
                // Host drives the bot (FPV weapon for fire logic + TPV weapon for visuals)
                // Remote clients only need the TPV weapon (same as any remote player)
                if (Object.HasStateAuthority)
                    SetupBotPlayer();
                else
                    SetupRemotePlayer();
            }
            else if (Object.HasInputAuthority)
            {
                SetupLocalPlayer();
            }
            else
            {
                SetupRemotePlayer();
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            UnsubscribeEvents();

            // Stop all coroutines (DeferredDestroy, IK-connect) to prevent
            // callbacks firing on a half-destroyed object.
            StopAllCoroutines();
            fpvIKCoroutine = null;
            tpvIKCoroutine = null;

            // Safely detach IK before destroying weapons (Burst safety)
            SafeDetachFPVIK();
            SafeDetachTPVIK();

            // Destroy weapon instances
            if (currentWeaponInstance != null)
            {
                Destroy(currentWeaponInstance);
                currentWeaponInstance = null;
                currentFireWeapon = null;
            }

            // Destroy safe IK target
            if (safeIKTarget != null)
            {
                Destroy(safeIKTarget.gameObject);
                safeIKTarget = null;
            }

            hasSpawned = false;
        }

        /// <summary>
        /// Safety net: unsubscribe static events even if Despawned() never fired
        /// (e.g. scene load destroyed the GO before Fusion processed the despawn).
        /// Also disables RigBuilders so in-flight Burst IK jobs don't read freed transforms.
        /// </summary>
        private void OnDestroy()
        {
            UnsubscribeEvents();

            // Disable RigBuilders FIRST so Burst stops scheduling IK jobs.
            // This must happen before any transforms are freed by scene unload.
            if (rigBuilder != null) rigBuilder.enabled = false;
            if (tpvRigBuilder != null) tpvRigBuilder.enabled = false;
        }

        /// <summary>
        /// Removes all static event subscriptions so stale delegates on
        /// MobileControlsController (DontDestroyOnLoad) don't invoke methods
        /// on this destroyed NetworkBehaviour.
        /// </summary>
        private void UnsubscribeEvents()
        {
            ArtisansGuns.UI.MobileControlsController.OnKnifeSelect -= EquipKnife;
            if (_onPrimarySelectHandler != null)
            {
                ArtisansGuns.UI.MobileControlsController.OnPrimarySelect -= _onPrimarySelectHandler;
                _onPrimarySelectHandler = null;
            }
            if (_onSecondarySelectHandler != null)
            {
                ArtisansGuns.UI.MobileControlsController.OnSecondarySelect -= _onSecondarySelectHandler;
                _onSecondarySelectHandler = null;
            }
        }
        
        /// <summary>
        /// Setup for local player (full initialization: UI, FPS weapons, TPV weapons)
        /// </summary>
        private void SetupLocalPlayer()
        {
            
            // Debug.Log("Ã°Å¸â€Â« [PlayerSetup] Starting weapon setup for local player");
            
            // Save original weaponHolder transform (from prefab)
            if (weaponHolder != null)
            {
                weaponHolderOriginalPosition = weaponHolder.localPosition;
                weaponHolderOriginalRotation = weaponHolder.localRotation;
                // Debug.Log($"Ã°Å¸â€™Â¾ [PlayerSetup] Saved original weaponHolder position: {weaponHolderOriginalPosition}");
            }
            
            // Find GameUIManager
            gameUIManager = GetComponent<GameUIManager>();
            if (gameUIManager == null)
            {
                // Debug.LogWarning("Ã¢Å¡Â Ã¯Â¸Â [PlayerSetup] GameUIManager not found on player!");
            }
                        // Find PlayerController
            playerController = GetComponent<PlayerController>();
            if (playerController == null)
            {
                Debug.LogWarning("âš ï¸ [PlayerSetup] PlayerController not found on player!");
            }
            
            // Subscribe to UIToolkit mobile controls events for weapon selection
            if (knifeWeaponConfig != null)
            {
                ArtisansGuns.UI.MobileControlsController.OnKnifeSelect += EquipKnife;
            }
            _onPrimarySelectHandler   = () => SwitchWeapon(true);
            _onSecondarySelectHandler = () => SwitchWeapon(false);
            ArtisansGuns.UI.MobileControlsController.OnPrimarySelect   += _onPrimarySelectHandler;
            ArtisansGuns.UI.MobileControlsController.OnSecondarySelect += _onSecondarySelectHandler;
            
            // Instantiate primary weapon
            if (primaryWeaponConfig != null)
            {
                SpawnWeapon(primaryWeaponConfig, true);
                
                // Update weapon cells UI (both primary and secondary)
                UpdatePrimaryWeaponCell();
                UpdateSecondaryWeaponCell();
                
                // Initialize GameUIManager with both weapons
                if (gameUIManager != null)
                {
                    gameUIManager.InitializeWeapons(primaryWeaponConfig, secondaryWeaponConfig);
                }

                // Cache original loadout for respawn restoration
                GetComponent<WeaponDropSystem>()?.CacheOriginalLoadout(primaryWeaponConfig, secondaryWeaponConfig);

                // Show drop button (player starts with a gun)
                UpdateDropButtonVisibility();
            }
            else
            {
                // Debug.LogWarning("âš ï¸ [PlayerSetup] No primary weapon config found!");
            }
        }
        
        /// <summary>
        /// Setup for remote player (only TPV weapons, visible to others)
        /// </summary>
        private void SetupRemotePlayer()
        {
            // Spawn TPV weapon for remote players (visible to others)
            if (primaryWeaponConfig != null)
            {
                SpawnTPVWeapon(primaryWeaponConfig);
            }
            else
            {
                Debug.LogWarning("âš ï¸ [PlayerSetup] Remote player has no primary weapon config!");
            }
        }

        /// <summary>
        /// Setup for bot player (host-controlled).
        /// Spawns FPV weapon for FireWeapon logic (ammo, fire rate, raycasting)
        /// and TPV weapon for visual representation to other players.
        /// No UI subscriptions, no GameUIManager, no mobile controls.
        /// </summary>
        private void SetupBotPlayer()
        {
            playerController = GetComponent<PlayerController>();

            // Save original weaponHolder transform
            if (weaponHolder != null)
            {
                weaponHolderOriginalPosition = weaponHolder.localPosition;
                weaponHolderOriginalRotation = weaponHolder.localRotation;
            }

            // Spawn FPV weapon for FireWeapon component (handles ammo, raycasting, damage)
            // SpawnWeapon() already calls SpawnTPVWeapon() internally at the end,
            // so we don't need to call SpawnTPVWeapon() again.
            if (primaryWeaponConfig != null)
            {
                Debug.Log($"[PlayerSetup] Bot SetupBotPlayer: weapon={primaryWeaponConfig.weaponName}, tpvPrefab={(primaryWeaponConfig.prefabWeaponTPV != null ? primaryWeaponConfig.prefabWeaponTPV.name : "NULL")}, tpvController={(tpvController != null ? "OK" : "NULL")}");
                SpawnWeapon(primaryWeaponConfig, true);

                // Force weapon ready immediately (bots don't play equip animations)
                if (currentFireWeapon != null)
                    currentFireWeapon.ForceReady();
            }
            else
            {
                Debug.LogError("[PlayerSetup] Bot has null primaryWeaponConfig! Check OnBeforeSpawned weapon data.");
            }
        }


        /// <summary>
        /// Update Primary weapon cell UI via MobileControlsController.
        /// </summary>
        private void UpdatePrimaryWeaponCell()
        {
            if (primaryWeaponConfig == null) return;
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl == null) return;
            int currentAmmo = (isPrimaryEquipped && !isKnifeEquipped && currentFireWeapon != null)
                ? currentFireWeapon.GetCurrentAmmo()
                : (primaryAmmo >= 0 ? primaryAmmo : primaryWeaponConfig.maxAmmo);
            ctrl.SetPrimaryWeapon(primaryWeaponConfig.whiteIcon, primaryWeaponConfig.maxAmmo);
            ctrl.UpdatePrimaryAmmo(currentAmmo);
        }

        /// <summary>
        /// Update Secondary weapon cell UI via MobileControlsController.
        /// </summary>
        private void UpdateSecondaryWeaponCell()
        {
            if (secondaryWeaponConfig == null) return;
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl == null) return;
            int currentAmmo = (!isPrimaryEquipped && !isKnifeEquipped && currentFireWeapon != null)
                ? currentFireWeapon.GetCurrentAmmo()
                : (secondaryAmmo >= 0 ? secondaryAmmo : secondaryWeaponConfig.maxAmmo);
            ctrl.SetSecondaryWeapon(secondaryWeaponConfig.whiteIcon, secondaryWeaponConfig.maxAmmo);
            ctrl.UpdateSecondaryAmmo(currentAmmo);
        }
        /// <summary>
        /// Load weapon configs for a remote player from their synced PlayerNetworkData.
        /// </summary>
        private void LoadWeaponsFromNetworkData()
        {
            var networkData = GetComponent<ArtisansGuns.Networking.PlayerNetworkData>();
            if (networkData == null) return;

            // Load primary weapon (use skin config if a non-default skin is equipped)
            string primaryId = networkData.PrimaryWeapon.ToString();
            string primarySkin = networkData.PrimarySkin.ToString();
            if (!string.IsNullOrEmpty(primaryId))
            {
                string primaryConfigId = (!string.IsNullOrEmpty(primarySkin) && primarySkin != "default") ? primarySkin.ToLower() : primaryId.ToLower();
                primaryWeaponConfig = LoadWeaponConfigById(primaryConfigId);
                if (primaryWeaponConfig != null)
                    primaryAmmo = primaryWeaponConfig.maxAmmo;
            }

            // Load secondary weapon (use skin config if a non-default skin is equipped)
            string secondaryId = networkData.SecondaryWeapon.ToString();
            string secondarySkin = networkData.SecondarySkin.ToString();
            if (!string.IsNullOrEmpty(secondaryId))
            {
                string secondaryConfigId = (!string.IsNullOrEmpty(secondarySkin) && secondarySkin != "default") ? secondarySkin.ToLower() : secondaryId.ToLower();
                secondaryWeaponConfig = LoadWeaponConfigById(secondaryConfigId);
                if (secondaryWeaponConfig != null)
                    secondaryAmmo = secondaryWeaponConfig.maxAmmo;
            }

            // Load knife from network data (already lowercase in PlayerNetworkData)
            string knifeId = networkData.KnifeWeapon.ToString();
            if (!string.IsNullOrEmpty(knifeId))
            {
                knifeWeaponConfig = LoadWeaponConfigById(knifeId);
            }

            // Fallback to defaults if nothing loaded
            if (primaryWeaponConfig == null)
            {
                primaryWeaponConfig = Resources.Load<WeaponConfig>("Weapons/TalonAR");
                primaryAmmo = 30;
                Debug.LogWarning("[PlayerSetup] Remote player using default primary weapon");
            }

            if (secondaryWeaponConfig == null)
            {
                secondaryWeaponConfig = Resources.Load<WeaponConfig>("Weapons/Bolt");
                secondaryAmmo = 15;
                Debug.LogWarning("[PlayerSetup] Remote player using default secondary weapon");
            }

            if (knifeWeaponConfig == null)
            {
                knifeWeaponConfig = Resources.Load<WeaponConfig>("Weapons/DefaultKnife");
                Debug.LogWarning("[PlayerSetup] Remote player using default knife weapon");
            }
        }

        
        /// <summary>
        /// Load weapon configs from LoadoutManager based on backend data
        /// </summary>
        private void LoadWeaponsFromLoadout()
        {
            // For remote players, load from PlayerNetworkData instead of LoadoutManager
            if (!Object.HasInputAuthority)
            {
                LoadWeaponsFromNetworkData();
                return;
            }
            
            // Local player: use LoadoutManager
            if (LoadoutManager.Instance == null || !LoadoutManager.Instance.IsInitialized())
            {
                // Debug.LogWarning("Ã¢Å¡Â Ã¯Â¸Â [PlayerSetup] LoadoutManager not initialized, using defaults");
                // Load defaults from Resources
                primaryWeaponConfig = Resources.Load<WeaponConfig>("Weapons/TalonAR");
                primaryAmmo = primaryWeaponConfig != null ? primaryWeaponConfig.maxAmmo : 30;
                
                secondaryWeaponConfig = Resources.Load<WeaponConfig>("Weapons/Bolt");
                secondaryAmmo = secondaryWeaponConfig != null ? secondaryWeaponConfig.maxAmmo : 15;
                return;
            }
            
            var loadout = LoadoutManager.Instance.GetLoadout();
            
            // Load primary weapon (use skin config if a non-default skin is equipped)
            string primaryId = loadout.primaryWeapon?.weaponId ?? "talon_ar";
            string primarySkinId = loadout.primaryWeapon?.skinId ?? "default";
            string primaryConfigId = (primarySkinId != "default") ? primarySkinId : primaryId;
            primaryWeaponConfig = LoadWeaponConfigById(primaryConfigId);
            
            if (primaryWeaponConfig == null)
            {
                Debug.LogWarning($"[PlayerSetup] Primary config null for '{primaryConfigId}', falling back to TalonAR");
                primaryWeaponConfig = Resources.Load<WeaponConfig>("Weapons/TalonAR");
            }
            if (primaryWeaponConfig != null)
            {
                primaryAmmo = primaryWeaponConfig.maxAmmo;
            }
            
            // Load secondary weapon (use skin config if a non-default skin is equipped)
            string secondaryId = loadout.secondaryWeapon?.weaponId ?? "bolt";
            string secondarySkinId = loadout.secondaryWeapon?.skinId ?? "default";
            string secondaryConfigId = (secondarySkinId != "default") ? secondarySkinId : secondaryId;
            secondaryWeaponConfig = LoadWeaponConfigById(secondaryConfigId);

            if (secondaryWeaponConfig == null)
            {
                Debug.LogWarning($"[PlayerSetup] Secondary config null for '{secondaryConfigId}', falling back to Bolt");
                secondaryWeaponConfig = Resources.Load<WeaponConfig>("Weapons/Bolt");
            }
            if (secondaryWeaponConfig != null)
            {
                secondaryAmmo = secondaryWeaponConfig.maxAmmo;
            }

            // Load knife weapon
            string knifeId = loadout.knifeSkin?.skinId;
            if (string.IsNullOrEmpty(knifeId)) knifeId = "default";
            knifeWeaponConfig = LoadWeaponConfigById(knifeId);

            if (knifeWeaponConfig == null)
            {
                Debug.LogWarning($"[PlayerSetup] Knife config null for '{knifeId}', knife will be unavailable");
            }
        }
        
        /// <summary>
        /// Load WeaponConfig from Resources by weapon ID
        /// </summary>
        private WeaponConfig LoadWeaponConfigById(string weaponId)
        {
            // Try to load from Resources/Weapons folder
            // Convert weapon ID to proper resource name (e.g., "talon_ar" -> "TalonAR")
            string resourceName = ConvertIdToResourceName(weaponId);
            WeaponConfig config = Resources.Load<WeaponConfig>($"Weapons/{resourceName}");
            
            if (config == null)
            {
                Debug.LogWarning($"[PlayerSetup] WeaponConfig not found for ID: '{weaponId}' (tried: Weapons/{resourceName})");
            }
            return config;
        }
        
        /// <summary>
        /// Convert backend weapon ID to Unity resource name
        /// Examples: "talon_ar" -> "TalonAR", "bolt" -> "Bolt", "default" -> "DefaultKnife", "talon_skull" -> "TalonSkull"
        /// </summary>
        private string ConvertIdToResourceName(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId))
                return "TalonAR";
            
            // Handle specific cases
            if (weaponId == "talon_ar") return "TalonAR";
            if (weaponId == "bolt") return "Bolt";
            if (weaponId == "default" || weaponId == "default_knife") return "DefaultKnife";
            
            // Generic conversion: snake_case to PascalCase
            // e.g. "talon_skull" -> "TalonSkull", "rifle_phantom" -> "RiflePhantom"
            var parts = weaponId.Split('_');
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
            {
                if (part.Length > 0)
                    sb.Append(char.ToUpper(part[0])).Append(part.Substring(1));
            }
            return sb.ToString();
        }
        
        /// <summary>
        /// Instantiate weapon on weaponHolder and setup IK + Animator
        /// </summary>
        /// <param name="weaponConfig">Weapon configuration to spawn</param>
        /// <param name="isPrimary">True if spawning primary weapon, false for secondary</param>
        private void SpawnWeapon(WeaponConfig weaponConfig, bool isPrimary)
        {
            if (weaponConfig == null || weaponConfig.weaponPrefab == null)
            {
                // Debug.LogError("Ã¢ÂÅ’ [PlayerSetup] Cannot spawn weapon: config or prefab is null");
                return;
            }
            
            if (weaponHolder == null)
            {
                // Debug.LogError("Ã¢ÂÅ’ [PlayerSetup] WeaponHolder transform not assigned!");
                return;
            }
            
            // Save ammo from current weapon before destroying it (but NOT from knife)
            // Also skip saving if the slot's config was already cleared (e.g. after DropCurrentWeapon)
            if (currentFireWeapon != null && !isKnifeEquipped)
            {
                // Cancel any active reload before switching weapons
                currentFireWeapon.CancelReload();
                int currentAmmo = currentFireWeapon.GetCurrentAmmo();
                
                if (isPrimaryEquipped && primaryWeaponConfig != null)
                {
                    primaryAmmo = currentAmmo;
                }
                else if (!isPrimaryEquipped && secondaryWeaponConfig != null)
                {
                    secondaryAmmo = currentAmmo;
                }
                // Debug.Log($"Ã°Å¸â€™Â¾ [PlayerSetup] Saved ammo: {currentAmmo} for {(isPrimaryEquipped ? "primary" : "secondary")}");
            }
            
            // Clear previous weapon if any
            if (currentWeaponInstance != null)
            {
                // TIMING FIX: rigBuilder.Build() schedules the new Burst graph for the NEXT frame.
                // The current frame's Burst job is already in flight with the old target handles.
                // We must NOT destroy the old weapon until the new graph is live (1 frame later).
                // Solution:
                //   1. Park IK targets to the safe transform and call Build() (new graph queued)
                //   2. Hide + unparent the old weapon immediately (no visual artifact)
                //   3. Destroy it next frame via coroutine (Burst job is done by then)
                SafeDetachFPVIK();

                GameObject oldWeapon = currentWeaponInstance;
                oldWeapon.SetActive(false);              // Invisible immediately
                oldWeapon.transform.SetParent(null);     // Unparent so weaponHolder is free
                StartCoroutine(DeferredDestroy(oldWeapon)); // Actual Destroy() next frame
                currentWeaponInstance = null;
                currentFireWeapon = null;
            }
            
            // CRITICAL: Restore weaponHolder to original position/rotation from prefab
            // This prevents recoil/sway residuals from previous weapon
            weaponHolder.localPosition = weaponHolderOriginalPosition;
            weaponHolder.localRotation = weaponHolderOriginalRotation;
            // Debug.Log($"Ã°Å¸â€â€ž [PlayerSetup] WeaponHolder restored to original: {weaponHolderOriginalPosition}");
            
            // Instantiate weapon prefab as child of weaponHolder
            // Uses LOCAL position/rotation/scale saved in the prefab
            currentWeaponInstance = Instantiate(weaponConfig.weaponPrefab, weaponHolder);

            // Bots need the FPV weapon GO active so MonoBehaviour lifecycle (Awake/Start)
            // runs and FireWeapon can initialize properly. The weapon is on layer 6 (FPV),
            // invisible to all cameras except the local player's FPV overlay.
            if (playerController != null && playerController.IsBotControlled
                && !currentWeaponInstance.activeSelf)
            {
                currentWeaponInstance.SetActive(true);
            }
            
            // Set WeaponHolder + PlayerController BEFORE initializing components.
            // Passing playerController explicitly avoids FindObjectOfType, which in
            // multiplayer can return the REMOTE player's controller.
            var weaponRecoil = currentWeaponInstance.GetComponent<WeaponRecoil>();
            if (weaponRecoil != null)
            {
                weaponRecoil.SetWeaponHolder(weaponHolder);
                weaponRecoil.SetPlayerController(playerController);
            }
            
            var weaponSway = weaponHolder.GetComponent<WeaponSway>();
            if (weaponSway != null)
            {
                weaponSway.SetWeaponHolder(weaponHolder);
                weaponSway.SetPlayerController(playerController);
                // If new weapon has no recoil, clear sway's stale reference so sway applies directly
                if (weaponRecoil == null)
                    weaponSway.SetWeaponRecoil(null);
            }
            // Get FireWeapon component and initialize
            currentFireWeapon = currentWeaponInstance.GetComponent<FireWeapon>();
            if (currentFireWeapon != null)
            {
                currentFireWeapon.Initialize(weaponConfig);
                
                // Restore saved ammo (or use maxAmmo if first time spawning)
                int savedAmmo = isPrimary ? primaryAmmo : secondaryAmmo;
                if (savedAmmo < 0)
                {
                    savedAmmo = weaponConfig.maxAmmo; // First time spawning this weapon
                }
                currentFireWeapon.SetAmmo(savedAmmo);
                
                // Debug.Log($"Ã°Å¸â€Â« [PlayerSetup] Weapon spawned: {weaponConfig.weaponName}, Ammo: {savedAmmo}");
            }
            else
            {
                // Debug.LogError("Ã¢ÂÅ’ [PlayerSetup] FireWeapon component not found on weapon prefab!");
            }
            
            // Skip FPV IK and animator setup for bots (FPV weapon may be inactive)
            bool isBotPlayer = playerController != null && playerController.IsBotControlled;
            if (!isBotPlayer)
            {
                // Connect weapon grips to player IK constraints (async to wait for rig rebuild)
                if (fpvIKCoroutine != null) StopCoroutine(fpvIKCoroutine);
                fpvIKCoroutine = ConnectWeaponGripsToIKCoroutine(currentWeaponInstance);
                StartCoroutine(fpvIKCoroutine);
            }
            
            // Change hands AnimatorController if provided
            if (!isBotPlayer && handsAnimator != null && weaponConfig.handsAnimatorController != null)
            {
                // CRITICAL: Fully reset animator to apply new pose
                handsAnimator.enabled = false;
                handsAnimator.runtimeAnimatorController = weaponConfig.handsAnimatorController;
                handsAnimator.enabled = true;
                
                // Force Idle state (most animators use "Idle" as default state)
                handsAnimator.Rebind();
                handsAnimator.Update(0f);
                
                // Debug.Log($"ðŸŽ¬ [PlayerSetup] Hands animator changed to: {weaponConfig.handsAnimatorController.name}");
            }
            else if (handsAnimator == null)
            {
                // Debug.LogWarning("Ã¢Å¡Â Ã¯Â¸Â [PlayerSetup] Hands animator not assigned! Assign mixamorig:Spine2 animator.");
            }
            
            // Update equipped state
            isPrimaryEquipped = isPrimary;
            isKnifeEquipped = false; // Gun equipped (not knife)

            // Update active weapon highlight in UIToolkit HUD (skip for bots — they don't own the UI)
            if (!isBotPlayer)
                ArtisansGuns.UI.MobileControlsController.Instance?.SetActiveWeapon(isPrimary);

            // Inform remote players which weapon slot is now active so they swap their TPV model
            if (HasInputAuthority)
                ActiveWeaponSlot = isPrimary ? 0 : 1;
            
            // Update player movement speed based on weapon weight
            if (playerController != null)
            {
                playerController.UpdateWeaponSpeedModifier(weaponConfig.speedMultiplier);
            }
            
            // Update BOTH weapon cells UI (need to show both weapons' ammo correctly)
            if (!isBotPlayer)
            {
                UpdatePrimaryWeaponCell();
                UpdateSecondaryWeaponCell();
            }
            
            // Spawn TPV weapon (third-person view, visible to other players)
            SpawnTPVWeapon(weaponConfig);
        }
        
        /// <summary>
        /// Render() runs every frame on all clients.
        /// Remote players watch ActiveWeaponSlot for changes and swap their TPV weapon model.
        /// </summary>
        public override void Render()
        {
            // Only handle remote players
            if (HasInputAuthority) return;

            int slot = ActiveWeaponSlot;
            if (slot == lastActiveWeaponSlot) return;

            // Slot 3 = ability active (TPV managed by RPC_EquipTPVGrenade/RPC_UnequipTPVGrenade)
            if (slot == 3) { lastActiveWeaponSlot = slot; return; }

            WeaponConfig config = slot switch
            {
                0 => primaryWeaponConfig,
                1 => secondaryWeaponConfig,
                2 => knifeWeaponConfig,
                _ => primaryWeaponConfig
            };

            if (config != null)
            {
                SpawnTPVWeapon(config);
                lastActiveWeaponSlot = slot;
            }
            // If config is null, do NOT update lastActiveWeaponSlot
            // so Render retries on the next frame once the config loads.
        }

        /// <summary>
        /// Returns the WeaponConfig for the currently active weapon slot.
        /// Works on ALL clients (local + remote) because weapon configs are loaded
        /// from Resources on every client, and ActiveWeaponSlot is [Networked].
        /// Used by RPCs that need to read prefab/sound references on the receiving side.
        /// </summary>
        public WeaponConfig GetActiveWeaponConfig()
        {
            return ActiveWeaponSlot switch
            {
                0 => primaryWeaponConfig,
                1 => secondaryWeaponConfig,
                2 => knifeWeaponConfig,
                _ => primaryWeaponConfig
            };
        }

        /// <summary>
        /// Switch between primary and secondary weapon
        /// Called by GameUIManager when switch button is clicked
        /// </summary>
        /// <param name="switchToPrimary">True to switch to primary, false for secondary</param>
        public void SwitchWeapon(bool switchToPrimary)
        {
            // Already have this slot equipped — nothing to do
            if (!isAbilityEquipped && !isKnifeEquipped && isPrimaryEquipped == switchToPrimary)
                return;

            WeaponConfig targetWeapon = switchToPrimary ? primaryWeaponConfig : secondaryWeaponConfig;
            
            if (targetWeapon == null)
            {
                // Debug.LogWarning($"Ã¢Å¡Â Ã¯Â¸Â [PlayerSetup] Cannot switch to {(switchToPrimary ? "primary" : "secondary")} - weapon not equipped!");
                return;
            }
            
            // Debug.Log($"Ã°Å¸â€â€ž [PlayerSetup] Switching to {(switchToPrimary ? "PRIMARY" : "SECONDARY")}: {targetWeapon.weaponName}");
            // If grenade ability is still active, cancel it and notify remote clients to restore TPV
            if (isAbilityEquipped)
            {
                isAbilityEquipped = false;
                GetComponent<AbilitySystem>()?.CancelActiveGrenade(switchToPrimary);
            }

            SpawnWeapon(targetWeapon, switchToPrimary);

            UpdateDropButtonVisibility();
        }
        
        /// <summary>        /// Equip knife weapon
        /// Called when player presses SelectKnifeButton
        /// </summary>
        public void EquipKnife()
        {
            if (knifeWeaponConfig == null)
            {
                Debug.LogWarning("âš ï¸ [PlayerSetup] Cannot equip knife - no knife config loaded!");
                return;
            }
            
            Debug.Log($"ðŸ”ª [PlayerSetup] Equipping knife: {knifeWeaponConfig.weaponName}");
            
            // Spawn knife as weapon (use false for isPrimary since it's neither primary nor secondary)
            SpawnKnife(knifeWeaponConfig);
        }
        
        /// <summary>
        /// Spawn knife weapon (special case, doesn't use ammo saving)
        /// </summary>
        private void SpawnKnife(WeaponConfig weaponConfig)
        {
            Debug.Log($"ðŸ”ª [PlayerSetup] SpawnKnife called: {weaponConfig.weaponName}");
            
            if (weaponConfig == null || weaponConfig.weaponPrefab == null)
            {
                Debug.LogError("âŒ [PlayerSetup] Cannot spawn knife: config or prefab is null");
                return;
            }
            
            if (weaponHolder == null)
            {
                Debug.LogError("âŒ [PlayerSetup] WeaponHolder transform not assigned!");
                return;
            }
            
            // Save ammo from current weapon before destroying it (but NOT from knife)
            if (currentFireWeapon != null && !isKnifeEquipped)
            {
                // Cancel any active reload before switching weapons
                currentFireWeapon.CancelReload();
                int currentAmmo = currentFireWeapon.GetCurrentAmmo();
                
                // Save to appropriate slot based on what was equipped
                // Skip saving if the slot config was already cleared (e.g. after DropCurrentWeapon)
                if (isPrimaryEquipped && primaryWeaponConfig != null)
                {
                    primaryAmmo = currentAmmo;
                }
                else if (!isPrimaryEquipped && secondaryWeaponConfig != null)
                {
                    secondaryAmmo = currentAmmo;
                }
                Debug.Log($"ðŸ'¾ [PlayerSetup] Saved ammo before equipping knife: {currentAmmo} for {(isPrimaryEquipped ? "primary" : "secondary")}");
            }
            
            // Destroy previous weapon
            if (currentWeaponInstance != null)
            {
                // Same deferred-destroy pattern as SpawnWeapon (see comment there)
                SafeDetachFPVIK();

                GameObject oldWeapon = currentWeaponInstance;
                oldWeapon.SetActive(false);
                oldWeapon.transform.SetParent(null);
                StartCoroutine(DeferredDestroy(oldWeapon));
                currentWeaponInstance = null;
                currentFireWeapon = null;
            }
            
            // Reset weaponHolder transform to original (from prefab)
            weaponHolder.localPosition = weaponHolderOriginalPosition;
            weaponHolder.localRotation = weaponHolderOriginalRotation;
            
            // Instantiate knife
            currentWeaponInstance = Instantiate(weaponConfig.weaponPrefab, weaponHolder);
            
            // Setup weapon scripts (knife doesn't have WeaponRecoil)
            // WeaponSway lives on weaponHolder (not the weapon instance)
            var weaponSway = weaponHolder.GetComponent<WeaponSway>();
            if (weaponSway != null)
            {
                weaponSway.SetWeaponHolder(weaponHolder);
                weaponSway.SetPlayerController(playerController);
                weaponSway.SetWeaponRecoil(null); // Knife has no recoil, sway applies directly
            }
            
            // Get FireWeapon component and initialize
            currentFireWeapon = currentWeaponInstance.GetComponent<FireWeapon>();
            if (currentFireWeapon != null)
            {
                currentFireWeapon.Initialize(weaponConfig);
                // Knife has infinite ammo (999), no need to restore saved ammo
            }
            else
            {
                Debug.LogError("âŒ [PlayerSetup] FireWeapon component not found on knife prefab!");
            }
            
            // Connect grips to IK
            if (fpvIKCoroutine != null) StopCoroutine(fpvIKCoroutine);
            fpvIKCoroutine = ConnectWeaponGripsToIKCoroutine(currentWeaponInstance);
            StartCoroutine(fpvIKCoroutine);
            
            // Change hands AnimatorController if provided
            if (handsAnimator != null && weaponConfig.handsAnimatorController != null)
            {
                // CRITICAL: Fully reset animator to apply new pose
                handsAnimator.enabled = false;
                handsAnimator.runtimeAnimatorController = weaponConfig.handsAnimatorController;
                handsAnimator.enabled = true;
                
                // Force Idle state (Rebind resets to default state automatically)
                handsAnimator.Rebind();
                handsAnimator.Update(0f);
                
                Debug.Log($"ðŸŽ¬ [PlayerSetup] Knife hands animator changed to: {weaponConfig.handsAnimatorController.name}");
            }
            else if (handsAnimator == null)
            {
                Debug.LogWarning("âš ï¸ [PlayerSetup] Hands animator not assigned! Assign mixamorig:Spine2 animator.");
            }
            else if (weaponConfig.handsAnimatorController == null)
            {
                Debug.LogWarning($"âš ï¸ [PlayerSetup] Knife WeaponConfig has no handsAnimatorController assigned: {weaponConfig.weaponName}");
            }
            
            // Mark knife as equipped
            isKnifeEquipped = true;
            isPrimaryEquipped = false; // Knife replaces current weapon

            // Inform remote players that knife is now active
            if (HasInputAuthority)
                ActiveWeaponSlot = 2;

            // Clear weapon-cell highlight — neither cell active while knife is out
            ArtisansGuns.UI.MobileControlsController.Instance?.SetActiveWeaponSlot(-1);
            
            // Update player movement speed based on knife weight
            if (playerController != null)
            {
                playerController.UpdateWeaponSpeedModifier(weaponConfig.speedMultiplier);
            }
            
            // Update weapon cells UI (knife can be primary or secondary weapon)
            // For now, assume knife is in primary weapon slot
            // TODO: Detect which slot knife is in and update accordingly
            UpdatePrimaryWeaponCell();
            UpdateSecondaryWeaponCell();
            
            // Spawn TPV weapon (third-person view, visible to other players)
            SpawnTPVWeapon(weaponConfig);

            UpdateDropButtonVisibility();
        }
        
        /// <summary>
        /// Called by FireWeapon when ammo changes — refreshes both weapon cell displays.
        /// </summary>
        public void UpdateWeaponCellsOnAmmoChange()
        {
            UpdatePrimaryWeaponCell();
            UpdateSecondaryWeaponCell();
        }

        /// <summary>
        /// Get current active weapon
        /// </summary>
        public FireWeapon GetCurrentWeapon()
        {
            return currentFireWeapon;
        }

        // ─── Drop / Pick-up API (used by WeaponDropSystem) ─────────────────

        /// <summary>True when the primary weapon is currently equipped (not secondary/knife).</summary>
        public bool IsPrimaryEquipped => isPrimaryEquipped && !isKnifeEquipped;

        /// <summary>True when the knife is currently equipped.</summary>
        public bool IsKnifeEquipped => isKnifeEquipped;

        /// <summary>Returns current magazine ammo of the weapon in hand (0 if knife/null).</summary>
        public int GetCurrentAmmo()
        {
            if (currentFireWeapon == null || isKnifeEquipped) return 0;
            return currentFireWeapon.GetCurrentAmmo();
        }

        /// <summary>Returns saved primary ammo (or current if primary is in hand).</summary>
        public int GetPrimaryAmmo()
        {
            if (isPrimaryEquipped && !isKnifeEquipped && currentFireWeapon != null)
                return currentFireWeapon.GetCurrentAmmo();
            return primaryAmmo >= 0 ? primaryAmmo : (primaryWeaponConfig != null ? primaryWeaponConfig.maxAmmo : 0);
        }

        /// <summary>Returns saved secondary ammo (or current if secondary is in hand).</summary>
        public int GetSecondaryAmmo()
        {
            if (!isPrimaryEquipped && !isKnifeEquipped && currentFireWeapon != null)
                return currentFireWeapon.GetCurrentAmmo();
            return secondaryAmmo >= 0 ? secondaryAmmo : (secondaryWeaponConfig != null ? secondaryWeaponConfig.maxAmmo : 0);
        }

        /// <summary>
        /// Drops the currently equipped weapon (removes from player, clears slot, switches to fallback).
        /// Called by WeaponDropSystem. Does NOT spawn the world object — that's done by the RPC.
        /// </summary>
        public void DropCurrentWeapon(bool droppingPrimary)
        {
            // Play drop sound
            PlayDropSound();

            // Save ammo before destroying
            if (currentFireWeapon != null && !isKnifeEquipped)
            {
                currentFireWeapon.CancelReload();
            }

            // Clear the slot
            ClearWeaponSlot(droppingPrimary);

            // Switch to fallback: other gun > knife
            if (droppingPrimary && secondaryWeaponConfig != null)
            {
                SwitchWeapon(false); // equip secondary
            }
            else if (!droppingPrimary && primaryWeaponConfig != null)
            {
                SwitchWeapon(true); // equip primary
            }
            else
            {
                // Both slots empty — equip knife
                EquipKnife();
            }

            // Show/hide drop button based on new weapon
            UpdateDropButtonVisibility();
        }

        /// <summary>
        /// Clears a weapon slot (sets config to null, resets ammo, clears HUD cell).
        /// </summary>
        public void ClearWeaponSlot(bool primary)
        {
            if (primary)
            {
                primaryWeaponConfig = null;
                primaryAmmo = -1;
            }
            else
            {
                secondaryWeaponConfig = null;
                secondaryAmmo = -1;
            }

            // Clear the HUD cell
            ArtisansGuns.UI.MobileControlsController.Instance?.ClearWeaponCell(primary);
        }

        /// <summary>
        /// Equips a picked-up weapon into its correct slot and spawns it.
        /// </summary>
        public void EquipPickedWeapon(WeaponConfig config, int ammo, bool isPrimSlot)
        {
            if (config == null) return;

            // Play pick-up sound
            PlayPickSound();

            // Spawn the weapon in hand first (internally saves old weapon's ammo)
            SpawnWeapon(config, isPrimSlot);

            // Set config + ammo AFTER SpawnWeapon to avoid overwrite from internal save
            if (isPrimSlot)
            {
                primaryWeaponConfig = config;
                primaryAmmo = ammo;
            }
            else
            {
                secondaryWeaponConfig = config;
                secondaryAmmo = ammo;
            }

            // Override magazine ammo with the picked-up value
            if (currentFireWeapon != null)
                currentFireWeapon.SetAmmo(ammo);

            // Refresh HUD cells (config was set after SpawnWeapon so cells need manual update)
            UpdatePrimaryWeaponCell();
            UpdateSecondaryWeaponCell();

            UpdateDropButtonVisibility();

            // Notify remote clients so they update their cached config + TPV
            if (HasInputAuthority)
                RPC_NotifyWeaponChanged(config.weaponId, isPrimSlot ? 0 : 1);
        }

        /// <summary>
        /// Places a picked-up weapon into its slot (config + ammo + HUD) WITHOUT
        /// equipping it in hand.  The player keeps whatever weapon they currently hold.
        /// </summary>
        public void SlotWeapon(WeaponConfig config, int ammo, bool isPrimSlot)
        {
            if (config == null) return;

            // Play pick-up sound
            PlayPickSound();

            if (isPrimSlot)
            {
                primaryWeaponConfig = config;
                primaryAmmo = ammo;
            }
            else
            {
                secondaryWeaponConfig = config;
                secondaryAmmo = ammo;
            }

            // Refresh HUD cells so the icon + ammo appear
            UpdatePrimaryWeaponCell();
            UpdateSecondaryWeaponCell();

            // Notify remote clients so they update their cached config
            if (HasInputAuthority)
                RPC_NotifyWeaponChanged(config.weaponId, isPrimSlot ? 0 : 1);

            // Drop button stays whatever it was — weapon in hand didn't change
        }

        /// <summary>
        /// RPC sent by the local player when a weapon slot's config changes
        /// (e.g. picked up a different weapon). Remote clients update their cached
        /// config and re-spawn the TPV model if the changed slot is currently active.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        private void RPC_NotifyWeaponChanged(string weaponConfigId, int slot)
        {
            if (HasInputAuthority) return; // local player already handled

            WeaponConfig config = LoadWeaponConfigById(weaponConfigId);
            if (config == null) return;

            if (slot == 0)
            {
                primaryWeaponConfig = config;
                primaryAmmo = config.maxAmmo;
            }
            else if (slot == 1)
            {
                secondaryWeaponConfig = config;
                secondaryAmmo = config.maxAmmo;
            }

            // If this is the currently active slot, re-spawn TPV immediately
            if (ActiveWeaponSlot == slot)
                SpawnTPVWeapon(config);
        }

        /// <summary>
        /// Restores original loadout (called on respawn).
        /// Resets both weapons to original configs with full ammo.
        /// </summary>
        public void RestoreLoadout(WeaponConfig originalPrimary, WeaponConfig originalSecondary)
        {
            primaryWeaponConfig   = originalPrimary;
            secondaryWeaponConfig = originalSecondary;

            // Re-spawn primary weapon (this will save current weapon ammo first,
            // so we must set full ammo AFTER SpawnWeapon to avoid being overwritten)
            if (primaryWeaponConfig != null)
            {
                SpawnWeapon(primaryWeaponConfig, true);
            }

            // Force full ammo AFTER SpawnWeapon (which may have saved stale ammo from the old weapon)
            primaryAmmo   = originalPrimary   != null ? originalPrimary.maxAmmo   : -1;
            secondaryAmmo = originalSecondary != null ? originalSecondary.maxAmmo : -1;

            // Also update the live FireWeapon component to full ammo
            if (currentFireWeapon != null && originalPrimary != null)
                currentFireWeapon.SetAmmo(originalPrimary.maxAmmo);

            // Refresh HUD cells
            UpdatePrimaryWeaponCell();
            UpdateSecondaryWeaponCell();
            UpdateDropButtonVisibility();

            // Notify remote clients so their cached configs + TPV update back to originals
            if (HasInputAuthority)
            {
                if (originalPrimary != null)
                    RPC_NotifyWeaponChanged(originalPrimary.weaponId, 0);
                if (originalSecondary != null)
                    RPC_NotifyWeaponChanged(originalSecondary.weaponId, 1);
            }
        }

        /// <summary>Shows Drop button only when a gun (not knife) is in hand.</summary>
        public void UpdateDropButtonVisibility()
        {
            bool show = !isKnifeEquipped && currentFireWeapon != null;
            string weaponId = null;
            if (show)
            {
                var config = isPrimaryEquipped ? primaryWeaponConfig : secondaryWeaponConfig;
                if (config != null) weaponId = config.weaponId;
            }
            ArtisansGuns.UI.MobileControlsController.Instance?.ShowDropButton(show, weaponId);
        }

        private void PlayPickSound()
        {
            if (_pickSound == null) _pickSound = Resources.Load<AudioClip>("Sounds/PickSound");
            if (_pickSound != null) AudioSource.PlayClipAtPoint(_pickSound, transform.position);
        }

        private void PlayDropSound()
        {
            if (_dropSound == null) _dropSound = Resources.Load<AudioClip>("Sounds/DropSound");
            if (_dropSound != null) AudioSource.PlayClipAtPoint(_dropSound, transform.position);
        }
        
        /// <summary>
        /// Parks all FPV IK targets on the permanent safe transform and sets
        /// weights to 0, then rebuilds the rig synchronously. Must be called
        /// BEFORE Destroy(currentWeaponInstance) to avoid Burst NullRef crashes.
        /// </summary>
        private void SafeDetachFPVIK()
        {
            if (safeIKTarget == null) return;

            if (rightHandIKConstraint != null)
            {
                var d = rightHandIKConstraint.data;
                d.target = safeIKTarget;
                rightHandIKConstraint.data   = d;
                rightHandIKConstraint.weight = 0f;
            }

            if (leftHandIKConstraint != null)
            {
                var d = leftHandIKConstraint.data;
                d.target = safeIKTarget;
                leftHandIKConstraint.data   = d;
                leftHandIKConstraint.weight = 0f;
            }

            // Build() schedules the new Burst graph (targets now point to safeIKTarget).
            // This takes effect the NEXT frame â€” that's why we defer Destroy() by 1 frame.
            if (rigBuilder != null)
                rigBuilder.Build();
        }

        /// <summary>
        /// Parks all TPV IK targets on the permanent safe transform and sets
        /// weights to 0, then rebuilds the TPV rig synchronously. Must be called
        /// BEFORE Destroy(tpvWeaponInstance) to avoid Burst NullRef crashes.
        /// </summary>
        private void SafeDetachTPVIK()
        {
            if (safeIKTarget == null) return;

            if (tpvRightHandIKConstraint != null)
            {
                var d = tpvRightHandIKConstraint.data;
                d.target = safeIKTarget;
                tpvRightHandIKConstraint.data = d;
                tpvRightHandIKConstraint.weight = 0f;
            }

            if (tpvLeftHandIKConstraint != null)
            {
                var d = tpvLeftHandIKConstraint.data;
                d.target = safeIKTarget;
                tpvLeftHandIKConstraint.data = d;
                tpvLeftHandIKConstraint.weight = 0f;
            }

            // NOTE: Do NOT toggle tpvRigBuilder.enabled â€” that resets the animator
            // state and breaks weapon posture. Just call Build() to schedule the
            // new Burst graph with the safe target for the next frame.
            if (tpvRigBuilder != null)
                tpvRigBuilder.Build();
        }

        /// <summary>
        /// Destroys a GameObject after waiting one frame. Used for weapon switching:
        /// after SafeDetachFPVIK() + rigBuilder.Build(), we must wait 1 frame for the
        /// new Burst job graph to go live before the old weapon's transforms are freed.
        /// </summary>
        private System.Collections.IEnumerator DeferredDestroy(GameObject obj)
        {
            yield return null; // Let the new Burst graph take over
            if (obj != null) Destroy(obj);
        }

        /// <summary>
        /// Connect weapon grips to player IK constraints (coroutine version)
        /// Waits 1 frame after Build() to ensure rig is fully rebuilt
        /// Finds RightHandGrip and LeftHandGrip transforms in weapon prefab
        /// and assigns them as targets to player's IK constraints
        /// </summary>
        /// <param name="weaponInstance">Spawned weapon GameObject</param>
        private System.Collections.IEnumerator ConnectWeaponGripsToIKCoroutine(GameObject weaponInstance)
        {
            if (weaponInstance == null)
            {
                Debug.LogError("âŒ [PlayerSetup] Cannot connect grips - weapon instance is null!");
                yield break;
            }
            
            if (rightHandIKConstraint == null || leftHandIKConstraint == null)
            {
                Debug.LogError("âŒ [PlayerSetup] IK Constraints not assigned in inspector!");
                yield break;
            }
            
            if (rigBuilder == null)
            {
                yield break;
            }

            // Wait 1 frame: lets the Animator finish its current evaluation with
            // the safe (parked) targets before we switch to the real grips.
            yield return null;
            
            // Guard: weapon may have been destroyed while we waited (rapid switching)
            if (weaponInstance == null) yield break;

            // Find grip transforms in weapon prefab (recursive search)
            Transform rightGrip = FindTransformRecursive(weaponInstance.transform, "RightHandGrip");
            Transform leftGrip  = FindTransformRecursive(weaponInstance.transform, "LeftHandGrip");
            
            if (rightGrip == null)
            {
                Debug.LogError($"âŒ [PlayerSetup] RightHandGrip not found in weapon {weaponInstance.name}!");
                yield break;
            }
            
            if (leftGrip == null)
            {
                Debug.LogError($"âŒ [PlayerSetup] LeftHandGrip not found in weapon {weaponInstance.name}!");
                yield break;
            }
            
            // Assign real grips to IK constraints
            var rightData = rightHandIKConstraint.data;
            rightData.target = rightGrip;
            rightHandIKConstraint.data = rightData;
            
            var leftData = leftHandIKConstraint.data;
            leftData.target = leftGrip;
            leftHandIKConstraint.data = leftData;
            
            rightHandIKConstraint.weight = 1f;
            leftHandIKConstraint.weight  = 1f;
            
            // Rebuild rig so Burst picks up the updated targets cleanly
            rigBuilder.enabled = false;
            rigBuilder.enabled = true;
            rigBuilder.Build();
            
            yield return null; // One more frame for rig to fully settle
        }
        
        /// <summary>
        /// Recursively search for a transform by name in hierarchy
        /// Allows finding grips nested inside other objects (e.g., LeftHandGrip inside Charger)
        /// </summary>
        private Transform FindTransformRecursive(Transform parent, string name)
        {
            // Check if this transform matches
            if (parent.name == name)
                return parent;
            
            // Check all children recursively
            foreach (Transform child in parent)
            {
                Transform result = FindTransformRecursive(child, name);
                if (result != null)
                    return result;
            }
            
            return null;
        }
        
        /// <summary>
        /// Spawn TPV weapon (visible to other players, not local player)
        /// Called from SpawnWeapon() and SpawnKnife() to spawn third-person weapon model
        /// </summary>
        private void SpawnTPVWeapon(WeaponConfig weaponConfig)
        {
            // Only spawn TPV weapon if TPVController is assigned
            if (tpvController == null)
            {
                Debug.LogWarning("âš ï¸ [PlayerSetup] TPVController not assigned - skipping TPV weapon spawn");
                return;
            }
            
            // Check if weapon has TPV prefab
            if (weaponConfig.prefabWeaponTPV == null)
            {
                Debug.LogWarning($"âš ï¸ [PlayerSetup] WeaponConfig has no TPV prefab: {weaponConfig.weaponName}");
                return;
            }
            
            Debug.Log($"ðŸŽ­ [PlayerSetup] Spawning TPV weapon: {weaponConfig.weaponName}");
            
            // Park TPV IK targets on safe transform BEFORE destroying the old TPV weapon.
            // tpvRigBuilder.Build() schedules the new Burst graph for next frame.
            // The old weapon is destroyed next frame via DeferredDestroy in PlayerTPVController.
            SafeDetachTPVIK();
            
            // Spawn TPV weapon using PlayerTPVController (uses deferred destroy internally)
            tpvController.SpawnTPVWeapon(weaponConfig.prefabWeaponTPV);
            
            // Tell TPVController whether this is a knife (changes attack behavior)
            tpvController.SetIsKnife(weaponConfig.isKnife);
            
            // Pass muzzle flash data to TPVController for remote player effects.
            // Prefer tpvMuzzleFlashPrefab (simpler TPV version); fall back to FPV prefab if not set.
            GameObject tpvFlashPrefab = weaponConfig.tpvMuzzleFlashPrefab != null
                ? weaponConfig.tpvMuzzleFlashPrefab
                : weaponConfig.muzzleFlashPrefab;
            if (tpvFlashPrefab != null)
            {
                tpvController.SetMuzzleFlashData(tpvFlashPrefab, weaponConfig.muzzleFlashDuration, weaponConfig.tpvMuzzleFlashScale);
            }

            // Pass TPV bullet trail data
            tpvController.SetTrailData(weaponConfig.tpvTrailPrefab, weaponConfig.tpvTrailSpeed);

            // Pass TPV fire sound (fall back to FPV fire sound if no TPV-specific clip)
            AudioClip tpvFireClip = weaponConfig.fireSoundTPV != null
                ? weaponConfig.fireSoundTPV
                : weaponConfig.fireSound;
            tpvController.SetFireSoundData(tpvFireClip);

            // Pass reload sounds so TPV animation events can play them in 3D
            tpvController.SetReloadSoundData(weaponConfig.reloadSounds);

            Debug.Log($"[PlayerSetup] SpawnTPVWeapon done: flash={(tpvFlashPrefab != null ? tpvFlashPrefab.name : "NULL")}, sound={(tpvFireClip != null ? tpvFireClip.name : "NULL")}, trail={(weaponConfig.tpvTrailPrefab != null ? weaponConfig.tpvTrailPrefab.name : "NULL")}");
            
            // Get reference to spawned weapon (for later cleanup)
            // Note: SpawnTPVWeapon creates the instance internally
            // We can get it from tpvController if needed
            
            // Update TPV upper body animator (Spine2) with weapon-specific controller
            Animator tpvUpperBodyAnimator = tpvController.GetUpperBodyAnimator();
            if (tpvUpperBodyAnimator != null && weaponConfig.handsAnimatorControllerTPV != null)
            {
                tpvUpperBodyAnimator.enabled = false;
                tpvUpperBodyAnimator.runtimeAnimatorController = weaponConfig.handsAnimatorControllerTPV;
                tpvUpperBodyAnimator.enabled = true;
                tpvUpperBodyAnimator.Rebind();
                tpvUpperBodyAnimator.Update(0f);
                
                Debug.Log($"ðŸŽ¬ [PlayerSetup] TPV upper body animator changed to: {weaponConfig.handsAnimatorControllerTPV.name}");
            }
            else if (tpvUpperBodyAnimator == null)
            {
                Debug.LogWarning("âš ï¸ [PlayerSetup] TPV upper body animator not found!");
            }
            else if (weaponConfig.handsAnimatorControllerTPV == null)
            {
                Debug.LogWarning($"âš ï¸ [PlayerSetup] WeaponConfig has no TPV hands animator controller: {weaponConfig.weaponName}");
            }
            
            // Connect TPV weapon grips to TPV IK constraints
            // This needs to be done after weapon is spawned
            // Stop any in-flight TPV IK coroutine (rapid switching guard)
            if (tpvIKCoroutine != null) StopCoroutine(tpvIKCoroutine);
            tpvIKCoroutine = ConnectTPVWeaponGripsToIKCoroutine();
            StartCoroutine(tpvIKCoroutine);
        }
        
        /// <summary>
        /// Connect TPV weapon grips to TPV IK constraints (coroutine version)
        /// Similar to ConnectWeaponGripsToIKCoroutine but for third-person view
        /// </summary>
        private System.Collections.IEnumerator ConnectTPVWeaponGripsToIKCoroutine()
        {
            if (tpvController == null || tpvRightHandIKConstraint == null || tpvLeftHandIKConstraint == null)
            {
                Debug.LogWarning("âš ï¸ [PlayerSetup] TPV IK constraints not assigned - skipping grip connection");
                yield break;
            }
            
            // Wait 1 frame (same reason as FPV: let the Animator finish with safe targets first)
            yield return null;

            Transform tpvWeaponHolder = tpvController.tpvWeaponHolder;
            if (tpvWeaponHolder == null || tpvWeaponHolder.childCount == 0)
            {
                Debug.LogWarning("âš ï¸ [PlayerSetup] No TPV weapon found in TPV weapon holder");
                yield break;
            }
            
            // Guard: holder must still exist
            if (tpvWeaponHolder == null || tpvWeaponHolder.childCount == 0) yield break;

            GameObject tpvWeaponInstance = tpvWeaponHolder.GetChild(0).gameObject;
            
            Transform rightGrip = FindTransformRecursive(tpvWeaponInstance.transform, "RightGrip");
            Transform leftGrip  = FindTransformRecursive(tpvWeaponInstance.transform, "LeftGrip");
            
            if (rightGrip == null || leftGrip == null)
            {
                Debug.LogWarning($"âš ï¸ [PlayerSetup] TPV weapon missing grips! RightGrip: {rightGrip != null}, LeftGrip: {leftGrip != null}");
                yield break;
            }
            
            tpvRightHandIKConstraint.data.target = rightGrip;
            tpvLeftHandIKConstraint.data.target  = leftGrip;

            // Restore IK weights (SafeDetachTPVIK sets them to 0)
            tpvRightHandIKConstraint.weight = 1f;
            tpvLeftHandIKConstraint.weight  = 1f;
            
            if (tpvRigBuilder != null)
            {
                tpvRigBuilder.enabled = false;
                tpvRigBuilder.enabled = true;
                tpvRigBuilder.Build();

                yield return null;
                Debug.Log("ðŸ”§ [PlayerSetup] TPV rig rebuild complete");
            }
        }
        
        /// <summary>
        /// Disable visual components when in lobby (lightweight mode)
        /// </summary>
        private void DisableVisualsForLobby()
        {
            // Disable all renderers (mesh, skinned mesh)
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.enabled = false;
            }
            
            // Disable camera if exists
            var camera = GetComponentInChildren<Camera>();
            if (camera != null)
            {
                camera.gameObject.SetActive(false);
            }
            
            // Disable audio listener if exists
            var audioListener = GetComponentInChildren<AudioListener>();
            if (audioListener != null)
            {
                audioListener.enabled = false;
            }
            
            // Disable PlayerController to prevent movement in lobby
            if (playerController != null)
            {
                playerController.enabled = false;
            }
        }
        
        /// <summary>
        /// Re-enable visual components when entering game scene
        /// </summary>
        private void EnableVisualsForGame()
        {
            // Re-enable all renderers
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.enabled = true;
            }
            
            // Re-enable camera for local player
            if (Object.HasInputAuthority)
            {
                var camera = GetComponentInChildren<Camera>(true);
                if (camera != null)
                {
                    camera.gameObject.SetActive(true);
                }
                
                var audioListener = GetComponentInChildren<AudioListener>(true);
                if (audioListener != null)
                {
                    audioListener.enabled = true;
                }
            }
            
            // Re-enable PlayerController
            if (playerController != null)
            {
                playerController.enabled = true;
            }
        }

        // â”€â”€â”€ Ability item API (called by AbilitySystem) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Replaces the current weapon with an ability FPV prefab (e.g. grenade).
        /// Follows the same IK / animator pattern as SpawnKnife.
        /// Call UnequipAbilityItem() when the ability animation finishes.
        /// </summary>
        public void EquipAbilityItem(GameObject abilityFPVPrefab, RuntimeAnimatorController abilityHandsAnimator)
        {
            if (abilityFPVPrefab == null) return;

            // Remember which weapon slot was active so we can restore it
            restoredPrimaryAfterAbility = isPrimaryEquipped;
            isAbilityEquipped = true;
            isKnifeEquipped = false; // Grenade replaces whatever is in hand

            // Mark slot=3 ("ability") so remote Render() detects the change back to 0 or 1
            if (HasInputAuthority) ActiveWeaponSlot = 3;

            // Save and cancel any active weapon / reload
            if (currentFireWeapon != null && !isKnifeEquipped)
            {
                currentFireWeapon.CancelReload();
                int ammo = currentFireWeapon.GetCurrentAmmo();
                if (isPrimaryEquipped) primaryAmmo = ammo; else secondaryAmmo = ammo;
            }

            // Safely destroy current FPV weapon
            if (currentWeaponInstance != null)
            {
                SafeDetachFPVIK();
                GameObject old = currentWeaponInstance;
                old.SetActive(false);
                old.transform.SetParent(null);
                StartCoroutine(DeferredDestroy(old));
                currentWeaponInstance = null;
                currentFireWeapon = null;
            }

            // Reset weaponHolder
            weaponHolder.localPosition = weaponHolderOriginalPosition;
            weaponHolder.localRotation = weaponHolderOriginalRotation;

            // Spawn ability FPV prefab in weaponHolder
            currentWeaponInstance = Instantiate(abilityFPVPrefab, weaponHolder);

            // Apply hands animator (Spine2)
            if (handsAnimator != null && abilityHandsAnimator != null)
            {
                handsAnimator.enabled = false;
                handsAnimator.runtimeAnimatorController = abilityHandsAnimator;
                handsAnimator.enabled = true;
                handsAnimator.Rebind();
                handsAnimator.Update(0f);
            }

            // Connect RightHandGrip / LeftHandGrip to FPV IK
            if (fpvIKCoroutine != null) StopCoroutine(fpvIKCoroutine);
            fpvIKCoroutine = ConnectWeaponGripsToIKCoroutine(currentWeaponInstance);
            StartCoroutine(fpvIKCoroutine);
        }

        /// <summary>
        /// Destroys the ability FPV instance and re-spawns the weapon that was
        /// active before EquipAbilityItem() was called.
        /// </summary>
        public void UnequipAbilityItem()
        {
            if (!isAbilityEquipped) return;
            isAbilityEquipped = false;

            // Destroy ability FPV (same safe-detach pattern as weapon switching)
            if (currentWeaponInstance != null)
            {
                SafeDetachFPVIK();
                GameObject old = currentWeaponInstance;
                old.SetActive(false);
                old.transform.SetParent(null);
                StartCoroutine(DeferredDestroy(old));
                currentWeaponInstance = null;
                currentFireWeapon = null;
            }

            // Restore previous weapon (SpawnWeapon also re-registers FireButton listeners)
            WeaponConfig toRestore = restoredPrimaryAfterAbility ? primaryWeaponConfig : secondaryWeaponConfig;
            if (toRestore != null)
                SpawnWeapon(toRestore, restoredPrimaryAfterAbility);
        }

        /// <summary>Returns the Camera component on this player (PlayerCamera child).</summary>
        public Camera GetPlayerCamera()
        {
            return GetComponentInChildren<Camera>(true);
        }

        // â”€â”€â”€ TPV Ability item API (called by AbilitySystem via RPC) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Swaps the TPV weapon for the grenade prefab and sets the Spine2 posture animator.
        /// Called on remote clients so other players can see the grenade being held.
        /// </summary>
        public void EquipTPVAbilityItem(GameObject grenadePrefabTPV, RuntimeAnimatorController postureAnimator)
        {
            if (tpvController == null || grenadePrefabTPV == null) return;
            _tpvAbilityActive = true;

            SafeDetachTPVIK();
            tpvController.SpawnTPVWeapon(grenadePrefabTPV);

            // Set Spine2 posture animator
            Animator upperBodyAnimator = tpvController.GetUpperBodyAnimator();
            if (upperBodyAnimator != null && postureAnimator != null)
            {
                upperBodyAnimator.enabled = false;
                upperBodyAnimator.runtimeAnimatorController = postureAnimator;
                upperBodyAnimator.enabled = true;
                upperBodyAnimator.Rebind();
                upperBodyAnimator.Update(0f);
            }

            // Connect LeftGrip / RightGrip to TPV IK constraints
            if (tpvIKCoroutine != null) StopCoroutine(tpvIKCoroutine);
            tpvIKCoroutine = ConnectTPVWeaponGripsToIKCoroutine();
            StartCoroutine(tpvIKCoroutine);
        }

        /// <summary>
        /// Triggers the "Shoot" animation on Spine2 and destroys the TPV grenade mesh
        /// (coincides with the moment the projectile appears).
        /// </summary>
        public void ThrowTPVAbilityItem()
        {
            if (!_tpvAbilityActive || tpvController == null) return;

            // Play throw animation on Spine2 (only if the posture animator has the parameter)
            Animator upperBodyAnimator = tpvController.GetUpperBodyAnimator();
            if (upperBodyAnimator != null && HasAnimatorParameter(upperBodyAnimator, "Shoot", AnimatorControllerParameterType.Trigger))
                upperBodyAnimator.SetTrigger("Shoot");

            // Detach IK first, then immediately destroy the grenade model
            SafeDetachTPVIK();
            tpvController.ClearCurrentTPVWeapon();
        }

        /// <summary>
        /// Restores the normal TPV weapon and posture animator after the ability ends.
        /// Called on remote clients when the throw animation completes.
        /// </summary>
        public void UnequipTPVAbilityItem(bool isPrimary)
        {
            if (!_tpvAbilityActive) return;
            _tpvAbilityActive = false;

            // Restore the weapon that was active BEFORE the grenade (primary or secondary)
            WeaponConfig toRestore = isPrimary ? primaryWeaponConfig : secondaryWeaponConfig;
            if (toRestore != null)
                SpawnTPVWeapon(toRestore);
        }

        /// <summary>Returns true when the animator has a parameter with the given name and type.</summary>
        private static bool HasAnimatorParameter(Animator animator, string paramName, AnimatorControllerParameterType type)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (var p in animator.parameters)
                if (p.name == paramName && p.type == type) return true;
            return false;
        }
    }
}
