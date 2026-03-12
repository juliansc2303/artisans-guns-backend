using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Attach to the CrimsonUltimateProjectile prefab (also needs a Rigidbody).
    /// Call Launch() immediately after Instantiating.
    ///
    /// On collision with ANY surface (environment or player), it starts a
    /// detonation timer. A short grace period after launch prevents instant
    /// self-collision with the thrower.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CrimsonUltimateProjectile : MonoBehaviour
    {
        private Rigidbody rb;
        private System.Action<Vector3> _onDetonate;
        private bool _hasCollided;
        private bool _hasDetonated;
        private float _detonationDelay = 1.5f;
        private float _detonationTimer;

        // Grace period: ignore the thrower briefly so the grenade clears the player.
        private GameObject _throwerRoot;
        private float _graceTimer;
        private const float GRACE_PERIOD = 0.3f;

        // 3D impact sound (heard by ALL clients)
        private static AudioClip _impactClip;
        private static bool _impactClipLoaded;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Kick the projectile in the given world direction at the given speed.
        /// <paramref name="onDetonate"/> is invoked with the spawn position when the effect should appear.
        /// <paramref name="detonationDelay"/> seconds after first collision, the effect spawns.
        /// <paramref name="throwerRoot"/> is the throwing player's root — ignored only during the 0.3s grace period.
        /// </summary>
        public void Launch(Vector3 direction, float speed, System.Action<Vector3> onDetonate, float detonationDelay = 1.5f, GameObject throwerRoot = null)
        {
            _onDetonate      = onDetonate;
            _detonationDelay = detonationDelay;
            _throwerRoot     = throwerRoot;
            _graceTimer      = GRACE_PERIOD;

            if (rb != null)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.linearVelocity = direction * speed;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_hasCollided) return;

            // During the grace period, only ignore the thrower (so the grenade
            // can clear the player's own collider). All other players activate it.
            if (_graceTimer > 0f && _throwerRoot != null)
            {
                var hitSetup = collision.gameObject.GetComponentInParent<ArtisansGuns.Game.PlayerSetup>();
                if (hitSetup != null && hitSetup.gameObject == _throwerRoot) return;
            }

            _hasCollided = true;
            _detonationTimer = _detonationDelay;

            // Play 3D impact sound at collision point (all clients hear this)
            PlayImpactSound();

            // Stop the projectile from bouncing wildly — kill velocity
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }

        private void Update()
        {
            // Count down the grace timer
            if (_graceTimer > 0f)
                _graceTimer -= Time.deltaTime;

            if (!_hasCollided || _hasDetonated) return;

            _detonationTimer -= Time.deltaTime;
            if (_detonationTimer <= 0f)
            {
                Detonate();
            }
        }

        private void Detonate()
        {
            _hasDetonated = true;

            // Take the current XZ position, Y = 0 (ground level)
            Vector3 effectPos = new Vector3(transform.position.x, 0f, transform.position.z);

            _onDetonate?.Invoke(effectPos);

            Destroy(gameObject);
        }

        // ─── 3D Impact Sound ─────────────────────────────────────────────

        private void PlayImpactSound()
        {
            if (!_impactClipLoaded)
            {
                _impactClip = Resources.Load<AudioClip>("Sounds/UltimateImpact");
                _impactClipLoaded = true;
            }
            if (_impactClip == null) return;

            GameObject sfxGO = new GameObject("UltImpactSFX");
            sfxGO.transform.position = transform.position;
            AudioSource src  = sfxGO.AddComponent<AudioSource>();
            src.clip         = _impactClip;
            src.spatialBlend = 1f;           // full 3D
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance  = 1f;
            src.maxDistance  = 30f;
            src.volume       = 1f;
            src.playOnAwake  = false;
            src.Play();
            Destroy(sfxGO, _impactClip.length + 0.1f);
        }
    }
}
