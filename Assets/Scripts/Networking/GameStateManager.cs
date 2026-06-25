using UnityEngine;
using Fusion;
using System.Linq;
using System.Collections.Generic;
using ArtisansGuns.Game;

namespace ArtisansGuns.Networking
{
    /// <summary>
    /// GameStateManager - Manages the global synchronised game state.
    ///
    /// Flow:  WaitingForPlayers → PreStart (12 s) → Countdown (3-2-1) → GameInProgress → MatchEnded
    ///
    /// PreStart gives all clients time to fully load before the 3-2-1 freeze.
    /// </summary>
    public class GameStateManager : NetworkBehaviour
    {
        public static GameStateManager Instance { get; set; }

        // ── Existing ────────────────────────────────────────────────────
        [Networked] public int CountdownValue { get; set; }             // -1=not started, 0-3=countdown seconds
        [Networked] public NetworkBool CountdownStarted { get; set; }   // True during 3-2-1
        [Networked] public NetworkBool GameInProgress { get; set; }

        // ── Pre-start (12 s warm-up before 3-2-1) ───────────────────────
        [Networked] public NetworkBool PreStartActive { get; set; }     // True during the 12 s phase
        [Networked] public int PreStartSecondsLeft { get; set; }        // 12→0
        private const int PRE_START_DURATION = 12;

        // ── Match Timer & End State ──────────────────────────────────────
        /// <summary>Seconds remaining in the match. Host decrements, all clients read.</summary>
        [Networked] public int MatchTimeRemaining { get; set; }

        /// <summary>0=in_progress, 1=team_a_wins, 2=team_b_wins, 3=draw</summary>
        [Networked] public byte MatchResult { get; set; }

        /// <summary>True once the match has ended (timer expired). Distinct from GameInProgress.</summary>
        [Networked] public NetworkBool MatchEnded { get; set; }

        /// <summary>Peak number of unique players that were simultaneously in the match.</summary>
        [Networked] public int MaxSimultaneousPlayers { get; set; }

        /// <summary>Final team kill counts, set by host in EndMatch(). All clients read these for display.</summary>
        [Networked] public int FinalTeamAKills { get; set; }
        [Networked] public int FinalTeamBKills { get; set; }

        /// <summary>Running team kill totals — persist even when players leave the room.</summary>
        [Networked] public int RunningTeamAKills { get; set; }
        [Networked] public int RunningTeamBKills { get; set; }

        /// <summary>Unique match identifier (first 16 chars of GUID). Set by host at match start.</summary>
        [Networked, Capacity(16)] public NetworkString<_16> MatchId { get; set; }

        /// <summary>Match duration in seconds.</summary>
        private const int MATCH_DURATION_SECONDS = 600;

        // Host-only: track unique player IDs that have joined during this match
        private HashSet<int> _uniquePlayerIds = new HashSet<int>();
        private Coroutine _matchTimerCoroutine;
        private Coroutine _preStartCoroutine;
        private Coroutine _countdownCoroutine;

