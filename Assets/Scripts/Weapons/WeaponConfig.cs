using UnityEngine;
using System;

namespace ArtisansGuns.Weapons
{
    /// <summary>
    /// WeaponConfig - ScriptableObject that defines weapon properties
    /// Used to match weapons from backend with in-game prefabs
    /// </summary>
    [CreateAssetMenu(fileName = "New Weapon", menuName = "Artisans Guns/Weapon Config")]
    public class WeaponConfig : ScriptableObject
    {
        [Header("Weapon Identity")]
        [Tooltip("Must match weapon ID from backend (e.g., 'talon_ar', 'bolt')")]
        public string weaponId;
        
        [Tooltip("Display name for UI")]
        public string weaponName;
        
        [Tooltip("Is this a knife/melee weapon? (uses different logic: no firePoint, no reload, infinite ammo, attack animation)")]
        public bool isKnife = false;

        [Tooltip("True if this weapon occupies the PRIMARY slot (e.g. Assault Rifles)")]
        public bool isPrimary = false;

        [Tooltip("True if this weapon occupies the SECONDARY slot (e.g. Pistols)")]
        public bool isSecondary = false;

        [Tooltip("True if this weapon fires multiple pellets per shot (e.g. Shotguns)")]
        public bool isShotgun = false;

        [Header("Shotgun Settings")]
        [Tooltip("Number of pellets per shot. Total damage is divided equally among pellets.")]
        [Range(1, 20)]
        public int pelletCount = 9;

        [Tooltip("Cone half-angle (degrees) for pellet spread.")]
        [Range(0f, 30f)]
        public float shotgunSpreadAngle = 5f;
        
        [Tooltip("Weapon class (AR, Pistol, Sniper, etc.)")]
        public WeaponClass weaponClass = WeaponClass.AssaultRifle;
        
        [Header("UI Icons")]
        [Tooltip("White icon sprite for UI (used in weapon switch button, HUD, etc.)")]
        public Sprite whiteIcon;

        [Tooltip("Kill UI overlay image shown on screen when you get a kill with this weapon.\n" +
                 "If null, falls back to Resources/KillUI/DefaultKillUI.")]
        public Sprite killUISprite;
        
        [Header("Weapon Prefab")]
        [Tooltip("Prefab of the weapon model with FireWeapon and WeaponRecoil scripts")]
        public GameObject weaponPrefab;
        
        [Header("Fire Settings")]
        [Tooltip("Rounds per minute (e.g., 600 = 10 rounds/sec)")]
        public float fireRate = 600f;
        
        [Tooltip("Is this weapon automatic? (hold to fire continuously)")]
        public bool isAutomatic = true;
        
        [Tooltip("Fire sound effect")]
        public AudioClip fireSound;
        
        [Tooltip("Muzzle flash effect (instantiated at firePoint) - FPV / local player only")]
        public GameObject muzzleFlashPrefab;
        
        [Tooltip("Muzzle flash effect for the TPV weapon (visible to other players). If null, falls back to muzzleFlashPrefab.")]
        public GameObject tpvMuzzleFlashPrefab;

        [Tooltip("World-space scale multiplier for the TPV muzzle flash. 1 = default prefab size.")]
        public float tpvMuzzleFlashScale = 1f;

        [Tooltip("TrailRenderer prefab for the TPV bullet trail (visible to other players only).")]
        public GameObject tpvTrailPrefab;

        [Tooltip("Speed at which the TPV trail travels from firepoint to impact (units/s).")]
        public float tpvTrailSpeed = 80f;
        
        [Tooltip("Duration before muzzle flash is destroyed (seconds)")]
        public float muzzleFlashDuration = 0.1f;
        
        [Header("Audio Settings")]
        [Tooltip("Reload sounds (can add as many as needed). Call PlayReloadSound(index) from Animation Events")]
        public AudioClip[] reloadSounds;
        
        [Tooltip("Sound played when trying to fire with empty magazine (plays once per trigger pull)")]
        public AudioClip emptyMagazineSound;
        
        [Header("Ammo Settings")]
        [Tooltip("Maximum ammunition capacity for this weapon")]
        public int maxAmmo = 30;
        
        [Tooltip("Weapon range in meters (use low values for melee like knife, high for guns)")]
        public float bulletRange = 100f;
        
        [Tooltip("Damage per bullet/hit (base damage before modifiers)")]
        public float damage = 25f;

