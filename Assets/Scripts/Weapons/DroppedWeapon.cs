using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ArtisansGuns.Weapons
{
    /// <summary>
    /// Component placed on a dropped weapon in the world.
    /// 
    /// The TPV weapon prefab should have a solid collider (BoxCollider, isTrigger=false,
    /// DISABLED) for ground physics.  A fallback BoxCollider is added if none is found.
    /// Pickup detection uses Physics.OverlapSphere (not triggers), so it works
    /// regardless of the layer collision matrix.
    ///
    /// Lifecycle:
    ///   1. Spawned by WeaponDropSystem via RPC (plain Instantiate, not networked).
    ///   2. Falls with Rigidbody + solid collider until it lands.
    ///   3. OverlapSphere detects nearby players for pickup.
    ///   4. If picked up → RPC destroys on all clients. If 15 s expire → auto-destroyed.
    /// </summary>
    public class DroppedWeapon : MonoBehaviour
    {
        // ── Drop data (set by spawner) ─────────────────────────────────
        [HideInInspector] public string weaponConfigId;
        [HideInInspector] public int    ammoCount;
        [HideInInspector] public bool   isPrimarySlot;
        [HideInInspector] public uint   dropId;
        [HideInInspector] public int    dropperPlayerId = -1; // prevents dropper from auto-picking

        // ── Cross-client registry (keyed by dropId) ───────────────────
        private static readonly Dictionary<uint, DroppedWeapon> _registry = new();

        // ── Config ─────────────────────────────────────────────────────
        private const float DESPAWN_TIME            = 15f;
        private const float PICKUP_COOLDOWN         = 0.5f;  // non-dropper
        private const float DROPPER_PICKUP_COOLDOWN = 0.69f; // dropper can re-pick quickly
        private const float DROP_FORWARD_FORCE      = 4f;
        private const float DROP_UPWARD_FORCE       = 2.5f;
        private const float WEAPON_SCALE            = 4.0f;
        private const float DETECT_RADIUS           = 2.0f;  // OverlapSphere radius

        // ── Runtime ────────────────────────────────────────────────────
        private Rigidbody _rb;
        private float     _spawnTime;
        private float     _pickupEnableTime;
        private float     _dropperPickupEnableTime;
        private bool      _pickedUp;

        /// <summary>
        /// The nearest DroppedWeapon to the local player (or null).
        /// </summary>
        public static DroppedWeapon NearbyWeapon { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // Initialization
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called right after Instantiate.  Enables prefab colliders, adds
        /// Rigidbody, applies impulse in the dropper's aim direction.
        /// </summary>
        public void Initialize(Vector3 dropPosition, Vector3 aimDirection)
        {
            transform.position = dropPosition;
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // ── Scale up for world visibility ──────────────────────────
            transform.localScale *= WEAPON_SCALE;

            // ── Rigidbody ──────────────────────────────────────────────
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
                _rb = gameObject.AddComponent<Rigidbody>();
            _rb.mass                   = 1.5f;
            _rb.linearDamping          = 0.5f;
            _rb.angularDamping         = 1f;
            _rb.useGravity             = true;
            _rb.isKinematic            = false;
            _rb.interpolation          = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // ── Enable prefab colliders for ground physics ─────────────
            bool hasPhysicsCollider = false;
            foreach (var col in GetComponentsInChildren<Collider>(true))
            {
                col.enabled = true;
                if (!col.isTrigger) hasPhysicsCollider = true;
            }
            if (!hasPhysicsCollider)
                gameObject.AddComponent<BoxCollider>();

            // ── Layer: "Weapon" — only collides with environment (Default)
            // in the matrix.  Pickup detection uses OverlapSphere (ignores matrix).
            int layer = LayerMask.NameToLayer("Weapon");
            if (layer >= 0)
                SetLayerRecursive(gameObject, layer);

            // ── Impulse in the player's aim direction ──────────────────
            Vector3 dir   = aimDirection.normalized;
            Vector3 force = dir * DROP_FORWARD_FORCE + Vector3.up * DROP_UPWARD_FORCE;
            _rb.AddForce(force, ForceMode.Impulse);
            _rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);

            // ── Timers ─────────────────────────────────────────────────
            _spawnTime               = Time.time;
            _pickupEnableTime        = _spawnTime + PICKUP_COOLDOWN;
            _dropperPickupEnableTime = _spawnTime + DROPPER_PICKUP_COOLDOWN;

            // ── Registry ───────────────────────────────────────────────
            if (dropId != 0)
                _registry[dropId] = this;
        }

        // ────────────────────────────────────────────────────────────────
        // Tick
        // ────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_pickedUp) return;

            if (Time.time - _spawnTime >= DESPAWN_TIME)
            {
                CleanupAndDestroy();
                return;
            }

            DetectNearbyPlayers();
        }

        // ────────────────────────────────────────────────────────────────
        // Proximity detection (OverlapSphere — ignores layer collision matrix)
        // ────────────────────────────────────────────────────────────────

        private void DetectNearbyPlayers()
        {
            Collider[] hits = Physics.OverlapSphere(
                transform.position, DETECT_RADIUS,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            bool localPlayerInRange = false;

            foreach (var hit in hits)
            {
                // Skip own colliders
                if (hit.transform.IsChildOf(transform)) continue;

                var setup = hit.GetComponentInParent<ArtisansGuns.Game.PlayerSetup>();
                if (setup == null) continue;
                if (setup.Object == null || !setup.Object.HasInputAuthority) continue;

                var health = setup.GetComponent<ArtisansGuns.Game.PlayerHealth>();
                if (health != null && health.IsDead) continue;

                localPlayerInRange = true;

                // Cooldown — longer for the dropper to prevent instant re-grab
                int localPlayerId = setup.Object.InputAuthority.PlayerId;
                bool isDropper = (localPlayerId == dropperPlayerId);
                float cooldownEnd = isDropper ? _dropperPickupEnableTime : _pickupEnableTime;
                if (Time.time < cooldownEnd) continue;

                // Try auto-pickup if matching slot is empty
                var dropSys = setup.GetComponent<WeaponDropSystem>();
                if (dropSys != null && dropSys.TryAutoPickup(this))
                    return;

                // Slot occupied → mark as nearby for Pick button
                NearbyWeapon = this;
                return;
            }

            if (!localPlayerInRange && NearbyWeapon == this)
                NearbyWeapon = null;
        }

        // ────────────────────────────────────────────────────────────────
        // Pickup
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Marks this weapon as picked up. Returns data + true if successful,
        /// or (null, 0, false, false) if already consumed.
        /// Does NOT destroy — the RPC handles destruction on all clients.
        /// </summary>
        public (string configId, int ammo, bool isPrimary, bool success) Pickup()
        {
            if (_pickedUp) return (null, 0, false, false);

            _pickedUp = true;
            if (NearbyWeapon == this)
                NearbyWeapon = null;

            return (weaponConfigId, ammoCount, isPrimarySlot, true);
        }

        // ────────────────────────────────────────────────────────────────
        // Cleanup / Registry
        // ────────────────────────────────────────────────────────────────

        private void CleanupAndDestroy()
        {
            if (NearbyWeapon == this)
                NearbyWeapon = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (NearbyWeapon == this)
                NearbyWeapon = null;
            if (dropId != 0)
                _registry.Remove(dropId);
        }

        public static DroppedWeapon FindById(uint id)
        {
            return id != 0 && _registry.TryGetValue(id, out var dw) ? dw : null;
        }

        public static void DestroyById(uint id)
        {
            var dw = FindById(id);
            if (dw != null) Destroy(dw.gameObject);
        }

        /// <summary>Destroys every dropped weapon in the scene and clears the registry.</summary>
        public static void DestroyAll()
        {
            NearbyWeapon = null;
            // Copy keys to avoid modifying dict during iteration (OnDestroy removes entries)
            var ids = new List<uint>(_registry.Keys);
            foreach (var id in ids)
            {
                if (_registry.TryGetValue(id, out var dw) && dw != null)
                    Destroy(dw.gameObject);
            }
            _registry.Clear();
        }

        // ────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
