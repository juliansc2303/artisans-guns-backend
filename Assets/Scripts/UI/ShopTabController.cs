using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using System.Text;
using UnityEngine.Networking;
using ArtisansGuns.Auth;
using ArtisansGuns.Data;
using ArtisansGuns.Managers;

namespace ArtisansGuns.UI
{
    public class ShopTabController : MonoBehaviour
    {
        private VisualElement shopContent;
        private ScrollView shopGrid;
        private Button buyButton;
        private Label selectedItemLabel;
        [SerializeField] private UIDocument uiDocument;

        private ShopItemDefinition.ShopItem selectedItem;
        private VisualElement selectedCard;

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

            if (buyButton != null)
                buyButton.RegisterCallback<ClickEvent>(evt => OnBuyClicked());

            selectedItem = null;
            selectedCard = null;
            UpdateBuyButton();
            PopulateShop();
        }

        private void OnDisable()
        {
            if (buyButton != null)
                buyButton.UnregisterCallback<ClickEvent>(evt => OnBuyClicked());
        }

        private void PopulateShop()
        {
            if (shopGrid == null) return;
            shopGrid.Clear();
            selectedItem = null;
            selectedCard = null;

            var items = ShopItemDefinition.GetAllItems();
            foreach (var item in items)
            {
                bool isOwned = item.skinId == "default" ||
                    (LoadoutManager.Instance?.IsSkinUnlocked(item.weaponId, item.skinId) ?? false);
                var card = CreateShopCard(item, isOwned);
                shopGrid.Add(card);
            }
            UpdateBuyButton();
        }

        private VisualElement CreateShopCard(ShopItemDefinition.ShopItem item, bool owned)
        {
            var card = new VisualElement();
            card.AddToClassList("shop-card");

            // Name label
            var nameLabel = new Label(item.displayName);
            nameLabel.AddToClassList("shop-card-name");
            card.Add(nameLabel);

            // Icon
            var icon = new VisualElement();
            icon.AddToClassList("shop-card-icon");
            var texture = Resources.Load<Texture2D>(item.iconPath);
            if (texture != null)
                icon.style.backgroundImage = new StyleBackground(texture);
            card.Add(icon);

            // Price row
            var priceRow = new VisualElement();
            priceRow.AddToClassList("shop-card-price-row");

            if (owned)
            {
                card.AddToClassList("owned");
                var ownedLabel = new Label("OWNED");
                ownedLabel.AddToClassList("shop-card-owned-label");
                priceRow.Add(ownedLabel);
            }
            else
            {
                // Price text
                var priceLabel = new Label(item.price.ToString());
                priceLabel.AddToClassList("shop-card-price");
                priceRow.Add(priceLabel);

                // Currency icon (right of price)
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

            // Click to select (not buy)
            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (owned) return;
                SoundManager.Instance?.PlayClick();
                SelectCard(item, card);
            });

            return card;
        }

        private void SelectCard(ShopItemDefinition.ShopItem item, VisualElement card)
        {
            // Deselect previous
            selectedCard?.RemoveFromClassList("selected");

            selectedItem = item;
            selectedCard = card;
            card.AddToClassList("selected");
            UpdateBuyButton();
        }

        private void UpdateBuyButton()
        {
            if (buyButton == null) return;

            if (selectedItem == null)
            {
                buyButton.Clear();
                buyButton.text = "SELECT AN ITEM";
                buyButton.SetEnabled(false);
                if (selectedItemLabel != null) selectedItemLabel.text = "";
                return;
            }

            // Check currency
            int playerCurrency = GetPlayerCurrency(selectedItem.currency);
            bool canAfford = playerCurrency >= selectedItem.price;

            if (selectedItemLabel != null)
                selectedItemLabel.text = selectedItem.displayName;

            // Rebuild button content with currency icon
            buyButton.text = "";
            buyButton.Clear();
            var btnContainer = new VisualElement();
            btnContainer.style.flexDirection = FlexDirection.Row;
            btnContainer.style.alignItems = Align.Center;
            btnContainer.style.justifyContent = Justify.Center;
            btnContainer.pickingMode = PickingMode.Ignore;

            string prefix = canAfford ? "BUY" : "NOT ENOUGH";
            var textLabel = new Label($"{prefix} \u2014 {selectedItem.price}");
            textLabel.pickingMode = PickingMode.Ignore;
            textLabel.AddToClassList("shop-buy-label");
            btnContainer.Add(textLabel);

            string currPath = selectedItem.currency == ShopItemDefinition.CurrencyType.RivalCoins
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
            if (selectedItem == null) return;

            int playerCurrency = GetPlayerCurrency(selectedItem.currency);
            if (playerCurrency < selectedItem.price)
            {
                Debug.LogWarning("[Shop] Not enough currency");
                return;
            }

            SoundManager.Instance?.PlaySelect();
            StartCoroutine(PurchaseItem(selectedItem));
        }

        private IEnumerator PurchaseItem(ShopItemDefinition.ShopItem item)
        {
            AuthManager authMgr = AuthManager.Instance;
            string token = authMgr != null ? authMgr.GetCurrentToken() : null;
            if (string.IsNullOrEmpty(token)) yield break;

            // Disable buy button during purchase
            if (buyButton != null)
            {
                buyButton.text = "PURCHASING...";
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
                        // Refresh loadout to get updated currency + unlocked skins
                        LoadoutManager.Instance?.RefreshLoadout(success =>
                        {
                            if (success)
                                PopulateShop();
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

        [System.Serializable]
        private class PurchaseSkinRequest
        {
            public string weaponId;
            public string skinId;
            public int price;
            public string currencyType;
        }

        [System.Serializable]
        private class PurchaseResponse
        {
            public bool success;
            public string error;
        }
    }
}
