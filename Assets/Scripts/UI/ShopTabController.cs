using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using System.Text;
using UnityEngine.Networking;
using ArtisansGuns.Auth;
using ArtisansGuns.Data;
using ArtisansGuns.Managers;
using static ArtisansGuns.Managers.LocalizationManager;

namespace ArtisansGuns.UI
{
    public class ShopTabController : MonoBehaviour
    {
        private VisualElement shopContent;
        private ScrollView shopGrid;
        private Button buyButton;
        private Label selectedItemLabel;
        [SerializeField] private UIDocument uiDocument;

        // Subtab buttons
        private Button weaponsSubtab;
        private Button hatsSubtab;
        private string activeSubtab = "weapons"; // "weapons" or "hats"

        // Weapon selection
        private ShopItemDefinition.ShopItem selectedWeaponItem;
        private VisualElement selectedWeaponCard;

        // Hat selection
        private HatDefinition selectedHatItem;
        private VisualElement selectedHatCard;

        private const string BASE_URL = "https://ryvalen.onrender.com/api";
        private const int REQUEST_TIMEOUT = 120;

        private void OnEnable()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
                uiDocument = GetComponentInChildren<UIDocument>();
            if (uiDocument == null || uiDocument.rootVisualElement == null) return;

            var root = uiDocument.rootVisualElement;
            shopContent = root.Q<VisualElement>("ShopContent");
            shopGrid = root.Q<ScrollView>("ShopGrid");
            buyButton = root.Q<Button>("ShopBuyButton");
            selectedItemLabel = root.Q<Label>("ShopSelectedItemLabel");
            weaponsSubtab = root.Q<Button>("ShopWeaponsSubtab");
            hatsSubtab = root.Q<Button>("ShopHatsSubtab");

            if (buyButton != null)
                buyButton.RegisterCallback<ClickEvent>(evt => OnBuyClicked());
            if (weaponsSubtab != null)
                weaponsSubtab.RegisterCallback<ClickEvent>(evt => SwitchSubtab("weapons"));
            if (hatsSubtab != null)
                hatsSubtab.RegisterCallback<ClickEvent>(evt => SwitchSubtab("hats"));

            selectedWeaponItem = null;
            selectedWeaponCard = null;
            selectedHatItem = null;
            selectedHatCard = null;
            activeSubtab = "weapons";
            UpdateSubtabStyles();
            PopulateGrid();

            LocalizationManager.OnLanguageChanged += LocalizeUI;
            LocalizeUI();
        }

        private void OnDisable()
        {
            if (buyButton != null)
                buyButton.UnregisterCallback<ClickEvent>(evt => OnBuyClicked());
            LocalizationManager.OnLanguageChanged -= LocalizeUI;
        }

        private void SwitchSubtab(string subtab)
        {
            if (subtab == activeSubtab) return;
            SoundManager.Instance?.PlayClick();
            activeSubtab = subtab;
            UpdateSubtabStyles();
            PopulateGrid();
        }

        private void UpdateSubtabStyles()
        {
            weaponsSubtab?.RemoveFromClassList("shop-subtab-active");
            hatsSubtab?.RemoveFromClassList("shop-subtab-active");
            if (activeSubtab == "weapons")
                weaponsSubtab?.AddToClassList("shop-subtab-active");
            else
                hatsSubtab?.AddToClassList("shop-subtab-active");
        }

        private void PopulateGrid()
        {
            if (activeSubtab == "weapons")
                PopulateWeapons();
            else
                PopulateHats();
        }

        // ─── WEAPONS ──────────────────────────────────────────────────

        private void PopulateWeapons()
        {
            if (shopGrid == null) return;
            shopGrid.Clear();
            selectedWeaponItem = null;
            selectedWeaponCard = null;

            var items = ShopItemDefinition.GetAllItems();
            foreach (var item in items)
            {
                bool isOwned = item.skinId == "default" ||
                    (LoadoutManager.Instance?.IsSkinUnlocked(item.weaponId, item.skinId) ?? false);
                var card = CreateWeaponCard(item, isOwned);
                shopGrid.Add(card);
            }
            UpdateBuyButton();
        }

