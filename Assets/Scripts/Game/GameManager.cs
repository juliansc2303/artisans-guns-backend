using UnityEngine;
using Fusion;
using ArtisansGuns.Networking;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// GameManager - Maneja el estado del juego y spawn de jugadores
    /// PERSISTENT: DontDestroyOnLoad - stays alive across map changes
    /// Finds MapSpawnManager in each map scene for spawn points
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Prefabs")]
        [SerializeField] private GameObject playerPrefab;

        // Spawn management
        private NetworkRunner runner;
        private MapSpawnManager currentMapSpawnManager;

        private void Awake()
        {
            // Singleton pattern with DontDestroyOnLoad
            if (Instance != null && Instance != this)
            {
                // Debug.Log("⚠️ [GameManager] Duplicate instance found, destroying...");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Debug.Log("✅ [GameManager] Initialized as persistent singleton");
        }

        private void Start()
        {
            runner = FindObjectOfType<NetworkRunner>();

            if (runner != null && runner.IsRunning)
            {
                // Debug.Log("ℹ️ [GameManager] NetworkRunner found - ready for player spawning");
            }
            
            // Find spawn manager in current scene
            FindMapSpawnManager();
        }
        
        private void OnEnable()
        {
            // Subscribe to scene loaded event to find new MapSpawnManager
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        
        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        
        /// <summary>
        /// Called when a new scene is loaded - find the MapSpawnManager
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // Debug.Log($"🗺️ [GameManager] Scene loaded: {scene.name}");
            FindMapSpawnManager();
        }
        
        /// <summary>
        /// Find MapSpawnManager in current scene
        /// </summary>
        private void FindMapSpawnManager()
        {
            currentMapSpawnManager = FindObjectOfType<MapSpawnManager>();
            
            if (currentMapSpawnManager != null)
            {
                // Debug.Log($"✅ [GameManager] Found MapSpawnManager for map: {currentMapSpawnManager.MapName}");
            }
            else
            {
                // Debug.LogWarning("⚠️ [GameManager] No MapSpawnManager found in scene! Players will spawn at random positions.");
            }
        }

        public void SpawnPlayer(PlayerRef player)
        {
            if (playerPrefab == null)
            {
                // Debug.LogError("❌ Player prefab not assigned!");
                return;
            }

            // Get player's team from network data
            var runner = FindObjectOfType<NetworkRunner>();
            if (runner == null)
            {
                // Debug.LogError("❌ NetworkRunner not found!");
                return;
            }

            // Get the player's network data to retrieve their team
            var playerObject = runner.GetPlayerObject(player);
            int playerTeam = 0; // Default to Team A

            if (playerObject != null)
            {
                var networkData = playerObject.GetComponent<PlayerNetworkData>();
                if (networkData != null)
                {
                    playerTeam = networkData.Team;
                    // Debug.Log($"🎯 Player {player.PlayerId} assigned to Team {playerTeam}");
                }
            }

            Vector3 spawnPosition = GetSpawnPositionForTeam(playerTeam);
            Quaternion spawnRotation = GetSpawnRotationForTeam(playerTeam);

            // Debug.Log($"🎮 Spawning player at {spawnPosition} (Team {playerTeam})");

            var spawnedObject = runner.Spawn(
                playerPrefab,
                spawnPosition,
                spawnRotation,
                player
            );

            if (spawnedObject != null)
            {
                // Debug.Log($"✅ Player spawned successfully: {player.PlayerId} at Team {playerTeam} spawn point");
            }
            else
            {
                // Debug.LogError($"❌ Failed to spawn player: {player.PlayerId}");
            }
        }

        /// <summary>
        /// Get spawn position for team - delegates to MapSpawnManager
        /// Called by NetworkManager when spawning players
        /// </summary>
        public Vector3 GetSpawnPositionForTeam(int team, int playerIndex = -1)
        {
            // ALWAYS try to find MapSpawnManager if we don't have it yet
            // This ensures we find it even if GameManager loaded before the scene
            if (currentMapSpawnManager == null)
            {
                Debug.LogWarning("[GameManager] MapSpawnManager not cached, searching now...");
                FindMapSpawnManager();
            }
            
            if (currentMapSpawnManager != null)
            {
                Vector3 spawnPos = currentMapSpawnManager.GetSpawnPosition(team, playerIndex);
                Debug.Log($"[GameManager] SpawnPos for Team={team} Index={playerIndex} → {spawnPos} (map={currentMapSpawnManager.MapName})");
                return spawnPos;
            }
            
            // Fallback if no MapSpawnManager found
            Debug.LogWarning($"[GameManager] No MapSpawnManager! Using RANDOM spawn for Team {team}");
            return GetRandomSpawnPosition();
        }
        
        /// <summary>
        /// Get the safest (least-occupied) spawn position for team — used by respawn logic.
        /// Picks the spawn point farthest from all alive players; scatters if only one
        /// point exists and someone is standing on it.
        /// </summary>
        public Vector3 GetSafeSpawnPositionForTeam(int team)
        {
            if (currentMapSpawnManager == null)
                FindMapSpawnManager();

            if (currentMapSpawnManager != null)
            {
                Vector3 safePos = currentMapSpawnManager.GetSafestSpawnPosition(team);
                Debug.Log($"[GameManager] SafeSpawnPos for Team={team} → {safePos}");
                return safePos;
            }

            Debug.LogWarning($"[GameManager] No MapSpawnManager — using random spawn for Team {team}");
            return GetRandomSpawnPosition();
        }

        /// <summary>
        /// Get spawn rotation for team - delegates to MapSpawnManager
        /// </summary>
        public Quaternion GetSpawnRotationForTeam(int team, int playerIndex = -1)
        {
            if (currentMapSpawnManager != null)
            {
                return currentMapSpawnManager.GetSpawnRotation(team, playerIndex);
            }
            
            return Quaternion.identity;
        }
        
        /// <summary>
        /// Fallback random spawn if no MapSpawnManager
        /// </summary>
        private Vector3 GetRandomSpawnPosition()
        {
            return new Vector3(
                Random.Range(-10f, 10f),
                1f,
                Random.Range(-10f, 10f)
            );
        }

        /// <summary>
        /// Called when player is ready to spawn
        /// </summary>
        public void OnPlayerReady(PlayerRef player)
        {
            SpawnPlayer(player);
        }
    }
}
