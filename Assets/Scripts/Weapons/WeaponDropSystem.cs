using UnityEngine;
using Fusion;
using ArtisansGuns.Weapons;
using ArtisansGuns.Game;

namespace ArtisansGuns.Weapons
{
    /// <summary>
    /// Handles weapon drop and pick-up logic for one player.
    /// Add this component to the same PlayerPrefab that has PlayerSetup.
    ///
    /// Slot rules:
    ///   • Slot 1 = primary weapons ONLY.  Slot 2 = secondary weapons ONLY.
    ///   • If the matching slot is empty AND player is NOT the dropper → auto-pickup.
    ///   • If both slots occupied → "Pick" button appears.
    ///   • Pick with same-type equipped → SWAP (drop old, equip new in hand).
    ///   • Pick with different-type equipped → just slot it (no equip).
    ///
    /// Drop spawns from abilitySpawner and flies in the camera's aim direction.
    /// </summary>
    public class WeaponDropSystem : NetworkBehaviour
    {
        // ── Cached refs ────────────────────────────────────────────────
        private PlayerSetup  _setup;
        private PlayerHealth _health;

        // ── Original loadout (restored on respawn) ─────────────────────
        private WeaponConfig _originalPrimary;
        private WeaponConfig _originalSecondary;

        // ── Pickup HUD visibility ──────────────────────────────────────
        private bool _pickButtonVisible;

        // ── Unique ID generator for cross-client drop sync ─────────────
        private ushort _dropCounter;
        private uint NextDropId()
        {
            _dropCounter++;
            int playerId = Object != null ? Object.InputAuthority.PlayerId : 0;
            return ((uint)playerId << 16) | _dropCounter;
        }

        // ────────────────────────────────────────────────────────────────
        // Lifecycle
        // ────────────────────────────────────────────────────────────────

        public override void Spawned()
        {
            if (!Object.HasInputAuthority)
            {
                enabled = false;
                return;
            }

            _setup  = GetComponent<PlayerSetup>();
            _health = GetComponent<PlayerHealth>();

            ArtisansGuns.UI.MobileControlsController.OnDropWeapon += OnDropPressed;
            ArtisansGuns.UI.MobileControlsController.OnPickWeapon += OnPickPressed;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            ArtisansGuns.UI.MobileControlsController.OnDropWeapon -= OnDropPressed;
            ArtisansGuns.UI.MobileControlsController.OnPickWeapon -= OnPickPressed;
        }

        public void CacheOriginalLoadout(WeaponConfig primary, WeaponConfig secondary)
        {
            _originalPrimary   = primary;
            _originalSecondary = secondary;
        }

        // ────────────────────────────────────────────────────────────────
        // Update — manage Pick button visibility
        // ────────────────────────────────────────────────────────────────

