using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using ArtisansGuns.Weapons;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// GameUIManager - Manages gameplay HUD (ammo, weapon icons, health, etc.)
    /// NetworkBehaviour to sync UI state across clients (local player only sees their own UI)
    /// Delegates all visual updates to MobileControlsController (UIToolkit-based HUD).
    /// </summary>
    public class GameUIManager : NetworkBehaviour
    {
        public static GameUIManager Instance { get; private set; }
        
        [Header("References")]
        private PlayerSetup playerSetup;
        
        [Header("Weapon State")]
        private WeaponConfig primaryWeapon;
        private WeaponConfig secondaryWeapon;
        private bool isPrimaryEquipped = true;
        
        private void Awake()
        {
            // DO NOT use singleton Destroy(gameObject) here!
            // This script lives on the PlayerPrefab - destroying gameObject
            // would destroy the entire remote player's NetworkObject.
            // Instead, just track the local player's instance.
        }
        
        public override void Spawned()
        {
            // Only setup for local player
            if (!Object.HasInputAuthority)
            {
                enabled = false; // Disable component for remote players
                return; // Remote players don't need UI
            }
            
            // Singleton: only the local player's instance is tracked
            Instance = this;
            
            // Check if we're in a game scene (not Room or Lobby)
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "Room" || currentScene == "Lobby")
            {
                // Debug.Log($"ðŸŽ® [GameUIManager] Player spawned in {currentScene}, skipping UI setup (no gameplay UI needed)");
                return; // Don't setup UI in lobby/room scenes
            }
            
            // Debug.Log($"ðŸŽ® [GameUIManager] Starting UI setup for local player in {currentScene}...");
            
            // Find PlayerSetup component on local player
            playerSetup = GetComponent<PlayerSetup>();
            if (playerSetup == null)
            {
                return;
            }
            
            // Debug.Log("âœ… [GameUIManager] Initialized for local player");
        }
        
        /// <summary>
        /// Initialize UI with weapon configs from loadout.
        /// Delegates to MobileControlsController for UIToolkit HUD.
        /// </summary>
        public void InitializeWeapons(WeaponConfig primary, WeaponConfig secondary)
        {
            primaryWeapon   = primary;
            secondaryWeapon = secondary;
            isPrimaryEquipped = true;
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl == null) return;
            if (primary   != null) ctrl.SetPrimaryWeapon(primary.whiteIcon,   primary.maxAmmo);
            if (secondary != null) ctrl.SetSecondaryWeapon(secondary.whiteIcon, secondary.maxAmmo);
            ctrl.UpdatePrimaryAmmo(primary?.maxAmmo ?? 0);
            ctrl.SetActiveWeapon(true);
        }

        /// <summary>
        /// Update ammo display. Called by FireWeapon.NotifyAmmoChanged().
        /// </summary>
        public void UpdateAmmoDisplay(int currentAmmo, int maxAmmo)
        {
            var ctrl = ArtisansGuns.UI.MobileControlsController.Instance;
            if (ctrl == null) return;
            if (isPrimaryEquipped)
                ctrl.UpdatePrimaryAmmo(currentAmmo);
            else
                ctrl.UpdateSecondaryAmmo(currentAmmo);
        }

        /// <summary>Get currently equipped weapon config.</summary>
        public WeaponConfig GetEquippedWeapon()
        {
            return isPrimaryEquipped ? primaryWeapon : secondaryWeapon;
        }

        /// <summary>Called when player picks up a weapon (future system).</summary>
        public void OnWeaponPickedUp(WeaponConfig weapon, bool isSecondary)
        {
            if (isSecondary) secondaryWeapon = weapon;
            else             primaryWeapon   = weapon;
            InitializeWeapons(primaryWeapon, secondaryWeapon);
        }
    }
}
