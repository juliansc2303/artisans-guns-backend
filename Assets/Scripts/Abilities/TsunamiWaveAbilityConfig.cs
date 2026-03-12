using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Config for Pato's Ability 1 — Tsunami Wave.
    ///
    /// The player casts the wave while grounded. It erupts from below,
    /// carries the caster on top (like a surfboard/sled), travels forward
    /// for <see cref="waveDuration"/> seconds, then disappears.
    ///
    /// Bullets collide with the Water layer (acts as a mobile shield).
    /// Other players/environment pass through it (layer collision matrix).
    /// </summary>
    [CreateAssetMenu(fileName = "TsunamiWaveAbilityConfig", menuName = "Artisans Guns/Abilities/Tsunami Wave")]
    public class TsunamiWaveAbilityConfig : AbilityConfig
    {
        [Header("Wave Prefab")]
        [Tooltip("TsunamiVFX prefab to spawn. Must have colliders on Water layer.")]
        public GameObject wavePrefab;

        [Header("Wave Movement")]
        [Tooltip("Forward speed of the wave (units/s)")]
        public float waveSpeed = 14f;

        [Tooltip("How long the wave travels before disappearing (seconds)")]
        public float waveDuration = 3f;

        [Tooltip("Vertical offset below the spawn point where the wave starts " +
                 "(it rises up into position). Positive = further below.")]
        public float riseFromBelow = 3f;

        [Tooltip("How fast the wave rises from below to ride height (units/s)")]
        public float riseSpeed = 12f;

        [Header("Rider Settings")]
        [Tooltip("Height offset above the wave's Y where the rider stands")]
        public float riderHeightOffset = 1.0f;

        [Header("Audio")]
        [Tooltip("Sound played when the wave spawns")]
        public AudioClip spawnSound;
    }
}
