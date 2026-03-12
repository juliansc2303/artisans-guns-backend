using UnityEngine;
using ArtisansGuns.Abilities;

namespace ArtisansGuns.Characters
{
    /// <summary>
    /// CharacterConfig — ScriptableObject that describes a playable character.
    ///
    /// Add one per character (e.g., Crimson) under Resources/Characters/
    /// so CharacterSetupHandler can load it by character ID.
    ///
    /// Mesh setup:
    ///   tpvMesh  → assigned to the PlayerTPV   SkinnedMeshRenderer
    ///   armsMesh → assigned to the ARMS (FPV)  SkinnedMeshRenderer
    ///
    /// Abilities:
    ///   ability1 → bound to Ability1Button in the HUD
    ///   ability2 → bound to Ability2Button in the HUD
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterConfig_Crimson", menuName = "Artisans Guns/Character Config")]
    public class CharacterConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Must match the character ID in the backend loadout (lowercase, e.g. 'crimson')")]
        public string characterId = "crimson";

        [Header("Meshes")]
        [Tooltip("Mesh applied to the PlayerTPV SkinnedMeshRenderer (third-person body visible to others)")]
        public Mesh tpvMesh;

        [Tooltip("Materials applied to the PlayerTPV SkinnedMeshRenderer (must match sub-mesh count)")]
        public Material[] tpvMaterials;

        [Tooltip("Mesh applied to the ARMS SkinnedMeshRenderer (first-person arms, visible only to local player)")]
        public Mesh armsMesh;

        [Tooltip("Materials applied to the ARMS SkinnedMeshRenderer (must match sub-mesh count)")]
        public Material[] armsMaterials;

        [Header("Abilities")]
        [Tooltip("Config for the first ability (Ability1Button)")]
        public AbilityConfig ability1;

        [Tooltip("Config for the second ability (Ability2Button)")]
        public AbilityConfig ability2;

        [Header("Ultimate")]
        [Tooltip("Config for the ultimate ability — charged by 5 kills via ComboKillManager")]
        public AbilityConfig ultimate;

        [Header("Death")]
        [Tooltip("Visual Effect Graph prefab spawned at the player position on death (e.g. dissolve / particles)")]
        public GameObject deathVFXPrefab;

        [Tooltip("How long the death VFX lives before being destroyed (seconds)")]
        public float deathVFXDuration = 3f;
    }
}
