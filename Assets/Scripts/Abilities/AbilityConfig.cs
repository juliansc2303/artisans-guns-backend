using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Base ScriptableObject for all ability configs.
    /// Each ability type (SmokeGrenade, VisionPulse, ...) derives from this.
    /// </summary>
    public class AbilityConfig : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Display name of this ability")]
        public string abilityName = "Ability";

        [Header("UI")]
        [Tooltip("Icon shown on the ability button in the HUD")]
        public Sprite icon;

        [Header("Cooldown")]
        [Tooltip("Cooldown in seconds after the ability is used")]
        public float cooldownSeconds = 10f;
    }
}
