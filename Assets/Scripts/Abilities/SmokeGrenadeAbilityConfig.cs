using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Config for the Smoke Grenade ability.
    /// Holds every prefab and value the ability needs.
    /// </summary>
    [CreateAssetMenu(fileName = "SmokeGrenadeAbilityConfig", menuName = "Artisans Guns/Abilities/Smoke Grenade")]
    public class SmokeGrenadeAbilityConfig : AbilityConfig
    {
        [Header("FPV Grenade Prefab (WeaponHolder)")]
        [Tooltip("Prefab spawned in WeaponHolder — contains Animator, RightHandGrip, LeftHandGrip and SmokeGrenadeAbility script")]
        public GameObject grenadeFPVPrefab;

        [Tooltip("AnimatorController applied to Spine2 (handsAnimator) while grenade is equipped")]
        public RuntimeAnimatorController grenadesHandsAnimatorController;

        [Header("Projectile")]
        [Tooltip("Prefab instantiated at AbilitySpawner when the throw animation fires OnThrowGrenade event")]
        public GameObject grenadeProjectilePrefab;

        [Tooltip("Speed (m/s) at which the grenade projectile travels")]
        public float throwSpeed = 14f;

        [Header("Smoke")]
        [Tooltip("CrimsonSmoke prefab instantiated when the grenade hits an Environment surface")]
        public GameObject smokePrefab;

        [Tooltip("How many seconds the smoke lasts")]
        public float smokeDuration = 8f;

        [Header("Charges")]
        [Tooltip("Max number of charges the player starts with (and resets to on kill)")]
        [Range(1, 4)]
        public int maxCharges = 2;

        [Header("TPV — visible to other players")]
        [Tooltip("Grenade prefab shown in the TPV weapon holder for other players. Must have LeftGrip and RightGrip children.")]
        public GameObject grenadePrefabTPV;

        [Tooltip("AnimatorController applied to Spine2 (upperBodyAnimator) while the grenade is equipped in TPV")]
        public RuntimeAnimatorController postureAnimatorControllerTPV;
    }
}
