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
    public class HatsTabController : MonoBehaviour
    {
        private ScrollView hatsGrid;
        private Button equipButton;
        private Label selectedLabel;
        [SerializeField] private UIDocument uiDocument;

        private HatDefinition selectedHat;
        private VisualElement selectedCard;
        private string equippedHatId;

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
            hatsGrid = root.Q<ScrollView>("HatsGrid");
            equipButton = root.Q<Button>("HatsEquipButton");
            selectedLabel = root.Q<Label>("HatsSelectedLabel");

            if (equipButton != null)
                equipButton.RegisterCallback<ClickEvent>(evt => OnEquipClicked());

            selectedHat = null;
            selectedCard = null;
            equippedHatId = LoadoutManager.Instance?.GetLoadout()?.selectedHat ?? "none";
            UpdateEquipButton();
            PopulateHats();
        }

        private void OnDisable()
        {
            if (equipButton != null)
                equipButton.UnregisterCallback<ClickEvent>(evt => OnEquipClicked());
        }

        private void PopulateHats()
        {
            if (hatsGrid == null) return;
            hatsGrid.Clear();
            selectedHat = null;
            selectedCard = null;
            equippedHatId = LoadoutManager.Instance?.GetLoadout()?.selectedHat ?? "none";

            var allHats = HatDefinition.GetAllHats();
            var loadout = LoadoutManager.Instance?.GetLoadout();
            string[] ownedHats = loadout?.unlockedHats;

            foreach (var hat in allHats)
            {
                bool owned = IsHatOwned(hat.hatId, ownedHats);
                bool equipped = hat.hatId == equippedHatId;
                var card = CreateHatCard(hat, owned, equipped);
                hatsGrid.Add(card);
            }
            UpdateEquipButton();
        }

        private bool IsHatOwned(string hatId, string[] ownedHats)
        {
            if (ownedHats == null) return false;
            foreach (var h in ownedHats)
                if (h == hatId) return true;
            return false;
        }

        private VisualElement CreateHatCard(HatDefinition hat, bool owned, bool equipped)
        {
            var card = new VisualElement();
            card.AddToClassList("hat-card");
            if (!owned)
                card.AddToClassList("hat-card-locked");

            // Name
            var nameLabel = new Label(hat.displayName);
            nameLabel.AddToClassList("hat-card-name");
            card.Add(nameLabel);

            // Icon
            var icon = new VisualElement();
            icon.AddToClassList("hat-card-icon");
            var tex = Resources.Load<Texture2D>(hat.iconPath);
            if (tex != null)
                icon.style.backgroundImage = new StyleBackground(tex);
            card.Add(icon);

            // Price row / status
            var priceRow = new VisualElement();
            priceRow.AddToClassList("hat-card-price-row");

            if (equipped)
            {
                var equippedLabel = new Label("EQUIPPED");
                equippedLabel.AddToClassList("hat-card-equipped-label");
                priceRow.Add(equippedLabel);
            }
            else if (owned)
            {
                var ownedLabel = new Label("OWNED");
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

            // Click
            card.RegisterCallback<ClickEvent>(evt =>
            {
                SoundManager.Instance?.PlayClick();
                SelectCard(hat, card, owned);
            });

            return card;
        }

        private void SelectCard(HatDefinition hat, VisualElement card, bool owned)
        {
            selectedCard?.RemoveFromClassList("selected");
            selectedHat = hat;
            selectedCard = card;
            card.AddToClassList("selected");
            UpdateEquipButton(owned);
        }

        private void UpdateEquipButton(bool? selectedOwned = null)
        {
            if (equipButton == null) return;

            if (selectedHat == null)
            {
                equipButton.Clear();
                equipButton.text = "SELECT A HAT";
                equipButton.SetEnabled(false);
                if (selectedLabel != null) selectedLabel.text = "";
                return;
            }

            if (selectedLabel != null)
                selectedLabel.text = selectedHat.displayName;

            bool owned = selectedOwned ?? IsHatOwned(selectedHat.hatId,
                LoadoutManager.Instance?.GetLoadout()?.unlockedHats);
            bool alreadyEquipped = selectedHat.hatId == equippedHatId;

            equipButton.Clear();
            equipButton.text = "";

            if (alreadyEquipped)
            {
                equipButton.text = "UNEQUIP";
                equipButton.SetEnabled(true);
                equipButton.RemoveFromClassList("shop-buy-disabled");
            }
            else if (owned)
            {
                equipButton.text = "EQUIP";
                equipButton.SetEnabled(true);
                equipButton.RemoveFromClassList("shop-buy-disabled");
            }
            else
            {
                // Need to buy — show price
                int balance = GetPlayerCurrency(selectedHat.currency);
                bool canAfford = balance >= selectedHat.price;

                var btnContainer = new VisualElement();
                btnContainer.style.flexDirection = FlexDirection.Row;
                btnContainer.style.alignItems = Align.Center;
                btnContainer.style.justifyContent = Justify.Center;
                btnContainer.pickingMode = PickingMode.Ignore;

                string prefix = canAfford ? "BUY" : "NOT ENOUGH";
                var textLabel = new Label($"{prefix} — {selectedHat.price}");
                textLabel.pickingMode = PickingMode.Ignore;
                textLabel.AddToClassList("shop-buy-label");
                btnContainer.Add(textLabel);

                string currPath = selectedHat.currency == ShopItemDefinition.CurrencyType.RivalCoins
                    ? "Icons/RivalEssenceIcon"
                    : "Icons/RivalPointsIcon";
                var currIcon = new VisualElement();
                currIcon.pickingMode = PickingMode.Ignore;
                currIcon.AddToClassList("shop-buy-currency-icon");
                var tex = Resources.Load<Texture2D>(currPath);
                if (tex != null)
                    currIcon.style.backgroundImage = new StyleBackground(tex);
                btnContainer.Add(currIcon);

                equipButton.Add(btnContainer);

                if (canAfford)
                {
                    equipButton.SetEnabled(true);
                    equipButton.RemoveFromClassList("shop-buy-disabled");
                }
                else
                {
                    equipButton.SetEnabled(false);
                    equipButton.AddToClassList("shop-buy-disabled");
                }
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

        private void OnEquipClicked()
        {
            if (selectedHat == null) return;

            var loadout = LoadoutManager.Instance?.GetLoadout();
            bool owned = IsHatOwned(selectedHat.hatId, loadout?.unlockedHats);
            bool alreadyEquipped = selectedHat.hatId == equippedHatId;

            if (alreadyEquipped)
            {
                // Unequip
                SoundManager.Instance?.PlaySelect();
                LoadoutManager.Instance?.UpdateHat("none");
                equippedHatId = "none";
                PopulateHats();
                return;
            }

            if (owned)
            {
                // Equip
                SoundManager.Instance?.PlaySelect();
                LoadoutManager.Instance?.UpdateHat(selectedHat.hatId);
                equippedHatId = selectedHat.hatId;
                PopulateHats();
                return;
            }

            // Purchase
            int balance = GetPlayerCurrency(selectedHat.currency);
            if (balance < selectedHat.price) return;

            SoundManager.Instance?.PlaySelect();
            StartCoroutine(PurchaseHat(selectedHat));
        }

        private IEnumerator PurchaseHat(HatDefinition hat)
        {
            string token = AuthManager.Instance?.GetCurrentToken();
            if (string.IsNullOrEmpty(token)) yield break;

            if (equipButton != null)
            {
                equipButton.text = "PURCHASING...";
                equipButton.SetEnabled(false);
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
                            if (success) PopulateHats();
                        });
                    }
                    else
                    {
                        Debug.LogWarning($"[Hats] Purchase failed: {response.error}");
                        UpdateEquipButton();
                    }
                }
                else
                {
                    Debug.LogWarning($"[Hats] Request failed: {request.error}");
                    UpdateEquipButton();
                }
            }
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
    }
}
