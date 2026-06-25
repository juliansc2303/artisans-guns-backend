using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections;

namespace ArtisansGuns.Audio
{
    /// <summary>
    /// ComboKillManager — Singleton that drives the 5-kill combo cycle.
    ///
    /// Kills 1-4: plays the equipped weapon's killSound with ascending pitch.
    /// Kill 5:    plays the weapon's climaxSound → fires UltimateReady event.
    ///
    /// While ultimate is active, each subsequent kill plays climaxSound of the
    /// current weapon at base pitch.
    ///
    /// The combo counter is GLOBAL across all weapons. If you get 3 kills with
    /// weapon A and switch to weapon B, kill 4 plays B's killSound at pitch[3].
    ///
    /// Combo resets on player death (called from PlayerHealth).
    /// </summary>
    public class ComboKillManager : MonoBehaviour
    {
        // ─── Singleton ──────────────────────────────────────────────────
        private static ComboKillManager _instance;
        public static ComboKillManager Instance => _instance;

        // ─── Events ─────────────────────────────────────────────────────
        /// <summary>Fired when the 5th kill is registered. AbilitySystem listens to activate the ultimate.</summary>
        public static event Action OnUltimateReady;

        /// <summary>Fired when combo resets (death). AbilitySystem listens to deactivate the ultimate.</summary>
        public static event Action OnUltimateReset;

        /// <summary>Fired every kill with the current combo index (1-5, or 0 on reset). UI listens for dot updates.</summary>
        public static event Action<int> OnComboKillRegistered;

        // ─── Tuning ─────────────────────────────────────────────────────
        [Header("Pitch Ladder (kills 1-4)")]
        [Tooltip("Fixed pitch values for kills 1, 2, 3, 4. Pentatonic feel.")]
        [SerializeField] private float[] killPitches = { 0.85f, 0.95f, 1.05f, 1.18f };

        [Header("Volume")]
        [SerializeField] private float killVolume   = 1.0f;
        [SerializeField] private float climaxVolume = 1.0f;

        // ─── Public State ───────────────────────────────────────────────
        /// <summary>Current kill within the 1-5 cycle (0 = no kills yet).</summary>
        public int ComboIndex => _comboIndex;

        /// <summary>True once the 5th kill has been registered and ultimate is active.</summary>
        public bool IsUltimateActive => _ultimateActive;

        // ─── Private State ──────────────────────────────────────────────
        private int  _comboIndex;        // 0-5 within cycle
        private bool _ultimateActive;
        private int  _killStreak;         // total kills without dying (shown in KillUI)

        // ─── Audio ──────────────────────────────────────────────────────
        private AudioSource _source;

        // ─── White Kill Flash ───────────────────────────────────────────
        private VisualElement _killFlashOverlay;
        private Coroutine     _killFlashCoroutine;
        private Coroutine     _hitpauseCoroutine;

        // ─────────────────────────────────────────────────────────────────
        // Unity Lifecycle
        // ─────────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            _source              = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake  = false;
            _source.spatialBlend = 0f;   // full 2D
            _source.priority     = 32;

            // Bootstrap KillStreakUIManager on the same DontDestroyOnLoad object
            if (ArtisansGuns.UI.KillStreakUIManager.Instance == null)
                gameObject.AddComponent<ArtisansGuns.UI.KillStreakUIManager>();
        }

        // ─────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called from PlayerHealth.IncrementKillForShooter (local machine only).
        /// </summary>
        public void OnKillConfirmed(ArtisansGuns.Weapons.WeaponConfig weaponCfg)
        {
            if (weaponCfg == null) return;

            // ── White flash + hitpause dopamine hit on EVERY kill ──
            FlashKillWhite();
            TriggerHitpause();

            _killStreak++;

            if (_ultimateActive)
            {
                // While ultimate is active, every kill plays climax sound
                PlayClip(weaponCfg.climaxSound, 1f, climaxVolume);
                FlashKillWhite();

                // Show Kill UI with total kill streak (not capped at 5)
                ArtisansGuns.UI.KillStreakUIManager.Instance?.ShowKillUI(weaponCfg, _killStreak);
                return;
            }

            _comboIndex++;
            OnComboKillRegistered?.Invoke(_comboIndex);

            if (_comboIndex < 5)
            {
                // Kills 1-4: ascending pitch
                float pitch = GetKillPitch(_comboIndex);
                PlayClip(weaponCfg.killSound, pitch, killVolume);
            }
            else
            {
                // Kill 5: CLIMAX — activate ultimate
                _ultimateActive = true;
                PlayClip(weaponCfg.climaxSound, 1f, climaxVolume);
                OnUltimateReady?.Invoke();
                Debug.Log("[ComboKillManager] ULTIMATE READY! 5 kills reached.");
            }

            // Show Kill UI overlay with total kill streak
            ArtisansGuns.UI.KillStreakUIManager.Instance?.ShowKillUI(weaponCfg, _killStreak);
        }

        /// <summary>
        /// Resets only the kill-streak counter shown in KillUI.
        /// Combo charges (dots) and ultimate state are NOT touched.
        /// Called from RPC_Die on the victim's client.
        /// </summary>
        public void ResetKillStreakOnDeath()
        {
            _killStreak = 0;
            Debug.Log("[ComboKillManager] Kill streak reset on death (combo charges preserved).");
        }

        /// <summary>
        /// Resets combo to zero and deactivates ultimate. Called on player death.
        /// </summary>
        public void ResetCombo()
        {
            bool wasUltimate = _ultimateActive;
            // Kill charges persist through death — do NOT reset _comboIndex.
            // Only reset the ultimate-active flag if it was charged.
            _ultimateActive = false;

            // Always reset the kill streak counter on death
            _killStreak = 0;

            if (wasUltimate)
            {
                _comboIndex = 0;
                OnUltimateReset?.Invoke();
                OnComboKillRegistered?.Invoke(0);
            }

            Debug.Log($"[ComboKillManager] Combo reset on death. comboIndex={_comboIndex}, killStreak=0");
        }

