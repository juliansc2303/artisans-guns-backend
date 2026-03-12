using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArtisansGuns.Data
{
    /// <summary>
    /// Static weapon definitions and registry
    /// Contains all available weapons in the game
    /// </summary>
    public static class WeaponDefinition
    {
        public enum WeaponCategory
        {
            Primary,
            Secondary,
            Knife
        }

        [Serializable]
        public class Weapon
        {
            public string weaponId;        // e.g. "talon_ar", "bolt"
            public string displayName;     // e.g. "TALON-AR", "BOLT"
            public WeaponCategory category;
            public string iconPath;        // Path in Resources, e.g. "Icons/Talon-ARIcon"
            public bool isDefault;         // If true, player starts with this weapon

            public Weapon(string id, string name, WeaponCategory cat, string icon, bool defaultWeapon = false)
            {
                weaponId = id;
                displayName = name;
                category = cat;
                iconPath = icon;
                isDefault = defaultWeapon;
            }
        }

        // ===================================
        // WEAPON REGISTRY
        // ===================================

        private static readonly List<Weapon> allWeapons = new List<Weapon>
        {
            // PRIMARY WEAPONS
            new Weapon("talon_ar", "TALON-AR", WeaponCategory.Primary, "Icons/Talon-ARIcon", defaultWeapon: true),

            // SECONDARY WEAPONS
            new Weapon("bolt", "BOLT", WeaponCategory.Secondary, "Icons/BoltIcon", defaultWeapon: true),
        };

        /// <summary>
        /// Get all weapons of a specific category
        /// </summary>
        public static List<Weapon> GetWeaponsByCategory(WeaponCategory category)
        {
            return allWeapons.FindAll(w => w.category == category);
        }

        /// <summary>
        /// Get weapon by ID
        /// </summary>
        public static Weapon GetWeaponById(string weaponId)
        {
            return allWeapons.Find(w => w.weaponId == weaponId);
        }

        /// <summary>
        /// Get default weapon for a category
        /// </summary>
        public static Weapon GetDefaultWeapon(WeaponCategory category)
        {
            var weapon = allWeapons.Find(w => w.category == category && w.isDefault);
            if (weapon == null)
            {
                // Fallback to first weapon in category
                var categoryWeapons = GetWeaponsByCategory(category);
                weapon = categoryWeapons.Count > 0 ? categoryWeapons[0] : null;
            }
            return weapon;
        }

        /// <summary>
        /// Get all weapons
        /// </summary>
        public static List<Weapon> GetAllWeapons()
        {
            return new List<Weapon>(allWeapons);
        }
    }
}
