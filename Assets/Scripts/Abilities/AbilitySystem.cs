using System.Collections;
using UnityEngine;
using Fusion;
using ArtisansGuns.Game;
using ArtisansGuns.Characters;
using ArtisansGuns.Networking;
using ArtisansGuns.Audio;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Orchestrates character abilities (Crimson smoke/pulse, Pato tsunami/super-jump, etc.).
    /// Add this component to the same player prefab that has PlayerSetup.
    ///
    /// Call Initialize(CharacterConfig) from CharacterSetupHandler after Spawned().
    /// Automatically disabled for remote players: only the local (InputAuthority) player
    /// runs ability logic.
    ///
    /// Canvas UI elements expected (found by name, searched scene-wide):
    ///   • "Ability1Button"  — the uGUI Button for Ability 1
    ///   • "Ability2Button"  — the uGUI Button for Ability 2
    ///   • "Ability1Dial"    — parent object that holds the Timer + icon Image
    ///   • "Ability2Dial"    — parent object that holds the Timer + icon Image
    ///
    /// Each Dial object is expected to contain:
    ///   • Timer  component          (cooldown countdown)
    ///   • Image child named "AbilityIcon"         (displays ability icon)
    /// </summary>
    public class AbilitySystem : NetworkBehaviour
    {
        // ------------------------------------------------------------------
        // Private runtime refs
        // ------------------------------------------------------------------
        private PlayerSetup playerSetup;

        private CharacterConfig character;
        private SmokeGrenadeAbilityConfig smokeConfig;
        private VisionPulseAbilityConfig pulseConfig;

        // ── Pato ability configs ──
        private TsunamiWaveAbilityConfig tsunamiConfig;
        private WaterSuperJumpAbilityConfig superJumpConfig;
        private GameObject _tsunamiPrefab; // cached for remote RPC

        // Determines which ability set is active
        private enum AbilitySet { None, Crimson, Pato }
        private AbilitySet _activeSet = AbilitySet.None;

        // Cooldown flags (true while on cooldown)
        private bool ability1OnCooldown;
        private bool ability2OnCooldown;
        private Coroutine _cooldown1Coroutine;
        private Coroutine _cooldown2Coroutine;

        // Reference to the live grenade FPV (obtained after EquipAbilityItem)
        private SmokeGrenadeAbility currentGrenadeAbility;

        // ── Ultimate ability ──
        private CrimsonUltimateAbilityConfig _ultimateConfig;
        private CrimsonUltimateAbility _currentUltimateAbility;
        private PatoUltimateAbilityConfig _patoUltConfig;
        private GameObject _patoUltWavePrefab; // cached for RPC
        private bool _ultimateCharged;   // true when 5 kills reached and waiting to be used
        private bool _ultimateUsed;      // true after throwing — prevents re-use until reset
        private GameObject _ultProjectilePrefab;
        private GameObject _ultEffectPrefab;
        private float _ultThrowSpeed;
        private float _ultDamage;
        private float _ultEffectDuration;
        private float _ultDetonationDelay;
        private GameObject _ultPrefabTPV;
        private RuntimeAnimatorController _ultPostureAnimTPV;

        // Cached data for RPC-based spawning
        private GameObject _smokePrefab;
        private float      _smokeDuration;
        private GameObject _projectilePrefab;
        private float      _throwSpeed;
        private GameObject _grenadePrefabTPV;
        private RuntimeAnimatorController _postureAnimatorTPV;

        // Cached sound clips (loaded once in Spawned)
        private static AudioClip _smokeLandClip;
        private static AudioClip _visionPulseClip;

        // Original FireButton EventTrigger entries (saved so we can restore them)
        private bool fireButtonHijacked;

        // ------------------------------------------------------------------
        // Fusion lifecycle
        // ------------------------------------------------------------------

        public override void Spawned()
        {
            // Disable for remote players — they do not run ability logic locally
            if (!Object.HasInputAuthority)
            {
                enabled = false;
                return;
            }

            playerSetup = GetComponent<PlayerSetup>();

            // Pre-load shared sound clips once
            if (_smokeLandClip   == null) _smokeLandClip   = Resources.Load<AudioClip>("Sounds/SmokeLand");
            if (_visionPulseClip == null) _visionPulseClip = Resources.Load<AudioClip>("Sounds/VisionPulse");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ArtisansGuns.UI.MobileControlsController.OnAbility1 -= OnAbility1Pressed;
            ArtisansGuns.UI.MobileControlsController.OnAbility2 -= OnAbility2Pressed;

            // Unsubscribe from combo events
            ComboKillManager.OnUltimateReady -= OnUltimateCharged;
            ComboKillManager.OnUltimateReset -= OnUltimateReset;
            ArtisansGuns.UI.MobileControlsController.OnUltimate -= OnUltimatePressed;
        }

        // ------------------------------------------------------------------
        // Public API — called by CharacterSetupHandler
        // ------------------------------------------------------------------

        /// <summary>
        /// Initialises the ability HUD and readies both abilities.
        /// Must be called AFTER Spawned() by CharacterSetupHandler (local player only).
        /// </summary>
        public void Initialize(CharacterConfig config)
        {
            if (!Object.HasInputAuthority) return;

            character   = config;

            // ── Detect ability set from config types ────────────────────
            smokeConfig    = config?.ability1 as SmokeGrenadeAbilityConfig;
            pulseConfig    = config?.ability2 as VisionPulseAbilityConfig;
            tsunamiConfig  = config?.ability1 as TsunamiWaveAbilityConfig;
            superJumpConfig = config?.ability2 as WaterSuperJumpAbilityConfig;

            if (tsunamiConfig != null && superJumpConfig != null)
                _activeSet = AbilitySet.Pato;
            else if (smokeConfig != null && pulseConfig != null)
                _activeSet = AbilitySet.Crimson;
            else
                _activeSet = AbilitySet.None;

            // Cache tsunami prefab for local client
            if (tsunamiConfig != null)
                _tsunamiPrefab = tsunamiConfig.wavePrefab;

            // -- Subscribe to UIToolkit mobile controls ---------------------------
            ArtisansGuns.UI.MobileControlsController.OnAbility1 += OnAbility1Pressed;
            ArtisansGuns.UI.MobileControlsController.OnAbility2 += OnAbility2Pressed;

            // -- Set ability icons in HUD -----------------------------------------
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl != null)
            {
                AbilityConfig a1 = (AbilityConfig)tsunamiConfig ?? smokeConfig;
                AbilityConfig a2 = (AbilityConfig)superJumpConfig ?? pulseConfig;

                if (a1?.icon != null) ctrl.SetAbility1Icon(a1.icon);
                if (a2?.icon != null) ctrl.SetAbility2Icon(a2.icon);
                ctrl.SetAbility1Progress(1f, DIAL_READY_COLOR);
                ctrl.SetAbility2Progress(1f, DIAL_READY_COLOR);
                ctrl.SetAbility1Interactable(true);
                ctrl.SetAbility2Interactable(true);
            }

            // ── Ultimate ability config ─────────────────────────────────
            _ultimateConfig = config?.ultimate as CrimsonUltimateAbilityConfig;
            if (_ultimateConfig != null)
            {
                _ultProjectilePrefab = _ultimateConfig.ultimateProjectilePrefab;
                _ultEffectPrefab     = _ultimateConfig.ultimateEffectPrefab;
                _ultThrowSpeed       = _ultimateConfig.throwSpeed;
                _ultDamage           = _ultimateConfig.damage;
                _ultEffectDuration   = _ultimateConfig.effectDuration;
                _ultDetonationDelay  = _ultimateConfig.detonationDelay;
                _ultPrefabTPV        = _ultimateConfig.ultimatePrefabTPV;
                _ultPostureAnimTPV   = _ultimateConfig.postureAnimatorControllerTPV;
            }

            // ── Pato Ultimate ability config ─────────────────────────────
            _patoUltConfig = config?.ultimate as PatoUltimateAbilityConfig;
            if (_patoUltConfig != null)
            {
                _patoUltWavePrefab = _patoUltConfig.wavePrefab;

                // Prewarm: instantiate + destroy so VFX shaders are compiled
                // and first real spawn doesn't lag.
                if (_patoUltWavePrefab != null)
                {
                    var warmup = Instantiate(_patoUltWavePrefab);
                    warmup.transform.position = Vector3.down * 500f; // off-screen
                    Destroy(warmup, 0.1f);
                }
            }

            // Subscribe to combo kill events for ultimate charging
            ComboKillManager.OnUltimateReady += OnUltimateCharged;
            ComboKillManager.OnUltimateReset += OnUltimateReset;
            ArtisansGuns.UI.MobileControlsController.OnUltimate += OnUltimatePressed;

            // Set ultimate UI (icon + 0/5 dots)
            AbilityConfig ultimateIconSource = (AbilityConfig)_ultimateConfig ?? _patoUltConfig;
            if (ctrl != null && ultimateIconSource != null)
            {
                ctrl.SetUltimateIcon(ultimateIconSource.icon);
                ctrl.SetUltimateDots(0);
                ctrl.SetUltimateInteractable(false);
            }

            // Subscribe to per-kill dot updates
            ComboKillManager.OnComboKillRegistered += OnComboKillForUI;
        }

        private void OnComboKillForUI(int comboIndex)
        {
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl == null) return;
            ctrl.SetUltimateDots(comboIndex);
        }

        // ------------------------------------------------------------------
        // Button handlers
        // ------------------------------------------------------------------

        private void OnAbility1Pressed()
        {
            if (ability1OnCooldown) return;

            switch (_activeSet)
            {
                case AbilitySet.Crimson:
                    // Grenade already in hand — don't restart the equip animation
                    if (currentGrenadeAbility != null) return;
                    if (smokeConfig == null)
                    {
                        Debug.LogWarning("[AbilitySystem] Ability 1 config is not a SmokeGrenadeAbilityConfig");
                        return;
                    }
                    ActivateSmokeGrenade(smokeConfig);
                    break;

                case AbilitySet.Pato:
                    ActivateTsunamiWave();
                    break;

                default:
                    Debug.LogWarning("[AbilitySystem] No ability set configured for Ability 1");
                    break;
            }
        }

        private void OnAbility2Pressed()
        {
            if (ability2OnCooldown) return;

            switch (_activeSet)
            {
                case AbilitySet.Crimson:
                    if (pulseConfig == null)
                    {
                        Debug.LogWarning("[AbilitySystem] Ability 2 config is not a VisionPulseAbilityConfig");
                        return;
                    }

                    var smoke = CrimsonSmoke.ActiveSmoke;
                    if (smoke == null)
                    {
                        Debug.Log("[AbilitySystem] Vision Pulse: no active smoke in scene");
                        return;
                    }

                    smoke.TriggerVisionPulse(pulseConfig);
                    StartCooldown(2, pulseConfig.cooldownSeconds);

                    // Play vision pulse sound: local player hears it + RPC so enemies hear it too
                    PlayLocal2DSound(_visionPulseClip, 1f);
                    RPC_PlayVisionPulseSound();
                    break;

                case AbilitySet.Pato:
                    ActivateWaterSuperJump();
                    break;

                default:
                    Debug.LogWarning("[AbilitySystem] No ability set configured for Ability 2");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Smoke Grenade flow
        // ------------------------------------------------------------------

        private void ActivateSmokeGrenade(SmokeGrenadeAbilityConfig cfg)
        {
            if (playerSetup == null) return;

            // Tell PlayerSetup to swap the weapon for the grenade FPV
            playerSetup.EquipAbilityItem(cfg.grenadeFPVPrefab, cfg.grenadesHandsAnimatorController);

            // Locate the SmokeGrenadeAbility script that was just instantiated
            // (it lives on the same object that was parented to WeaponHolder)
            currentGrenadeAbility = playerSetup.weaponHolder
                .GetComponentInChildren<SmokeGrenadeAbility>();

            if (currentGrenadeAbility == null)
            {
                Debug.LogError("[AbilitySystem] SmokeGrenadeAbility not found on grenade FPV prefab!");
                playerSetup.UnequipAbilityItem();
                return;
            }

            // Cache data for RPCs
            _smokePrefab          = cfg.smokePrefab;
            _smokeDuration        = cfg.smokeDuration;
            _projectilePrefab     = cfg.grenadeProjectilePrefab;
            _throwSpeed           = cfg.throwSpeed;
            _grenadePrefabTPV     = cfg.grenadePrefabTPV;
            _postureAnimatorTPV   = cfg.postureAnimatorControllerTPV;

            // Pass the ability spawner so the grenade knows where to spawn the projectile
            currentGrenadeAbility.abilitySpawner      = playerSetup.abilitySpawner;
            currentGrenadeAbility.projectilePrefab    = cfg.grenadeProjectilePrefab;
            currentGrenadeAbility.throwSpeed           = cfg.throwSpeed;
            currentGrenadeAbility.onProjectileThrown   = OnLocalProjectileThrown;

            // Subscribe to throw-complete so we can restore the weapon and start cooldown
            currentGrenadeAbility.OnThrowCompleted += OnSmokeGrenadeThrowComplete;

            // Hijack the FireButton FIRST — before any RPC that could potentially fault
            HijackFireButton();

            // Notify all clients to equip the TPV grenade on this player's character
            RPC_EquipTPVGrenade();
        }

        private void OnSmokeGrenadeThrowComplete()
        {
            if (currentGrenadeAbility != null)
                currentGrenadeAbility.OnThrowCompleted -= OnSmokeGrenadeThrowComplete;

            currentGrenadeAbility = null;

            // Restore normal fire button BEFORE unequipping so it still gets
            // re-registered by SpawnWeapon → FireWeapon.Initialize()
            RestoreFireButton();

            // Re-spawn the weapon that was active before the grenade
            playerSetup.UnequipAbilityItem();

            // Notify all clients to restore the correct TPV weapon
            RPC_UnequipTPVGrenade(playerSetup.WasUsingPrimaryBeforeAbility);

            // Now start cooldown
            if (smokeConfig != null)
                StartCooldown(1, smokeConfig.cooldownSeconds);
        }

        // ------------------------------------------------------------------
        // FireButton hijacking
        // ------------------------------------------------------------------

        /// <summary>
        /// Redirects the fire button to throw the grenade instead of shooting.
        /// </summary>
        private void HijackFireButton()
        {
            ArtisansGuns.UI.MobileControlsController.Instance?.SetFireOverride(
                () => currentGrenadeAbility?.ThrowGrenade(),
                null
            );
            fireButtonHijacked = true;
        }

        // ------------------------------------------------------------------
        // Networked Smoke Spawning
        // ------------------------------------------------------------------

        /// <summary>
        /// Called when the animation event fires. Sends an RPC so every client
        /// spawns and physically simulates the grenade projectile.
        /// </summary>
        private void OnLocalProjectileThrown(Vector3 spawnPos, Vector3 direction, float speed)
        {
            RPC_SpawnProjectile(spawnPos, direction, speed);
        }

        /// <summary>
        /// Called locally when the grenade projectile detonates.
        /// Sends an RPC so every client spawns the smoke cloud.
        /// </summary>
        private void OnLocalSmokeDetonate(Vector3 position)
        {
            RPC_SpawnSmoke(position, _smokeDuration);
        }

        /// <summary>
        /// Executed on ALL clients. Each client instantiates and physically simulates
        /// the projectile from the same initial state, so trajectories match.
        /// Only the InputAuthority client wires the detonation callback to trigger smoke.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_SpawnProjectile(Vector3 spawnPos, Vector3 direction, float speed)
        {
            GameObject prefab = _projectilePrefab;

            // Remote clients haven't cached the prefab — load from CharacterConfig.
            if (prefab == null)
                prefab = LoadProjectilePrefabFromConfig();

            if (prefab == null)
            {
                Debug.LogWarning("[AbilitySystem] RPC_SpawnProjectile: could not resolve projectile prefab");
                return;
            }

            Quaternion rotation  = direction != Vector3.zero
                ? Quaternion.LookRotation(direction)
                : Quaternion.identity;

            GameObject go   = Instantiate(prefab, spawnPos, rotation);
            var        proj = go.GetComponent<GrenadeProjectile>();
            if (proj != null)
            {
                // Only the InputAuthority triggers the smoke RPC on detonation.
                // Remote clients run the physics visually and self-destroy on impact.
                System.Action<Vector3> detonateCallback =
                    Object.HasInputAuthority ? (System.Action<Vector3>)OnLocalSmokeDetonate : null;

                proj.Launch(direction, speed, detonateCallback, gameObject);

                // On remote clients: trigger the throw animation on Spine2 and hide the TPV grenade mesh
                if (!Object.HasInputAuthority)
                {
                    var setup = GetComponent<PlayerSetup>();
                    setup?.ThrowTPVAbilityItem();
                }
            }
        }

        private GameObject LoadProjectilePrefabFromConfig()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return null;

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return null;

            string lower = agentId.ToLower();
            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }
            if (cfg == null) return null;

            var smokeCfg = cfg.ability1 as SmokeGrenadeAbilityConfig;
            return smokeCfg?.grenadeProjectilePrefab;
        }

        /// <summary>
        /// Executed on ALL clients. Spawns the CrimsonSmoke prefab at the given position.
        /// The local (InputAuthority) client uses its cached prefab; remote clients load
        /// the CharacterConfig from Resources to obtain the smoke prefab.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_SpawnSmoke(Vector3 position, float duration)
        {
            GameObject prefab = _smokePrefab;

            // Remote clients haven't cached the prefab — load from CharacterConfig.
            if (prefab == null)
            {
                prefab = LoadSmokePrefabFromConfig();
            }

            if (prefab != null)
            {
                GameObject smokeGO = Instantiate(prefab, position, Quaternion.identity);
                CrimsonSmoke smoke = smokeGO.GetComponent<CrimsonSmoke>();
                if (smoke != null)
                    smoke.Initialize(duration);

                // Play smoke-land sound spatially (full 3D, same config as footsteps)
                PlaySpatialSoundAtPoint(_smokeLandClip, position, 1f, 25f, 1f);
            }
            else
            {
                Debug.LogWarning("[AbilitySystem] RPC_SpawnSmoke: could not resolve smoke prefab");
            }
        }

        /// <summary>
        /// Fallback: loads CharacterConfig from Resources using the networked SelectedAgent,
        /// then returns the smokePrefab from the SmokeGrenadeAbilityConfig (ability1).
        /// </summary>
        private GameObject LoadSmokePrefabFromConfig()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return null;

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return null;

            string lower = agentId.ToLower();
            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }
            if (cfg == null) return null;

            var smokeCfg = cfg.ability1 as SmokeGrenadeAbilityConfig;
            return smokeCfg?.smokePrefab;
        }

        // ------------------------------------------------------------------
        // TPV Grenade RPCs
        // ------------------------------------------------------------------

        /// <summary>
        /// Sent by the throwing player to all clients so they show the TPV grenade
        /// in the correct hand and set the posture animator on Spine2.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_EquipTPVGrenade()
        {
            if (Object.HasInputAuthority) return;   // local player handles it via EquipAbilityItem
            var setup = GetComponent<PlayerSetup>();
            if (setup == null) return;
            var (prefab, animator) = LoadTPVGrenadeDataFromConfig();
            if (prefab != null)
                setup.EquipTPVAbilityItem(prefab, animator);
        }

        /// <summary>
        /// Sent by the throwing player to all clients so they restore the normal TPV weapon.
        /// isPrimary: which slot was active before the grenade was equipped.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_UnequipTPVGrenade(bool isPrimary)
        {
            if (Object.HasInputAuthority) return;   // local player handles it via UnequipAbilityItem
            var setup = GetComponent<PlayerSetup>();
            setup?.UnequipTPVAbilityItem(isPrimary);
        }

        /// <summary>
        /// Called by PlayerSetup when the player manually switches weapons while the grenade
        /// ability is still active (i.e. without throwing). Cleans up local state and notifies
        /// remote clients to restore their TPV weapon.
        /// </summary>
        public void CancelActiveGrenade(bool restoringPrimary)
        {
            if (currentGrenadeAbility != null)
            {
                currentGrenadeAbility.OnThrowCompleted -= OnSmokeGrenadeThrowComplete;
                currentGrenadeAbility = null;
            }
            RestoreFireButton();
            RPC_UnequipTPVGrenade(restoringPrimary);
        }

        private (GameObject prefab, RuntimeAnimatorController postureAnimator) LoadTPVGrenadeDataFromConfig()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return (null, null);

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return (null, null);

            string lower = agentId.ToLower();
            var cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }
            if (cfg == null) return (null, null);

            var smokeCfg = cfg.ability1 as SmokeGrenadeAbilityConfig;
            return (smokeCfg?.grenadePrefabTPV, smokeCfg?.postureAnimatorControllerTPV);
        }

        // ------------------------------------------------------------------
        // Pato — Tsunami Wave (Ability 1)
        // ------------------------------------------------------------------

        /// <summary>
        /// Casts the Tsunami Wave. Must be grounded.
        /// Spawns the wave via RPC so all clients see it.
        /// Only the caster (InputAuthority) rides on top.
        /// </summary>
        private void ActivateTsunamiWave()
        {
            if (tsunamiConfig == null) return;

            // Must be grounded to cast
            var pc = GetComponent<ArtisansGuns.Game.PlayerController>();
            if (pc == null || !pc.IsGrounded)
            {
                Debug.Log("[AbilitySystem] Tsunami Wave: must be grounded to cast");
                return;
            }

            // Raycast down from player to find the actual surface Y.
            // The wave spawns at surface + 0.1 so it never clips under the floor.
            Vector3 spawnPos = transform.position;
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit groundHit, 10f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                spawnPos.y = groundHit.point.y + 0.1f;
            }
            else
            {
                // Fallback: CharacterController bottom
                var cc = GetComponent<CharacterController>();
                if (cc != null)
                    spawnPos.y = transform.position.y - cc.height * 0.5f + 0.1f;
            }

            // Direction = camera forward projected onto XZ plane (horizontal only)
            Transform cam = Camera.main?.transform;
            Vector3 dir = cam != null ? cam.forward : transform.forward;
            dir.y = 0f;
            dir.Normalize();

            // RPC → all clients spawn the wave visually
            RPC_SpawnTsunamiWave(spawnPos, dir);

            // Start cooldown
            StartCooldown(1, tsunamiConfig.cooldownSeconds);

            // Play spawn sound locally
            if (tsunamiConfig.spawnSound != null)
                PlayLocal2DSound(tsunamiConfig.spawnSound, 1f);
        }

        /// <summary>
        /// Spawns the Tsunami Wave on ALL clients via plain Instantiate.
        /// Only the InputAuthority client attaches rider logic.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_SpawnTsunamiWave(Vector3 spawnPos, Vector3 direction)
        {
            GameObject prefab = _tsunamiPrefab;

            // Remote clients load prefab from CharacterConfig
            if (prefab == null)
                prefab = LoadTsunamiPrefabFromConfig();

            if (prefab == null)
            {
                Debug.LogWarning("[AbilitySystem] RPC_SpawnTsunamiWave: could not resolve wave prefab");
                return;
            }

            // Face the wave in the movement direction
            Quaternion rotation = direction != Vector3.zero
                ? Quaternion.LookRotation(direction)
                : Quaternion.identity;

            GameObject waveGO = Instantiate(prefab, spawnPos, rotation);
            TsunamiWave wave  = waveGO.GetComponent<TsunamiWave>();

            if (wave == null)
            {
                wave = waveGO.AddComponent<TsunamiWave>();
            }

            // Configure from the ability config (use defaults if config not available on remote)
            TsunamiWaveAbilityConfig cfg = tsunamiConfig;
            if (cfg == null)
                cfg = LoadTsunamiConfigFromCharacter();

            wave.moveDirection     = direction.normalized;
            wave.waveSpeed         = cfg?.waveSpeed         ?? 14f;
            wave.waveDuration      = cfg?.waveDuration      ?? 3f;
            wave.riseFromBelow     = cfg?.riseFromBelow     ?? 3f;
            wave.riseSpeed         = cfg?.riseSpeed         ?? 12f;
            wave.riderHeightOffset = cfg?.riderHeightOffset ?? 1f;

            // Only the casting player (InputAuthority) rides the wave
            if (Object.HasInputAuthority)
            {
                var cc = GetComponent<CharacterController>();
                var pc = GetComponent<ArtisansGuns.Game.PlayerController>();
                wave.riderController       = cc;
                wave.riderPlayerController = pc;
            }

            wave.Launch(spawnPos);

            // Play spawn sound spatially for remote clients
            if (!Object.HasInputAuthority && cfg?.spawnSound != null)
            {
                PlaySpatialSoundAtPoint(cfg.spawnSound, spawnPos, 1f, 30f, 0.8f);
            }
        }

        private GameObject LoadTsunamiPrefabFromConfig()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return null;

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return null;

            string lower = agentId.ToLower();
            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }
            if (cfg == null) return null;

            var tCfg = cfg.ability1 as TsunamiWaveAbilityConfig;
            return tCfg?.wavePrefab;
        }

        private TsunamiWaveAbilityConfig LoadTsunamiConfigFromCharacter()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return null;

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return null;

            string lower = agentId.ToLower();
            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }

            return cfg?.ability1 as TsunamiWaveAbilityConfig;
        }

        // ------------------------------------------------------------------
        // Pato — Water Super Jump (Ability 2)
        // ------------------------------------------------------------------

        /// <summary>
        /// Activates the Water Super Jump.
        /// Can only fire while the player is riding a Tsunami Wave.
        /// </summary>
        private void ActivateWaterSuperJump()
        {
            if (superJumpConfig == null) return;

            // Must be standing on a wave (riding)
            var activeWave = TsunamiWave.ActiveRiderWave;
            if (activeWave == null || !activeWave.IsRiding)
            {
                Debug.Log("[AbilitySystem] Water Super Jump: must be riding a Tsunami Wave");
                return;
            }

            // Dismount the wave
            activeWave.DismountRider();

            // Apply super jump velocity to the player
            var pc = GetComponent<ArtisansGuns.Game.PlayerController>();
            if (pc != null)
            {
                pc.SetVelocityY(superJumpConfig.jumpForce);
            }

            // Start cooldown
            StartCooldown(2, superJumpConfig.cooldownSeconds);

            // Play sound
            if (superJumpConfig.jumpSound != null)
                PlayLocal2DSound(superJumpConfig.jumpSound, 1f);
        }

        // ------------------------------------------------------------------
        // Ultimate — Crimson BAM (charged by 5 kills)
        // ------------------------------------------------------------------

        private void OnUltimateCharged()
        {
            _ultimateCharged = true;
            _ultimateUsed    = false;
            Debug.Log("[AbilitySystem] Ultimate CHARGED — ready to fire!");

            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            ctrl?.SetUltimateDotsActive(true);   // all 5 dots turn green
            ctrl?.SetUltimateInteractable(true);
        }

        private void OnUltimateReset()
        {
            _ultimateCharged = false;
            _ultimateUsed    = false;
            _currentUltimateAbility = null;

            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            ctrl?.SetUltimateDotsActive(false);  // dots back to dim grey
            ctrl?.SetUltimateDots(0);
            ctrl?.SetUltimateInteractable(false);

            Debug.Log("[AbilitySystem] Ultimate RESET (death).");
        }

        private void OnUltimatePressed()
        {
            if (!_ultimateCharged || _ultimateUsed) return;

            // Pato ultimate: spawn flash wave directly (no FPV equip)
            if (_patoUltConfig != null)
            {
                ActivatePatoUltimate();
                return;
            }

            // Crimson ultimate: FPV equip + throw
            if (_ultimateConfig == null) return;
            if (_currentUltimateAbility != null) return; // already equipping
            ActivateCrimsonUltimate();
        }

        private void ActivateCrimsonUltimate()
        {
            if (playerSetup == null || _ultimateConfig == null) return;

            // Equip the ultimate FPV item (same pattern as smoke grenade)
            playerSetup.EquipAbilityItem(
                _ultimateConfig.ultimateFPVPrefab,
                _ultimateConfig.crimsonUltimateHandsAnimator);

            _currentUltimateAbility = playerSetup.weaponHolder
                .GetComponentInChildren<CrimsonUltimateAbility>();

            if (_currentUltimateAbility == null)
            {
                Debug.LogError("[AbilitySystem] CrimsonUltimateAbility not found on ultimate FPV prefab!");
                playerSetup.UnequipAbilityItem();
                return;
            }

            _currentUltimateAbility.abilitySpawner    = playerSetup.abilitySpawner;
            _currentUltimateAbility.projectilePrefab  = _ultimateConfig.ultimateProjectilePrefab;
            _currentUltimateAbility.throwSpeed         = _ultimateConfig.throwSpeed;
            _currentUltimateAbility.onProjectileThrown = OnLocalUltimateProjectileThrown;
            _currentUltimateAbility.OnThrowCompleted  += OnUltimateThrowComplete;

            HijackFireButton();
            ArtisansGuns.UI.MobileControlsController.Instance?.SetFireOverride(
                () => _currentUltimateAbility?.ThrowUltimate(),
                null
            );
            fireButtonHijacked = true;

            // Notify TPV on all clients
            RPC_EquipTPVUltimate();
        }

        private void OnUltimateThrowComplete()
        {
            if (_currentUltimateAbility != null)
                _currentUltimateAbility.OnThrowCompleted -= OnUltimateThrowComplete;

            _currentUltimateAbility = null;
            _ultimateUsed = true;
            _ultimateCharged = false;

            RestoreFireButton();
            playerSetup.UnequipAbilityItem();

            // Restore TPV weapon for all clients
            RPC_UnequipTPVUltimate(playerSetup.WasUsingPrimaryBeforeAbility);

            // Instantly reset combo so kills start accumulating again (NO cooldown)
            var combo = ComboKillManager.Instance;
            if (combo != null) combo.ResetComboAfterThrow();

            // Reset UI: dots back to 0/dim, button locked
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            ctrl?.SetUltimateDotsActive(false);
            ctrl?.SetUltimateDots(0);
            ctrl?.SetUltimateInteractable(false);
        }

        // ------------------------------------------------------------------
        // Pato Ultimate — Flash Wave
        // ------------------------------------------------------------------

        /// <summary>
        /// Activates Pato's ultimate: spawns a large flash wave that travels
        /// in the XZ plane at Y=0, blinding enemies it contacts.
        /// No FPV equip needed — fires directly.
        /// </summary>
        private void ActivatePatoUltimate()
        {
            if (_patoUltConfig == null || playerSetup == null) return;

            // Direction = camera forward projected onto XZ
            Transform cam = Camera.main?.transform;
            Vector3 dir = cam != null ? cam.forward : transform.forward;
            dir.y = 0f;
            dir.Normalize();

            // Spawn at Y=0, player's XZ position
            Vector3 spawnPos = new Vector3(transform.position.x, 0f, transform.position.z);

            // RPC → all clients spawn the wave
            RPC_SpawnPatoUltimateWave(spawnPos, dir);

            // Mark as used immediately
            _ultimateUsed = true;
            _ultimateCharged = false;

            // Play spawn sound locally
            if (_patoUltConfig.spawnSound != null)
                PlayLocal2DSound(_patoUltConfig.spawnSound, 1f);

            // Reset combo so kills start accumulating again
            var combo = ComboKillManager.Instance;
            if (combo != null) combo.ResetComboAfterThrow();

            // Reset UI: dots → 0, button locked
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            ctrl?.SetUltimateDotsActive(false);
            ctrl?.SetUltimateDots(0);
            ctrl?.SetUltimateInteractable(false);
        }

        /// <summary>
        /// Spawns the Pato Ultimate flash wave on ALL clients.
        /// Each client instantiates the wave locally. The flash effect
        /// (fog + underwater audio) is applied only on the victim's local client.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_SpawnPatoUltimateWave(Vector3 spawnPos, Vector3 direction)
        {
            GameObject prefab = _patoUltWavePrefab;

            // Remote clients load prefab from CharacterConfig
            if (prefab == null)
                prefab = LoadPatoUltimatePrefabFromConfig();

            if (prefab == null)
            {
                Debug.LogWarning("[AbilitySystem] RPC_SpawnPatoUltimateWave: could not resolve wave prefab");
                return;
            }

            // Resolve config for wave parameters
            PatoUltimateAbilityConfig cfg = _patoUltConfig;
            if (cfg == null)
                cfg = LoadPatoUltimateConfigFromCharacter();

            Quaternion rotation = direction != Vector3.zero
                ? Quaternion.LookRotation(direction)
                : Quaternion.identity;

            GameObject waveGO = Instantiate(prefab, spawnPos, rotation);
            PatoUltimateWave wave = waveGO.GetComponent<PatoUltimateWave>();

            if (wave == null)
                wave = waveGO.AddComponent<PatoUltimateWave>();

            wave.moveDirection = direction.normalized;
            wave.waveSpeed     = cfg?.waveSpeed     ?? 16f;
            wave.waveDuration  = cfg?.waveDuration  ?? 5f;
            wave.flashDuration = cfg?.flashDuration  ?? 4f;
            wave.casterRef     = Object.InputAuthority;

            wave.Launch(spawnPos);

            // Play spawn sound spatially for remote clients
            if (!Object.HasInputAuthority && cfg?.spawnSound != null)
            {
                PlaySpatialSoundAtPoint(cfg.spawnSound, spawnPos, 1f, 40f, 0.9f);
            }
        }

        private GameObject LoadPatoUltimatePrefabFromConfig()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return null;

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return null;

            string lower = agentId.ToLower();
            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }
            if (cfg == null) return null;

            var ultCfg = cfg.ultimate as PatoUltimateAbilityConfig;
            return ultCfg?.wavePrefab;
        }

        private PatoUltimateAbilityConfig LoadPatoUltimateConfigFromCharacter()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return null;

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return null;

            string lower = agentId.ToLower();
            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }

            return cfg?.ultimate as PatoUltimateAbilityConfig;
        }

        // ── Ultimate projectile + effect RPCs ────────────────────────────

        private void OnLocalUltimateProjectileThrown(Vector3 spawnPos, Vector3 direction, float speed)
        {
            RPC_SpawnUltimateProjectile(spawnPos, direction, speed);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_SpawnUltimateProjectile(Vector3 spawnPos, Vector3 direction, float speed)
        {
            GameObject prefab = _ultProjectilePrefab;

            if (prefab == null)
                prefab = LoadUltimatePrefabFromConfig("projectile");

            if (prefab == null)
            {
                Debug.LogWarning("[AbilitySystem] RPC_SpawnUltimateProjectile: could not resolve prefab");
                return;
            }

            // Resolve effect prefab NOW so the projectile can spawn it on detonation
            GameObject effectPrefab = _ultEffectPrefab;
            if (effectPrefab == null)
                effectPrefab = LoadUltimatePrefabFromConfig("effect");

            bool isAuthority = Object.HasInputAuthority;
            float damage     = isAuthority ? (_ultDamage > 0 ? _ultDamage : 80f) : 0f;
            float duration   = _ultEffectDuration > 0 ? _ultEffectDuration : 3f;
            Fusion.PlayerRef shooterRef = isAuthority ? Object.InputAuthority : default;

            // Capture for closure
            GameObject capturedEffPrefab = effectPrefab;

            System.Action<Vector3> detonateCallback = (effectPos) =>
            {
                if (capturedEffPrefab == null)
                {
                    Debug.LogWarning("[AbilitySystem] Detonate: no effect prefab available");
                    return;
                }
                GameObject effectGO = Instantiate(capturedEffPrefab, effectPos, Quaternion.identity);
                var eff = effectGO.GetComponent<CrimsonUltimateEffect>();
                if (eff == null) eff = effectGO.AddComponent<CrimsonUltimateEffect>();
                eff.Initialize(damage, duration, shooterRef);
            };

            Quaternion rotation = direction != Vector3.zero
                ? Quaternion.LookRotation(direction)
                : Quaternion.identity;

            GameObject go   = Instantiate(prefab, spawnPos, rotation);
            var        proj = go.GetComponent<CrimsonUltimateProjectile>();
            if (proj != null)
            {
                float delay = _ultDetonationDelay > 0 ? _ultDetonationDelay : 1.5f;
                proj.Launch(direction, speed, detonateCallback, delay, gameObject);

                if (!isAuthority)
                {
                    var setup = GetComponent<PlayerSetup>();
                    setup?.ThrowTPVAbilityItem();
                }
            }
        }

        // ── Ultimate TPV RPCs ────────────────────────────────────────────

        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_EquipTPVUltimate()
        {
            if (Object.HasInputAuthority) return;
            var setup = GetComponent<PlayerSetup>();
            if (setup == null) return;
            var (prefab, animator) = LoadTPVUltimateDataFromConfig();
            if (prefab != null)
                setup.EquipTPVAbilityItem(prefab, animator);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_UnequipTPVUltimate(bool isPrimary)
        {
            if (Object.HasInputAuthority) return;
            var setup = GetComponent<PlayerSetup>();
            setup?.UnequipTPVAbilityItem(isPrimary);
        }

        // ── Ultimate config loaders (for remote clients) ─────────────────

        private GameObject LoadUltimatePrefabFromConfig(string type)
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return null;

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return null;

            string lower = agentId.ToLower();
            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }
            if (cfg == null) return null;

            var ultCfg = cfg.ultimate as CrimsonUltimateAbilityConfig;
            if (ultCfg == null) return null;

            return type == "projectile" ? ultCfg.ultimateProjectilePrefab : ultCfg.ultimateEffectPrefab;
        }

        private (GameObject prefab, RuntimeAnimatorController animator) LoadTPVUltimateDataFromConfig()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return (null, null);

            string agentId = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentId)) return (null, null);

            string lower = agentId.ToLower();
            var cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
            if (cfg == null)
            {
                string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }
            if (cfg == null) return (null, null);

            var ultCfg = cfg.ultimate as CrimsonUltimateAbilityConfig;
            return (ultCfg?.ultimatePrefabTPV, ultCfg?.postureAnimatorControllerTPV);
        }

        /// <summary>
        /// Restores the fire button to normal weapon-fire behaviour.
        /// </summary>
        private void RestoreFireButton()
        {
            if (!fireButtonHijacked) return;
            ArtisansGuns.UI.MobileControlsController.Instance?.ClearFireOverride();
            fireButtonHijacked = false;
        }

        // Dial colours (used for radial cooldown progress in MobileControlsController)
        private static readonly Color DIAL_READY_COLOR    = new Color(0.15f, 0.85f, 0.25f, 1f); // green
        private static readonly Color DIAL_COOLDOWN_COLOR = new Color(1.00f, 0.50f, 0.05f, 1f); // orange

        // ------------------------------------------------------------------
        // Cooldown helpers
        // ------------------------------------------------------------------

        private void StartCooldown(int slot, float seconds)
        {
            if (slot == 1)
            {
                ability1OnCooldown = true;
                if (_cooldown1Coroutine != null) StopCoroutine(_cooldown1Coroutine);
                _cooldown1Coroutine = StartCoroutine(CooldownUICoroutine(slot, seconds));
            }
            else
            {
                ability2OnCooldown = true;
                if (_cooldown2Coroutine != null) StopCoroutine(_cooldown2Coroutine);
                _cooldown2Coroutine = StartCoroutine(CooldownUICoroutine(slot, seconds));
            }
        }

        private IEnumerator CooldownUICoroutine(int slot, float seconds)
        {
            float elapsed = 0f;
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;

            // Lock interactable while on cooldown
            if (ctrl != null)
            {
                if (slot == 1) ctrl.SetAbility1Interactable(false);
                else           ctrl.SetAbility2Interactable(false);
            }

            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);
                ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
                if (ctrl != null)
                {
                    if (slot == 1) ctrl.SetAbility1Progress(t, DIAL_COOLDOWN_COLOR);
                    else           ctrl.SetAbility2Progress(t, DIAL_COOLDOWN_COLOR);
                }
                yield return null;
            }

            // Cooldown finished
            ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (slot == 1)
            {
                ability1OnCooldown = false;
                if (ctrl != null)
                {
                    ctrl.SetAbility1Progress(1f, DIAL_READY_COLOR);
                    ctrl.SetAbility1Interactable(true);
                }
            }
            else
            {
                ability2OnCooldown = false;
                if (ctrl != null)
                {
                    ctrl.SetAbility2Progress(1f, DIAL_READY_COLOR);
                    ctrl.SetAbility2Interactable(true);
                }
            }
        }

        // ------------------------------------------------------------------
        // Kill reward: reset ability 1 & 2 cooldowns (NOT ultimate)
        // ------------------------------------------------------------------

        /// <summary>
        /// Called when the local player gets a kill. Instantly resets ability 1 and 2
        /// cooldowns so they can be used again. Does NOT reset ultimate.
        /// </summary>
        public void ResetAbilityCooldownsOnKill()
        {
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;

            if (ability1OnCooldown)
            {
                if (_cooldown1Coroutine != null) { StopCoroutine(_cooldown1Coroutine); _cooldown1Coroutine = null; }
                ability1OnCooldown = false;
                if (ctrl != null)
                {
                    ctrl.SetAbility1Progress(1f, DIAL_READY_COLOR);
                    ctrl.SetAbility1Interactable(true);
                }
            }

            if (ability2OnCooldown)
            {
                if (_cooldown2Coroutine != null) { StopCoroutine(_cooldown2Coroutine); _cooldown2Coroutine = null; }
                ability2OnCooldown = false;
                if (ctrl != null)
                {
                    ctrl.SetAbility2Progress(1f, DIAL_READY_COLOR);
                    ctrl.SetAbility2Interactable(true);
                }
            }
        }

        /// <summary>
        /// Full reset for a new match — clears ultimate charge, cooldowns, and UI.
        /// Called from GameStateManager.RPC_ResetAllPlayers at match start.
        /// </summary>
        public void ResetForNewMatch()
        {
            _ultimateCharged = false;
            _ultimateUsed    = false;
            _currentUltimateAbility = null;

            // Reset ability cooldowns
            ResetAbilityCooldownsOnKill();

            // Reset ultimate UI
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl != null)
            {
                ctrl.SetUltimateDotsActive(false);
                ctrl.SetUltimateDots(0);
                ctrl.SetUltimateInteractable(false);
            }

            Debug.Log("[AbilitySystem] Full reset for new match.");
        }

        // ------------------------------------------------------------------
        // Sound helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// RPC: plays the vision pulse activation sound on all clients.
        /// Each client decides whether to play it based on team:
        /// enemies and local player hear it; teammates do NOT.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        private void RPC_PlayVisionPulseSound()
        {
            // The local player already played it in OnAbility2Pressed — skip duplicate.
            if (Object.HasInputAuthority) return;

            // Determine team relationship
            var localNetData = FindLocalPlayerNetworkData();
            var myNetData    = GetComponent<PlayerNetworkData>();
            if (localNetData == null || myNetData == null) return;

            // Only enemies hear it (different team)
            if (localNetData.Team == myNetData.Team) return;

            PlayLocal2DSound(_visionPulseClip, 1f);
        }

        /// <summary>
        /// Plays a clip as 2D (no spatialization) on a temporary AudioSource.
        /// </summary>
        private static void PlayLocal2DSound(AudioClip clip, float volume)
        {
            if (clip == null) return;

            // Use a temporary GO so the sound survives even if this component is destroyed
            GameObject go = new GameObject("TempAudio2D");
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 0f;   // fully 2D
            src.playOnAwake  = false;
            src.clip         = clip;
            src.volume       = volume;
            src.Play();
            Destroy(go, clip.length + 0.1f);
        }

        /// <summary>
        /// Plays a clip as full 3D spatial audio at a world position (like footsteps).
        /// </summary>
        private static void PlaySpatialSoundAtPoint(AudioClip clip, Vector3 position,
                                                     float minDist, float maxDist, float volume)
        {
            if (clip == null) return;

            GameObject go  = new GameObject("TempAudio3D");
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.spatialBlend = 1f;                          // full 3D
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance   = minDist;
            src.maxDistance   = maxDist;
            src.playOnAwake  = false;
            src.clip         = clip;
            src.volume       = volume;
            src.Play();
            Destroy(go, clip.length + 0.1f);
        }

        private static PlayerNetworkData FindLocalPlayerNetworkData()
        {
            foreach (var nd in FindObjectsByType<PlayerNetworkData>(FindObjectsSortMode.None))
            {
                if (nd.HasInputAuthority) return nd;
            }
            return null;
        }
    }
}
