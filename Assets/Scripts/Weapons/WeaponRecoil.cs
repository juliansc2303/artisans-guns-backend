using UnityEngine;
using ArtisansGuns.Game;

namespace ArtisansGuns.Weapons
{
    /// <summary>
    /// WeaponRecoil — Pattern-based recoil engine with counter-steer skill expression.
    ///
    /// Core design:
    ///   • Each weapon owns a RecoilPattern ScriptableObject with a fixed sequence of
    ///     (horizontal, vertical) camera kicks in degrees.
    ///   • Bullets ALWAYS go where the crosshair points — NO spread.
    ///     Recoil only moves the camera.
    ///   • The pattern advances one step per shot, paced by the weapon's fire rate.
    ///   • If the player stops firing long enough, the pattern index resets.
    ///   • Counter-steer: dragging the camera OPPOSITE to the next kick at the
    ///     moment of fire REDUCES the kick (reward).
    ///   • Wrong-steer: dragging the camera in the SAME direction as the kick
    ///     AMPLIFIES the kick (punishment).
    ///   • Moving adds a random horizontal perturbation + a global multiplier.
    ///   • Visual weapon kickback (position + rotation) is independent of the
    ///     pattern and keeps the existing feel.
    ///
    /// Attached to weapon prefab.  Controlled by FireWeapon via ApplyRecoil().
    /// </summary>
    public class WeaponRecoil : MonoBehaviour
    {
        // ──────────────────────── REFERENCES ────────────────────────

        private WeaponConfig weaponConfig;
        private RecoilPattern pattern;
        private PlayerController playerController;
        private Transform weaponHolder;
        private WeaponSway weaponSway;

        // ───────────────────── PATTERN PLAYBACK ─────────────────────

        private int _patternIndex;       // current step in the pattern
        private float _lastShotTime;     // Time.time of the most recent shot
        private float _fireInterval;     // seconds between shots (derived from fire rate)

        // ──────────────── SMOOTH KICK APPLICATION ───────────────────
        // Kicks are accumulated and drained over a few frames for responsive
        // but non-jarring camera movement.

        private float _pendingPitchKick; // remaining pitch kick to drain (degrees, positive = up)
        private float _pendingYawKick;   // remaining yaw kick to drain   (degrees, positive = right)

        // ─────────────── VISUAL WEAPON RECOIL STATE ─────────────────

        private Vector3    originalLocalPosition;
        private Quaternion originalLocalRotation;
        private Vector3    currentWeaponRecoilPosition;
        private Vector3    targetWeaponRecoilPosition;
        private Quaternion currentWeaponRecoilRotation;
        private Quaternion targetWeaponRecoilRotation;

        // ═══════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ═══════════════════════════════════════════════════════════════

        private void Start()
        {
            // Fallback — should already be set by PlayerSetup
            if (playerController == null)
                playerController = FindObjectOfType<PlayerController>();
        }

        /// <summary>
        /// Initialize recoil system with weapon config.
        /// Called AFTER PlayerSetup applies position/rotation offsets.
        /// </summary>
        public void Initialize(WeaponConfig config)
        {
            weaponConfig = config;
            pattern = config != null ? config.recoilPattern : null;

            // Fire interval from RPM
            _fireInterval = (config != null && config.fireRate > 0f)
                ? 60f / config.fireRate
                : 0.1f;

            // Reset pattern state
            _patternIndex = 0;
            _lastShotTime = -999f;
            _pendingPitchKick = 0f;
            _pendingYawKick = 0f;

            if (playerController == null)
                playerController = FindObjectOfType<PlayerController>();

            if (weaponHolder == null)
                weaponHolder = transform.parent;

            if (weaponHolder == null) return;

            originalLocalPosition = weaponHolder.localPosition;
            originalLocalRotation = weaponHolder.localRotation;
            currentWeaponRecoilPosition = Vector3.zero;
            targetWeaponRecoilPosition  = Vector3.zero;
            currentWeaponRecoilRotation = Quaternion.identity;
            targetWeaponRecoilRotation  = Quaternion.identity;
        }

