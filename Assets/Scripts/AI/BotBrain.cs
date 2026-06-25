using UnityEngine;
using Fusion;
using ArtisansGuns.Game;
using ArtisansGuns.Networking;
using ArtisansGuns.Weapons;
using ArtisansGuns.Abilities;

namespace ArtisansGuns.AI
{
    /// <summary>
    /// Core bot AI attached to a spawned player prefab.  Drives PlayerController
    /// movement/aim and FireWeapon shooting through direct API calls — no UI or
    /// MobileControlsController involved.
    ///
    /// Runs on the HOST machine only (the host owns bot PlayerRefs).
    ///
    /// Perception:
    ///   • FOV (110°) — only detects enemies within a forward cone
    ///   • Smoke LoS — CrimsonSmoke clouds block vision
    ///   • Sound — nearby moving, non-crouching enemies are heard
    ///   • Damage — getting hit instantly reveals the attacker
    ///   • Aim-before-fire — won't shoot until facing the target
    ///   • No range cap — if visible, the bot engages at any distance
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class BotBrain : MonoBehaviour
    {
        // ═════════════════════════════════════════════════════════════════
        // PUBLIC API (set by BotManager before first frame)
        // ═════════════════════════════════════════════════════════════════
        [HideInInspector] public BotPersonality personality;
        [HideInInspector] public int team;               // 0 = A, 1 = B

        // ═════════════════════════════════════════════════════════════════
        // STATE MACHINE
        // ═════════════════════════════════════════════════════════════════
        public enum BotState
        {
            Idle,           // waiting for match to start
            Patrolling,     // roaming/pushing toward mid-map
            Engaging,       // enemy spotted — tracking + shooting
            Pursuing,       // lost sight, moving to last known position
            Retreating,     // low HP, running to cover/spawn
            Reloading,      // proactively reloading behind cover
            Dead            // waiting for respawn
        }

        public BotState CurrentState { get; private set; } = BotState.Idle;

        // ═════════════════════════════════════════════════════════════════
        // REFERENCES
        // ═════════════════════════════════════════════════════════════════
        private PlayerController _pc;
        private PlayerSetup      _setup;
        private PlayerHealth     _health;
        private PlayerNetworkData _netData;
        private Transform        _transform;

        // ═════════════════════════════════════════════════════════════════
        // SUBSYSTEMS
        // ═════════════════════════════════════════════════════════════════
        private BotNavigation _nav;

        // ═════════════════════════════════════════════════════════════════
        // TARGETING
        // ═════════════════════════════════════════════════════════════════
        private PlayerHealth  _currentTarget;
        private Transform     _currentTargetTransform;
        private Vector3       _lastKnownTargetPos;
        private float         _targetAcquiredTime;   // Time.time when first spotted
        private float         _targetLostTime;       // Time.time when lost LoS
        private bool          _hasLineOfSight;

        // ═════════════════════════════════════════════════════════════════
        // AIMING
        // ═════════════════════════════════════════════════════════════════
        private float _currentYaw;
        private float _currentPitch;
        private Vector3 _aimPoint;           // world point we're aiming at
        private Vector3 _aimOffset;          // human-like offset (jitter)
        private float   _nextJitterTime;

        // ═════════════════════════════════════════════════════════════════
        // COMBAT
        // ═════════════════════════════════════════════════════════════════
        private bool  _isFiring;
        private float _burstStartTime;
        private float _burstEndTime;
        private float _nextBurstTime;
        private bool  _wantsReload;

        // ═════════════════════════════════════════════════════════════════
        // MOVEMENT
        // ═════════════════════════════════════════════════════════════════
        private Vector2 _botMoveInput;
        private bool    _botJumpInput;
        private float   _nextStrafeChange;
        private float   _strafeDir;          // -1 or 1
        private bool    _wantsCrouch;
        private float   _nextCrouchToggle;

        // ═════════════════════════════════════════════════════════════════
        // SCAN TIMING
        // ═════════════════════════════════════════════════════════════════
        private float _nextScanTime;
        private const float SCAN_INTERVAL = 0.2f;  // seconds between enemy scans
        private float _nextDecisionTime;
        private const float DECISION_INTERVAL = 0.15f;

        // ═════════════════════════════════════════════════════════════════
        // PERCEPTION CONSTANTS
        // ═════════════════════════════════════════════════════════════════
        private const float FOV_HALF_ANGLE    = 55f;    // half of 110° cone
        private const float SOUND_HEAR_RANGE  = 18f;    // metres: footstep hearing
        private const float SOUND_HEAR_RANGE_SQ = SOUND_HEAR_RANGE * SOUND_HEAR_RANGE;
        private const float AIM_FIRE_THRESHOLD = 14f;   // degrees offset allowed to fire

        // ═════════════════════════════════════════════════════════════════
        // DAMAGE-AWARENESS
        // ═════════════════════════════════════════════════════════════════
        private float _lastHP;
        private PlayerHealth _damageAlertSource;    // who shot us
        private float        _damageAlertTime;      // when the damage arrived

        // ═════════════════════════════════════════════════════════════════
        // GUNFIRE / NEAR-MISS AWARENESS
        // ═════════════════════════════════════════════════════════════════
        private const float GUNFIRE_HEAR_RANGE    = 45f;    // metres: hear gunshots
        private const float NEAR_MISS_RADIUS      = 3f;     // metres: bullet passes within this → alert
        private Vector3 _gunfireAlertPos;                    // world pos of last heard gunfire
        private float   _gunfireAlertTime;                   // Time.time of alert
        private bool    _hasGunfireAlert;                    // pending investigation

        // ═════════════════════════════════════════════════════════════════
        // DYNAMIC PATROL
        // ═════════════════════════════════════════════════════════════════
        private int   _patrolDestinationsReached;            // count of reached patrol nodes
        private Vector3 _mapCenter = Vector3.zero;           // estimated map center

        // ═════════════════════════════════════════════════════════════════
        // HUMAN-LIKE IMPERFECTION
        // ═════════════════════════════════════════════════════════════════
        private float _aimSpeedMultiplier = 1f;   // per-engagement aim speed variance
        private float _nextGlanceTime;            // patrol: next random head turn
        private float _glanceEndTime;             // when to stop glancing
        private float _glanceYawOffset;           // temporary yaw offset for glance
        private float _microPauseEnd;             // patrol: brief stop for "checking"
        private float _nextMicroPause;            // when to next micro-pause
        private float _firstShotDelay;            // extra delay before first burst of an engagement
        private bool  _firstShotDelayUsed;        // already applied this engagement
        private float _whiffEndTime;              // momentarily lose track mid-fight

        // ═════════════════════════════════════════════════════════════════
        // FLASH BLINDNESS (Tsunami Flash ultimate)
        // ═════════════════════════════════════════════════════════════════
        private float _flashEndTime;               // Time.time when flash wears off
        private float _flashAimDriftYaw;           // random yaw drift while blinded
        private float _flashAimDriftPitch;         // random pitch drift while blinded
        private float _nextFlashDriftTime;         // when to re-roll drift direction
        private int   _flashBehavior;              // 0=panicked spray, 1=frozen, 2=erratic move

        /// <summary>True when this bot is currently flash-blinded.</summary>
        public bool IsFlashed => Time.time < _flashEndTime;

        // Layer mask for LoS raycasts (exclude triggers, include default + enemy)
        private int _losLayerMask;

        // ═════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═════════════════════════════════════════════════════════════════

        private void Awake()
        {
            _pc        = GetComponent<PlayerController>();
            _setup     = GetComponent<PlayerSetup>();
            _health    = GetComponent<PlayerHealth>();
            _netData   = GetComponent<PlayerNetworkData>();
            _transform = transform;

            _nav = new BotNavigation(1.5f);
            _losLayerMask = LayerMask.GetMask("Default");

            // Stagger scan times so all bots don't scan on the same frame
            _nextScanTime     = Time.time + Random.Range(0f, SCAN_INTERVAL);
            _nextDecisionTime = Time.time + Random.Range(0f, DECISION_INTERVAL);

            FireWeapon.OnShotFired += OnWeaponShotFired;
        }

        private void OnDestroy()
        {
            FireWeapon.OnShotFired -= OnWeaponShotFired;
        }

        /// <summary>
        /// Called for EVERY weapon shot in the game.
        /// Detects gunfire sound AND near-miss bullets.
        /// </summary>
        private void OnWeaponShotFired(Vector3 origin, Vector3 direction, float range, int shooterTeam)
        {
            if (personality == null) return;
            if (shooterTeam == team) return;       // ignore friendly fire
            if (_health != null && _health.IsDead) return;

            Vector3 myPos = _transform.position + Vector3.up * 1.0f;
            Vector3 toMe = myPos - origin;
            float dist = toMe.magnitude;

            // ── Near-miss detection: closest point on bullet ray to bot ──
            if (dist < range)
            {
                float t = Mathf.Clamp(Vector3.Dot(toMe, direction), 0f, range);
                Vector3 closestOnRay = origin + direction * t;
                float missDistance = Vector3.Distance(closestOnRay, myPos);

                if (missDistance < NEAR_MISS_RADIUS)
                {
                    // Bullet whizzed past — instant alert toward shooter position
                    _gunfireAlertPos = origin;
                    _gunfireAlertTime = Time.time;
                    _hasGunfireAlert = true;
                    return;
                }
            }

            // ── Gunfire sound detection: heard the shot ─────────────────
            if (dist < GUNFIRE_HEAR_RANGE)
            {
                _gunfireAlertPos = origin;
                _gunfireAlertTime = Time.time;
                _hasGunfireAlert = true;
            }
        }

        public void Initialize(BotPersonality p, int botTeam)
        {
            personality = p;
            team = botTeam;

            // Initialize aim to current facing
            _currentYaw   = _transform.eulerAngles.y;
            _currentPitch = 0f;

            // Patrol area = spawn area ± 30m
            _nav.SetRoamArea(_transform.position, 30f);

            // Estimate map center from spawn managers
            var mapSpawn = FindAnyObjectByType<MapSpawnManager>();
            if (mapSpawn != null)
            {
                Vector3 spawnA = mapSpawn.GetSpawnPosition(0);
                Vector3 spawnB = mapSpawn.GetSpawnPosition(1);
                _mapCenter = (spawnA + spawnB) * 0.5f;
            }
            else
            {
                _mapCenter = _transform.position;
            }

            _lastHP = PlayerHealth.MAX_HP;
            _patrolDestinationsReached = 0;
            _nextGlanceTime = Time.time + Random.Range(3f, 8f);
            _nextMicroPause = Time.time + Random.Range(6f, 15f);
        }

        // ═════════════════════════════════════════════════════════════════
        // MAIN LOOP  (runs every frame on host)
        // ═════════════════════════════════════════════════════════════════

        private void Update()
        {
            if (personality == null) return;
            if (PlayerController.InputFrozen)
            {
                _botMoveInput = Vector2.zero;
                _botJumpInput = false;
                ApplyInputToController();
                return;
            }

            // Check dead state
            if (_health != null && _health.IsDead)
            {
                CurrentState = BotState.Dead;
                _botMoveInput = Vector2.zero;
                _isFiring = false;
                ApplyInputToController();
                return;
            }
            else if (CurrentState == BotState.Dead)
            {
                // Just respawned
                CurrentState = BotState.Patrolling;
                _nav.Clear();
                _nav.SetRoamArea(_transform.position, 30f);
                _lastHP = _health != null ? _health.HP : PlayerHealth.MAX_HP;
            }

            // ── FLASH BLINDNESS override ─────────────────────────────────
            if (IsFlashed)
            {
                ExecuteFlashBlind();
                UpdateAim();
                UpdateCombat();
                ApplyInputToController();
                return;
            }

            // ── Periodic enemy scan ─────────────────────────────────────
            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + SCAN_INTERVAL;
                ScanForEnemies();
            }

            // ── Damage awareness ────────────────────────────────────────
            DetectIncomingDamage();

            // ── State-machine decisions ─────────────────────────────────
            if (Time.time >= _nextDecisionTime)
            {
                _nextDecisionTime = Time.time + DECISION_INTERVAL;
                MakeDecision();
            }

            // ── Execute current state ───────────────────────────────────
            ExecuteState();

            // ── Update aim smoothly ─────────────────────────────────────
            UpdateAim();

            // ── Combat (fire/reload) ────────────────────────────────────
            UpdateCombat();

            // ── Apply to PlayerController ───────────────────────────────
            ApplyInputToController();
        }

