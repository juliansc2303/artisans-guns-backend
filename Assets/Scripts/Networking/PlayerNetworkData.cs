using UnityEngine;
using Fusion;
using ArtisansGuns.Auth;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ArtisansGuns.Networking
{
    /// <summary>
    /// PlayerNetworkData - Representa los datos de un jugador en la red
    /// Sincronizado automÃ¡ticamente por Fusion
    /// </summary>
    public class PlayerNetworkData : NetworkBehaviour
    {
        // === STATIC PLAYER REGISTRY ===
        // Persists player data snapshots even when remote NetworkObjects are transient (Shared Mode)
        // Updated every tick in Spawned()/Render(), used by LobbyUIController for reliable discovery
        
        // Track despawn times to prevent rapid respawn loops
        private static Dictionary<int, float> lastDespawnTime = new Dictionary<int, float>();
        private const float RESPAWN_COOLDOWN = 2.0f; // Wait 2 seconds before allowing respawn
        
        public struct PlayerDataSnapshot
        {
            public string Username;
            public string CharacterName;
            public string SelectedAgent;
            public int CharacterType;
            public bool IsReady;
            public int Team;
            public int JoinOrder;
            public bool TeamAssigned;
            public int Level;
            public string PrimaryWeapon;
            public string SecondaryWeapon;
            public string KnifeWeapon;
            public string PrimarySkin;
            public string SecondarySkin;
            public int Kills;
            public int Deaths;
            public int Headshots;
            public int CurrentStreak;
            public int BestStreak;
            public string SelectedHat;
            public string Ability1;
            public string Ability2;
            public string Ultimate;
            public PlayerRef PlayerRef;
            public NetworkId NetworkId;
            public bool HasInputAuthority;
        }

        public static readonly Dictionary<PlayerRef, PlayerDataSnapshot> PlayerCache = new Dictionary<PlayerRef, PlayerDataSnapshot>();

        /// <summary>
        /// Clear the static player cache (call when leaving the room/session)
        /// </summary>
        public static void ClearPlayerCache()
        {
            PlayerCache.Clear();
            Debug.Log("[PlayerNetworkData] PlayerCache cleared");
        }

        // Guard flag to prevent multiple Spawned() executions on the same instance
        private bool hasSpawned = false;
        
        [Networked] public NetworkString<_32> Username { get; set; }
        [Networked] public NetworkString<_32> CharacterName { get; set; } // User's actual character name (e.g. "sea") 
        [Networked] public NetworkString<_32> SelectedAgent { get; set; } // Selected agent (e.g. "CRIMSON")
        [Networked] public int CharacterType { get; set; } // 0=CRIMSON, 1=VIBE, 2=SIGHT, 3=PATO (derived from SelectedAgent)
        [Networked] public NetworkBool IsReady { get; set; }
        [Networked] public NetworkBool InGame { get; set; } // True when player is in active game (GameScene)
        [Networked] public int Team { get; set; } // 0=Team A, 1=Team B
        [Networked] public int JoinOrder { get; set; } // Order in which player joined (0=first/host, 1=second, etc.)
        [Networked] public NetworkBool TeamAssigned { get; set; } // Flag to track if team has been assigned
        [Networked] public int Level { get; set; } // Player level
        [Networked] public NetworkString<_32> PrimaryWeapon { get; set; } // Primary weapon ID
        [Networked] public NetworkString<_32> SecondaryWeapon { get; set; } // Secondary weapon ID
        [Networked] public NetworkString<_32> KnifeWeapon { get; set; } // Knife skin ID
        [Networked] public NetworkString<_32> PrimarySkin { get; set; } // Primary weapon skin ID
        [Networked] public NetworkString<_32> SecondarySkin { get; set; } // Secondary weapon skin ID
        [Networked] public int Kills { get; set; }   // Kill count this match
        [Networked] public int Deaths { get; set; }  // Death count this match
        [Networked] public int Headshots { get; set; }  // Headshot kill count this match
        [Networked] public int CurrentStreak { get; set; }  // Current kill streak (resets on death)
        [Networked] public int BestStreak { get; set; }  // Best kill streak this match
        [Networked] public NetworkString<_32> SelectedHat { get; set; }
        [Networked] public NetworkString<_32> Ability1 { get; set; }
        [Networked] public NetworkString<_32> Ability2 { get; set; }
        [Networked] public NetworkString<_32> Ultimate { get; set; }

        public override void Spawned()
        {
            // Guard against multiple Spawned() calls from UpdateRemotePrefabs
            if (hasSpawned)
            {
                return;
            }
            hasSpawned = true;
            
            // Check if this is a re-spawn (team already assigned)
            bool isRespawn = TeamAssigned;
            
            // Each client initializes their own player data via RPC
            if (HasInputAuthority)
            {
                // Check if we already have data (respawn case)
                bool hasExistingData = !string.IsNullOrEmpty(Username.ToString()) && 
                                       !string.IsNullOrEmpty(CharacterName.ToString());
                
                if (hasExistingData)
                {
                    // Debug.Log($"â†©ï¸ Player respawned with existing data: {Username} - {CharacterName} (CharacterType: {CharacterType})");
                    
                    // Only request team assignment if not already assigned
                    if (!isRespawn && NetworkManager.Instance != null)
                    {
                        // Debug.Log($"ðŸŽ¯ Requesting team assignment for respawned player {Username}");
                        NetworkManager.Instance.AssignPlayerTeam(Object.InputAuthority);
                    }
                    
                    return; // Skip data initialization, already have it
                }
                
                // NEW SPAWN: Initialize data from AuthManager/PlayerPrefs
                // Debugging: Check AuthManager state
                // Debug.Log($"ðŸ” DEBUG - AuthManager.Instance exists: {AuthManager.Instance != null}");
                if (AuthManager.Instance != null)
                {
                    // Debug.Log($"ðŸ” DEBUG - IsLoggedIn: {AuthManager.Instance.IsLoggedIn()}");
                }
                
                // This player belongs to us, send our data to the server
                string username = "";
                string characterName = ""; // This should be the user's character name (e.g. "sea"), not the agent
                
                if (AuthManager.Instance != null)
                {
                    var user = AuthManager.Instance.GetCurrentUser();
                    if (user != null && !string.IsNullOrEmpty(user.username))
                    {
                        username = user.username;
                        characterName = user.characterName; // Get the actual character name (e.g. "sea")
                        // Debug.Log($"âœ… Got username from AuthManager: '{username}'");
                        // Debug.Log($"âœ… Got characterName from AuthManager: '{characterName}'");
                    }
                    else
                    {
                        // Debug.LogWarning($"âš ï¸ AuthManager.GetCurrentUser() returned null or empty username");
                    }
                }
                
                // Fallback to PlayerPrefs if AuthManager didn't have it
                if (string.IsNullOrEmpty(username) && PlayerPrefs.HasKey("user_username"))
                {
                    username = PlayerPrefs.GetString("user_username", "");
                    // Debug.Log($"ðŸ“¦ Got username from PlayerPrefs (fallback): '{username}'");
                }
                
                if (string.IsNullOrEmpty(characterName) && PlayerPrefs.HasKey("user_characterName"))
                {
                    characterName = PlayerPrefs.GetString("user_characterName", "DefaultCharacter");
                    // Debug.Log($"ðŸ“¦ Got characterName from PlayerPrefs (fallback): '{characterName}'");
                }

                // Get selected agent and loadout data - THIS IS SEPARATE FROM CHARACTER NAME
                string selectedAgent = "CRIMSON";
                int level = 1;
                string primaryWeapon = "PHANTOM";
                string secondaryWeapon = "GHOST";
                string knifeWeapon = "default"; // Default knife skin
                string primarySkin = "default";
                string secondarySkin = "default";
                string selectedHat = "none";
                string ability1 = "smoke_grenade";
                string ability2 = "dash";
                string ultimate = "crimson_ultimate";
                
                // Try LoadoutManager for selected agent (this is the agent like "CRIMSON", separate from character name)
                if (ArtisansGuns.Managers.LoadoutManager.Instance != null && 
                    ArtisansGuns.Managers.LoadoutManager.Instance.IsInitialized())
                {
                    var loadout = ArtisansGuns.Managers.LoadoutManager.Instance.GetLoadout();
                    
                    // Use selected agent from loadout (this is the agent like "CRIMSON", not the user's character name)
                    if (!string.IsNullOrEmpty(loadout.selectedCharacter))
                    {
                        selectedAgent = loadout.selectedCharacter.ToUpper();
                        // Debug.Log($"âœ… Got selected agent from LoadoutManager: '{selectedAgent}'");
                    }
                    
                    level = loadout.level;
                    primaryWeapon = loadout.primaryWeapon?.weaponId?.ToUpper() ?? "PHANTOM";
                    secondaryWeapon = loadout.secondaryWeapon?.weaponId?.ToUpper() ?? "GHOST";
                    knifeWeapon = loadout.knifeSkin?.skinId?.ToLower() ?? "default"; // Get knife skin from loadout
                    primarySkin = loadout.primaryWeapon?.skinId?.ToLower() ?? "default";
                    secondarySkin = loadout.secondaryWeapon?.skinId?.ToLower() ?? "default";
                    selectedHat = loadout.selectedHat ?? "none";
                    ability1 = loadout.ability1 ?? "smoke_grenade";
                    ability2 = ""; // No longer used — agents have 1 ability only
                    ultimate = loadout.ultimate ?? "crimson_ultimate";

                    // Override abilities from CharacterConfig (fixed per agent)
                    string agentLower = selectedAgent?.ToLower() ?? "crimson";
                    var charCfg = UnityEngine.Resources.Load<ArtisansGuns.Characters.CharacterConfig>($"Characters/{agentLower}");
                    if (charCfg == null)
                    {
                        string cap = char.ToUpper(agentLower[0]) + agentLower.Substring(1).ToLower();
                        charCfg = UnityEngine.Resources.Load<ArtisansGuns.Characters.CharacterConfig>($"Characters/{cap}");
                    }
                    if (charCfg != null)
                    {
                        if (charCfg.ability1 != null) ability1 = charCfg.ability1.abilityId;
                        // Ultimate is selectable — keep from loadout, don't override from CharacterConfig
                    }
                }
                else
                {
                    // Fallback to PlayerPrefs if LoadoutManager not available
                    string prefAgent = PlayerPrefs.GetString("selected_character", "CRIMSON");
                    if (!string.IsNullOrEmpty(prefAgent))
                    {
                        selectedAgent = prefAgent;
                        // Debug.Log($"âœ… Got selected agent from PlayerPrefs (fallback): '{selectedAgent}'");
                    }
                }
                
                // Debug.Log($"ðŸ"¤ Sending player data: username='{username}', characterName='{characterName}', selectedAgent='{selectedAgent}' (Level {level}, {primaryWeapon}/{secondaryWeapon}/{knifeWeapon})");
                
                // Send our data to be set - IMPORTANT: Send characterName (user's name) not selectedAgent
                RPC_SetPlayerData(username, characterName, level, primaryWeapon, secondaryWeapon, knifeWeapon, selectedAgent, primarySkin, secondarySkin, selectedHat, ability1, ability2, ultimate);
                
                // Restore team from cache if available (e.g., after scene change destroyed old object)
                if (!isRespawn && PlayerCache.TryGetValue(Object.InputAuthority, out var cachedData) && cachedData.TeamAssigned)
                {
                    Team = cachedData.Team;
                    JoinOrder = cachedData.JoinOrder;
                    TeamAssigned = true;
                }
                else if (!isRespawn && NetworkManager.Instance != null)
                {
                    StartCoroutine(DelayedTeamAssignment());
                }
                
                // Duplicate session guard: if another player in the room uses
                // the same username (e.g. same account on two devices), the
                // newer connection (higher PlayerId) disconnects.
                StartCoroutine(CheckDuplicateSession());
                string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (currentScene == "GameScene")
                {
                    var camera = GetComponentInChildren<Camera>(true); // includeInactive = true
                    if (camera != null)
                    {
                        camera.gameObject.SetActive(true);
                        // Debug.Log($"ðŸ“· Camera activated for local player {username}");
                    }
                    else
                    {
                        // Debug.LogWarning($"âš ï¸ No camera found in player prefab for {username}");
                    }
                }
            }

            // HOST MODE: host has StateAuthority over remote players' objects.
            // When the host spawns a remote player, Spawned() fires on the host with
            // HasStateAuthority=true, HasInputAuthority=false — assign their team here.
            if (HasStateAuthority && !HasInputAuthority && !isRespawn)
            {
                StartCoroutine(DelayedTeamAssignment());
            }

            // Update static player cache (persists even if this object is transient in Shared Mode)
            UpdatePlayerCache();

            // Notify UI immediately (synchronous - the object IS alive right now)
            // This replaces the 0.3s coroutine which died when remote objects were destroyed between ticks
            // Debug.Log($"🔔 [PlayerNetworkData] Notifying UI refresh - NetworkId:{Object.Id}, Username:'{Username}', HasInputAuthority:{HasInputAuthority}");
            NetworkManager.Instance?.NotifyPlayerDataChanged();
        }

        /// <summary>
        /// Update the static player cache with current [Networked] data.
        /// Called from Spawned() and Render() to keep the cache always up-to-date.
        /// </summary>
        public void UpdatePlayerCache()
        {
            if (Object == null || !Object.IsValid) return;
            PlayerCache[Object.InputAuthority] = new PlayerDataSnapshot
            {
                Username = Username.ToString(),
                CharacterName = CharacterName.ToString(),
                SelectedAgent = SelectedAgent.ToString(),
                CharacterType = CharacterType,
                IsReady = IsReady,
                Team = Team,
                JoinOrder = JoinOrder,
                TeamAssigned = TeamAssigned,
                Level = Level,
                PrimaryWeapon = PrimaryWeapon.ToString(),
                SecondaryWeapon = SecondaryWeapon.ToString(),
                KnifeWeapon = KnifeWeapon.ToString(),
                PrimarySkin = PrimarySkin.ToString(),
                SecondarySkin = SecondarySkin.ToString(),
                Kills = Kills,
                Deaths = Deaths,
                Headshots = Headshots,
                CurrentStreak = CurrentStreak,
                BestStreak = BestStreak,
                SelectedHat = SelectedHat.ToString(),
                Ability1 = Ability1.ToString(),
                Ability2 = Ability2.ToString(),
                Ultimate = Ultimate.ToString(),
                PlayerRef = Object.InputAuthority,
                NetworkId = Object.Id,
                HasInputAuthority = HasInputAuthority
            };
        }

        /// <summary>
        /// Render is called once per Unity frame - keeps cache updated with latest [Networked] values
        /// This captures changes to IsReady, Team, etc. that happen between Spawned() calls
        /// </summary>
        public override void Render()
        {
            UpdatePlayerCache();
        }

        /// <summary>
        /// Diagnostic: Log when this object is destroyed to understand remote object lifecycle
        /// </summary>
        private void OnDestroy()
        {
        }

        /// <summary>
        /// Called by Fusion when this NetworkObject is despawned.
        /// If this is called, Fusion is INTENTIONALLY removing the object.
        /// If only OnDestroy() fires (without Despawned), something external is destroying it.
        /// </summary>
        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            int playerId = Object != null ? Object.InputAuthority.PlayerId : -1;
            
            // Track despawn time to detect rapid loops
            if (playerId >= 0)
            {
                lastDespawnTime[playerId] = Time.time;
            }
            
            // Reset hasSpawned to allow re-spawn if needed
            hasSpawned = false;
        }
        
        /// <summary>
        /// Delays team assignment to allow all network objects to sync first
        /// </summary>
        private IEnumerator DelayedTeamAssignment()
        {
            // Debug.Log($"â³ Waiting 1 second for network synchronization before team assignment...");
            yield return new WaitForSeconds(1f);
            
            if (NetworkManager.Instance != null && !TeamAssigned)
            {
                // Debug.Log($"âœ… Network synced - now assigning team for {Username}");
                NetworkManager.Instance.AssignPlayerTeam(Object.InputAuthority);
            }
            else if (TeamAssigned)
            {
                // Debug.Log($"â­ï¸ Team already assigned during wait period for {Username}");
            }
        }
        /// <summary>
        /// Wait for network data to propagate, then check if another player in
        /// the room uses the same username. The newer connection (higher PlayerId)
        /// disconnects to avoid duplicate identities.
        /// </summary>
        private IEnumerator CheckDuplicateSession()
        {
            yield return new WaitForSeconds(3f);
            
            if (Object == null || !Object.IsValid) yield break;
            if (!HasInputAuthority) yield break;
            
            string myName = Username.ToString();
            if (string.IsNullOrEmpty(myName)) yield break;
            
            var allPlayers = FindObjectsOfType<PlayerNetworkData>();
            foreach (var other in allPlayers)
            {
                if (other == this || other.Object == null || !other.Object.IsValid) continue;
                if (other.Username.ToString() != myName) continue;
                
                // Same username found on another player — newer connection disconnects
                if (Object.InputAuthority.PlayerId >= other.Object.InputAuthority.PlayerId)
                {
                    Debug.LogWarning($"[PlayerNetworkData] Duplicate session: '{myName}' already in room. Disconnecting...");
                    if (NetworkManager.Instance?.Runner != null)
                        NetworkManager.Instance.Runner.Shutdown();
                    UnityEngine.SceneManagement.SceneManager.LoadScene("LoginScene");
                    yield break;
                }
            }
        }
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_SetPlayerData(string username, string characterName, int level, string primaryWeapon, string secondaryWeapon, string knifeWeapon, string selectedAgent, string primarySkin = "default", string secondarySkin = "default", string selectedHat = "none", string ability1 = "smoke_grenade", string ability2 = "dash", string ultimate = "crimson_ultimate")
        {
            // Debug.Log($"ðŸ"¥ RPC_SetPlayerData received on StateAuthority: username='{username}', characterName='{characterName}', selectedAgent='{selectedAgent}', level={level}, weapons={primaryWeapon}/{secondaryWeapon}, knife={knifeWeapon}");
            
            // The StateAuthority sets the networked data
            Username = username;
            CharacterName = characterName; // This is the user's actual character name (e.g. "sea")
            SelectedAgent = selectedAgent; // This is the selected agent (e.g. "CRIMSON")  
            CharacterType = GetCharacterTypeIndex(selectedAgent); // Use selectedAgent to determine type, not characterName
            IsReady = false;
            Level = level;
            PrimaryWeapon = primaryWeapon;
            SecondaryWeapon = secondaryWeapon;
            KnifeWeapon = knifeWeapon; // Set knife skin ID
            PrimarySkin = primarySkin;
            SecondarySkin = secondarySkin;
            SelectedHat = selectedHat;
            Ability1 = ability1;
            Ability2 = ability2;
            Ultimate = ultimate;

            // Debug.Log($"âœ… Player data set on StateAuthority: {Username} - CharacterName:'{CharacterName}', SelectedAgent:'{SelectedAgent}', CharacterType:{CharacterType}, Level {Level}, Knife:{KnifeWeapon}");
            
            // Trigger UI refresh after data is set
            if (NetworkManager.Instance != null)
            {
                // Debug.Log($"ðŸ”” Notifying NetworkManager that player data changed");
                NetworkManager.Instance.NotifyPlayerDataChanged();
            }
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_SetTeamAndJoinOrder(int team, int joinOrder)
        {
            Team = team;
            JoinOrder = joinOrder;
            TeamAssigned = true; // Mark as assigned
            // Debug.Log($"âœ… Team assignment received: {Username} -> Team {team}, Join Order {joinOrder}");
            
            // Reposition player if in GameScene
            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene == "GameScene" && Object != null)
            {
                // Calculate correct position based on team
                float teamOffset = (team == 0) ? -5f : 5f; // Team A left, Team B right
                float randomZ = UnityEngine.Random.Range(-3f, 3f);
                Vector3 correctPosition = new Vector3(teamOffset, 1f, randomZ);
                
                // Move player to correct position
                transform.position = correctPosition;
                // Debug.Log($"ðŸ“ Repositioned {Username} to Team {team} spawn point at {correctPosition}");
            }
        }
        
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_LoadGameScene()
        {
            // .IO style: scene is already loaded (Sandbox), no-op
        }

        public void InitializePlayerData()
        {
            // Deprecated - now using RPC in Spawned()
            // Kept for backwards compatibility if called elsewhere
            if (HasStateAuthority)
            {
                var user = ArtisansGuns.Auth.AuthManager.Instance?.GetCurrentUser();
                if (user != null)
                {
                    Username = user.username ?? "Player";
                }
                string selectedChar = ArtisansGuns.Managers.LoadoutManager.Instance?.GetLoadout()?.selectedCharacter ?? "crimson";
                CharacterName = selectedChar;
                CharacterType = GetCharacterTypeIndex(selectedChar);
                IsReady = false;

                // Debug.Log($"âœ… Player data initialized (legacy): {Username} - {CharacterName}");
            }
        }

        private int GetCharacterTypeIndex(string characterName)
        {
            switch (characterName.ToUpper())
            {
                case "CRIMSON": return 0;
                case "VIBE": return 1;
                case "SIGHT": return 2;
                case "PATO": return 3;
                default: return 0;
            }
        }

        public void SetReady(bool ready)
        {
            if (HasStateAuthority)
            {
                IsReady = ready;
            }
            else
            {
                // In Host Mode, clients don't have StateAuthority on their own objects—ask host to set it
                RPC_SetReady(ready);
            }
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_SetReady(bool ready) { IsReady = ready; }
        
        /// <summary>
        /// Monitor game state and auto-mark players as InGame when countdown starts
        /// This ensures all ready players are included in the game
        /// Called every network tick (default 60 times per second)
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            // Only process on clients with state authority (each client manages their own InGame flag)
            if (!HasStateAuthority)
                return;
                
            // Check if countdown has started or game is in progress
            // Important: GameStateManager must be spawned before accessing networked properties
            if (GameStateManager.Instance != null && GameStateManager.Instance.Object != null && GameStateManager.Instance.Object.IsValid)
            {
                bool countdownStarted = GameStateManager.Instance.CountdownStarted;
                bool gameInProgress = GameStateManager.Instance.GameInProgress;
                
                // If countdown started, mark all players InGame (not just ready ones)
                // This ensures the host (who might not have clicked ready) also gets marked
                if (countdownStarted && !InGame)
                {
                    InGame = true;
                    // Debug.Log($"ðŸŽ® [{Runner.Tick}] Auto-marked {Username} as InGame (countdown started)");
                }
                
                // Also mark InGame if game is already in progress (for late joiners)
                if (gameInProgress && !InGame)
                {
                    InGame = true;
                    // Debug.Log($"ðŸŽ® [{Runner.Tick}] Auto-marked {Username} as InGame (game in progress, late joiner)");
                }
            }
        }
    }
}
