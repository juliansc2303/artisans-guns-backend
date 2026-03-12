using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Config for Pato's Ultimate ability — Tsunami Flash Wave.
    ///
    /// A massive wave that travels in the caster's look direction (XZ only, full map height).
    /// Enemies caught in the wave are blinded (fog + underwater audio + TPV VFX feedback).
    /// No cooldown — charged by 5 kills via ComboKillManager.
    /// </summary>
    [CreateAssetMenu(fileName = "PatoUltimateAbilityConfig", menuName = "Artisans Guns/Abilities/Pato Ultimate")]
    public class PatoUltimateAbilityConfig : AbilityConfig
    {
        [Header("Wave Prefab")]
        [Tooltip("TsunamiUltimate prefab — must have BoxColliders (IsTrigger) and PatoUltimateWave script")]
        public GameObject wavePrefab;

        [Header("Wave Movement")]
        [Tooltip("Forward speed of the wave (units/s)")]
        public float waveSpeed = 16f;

        [Tooltip("How long the wave travels before disappearing (seconds)")]
        public float waveDuration = 5f;

        [Header("Flash Effect")]
        [Tooltip("Duration of the flash/blind effect on enemies hit by the wave (seconds)")]
        public float flashDuration = 4f;

        [Header("Audio")]
        [Tooltip("Sound played when the wave spawns (2D for caster, 3D for others)")]
        public AudioClip spawnSound;
    }
}