        // ═══════════════════════════════════════════════════════════════
        //  PUBLIC API — called by other scripts
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Set WeaponHolder reference (called by PlayerSetup before Initialize).
        /// </summary>
        public void SetWeaponHolder(Transform holder)
        {
            weaponHolder = holder;
            weaponSway = holder.GetComponent<WeaponSway>();
            if (weaponSway != null)
                weaponSway.SetWeaponRecoil(this);
        }

        /// <summary>
        /// Set PlayerController explicitly (avoids FindObjectOfType in multiplayer).
        /// </summary>
        public void SetPlayerController(PlayerController pc)
        {
            playerController = pc;
        }

        // ═══════════════════════════════════════════════════════════════
        //  APPLY RECOIL  —  called by FireWeapon once per shot
        // ═══════════════════════════════════════════════════════════════

        public void ApplyRecoil()
        {
            if (weaponConfig == null || playerController == null || weaponHolder == null)
                return;

            // ── 1. Pattern reset check ──────────────────────────────
            float resetGap = _fireInterval * (pattern != null ? pattern.resetGapMultiplier : 2.5f);
            if (Time.time - _lastShotTime > resetGap)
                _patternIndex = 0;

            _lastShotTime = Time.time;

            // ── 2. Read the next kick from the pattern ──────────────
            Vector2 rawKick;
            if (pattern != null && pattern.Length > 0)
            {
                rawKick = pattern.GetKick(_patternIndex);
            }
            else
            {
                // Fallback: simple upward kick from legacy config values
                rawKick = new Vector2(0f, weaponConfig.recoilKickAmount);
            }

            _patternIndex++;

            // ── 3. Counter-steer evaluation ─────────────────────────
            float steerMultiplier = EvaluateCounterSteer(rawKick);

            // ── 4. Compute final kick (no movement multiplier — spread handles that) ─
            Vector2 finalKick = rawKick * steerMultiplier;

            // ── 5. Accumulate into pending kick (drained in Update) ─
            _pendingPitchKick += finalKick.y;
            _pendingYawKick   += finalKick.x;

            // ── 6. Visual weapon kickback ───────────────────────────
            bool isMoving = playerController.IsMoving();
            ApplyVisualKickback(isMoving);
        }

        // ═══════════════════════════════════════════════════════════════
        //  COUNTER-STEER
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Evaluate how well the player is counter-steering at the moment of fire.
        /// Returns a multiplier:  &lt; 1 = rewarded,  1 = neutral,  &gt; 1 = punished.
        /// </summary>
        private float EvaluateCounterSteer(Vector2 kick)
        {
            if (pattern == null) return 1f;
            if (kick.sqrMagnitude < 0.0001f) return 1f;

            Vector2 delta = playerController.GetCameraDelta();

            // Not enough input → neutral
            if (delta.magnitude < pattern.minDeltaMagnitude)
                return 1f;

            // Dot product between camera movement direction and kick direction.
            // -1 = perfect counter-steer, +1 = same direction, 0 = perpendicular.
            float alignment = Vector2.Dot(delta.normalized, kick.normalized);

            float threshold = pattern.steerDetectionThreshold;

            if (alignment < -threshold)
            {
                // COUNTER-STEERING → reward (reduce kick)
                float t = Mathf.InverseLerp(-threshold, -1f, alignment);
                return Mathf.Lerp(1f, pattern.counterSteerMinMultiplier, t);
            }

            if (alignment > threshold)
            {
                // WRONG-STEERING → punishment (amplify kick)
                float t = Mathf.InverseLerp(threshold, 1f, alignment);
                return Mathf.Lerp(1f, pattern.wrongSteerAmplification, t);
            }

            // Dead zone → neutral
            return 1f;
        }

        // ═══════════════════════════════════════════════════════════════
        //  VISUAL WEAPON KICKBACK  (position + rotation on WeaponHolder)
        // ═══════════════════════════════════════════════════════════════

        private void ApplyVisualKickback(bool isMoving)
        {
            float weaponMultiplier = isMoving ? weaponConfig.weaponMovingMultiplier : 1f;

            Vector3 positionKick = new Vector3(
                0f,
                weaponConfig.weaponUpForce * weaponMultiplier,
                -weaponConfig.weaponBackForce * weaponMultiplier
            );
            targetWeaponRecoilPosition = positionKick;

            float verticalRoll  = weaponConfig.weaponVerticalRoll  * weaponMultiplier;
            float horizontalRoll = Random.Range(-weaponConfig.weaponHorizontalRoll,
                                                 weaponConfig.weaponHorizontalRoll) * weaponMultiplier;

            targetWeaponRecoilRotation = Quaternion.Euler(-verticalRoll, horizontalRoll, 0f);
        }

