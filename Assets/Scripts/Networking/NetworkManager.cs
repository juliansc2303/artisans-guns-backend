using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;
using ArtisansGuns.Auth;
using ArtisansGuns.Loading;

namespace ArtisansGuns.Networking
{
    /// <summary>
    /// NetworkManager - Maneja toda la lógica de networking con Photon Fusion
    /// Singleton persistente entre escenas
    /// </summary>
    public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static NetworkManager Instance { get; private set; }

        [Header("Fusion Settings")]
        [SerializeField] private NetworkRunner networkRunnerPrefab;
        [SerializeField] private NetworkObject playerPrefab; // Player prefab used for ALL scenes (lobby + game)
        // NOTE: playerDataPrefab removed - using same prefab everywhere to avoid Fusion spawn conflicts
        [SerializeField] private NetworkObject gameStateManagerPrefab; // Prefab for GameStateManager

        // Runner instance
        private NetworkRunner runner;
        public NetworkRunner Runner => runner; // Public getter for Runner

        // Room data
        private string currentRoomName;
        private string currentMapName;
        private bool isHost;
        
        // Team and join order tracking
        private int nextJoinOrder = 0; // Tracks join order (0=first/host, 1=second, etc.)
        private int teamACount = 0;    // Current count of Team A players
        private int teamBCount = 0;    // Current count of Team B players
        public PlayerRef CurrentHost { get; private set; } = PlayerRef.None;
        
        // Initialization state
        private bool isNetworkReady = false;
        private bool isPollingRooms = false;
        private Coroutine pollCoroutine;
        private bool isLeavingRoom = false; // Flag to track voluntary disconnections
        
        /// <summary>Room code for the current session (shown in settings for invites).</summary>
        public string CurrentRoomCode { get; private set; }
        
        // Cached session list
        private List<SessionInfo> cachedSessions = new List<SessionInfo>();

        // Events
        public event Action<List<SessionInfo>> OnRoomListUpdated;
        public event Action<PlayerRef, NetworkObject> OnPlayerJoinedRoom;
        public event Action<PlayerRef> OnPlayerLeftRoom;
        public event Action<string> OnRoomCreated;
        public event Action<string> OnJoinedRoom;
        public event Action<string> OnJoinRoomFailed;
        public event Action OnGameStarted;
        public event Action OnDisconnected;
        public event Action OnPlayerDataChanged; // New event for when player data updates

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
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ===================================
        // APP LIFECYCLE (screen lock / alt-tab)
        // ===================================

