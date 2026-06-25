using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Config for the Dash ability.
    ///
    /// The player dashes almost instantly in the direction they are currently
    /// moving.  If standing still, they dash forward.  Physics-based movement
    /// via CharacterController.Move ensures wall collision — no clipping.
    /// </summary>
    [CreateAssetMenu(fileName = "DashAbilityConfig", menuName = "Artisans Guns/Abilities/Dash")]
    public class DashAbilityConfig : AbilityConfig
    {
        [Header("Dash Settings")]
        [Tooltip("Total distance the dash covers (units / metres)")]
        public float dashDistance = 8f;

        [Tooltip("Duration of the dash in seconds (lower = faster / snappier)")]
        public float dashDuration = 0.15f;

        [Header("Audio")]
        [Tooltip("Sound played when the dash activates")]
        public AudioClip dashSound;

        [Tooltip("Volume of the dash sound (0-1)")]
        [Range(0f, 1f)]
        public float dashSoundVolume = 1f;
    }
}
