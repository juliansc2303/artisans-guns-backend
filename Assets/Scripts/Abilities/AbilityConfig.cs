using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Base ScriptableObject for all ability configs.
    /// Each ability type (SmokeGrenade, Dash, ...) derives from this.
    /// </summary>
    public class AbilityConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique ID used to identify this ability in loadouts (e.g. smoke_grenade)")]
        public string abilityId = "";

        [Tooltip("Display name of this ability")]
        public string abilityName = "Ability";

        [Tooltip("Short description for the ability selection UI")]
        public string description = "";

        [Header("UI")]
        [Tooltip("Icon shown on the ability button in the HUD")]
        public Sprite icon;

        [Header("Cooldown")]
        [Tooltip("Cooldown in seconds after the ability is used")]
        public float cooldownSeconds = 10f;

        /// <summary>
        /// Whether this ability is an ultimate (charged by kills, not on cooldown).
        /// </summary>
        public bool IsUltimate => this is CrimsonUltimateAbilityConfig || this is PatoUltimateAbilityConfig;
    }
}