        // ═══════════════════════════════════════════════════════════════
        //  UPDATE — drain pending kicks + animate visual recoil
        // ═══════════════════════════════════════════════════════════════

        private void Update()
        {
            if (weaponConfig == null || playerController == null) return;

            float dt = Time.deltaTime;
            float kickSpeed = (pattern != null) ? pattern.kickApplicationSpeed : 25f;

            // ── Drain pending camera kick ───────────────────────────
            if (Mathf.Abs(_pendingPitchKick) > 0.001f || Mathf.Abs(_pendingYawKick) > 0.001f)
            {
                float factor = kickSpeed * dt;

                float pitchThisFrame = _pendingPitchKick * factor;
                float yawThisFrame   = _pendingYawKick   * factor;

                playerController.AddRecoilImpulse(pitchThisFrame);
                playerController.AddHorizontalRecoilImpulse(yawThisFrame);

                _pendingPitchKick -= pitchThisFrame;
                _pendingYawKick   -= yawThisFrame;

                // Snap to zero when negligible
                if (Mathf.Abs(_pendingPitchKick) < 0.001f) _pendingPitchKick = 0f;
                if (Mathf.Abs(_pendingYawKick)   < 0.001f) _pendingYawKick   = 0f;
            }

            // ── Animate visual weapon recoil ────────────────────────
            currentWeaponRecoilPosition = Vector3.Lerp(
                currentWeaponRecoilPosition, targetWeaponRecoilPosition, dt * 20f);
            currentWeaponRecoilRotation = Quaternion.Slerp(
                currentWeaponRecoilRotation, targetWeaponRecoilRotation, dt * 20f);

            // Recover to rest
            targetWeaponRecoilPosition = Vector3.Lerp(
                targetWeaponRecoilPosition, Vector3.zero, dt * weaponConfig.weaponRecoverySpeed);
            targetWeaponRecoilRotation = Quaternion.Slerp(
                targetWeaponRecoilRotation, Quaternion.identity, dt * weaponConfig.weaponRecoverySpeed);

            // Snap when close
            if (targetWeaponRecoilPosition.magnitude < 0.001f)
            {
                targetWeaponRecoilPosition  = Vector3.zero;
                currentWeaponRecoilPosition = Vector3.zero;
            }
            if (Quaternion.Angle(targetWeaponRecoilRotation, Quaternion.identity) < 0.1f)
            {
                targetWeaponRecoilRotation  = Quaternion.identity;
                currentWeaponRecoilRotation = Quaternion.identity;
            }

            // ── Combine sway + recoil and write to WeaponHolder ─────
            Vector3    swayPos = Vector3.zero;
            Quaternion swayRot = Quaternion.identity;

            if (weaponSway != null)
            {
                swayPos = weaponSway.GetCurrentSwayPositionOffset();
                swayRot = weaponSway.GetCurrentSwayRotationOffset();
            }

            weaponHolder.localPosition = originalLocalPosition + swayPos + currentWeaponRecoilPosition;
            weaponHolder.localRotation = originalLocalRotation * swayRot * currentWeaponRecoilRotation;
        }

        // ═══════════════════════════════════════════════════════════════
        //  QUERIES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// True while the weapon model is actively kicking back.
        /// Used by WeaponSway to avoid interfering with recoil animation.
        /// </summary>
        public bool HasActiveRecoil()
        {
            return currentWeaponRecoilPosition.magnitude > 0.001f ||
                   Quaternion.Angle(currentWeaponRecoilRotation, Quaternion.identity) > 0.1f;
        }

        /// <summary>Current step in the recoil pattern (read-only, for UI/debug).</summary>
        public int CurrentPatternIndex => _patternIndex;

        /// <summary>Reset pattern to the first step (e.g., on weapon switch or reload).</summary>
        public void ResetPattern()
        {
            _patternIndex     = 0;
            _pendingPitchKick = 0f;
            _pendingYawKick   = 0f;
        }
    }
}
