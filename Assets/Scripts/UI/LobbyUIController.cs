using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using ArtisansGuns.Auth;
using ArtisansGuns.Networking;
using ArtisansGuns.Managers;
using ArtisansGuns.Data;

namespace ArtisansGuns.UI
{
    public class LobbyUIController : MonoBehaviour
    {
        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset playerCardTemplate;
        private StyleSheet playerCardStyleSheet;
        
        // Tab Controllers (optional, on same GameObject)
        private WeaponsTabController weaponsTabController;
        private AgentsTabController agentsTabController;
        private ShopTabController shopTabController;
        private HatsTabController hatsTabController;
        
        // Settings Panel Controller (unified across scenes)
        private SettingsUIController settingsUIController;

        // Main UI Elements
        private Button changeCharacterButton;
        private Button refreshButton;
        private Button createRoomButton;
        private Button quickPlayButton;

        // Tab Content Containers (managed by PersistentUI navigation)
        private VisualElement lobbyContent;
        private VisualElement weaponsContent;
        private VisualElement agentsContent;
        private VisualElement shopContent;
        private VisualElement hatsContent;
        private VisualElement roomContent; // ROOM TAB (when in a room)

        // Room UI Elements
        private VisualElement exitButtonContainer;
       private Button exitButton;
        private Button lobbyButton; // Logo button (becomes LEAVE when in room)
        private Button weaponsButton;
        private Button charactersButton;
        private Button shopButton;
        private Button hatsButton;
        private Label logoLabel;
        private Label playersCountLabel;
        private ScrollView teamAList;
        private ScrollView teamBList;
        private Button readyButton;
        private Button startGameButton;
        private VisualElement hostControls;
        private VisualElement waitingMessage;
        private VisualElement countdownOverlay;
        private Label countdownLabel;
        private Label mapHeaderLabel;
        private Label roomIdLabel;
        private Label gameModeLabel;
        private Label maxPlayersLabel;
        private VisualElement mapImage;
        
        // Room state
        private bool isInRoom = false;
        private bool isHost = false;
        private string currentRoomName = "";
        private string currentMapName = "";
        private List<PlayerInRoom> playersInRoom = new List<PlayerInRoom>();

        // 16:9 Aspect Ratio Letterbox
        private VisualElement aspectBackground;
        private VisualElement aspectMainContainer;

        // Character Display (from Backend Loadout)
        private Label playerNameLabel;
        private Label playerIdentLabel;
        private Label characterNameLabel;
        private Label usernameLabel;
        private Label levelLabel;
        private VisualElement xpBarFill;
        private Label xpLabel;
        private Label primaryWeaponLabel;
        private Label secondaryWeaponLabel;
        private VisualElement primaryWeaponIconHome;
        private VisualElement secondaryWeaponIconHome;
        private VisualElement knifeWeaponIconHome;
        private Label knifeWeaponLabelHome;
        private VisualElement characterPreview;

        // Room List
        private ScrollView roomList;

        // Overlays
        private VisualElement characterSelectOverlay;
        private VisualElement createRoomOverlay;
        private VisualElement loadingOverlay;
        private Label loadingMessage;
        private Label loadingSubtext;
        private VisualElement loadingSpinner;
        private Button retryConnectionButton;

        // Character Select Panel
        private Button closeCharSelectButton;
        private VisualElement charOptionCrimson;
        private VisualElement charOptionVibe;
        private VisualElement charOptionSight;
        private VisualElement charOptionPato;
        
        // Currency Display (in LobbyScreen header)
        private Label rivalEssenceLabel;
        private Label rivalPointsLabel;

        // Create Room Panel
        private Button closeCreateRoomButton;
        private TextField roomNameField;   // legacy, no longer in UXML
        private DropdownField mapNameField; // legacy, no longer in UXML
        private Button confirmCreateRoomButton;
        
        // Private Room - Join by Code
        private TextField roomCodeField;
        private Button joinPrivateRoomButton;
        
        // Gamemode
        private VisualElement gamemodeSelector;
        private Button gamemodePrevButton;
        private Button gamemodeNextButton;
        private Label gamemodeLabel;
        private string currentGamemode = "tdm"; // Only "tdm" (Team Deathmatch) for v1
        private static readonly string[] GAMEMODE_IDS = { "tdm" };
        private static readonly string[] GAMEMODE_NAMES = { "TEAM DEATHMATCH" };
        
        // Save Progress (Google Sign-In)
        private VisualElement googlePromoPanel;
        private Button googlePromoButton;
        private VisualElement saveProgressOverlay;
        private Button closeSaveProgressButton;
        private Label saveProgressError;
        private Button googleSignInButton;
        private Button loginInsteadButton;
        
        // Character Name Overlay (shown after Google Sign-In)
        private VisualElement characterNameOverlay;
        private Button closeCharNameButton;
        private TextField charNameField;
        private Label charNameError;
        private Button confirmCharNameButton;
        private string pendingGoogleIdToken; // stored after Google Sign-In, used when confirming char name
        
        // Google Link Reward Overlay
        private VisualElement googleRewardOverlay;
        private Label rewardAmountLabel;
        private Button claimRewardButton;

        // Login Overlay (Google)
        private VisualElement loginOverlay;
        private Button closeLoginButton;
        private Label loginError;
        private Button confirmLoginButton;

        // Settings data (character data comes from LoadoutManager)
        private float currentSensitivity = 1.0f;

        // Initial load tracking
        private bool initialLoadComplete = false;

        // Room data (real-time from Fusion)
        private List<SessionInfo> activeRooms = new List<SessionInfo>();
        private const int MAX_ROOMS = 10;
        private bool isUpdatingRoomList = false;

        private void Awake()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            
            // Get tab controllers if they exist on same GameObject
            weaponsTabController = GetComponent<WeaponsTabController>();
            if (weaponsTabController != null)
            {
                weaponsTabController.enabled = false; // Start disabled
            }
            
            agentsTabController = GetComponent<AgentsTabController>();
            if (agentsTabController != null)
            {
                agentsTabController.enabled = false; // Start disabled
            }
            
            shopTabController = GetComponent<ShopTabController>();
            if (shopTabController != null)
            {
                shopTabController.enabled = false; // Start disabled
            }
            
            hatsTabController = GetComponent<HatsTabController>();
            if (hatsTabController != null)
            {
                hatsTabController.enabled = false; // Start disabled
            }
        }