        // ═════════════════════════════════════════════════════════════════
        // FLASH BLINDNESS BEHAVIOR
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Called by PatoUltimateWave when this bot is hit by Tsunami Flash.
        /// </summary>
        public void ApplyFlashBlind(float duration)
        {
            _flashEndTime = Time.time + duration;
            _nextFlashDriftTime = 0f;

            // Roll a behavior for this flash: 0 = panicked spray, 1 = frozen, 2 = erratic move
            float roll = Random.value;
            if (roll < 0.35f)      _flashBehavior = 0;  // spray wildly
            else if (roll < 0.65f) _flashBehavior = 1;  // freeze in place
            else                   _flashBehavior = 2;  // run erratically

            // Lose current target awareness
            _hasLineOfSight = false;
            _targetLostTime = Time.time;
        }

        /// <summary>
        /// Overrides all normal AI while flash-blinded.
        /// Behaviors feel natural: some bots spray at their last known direction,
        /// some freeze, some run around panicked.
        /// </summary>
        private void ExecuteFlashBlind()
        {
            // Re-roll aim drift periodically for realism
            if (Time.time >= _nextFlashDriftTime)
            {
                _nextFlashDriftTime = Time.time + Random.Range(0.3f, 0.8f);
                _flashAimDriftYaw   = Random.Range(-40f, 40f);
                _flashAimDriftPitch = Random.Range(-15f, 10f);
            }

            // Aim drifts randomly away from where we were looking
            _aimPoint = _transform.position
                        + Quaternion.Euler(_flashAimDriftPitch, _currentYaw + _flashAimDriftYaw, 0f)
                        * Vector3.forward * 15f
                        + Vector3.up * 1.5f;

            switch (_flashBehavior)
            {
                case 0: // Panicked spray — fires at nothing, moves slightly
                    _isFiring = Random.value < 0.6f;
                    _botMoveInput = new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(-0.2f, 0.2f));
                    break;

                case 1: // Frozen — stops everything, occasionally fires
                    _isFiring = Random.value < 0.15f;
                    _botMoveInput = Vector2.zero;
                    break;

                case 2: // Erratic — runs around, doesn't fire
                    _isFiring = false;
                    if (Time.time >= _nextFlashDriftTime - 0.1f) // sync with drift re-roll
                        _botMoveInput = new Vector2(Random.Range(-1f, 1f), Random.Range(0.3f, 1f));
                    break;
            }

