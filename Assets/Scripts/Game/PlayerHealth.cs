using UnityEngine;
using UnityEngine.UIElements;
using Fusion;
using ArtisansGuns.Networking;
using ArtisansGuns.Characters;
using ArtisansGuns.Weapons;
using ArtisansGuns.Abilities;
using TMPro;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// PlayerHealth — NetworkBehaviour that lives on every PlayerPrefab.
    /// Handles HP, damage RPC, death VFX, respawn countdown, and kill/death tracking.
    ///
    /// SECURITY MODEL (Shared Mode):
    ///   • The SHOOTER validates the hit locally (raycast + layer mask + team check)
    ///     then sends RPC_TakeDamage to the VICTIM's object.
    ///   • The VICTIM applies damage to its own [Networked] HP (authority over own data).
    ///   • On death, the VICTIM broadcasts RPC_Die so every client plays VFX / hides model.
    ///   • Kill/death counters are on PlayerNetworkData (Networked) — updated by each
    ///     player on their own object (shooter increments own Kills, victim increments own Deaths).
    /// </summary>
    public class PlayerHealth : NetworkBehaviour
    {
        // ────────────────────────────────────────────────────────────────────
        // Constants
        // ────────────────────────────────────────────────────────────────────
        public const float MAX_HP = 150f;
        public const float RESPAWN_SECONDS = 3f;

        // ────────────────────────────────────────────────────────────────────
        // Networked state
        // ────────────────────────────────────────────────────────────────────
        [Networked] public float HP { get; set; }
        [Networked] public NetworkBool IsDead { get; set; }

        /// <summary>
        /// Local-only predicted HP. Each client that calls DealDamage subtracts
        /// damage from this value so that consecutive hits in rapid succession
        /// are tracked BEFORE the Networked HP replicates. When PredictedHP
        /// reaches 0, PredictedDead becomes true and further hits/blood are skipped.
        /// Reset whenever HP is synced or player respawns/ceremony resets.
        /// </summary>
        [System.NonSerialized] public float PredictedHP = MAX_HP;

        /// <summary>True when PredictedHP has reached zero on this client.</summary>
        public bool PredictedDead => PredictedHP <= 0f;

        /// <summary>
        /// Set by DealDamage when predicted kill rewards fire instantly.
        /// Cleared by IncrementKillForShooter so the RPC fallback doesn't double-fire.
        /// </summary>
        [System.NonSerialized] public bool PredictedKillRewardPending;

        // ────────────────────────────────────────────────────────────────────
        // Inspector
        // ────────────────────────────────────────────────────────────────────
        [Header("Immunity")]
        [Tooltip("Material applied to TPV body + FPV arms during spawn immunity (green outline)")]
        [SerializeField] private Material inmuneMaterial;
        private const float IMMUNITY_SECONDS = 3f;

        // ────────────────────────────────────────────────────────────────────
        // Cached references (set in Spawned)
        // ────────────────────────────────────────────────────────────────────
        private PlayerNetworkData  netData;
        private PlayerController   playerController;
        private PlayerSetup        playerSetup;
        private PlayerTPVController tpvController;
        private CharacterController charController;

        // Death overlay — UIToolkit VisualElement inside the GameplayHUD UIDocument.
        // Inserted at index 0 (behind HUD buttons). pickingMode=Ignore so it never
        // absorbs touch events; FireWeapon.IsDead guard is the competitive block.
        private VisualElement deathOverlay;
        private Label respawnText;

        // Damage vignette (red edges on hit) — UIToolkit based
        private UnityEngine.UIElements.VisualElement damageVignetteElement;
        private float damageVignetteAlpha;
        private const float VIGNETTE_FADE_SPEED = 2.5f;
        private const float VIGNETTE_MAX_ALPHA = 0.55f;

        // UI Toolkit health bar (from GameplayHUD UIDocument in scene)
        private Label healthTextElement;
        private VisualElement healthFillElement;

        // Respawn timer (local-only, runs on the dead player's client)
        private float respawnTimer;
        private bool  waitingToRespawn;

        // Death VFX instance (so we can clean it up)
        private GameObject deathVFXInstance;

        /// <summary>
        /// Stores the bot's NetworkObject when a bot deals damage (set on HOST only).
        /// Used by IncrementKillForShooter as a fallback when shooterRef == PlayerRef.None.
        /// </summary>
        [System.NonSerialized] public NetworkObject LastBotKiller;

        // Team layer assigner — re-triggered on respawn to restore correct Enemy/Teammate layer
        private TeamLayerAssigner teamLayerAssigner;

        // Tracks the last observed networked HP so remote clients can detect
        // upward HP changes (kill-reward healing) and sync PredictedHP accordingly.
        private float _lastKnownHP;

        // Grace period: when a predicted kill fires, we record the timestamp.
        // The Render() safety-net will NOT reconcile PredictedHP until this
        // grace window expires — giving RPC_Die time to arrive and confirm.
        private float _predictedKillTime = -10f;
        private const float RECONCILE_GRACE_SEC = 1.5f;

        // Immunity state
        private SkinnedMeshRenderer tpvSMR;          // cached from playerSetup
        private SkinnedMeshRenderer armsSMR;          // cached from playerSetup
        private Material[] tpvOriginalMaterials;      // snapshot taken on first immunity
        private Material[] armsOriginalMaterials;
        private Coroutine  immunityCoroutine;
        private bool       isImmune;                  // true during spawn protection window

        /// <summary>Public read-only access so the shooter's client can skip immune targets.</summary>
        public bool IsImmune => isImmune;

        /// <summary>
        /// Called by CharacterSetupHandler after applying character materials.
        /// If immunity is active, re-snapshots the correct originals and re-applies
        /// the immunity material so it isn't lost to the mesh swap.
        /// </summary>
        public void RefreshImmunityMaterials()
        {
            if (!isImmune || inmuneMaterial == null) return;

            if (tpvSMR != null)
            {
                tpvOriginalMaterials = tpvSMR.sharedMaterials;
                tpvSMR.sharedMaterials = BuildUniformArray(inmuneMaterial, tpvOriginalMaterials.Length);
            }

            if (Object.HasInputAuthority && armsSMR != null)
            {
                armsOriginalMaterials = armsSMR.sharedMaterials;
                armsSMR.sharedMaterials = BuildUniformArray(inmuneMaterial, armsOriginalMaterials.Length);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Fusion lifecycle
        // ────────────────────────────────────────────────────────────────────

        public override void Spawned()
        {
            // Cache sibling components
            netData          = GetComponent<PlayerNetworkData>();
            playerController = GetComponent<PlayerController>();
            playerSetup      = GetComponent<PlayerSetup>();
            tpvController     = playerSetup != null ? playerSetup.tpvController         : null;
            charController    = GetComponent<CharacterController>();
            teamLayerAssigner = GetComponent<TeamLayerAssigner>();

            // Cache mesh renderers for immunity material swap
            if (playerSetup != null)
            {
                tpvSMR  = playerSetup.tpvSkinnedMeshRenderer;
                armsSMR = playerSetup.armsSkinnedMeshRenderer;
            }

            // Initialize HP (authority sets own HP; host sets bot HP)
            bool isBot = playerController != null && playerController.IsBotControlled;
            if (Object.HasInputAuthority || (isBot && Object.HasStateAuthority))
            {
                HP = MAX_HP;
                IsDead = false;
            }

            // Clear local prediction for all clients
            PredictedHP = MAX_HP;

            // Build or find the death overlay UI (local player only)
            if (Object.HasInputAuthority)
            {
                CreateDeathOverlayUI();
                CreateDamageVignetteUI();
                CacheHealthBarUI();
                UpdateHealthBarUI();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Public API — Ceremony reset
        // ────────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Resets HP to full and clears death state. Called by ceremony system
        /// before a new round starts. Only the local player (InputAuthority) should call this.
        /// </summary>
        public void ResetForNewRound()
        {
            // Reset momentum first so CurrentMaxHP returns base value
            GetComponent<MomentumManager>()?.ResetForNewRound();

            HP = MAX_HP;
            IsDead = false;
            PredictedHP = MAX_HP;
            waitingToRespawn = false;

            // Hide death overlay if it was showing
            if (deathOverlay != null) deathOverlay.style.display = DisplayStyle.None;

            // Update health bar UI
            UpdateHealthBarUI();
        }

        // ────────────────────────────────────────────────────────────────────
        // Public API — called by FireWeapon on the SHOOTER'S client
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called on the SHOOTER's client after a successful raycast hit on an enemy.
        /// Sends an RPC to the VICTIM so they apply the damage.
        /// </summary>
        public static void DealDamage(
            PlayerHealth victim,
            float damage,
            bool isHeadshot,
            float headshotMultiplier,
            PlayerRef shooterRef,
            string weaponId = "",
            NetworkObject botShooterObj = null)
        {
            if (victim == null || victim.IsDead || victim.PredictedDead) return;

            // Track bot shooter for kill credit (set only on HOST)
            if (botShooterObj != null)
                victim.LastBotKiller = botShooterObj;

            // Block damage on immune targets (spawn protection) — prevents
            // false predicted kills when immunity just ended on the shooter's
            // client but is still active on the victim's authority.
            if (victim.IsImmune) return;

            // Block damage from dead shooters — prevents mutual kills
            if (victim.Runner != null)
            {
                var shooterObj = victim.Runner.GetPlayerObject(shooterRef);
                if (shooterObj != null)
                {
                    var shooterHealth = shooterObj.GetComponent<PlayerHealth>();
                    if (shooterHealth != null && (shooterHealth.IsDead || shooterHealth.PredictedDead))
                        return;
                }
            }

            // If the victim was healed (kill reward) above our local PredictedHP,
            // sync up to prevent false death predictions from stale values.
            // Skip if PredictedDead — the server just hasn't confirmed the kill yet.
            if (victim.HP > victim.PredictedHP)
                victim.PredictedHP = victim.HP;

            // Block damage during the PreStart warm-up (players can move but not kill)
            var gsm = ArtisansGuns.Networking.GameStateManager.Instance;
            if (gsm != null && gsm.Object != null && gsm.Object.IsValid && gsm.PreStartActive) return;

            // Block ALL damage once the match has ended (covers delayed grenades/abilities)
            if (gsm != null && gsm.Object != null && gsm.Object.IsValid && gsm.MatchEnded) return;

            float finalDamage = isHeadshot ? damage * headshotMultiplier : damage;

            // Subtract from local predicted HP BEFORE sending the RPC.
            // This way rapid consecutive shots correctly accumulate.
            victim.PredictedHP -= finalDamage;
            if (victim.PredictedHP < 0f) victim.PredictedHP = 0f;

            victim.RPC_TakeDamage(finalDamage, shooterRef, WeaponIdToByte(weaponId), isHeadshot);

            if (victim.PredictedDead)
            {
                victim._predictedKillTime = Time.time;
                Debug.Log($"[PlayerHealth] PredictedDead (PredictedHP=0) for Player {victim.Object.InputAuthority.PlayerId}");

                // ── INSTANT predicted death visuals (same frame as lethal shot) ──
                // Hide TPV model + spawn death VFX immediately on the shooter's
                // client so there's no perceptible delay waiting for RPC_Die.
                if (victim.tpvController != null)
                    victim.tpvController.HideTPV();

                var victimCapsule = victim.GetComponent<CapsuleCollider>();
                if (victimCapsule != null) victimCapsule.enabled = false;

                if (victim.charController != null) victim.charController.enabled = false;

                // SpawnDeathVFX is handled by RPC_Die on all clients — skip here
                // to avoid duplicate VFX on the shooter's client.

                // ── INSTANT combo kill audio + white flash (predicted kill) ──
                // Fires on the shooter's client the same frame as the lethal shot,
                // eliminating the 2-RPC delay of RPC_TakeDamage → RPC_Die → IncrementKillForShooter.
                if (victim.Runner != null)
                {
                    var shooterObj = victim.Runner.GetPlayerObject(shooterRef);
                    if (shooterObj != null && shooterObj.HasInputAuthority)
                    {
                        var activeWeapon = shooterObj.GetComponent<PlayerSetup>()?.GetActiveWeaponConfig();
                        ArtisansGuns.Audio.ComboKillManager.Instance?.OnKillConfirmed(activeWeapon);

                        // ── Kill rewards: momentum buffs + reset ability cooldowns ──
                        var shooterHealth = shooterObj.GetComponent<PlayerHealth>();
                        if (shooterHealth != null)
                        {
                            shooterHealth.PredictedKillRewardPending = true;
                        }

                        // Momentum: speed + HP bonuses + start passive regen
                        shooterObj.GetComponent<MomentumManager>()?.OnKill();

                        shooterObj.GetComponent<AbilitySystem>()
                            ?.ResetAbilityCooldownsOnKill();
                    }
                }
            }
        }

        // ── Weapon code helpers (byte fits inside Fusion RPC primitives) ─────
        private static byte WeaponIdToByte(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return 0;
            switch (weaponId.ToLower())
            {
                case "talon_ar":      return 1;
                case "bolt":          return 2;
                case "knife":         return 3;
                case "default_knife": return 3;
                case "crimson_ultimate": return 4;
                case "onyx":            return 5;
                case "titan":           return 6;
                default:              return 0;
            }
        }

        private static string ByteToWeaponId(byte code)
        {
            switch (code)
            {
                case 1:  return "talon_ar";
                case 2:  return "bolt";
                case 3:  return "knife";
                case 4:  return "crimson_ultimate";
                case 5:  return "onyx";
                case 6:  return "titan";
                default: return "talon_ar";
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // RPCs
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Received on ALL clients (especially the victim who has authority over HP).
        /// In Shared Mode every client can write to their own [Networked] properties.
        /// </summary>
        // Weapon code of the last hit (stored locally on the victim, used when broadcasting kill feed)
        private byte lastHitWeaponCode;
        private bool lastHitWasHeadshot;

        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_TakeDamage(float damage, PlayerRef shooterRef, byte weaponCode = 0, bool isHeadshot = false)
        {
            // ── Host (StateAuthority) validates and applies damage ──────────
            if (Object.HasStateAuthority)
            {
                if (IsDead) return;
                if (isImmune) return;   // spawn immunity — no damage

                // Block damage from dead shooters (authoritative backup)
                var shooterObj = Runner.GetPlayerObject(shooterRef);
                if (shooterObj != null)
                {
                    var shooterHealth = shooterObj.GetComponent<PlayerHealth>();
                    if (shooterHealth != null && shooterHealth.IsDead) return;
                }

                // Block damage after match timer expired or match ended
                var gsm = ArtisansGuns.Networking.GameStateManager.Instance;
                if (gsm != null && gsm.Object != null && gsm.Object.IsValid && (gsm.MatchEnded || !gsm.GameInProgress)) return;

                lastHitWeaponCode  = weaponCode;
                lastHitWasHeadshot = isHeadshot;
                HP -= damage;
                Debug.Log($"[PlayerHealth] Took {damage:F0} damage → HP={HP:F0}");

                if (HP <= 0f)
                {
                    HP = 0f;
                    Die(shooterRef);
                }
            }

            // ── Victim client (InputAuthority): local visual feedback ───────
            if (Object.HasInputAuthority)
            {
                UpdateHealthBarUI();
                FlashDamageVignette();

                // 69% movement slow for 2 seconds on taking damage
                if (playerController != null)
                    playerController.ApplyDamageSlow();
            }
        }

        /// <summary>
        /// Fired by the HOST (StateAuthority) to all clients when a player dies.
        /// Handles visuals on every client, stats on host, and UI on victim.
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_Die(PlayerRef shooterRef, byte weaponCode = 0, bool isHeadshot = false)
        {
            // ── ALL clients: hide TPV + disable collider + set death layers ─
            if (tpvController != null)
                tpvController.HideTPV();

            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null) capsule.enabled = false;

            // Also disable CharacterController's built-in collider so dead bodies
            // don't block bullets (CC has its own capsule that is NOT the CapsuleCollider).
            if (charController != null) charController.enabled = false;

            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) gameObject.layer = playerLayer;
            if (tpvController != null)
            {
                GameObject tpvRoot = tpvController.playerTPVRoot;
                if (tpvRoot != null)
                    SetLayerRecursive(tpvRoot, playerLayer);
            }

            // ── ALL clients: death VFX + kill feed ─────────────────────────
            SpawnDeathVFX();
            ShowKillFeedEntry(shooterRef, ByteToWeaponId(weaponCode), isHeadshot);

            // ── ALL clients: credit the kill on the shooter's own machine ───
            // (only the shooter's machine has HasInputAuthority on the shooter's object)
            IncrementKillForShooter(shooterRef, isHeadshot);

            // ── Victim's machine (HasStateAuthority on own object): death stat
            if (Object.HasStateAuthority)
            {
                if (netData != null)
                {
                    netData.Deaths += 1;
                    netData.CurrentStreak = 0; // Reset kill streak on death
                    netData.UpdatePlayerCache();
                    Debug.Log($"[PlayerHealth] DEATH #{netData.Deaths} for {netData.Username}");
                }
            }

            // ── VICTIM client (InputAuthority): disable input + show UI ─────
            if (Object.HasInputAuthority)
            {
                // Drop all weapons on death (so other players can pick them up)
                GetComponent<WeaponDropSystem>()?.DropAllWeaponsOnDeath();

                // Kill charges toward ultimate persist through death — do NOT call full ResetCombo().
                // But the kill-streak counter (KillUI number) must reset on death.
                ArtisansGuns.Audio.ComboKillManager.Instance?.ResetKillStreakOnDeath();

                // Momentum: reset all speed + HP bonuses on death
                GetComponent<MomentumManager>()?.ResetOnDeath();

                if (playerController != null)
                    playerController.enabled = false;
                ShowDeathOverlay(true);
                waitingToRespawn = true;
                respawnTimer = RESPAWN_SECONDS;
            }

            // ── BOT: host starts respawn timer ──────────────────────────────
            bool isBotDeath = playerController != null && playerController.IsBotControlled && Object.HasStateAuthority;
            if (isBotDeath)
            {
                waitingToRespawn = true;
                respawnTimer = RESPAWN_SECONDS;
            }
        }

        /// <summary>
        /// Broadcast by the respawning player so every client re-shows the TPV model.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_Respawn(Vector3 spawnPos, Quaternion spawnRot)
        {
            // Clear predicted-dead on all clients so this player can be hit again
            PredictedHP = MAX_HP;

            // ── ALL clients: show TPV + restore collider + start immunity ───
            if (tpvController != null)
                tpvController.ShowTPV();

            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null) capsule.enabled = true;

            if (charController != null) charController.enabled = true;

            StartImmunity();

            // ── VICTIM client (InputAuthority): restore control + hide UI ───
            if (Object.HasInputAuthority)
            {
                // Re-enable movement input
                if (playerController != null)
                {
                    playerController.enabled = true;
                    playerController.ForceStand(); // clear crouch state from before death
                }

                ShowDeathOverlay(false);
                waitingToRespawn = false;
                UpdateHealthBarUI();

                // Teleport (InputAuthority drives the CharacterController position)
                if (charController != null) charController.enabled = false;
                transform.position = spawnPos;
                transform.rotation = spawnRot;
                if (charController != null) charController.enabled = true;

                if (playerController != null)
                {
                    playerController.NetworkPosition = spawnPos;
                    playerController.NetworkRotation = spawnRot;
                }

                // Restore original weapon loadout (full ammo) after respawn
                GetComponent<WeaponDropSystem>()?.RestoreOriginalLoadout();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Layer helpers (death)
        // ────────────────────────────────────────────────────────────────────

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        // ────────────────────────────────────────────────────────────────────
        // Immunity (spawn protection)
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Broadcast immunity start to ALL clients so every player sees the
        /// green material and the blood/damage guard works everywhere.
        /// Call this instead of StartImmunity() from non-RPC code paths
        /// (e.g. AssignPlayerTeam).
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_StartImmunity()
        {
            StartImmunity();
        }

        public void StartImmunity()
        {
            isImmune = true;

            int inmuneLayer = LayerMask.NameToLayer("InmunePlayer");
            if (inmuneLayer < 0)
            {
                Debug.LogWarning("[PlayerHealth] Layer 'InmunePlayer' not found — skipping immunity layer.");
                // isImmune still true — damage is blocked even without the layer
            }
            else
            {
                // Only the root PlayerPrefab (has the CapsuleCollider that raycasts hit).
                // No renderer here, so camera culling is unaffected.
                gameObject.layer = inmuneLayer;
            }

            // Restore TPV layer immediately to Enemy/Teammate/Player so cameras
            // render the respawned player correctly from frame 1 of immunity.
            // (RPC_Die had set TPV to "Player" — we fix that right now.)
            if (teamLayerAssigner != null)
                teamLayerAssigner.ResetLayerAssignment();

            // Apply immunity material — snapshot originals first (only once per life)
            if (inmuneMaterial != null)
            {
                if (tpvSMR != null)
                {
                    if (tpvOriginalMaterials == null || tpvOriginalMaterials.Length == 0)
                        tpvOriginalMaterials = tpvSMR.sharedMaterials;
                    tpvSMR.sharedMaterials = BuildUniformArray(inmuneMaterial, tpvSMR.sharedMaterials.Length);
                }

                // FPV arms — only the local player sees them
                if (Object.HasInputAuthority && armsSMR != null)
                {
                    if (armsOriginalMaterials == null || armsOriginalMaterials.Length == 0)
                        armsOriginalMaterials = armsSMR.sharedMaterials;
                    armsSMR.sharedMaterials = BuildUniformArray(inmuneMaterial, armsSMR.sharedMaterials.Length);
                }
            }

            // Restart coroutine (e.g. died again during immunity)
            if (immunityCoroutine != null)
                StopCoroutine(immunityCoroutine);
            immunityCoroutine = StartCoroutine(ImmunityCoroutine());
        }

        private System.Collections.IEnumerator ImmunityCoroutine()
        {
            yield return new WaitForSeconds(IMMUNITY_SECONDS);

            isImmune = false;

            // Safety net: reset predicted HP so shooters that fired during
            // immunity don't leave a stale low value.
            PredictedHP = HP > 0f ? HP : MAX_HP;

            // Restore original materials
            if (tpvSMR != null && tpvOriginalMaterials != null && tpvOriginalMaterials.Length > 0)
                tpvSMR.sharedMaterials = tpvOriginalMaterials;

            if (Object.HasInputAuthority && armsSMR != null &&
                armsOriginalMaterials != null && armsOriginalMaterials.Length > 0)
                armsSMR.sharedMaterials = armsOriginalMaterials;

            // Clear cache so next immunity snapshots fresh materials
            tpvOriginalMaterials  = null;
            armsOriginalMaterials = null;

            // Restore root PlayerPrefab layer to "Player"
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) gameObject.layer = playerLayer;

            // Re-assign Enemy / Teammate / Player layer on TPV + all children
            if (teamLayerAssigner != null)
                teamLayerAssigner.ResetLayerAssignment();

            immunityCoroutine = null;
        }

        private static Material[] BuildUniformArray(Material mat, int count)
        {
            var arr = new Material[Mathf.Max(1, count)];
            for (int i = 0; i < arr.Length; i++) arr[i] = mat;
            return arr;
        }

        // ────────────────────────────────────────────────────────────────────
        // Internal — death flow
        // ────────────────────────────────────────────────────────────────────

        private void Die(PlayerRef shooterRef)
        {
            // HOST (StateAuthority) sets dead flag and broadcasts to all clients.
            // Local visual/UI effects are handled inside RPC_Die's HasInputAuthority block.
            IsDead = true;
            RPC_Die(shooterRef, lastHitWeaponCode, lastHitWasHeadshot);
        }

        private void Update()
        {
            bool isBot = playerController != null && playerController.IsBotControlled;
            if (!Object) return;
            if (!Object.HasInputAuthority && !(isBot && Object.HasStateAuthority)) return;

            // Fade damage vignette
            if (damageVignetteAlpha > 0f)
            {
                damageVignetteAlpha -= Time.deltaTime * VIGNETTE_FADE_SPEED;
                if (damageVignetteAlpha <= 0f) damageVignetteAlpha = 0f;
                UpdateVignetteAlpha();
            }

            if (!waitingToRespawn) return;

            respawnTimer -= Time.deltaTime;

            // Update countdown text
            if (respawnText != null)
            {
                int displaySeconds = Mathf.CeilToInt(Mathf.Max(0f, respawnTimer));
                respawnText.text = $"Respawning in {displaySeconds}...";
            }

            // Don't respawn if match has ended — dead players stay dead through ceremony
            var gsm = ArtisansGuns.Networking.GameStateManager.Instance;
            if (gsm != null && gsm.Object != null && gsm.Object.IsValid && gsm.MatchEnded)
            {
                if (respawnText != null) respawnText.text = "";
                return;
            }

            if (respawnTimer <= 0f)
            {
                waitingToRespawn = false;
                bool isBotRespawn = playerController != null && playerController.IsBotControlled;
                if (isBotRespawn)
                    BotRespawn();
                else
                    Respawn();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Respawn
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called on the victim's machine when the respawn timer expires.
        /// Picks a safe spawn position and broadcasts RPC_Respawn to all clients.
        /// </summary>
        private void Respawn()
        {
            // ── Pick a safe spawn position (host has GameManager access) ───
            int team = netData != null ? netData.Team : 0;
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;

            try
            {
                var gm = GameManager.Instance;
                if (gm != null)
                {
                    spawnPos = gm.GetSafeSpawnPositionForTeam(team);
                    spawnRot = gm.GetSpawnRotationForTeam(team);
                }
                else
                {
                    Debug.LogWarning("[PlayerHealth] GameManager not found — respawning at origin");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PlayerHealth] Error getting spawn position: {e.Message}. Using fallback.");
            }

            // ── Host resets networked state ─────────────────────────────────
            HP = MAX_HP;
            IsDead = false;

            // ── Broadcast: visuals + victim UI handled inside RPC_Respawn ───
            RPC_Respawn(spawnPos, spawnRot);

            Debug.Log($"[PlayerHealth] Respawned at {spawnPos} with {HP} HP");
        }

        /// <summary>
        /// Bot respawn — called on host when bot's respawn timer expires.
        /// Uses StateAuthority RPC since bots have no InputAuthority.
        /// </summary>
        private void BotRespawn()
        {
            int team = netData != null ? netData.Team : 0;
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;

            try
            {
                var gm = GameManager.Instance;
                if (gm != null)
                {
                    spawnPos = gm.GetSafeSpawnPositionForTeam(team);
                    spawnRot = gm.GetSpawnRotationForTeam(team);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PlayerHealth] Bot respawn error: {e.Message}");
            }

            // Reset networked state
            HP = MAX_HP;
            IsDead = false;

            // Teleport bot
            if (charController != null) charController.enabled = false;
            transform.position = spawnPos;
            transform.rotation = spawnRot;
            if (charController != null) charController.enabled = true;

            if (playerController != null)
            {
                playerController.NetworkPosition = spawnPos;
                playerController.NetworkRotation = spawnRot;
            }

            // Broadcast visual respawn to all clients
            RPC_BotRespawn(spawnPos, spawnRot);

            Debug.Log($"[PlayerHealth] Bot respawned at {spawnPos}");
        }

        /// <summary>
        /// Broadcast by host so every client re-shows the bot's TPV model.
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_BotRespawn(Vector3 spawnPos, Quaternion spawnRot)
        {
            PredictedHP = MAX_HP;

            if (tpvController != null)
                tpvController.ShowTPV();

            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null) capsule.enabled = true;

            StartImmunity();
        }

        // ────────────────────────────────────────────────────────────────────
        // Kill credit helper
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called on every client inside RPC_Die.
        /// Only the shooter's own machine has HasInputAuthority on the shooter's object,
        /// so only that machine increments Kills.
        /// </summary>
        private void IncrementKillForShooter(PlayerRef shooterRef, bool isHeadshot = false)
        {
            if (!Runner) return;

            foreach (var player in Runner.ActivePlayers)
            {
                if (player != shooterRef) continue;

                var shooterObj = Runner.GetPlayerObject(player);
                if (shooterObj == null) break;

                var shooterData = shooterObj.GetComponent<PlayerNetworkData>();
                if (shooterData == null) break;

                // In Shared Mode each player has InputAuthority only on their own object
                if (shooterObj.HasInputAuthority)
                {
                    shooterData.Kills += 1;
                    
                    // Increment the global team kill counter (persists after player leaves)
                    var gsm = ArtisansGuns.Networking.GameStateManager.Instance;
                    if (gsm != null && gsm.Object != null && gsm.Object.IsValid)
                        gsm.RPC_AddTeamKill(shooterData.Team);
                    
                    // Track headshot kills
                    if (isHeadshot)
                        shooterData.Headshots += 1;
                    
                    // Track kill streak
                    shooterData.CurrentStreak += 1;
                    if (shooterData.CurrentStreak > shooterData.BestStreak)
                        shooterData.BestStreak = shooterData.CurrentStreak;
                    
                    shooterData.UpdatePlayerCache();
                    Debug.Log($"[PlayerHealth] KILL #{shooterData.Kills} for {shooterData.Username}");

                    // Check if DealDamage already fired predicted kill rewards.
                    // If not (PredictedHP didn't reach 0 because other damage was unknown),
                    // fire rewards as a fallback so kills always feel rewarding.
                    var shooterHealth = shooterObj.GetComponent<PlayerHealth>();
                    if (shooterHealth != null && shooterHealth.PredictedKillRewardPending)
                    {
                        // Predicted path already fired rewards — just clear the flag
                        shooterHealth.PredictedKillRewardPending = false;
                    }
                    else
                    {
                        // Fallback: predicted kill didn't trigger — fire rewards now
                        var activeWeapon = shooterObj.GetComponent<PlayerSetup>()?.GetActiveWeaponConfig();
                        ArtisansGuns.Audio.ComboKillManager.Instance?.OnKillConfirmed(activeWeapon);

                        // Momentum: speed + HP bonuses + start passive regen
                        shooterObj.GetComponent<MomentumManager>()?.OnKill();

                        shooterObj.GetComponent<AbilitySystem>()
                            ?.ResetAbilityCooldownsOnKill();

                        Debug.Log("[PlayerHealth] Fallback kill rewards fired from RPC_Die");
                    }
                }
                break;
            }

            // ── Bot kill credit fallback ─────────────────────────────────
            // LastBotKiller is set only on the HOST (where bots run).
            // If the standard PlayerRef loop didn't find the shooter (bot has None),
            // credit the kill to the bot object directly.
            if (LastBotKiller != null && LastBotKiller.IsValid)
            {
                var botData = LastBotKiller.GetComponent<PlayerNetworkData>();
                if (botData != null && LastBotKiller.HasStateAuthority)
                {
                    botData.Kills += 1;
                    if (isHeadshot) botData.Headshots += 1;
                    botData.CurrentStreak += 1;
                    if (botData.CurrentStreak > botData.BestStreak)
                        botData.BestStreak = botData.CurrentStreak;
                    botData.UpdatePlayerCache();

                    var gsm = ArtisansGuns.Networking.GameStateManager.Instance;
                    if (gsm != null && gsm.Object != null && gsm.Object.IsValid)
                        gsm.RPC_AddTeamKill(botData.Team);

                    Debug.Log($"[PlayerHealth] Bot kill credit: {botData.Username} got kill #{botData.Kills}");
                }
                LastBotKiller = null;
            }
        }

        /// <summary>
        /// Resolves killer/victim names from network data and pushes to the kill feed.
        /// Called inside RPC_Die (runs on every client).
        /// </summary>
        private void ShowKillFeedEntry(PlayerRef shooterRef, string weaponId, bool isHeadshot)
        {
            // Victim name + team (this object)
            string victimName = netData != null ? netData.CharacterName.ToString() : "???";
            if (string.IsNullOrEmpty(victimName) || victimName == "0")
                victimName = netData?.Username.ToString() ?? "???";
            int victimTeam = netData != null ? netData.Team : 0;

            // Killer name + team (find their PlayerNetworkData in the scene)
            string killerName = "???";
            int killerTeam = 1; // default opposite team
            if (Runner != null)
            {
                var killerObj = Runner.GetPlayerObject(shooterRef);
                if (killerObj != null)
                {
                    var killerData = killerObj.GetComponent<PlayerNetworkData>();
                    if (killerData != null)
                    {
                        killerName = killerData.CharacterName.ToString();
                        if (string.IsNullOrEmpty(killerName) || killerName == "0")
                            killerName = killerData.Username.ToString();
                        killerTeam = killerData.Team;
                    }
                }
                // Bot fallback: LastBotKiller was set on HOST by DealDamage
                else if (LastBotKiller != null && LastBotKiller.IsValid)
                {
                    var botData = LastBotKiller.GetComponent<PlayerNetworkData>();
                    if (botData != null)
                    {
                        killerName = botData.CharacterName.ToString();
                        if (string.IsNullOrEmpty(killerName) || killerName == "0")
                            killerName = botData.Username.ToString();
                        killerTeam = botData.Team;
                    }
                }
            }

            ArtisansGuns.UI.KillFeedManager.Instance.ShowKill(
                killerName, weaponId, victimName, isHeadshot, killerTeam, victimTeam);
        }

        // ────────────────────────────────────────────────────────────────────
        // Death VFX
        // ────────────────────────────────────────────────────────────────────

        private void SpawnDeathVFX()
        {
            // Resolve CharacterConfig to get VFX prefab
            GameObject vfxPrefab = null;
            float vfxDuration = 3f;

            if (netData != null)
            {
                string agentId = netData.SelectedAgent.ToString();
                if (!string.IsNullOrEmpty(agentId))
                {
                    string lower = agentId.ToLower();
                    CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{lower}");
                    if (cfg == null)
                    {
                        string cap = char.ToUpper(lower[0]) + lower.Substring(1);
                        cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
                    }
                    if (cfg != null)
                    {
                        vfxPrefab = cfg.deathVFXPrefab;
                        vfxDuration = cfg.deathVFXDuration;
                    }
                }
            }

            if (vfxPrefab == null) return;

            // Use transform.position (where the player was standing)
            deathVFXInstance = Instantiate(vfxPrefab, transform.position, Quaternion.identity);
            Destroy(deathVFXInstance, vfxDuration);
        }

        // ────────────────────────────────────────────────────────────────────
        // Death overlay UI (local player only — black screen + countdown)
        // ────────────────────────────────────────────────────────────────────

        private void CreateDeathOverlayUI()
        {
            // Find the GameplayHUD UIDocument (same one that holds the health bar)
            UIDocument hudDoc = null;
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (doc.rootVisualElement != null &&
                    doc.rootVisualElement.Q<Label>("HealthText") != null)
                {
                    hudDoc = doc;
                    break;
                }
            }
            if (hudDoc == null)
            {
                Debug.LogWarning("[PlayerHealth] GameplayHUD UIDocument not found — death overlay skipped");
                return;
            }

            var root = hudDoc.rootVisualElement;

            // Reuse existing overlay if already created (reconnect / Spawned called twice)
            var existing = root.Q<VisualElement>("DeathOverlayPanel");
            if (existing != null)
            {
                deathOverlay = existing;
                respawnText  = existing.Q<Label>("RespawnCountdown");
                deathOverlay.style.display = DisplayStyle.None;
                return;
            }

            // ── Full-screen black panel ───────────────────────────────────────
            // Placed ABOVE joystick/fire/health but BELOW Scoreboard/Settings/Scores,
            // same z-layer as CeremonyCountdownOverlay.
            // pickingMode = Ignore → zero event absorption; joystick / fire button
            // (UGUI Canvas) are unaffected. FireWeapon.IsDead is the competitive guard.
            deathOverlay = new VisualElement();
            deathOverlay.name = "DeathOverlayPanel";
            deathOverlay.pickingMode = PickingMode.Ignore;
            deathOverlay.style.position = Position.Absolute;
            deathOverlay.style.left   = 0;
            deathOverlay.style.top    = 0;
            deathOverlay.style.right  = 0;
            deathOverlay.style.bottom = 0;
            deathOverlay.style.backgroundColor = new StyleColor(Color.black);
            deathOverlay.style.alignItems      = Align.Center;
            deathOverlay.style.justifyContent  = Justify.Center;
            deathOverlay.style.flexDirection   = FlexDirection.Column;
            deathOverlay.style.display         = DisplayStyle.None; // hidden until death

            // ── "YOU DIED" label ─────────────────────────────────────────────
            var diedLabel = new Label("YOU DIED");
            diedLabel.name = "YouDiedLabel";
            diedLabel.pickingMode = PickingMode.Ignore;
            diedLabel.style.fontSize = 64;
            diedLabel.style.color = new StyleColor(new Color(0.85f, 0.12f, 0.12f, 1f));
            diedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            diedLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            diedLabel.style.marginBottom = 16;

            // ── Respawn countdown label ──────────────────────────────────────
            respawnText = new Label("Respawning in 3...");
            respawnText.name = "RespawnCountdown";
            respawnText.pickingMode = PickingMode.Ignore;
            respawnText.style.fontSize = 32;
            respawnText.style.color = new StyleColor(new Color(0.7f, 0.15f, 0.15f, 0.9f));
            respawnText.style.unityFontStyleAndWeight = FontStyle.Bold;
            respawnText.style.unityTextAlign = TextAnchor.MiddleCenter;

            deathOverlay.Add(diedLabel);
            deathOverlay.Add(respawnText);

            // Insert right after CeremonyCountdownOverlay → covers joystick/health/fire
            // but below Scoreboard/Settings/Scores (same z-layer as ceremony overlay).
            // IMPORTANT: use ceremonyOverlay.parent (the UXML "Root" element), NOT
            // hudDoc.rootVisualElement, because the UXML tree lives inside a
            // TemplateContainer child — calling IndexOf on the wrong parent returns -1.
            var ceremonyOverlay = root.Q<VisualElement>("CeremonyCountdownOverlay");
            if (ceremonyOverlay != null)
            {
                var container = ceremonyOverlay.parent;
                int idx = container.IndexOf(ceremonyOverlay);
                container.Insert(idx + 1, deathOverlay);
            }
            else
            {
                var scoreboard = root.Q<VisualElement>("Scoreboard");
                if (scoreboard != null)
                {
                    var container = scoreboard.parent;
                    container.Insert(container.IndexOf(scoreboard), deathOverlay);
                }
                else
                    root.Add(deathOverlay);
            }
        }

        private void ShowDeathOverlay(bool show)
        {
            if (deathOverlay != null)
                deathOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ────────────────────────────────────────────────────────────────────
        // Damage vignette (red edges on hit feedback) — UIToolkit
        // ────────────────────────────────────────────────────────────────────

        private void CreateDamageVignetteUI()
        {
            // Find the GameplayHUD UIDocument (same as death overlay)
            UIDocument hudDoc = null;
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (doc.rootVisualElement != null &&
                    doc.rootVisualElement.Q<Label>("HealthText") != null)
                {
                    hudDoc = doc;
                    break;
                }
            }
            if (hudDoc == null)
            {
                Debug.LogWarning("[PlayerHealth] GameplayHUD UIDocument not found — damage vignette skipped");
                return;
            }

            var root = hudDoc.rootVisualElement;

            // Reuse existing element if already created
            var existing = root.Q<VisualElement>("DamageVignetteOverlay");
            if (existing != null)
            {
                damageVignetteElement = existing;
                damageVignetteElement.style.opacity = 0f;
                return;
            }

            // Create a full-screen VisualElement with a radial gradient texture
            damageVignetteElement = new VisualElement();
            damageVignetteElement.name = "DamageVignetteOverlay";
            damageVignetteElement.pickingMode = PickingMode.Ignore;
            damageVignetteElement.style.position = Position.Absolute;
            damageVignetteElement.style.left   = 0;
            damageVignetteElement.style.top    = 0;
            damageVignetteElement.style.right  = 0;
            damageVignetteElement.style.bottom = 0;
            damageVignetteElement.style.opacity = 0f;

            // Create a radial gradient texture: transparent center, red edges
            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                    float alpha = Mathf.Clamp01((dist - 0.4f) / 0.6f);
                    alpha = alpha * alpha;
                    tex.SetPixel(x, y, new Color(0.8f, 0.05f, 0.05f, alpha));
                }
            }
            tex.Apply();

            damageVignetteElement.style.backgroundImage = new StyleBackground(tex);

            // Insert AFTER death overlay so it sits on top when both are visible
            // but below Scoreboard/Settings
            var deathPanel = root.Q<VisualElement>("DeathOverlayPanel");
            if (deathPanel != null)
            {
                var container = deathPanel.parent;
                int idx = container.IndexOf(deathPanel);
                container.Insert(idx + 1, damageVignetteElement);
            }
            else
            {
                // Fallback: insert before Scoreboard
                var scoreboard = root.Q<VisualElement>("Scoreboard");
                if (scoreboard != null)
                {
                    var container = scoreboard.parent;
                    container.Insert(container.IndexOf(scoreboard), damageVignetteElement);
                }
                else
                    root.Add(damageVignetteElement);
            }
        }

        private void FlashDamageVignette()
        {
            damageVignetteAlpha = VIGNETTE_MAX_ALPHA;
            UpdateVignetteAlpha();
        }

        private void UpdateVignetteAlpha()
        {
            if (damageVignetteElement != null)
                damageVignetteElement.style.opacity = damageVignetteAlpha;
        }

        // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        // UI Toolkit health bar (GameplayHUD)
        // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        /// <summary>
        /// Find the HealthText and HealthFill elements from the GameplayHUD UIDocument in the scene.
        /// </summary>
        private void CacheHealthBarUI()
        {
            // Find all UIDocuments in scene, look for one with HealthText
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (doc.rootVisualElement == null) continue;

                var txt = doc.rootVisualElement.Q<Label>("HealthText");
                if (txt != null)
                {
                    healthTextElement = txt;
                    healthFillElement = doc.rootVisualElement.Q<VisualElement>("HealthFill");
                    break;
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Fusion Render — sync PredictedHP on remote clients when HP increases
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs every visual frame on all clients. On remote clients (non-authority),
        /// detects upward HP changes (e.g. kill-reward healing) and syncs PredictedHP
        /// so stale low values don't cause false PredictedDead predictions.
        /// </summary>
        public override void Render()
        {
            if (!Object.HasInputAuthority && HP > _lastKnownHP && !IsDead)
            {
                PredictedHP = HP;
            }
            _lastKnownHP = HP;

            // ── Safety net: keep TPV/collider in sync with [Networked] IsDead ──
            // If an RPC was missed (frame drop, hitpause), this corrects the ghost state.
            // IMPORTANT: We honour a grace period after a predicted kill so the safety
            // net doesn't re-show the victim before RPC_Die has time to arrive.
            if (!Object.HasInputAuthority && tpvController != null)
            {
                bool withinGrace = (Time.time - _predictedKillTime) < RECONCILE_GRACE_SEC;

                if (!IsDead && !tpvController.IsTPVVisible && !withinGrace)
                {
                    tpvController.ShowTPV();
                    var cap = GetComponent<CapsuleCollider>();
                    if (cap != null) cap.enabled = true;

                    // Reconcile false predicted kill: server says alive, prediction said dead.
                    if (PredictedDead)
                    {
                        PredictedHP = HP > 0f ? HP : MAX_HP;
                        Debug.Log($"[PlayerHealth] Reconciled false PredictedDead for Player {Object.InputAuthority.PlayerId} — PredictedHP restored to {PredictedHP}");
                    }
                }
                else if (IsDead && tpvController.IsTPVVisible)
                {
                    tpvController.HideTPV();
                    var cap = GetComponent<CapsuleCollider>();
                    if (cap != null) cap.enabled = false;
                }
            }
        }

        /// <summary>
        /// Update the HUD health bar text and fill width to reflect current HP.
        /// </summary>
        public void UpdateHealthBarUI()
        {
            if (healthTextElement != null)
            {
                healthTextElement.text = Mathf.CeilToInt(Mathf.Max(0f, HP)).ToString();
            }

            if (healthFillElement != null)
            {
                // Use momentum max HP so the bar scales with kill streak bonuses
                var momentum = GetComponent<MomentumManager>();
                float maxHP = momentum != null ? momentum.CurrentMaxHP : MAX_HP;
                float pct = Mathf.Clamp01(HP / maxHP) * 100f;
                healthFillElement.style.width = new StyleLength(new Length(pct, LengthUnit.Percent));
            }
        }

    }
}
