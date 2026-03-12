using UnityEngine;
using UnityEngine.UIElements;
using ArtisansGuns.Data;
using ArtisansGuns.Managers;
using System.Collections.Generic;
using System.Linq;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// WeaponsTabController - Manages weapon selection UI
    /// Allows players to select primary and secondary weapons
    /// </summary>
    public class WeaponsTabController : MonoBehaviour
    {
        [Header("UI Document (auto-detected if null)")]
        [SerializeField] private UIDocument uiDocument;

        // UI Elements - Views
        private VisualElement weaponsTabRoot;
        private VisualElement mainView;
        private VisualElement selectionView;
        private VisualElement selectionHeader;
        private VisualElement knifeSkinsView;
        private VisualElement knifeSkinsHeader;

        // UI Elements - Main View
        private Button primarySlotButton;
        private Button secondarySlotButton;
        private Button knifeSlotButton;
        private VisualElement primaryWeaponIcon;
        private VisualElement secondaryWeaponIcon;
        private VisualElement knifeIcon;
        private Label primaryWeaponName;
        private Label secondaryWeaponName;
        private Label knifeName;

        // UI Elements - Selection View
        private Button backButton;
        private Label selectionTitle;
        private ScrollView weaponsGrid;
        private Button lockInButton;
        private Button skinsButton;

        // UI Elements - Knife Skins View
        private Button knifeBackButton;
        private Label knifeSkinsTitle;
        private ScrollView knifeSkinsGrid;
        private Button knifeSelectButton;

        // UI Elements - Weapon Skins View
        private VisualElement weaponSkinsView;
        private VisualElement weaponSkinsHeader;
        private Button weaponSkinsBackButton;
        private Label weaponSkinsTitle;
        private ScrollView weaponSkinsGrid;
        private Button weaponSkinEquipButton;

        // State
        private WeaponDefinition.WeaponCategory currentSelectionCategory;
        private WeaponDefinition.Weapon selectedWeaponInGrid;
        private WeaponDefinition.Weapon currentPrimaryWeapon;
        private WeaponDefinition.Weapon currentSecondaryWeapon;
        private KnifeSkinDefinition.KnifeSkin currentKnifeSkin;
        private KnifeSkinDefinition.KnifeSkin selectedKnifeSkinInGrid;
        private WeaponSkinDefinition.WeaponSkin selectedWeaponSkinInGrid;
        private string currentPrimarySkinId = "default";
        private string currentSecondarySkinId = "default";

        private void Awake()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            if (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                // Debug.LogError("âŒ WeaponsTabController: UIDocument not found!");
                return;
            }

            var root = uiDocument.rootVisualElement;
            
            // Find WeaponsContent container first (since we're embedded in LobbyScreen)
            var weaponsContent = root.Q<VisualElement>("WeaponsContent");
            if (weaponsContent == null)
            {
                // Debug.LogError("âŒ WeaponsTabController: WeaponsContent container not found!");
                return;
            }
            
            CacheUIElements(weaponsContent);
            RegisterEventHandlers();
            InitializeWeapons();
            ShowMainView();

            // Subscribe to loadout updates so we re-render if data arrives after init
            if (LoadoutManager.Instance != null)
                LoadoutManager.Instance.OnLoadoutUpdated += OnLoadoutRefreshed;
        }

        private void OnDisable()
        {
            UnregisterEventHandlers();
            if (LoadoutManager.Instance != null)
                LoadoutManager.Instance.OnLoadoutUpdated -= OnLoadoutRefreshed;
        }

        private void OnLoadoutRefreshed(LoadoutManager.LoadoutData loadout)
        {
            // Re-read weapons from refreshed loadout
            if (loadout.primaryWeapon != null && !string.IsNullOrEmpty(loadout.primaryWeapon.weaponId))
            {
                var w = WeaponDefinition.GetWeaponById(loadout.primaryWeapon.weaponId);
                if (w != null) currentPrimaryWeapon = w;
                if (!string.IsNullOrEmpty(loadout.primaryWeapon.skinId))
                    currentPrimarySkinId = loadout.primaryWeapon.skinId;
            }
            if (loadout.secondaryWeapon != null && !string.IsNullOrEmpty(loadout.secondaryWeapon.weaponId))
            {
                var w = WeaponDefinition.GetWeaponById(loadout.secondaryWeapon.weaponId);
                if (w != null) currentSecondaryWeapon = w;
                if (!string.IsNullOrEmpty(loadout.secondaryWeapon.skinId))
                    currentSecondarySkinId = loadout.secondaryWeapon.skinId;
            }
            if (loadout.knifeSkin != null && !string.IsNullOrEmpty(loadout.knifeSkin.skinId))
            {
                var k = KnifeSkinDefinition.GetKnifeSkinById(loadout.knifeSkin.skinId);
                if (k != null) currentKnifeSkin = k;
            }
            if (currentKnifeSkin == null)
                currentKnifeSkin = KnifeSkinDefinition.GetDefaultKnifeSkin();

            UpdateMainViewDisplay();
        }

        private void CacheUIElements(VisualElement root)
        {
            weaponsTabRoot = root.Q<VisualElement>("WeaponsTabRoot");

            // Views
            mainView = root.Q<VisualElement>("MainView");
            selectionView = root.Q<VisualElement>("SelectionView");
            selectionHeader = root.Q<VisualElement>("SelectionHeader");
            knifeSkinsView = root.Q<VisualElement>("KnifeSkinsView");
            knifeSkinsHeader = root.Q<VisualElement>("KnifeSkinsHeader");

            // Main View elements
            primarySlotButton = root.Q<Button>("PrimarySlotButton");
            secondarySlotButton = root.Q<Button>("SecondarySlotButton");
            knifeSlotButton = root.Q<Button>("KnifeSlotButton");
            primaryWeaponIcon = root.Q<VisualElement>("PrimaryWeaponIcon");
            secondaryWeaponIcon = root.Q<VisualElement>("SecondaryWeaponIcon");
            knifeIcon = root.Q<VisualElement>("KnifeIcon");
            primaryWeaponName = root.Q<Label>("PrimaryWeaponName");
            secondaryWeaponName = root.Q<Label>("SecondaryWeaponName");
            knifeName = root.Q<Label>("KnifeName");

            // Selection View elements
            backButton = root.Q<Button>("BackButton");
            selectionTitle = root.Q<Label>("SelectionTitle");
            weaponsGrid = root.Q<ScrollView>("WeaponsGrid");
            lockInButton = root.Q<Button>("LockInButton");
            skinsButton = root.Q<Button>("SkinsButton");
            
            // Knife Skins View elements
            knifeBackButton = root.Q<Button>("KnifeBackButton");
            knifeSkinsTitle = root.Q<Label>("KnifeSkinsTitle");
            knifeSkinsGrid = root.Q<ScrollView>("KnifeSkinsGrid");
            knifeSelectButton = root.Q<Button>("KnifeSelectButton");
            
            // Weapon Skins View elements
            weaponSkinsView = root.Q<VisualElement>("WeaponSkinsView");
            weaponSkinsHeader = root.Q<VisualElement>("WeaponSkinsHeader");
            weaponSkinsBackButton = root.Q<Button>("WeaponSkinsBackButton");
            weaponSkinsTitle = root.Q<Label>("WeaponSkinsTitle");
            weaponSkinsGrid = root.Q<ScrollView>("WeaponSkinsGrid");
            weaponSkinEquipButton = root.Q<Button>("WeaponSkinEquipButton");
            
            // Debug: Check if critical elements were found
            if (primarySlotButton == null) Debug.LogError("❌ PrimarySlotButton not found!");
            if (secondarySlotButton == null) Debug.LogError("❌ SecondarySlotButton not found!");
            if (knifeSlotButton == null) Debug.LogError("❌ KnifeSlotButton not found!");
            if (primaryWeaponName == null) Debug.LogError("❌ PrimaryWeaponName not found!");
            if (secondaryWeaponName == null) Debug.LogError("❌ SecondaryWeaponName not found!");
            if (knifeName == null) Debug.LogError("❌ KnifeName not found!");
            
            Debug.Log($"✅ WeaponsTabController: UI elements cached. KnifeSlot={knifeSlotButton != null}");
            // if (secondaryWeaponName == null) Debug.LogError("❌ SecondaryWeaponName not found!");
        }

        private void RegisterEventHandlers()
        {
            primarySlotButton?.RegisterCallback<ClickEvent>(evt => OnPrimarySlotClicked());
            secondarySlotButton?.RegisterCallback<ClickEvent>(evt => OnSecondarySlotClicked());
            knifeSlotButton?.RegisterCallback<ClickEvent>(evt => OnKnifeSlotClicked());
            backButton?.RegisterCallback<ClickEvent>(evt => OnBackButtonClicked());
            lockInButton?.RegisterCallback<ClickEvent>(evt => OnLockInClicked());
            skinsButton?.RegisterCallback<ClickEvent>(evt => OnSkinsButtonClicked());
            knifeBackButton?.RegisterCallback<ClickEvent>(evt => OnKnifeBackButtonClicked());
            knifeSelectButton?.RegisterCallback<ClickEvent>(evt => OnKnifeSelectClicked());
            weaponSkinsBackButton?.RegisterCallback<ClickEvent>(evt => OnWeaponSkinsBackClicked());
            weaponSkinEquipButton?.RegisterCallback<ClickEvent>(evt => OnWeaponSkinEquipClicked());
        }

        private void UnregisterEventHandlers()
        {
            primarySlotButton?.UnregisterCallback<ClickEvent>(evt => OnPrimarySlotClicked());
            secondarySlotButton?.UnregisterCallback<ClickEvent>(evt => OnSecondarySlotClicked());
            knifeSlotButton?.UnregisterCallback<ClickEvent>(evt => OnKnifeSlotClicked());
            backButton?.UnregisterCallback<ClickEvent>(evt => OnBackButtonClicked());
            lockInButton?.UnregisterCallback<ClickEvent>(evt => OnLockInClicked());
            skinsButton?.UnregisterCallback<ClickEvent>(evt => OnSkinsButtonClicked());
            knifeBackButton?.UnregisterCallback<ClickEvent>(evt => OnKnifeBackButtonClicked());
            knifeSelectButton?.UnregisterCallback<ClickEvent>(evt => OnKnifeSelectClicked());
            weaponSkinsBackButton?.UnregisterCallback<ClickEvent>(evt => OnWeaponSkinsBackClicked());
            weaponSkinEquipButton?.UnregisterCallback<ClickEvent>(evt => OnWeaponSkinEquipClicked());
        }

        /// <summary>
        /// Initialize weapons from LoadoutManager or defaults
        /// </summary>
        private void InitializeWeapons()
        {
            // Try to load from LoadoutManager first
            if (LoadoutManager.Instance != null && LoadoutManager.Instance.IsInitialized())
            {
                var loadout = LoadoutManager.Instance.GetLoadout();

                // Load primary weapon
                if (loadout.primaryWeapon != null && !string.IsNullOrEmpty(loadout.primaryWeapon.weaponId))
                {
                    currentPrimaryWeapon = WeaponDefinition.GetWeaponById(loadout.primaryWeapon.weaponId);
                    if (!string.IsNullOrEmpty(loadout.primaryWeapon.skinId))
                        currentPrimarySkinId = loadout.primaryWeapon.skinId;
                }

                // Load secondary weapon
                if (loadout.secondaryWeapon != null && !string.IsNullOrEmpty(loadout.secondaryWeapon.weaponId))
                {
                    currentSecondaryWeapon = WeaponDefinition.GetWeaponById(loadout.secondaryWeapon.weaponId);
                    if (!string.IsNullOrEmpty(loadout.secondaryWeapon.skinId))
                        currentSecondarySkinId = loadout.secondaryWeapon.skinId;
                }

                // Debug.Log($"âœ… Loaded weapons from LoadoutManager: Primary={currentPrimaryWeapon?.displayName}, Secondary={currentSecondaryWeapon?.displayName}");
            }

            // Fallback to defaults if not loaded
            if (currentPrimaryWeapon == null)
            {
                currentPrimaryWeapon = WeaponDefinition.GetDefaultWeapon(WeaponDefinition.WeaponCategory.Primary);
                // Debug.Log($"âš ï¸ Using default primary weapon: {currentPrimaryWeapon?.displayName}");
            }

            if (currentSecondaryWeapon == null)
            {
                currentSecondaryWeapon = WeaponDefinition.GetDefaultWeapon(WeaponDefinition.WeaponCategory.Secondary);
                // Debug.Log($"âš ï¸ Using default secondary weapon: {currentSecondaryWeapon?.displayName}");
            }
            // Load knife skin
            if (LoadoutManager.Instance != null && LoadoutManager.Instance.IsInitialized())
            {
                var loadout = LoadoutManager.Instance.GetLoadout();
                if (loadout.knifeSkin != null && !string.IsNullOrEmpty(loadout.knifeSkin.skinId))
                {
                    currentKnifeSkin = KnifeSkinDefinition.GetKnifeSkinById(loadout.knifeSkin.skinId);
                }
            }

            if (currentKnifeSkin == null)
            {
                currentKnifeSkin = KnifeSkinDefinition.GetDefaultKnifeSkin();
                // Debug.Log($"âš ï¸ Using default knife skin: {currentKnifeSkin?.displayName}");
            }
            UpdateMainViewDisplay();
        }

        /// <summary>
        /// Update main view to show current weapons
        /// </summary>
        private void UpdateMainViewDisplay()
        {
            // Null check for UI elements
            if (primaryWeaponName == null || secondaryWeaponName == null)
            {
                // Debug.LogError("âŒ UpdateMainViewDisplay: Weapon name labels are null!");
                return;
            }
            
            // Update primary weapon display
            if (currentPrimaryWeapon != null)
            {
                primaryWeaponName.text = currentPrimaryWeapon.displayName;
                
                // Use skin icon if a non-default skin is equipped
                string primaryIconPath = currentPrimaryWeapon.iconPath;
                if (currentPrimarySkinId != "default")
                {
                    var skin = WeaponSkinDefinition.GetSkin(currentPrimaryWeapon.weaponId, currentPrimarySkinId);
                    if (skin != null) primaryIconPath = skin.iconPath;
                }
                var primaryIcon = Resources.Load<Texture2D>(primaryIconPath);
                if (primaryIcon != null && primaryWeaponIcon != null)
                {
                    primaryWeaponIcon.style.backgroundImage = new StyleBackground(primaryIcon);
                }
                else
                {
                    // Debug.LogWarning($"âš ï¸ Could not load icon: {currentPrimaryWeapon.iconPath}");
                }
            }
            else
            {
                primaryWeaponName.text = "PRIMARY";
            }

            // Update secondary weapon display
            if (currentSecondaryWeapon != null)
            {
                secondaryWeaponName.text = currentSecondaryWeapon.displayName;
                
                // Use skin icon if a non-default skin is equipped
                string secondaryIconPath = currentSecondaryWeapon.iconPath;
                if (currentSecondarySkinId != "default")
                {
                    var skin = WeaponSkinDefinition.GetSkin(currentSecondaryWeapon.weaponId, currentSecondarySkinId);
                    if (skin != null) secondaryIconPath = skin.iconPath;
                }
                var secondaryIcon = Resources.Load<Texture2D>(secondaryIconPath);
                if (secondaryIcon != null && secondaryWeaponIcon != null)
                {
                    secondaryWeaponIcon.style.backgroundImage = new StyleBackground(secondaryIcon);
                }
                else
                {
                    // Debug.LogWarning($"âš ï¸ Could not load icon: {currentSecondaryWeapon.iconPath}");
                }
            }
            else
            {
                secondaryWeaponName.text = "SECONDARY";
            }

            // Update knife display
            if (currentKnifeSkin != null && knifeName != null)
            {
                knifeName.text = currentKnifeSkin.displayName;
                
                // Load icon from Resources
                var knifeIconTexture = Resources.Load<Texture2D>(currentKnifeSkin.iconPath);
                if (knifeIconTexture != null && knifeIcon != null)
                {
                    knifeIcon.style.backgroundImage = new StyleBackground(knifeIconTexture);
                }
                else
                {
                    // Debug.LogWarning($"âš ï¸ Could not load icon: {currentKnifeSkin.iconPath}");
                }
            }
            else
            {
                if (knifeName != null) knifeName.text = "KNIFE";
            }
        }

        // ===================================
        // VIEW NAVIGATION
        // ===================================

        private void ShowMainView()
        {
            mainView?.RemoveFromClassList("hidden");
            selectionView?.AddToClassList("hidden");
            selectionHeader?.AddToClassList("hidden");
            knifeSkinsView?.AddToClassList("hidden");
            knifeSkinsHeader?.AddToClassList("hidden");
            weaponSkinsView?.AddToClassList("hidden");
        }

        private void ShowSelectionView(WeaponDefinition.WeaponCategory category)
        {
            currentSelectionCategory = category;
            
            mainView?.AddToClassList("hidden");
            selectionView?.RemoveFromClassList("hidden");
            selectionHeader?.RemoveFromClassList("hidden");
            knifeSkinsView?.AddToClassList("hidden");
            knifeSkinsHeader?.AddToClassList("hidden");
            weaponSkinsView?.AddToClassList("hidden");

            // Update title
            string categoryName = category == WeaponDefinition.WeaponCategory.Primary ? "PRIMARY" : "SECONDARY";
            selectionTitle.text = $"{categoryName} WEAPONS";

            // Populate weapons grid
            PopulateWeaponsGrid(category);
            
            // Update lock-in button state
            UpdateLockInButton();
        }

        private void ShowKnifeSkinsView()
        {
            mainView?.AddToClassList("hidden");
            selectionView?.AddToClassList("hidden");
            selectionHeader?.AddToClassList("hidden");
            knifeSkinsView?.RemoveFromClassList("hidden");
            knifeSkinsHeader?.RemoveFromClassList("hidden");
            weaponSkinsView?.AddToClassList("hidden");

            // Update title
            if (knifeSkinsTitle != null)
                knifeSkinsTitle.text = "KNIFE SKINS";

            // Populate knife skins grid
            PopulateKnifeSkinsGrid();
            
            // Update select button state
            UpdateKnifeSelectButton();
        }

        private void ShowWeaponSkinsView(WeaponDefinition.Weapon weapon)
        {
            mainView?.AddToClassList("hidden");
            selectionView?.AddToClassList("hidden");
            selectionHeader?.AddToClassList("hidden");
            knifeSkinsView?.AddToClassList("hidden");
            knifeSkinsHeader?.AddToClassList("hidden");
            weaponSkinsView?.RemoveFromClassList("hidden");

            if (weaponSkinsTitle != null)
                weaponSkinsTitle.text = $"{weapon.displayName} SKINS";

            PopulateWeaponSkinsGrid(weapon);
            UpdateWeaponSkinEquipButton();
        }

        // ===================================
        // WEAPONS GRID
        // ===================================

        private void PopulateWeaponsGrid(WeaponDefinition.WeaponCategory category)
        {
            weaponsGrid.Clear();

            var weapons = WeaponDefinition.GetWeaponsByCategory(category);
            
            // Set initially selected weapon based on current loadout
            var currentWeapon = category == WeaponDefinition.WeaponCategory.Primary 
                ? currentPrimaryWeapon 
                : currentSecondaryWeapon;
            
            selectedWeaponInGrid = currentWeapon;

            foreach (var weapon in weapons)
            {
                var weaponCard = CreateWeaponCard(weapon, weapon == currentWeapon);
                weaponsGrid.Add(weaponCard);
            }

            // Debug.Log($"ðŸ“‹ Populated grid with {weapons.Count} {category} weapons");
        }

        private VisualElement CreateWeaponCard(WeaponDefinition.Weapon weapon, bool selected)
        {
            var card = new VisualElement();
            card.AddToClassList("weapon-card");
            if (selected)
                card.AddToClassList("selected");

            // Name label (top center)
            var nameLabel = new Label(weapon.displayName);
            nameLabel.AddToClassList("weapon-card-name");
            card.Add(nameLabel);

            // Icon (fills remaining space)
            var icon = new VisualElement();
            icon.AddToClassList("weapon-card-icon");
            
            var weaponIcon = Resources.Load<Texture2D>(weapon.iconPath);
            if (weaponIcon != null)
            {
                icon.style.backgroundImage = new StyleBackground(weaponIcon);
            }
            
            card.Add(icon);

            // Click handler
            card.RegisterCallback<ClickEvent>(evt =>
            {
                ArtisansGuns.Managers.SoundManager.Instance?.PlayClick();
                OnWeaponCardClicked(weapon, card);
            });

            return card;
        }

        // ===================================
        // KNIFE SKINS GRID
        // ===================================

        private void PopulateKnifeSkinsGrid()
        {
            if (knifeSkinsGrid == null) return;
            
            knifeSkinsGrid.Clear();

            var allSkins = KnifeSkinDefinition.GetAllKnifeSkins();
            
            // Set initially selected skin based on current equipped skin
            selectedKnifeSkinInGrid = currentKnifeSkin;

            foreach (var skin in allSkins)
            {
                var skinCard = CreateKnifeSkinCard(skin, skin == currentKnifeSkin);
                knifeSkinsGrid.Add(skinCard);
            }

            // Debug.Log($"ðŸ"ª Populated knife skins grid with {allSkins.Count} skins");
        }

        private VisualElement CreateKnifeSkinCard(KnifeSkinDefinition.KnifeSkin skin, bool equipped)
        {
            var card = new VisualElement();
            card.AddToClassList("knife-skin-card");
            if (equipped)
                card.AddToClassList("selected");

            // Name label (top center)
            var nameLabel = new Label(skin.displayName);
            nameLabel.AddToClassList("knife-skin-name");
            card.Add(nameLabel);

            // Icon (fills remaining space)
            var icon = new VisualElement();
            icon.AddToClassList("knife-skin-icon");
            
            var skinIcon = Resources.Load<Texture2D>(skin.iconPath);
            if (skinIcon != null)
            {
                icon.style.backgroundImage = new StyleBackground(skinIcon);
            }
            
            card.Add(icon);

            // Check if unlocked
            bool isUnlocked = LoadoutManager.Instance?.IsSkinUnlocked("knife", skin.skinId) ?? skin.isDefault;
            
            // If not unlocked, show cost and lock indicator
            if (!isUnlocked)
            {
                card.AddToClassList("locked");
                
                var costLabel = new Label($"{skin.cost} BP");
                costLabel.AddToClassList("knife-skin-cost");
                card.Add(costLabel);
            }

            // Click handler
            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (isUnlocked)
                {
                    ArtisansGuns.Managers.SoundManager.Instance?.PlayClick();
                    OnKnifeSkinCardClicked(skin, card);
                }
                else
                {
                    // TODO: Show purchase dialog
                    // Debug.Log($"ðŸ"' Knife skin {skin.displayName} is locked. Cost: {skin.cost} BP");
                }
            });

            return card;
        }

        // ===================================
        // WEAPON SKINS GRID
        // ===================================

        private void PopulateWeaponSkinsGrid(WeaponDefinition.Weapon weapon)
        {
            if (weaponSkinsGrid == null) return;

            weaponSkinsGrid.Clear();

            var skins = WeaponSkinDefinition.GetSkinsForWeapon(weapon.weaponId);

            // Determine current equipped skin for this weapon
            string equippedSkinId = "default";
            if (weapon.category == WeaponDefinition.WeaponCategory.Primary)
                equippedSkinId = currentPrimarySkinId;
            else if (weapon.category == WeaponDefinition.WeaponCategory.Secondary)
                equippedSkinId = currentSecondarySkinId;

            selectedWeaponSkinInGrid = null;

            foreach (var skin in skins)
            {
                bool isEquipped = skin.skinId == equippedSkinId;
                var skinCard = CreateWeaponSkinCard(skin, isEquipped);
                weaponSkinsGrid.Add(skinCard);

                if (isEquipped)
                    selectedWeaponSkinInGrid = skin;
            }
        }

        private VisualElement CreateWeaponSkinCard(WeaponSkinDefinition.WeaponSkin skin, bool equipped)
        {
            var card = new VisualElement();
            card.AddToClassList("weapon-skin-card");
            if (equipped)
                card.AddToClassList("selected");

            // Name label (top center)
            var nameLabel = new Label(skin.displayName);
            nameLabel.AddToClassList("weapon-skin-name");
            card.Add(nameLabel);

            // Icon (fills remaining space)
            var icon = new VisualElement();
            icon.AddToClassList("weapon-skin-icon");

            var skinIcon = Resources.Load<Texture2D>(skin.iconPath);
            if (skinIcon != null)
            {
                icon.style.backgroundImage = new StyleBackground(skinIcon);
            }

            card.Add(icon);

            // Check if unlocked
            bool isUnlocked = skin.isDefault || (LoadoutManager.Instance?.IsSkinUnlocked(skin.weaponId, skin.skinId) ?? false);

            if (!isUnlocked)
            {
                card.AddToClassList("locked");

                var lockOverlay = new VisualElement();
                lockOverlay.AddToClassList("weapon-skin-lock-overlay");
                var lockIcon = new Label("\U0001F512");
                lockIcon.AddToClassList("weapon-skin-lock-icon");
                lockOverlay.Add(lockIcon);
                card.Add(lockOverlay);
            }

            // Click handler
            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (isUnlocked)
                {
                    ArtisansGuns.Managers.SoundManager.Instance?.PlayClick();
                    OnWeaponSkinCardClicked(skin, card);
                }
            });

            return card;
        }

        private void OnWeaponSkinCardClicked(WeaponSkinDefinition.WeaponSkin skin, VisualElement card)
        {
            if (weaponSkinsGrid == null) return;

            // Deselect all
            var allCards = weaponSkinsGrid.Query<VisualElement>(className: "weapon-skin-card").ToList();
            foreach (var c in allCards)
                c.RemoveFromClassList("selected");

            card.AddToClassList("selected");
            selectedWeaponSkinInGrid = skin;
            UpdateWeaponSkinEquipButton();
        }

        private void OnWeaponSkinsBackClicked()
        {
            // Go back to the weapon selection view
            ShowSelectionView(currentSelectionCategory);
        }

        private void OnWeaponSkinEquipClicked()
        {
            if (selectedWeaponSkinInGrid == null || selectedWeaponInGrid == null) return;

            ArtisansGuns.Managers.SoundManager.Instance?.PlaySelect();

            string skinId = selectedWeaponSkinInGrid.skinId;

            // Update the current skin tracking
            if (currentSelectionCategory == WeaponDefinition.WeaponCategory.Primary)
            {
                currentPrimarySkinId = skinId;
            }
            else
            {
                currentSecondarySkinId = skinId;
            }

            // Update the weapon icon in the main view to use skin icon
            UpdateMainViewDisplay();

            // Save to loadout
            if (currentSelectionCategory == WeaponDefinition.WeaponCategory.Primary)
            {
                SaveWeaponToLoadout(selectedWeaponInGrid, currentSelectionCategory);
            }
            else
            {
                SaveWeaponToLoadout(selectedWeaponInGrid, currentSelectionCategory);
            }

            // Update weapon card icon in the selection grid to use skin icon
            // Go back to selection view
            ShowSelectionView(currentSelectionCategory);
        }

        private void UpdateWeaponSkinEquipButton()
        {
            if (weaponSkinEquipButton == null || selectedWeaponSkinInGrid == null) return;

            string equippedSkinId = "default";
            if (currentSelectionCategory == WeaponDefinition.WeaponCategory.Primary)
                equippedSkinId = currentPrimarySkinId;
            else if (currentSelectionCategory == WeaponDefinition.WeaponCategory.Secondary)
                equippedSkinId = currentSecondarySkinId;

            bool isEquipped = selectedWeaponSkinInGrid.skinId == equippedSkinId;

            if (isEquipped)
            {
                weaponSkinEquipButton.text = "EQUIPPED";
                weaponSkinEquipButton.SetEnabled(false);
            }
            else
            {
                weaponSkinEquipButton.text = "EQUIP";
                weaponSkinEquipButton.SetEnabled(true);
            }
        }

        // ===================================
        // EVENT HANDLERS
        // ===================================

        private void OnPrimarySlotClicked()
        {
            // Debug.Log("ðŸ”« Primary slot clicked - showing primary weapons");
            ShowSelectionView(WeaponDefinition.WeaponCategory.Primary);
        }

        private void OnSecondarySlotClicked()
        {
            // Debug.Log("ðŸ”« Secondary slot clicked - showing secondary weapons");
            ShowSelectionView(WeaponDefinition.WeaponCategory.Secondary);
        }
        
        private void OnKnifeSlotClicked()
        {
            // Knife goes directly to Knife Skins screen
            // Debug.Log("ðŸ"ª Knife slot clicked - showing knife skins view");
            ShowKnifeSkinsView();
        }
        private void OnSkinsButtonClicked()
        {
            // Show skins for the currently selected weapon in the grid
            if (selectedWeaponInGrid != null)
            {
                ShowWeaponSkinsView(selectedWeaponInGrid);
            }
        }        
        private void OnBackButtonClicked()
        {
            // Debug.Log("â—„ Back button clicked - returning to main view");
            ShowMainView();
        }

        private void OnKnifeBackButtonClicked()
        {
            // Debug.Log("â—„ Knife back button clicked - returning to main view");
            ShowMainView();
        }

        private void OnKnifeSkinCardClicked(KnifeSkinDefinition.KnifeSkin skin, VisualElement card)
        {
            // Debug.Log($"ðŸŽ¯ Knife skin selected: {skin.displayName}");

            // Deselect all cards
            if (knifeSkinsGrid != null)
            {
                var allCards = knifeSkinsGrid.Query<VisualElement>(className: "knife-skin-card").ToList();
                foreach (var c in allCards)
                {
                    c.RemoveFromClassList("selected");
                }
            }

            // Select clicked card
            card.AddToClassList("selected");
            selectedKnifeSkinInGrid = skin;
            
            // Update select button
            UpdateKnifeSelectButton();
        }

        private void OnKnifeSelectClicked()
        {
            if (selectedKnifeSkinInGrid == null)
            {
                // Debug.LogWarning("âš ï¸ No knife skin selected!");
                return;
            }

            ArtisansGuns.Managers.SoundManager.Instance?.PlaySelect();

            // Debug.Log($"ðŸ"' Selecting knife skin: {selectedKnifeSkinInGrid.displayName}");

            // Update current knife skin
            currentKnifeSkin = selectedKnifeSkinInGrid;

            // Save to LoadoutManager
            SaveKnifeSkinToLoadout(selectedKnifeSkinInGrid);

            // Update main view and return
            UpdateMainViewDisplay();
            ShowMainView();
        }

        private void OnWeaponCardClicked(WeaponDefinition.Weapon weapon, VisualElement card)
        {
            // Debug.Log($"ðŸŽ¯ Weapon selected: {weapon.displayName}");

            // Deselect all cards
            var allCards = weaponsGrid.Query<VisualElement>(className: "weapon-card").ToList();
            foreach (var c in allCards)
            {
                c.RemoveFromClassList("selected");
            }

            // Select clicked card
            card.AddToClassList("selected");
            selectedWeaponInGrid = weapon;
            
            // Update lock-in button based on current weapon
            UpdateLockInButton();
        }

        private void OnLockInClicked()
        {
            if (selectedWeaponInGrid == null)
            {
                // Debug.LogWarning("âš ï¸ No weapon selected!");
                return;
            }
            ArtisansGuns.Managers.SoundManager.Instance?.PlaySelect();
            // Debug.Log($"ðŸ”’ Locking in weapon: {selectedWeaponInGrid.displayName} ({currentSelectionCategory})");

            // Update current weapon
            if (currentSelectionCategory == WeaponDefinition.WeaponCategory.Primary)
            {
                currentPrimaryWeapon = selectedWeaponInGrid;
            }
            else
            {
                currentSecondaryWeapon = selectedWeaponInGrid;
            }

            // Save to LoadoutManager
            SaveWeaponToLoadout(selectedWeaponInGrid, currentSelectionCategory);

            // Update main view and return
            UpdateMainViewDisplay();
            ShowMainView();
        }

        // ===================================
        // LOADOUT MANAGER INTEGRATION
        // ===================================

        private void SaveWeaponToLoadout(WeaponDefinition.Weapon weapon, WeaponDefinition.WeaponCategory category)
        {
            if (LoadoutManager.Instance == null)
            {
                // Debug.LogWarning("âš ï¸ LoadoutManager not available - weapon selection saved locally only");
                return;
            }

            string weaponId = weapon.weaponId;
            string skinId = category == WeaponDefinition.WeaponCategory.Primary 
                ? currentPrimarySkinId 
                : currentSecondarySkinId;

            if (category == WeaponDefinition.WeaponCategory.Primary)
            {
                LoadoutManager.Instance.UpdatePrimaryWeapon(weaponId, skinId, (success) =>
                {
                    if (success)
                    {
                        // Debug.Log($"âœ… Primary weapon saved to backend: {weapon.displayName}");
                    }
                    else
                    {
                        // Debug.LogError($"âŒ Failed to save primary weapon to backend");
                    }
                });
            }
            else
            {
                LoadoutManager.Instance.UpdateSecondaryWeapon(weaponId, skinId, (success) =>
                {
                    if (success)
                    {
                        // Debug.Log($"âœ… Secondary weapon saved to backend: {weapon.displayName}");
                    }
                    else
                    {
                        // Debug.LogError($"âŒ Failed to save secondary weapon to backend");
                    }
                });
            }
        }

        /// <summary>
        /// Updates the lock-in button text based on whether the selected weapon is already equipped
        /// </summary>
        private void UpdateLockInButton()
        {
            if (lockInButton == null || selectedWeaponInGrid == null)
                return;

            // Check if the selected weapon is the current weapon for this category
            bool isCurrentWeapon = false;
            
            if (currentSelectionCategory == WeaponDefinition.WeaponCategory.Primary)
            {
                isCurrentWeapon = currentPrimaryWeapon != null && currentPrimaryWeapon.weaponId == selectedWeaponInGrid.weaponId;
            }
            else
            {
                isCurrentWeapon = currentSecondaryWeapon != null && currentSecondaryWeapon.weaponId == selectedWeaponInGrid.weaponId;
            }

            if (isCurrentWeapon)
            {
                lockInButton.text = "SELECTED";
                lockInButton.SetEnabled(false);
                lockInButton.style.backgroundColor = new Color(0.3f, 0.6f, 0.3f, 0.8f); // Green-ish
            }
            else
            {
                lockInButton.text = "LOCK IN";
                lockInButton.SetEnabled(true);
                lockInButton.style.backgroundColor = new Color(0.2f, 0.4f, 0.8f, 1f); // Blue
            }
        }

        private void SaveKnifeSkinToLoadout(KnifeSkinDefinition.KnifeSkin skin)
        {
            if (LoadoutManager.Instance == null)
            {
                // Debug.LogWarning("⚠️ LoadoutManager not available - knife skin selection saved locally only");
                return;
            }

            LoadoutManager.Instance.UpdateKnifeSkin(skin.skinId, (success) =>
            {
                if (success)
                {
                    // Debug.Log($"✅ Knife skin saved to backend: {skin.displayName}");
                }
                else
                {
                    // Debug.LogError($"❌ Failed to save knife skin to backend");
                }
            });
        }

        private void UpdateKnifeSelectButton()
        {
            if (knifeSelectButton == null || selectedKnifeSkinInGrid == null)
                return;

            // Check if the selected knife skin is the current equipped skin
            bool isCurrentSkin = currentKnifeSkin != null && 
                                 currentKnifeSkin.skinId == selectedKnifeSkinInGrid.skinId;

            if (isCurrentSkin)
            {
                knifeSelectButton.text = "EQUIPPED";
                knifeSelectButton.SetEnabled(false);
                knifeSelectButton.style.backgroundColor = new Color(0.3f, 0.6f, 0.3f, 0.8f); // Green-ish
            }
            else
            {
                knifeSelectButton.text = "SELECT";
                knifeSelectButton.SetEnabled(true);
                knifeSelectButton.style.backgroundColor = new Color(0.2f, 0.4f, 0.8f, 1f); // Blue
            }
        }
    }
}