        private VisualElement CreateWeaponCard(ShopItemDefinition.ShopItem item, bool owned)
        {
            var card = new VisualElement();
            card.AddToClassList("shop-card");

            var nameLabel = new Label(item.displayName);
            nameLabel.AddToClassList("shop-card-name");
            card.Add(nameLabel);

            var icon = new VisualElement();
            icon.AddToClassList("shop-card-icon");
            var texture = Resources.Load<Texture2D>(item.iconPath);
            if (texture != null)
                icon.style.backgroundImage = new StyleBackground(texture);
            card.Add(icon);

            var priceRow = new VisualElement();
            priceRow.AddToClassList("shop-card-price-row");

            if (owned)
            {
                card.AddToClassList("owned");
                var ownedLabel = new Label(T("OWNED"));
                ownedLabel.AddToClassList("shop-card-owned-label");
                priceRow.Add(ownedLabel);
            }
            else
            {
                var priceLabel = new Label(item.price.ToString());
                priceLabel.AddToClassList("shop-card-price");
                priceRow.Add(priceLabel);

                var currencyIcon = new VisualElement();
                currencyIcon.AddToClassList("shop-card-currency-icon");
                string currencyPath = item.currency == ShopItemDefinition.CurrencyType.RivalCoins
                    ? "Icons/RivalEssenceIcon"
                    : "Icons/RivalPointsIcon";
                var currTex = Resources.Load<Texture2D>(currencyPath);
                if (currTex != null)
                    currencyIcon.style.backgroundImage = new StyleBackground(currTex);
                priceRow.Add(currencyIcon);
            }

            card.Add(priceRow);

            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (owned) return;
                SoundManager.Instance?.PlayClick();
                SelectWeaponCard(item, card);
            });

            return card;
        }

        private void SelectWeaponCard(ShopItemDefinition.ShopItem item, VisualElement card)
        {
            selectedWeaponCard?.RemoveFromClassList("selected");
            selectedWeaponItem = item;
            selectedWeaponCard = card;
            card.AddToClassList("selected");
            UpdateBuyButton();
        }

        // ─── HATS ─────────────────────────────────────────────────────

        private void PopulateHats()
        {
            if (shopGrid == null) return;
            shopGrid.Clear();
            selectedHatItem = null;
            selectedHatCard = null;

            var allHats = HatDefinition.GetAllHats();
            var loadout = LoadoutManager.Instance?.GetLoadout();
            string[] ownedHats = loadout?.unlockedHats;

            foreach (var hat in allHats)
            {
                bool owned = IsHatOwned(hat.hatId, ownedHats);
                var card = CreateHatCard(hat, owned);
                shopGrid.Add(card);
            }
            UpdateBuyButton();
        }

        private bool IsHatOwned(string hatId, string[] ownedHats)
        {
            if (ownedHats == null) return false;
            foreach (var h in ownedHats)
                if (h == hatId) return true;
            return false;
        }

        private VisualElement CreateHatCard(HatDefinition hat, bool owned)
        {
            var card = new VisualElement();
            card.AddToClassList("hat-card");
            if (owned)
                card.AddToClassList("owned");

            // Padlock overlay for locked hats
            if (!owned)
            {
                var lockOverlay = new VisualElement();
                lockOverlay.AddToClassList("hat-card-lock-overlay");
                var lockIcon = new Label("\U0001F512");
                lockIcon.AddToClassList("hat-card-lock-icon");
                lockOverlay.Add(lockIcon);
                card.Add(lockOverlay);
            }

            var nameLabel = new Label(hat.displayName);
            nameLabel.AddToClassList("hat-card-name");
            card.Add(nameLabel);

            var icon = new VisualElement();
            icon.AddToClassList("hat-card-icon");
            var tex = Resources.Load<Texture2D>(hat.iconPath);
            if (tex != null)
                icon.style.backgroundImage = new StyleBackground(tex);
            card.Add(icon);

            var priceRow = new VisualElement();
            priceRow.AddToClassList("hat-card-price-row");

            if (owned)
            {
                var ownedLabel = new Label(T("OWNED"));
                ownedLabel.AddToClassList("hat-card-owned-label");
                priceRow.Add(ownedLabel);
            }
            else
            {
                var priceLabel = new Label(hat.price.ToString());
                priceLabel.AddToClassList("hat-card-price");
                priceRow.Add(priceLabel);

                string currencyPath = hat.currency == ShopItemDefinition.CurrencyType.RivalCoins
                    ? "Icons/RivalEssenceIcon"
                    : "Icons/RivalPointsIcon";
                var currIcon = new VisualElement();
                currIcon.AddToClassList("shop-card-currency-icon");
                var currTex = Resources.Load<Texture2D>(currencyPath);
                if (currTex != null)
                    currIcon.style.backgroundImage = new StyleBackground(currTex);
                priceRow.Add(currIcon);
            }
            card.Add(priceRow);

            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (owned) return;
                SoundManager.Instance?.PlayClick();
                SelectHatCard(hat, card);
            });

            return card;
        }

        private void SelectHatCard(HatDefinition hat, VisualElement card)
        {
            selectedHatCard?.RemoveFromClassList("selected");
            selectedHatItem = hat;
            selectedHatCard = card;
            card.AddToClassList("selected");
            UpdateBuyButton();
        }

        // ─── SHARED BUY BUTTON ─────────────────────────────────────────

        private void UpdateBuyButton()
        {
            if (buyButton == null) return;

            bool hasSelection = activeSubtab == "weapons" ? selectedWeaponItem != null : selectedHatItem != null;

            if (!hasSelection)
            {
                buyButton.Clear();
                buyButton.text = T("SELECT AN ITEM");
                buyButton.SetEnabled(false);
                if (selectedItemLabel != null) selectedItemLabel.text = "";
                return;
            }

            int price;
            ShopItemDefinition.CurrencyType currency;
            string displayName;

            if (activeSubtab == "weapons")
            {
                price = selectedWeaponItem.price;
                currency = selectedWeaponItem.currency;
                displayName = selectedWeaponItem.displayName;
            }
            else
            {
                price = selectedHatItem.price;
                currency = selectedHatItem.currency;
                displayName = selectedHatItem.displayName;
            }

            int playerCurrency = GetPlayerCurrency(currency);
            bool canAfford = playerCurrency >= price;

            if (selectedItemLabel != null)
                selectedItemLabel.text = displayName;

            buyButton.text = "";
            buyButton.Clear();
            var btnContainer = new VisualElement();
            btnContainer.style.flexDirection = FlexDirection.Row;
            btnContainer.style.alignItems = Align.Center;
            btnContainer.style.justifyContent = Justify.Center;
            btnContainer.pickingMode = PickingMode.Ignore;

            string prefix = canAfford ? T("BUY") : T("NOT ENOUGH");
            var textLabel = new Label($"{prefix} \u2014 {price}");
            textLabel.pickingMode = PickingMode.Ignore;
            textLabel.AddToClassList("shop-buy-label");
            btnContainer.Add(textLabel);

            string currPath = currency == ShopItemDefinition.CurrencyType.RivalCoins
                ? "Icons/RivalEssenceIcon"
                : "Icons/RivalPointsIcon";
            var currIcon = new VisualElement();
            currIcon.pickingMode = PickingMode.Ignore;
            currIcon.AddToClassList("shop-buy-currency-icon");
            var tex = Resources.Load<Texture2D>(currPath);
            if (tex != null)
                currIcon.style.backgroundImage = new StyleBackground(tex);
            btnContainer.Add(currIcon);

            buyButton.Add(btnContainer);

            if (canAfford)
            {
                buyButton.SetEnabled(true);
                buyButton.RemoveFromClassList("shop-buy-disabled");
            }
            else
            {
                buyButton.SetEnabled(false);
                buyButton.AddToClassList("shop-buy-disabled");
            }
        }

        private int GetPlayerCurrency(ShopItemDefinition.CurrencyType type)
        {
            var loadout = LoadoutManager.Instance?.GetLoadout();
            if (loadout == null) return 0;
            return type == ShopItemDefinition.CurrencyType.RivalCoins
                ? loadout.bluePoints
                : loadout.rivalCoins;
        }

        private void OnBuyClicked()
        {
            if (activeSubtab == "weapons")
            {
                if (selectedWeaponItem == null) return;
                int balance = GetPlayerCurrency(selectedWeaponItem.currency);
                if (balance < selectedWeaponItem.price) return;
                SoundManager.Instance?.PlaySelect();
                StartCoroutine(PurchaseWeaponSkin(selectedWeaponItem));
            }
            else
            {
                if (selectedHatItem == null) return;
                int balance = GetPlayerCurrency(selectedHatItem.currency);
                if (balance < selectedHatItem.price) return;
                SoundManager.Instance?.PlaySelect();
                StartCoroutine(PurchaseHat(selectedHatItem));
            }
        }

        // ─── PURCHASE COROUTINES ───────────────────────────────────────

        private IEnumerator PurchaseWeaponSkin(ShopItemDefinition.ShopItem item)
        {
            AuthManager authMgr = AuthManager.Instance;
            string token = authMgr != null ? authMgr.GetCurrentToken() : null;
            if (string.IsNullOrEmpty(token)) yield break;

            if (buyButton != null)
            {
                buyButton.text = T("PURCHASING...");
                buyButton.SetEnabled(false);
            }

            string json = JsonUtility.ToJson(new PurchaseSkinRequest
            {
                weaponId = item.weaponId,
                skinId = item.skinId,
                price = item.price,
                currencyType = item.currency == ShopItemDefinition.CurrencyType.RivalCoins ? "blue_points" : "rival_coins"
            });

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/loadout/purchase-skin", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] body = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<PurchaseResponse>(request.downloadHandler.text);
                    if (response.success)
                    {
                        LoadoutManager.Instance?.RefreshLoadout(success =>
                        {
                            if (success) PopulateGrid();
                        });
                    }
                    else
                    {
                        Debug.LogWarning($"[Shop] Purchase failed: {response.error}");
                        UpdateBuyButton();
                    }
                }
                else
                {
                    Debug.LogWarning($"[Shop] Request failed: {request.error}");
                    UpdateBuyButton();
                }
            }
        }

        private IEnumerator PurchaseHat(HatDefinition hat)
        {
            string token = AuthManager.Instance?.GetCurrentToken();
            if (string.IsNullOrEmpty(token)) yield break;

            if (buyButton != null)
            {
                buyButton.text = T("PURCHASING...");
                buyButton.SetEnabled(false);
            }

            string currencyType = hat.currency == ShopItemDefinition.CurrencyType.RivalCoins
                ? "blue_points" : "rival_coins";

            string json = JsonUtility.ToJson(new PurchaseHatRequest
            {
                hatId = hat.hatId,
                price = hat.price,
                currencyType = currencyType
            });

            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/loadout/purchase-hat", "POST"))
            {
                request.timeout = REQUEST_TIMEOUT;
                byte[] body = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {token}");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var response = JsonUtility.FromJson<PurchaseResponse>(request.downloadHandler.text);
                    if (response.success)
                    {
                        LoadoutManager.Instance?.RefreshLoadout(success =>
                        {
                            if (success) PopulateGrid();
                        });
                    }
                    else
                    {
                        Debug.LogWarning($"[Shop] Hat purchase failed: {response.error}");
                        UpdateBuyButton();
                    }
                }
                else
                {
                    Debug.LogWarning($"[Shop] Hat request failed: {request.error}");
                    UpdateBuyButton();
                }
            }
        }

        [System.Serializable]
        private class PurchaseSkinRequest
        {
            public string weaponId;
            public string skinId;
            public int price;
            public string currencyType;
        }

        [System.Serializable]
        private class PurchaseHatRequest
        {
            public string hatId;
            public int price;
            public string currencyType;
        }

        [System.Serializable]
        private class PurchaseResponse
        {
            public bool success;
            public string error;
        }

        private void LocalizeUI()
        {
            // Shop title
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            var shopTitle = root?.Q<Label>(className: "shop-tab-title");
            if (shopTitle != null) shopTitle.text = T("SHOP");

            // Re-set subtab button labels (set .text directly on Button)
            if (weaponsSubtab != null) weaponsSubtab.text = T("WEAPONS");
            if (hatsSubtab != null) hatsSubtab.text = T("HATS");

            // Refresh the grid (cards have OWNED labels) and buy button
            PopulateGrid();
        }
    }
}
