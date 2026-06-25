using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using ArtisansGuns.Game;
using Fusion;

namespace ArtisansGuns.Weapons
{
    /// <summary>
    /// FireWeapon - Handles weapon firing logic
    /// Attached to weapon prefab
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FireWeapon : MonoBehaviour
    {
        // ── Static gunfire event: origin, direction, range, shooterTeam ──
        // Bots subscribe to detect gunfire and near-miss bullets.
        public static event System.Action<Vector3, Vector3, float, int> OnShotFired;

        [Header("Fire Settings")]
        private WeaponConfig weaponConfig;
        private float fireRateInterval; // Time between shots in seconds
        private float lastFireTime;
        
        [Header("Fire Point")]
        [Tooltip("Point where bullets spawn (empty GameObject at barrel end). Optional for knife weapons.")]
        public Transform firePoint;
        
        [Header("Ammo State")]
        private int currentAmmo;
        private int maxAmmo;
        
        [Header("Components")]
        private AudioSource audioSource;
        private WeaponRecoil weaponRecoil;
        private Animator weaponAnimator;
        private Camera playerCamera;
        private Camera fpvCamera;  // FPV Overlay camera (layer 6) — used for trail FPV-sync
        
        [Header("State")]
        private bool isFiring = false;
        private bool isReloading = false;
        private bool isWeaponReady = false; // Set to true when equip animation completes
        private bool hasPlayedEmptySound = false; // Flag to prevent multiple empty sounds when holding fire
        private float _knifeAttackCooldown = 0.15f; // Minimum seconds between knife attacks
        private float _lastKnifeAttackTime = -10f;  // Last knife attack timestamp
        
        // TPV sync: cached reference to notify shot/reload events
        private ArtisansGuns.Game.PlayerController playerControllerRef;
        // Dead-check: blocks firing on the dead player's own machine (server-authoritative guard)
        private ArtisansGuns.Game.PlayerHealth playerHealthRef;

        /// <summary>Layer mask for weapon raycasts — only Default (0) + Enemy (9).</summary>
        private LayerMask hitLayerMask;
        
        // VFX pre-warm: ensures VFX Graph shaders are compiled on this GPU
        private bool vfxPrewarmed = false;
        private GameObject activePreWarmInstance; // Cleanup reference if destroyed mid-coroutine
        
        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            weaponRecoil = GetComponent<WeaponRecoil>();
            weaponAnimator = GetComponent<Animator>(); // Animator on weapon prefab
        }
        