        /// <summary>
        /// Resets combo after the ultimate is thrown (NOT death).
        /// Clears the counter so kills start accumulating again immediately.
        /// Does NOT fire OnUltimateReset — AbilitySystem handles its own UI update.
        /// </summary>
        public void ResetComboAfterThrow()
        {
            _comboIndex     = 0;
            _ultimateActive = false;
            // Kill streak persists through ultimate throw — only reset on death

            OnComboKillRegistered?.Invoke(0);
            Debug.Log($"[ComboKillManager] Combo reset after throw — ready for new cycle. killStreak={_killStreak}");
        }

        /// <summary>
        /// Full reset for a new match. Clears all combo state and ultimate.
        /// Called from GameStateManager.RPC_ResetAllPlayers at match start.
        /// </summary>
        public void ResetForNewMatch()
        {
            _comboIndex     = 0;
            _ultimateActive = false;
            _killStreak     = 0;

            OnUltimateReset?.Invoke();
            OnComboKillRegistered?.Invoke(0);
            Debug.Log("[ComboKillManager] Full reset for new match.");
        }

        // ─────────────────────────────────────────────────────────────────
        // Audio
        // ─────────────────────────────────────────────────────────────────

        private void PlayClip(AudioClip clip, float pitch, float volume)
        {
            if (clip == null || _source == null) return;
            _source.pitch  = pitch;
            _source.volume = volume;
            _source.PlayOneShot(clip);
        }

        private float GetKillPitch(int index)
        {
            // index is 1-based (1,2,3,4)
            int i = Mathf.Clamp(index - 1, 0, killPitches.Length - 1);
            return killPitches[i];
        }

        // ─────────────────────────────────────────────────────────────────
        // White Kill Flash (UIToolkit overlay — dopamine hit on every kill)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lazily finds the GameplayHUD UIDocument and creates a full-screen
        /// white overlay for the kill flash effect.
        /// </summary>
        private void EnsureKillFlashOverlay()
        {
            // If cached overlay is stale (panel destroyed on scene reload), discard it
            if (_killFlashOverlay != null && _killFlashOverlay.panel == null)
                _killFlashOverlay = null;

            if (_killFlashOverlay != null) return;

            // Find GameplayHUD UIDocument (same approach as PlayerHealth)
            UIDocument hudDoc = null;
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (doc.rootVisualElement != null &&
                    doc.rootVisualElement.Q<Label>("HealthText") != null)
                {
                    hudDoc = doc;
                    break;
                }
            }
            if (hudDoc == null) return;

            var root = hudDoc.rootVisualElement;

            // Reuse if already exists (scene reloads)
            var existing = root.Q<VisualElement>("KillFlashOverlay");
            if (existing != null)
            {
                _killFlashOverlay = existing;
                _killFlashOverlay.style.opacity = 0f;
                return;
            }

            _killFlashOverlay = new VisualElement();
            _killFlashOverlay.name = "KillFlashOverlay";
            _killFlashOverlay.pickingMode = PickingMode.Ignore;
            _killFlashOverlay.style.position = Position.Absolute;
            _killFlashOverlay.style.left   = 0;
            _killFlashOverlay.style.top    = 0;
            _killFlashOverlay.style.right  = 0;
            _killFlashOverlay.style.bottom = 0;
            _killFlashOverlay.style.backgroundColor = new StyleColor(Color.white);
            _killFlashOverlay.style.opacity = 0f;

            // Insert near the top of the visual tree so it overlays gameplay
            // but below Scoreboard/Settings
            var scoreboard = root.Q<VisualElement>("Scoreboard");
            if (scoreboard != null)
            {
                var container = scoreboard.parent;
                container.Insert(container.IndexOf(scoreboard), _killFlashOverlay);
            }
            else
            {
                root.Add(_killFlashOverlay);
            }
        }

        /// <summary>
        /// Triggers a very fast white screen flash — called on every kill.
        /// </summary>
        private void FlashKillWhite()
        {
            EnsureKillFlashOverlay();
            if (_killFlashOverlay == null) return;

            if (_killFlashCoroutine != null)
                StopCoroutine(_killFlashCoroutine);

            _killFlashCoroutine = StartCoroutine(KillFlashRoutine());
        }

        private IEnumerator KillFlashRoutine()
        {
            // Instant white flash → rapid fade out (~0.12s)
            _killFlashOverlay.style.opacity = 0.65f;

            float elapsed = 0f;
            const float DURATION = 0.12f;

            while (elapsed < DURATION)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / DURATION;
                _killFlashOverlay.style.opacity = Mathf.Lerp(0.65f, 0f, t * t); // ease-in
                yield return null;
            }

            _killFlashOverlay.style.opacity = 0f;
            _killFlashCoroutine = null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Hitpause (micro time-freeze on kill for visceral impact)
        // ─────────────────────────────────────────────────────────────────

        private void TriggerHitpause()
        {
            if (_hitpauseCoroutine != null)
            {
                StopCoroutine(_hitpauseCoroutine);
                Time.timeScale = 1f;  // Restore in case previous coroutine was mid-pause
            }
            _hitpauseCoroutine = StartCoroutine(HitpauseRoutine());
        }

        private IEnumerator HitpauseRoutine()
        {
            Time.timeScale = 0.01f;
            yield return new WaitForSecondsRealtime(0.03f);
            Time.timeScale = 1f;
            _hitpauseCoroutine = null;
        }
    }
}
