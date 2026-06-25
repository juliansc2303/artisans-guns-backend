using System;
using System.Collections.Generic;
using System.Linq;

namespace ArtisansGuns.Data
{
    public static class WeaponSkinDefinition
    {
        [Serializable]
        public class WeaponSkin
        {
            public string skinId;
            public string weaponId;
            public string displayName;
            public string iconPath;
            public bool isDefault;
            public int cost;

            public WeaponSkin(string id, string weapon, string name, string icon, bool defaultSkin = false, int skinCost = 0)
            {
                skinId = id;
                weaponId = weapon;
                displayName = name;
                iconPath = icon;
                isDefault = defaultSkin;
                cost = skinCost;
            }
        }

        private static readonly List<WeaponSkin> allWeaponSkins = new List<WeaponSkin>
        {
            // TALON-AR SKINS
            new WeaponSkin("default", "talon_ar", "DEFAULT", "Icons/Talon-ARIcon", defaultSkin: true),
            new WeaponSkin("talon_skull", "talon_ar", "TALON-SKULL", "Icons/TalonSkullIcon", defaultSkin: false, skinCost: 500),

            // BOLT SKINS
            new WeaponSkin("default", "bolt", "DEFAULT", "Icons/BoltIcon", defaultSkin: true),

            // ONYX SKINS
            new WeaponSkin("default", "onyx", "DEFAULT", "Icons/OnyxIcon", defaultSkin: true),

            // TITAN SKINS
            new WeaponSkin("default", "titan", "DEFAULT", "Icons/TitanIcon", defaultSkin: true),
        };

        public static List<WeaponSkin> GetSkinsForWeapon(string weaponId)
        {
            return allWeaponSkins.FindAll(s => s.weaponId == weaponId);
        }

        public static WeaponSkin GetSkin(string weaponId, string skinId)
        {
            return allWeaponSkins.Find(s => s.weaponId == weaponId && s.skinId == skinId);
        }

        public static WeaponSkin GetDefaultSkin(string weaponId)
        {
            var skin = allWeaponSkins.Find(s => s.weaponId == weaponId && s.isDefault);
            if (skin == null)
            {
                var weaponSkins = GetSkinsForWeapon(weaponId);
                skin = weaponSkins.Count > 0 ? weaponSkins[0] : null;
            }
            return skin;
        }
    }
}
