using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using ArtisansGuns.Game;
using ArtisansGuns.Networking;

namespace ArtisansGuns.AI
{
    /// <summary>
    /// Manages the lifecycle of all bot players in a match.
    /// Runs ONLY on the master client (host).
    ///
    /// Bots trickle-spawn every 30 seconds after the first real player enters the game.
    /// When a bot spawns and total player+bot count hits MIN_PLAYERS_TO_START (2),
    /// the normal pre-start ceremony triggers automatically.
    ///
    /// Usage:
    ///   1. Place this component on a GameObject in your GameScene (or the NetworkManager).
    ///   2. Assign the same playerPrefab used for real players.
    ///   3. Bots auto-spawn 30 s after a real player enters, then every 30 s until full.
    /// </summary>
    public class BotManager : MonoBehaviour
    {
        public static BotManager Instance { get; private set; }

        [Header("Configuration")]
        [Tooltip("Same player prefab used for real players")]
        [SerializeField] private NetworkObject playerPrefab;

        [Tooltip("Desired total players per team (bots fill remaining slots)")]
        [SerializeField] private int desiredPlayersPerTeam = 5;

        [Tooltip("Skill range for bots: x = min, y = max (0-1)")]
        [SerializeField] private Vector2 skillRange = new Vector2(0.05f, 0.42f);

        [Tooltip("Seconds between bot spawns (first bot and subsequent)")]
        [SerializeField] private float trickleInterval = 10f;

        // ── Bot tracking ────────────────────────────────────────────────
        private readonly List<BotInstance> _bots = new List<BotInstance>();
        private NetworkRunner _runner;

        // ── Trickle state ───────────────────────────────────────────────
        private bool _trickleActive;
        private float _trickleTimer;

        private struct BotInstance
        {
            public NetworkObject netObj;
            public BotBrain brain;
            public int team;
        }

        // ═════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ═════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Force runtime values — Unity's serialized Inspector values override
            // code defaults, so we must set them here to guarantee correct counts.
            desiredPlayersPerTeam = 5;
            trickleInterval = 10f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // ── Acquire runner if we don't have one yet ──
            if (_runner == null || !_runner.IsRunning)
            {
                _runner = NetworkManager.Instance?.Runner;
                if (_runner == null || !_runner.IsRunning) return;
            }

            // Only the master client manages bots
            if (!_runner.IsSharedModeMasterClient) return;

            // Stop trickle when match has ended or bots are frozen for scoreboard
            var gsm = GameStateManager.Instance;
            if (gsm != null && gsm.MatchEnded) return;

            // ── Start trickle when there's at least 1 real player in the game scene ──
            if (!_trickleActive)
            {
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                bool inGameScene = scene == "Sandbox" || scene.StartsWith("Map");
                if (!inGameScene) return;

                int realPlayers = 0;
                foreach (var p in _runner.ActivePlayers) realPlayers++;

                if (realPlayers > 0)
                {
                    _trickleActive = true;
                    _trickleTimer = trickleInterval;
                    Debug.Log($"[BotManager] Trickle started — first bot in {trickleInterval}s");
                }
                return;
            }

            // ── Trickle timer ──
            _trickleTimer -= Time.deltaTime;
            if (_trickleTimer <= 0f)
            {
                _trickleTimer = trickleInterval;
                TrySpawnNextBot();
            }

            // ── Periodic cleanup: remove excess bots when real players join ──
            CleanupExcessBots();
        }

        // ═════════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Remove all bots (call when match ends or room closes).
        /// </summary>
        public void RemoveAllBots()
        {
            foreach (var bot in _bots)
            {
                if (bot.netObj != null && bot.netObj.IsValid)
                {
                    _runner?.Despawn(bot.netObj);
                }
            }
            _bots.Clear();
            _trickleActive = false;
            Debug.Log("[BotManager] All bots removed");
        }

        /// <summary>
        /// Stops all bot AI but keeps the NetworkObjects alive so the scoreboard
        /// can read their PlayerNetworkData (kills, deaths, etc.).
        /// Call DelayedRemoveAllBots() after the scoreboard has been populated.
        /// </summary>
        public void StopAllBotAI()
        {
            _trickleActive = false;
            foreach (var bot in _bots)
            {
                if (bot.brain != null)
                    bot.brain.enabled = false;
            }
            Debug.Log("[BotManager] All bot AI stopped (kept alive for scoreboard)");
        }

        /// <summary>
        /// Despawns all bots after a delay, giving the scoreboard time to read data.
        /// </summary>
        public void DelayedRemoveAllBots(float delay = 15f)
        {
            StartCoroutine(DelayedRemoveCoroutine(delay));
        }

        private System.Collections.IEnumerator DelayedRemoveCoroutine(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            RemoveAllBots();
        }

