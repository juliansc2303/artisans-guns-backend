using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArtisansGuns.Data
{
    /// <summary>
    /// Knife skin definitions and registry
    /// Contains all available knife skins in the game
    /// </summary>
    public static class KnifeSkinDefinition
    {
        [Serializable]
        public class KnifeSkin
        {
            public string skinId;          // e.g. "default", "dragon", "karambit"
            public string displayName;     // e.g. "DEFAULT", "DRAGON BLADE", "KARAMBIT"
            public string iconPath;        // Path in Resources, e.g. "Icons/Knives/DefaultKnife"
            public bool isDefault;         // If true, player starts with this skin
            public int cost;               // Blue Points cost (0 if default or already unlocked)

            public KnifeSkin(string id, string name, string icon, bool defaultSkin = false, int skinCost = 0)
            {
                skinId = id;
                displayName = name;
                iconPath = icon;
                isDefault = defaultSkin;
                cost = skinCost;
            }
        }

        // ===================================
        // KNIFE SKIN REGISTRY
        // ===================================

        private static readonly List<KnifeSkin> allKnifeSkins = new List<KnifeSkin>
        {
            // DEFAULT KNIFE (always unlocked)
            new KnifeSkin("default", "DEFAULT", "Icons/Knives/DefaultKnife", defaultSkin: true, skinCost: 0),
            
            // FUTURE SKINS (purchasable/unlockable)
            // new KnifeSkin("dragon", "DRAGON BLADE", "Icons/Knives/DragonKnife", defaultSkin: false, skinCost: 1000),
            // new KnifeSkin("karambit", "KARAMBIT", "Icons/Knives/KarambitKnife", defaultSkin: false, skinCost: 1500),
        };

        /// <summary>
        /// Get all knife skins
        /// </summary>
        public static List<KnifeSkin> GetAllKnifeSkins()
        {
            return new List<KnifeSkin>(allKnifeSkins);
        }

        /// <summary>
        /// Get knife skin by ID
        /// </summary>
        public static KnifeSkin GetKnifeSkinById(string skinId)
        {
            return allKnifeSkins.Find(s => s.skinId == skinId);
        }

        /// <summary>
        /// Get default knife skin
        /// </summary>
        public static KnifeSkin GetDefaultKnifeSkin()
        {
            var skin = allKnifeSkins.Find(s => s.isDefault);
            if (skin == null)
            {
                // Fallback to first skin
                skin = allKnifeSkins.Count > 0 ? allKnifeSkins[0] : null;
            }
            return skin;
        }
    }
}
