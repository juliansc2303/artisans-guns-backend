using System;
using System.Collections.Generic;

namespace ArtisansGuns.Data
{
    public static class ShopItemDefinition
    {
        public enum CurrencyType
        {
            RivalCoins,
            RivalDiamonds
        }

        [Serializable]
        public class ShopItem
        {
            public string itemId;
            public string weaponId;
            public string skinId;
            public string displayName;
            public string iconPath;
            public int price;
            public CurrencyType currency;

            public ShopItem(string id, string weapon, string skin, string name, string icon, int cost, CurrencyType curr)
            {
                itemId = id;
                weaponId = weapon;
                skinId = skin;
                displayName = name;
                iconPath = icon;
                price = cost;
                currency = curr;
            }
        }

        private static readonly List<ShopItem> allShopItems = new List<ShopItem>
        {
            // TALON-AR SKINS
            new ShopItem("shop_talon_skull", "talon_ar", "talon_skull", "TALON-SKULL", "Icons/TalonSkullIcon", 1000, CurrencyType.RivalCoins),
        };

        public static List<ShopItem> GetAllItems()
        {
            return new List<ShopItem>(allShopItems);
        }

        public static ShopItem GetItem(string itemId)
        {
            return allShopItems.Find(i => i.itemId == itemId);
        }
    }
}