        /// <summary>
        /// Reset all bots for a new round: kills/deaths to 0, reposition to team spawn,
        /// restore health, and re-enable AI.
        /// </summary>
        public void ResetAllBotsForNewRound()
        {
            var gm = ArtisansGuns.Game.GameManager.Instance;

            foreach (var bot in _bots)
            {
                if (bot.netObj == null || !bot.netObj.IsValid) continue;

                var nd = bot.netObj.GetComponent<PlayerNetworkData>();
                if (nd != null)
                {
                    nd.Kills = 0;
                    nd.Deaths = 0;
                    nd.Headshots = 0;
                    nd.CurrentStreak = 0;
                    nd.BestStreak = 0;
                }

                // Reposition to team spawn
                if (gm != null)
                {
                    Vector3 pos = gm.GetSpawnPositionForTeam(bot.team);
                    Quaternion rot = gm.GetSpawnRotationForTeam(bot.team);

                    var pc = bot.netObj.GetComponent<PlayerController>();
                    var cc = bot.netObj.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    bot.netObj.transform.position = pos;
                    bot.netObj.transform.rotation = rot;
                    if (pc != null)
                    {
                        pc.NetworkPosition = pos;
                        pc.NetworkRotation = rot;
                    }
                    if (cc != null) cc.enabled = true;
                }

                // Reset health + grant spawn immunity
                var health = bot.netObj.GetComponent<ArtisansGuns.Game.PlayerHealth>();
                if (health != null)
                {
                    health.ResetForNewRound();
                    health.StartImmunity();
                }

                // Re-show TPV model + re-enable colliders
                var tpv = bot.netObj.GetComponent<ArtisansGuns.Game.PlayerSetup>()?.tpvController;
                if (tpv != null) tpv.ShowTPV();

                var capsule = bot.netObj.GetComponent<CapsuleCollider>();
                if (capsule != null) capsule.enabled = true;

                var charController = bot.netObj.GetComponent<CharacterController>();
                if (charController != null) charController.enabled = true;

                // Re-enable AI
                if (bot.brain != null)
                    bot.brain.enabled = true;
            }

            Debug.Log($"[BotManager] Reset {_bots.Count} bots for new round");
        }