        [Tooltip("Damage multiplier when the bullet hits a collider tagged 'Head'")]
        public float headshotMultiplier = 2.0f;
        
        [Header("Player Movement")]
        [Tooltip("Movement speed multiplier when this weapon is equipped (1.0 = normal, 1.2 = 20% faster, 0.8 = 20% slower to simulate weight)")]
        [Range(0.5f, 1.5f)]
        public float speedMultiplier = 1.0f;
        
        [Header("VFX Settings")]
        [Tooltip("Impact effect prefab (spawned at bullet hit point)")]
        public GameObject impactEffectPrefab;
        
        [Tooltip("Duration before impact effect is destroyed (seconds)")]
        public float impactEffectDuration = 2f;

        [Tooltip("Impact sound played at the hit point in 3D space (heard by all players)")]
        public AudioClip impactSound;

        [Header("Tag-Based Impact Overrides")]
        [Tooltip("Override impact effect and sound for specific tags (e.g. Water, Metal). " +
                 "If a hit object's tag matches an entry here, that effect/sound is used instead of the default.")]
        public TagImpactOverride[] tagImpactOverrides;

        [Tooltip("Fire sound for third-person view (heard by remote players when you shoot). If null, remote players won't hear a fire sound.")]
        public AudioClip fireSoundTPV;
        
        [Header("Recoil Pattern")]
        [Tooltip("Recoil pattern ScriptableObject defining per-shot camera kick sequence, " +
                 "counter-steer settings, and movement modifiers.\n" +
                 "Create via: Assets → Create → Artisans Guns → Recoil Pattern")]
        public RecoilPattern recoilPattern;

        [Header("Camera Recoil Settings (legacy — used as fallback when no RecoilPattern is assigned)")]
        [Tooltip("Vertical camera kick per shot (degrees) - higher = more upward kick")]
        public float recoilKickAmount = 0.5f;
        
        [Tooltip("How quickly the recoil impulse is applied (higher = snappier, lower = smoother)")]
        public float recoilSmoothness = 15f;
        
        [Header("Bullet Spread (movement / airborne)")]
        [Tooltip("Random bullet spread angle (degrees) when the player is moving on the ground.\n" +
                 "0 = perfectly accurate, 2 = moderate spread.")]
        [Range(0f, 100f)]
        public float movementSpreadAngle = 1.5f;
        
        [Tooltip("Random bullet spread angle (degrees) when the player is airborne (jumping/falling).\n" +
                 "Takes priority over movementSpreadAngle — they are NOT summed.")]
        [Range(0f, 100f)]
        public float jumpSpreadAngle = 3f;
        
        [Header("Visual Weapon Recoil")]
        [Tooltip("Upward force applied to weapon when firing")]
        public float weaponUpForce = 0.08f;
        
        [Tooltip("Backward force applied to weapon when firing")]
        public float weaponBackForce = 0.12f;
        
        [Tooltip("Vertical roll (pitch) applied to weapon")]
        public float weaponVerticalRoll = 5f;
        
        [Tooltip("Horizontal roll (yaw) randomness applied to weapon")]
        public float weaponHorizontalRoll = 2f;
        
        [Tooltip("How quickly weapon returns to original position")]
        public float weaponRecoverySpeed = 8f;
        
        [Tooltip("Visual recoil multiplier when moving (makes weapon shake more)")]
        public float weaponMovingMultiplier = 1.3f;
        
        [Header("In-Game Position Adjustment")]
        [Tooltip("Position offset after weapon is instantiated on WeaponHolder")]
        public Vector3 positionOffset = Vector3.zero;
        
        [Tooltip("Rotation offset after weapon is instantiated on WeaponHolder")]
        public Vector3 rotationOffset = Vector3.zero;
        
        [Tooltip("Scale adjustment for weapon model")]
        public Vector3 scaleMultiplier = Vector3.one;
        
        [Header("Grip Setup")]
        [TextArea(3, 6)]
        [Tooltip("IMPORTANT: Weapon prefab MUST have child GameObjects named 'RightHandGrip' and 'LeftHandGrip'. These will be assigned as IK targets at runtime.")]
        public string gripSetupNote = "âš ï¸ Weapon prefab must contain:\nâ€¢ RightHandGrip (Transform)\nâ€¢ LeftHandGrip (Transform)\n\nPosition these grips during weapon reload animation.\nPlayerSetup will find them by name and connect to IK constraints.";
        
