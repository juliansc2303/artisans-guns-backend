using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Lives on the FPV grenade prefab that is spawned into WeaponHolder.
    /// Receives animation events from the hands animator and co-ordinates with AbilitySystem.
    ///
    /// Unity Animator setup required on the HANDS animator:
    ///   • Idle state  → Animation Event  "OnAbilityReady"   (enables throwing)
    ///   • Shoot anim  → Animation Event  "OnThrowGrenade"   (mid-point: spawns projectile)
    ///   • Shoot anim  → Animation Event  "OnThrowComplete"  (end-point: notifies system)
    ///   • Parameter   → Trigger          "Shoot"            (triggered by ThrowGrenade())
    /// </summary>
    public class SmokeGrenadeAbility : MonoBehaviour
    {
        [Tooltip("Projectile prefab to spawn (should have GrenadeProjectile + Rigidbody)")]
        public GameObject projectilePrefab;

        [Tooltip("Speed at which the projectile is launched")]
        public float throwSpeed = 14f;

        [Header("Local Sounds (2D — only the local player hears these)")]
        [SerializeField] private AudioClip equipSound;
        [SerializeField] private AudioClip throwSound;

        private AudioSource _localAudio;

        /// <summary>
        /// Callback invoked with (spawnPosition, direction, speed) when the throw animation
        /// fires. AbilitySystem wires this to send an RPC so all clients see the projectile.
        /// </summary>
        [HideInInspector] public System.Action<Vector3, Vector3, float> onProjectileThrown;

        // The point inside PlayerCamera where projectiles are spawned.
        // Set by AbilitySystem after instantiation.
        [HideInInspector] public Transform abilitySpawner;

        // True once the Idle animation has fired OnAbilityReady.
        public bool IsReady { get; private set; }

        // AbilitySystem subscribes to this to know when the full throw sequence is done.
        public System.Action OnThrowCompleted;

        private Animator animator;

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null)
                animator = GetComponent<Animator>();

            // Local-only 2D AudioSource for equip/throw sounds
            _localAudio = gameObject.AddComponent<AudioSource>();
            _localAudio.spatialBlend = 0f;   // fully 2D
            _localAudio.playOnAwake  = false;
            _localAudio.loop         = false;

            // Load clips from Resources if not assigned in Inspector
            if (equipSound == null) equipSound = Resources.Load<AudioClip>("Sounds/SmokeEquip");
            if (throwSound == null) throwSound = Resources.Load<AudioClip>("Sounds/SmokeThrow");
        }

        // ---------- Public API ----------

        /// <summary>Begins the throw animation if the ability is ready.</summary>
        public void ThrowGrenade()
        {
            if (!IsReady)
            {
                Debug.Log("[SmokeGrenadeAbility] Not ready yet — waiting for OnAbilityReady animation event");
                return;
            }

            if (animator == null)
            {
                Debug.LogWarning("[SmokeGrenadeAbility] No Animator found — cannot throw");
                return;
            }

            IsReady = false; // Prevent double-throw until next Idle state
            animator.SetTrigger("Shoot");
        }

        // ---------- Animation Events (assigned in Unity Animator) ----------

        /// <summary>Animation event: called from the Idle state — enables throwing.</summary>
        public void OnAbilityReady()
        {
            IsReady = true;
        }

        /// <summary>
        /// Animation event: called at the mid-point of the Shoot animation —
        /// instantiates and launches the grenade projectile.
        /// </summary>
        public void OnThrowGrenade()
        {
            if (projectilePrefab == null)
            {
                Debug.LogWarning("[SmokeGrenadeAbility] projectilePrefab not assigned");
                return;
            }

            Transform spawnPoint = abilitySpawner;
            if (spawnPoint == null)
            {
                // Fallback: use our own transform
                spawnPoint = transform;
                Debug.LogWarning("[SmokeGrenadeAbility] abilitySpawner not set — using grenade transform as fallback");
            }

            // Notify AbilitySystem, which will RPC-spawn the projectile on ALL clients.
            onProjectileThrown?.Invoke(spawnPoint.position, spawnPoint.forward, throwSpeed);
        }

        /// <summary>
        /// Animation event: called at the end of the Shoot animation —
        /// tells AbilitySystem the ability sequence is finished.
        /// </summary>
        public void OnThrowComplete()
        {
            OnThrowCompleted?.Invoke();
        }

        // ---------- Sound methods (hook to Animation Events) ----------

        /// <summary>
        /// Call from the StartGranade animation event.
        /// Plays a local-only 2D sound when the smoke grenade is equipped.
        /// </summary>
        public void PlayEquipSound()
        {
            if (_localAudio != null && equipSound != null)
                _localAudio.PlayOneShot(equipSound);
        }

        /// <summary>
        /// Call from the throw animation event (e.g. mid-point of Shoot).
        /// Plays a local-only 2D sound when the grenade is thrown.
        /// </summary>
        public void PlayThrowSound()
        {
            if (_localAudio != null && throwSound != null)
                _localAudio.PlayOneShot(throwSound);
        }
    }
}
