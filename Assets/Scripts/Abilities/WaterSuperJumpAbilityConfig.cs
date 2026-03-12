using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Config for Pato's Ability 2 — Water Super Jump.
    ///
    /// Can only be activated while the player is standing on a collider
    /// in the Water layer (i.e. riding a Tsunami Wave).
    /// Launches the player high into the air.
    /// </summary>
    [CreateAssetMenu(fileName = "WaterSuperJumpAbilityConfig", menuName = "Artisans Guns/Abilities/Water Super Jump")]
    public class WaterSuperJumpAbilityConfig : AbilityConfig
    {
        [Header("Jump Settings")]
        [Tooltip("Upward velocity applied when super-jumping (units/s)")]
        public float jumpForce = 18f;

        [Header("Audio")]
        [Tooltip("Sound played when the super jump is activated")]
        public AudioClip jumpSound;
    }
}