        private void Update()
        {
            bool nearbyExists = DroppedWeapon.NearbyWeapon != null;
            bool bothOccupied = _setup.primaryWeaponConfig != null
                             && _setup.secondaryWeaponConfig != null;
            bool shouldShow   = nearbyExists && bothOccupied;

            if (shouldShow && !_pickButtonVisible)
            {
                _pickButtonVisible = true;
                var dw = DroppedWeapon.NearbyWeapon;
                string weaponId = dw != null ? dw.weaponConfigId : null;
                ArtisansGuns.UI.MobileControlsController.Instance?.ShowPickButton(true, weaponId);
            }
            else if (!shouldShow && _pickButtonVisible)
            {
                _pickButtonVisible = false;
                ArtisansGuns.UI.MobileControlsController.Instance?.ShowPickButton(false, null);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // AUTO-PICKUP (called from DroppedWeapon.OnTriggerStay)
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to auto-pickup the dropped weapon if the matching slot is empty.
        /// Both non-droppers and the dropper (after a longer cooldown) can auto-pickup.
        /// Returns true if pickup succeeded.
        /// </summary>
        public bool TryAutoPickup(DroppedWeapon dw)
        {
            if (_setup == null || dw == null) return false;
            if (_health != null && _health.IsDead) return false;

            WeaponConfig config = LoadWeaponConfigById(dw.weaponConfigId);
            if (config == null) return false;

            bool targetPrimary = config.isPrimary;

            // Only auto-pickup if the matching slot is empty
            WeaponConfig slotConfig = targetPrimary
                ? _setup.primaryWeaponConfig
                : _setup.secondaryWeaponConfig;
            if (slotConfig != null) return false;

            // ── Slot is empty: auto-pickup ────────────────────────────
            uint pickupId = dw.dropId;
            var (_, ammo, _, success) = dw.Pickup();
            if (!success) return false;

            bool shouldEquip = ShouldEquipOnPickup(targetPrimary);
            if (shouldEquip)
                _setup.EquipPickedWeapon(config, ammo, targetPrimary);
            else
                _setup.SlotWeapon(config, ammo, targetPrimary);

            RPC_PickupDroppedWeapon(pickupId);
            return true;
        }

        // ────────────────────────────────────────────────────────────────
        // DROP
        // ────────────────────────────────────────────────────────────────

        /// <summary>Gets the spawn position for dropped weapons (abilitySpawner or fallback).</summary>
        private Vector3 GetDropSpawnPosition()
        {
            if (_setup != null && _setup.abilitySpawner != null)
                return _setup.abilitySpawner.position;
            return transform.position + Vector3.up * 1.2f;
        }

        /// <summary>Gets camera aim direction (full 3D, including vertical).</summary>
        private Vector3 GetAimDirection()
        {
            if (Camera.main != null)
                return Camera.main.transform.forward;
            return transform.forward;
        }

        private void OnDropPressed()
        {
            if (_setup == null) return;
            if (_health != null && _health.IsDead) return;

            bool isPrimaryEquipped = _setup.IsPrimaryEquipped;
            bool isKnifeEquipped   = _setup.IsKnifeEquipped;
            if (isKnifeEquipped) return;

            WeaponConfig dropConfig = isPrimaryEquipped
                ? _setup.primaryWeaponConfig
                : _setup.secondaryWeaponConfig;
            if (dropConfig == null || dropConfig.isKnife) return;

            int dropAmmo = _setup.GetCurrentAmmo();

            _setup.DropCurrentWeapon(isPrimaryEquipped);

            Vector3 dropPos = GetDropSpawnPosition();
            Vector3 aimDir  = GetAimDirection();
            int myPlayerId  = Object.InputAuthority.PlayerId;

            RPC_SpawnDroppedWeapon(dropConfig.weaponId, dropAmmo, dropConfig.isPrimary,
                                  dropPos, aimDir, NextDropId(), myPlayerId);
        }

        public void DropAllWeaponsOnDeath()
        {
            if (_setup == null) return;

            // Only drop the weapon the player had in hand when they died
            bool isPrimary = _setup.IsPrimaryEquipped;
            bool isKnife   = _setup.IsKnifeEquipped;
            if (isKnife) return; // knife is never dropped

            WeaponConfig dropConfig = isPrimary
                ? _setup.primaryWeaponConfig
                : _setup.secondaryWeaponConfig;
            if (dropConfig == null || dropConfig.isKnife) return;

            int ammo       = _setup.GetCurrentAmmo();
            Vector3 dropPos = GetDropSpawnPosition();
            Vector3 aimDir  = transform.forward;
            int myPlayerId  = Object.InputAuthority.PlayerId;

            RPC_SpawnDroppedWeapon(
                dropConfig.weaponId, ammo, dropConfig.isPrimary,
                dropPos, aimDir, NextDropId(), myPlayerId);
        }

        public void RestoreOriginalLoadout()
        {
            if (_setup == null) return;
            _setup.RestoreLoadout(_originalPrimary, _originalSecondary);
        }

        // ────────────────────────────────────────────────────────────────
        // PICK UP (manual — button press)
        // ────────────────────────────────────────────────────────────────

        private void OnPickPressed()
        {
            if (_setup == null) return;
            if (_health != null && _health.IsDead) return;

            var nearby = DroppedWeapon.NearbyWeapon;
            if (nearby == null) return;

            uint pickupDropId = nearby.dropId;
            var (configId, ammo, _, success) = nearby.Pickup();
            if (!success) return;

            WeaponConfig pickedConfig = LoadWeaponConfigById(configId);
            if (pickedConfig == null)
            {
                Debug.LogWarning($"[WeaponDropSystem] Could not load weapon config for '{configId}'");
                return;
            }

            bool targetPrimary = pickedConfig.isPrimary;

            // If target slot is occupied → drop existing first (swap)
            WeaponConfig existingConfig = targetPrimary
                ? _setup.primaryWeaponConfig
                : _setup.secondaryWeaponConfig;

            if (existingConfig != null && !existingConfig.isKnife)
            {
                int existingAmmo = targetPrimary ? _setup.GetPrimaryAmmo() : _setup.GetSecondaryAmmo();
                _setup.ClearWeaponSlot(targetPrimary);

                Vector3 dropPos = GetDropSpawnPosition();
                Vector3 aimDir  = GetAimDirection();
                int myPlayerId  = Object.InputAuthority.PlayerId;
                RPC_SpawnDroppedWeapon(existingConfig.weaponId, existingAmmo,
                                      existingConfig.isPrimary, dropPos, aimDir,
                                      NextDropId(), myPlayerId);
            }

            bool shouldEquip = ShouldEquipOnPickup(targetPrimary);
            if (shouldEquip)
                _setup.EquipPickedWeapon(pickedConfig, ammo, targetPrimary);
            else
                _setup.SlotWeapon(pickedConfig, ammo, targetPrimary);

            RPC_PickupDroppedWeapon(pickupDropId);
        }

        // ────────────────────────────────────────────────────────────────
        // Shared pickup-equip decision
        // ────────────────────────────────────────────────────────────────

        private bool ShouldEquipOnPickup(bool pickedIsPrimary)
        {
            if (_setup.IsKnifeEquipped) return false;
            return _setup.IsPrimaryEquipped == pickedIsPrimary;
        }

        // ────────────────────────────────────────────────────────────────
        // RPCs
        // ────────────────────────────────────────────────────────────────

        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_SpawnDroppedWeapon(
            string weaponId, int ammo, bool isPrimSlot,
            Vector3 dropPos, Vector3 aimDir, uint dropId, int dropperPlayerId)
        {
            WeaponConfig config = LoadWeaponConfigById(weaponId);
            if (config == null || config.prefabWeaponTPV == null)
            {
                Debug.LogWarning($"[WeaponDropSystem] RPC_SpawnDroppedWeapon: no TPV prefab for '{weaponId}'");
                return;
            }

            GameObject go = Instantiate(config.prefabWeaponTPV);

            // Strip player-attached components (IK, animators)
            foreach (var animator in go.GetComponentsInChildren<Animator>(true))
                Destroy(animator);

            var dw = go.AddComponent<DroppedWeapon>();
            dw.weaponConfigId  = weaponId;
            dw.ammoCount       = ammo;
            dw.isPrimarySlot   = isPrimSlot;
            dw.dropId          = dropId;
            dw.dropperPlayerId = dropperPlayerId;
            dw.Initialize(dropPos, aimDir);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.All)]
        public void RPC_PickupDroppedWeapon(uint dropId)
        {
            DroppedWeapon.DestroyById(dropId);
        }

        // ────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────

        private static WeaponConfig LoadWeaponConfigById(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;
            string resourceName = ConvertIdToResourceName(weaponId);
            return Resources.Load<WeaponConfig>($"Weapons/{resourceName}");
        }

        private static string ConvertIdToResourceName(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return "TalonAR";
            if (weaponId == "talon_ar") return "TalonAR";
            if (weaponId == "bolt")     return "Bolt";
            if (weaponId == "default" || weaponId == "default_knife") return "DefaultKnife";

            // Generic snake_case → PascalCase: "talon_skull" → "TalonSkull"
            string[] parts = weaponId.ToLower().Split('_');
            var sb = new System.Text.StringBuilder();
            foreach (string part in parts)
            {
                if (part.Length > 0)
                    sb.Append(char.ToUpper(part[0])).Append(part.Substring(1));
            }
            return sb.ToString();
        }

        private static string GetWeaponDisplayName(string weaponId)
        {
            var config = LoadWeaponConfigById(weaponId);
            return config != null ? config.weaponName : weaponId;
        }
    }
}
