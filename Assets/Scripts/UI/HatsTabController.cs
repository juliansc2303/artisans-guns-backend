using UnityEngine;
using UnityEngine.UIElements;
using ArtisansGuns.Data;
using ArtisansGuns.Managers;
using static ArtisansGuns.Managers.LocalizationManager;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// Hats tab — browse all hats, equip/unequip owned ones.
    /// Purchasing is done in the Shop tab.
    /// </summary>
    public class HatsTabController : MonoBehaviour
    {
        private ScrollView hatsGrid;
        private Button equipButton;
        private Label selectedLabel;
        [SerializeField] private UIDocument uiDocument;

        private HatDefinition selectedHat;
        private VisualElement selectedCard;
        private string equippedHatId;

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

            LocalizationManager.OnLanguageChanged += LocalizeUI;
            LocalizeUI();
        }

        private void OnDisable()
        {
            if (equipButton != null)
                equipButton.UnregisterCallback<ClickEvent>(evt => OnEquipClicked());
            LocalizationManager.OnLanguageChanged -= LocalizeUI;
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

            var statusRow = new VisualElement();
            statusRow.AddToClassList("hat-card-price-row");

            if (equipped)
            {
                var equippedLabel = new Label(T("EQUIPPED"));
                equippedLabel.AddToClassList("hat-card-equipped-label");
                statusRow.Add(equippedLabel);
            }
            else if (owned)
            {
                var ownedLabel = new Label(T("OWNED"));
                ownedLabel.AddToClassList("hat-card-owned-label");
                statusRow.Add(ownedLabel);
            }
            else
            {
                var lockedLabel = new Label(T("LOCKED"));
                lockedLabel.AddToClassList("hat-card-locked-label");
                statusRow.Add(lockedLabel);
            }
            card.Add(statusRow);

            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (!owned) return; // Can't select locked hats
                SoundManager.Instance?.PlayClick();
                SelectCard(hat, card);
            });

            return card;
        }

        private void SelectCard(HatDefinition hat, VisualElement card)
        {
            selectedCard?.RemoveFromClassList("selected");
            selectedHat = hat;
            selectedCard = card;
            card.AddToClassList("selected");
            UpdateEquipButton();
        }

        private void UpdateEquipButton()
        {
            if (equipButton == null) return;

            if (selectedHat == null)
            {
                equipButton.Clear();
                equipButton.text = T("SELECT A HAT");
                equipButton.SetEnabled(false);
                if (selectedLabel != null) selectedLabel.text = "";
                return;
            }

            if (selectedLabel != null)
                selectedLabel.text = selectedHat.displayName;

            bool alreadyEquipped = selectedHat.hatId == equippedHatId;

            equipButton.Clear();

            if (alreadyEquipped)
            {
                equipButton.text = T("UNEQUIP");
                equipButton.SetEnabled(true);
            }
            else
            {
                equipButton.text = T("EQUIP");
                equipButton.SetEnabled(true);
            }
        }

        private void OnEquipClicked()
        {
            if (selectedHat == null) return;

            bool alreadyEquipped = selectedHat.hatId == equippedHatId;

            if (alreadyEquipped)
            {
                SoundManager.Instance?.PlaySelect();
                LoadoutManager.Instance?.UpdateHat("none");
                equippedHatId = "none";
                PopulateHats();
            }
            else
            {
                SoundManager.Instance?.PlaySelect();
                LoadoutManager.Instance?.UpdateHat(selectedHat.hatId);
                equippedHatId = selectedHat.hatId;
                PopulateHats();
            }
        }

        private void LocalizeUI()
        {
            PopulateHats();
        }
    }
}