        /// <summary>
        /// Remove one bot from the given team (to make room for a real player).
        /// </summary>
        public bool RemoveBotFromTeam(int team)
        {
            for (int i = _bots.Count - 1; i >= 0; i--)
            {
                if (_bots[i].team == team && _bots[i].netObj != null && _bots[i].netObj.IsValid)
                {
                    Debug.Log($"[BotManager] Removing bot from team {team} to make room");
                    _runner?.Despawn(_bots[i].netObj);
                    _bots.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// How many bots are currently active on a team.
        /// </summary>
        public int GetBotCount(int team)
        {
            int count = 0;
            foreach (var b in _bots)
                if (b.team == team && b.netObj != null && b.netObj.IsValid) count++;
            return count;
        }

        /// <summary>Total active bots across all teams.</summary>
        public int TotalBotCount
        {
            get
            {
                int count = 0;
                foreach (var b in _bots)
                    if (b.netObj != null && b.netObj.IsValid) count++;
                return count;
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // TRICKLE SPAWN LOGIC
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Spawns ONE bot on the team that needs it most, if there's room.
        /// </summary>
        private void TrySpawnNextBot()
        {
            if (_runner == null || !_runner.IsRunning || playerPrefab == null) return;

            // Clean stale entries
            _bots.RemoveAll(b => b.netObj == null || !b.netObj.IsValid);

            // Count real players per team (exclude bots)
            int realA = 0, realB = 0;
            int totalPDFound = 0;
            var allPD = FindObjectsByType<PlayerNetworkData>(FindObjectsSortMode.None);
            foreach (var pd in allPD)
            {
                if (pd.Object == null || !pd.Object.IsValid) continue;
                totalPDFound++;

                // Skip bots — check BOTH IsBotControlled AND our own _bots list
                var pc = pd.GetComponent<PlayerController>();
                bool isBot = (pc != null && pc.IsBotControlled);
                if (!isBot)
                {
                    // Double-check: is this object in our _bots list?
                    var nobj = pd.GetComponent<Fusion.NetworkObject>();
                    foreach (var b in _bots)
                    {
                        if (b.netObj == nobj) { isBot = true; break; }
                    }
                }
                if (isBot) continue;

                if (!pd.TeamAssigned) continue;
                if (pd.Team == 0) realA++;
                else if (pd.Team == 1) realB++;
            }

            int botsA = GetBotCount(0);
            int botsB = GetBotCount(1);
            int totalA = realA + botsA;
            int totalB = realB + botsB;

            Debug.Log($"[BotManager] Census: PD_found={totalPDFound} realA={realA} realB={realB} botsA={botsA} botsB={botsB} totalA={totalA} totalB={totalB} desired={desiredPlayersPerTeam}");

            // Determine which team needs a bot
            int spawnTeam = -1;
            if (totalA < desiredPlayersPerTeam && totalB < desiredPlayersPerTeam)
            {
                spawnTeam = (totalA <= totalB) ? 0 : 1;
            }
            else if (totalA < desiredPlayersPerTeam)
            {
                spawnTeam = 0;
            }
            else if (totalB < desiredPlayersPerTeam)
            {
                spawnTeam = 1;
            }

            if (spawnTeam < 0)
            {
                Debug.Log("[BotManager] Both teams full — no bot needed");
                return;
            }

            SpawnBot(spawnTeam);
        }

        /// <summary>
        /// Removes excess bots when real players have joined mid-match.
        /// Runs periodically from Update.
        /// </summary>
        private void CleanupExcessBots()
        {
            _bots.RemoveAll(b => b.netObj == null || !b.netObj.IsValid);

            int realA = 0, realB = 0;
            var allPD = FindObjectsByType<PlayerNetworkData>(FindObjectsSortMode.None);
            foreach (var pd in allPD)
            {
                if (pd.Object == null || !pd.Object.IsValid) continue;
                if (pd.GetComponent<PlayerController>()?.IsBotControlled == true) continue;
                if (!pd.TeamAssigned) continue;
                if (pd.Team == 0) realA++;
                else if (pd.Team == 1) realB++;
            }

            int wantBotsA = Mathf.Max(0, desiredPlayersPerTeam - realA);
            int wantBotsB = Mathf.Max(0, desiredPlayersPerTeam - realB);

            int curBotsA = GetBotCount(0);
            int curBotsB = GetBotCount(1);

            while (curBotsA > wantBotsA && RemoveBotFromTeam(0)) curBotsA--;
            while (curBotsB > wantBotsB && RemoveBotFromTeam(1)) curBotsB--;
        }

        // ═════════════════════════════════════════════════════════════════
        // SPAWN
        // ═════════════════════════════════════════════════════════════════

        private void SpawnBot(int team)
        {
            if (_runner == null || !_runner.IsRunning || playerPrefab == null) return;

            // Get spawn position from map
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;

            var mapSpawn = FindAnyObjectByType<MapSpawnManager>();
            if (mapSpawn != null)
            {
                spawnPos = mapSpawn.GetSpawnPosition(team);
                spawnRot = mapSpawn.GetSpawnRotation(team);
            }

            // Generate personality
            float skill = Random.Range(skillRange.x, skillRange.y);
            BotPersonality personality = BotPersonality.CreateRandom(skill);

            // Spawn using Fusion (host owns the bot — no PlayerRef means host authority)
            // CRITICAL: Set ALL [Networked] data in OnBeforeSpawned so it's available
            // when Spawned() fires on PlayerSetup (which reads weapon config to spawn FPV gun).
            Vector3 capturedPos = spawnPos;
            Quaternion capturedRot = spawnRot;
            int capturedTeam = team;
            int capturedLevel = Random.Range(3, 28);

            var netObj = _runner.Spawn(playerPrefab, spawnPos, spawnRot, PlayerRef.None,
                (runner, obj) =>
                {
                    // OnBeforeSpawned — runs BEFORE Spawned()
                    var pc = obj.GetComponent<PlayerController>();
                    if (pc != null)
                    {
                        pc.NetworkPosition = capturedPos;
                        pc.NetworkRotation = capturedRot;
                        pc.IsBotControlled = true;
                    }

                    var nd = obj.GetComponent<PlayerNetworkData>();
                    if (nd != null)
                    {
                        nd.Username = personality.displayName;
                        nd.CharacterName = personality.displayName;
                        nd.SelectedAgent = personality.agentId;
                        nd.Team = capturedTeam;
                        nd.TeamAssigned = true;
                        nd.PrimaryWeapon = personality.primaryWeaponId;
                        nd.SecondaryWeapon = personality.secondaryWeaponId;
                        nd.KnifeWeapon = "default";
                        nd.PrimarySkin = "default";
                        nd.SecondarySkin = "default";
                        nd.Level = capturedLevel;
                        nd.IsReady = true;
                        nd.InGame = true;
                    }
                });

            if (netObj == null)
            {
                Debug.LogError($"[BotManager] Failed to spawn bot for team {team}");
                return;
            }

            // Add BotBrain if not already present
            var brain = netObj.GetComponent<BotBrain>();
            if (brain == null)
                brain = netObj.gameObject.AddComponent<BotBrain>();

            brain.Initialize(personality, team);

            // Grant spawn immunity (same as real players)
            var health = netObj.GetComponent<ArtisansGuns.Game.PlayerHealth>();
            if (health != null)
                health.StartImmunity();

            _bots.Add(new BotInstance
            {
                netObj = netObj,
                brain = brain,
                team = team
            });

            Debug.Log($"[BotManager] Spawned bot '{personality.displayName}' " +
                      $"(team {team}, skill {skill:F2}, weapon {personality.primaryWeaponId})");

            // Show join notification (same as real players)
            var hud = FindAnyObjectByType<ArtisansGuns.UI.GameplayHUDController>();
            if (hud != null)
            {
                string joinText = ArtisansGuns.Managers.LocalizationManager.T("JOINED THE ROOM");
                hud.ShowNotification($"{personality.displayName} {joinText}");
            }
        }
    }
}
