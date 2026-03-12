using UnityEngine;
using ArtisansGuns.Data;

namespace ArtisansGuns.Data
{
    /// <summary>
    /// Extended weapon definition for TALON-AR rifle
    /// This shows how to configure individual weapons
    /// </summary>
    [CreateAssetMenu(fileName = "TalonAR_Config", menuName = "Artisans Guns/Legacy Weapon Config (unused)")]
    public class WeaponConfigLegacy : ScriptableObject
    {
        [Header("Weapon Identity")]
        public string weaponId = "talon_ar";
        public string displayName = "TALON-AR";
        public WeaponDefinition.WeaponCategory category = WeaponDefinition.WeaponCategory.Primary;
        
        [Header("Combat Stats")]
        public float damage = 35f;
        public float fireRate = 600f; // RPM
        public float range = 100f;
        public float accuracy = 0.85f;
        public bool isAutomatic = true;
        
        [Header("Handling")]
        public float reloadTime = 2.5f;
        public int magazineSize = 30;
        public int maxAmmo = 120;
        public float adsTime = 0.3f; // Aim down sights time
        
        [Header("Recoil Pattern")]
        public AnimationCurve horizontalRecoil;
        public AnimationCurve verticalRecoil;
        public float recoilStrength = 1f;
        public float recoilRecoverySpeed = 5f;
        
        [Header("Audio")]
        public AudioClip fireSound;
        public AudioClip reloadSound;
        public AudioClip emptyClickSound;
        public AudioClip adsSound;
        
        [Header("Visual Effects")]
        public GameObject muzzleFlashPrefab;
        public GameObject shellEjectPrefab;
        public GameObject impactEffectPrefab;
        
        [Header("IK Configuration")]
        public Vector3 leftHandPosition = new Vector3(0.2f, 0.1f, 0.5f);
        public Vector3 leftHandRotation = new Vector3(-10f, 0f, 0f);
        public Vector3 rightHandPosition = Vector3.zero;
        public Vector3 rightHandRotation = Vector3.zero;
        
        /// <summary>
        /// Convert to WeaponDefinition.Weapon format for compatibility
        /// </summary>
        public WeaponDefinition.Weapon ToWeaponDefinition()
        {
            return new WeaponDefinition.Weapon(
                this.weaponId,
                this.displayName,
                this.category,
                $"WeaponIcons/{weaponId}",
                false
            );
        }
    }
}
