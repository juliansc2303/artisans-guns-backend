using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Config for Crimson's Vision Pulse ability.
    /// Can only be activated while inside a CrimsonSmoke sphere.
    ///
    /// Effect: briefly lowers the InteriorSmoke Opacity shader property
    /// from 1 → pulseTargetOpacity → 1, giving the local player a brief
    /// glimpse through the smoke without revealing them to enemies outside.
    /// </summary>
    [CreateAssetMenu(fileName = "VisionPulseAbilityConfig", menuName = "Artisans Guns/Abilities/Vision Pulse")]
    public class VisionPulseAbilityConfig : AbilityConfig
    {
        [Header("Pulse Settings")]
        [Tooltip("Opacity value the Interior smoke is lowered to during the pulse (0 = fully transparent, 1 = fully opaque)")]
        [Range(0f, 1f)]
        public float pulseTargetOpacity = 0.9f;

        [Tooltip("Time (seconds) to transition from 1 → pulseTargetOpacity (the full cycle is 2× this)")]
        public float pulseFadeDuration = 0.25f;

        [Tooltip("How long the player stays at pulseTargetOpacity before fading back in")]
        public float pulseHoldDuration = 0.5f;
    }
}
