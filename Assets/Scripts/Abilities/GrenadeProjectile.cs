using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Attach to the grenade projectile prefab (also needs a Rigidbody).
    /// Call Launch() immediately after Instantiating to set it in motion.
    /// On collision with ANY surface (environment or player) it detonates.
    /// A short grace period (~0.3s) after launch prevents instant self-collision.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GrenadeProjectile : MonoBehaviour
    {
        private Rigidbody rb;
        private System.Action<Vector3> _onDetonate;
        private bool hasDetonated = false;

        // Grace period: ignore the thrower for a brief moment after launch
        // so the grenade clears the player's collider before becoming active.
        private GameObject _throwerRoot;
        private float _graceTimer;
        private const float GRACE_PERIOD = 0.3f;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Kick the projectile in the given world direction at the given speed.
        /// Called by SmokeGrenadeAbility.OnThrowGrenade().
        /// <paramref name="onDetonate"/> is invoked with the hit position when the grenade detonates.
        /// <paramref name="throwerRoot"/> is the throwing player's root — ignored only during the 0.3s grace period.
        /// </summary>
        public void Launch(Vector3 direction, float speed, System.Action<Vector3> onDetonate, GameObject throwerRoot = null)
        {
            _onDetonate  = onDetonate;
            _throwerRoot = throwerRoot;
            _graceTimer  = GRACE_PERIOD;

            if (rb != null)
            {
                // Continuous collision detection prevents tunnelling through thin surfaces
                // when the projectile is moving fast.
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.linearVelocity = direction * speed;
            }
        }

        private void Update()
        {
            // Count down the grace timer
            if (_graceTimer > 0f)
                _graceTimer -= Time.deltaTime;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (hasDetonated) return;

            // During the grace period, only ignore the thrower (so the grenade
            // can clear the player's own collider). All other players detonate it.
            if (_graceTimer > 0f && _throwerRoot != null)
            {
                var hitSetup = collision.gameObject.GetComponentInParent<ArtisansGuns.Game.PlayerSetup>();
                if (hitSetup != null && hitSetup.gameObject == _throwerRoot) return;
            }

            Detonate(collision.contacts[0].point);
        }

        private void Detonate(Vector3 position)
        {
            hasDetonated = true;

            _onDetonate?.Invoke(position);

            Destroy(gameObject);
        }
    }
}
