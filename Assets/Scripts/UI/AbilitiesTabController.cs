using UnityEngine;
using UnityEngine.UIElements;
using ArtisansGuns.Abilities;
using ArtisansGuns.Managers;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// Ultimates tab — circular icons for each available ultimate.
    /// Tap a circle to preview, then press EQUIPAR to equip.
    /// </summary>
    public class AbilitiesTabController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        private ScrollView ultimateGrid;
        private VisualElement selectedInfo;
        private Label selectedNameLabel;
        private Button equipButton;

        // State
        private string equippedUltimate;
        private string selectedUltimateId;  // currently highlighted (not yet equipped)

        private void OnEnable()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
                uiDocument = GetComponentInChildren<UIDocument>();
            if (uiDocument == null || uiDocument.rootVisualElement == null) return;

            var root = uiDocument.rootVisualElement;
            ultimateGrid     = root.Q<ScrollView>("UltimateGrid");
            selectedInfo     = root.Q<VisualElement>("UltimateSelectedInfo");
            selectedNameLabel = root.Q<Label>("UltimateSelectedName");
            equipButton      = root.Q<Button>("UltimateEquipButton");

            equipButton?.RegisterCallback<ClickEvent>(evt => OnEquipClicked());

            LoadEquippedFromLoadout();
            PopulateGrid();
        }

        public void Refresh()
        {
            LoadEquippedFromLoadout();
            PopulateGrid();
        }

        private void LoadEquippedFromLoadout()
        {
            var loadout = LoadoutManager.Instance?.GetLoadout();
            equippedUltimate = loadout?.ultimate ?? "crimson_ultimate";
            selectedUltimateId = null;
        }

        private void PopulateGrid()
        {
            if (ultimateGrid == null) return;
            ultimateGrid.Clear();

            var ultimates = AbilityRegistry.Ultimates;
            foreach (var cfg in ultimates)
            {
                bool equipped = cfg.abilityId == equippedUltimate;
                var circle = CreateUltimateCircle(cfg, equipped);
                ultimateGrid.Add(circle);
            }

            // Hide selected info until user taps a circle
            if (selectedInfo != null)
                selectedInfo.style.display = DisplayStyle.None;
            selectedUltimateId = null;
        }

        private VisualElement CreateUltimateCircle(AbilityConfig cfg, bool equipped)
        {
            var circle = new VisualElement();
            circle.AddToClassList("ultimate-circle");

            if (equipped)
                circle.AddToClassList("ultimate-circle-equipped");

            var icon = new VisualElement();
            icon.AddToClassList("ultimate-circle-icon");
            if (cfg.icon != null)
                icon.style.backgroundImage = new StyleBackground(cfg.icon);
            circle.Add(icon);

            circle.RegisterCallback<ClickEvent>(evt =>
            {
                SoundManager.Instance?.PlaySelect();
                SelectUltimate(cfg);
            });

            return circle;
        }

        private void SelectUltimate(AbilityConfig cfg)
        {
            selectedUltimateId = cfg.abilityId;

            // Update name label
            if (selectedNameLabel != null)
                selectedNameLabel.text = cfg.abilityName.ToUpper();

            // Show info + equip button (hide if already equipped)
            if (selectedInfo != null)
                selectedInfo.style.display = DisplayStyle.Flex;

            if (equipButton != null)
            {
                bool alreadyEquipped = cfg.abilityId == equippedUltimate;
                equipButton.style.display = alreadyEquipped ? DisplayStyle.None : DisplayStyle.Flex;
                if (alreadyEquipped && selectedNameLabel != null)
                    selectedNameLabel.text = cfg.abilityName.ToUpper() + "  (EQUIPADA)";
            }

            // Highlight selected circle, remove from others
            if (ultimateGrid != null)
            {
                foreach (var child in ultimateGrid.contentContainer.Children())
                {
                    child.RemoveFromClassList("ultimate-circle-selected");
                }
            }
            // Find the circle that matches and highlight it
            int idx = 0;
            var ultimates = AbilityRegistry.Ultimates;
            foreach (var ult in ultimates)
            {
                if (ult.abilityId == cfg.abilityId && idx < ultimateGrid.contentContainer.childCount)
                {
                    ultimateGrid.contentContainer[idx].AddToClassList("ultimate-circle-selected");
                    break;
                }
                idx++;
            }
        }

        private void OnEquipClicked()
        {
            if (string.IsNullOrEmpty(selectedUltimateId)) return;
            if (selectedUltimateId == equippedUltimate) return;

            equippedUltimate = selectedUltimateId;

            // Get current ability1 from loadout (unchanged)
            var loadout = LoadoutManager.Instance?.GetLoadout();
            string a1 = loadout?.ability1 ?? "smoke_grenade";

            // Persist to backend
            LoadoutManager.Instance?.UpdateAbilities(a1, "", equippedUltimate);

            SoundManager.Instance?.PlaySelect();

            // Rebuild grid to reflect new equipped state
            PopulateGrid();
        }
    }
}
