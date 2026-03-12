using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Placed on the TsunamiVFX prefab root.
    /// Handles:
    ///   1. Rising from below ground into ride position.
    ///   2. Moving forward at constant speed in the spawn direction.
    ///   3. Carrying the rider (local player) on top — applies platform velocity
    ///      each FixedUpdate so CharacterController moves with the wave.
    ///   4. Auto-destroying after the configured duration.
    ///
    /// Spawned via plain Instantiate inside an RPC (same pattern as CrimsonSmoke).
    /// Only the InputAuthority client (rider) tracks riding logic.
    /// </summary>
    public class TsunamiWave : MonoBehaviour
    {
        // ── Runtime config (set by AbilitySystem before first Update) ──────
        [HideInInspector] public Vector3 moveDirection;      // horizontal forward
        [HideInInspector] public float   waveSpeed;          // units/s
        [HideInInspector] public float   waveDuration;       // seconds
        [HideInInspector] public float   riseFromBelow;      // how far below spawn the wave starts
        [HideInInspector] public float   riseSpeed;          // units/s while rising
        [HideInInspector] public float   riderHeightOffset;  // how high above wave top the rider stands

        /// <summary>
        /// The local player's CharacterController.
        /// Only set on the InputAuthority client that cast the ability.
        /// Remote clients leave this null — they see the wave move but don't ride it.
        /// </summary>
        [HideInInspector] public CharacterController riderController;

        /// <summary>
        /// Reference to the local PlayerController (for super-jump & grounding queries).
        /// </summary>
        [HideInInspector] public ArtisansGuns.Game.PlayerController riderPlayerController;

        // ── Static accessor for Ability 2 (WaterSuperJump) ─────────────────
        /// <summary>
        /// The wave the local player is currently riding (or null).
        /// WaterSuperJump checks this to know if the player is on a wave.
        /// </summary>
        public static TsunamiWave ActiveRiderWave { get; private set; }

        // ── Internal state ─────────────────────────────────────────────────
        private float  _timer;
        private float  _targetY;       // final Y the wave should reach (ride height)
        private bool   _rising = true; // still rising into position?
        private bool   _isRider;       // true only on the InputAuthority client
        private bool   _riderOnWave;   // rider is currently on top of the wave

        // ── Init ───────────────────────────────────────────────────────────

        /// <summary>
        /// Called right after Instantiate by AbilitySystem.
        /// Sets the start position below spawn origin and begins the rise.
        /// </summary>
        public void Launch(Vector3 spawnOrigin)
        {
            _targetY = spawnOrigin.y;

            // Start below — the wave rises into view
            Vector3 startPos = spawnOrigin;
            startPos.y -= riseFromBelow;
            transform.position = startPos;

            _isRider = riderController != null;

            if (_isRider)
            {
                _riderOnWave = true;
                ActiveRiderWave = this;
            }

            // All players (including the rider) collide with the wave physically.
            // The rider stands on top of the wave colliders naturally via CharacterController.
            // Other players get pushed by the wave (counter to Crimson smoke).
            // Raycasts (bullets) also hit the wave — it acts as a shield.
        }

        // ── Tick ───────────────────────────────────────────────────────────

        private void Update()
        {
            float dt = Time.deltaTime;
            _timer += dt;

            // ── Duration check ──────────────────────────────────────────
            if (_timer >= waveDuration)
            {
                Cleanup();
                Destroy(gameObject);
                return;
            }

            // ── Rise phase ──────────────────────────────────────────────
            if (_rising)
            {
                Vector3 pos = transform.position;
                pos.y = Mathf.MoveTowards(pos.y, _targetY, riseSpeed * dt);
                transform.position = pos;

                if (Mathf.Abs(pos.y - _targetY) < 0.01f)
                    _rising = false;
            }

            // ── Forward movement ────────────────────────────────────────
            transform.position += moveDirection * waveSpeed * dt;

            // ── Rider tracking ──────────────────────────────────────────
            if (_isRider && _riderOnWave && riderController != null && riderController.enabled)
            {
                // Move the rider with the wave (platform velocity)
                Vector3 platformDelta = moveDirection * waveSpeed * dt;

                // If still rising, also apply vertical component
                if (_rising)
                {
                    float prevY = transform.position.y - moveDirection.y * waveSpeed * dt; // approximate
                    platformDelta.y += (transform.position.y - (transform.position.y - riseSpeed * dt));
                    // Simpler: just make sure rider stays at wave top
                }

                riderController.Move(platformDelta);

                // Keep rider snapped to wave top (prevents drift from gravity fighting)
                SnapRiderToWaveTop();

                // Check if rider walked off the wave (dismount)
                if (!IsRiderAboveWave())
                {
                    _riderOnWave = false;
                    ActiveRiderWave = null;
                }
            }
        }

        // ── Rider helpers ──────────────────────────────────────────────

        /// <summary>
        /// Snaps the rider's Y position to the top of the wave + offset,
        /// preventing gravity from pulling them through or away from the wave.
        /// Only adjusts Y if the rider is close to the expected ride height.
        /// </summary>
        private void SnapRiderToWaveTop()
        {
            if (riderController == null) return;

            float waveTopY = transform.position.y + riderHeightOffset;
            float riderY   = riderController.transform.position.y;

            // Only snap if the rider is within a reasonable range (not jumping high above)
            float diff = riderY - waveTopY;
            if (diff < -0.5f || diff > 3.0f) return; // too far below or way above (jumping)

            // Gently push rider to wave top
            if (Mathf.Abs(diff) > 0.05f)
            {
                Vector3 riderPos = riderController.transform.position;
                riderPos.y = Mathf.MoveTowards(riderPos.y, waveTopY, 15f * Time.deltaTime);
                // Use Move with vertical delta instead of setting position directly
                // (CharacterController doesn't like direct position set)
                float yDelta = waveTopY - riderController.transform.position.y;
                riderController.Move(new Vector3(0, yDelta * 0.5f, 0));
            }
        }

        /// <summary>
        /// Returns true if the rider's XZ position is roughly above the wave.
        /// Uses a generous radius so the player can move around on top.
        /// </summary>
        private bool IsRiderAboveWave()
        {
            if (riderController == null) return false;

            Vector3 riderXZ = riderController.transform.position;
            Vector3 waveXZ  = transform.position;
            riderXZ.y = 0;
            waveXZ.y  = 0;

            // Generous dismount radius — wave colliders span ~3 units
            return Vector3.Distance(riderXZ, waveXZ) < 5f;
        }

        /// <summary>
        /// Returns true if the rider is currently on this wave.
        /// Used by WaterSuperJump to check eligibility.
        /// </summary>
        public bool IsRiding => _riderOnWave;

        /// <summary>
        /// Force-dismount the rider (called when player uses Super Jump or ability ends).
        /// </summary>
        public void DismountRider()
        {
            _riderOnWave = false;
            if (ActiveRiderWave == this)
                ActiveRiderWave = null;
        }

        // ── Cleanup ────────────────────────────────────────────────────

        private void Cleanup()
        {
            if (ActiveRiderWave == this)
                ActiveRiderWave = null;
        }

        private void OnDestroy()
        {
            Cleanup();
        }
    }
}