        [Header("Animation Settings")]
        [Tooltip("AnimatorController for character hands (mixamorig:Spine2). Each weapon has unique hand animations.")]
        public RuntimeAnimatorController handsAnimatorController;
        
        [Header("Third Person View (TPV) Settings")]
        [Tooltip("Weapon prefab for third-person view (visible to other players, not local player)")]
        public GameObject prefabWeaponTPV;
        
        [Tooltip("AnimatorController for character hands in TPV (mixamorig:Spine2). Defines arm pose for this weapon in third person.")]
        public RuntimeAnimatorController handsAnimatorControllerTPV;
        
        [TextArea(3, 6)]
        [Tooltip("IMPORTANT: TPV weapon prefab MUST have child GameObjects named 'RightGrip' and 'LeftGrip'. These will be assigned as IK targets at runtime.")]
        public string tpvGripSetupNote = "⚠️ TPV weapon prefab must contain:\n• RightGrip (Transform)\n• LeftGrip (Transform, usually inside Charger object)\n\nPosition these grips during weapon animations.\nPlayerSetup will find them by name and connect to TPV IK constraints.";
        
        [Header("Combo Kill Audio")]
        [Tooltip("Sound played on kills 1-4 of the combo cycle. Pitch rises with each kill.")]
        public AudioClip killSound;

        [Tooltip("Climax sound played on the 5th kill (ultimate activation) and while ultimate is active.")]
        public AudioClip climaxSound;

        [Header("Hit Effects")]
        [Tooltip("Blood effect prefab for headshots (spawned on the victim's TPV)")]
        public GameObject headBloodPrefab;

        [Tooltip("Blood effect prefab for body shots (spawned on the victim's TPV)")]
        public GameObject bodyBloodPrefab;

        [Tooltip("Duration before blood effect is destroyed (seconds)")]
        public float bloodEffectDuration = 3f;

        [Header("Bullet Trail Settings")]
        [Tooltip("Material for the quad trail (use Particles/Unlit Additive or similar).")]
        public Material bulletTrailMaterial;

        [Tooltip("Seconds the full-length trail is visible before it starts shrinking. (~0.04-0.06)")]
        public float trailFlashDuration = 0.05f;

        [Tooltip("Speed at which the muzzle-end collapses toward the impact point (units/s).")]
        public float trailShrinkSpeed = 80f;

        [Tooltip("Visual width of the trail at maximum range (the 'correct' reference size).")]
        public float trailWidthFar  = 2f;

        [Tooltip("Visual width of the trail at point-blank / minimum distance.")]
        public float trailWidthNear = 0.05f;

        // Legacy — hidden to avoid confusion
        [HideInInspector] public float trailWidth = 0.05f;

        // Legacy / unused — kept so existing ScriptableObject assets don't lose data
        [HideInInspector] public float      trailWidthClose    = 0.025f;
        [HideInInspector] public float      trailMidDistance   = 20f;
        [HideInInspector] public float      trailFarDistance   = 80f;
        
        /// <summary>
        /// Weapon class enum
        /// Current implementation: AssaultRifle (TalonAR) only
        /// </summary>
        public enum WeaponClass
        {
            AssaultRifle,  // â† TalonAR (focus)
            // Future classes:
            // Pistol,     // Bolt (secondary weapon)
            // Sniper,
            // Shotgun,
            // SMG
        }
        /// <summary>
        /// Find a tag-based impact override for the given tag.
        /// Returns null if no override matches (use default impact).
        /// </summary>
        public TagImpactOverride GetImpactOverride(string tag)
        {
            if (tagImpactOverrides == null || string.IsNullOrEmpty(tag)) return null;
            foreach (var entry in tagImpactOverrides)
            {
                if (string.Equals(entry.tag, tag, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }
    }

    /// <summary>
    /// Defines an impact effect + sound override for a specific tag.
    /// E.g. "Water" → water sparks + splash sound, "Metal" → spark + clang, etc.
    /// </summary>
    [Serializable]
    public class TagImpactOverride
    {
        [Tooltip("The tag to match (e.g. Water, Metal, Wood)")]
        public string tag;

        [Tooltip("Impact effect prefab for this surface type")]
        public GameObject impactEffectPrefab;

        [Tooltip("Duration before the effect is destroyed")]
        public float impactEffectDuration = 2f;

        [Tooltip("Impact sound for this surface type")]
        public AudioClip impactSound;    }
}
