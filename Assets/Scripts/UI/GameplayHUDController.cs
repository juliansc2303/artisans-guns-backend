using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Networking;
using System.Linq;
using System.Collections;
using System.Text;
using ArtisansGuns.Networking;
using ArtisansGuns.Auth;
using Fusion;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// Manages the in-game HUD - simplified to only show Settings overlay
    /// </summary>
    public class GameplayHUDController : MonoBehaviour
    {
        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;
        
        // Player Reference (auto-detected)
        private PlayerNetworkData localPlayer;
        
        // UI Elements - Settings Menu (delegated to SettingsUIController)
        private SettingsUIController settingsUIController;
        private Button settingsButton;
        
        // UI Elements - Scores Panel
        private Button scoresButton;
        private VisualElement scoresOverlay;
        private Button scoresCloseButton;
        private VisualElement teamAPlayerList;
        private VisualElement teamBPlayerList;
        
        // UI Elements - Top Scoreboard (live timer + team kills)
        private Label gameTimerLabel;
        private Label teamAScoreLabel;
        private Label teamBScoreLabel;
        private Label goalTextLabel;
        
        // UI Elements - Post-Match Overlay
        private VisualElement postMatchOverlay;
        private Label postMatchResult;
        private Label postMatchScoreA;
        private Label postMatchScoreB;
        private VisualElement postMatchTeamAList;
        private VisualElement postMatchTeamBList;
        private bool matchEndHandled = false;
        private Coroutine _postMatchCoroutine;
        
        // UI Elements - Phase 1 Ceremony (big text + slow-mo)
        private VisualElement ceremonyResultOverlay;
        private VisualElement ceremonyResultGlow;
        private Label ceremonyResultText;
        private Label ceremonyResultSub;
        
        // End-match camera (placed in scene, disabled by default)
        [Header("End Match Camera")]
        [Tooltip("Assign a disabled Camera in the scene that shows a cinematic map view")]
        [SerializeField] private Camera endMatchCamera;
        
        [Header("End Match Audio")]
        [SerializeField] private AudioClip victorySfx;
        [SerializeField] private AudioClip defeatSfx;
        
        // Backend URL for match-end API
        private const string BASE_URL = "https://ryvalen.onrender.com/api";
        private const int REQUEST_TIMEOUT = 120;
        
        // Game State
        private bool isPaused = false;
        private bool isScoresOpen = false;
        private float scoresRefreshTimer = 0f;
        private const float SCORES_REFRESH_INTERVAL = 0.25f; // Refresh 4x per second
        
        // Ceremony UI
        private VisualElement ceremonyBanner;
        private VisualElement ceremonyCountdownOverlay;
        private Label ceremonyStatus;
        private Label ceremonyCountdown;
        private Label ceremonyPlayers;
        private Label ceremonyGamemode;
        // Overlay inner labels (inside the fullscreen countdown)
        private Label ceremonyOverlayGamemode;
        private Label ceremonyOverlayStatus;
        private Label ceremonyOverlayPlayers;
        private bool ceremonyDismissed = false;
        private bool countdownRequested = false; // prevent sending RPC every frame
        private bool gsmDiagLogged = false; // one-shot diagnostic
        private bool teamClassApplied = false; // one-shot team color
        private const int MIN_PLAYERS_TO_START = 2;
        
        // Join / Leave Notifications
        private VisualElement notificationContainer;
        private bool eventsSubscribed = false;
        
        // FPS Counter
        private Label fpsLabel;
        private float fpsTimer;
        private int fpsFrameCount;
        private const float FPS_UPDATE_INTERVAL = 0.5f;
        
        private void Awake()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
            
            // Ensure game is running
            Time.timeScale = 1f;
            // NOTE: DO NOT change panelSettings.sortingOrder here.
            // PanelSettings is a shared ScriptableObject — mutating it at runtime changes
            // all UIDocuments that use it AND, when sortingOrder > UGUI Canvas order,
            // UIToolkit's input dispatcher claims all pointer events before UGUI's
            // EventSystem, breaking the joystick, fire button and every other UGUI widget.
            // The death overlay is now a UIToolkit VisualElement inserted inside this very
            // UIDocument (at index 0, behind the HUD buttons), which naturally renders
            // above the UGUI Canvas without touching any sortingOrder.
        }
        
        private void OnEnable()
        {
            // Find local player automatically
            FindLocalPlayer();
            
            var root = uiDocument.rootVisualElement;
            
            // CRITICAL: The UIDocument root VisualElement covers the entire screen with
            // PickingMode.Position by default. This causes it to absorb ALL pointer events
            // (touches/clicks) before they reach the UGUI EventSystem, breaking the joystick,
            // fire button, reload button, and every other UGUI widget on the Canvas.
            //
            // Setting the ROOT to Ignore makes the transparent backdrop a non-target for
            // hit-testing. Interactive children (Button, etc.) keep their own
            // PickingMode.Position so they still receive events normally.
            //
            // This does NOT depend on sortingOrder — even with UIToolkit at order 1 and
            // the UGUI Canvas at order 3, the root must be Ignore for UGUI to work.
            root.pickingMode = PickingMode.Ignore;
            Debug.Log($"[GameplayHUD] OnEnable: root.pickingMode set to Ignore on '{gameObject.name}'");

            // Initialise mobile controls into this shared UIDocument root.
            MobileControlsController.Instance?.InitializeWithRoot(root);

            // Cache Settings Button (Top Right)
            settingsButton = root.Q<Button>("SettingsButton");
            
            // FPS Counter — positioned above the settings button
            fpsLabel = new Label("-- FPS");
            fpsLabel.pickingMode = PickingMode.Ignore;
            fpsLabel.style.position = Position.Absolute;
            fpsLabel.style.top = 20;
            fpsLabel.style.right = 30;
            fpsLabel.style.fontSize = 14;
            fpsLabel.style.color = new UnityEngine.Color(0f, 1f, 0f, 0.85f);
            fpsLabel.style.unityTextAlign = UnityEngine.TextAnchor.MiddleRight;
            fpsLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            // Insert FPS label near SettingsButton so overlay panels render on top of it
            var rootContainer = root.Q<VisualElement>("Root");
            if (rootContainer != null && settingsButton != null)
            {
                int idx = rootContainer.IndexOf(settingsButton);
                rootContainer.Insert(idx + 1, fpsLabel);
            }
            else
            {
                root.Add(fpsLabel);
            }
            
            // Cache Scores Panel
            scoresButton = root.Q<Button>("ScoresButton");
            scoresOverlay = root.Q<VisualElement>("ScoresOverlay");
            scoresCloseButton = root.Q<Button>("ScoresCloseButton");
            teamAPlayerList = root.Q<VisualElement>("TeamAPlayerList");
            teamBPlayerList = root.Q<VisualElement>("TeamBPlayerList");
            
            // Cache Ceremony elements
            ceremonyBanner = root.Q<VisualElement>("CeremonyBanner");
            ceremonyCountdownOverlay = root.Q<VisualElement>("CeremonyCountdownOverlay");
            ceremonyStatus = root.Q<Label>("CeremonyStatus");
            ceremonyCountdown = root.Q<Label>("CeremonyCountdown");
            ceremonyPlayers = root.Q<Label>("CeremonyPlayers");
            ceremonyGamemode = root.Q<Label>("CeremonyGamemode");
            ceremonyOverlayGamemode = root.Q<Label>("CeremonyOverlayGamemode");
            ceremonyOverlayStatus = root.Q<Label>("CeremonyOverlayStatus");
            ceremonyOverlayPlayers = root.Q<Label>("CeremonyOverlayPlayers");
            ceremonyDismissed = false;
            countdownRequested = false;
            teamClassApplied = false;
            
            // Cache notification container
            notificationContainer = root.Q<VisualElement>("NotificationContainer");
            
            // Cache top scoreboard labels
            gameTimerLabel = root.Q<Label>("GameTimer");
            teamAScoreLabel = root.Q<Label>("TeamAScore");
            teamBScoreLabel = root.Q<Label>("TeamBScore");
            goalTextLabel = root.Q<Label>("GoalText");
            if (goalTextLabel != null) goalTextLabel.text = "KILLS";
            
            // Cache post-match overlay
            postMatchOverlay = root.Q<VisualElement>("PostMatchOverlay");
            postMatchResult = root.Q<Label>("PostMatchResult");
            postMatchScoreA = root.Q<Label>("PostMatchScoreA");
            postMatchScoreB = root.Q<Label>("PostMatchScoreB");
            postMatchTeamAList = root.Q<VisualElement>("PostMatchTeamAList");
            postMatchTeamBList = root.Q<VisualElement>("PostMatchTeamBList");
            matchEndHandled = false;
            
            // Cache Phase 1 ceremony result overlay
            ceremonyResultOverlay = root.Q<VisualElement>("CeremonyResultOverlay");
            ceremonyResultGlow = root.Q<VisualElement>("CeremonyResultGlow");
            ceremonyResultText = root.Q<Label>("CeremonyResultText");
            ceremonyResultSub = root.Q<Label>("CeremonyResultSub");
            
            // Auto-find EndMatchCamera if not assigned in inspector
            if (endMatchCamera == null)
            {
                // GameObject.Find cannot find inactive GameObjects.
                // Use FindObjectsByType with Include flag to search inactive cameras too.
                var allCams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var cam in allCams)
                {
                    if (cam.gameObject.name == "EndMatchCamera")
                    {
                        endMatchCamera = cam;
                        break;
                    }
                }
                if (endMatchCamera != null)
                    Debug.Log("[HUD] Auto-found EndMatchCamera (was inactive)");
            }
            
            // Subscribe to join/leave events
            SubscribeToNetworkEvents();
            
            // Ensure InputFrozen starts off
            ArtisansGuns.Game.PlayerController.InputFrozen = false;
            
            // Initialize Settings Panel with unified SettingsUIController
            settingsUIController = GetComponent<SettingsUIController>();
            if (settingsUIController == null)
            {
                settingsUIController = gameObject.AddComponent<SettingsUIController>();
            }
            settingsUIController.FindSettingsPanelElements(root);
            
            // Subscribe to settings panel close event for cursor management
            settingsUIController.OnSettingsPanelClosed += OnSettingsPanelClosed;
            
            // Register settings button to open unified settings panel
            settingsButton?.RegisterCallback<ClickEvent>(evt => 
            {
                if (settingsUIController != null)
                {
                    settingsUIController.ShowSettings();
                    isPaused = true;
                    UnityEngine.Cursor.lockState = CursorLockMode.None;
                    UnityEngine.Cursor.visible = true;
                }
            });
            
            // Register scores button
            scoresButton?.RegisterCallback<ClickEvent>(evt => OpenScoresPanel());
            scoresCloseButton?.RegisterCallback<ClickEvent>(evt => CloseScoresPanel());
            
            // Initialize scores overlay as hidden
            if (scoresOverlay != null)
            {
                scoresOverlay.AddToClassList("hidden");
            }
        }
        
        /// <summary>
        /// Called when settings panel is closed (via close button or logout)
        /// </summary>
        private void OnSettingsPanelClosed()
        {
            isPaused = false;
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
        }
        
        private System.Collections.IEnumerator Start()
        {
            // Wait 2 frames for all DontDestroyOnLoad managers (PersistentUIManager,
            // CrosshairManager, KillFeedManager) to finish their Awake/Start/OnSceneLoaded,
            // then fix input layering between UIToolkit and UGUI Canvas.
            //
            // ROOT CAUSE: Unity's input dispatcher routes pointer events to the panel(s)
            // with the HIGHEST sortingOrder first. If any active UIDocument PanelSettings
            // has sortingOrder ≥ Canvas sortingOrder, UIToolkit consumes the event and
            // the UGUI Canvas (joystick / fire / reload) never sees it — even with
            // root.pickingMode = Ignore on the VisualElement side.
            //
            // FIX: after all singletons settle, read the maximum PanelSettings.sortingOrder
            // across ALL active UIDocuments, then bump every Screen-Space-Overlay Canvas
            // sortingOrder to max+1 so UGUI always wins input dispatch.
            // UIToolkit still renders its HUD visually (HUD elements show through the
            // transparent Canvas background) — only input priority changes.
            yield return null;
            yield return null;

            // Step 1: set root.pickingMode = Ignore on all UIDocument roots (belt + suspenders)
            // and track the highest PanelSettings sortingOrder in use.
            int maxPanelOrder = 0;
            var allDocs = FindObjectsOfType<UIDocument>();
            foreach (var doc in allDocs)
            {
                var r = doc.rootVisualElement;
                if (r == null) continue;
                if (r.pickingMode != PickingMode.Ignore)
                {
                    Debug.LogWarning($"[HUD] UIDoc '{doc.gameObject.name}' root was {r.pickingMode} → forcing Ignore");
                    r.pickingMode = PickingMode.Ignore;
                }
                if (doc.panelSettings != null)
                    maxPanelOrder = Mathf.Max(maxPanelOrder, (int)doc.panelSettings.sortingOrder);
            }

            // Step 2: ensure every UGUI Screen-Space-Overlay Canvas has sortingOrder > all UIToolkit panels
            // so UGUI receives pointer events first.
            var allCanvases = FindObjectsOfType<UnityEngine.Canvas>();
            foreach (var canvas in allCanvases)
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.sortingOrder <= maxPanelOrder)
                {
                    int newOrder = maxPanelOrder + 1;
                    Debug.LogWarning($"[HUD] Canvas '{canvas.gameObject.name}' sortingOrder={canvas.sortingOrder} ≤ UIToolkit max={maxPanelOrder} → bumping to {newOrder}");
                    canvas.sortingOrder = newOrder;
                }
            }
        }

        private void Update()
        {
            // Re-check for local player if not found yet
            if (localPlayer == null)
            {
                FindLocalPlayer();
            }
            
            // Retry event subscription if NetworkManager wasn't ready in OnEnable
            if (!eventsSubscribed) SubscribeToNetworkEvents();

            // Update FPS counter
            fpsFrameCount++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= FPS_UPDATE_INTERVAL)
            {
                int fps = Mathf.RoundToInt(fpsFrameCount / fpsTimer);
                if (fpsLabel != null) fpsLabel.text = $"{fps} FPS";
                fpsFrameCount = 0;
                fpsTimer = 0f;
            }
            
            // Update live scoreboard (timer + team kills)
            UpdateLiveScoreboard();
            
            // Update ceremony overlay
            UpdateCeremony();
            
            // Handle match end
            UpdateMatchEnd();

            // Real-time scoreboard refresh while open
            if (isScoresOpen)
            {
                scoresRefreshTimer -= Time.deltaTime;
                if (scoresRefreshTimer <= 0f)
                {
                    scoresRefreshTimer = SCORES_REFRESH_INTERVAL;
                    PopulateScoresPanel();
                }
            }
        }

        /// <summary>
        /// Manages the pre-match ceremony.
        /// Phase 1 (warmup): Top banner "WAITING FOR PLAYERS 1/2" — player can move freely.
        /// Phase 2 (countdown): 3-2-1 center text, controls frozen, all players reset.
        /// Phase 3 (playing): Banner hidden, normal match.
        /// </summary>
        private void UpdateCeremony()
        {
            if (ceremonyDismissed) return;

            // Don't process ceremony while the loading screen is still up —
            // the local player isn't ready yet (may still be below the map).
            if (ArtisansGuns.Loading.PreWarmManager.Instance != null
                && ArtisansGuns.Loading.PreWarmManager.Instance.IsLoading)
                return;

            var gsm = GameStateManager.Instance;
            
            // Fallback: if singleton lost (e.g. scene transition), try to re-find it
            if (gsm == null)
            {
                gsm = FindObjectOfType<GameStateManager>();
                if (gsm != null)
                {
                    Debug.Log("[Ceremony] GSM Instance was null — recovered via FindObjectOfType");
                    GameStateManager.Instance = gsm;
                }
            }
            
            // Guard: if GSM's network object was despawned, bail out
            if (gsm != null && (gsm.Object == null || !gsm.Object.IsValid)) return;
            
            // ── Match ended → don't touch ceremony, post-match overlay handles it ──
            if (gsm != null && gsm.MatchEnded)
            {
                if (ceremonyBanner != null) ceremonyBanner.AddToClassList("hidden");
                if (ceremonyCountdownOverlay != null) ceremonyCountdownOverlay.AddToClassList("hidden");
                return;
            }
            
            // ── Match already in progress → hide everything, unfreeze ──
            if (gsm != null && gsm.GameInProgress)
            {
                if (ceremonyBanner != null) ceremonyBanner.AddToClassList("hidden");
                if (ceremonyCountdownOverlay != null)
                {
                    ceremonyCountdownOverlay.AddToClassList("hidden");
                    ceremonyCountdownOverlay.RemoveFromClassList("team-a-overlay");
                    ceremonyCountdownOverlay.RemoveFromClassList("team-b-overlay");
                }
                ArtisansGuns.Game.PlayerController.InputFrozen = false;
                ceremonyDismissed = true;
                return;
            }
            
            int playerCount = NetworkManager.Instance != null ? NetworkManager.Instance.GetPlayerCount() : 0;

            // ── Countdown is running (3-2-1 freeze) ── 
            if (gsm != null && gsm.CountdownStarted && gsm.CountdownValue >= 0)
            {
                // Freeze all input
                ArtisansGuns.Game.PlayerController.InputFrozen = true;
                
                // Hide the small banner — the full-screen overlay replaces it
                if (ceremonyBanner != null) ceremonyBanner.AddToClassList("hidden");
                
                // Apply team color class (retry each frame until localPlayer is available AND TeamAssigned is true)
                if (!teamClassApplied && ceremonyCountdownOverlay != null)
                {
                    // Re-check localPlayer in case it wasn't found yet
                    if (localPlayer == null) FindLocalPlayer();
                    
                    if (localPlayer != null && localPlayer.TeamAssigned)
                    {
                        teamClassApplied = true;
                        int localTeam = localPlayer.Team;
                        string teamClass = localTeam == 0 ? "team-a-overlay" : "team-b-overlay";
                        ceremonyCountdownOverlay.AddToClassList(teamClass);
                        Debug.Log($"[Ceremony] Applied overlay team class: {teamClass} (team={localTeam}, player={localPlayer.Username})");
                    }
                    else if (localPlayer != null && !localPlayer.TeamAssigned)
                    {
                        Debug.Log($"[Ceremony] Waiting for TeamAssigned (player={localPlayer.Username}, Team={localPlayer.Team})");
                    }
                }
                
                // Show countdown overlay with big number
                if (ceremonyCountdownOverlay != null) ceremonyCountdownOverlay.RemoveFromClassList("hidden");
                if (ceremonyCountdown != null) ceremonyCountdown.text = gsm.CountdownValue > 0 ? gsm.CountdownValue.ToString() : "GO!";
                
                // Update overlay inner labels
                if (ceremonyOverlayStatus != null) ceremonyOverlayStatus.text = "MATCH STARTING";
                if (ceremonyOverlayPlayers != null) ceremonyOverlayPlayers.text = $"{playerCount} / {MIN_PLAYERS_TO_START} PLAYERS";
                
                return;
            }
            
            // ── Pre-start warm-up (12 s) → banner + timer, free movement ──
            if (gsm != null && gsm.PreStartActive)
            {
                if (ceremonyBanner != null) ceremonyBanner.RemoveFromClassList("hidden");
                if (ceremonyCountdownOverlay != null) ceremonyCountdownOverlay.AddToClassList("hidden");
                if (ceremonyStatus != null) ceremonyStatus.text = $"STARTING IN {gsm.PreStartSecondsLeft}...";
                if (ceremonyPlayers != null) ceremonyPlayers.text = $"{playerCount} / {MIN_PLAYERS_TO_START} PLAYERS";
                ArtisansGuns.Game.PlayerController.InputFrozen = false;
                return;
            }

            // ── Enough players → trigger pre-start sequence (any client, guarded against double-start) ──
            if (playerCount >= MIN_PLAYERS_TO_START)
            {
                if (ceremonyBanner != null) ceremonyBanner.RemoveFromClassList("hidden");
                if (ceremonyStatus != null) ceremonyStatus.text = "GET READY...";
                if (ceremonyPlayers != null) ceremonyPlayers.text = $"{playerCount} / {MIN_PLAYERS_TO_START} PLAYERS";

                // One-shot diagnostic: log GSM state when we first have enough players
                if (!gsmDiagLogged)
                {
                    gsmDiagLogged = true;
                    Debug.Log($"[Ceremony] Enough players ({playerCount}) — gsm={(gsm != null ? "OK" : "NULL")} " +
                              $"countdownRequested={countdownRequested}");
                    if (gsm != null)
                        Debug.Log($"[Ceremony] GSM state: CountdownStarted={gsm.CountdownStarted} GameInProgress={gsm.GameInProgress} PreStartActive={gsm.PreStartActive} HasStateAuth={gsm.HasStateAuthority}");
                }
                
                if (gsm != null && !gsm.PreStartActive && !gsm.CountdownStarted && !gsm.GameInProgress && !countdownRequested)
                {
                    countdownRequested = true;
                    
                    if (gsm.HasStateAuthority)
                    {
                        // We ARE the authority — call directly (avoids Fusion self-RPC edge case)
                        Debug.Log("[Ceremony] Local client IS GSM authority — starting pre-start directly");
                        gsm.BeginPreStartSequence();
                    }
                    else
                    {
                        // Route to whoever has StateAuthority via RPC
                        Debug.Log("[Ceremony] Sending RPC_BeginCountdownSequence to GSM authority");
                        gsm.RPC_BeginCountdownSequence();
                    }
                }
                return;
            }
            
            // ── Warmup: waiting for players, free movement ──
            if (ceremonyBanner != null) ceremonyBanner.RemoveFromClassList("hidden");
            if (ceremonyCountdownOverlay != null) ceremonyCountdownOverlay.AddToClassList("hidden");
            if (ceremonyStatus != null) ceremonyStatus.text = "WAITING FOR PLAYERS...";
            if (ceremonyPlayers != null) ceremonyPlayers.text = $"{playerCount} / {MIN_PLAYERS_TO_START} PLAYERS";
            ArtisansGuns.Game.PlayerController.InputFrozen = false;
        }
        
        private void FindLocalPlayer()
        {
            // Find the player with input authority (our local player)
            var allPlayers = FindObjectsOfType<PlayerNetworkData>();
            foreach (var player in allPlayers)
            {
                if (player.Object != null && player.Object.HasInputAuthority)
                {
                    localPlayer = player;
                    // Debug.Log($"âœ… Local player found: {player.Username}");
                    return;
                }
            }
        }
        
        // ===================================
        // SETTINGS MENU
        // ===================================
        
        // Settings are now handled by unified SettingsUIController integrated in OnEnable()
        
        private void ExitGame()
        {
            // Debug.Log("ðŸšª Exiting GameScene and returning to Room lobby...");
            Time.timeScale = 1f;
            
            if (NetworkManager.Instance != null && NetworkManager.Instance.Runner != null)
            {
                var runner = NetworkManager.Instance.Runner;
                
                // 1. Mark ourselves as NOT in game anymore
                if (localPlayer != null)
                {
                    localPlayer.InGame = false;
                    localPlayer.IsReady = false;
                    // Debug.Log($"ðŸ”„ Marked {localPlayer.Username} as not in game and not ready");
                }
                
                // 2. Find and despawn our PlayerController (3D avatar in GameScene)
                var ourController = FindObjectsOfType<ArtisansGuns.Game.PlayerController>()
                    .FirstOrDefault(pc => pc.Object != null && pc.Object.HasInputAuthority);
                
                if (ourController != null && ourController.Object != null)
                {
                    // Debug.Log($"ðŸ—‘ï¸ Despawning PlayerController for {localPlayer?.Username ?? "local player"}");
                    runner.Despawn(ourController.Object);
                }
                
                // 3. Load LobbyScene while KEEPING runner connected
                // PlayerNetworkData persists (DontDestroyOnLoad), we stay in the same session
                // Debug.Log("ðŸ  Loading LobbyScene (staying connected to session)...");
                UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
                
                // Debug.Log("âœ… Returned to lobby - still in same room, can rejoin by clicking READY");
            }
            else
            {
                // Debug.LogError("âŒ NetworkManager or Runner not found!");
                // Fallback: just load lobby
                UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
            }
        }
        
        private void EndGameTest()
        {
            // Debug.Log("ðŸ§ª TEST: Ending game and returning all players to lobby...");
            Time.timeScale = 1f;
            
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.RPC_EndGameForAll();
            }
            else
            {
                // Debug.LogError("âŒ GameStateManager not found!");
            }
        }        
        // ===================================
        // SCORES PANEL
        // ===================================
        
        private void OpenScoresPanel()
        {
            if (scoresOverlay == null)
            {
                Debug.LogWarning("[Scoreboard] scoresOverlay is null — cannot open");
                return;
            }
            
            // Show panel first so it appears even if population fails
            scoresOverlay.RemoveFromClassList("hidden");
            isScoresOpen = true;
            
            try { PopulateScoresPanel(); }
            catch (System.Exception e) { Debug.LogError("[Scoreboard] PopulateScoresPanel threw: " + e); }
            scoresRefreshTimer = SCORES_REFRESH_INTERVAL;
            
            // Show cursor for UI interaction
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }
        
        private void CloseScoresPanel()
        {
            if (scoresOverlay == null) return;
            
            scoresOverlay.AddToClassList("hidden");
            isScoresOpen = false;
            
            // Return cursor to game state (only if settings not open)
            if (!isPaused)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            }
        }
        
        private void PopulateScoresPanel()
        {
            if (teamAPlayerList == null || teamBPlayerList == null) return;
            
            teamAPlayerList.Clear();
            teamBPlayerList.Clear();
            
            // Add K/D column headers to each team
            teamAPlayerList.Add(BuildKDHeaderRow());
            teamBPlayerList.Add(BuildKDHeaderRow());
            
            // Gather all players from network cache
            var allPlayers = FindObjectsOfType<PlayerNetworkData>();
            
            foreach (var player in allPlayers)
            {
                if (player.Object == null) continue;
                
                // Skip players whose team hasn't been assigned yet (avoids
                // showing everyone in Team A during the 1-second assignment delay)
                if (!player.TeamAssigned) continue;
                
                bool isLocal = player.Object.HasInputAuthority;
                var row = BuildPlayerRow(
                    player.CharacterName.ToString(),
                    player.SelectedAgent.ToString(),
                    player.Kills,
                    player.Deaths,
                    player.Headshots,
                    player.BestStreak,
                    isLocal,
                    player.Team
                );
                
                if (player.Team == 0)
                    teamAPlayerList.Add(row);
                else
                    teamBPlayerList.Add(row);
            }
        }
        
        private VisualElement BuildPlayerRow(string characterName, string agent, int kills, int deaths, int headshots, int bestStreak, bool isLocal, int team = 0)
        {
            // Row container
            var row = new VisualElement();
            row.AddToClassList("scores-player-row");
            if (isLocal)
                row.AddToClassList(team == 0 ? "scores-player-row-local-a" : "scores-player-row-local-b");
            else
                row.AddToClassList(team == 0 ? "scores-row-team-a" : "scores-row-team-b");
            
            // ── Agent portrait (real icon texture, zoomed & cropped like Valorant) ──
            var icon = new VisualElement();
            icon.AddToClassList("scores-agent-icon");

            // Try to load the agent's real icon from AgentDefinition
            var agentData = ArtisansGuns.Data.AgentDefinition.GetAgentById(
                string.IsNullOrEmpty(agent) ? "crimson" : agent.ToLower());
            
            Texture2D portrait = null;
            if (agentData != null && !string.IsNullOrEmpty(agentData.iconPath))
                portrait = UnityEngine.Resources.Load<Texture2D>(agentData.iconPath);

            if (portrait != null)
            {
                // Inner element slightly oversized so the portrait fills and crops naturally
                var portraitInner = new VisualElement();
                portraitInner.AddToClassList("scores-agent-portrait-inner");
                portraitInner.style.backgroundImage = new UnityEngine.UIElements.StyleBackground(portrait);
                icon.Add(portraitInner);
                icon.style.backgroundColor = new UnityEngine.Color(0.12f, 0.08f, 0.16f, 1f);
            }
            else
            {
                // Fallback: agent-colored square with initial letter
                SetAgentIconColor(icon, agent);
                var initial = new Label(string.IsNullOrEmpty(agent) ? "?" : agent.Substring(0, 1).ToUpper());
                initial.AddToClassList("scores-agent-initial");
                icon.Add(initial);
            }
            
            // Character name
            var nameLabel = new Label(string.IsNullOrEmpty(characterName) ? "---" : characterName.ToUpper());
            nameLabel.AddToClassList("scores-player-name");
            
            // K/D container with explicit labels
            var kd = new VisualElement();
            kd.AddToClassList("scores-kd-container");
            
            var killsLabel = new Label(kills.ToString());
            killsLabel.AddToClassList("scores-kills");
            killsLabel.tooltip = "Kills";
            
            var sep = new Label("/");
            sep.AddToClassList("scores-separator");
            
            var deathsLabel = new Label(deaths.ToString());
            deathsLabel.AddToClassList("scores-deaths");
            deathsLabel.tooltip = "Deaths";
            
            kd.Add(killsLabel);
            kd.Add(sep);
            kd.Add(deathsLabel);
            
            // Headshots
            var hsLabel = new Label(headshots.ToString());
            hsLabel.AddToClassList("scores-stat-cell");
            hsLabel.tooltip = "Headshots";
            
            // Best Streak
            var streakLabel = new Label(bestStreak.ToString());
            streakLabel.AddToClassList("scores-stat-cell");
            streakLabel.tooltip = "Best Streak";
            
            row.Add(icon);
            row.Add(nameLabel);
            row.Add(kd);
            row.Add(hsLabel);
            row.Add(streakLabel);
            
            return row;
        }
        
        private void SetAgentIconColor(VisualElement icon, string agent)
        {
            // Color-code by agent type
            switch (agent?.ToUpper())
            {
                case "CRIMSON":
                    icon.style.backgroundColor = new UnityEngine.Color(0.55f, 0.1f, 0.1f, 0.9f);
                    break;
                case "VIBE":
                    icon.style.backgroundColor = new UnityEngine.Color(0.1f, 0.4f, 0.6f, 0.9f);
                    break;
                case "SIGHT":
                    icon.style.backgroundColor = new UnityEngine.Color(0.1f, 0.5f, 0.3f, 0.9f);
                    break;
                case "PATO":
                    icon.style.backgroundColor = new UnityEngine.Color(0.5f, 0.4f, 0.05f, 0.9f);
                    break;
                default:
                    icon.style.backgroundColor = new UnityEngine.Color(0.2f, 0.2f, 0.3f, 0.9f);
                    break;
            }
        }
        
        private VisualElement BuildKDHeaderRow()
        {
            var header = new VisualElement();
            header.AddToClassList("scores-player-row");
            header.style.justifyContent = Justify.FlexEnd;
            header.style.paddingRight = 4;
            header.style.marginBottom = 4;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = new UnityEngine.Color(1f, 1f, 1f, 0.15f);
            
            var kdHeader = new VisualElement();
            kdHeader.AddToClassList("scores-kd-container");
            
            var kLabel = new Label("K");
            kLabel.AddToClassList("scores-kills");
            kLabel.style.fontSize = 11;
            kLabel.style.color = new UnityEngine.Color(1f, 1f, 1f, 0.5f);
            
            var sepLabel = new Label("/");
            sepLabel.AddToClassList("scores-separator");
            
            var dLabel = new Label("D");
            dLabel.AddToClassList("scores-deaths");
            dLabel.style.fontSize = 11;
            dLabel.style.color = new UnityEngine.Color(1f, 1f, 1f, 0.5f);
            
            kdHeader.Add(kLabel);
            kdHeader.Add(sepLabel);
            kdHeader.Add(dLabel);
            
            var hsHeader = new Label("HS");
            hsHeader.AddToClassList("scores-stat-cell");
            hsHeader.style.fontSize = 11;
            hsHeader.style.color = new UnityEngine.Color(1f, 1f, 1f, 0.5f);
            
            var streakHeader = new Label("STR");
            streakHeader.AddToClassList("scores-stat-cell");
            streakHeader.style.fontSize = 11;
            streakHeader.style.color = new UnityEngine.Color(1f, 1f, 1f, 0.5f);
            
            header.Add(kdHeader);
            header.Add(hsHeader);
            header.Add(streakHeader);
            return header;
        }
        
        // ===================================
        // JOIN / LEAVE NOTIFICATIONS
        // ===================================
        
        private void SubscribeToNetworkEvents()
        {
            if (eventsSubscribed) return;
            if (NetworkManager.Instance == null) return;
            
            NetworkManager.Instance.OnPlayerJoinedRoom += OnNetworkPlayerJoined;
            NetworkManager.Instance.OnPlayerLeftRoom += OnNetworkPlayerLeft;
            eventsSubscribed = true;
        }
        
        private void UnsubscribeFromNetworkEvents()
        {
            if (!eventsSubscribed) return;
            if (NetworkManager.Instance == null) return;
            
            NetworkManager.Instance.OnPlayerJoinedRoom -= OnNetworkPlayerJoined;
            NetworkManager.Instance.OnPlayerLeftRoom -= OnNetworkPlayerLeft;
            eventsSubscribed = false;
        }
        
        private void OnNetworkPlayerJoined(PlayerRef player, Fusion.NetworkObject obj)
        {
            // Skip local player — you already know you joined
            var runner = NetworkManager.Instance?.Runner;
            if (runner != null && player == runner.LocalPlayer) return;
            
            // The PlayerNetworkData may not exist yet (Shared Mode).
            // Poll for up to 3 s until we can read their name.
            StartCoroutine(ShowJoinNotificationDelayed(player));
        }
        
        private IEnumerator ShowJoinNotificationDelayed(PlayerRef player)
        {
            string playerName = null;
            for (int i = 0; i < 40; i++) // up to 8 s (name needs Spawn → RPC → replicate)
            {
                var pd = FindObjectsOfType<PlayerNetworkData>()
                    .FirstOrDefault(p => p.Object != null && p.Object.InputAuthority == player);
                if (pd != null)
                {
                    // Try CharacterName first, then Username as fallback
                    string n = pd.CharacterName.ToString();
                    if (string.IsNullOrEmpty(n))
                        n = pd.Username.ToString();
                    if (!string.IsNullOrEmpty(n))
                    {
                        playerName = n;
                        break;
                    }
                }
                yield return new WaitForSeconds(0.2f);
            }
            
            ShowNotification($"{playerName ?? "A PLAYER"} JOINED THE ROOM");
        }
        
        private void OnNetworkPlayerLeft(PlayerRef player)
        {
            var runner = NetworkManager.Instance?.Runner;
            if (runner != null && player == runner.LocalPlayer) return;
            
            string playerName = null;
            
            // Try the live NetworkObject first (may already be invalid)
            var pd = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(p => p.Object != null && p.Object.InputAuthority == player);
            if (pd != null)
            {
                playerName = pd.CharacterName.ToString();
                if (string.IsNullOrEmpty(playerName))
                    playerName = pd.Username.ToString();
            }
            
            // Fallback: read from the static PlayerCache (survives despawn)
            if (string.IsNullOrEmpty(playerName) &&
                PlayerNetworkData.PlayerCache.TryGetValue(player, out var cached))
            {
                playerName = cached.CharacterName;
                if (string.IsNullOrEmpty(playerName))
                    playerName = cached.Username;
            }
            
            ShowNotification($"{(string.IsNullOrEmpty(playerName) ? "A PLAYER" : playerName)} LEFT THE ROOM");
        }
        
        private void ShowNotification(string text)
        {
            if (notificationContainer == null) return;
            
            var label = new Label(text);
            label.AddToClassList("notification-label");
            label.pickingMode = PickingMode.Ignore;
            notificationContainer.Add(label);
            
            // Limit visible notifications (remove oldest if > 4)
            while (notificationContainer.childCount > 4)
                notificationContainer.RemoveAt(0);
            
            StartCoroutine(FadeAndRemoveNotification(label, 4f));
        }
        
        private IEnumerator FadeAndRemoveNotification(Label label, float totalSeconds)
        {
            yield return new WaitForSeconds(totalSeconds - 0.7f);
            label.AddToClassList("fade-out");
            yield return new WaitForSeconds(0.7f);
            notificationContainer?.Remove(label);
        }
        
        // ===================================
        // LIVE SCOREBOARD (Timer + Team Kills)
        // ===================================
        
        private void UpdateLiveScoreboard()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.Object == null || !gsm.Object.IsValid) return;
            
            // Update timer
            if (gameTimerLabel != null)
            {
                int seconds = gsm.MatchTimeRemaining;
                if (gsm.GameInProgress || gsm.MatchEnded)
                {
                    int min = seconds / 60;
                    int sec = seconds % 60;
                    gameTimerLabel.text = $"{min}:{sec:D2}";
                }
                else
                {
                    gameTimerLabel.text = "--:--";
                }
            }
            
            // Update team kill scores
            if (teamAScoreLabel != null || teamBScoreLabel != null)
            {
                var (teamAKills, teamBKills) = gsm.GetTeamKills();
                if (teamAScoreLabel != null) teamAScoreLabel.text = teamAKills.ToString();
                if (teamBScoreLabel != null) teamBScoreLabel.text = teamBKills.ToString();
            }
        }
        
        // ===================================
        // POST-MATCH — Two-Phase Ceremony
        // Phase 1 (6s): Slow-mo + big VICTORIA/DERROTA text + sound
        // Phase 2 (6s): EndMatchCamera + scoreboard with rewards
        // ===================================
        
        private const float PHASE1_DURATION = 6f;
        private const float PHASE2_DURATION = 6f;
        private const float SLOWMO_TIMESCALE = 0.15f;
        
        private void UpdateMatchEnd()
        {
            var gsm = GameStateManager.Instance;
            if (gsm == null || gsm.Object == null || !gsm.Object.IsValid || !gsm.MatchEnded || matchEndHandled) return;
            
            matchEndHandled = true;
            _postMatchCoroutine = StartCoroutine(MatchEndCeremony());
        }
        
        /// <summary>
        /// Full end-match ceremony coroutine.
        /// Phase 1: freeze input, slow-mo, big result text with audio.
        /// Phase 2: normal time, cinematic camera, full scoreboard overlay.
        /// Then transition to lobby.
        /// </summary>
        private IEnumerator MatchEndCeremony()
        {
            var gsm = GameStateManager.Instance;
            
            // ══════════════════════════════════════════════════════════
            // PHASE 1 — Slow-mo + big result text (6 realtime seconds)
            // ══════════════════════════════════════════════════════════
            
            // Freeze input immediately
            ArtisansGuns.Game.PlayerController.InputFrozen = true;
            
            // Hide all mobile controls
            MobileControlsController.Instance?.HideAllControls();
            
            // Mute all game audio
            AudioListener.volume = 0f;
            
            // Slow motion
            Time.timeScale = SLOWMO_TIMESCALE;
            Time.fixedDeltaTime = 0.02f * SLOWMO_TIMESCALE;
            
            // Determine result
            string resultText = "MATCH OVER";
            string resultClass = "ceremony-draw";
            bool isVictory = false;
            if (localPlayer != null && localPlayer.TeamAssigned)
            {
                int myTeam = localPlayer.Team;
                byte result = gsm != null ? gsm.MatchResult : (byte)0;
                if (result == 3)
                {
                    resultText = "EMPATE";
                    resultClass = "ceremony-draw";
                }
                else if ((result == 1 && myTeam == 0) || (result == 2 && myTeam == 1))
                {
                    resultText = "Victory";
                    resultClass = "ceremony-victory";
                    isVictory = true;
                }
                else
                {
                    resultText = "Defeat";
                    resultClass = "ceremony-defeat";
                }
            }
            
            // Score line
            var (teamAKills, teamBKills) = gsm != null && gsm.Object != null && gsm.Object.IsValid ? gsm.GetTeamKills() : (0, 0);
            
            // Show Phase 1 overlay
            if (ceremonyResultOverlay != null)
            {
                // Hide all HUD children except our overlay
                var hudRoot = uiDocument.rootVisualElement.Q<VisualElement>("Root");
                if (hudRoot != null)
                {
                    foreach (var child in hudRoot.Children())
                    {
                        if (child != ceremonyResultOverlay && child != postMatchOverlay)
                            child.AddToClassList("hidden");
                    }
                }
                
                // Set text content
                if (ceremonyResultText != null) ceremonyResultText.text = resultText;
                if (ceremonyResultSub != null) ceremonyResultSub.text = $"{teamAKills}  —  {teamBKills}";
                
                // Reveal overlay + apply result class (triggers CSS transitions)
                ceremonyResultOverlay.RemoveFromClassList("hidden");
                ceremonyResultOverlay.AddToClassList(resultClass);
                
                // Trigger animated appearance on next frame (so USS transition activates)
                yield return null;
                ceremonyResultOverlay.AddToClassList("ceremony-visible");
            }
            
            // Play ceremony sound (bypasses AudioListener.volume mute)
            PlayCeremonySfx(isVictory);
            
            Debug.Log($"[HUD] Phase 1 — {resultText} (slow-mo, TeamA:{teamAKills} vs TeamB:{teamBKills})");
            
            // Wait 6 REALTIME seconds (Time.timeScale doesn't affect WaitForSecondsRealtime)
            yield return new WaitForSecondsRealtime(PHASE1_DURATION);
            
            // ══════════════════════════════════════════════════════════
            // PHASE 2 — Cinematic camera + full scoreboard (6 seconds)
            // ══════════════════════════════════════════════════════════
            
            // Restore normal time
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            
            // Restore global audio (was muted in Phase 1)
            AudioListener.volume = 1f;
            
            // Hide Phase 1 overlay
            if (ceremonyResultOverlay != null)
            {
                ceremonyResultOverlay.AddToClassList("hidden");
                ceremonyResultOverlay.RemoveFromClassList("ceremony-visible");
            }
            
            // Switch to cinematic camera
            SwitchToEndMatchCamera();
            
            // Populate scoreboard data
            // Re-derive the uppercase label for the scoreboard banner
            string bannerText = resultText;
            if (resultText == "Victory") bannerText = "VICTORIA";
            else if (resultText == "Defeat") bannerText = "DERROTA";
            if (postMatchResult != null) postMatchResult.text = bannerText;
            if (postMatchScoreA != null) postMatchScoreA.text = teamAKills.ToString();
            if (postMatchScoreB != null) postMatchScoreB.text = teamBKills.ToString();
            PopulatePostMatchScoreboard();
            
            // Send match results to backend (local player only)
            if (localPlayer != null && localPlayer.Object != null && localPlayer.Object.HasInputAuthority)
            {
                StartCoroutine(SendMatchEndToBackend(localPlayer, gsm));
            }
            
            // Show Phase 2 overlay
            if (postMatchOverlay != null)
            {
                postMatchOverlay.RemoveFromClassList("hidden");
                postMatchOverlay.pickingMode = PickingMode.Position;
                if (resultText == "Victory")
                    postMatchOverlay.AddToClassList("post-match-victory");
                else if (resultText == "Defeat")
                    postMatchOverlay.AddToClassList("post-match-defeat");
                else
                    postMatchOverlay.AddToClassList("post-match-draw");
            }
            
            Debug.Log("[HUD] Phase 2 — Scoreboard + EndMatchCamera");
            
            yield return new WaitForSecondsRealtime(PHASE2_DURATION);
            
            // ══════════════════════════════════════════════════════════
            // TRANSITION — Back to lobby
            // ══════════════════════════════════════════════════════════
            
            // Restore audio & timescale (safety)
            AudioListener.volume = 1f;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            
            // Only the host drives the scene transition
            if (gsm != null && gsm.HasStateAuthority)
            {
                gsm.RPC_EndGameForAll();
            }
            
            // Fallback: if still in GameScene after 3 extra seconds
            yield return new WaitForSecondsRealtime(3f);
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "LobbyScene")
            {
                Debug.LogWarning("[HUD] Post-match fallback — forcing local scene load to LobbyScene");
                UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
            }
        }
        
        /// <summary>
        /// Disables all player cameras and enables the pre-placed EndMatchCamera.
        /// </summary>
        private void SwitchToEndMatchCamera()
        {
            if (endMatchCamera == null)
            {
                Debug.LogWarning("[HUD] EndMatchCamera not found — keeping player camera active");
                return;
            }
            
            // Disable all player cameras (FPS + overlay)
            var allPlayerControllers = FindObjectsOfType<ArtisansGuns.Game.PlayerController>();
            foreach (var pc in allPlayerControllers)
            {
                var cameras = pc.GetComponentsInChildren<Camera>(true);
                foreach (var cam in cameras)
                    cam.enabled = false;
            }
            
            // Enable end-match camera
            endMatchCamera.gameObject.SetActive(true);
            endMatchCamera.enabled = true;
            
            // Ensure the end-match camera has an AudioListener
            if (endMatchCamera.GetComponent<AudioListener>() == null)
                endMatchCamera.gameObject.AddComponent<AudioListener>();
            
            Debug.Log("[HUD] Switched to EndMatchCamera");
        }
        
        /// <summary>
        /// Plays victory or defeat sound. Uses ignoreListenerVolume so it's audible
        /// even when AudioListener.volume = 0 (game audio muted).
        /// </summary>
        private void PlayCeremonySfx(bool victory)
        {
            AudioClip clip = victory ? victorySfx : defeatSfx;
            if (clip == null) return;
            
            // Always use this (HUD) GameObject — endMatchCamera may be inactive during Phase 1
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f;
            src.volume = 0.7f;
            src.ignoreListenerVolume = true;
            src.Play();
            Destroy(src, clip.length + 0.5f);
        }
        
        /// <summary>
        /// Builds the per-player scoreboard rows for the post-match overlay.
        /// Each row shows: agent icon, player name, kills, deaths, coins earned.
        /// </summary>
        private void PopulatePostMatchScoreboard()
        {
            if (postMatchTeamAList == null || postMatchTeamBList == null) return;
            
            postMatchTeamAList.Clear();
            postMatchTeamBList.Clear();
            
            var gsm = GameStateManager.Instance;
            var allPlayers = FindObjectsOfType<PlayerNetworkData>();
            
            foreach (var player in allPlayers)
            {
                if (player.Object == null) continue;
                if (!player.TeamAssigned) continue;
                
                bool isLocal = player.Object.HasInputAuthority;
                int coins = CalculatePlayerCoins(player, gsm);
                int xp = CalculatePlayerXp(player, gsm);
                
                var row = BuildPostMatchPlayerRow(
                    player.CharacterName.ToString(),
                    player.SelectedAgent.ToString(),
                    player.Kills,
                    player.Deaths,
                    player.Headshots,
                    player.BestStreak,
                    xp,
                    coins,
                    isLocal,
                    player.Team
                );
                
                if (player.Team == 0)
                    postMatchTeamAList.Add(row);
                else
                    postMatchTeamBList.Add(row);
            }
        }
        
        /// <summary>
        /// Calculates blue_points earned by a player for this match.
        /// Formula: kills * winMultiplier * playerMultiplier - (deaths * deathPenalty)
        /// </summary>
        private int CalculatePlayerCoins(PlayerNetworkData player, GameStateManager gsm)
        {
            if (gsm == null) return 0;
            
            int kills = player.Kills;
            int deaths = player.Deaths;
            int maxPlayers = Mathf.Max(gsm.MaxSimultaneousPlayers, 2);
            
            // Win multiplier: 1.5x winners, 1.0x draw, 0.5x losers (~⅓ of winner)
            float winMult = 0.5f;
            if (gsm.MatchResult == 3) // draw
                winMult = 1.0f;
            else if ((gsm.MatchResult == 1 && player.Team == 0) || (gsm.MatchResult == 2 && player.Team == 1))
                winMult = 1.5f;
            
            // Base: 10 coins per kill, player count bonus, minus death penalty
            float raw = (kills * 10f) * winMult * (1f + (maxPlayers - 2) * 0.1f) - (deaths * 2f);
            return Mathf.Max(0, Mathf.RoundToInt(raw));
        }
        
        private int CalculatePlayerXp(PlayerNetworkData player, GameStateManager gsm)
        {
            if (gsm == null) return 0;
            
            int k = player.Kills;
            int d = player.Deaths;
            int hs = player.Headshots;
            int bs = player.BestStreak;
            int maxP = Mathf.Max(gsm.MaxSimultaneousPlayers, 2);
            
            int actualPlayers = FindObjectsOfType<PlayerNetworkData>()
                .Count(p => p.Object != null && p.TeamAssigned);
            int actP = Mathf.Max(2, actualPlayers);
            
            float baseXp = (k * 50f) - (d * 10f) + (hs * 25f) + (bs * 15f);
            baseXp = Mathf.Max(0f, baseXp);
            
            float winMult = 0.5f;
            if (gsm.MatchResult == 3)
                winMult = 1.0f;
            else if ((gsm.MatchResult == 1 && player.Team == 0) || (gsm.MatchResult == 2 && player.Team == 1))
                winMult = 1.5f;
            
            float playerMult = Mathf.Max(0.2f, (float)actP / maxP);
            
            return Mathf.Max(0, Mathf.RoundToInt(baseXp * winMult * playerMult));
        }
        
        private VisualElement BuildPostMatchPlayerRow(string characterName, string agent, int kills, int deaths, int headshots, int bestStreak, int xp, int coins, bool isLocal, int team)
        {
            var row = new VisualElement();
            row.AddToClassList("pm-player-row");
            if (isLocal)
                row.AddToClassList(team == 0 ? "pm-row-local-a" : "pm-row-local-b");
            
            // Agent portrait
            var icon = new VisualElement();
            icon.AddToClassList("pm-agent-icon");
            
            var agentData = ArtisansGuns.Data.AgentDefinition.GetAgentById(
                string.IsNullOrEmpty(agent) ? "crimson" : agent.ToLower());
            
            Texture2D portrait = null;
            if (agentData != null && !string.IsNullOrEmpty(agentData.iconPath))
                portrait = UnityEngine.Resources.Load<Texture2D>(agentData.iconPath);
            
            if (portrait != null)
            {
                var portraitInner = new VisualElement();
                portraitInner.AddToClassList("pm-agent-portrait-inner");
                portraitInner.style.backgroundImage = new StyleBackground(portrait);
                icon.Add(portraitInner);
            }
            else
            {
                var initial = new Label(string.IsNullOrEmpty(agent) ? "?" : agent.Substring(0, 1).ToUpper());
                initial.AddToClassList("pm-agent-initial");
                icon.Add(initial);
            }
            
            // Player name
            var nameLabel = new Label(string.IsNullOrEmpty(characterName) ? "---" : characterName.ToUpper());
            nameLabel.AddToClassList("pm-player-name");
            
            // Kills
            var killsLabel = new Label(kills.ToString());
            killsLabel.AddToClassList("pm-stat");
            killsLabel.AddToClassList("pm-kills");
            
            // Deaths
            var deathsLabel = new Label(deaths.ToString());
            deathsLabel.AddToClassList("pm-stat");
            deathsLabel.AddToClassList("pm-deaths");
            
            // Headshots
            var hsLabel = new Label(headshots.ToString());
            hsLabel.AddToClassList("pm-stat");
            hsLabel.AddToClassList("pm-hs");
            
            // Best Streak (Combo Kill)
            var streakLabel = new Label(bestStreak.ToString());
            streakLabel.AddToClassList("pm-stat");
            streakLabel.AddToClassList("pm-streak");
            
            // XP earned
            var xpLabel = new Label($"+{xp}");
            xpLabel.AddToClassList("pm-xp");
            
            // Coins earned
            var coinsLabel = new Label($"+{coins}");
            coinsLabel.AddToClassList("pm-coins");
            
            row.Add(icon);
            row.Add(nameLabel);
            row.Add(killsLabel);
            row.Add(deathsLabel);
            row.Add(hsLabel);
            row.Add(streakLabel);
            row.Add(xpLabel);
            row.Add(coinsLabel);
            
            return row;
        }
        
        // ═══════════════════════════════════════════════════════════
        // Match-end backend persistence
        // ═══════════════════════════════════════════════════════════
        
        [System.Serializable]
        private class MatchEndRequest
        {
            public int kills;
            public int deaths;
            public int headshots;
            public int bestStreak;
            public int maxPlayers;
            public int actualPlayers;
            public bool won;
            public bool draw;
        }
        
        [System.Serializable]
        private class MatchEndResponse
        {
            public bool success;
            public string error;
            public int xpEarned;
            public int coinsEarned;
            public int diamondsEarned;
            public int newXp;
            public int newLevel;
            public int oldLevel;
            public int newBluePoints;
            public int newRivalCoins;
        }
        
        /// <summary>
        /// Sends local player's match results to backend for XP, coins, and diamond rewards.
        /// Called once during Phase 2 of MatchEndCeremony.
        /// </summary>
        private IEnumerator SendMatchEndToBackend(PlayerNetworkData player, GameStateManager gsm)
        {
            if (player == null || gsm == null) yield break;
            
            var authMgr = AuthManager.Instance;
            if (authMgr == null || !authMgr.HasBackendToken()) yield break;
            
            string token = authMgr.GetCurrentToken();
            if (string.IsNullOrEmpty(token)) yield break;
            
            // Determine win/draw
            bool won = false;
            bool draw = gsm.MatchResult == 3;
            if (!draw)
            {
                won = (gsm.MatchResult == 1 && player.Team == 0) ||
                      (gsm.MatchResult == 2 && player.Team == 1);
            }
            
            // Count actual players still in the match
            int actualPlayers = FindObjectsOfType<PlayerNetworkData>()
                .Count(p => p.Object != null && p.TeamAssigned);
            
            var requestData = new MatchEndRequest
            {
                kills = player.Kills,
                deaths = player.Deaths,
                headshots = player.Headshots,
                bestStreak = player.BestStreak,
                maxPlayers = gsm.MaxSimultaneousPlayers,
                actualPlayers = actualPlayers,
                won = won,
                draw = draw
            };
            
            string json = JsonUtility.ToJson(requestData);
            
            using (UnityWebRequest request = new UnityWebRequest($"{BASE_URL}/loadout/match-end", "POST"))
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
                    var response = JsonUtility.FromJson<MatchEndResponse>(request.downloadHandler.text);
                    if (response.success)
                    {
                        Debug.Log($"[HUD] Match results saved: +{response.xpEarned} XP, +{response.coinsEarned} coins, +{response.diamondsEarned} diamonds (lvl {response.oldLevel}→{response.newLevel})");
                        
                        // Show XP earned on scoreboard
                        ShowMatchRewardsSummary(response);
                        
                        // Refresh loadout so lobby has fresh data
                        ArtisansGuns.Managers.LoadoutManager.Instance?.RefreshLoadout();
                    }
                    else
                    {
                        Debug.LogWarning($"[HUD] Match end API error: {response.error}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[HUD] Match end request failed: {request.error}");
                }
            }
        }
        
        /// <summary>
        /// Show a brief XP/rewards summary on the post-match overlay.
        /// Creates a small panel at the bottom of the scoreboard.
        /// </summary>
        private void ShowMatchRewardsSummary(MatchEndResponse response)
        {
            if (postMatchOverlay == null) return;
            
            var rewardsContainer = new VisualElement();
            rewardsContainer.style.flexDirection = FlexDirection.Row;
            rewardsContainer.style.justifyContent = Justify.Center;
            rewardsContainer.style.alignItems = Align.Center;
            rewardsContainer.style.marginTop = 12;
            rewardsContainer.style.paddingTop = 8;
            rewardsContainer.style.paddingBottom = 8;
            
            var xpLabel = new Label($"+{response.xpEarned} XP");
            xpLabel.style.color = new Color(0.3f, 0.9f, 1f);
            xpLabel.style.fontSize = 18;
            xpLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            xpLabel.style.marginRight = 20;
            rewardsContainer.Add(xpLabel);
            
            var coinsLabel = new Label($"+{response.coinsEarned} Coins");
            coinsLabel.style.color = new Color(1f, 0.85f, 0.2f);
            coinsLabel.style.fontSize = 18;
            coinsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            coinsLabel.style.marginRight = 20;
            rewardsContainer.Add(coinsLabel);
            
            if (response.diamondsEarned > 0)
            {
                var diamondLabel = new Label($"+{response.diamondsEarned} Diamonds!");
                diamondLabel.style.color = new Color(0.6f, 0.4f, 1f);
                diamondLabel.style.fontSize = 18;
                diamondLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                rewardsContainer.Add(diamondLabel);
            }
            
            if (response.newLevel > response.oldLevel)
            {
                var lvlUpLabel = new Label($"  LEVEL UP! → {response.newLevel}");
                lvlUpLabel.style.color = new Color(1f, 0.6f, 0f);
                lvlUpLabel.style.fontSize = 20;
                lvlUpLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                rewardsContainer.Add(lvlUpLabel);
            }
            
            postMatchOverlay.Add(rewardsContainer);
        }
        
        private void OnDisable()
        {
            UnsubscribeFromNetworkEvents();
            
            // Safety: restore timeScale and audio in case ceremony was interrupted
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
            AudioListener.volume = 1f;
        }
    }
}