        private void Start()
        {
            EnsureCoreReferences();
            
            bool isBotWeapon = playerControllerRef != null && playerControllerRef.IsBotControlled;

            // Only pre-warm VFX in game scenes (not LobbyScene), and skip for bots.
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != "LobbyScene" && !isBotWeapon)
            {
                StartCoroutine(PreWarmMuzzleFlashVFX());
            }
            else
            {
                Debug.Log("[FireWeapon] Skipping VFX pre-warm in LobbyScene");
            }
        }

        /// <summary>
        /// Caches essential references (PlayerController, hitLayerMask, cameras).
        /// Safe to call multiple times — skips if already initialized.
        /// Called from both Start() and Initialize() to handle inactive-GO edge cases.
        /// </summary>
        private void EnsureCoreReferences()
        {
            // Already initialized — skip
            if (playerControllerRef != null) return;

            // Ensure Awake-level components are present
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (weaponRecoil == null) weaponRecoil = GetComponent<WeaponRecoil>();
            if (weaponAnimator == null) weaponAnimator = GetComponent<Animator>();

            // Cache PlayerController for TPV animation sync
            playerControllerRef = GetComponentInParent<ArtisansGuns.Game.PlayerController>();
            // Cache PlayerHealth for dead-guard
            playerHealthRef = GetComponentInParent<ArtisansGuns.Game.PlayerHealth>();

            // Find PlayerCamera for center-screen raycasting
            GameObject cameraObj = GameObject.Find("PlayerCamera");
            if (cameraObj != null)
                playerCamera = cameraObj.GetComponent<Camera>();

            // Find FPV Overlay camera (culls only layer 6 — same stack as PlayerCamera)
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam != playerCamera && cam.cullingMask == (1 << 6))
                {
                    fpvCamera = cam;
                    break;
                }
            }

            // Build hit layer mask: Default + Enemy + Water (wave shield)
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            int waterLayer = LayerMask.NameToLayer("Water");
            bool isBotWeapon = playerControllerRef != null && playerControllerRef.IsBotControlled;
            hitLayerMask = (1 << 0); // Default
            if (enemyLayer >= 0) hitLayerMask |= (1 << enemyLayer);
            if (waterLayer >= 0) hitLayerMask |= (1 << waterLayer);

            // Bots need to hit Teammate + Player layers in addition to Enemy:
            //  - Teammate: layers are from the HOST's perspective, so a bot on the
            //    opposite team hits targets on the "Teammate" layer.
            //  - Player: the HOST's own body is on the "Player" layer.
            // ApplyDamageToHit performs a team + self check to prevent friendly fire.
            if (isBotWeapon)
            {
                int teammateLayer = LayerMask.NameToLayer("Teammate");
                if (teammateLayer >= 0) hitLayerMask |= (1 << teammateLayer);
                int playerLayer = LayerMask.NameToLayer("Player");
                if (playerLayer >= 0) hitLayerMask |= (1 << playerLayer);
            }
        }
        
        /// <summary>
        /// Pre-warm VFX Graph by instantiating a full-scale muzzle flash for several frames.
        /// At 0.001 scale particles are frustum-culled and shaders don't actually compile.
        /// Full scale + real VFX.Play() forces GPU to compile the VFX shader variants.
        /// </summary>
        private System.Collections.IEnumerator PreWarmMuzzleFlashVFX()
        {
            // Wait a frame for Initialize() to set weaponConfig
            yield return null;
            
            if (weaponConfig == null || weaponConfig.muzzleFlashPrefab == null || firePoint == null)
            {
                Debug.Log("[FireWeapon] VFX pre-warm skipped (no prefab or firePoint)");
                yield break;
            }
            
            // Instantiate OFF-SCREEN (far position) so GPU compiles shaders but it's invisible to player.
            // We position it far away (outside frustum) rather than scaling to 0 to ensure GPU processes VFX properly.
            activePreWarmInstance = Instantiate(
                weaponConfig.muzzleFlashPrefab,
                firePoint.position + Vector3.up * 99999f, // Position off-screen (won't be rendered)
                firePoint.rotation,
                firePoint
            );
            activePreWarmInstance.transform.localPosition = Vector3.up * 99999f; // Keep it far away even if parent moves
            activePreWarmInstance.transform.localRotation = Quaternion.identity;
            activePreWarmInstance.transform.localScale = Vector3.one; // Full scale needed for GPU shader compilation
            
            // Hide the pre-warm instance by disabling all renderers (GPU still compiles, just invisible to player)
            foreach (Renderer renderer in activePreWarmInstance.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            
            // Set FPV layer so Overlay camera processes the VFX draw call
            activePreWarmInstance.layer = 6; // FPV
            foreach (Transform child in activePreWarmInstance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 6;
            
            // Explicitly play the VFX to force shader compilation on this GPU.
            // NOTE: Do NOT call Reinit() here — the VFX auto-plays via OnPlay on Awake.
            // Reinit() cancels that auto-play and can leave the system in alive=0 state.
            var vfx = activePreWarmInstance.GetComponentInChildren<VisualEffect>();
            if (vfx != null)
            {
                vfx.Play();
                vfx.Simulate(0.016f); // Force first simulation tick so GPU compiles shaders
            }
            
            // Wait several frames for VFX Graph to fully compile and render at least once
            for (int i = 0; i < 6; i++)
                yield return null;
            
            if (activePreWarmInstance != null)
                Destroy(activePreWarmInstance);
            activePreWarmInstance = null;
            vfxPrewarmed = true;
            Debug.Log($"[FireWeapon] VFX pre-warm complete (vfx found: {vfx != null})");
        }
        
        /// <summary>
        /// Initialize weapon with config data
        /// </summary>
        public void Initialize(WeaponConfig config)
        {
            weaponConfig = config;

            // Ensure essential references are cached even if Start() hasn't run yet
            // (happens when the GO was inactive at instantiation time).
            EnsureCoreReferences();
            
            // Reset weapon ready state (will be set to true by Animation Event)
            isWeaponReady = false;
            
            if (weaponConfig == null)
            {
                // Debug.LogError("âŒ [FireWeapon] WeaponConfig is null!");
                return;
            }
            
            // Calculate fire rate interval (RPM to seconds)
            fireRateInterval = 60f / weaponConfig.fireRate;
            
            // Initialize ammo from config
            if (weaponConfig.isKnife)
            {
                // Knife has infinite ammo (no reload needed)
                maxAmmo = 999;
                currentAmmo = 999;
                // Debug.Log("âœ… [FireWeapon] Knife initialized with infinite ammo");
            }
            else
            {
                // Normal weapon ammo
                maxAmmo = weaponConfig.maxAmmo;
                currentAmmo = maxAmmo;
            }
            
            NotifyAmmoChanged(); // Notify UI on initialization
            
            // Setup audio
            if (audioSource != null && weaponConfig.fireSound != null)
            {
                audioSource.clip = weaponConfig.fireSound;
            }
            
            // Initialize recoil component
            if (weaponRecoil != null)
            {
                weaponRecoil.Initialize(weaponConfig);
            }
            
            // Debug.Log($"ðŸ”« [FireWeapon] Initialized: {weaponConfig.weaponName}");
            // Debug.Log($"   Fire Rate: {weaponConfig.fireRate} RPM, Interval: {fireRateInterval:F3}s");
            // Debug.Log($"   Ammo: {currentAmmo}/{maxAmmo}, Range: {weaponConfig.bulletRange}m");
            // Debug.Log($"   Weapon animator: {(weaponAnimator != null ? "Connected" : "Missing")}");
            
            // Bots don't use UI events — BotBrain calls Fire/StartFiring/StopFiring/StartReload directly
            bool isBot = playerControllerRef != null && playerControllerRef.IsBotControlled;
            
            if (!isBot)
            {
                // Subscribe to UIToolkit mobile controls events
                if (weaponConfig.isAutomatic)
                {
                    ArtisansGuns.UI.MobileControlsController.OnFireDown += StartFiring;
                    ArtisansGuns.UI.MobileControlsController.OnFireUp   += StopFiring;
                }
                else
                {
                    ArtisansGuns.UI.MobileControlsController.OnFireDown += OnFireButtonPressed;
                }
                ArtisansGuns.UI.MobileControlsController.OnReload += StartReload;
            }
            
            // Show appropriate UI based on weapon type
            if (weaponConfig.isKnife)
            {
                // Knives are ready immediately (no equip animation)
                isWeaponReady = true;
                if (!isBot) ShowKnifeUI();
            }
            else
            {
                if (!isBot) ShowGunUI();
                // Safety: if Animation Event OnWeaponReady never fires, force ready after timeout
                if (!isBot) StartCoroutine(WeaponReadyFallback());
            }
        }
        
        /// <summary>
        /// Fallback: if the equip animation event never fires OnWeaponReady(),
        /// force the weapon to be ready after a short timeout.
        /// </summary>
        private System.Collections.IEnumerator WeaponReadyFallback()
        {
            yield return new WaitForSeconds(1.5f);
            if (!isWeaponReady)
            {
                Debug.LogWarning($"[FireWeapon] OnWeaponReady animation event did NOT fire after 1.5s - forcing ready for '{weaponConfig?.weaponName}'");
                isWeaponReady = true;
            }
        }

        /// <summary>
        /// Force weapon to be ready immediately (used by bots that skip equip animations).
        /// </summary>
        public void ForceReady()
        {
            isWeaponReady = true;
        }
        
        /// <summary>
        /// Show knife UI: hide reload button (knife has no reload).
        /// </summary>
        public void ShowKnifeUI()
        {
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl != null) ctrl.SetReloadButtonVisible(false);
        }
        
        /// <summary>
        /// Show gun UI: show reload button.
        /// </summary>
        public void ShowGunUI()
        {
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl != null) ctrl.SetReloadButtonVisible(true);
        }
        
        /// <summary>
        /// Reset ammo to full (called by ceremony reset before a new round).
        /// </summary>
        public void ResetAmmo()
        {
            if (weaponConfig == null) return;
            if (weaponConfig.isKnife)
            {
                maxAmmo = 999;
                currentAmmo = 999;
            }
            else
            {
                currentAmmo = weaponConfig.maxAmmo;
                maxAmmo = weaponConfig.maxAmmo;
            }
            isReloading = false;
            NotifyAmmoChanged();
        }
        
        /// <summary>
        /// Called when FireButton is pressed
        /// </summary>
        private void OnFireButtonPressed()
        {
            // Competitive guard: dead players cannot fire — checked locally AND ignored by
            // the network (RPC_TakeDamage requires InputAuthority on a live player).
            if (playerHealthRef != null && playerHealthRef.IsDead) return;

            // Ceremony freeze: cannot fire during countdown
            if (ArtisansGuns.Game.PlayerController.InputFrozen) return;
            
            // Knife uses Attack animation trigger instead of immediate fire
            if (weaponConfig != null && weaponConfig.isKnife)
            {
                if (weaponAnimator != null && isWeaponReady && !isReloading
                    && Time.time - _lastKnifeAttackTime >= _knifeAttackCooldown)
                {
                    _lastKnifeAttackTime = Time.time;
                    weaponAnimator.SetTrigger("Attack");
                    // Notify TPV for knife attack sync
                    if (playerControllerRef != null)
                    {
                        playerControllerRef.NotifyTPVShot();
                    }
                    // Debug.Log("🔪 [FireWeapon] Knife attack animation triggered");
                }
                return;
            }
            
            // Reset empty sound flag for semi-automatic weapons
            // This allows the empty sound to play on every click when out of ammo
            hasPlayedEmptySound = false;
            
            Fire();
        }
        
        /// <summary>
        /// Fire the weapon (single shot)
        /// </summary>
        public void Fire()
        {
            // Cannot fire if weapon is not ready (during equip animation)
            if (!isWeaponReady)
            {
                return;
            }
            
            // Cannot fire while reloading
            if (isReloading)
            {
                return;
            }
            
            // Check ammo
            if (currentAmmo <= 0)
            {
                // Debug.LogWarning("âš ï¸ [FireWeapon] Out of ammo! Reload needed.");
                
                // Play empty sound only once per trigger pull (not every frame for automatic weapons)
                if (!hasPlayedEmptySound && weaponConfig.emptyMagazineSound != null)
                {
                    audioSource.PlayOneShot(weaponConfig.emptyMagazineSound);
                    hasPlayedEmptySound = true;
                    // Debug.Log("ðŸ”‡ [FireWeapon] Empty magazine sound played");
                }
                
                return;
            }
            
            // Check fire rate cooldown
            if (Time.time - lastFireTime < fireRateInterval)
            {
                return; // Too soon to fire again
            }
            
            // Only require firePoint for non-knife weapons
            if (!weaponConfig.isKnife && firePoint == null)
            {
                // Debug.LogWarning("âš ï¸ [FireWeapon] FirePoint not assigned!");
                return;
            }
            
            // Consume ammo (knife has infinite, but we still track for consistency)
            currentAmmo--;
            NotifyAmmoChanged();
            // Debug.Log($"ðŸ’¥ [FireWeapon] Fired! Ammo: {currentAmmo}/{maxAmmo}");
            
            // Update fire time
            lastFireTime = Time.time;
            
            // Knife doesn't use fire sound/muzzle flash - only impact effects via AttackTiming()
            if (weaponConfig.isKnife)
            {
                // Knife attack - trigger handled by animation event calling AttackTiming()
                // No fire sound, no muzzle flash
                return;
            }
            
            // Play fire sound (guns only)
            // Bots skip FPV fire sound — the TPV system plays a 3D spatial sound instead
            bool isBotFiring = playerControllerRef != null && playerControllerRef.IsBotControlled;
            if (!isBotFiring && audioSource != null && weaponConfig.fireSound != null)
            {
                audioSource.PlayOneShot(weaponConfig.fireSound);
            }
            
            // Spawn muzzle flash at firePoint (guns only)
            // Bots skip FPV muzzle flash (layer 6, invisible to everyone) — the TPV
            // system spawns its own muzzle flash at the TPV weapon's fire point.
            if (!isBotFiring)
            {
                if (weaponConfig.muzzleFlashPrefab == null)
                {
                    Debug.LogWarning($"[FireWeapon] No muzzleFlashPrefab on WeaponConfig '{weaponConfig.weaponName}'");
                }
                else if (firePoint == null)
                {
                    Debug.LogWarning($"[FireWeapon] firePoint is null on '{gameObject.name}'");
                }
                else
                {
                    GameObject muzzleFlash = Instantiate(
                        weaponConfig.muzzleFlashPrefab,
                        firePoint.position,
                        firePoint.rotation,
                        firePoint // Parent to firePoint
                    );
                    
                    // Set local properties to zero (maintain prefab's configured offset)
                    muzzleFlash.transform.localPosition = Vector3.zero;
                    muzzleFlash.transform.localRotation = Quaternion.identity;
                    muzzleFlash.transform.localScale = Vector3.one;
                    
                    // Muzzle flash must be on FPV layer (6) so FPVCamera (Overlay, mask=64) renders it.
                    muzzleFlash.layer = 6; // FPV layer - rendered by FPVCamera Overlay
                    foreach (Transform child in muzzleFlash.GetComponentsInChildren<Transform>(true))
                        child.gameObject.layer = 6;
                    
                    // Explicitly play VFX Graph to ensure it renders immediately.
                    var vfx = muzzleFlash.GetComponentInChildren<VisualEffect>();
                    if (vfx != null)
                    {
                        vfx.Play();
                        vfx.Simulate(Time.deltaTime > 0 ? Time.deltaTime : 0.016f);
                    }
                    
                    // Auto-destroy after duration
                    Destroy(muzzleFlash, weaponConfig.muzzleFlashDuration);
                }
            }
            
            // Apply recoil
            if (weaponRecoil != null)
            {
                weaponRecoil.ApplyRecoil();
            }
            
            // Log bot fire for diagnostics
            if (isBotFiring)
                Debug.LogWarning($"[BOT-FIRE] Bot fired! ammo={currentAmmo}/{maxAmmo} ready={isWeaponReady} firePoint={(firePoint != null ? firePoint.position.ToString() : "NULL")}");

            // TODO: Spawn bullet/projectile or raycast hit detection
            // NotifyTPVShot is called inside PerformRaycastShot once the impact point is known
            PerformRaycastShot();

            // Broadcast gunfire event so bots can hear/detect near-misses
            {
                Vector3 shotOrigin = firePoint != null ? firePoint.position : transform.position;
                Vector3 shotDir;
                if (isBotFiring)
                    shotDir = Quaternion.Euler(playerControllerRef.CurrentPitch, playerControllerRef.CurrentYaw, 0f) * Vector3.forward;
                else if (playerCamera != null)
                    shotDir = playerCamera.transform.forward;
                else
                    shotDir = (firePoint != null ? firePoint.forward : transform.forward);
                int shooterTeam = -1;
                var nd = playerControllerRef?.GetComponent<ArtisansGuns.Networking.PlayerNetworkData>();
                if (nd != null) shooterTeam = nd.Team;
                OnShotFired?.Invoke(shotOrigin, shotDir, weaponConfig.bulletRange, shooterTeam);
            }
        }

        /// <summary>
        /// Applies random bullet spread when the player is moving or airborne.
        /// Airborne spread takes priority (not summed with movement spread).
        /// Returns the original direction unmodified when the player is idle on the ground.
        /// </summary>
        private Vector3 ApplyBulletSpread(Vector3 direction)
        {
            if (weaponConfig == null) return direction;

            float spreadAngle = 0f;

            bool isGrounded = playerControllerRef != null && playerControllerRef.IsGrounded;
            bool isMoving   = playerControllerRef != null && playerControllerRef.IsMoving();

            if (!isGrounded)
            {
                // Airborne spread (priority)
                var pattern = weaponConfig.recoilPattern;
                spreadAngle = pattern != null ? pattern.jumpSpreadAngle : weaponConfig.jumpSpreadAngle;
            }
            else if (isMoving)
            {
                // Ground movement spread
                var pattern = weaponConfig.recoilPattern;
                spreadAngle = pattern != null ? pattern.movementSpreadAngle : weaponConfig.movementSpreadAngle;
            }

            if (spreadAngle <= 0f) return direction;

            // Random point inside a cone of spreadAngle degrees
            float halfRad = spreadAngle * 0.5f * Mathf.Deg2Rad;
            Vector2 rnd = Random.insideUnitCircle * Mathf.Tan(halfRad);
            Quaternion rot = Quaternion.LookRotation(direction);
            Vector3 spread = rot * new Vector3(rnd.x, rnd.y, 1f);
            return spread.normalized;
        }
        
        /// <summary>
        /// Perform raycast shot (placeholder for actual shooting logic)
        /// Spawns bullet trail from firePoint to impact
        /// Raycast from center screen (camera) for accurate feedback
        /// </summary>
        private void PerformRaycastShot()
        {
            if (firePoint == null) return;
            
            // Shotgun: fire multiple pellets, each with independent spread & hit detection
            if (weaponConfig.isShotgun && weaponConfig.pelletCount > 1)
            {
                PerformShotgunShot();
                return;
            }
            
            RaycastHit hit;
            float range = weaponConfig.bulletRange;
            
            Vector3 endPoint;
            Vector3 rayOrigin;
            Vector3 rayDirection;
            
            // Raycast from camera center (center screen) for player accuracy
            if (playerControllerRef != null && playerControllerRef.IsBotControlled)
            {
                // Bot: aim from eye height using controller's yaw/pitch
                rayOrigin = playerControllerRef.transform.position + Vector3.up * 1.5f;
                rayDirection = Quaternion.Euler(playerControllerRef.CurrentPitch, playerControllerRef.CurrentYaw, 0f) * Vector3.forward;
            }
            else if (playerCamera != null)
            {
                rayOrigin = playerCamera.transform.position;
                rayDirection = playerCamera.transform.forward;
            }
            else
            {
                // Fallback to firePoint
                rayOrigin = firePoint.position;
                rayDirection = firePoint.forward;
            }
            
            // Apply bullet spread when moving or airborne
            rayDirection = ApplyBulletSpread(rayDirection);

            // Cast ray from camera center forward (layer-masked: Default + Enemy)
            if (Physics.Raycast(rayOrigin, rayDirection, out hit, range, hitLayerMask))
            {
                // Debug.Log($"ðŸŽ¯ [FireWeapon] Hit: {hit.collider.name} at distance {hit.distance:F2}m");
                
                endPoint = hit.point;

                // Spawn synchronized impact effect on all clients — skip enemy hits (blood handles those)
                int _enemyLayer = LayerMask.NameToLayer("Enemy");
                bool _hitEnemy  = _enemyLayer >= 0 && hit.collider.gameObject.layer == _enemyLayer;
                if (!_hitEnemy && playerControllerRef != null)
                    playerControllerRef.RPC_SpawnBulletImpact(hit.point, hit.normal, hit.collider.tag);

                // Apply damage to enemy player
                ApplyDamageToHit(hit);
                
                // Visualize hit point (debug)
                Debug.DrawLine(rayOrigin, hit.point, Color.red, 0.5f);
                
                // Spawn bullet trail
                SpawnBulletTrail(hit.point);
            }
            else
            {
                // No hit, draw full range
                endPoint = rayOrigin + rayDirection * range;
                Debug.DrawRay(rayOrigin, rayDirection * range, Color.yellow, 0.5f);
                
                // Spawn bullet trail to max range
                SpawnBulletTrail(endPoint);
            }

            // Notify TPV with the known impact point (after raycast so endPoint is set)
            if (playerControllerRef != null)
                playerControllerRef.NotifyTPVShot(endPoint);
        }

        /// <summary>
        /// Fires multiple pellets in a cone. Aggregates total damage per victim
        /// so only ONE DealDamage call (= one RPC) is sent per enemy hit,
        /// eliminating the delay caused by 9 individual RPCs.
        /// Each pellet still checks headshot independently.
        /// </summary>
        private void PerformShotgunShot()
        {
            float range = weaponConfig.bulletRange;
            int pellets = weaponConfig.pelletCount;
            float damagePerPellet = weaponConfig.damage / pellets;
            float spreadAngle = weaponConfig.shotgunSpreadAngle;

            Vector3 rayOrigin;
            Vector3 baseDirection;

            if (playerControllerRef != null && playerControllerRef.IsBotControlled)
            {
                rayOrigin = playerControllerRef.transform.position + Vector3.up * 1.5f;
                baseDirection = Quaternion.Euler(playerControllerRef.CurrentPitch, playerControllerRef.CurrentYaw, 0f) * Vector3.forward;
            }
            else if (playerCamera != null)
            {
                rayOrigin = playerCamera.transform.position;
                baseDirection = playerCamera.transform.forward;
            }
            else
            {
                rayOrigin = firePoint.position;
                baseDirection = firePoint.forward;
            }

            // Apply movement/airborne spread to the base direction first
            baseDirection = ApplyBulletSpread(baseDirection);

            Vector3 lastEndPoint = rayOrigin + baseDirection * range;
            int enemyLayer = LayerMask.NameToLayer("Enemy");

            // Aggregate damage per victim: (totalDamage, anyHeadshot, lastHitPoint)
            var victimDamage = new System.Collections.Generic.Dictionary<PlayerHealth, (float total, bool headshot, Vector3 hitPoint)>();

            for (int i = 0; i < pellets; i++)
            {
                // Random direction within shotgun cone
                float halfRad = spreadAngle * 0.5f * Mathf.Deg2Rad;
                Vector2 rnd = Random.insideUnitCircle * Mathf.Tan(halfRad);
                Quaternion rot = Quaternion.LookRotation(baseDirection);
                Vector3 pelletDir = (rot * new Vector3(rnd.x, rnd.y, 1f)).normalized;

                Vector3 endPoint;

                if (Physics.Raycast(rayOrigin, pelletDir, out RaycastHit hit, range, hitLayerMask))
                {
                    endPoint = hit.point;

                    bool hitEnemy = enemyLayer >= 0 && hit.collider.gameObject.layer == enemyLayer;

                    // Impact effect on non-enemy surfaces
                    if (!hitEnemy && playerControllerRef != null)
                        playerControllerRef.RPC_SpawnBulletImpact(hit.point, hit.normal, hit.collider.tag);

                    // Accumulate damage for enemy victims
                    if (hitEnemy)
                    {
                        PlayerHealth vh = hit.collider.GetComponentInParent<PlayerHealth>();
                        if (vh != null && !vh.IsDead && !vh.PredictedDead && !vh.IsImmune)
                        {
                            bool isHeadshot = hit.collider.CompareTag("Head");
                            float pelletFinal = isHeadshot ? damagePerPellet * weaponConfig.headshotMultiplier : damagePerPellet;

                            if (victimDamage.TryGetValue(vh, out var existing))
                                victimDamage[vh] = (existing.total + pelletFinal, existing.headshot || isHeadshot, hit.point);
                            else
                                victimDamage[vh] = (pelletFinal, isHeadshot, hit.point);

                            // Per-pellet blood + hitmarker for immediate feedback
                            if (playerControllerRef != null)
                            {
                                PlayerRef victimRef = vh.Object.InputAuthority;
                                playerControllerRef.RPC_SpawnBloodEffect(hit.point, isHeadshot, victimRef);
                            }
                            // Hitmarker only for local player weapons (not bots)
                            if (playerControllerRef == null || !playerControllerRef.IsBotControlled)
                                ArtisansGuns.UI.CrosshairManager.Instance?.ShowHitMarker(isHeadshot);
                        }
                    }

                    Debug.DrawLine(rayOrigin, hit.point, Color.red, 0.5f);
                }
                else
                {
                    endPoint = rayOrigin + pelletDir * range;
                    Debug.DrawRay(rayOrigin, pelletDir * range, Color.yellow, 0.5f);
                }

                SpawnBulletTrail(endPoint);
                lastEndPoint = endPoint;
            }

            // Apply aggregated damage — one DealDamage (one RPC) per victim
            PlayerRef shooterRef = default;
            Fusion.NetworkObject botObj = null;
            if (playerControllerRef != null && playerControllerRef.Object != null)
            {
                shooterRef = playerControllerRef.Object.InputAuthority;
                if (playerControllerRef.IsBotControlled)
                    botObj = playerControllerRef.Object;
            }

            foreach (var kvp in victimDamage)
            {
                PlayerHealth victim = kvp.Key;
                float totalDamage = kvp.Value.total;
                bool anyHeadshot = kvp.Value.headshot;

                // Pass totalDamage as raw damage with headshotMultiplier=1 since
                // we already computed per-pellet headshot multipliers above.
                PlayerHealth.DealDamage(victim, totalDamage, anyHeadshot, 1f, shooterRef, weaponConfig.weaponId, botObj);
            }

            // Notify TPV with the last pellet's end point
            if (playerControllerRef != null)
                playerControllerRef.NotifyTPVShot(lastEndPoint);
        }
        
        /// <summary>
        /// Start continuous firing (hold button)
        /// </summary>
        public void StartFiring()
        {
            // Dead guard for automatic weapons (hold-to-fire)
            if (playerHealthRef != null && playerHealthRef.IsDead) return;
            isFiring = true;
        }
        
        /// <summary>
        /// Stop continuous firing
        /// </summary>
        public void StopFiring()
        {
            isFiring = false;
            
            // Reset empty sound flag when button is released (allows sound on next trigger pull)
            hasPlayedEmptySound = false;
        }
        
        private void Update()
        {
            // Handle continuous fire if button is held
            if (isFiring)
            {
                Fire();
            }
        }
        
        /// <summary>
        /// Start reload sequence
        /// Triggers Reload animation and restores ammo after duration
        /// </summary>
        public void StartReload()
        {
            // Knife weapons don't need reload (infinite ammo)
            if (weaponConfig != null && weaponConfig.isKnife)
            {
                // Debug.Log("[FireWeapon] Knife weapons don't reload");
                return;
            }
            
            // Cannot reload if weapon is not ready (during equip animation)
            if (!isWeaponReady)
            {
                return;
            }
            
            if (isReloading)
            {
                // Debug.LogWarning("âš ï¸ [FireWeapon] Already reloading!");
                return;
            }
            
            // Check if already full ammo
            if (currentAmmo >= maxAmmo)
            {
                // Debug.LogWarning("âš ï¸ [FireWeapon] Ammo already full!");
                return;
            }
            
            isReloading = true;
            
            // Reset empty sound flag (allows sound to play again after reload)
            hasPlayedEmptySound = false;
            
            // Reset recoil pattern so next burst starts from step 0
            if (weaponRecoil != null)
            {
                weaponRecoil.ResetPattern();
            }
            
            // Bots skip the FPV animator (no AnimatorController assigned) and
            // complete reload after the configured duration via Invoke.
            bool isBotReload = playerControllerRef != null && playerControllerRef.IsBotControlled;
            if (isBotReload)
            {
                float reloadSec = 2.5f;
                Invoke(nameof(OnReloadComplete), reloadSec);
            }
            else
            {
                // Trigger weapon animator (Reload trigger)
                if (weaponAnimator != null)
                {
                    weaponAnimator.SetTrigger("Reload");
                }
            }
            
            // Notify TPV weapon animation (syncs ReloadTPV trigger to remote players)
            if (playerControllerRef != null)
            {
                playerControllerRef.NotifyTPVReload();
            }
            
            // NOTE: Reload will complete when OnReloadComplete() is called from Animation Event
            // Add Animation Event to reload animation at the exact frame where reload should happen
            // Debug.Log($"[FireWeapon] Reload started: {currentAmmo}/{maxAmmo} - waiting for animation event");
        }
        
        /// <summary>
        /// Check if currently reloading
        /// </summary>
        public bool IsReloading()
        {
            return isReloading;
        }
        
        /// <summary>
        /// Called from Animation Event at the exact moment reload should complete
        /// Add this method as an Animation Event in the reload animation clip
        /// </summary>
        public void OnReloadComplete()
        {
            if (!isReloading)
            {
                // Debug.LogWarning("[FireWeapon] OnReloadComplete called but not reloading");
                return;
            }
            
            currentAmmo = maxAmmo;
            isReloading = false;
            NotifyAmmoChanged();
            
            // Debug.Log($"[FireWeapon] Reload complete! Ammo: {currentAmmo}/{maxAmmo}");
        }
        
        /// <summary>
        /// Called from Animation Event during knife attack animation at the exact moment of impact
        /// Performs raycast to detect hit and apply damage
        /// Used for knife/melee weapons only
        /// </summary>
        public void AttackTiming()
        {
            if (!weaponConfig.isKnife)
            {
                // Debug.LogWarning("[FireWeapon] AttackTiming called on non-knife weapon!");
                return;
            }
            
            // Debug.Log("ðŸ"ª [FireWeapon] AttackTiming - performing knife attack raycast");
            
            // Perform raycast from camera center (player's aim point)
            RaycastHit hit;
            float range = weaponConfig.bulletRange; // Use weapon range for knife reach
            
            Vector3 rayOrigin;
            Vector3 rayDirection;
            
            // Raycast from camera center for accurate aim
            if (playerCamera != null)
            {
                rayOrigin = playerCamera.transform.position;
                rayDirection = playerCamera.transform.forward;
            }
            else
            {
                // Fallback to weapon transform
                rayOrigin = transform.position;
                rayDirection = transform.forward;
            }
            
            // Cast ray from camera center forward (layer-masked)
            if (Physics.Raycast(rayOrigin, rayDirection, out hit, range, hitLayerMask))
            {
                // Spawn synchronized bullet impact ONLY on non-enemy surfaces
                int _knifeEnemyLayer = LayerMask.NameToLayer("Enemy");
                bool _knifeHitEnemy = (_knifeEnemyLayer >= 0 && hit.collider.gameObject.layer == _knifeEnemyLayer);
                if (!_knifeHitEnemy && playerControllerRef != null)
                    playerControllerRef.RPC_SpawnBulletImpact(hit.point, hit.normal, hit.collider.tag);

                // ── Apply damage to enemy player (blood RPC is inside) ──
                ApplyDamageToHit(hit);
                
                // Visualize hit point (debug)
                Debug.DrawLine(rayOrigin, hit.point, Color.red, 0.5f);
            }
            else
            {
                // Miss - no hit within range
                Debug.DrawRay(rayOrigin, rayDirection * range, Color.yellow, 0.5f);
                // Debug.Log("âš« [FireWeapon] Knife attack missed");
            }
        }
        
        // ────────────────────────────────────────────────────────────────
        // Damage application (shared by gun + knife raycasts)
        // ────────────────────────────────────────────────────────────────
        
        /// <summary>
        /// Given a raycast hit, find the PlayerHealth on the enemy and deal damage.
        /// Checks tag ("Head" vs "Body") for headshot multiplier.
        /// </summary>
        private void ApplyDamageToHit(RaycastHit hit)
        {
            // Only process colliders on player-character layers.
            // Human weapons hit Enemy only.  Bot weapons also hit Teammate + Player
            // because layers are assigned from the HOST's viewpoint.
            int enemyLayer    = LayerMask.NameToLayer("Enemy");
            int teammateLayer = LayerMask.NameToLayer("Teammate");
            int playerLayer   = LayerMask.NameToLayer("Player");
            int hitLayer      = hit.collider.gameObject.layer;

            bool isEnemyLayer    = enemyLayer >= 0 && hitLayer == enemyLayer;
            bool isTeammateLayer = teammateLayer >= 0 && hitLayer == teammateLayer;
            bool isPlayerLayer   = playerLayer >= 0 && hitLayer == playerLayer;
            if (!isEnemyLayer && !isTeammateLayer && !isPlayerLayer) return;

            // ── Self-damage guard: never let a shooter damage itself ────
            PlayerHealth victimHealth = hit.collider.GetComponentInParent<PlayerHealth>();
            if (victimHealth != null && victimHealth == playerHealthRef) return;

            // For bots hitting Teammate or Player layer: verify the target is
            // on a DIFFERENT team (layers are from the host's perspective).
            if (isTeammateLayer || isPlayerLayer)
            {
                bool isBotShooter = playerControllerRef != null && playerControllerRef.IsBotControlled;
                if (!isBotShooter) return; // human weapons should never damage teammates

                var shooterData = playerControllerRef.GetComponent<ArtisansGuns.Networking.PlayerNetworkData>();
                var victimData  = hit.collider.GetComponentInParent<ArtisansGuns.Networking.PlayerNetworkData>();
                if (shooterData != null && victimData != null && shooterData.Team == victimData.Team)
                    return; // same team — no friendly fire
            }
            
            // Validate victim (reuse early lookup)
            if (victimHealth == null) return;
            if (victimHealth.IsDead || victimHealth.PredictedDead) return;
            if (victimHealth.IsImmune) return;   // immune players: no damage, no blood, no prediction
            
            // Headshot check via tag
            bool isHeadshot = hit.collider.CompareTag("Head");
            
            // Get local player ref for kill credit
            PlayerRef shooterRef = default;
            Fusion.NetworkObject botShooterObj = null;
            if (playerControllerRef != null && playerControllerRef.Object != null)
            {
                shooterRef = playerControllerRef.Object.InputAuthority;
                // For bots, InputAuthority is None — pass the actual NetworkObject for kill credit
                if (playerControllerRef.IsBotControlled)
                    botShooterObj = playerControllerRef.Object;
            }
            
            // Deal damage through PlayerHealth static API
            PlayerHealth.DealDamage(
                victimHealth,
                weaponConfig.damage,
                isHeadshot,
                weaponConfig.headshotMultiplier,
                shooterRef,
                weaponConfig.weaponId,
                botShooterObj
            );

            // Spawn synchronized blood effect on the victim's TPV (invisible to victim)
            // Blood RPC lives on the SHOOTER's PlayerController so it can read the weapon config
            if (playerControllerRef != null)
            {
                PlayerRef victimRef = victimHealth.Object.InputAuthority;
                playerControllerRef.RPC_SpawnBloodEffect(hit.point, isHeadshot, victimRef);
            }

            // Hit-marker feedback (local shooter only — bots must NOT trigger the local player's hitmarker)
            if (playerControllerRef == null || !playerControllerRef.IsBotControlled)
                ArtisansGuns.UI.CrosshairManager.Instance?.ShowHitMarker(isHeadshot);

            Debug.Log($"[FireWeapon] Hit {hit.collider.name} (headshot={isHeadshot}) for {weaponConfig.damage}{(isHeadshot ? " x" + weaponConfig.headshotMultiplier : "")} damage");
        }
        
        /// <summary>
        /// Cancel reload (called when weapon is switched or deactivated)
        /// </summary>
        public void CancelReload()
        {
            if (isReloading)
            {
                isReloading = false;
                CancelInvoke(nameof(OnReloadComplete));
                // Debug.Log($"[FireWeapon] Reload cancelled");
            }
        }
        
        /// <summary>
        /// Called from Animation Event when equip animation completes
        /// Add this method as an Animation Event in the StartBolt/StartTalonAR animation
        /// </summary>
        public void OnWeaponReady()
        {
            isWeaponReady = true;
            // Debug.Log($"[FireWeapon] Weapon ready to fire!");
        }
        
        /// <summary>
        /// Check if weapon is ready to fire (equip animation completed)
        /// </summary>
        public bool IsReady()
        {
            return isWeaponReady;
        }

        /// <summary>Returns the WeaponConfig this weapon was initialized with.</summary>
        public WeaponConfig GetWeaponConfig() => weaponConfig;
        
        /// <summary>
        /// Get current ammo count
        /// </summary>
        public int GetCurrentAmmo()
        {
            return currentAmmo;
        }
        
        /// <summary>
        /// Get max ammo capacity
        /// </summary>
        public int GetMaxAmmo()
        {
            return maxAmmo;
        }
        
        /// <summary>
        /// Check if weapon is currently firing
        /// </summary>
        public bool IsFiring()
        {
            return isFiring;
        }
        
        /// <summary>
        /// Play reload sound by index (called from Animation Events)
        /// Usage in Animation Event: Add event keyframe at desired time, call PlayReloadSound(0), PlayReloadSound(1), etc.
        /// </summary>
        /// <param name="index">Index of the sound in weaponConfig.reloadSounds array</param>
        public void PlayReloadSound(int index)
        {
            if (weaponConfig == null)
            {
                // Debug.LogWarning("âš ï¸ [FireWeapon] WeaponConfig is null, cannot play reload sound");
                return;
            }
            
            if (weaponConfig.reloadSounds == null || weaponConfig.reloadSounds.Length == 0)
            {
                // Debug.LogWarning("âš ï¸ [FireWeapon] No reload sounds configured in WeaponConfig");
                return;
            }
            
            if (index < 0 || index >= weaponConfig.reloadSounds.Length)
            {
                // Debug.LogWarning($"âš ï¸ [FireWeapon] Reload sound index {index} out of range (0-{weaponConfig.reloadSounds.Length - 1})");
                return;
            }
            
            AudioClip soundClip = weaponConfig.reloadSounds[index];
            
            if (soundClip == null)
            {
                // Debug.LogWarning($"âš ï¸ [FireWeapon] Reload sound at index {index} is null");
                return;
            }
            
            audioSource.PlayOneShot(soundClip);
            // Debug.Log($"ðŸ”Š [FireWeapon] Playing reload sound {index}: {soundClip.name}");
        }
        // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500
        // Sound methods callable from Animation Events (knife attack, etc.)
        // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

        /// <summary>
        /// Plays the FPV fire sound (weaponConfig.fireSound) on the local AudioSource.
        /// Call this from an Animation Event on knife/melee attack animations
        /// at the exact frame where the swing should be heard locally.
        /// For guns this is called automatically in FireSingleShot() \u2014
        /// no need to add animation events for guns.
        /// </summary>
        public void PlayFireSound()
        {
            if (weaponConfig == null || weaponConfig.fireSound == null || audioSource == null) return;
            audioSource.PlayOneShot(weaponConfig.fireSound);
        }

        /// <summary>
        /// Triggers the TPV fire sound so remote players hear the weapon fire.
        /// Call this from an Animation Event on knife/melee attack animations
        /// at the exact swing frame (can be the same keyframe as PlayFireSound
        /// or slightly different for the TPV timing).
        /// This increments NetworkWeaponTrigger with a special \u201csound-only\u201d bit
        /// so the remote TPVController plays the fire sound without spawning
        /// muzzle flash or trail.
        /// </summary>
        public void PlayFireSoundTPV()
        {
            if (playerControllerRef == null) return;
            playerControllerRef.NotifyTPVFireSound();
        }        
        /// <summary>
        /// Set ammo count (used when switching weapons or picking up dropped weapons)
        /// </summary>
        public void SetAmmo(int ammo)
        {
            currentAmmo = Mathf.Clamp(ammo, 0, maxAmmo);
            NotifyAmmoChanged();
            // Debug.Log($"ðŸ“ [FireWeapon] Ammo set to {currentAmmo}/{maxAmmo}");
        }
        
        /// <summary>
        /// Notify GameUIManager about ammo changes
        /// </summary>
        private void NotifyAmmoChanged()
        {
            // Bots must NOT update the local player's ammo UI
            if (playerControllerRef != null && playerControllerRef.IsBotControlled) return;

            // Update weapon cells UI in PlayerSetup (handles both primary + secondary correctly
            // based on PlayerSetup's own state, unlike GameUIManager which has stale tracking)
            var playerSetup = GetComponentInParent<ArtisansGuns.Game.PlayerSetup>();
            if (playerSetup != null)
            {
                playerSetup.UpdateWeaponCellsOnAmmoChange();
            }
        }
        
        /// <summary>
        /// Spawns a quad-based bullet trail from the weapon's firePoint to the target position.
        /// Uses BulletTrail.cs — no TrailRenderer or LineRenderer.
        /// The trail appears instantly at full length, then the muzzle-end shrinks toward impact.
        /// </summary>
        private void SpawnBulletTrail(Vector3 targetPosition)
        {
            if (firePoint == null)        { Debug.LogWarning("[Trail] BLOCKED: firePoint is null"); return; }
            // Bots skip FPV trail entirely — TPV system handles their visual trail.
            // NOTE: playerCamera != null even for bots because EnsureCoreReferences uses
            // GameObject.Find("PlayerCamera") which finds the local player's camera.
            if (playerControllerRef != null && playerControllerRef.IsBotControlled) return;
            if (playerCamera == null)     return;
            if (weaponConfig == null)     { Debug.LogWarning("[Trail] BLOCKED: weaponConfig is null"); return; }
            if (weaponConfig.bulletTrailMaterial == null) { Debug.LogWarning("[Trail] BLOCKED: bulletTrailMaterial is null on " + weaponConfig.name); return; }

            // Lerp width: trailWidthNear at distance 0, trailWidthFar at bulletRange
            float dist = Vector3.Distance(firePoint.position, targetPosition);
            float t     = Mathf.Clamp01(dist / Mathf.Max(weaponConfig.bulletRange, 0.01f));
            float width = Mathf.Lerp(weaponConfig.trailWidthNear, weaponConfig.trailWidthFar, t);

            BulletTrail.Create(
                firePoint.position,
                targetPosition,
                weaponConfig.bulletTrailMaterial,
                width,
                weaponConfig.trailShrinkSpeed,
                weaponConfig.trailFlashDuration,
                playerCamera,
                fpvCamera
            );
        }
        
        /// <summary>
        /// Switch to a different weapon configuration (gun or knife)
        /// Call this method to switch between primary, secondary, and knife
        /// </summary>
        /// <param name="newWeaponConfig">The new WeaponConfig to switch to</param>
        public void SwitchWeapon(WeaponConfig newWeaponConfig)
        {
            if (newWeaponConfig == null)
            {
                Debug.LogError("❌ [FireWeapon] Cannot switch to null WeaponConfig");
                return;
            }
            
            // Stop current weapon state
            StopFiring();
            StopAllCoroutines();
            
            // Clean up pre-warm muzzle flash instance if it exists
            if (activePreWarmInstance != null)
            {
                Destroy(activePreWarmInstance);
                activePreWarmInstance = null;
            }
            
            isReloading = false;
            isWeaponReady = true;
            
            // Update weapon config
            weaponConfig = newWeaponConfig;
            
            // Reinitialize with new config
            if (weaponConfig.isKnife)
            {
                // Knife: infinite ammo
                maxAmmo = 999;
                currentAmmo = 999;
            }
            else
            {
                // Gun: use config ammo
                maxAmmo = weaponConfig.maxAmmo;
                currentAmmo = maxAmmo;
            }
            
            // Update fire rate
            fireRateInterval = 60f / weaponConfig.fireRate;
            
            // Update audio
            if (audioSource != null && weaponConfig.fireSound != null)
            {
                audioSource.clip = weaponConfig.fireSound;
            }
            
            // Update recoil
            if (weaponRecoil != null)
            {
                weaponRecoil.Initialize(weaponConfig);
            }
            
            // Show appropriate UI
            if (weaponConfig.isKnife)
            {
                ShowKnifeUI();
            }
            else
            {
                ShowGunUI();
            }
            
            // Re-subscribe fire events with the correct mode for the new weapon
            ArtisansGuns.UI.MobileControlsController.OnFireDown -= StartFiring;
            ArtisansGuns.UI.MobileControlsController.OnFireDown -= OnFireButtonPressed;
            ArtisansGuns.UI.MobileControlsController.OnFireUp   -= StopFiring;
            if (weaponConfig.isAutomatic)
            {
                ArtisansGuns.UI.MobileControlsController.OnFireDown += StartFiring;
                ArtisansGuns.UI.MobileControlsController.OnFireUp   += StopFiring;
            }
            else
            {
                ArtisansGuns.UI.MobileControlsController.OnFireDown += OnFireButtonPressed;
            }
            
            // Notify UI of ammo change
            NotifyAmmoChanged();
            
            Debug.Log($"🔄 [FireWeapon] Switched to: {weaponConfig.weaponName} (isKnife: {weaponConfig.isKnife})");
        }
       private void OnDestroy()
        {
            // Clean up pre-warm instance if coroutine was aborted (e.g. scene change)
            if (activePreWarmInstance != null)
            {
                Destroy(activePreWarmInstance);
                activePreWarmInstance = null;
            }
            
            // Unsubscribe from UIToolkit mobile controls events
            ArtisansGuns.UI.MobileControlsController.OnFireDown -= StartFiring;
            ArtisansGuns.UI.MobileControlsController.OnFireDown -= OnFireButtonPressed;
            ArtisansGuns.UI.MobileControlsController.OnFireUp   -= StopFiring;
            ArtisansGuns.UI.MobileControlsController.OnReload   -= StartReload;
        }
    }
}