        // ── Static backup: survives GSM destruction during host-leave ────
        public struct MatchStateBackup
        {
            public bool Valid;
            public int CountdownValue;
            public bool CountdownStarted;
            public bool GameInProgress;
            public bool PreStartActive;
            public int PreStartSecondsLeft;
            public int MatchTimeRemaining;
            public byte MatchResult;
            public bool MatchEnded;
            public int MaxSimultaneousPlayers;
            public int FinalTeamAKills;
            public int FinalTeamBKills;
            public int RunningTeamAKills;
            public int RunningTeamBKills;
            public string MatchId;
        }
        public static MatchStateBackup Backup;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.Log($"[GSM] Duplicate detected — destroying new instance (existing={Instance.gameObject.name})");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"[GSM] Awake — Instance set, DontDestroyOnLoad applied");
        }

        public override void Spawned()
        {
            // Re-register Instance in case Awake() ran on a different (now-destroyed) copy
            if (Instance == null || Instance != this)
            {
                Instance = this;
                Debug.Log("[GSM] Spawned — re-registered Instance (was null or stale)");
            }
            
            // ── Restore from backup if the previous GSM was destroyed (host-leave) ──
            if (HasStateAuthority && Backup.Valid)
            {
                Debug.Log("[GSM] Spawned — restoring match state from backup");
                RestoreMatchState();
                ResumeMatchIfNeeded();
                return;
            }
            
            // Only initialize to "not started" if there's no active match.
            // When StateAuthority transfers mid-match (host left), the [Networked]
            // properties already carry the correct live state — don't overwrite them.
            // Also reset if MatchEnded is true — that means the previous match finished
            // and this is a fresh session (e.g. Start Game pressed again).
            if (HasStateAuthority && (MatchEnded || (!GameInProgress && !PreStartActive && !CountdownStarted)))
            {
                CountdownValue = -1;
                CountdownStarted = false;
                GameInProgress = false;
                PreStartActive = false;
                PreStartSecondsLeft = 0;
                MatchTimeRemaining = 0;
                MatchResult = 0;
                MatchEnded = false;
                MaxSimultaneousPlayers = 0;
                Debug.Log("[GSM] Spawned — fresh state initialized (no active match or previous match ended)");
            }
            else if (HasStateAuthority)
            {
                Debug.Log($"[GSM] Spawned — StateAuthority acquired mid-game, preserving state (GameInProgress={GameInProgress}, PreStart={PreStartActive}, Countdown={CountdownStarted}, MatchEnded={MatchEnded}, TimeRemaining={MatchTimeRemaining})");
                // Resume match timer if match is running but coroutine is dead (host left)
                ResumeMatchIfNeeded();
            }
            Debug.Log($"[GSM] Spawned — HasStateAuthority={HasStateAuthority} Object={Object?.Id}");
        }

        /// <summary>
        /// Called every Fusion tick. Used to detect StateAuthority transfer mid-match
        /// and resume the match timer coroutine if needed.
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            ResumeMatchIfNeeded();
        }

        /// <summary>
        /// If a match/prestart/countdown is active but the local coroutine is null
        /// (because the previous host left), restart the appropriate coroutine.
        /// </summary>
        private void ResumeMatchIfNeeded()
        {
            if (!HasStateAuthority) return;

            // ── 1. Match in progress but timer coroutine is dead ──
            if (GameInProgress && !MatchEnded && _matchTimerCoroutine == null && MatchTimeRemaining > 0)
            {
                Debug.Log($"[GSM] Resuming match timer — {MatchTimeRemaining}s remaining");
                _matchTimerCoroutine = StartCoroutine(MatchTimerCoroutine());
                return;
            }

            // ── 2. Match timer hit 0 but EndMatch never ran ──
            if (GameInProgress && !MatchEnded && MatchTimeRemaining <= 0 && _matchTimerCoroutine == null)
            {
                Debug.Log("[GSM] Gap: match timer at 0 but EndMatch never ran — ending match now");
                GameInProgress = false;
                EndMatch();
                return;
            }

            // ── 3. PreStart still counting down (SecondsLeft > 0) ──
            if (PreStartActive && PreStartSecondsLeft > 0 && !CountdownStarted && !GameInProgress && _preStartCoroutine == null)
            {
                Debug.Log($"[GSM] Resuming pre-start sequence — {PreStartSecondsLeft}s remaining");
                PreStartActive = false; // Clear so DoPreStartSequenceFromSeconds can re-enter
                _preStartCoroutine = StartCoroutine(DoPreStartSequenceFromSeconds(PreStartSecondsLeft));
                return;
            }

            // ── 4. GAP: PreStart finished (SecondsLeft=0) but countdown never started ──
            if (PreStartActive && PreStartSecondsLeft <= 0 && !CountdownStarted && !GameInProgress)
            {
                Debug.Log("[GSM] Gap: PreStart finished but countdown never started — starting countdown");
                StartCountdown(); // sets CountdownStarted=true, CountdownValue=3
                _countdownCoroutine = StartCoroutine(ResumeCountdownSequence());
                return;
            }

            // ── 5. Countdown in progress (Value > 0) ──
            if (CountdownStarted && CountdownValue > 0 && !GameInProgress && _countdownCoroutine == null)
            {
                Debug.Log($"[GSM] Resuming countdown from {CountdownValue}");
                _countdownCoroutine = StartCoroutine(ResumeCountdownSequence());
                return;
            }

            // ── 6. GAP: Countdown reached 0 but game never started ──
            if (CountdownStarted && CountdownValue <= 0 && !GameInProgress)
            {
                Debug.Log("[GSM] Gap: Countdown at 0 but game never started — triggering game start");
                TickCountdown(); // will set GameInProgress=true, start match timer
                return;
            }
        }

        /// <summary>
        /// Resumes the pre-start sequence from a given number of seconds remaining,
        /// then flows into the 3-2-1 countdown.
        /// </summary>
        private System.Collections.IEnumerator DoPreStartSequenceFromSeconds(int secondsLeft)
        {
            PreStartActive = true;
            for (int i = secondsLeft; i > 0; i--)
            {
                PreStartSecondsLeft = i;
                yield return new WaitForSeconds(1f);
            }
            PreStartSecondsLeft = 0;
            StartCountdown();
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(1f);
                TickCountdown();
            }
        }

        /// <summary>
        /// Resumes a 3-2-1 countdown that was interrupted by host leaving.
        /// </summary>
        private System.Collections.IEnumerator ResumeCountdownSequence()
        {
            int remaining = CountdownValue;
            for (int i = 0; i <= remaining; i++)
            {
                yield return new WaitForSeconds(1f);
                TickCountdown();
            }
        }

        /// <summary>
        /// Start the 3-2-1 countdown (only callable by host).
        /// Sets CountdownValue=3 for a quick 3-2-1 ceremony.
        /// </summary>
        public void StartCountdown()
        {
            if (!HasStateAuthority) return;

            // End pre-start phase
            PreStartActive = false;
            PreStartSecondsLeft = 0;

            // Set flags FIRST so all clients see them immediately
            CountdownStarted = true;
            CountdownValue = 3;
            
            // Broadcast reset to ALL clients (reset HP, position, ammo, kills, deaths)
            RPC_ResetAllPlayers();
        }
        
        /// <summary>
        /// RPC broadcast to every client: each client resets their own local player
        /// (HP, position, ammo, kills, deaths) for a fresh round start.
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_ResetAllPlayers()
        {
            Debug.Log("[GameStateManager] RPC_ResetAllPlayers received — resetting ALL players");
            
            var runner = NetworkManager.Instance?.Runner;
            if (runner == null || !runner.IsRunning) return;

            // ── Step 1: Restore visuals for EVERY player on this client ──
            // (RPC_Die hides the TPV on all clients; we must re-show them all)
            var allPlayers = FindObjectsOfType<PlayerNetworkData>();
            foreach (var pd in allPlayers)
            {
                if (pd == null || pd.Object == null) continue;
                
                var health = pd.GetComponent<ArtisansGuns.Game.PlayerHealth>();
                var tpv = pd.GetComponent<ArtisansGuns.Game.PlayerSetup>()?.tpvController;
                
                // Re-show third-person model + re-enable collider (undoes RPC_Die)
                if (tpv != null) tpv.ShowTPV();
                
                var capsule = pd.GetComponent<CapsuleCollider>();
                if (capsule != null) capsule.enabled = true;
                
                // Clear PredictedDead on all clients (shooter may have victim at 0)
                if (health != null)
                {
                    health.PredictedHP = ArtisansGuns.Game.PlayerHealth.MAX_HP;
                }
            }
            
            // ── Step 2: Reset LOCAL player's own state (HP, position, ammo, etc.) ──
            var localPD = allPlayers
                .FirstOrDefault(pd => pd != null && pd.Object != null && pd.Object.HasInputAuthority);
            
            if (localPD == null) return;
            
            // Reset kills and deaths
            localPD.Kills = 0;
            localPD.Deaths = 0;
            
            // Reset health (full HP, clear death state, update HUD)
            var localHealth = localPD.GetComponent<ArtisansGuns.Game.PlayerHealth>();
            if (localHealth != null)
            {
                localHealth.ResetForNewRound();
            }
            
            // Re-enable PlayerController if it was disabled by RPC_Die
            var pc = localPD.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = true;
            
            // Reset position to team spawn — only if team is already assigned.
            // Players whose team hasn't been set yet (Team default 0, TeamAssigned false)
            // will be repositioned correctly by DelayedTeamAssignment.
            var gm = ArtisansGuns.Game.GameManager.Instance;
            if (pc != null && gm != null && localPD.TeamAssigned)
            {
                int team = localPD.Team;
                int index = localPD.JoinOrder / 2;
                Vector3 spawnPos = gm.GetSpawnPositionForTeam(team, index);
                Quaternion spawnRot = gm.GetSpawnRotationForTeam(team, index);
                
                var cc = pc.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false; // disable CC to allow teleport
                pc.transform.position = spawnPos;
                pc.transform.rotation = spawnRot;
                pc.NetworkPosition = spawnPos;
                pc.NetworkRotation = spawnRot;
                if (cc != null) cc.enabled = true;
                Debug.Log($"[RPC_ResetAllPlayers] Repositioned to Team {team} spawn: {spawnPos}");
            }
            else if (!localPD.TeamAssigned)
            {
                Debug.Log("[RPC_ResetAllPlayers] Team not assigned yet — skipping reposition (DelayedTeamAssignment will handle it)");
            }
            
            // ── Step 3: Host resets bots (kills, deaths, position) ──
            var botMgr = ArtisansGuns.AI.BotManager.Instance;
            if (botMgr != null && runner.IsSharedModeMasterClient)
                botMgr.ResetAllBotsForNewRound();

            // Destroy any dropped weapons left in the scene from PreStart
            ArtisansGuns.Weapons.DroppedWeapon.DestroyAll();

            // Restore original weapon loadout (in case player dropped a weapon during PreStart)
            var dropSystem = localPD.GetComponent<ArtisansGuns.Weapons.WeaponDropSystem>();
            if (dropSystem != null)
                dropSystem.RestoreOriginalLoadout();

            // Reset ammo on all equipped weapons
            var weapons = localPD.GetComponentsInChildren<ArtisansGuns.Weapons.FireWeapon>(true);
            foreach (var weapon in weapons)
            {
                weapon.ResetAmmo();
            }

            // Grant spawn immunity for the round start (same visual + invincibility as respawn)
            if (localHealth != null)
            {
                localHealth.StartImmunity();
            }

            // Reset combo/ultimate state for the new match
            ArtisansGuns.Audio.ComboKillManager.Instance?.ResetForNewMatch();

            // Reset ability system (ultimate charge + cooldowns)
            var abilitySystem = localPD.GetComponent<ArtisansGuns.Abilities.AbilitySystem>();
            if (abilitySystem != null)
                abilitySystem.ResetForNewMatch();

            // Force-stand if crouching (clear crouch state for match start)
            var playerCtrl = localPD.GetComponent<ArtisansGuns.Game.PlayerController>();
            if (playerCtrl != null)
                playerCtrl.ForceStand();
        }

        /// <summary>
        /// Tick countdown every second (called by host's coroutine)
        /// </summary>
        public void TickCountdown()
        {
            if (!HasStateAuthority)
                return;

            if (CountdownValue > 0)
            {
                CountdownValue--;
            }
            else if (CountdownValue == 0)
            {
                GameInProgress = true;
                CountdownStarted = false;
                CountdownValue = -1;

                // ── Start match timer ──
                MatchTimeRemaining = MATCH_DURATION_SECONDS;
                MatchResult = 0;
                MatchEnded = false;
                FinalTeamAKills = 0;
                FinalTeamBKills = 0;
                RunningTeamAKills = 0;
                RunningTeamBKills = 0;
                MatchId = System.Guid.NewGuid().ToString().Substring(0, 16);
                _uniquePlayerIds.Clear();
                TrackCurrentPlayers();
                _matchTimerCoroutine = StartCoroutine(MatchTimerCoroutine());
                Debug.Log($"[GSM] Match started — MatchId={MatchId}, Duration={MATCH_DURATION_SECONDS}s");
            }
        }

        /// <summary>
        /// Host-only coroutine: ticks every second, ends match when time runs out.
        /// </summary>
        private System.Collections.IEnumerator MatchTimerCoroutine()
        {
            while (MatchTimeRemaining > 0)
            {
                yield return new WaitForSeconds(1f);
                if (!HasStateAuthority) yield break;
                MatchTimeRemaining--;
                TrackCurrentPlayers();
            }
            // Block new damage immediately
            GameInProgress = false;
            // Wait for in-flight kill state to propagate across the network
            yield return new WaitForSeconds(0.5f);
            EndMatch();
        }

        /// <summary>
        /// Counts current players and updates MaxSimultaneousPlayers.
        /// </summary>
        private void TrackCurrentPlayers()
        {
            var runner = NetworkManager.Instance?.Runner;
            if (runner == null) return;

            int currentCount = runner.ActivePlayers.Count();
            if (currentCount > MaxSimultaneousPlayers)
                MaxSimultaneousPlayers = currentCount;
        }

        /// <summary>
        /// Called by host when timer reaches 0. Calculates winner and sets MatchEnded.
        /// </summary>
        private void EndMatch()
        {
            if (!HasStateAuthority) return;

            // Use running counters — they survive player disconnects
            int teamAKills = RunningTeamAKills;
            int teamBKills = RunningTeamBKills;

            // Store authoritative final scores so all clients see the same values
            FinalTeamAKills = teamAKills;
            FinalTeamBKills = teamBKills;

            if (teamAKills > teamBKills)
                MatchResult = 1; // Team A wins
            else if (teamBKills > teamAKills)
                MatchResult = 2; // Team B wins
            else
                MatchResult = 3; // Draw

            GameInProgress = false;
            MatchEnded = true;

            // Stop bot AI but keep them alive for the scoreboard
            var botMgr = ArtisansGuns.AI.BotManager.Instance;
            if (botMgr != null)
                botMgr.StopAllBotAI();

            Debug.Log($"[GSM] Match ended — TeamA:{teamAKills} vs TeamB:{teamBKills}, Result={MatchResult}");
        }

        /// <summary>
        /// Returns team kills for scoreboard display. 0=TeamA, 1=TeamB.
        /// Uses running counters that persist even after players disconnect.
        /// </summary>
        public (int teamAKills, int teamBKills) GetTeamKills()
        {
            // After match ends, return the authoritative finalized scores
            if (MatchEnded)
                return (FinalTeamAKills, FinalTeamBKills);

            return (RunningTeamAKills, RunningTeamBKills);
        }

        /// <summary>
        /// Called by the killer's client to increment the global team kill counter.
        /// Routed to StateAuthority which writes the [Networked] property.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_AddTeamKill(int team)
        {
            if (team == 0)
                RunningTeamAKills++;
            else if (team == 1)
                RunningTeamBKills++;
        }

        /// <summary>
        /// Any client can request the pre-start sequence via this RPC.
        /// Fusion routes it to the StateAuthority which runs the full 12 s + 3-2-1 sequence.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_BeginCountdownSequence()
        {
            BeginPreStartSequence();
        }

        /// <summary>
        /// Direct call version (use when the caller already IS StateAuthority).
        /// Guards against double-start.
        /// </summary>
        public void BeginCountdownSequence()
        {
            BeginPreStartSequence();
        }

        /// <summary>
        /// Starts the 12-second warm-up before the 3-2-1 countdown.
        /// Called when enough players are in the room.
        /// </summary>
        public void BeginPreStartSequence()
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[GSM] BeginPreStartSequence called but we don't have StateAuthority — ignoring");
                return;
            }

            // If a previous match ended, reset stale flags so a new game can start
            if (MatchEnded)
            {
                MatchEnded = false;
                MatchResult = 0;
                GameInProgress = false;
                CountdownStarted = false;
                PreStartActive = false;
                Debug.Log("[GSM] BeginPreStartSequence — cleared stale MatchEnded state");
            }

            if (PreStartActive || CountdownStarted || GameInProgress) return;
            Debug.Log("[GSM] BeginPreStartSequence — starting 12 s warm-up");
            _preStartCoroutine = StartCoroutine(DoPreStartSequence());
        }

        private System.Collections.IEnumerator DoPreStartSequence()
        {
            // ── Phase 1: 12-second pre-start ─────────────────────────────
            PreStartActive = true;
            PreStartSecondsLeft = PRE_START_DURATION;

            for (int i = PRE_START_DURATION; i > 0; i--)
            {
                PreStartSecondsLeft = i;
                yield return new WaitForSeconds(1f);
            }
            PreStartSecondsLeft = 0;

            // ── Phase 2: 3-2-1 countdown ─────────────────────────────────
            StartCountdown();                          // sets CountdownStarted, resets all players
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(1f);
                TickCountdown();
            }
        }

        private System.Collections.IEnumerator DoCountdownSequence()
        {
            StartCountdown();
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(1f);
                TickCountdown();
            }
        }
        
        /// <summary>
        /// Reset game state when all players return to lobby
        /// Only callable by state authority (host)
        /// </summary>
        public void ResetGameState()
        {
            if (!HasStateAuthority)
            {
                return;
            }
            
            GameInProgress = false;
            CountdownStarted = false;
            CountdownValue = -1;
            PreStartActive = false;
            PreStartSecondsLeft = 0;
            MatchTimeRemaining = 0;
            MatchResult = 0;
            MatchEnded = false;
            MaxSimultaneousPlayers = 0;
            
            if (_matchTimerCoroutine != null)
            {
                StopCoroutine(_matchTimerCoroutine);
                _matchTimerCoroutine = null;
            }
            if (_preStartCoroutine != null)
            {
                StopCoroutine(_preStartCoroutine);
                _preStartCoroutine = null;
            }
            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null;
            }
            _uniquePlayerIds.Clear();
            
            // Reset all player states
            var runner = NetworkManager.Instance?.Runner;
            if (runner != null)
            {
                foreach (var playerObj in runner.ActivePlayers)
                {
                    var playerData = runner.GetPlayerObject(playerObj)?.GetComponent<PlayerNetworkData>();
                    if (playerData != null)
                    {
                        playerData.InGame = false;
                        playerData.IsReady = false;
                        // Debug.Log($"ðŸ”„ Reset player {playerData.Username} state");
                    }
                }
            }
            
            // Debug.Log("âœ… Game state reset complete - ready for new game");
        }
        
        /// <summary>
        /// RPC to end the game for all players (TEST ONLY)
        /// Any player can call this, executes on all clients
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_EndGameForAll()
        {
            // Debug.Log("ðŸ§ª TEST: Received RPC_EndGameForAll - ending game for all players");
            
            var runner = NetworkManager.Instance?.Runner;
            if (runner == null)
            {
                // Debug.LogError("âŒ NetworkRunner not found!");
                return;
            }
            
            // ALL clients: Find and despawn their own PlayerController
            var localController = UnityEngine.Object.FindObjectsOfType<ArtisansGuns.Game.PlayerController>()
                .FirstOrDefault(pc => pc.Object != null && pc.Object.HasInputAuthority);
            
            if (localController != null && localController.Object != null && localController.Object.IsValid)
            {
                // Debug.Log($"ðŸ—‘ï¸ Despawning local PlayerController");
                if (HasStateAuthority)
                {
                    Runner.Despawn(localController.Object);
                }
            }
            
            // ALL clients: Mark their local PlayerNetworkData
            var localPlayerData = runner.GetAllBehaviours<PlayerNetworkData>()
                .FirstOrDefault(pd => pd != null && pd.Object != null && pd.Object.HasInputAuthority);
            
            if (localPlayerData != null)
            {
                localPlayerData.InGame = false;
                localPlayerData.IsReady = false;
                // Debug.Log($"ðŸ”„ Reset local player {localPlayerData.Username} - InGame=false, IsReady=false");
            }
            
            // ONLY HOST: Reset game state and load LobbyScene for all clients
            if (HasStateAuthority)
            {
                // Debug.Log("ðŸ”„ Host: Resetting game state...");
                GameInProgress = false;
                CountdownStarted = false;
                CountdownValue = -1;
                PreStartActive = false;
                PreStartSecondsLeft = 0;
                
                // Despawn all remaining PlayerControllers
                var allControllers = UnityEngine.Object.FindObjectsOfType<ArtisansGuns.Game.PlayerController>();
                foreach (var controller in allControllers)
                {
                    if (controller.Object != null && controller.Object.IsValid)
                    {
                        // Debug.Log($"ðŸ—‘ï¸ Host: Despawning PlayerController for player {controller.Object.InputAuthority.PlayerId}");
                        Runner.Despawn(controller.Object);
                    }
                }
                
                // Load LobbyScene for ALL clients (Fusion will sync this)
                // Debug.Log("ðŸ  Host: Loading LobbyScene for all players (staying in room)...");
                runner.LoadScene("LobbyScene");
            }
        }

        // ── Save/Restore match state across GSM destruction ─────────────

        /// <summary>
        /// Snapshots all [Networked] match state into a static struct so it
        /// survives the GSM being destroyed during a host-leave.
        /// </summary>
        public void SaveMatchState()
        {
            Backup = new MatchStateBackup
            {
                Valid = true,
                CountdownValue = CountdownValue,
                CountdownStarted = CountdownStarted,
                GameInProgress = GameInProgress,
                PreStartActive = PreStartActive,
                PreStartSecondsLeft = PreStartSecondsLeft,
                MatchTimeRemaining = MatchTimeRemaining,
                MatchResult = MatchResult,
                MatchEnded = MatchEnded,
                MaxSimultaneousPlayers = MaxSimultaneousPlayers,
                FinalTeamAKills = FinalTeamAKills,
                FinalTeamBKills = FinalTeamBKills,
                RunningTeamAKills = RunningTeamAKills,
                RunningTeamBKills = RunningTeamBKills,
                MatchId = MatchId.ToString()
            };
            Debug.Log($"[GSM] SaveMatchState — GameInProgress={GameInProgress}, TimeRemaining={MatchTimeRemaining}, MatchEnded={MatchEnded}");
        }

        /// <summary>
        /// Restores the backed-up match state onto this (newly spawned) GSM.
        /// Only call with HasStateAuthority. Clears the backup after restoring.
        /// </summary>
        public void RestoreMatchState()
        {
            if (!Backup.Valid) return;
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[GSM] RestoreMatchState called without StateAuthority — skipping");
                return;
            }

            CountdownValue = Backup.CountdownValue;
            CountdownStarted = Backup.CountdownStarted;
            GameInProgress = Backup.GameInProgress;
            PreStartActive = Backup.PreStartActive;
            PreStartSecondsLeft = Backup.PreStartSecondsLeft;
            MatchTimeRemaining = Backup.MatchTimeRemaining;
            MatchResult = Backup.MatchResult;
            MatchEnded = Backup.MatchEnded;
            MaxSimultaneousPlayers = Backup.MaxSimultaneousPlayers;
            FinalTeamAKills = Backup.FinalTeamAKills;
            FinalTeamBKills = Backup.FinalTeamBKills;
            RunningTeamAKills = Backup.RunningTeamAKills;
            RunningTeamBKills = Backup.RunningTeamBKills;
            MatchId = Backup.MatchId ?? "";
            Backup.Valid = false;

            Debug.Log($"[GSM] RestoreMatchState — GameInProgress={GameInProgress}, TimeRemaining={MatchTimeRemaining}, MatchEnded={MatchEnded}");
        }

        /// <summary>
        /// Fusion callback: GSM is being despawned from the network.
        /// Save a snapshot so the remaining client can restore state on a fresh GSM.
        /// </summary>
        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (hasState)
            {
                Debug.Log("[GSM] Despawned — saving match state backup");
                SaveMatchState();
            }
            else
            {
                Debug.LogWarning("[GSM] Despawned — no state available to save");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Debug.LogWarning($"[GSM] OnDestroy — clearing Instance! gameObject={gameObject.name}");
                Instance = null;
            }
        }

    }
}
