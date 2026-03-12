using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using ArtisansGuns.Networking;
using ArtisansGuns.Managers;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// PersistentUIManager - Manages UI elements that persist across all scenes
    /// Includes: Header, Currency Display, Settings, Friends Panel (future)
    /// This ensures seamless navigation like Valorant - no UI flickering between scenes
    /// 
    /// SETUP INSTRUCTIONS:
    /// 1. Create empty GameObject "PersistentUI" in LOBBYSCENE (first scene with header)
    /// 2. Add UIDocument component, assign PersistentUI.uxml
    /// 3. Add this PersistentUIManager component
    /// 4. GameObject will persist automatically across LobbyScene, RoomScene, GameScene, etc.
    /// 
    /// IMPORTANT: 
    /// - Must be created in LobbyScene (first scene after login)
    /// - LoginScene should NOT have this (no header before login)
    /// - DontDestroyOnLoad handles persistence automatically
    /// </summary>
    public class PersistentUIManager : MonoBehaviour
    {
        public static PersistentUIManager Instance { get; private set; }

        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;

        // Header Navigation Buttons
        private Button lobbyButton;
        private Button weaponsButton;
        private Button charactersButton;
        private Button shopButton;
        private Label logoLabel; // Logo button label (LOBBY / LEAVE)

        // Currency Display
        private Label rivalEssenceLabel;
        private Label rivalPointsLabel;

        // Settings Button
        private Button settingsButton;

        // Room-specific UI (only visible in RoomScene)
        private VisualElement roomNavContainer;
        private Button roomButton;
        private Label playersCountLabel;
        
        // Room tab content (managed when in RoomScene)
        private VisualElement roomContent;
        private VisualElement weaponsContent;
        private VisualElement agentsContent;
        
        // Event subscription tracking
        private bool isSubscribedToLoadout = false;

        private void Awake()
        {
            // Singleton pattern
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Ensure UIDocument component exists
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }
            
            // Verify UIDocument has a visual tree asset assigned
            if (uiDocument == null || uiDocument.visualTreeAsset == null)
            {
                // Debug.LogError("âŒ PersistentUIManager requires a UIDocument with PersistentUI.uxml assigned!");
                return;
            }

            // Debug.Log("âœ… PersistentUIManager initialized - UI will persist across scenes");
        }

        private void Start()
        {
            // Initialize after Awake to ensure UIDocument is ready
            InitializeUI();
        }
        
        private void InitializeUI()
        {
            if (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                // Debug.LogError("âŒ UIDocument not ready for initialization");
                return;
            }
            
            CacheUIElements();
            RegisterEvents();
            UpdateUIForCurrentScene();
        }
        
        private void CacheUIElements()
        {
            var root = uiDocument.rootVisualElement;

            // Cache header elements
            lobbyButton = root.Q<Button>("LobbyButton");
            logoLabel = root.Q<Label>("LogoLabel");
            weaponsButton = root.Q<Button>("WeaponsButton");
            charactersButton = root.Q<Button>("CharactersButton");
            shopButton = root.Q<Button>("ShopButton");
            settingsButton = root.Q<Button>("SettingsButton");

            // Currency labels
            rivalEssenceLabel = root.Q<Label>("RivalEssenceLabel");
            rivalPointsLabel = root.Q<Label>("RivalPointsLabel");

            // Room-specific elements
            roomNavContainer = root.Q<VisualElement>("RoomNavContainer");
            roomButton = root.Q<Button>("RoomButton");
            playersCountLabel = root.Q<Label>("PlayersCountLabel");
            
            // Try to find room tab content (only exists in RoomScene)
            roomContent = null;
            weaponsContent = null;
            agentsContent = null;
        }
        
        private void RegisterEvents()
        {
            // Register navigation events
            lobbyButton?.RegisterCallback<ClickEvent>(evt => OnLobbyButtonClicked());
            weaponsButton?.RegisterCallback<ClickEvent>(evt => OnWeaponsButtonClicked());
            charactersButton?.RegisterCallback<ClickEvent>(evt => OnCharactersButtonClicked());
            shopButton?.RegisterCallback<ClickEvent>(evt => OnShopButtonClicked());
            settingsButton?.RegisterCallback<ClickEvent>(evt => OnSettingsButtonClicked());
            roomButton?.RegisterCallback<ClickEvent>(evt => OnRoomButtonClicked());

            // Subscribe to scene changes
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // Subscribe to loadout updates for currency display
            if (LoadoutManager.Instance != null && !isSubscribedToLoadout)
            {
                // Debug.Log("âœ… [PersistentUI] Subscribing to LoadoutManager events");
                LoadoutManager.Instance.OnLoadoutUpdated += OnLoadoutUpdated;
                isSubscribedToLoadout = true;
            }
            else if (LoadoutManager.Instance == null)
            {
                // Debug.LogWarning("âš ï¸ [PersistentUI] LoadoutManager not ready yet - will subscribe later");
            }
        }
        
        private void OnEnable()
        {
            // Re-register events if object was disabled/re-enabled
            if (uiDocument != null && uiDocument.rootVisualElement != null && lobbyButton == null)
            {
                CacheUIElements();
                RegisterEvents();
                UpdateUIForCurrentScene();
            }
            
            // Try to subscribe to LoadoutManager if it wasn't available earlier
            if (LoadoutManager.Instance != null && !isSubscribedToLoadout)
            {
                // Debug.Log("âœ… [PersistentUI] Late subscription to LoadoutManager");
                LoadoutManager.Instance.OnLoadoutUpdated += OnLoadoutUpdated;
                isSubscribedToLoadout = true;
                
                // Immediately update currency display
                UpdateCurrencyDisplay();
            }
        }

        private void OnDisable()
        {
            // Unregister events
            if (lobbyButton != null)
            {
                lobbyButton.UnregisterCallback<ClickEvent>(evt => OnLobbyButtonClicked());
                weaponsButton?.UnregisterCallback<ClickEvent>(evt => OnWeaponsButtonClicked());
                charactersButton?.UnregisterCallback<ClickEvent>(evt => OnCharactersButtonClicked());
                shopButton?.UnregisterCallback<ClickEvent>(evt => OnShopButtonClicked());
                settingsButton?.UnregisterCallback<ClickEvent>(evt => OnSettingsButtonClicked());
                roomButton?.UnregisterCallback<ClickEvent>(evt => OnRoomButtonClicked());
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            // Unsubscribe from LoadoutManager events
            if (LoadoutManager.Instance != null)
            {
                LoadoutManager.Instance.OnLoadoutUpdated -= OnLoadoutUpdated;
            }
            
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Update UI elements based on current scene
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Clear room content references when leaving RoomScene
            if (scene.name != "RoomScene")
            {
                roomContent = null;
                weaponsContent = null;
                agentsContent = null;
            }
            
            UpdateUIForCurrentScene();
        }

        private void UpdateUIForCurrentScene()
        {
            string currentScene = SceneManager.GetActiveScene().name;

            // IMPORTANTE: Ocultar TODO el UI persistente en escenas de gameplay.
            // El UIDocument persistente cubre toda la pantalla con PickingMode.Position;
            // si permanece activo en gameplay bloquea el joystick y todos los botones del Canvas.
            // Añadir aquí cualquier escena de mapa que se agregue en el futuro.
            bool isGameplayScene = currentScene == "Sandbox" || currentScene == "GameScene";
            Debug.Log($"[PersistentUI] UpdateUIForCurrentScene: scene='{currentScene}' isGameplayScene={isGameplayScene}");
            if (isGameplayScene)
            {
                gameObject.SetActive(false); // Ocultar completamente el GameObject
                return;
            }
            else
            {
                // Asegurar que estÃ© visible en otras escenas
                if (!gameObject.activeSelf)
                {
                    gameObject.SetActive(true);
                }
            }

            // Update active navigation button
            UpdateActiveNavButton(currentScene);

            // Update logo button text and show/hide room-specific UI
            if (currentScene == "RoomScene")
            {
                // In room: Show LEAVE, show room nav, show player count
                if (logoLabel != null)
                {
                    logoLabel.text = "LEAVE";
                }
                roomNavContainer?.RemoveFromClassList("hidden");
                playersCountLabel?.RemoveFromClassList("hidden");
            }
            else
            {
                // In lobby or other scenes: Show LOBBY, hide room nav, hide player count
                if (logoLabel != null)
                {
                    logoLabel.text = "LOBBY";
                }
                roomNavContainer?.AddToClassList("hidden");
                playersCountLabel?.AddToClassList("hidden");
            }

            // Update currency (could be fetched from backend/PlayerPrefs)
            UpdateCurrencyDisplay();
        }

        private void UpdateActiveNavButton(string sceneName)
        {
            // Remove active class from all buttons
            lobbyButton?.RemoveFromClassList("logo-button-active");
            weaponsButton?.RemoveFromClassList("nav-button-active");
            charactersButton?.RemoveFromClassList("nav-button-active");
            roomButton?.RemoveFromClassList("nav-button-active");

            // Add active class to current button (default lobby/room tabs)
            switch (sceneName)
            {
                case "LobbyScene":
                case "WeaponsScene":
                case "CharactersScene":
                    lobbyButton?.AddToClassList("logo-button-active");
                    break;
                case "RoomScene":
                    roomButton?.AddToClassList("nav-button-active");
                    break;
            }
        }

        /// <summary>
        /// Called when loadout data is updated (from LoadoutManager event)
        /// </summary>
        private void OnLoadoutUpdated(LoadoutManager.LoadoutData loadout)
        {
            // Debug.Log($"ðŸ”” [PersistentUI] OnLoadoutUpdated event received - Blue Points: {loadout.bluePoints}, Rival Coins: {loadout.rivalCoins}");
            UpdateCurrencyDisplay();
        }

        /// <summary>
        /// Update currency display (Blue Points + Rival Coins)
        /// </summary>
        private void UpdateCurrencyDisplay()
        {
            // Debug.Log("ðŸ”„ [PersistentUI] UpdateCurrencyDisplay called");
            
            var loadout = LoadoutManager.Instance?.GetLoadout();
            
            if (loadout != null)
            {
                // Debug.Log($"ðŸ’° [PersistentUI] Loadout found - Blue Points: {loadout.bluePoints}, Rival Coins: {loadout.rivalCoins}");
                
                if (rivalEssenceLabel != null)
                {
                    string formattedBluePoints = loadout.bluePoints.ToString("#,##0");
                    rivalEssenceLabel.text = formattedBluePoints;
                    // Debug.Log($"âœ… [PersistentUI] Set rivalEssenceLabel to: {formattedBluePoints}");
                }
                else
                {
                    // Debug.LogError("âŒ [PersistentUI] rivalEssenceLabel is NULL!");
                }

                if (rivalPointsLabel != null)
                {
                    string formattedRivalCoins = loadout.rivalCoins.ToString("#,##0");
                    rivalPointsLabel.text = formattedRivalCoins;
                    // Debug.Log($"âœ… [PersistentUI] Set rivalPointsLabel to: {formattedRivalCoins}");
                }
                else
                {
                    // Debug.LogError("âŒ [PersistentUI] rivalPointsLabel is NULL!");
                }
            }
            else
            {
                // Debug.LogWarning("âš ï¸ [PersistentUI] LoadoutManager.Instance or loadout is null - using fallback 0");
                
                // Fallback to 0 if loadout not initialized
                if (rivalEssenceLabel != null)
                {
                    rivalEssenceLabel.text = "0";
                }

                if (rivalPointsLabel != null)
                {
                    rivalPointsLabel.text = "0";
                }
            }
        }

        /// <summary>
        /// Update player count in room (called from RoomUIController)
        /// </summary>
        public void UpdatePlayerCount(int current, int max)
        {
            if (playersCountLabel != null)
            {
                playersCountLabel.text = $"{current}/{max}";
            }
        }

        // ===================================
        // Navigation Handlers
        // ===================================

        private void OnLobbyButtonClicked()
        {
            string currentScene = SceneManager.GetActiveScene().name;

            if (currentScene == "RoomScene")
            {
                // Leave room and return to lobby
                LeaveRoomAsync();
            }
            else if (currentScene == "LobbyScene")
            {
                // Switch to lobby tab within LobbyScene
                SwitchLobbyTab("lobby");
            }
            else if (currentScene != "LobbyScene")
            {
                SceneManager.LoadScene("LobbyScene");
            }
        }
        
        /// <summary>
        /// Async method to leave room properly
        /// </summary>
        private async void LeaveRoomAsync()
        {
            // Debug.Log("ðŸšª Leaving room from persistent UI...");
            
            if (NetworkManager.Instance != null)
            {
                await NetworkManager.Instance.LeaveRoom();
            }
            else
            {
                // Fallback
                SceneManager.LoadScene("LobbyScene");
            }
        }

        private void OnWeaponsButtonClicked()
        {
            string currentScene = SceneManager.GetActiveScene().name;
            
            if (currentScene == "RoomScene")
            {
                // In room: Switch to weapons tab within room
                SwitchRoomTab("weapons");
            }
            else if (currentScene == "LobbyScene")
            {
                // In lobby: Switch to weapons tab
                SwitchLobbyTab("weapons");
            }
            else
            {
                // Debug.Log("ðŸ”« Weapons button clicked - TODO: Implement weapons scene/panel");
                // TODO: Load WeaponsScene or show weapons panel overlay
            }
        }

        private void OnCharactersButtonClicked()
        {
            string currentScene = SceneManager.GetActiveScene().name;
            
            if (currentScene == "RoomScene")
            {
                // In room: Switch to agents tab within room
                SwitchRoomTab("agents");
            }
            else if (currentScene == "LobbyScene")
            {
                // In lobby: Switch to agents tab
                SwitchLobbyTab("agents");
            }
            else
            {
                // Debug.Log("ðŸ‘¤ Characters button clicked - TODO: Implement characters scene/panel");
                // TODO: Load CharactersScene or show characters panel overlay
            }
        }
        private void OnShopButtonClicked()
        {
            string currentScene = SceneManager.GetActiveScene().name;
            
            if (currentScene == "LobbyScene")
            {
                SwitchLobbyTab("shop");
            }
        }
        private void OnSettingsButtonClicked()
        {
            // Debug.Log("âš™ï¸ Settings button clicked - TODO: Implement settings overlay");
            // TODO: Show settings overlay
        }

        private void OnRoomButtonClicked()
        {
            // Switch back to room content
            SwitchRoomTab("room");
        }
        
        /// <summary>
        /// Switch between room tabs (ROOM/WEAPONS/AGENTS) - Only works in RoomScene
        /// </summary>
        private void SwitchRoomTab(string tabName)
        {
            // Find room content elements if not cached
            if (roomContent == null)
            {
                var activeScene = SceneManager.GetActiveScene();
                var rootObjects = activeScene.GetRootGameObjects();
                
                foreach (var obj in rootObjects)
                {
                    var uiDoc = obj.GetComponent<UIDocument>();
                    if (uiDoc != null)
                    {
                        var root = uiDoc.rootVisualElement;
                        roomContent = root.Q<VisualElement>("RoomContent");
                        weaponsContent = root.Q<VisualElement>("WeaponsContent");
                        agentsContent = root.Q<VisualElement>("AgentsContent");
                        break;
                    }
                }
            }
            
            if (roomContent == null)
            {
                // Debug.LogWarning("âš ï¸ Room content not found - tab switching unavailable");
                return;
            }
            
            // Debug.Log($"ðŸ”„ Switching to room tab: {tabName}");
            
            // Hide all content
            roomContent?.AddToClassList("hidden");
            weaponsContent?.AddToClassList("hidden");
            agentsContent?.AddToClassList("hidden");
            
            // Remove active from all buttons
            roomButton?.RemoveFromClassList("nav-button-active");
            weaponsButton?.RemoveFromClassList("nav-button-active");
            charactersButton?.RemoveFromClassList("nav-button-active");
            
            // Show selected content and activate button
            switch (tabName)
            {
                case "room":
                    roomContent?.RemoveFromClassList("hidden");
                    roomButton?.AddToClassList("nav-button-active");
                    break;
                case "weapons":
                    weaponsContent?.RemoveFromClassList("hidden");
                    weaponsButton?.AddToClassList("nav-button-active");
                    break;
                case "agents":
                    agentsContent?.RemoveFromClassList("hidden");
                    charactersButton?.AddToClassList("nav-button-active");
                    break;
            }
        }
        
        /// <summary>
        /// Switch between lobby tabs (LOBBY/WEAPONS/AGENTS) - Only works in LobbyScene
        /// </summary>
        private void SwitchLobbyTab(string tabName)
        {
            // Find LobbyUIController in current scene
            var lobbyController = FindObjectOfType<LobbyUIController>();
            
            if (lobbyController != null)
            {
                // Debug.Log($"ðŸ”„ Switching lobby tab to: {tabName}");
                lobbyController.SetActiveTab(tabName);
                
                // Update navigation button states
                weaponsButton?.RemoveFromClassList("nav-button-active");
                charactersButton?.RemoveFromClassList("nav-button-active");
                shopButton?.RemoveFromClassList("nav-button-active");
                lobbyButton?.RemoveFromClassList("logo-button-active");
                
                switch (tabName)
                {
                    case "lobby":
                        lobbyButton?.AddToClassList("logo-button-active");
                        break;
                    case "weapons":
                        weaponsButton?.AddToClassList("nav-button-active");
                        break;
                    case "agents":
                    case "characters":
                        charactersButton?.AddToClassList("nav-button-active");
                        break;
                    case "shop":
                        shopButton?.AddToClassList("nav-button-active");
                        break;
                }
            }
            else
            {
                // Debug.LogWarning("âš ï¸ LobbyUIController not found - tab switching unavailable");
            }
        }
    }
}
