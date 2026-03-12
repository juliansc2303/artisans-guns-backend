using UnityEngine;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// MapSpawnManager - Manages spawn points for a specific map
    /// Place this component in each map scene (Sandbox, Desert, etc.)
    /// GameManager will find this and use its spawn points
    /// </summary>
    public class MapSpawnManager : MonoBehaviour
    {
        [Header("Map Info")]
        [Tooltip("Name of this map (e.g., 'Sandbox', 'Desert', 'Factory')")]
        [SerializeField] private string mapName = "Sandbox";
        
        [Tooltip("Maximum players for this map")]
        [SerializeField] private int maxPlayers = 10;
        
        [Header("Spawn Points")]
        [Tooltip("Spawn points for Team A (Blue)")]
        [SerializeField] private Transform[] spawnPointsTeamA;
        
        [Tooltip("Spawn points for Team B (Red)")]
        [SerializeField] private Transform[] spawnPointsTeamB;
        
        // Singleton instance for current map
        public static MapSpawnManager Instance { get; private set; }
        
        // Spawn point tracking (legacy counter - only used when no playerIndex is provided)
        private int nextSpawnIndexTeamA = 0;
        private int nextSpawnIndexTeamB = 0;
        
        // Public getters
        public string MapName => mapName;
        public int MaxPlayers => maxPlayers;
        
        private void Awake()
        {
            // Register as the current map's spawn manager
            if (Instance != null && Instance != this)
            {
                // Debug.LogWarning($"⚠️ [MapSpawnManager] Multiple MapSpawnManagers in scene! Destroying duplicate.");
                Destroy(gameObject);
                return;
            }
            
            Instance = this;
            // Debug.Log($"🗺️ [MapSpawnManager] Map '{mapName}' initialized with {spawnPointsTeamA?.Length ?? 0} Team A spawns, {spawnPointsTeamB?.Length ?? 0} Team B spawns");
        }
        
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
        
        /// <summary>
        /// Get spawn position for a specific team
        /// </summary>
        public Vector3 GetSpawnPosition(int team, int playerIndex = -1)
        {
            Transform[] spawnPoints = team == 0 ? spawnPointsTeamA : spawnPointsTeamB;
            
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                Debug.LogWarning($"[MapSpawnManager] No spawn points configured for Team {team}! Using random position.");
                return GetRandomSpawnPosition();
            }
            
            // Deterministic: if playerIndex given, use it directly (same on all clients)
            // Fallback: use internal counter (legacy, non-deterministic)
            int spawnIndex;
            if (playerIndex >= 0)
            {
                spawnIndex = playerIndex % spawnPoints.Length;
            }
            else
            {
                spawnIndex = (team == 0 ? nextSpawnIndexTeamA : nextSpawnIndexTeamB) % spawnPoints.Length;
                if (team == 0) nextSpawnIndexTeamA++; else nextSpawnIndexTeamB++;
            }
            
            Debug.Log($"[MapSpawnManager] Team={team} playerIdx={playerIndex} spawnIdx={spawnIndex}/{spawnPoints.Length} name={spawnPoints[spawnIndex].name} pos={spawnPoints[spawnIndex].position}");
            return spawnPoints[spawnIndex].position;
        }
        
        /// <summary>
        /// Get spawn rotation for a specific team.
        /// playerIndex: pass JoinOrder/2 for deterministic result. -1 uses last counter value.
        /// </summary>
        public Quaternion GetSpawnRotation(int team, int playerIndex = -1)
        {
            Transform[] spawnPoints = team == 0 ? spawnPointsTeamA : spawnPointsTeamB;
            
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return Quaternion.identity;
            }
            
            int spawnIndex;
            if (playerIndex >= 0)
            {
                spawnIndex = playerIndex % spawnPoints.Length;
            }
            else
            {
                spawnIndex = team == 0 ? 
                    (nextSpawnIndexTeamA - 1 + spawnPoints.Length) % spawnPoints.Length : 
                    (nextSpawnIndexTeamB - 1 + spawnPoints.Length) % spawnPoints.Length;
            }
            
            return spawnPoints[spawnIndex].rotation;
        }
        
        /// <summary>
        /// Returns the spawn point that is farthest from all living players (safest for respawn).
        /// If only one spawn point exists and a player is standing on it, a random XZ scatter
        /// offset is applied so the respawning player does not clip into anyone.
        /// </summary>
        public Vector3 GetSafestSpawnPosition(int team)
        {
            const float MIN_SAFE_RADIUS = 2.5f;   // metres — player counts as "too close"
            const float SCATTER_RADIUS  = 3.0f;   // metres — how far to scatter when forced

            Transform[] spawnPoints = team == 0 ? spawnPointsTeamA : spawnPointsTeamB;

            if (spawnPoints == null || spawnPoints.Length == 0)
                return GetRandomSpawnPosition();

            // Collect alive player positions
            var allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);

            Vector3 bestPos      = spawnPoints[0].position;
            float   bestMinDist  = -1f;

            foreach (var sp in spawnPoints)
            {
                if (sp == null) continue;

                float minDist = float.MaxValue;
                foreach (var ph in allPlayers)
                {
                    if (ph == null || ph.IsDead) continue;
                    float d = Vector3.Distance(sp.position, ph.transform.position);
                    if (d < minDist) minDist = d;
                }

                // No alive players at all — use first point immediately
                if (minDist == float.MaxValue)
                    return spawnPoints[0].position;

                if (minDist > bestMinDist)
                {
                    bestMinDist = minDist;
                    bestPos     = sp.position;
                }
            }

            // If even the best spawn is inside someone's personal space, scatter
            if (bestMinDist >= 0f && bestMinDist < MIN_SAFE_RADIUS)
            {
                Vector2 circle = Random.insideUnitCircle.normalized * SCATTER_RADIUS;
                bestPos += new Vector3(circle.x, 0f, circle.y);
                Debug.Log($"[MapSpawnManager] All spawn points occupied — scattered respawn to {bestPos}");
            }

            return bestPos;
        }

        /// <summary>
        /// Fallback random spawn position if no spawn points configured
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
        /// Validate spawn points setup (called from Inspector or Editor script)
        /// </summary>
        private void OnValidate()
        {
            if (spawnPointsTeamA == null || spawnPointsTeamA.Length == 0)
            {
                // Debug.LogWarning($"⚠️ [MapSpawnManager] Map '{mapName}' has no Team A spawn points!");
            }
            
            if (spawnPointsTeamB == null || spawnPointsTeamB.Length == 0)
            {
                // Debug.LogWarning($"⚠️ [MapSpawnManager] Map '{mapName}' has no Team B spawn points!");
            }
        }
        
#if UNITY_EDITOR
        /// <summary>
        /// Draw spawn points in Scene view for easier setup
        /// </summary>
        private void OnDrawGizmos()
        {
            // Draw Team A spawns (Blue)
            if (spawnPointsTeamA != null)
            {
                Gizmos.color = Color.blue;
                foreach (var spawn in spawnPointsTeamA)
                {
                    if (spawn != null)
                    {
                        Gizmos.DrawWireSphere(spawn.position, 0.5f);
                        Gizmos.DrawRay(spawn.position, spawn.forward * 2f);
                    }
                }
            }
            
            // Draw Team B spawns (Red)
            if (spawnPointsTeamB != null)
            {
                Gizmos.color = Color.red;
                foreach (var spawn in spawnPointsTeamB)
                {
                    if (spawn != null)
                    {
                        Gizmos.DrawWireSphere(spawn.position, 0.5f);
                        Gizmos.DrawRay(spawn.position, spawn.forward * 2f);
                    }
                }
            }
        }
#endif
    }
}