        private bool _wasPaused = false;

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                _wasPaused = true;
                return;
            }

            // Resuming from pause (screen unlock)
            if (!_wasPaused) return;
            _wasPaused = false;

            HandleAppResume();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // On Android, OnApplicationPause is the primary callback.
            // OnApplicationFocus supplements it for alt-tab / overlay scenarios.
            if (hasFocus && _wasPaused)
            {
                _wasPaused = false;
                HandleAppResume();
            }
        }

        private async void HandleAppResume()
        {
            // Give Unity a frame to stabilise
            await Task.Delay(500);

            bool runnerAlive = runner != null && runner.IsRunning;
            string currentScene = SceneManager.GetActiveScene().name;
            bool isGameScene = currentScene == "Sandbox" || currentScene.StartsWith("Map");

            Debug.Log($"[NetworkManager] App resumed — scene={currentScene}, runnerAlive={runnerAlive}, isGameScene={isGameScene}");

            if (!runnerAlive)
            {
                // Always dismiss a stuck loading screen when runner is dead
                if (PreWarmManager.Instance != null && PreWarmManager.Instance.IsLoading)
                {
                    Debug.LogWarning("[NetworkManager] Runner dead + loading screen active — dismissing loading");
                    PreWarmManager.Instance.HideLoading();
                }

                if (isGameScene)
                {
                    // Runner died while loading/in-game — force back to lobby
                    Debug.LogWarning($"[NetworkManager] Runner lost during pause in {currentScene} — returning to LobbyScene");
                    isNetworkReady = false;
                    SceneManager.LoadScene("LobbyScene");

                    // Re-initialize networking after scene load settles
                    await Task.Delay(1500);
                    try { await InitializeNetworking(); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[NetworkManager] Re-init after pause failed: {ex.Message}");
                    }
                }
                else if (currentScene == "LobbyScene")
                {
                    // Runner died while in lobby — silently re-initialize
                    Debug.LogWarning("[NetworkManager] Runner lost during pause in LobbyScene — re-initializing");
                    try { await InitializeNetworking(); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[NetworkManager] Re-init after pause failed: {ex.Message}");
                    }
                }
            }
            else if (isGameScene)
            {
                // Runner alive + game scene — make sure local player is spawned
                // (handles the case where scene loaded during background but spawn failed)
                EnsureLocalPlayerSpawned();
            }
        }

        // ===================================
        // PUBLIC API
        // ===================================

        /// <summary>
        /// Destroys the current runner (lobby or stale) and creates a brand-new
        /// runner for starting a game session.  Fusion runners must not be reused
        /// after Shutdown — this guarantees a fresh instance for every StartGame.
        /// </summary>
        private async Task<bool> PrepareRunnerForGame()
        {
            // Stop lobby polling — we're transitioning to a game
            isPollingRooms = false;
            if (pollCoroutine != null) { StopCoroutine(pollCoroutine); pollCoroutine = null; }

            // Safety net: reset combo/kill streak before starting a new session
            ArtisansGuns.Audio.ComboKillManager.Instance?.ResetForNewMatch();

            // Tear down whatever runner exists (lobby runner or orphaned game runner)
            if (runner != null)
            {
                try
                {
                    runner.RemoveCallbacks(this);
                    if (!runner.IsShutdown)
                        await runner.Shutdown(true, ShutdownReason.Ok);
                }
                catch { /* expected during rapid leave/join cycles */ }

                if (runner != null && runner.gameObject != null)
                    Destroy(runner.gameObject);
                runner = null;

                // Give Unity a frame to process the Destroy
                await System.Threading.Tasks.Task.Delay(100);
            }

            // Create a completely fresh runner for the game session
            if (networkRunnerPrefab == null)
            {
                Debug.LogError("[PrepareRunnerForGame] networkRunnerPrefab is null!");
                return false;
            }

            runner = Instantiate(networkRunnerPrefab);
            runner.name = "NetworkRunner_Game";
            DontDestroyOnLoad(runner.gameObject);
            runner.AddCallbacks(this);

            // Spread async scene loading across more frames so individual frames
            // stay short and Runner.Update() can send heartbeats.
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.Low;

            Debug.Log("[PrepareRunnerForGame] Fresh runner created");
            return true;
        }

        /// <summary>
        /// Initialize Fusion and connect to lobby
        /// </summary>
        public async Task InitializeNetworking()
        {            
            // Reset network ready flag
            isNetworkReady = false;
            
            // If there's an existing runner that's still active, shut it down first
            if (runner != null)
            { // Debug.Log("⚠️ Found existing runner, shutting it down before creating new one...");
                try
                {
                    if (runner.IsRunning)
                    {
                        await runner.Shutdown(true, ShutdownReason.Ok);
                    }
                    
                    if (runner.gameObject != null)
                    {
                        Destroy(runner.gameObject);
                    }
                    
                    runner = null;
                    await System.Threading.Tasks.Task.Delay(500); // Wait for cleanup
                }
                catch (System.Exception e)
                { // Debug.LogWarning($"Exception during runner cleanup: {e.Message}");
                    runner = null;
                }
            }
                        
            if (runner == null)
            {
                runner = Instantiate(networkRunnerPrefab);
                runner.name = "NetworkRunner_Lobby";
                DontDestroyOnLoad(runner.gameObject);
                
                // Ensure callbacks are registered
                runner.AddCallbacks(this);
                // Callbacks registered
            }

            // Join Shared lobby - Photon Cloud handles server, no player is host // Debug.Log("🔌 Connecting to Photon Fusion lobby (SessionLobby.Shared, region: us)...");
            var result = await runner.JoinSessionLobby(SessionLobby.Shared, "us");

            if (!result.Ok)
            { // Debug.LogError($"Failed to join lobby: {result.ShutdownReason}");
                isNetworkReady = false;
            }
            else
            { // Debug.Log("✅ Connected to Photon Fusion lobby (region: us)");
                // Lobby runner active
                isNetworkReady = true;
                
                // Start polling room list — force-stop any stale coroutine first
                if (pollCoroutine != null)
                {
                    StopCoroutine(pollCoroutine);
                    pollCoroutine = null;
                }
                isPollingRooms = false;
                pollCoroutine = StartCoroutine(PollRoomList());
            }
        }

        /// <summary>
        /// Check if network is ready to create/join rooms
        /// </summary>
        public bool IsNetworkReady()
        {
            return isNetworkReady && runner != null;
        }

        /// <summary>
        /// Poll room list every 2 seconds for real-time updates
        /// </summary>
        private System.Collections.IEnumerator PollRoomList()
        {
            isPollingRooms = true;
            
            yield return new UnityEngine.WaitForSeconds(1f); // Wait for initial connection
            
            while (isNetworkReady && runner != null && isPollingRooms)
            {
                // In SessionLobby: IsRunning = False, GameMode = 0
                // In game room: IsRunning = True, GameMode = Shared
                bool isInGameRoom = runner.IsRunning && (runner.GameMode == GameMode.Shared ||
                                                          runner.GameMode == GameMode.Host ||
                                                          runner.GameMode == GameMode.Client);
                
                if (!isInGameRoom)
                {
                    // We're in lobby (not in a game room) - request session list update
                    _ = RefreshSessionList();
                }
                
                yield return new UnityEngine.WaitForSeconds(2f);
            }
            
            isPollingRooms = false;
            pollCoroutine = null;
        }
        
        /// <summary>
        /// Force refresh of session list from Fusion
        /// </summary>
        private async System.Threading.Tasks.Task RefreshSessionList()
        {
            if (runner != null)
            {
                try
                { // Debug.Log($"🔄 Requesting session list update... (Runner: {runner.name}, Mode: {runner.GameMode})");
                    // Re-join Shared lobby to get updated room list (MUST match InitializeNetworking lobby type)
                    var result = await runner.JoinSessionLobby(SessionLobby.Shared, "us"); // Debug.Log($"✅ Session list refreshed - Result: {result.Ok}, Error: {result.ErrorMessage}");
                }
                catch (System.Exception e)
                { // Debug.LogWarning($"Failed to refresh session list: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Create a new room and go directly to game (no lobby/countdown)
        /// </summary>
        public async Task<bool> CreateRoom(string roomName, string mapName, bool isPrivate = false, string gamemode = "tdm")
        {
            if (!isNetworkReady)
            {
                return false;
            }

            // CRITICAL: Clear ALL stale data from previous sessions
            ResetSessionState();

            currentRoomName = roomName;
            currentMapName = mapName;
            isHost = true;

            // Save to PlayerPrefs for room scene
            PlayerPrefs.SetString("current_room_name", roomName);
            PlayerPrefs.SetString("current_map_name", mapName);
            PlayerPrefs.SetInt("is_room_host", 1);
            PlayerPrefs.Save();

            // Show loading screen before scene transition
            if (PreWarmManager.Instance != null)
                PreWarmManager.Instance.ShowLoading();

            // CRITICAL: Create a fresh runner — Fusion runners must not be reused after Shutdown.
            if (!await PrepareRunnerForGame())
            {
                PreWarmManager.Instance?.HideLoading();
                return false;
            }

            var sceneManager = EnsureSceneManager(runner);

            // Connect to Photon WITHOUT loading the game scene yet.
            // This lets the Runner establish the connection and start sending
            // heartbeats BEFORE the heavy scene load blocks the main thread.
            var args = new StartGameArgs()
            {
                GameMode = GameMode.Shared,
                SessionName = roomName,
                SceneManager = sceneManager,
                PlayerCount = 10,
                IsVisible = true, // Always visible so join-by-code works; private filtered in UI
                IsOpen = true
            };

            args.SessionProperties = new Dictionary<string, SessionProperty>
            {
                { "map", mapName },
                { "host", GetCurrentUsername() },
                { "room_code", CurrentRoomCode ?? "" },
                { "is_private", isPrivate ? "1" : "0" },
                { "gamemode", gamemode }
            };
            var result = await runner.StartGame(args);

            Debug.Log($"[NetworkManager] CreateRoom StartGame - Ok:{result.Ok} Reason:{result.ShutdownReason} Error:{result.ErrorMessage}");

            if (!result.Ok)
            {
                Debug.LogError($"[NetworkManager] Failed to create room: {result.ShutdownReason} - {result.ErrorMessage}");
                PreWarmManager.Instance?.HideLoading();
                return false;
            }

            if (runner.IsShutdown)
            {
                Debug.LogError("[NetworkManager] Runner is shutdown after StartGame!");
                PreWarmManager.Instance?.HideLoading();
                return false;
            }

            // Connection established — NOW load the game scene.
            // Runner.Update() can keep sending heartbeats between async load frames.
            var gameSceneRef = GetSceneRef("Sandbox");
            Debug.Log($"[NetworkManager] Connection ready, loading Sandbox scene (SceneRef={gameSceneRef})");
            runner.LoadScene(gameSceneRef);
            
            // Set host flag and store room data
            isHost = true;
            currentRoomName = roomName;
            currentMapName = mapName;
            OnRoomCreated?.Invoke(roomName);
            OnJoinedRoom?.Invoke(roomName);
            
            return true;
        }

        /// <summary>
        /// Join an existing room and go directly to game
        /// </summary>
        public async Task<bool> JoinRoom(string roomName)
        {
            // CRITICAL: Clear ALL stale data from previous sessions
            ResetSessionState();

            currentRoomName = roomName;
            isHost = false;

            // Save to PlayerPrefs
            PlayerPrefs.SetString("current_room_name", roomName);
            PlayerPrefs.SetInt("is_room_host", 0);
            PlayerPrefs.Save();

            // Show loading screen before scene transition
            if (PreWarmManager.Instance != null)
                PreWarmManager.Instance.ShowLoading();

            // CRITICAL: Create a fresh runner — Fusion runners must not be reused after Shutdown.
            if (!await PrepareRunnerForGame())
            {
                PreWarmManager.Instance?.HideLoading();
                return false;
            }

            var sceneManager = EnsureSceneManager(runner);

            // Connect to Photon WITHOUT loading the game scene yet.
            // This lets the Runner establish the connection and start sending
            // heartbeats BEFORE the heavy scene load blocks the main thread.
            var args = new StartGameArgs()
            {
                GameMode = GameMode.Shared,
                SessionName = roomName,
                SceneManager = sceneManager
            };
            var result = await runner.StartGame(args);

            Debug.Log($"[NetworkManager] JoinRoom StartGame - Ok:{result.Ok} Reason:{result.ShutdownReason} Error:{result.ErrorMessage}");

            if (!result.Ok)
            {
                Debug.LogError($"[NetworkManager] Failed to join room: {result.ShutdownReason} - {result.ErrorMessage}");
                PreWarmManager.Instance?.HideLoading();
                OnJoinRoomFailed?.Invoke(result.ShutdownReason.ToString());
                return false;
            }

            if (runner.IsShutdown)
            {
                Debug.LogError("[NetworkManager] Runner is shutdown after StartGame!");
                PreWarmManager.Instance?.HideLoading();
                return false;
            }

            // Connection established — NOW load the game scene.
            // Runner.Update() can keep sending heartbeats between async load frames.
            var gameSceneRef = GetSceneRef("Sandbox");
            Debug.Log($"[NetworkManager] Connection ready, loading Sandbox scene (SceneRef={gameSceneRef})");
            runner.LoadScene(gameSceneRef);

            // Set state
            isHost = false;
            currentRoomName = roomName;
            
            // Get map name from session properties if available
            if (runner.SessionInfo.Properties.TryGetValue("map", out var mapProp))
            {
                currentMapName = mapProp.PropertyValue.ToString();
            }
            // Get room code from session properties (for display in settings)
            if (runner.SessionInfo.Properties.TryGetValue("room_code", out var codeProp))
            {
                CurrentRoomCode = codeProp.PropertyValue.ToString();
            }
            OnJoinedRoom?.Invoke(roomName);
            
            return true;
        }

        // ===================================
        // .IO QUICK-PLAY & PRIVATE ROOMS
        // ===================================

        /// <summary>
        /// .IO-style Quick Play: find an open room with space, or auto-create one.
        /// Goes directly to Sandbox.
        /// </summary>
        public async Task<bool> QuickPlay()
        {
            // Look for an open PUBLIC room with space
            var room = cachedSessions.FirstOrDefault(s => s.IsOpen && s.PlayerCount < s.MaxPlayers
                && (!s.Properties.TryGetValue("is_private", out var priv) || priv.PropertyValue.ToString() != "1"));
            if (room != null)
            {
                // Grab room code from session properties
                if (room.Properties.TryGetValue("room_code", out var rc))
                    CurrentRoomCode = rc.PropertyValue.ToString();
                Debug.Log($"[NetworkManager] QuickPlay joining: {room.Name} ({room.PlayerCount}/{room.MaxPlayers})");
                return await JoinRoom(room.Name);
            }
            
            // No room found — create one with auto-name
            string code = GenerateRoomCode();
            CurrentRoomCode = code;
            string autoName = $"Game_{code}";
            Debug.Log($"[NetworkManager] QuickPlay creating: {autoName}");
            return await CreateRoom(autoName, "Sandbox", isPrivate: false);
        }

        /// <summary>
        /// Create a private room with a 6-char code (not visible in room list).
        /// Returns the room code on success, null on failure.
        /// </summary>
        public async Task<string> CreatePrivateRoom(string mapName = "Sandbox")
        {
            string code = GenerateRoomCode();
            CurrentRoomCode = code;
            string roomName = $"Private_{code}";
            bool ok = await CreateRoom(roomName, mapName, isPrivate: true);
            if (ok)
            {
                return code;
            }
            CurrentRoomCode = null;
            return null;
        }

        /// <summary>
        /// Join a private room by its numeric code.
        /// First searches cached sessions for matching room_code property,
        /// then falls back to session name convention.
        /// </summary>
        public async Task<bool> JoinPrivateRoom(string code)
        {
            code = code.Trim();
            // Search cached sessions for a room with this code
            var match = cachedSessions.FirstOrDefault(s =>
                s.Properties.TryGetValue("room_code", out var rc) && rc.PropertyValue.ToString() == code);
            if (match != null)
            {
                CurrentRoomCode = code;
                return await JoinRoom(match.Name);
            }
            // Fallback: try the naming convention
            CurrentRoomCode = code;
            string roomName = $"Private_{code}";
            return await JoinRoom(roomName);
        }

        /// <summary>Generate a unique 6-digit numeric room code.</summary>
        private string GenerateRoomCode()
        {
            var rng = new System.Random();
            string code;
            int attempts = 0;
            do
            {
                code = rng.Next(100000, 999999).ToString();
                attempts++;
            }
            while (attempts < 50 && cachedSessions.Any(s =>
                s.Properties.TryGetValue("room_code", out var rc) && rc.PropertyValue.ToString() == code));
            return code;
        }

        /// <summary>
        /// Leave current room and return to lobby
        /// </summary>
        public async Task LeaveRoom()
        {
            // Set flag to indicate this is a voluntary disconnection
            isLeavingRoom = true;
            
            // IMPORTANT: Do NOT destroy the GSM while the runner is still running!
            // If we have StateAuthority on the GSM and destroy it, Fusion despawns it
            // network-wide, killing the match for remaining players.
            // Just clear the local reference — runner.Shutdown() handles cleanup locally,
            // and Fusion transfers StateAuthority to remaining clients.
            GameStateManager.Instance = null;
            GameStateManager.Backup = default; // Clear backup — the leaving client shouldn't restore stale state
            
            // Reset combo/kill streak/ultimate state so it doesn't carry into the next session
            ArtisansGuns.Audio.ComboKillManager.Instance?.ResetForNewMatch();
            
            // Clear the player cache when leaving the room
            PlayerNetworkData.ClearPlayerCache();
            
            // Stop polling
            isPollingRooms = false;
            if (pollCoroutine != null) { StopCoroutine(pollCoroutine); pollCoroutine = null; }
            
            try
            {
                if (runner != null)
                {
                    // Clear ready status if still connected
                    if (runner.IsRunning)
                    {
                        var localPlayerData = FindObjectsOfType<PlayerNetworkData>()
                            .FirstOrDefault(pd => pd != null && pd.Object != null && pd.Object.HasInputAuthority);
                        
                        if (localPlayerData != null && localPlayerData.IsReady && localPlayerData.HasStateAuthority)
                        {
                            localPlayerData.IsReady = false;
                            localPlayerData.InGame = false;
                            await System.Threading.Tasks.Task.Delay(100);
                        }
                    }
                    
                    // Shutdown — gracefully if running, skip if already shut down
                    try
                    {
                        runner.RemoveCallbacks(this);
                        if (!runner.IsShutdown)
                            await runner.Shutdown(true, ShutdownReason.Ok);
                    }
                    catch { /* expected during rapid leave cycles */ }
                    
                    // ALWAYS destroy the runner GO — regardless of IsRunning/IsShutdown state
                    if (runner != null && runner.gameObject != null)
                    {
                        Destroy(runner.gameObject);
                    }
                    runner = null;
                    
                    // Wait for Photon to fully disconnect
                    await System.Threading.Tasks.Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LeaveRoom] cleanup exception: {ex.Message}");
                // Ensure runner is always nulled
                if (runner != null && runner.gameObject != null)
                    Destroy(runner.gameObject);
                runner = null;
            }
            finally
            {
                // Always reset flags
                isLeavingRoom = false;
                isNetworkReady = false;
                runner = null; // Triple-ensure runner is nulled
            }

            // After runner shutdown, clean up any orphaned GSM that survived DontDestroyOnLoad.
            // This is safe because the runner is dead — no network despawn will be broadcast.
            var orphanedGSM = FindObjectOfType<GameStateManager>();
            if (orphanedGSM != null)
            {
                Debug.Log("[NetworkManager] Cleaning up orphaned GSM after runner shutdown");
                orphanedGSM.gameObject.SetActive(false);
                Destroy(orphanedGSM.gameObject);
                GameStateManager.Instance = null;
            }

            // Clear room data
            PlayerPrefs.DeleteKey("current_room_name");
            PlayerPrefs.DeleteKey("current_map_name");
            PlayerPrefs.DeleteKey("is_room_host");
            PlayerPrefs.Save();

            // Wait a frame for cleanup
            await System.Threading.Tasks.Task.Delay(300);
            
            try
            { // Debug.Log("🔄 Re-initializing networking for lobby...");
                
                // Stop any existing polling
                if (pollCoroutine != null)
                {
                    StopCoroutine(pollCoroutine);
                    pollCoroutine = null;
                }
                isPollingRooms = false;
                
                // Re-initialize to join lobby
                await InitializeNetworking(); // Debug.Log("✅ Reconnected to lobby successfully");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkManager] First re-init failed: {ex.Message}, retrying...");
                // Retry once after a short delay
                try
                {
                    await System.Threading.Tasks.Task.Delay(1000);
                    await InitializeNetworking();
                }
                catch (Exception ex2)
                {
                    Debug.LogError($"[NetworkManager] Re-init retry also failed: {ex2.Message}");
                }
            }

            // Return to lobby scene // Debug.Log("📍 Loading LobbyScene...");
            SceneManager.LoadScene("LobbyScene");
        }

        /// <summary>
        /// Start the game - triggers the pre-match ceremony countdown.
        /// After countdown finishes, GameInProgress becomes true and players spawn.
        /// </summary>
        public async Task StartGame()
        {
            if (runner == null || !runner.IsRunning)
            {
                Debug.LogWarning("[StartGame] BLOCKED: runner is null or not running");
                return;
            }

            // If GSM.Instance is null, try to find/register an existing one
            if (GameStateManager.Instance == null)
            {
                var existingGSM = FindObjectOfType<GameStateManager>();
                if (existingGSM != null && existingGSM.gameObject.activeInHierarchy
                    && existingGSM.Object != null && existingGSM.Object.IsValid)
                {
                    GameStateManager.Instance = existingGSM;
                    Debug.Log("[StartGame] Re-registered orphaned GSM");
                }
                else
                {
                    Debug.LogWarning("[StartGame] BLOCKED: GameStateManager.Instance is null and no valid GSM found in scene");
                    return;
                }
            }

            var gsm = GameStateManager.Instance;
            if (gsm.Object == null || !gsm.Object.IsValid)
            {
                Debug.LogWarning("[StartGame] BLOCKED: GSM NetworkObject is null or invalid");
                return;
            }

            // Check guards and log which one blocks
            if (gsm.CountdownStarted)
            {
                Debug.Log("[StartGame] Skipped: CountdownStarted is already true");
            }
            else if (gsm.GameInProgress)
            {
                Debug.Log("[StartGame] Skipped: GameInProgress is already true");
            }
            else if (gsm.PreStartActive)
            {
                Debug.Log("[StartGame] Skipped: PreStartActive is already true");
            }
            else
            {
                // All guards clear — start the game
                Debug.Log($"[StartGame] Starting game (HasSA={gsm.HasStateAuthority}, MatchEnded={gsm.MatchEnded})");
                if (gsm.HasStateAuthority)
                    gsm.BeginCountdownSequence();
                else
                    gsm.RPC_BeginCountdownSequence();
            }

            OnGameStarted?.Invoke();
        }

        /// <summary>
        /// Get list of available rooms (returns cached list)
        /// </summary>
        public List<SessionInfo> GetAvailableRooms()
        {
            return new List<SessionInfo>(cachedSessions);
        }

        /// <summary>
        /// Get current player count in room
        /// </summary>
        public int GetPlayerCount()
        {
            if (runner == null || !runner.IsRunning)
                return 0;

            return runner.ActivePlayers.Count();
        }

        /// <summary>
        /// Check if local player created the room (room master in Shared Mode)
        /// In Shared Mode, the room creator has privileges like starting the game
        /// </summary>
        public bool IsHost()
        {
            return isHost && runner != null && runner.IsRunning;
        }

        /// <summary>
        /// Get current room name
        /// </summary>
        public string GetCurrentRoomName()
        {
            return currentRoomName;
        }

        /// <summary>
        /// Get current map name
        /// </summary>
        public string GetCurrentMapName()
        {
            return currentMapName;
        }

        /// <summary>
        /// Check if local player is host
        /// </summary>
        public bool IsLocalPlayerHost()
        {
            if (runner == null || !runner.IsRunning)
                return false;

            // In Shared Mode, the logical host is the player with JoinOrder 0
            var ourPlayerData = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(pd => pd.Object != null && pd.Object.HasInputAuthority);

            return ourPlayerData != null && ourPlayerData.JoinOrder == 0;
        }
        
        /// <summary>
        /// Called when player data changes (username, character, etc.)
        /// Triggers UI refresh
        /// </summary>
        public void NotifyPlayerDataChanged()
        {
            // Notifying UI of player data change
            OnPlayerDataChanged?.Invoke();
        }

        /// <summary>
        /// Spawn player prefab for a player
        /// </summary>
        private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
        {
            // CRITICAL: Final guard against double-spawn (OnPlayerJoined + OnSceneLoadDone race)
            var alreadySpawned = runner.GetPlayerObject(player);
            if (alreadySpawned != null && alreadySpawned.IsValid)
            {
                Debug.Log($"[SpawnPlayer] SKIPPED Player {player.PlayerId} - already has valid NetworkObject");
                return;
            }
            
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool isGameScene = sceneName == "Sandbox" || sceneName.StartsWith("Map");
            
            // ALWAYS use playerPrefab (same prefab for lobby and game)
            // Visual components will be disabled in lobby via PlayerSetup
            NetworkObject prefabToSpawn = playerPrefab;
            
            if (prefabToSpawn == null)
            {
                Debug.LogError("❌ playerPrefab is null!");
                return;
            }
            
            Vector3 spawnPosition;
            Quaternion spawnRotation = Quaternion.identity;
            
            if (isGameScene)
            {
                // In Sandbox, use GameManager spawn points
                var gameManager = FindObjectOfType<ArtisansGuns.Game.GameManager>();
                
                if (gameManager != null)
                {
                    // Get existing PlayerNetworkData to determine team
                    var existingPlayerData = FindObjectsOfType<PlayerNetworkData>()
                        .FirstOrDefault(pd => pd.Object != null && pd.Object.InputAuthority == player);
                    
                    int playerTeam = 0; // Default Team A
                    int playerIndex = 0;
                    string teamSource = "default";
                    
                    if (existingPlayerData != null && existingPlayerData.TeamAssigned)
                    {
                        playerTeam = existingPlayerData.Team;
                        playerIndex = existingPlayerData.JoinOrder / 2;
                        teamSource = "liveObject";
                    }
                    else if (PlayerNetworkData.PlayerCache.TryGetValue(player, out var cachedTeamData) && cachedTeamData.TeamAssigned)
                    {
                        // Restore from cache (after scene change, live object was destroyed)
                        playerTeam = cachedTeamData.Team;
                        playerIndex = cachedTeamData.JoinOrder / 2;
                        teamSource = "cache";
                    }
                    else
                    {
                        teamSource = "NO_DATA";
                        Debug.LogWarning($"[SpawnPlayer] Player {player.PlayerId} has NO team data! existingPD={existingPlayerData != null} cacheHas={PlayerNetworkData.PlayerCache.ContainsKey(player)}");
                    }
                    
                    if (teamSource == "NO_DATA")
                    {
                        // No team data yet — spawn underground so the CharacterController doesn't
                        // collide with / push existing players. DelayedTeamAssignment will call
                        // RepositionPlayerToTeamSpawn once the team is resolved.
                        spawnPosition = new Vector3(0f, -100f, 0f);
                        spawnRotation = Quaternion.identity;
                        Debug.Log($"[SpawnPlayer] Player {player.PlayerId} no team data → temporary underground spawn, will reposition after team assignment");
                    }
                    else
                    {
                        // Usar GameManager para obtener spawn position correcto (deterministic via playerIndex)
                        spawnPosition = gameManager.GetSpawnPositionForTeam(playerTeam, playerIndex);
                        spawnRotation = gameManager.GetSpawnRotationForTeam(playerTeam, playerIndex);
                    }
                    Debug.Log($"[SpawnPlayer] Player {player.PlayerId} Team={playerTeam} Index={playerIndex} Pos={spawnPosition} src={teamSource}");
                }
                else
                {
                    // Fallback si no hay GameManager // Debug.LogWarning("⚠️ GameManager not found, using default spawn position");
                    spawnPosition = new Vector3(0f, 1f, 0f);
                }
            }
            else
            {
                // In LobbyScene, just spawn at origin (not visible anyway, just for data)
                spawnPosition = Vector3.zero; // Debug.Log($"🎮 Spawning player {player.PlayerId} in lobby - position {spawnPosition}");
            }

            // Use OnBeforeSpawned to initialize [Networked] position so remote clients
            // receive the correct spawn position in the very first network snapshot
            Vector3 capturedPos = spawnPosition;
            Quaternion capturedRot = spawnRotation;
            var playerObject = runner.Spawn(prefabToSpawn, spawnPosition, spawnRotation, player,
                (runner, obj) =>
                {
                    var pc = obj.GetComponent<ArtisansGuns.Game.PlayerController>();
                    if (pc != null)
                    {
                        pc.NetworkPosition = capturedPos;
                        pc.NetworkRotation = capturedRot;
                    }
                });

            if (playerObject != null)
            {
                // Verify position was applied correctly
                Debug.Log($"[SpawnPlayer] VERIFY Player {player.PlayerId} transform.pos={playerObject.transform.position} NetworkPos={playerObject.GetComponent<ArtisansGuns.Game.PlayerController>()?.NetworkPosition}");
                
                // Register this player's object with Fusion for proper tracking
                runner.SetPlayerObject(player, playerObject);
                
                // Team assignment will be done via AssignPlayerTeam() method called from PlayerNetworkData.Spawned()

                OnPlayerJoinedRoom?.Invoke(player, playerObject);
            }
            else
            {
                Debug.LogError($"❌ Failed to spawn player for {player.PlayerId} - Runner state: {runner.State}");
            }
        }

        // ===================================
        // FUSION CALLBACKS
        // ===================================

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            
            // Check if this player already has a PlayerNetworkData (reconnection)
            var existingPlayerData = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(pd => pd.Object != null && pd.Object.InputAuthority == player);
            
            if (existingPlayerData != null)
            {
                // If in Sandbox, they should be able to rejoin
                string currentSceneName = SceneManager.GetActiveScene().name;
                if (currentSceneName == "Sandbox")
                {
                    return; // Don't spawn duplicate, they'll re-enter with existing data
                }
            }
            
            // .IO: Always allow new joiners (no game-in-progress rejection)
            
            // Spawn GameStateManager if it doesn't exist (any local player can do this in Shared Mode)
            if (GameStateManager.Instance == null && runner.GameMode == GameMode.Shared && player == runner.LocalPlayer)
            {
                // Try to find an existing GSM object in the scene first
                var existingGSM = FindObjectOfType<GameStateManager>();
                if (existingGSM != null && existingGSM.gameObject.activeInHierarchy)
                {
                    GameStateManager.Instance = existingGSM;
                    Debug.Log("[OnPlayerJoined] GSM Instance was null but object exists — re-registered");
                }
                else if (gameStateManagerPrefab != null)
                {
                    Debug.Log("[OnPlayerJoined] Spawning GameStateManager (Shared Mode, no existing GSM)");
                    runner.Spawn(gameStateManagerPrefab, Vector3.zero, Quaternion.identity, PlayerRef.None);
                }
                else
                {
                    Debug.LogError("[OnPlayerJoined] GameStateManager prefab not assigned!");
                }
            }
            
            // Notify listeners
            OnPlayerJoinedRoom?.Invoke(player, null);
            
            // SHARED MODE: each client spawns their OWN player only
            if (runner.GameMode == GameMode.Shared)
            {
                bool isMyPlayer = player == runner.LocalPlayer;
                if (!isMyPlayer)
                    return; // Remote players spawn themselves

                var existingObject = runner.GetPlayerObject(player);
                if (existingObject != null && existingObject.IsValid)
                    return;
            }
            else if (!runner.IsServer)
            {
                return; // non-Shared, non-server: only server spawns
            }
            
            // Check for duplicate before spawning
            var existingPlayer = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(pd => pd.Object != null && pd.Object.InputAuthority == player);
            
            if (existingPlayer != null)
            {
                return;
            }
            
            // CRITICAL: Only spawn from OnPlayerJoined if we're already in the game scene.
            // If still in LobbyScene, OnSceneLoadDone will handle spawning when the
            // game scene loads. Spawning in LobbyScene creates a throwaway player at (0,0,0)
            // that gets destroyed during scene transition, wasting resources and causing
            // "spawned and despawned in same tick" warnings on remote clients.
            string spawnScene = SceneManager.GetActiveScene().name;
            bool isGameSceneReady = spawnScene == "Sandbox" || spawnScene.StartsWith("Map");
            if (!isGameSceneReady)
            {
                Debug.Log($"[OnPlayerJoined] DEFERRED Player {player.PlayerId} spawn - waiting for game scene (current: {spawnScene})");
                return;
            }

            // Don't spawn while PreWarmManager is running — OnSceneLoadDone will spawn after pre-warm completes
            if (PreWarmManager.Instance != null && PreWarmManager.Instance.IsLoading)
            {
                Debug.Log($"[OnPlayerJoined] DEFERRED Player {player.PlayerId} spawn - pre-warm in progress");
                return;
            }
            
            SpawnPlayer(runner, player);
        }

        /// <summary>
        /// Debug method to check current player state
        /// </summary>
        [ContextMenu("Debug: Show All Players")]
        public void DebugShowAllPlayers()
        {
            if (runner == null || !runner.IsRunning)
            {
                Debug.LogWarning("❌ Runner not running!");
                return;
            }

            Debug.Log("=== PLAYER STATE DEBUG ===");
            Debug.Log($"Active Players: {runner.ActivePlayers.Count()}");
            Debug.Log($"Local Player: {runner.LocalPlayer.PlayerId}");
            Debug.Log($"Is Server (Host): {runner.IsServer}");
            
            var allPlayerData = FindObjectsOfType<PlayerNetworkData>();
            Debug.Log($"\n📊 Total PlayerNetworkData in scene: {allPlayerData.Length}");
            
            foreach (var pd in allPlayerData)
            {
                if (pd.Object != null)
                {
                    Debug.Log($"  🎮 Player {pd.Object.InputAuthority.PlayerId}:");
                    Debug.Log($"     - Username: {pd.Username}");
                    Debug.Log($"     - Team: {pd.Team}");
                    Debug.Log($"     - NetworkId: {pd.Object.Id}");
                    Debug.Log($"     - HasInputAuthority: {pd.Object.HasInputAuthority}");
                    Debug.Log($"     - HasStateAuthority: {pd.Object.HasStateAuthority}");
                    Debug.Log($"     - IsValid: {pd.Object.IsValid}");
                }
            }
            
            Debug.Log("=========================");
        }

        /// <summary>
        /// Assign team and join order to a player (called from PlayerNetworkData.Spawned)
        /// </summary>
        public void AssignPlayerTeam(PlayerRef player)
        {
            // Find the player's NetworkData object (must be the local one we have authority over)
            var playerData = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(pd => pd.Object != null && pd.Object.InputAuthority == player);
            
            if (playerData == null)
            { Debug.LogError($"❌ Could not find PlayerNetworkData for player {player.PlayerId}");
                return;
            }
            
            // In Shared mode, each client assigns their OWN team (they have StateAuthority on their own object)
            if (!playerData.HasStateAuthority)
            {
                return;
            }
            
            // Skip if already assigned
            if (playerData.TeamAssigned)
            {
                return;
            }
            
            // Use PlayerCache to see ALL players' team assignments (including remote players)
            // This is crucial because FindObjectsOfType may not find remote players whose objects are transient
            var allCachedPlayers = PlayerNetworkData.PlayerCache.Values.ToList();
            
            // Calculate current team counts from already-assigned players (from cache)
            var assignedPlayers = allCachedPlayers.Where(p => p.TeamAssigned).ToList();
            int currentTeamACount = assignedPlayers.Count(p => p.Team == 0);
            int currentTeamBCount = assignedPlayers.Count(p => p.Team == 1);
            
            // DETERMINISTIC JOIN ORDER: Find first available join order to prevent conflicts
            // (Race condition: two players might calculate same order if cache is stale)
            int nextOrder = 0;
            var usedOrders = new HashSet<int>(assignedPlayers.Select(p => p.JoinOrder));
            while (usedOrders.Contains(nextOrder))
            {
                nextOrder++;
            }
            
            Debug.Log($"🔢 [AssignPlayerTeam] Calculated JoinOrder {nextOrder} (used orders: {string.Join(",", usedOrders)})");
            
            // Set host if this is first player (join order 0)
            if (nextOrder == 0)
            {
                CurrentHost = playerData.Object.InputAuthority;
            }
            
            // DETERMINISTIC TEAM: Assign based ONLY on JoinOrder (not team counts)
            // This prevents race conditions where both players see empty teams and choose the same one
            int assignedTeam = (nextOrder % 2 == 0) ? 0 : 1;
            
            Debug.Log($"🎲 [AssignPlayerTeam] Deterministic assignment: JoinOrder {nextOrder} → Team {assignedTeam} (TeamA:{currentTeamACount + (assignedTeam == 0 ? 1 : 0)}, TeamB:{currentTeamBCount + (assignedTeam == 1 ? 1 : 0)})");
            
            // Assign directly (we have StateAuthority on our own object)
            playerData.Team = assignedTeam;
            playerData.JoinOrder = nextOrder;
            playerData.TeamAssigned = true;
            
            // Update player cache immediately after assignment to prevent other concurrent assignments from seeing stale data
            playerData.UpdatePlayerCache();
            
            Debug.Log($"✅ [AssignPlayerTeam] {playerData.Username} → Team {assignedTeam}, JoinOrder {nextOrder} | Cache updated");
            
            // Notify UI of the change
            NotifyPlayerDataChanged();
            
            // Reposition if in GameScene
            RepositionPlayerToTeamSpawn(playerData.Object, assignedTeam);

            // Grant spawn immunity (same green-outline invincibility as respawn).
            // This covers both: initial round start and late joiners mid-match.
            // Use RPC so ALL clients see the immunity material + block blood.
            var health = playerData.GetComponent<ArtisansGuns.Game.PlayerHealth>();
            if (health != null)
            {
                health.RPC_StartImmunity();
                Debug.Log($"[AssignPlayerTeam] Spawn immunity broadcast for {playerData.Username}");
            }
        }
        
        // Reposiciona al jugador en el spawn point de su equipo
        private void RepositionPlayerToTeamSpawn(NetworkObject playerObject, int team)
        {
            if (playerObject == null) return;
            
            // Solo reposicionar en Sandbox
            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName != "Sandbox") return;
            
            var gameManager = FindObjectOfType<ArtisansGuns.Game.GameManager>();
            if (gameManager != null)
            {
                // Determine player index from JoinOrder for deterministic spawn selection.
                // JoinOrder 0,2,4 → Team A players 0,1,2  |  JoinOrder 1,3,5 → Team B players 0,1,2
                int playerIndex = 0;
                var playerData = playerObject.GetComponent<ArtisansGuns.Networking.PlayerNetworkData>();
                if (playerData != null)
                {
                    playerIndex = playerData.JoinOrder / 2;
                }
                
                Vector3 spawnPosition = gameManager.GetSpawnPositionForTeam(team, playerIndex);
                Quaternion spawnRotation = gameManager.GetSpawnRotationForTeam(team, playerIndex);
                
                // CharacterController blocks direct transform.position changes - must disable first
                var cc = playerObject.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                
                playerObject.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
                
                if (cc != null) cc.enabled = true;
                
                Debug.Log($"[RepositionPlayer] Team {team}, Index {playerIndex} → pos {spawnPosition}");
            }
        }
        
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        { // Debug.Log($"❌ Player left: {player.PlayerId}");
            
            // Track if the leaving player was host
            bool wasHost = (player == CurrentHost);
            
            // Update team counts
            var leavingPlayerData = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(pd => pd.Object != null && pd.Object.InputAuthority == player);
            
            if (leavingPlayerData != null)
            {
                if (leavingPlayerData.Team == 0)
                    teamACount--;
                else
                    teamBCount--; // Debug.Log($"👥 Team counts updated: Team A={teamACount}, Team B={teamBCount}");
            }
            
            // Notify listeners
            OnPlayerLeftRoom?.Invoke(player);
            
            // Check if game is in progress (ensure GameStateManager is spawned before accessing)
            var gsm = GameStateManager.Instance;
            bool gsmValid = gsm != null && gsm.Object != null && gsm.Object.IsValid;
            bool gameInProgress = gsmValid && gsm.GameInProgress;
            
            // ── CRITICAL: Save GSM state BEFORE Fusion can destroy it ──
            // When the StateAuthority holder disconnects, Fusion may destroy the GSM
            // on all clients. We need a backup to restore on the new GSM.
            if (gsmValid && (gameInProgress || gsm.PreStartActive || gsm.CountdownStarted))
            {
                gsm.SaveMatchState();
                Debug.Log($"[OnPlayerLeft] Saved GSM state (GameInProgress={gsm.GameInProgress}, Time={gsm.MatchTimeRemaining})");
                
                // Try to claim StateAuthority — if we get it before Fusion's cleanup,
                // the GSM survives and we don't need the backup at all.
                if (!gsm.Object.HasStateAuthority)
                {
                    gsm.Object.RequestStateAuthority();
                    Debug.Log("[OnPlayerLeft] Requested StateAuthority on GSM to prevent destruction");
                }
            }
            
            if (gameInProgress)
            { // Debug.Log($"🎮 Game in progress - player {player.PlayerId} data will persist for rejoin");
                // IMPORTANT: Don't despawn player objects during active game - allow rejoining
                // Objects will be cleaned up by Fusion after timeout if player doesn't return
            }
            else
            {
                // Clean up the player's network objects (lobby only)
                if (runner.IsServer || runner.GameMode == GameMode.Shared)
                {
                    var playerObjects = FindObjectsOfType<NetworkObject>();
                    foreach (var obj in playerObjects)
                    {
                        if (obj != null && obj.InputAuthority == player && obj.IsValid)
                        { // Debug.Log($"🗑️ Despawning object {obj.name} for player {player.PlayerId}");
                            // In Shared Mode, only despawn if we have state authority
                            if (runner.GameMode == GameMode.Shared && !obj.HasStateAuthority)
                                continue;
                            runner.Despawn(obj);
                        }
                    }
                }
            }
            
            // Transfer host if host left
            if (wasHost)
            {
                TransferHost(runner);
            }

            // Safety net: if the GSM was destroyed when the StateAuthority left,
            // the new host must respawn it so the game can continue.
            if (player != runner.LocalPlayer && runner.GameMode == GameMode.Shared)
            {
                StartCoroutine(EnsureGSMAfterPlayerLeft(runner));
            }

            // If we are the one who left, return to lobby
            if (player == runner.LocalPlayer)
            { // Debug.LogWarning("We left the room!");
                _ = LeaveRoom();
            }
        }
        
        /// <summary>
        /// Transfer host to the player with lowest join order (who joined first after previous host)
        /// </summary>
        private void TransferHost(NetworkRunner runner)
        {
            var allPlayers = FindObjectsOfType<PlayerNetworkData>()
                .Where(pd => pd.Object != null && pd.Object.IsValid)
                .OrderBy(pd => pd.JoinOrder)
                .ToList();
            
            if (allPlayers.Count > 0)
            {
                // New host is player with lowest join order still in room
                var newHostData = allPlayers[0];
                CurrentHost = newHostData.Object.InputAuthority; // Debug.Log($"👑 HOST TRANSFERRED to Player {CurrentHost.PlayerId} ({newHostData.Username}, join order: {newHostData.JoinOrder})"); // Debug.Log($"   All remaining players: {string.Join(", ", allPlayers.Select(p => $"{p.Username}(Order:{p.JoinOrder})"))}");
                
                // Update isHost flag for local player
                if (CurrentHost == runner.LocalPlayer)
                {
                    isHost = true; // Debug.Log("🎉 WE are now HOST! Updating UI...");
                    
                    // Trigger UI refresh to show host controls
                    // The LobbyUIController will detect this and update the UI accordingly
                    OnPlayerDataChanged?.Invoke();
                }
                else
                {
                    isHost = false; // Debug.Log($"ℹ️ New host is {newHostData.Username} (we are not host)");
                }
                
                // If the room is currently in a game, keep it open for rejoining
                // CRITICAL: Check if GameStateManager is spawned before accessing networked properties
                if (GameStateManager.Instance != null && 
                    GameStateManager.Instance.Object != null && 
                    GameStateManager.Instance.Object.IsValid && 
                    GameStateManager.Instance.GameInProgress)
                { // Debug.Log("🎮 Game in progress - keeping room open for rejoining");
                }
                else if (GameStateManager.Instance != null && GameStateManager.Instance.Object == null)
                { // Debug.LogWarning("⚠️ GameStateManager exists but not spawned - cannot check GameInProgress");
                }
            }
            else
            {
                CurrentHost = PlayerRef.None; // Debug.LogWarning("⚠️ No players left to transfer host to - room will close");
            }
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            bool unexpected = !isLeavingRoom && shutdownReason != ShutdownReason.Ok;

            if (unexpected)
            {
                Debug.LogWarning($"[NetworkManager] Runner shutdown (UNEXPECTED): {shutdownReason}");
            }
            
            // Clear all session state on shutdown to prevent stale data in next session
            ResetSessionState();
            ArtisansGuns.Audio.ComboKillManager.Instance?.ResetForNewMatch();
            CurrentRoomCode = null;

            // Dismiss a stuck loading screen so the user isn't trapped
            if (PreWarmManager.Instance != null && PreWarmManager.Instance.IsLoading)
            {
                Debug.LogWarning("[NetworkManager] Dismissing loading screen after shutdown");
                PreWarmManager.Instance.HideLoading();
            }

            // If the disconnect was unexpected and we're in a game scene,
            // go back to lobby so the player can retry.
            string currentScene = SceneManager.GetActiveScene().name;
            bool inGameScene = currentScene == "Sandbox" || currentScene.StartsWith("Map");
            if (unexpected && inGameScene)
            {
                Debug.LogWarning($"[NetworkManager] Unexpected shutdown in {currentScene} — returning to LobbyScene");
                isNetworkReady = false;
                SceneManager.LoadScene("LobbyScene");
                // Re-initialize after settling
                RetryNetworkingAfterDelay();
            }

            // Free unused assets and run GC to reclaim memory on low-RAM devices
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
            
            OnDisconnected?.Invoke();
        }

        /// <summary>Delayed re-initialization after an unexpected disconnect sends us back to lobby.</summary>
        private async void RetryNetworkingAfterDelay()
        {
            await Task.Delay(2000);
            try
            {
                await InitializeNetworking();
                Debug.Log("[NetworkManager] Re-initialized networking after unexpected disconnect");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NetworkManager] Re-init after disconnect failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset all session-specific state: player cache, team counts, join order.
        /// Called on CreateRoom, JoinRoom, and OnShutdown to prevent stale data.
        /// </summary>
        private void ResetSessionState()
        {
            PlayerNetworkData.ClearPlayerCache();
            teamACount = 0;
            teamBCount = 0;
            nextJoinOrder = 0;
            Debug.Log("[NetworkManager] Session state reset (cache, teams, joinOrder)");
        }

        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        { // Debug.Log($"📋 ==================== SESSION LIST UPDATED ===================="); // Debug.Log($"📋 Room list updated: {sessionList.Count} rooms available"); // Debug.Log($"📋 Runner: {runner.name}, Mode: {runner.Mode}, Region: {runner.SessionInfo.Region}");
            
            if (sessionList.Count > 0)
            {
                foreach (var session in sessionList)
                { // Debug.Log($"   🏠 Room: {session.Name}"); // Debug.Log($"      IsOpen: {session.IsOpen}, IsVisible: {session.IsVisible}"); // Debug.Log($"      Players: {session.PlayerCount}/{session.MaxPlayers}"); // Debug.Log($"      Region: {session.Region}");
                    
                    if (session.Properties != null && session.Properties.Count > 0)
                    { // Debug.Log($"      Properties:");
                        foreach (var prop in session.Properties)
                        { // Debug.Log($"         {prop.Key} = {prop.Value}");
                        }
                    }
                }
            }
            else
            { // Debug.LogWarning("   ⚠️ No rooms found in session list");
            } // Debug.Log($"📋 ==============================================================");
            
            cachedSessions = new List<SessionInfo>(sessionList);
            OnRoomListUpdated?.Invoke(sessionList);
        }

        public void OnConnectedToServer(NetworkRunner runner)
        { // Debug.Log("✅ Connected to Photon server");
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            // Only log as warning if it's an unexpected disconnection
            if (isLeavingRoom)
            { // Debug.Log($"✅ Disconnected from server (expected): {reason}");
                isLeavingRoom = false; // Reset flag
            }
            else
            { // Debug.LogWarning($"⚠️ Disconnected from server (unexpected): {reason}");
            }
            
            // Always reset network state on disconnect
            isNetworkReady = false;
            
            OnDisconnected?.Invoke();
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        { // Debug.LogError($"❌ Connection failed: {reason}");
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            // Input will be handled in game scene
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
        {
        }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            // Accept all connections (you can add validation here)
            request.Accept();
        }

        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
        {
        }

        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
        {
        }

        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        { // Debug.Log("🔄 Host migration started");
        }

        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
        }

        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
        {
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
            string sceneName = SceneManager.GetActiveScene().name;
            bool isGameScene = sceneName == "Sandbox" || sceneName.StartsWith("Map");

            // For game scenes: run pre-warm FIRST, then spawn the player
            if (isGameScene && PreWarmManager.Instance != null && PreWarmManager.Instance.IsLoading)
            {
                Debug.Log($"[OnSceneLoadDone] Game scene '{sceneName}' — deferring spawn until pre-warm completes");
                PreWarmManager.Instance.RunPreWarm(() =>
                {
                    // Use this.runner (current field) in case the captured parameter became stale
                    var activeRunner = this.runner;
                    if (activeRunner == null || activeRunner.IsShutdown)
                    {
                        Debug.LogWarning("[OnSceneLoadDone] Runner died during pre-warm — cannot spawn player");
                        return;
                    }
                    Debug.Log("[OnSceneLoadDone] Pre-warm complete — now spawning player");
                    SpawnPlayerIfNeeded(activeRunner, sceneName);
                    EnsureGSM(activeRunner, sceneName);
                });
                return;
            }
            
            // Non-game scenes or no pre-warm manager: spawn immediately
            SpawnPlayerIfNeeded(runner, sceneName);
            EnsureGSM(runner, sceneName);
        }

        /// <summary>Spawn the local player if they don't already exist in the scene.</summary>
        private void SpawnPlayerIfNeeded(NetworkRunner runner, string sceneName)
        {
            if (runner == null || runner.IsShutdown)
            {
                Debug.LogWarning($"[SpawnPlayerIfNeeded] Runner is null or shutdown — cannot spawn in {sceneName}");
                return;
            }

            var existingPlayer = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(pd => pd != null && pd.Object != null && pd.Object.InputAuthority == runner.LocalPlayer);

            if (existingPlayer == null)
            {
                Debug.Log($"[OnSceneLoadDone] Spawning local player in {sceneName}");
                SpawnPlayer(runner, runner.LocalPlayer);
            }
            else
            {
                Debug.Log($"[OnSceneLoadDone] Player already exists in {sceneName}, skipping spawn");
            }
        }

        /// <summary>
        /// Safety net: verify the local player exists in the game scene.
        /// Called from HandleAppResume when runner is alive but player may be missing.
        /// </summary>
        private void EnsureLocalPlayerSpawned()
        {
            if (runner == null || !runner.IsRunning) return;

            string sceneName = SceneManager.GetActiveScene().name;
            bool isGameScene = sceneName == "Sandbox" || sceneName.StartsWith("Map");
            if (!isGameScene) return;

            // If PreWarm is still running, the callback chain will handle spawning
            if (PreWarmManager.Instance != null && PreWarmManager.Instance.IsLoading) return;

            var existingPlayer = FindObjectsOfType<PlayerNetworkData>()
                .FirstOrDefault(pd => pd != null && pd.Object != null && pd.Object.InputAuthority == runner.LocalPlayer);

            if (existingPlayer == null)
            {
                Debug.LogWarning("[EnsureLocalPlayerSpawned] No local player found in game scene — force spawning");
                SpawnPlayer(runner, runner.LocalPlayer);
            }
        }

        /// <summary>Ensure GameStateManager exists in the game scene.</summary>
        private void EnsureGSM(NetworkRunner runner, string sceneName)
        {
            if (GameStateManager.Instance == null && (sceneName == "Sandbox" || sceneName.StartsWith("Map")))
            {
                var existingGSM = FindObjectOfType<GameStateManager>();
                if (existingGSM != null && existingGSM.gameObject.activeInHierarchy)
                {
                    Debug.Log("[OnSceneLoadDone] GSM Instance was null but object exists — re-registering");
                    GameStateManager.Instance = existingGSM;
                }
                else if (isHost && gameStateManagerPrefab != null)
                {
                    Debug.Log("[OnSceneLoadDone] GSM missing — host re-spawning GameStateManager");
                    runner.Spawn(gameStateManagerPrefab, Vector3.zero, Quaternion.identity, PlayerRef.None);
                }
                else
                {
                    Debug.LogWarning($"[OnSceneLoadDone] GSM still null (isHost={isHost}, prefab={(gameStateManagerPrefab != null)})");
                }
            }
        }

        /// <summary>
        /// After a player leaves, wait a beat for Fusion to finish cleanup, then
        /// check if the GSM was destroyed (StateAuthority left). If so, respawn it.
        /// </summary>
        private System.Collections.IEnumerator EnsureGSMAfterPlayerLeft(NetworkRunner runner)
        {
            // Wait for Fusion to process the disconnect and potential object destruction
            yield return new WaitForSeconds(0.5f);

            if (runner == null || !runner.IsRunning) yield break;

            bool gsmMissing = GameStateManager.Instance == null 
                           || GameStateManager.Instance.gameObject == null;

            // Also check if the NetworkObject was despawned (Object becomes null)
            if (!gsmMissing && GameStateManager.Instance.Object == null)
            {
                Debug.Log("[EnsureGSM] GSM exists but NetworkObject was despawned — destroying stale GO");
                Destroy(GameStateManager.Instance.gameObject);
                GameStateManager.Instance = null;
                gsmMissing = true;
            }

            // If GSM survived (RequestStateAuthority worked), ensure the timer coroutine is running
            if (!gsmMissing && GameStateManager.Instance.Object != null && GameStateManager.Instance.Object.IsValid)
            {
                Debug.Log("[EnsureGSM] GSM survived host-leave — clearing backup");
                GameStateManager.Backup = default; // Clear unused backup
                yield break;
            }

            if (gsmMissing)
            {
                bool hasBackup = GameStateManager.Backup.Valid;
                
                // Try to find an orphaned GSM in the scene
                var existingGSM = FindObjectOfType<GameStateManager>();
                if (existingGSM != null && existingGSM.Object != null && existingGSM.Object.IsValid)
                {
                    GameStateManager.Instance = existingGSM;
                    Debug.Log("[EnsureGSM] Re-registered orphaned GSM after player left");
                    
                    // Restore state if we have a backup and StateAuthority
                    if (hasBackup && existingGSM.HasStateAuthority)
                    {
                        existingGSM.RestoreMatchState();
                    }
                }
                else if (gameStateManagerPrefab != null)
                {
                    Debug.Log($"[EnsureGSM] GSM destroyed after player left — respawning (hasBackup={hasBackup})");
                    if (existingGSM != null) Destroy(existingGSM.gameObject);
                    runner.Spawn(gameStateManagerPrefab, Vector3.zero, Quaternion.identity, PlayerRef.None);
                    // RestoreMatchState will be called from GSM.Spawned() if Backup.Valid is true
                }
            }
        }

        public void OnSceneLoadStart(NetworkRunner runner)
        {
            // CRITICAL: Explicitly despawn local player's NetworkObject before scene unloads.
            // In Fusion Shared Mode, each client has StateAuthority over its own objects.
            // If we don't despawn here, the scene unload destroys the GameObject but Fusion
            // still considers the NetworkObject alive. The next snapshot sent to remote clients
            // shows it as "exists then destroyed in same tick" → "spawned and despawned in same tick" warning.
            // That ghost NetworkObject also corrupts the URP camera stack reference on the joiner.
            if (runner == null || !runner.IsRunning) return;
            
            // Only despawn player prefabs (those with PlayerNetworkData) owned by this client.
            // Be explicit to avoid despawning GameStateManager or other persistent networked objects.
            var myPlayerObjects = FindObjectsOfType<PlayerNetworkData>()
                .Where(pd => pd != null && pd.Object != null && pd.Object.HasStateAuthority)
                .Select(pd => pd.Object)
                .ToArray();
                
            foreach (var obj in myPlayerObjects)
            {
                Debug.Log($"[OnSceneLoadStart] Despawning lobby player: {obj.name} (Player {obj.InputAuthority.PlayerId}) before scene transition");
                runner.Despawn(obj);
            }
        }

        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
        {
        }

        public void OnDisconnectedFromServer(NetworkRunner runner)
        {
        }

        // ===================================
        // HELPER METHODS
        // ===================================

        /// <summary>
        /// Ensures the NetworkRunner has a NetworkSceneManagerDefault component.
        /// Without a scene manager, Fusion cannot properly manage remote object proxies
        /// or persist NetworkObjects across scene transitions.
        /// </summary>
        private Fusion.INetworkSceneManager EnsureSceneManager(NetworkRunner runner)
        {
            var existing = runner.GetComponent<NetworkSceneManagerDefault>();
            if (existing != null)
            {
                return existing;
            }
            
            var sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();
            return sceneManager;
        }

        /// <summary>
        /// Gets a SceneRef for a scene by name from Build Settings.
        /// Used to specify initial scene in StartGameArgs so Fusion loads it with proper NetworkObject management.
        /// </summary>
        private SceneRef GetSceneRef(string sceneName)
        {
            // Get scene build index from Build Settings
            int sceneIndex = -1;
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                if (scenePath.Contains(sceneName))
                {
                    sceneIndex = i;
                    break;
                }
            }

            if (sceneIndex >= 0)
            {
                return SceneRef.FromIndex(sceneIndex);
            }
            else
            {
                Debug.LogError($"[NetworkManager] {sceneName} not found in Build Settings!");
                return default;
            }
        }
        
        private string GetCurrentUsername()
        {
            if (AuthManager.Instance != null)
            {
                var user = AuthManager.Instance.GetCurrentUser();
                return user?.username ?? "Player";
            }
            return "Player";
        }

        private string GetCurrentCharacter()
        {
            return PlayerPrefs.GetString("selected_character", "CRIMSON");
        }
    }
}