            _wantsCrouch = false;
        }

        // ═════════════════════════════════════════════════════════════════
        // ENEMY SCANNING
        // ═════════════════════════════════════════════════════════════════

        private void ScanForEnemies()
        {
            float bestScore = float.MaxValue;
            PlayerHealth bestTarget = null;
            Transform bestTransform = null;
            bool bestHasVisualLoS = false;

            Vector3 eyePos = _transform.position + Vector3.up * 1.5f;
            Vector3 botForward = Quaternion.Euler(0, _currentYaw, 0) * Vector3.forward;

            var allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
            foreach (var ph in allPlayers)
            {
                if (ph == _health) continue;
                if (ph.IsDead) continue;

                var nd = ph.GetComponent<PlayerNetworkData>();
                if (nd == null) continue;
                if (nd.Team == team) continue;

                Transform t = ph.transform;
                Vector3 targetCenter = t.position + Vector3.up * 1.1f;
                Vector3 toEnemy = targetCenter - eyePos;
                float dist = toEnemy.magnitude;

                // ── Detection sources ──────────────────────────────────
                // 1) Damage-alert: this enemy just shot us → instant awareness
                bool isDamageAlert = (_damageAlertSource == ph
                                      && Time.time - _damageAlertTime < 0.5f);

                // 2) Visual: must be inside FOV cone AND clear LoS AND no smoke
                bool inFOV = false;
                bool visualLoS = false;
                if (dist > 0.1f)
                {
                    float angle = Vector3.Angle(botForward, toEnemy);
                    inFOV = angle <= FOV_HALF_ANGLE;
                }
                if (inFOV)
                {
                    visualLoS = CheckLineOfSight(eyePos, targetCenter)
                                && !CrimsonSmoke.IsLineObscuredBySmoke(eyePos, targetCenter);
                }

                // 3) Sound: enemy is close + moving + NOT crouching
                bool heardBySound = false;
                if (!visualLoS && dist * dist < SOUND_HEAR_RANGE_SQ)
                {
                    var enemyPC = ph.GetComponent<PlayerController>();
                    if (enemyPC != null && enemyPC.IsMoving() && !enemyPC.IsCrouching)
                        heardBySound = true;
                }

                // 4) Already tracking: keep awareness during search window
                bool alreadyTracking = (ph == _currentTarget
                                        && _currentTargetTransform != null);

                bool detected = isDamageAlert || (inFOV && visualLoS) || heardBySound;
                if (!detected && !alreadyTracking) continue;

                bool thisHasVisualLoS = inFOV && visualLoS;
                float score = dist;
                if (ph == _currentTarget) score -= 5f;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = ph;
                    bestTransform = t;
                    bestHasVisualLoS = thisHasVisualLoS;
                }
            }

            if (bestTarget != null)
            {
                if (_currentTarget != bestTarget)
                {
                    _targetAcquiredTime = Time.time;
                    _nextJitterTime = 0f;
                    // Per-engagement variance: aim speed ±30%
                    _aimSpeedMultiplier = Random.Range(0.40f, 1.0f);
                    _firstShotDelayUsed = false;
                    _firstShotDelay = Random.Range(0.25f, 0.70f) * (1.3f - personality.skillLevel);
                }
                _currentTarget = bestTarget;
                _currentTargetTransform = bestTransform;
                _lastKnownTargetPos = bestTransform.position;
                _hasLineOfSight = bestHasVisualLoS;
                if (bestHasVisualLoS) _targetLostTime = 0f;
            }
            else if (_currentTarget != null)
            {
                _hasLineOfSight = false;
                if (_targetLostTime <= 0f)
                    _targetLostTime = Time.time;
            }
        }

        private bool CheckLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.1f) return true;

            if (Physics.Raycast(from, dir.normalized, out RaycastHit hit, dist, _losLayerMask))
            {
                // Hit something — check if it's the target or geometry
                var ph = hit.collider.GetComponentInParent<PlayerHealth>();
                if (ph != null && ph != _health)
                    return true; // ray hit a player (the target or another enemy)
                return false;    // ray hit geometry — no LoS
            }
            return true; // nothing in the way
        }

        // ═════════════════════════════════════════════════════════════════
        // DAMAGE AWARENESS
        // ═════════════════════════════════════════════════════════════════

        private void DetectIncomingDamage()
        {
            if (_health == null) return;
            float currentHP = _health.HP;

            if (currentHP < _lastHP)
            {
                // We took damage — find the most likely attacker (closest enemy
                // with LoS to us), then instantly lock awareness onto them.
                PlayerHealth bestAttacker = null;
                float bestDist = float.MaxValue;
                Vector3 eyePos = _transform.position + Vector3.up * 1.5f;

                var allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
                foreach (var ph in allPlayers)
                {
                    if (ph == _health || ph.IsDead) continue;
                    var nd = ph.GetComponent<PlayerNetworkData>();
                    if (nd == null || nd.Team == team) continue;

                    float d = Vector3.Distance(_transform.position, ph.transform.position);
                    if (d < bestDist)
                    {
                        Vector3 targetCenter = ph.transform.position + Vector3.up * 1.1f;
                        if (CheckLineOfSight(eyePos, targetCenter))
                        {
                            bestDist = d;
                            bestAttacker = ph;
                        }
                    }
                }

                if (bestAttacker != null)
                {
                    _damageAlertSource = bestAttacker;
                    _damageAlertTime = Time.time;
                    _currentTarget = bestAttacker;
                    _currentTargetTransform = bestAttacker.transform;
                    _lastKnownTargetPos = bestAttacker.transform.position;
                    // Skip reaction delay — react immediately to being shot
                    _targetAcquiredTime = Time.time - personality.reactionTime;
                    _hasLineOfSight = true;
                    _targetLostTime = 0f;
                }
            }
            _lastHP = currentHP;
        }

        // ═════════════════════════════════════════════════════════════════
        // DECISION MAKING
        // ═════════════════════════════════════════════════════════════════

        private void MakeDecision()
        {
            float hpRatio = _health != null ? _health.HP / PlayerHealth.MAX_HP : 1f;

            // ── Retreat if low HP ───────────────────────────────────────
            if (hpRatio <= personality.retreatThreshold && CurrentState == BotState.Engaging)
            {
                CurrentState = BotState.Retreating;
                _isFiring = false;
                return;
            }

            // ── Engaging: have a live target with LoS ──────────────────
            if (_currentTarget != null && !_currentTarget.IsDead && _hasLineOfSight)
            {
                float timeSinceAcquired = Time.time - _targetAcquiredTime;
                if (timeSinceAcquired >= personality.reactionTime)
                {
                    CurrentState = BotState.Engaging;
                    _targetLostTime = 0f;
                    return;
                }
                // Still in reaction delay — patrol/idle
            }

            // ── Pursuing: had a target, lost visual LoS ───────────────
            // Also pursue when detected by sound/damage but no visual LoS
            if (_currentTarget != null && !_currentTarget.IsDead && !_hasLineOfSight)
            {
                if (_targetLostTime <= 0f) _targetLostTime = Time.time;
                float timeLost = Time.time - _targetLostTime;
                if (timeLost < personality.searchDuration)
                {
                    CurrentState = BotState.Pursuing;
                    return;
                }
                else
                {
                    // Give up
                    _currentTarget = null;
                    _currentTargetTransform = null;
                }
            }

            // ── Clean up dead target ───────────────────────────────────
            if (_currentTarget != null && _currentTarget.IsDead)
            {
                _currentTarget = null;
                _currentTargetTransform = null;
                _isFiring = false;
            }

            // ── Gunfire investigation: heard shots or near-miss ─────────
            if (_currentTarget == null && _hasGunfireAlert
                && Time.time - _gunfireAlertTime < 3f)
            {
                _hasGunfireAlert = false;
                _lastKnownTargetPos = _gunfireAlertPos;
                CurrentState = BotState.Pursuing;
                _targetLostTime = Time.time;
                _nav.Clear();
                return;
            }

            // ── Default: Patrolling ────────────────────────────────────
            if (CurrentState == BotState.Retreating && hpRatio > personality.retreatThreshold + 0.1f)
                CurrentState = BotState.Patrolling;
            else if (CurrentState != BotState.Retreating)
                CurrentState = BotState.Patrolling;
        }

        // ═════════════════════════════════════════════════════════════════
        // STATE EXECUTION
        // ═════════════════════════════════════════════════════════════════

        private void ExecuteState()
        {
            switch (CurrentState)
            {
                case BotState.Patrolling:  ExecutePatrol();   break;
                case BotState.Engaging:    ExecuteEngage();   break;
                case BotState.Pursuing:    ExecutePursue();   break;
                case BotState.Retreating:  ExecuteRetreat();  break;
                default:
                    _botMoveInput = Vector2.zero;
                    break;
            }
        }

        // ── PATROL ──────────────────────────────────────────────────────
        private void ExecutePatrol()
        {
            _isFiring = false;
            _wantsCrouch = false;

            // Handle stuck — try unstick, and shift roam area toward map center
            if (_nav.IsStuck)
            {
                _nav.Unstick(_transform.position);
                Vector3 toCenter = (_mapCenter - _nav.RoamCenter).normalized;
                _nav.SetRoamArea(_nav.RoamCenter + toCenter * 10f, 40f);
                return;
            }

            // Handle oscillation — bot is moving but trapped in a small area
            if (_nav.IsOscillating)
            {
                _nav.Clear();
                // Jump roam center dramatically toward map center
                Vector3 newCenter = Vector3.Lerp(_transform.position, _mapCenter, 0.6f);
                _nav.SetRoamArea(newCenter, 40f);
                // Force immediate destination pick
                if (_nav.TryPickRoamDestination(_transform.position, out Vector3 escapeDest))
                    _nav.SetDestination(_transform.position, escapeDest);
                return;
            }

            // If no path, pick a new destination
            if (!_nav.HasPath)
            {
                _patrolDestinationsReached++;

                // Every 3rd destination, push roam center toward map center
                if (_patrolDestinationsReached % 3 == 0)
                {
                    Vector3 newCenter = Vector3.Lerp(_transform.position, _mapCenter, 0.4f);
                    _nav.SetRoamArea(newCenter, 40f);
                }
                else
                {
                    _nav.SetRoamArea(_transform.position, 30f);
                }

                if (_nav.TryPickRoamDestination(_transform.position, out Vector3 dest))
                    _nav.SetDestination(_transform.position, dest);
                else
                    _botMoveInput = Vector2.zero;
                return;
            }

            // Follow path
            Vector3 moveDir = _nav.GetMoveDirection(_transform.position);

            // ── Micro-pause: briefly stop to look around ────────────────
            if (Time.time >= _nextMicroPause && Time.time < _microPauseEnd)
            {
                _botMoveInput = Vector2.zero;
                return;
            }
            if (Time.time >= _nextMicroPause)
            {
                float pauseDuration = Random.Range(0.4f, 1.5f);
                _microPauseEnd = Time.time + pauseDuration;
                _nextMicroPause = Time.time + pauseDuration + Random.Range(8f, 20f);
                // Trigger a glance during the pause
                _glanceYawOffset = Random.Range(-90f, 90f);
                _glanceEndTime = Time.time + pauseDuration;
                _botMoveInput = Vector2.zero;
                return;
            }

            _botMoveInput = WorldDirToLocalInput(moveDir);

            // ── Glance: random head turns while patrolling ──────────────
            if (Time.time >= _nextGlanceTime && Time.time >= _glanceEndTime)
            {
                _glanceYawOffset = Random.Range(-70f, 70f);
                _glanceEndTime = Time.time + Random.Range(0.4f, 1.0f);
                _nextGlanceTime = Time.time + Random.Range(3f, 9f);
            }

            // Look in movement direction (with glance offset)
            if (moveDir.sqrMagnitude > 0.01f)
            {
                Vector3 lookDir = moveDir;
                if (Time.time < _glanceEndTime)
                {
                    lookDir = Quaternion.Euler(0, _glanceYawOffset, 0) * moveDir;
                }
                _aimPoint = _transform.position + lookDir * 10f + Vector3.up * 1.5f;
            }
        }

        // ── ENGAGE ──────────────────────────────────────────────────────
        private void ExecuteEngage()
        {
            if (_currentTargetTransform == null) return;

            // ── Whiff: briefly lose track mid-fight (more likely for low skill) ──
            if (Time.time < _whiffEndTime)
            {
                // Aim drifts to a random nearby point instead of target
                _isFiring = false;
                return;
            }
            // Whiff chance — much higher when target is moving
            float whiffBase = (1f - personality.skillLevel) * 0.018f; // ~1.8% per frame for skill=0
            {
                var targetPC = _currentTargetTransform != null
                    ? _currentTargetTransform.GetComponent<PlayerController>()
                      ?? _currentTargetTransform.GetComponentInParent<PlayerController>()
                    : null;
                if (targetPC != null && targetPC.IsMoving())
                    whiffBase *= 2.5f; // moving targets cause more whiffs
            }
            if (Random.value < whiffBase)
            {
                _whiffEndTime = Time.time + Random.Range(0.3f, 0.8f);
                _aimPoint += new Vector3(Random.Range(-2f, 2f), Random.Range(-0.5f, 0.5f), Random.Range(-2f, 2f));
                _isFiring = false;
                return;
            }

            Vector3 targetPos = _currentTargetTransform.position;
            Vector3 toTarget = targetPos - _transform.position;
            float dist = toTarget.magnitude;

            // ── Stop-and-shoot: only move during burst pauses ───────────
            // When actively firing a burst, stand still for accuracy.
            // During burst pauses (repositioning window), strafe/approach.
            bool inBurst = _burstStartTime > 0f && Time.time < _burstEndTime;
            bool waitingToFire = _isFiring && Time.time < _nextBurstTime;

            if (inBurst || waitingToFire)
            {
                // Standing still — aiming and firing
                _botMoveInput = Vector2.zero;
            }
            else
            {
                // ── Strafing (only between bursts) ──────────────────────
                if (Time.time >= _nextStrafeChange)
                {
                    _nextStrafeChange = Time.time + Random.Range(0.4f, 1.2f);
                    if (Random.value < personality.strafeFrequency)
                        _strafeDir = Random.value > 0.5f ? 1f : -1f;
                    else
                        _strafeDir = 0f;
                }

                // ── Approach or backoff based on preferred range ────────
                float moveForward = 0f;
                if (dist > personality.preferredRange + 3f)
                    moveForward = 1f;   // too far, push forward
                else if (dist < personality.preferredRange - 5f)
                    moveForward = -0.5f; // too close, back up
                else
                    moveForward = 0f;    // in sweet spot

                _botMoveInput = new Vector2(_strafeDir * 0.7f, moveForward);
                if (_botMoveInput.magnitude > 1f)
                    _botMoveInput = _botMoveInput.normalized;
            }

            // ── Crouch during combat ────────────────────────────────────
            if (Time.time >= _nextCrouchToggle)
            {
                _nextCrouchToggle = Time.time + Random.Range(1.5f, 4f);
                _wantsCrouch = Random.value < personality.crouchCombatChance;
            }

            // ── Jump during combat (rare) ───────────────────────────────
            if (Random.value < personality.jumpCombatChance * Time.deltaTime)
                _botJumpInput = true;

            // ── Aim at target ───────────────────────────────────────────
            float aimHeight = Random.value < personality.headshotTendency ? 1.7f : 1.1f;
            _aimPoint = targetPos + Vector3.up * aimHeight;

            // ── Fire only when actually facing the target ───────────────
            Vector3 eyePos = _transform.position + Vector3.up * 1.5f;
            Vector3 desiredDir = _aimPoint - eyePos;
            Vector3 currentDir = Quaternion.Euler(_currentPitch, _currentYaw, 0f) * Vector3.forward;
            float aimError = Vector3.Angle(currentDir, desiredDir);
            _isFiring = aimError <= AIM_FIRE_THRESHOLD;
        }

        // ── PURSUE (move to last known position) ────────────────────────
        private void ExecutePursue()
        {
            _isFiring = false;

            if (!_nav.HasPath || _nav.IsStuck)
            {
                _nav.SetDestination(_transform.position, _lastKnownTargetPos);
            }

            Vector3 moveDir = _nav.GetMoveDirection(_transform.position);
            _botMoveInput = WorldDirToLocalInput(moveDir);

            // Look toward last known position
            _aimPoint = _lastKnownTargetPos + Vector3.up * 1.5f;

            // If arrived at last known position, switch to patrol
            float distToLastKnown = Vector3.Distance(_transform.position, _lastKnownTargetPos);
            if (distToLastKnown < 3f)
            {
                _currentTarget = null;
                _currentTargetTransform = null;
                CurrentState = BotState.Patrolling;
                _nav.Clear();
            }
        }

        // ── RETREAT (run toward spawn/safety) ───────────────────────────
        private void ExecuteRetreat()
        {
            _isFiring = false;
            _wantsCrouch = false;

            if (!_nav.HasPath || _nav.IsStuck)
            {
                // Head toward spawn area
                Vector3 retreatPoint = _nav.CurrentTarget;
                // Use spawn position as safe zone approximation
                var mapSpawn = FindAnyObjectByType<MapSpawnManager>();
                if (mapSpawn != null)
                {
                    retreatPoint = mapSpawn.GetSpawnPosition(team);
                }
                _nav.SetDestination(_transform.position, retreatPoint);
            }

            Vector3 moveDir = _nav.GetMoveDirection(_transform.position);
            _botMoveInput = WorldDirToLocalInput(moveDir);

            // Look backward briefly (combat awareness)
            if (_currentTargetTransform != null)
                _aimPoint = _currentTargetTransform.position + Vector3.up * 1.2f;
        }

        // ═════════════════════════════════════════════════════════════════
        // AIM UPDATE (smooth tracking with human-like jitter)
        // ═════════════════════════════════════════════════════════════════

        private void UpdateAim()
        {
            // Update jitter offset periodically
            if (Time.time >= _nextJitterTime)
            {
                _nextJitterTime = Time.time + Random.Range(0.08f, 0.25f);
                float jitterScale = personality.aimJitter;
                // Reduce jitter when engaging (more focused)
                if (CurrentState == BotState.Engaging)
                    jitterScale *= 0.6f;
                _aimOffset = new Vector3(
                    Random.Range(-jitterScale, jitterScale) * 0.03f,
                    Random.Range(-jitterScale, jitterScale) * 0.02f,
                    0f
                );
            }

            // Apply accuracy modifier — less skilled bots aim further from center
            float accuracyError = (1f - personality.baseAccuracy) * 2f;

            // ── Moving target penalty: harder to hit a strafing player ──
            float movingPenalty = 1f;
            if (_currentTarget != null && CurrentState == BotState.Engaging)
            {
                var targetPC = _currentTarget.GetComponent<PlayerController>();
                if (targetPC != null && targetPC.IsMoving())
                {
                    movingPenalty = 2.5f;   // jitter & error scale up vs moving targets
                }
            }

            Vector3 finalAimPoint = _aimPoint + _aimOffset * accuracyError * movingPenalty;

            // Calculate desired yaw/pitch toward aim point
            Vector3 toAim = finalAimPoint - (_transform.position + Vector3.up * 1.5f);
            if (toAim.sqrMagnitude < 0.01f) return;

            float desiredYaw = Mathf.Atan2(toAim.x, toAim.z) * Mathf.Rad2Deg;
            float desiredPitch = -Mathf.Asin(Mathf.Clamp(toAim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            desiredPitch = Mathf.Clamp(desiredPitch, -80f, 80f);

            // Smooth rotation at personality-defined speed (with per-engagement variance)
            float maxRotation = personality.aimSpeed * _aimSpeedMultiplier * Time.deltaTime;
            // Slow down tracking when target is moving (harder to keep crosshair on)
            if (movingPenalty > 1f) maxRotation *= 0.65f;
            _currentYaw   = Mathf.MoveTowardsAngle(_currentYaw, desiredYaw, maxRotation);
            _currentPitch = Mathf.MoveTowards(_currentPitch, desiredPitch, maxRotation * 0.8f);
        }

        // ═════════════════════════════════════════════════════════════════
        // COMBAT (burst fire, reload logic)
        // ═════════════════════════════════════════════════════════════════

        private void UpdateCombat()
        {
            FireWeapon fw = _setup != null ? _setup.GetCurrentWeapon() : null;
            if (fw == null) return;

            // ── Reload logic ────────────────────────────────────────────
            int currentAmmo = fw.GetCurrentAmmo();
            int maxAmmo = fw.GetMaxAmmo();
            float ammoRatio = maxAmmo > 0 ? (float)currentAmmo / maxAmmo : 1f;

            if (currentAmmo <= 0)
            {
                // Must reload — empty magazine
                fw.StartReload();
                _isFiring = false;
                return;
            }

            if (!_isFiring && ammoRatio < personality.reloadThreshold
                && CurrentState != BotState.Engaging)
            {
                // Proactive reload when safe
                fw.StartReload();
                return;
            }

            // ── Burst fire pattern ──────────────────────────────────────
            if (_isFiring)
            {
                if (fw.IsReloading()) return;

                // Extra hesitation before the very first burst of this engagement
                if (!_firstShotDelayUsed)
                {
                    _firstShotDelayUsed = true;
                    _nextBurstTime = Time.time + _firstShotDelay;
                }

                if (Time.time < _nextBurstTime)
                {
                    // In burst pause
                    fw.StopFiring();
                    return;
                }

                if (_burstStartTime <= 0f)
                {
                    // Start new burst
                    _burstStartTime = Time.time;
                    _burstEndTime = Time.time + personality.burstDuration + Random.Range(-0.05f, 0.08f);
                }

                if (Time.time < _burstEndTime)
                {
                    // Firing burst — call Fire() directly for ALL weapon types.
                    // Bots cannot rely on the StartFiring→Update→Fire chain because
                    // the FPV weapon's MonoBehaviour.Update() does not run on Fusion
                    // host-owned objects with no InputAuthority.
                    fw.Fire();
                }
                else
                {
                    // Burst done — pause
                    fw.StopFiring();
                    _burstStartTime = 0f;
                    _nextBurstTime = Time.time + personality.burstPause + Random.Range(-0.03f, 0.05f);
                }
            }
            else
            {
                fw.StopFiring();
                _burstStartTime = 0f;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // APPLY TO PLAYERCONTROLLER
        // ═════════════════════════════════════════════════════════════════

        private void ApplyInputToController()
        {
            if (_pc == null) return;

            // Feed movement + jump
            _pc.SetBotInput(_botMoveInput, _botJumpInput, _currentYaw, _currentPitch, _wantsCrouch);
            _botJumpInput = false; // consume
        }

        // ═════════════════════════════════════════════════════════════════
        // HELPERS
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Converts a world-space XZ direction to a local-space (relative to bot's facing)
        /// Vector2 input for PlayerController (x = strafe, y = forward).
        /// </summary>
        private Vector2 WorldDirToLocalInput(Vector3 worldDir)
        {
            if (worldDir.sqrMagnitude < 0.001f) return Vector2.zero;

            // bot's forward direction (XZ only)
            Vector3 forward = Quaternion.Euler(0, _currentYaw, 0) * Vector3.forward;
            Vector3 right   = Quaternion.Euler(0, _currentYaw, 0) * Vector3.right;

            float fwd  = Vector3.Dot(worldDir, forward);
            float strafe = Vector3.Dot(worldDir, right);

            var input = new Vector2(strafe, fwd);
            if (input.magnitude > 1f) input.Normalize();
            return input;
        }
    }
}