        private async void Start()
        {
            // Safety net: re-register click sounds in Start() in case SoundManager wasn't
            // ready during OnEnable (its Awake may run after ours on first scene load).
            if (uiDocument != null && uiDocument.rootVisualElement != null)
            {
                ArtisansGuns.Managers.SoundManager.Instance?.RegisterGlobalClickSounds(uiDocument.rootVisualElement);
            }

            // Initialize networking
            if (NetworkManager.Instance != null)
            {
                await NetworkManager.Instance.InitializeNetworking();
                
                // Defensive unsubscribe before subscribe to prevent stacking on scene reload
                NetworkManager.Instance.OnRoomListUpdated -= OnRoomListUpdated;
                NetworkManager.Instance.OnRoomCreated -= OnRoomCreatedSuccess;
                NetworkManager.Instance.OnJoinedRoom -= OnJoinRoomSuccess;
                NetworkManager.Instance.OnJoinRoomFailed -= OnJoinRoomFailed;
                NetworkManager.Instance.OnPlayerJoinedRoom -= OnPlayerJoinedRoom;
                NetworkManager.Instance.OnPlayerLeftRoom -= OnPlayerLeftRoom;
                NetworkManager.Instance.OnPlayerDataChanged -= OnPlayerDataChanged;

                // Subscribe to network events
                NetworkManager.Instance.OnRoomListUpdated += OnRoomListUpdated;
                NetworkManager.Instance.OnRoomCreated += OnRoomCreatedSuccess;
                NetworkManager.Instance.OnJoinedRoom += OnJoinRoomSuccess;
                NetworkManager.Instance.OnJoinRoomFailed += OnJoinRoomFailed;
                NetworkManager.Instance.OnPlayerJoinedRoom += OnPlayerJoinedRoom;
                NetworkManager.Instance.OnPlayerLeftRoom += OnPlayerLeftRoom;
                NetworkManager.Instance.OnPlayerDataChanged += OnPlayerDataChanged;
            }
            else
            {
                // Debug.LogError("âŒ NetworkManager not found!");
            }
            
            // Wait for LoadoutManager to be ready and subscribe
            await System.Threading.Tasks.Task.Yield(); // Wait one frame
            
            if (LoadoutManager.Instance != null)
            {
                // Debug.Log("âœ… [LobbyUI] LoadoutManager found in Start(), subscribing and updating currency");
                LoadoutManager.Instance.OnLoadoutUpdated += OnLoadoutUpdated;
                
                // Refresh display now that we're subscribed — LoadoutManager may already
                // have data from InitializeLoadoutFromAuth that fired before our subscription.
                UpdateCharacterDisplay();
                UpdateCurrencyDisplay();

                // If loadout already has data, dismiss the loading screen
                if (!initialLoadComplete && LoadoutManager.Instance.IsInitialized())
                {
                    initialLoadComplete = true;
                    HideLoading();
                }
            }
            else
            {
                // Debug.LogWarning("âš ï¸ [LobbyUI] LoadoutManager still not available in Start()");
            }
        
            // Subscribe to auth connection failures
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnConnectionFailed += HandleConnectionFailed;
            }
        }

        private void OnEnable()
        {
            // Load player card template from Resources if not assigned
            if (playerCardTemplate == null)
            {
                playerCardTemplate = Resources.Load<VisualTreeAsset>("UI/PlayerCard");
                if (playerCardTemplate != null)
                {
                    // Debug.Log("âœ… PlayerCard template loaded from Resources");
                }
                else
                {
                    // Debug.LogError("âŒ Failed to load PlayerCard template from Resources/UI/PlayerCard.uxml");
                }
            }

            // Load player card stylesheet from Resources
            if (playerCardStyleSheet == null)
            {
                playerCardStyleSheet = Resources.Load<StyleSheet>("UI/PlayerCard");
                if (playerCardStyleSheet != null)
                {
                    // Debug.Log("âœ… PlayerCard stylesheet loaded from Resources");
                }
                else
                {
                    // Debug.LogWarning("âš ï¸ Failed to load PlayerCard stylesheet from Resources/UI/PlayerCard.uss");
                }
            }
            
            var root = uiDocument.rootVisualElement;

            // 16:9 letterbox: root = black bars, Background = 16:9 frame,
            // MainContainer = fixed 1920x1080 scaled to fill the frame
            aspectBackground = root.Q<VisualElement>("Background");
            aspectMainContainer = root.Q<VisualElement>("MainContainer");
            root.style.backgroundColor = Color.black;
            root.RegisterCallback<GeometryChangedEvent>(_ => ApplyAspectRatio16x9(root));
            root.schedule.Execute(() => ApplyAspectRatio16x9(root));

            // Add PlayerCard stylesheet to root document so ALL elements can use its classes
            if (playerCardStyleSheet != null && !root.styleSheets.Contains(playerCardStyleSheet))
            {
                root.styleSheets.Add(playerCardStyleSheet);
                // Debug.Log("ðŸŽ¨ Added PlayerCard stylesheet to root document - styles now available globally");
            }

            // Main UI Elements (header buttons now handled by PersistentUIManager)
            changeCharacterButton = root.Q<Button>("ChangeCharacterButton");
            refreshButton = root.Q<Button>("RefreshButton");
            createRoomButton = root.Q<Button>("CreateRoomButton");
            quickPlayButton = root.Q<Button>("QuickPlayButton");

            // Cache tab content containers (navigation buttons handled by PersistentUIManager)
            lobbyContent = root.Q<VisualElement>("LobbyContent");
            weaponsContent = root.Q<VisualElement>("WeaponsContent");
            agentsContent = root.Q<VisualElement>("AgentsContent");
            shopContent = root.Q<VisualElement>("ShopContent");
            roomContent = root.Q<VisualElement>("RoomContent");

            // Cache header elements
            exitButtonContainer = root.Q<VisualElement>("ExitButtonContainer");
            exitButton = root.Q<Button>("ExitButton");
            lobbyButton = root.Q<Button>("LobbyButton");
            weaponsButton = root.Q<Button>("WeaponsButton");
            charactersButton = root.Q<Button>("CharactersButton");
            shopButton = root.Q<Button>("ShopButton");
            hatsButton = root.Q<Button>("HatsButton");
            hatsContent = root.Q<VisualElement>("HatsContent");
            logoLabel = root.Q<Label>("LogoLabel");
            playersCountLabel = root.Q<Label>("PlayersCountLabel");
            
            // Cache currency labels
            rivalEssenceLabel = root.Q<Label>("RivalEssenceLabel");
            rivalPointsLabel = root.Q<Label>("RivalPointsLabel");
            
            // Cache room elements
            teamAList = root.Q<ScrollView>("TeamAList");
            teamBList = root.Q<ScrollView>("TeamBList");
            
            // Configure team lists for horizontal layout (5 cards per row)
            if (teamAList != null)
            {
                teamAList.contentContainer.style.flexDirection = FlexDirection.Row;
                teamAList.contentContainer.style.flexWrap = Wrap.NoWrap;
                teamAList.contentContainer.style.justifyContent = Justify.FlexStart;
                teamAList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                teamAList.verticalScrollerVisibility   = ScrollerVisibility.Hidden;
            }
            
            if (teamBList != null)
            {
                teamBList.contentContainer.style.flexDirection = FlexDirection.Row;
                teamBList.contentContainer.style.flexWrap = Wrap.NoWrap;
                teamBList.contentContainer.style.justifyContent = Justify.FlexStart;
                teamBList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                teamBList.verticalScrollerVisibility   = ScrollerVisibility.Hidden;
            }

            // Apply team panel colors inline (bypass USS caching issues)
            Color orangeTeam = new Color(1f, 0.373f, 0.196f, 1f);   // rgb(255,95,50)
            Color cyanTeam   = new Color(0f, 0.765f, 0.941f, 1f);   // rgb(0,195,240)

            var teamAPanel = root.Q<VisualElement>("TeamAPanel");
            if (teamAPanel != null)
            {
                teamAPanel.style.borderTopColor  = orangeTeam;
                teamAPanel.style.borderLeftColor = orangeTeam;
                teamAPanel.style.borderTopWidth  = 4;
                teamAPanel.style.borderLeftWidth = 4;
                var titleA = teamAPanel.Q<Label>(className: "panel-title");
                if (titleA != null)
                {
                    titleA.style.color           = orangeTeam;
                    titleA.style.backgroundColor = new Color(1f, 0.373f, 0.196f, 0.20f);
                }
            }

            var teamBPanel = root.Q<VisualElement>("TeamBPanel");
            if (teamBPanel != null)
            {
                teamBPanel.style.borderTopColor  = cyanTeam;
                teamBPanel.style.borderLeftColor = cyanTeam;
                teamBPanel.style.borderTopWidth  = 4;
                teamBPanel.style.borderLeftWidth = 4;
                var titleB = teamBPanel.Q<Label>(className: "panel-title");
                if (titleB != null)
                {
                    titleB.style.color           = cyanTeam;
                    titleB.style.backgroundColor = new Color(0f, 0.765f, 0.941f, 0.20f);
                }
            }
            
            readyButton = root.Q<Button>("ReadyButton");
            startGameButton = root.Q<Button>("StartGameButton");
            hostControls = root.Q<VisualElement>("HostControls");
            waitingMessage = root.Q<VisualElement>("WaitingMessage");
            countdownOverlay = root.Q<VisualElement>("CountdownOverlay");
            countdownLabel = root.Q<Label>("CountdownLabel");
            mapHeaderLabel = root.Q<Label>("MapHeaderLabel");
            roomIdLabel = root.Q<Label>("RoomIdLabel");
            gameModeLabel = root.Q<Label>("GameModeLabel");
            maxPlayersLabel = root.Q<Label>("MaxPlayersLabel");
            mapImage = root.Q<VisualElement>("MapImage");

            characterNameLabel = root.Q<Label>("CharacterNameLabel");
            usernameLabel = root.Q<Label>("UsernameLabel");
            playerNameLabel = root.Q<Label>("PlayerNameLabel");
            playerIdentLabel = root.Q<Label>("PlayerIdentLabel");
            levelLabel = root.Q<Label>("LevelLabel");
            xpBarFill = root.Q<VisualElement>("XPBarFill");
            xpLabel = root.Q<Label>("XPLabel");
            primaryWeaponLabel = root.Q<Label>("PrimaryWeaponLabel");
            secondaryWeaponLabel = root.Q<Label>("SecondaryWeaponLabel");
            primaryWeaponIconHome = root.Q<VisualElement>("PrimaryWeaponSlot")?.Q<VisualElement>(className: "equipped-weapon-icon");
            secondaryWeaponIconHome = root.Q<VisualElement>("SecondaryWeaponSlot")?.Q<VisualElement>(className: "equipped-weapon-icon");
            knifeWeaponIconHome = root.Q<VisualElement>("KnifeWeaponSlot")?.Q<VisualElement>(className: "equipped-weapon-icon");
            knifeWeaponLabelHome = root.Q<Label>("KnifeWeaponLabel");
            characterPreview = root.Q<VisualElement>("CharacterPreview");
            roomList = root.Q<ScrollView>("RoomList");

            // Global click sounds — register on root so every Button plays a tap SFX.
            // Also done again in Start() as a safety net in case SoundManager wasn't ready yet.
            ArtisansGuns.Managers.SoundManager.Instance?.RegisterGlobalClickSounds(root);

            // Cache overlays
            characterSelectOverlay = root.Q<VisualElement>("CharacterSelectOverlay");
            createRoomOverlay = root.Q<VisualElement>("CreateRoomOverlay");
            loadingOverlay = root.Q<VisualElement>("LoadingOverlay");
            loadingMessage = root.Q<Label>("LoadingMessage");
            loadingSubtext = root.Q<Label>("LoadingSubtext");
            loadingSpinner = root.Q<VisualElement>("LoadingSpinner");
            retryConnectionButton = root.Q<Button>("RetryConnectionButton");
            if (retryConnectionButton != null)
            {
                retryConnectionButton.clicked += OnRetryConnectionClicked;
                retryConnectionButton.AddToClassList("hidden");
            }

            // Show loading overlay while auth + loadout initialize
            if (!initialLoadComplete)
            {
                ShowLoading("CONNECTING...", "INITIALIZING SESSION");
            }

            // Settings panel elements - will be handled by SettingsUIController
            // (no caching needed - SettingsUIController finds them by name)
            
            // Initialize Settings Panel with unified SettingsUIController
            settingsUIController = GetComponent<SettingsUIController>();
            if (settingsUIController == null)
            {
                settingsUIController = gameObject.AddComponent<SettingsUIController>();
            }
            settingsUIController.FindSettingsPanelElements(root);
            
            // Subscribe to settings panel close event
            settingsUIController.OnSettingsPanelClosed += OnSettingsPanelClosed;
            settingsUIController.OnLogoutPerformed += OnLogoutPerformed;

            // Character select elements
            closeCharSelectButton = root.Q<Button>("CloseCharSelectButton");
            charOptionCrimson = root.Q<VisualElement>("CharOption_CRIMSON");
            charOptionVibe = root.Q<VisualElement>("CharOption_VIBE");
            charOptionSight = root.Q<VisualElement>("CharOption_SIGHT");
            charOptionPato = root.Q<VisualElement>("CharOption_PATO");

            // Private room elements
            closeCreateRoomButton = root.Q<Button>("CloseCreateRoomButton");
            confirmCreateRoomButton = root.Q<Button>("ConfirmCreateRoomButton");
            roomCodeField = root.Q<TextField>("RoomCodeField");
            joinPrivateRoomButton = root.Q<Button>("JoinPrivateRoomButton");
            
            // Gamemode elements
            gamemodeSelector = root.Q<VisualElement>("GamemodeSelector");
            gamemodePrevButton = root.Q<Button>("GamemodePrevButton");
            gamemodeNextButton = root.Q<Button>("GamemodeNextButton");
            gamemodeLabel = root.Q<Label>("GamemodeLabel");
            
            // Save Progress (Google Sign-In) elements
            googlePromoPanel = root.Q<VisualElement>("GooglePromoPanel");
            googlePromoButton = root.Q<Button>("GooglePromoButton");
            saveProgressOverlay = root.Q<VisualElement>("SaveProgressOverlay");
            closeSaveProgressButton = root.Q<Button>("CloseSaveProgressButton");
            saveProgressError = root.Q<Label>("SaveProgressError");
            googleSignInButton = root.Q<Button>("GoogleSignInButton");
            loginInsteadButton = root.Q<Button>("LoginInsteadButton");
            
            // Character Name Overlay elements
            characterNameOverlay = root.Q<VisualElement>("CharacterNameOverlay");
            closeCharNameButton = root.Q<Button>("CloseCharNameButton");
            charNameField = root.Q<TextField>("CharNameField");
            charNameError = root.Q<Label>("CharNameError");
            confirmCharNameButton = root.Q<Button>("ConfirmCharNameButton");
            
            // Google Link Reward elements
            googleRewardOverlay = root.Q<VisualElement>("GoogleRewardOverlay");
            rewardAmountLabel = root.Q<Label>("RewardAmountLabel");
            claimRewardButton = root.Q<Button>("ClaimRewardButton");

            // Login Overlay elements (Google)
            loginOverlay = root.Q<VisualElement>("LoginOverlay");
            closeLoginButton = root.Q<Button>("CloseLoginButton");
            loginError = root.Q<Label>("LoginError");
            confirmLoginButton = root.Q<Button>("ConfirmLoginButton");
            
            // Subscribe to auth events EARLY (before Start) so we don't miss OnGuestReady
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnGuestReady += HandleGuestReadyInLobby;
                AuthManager.Instance.OnLoginSuccess += HandleLoginSuccessInLobby;
            }

            // Show/hide save progress button based on auth mode.
            UpdateSaveProgressButtonVisibility();
            // Safety net: re-check after one frame in case UIToolkit layout wasn't ready yet
            root.schedule.Execute(() => UpdateSaveProgressButtonVisibility());

            // Register button events
            RegisterEvents();

            // Load saved data
            LoadPlayerData();

            // Initialize UI
            UpdateCharacterDisplay();
            // UpdateCurrencyDisplay(); // Removed - will be called in Start() when LoadoutManager is ready
            UpdateCharacterSelection(); // Update selection visual state
            RefreshRoomList();
            
            // Set initial header state (lobby tab active on launch)
            UpdateHeaderButtonStates("lobby");
        }

        private void OnDisable()
        {
            UnregisterEvents();
            
            // Unsubscribe from settings events
            if (settingsUIController != null)
            {
                settingsUIController.OnSettingsPanelClosed -= OnSettingsPanelClosed;
                settingsUIController.OnLogoutPerformed -= OnLogoutPerformed;
            }
            
            // Unsubscribe from network events
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.OnRoomListUpdated -= OnRoomListUpdated;
                NetworkManager.Instance.OnRoomCreated -= OnRoomCreatedSuccess;
                NetworkManager.Instance.OnJoinedRoom -= OnJoinRoomSuccess;
                NetworkManager.Instance.OnJoinRoomFailed -= OnJoinRoomFailed;
                NetworkManager.Instance.OnPlayerJoinedRoom -= OnPlayerJoinedRoom;
                NetworkManager.Instance.OnPlayerLeftRoom -= OnPlayerLeftRoom;
                NetworkManager.Instance.OnPlayerDataChanged -= OnPlayerDataChanged;
            }
            
            // Unsubscribe from loadout events
            if (LoadoutManager.Instance != null)
            {
                LoadoutManager.Instance.OnLoadoutUpdated -= OnLoadoutUpdated;
            }

            // Unsubscribe from auth events
            if (AuthManager.Instance != null)
            {
                AuthManager.Instance.OnConnectionFailed -= HandleConnectionFailed;
                AuthManager.Instance.OnGuestReady -= HandleGuestReadyInLobby;
                AuthManager.Instance.OnLoginSuccess -= HandleLoginSuccessInLobby;
            }
            if (retryConnectionButton != null) retryConnectionButton.clicked -= OnRetryConnectionClicked;
        }

        private void RegisterEvents()
        {
            // Main buttons (header navigation now handled by PersistentUIManager)
            changeCharacterButton?.RegisterCallback<ClickEvent>(evt => ShowCharacterSelect());
            refreshButton?.RegisterCallback<ClickEvent>(evt => ToggleRoomList());
            createRoomButton?.RegisterCallback<ClickEvent>(evt => ShowCreateRoom());

            // Room panel close
            var closeRoomPanelButton = uiDocument.rootVisualElement.Q<Button>("CloseRoomPanelButton");
            closeRoomPanelButton?.RegisterCallback<ClickEvent>(evt => {
                var roomOverlay = uiDocument.rootVisualElement.Q<VisualElement>("RoomPanelOverlay");
                roomOverlay?.AddToClassList("hidden");
            });
            quickPlayButton?.RegisterCallback<ClickEvent>(evt => OnQuickPlayClicked());

            // Header elements
            lobbyButton?.RegisterCallback<ClickEvent>(evt => OnLobbyButtonClicked());
            exitButton?.RegisterCallback<ClickEvent>(evt => ExitRoomMode());
            weaponsButton?.RegisterCallback<ClickEvent>(evt => SetActiveTab("weapons"));
            charactersButton?.RegisterCallback<ClickEvent>(evt => SetActiveTab("agents"));
            shopButton?.RegisterCallback<ClickEvent>(evt => SetActiveTab("shop"));
            hatsButton?.RegisterCallback<ClickEvent>(evt => SetActiveTab("hats"));

            // Room elements
            readyButton?.RegisterCallback<ClickEvent>(evt => OnReadyButtonClicked());
            startGameButton?.RegisterCallback<ClickEvent>(evt => OnStartGameClicked());

            // Settings panel - now handled by SettingsUIController
            // The closeSettingsButton, sensitivitySlider, and logoutButton are wired by SettingsUIController
            
            // Check if there's a SettingsButton in the header
            var settingsButton = uiDocument.rootVisualElement.Q<Button>("SettingsButton");
            settingsButton?.RegisterCallback<ClickEvent>(evt => 
            {
                if (settingsUIController != null)
                {
                    settingsUIController.ShowSettings();
                }
            });

            // Character select
            closeCharSelectButton?.RegisterCallback<ClickEvent>(evt => HideCharacterSelect());
            charOptionCrimson?.RegisterCallback<ClickEvent>(evt => SelectCharacter("CRIMSON"));
            charOptionVibe?.RegisterCallback<ClickEvent>(evt => SelectCharacter("VIBE"));
            charOptionSight?.RegisterCallback<ClickEvent>(evt => SelectCharacter("SIGHT"));
            charOptionPato?.RegisterCallback<ClickEvent>(evt => SelectCharacter("PATO"));

            // Private room overlay
            closeCreateRoomButton?.RegisterCallback<ClickEvent>(evt => HideCreateRoom());
            confirmCreateRoomButton?.RegisterCallback<ClickEvent>(evt => OnCreateRoomConfirmed());
            joinPrivateRoomButton?.RegisterCallback<ClickEvent>(evt => OnJoinPrivateRoomClicked());
            
            // Save Progress / Login overlays (Google Sign-In)
            googlePromoButton?.RegisterCallback<ClickEvent>(evt => ShowSaveProgressOverlay());
            closeSaveProgressButton?.RegisterCallback<ClickEvent>(evt => HideSaveProgressOverlay());
            googleSignInButton?.RegisterCallback<ClickEvent>(evt => OnGoogleSignInForLink());
            loginInsteadButton?.RegisterCallback<ClickEvent>(evt => ShowLoginOverlay());
            closeCharNameButton?.RegisterCallback<ClickEvent>(evt => HideCharacterNameOverlay());
            confirmCharNameButton?.RegisterCallback<ClickEvent>(evt => OnConfirmCharacterName());
            claimRewardButton?.RegisterCallback<ClickEvent>(evt => HideGoogleRewardOverlay());
            closeLoginButton?.RegisterCallback<ClickEvent>(evt => HideLoginOverlay());
            confirmLoginButton?.RegisterCallback<ClickEvent>(evt => OnGoogleSignInForLogin());
        }

        private void UnregisterEvents()
        {
            // Main buttons
            changeCharacterButton?.UnregisterCallback<ClickEvent>(evt => ShowCharacterSelect());
            refreshButton?.UnregisterCallback<ClickEvent>(evt => ToggleRoomList());
            createRoomButton?.UnregisterCallback<ClickEvent>(evt => ShowCreateRoom());
            quickPlayButton?.UnregisterCallback<ClickEvent>(evt => OnQuickPlayClicked());

            // Header elements
            lobbyButton?.UnregisterCallback<ClickEvent>(evt => OnLobbyButtonClicked());
            exitButton?.UnregisterCallback<ClickEvent>(evt => ExitRoomMode());
            weaponsButton?.UnregisterCallback<ClickEvent>(evt => SetActiveTab("weapons"));
            charactersButton?.UnregisterCallback<ClickEvent>(evt => SetActiveTab("agents"));

            // Room elements
            readyButton?.UnregisterCallback<ClickEvent>(evt => OnReadyButtonClicked());
            startGameButton?.UnregisterCallback<ClickEvent>(evt => OnStartGameClicked());

            // Settings panel - unregistration handled by SettingsUIController's OnDisable()
            // No need to unregister closeSettingsButton, sensitivitySlider, or logoutButton here

            closeCharSelectButton?.UnregisterCallback<ClickEvent>(evt => HideCharacterSelect());
            charOptionCrimson?.UnregisterCallback<ClickEvent>(evt => SelectCharacter("CRIMSON"));
            charOptionVibe?.UnregisterCallback<ClickEvent>(evt => SelectCharacter("VIBE"));
            charOptionSight?.UnregisterCallback<ClickEvent>(evt => SelectCharacter("SIGHT"));
            charOptionPato?.UnregisterCallback<ClickEvent>(evt => SelectCharacter("PATO"));

            closeCreateRoomButton?.UnregisterCallback<ClickEvent>(evt => HideCreateRoom());
            confirmCreateRoomButton?.UnregisterCallback<ClickEvent>(evt => OnCreateRoomConfirmed());
            joinPrivateRoomButton?.UnregisterCallback<ClickEvent>(evt => OnJoinPrivateRoomClicked());
            
            googlePromoButton?.UnregisterCallback<ClickEvent>(evt => ShowSaveProgressOverlay());
            closeSaveProgressButton?.UnregisterCallback<ClickEvent>(evt => HideSaveProgressOverlay());
            googleSignInButton?.UnregisterCallback<ClickEvent>(evt => OnGoogleSignInForLink());
            loginInsteadButton?.UnregisterCallback<ClickEvent>(evt => ShowLoginOverlay());
            closeCharNameButton?.UnregisterCallback<ClickEvent>(evt => HideCharacterNameOverlay());
            confirmCharNameButton?.UnregisterCallback<ClickEvent>(evt => OnConfirmCharacterName());
            claimRewardButton?.UnregisterCallback<ClickEvent>(evt => HideGoogleRewardOverlay());
            closeLoginButton?.UnregisterCallback<ClickEvent>(evt => HideLoginOverlay());
            confirmLoginButton?.UnregisterCallback<ClickEvent>(evt => OnGoogleSignInForLogin());
        }

        // ===================================
        // Settings Panel (handled by SettingsUIController)
        // ===================================
        
        /// <summary>
        /// Called when SettingsUIController closes the settings panel
        /// </summary>
        private void OnSettingsPanelClosed()
        {
            // Save player data when settings panel closes
            SavePlayerData();
        }

        private void OnLogoutPerformed()
        {
            // Immediately refresh UI to guest state (don't wait for OnGuestReady coroutine)
            UpdateSaveProgressButtonVisibility();
            settingsUIController?.UpdateLogoutButtonVisibility();
            UpdateCharacterDisplay();
        }

        // ===================================
        // Character Selection
        // ===================================
        private void ShowCharacterSelect()
        {
            characterSelectOverlay?.RemoveFromClassList("hidden");
            UpdateCharacterSelection();
        }

        private void HideCharacterSelect()
        {
            characterSelectOverlay?.AddToClassList("hidden");
        }

        private void SelectCharacter(string characterName)
        {
            ArtisansGuns.Managers.SoundManager.Instance?.PlaySelect();
            // Debug.Log($"ðŸŽ­ Selecting character: {characterName}");
            
            if (LoadoutManager.Instance != null)
            {
                // Update backend via LoadoutManager
                LoadoutManager.Instance.UpdateCharacter(characterName, (success) =>
                {
                    if (success)
                    {
                        // Debug.Log($"âœ… Character changed to {characterName}");
                        UpdateCharacterSelection();
                        HideCharacterSelect();
                    }
                    else
                    {
                        // Debug.LogError($"âŒ Failed to change character to {characterName}");
                        // TODO: Show error message to player
                    }
                });
            }
            else
            {
                // Debug.LogError("âŒ LoadoutManager not available!");
            }
        }

        private void UpdateCharacterDisplay()
        {
            // Update player ident with the characterName from registration
            // This doesn't depend on LoadoutManager, so do it first
            if (playerIdentLabel != null)
            {
                string identText = "IDENT // CHARACTER";
                
                // Try AuthManager first
                if (AuthManager.Instance != null)
                {
                    var user = AuthManager.Instance.GetCurrentUser();
                    // Debug.Log($"ðŸ” DEBUG - AuthManager.GetCurrentUser() = {(user != null ? "not null" : "NULL")}");
                    if (user != null)
                    {
                        // Debug.Log($"ðŸ” DEBUG - user.characterName = '{user.characterName}'");
                        if (!string.IsNullOrEmpty(user.characterName))
                        {
                            identText = user.characterName.ToUpper();
                        }
                    }
                }
                
                // Fallback to PlayerPrefs
                if (identText == "IDENT // CHARACTER" && PlayerPrefs.HasKey("user_character_name"))
                {
                    string charName = PlayerPrefs.GetString("user_character_name", "");
                    // Debug.Log($"ðŸ” DEBUG - PlayerPrefs user_character_name = '{charName}'");
                    if (!string.IsNullOrEmpty(charName))
                    {
                        identText = charName.ToUpper();
                    }
                }
                
                playerIdentLabel.text = identText;
                // Debug.Log($"âœ… PlayerIdentLabel set to: '{identText}'");

                // Also show the player's name above the XP bar
                if (playerNameLabel != null && identText != "IDENT // CHARACTER")
                    playerNameLabel.text = identText;
            }
            
            if (LoadoutManager.Instance != null && LoadoutManager.Instance.IsInitialized())
            {
                var loadout = LoadoutManager.Instance.GetLoadout();
                
                // Re-cache weapon icon elements if they were lost (e.g. scene reload via Fusion)
                if (knifeWeaponIconHome == null || knifeWeaponLabelHome == null)
                {
                    var root = uiDocument?.rootVisualElement;
                    if (root != null)
                    {
                        if (knifeWeaponIconHome == null)
                            knifeWeaponIconHome = root.Q<VisualElement>("KnifeWeaponSlot")?.Q<VisualElement>(className: "equipped-weapon-icon");
                        if (knifeWeaponLabelHome == null)
                            knifeWeaponLabelHome = root.Q<Label>("KnifeWeaponLabel");
                        if (primaryWeaponIconHome == null)
                            primaryWeaponIconHome = root.Q<VisualElement>("PrimaryWeaponSlot")?.Q<VisualElement>(className: "equipped-weapon-icon");
                        if (secondaryWeaponIconHome == null)
                            secondaryWeaponIconHome = root.Q<VisualElement>("SecondaryWeaponSlot")?.Q<VisualElement>(className: "equipped-weapon-icon");
                    }
                }
                
                // Update all labels with backend data
                if (characterNameLabel != null)
                    characterNameLabel.text = loadout.selectedCharacter?.ToUpper() ?? "CRIMSON";
                
                if (usernameLabel != null)
                    usernameLabel.text = loadout.selectedCharacter?.ToUpper() ?? "CRIMSON";
                
                if (levelLabel != null)
                    levelLabel.text = $"{loadout.level}";
                
                UpdateXPBar(loadout.xp, loadout.level);
                
                if (primaryWeaponLabel != null)
                {
                    string primaryId = loadout.primaryWeapon?.weaponId ?? "talon_ar";
                    string primarySkinId = loadout.primaryWeapon?.skinId ?? "default";
                    primaryWeaponLabel.text = GetWeaponDisplayName(primaryId);
                    UpdateHomeWeaponIcon(primaryWeaponIconHome, primaryId, primarySkinId);
                }
                
                if (secondaryWeaponLabel != null)
                {
                    string secondaryId = loadout.secondaryWeapon?.weaponId ?? "bolt";
                    string secondarySkinId = loadout.secondaryWeapon?.skinId ?? "default";
                    secondaryWeaponLabel.text = GetWeaponDisplayName(secondaryId);
                    UpdateHomeWeaponIcon(secondaryWeaponIconHome, secondaryId, secondarySkinId);
                }
                
                // Update knife icon
                {
                    string knifeSkinId = loadout.knifeSkin?.skinId;
                    if (string.IsNullOrEmpty(knifeSkinId)) knifeSkinId = "default";
                    if (knifeWeaponLabelHome != null)
                        knifeWeaponLabelHome.text = knifeSkinId.ToUpper();
                    UpdateHomeKnifeIcon(knifeWeaponIconHome, knifeSkinId);
                }
                
                // Update character icon in lobby PlayerCard
                UpdateCharacterIcon(loadout.selectedCharacter);
                
                // Debug.Log($"âœ… Character display updated from backend: {loadout.selectedCharacter}");
            }
            else
            {
                // Debug.LogWarning("âš ï¸ LoadoutManager not initialized yet - using defaults (but NOT overwriting playerIdentLabel)");
                
                // Fallback to default values (playerIdentLabel already set from AuthManager above)
                if (characterNameLabel != null)
                    characterNameLabel.text = "CRIMSON";
                if (usernameLabel != null)
                    usernameLabel.text = "CRIMSON";
                if (levelLabel != null)
                    levelLabel.text = "1";
                UpdateXPBar(0, 1);
                if (primaryWeaponLabel != null)
                {
                    primaryWeaponLabel.text = "TALON-AR";
                    UpdateHomeWeaponIcon(primaryWeaponIconHome, "talon_ar", "default");
                }
                if (secondaryWeaponLabel != null)
                {
                    secondaryWeaponLabel.text = "BOLT";
                    UpdateHomeWeaponIcon(secondaryWeaponIconHome, "bolt", "default");
                }
                // Knife fallback
                if (knifeWeaponLabelHome != null)
                    knifeWeaponLabelHome.text = "DEFAULT";
                UpdateHomeKnifeIcon(knifeWeaponIconHome, "default");
                    
                // Update character icon with default
                UpdateCharacterIcon("crimson");
            }
        }
        
        /// <summary>
        /// Update the character icon in the lobby PlayerCard - same as agent cards but bigger
        /// </summary>
        // =====================================================
        //  ASPECT RATIO 16:9 - UI Toolkit letterbox + fixed scale
        //
        //  Strategy:
        //    - root          = full screen, black background (letterbox bars)
        //    - Background    = absolute, sized to 16:9 box frame
        //    - MainContainer = ALWAYS laid out at 1920x1080 (reference res),
        //                      then scaled uniformly to fill the 16:9 box.
        //  Result: content size is ALWAYS consistent regardless of device.
        // =====================================================
        private const float REF_W = 1920f;
        private const float REF_H = 1080f;

        private void ApplyAspectRatio16x9(VisualElement panel)
        {
            if (aspectBackground == null || aspectMainContainer == null) return;

            float screenW = panel.resolvedStyle.width;
            float screenH = panel.resolvedStyle.height;
            if (screenW <= 1 || screenH <= 1) return; // not yet laid out

            const float targetAspect = REF_W / REF_H; // 16/9
            float current = screenW / screenH;

            float viewW, viewH, left, top;
            if (current > targetAspect)
            {
                // Wider than 16:9 → pillarbox (black bars on sides)
                viewH = screenH;
                viewW = Mathf.Round(screenH * targetAspect);
                left  = Mathf.Round((screenW - viewW) / 2f);
                top   = 0f;
            }
            else
            {
                // Taller than 16:9 → letterbox (black bars top/bottom)
                viewW = screenW;
                viewH = Mathf.Round(screenW / targetAspect);
                left  = 0f;
                top   = Mathf.Round((screenH - viewH) / 2f);
            }

            // Background = the visible 16:9 frame (clips overflow)
            aspectBackground.style.position = Position.Absolute;
            aspectBackground.style.left     = left;
            aspectBackground.style.top      = top;
            aspectBackground.style.width    = viewW;
            aspectBackground.style.height   = viewH;
            aspectBackground.style.overflow = Overflow.Hidden;

            // MainContainer always laid out at reference resolution 1920x1080
            // Scale uniformly so it fills the 16:9 box exactly
            float scale = viewW / REF_W;  // == viewH / REF_H for 16:9 box
            aspectMainContainer.style.position        = Position.Absolute;
            aspectMainContainer.style.left            = 0;
            aspectMainContainer.style.top             = 0;
            aspectMainContainer.style.width           = REF_W;
            aspectMainContainer.style.height          = REF_H;
            aspectMainContainer.style.transformOrigin = new TransformOrigin(0, 0, 0);
            aspectMainContainer.style.scale           = new Scale(new Vector3(scale, scale, 1f));
        }

        private void UpdateCharacterIcon(string selectedCharacter)
        {
            if (characterPreview == null) return;
            
            // Clear existing content
            characterPreview.Clear();
            
            // Get agent data
            var agent = AgentDefinition.GetAgentById(selectedCharacter?.ToLower() ?? "crimson");
            if (agent != null)
            {
                // Debug.Log($"ðŸ” [LobbyUI] Updating character icon for: {agent.displayName}, iconPath: {agent.iconPath}");
                var iconTexture = Resources.Load<Texture2D>(agent.iconPath);
                
                if (iconTexture != null)
                {
                    // Set the icon directly on the characterPreview element (card center)
                    characterPreview.style.backgroundImage = new StyleBackground(iconTexture);
                    characterPreview.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                    characterPreview.style.position = Position.Absolute;
                    characterPreview.style.top = 0;
                    characterPreview.style.left = 0;
                    characterPreview.style.width = new StyleLength(new Length(100, LengthUnit.Percent));
                    characterPreview.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
                }
            }
        }

        /// <summary>
        /// Called when LoadoutManager updates (after backend changes)
        /// </summary>
        private void OnLoadoutUpdated(LoadoutManager.LoadoutData loadout)
        {
            // Debug.Log("ðŸ”„ Loadout updated - Refreshing character display and currency");
            UpdateCharacterDisplay();
            UpdateCurrencyDisplay();

            // Hide initial loading screen once loadout data arrives
            if (!initialLoadComplete)
            {
                initialLoadComplete = true;
                HideLoading();
            }
        }
        
        /// <summary>
        /// Update currency display in LobbyScreen header
        /// </summary>
        private void UpdateCurrencyDisplay()
        {
            // Debug.Log($"ðŸ”„ [LobbyUI] UpdateCurrencyDisplay called - rivalEssenceLabel: {(rivalEssenceLabel != null ? "âœ…" : "âŒ")}, rivalPointsLabel: {(rivalPointsLabel != null ? "âœ…" : "âŒ")}");
            
            var loadout = LoadoutManager.Instance?.GetLoadout();
            
            if (loadout != null)
            {
                // Debug.Log($"ðŸ’° [LobbyUI] Loadout found - Blue Points: {loadout.bluePoints}, Rival Coins: {loadout.rivalCoins}");
                
                if (rivalEssenceLabel != null)
                {
                    string formattedBluePoints = loadout.bluePoints.ToString("#,##0");
                    rivalEssenceLabel.text = formattedBluePoints;
                    // Debug.Log($"âœ… [LobbyUI] Set rivalEssenceLabel to: '{formattedBluePoints}'");
                }
                else
                {
                    // Debug.LogError("âŒ [LobbyUI] rivalEssenceLabel is NULL!");
                }

                if (rivalPointsLabel != null)
                {
                    string formattedRivalCoins = loadout.rivalCoins.ToString("#,##0");
                    rivalPointsLabel.text = formattedRivalCoins;
                    // Debug.Log($"âœ… [LobbyUI] Set rivalPointsLabel to: '{formattedRivalCoins}'");
                }
                else
                {
                    // Debug.LogError("âŒ [LobbyUI] rivalPointsLabel is NULL!");
                }
            }
            else
            {
                // Debug.LogWarning("âš ï¸ [LobbyUI] LoadoutManager.Instance or loadout is null");
            }
        }
        
        /// <summary>
        /// Convert weapon ID to display name
        /// </summary>
        private void UpdateHomeKnifeIcon(VisualElement iconElement, string skinId)
        {
            if (iconElement == null) return;
            
            Texture2D tex = null;
            
            // Try skin-specific icon first
            var skin = KnifeSkinDefinition.GetKnifeSkinById(skinId);
            if (skin != null)
                tex = Resources.Load<Texture2D>(skin.iconPath);
            
            // Fallback: default knife skin icon
            if (tex == null)
            {
                var defaultSkin = KnifeSkinDefinition.GetDefaultKnifeSkin();
                if (defaultSkin != null)
                    tex = Resources.Load<Texture2D>(defaultSkin.iconPath);
            }
            
            // Fallback: known knife icon
            if (tex == null)
                tex = Resources.Load<Texture2D>("Icons/Knives/DefaultKnife");
            
            if (tex != null)
            {
                iconElement.style.backgroundImage = new StyleBackground(tex);
                iconElement.style.unityBackgroundImageTintColor = Color.white;
            }
        }

        private void UpdateHomeWeaponIcon(VisualElement iconElement, string weaponId, string skinId = "default")
        {
            if (iconElement == null) return;
            
            // Try skin icon first
            if (skinId != "default")
            {
                var skin = WeaponSkinDefinition.GetSkin(weaponId, skinId);
                if (skin != null)
                {
                    var skinTex = Resources.Load<Texture2D>(skin.iconPath);
                    if (skinTex != null)
                    {
                        iconElement.style.backgroundImage = new StyleBackground(skinTex);
                        iconElement.style.unityBackgroundImageTintColor = Color.white;
                        return;
                    }
                }
            }
            
            // Fallback to default weapon icon
            var weapon = WeaponDefinition.GetWeaponById(weaponId);
            if (weapon == null) return;
            var tex = Resources.Load<Texture2D>(weapon.iconPath);
            if (tex != null)
            {
                iconElement.style.backgroundImage = new StyleBackground(tex);
                iconElement.style.unityBackgroundImageTintColor = Color.white;
            }
        }

        private string GetWeaponDisplayName(string weaponId)
        {
            return weaponId switch
            {
                "talon_ar" => "TALON-AR",
                "bolt" => "BOLT",
                "rifle_phantom" => "PHANTOM",
                "rifle_vandal" => "VANDAL",
                "shotgun_bucky" => "BUCKY",
                "smg_stinger" => "STINGER",
                "pistol_ghost" => "GHOST",
                _ => weaponId.ToUpper()
            };
        }
        
        /// <summary>
        /// XP curve: per-level cost N→N+1 = N*(100+2N).
        /// Cumulative: totalXpForLevel(L) = 50*L*(L-1) + L*(L-1)*(2L-1)/3.
        /// </summary>
        private static int TotalXpForLevel(int level)
        {
            int L = level;
            return Mathf.RoundToInt(50f * L * (L - 1) + L * (L - 1) * (2f * L - 1) / 3f);
        }
        
        private void UpdateXPBar(int totalXp, int level)
        {
            int currentThreshold = TotalXpForLevel(level);
            int nextThreshold = TotalXpForLevel(level + 1);
            int xpInLevel = totalXp - currentThreshold;
            int xpNeeded = nextThreshold - currentThreshold;
            float pct = xpNeeded > 0 ? Mathf.Clamp01((float)xpInLevel / xpNeeded) : 0f;
            
            if (xpBarFill != null)
                xpBarFill.style.width = new StyleLength(new Length(pct * 100f, LengthUnit.Percent));
            
            if (xpLabel != null)
                xpLabel.text = $"{xpInLevel}/{xpNeeded} XP";
        }

        private void UpdateCharacterSelection()
        {
            // Remove selected class from all
            charOptionCrimson?.RemoveFromClassList("selected");
            charOptionVibe?.RemoveFromClassList("selected");
            charOptionSight?.RemoveFromClassList("selected");
            charOptionPato?.RemoveFromClassList("selected");

            // Get current character from LoadoutManager
            string currentCharacter = "CRIMSON"; // Default
            if (LoadoutManager.Instance != null && LoadoutManager.Instance.IsInitialized())
            {
                currentCharacter = LoadoutManager.Instance.GetLoadout().selectedCharacter ?? "CRIMSON";
            }

            // Add selected class to current
            switch (currentCharacter.ToUpper())
            {
                case "CRIMSON":
                    charOptionCrimson?.AddToClassList("selected");
                    break;
                case "VIBE":
                    charOptionVibe?.AddToClassList("selected");
                    break;
                case "SIGHT":
                    charOptionSight?.AddToClassList("selected");
                    break;
                case "PATO":
                    charOptionPato?.AddToClassList("selected");
                    break;
            }
        }

        // ===================================
        // Room List (Real-time from Fusion)
        // ===================================
        private void RefreshRoomList()
        {
            // List is automatically updated via OnRoomListUpdated callback
        }

        /// <summary>Toggle the room list panel visibility and refresh.</summary>
        private void ToggleRoomList()
        {
            var roomOverlay = uiDocument.rootVisualElement.Q<VisualElement>("RoomPanelOverlay");
            if (roomOverlay == null) return;
            
            if (roomOverlay.ClassListContains("hidden"))
            {
                roomOverlay.RemoveFromClassList("hidden");
                RefreshRoomList();
            }
            else
            {
                roomOverlay.AddToClassList("hidden");
            }
        }

        private void OnRoomListUpdated(List<SessionInfo> sessions)
        {
            // Debug.Log($"ðŸ“‹ Received {sessions.Count} rooms from Fusion");
            
            // Only update UI if room list actually changed
            bool hasChanges = activeRooms.Count != sessions.Count;
            if (!hasChanges && activeRooms.Count > 0)
            {
                // Check if room details changed
                for (int i = 0; i < activeRooms.Count; i++)
                {
                    if (i >= sessions.Count || 
                        activeRooms[i].Name != sessions[i].Name ||
                        activeRooms[i].PlayerCount != sessions[i].PlayerCount)
                    {
                        hasChanges = true;
                        break;
                    }
                }
            }
            
            if (hasChanges || activeRooms.Count == 0)
            {
                activeRooms = sessions;
                UpdateRoomListUI();
            }

            // Quick Play is always enabled (creates a room if needed)
            if (quickPlayButton != null)
            {
                quickPlayButton.SetEnabled(true);
            }
        }

        /// <summary>
        /// Called when a player joins the room - updates player cards immediately
        /// </summary>
        private void OnPlayerJoinedRoom(PlayerRef player, NetworkObject playerObject)
        {
            // Only refresh if we're in a room
            if (isInRoom)
            {
                RefreshRoomPlayers();
                
                // Schedule delayed refreshes to catch remote player data that arrives later
                // (remote player's PlayerNetworkData.Spawned() may not have fired yet when OnPlayerJoined triggers)
                StartCoroutine(DelayedRefreshCoroutine());
            }
        }
        
        /// <summary>
        /// Delayed refresh to catch late-arriving remote player data in PlayerCache
        /// </summary>
        private System.Collections.IEnumerator DelayedRefreshCoroutine()
        {
            yield return new WaitForSeconds(0.5f);
            if (isInRoom) RefreshRoomPlayers();
            
            yield return new WaitForSeconds(1.0f);
            if (isInRoom) RefreshRoomPlayers();
            
            yield return new WaitForSeconds(1.5f);
            if (isInRoom) RefreshRoomPlayers();
        }

        /// <summary>
        /// Called when a player leaves the room - updates player cards immediately
        /// </summary>
        private void OnPlayerLeftRoom(PlayerRef player)
        {
            // Remove player from the static cache
            if (ArtisansGuns.Networking.PlayerNetworkData.PlayerCache.ContainsKey(player))
            {
                ArtisansGuns.Networking.PlayerNetworkData.PlayerCache.Remove(player);
            }
            
            // Only refresh if we're in a room
            if (isInRoom)
            {
                RefreshRoomPlayers();
            }
        }

        /// <summary>
        /// Called when player data changes (username, team, ready state, etc.) - updates player cards immediately
        /// </summary>
        private void OnPlayerDataChanged()
        {
            // Debug.Log($"🔄 [LobbyUI] Player data changed - refreshing player list");
            
            // Only refresh if we're in a room
            if (isInRoom)
            {
                RefreshRoomPlayers();
            }
        }

        private void UpdateRoomListUI()
        {
            if (isUpdatingRoomList) return; // Prevent concurrent updates
            
            isUpdatingRoomList = true;
            
            roomList?.Clear();

            // Filter out private rooms from the public list
            var publicRooms = activeRooms.Where(s =>
                !s.Properties.TryGetValue("is_private", out var priv) || priv.PropertyValue.ToString() != "1").ToList();

            foreach (var session in publicRooms)
            {
                var roomItem = CreateRoomItem(session);
                roomList?.Add(roomItem);
            }

            if (publicRooms.Count == 0)
            {
                var emptyLabel = new Label("No rooms available. Create one!");
                emptyLabel.style.color = new StyleColor(new Color(0.6f, 0.7f, 0.8f, 0.6f));
                emptyLabel.style.fontSize = 16;
                emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyLabel.style.paddingTop = 40;
                emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                roomList?.Add(emptyLabel);
            }
            
            // Update create room button state
            UpdateCreateRoomButton();
            
            isUpdatingRoomList = false;
        }

        private VisualElement CreateRoomItem(SessionInfo session)
        {
            var roomItem = new VisualElement();
            roomItem.AddToClassList("room-item");

            var roomInfo = new VisualElement();
            roomInfo.AddToClassList("room-info");

            var roomName = new Label(session.Name);
            roomName.AddToClassList("room-name");
            roomInfo.Add(roomName);

            // Get map name from session properties
            string mapName = "Unknown Map";
            if (session.Properties.TryGetValue("map", out var mapProperty))
            {
                mapName = mapProperty.PropertyValue.ToString();
            }

            var roomDetails = new Label($"{session.PlayerCount}/{session.MaxPlayers} Players â€¢ {mapName}");
            roomDetails.AddToClassList("room-players");
            roomInfo.Add(roomDetails);

            roomItem.Add(roomInfo);

            var joinButton = new Button(() => OnJoinRoom(session.Name)) { text = "JOIN" };
            joinButton.AddToClassList("room-join-button");
            
            // Disable join if room is full
            if (session.PlayerCount >= session.MaxPlayers)
            {
                joinButton.SetEnabled(false);
                joinButton.text = "FULL";
            }
            
            roomItem.Add(joinButton);

            return roomItem;
        }

        private async void OnJoinRoom(string roomName)
        {
            // Debug.Log($"Joining room: {roomName}");
            
            // Show loading spinner
            ShowLoading("JOINING ROOM...", "CONNECTING TO SERVER");
            
            if (NetworkManager.Instance != null)
            {
                bool success = await NetworkManager.Instance.JoinRoom(roomName);
                if (!success)
                {
                    // Debug.LogError("Failed to join room");
                    HideLoading();
                }
            }
            else
            {
                HideLoading();
            }
        }

        private void OnJoinRoomSuccess(string roomName)
        {
            // .IO: Joining sends player directly to Sandbox
            HideLoading();
        }

        private void OnJoinRoomFailed(string reason)
        {
            // Debug.LogError($"âŒ Failed to join room: {reason}");
            HideLoading();
        }

        private async void OnQuickPlayClicked()
        {
            ShowLoading("SEARCHING MATCH...", "CONNECTING");
            
            if (NetworkManager.Instance != null)
            {
                // Wait for network to be ready
                if (!NetworkManager.Instance.IsNetworkReady())
                {
                    ShowLoading("CONNECTING...", "PLEASE WAIT");
                    int attempts = 0;
                    while (!NetworkManager.Instance.IsNetworkReady() && attempts < 50)
                    {
                        await System.Threading.Tasks.Task.Delay(100);
                        attempts++;
                    }
                    if (!NetworkManager.Instance.IsNetworkReady())
                    {
                        HideLoading();
                        return;
                    }
                    ShowLoading("SEARCHING MATCH...", "CONNECTING");
                }

                bool success = await NetworkManager.Instance.QuickPlay();
                if (!success)
                {
                    HideLoading();
                }
                // On success, Fusion loads Sandbox automatically
            }
            else
            {
                HideLoading();
            }
        }

        // ===================================
        // Navigation Tabs
        // ===================================
        // Tab Navigation (called by PersistentUIManager)
        // ===================================
        
        /// <summary>
        /// Switch tabs in LobbyScene - called by PersistentUIManager
        /// Button active classes are handled by PersistentUIManager
        /// </summary>
        public void SetActiveTab(string tabName)
        {
            // Hide all tab contents
            lobbyContent?.AddToClassList("hidden");
            weaponsContent?.AddToClassList("hidden");
            agentsContent?.AddToClassList("hidden");
            shopContent?.AddToClassList("hidden");
            hatsContent?.AddToClassList("hidden");
            roomContent?.AddToClassList("hidden");
            
            // Disable tab controllers when switching away
            if (weaponsTabController != null && tabName != "weapons")
            {
                weaponsTabController.enabled = false;
            }
            if (agentsTabController != null && tabName != "agents")
            {
                agentsTabController.enabled = false;
            }
            if (shopTabController != null && tabName != "shop")
            {
                shopTabController.enabled = false;
            }
            if (hatsTabController != null && tabName != "hats")
            {
                hatsTabController.enabled = false;
            }

            // Update header button active states
            UpdateHeaderButtonStates(tabName);

            // Show corresponding content
            switch (tabName)
            {
                case "lobby":
                    lobbyContent?.RemoveFromClassList("hidden");
                    UpdateCharacterDisplay();
                    UpdateCurrencyDisplay();
                    RefreshRoomList();
                    break;
                case "weapons":
                    weaponsContent?.RemoveFromClassList("hidden");
                    // Enable WeaponsTabController if available
                    if (weaponsTabController != null)
                    {
                        weaponsTabController.enabled = true;
                    }
                    break;
                case "characters":
                case "agents":
                    agentsContent?.RemoveFromClassList("hidden");
                    // Enable AgentsTabController if available
                    if (agentsTabController != null)
                    {
                        agentsTabController.enabled = true;
                    }
                    break;
                case "shop":
                    shopContent?.RemoveFromClassList("hidden");
                    if (shopTabController != null)
                    {
                        shopTabController.enabled = true;
                    }
                    break;
                case "hats":
                    hatsContent?.RemoveFromClassList("hidden");
                    if (hatsTabController != null)
                    {
                        hatsTabController.enabled = true;
                    }
                    break;
                case "room":
                    roomContent?.RemoveFromClassList("hidden");
                    RefreshRoomPlayers();
                    break;
            }
        }
        
        private void UpdateHeaderButtonStates(string activeTab)
        {
            // Remove all active states
            lobbyButton?.RemoveFromClassList("side-nav-btn-active");
            weaponsButton?.RemoveFromClassList("side-nav-btn-active");
            charactersButton?.RemoveFromClassList("side-nav-btn-active");
            shopButton?.RemoveFromClassList("side-nav-btn-active");
            hatsButton?.RemoveFromClassList("side-nav-btn-active");
            
            // Apply active state to the selected tab's button
            switch (activeTab)
            {
                case "lobby":
                case "room":
                    lobbyButton?.AddToClassList("side-nav-btn-active");
                    break;
                case "weapons":
                    weaponsButton?.AddToClassList("side-nav-btn-active");
                    break;
                case "characters":
                case "agents":
                    charactersButton?.AddToClassList("side-nav-btn-active");
                    break;
                case "shop":
                    shopButton?.AddToClassList("side-nav-btn-active");
                    break;
                case "hats":
                    hatsButton?.AddToClassList("side-nav-btn-active");
                    break;
            }
        }

        // ===================================
        // Create Room
        // ===================================
        private void ShowCreateRoom()
        {
            // Check if max rooms reached
            if (activeRooms.Count >= MAX_ROOMS)
            {
                // Debug.LogWarning("Maximum number of rooms reached!");
                return;
            }
            
            createRoomOverlay?.RemoveFromClassList("hidden");
            
            // Clear join code field
            roomCodeField?.SetValueWithoutNotify("");
        }
        
        private void UpdateCreateRoomButton()
        {
            // .IO: always allow private room creation
            if (createRoomButton == null) return;
            createRoomButton.SetEnabled(true);
        }

        private void HideCreateRoom()
        {
            createRoomOverlay?.AddToClassList("hidden");
        }

        private void ShowLoading(string message = "CREATING ROOM...", string subtext = "SETTING UP SERVER")
        {
            if (loadingMessage != null)
                loadingMessage.text = message;
            if (loadingSubtext != null)
                loadingSubtext.text = subtext;
            
            loadingOverlay?.RemoveFromClassList("hidden");
            
            // Start spinner animation
            StartSpinnerAnimation();
        }

        private void HideLoading()
        {
            loadingOverlay?.AddToClassList("hidden");
            retryConnectionButton?.AddToClassList("hidden");
            
            // Stop spinner animation
            StopSpinnerAnimation();
        }

        private void HandleConnectionFailed(string error)
        {
            if (loadingMessage != null)
                loadingMessage.text = "CONNECTION FAILED";
            if (loadingSubtext != null)
                loadingSubtext.text = error;
            
            StopSpinnerAnimation();
            loadingOverlay?.RemoveFromClassList("hidden");
            retryConnectionButton?.RemoveFromClassList("hidden");
        }

        private void OnRetryConnectionClicked()
        {
            retryConnectionButton?.AddToClassList("hidden");
            ShowLoading("RECONNECTING...", "CONNECTING TO SERVER");
            AuthManager.Instance?.RetryConnection();
        }

        private System.Collections.IEnumerator spinnerCoroutine;

        private void StartSpinnerAnimation()
        {
            if (spinnerCoroutine != null)
                StopCoroutine(spinnerCoroutine);
            
            spinnerCoroutine = SpinnerAnimation();
            StartCoroutine(spinnerCoroutine);
        }

        private void StopSpinnerAnimation()
        {
            if (spinnerCoroutine != null)
            {
                StopCoroutine(spinnerCoroutine);
                spinnerCoroutine = null;
            }
        }

        private System.Collections.IEnumerator SpinnerAnimation()
        {
            if (loadingSpinner == null) yield break;
            
            float rotation = 0f;
            while (true)
            {
                rotation += 360f * Time.deltaTime; // 1 rotation per second
                if (rotation >= 360f) rotation -= 360f;
                
                loadingSpinner.style.rotate = new Rotate(rotation);
                
                yield return null;
            }
        }

        private async void OnCreateRoomConfirmed()
        {
            // .IO: Create private room with auto-generated code

            // Disable button to prevent double-click
            if (confirmCreateRoomButton != null)
                confirmCreateRoomButton.SetEnabled(false);
            
            // Wait for network to be ready
            if (NetworkManager.Instance != null && !NetworkManager.Instance.IsNetworkReady())
            {
                ShowLoading("CONNECTING...", "PLEASE WAIT");
                
                int attempts = 0;
                while (!NetworkManager.Instance.IsNetworkReady() && attempts < 50)
                {
                    await System.Threading.Tasks.Task.Delay(100);
                    attempts++;
                }
                
                if (!NetworkManager.Instance.IsNetworkReady())
                {
                    HideLoading();
                    if (confirmCreateRoomButton != null)
                        confirmCreateRoomButton.SetEnabled(true);
                    return;
                }
            }
            
            HideCreateRoom();
            ShowLoading("CREATING PRIVATE ROOM...", "GENERATING CODE");

            if (NetworkManager.Instance != null)
            {
                // Use map from the overlay (currently only Sandbox)
                string code = await NetworkManager.Instance.CreatePrivateRoom("Sandbox");
                if (code != null)
                {
                    // Show the code briefly before scene transitions
                    ShowLoading("PRIVATE ROOM CREATED", $"CODE: {code}");
                }
                else
                {
                    HideLoading();
                    if (confirmCreateRoomButton != null)
                        confirmCreateRoomButton.SetEnabled(true);
                }
            }
            else
            {
                HideLoading();
                if (confirmCreateRoomButton != null)
                    confirmCreateRoomButton.SetEnabled(true);
            }
        }

        private void OnRoomCreatedSuccess(string roomName)
        {
            // .IO: Room created, Fusion loads Sandbox automatically
            HideLoading();
            
            if (confirmCreateRoomButton != null)
                confirmCreateRoomButton.SetEnabled(true);
        }

        private async void OnJoinPrivateRoomClicked()
        {
            string code = roomCodeField?.value?.Trim();
            
            if (string.IsNullOrEmpty(code) || code.Length < 6)
            {
                // Brief visual feedback — the user hasn't entered enough chars (6 digits)
                return;
            }

            if (joinPrivateRoomButton != null)
                joinPrivateRoomButton.SetEnabled(false);

            HideCreateRoom();
            ShowLoading("JOINING...", $"CODE: {code}");

            if (NetworkManager.Instance != null)
            {
                bool success = await NetworkManager.Instance.JoinPrivateRoom(code);
                if (!success)
                {
                    HideLoading();
                    // Re-show overlay so user can retry
                    ShowCreateRoom();
                    if (roomCodeField != null) roomCodeField.SetValueWithoutNotify(code);
                }
            }
            else
            {
                HideLoading();
            }

            if (joinPrivateRoomButton != null)
                joinPrivateRoomButton.SetEnabled(true);
        }

        // ===================================
        // Save Progress / Account Creation
        // ===================================

        /// <summary>
        /// Called when a guest session is ready (initial load or after logout).
        /// Refreshes UI to reflect guest state.
        /// </summary>
        private void HandleGuestReadyInLobby(AuthManager.UserData user)
        {
            UpdateSaveProgressButtonVisibility();
            settingsUIController?.UpdateLogoutButtonVisibility();
            UpdateCharacterDisplay();
        }

        /// <summary>
        /// Called when a login succeeds (from login overlay or restored session).
        /// Refreshes UI to reflect logged-in state.
        /// </summary>
        private void HandleLoginSuccessInLobby(AuthManager.UserData user)
        {
            // Close any sign-up overlays that might have been opened during the
            // brief window before auth mode was confirmed
            HideCharacterNameOverlay();
            HideSaveProgressOverlay();
            UpdateSaveProgressButtonVisibility();
            settingsUIController?.UpdateLogoutButtonVisibility();
            UpdateCharacterDisplay();
        }

        private void UpdateSaveProgressButtonVisibility()
        {
            if (googlePromoPanel == null)
            {
                Debug.LogWarning("[LobbyUI] GooglePromoPanel is NULL - cannot update visibility");
                return;
            }
            
            bool isGuest = AuthManager.Instance != null && AuthManager.Instance.IsGuest;
            Debug.Log($"[LobbyUI] UpdateSaveProgressButtonVisibility - isGuest={isGuest}");
            
            // Use inline style.display (overrides USS classes) to guarantee visibility change
            googlePromoPanel.style.display = isGuest ? DisplayStyle.Flex : DisplayStyle.None;
        }
        
        private void ShowSaveProgressOverlay()
        {
            saveProgressOverlay?.RemoveFromClassList("hidden");
            if (saveProgressError != null) saveProgressError.style.display = DisplayStyle.None;
        }
        
        private void HideSaveProgressOverlay()
        {
            saveProgressOverlay?.AddToClassList("hidden");
        }
        
        private void ShowLoginOverlay()
        {
            HideSaveProgressOverlay();
            loginOverlay?.RemoveFromClassList("hidden");
            if (loginError != null) loginError.style.display = DisplayStyle.None;
        }
        
        private void HideLoginOverlay()
        {
            loginOverlay?.AddToClassList("hidden");
        }

        private void ShowCharacterNameOverlay()
        {
            HideSaveProgressOverlay();
            characterNameOverlay?.RemoveFromClassList("hidden");
            charNameField?.SetValueWithoutNotify("");
            if (charNameError != null) charNameError.style.display = DisplayStyle.None;
        }

        private void HideCharacterNameOverlay()
        {
            characterNameOverlay?.AddToClassList("hidden");
            pendingGoogleIdToken = null;
        }

        // --- Google Link Reward ---

        private void ShowGoogleRewardOverlay(int bonusAmount)
        {
            if (rewardAmountLabel != null)
                rewardAmountLabel.text = $"+{bonusAmount:#,##0}";
            googleRewardOverlay?.RemoveFromClassList("hidden");
        }

        private void HideGoogleRewardOverlay()
        {
            googleRewardOverlay?.AddToClassList("hidden");
        }

        // --- Google Sign-In for Save Progress (link guest to Google) ---

        private Coroutine _googleSignInTimeoutLink;

        private void OnGoogleSignInForLink()
        {
            if (GoogleAuthService.Instance == null)
            {
                ShowSaveProgressError("Google Sign-In not available");
                return;
            }
            if (googleSignInButton != null) googleSignInButton.SetEnabled(false);

            // Defensive: unsubscribe first to prevent stacking if called twice
            GoogleAuthService.Instance.OnGoogleSignInSuccess -= OnGoogleSignInForLinkSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed -= OnGoogleSignInForLinkFailed;
            GoogleAuthService.Instance.OnGoogleSignInSuccess += OnGoogleSignInForLinkSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed += OnGoogleSignInForLinkFailed;
            GoogleAuthService.Instance.SignIn();

            // Safety timeout: re-enable button if Google callback never fires
            if (_googleSignInTimeoutLink != null) StopCoroutine(_googleSignInTimeoutLink);
            _googleSignInTimeoutLink = StartCoroutine(GoogleSignInTimeoutLink());
        }

        private System.Collections.IEnumerator GoogleSignInTimeoutLink()
        {
            yield return new WaitForSeconds(15f);
            Debug.LogWarning("[Lobby] Google Sign-In timed out (link) — re-enabling button");
            GoogleAuthService.Instance.OnGoogleSignInSuccess -= OnGoogleSignInForLinkSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed -= OnGoogleSignInForLinkFailed;
            if (googleSignInButton != null) googleSignInButton.SetEnabled(true);
        }

        private void OnGoogleSignInForLinkSuccess(string idToken)
        {
            if (_googleSignInTimeoutLink != null) { StopCoroutine(_googleSignInTimeoutLink); _googleSignInTimeoutLink = null; }
            GoogleAuthService.Instance.OnGoogleSignInSuccess -= OnGoogleSignInForLinkSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed -= OnGoogleSignInForLinkFailed;
            if (googleSignInButton != null) googleSignInButton.SetEnabled(true);

            // Store the token and show character name selection
            pendingGoogleIdToken = idToken;
            ShowCharacterNameOverlay();
        }

        private void OnGoogleSignInForLinkFailed(string error)
        {
            if (_googleSignInTimeoutLink != null) { StopCoroutine(_googleSignInTimeoutLink); _googleSignInTimeoutLink = null; }
            GoogleAuthService.Instance.OnGoogleSignInSuccess -= OnGoogleSignInForLinkSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed -= OnGoogleSignInForLinkFailed;
            if (googleSignInButton != null) googleSignInButton.SetEnabled(true);
            ShowSaveProgressError(error);
        }

        // --- Confirm character name (after Google Sign-In) ---

        private void OnConfirmCharacterName()
        {
            string characterName = charNameField?.value?.Trim();

            if (string.IsNullOrEmpty(characterName) || characterName.Length < 3 || characterName.Length > 18)
            {
                ShowCharNameError("Name must be 3-18 characters");
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(characterName, @"^[a-zA-Z0-9\s]+$"))
            {
                ShowCharNameError("Only letters, numbers and spaces allowed");
                return;
            }
            if (string.IsNullOrEmpty(pendingGoogleIdToken))
            {
                ShowCharNameError("Google sign-in expired. Please try again.");
                HideCharacterNameOverlay();
                return;
            }

            if (confirmCharNameButton != null) confirmCharNameButton.SetEnabled(false);

            AuthManager.Instance.OnGoogleLinkSuccess += OnGoogleLinkSuccess;
            AuthManager.Instance.OnGoogleLinkFailed += OnGoogleLinkFailed;

            AuthManager.Instance.GoogleLink(pendingGoogleIdToken, characterName);
        }

        private void OnGoogleLinkSuccess(AuthManager.UserData user)
        {
            AuthManager.Instance.OnGoogleLinkSuccess -= OnGoogleLinkSuccess;
            AuthManager.Instance.OnGoogleLinkFailed -= OnGoogleLinkFailed;

            HideCharacterNameOverlay();
            HideSaveProgressOverlay();
            if (confirmCharNameButton != null) confirmCharNameButton.SetEnabled(true);

            UpdateSaveProgressButtonVisibility();
            settingsUIController?.UpdateLogoutButtonVisibility();
            UpdateCharacterDisplay();
            UpdateCurrencyDisplay();

            // Show reward panel if bonus was awarded
            int bonus = AuthManager.Instance.LastGoogleLinkBonus;
            if (bonus > 0)
            {
                ShowGoogleRewardOverlay(bonus);
            }
        }

        private void OnGoogleLinkFailed(string error)
        {
            AuthManager.Instance.OnGoogleLinkSuccess -= OnGoogleLinkSuccess;
            AuthManager.Instance.OnGoogleLinkFailed -= OnGoogleLinkFailed;
            if (confirmCharNameButton != null) confirmCharNameButton.SetEnabled(true);
            ShowCharNameError(error);
        }

        // --- Google Sign-In for Login (restore existing account) ---

        private Coroutine _googleSignInTimeoutLogin;

        private void OnGoogleSignInForLogin()
        {
            if (GoogleAuthService.Instance == null)
            {
                ShowLoginError("Google Sign-In not available");
                return;
            }
            if (confirmLoginButton != null) confirmLoginButton.SetEnabled(false);

            // Defensive: unsubscribe first to prevent stacking
            GoogleAuthService.Instance.OnGoogleSignInSuccess -= OnGoogleSignInForLoginSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed -= OnGoogleSignInForLoginFailed;
            GoogleAuthService.Instance.OnGoogleSignInSuccess += OnGoogleSignInForLoginSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed += OnGoogleSignInForLoginFailed;
            GoogleAuthService.Instance.SignIn();

            // Safety timeout: re-enable button if Google callback never fires
            if (_googleSignInTimeoutLogin != null) StopCoroutine(_googleSignInTimeoutLogin);
            _googleSignInTimeoutLogin = StartCoroutine(GoogleSignInTimeoutLogin());
        }

        private System.Collections.IEnumerator GoogleSignInTimeoutLogin()
        {
            yield return new WaitForSeconds(15f);
            Debug.LogWarning("[Lobby] Google Sign-In timed out (login) — re-enabling button");
            GoogleAuthService.Instance.OnGoogleSignInSuccess -= OnGoogleSignInForLoginSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed -= OnGoogleSignInForLoginFailed;
            if (confirmLoginButton != null) confirmLoginButton.SetEnabled(true);
        }

        private void OnGoogleSignInForLoginSuccess(string idToken)
        {
            if (_googleSignInTimeoutLogin != null) { StopCoroutine(_googleSignInTimeoutLogin); _googleSignInTimeoutLogin = null; }
            GoogleAuthService.Instance.OnGoogleSignInSuccess -= OnGoogleSignInForLoginSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed -= OnGoogleSignInForLoginFailed;

            // Send to backend — backend verifies and finds the linked account
            AuthManager.Instance.OnGoogleLoginSuccess += OnGoogleLoginSuccess;
            AuthManager.Instance.OnGoogleLoginFailed += OnGoogleLoginFailed;
            AuthManager.Instance.GoogleLogin(idToken);
        }

        private void OnGoogleSignInForLoginFailed(string error)
        {
            if (_googleSignInTimeoutLogin != null) { StopCoroutine(_googleSignInTimeoutLogin); _googleSignInTimeoutLogin = null; }
            GoogleAuthService.Instance.OnGoogleSignInSuccess -= OnGoogleSignInForLoginSuccess;
            GoogleAuthService.Instance.OnGoogleSignInFailed -= OnGoogleSignInForLoginFailed;
            if (confirmLoginButton != null) confirmLoginButton.SetEnabled(true);
            ShowLoginError(error);
        }

        private void OnGoogleLoginSuccess(AuthManager.UserData user)
        {
            AuthManager.Instance.OnGoogleLoginSuccess -= OnGoogleLoginSuccess;
            AuthManager.Instance.OnGoogleLoginFailed -= OnGoogleLoginFailed;

            HideLoginOverlay();
            if (confirmLoginButton != null) confirmLoginButton.SetEnabled(true);

            UpdateSaveProgressButtonVisibility();
            settingsUIController?.UpdateLogoutButtonVisibility();
            UpdateCharacterDisplay();
        }

        private void OnGoogleLoginFailed(string error)
        {
            AuthManager.Instance.OnGoogleLoginSuccess -= OnGoogleLoginSuccess;
            AuthManager.Instance.OnGoogleLoginFailed -= OnGoogleLoginFailed;
            if (confirmLoginButton != null) confirmLoginButton.SetEnabled(true);
            ShowLoginError(error);
        }
        
        private void ShowSaveProgressError(string message)
        {
            if (saveProgressError != null)
            {
                saveProgressError.text = message;
                saveProgressError.style.display = DisplayStyle.Flex;
            }
        }

        private void ShowCharNameError(string message)
        {
            if (charNameError != null)
            {
                charNameError.text = message;
                charNameError.style.display = DisplayStyle.Flex;
            }
        }

        private void ShowLoginError(string message)
        {
            if (loginError != null)
            {
                loginError.text = message;
                loginError.style.display = DisplayStyle.Flex;
            }
        }

        // ===================================
        // Data Persistence
        // ===================================
        private void LoadPlayerData()
        {
            // Character data now comes from LoadoutManager (backend)
            // Only load local settings like sensitivity
            
            currentSensitivity = ArtisansGuns.Managers.SettingsManager.Instance?.GetMouseSensitivity() ?? PlayerPrefs.GetFloat("player_sensitivity", 2.0f);
            // SettingsUIController handles updating its own slider via LoadSettings()
        // Debug.Log($"ðŸ’¾ Loaded settings - Sensitivity: {currentSensitivity}");
        }

        private void SavePlayerData()
        {
            // Only save local settings - character/loadout saved by LoadoutManager
            PlayerPrefs.SetFloat("sensitivity", currentSensitivity);
            PlayerPrefs.Save();
            // Debug.Log($"ðŸ’¾ Saved settings");
        }

        // ===================================
        // Room Management
        // ===================================
        private void EnterRoomMode(string roomName, bool isRoomHost)
        {
            isInRoom = true;
            isHost = isRoomHost;
            currentRoomName = roomName;
            
            if (NetworkManager.Instance != null)
            {
                currentMapName = NetworkManager.Instance.GetCurrentMapName();
            }
            
            // Update header
            if (logoLabel != null) logoLabel.text = "ROOM";
            exitButtonContainer?.RemoveFromClassList("hidden");
            playersCountLabel?.RemoveFromClassList("hidden");
            
            // Update room info
            if (mapHeaderLabel != null) mapHeaderLabel.text = currentMapName.ToUpper();
            if (roomIdLabel != null) roomIdLabel.text = $"ROOM ID // {roomName}";
            if (gameModeLabel != null) gameModeLabel.text = "MODE // DEATHMATCH";
            if (maxPlayersLabel != null) maxPlayersLabel.text = "CAPACITY // 10";
            
            // Load map image from Resources/UI folder
            if (mapImage != null)
            {
                string imagePath = $"UI/{currentMapName}";
                // Debug.Log($"ðŸ–¼ï¸ [LobbyUI] Attempting to load map image: Resources/{imagePath}");
                
                Texture2D mapTexture = Resources.Load<Texture2D>(imagePath);
                
                if (mapTexture != null)
                {
                    mapImage.style.backgroundImage = StyleKeyword.Null;
                    mapImage.style.backgroundImage = new StyleBackground(mapTexture);
                    mapImage.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                    mapImage.style.display = DisplayStyle.Flex;
                    mapImage.style.visibility = Visibility.Visible;
                    mapImage.style.opacity = 1f;
                    
                    // Debug.Log($"âœ… [LobbyUI] Map image loaded: {imagePath}, Size: {mapTexture.width}x{mapTexture.height}");
                }
                else
                {
                    // Debug.LogWarning($"âš ï¸ [LobbyUI] Map image not found at: Assets/Resources/{imagePath}.png");
                }
            }
            else
            {
                // Debug.LogWarning("âš ï¸ [LobbyUI] MapImage VisualElement not found in UI");
            }
            
            // Debug.Log($"ðŸŽ® EnterRoomMode called - IsHost: {isHost}");
            // Debug.Log($"  ReadyButton: {(readyButton != null ? "Found" : "NULL")}");
            // Debug.Log($"  StartGameButton: {(startGameButton != null ? "Found" : "NULL")}");
            // Debug.Log($"  HostControls: {(hostControls != null ? "Found" : "NULL")}");
            
            // Show/hide controls based on host status
            if (isHost)
            {
                // Debug.Log("  ðŸ”¹ Configuring as HOST");
                if (hostControls != null)
                {
                    hostControls.RemoveFromClassList("hidden");
                    // Debug.Log("    âœ… HostControls shown");
                }
                if (readyButton != null)
                {
                    readyButton.AddToClassList("hidden");
                    // Debug.Log("    âœ… ReadyButton hidden");
                }
                if (startGameButton != null)
                {
                    startGameButton.text = "START GAME";
                    // Debug.Log("    âœ… StartGameButton text set to 'START GAME'");
                }
                waitingMessage?.AddToClassList("hidden");
            }
            else
            {
                // Debug.Log("  ðŸ”¹ Configuring as CLIENT");
                if (hostControls != null)
                {
                    hostControls.AddToClassList("hidden");
                    // Debug.Log("    âœ… HostControls hidden");
                }
                if (readyButton != null)
                {
                    readyButton.RemoveFromClassList("hidden");
                    readyButton.text = "READY";
                    // Debug.Log("    âœ… ReadyButton shown with text 'READY'");
                }
                waitingMessage?.AddToClassList("hidden");
            }
            
            // Switch to room tab
            SetActiveTab("room");
            
            // DON'T call RefreshRoomPlayers() here - it executes before network objects spawn!
            // Event-driven updates will handle all refreshes:
            // - OnPlayerJoinedRoom fires when player joins
            // - OnPlayerLeftRoom fires when player leaves  
            // - OnPlayerDataChanged fires when player data changes (team, ready, etc.)
            // - NotifyPlayerDataAfterSync (0.3s delay) ensures both clients' objects are synced
            
            // Start monitoring countdown state from network
            InvokeRepeating(nameof(UpdateCountdownDisplay), 0.1f, 0.1f);
            
            // Periodic refresh to sync ready states and team changes from remote players
            InvokeRepeating(nameof(RefreshRoomPlayers), 2f, 1.5f);
            
            // Debug.Log($"âœ… Entered room mode - Room: {roomName}, IsHost: {isHost}");
        }
        
        private async void ExitRoomMode()
        {
            if (!isInRoom) return;
            
            // Stop countdown and periodic refresh
            CancelInvoke(nameof(UpdateCountdownDisplay));
            CancelInvoke(nameof(RefreshRoomPlayers));
            
            // Leave room via NetworkManager
            if (NetworkManager.Instance != null)
            {
                ShowLoading("LEAVING ROOM...", "DISCONNECTING");
                await NetworkManager.Instance.LeaveRoom();
                HideLoading();
            }
            
            // Reset state
            isInRoom = false;
            isHost = false;
            currentRoomName = "";
            playersInRoom.Clear();
            
            // Reset header to lobby state
            if (logoLabel != null) logoLabel.text = "LOBBY";
            exitButtonContainer?.AddToClassList("hidden");
            playersCountLabel?.AddToClassList("hidden");
            
            // Return to lobby tab
            SetActiveTab("lobby");
            RefreshRoomList();
            
            // Debug.Log("âœ… Exited room mode");
        }
        
        private void OnLobbyButtonClicked()
        {
            if (isInRoom)
            {
                // In room - go to room tab
                SetActiveTab("room");
            }
            else
            {
                // In lobby - show lobby tab
                SetActiveTab("lobby");
            }
        }
        
        private void RefreshRoomPlayers()
        {
            if (!isInRoom) return;
            
            var runner = FindObjectOfType<NetworkRunner>();
            if (runner == null || !runner.IsRunning)
            {
                // Debug.LogWarning("âš ï¸ NetworkRunner not running");
                CancelInvoke(nameof(RefreshRoomPlayers));
                return;
            }
            
            // Clear current lists
            teamAList?.Clear();
            teamBList?.Clear();
            playersInRoom.Clear();
            
            // Use static PlayerCache for reliable discovery (works even when remote objects are transient)
            var allCachedPlayers = ArtisansGuns.Networking.PlayerNetworkData.PlayerCache.Values
                .OrderBy(c => c.JoinOrder)
                .ToList();
            
            if (allCachedPlayers.Count == 0)
            {
                // Fallback: try FindObjectsOfType in case cache hasn't been populated yet
                // SAFE: Only access valid, spawned PlayerNetworkData objects
                var livePlayerData = FindObjectsOfType<ArtisansGuns.Networking.PlayerNetworkData>()
                    .Where(pd => pd != null && pd.Object != null && pd.Object.IsValid)
                    .ToList();
                foreach (var pd in livePlayerData)
                    pd.UpdatePlayerCache();
                allCachedPlayers = ArtisansGuns.Networking.PlayerNetworkData.PlayerCache.Values
                    .OrderBy(c => c.JoinOrder)
                    .ToList();
                if (allCachedPlayers.Count == 0) return;
            }
            
            // Debug.Log($"[RefreshRoomPlayers] Found {allCachedPlayers.Count} players (from PlayerCache):");
            // for (int i = 0; i < allCachedPlayers.Count; i++)
            // {
            //     var pd = allCachedPlayers[i];
            //     Debug.Log($"  Player {i}: User={pd.Username}, JoinOrder={pd.JoinOrder}, Team={pd.Team}, NetworkId={pd.NetworkId}, IsLocal={pd.HasInputAuthority}");
            // }
            
            // Determinar el host (el jugador con el JoinOrder mas bajo)
            var hostPlayer = allCachedPlayers.FirstOrDefault();
            
            foreach (var cached in allCachedPlayers)
            {
                // Skip players whose team hasn't been resolved yet (avoids
                // briefly showing them in Team A during the 1-second assignment delay)
                if (!cached.TeamAssigned) continue;
                
                bool isLocal = cached.HasInputAuthority;
                bool isPlayerHost = cached.PlayerRef.Equals(hostPlayer.PlayerRef);
                
                string username = cached.Username;
                string characterName = cached.CharacterName;
                
                // Get agent name - prioritize SelectedAgent from cached data
                string agentName = "crimson"; // Default
                if (!string.IsNullOrEmpty(cached.SelectedAgent))
                {
                    agentName = cached.SelectedAgent.ToLower();
                }
                else if (isLocal && LoadoutManager.Instance != null)
                {
                    var loadout = LoadoutManager.Instance.GetLoadout();
                    agentName = loadout?.selectedCharacter?.ToLower() ?? "crimson";
                }
                
                int level = cached.Level;
                string primaryWeapon = cached.PrimaryWeapon;
                string secondaryWeapon = cached.SecondaryWeapon;
                int team = cached.Team;
                bool isReady = cached.IsReady;
                int joinOrder = cached.JoinOrder;
                
                playersInRoom.Add(new PlayerInRoom
                {
                    Username = username,
                    CharacterName = characterName,
                    AgentName = agentName,
                    IsHost = isPlayerHost,
                    IsLocal = isLocal,
                    Team = team,
                    IsReady = isReady,
                    JoinOrder = joinOrder,
                    Level = level,
                    PrimaryWeapon = primaryWeapon,
                    SecondaryWeapon = secondaryWeapon
                });
            }
            // Update player count
            int playerCount = playersInRoom.Count;
            if (playersCountLabel != null) playersCountLabel.text = $"{playerCount}/10";
            
            // Separate players by team and render
            var teamAPlayers = playersInRoom.Where(p => p.Team == 0).OrderBy(p => p.JoinOrder).ToList();
            var teamBPlayers = playersInRoom.Where(p => p.Team == 1).OrderBy(p => p.JoinOrder).ToList();
            
            // Add Team A players
            foreach (var player in teamAPlayers)
            {
                var playerItem = CreatePlayerItem(player);
                teamAList.Add(playerItem);
            }
            
            // Add Team B players
            foreach (var player in teamBPlayers)
            {
                var playerItem = CreatePlayerItem(player);
                teamBList.Add(playerItem);
            }
            
            // Debug.Log($"Room players refreshed: {playerCount} players (Team A: {teamAPlayers.Count}, Team B: {teamBPlayers.Count})");
            
            // Update START GAME button state (only for host)
            // Check if local player is the current host (might have changed due to host transfer)
            var currentLocalPlayerCached = allCachedPlayers.FirstOrDefault(c => c.HasInputAuthority);
            bool isCurrentHost = currentLocalPlayerCached.HasInputAuthority && currentLocalPlayerCached.PlayerRef.Equals(allCachedPlayers.OrderBy(c => c.JoinOrder).FirstOrDefault().PlayerRef);
            
            // Update isHost flag if it changed
            if (isInRoom && isCurrentHost != isHost)
            {
                // Debug.Log($"ðŸ”„ Host status changed: was {isHost}, now {isCurrentHost}");
                isHost = isCurrentHost;
                
                // Update UI visibility
                if (isHost)
                {
                    hostControls?.RemoveFromClassList("hidden");
                    readyButton?.AddToClassList("hidden");
                }
                else
                {
                    hostControls?.AddToClassList("hidden");
                    readyButton?.RemoveFromClassList("hidden");
                }
            }
            
            if (isHost && startGameButton != null)
            {
                // Find host player's team
                var localPlayer = playersInRoom.FirstOrDefault(p => p.IsLocal);
                if (localPlayer != null)
                {
                    // âœ… Allow game to start even without opposite team (for testing/solo play)
                    // Host can always start the game if at least they are in the room
                    startGameButton.SetEnabled(true);
                    startGameButton.text = "START GAME";
                    
                    // Optional: Show different message if no opposite team
                    int hostTeam = localPlayer.Team;
                    int oppositeTeam = hostTeam == 0 ? 1 : 0;
                    bool hasOppositeTeam = playersInRoom.Any(p => p.Team == oppositeTeam);
                    
                    if (!hasOppositeTeam)
                    {
                        // Debug.Log("âš ï¸ No players on opposite team - solo/testing mode");
                    }
                }
            }
        }
        
        private void OnReadyButtonClicked()
        {
            // Debug.Log("ðŸ”˜ Ready button clicked");
            
            // Find local player's PlayerNetworkData
            var localPlayerData = FindObjectsOfType<ArtisansGuns.Networking.PlayerNetworkData>()
                .FirstOrDefault(pd => pd != null && pd.Object != null && pd.Object.HasInputAuthority);
            
            if (localPlayerData == null)
            {
                // Debug.LogError("âŒ Could not find local player's PlayerNetworkData!");
                return;
            }
            
            // Toggle ready state
            bool newReadyState = !localPlayerData.IsReady;
            
            if (localPlayerData.HasStateAuthority)
            {
                localPlayerData.IsReady = newReadyState;
            }
            else
            {
                // Call RPC to set ready state on state authority
                localPlayerData.SetReady(newReadyState);
            }
            
            // Update button text to reflect current state
            readyButton.text = newReadyState ? "READY ✓" : "READY";
            
            // Update cache immediately so UI shows the change right away
            localPlayerData.UpdatePlayerCache();
            
            // Refresh player list to show updated ready states
            RefreshRoomPlayers();
        }
        
        /// <summary>
        /// Update countdown display based on GameStateManager networked state
        /// Called every 0.1 seconds to sync UI with network state
        /// </summary>
        private void UpdateCountdownDisplay()
        {
            if (!isInRoom || countdownOverlay == null || countdownLabel == null)
                return;
                
            // Check if GameStateManager exists
            var gameState = ArtisansGuns.Networking.GameStateManager.Instance;
            if (gameState == null)
            {
                // Hide overlay if no game state
                countdownOverlay.AddToClassList("hidden");
                return;
            }
            
            int countdownValue = gameState.CountdownValue;
            
            if (countdownValue >= 0)
            {
                // Show countdown overlay with networked value
                countdownOverlay.RemoveFromClassList("hidden");

                var titleLabel   = countdownOverlay.Q<Label>("CountdownTitle");
                var messageLabel = countdownOverlay.Q<Label>("CountdownMessage");

                if (countdownValue > 0)
                {
                    // Big label = just the number; title visible; message hidden
                    countdownLabel.text = countdownValue.ToString();
                    if (titleLabel   != null) { titleLabel.style.display   = DisplayStyle.Flex; }
                    if (messageLabel != null) { messageLabel.style.display = DisplayStyle.None; }
                }
                else
                {
                    // countdownValue == 0: show "MISSION COMMENCING"; hide number + title
                    countdownLabel.text = "";
                    if (titleLabel   != null) { titleLabel.style.display   = DisplayStyle.None; }
                    if (messageLabel != null) { messageLabel.style.display = DisplayStyle.Flex; }
                }
            }
            else
            {
                // Hide countdown overlay when not in countdown
                countdownOverlay.AddToClassList("hidden");
            }
        }
        
        private void OnStartGameClicked()
        {
            if (!isHost)
            {
                // Debug.LogWarning("âš ï¸ Only host can start the game!");
                return;
            }
            
            // Validate: Must have at least 1 ready player on the opposite team
            // âœ… Solo mode enabled - no validation needed for opposite team
            // Debug.Log("ðŸŽ® Starting game via NetworkManager (solo mode enabled)...");
            
            // Call NetworkManager to start game (it will handle GameStateManager countdown)
            if (NetworkManager.Instance != null)
            {
                NetworkManager.Instance.StartGame();
            }
        }
    
        
        private VisualElement CreatePlayerItem(PlayerInRoom player)
        {
            // Debug.Log($"ðŸ” [LobbyUI] CreatePlayerItem called - Username: '{player.Username}', AgentName: '{player.AgentName}', CharacterName: '{player.CharacterName}'");
            
            // Ensure player card template is loaded
            if (playerCardTemplate == null)
            {
                playerCardTemplate = Resources.Load<VisualTreeAsset>("UI/PlayerCard");
                if (playerCardTemplate == null)
                {
                    // Debug.LogError("âŒ PlayerCard template not found - creating fallback");
                    return CreateFallbackPlayerItem(player);
                }
            }

            var playerCard = playerCardTemplate.Instantiate();
            var root = playerCard.Q<VisualElement>("PlayerCard");
            
            if (root == null)
            {
                // Debug.LogError("âŒ PlayerCard root not found in template!");
                return CreateFallbackPlayerItem(player);
            }

            // Apply inline styles - Root card
            root.style.width = 160;
            root.style.minHeight = 320;
            root.style.maxHeight = 370;
            root.style.backgroundColor = new Color(0.02f, 0.02f, 0.03f, 0.95f); // temp, overridden below
            root.style.borderBottomColor = new Color(0.18f, 0.18f, 0.2f, 1f);
            root.style.borderRightColor  = new Color(0.18f, 0.18f, 0.2f, 1f);
            root.style.borderTopColor    = new Color(0.18f, 0.18f, 0.2f, 1f);
            root.style.borderBottomWidth = 1;
            root.style.borderLeftWidth = 4;
            root.style.borderRightWidth = 1;
            root.style.borderTopWidth = 1;
            root.style.paddingBottom = 16;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 16;
            root.style.marginTop = 12;
            root.style.marginBottom = 8;
            root.style.marginLeft = 3;
            root.style.marginRight = 3;
            root.style.flexDirection = FlexDirection.Column;

            // Determine player state and card colors
            bool isInGame = false;
            
            // Check if player is currently InGame (in GameScene)
            // SAFE: Only access spawned objects with valid NetworkObject
            var playerNetworkData = FindObjectsOfType<ArtisansGuns.Networking.PlayerNetworkData>()
                .FirstOrDefault(pd => pd.Object != null && pd.Object.IsValid && pd.Username.ToString() == player.Username);
            if (playerNetworkData != null)
            {
                isInGame = playerNetworkData.InGame;
            }
            
            // âš ï¸ IMPORTANT: ALL teams use the SAME colors - NO difference between Team A and Team B!
            // Default orange color for ALL players regardless of team
            // Team A = orange brand, Team B = complementary cyan
            Color teamColor   = player.Team == 0 ? new Color(1f, 0.37f, 0.20f, 1f)        : new Color(0f, 0.765f, 0.941f, 1f);
            Color teamBgColor = player.Team == 0 ? new Color(0.09f, 0.03f, 0.02f, 0.95f)  : new Color(0.02f, 0.07f, 0.10f, 0.95f);

            root.style.backgroundColor = teamBgColor;
            root.style.borderLeftColor = teamColor;

            if (player.IsHost)
            {
                Color hostColor = player.Team == 0
                    ? new Color(1f, 0.45f, 0.27f, 1f)
                    : new Color(0.2f, 0.85f, 1f, 1f);
                root.style.borderLeftColor = hostColor;
            }

            if (player.IsReady && !isInGame)
            {
                root.style.borderLeftColor = new Color(0.2f, 0.9f, 0.4f, 1f);
                root.style.borderLeftWidth = 5;
                root.style.backgroundColor = new Color(0.03f, 0.10f, 0.05f, 0.95f);
            }

            if (isInGame)
            {
                root.style.borderLeftColor = new Color(1f, 0.88f, 0.1f, 1f);
                root.style.borderLeftWidth = 5;
                root.style.backgroundColor = new Color(0.10f, 0.09f, 0.01f, 0.95f);
            }

            // Populate and style all elements
            
            // Username Panel (top bar)
            var usernamePanel = root.Q<VisualElement>("UsernamePanel");
            if (usernamePanel != null)
            {
                usernamePanel.style.flexDirection = FlexDirection.Row;
                usernamePanel.style.justifyContent = Justify.SpaceBetween;
                usernamePanel.style.alignItems = Align.Center;
                usernamePanel.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 0.8f);
                usernamePanel.style.paddingTop = 6;
                usernamePanel.style.paddingBottom = 6;
                usernamePanel.style.paddingLeft = 10;
                usernamePanel.style.paddingRight = 10;
                usernamePanel.style.marginBottom = 8;
                usernamePanel.style.borderBottomWidth = 1;
                usernamePanel.style.borderBottomColor = new Color(0.2f, 0.2f, 0.22f, 1f);
            }
            
            // User Character Name (in username panel - e.g., SEA)
            var usernameLabel = root.Q<Label>("Username");
            if (usernameLabel != null)
            {
                usernameLabel.text = player.CharacterName; // User's actual character name (e.g., "sea")
                usernameLabel.style.fontSize = 11;
                usernameLabel.style.color = new Color(0.98f, 0.98f, 1f, 1f);
                usernameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                usernameLabel.style.flexGrow = 1;
            }
            
            // Host Crown (inside username panel, right side)
            var hostCrown = root.Q<Label>("HostCrown");
            if (hostCrown != null)
            {
                if (player.IsHost)
                {
                    hostCrown.style.display = DisplayStyle.Flex;
                    hostCrown.style.fontSize = 18;
                    hostCrown.style.marginLeft = 8;
                }
                else
                {
                    hostCrown.style.display = DisplayStyle.None;
                }
            }
            
            // Hide ReadyBadge and CardHeader always
            var readyBadge = root.Q<Label>("ReadyBadge");
            if (readyBadge != null) readyBadge.style.display = DisplayStyle.None;

            // Hide CardHeader (redundant text)
            var cardHeader = root.Q<Label>("CardHeader");
            if (cardHeader != null)
            {
                cardHeader.style.display = DisplayStyle.None;
            }

            // Agent Name (large orange text - e.g., CRIMSON)  
            var agentLabel = root.Q<Label>("AgentName");
            if (agentLabel != null)
            {
                // Get the agent display name from AgentDefinition
                var agent = AgentDefinition.GetAgentById(player.AgentName?.ToLower() ?? "crimson");
                string agentDisplayName = agent?.displayName?.ToUpper() ?? player.AgentName?.ToUpper() ?? "CRIMSON";
                
                // Debug.Log($"ðŸ” [LobbyUI] Setting agent display name: AgentId='{player.AgentName}', AgentDisplayName='{agentDisplayName}', UserCharacterName='{player.CharacterName}'");
                agentLabel.text = agentDisplayName;
                agentLabel.style.fontSize = 13;
                agentLabel.style.color = player.Team == 0
                    ? new Color(1f, 0.37f, 0.20f, 1f)
                    : new Color(0f, 0.765f, 0.941f, 1f);
                agentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                agentLabel.style.marginBottom = 8;
                agentLabel.style.letterSpacing = 0.5f;
                agentLabel.style.whiteSpace = WhiteSpace.Normal;
            }

            // Character Icon - Same as agent cards but bigger (140x140 vs 120x120) 
            var characterIcon = root.Q<VisualElement>("CharacterIcon");
            if (characterIcon != null)
            {
                // Debug.Log($"ðŸ” [LobbyUI] CharacterIcon element found for player: {player.Username}");
                // Container styling - same as agent cards but bigger
                characterIcon.style.width = 140;
                characterIcon.style.height = 140;
                characterIcon.style.flexShrink = 0;   // never let layout squash the square
                characterIcon.style.marginTop = 4;
                characterIcon.style.marginBottom = 8;
                characterIcon.style.alignSelf = Align.Center;
                characterIcon.style.backgroundColor = new Color(0.078f, 0.117f, 0.176f, 0.8f); // rgba(20, 30, 45, 0.8)
                characterIcon.style.borderTopWidth = 3;
                characterIcon.style.borderBottomWidth = 3;
                characterIcon.style.borderLeftWidth = 3;
                characterIcon.style.borderRightWidth = 3;
                Color iconBorder = player.Team == 0
                    ? new Color(1f, 0.37f, 0.20f, 0.5f)
                    : new Color(0f, 0.765f, 0.941f, 0.5f);
                characterIcon.style.borderTopColor    = iconBorder;
                characterIcon.style.borderBottomColor = iconBorder;
                characterIcon.style.borderLeftColor   = iconBorder;
                characterIcon.style.borderRightColor  = iconBorder;
                characterIcon.style.overflow = Overflow.Hidden;
                
                // Load agent icon from Resources (same path as AgentDefinition)
                // Debug.Log($"ðŸ” [LobbyUI] Loading icon for player: AgentName='{player.AgentName}', looking up agent with id='{player.AgentName?.ToLower() ?? "crimson"}'");
                var agent = AgentDefinition.GetAgentById(player.AgentName?.ToLower() ?? "crimson");
                if (agent != null)
                {
                    // Debug.Log($"ðŸ” [LobbyUI] Agent found: {agent.displayName}, iconPath: {agent.iconPath}");
                    var iconTexture = Resources.Load<Texture2D>(agent.iconPath);
                    if (iconTexture != null)
                    {
                        // Inner element uses the SAME CSS class as agent cards:
                        // scale: 2.5; translate: 0 30px; -unity-background-scale-mode: scale-and-crop
                        characterIcon.Clear();
                        var iconElement = new VisualElement();
                        iconElement.AddToClassList("agent-card-icon");
                        iconElement.style.backgroundImage = new StyleBackground(iconTexture);
                        characterIcon.Add(iconElement);
                        // Debug.Log($"âœ… [LobbyUI] Loaded agent icon for {player.AgentName}: {agent.iconPath} - agent card style, bigger size");
                    }
                    else
                    {
                        // Debug.LogWarning($"âš ï¸ [LobbyUI] Could not load agent icon texture: {agent.iconPath}");
                    }
                }
                else
                {
                    // Debug.LogWarning($"âš ï¸ [LobbyUI] Agent not found for agentName: '{player.AgentName}' (lookup id: '{player.AgentName?.ToLower() ?? "crimson"}')");
                }
            }
            else
            {
                // Debug.LogWarning($"âš ï¸ [LobbyUI] CharacterIcon element not found in PlayerCard template for player: {player.Username}");
            }

            // Weapons Header
            var weaponsHeader = root.Q<Label>("WeaponsHeader");
            if (weaponsHeader != null)
            {
                weaponsHeader.text = "WEAPONS";
                weaponsHeader.style.fontSize = 8;
                weaponsHeader.style.color = new Color(0.5f, 0.5f, 0.55f, 1f);
                weaponsHeader.style.marginBottom = 4;
                weaponsHeader.style.marginTop = 2;
                weaponsHeader.style.letterSpacing = 0.5f;
            }
            
            // Primary Weapon
            var primaryLabel = root.Q<Label>("PrimaryWeapon");
            if (primaryLabel != null)
            {
                primaryLabel.text = $"{player.PrimaryWeapon?.ToUpper() ?? "TALON-AR"} // 1";
                primaryLabel.style.fontSize = 7;
                primaryLabel.style.color = new Color(0.86f, 0.86f, 0.88f, 1f);
                primaryLabel.style.marginBottom = 2;
                primaryLabel.style.whiteSpace = WhiteSpace.Normal;
            }
            
            // Secondary Weapon
            var secondaryLabel = root.Q<Label>("SecondaryWeapon");
            if (secondaryLabel != null)
            {
                secondaryLabel.text = $"{player.SecondaryWeapon?.ToUpper() ?? "BOLT"} // 2";
                secondaryLabel.style.fontSize = 7;
                secondaryLabel.style.color = new Color(0.6f, 0.6f, 0.65f, 1f);
                secondaryLabel.style.marginBottom = 6;
                secondaryLabel.style.whiteSpace = WhiteSpace.Normal;
            }

            // Level
            var levelLabel = root.Q<Label>("Level");
            if (levelLabel != null)
            {
                levelLabel.text = $"LVL {player.Level}";
                levelLabel.style.fontSize = 10;
                levelLabel.style.color = player.Team == 0
                    ? new Color(1f, 0.37f, 0.20f, 1f)
                    : new Color(0f, 0.765f, 0.941f, 1f);
                levelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                levelLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                levelLabel.style.marginTop = 18;
                levelLabel.style.marginBottom = 6;
            }

            return root;
        }
        
        private VisualElement CreateFallbackPlayerItem(PlayerInRoom player)
        {
            var container = new VisualElement();
            container.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
            container.style.paddingTop = new StyleLength(10);
            container.style.paddingBottom = new StyleLength(10);
            container.style.paddingLeft = new StyleLength(10);
            container.style.paddingRight = new StyleLength(10);
            container.style.marginBottom = new StyleLength(5);
            
            var nameLabel = new Label($"{player.Username} ({player.CharacterName})");
            nameLabel.style.color = Color.white;
            container.Add(nameLabel);
            
            if (player.IsHost)
            {
                var hostLabel = new Label("HOST");
                hostLabel.style.color = new Color(1f, 0.5f, 0f, 1f);
                container.Add(hostLabel);
            }
            
            return container;
        }
    }
    
    // Helper class for room players
    public class PlayerInRoom
    {
        public string Username { get; set; }
        public string PlayerName { get; set; }
        public string CharacterName { get; set; } // Nombre del personaje (ej: SEA)
        public string AgentName { get; set; } // Agente seleccionado (ej: CRIMSON)
        public bool IsLocal { get; set; }
        public bool IsHost { get; set; }
        public bool IsReady { get; set; }
        public int Team { get; set; } // 0 = Team A, 1 = Team B
        public int JoinOrder { get; set; }
        public int Level { get; set; }
        public string PrimaryWeapon { get; set; }
        public string SecondaryWeapon { get; set; }
    }
}


