using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Config for Crimson's Ultimate ability.
    /// Same pattern as SmokeGrenadeAbilityConfig — holds all prefab/value refs.
    /// No cooldown: charged by 5 kills via ComboKillManager.
    /// </summary>
    [CreateAssetMenu(fileName = "CrimsonUltimateAbilityConfig", menuName = "Artisans Guns/Abilities/Crimson Ultimate")]
    public class CrimsonUltimateAbilityConfig : AbilityConfig
    {
        // Ultimate has NO cooldown — charged by kills.
        // We just set cooldownSeconds = 0 in the asset; no field hiding needed
        // ("private new" causes Unity serialization conflict).

        [Header("FPV Prefab (WeaponHolder)")]
        [Tooltip("Prefab spawned in WeaponHolder — contains Animator, RightHandGrip, LeftHandGrip and CrimsonUltimateAbility script")]
        public GameObject ultimateFPVPrefab;

        [Tooltip("AnimatorController applied to Spine2 (handsAnimator) while ultimate item is equipped")]
        public RuntimeAnimatorController crimsonUltimateHandsAnimator;

        [Header("Projectile")]
        [Tooltip("CrimsonUltimateProjectile prefab — launched from AbilitySpawner")]
        public GameObject ultimateProjectilePrefab;

        [Tooltip("Speed (m/s) at which the projectile travels")]
        public float throwSpeed = 14f;

        [Header("Effect")]
        [Tooltip("CrimsonUltimateEffect prefab — the BAM effect spawned after projectile lands")]
        public GameObject ultimateEffectPrefab;

        [Tooltip("Damage dealt to enemies inside the effect radius")]
        public float damage = 80f;

        [Tooltip("How long the BAM effect stays active (seconds)")]
        public float effectDuration = 3f;

        [Tooltip("Seconds after projectile impact before the BAM effect spawns (projectile keeps moving during this time)")]
        public float detonationDelay = 1.5f;

        [Header("TPV — visible to other players")]
        [Tooltip("Prefab shown in the TPV weapon holder. Must have LeftGrip and RightGrip children.")]
        public GameObject ultimatePrefabTPV;

        [Tooltip("AnimatorController applied to Spine2 in TPV while ultimate item is equipped")]
        public RuntimeAnimatorController postureAnimatorControllerTPV;
    }
}
