using UnityEngine;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// Momentum system — kill streaks grant incremental movement speed and max HP bonuses.
    /// Attached to each player prefab alongside PlayerHealth and PlayerController.
    ///
    /// On each kill:
    ///   • Max HP bonus: +50 per kill (uncapped)
    ///   • Passive regen starts: 15% of current max HP every 3 seconds
    ///
    /// On death:
    ///   • All bonuses reset to zero
    ///   • Regen stops
    /// </summary>
    public class MomentumManager : MonoBehaviour
    {
        // ── Tuning ──────────────────────────────────────────────────────
        private const float HP_BONUS_PER_KILL     = 30f;    // +30 HP per kill
        private const float REGEN_PERCENT         = 0.12f;  // 12% of maxHP
        private const float REGEN_INTERVAL        = 3f;     // seconds

        // ── State ───────────────────────────────────────────────────────
        private int   _streak;
        private float _hpBonus;          // 0, 50, 100, ...
        private float _regenTimer;
        private bool  _regenActive;

        // ── References ──────────────────────────────────────────────────
        private PlayerHealth     _health;

        /// <summary>Current kill streak for UI or debug.</summary>
        public int Streak => _streak;

        /// <summary>Current max HP including bonus.</summary>
        public float CurrentMaxHP => PlayerHealth.MAX_HP + _hpBonus;

        // ─────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ─────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
        }

        private void Update()
        {
            if (_health == null || _health.IsDead) return;
            if (!_regenActive) return;

            _regenTimer -= Time.deltaTime;
            if (_regenTimer <= 0f)
            {
                _regenTimer = REGEN_INTERVAL;
                ApplyRegen();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called when this player gets a kill. Increments streak and applies buffs.
        /// </summary>
        public void OnKill()
        {
            _streak++;
            _hpBonus = _streak * HP_BONUS_PER_KILL;

            // Raise max HP (don't set current HP — regen will fill it)
            // Start passive regen timer (resets cooldown on each kill)
            _regenActive = true;
            _regenTimer  = REGEN_INTERVAL;
        }

        /// <summary>
        /// Called on death — resets all momentum bonuses.
        /// </summary>
        public void ResetOnDeath()
        {
            _streak      = 0;
            _hpBonus     = 0f;
            _regenActive = false;
            _regenTimer  = 0f;
        }

        /// <summary>
        /// Called on respawn/ceremony reset — ensures clean state.
        /// </summary>
        public void ResetForNewRound()
        {
            ResetOnDeath();
        }

        // ─────────────────────────────────────────────────────────────────
        // Internal
        // ─────────────────────────────────────────────────────────────────

        private void ApplyRegen()
        {
            if (_health == null || _health.IsDead) return;

            float maxHP = CurrentMaxHP;
            float currentHP = _health.HP;
            if (currentHP >= maxHP) return;

            float healAmount = maxHP * REGEN_PERCENT;
            float newHP = Mathf.Min(currentHP + healAmount, maxHP);
            _health.HP = newHP;
            _health.PredictedHP = newHP;
            _health.UpdateHealthBarUI();
        }
    }
}
