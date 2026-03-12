using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ArtisansGuns.Game;
using ArtisansGuns.Networking;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// CrimsonUltimateEffect — the BAM explosion effect spawned after the
    /// ultimate projectile detonates.
    ///
    /// Prefab structure expected:
    ///   CrimBamEffect (this script + CapsuleCollider[trigger] + Visual Effect)
    ///
    /// Damage is dealt via Physics.OverlapSphere every 0.3s for the full duration.
    /// Each tick damages every Enemy-layer player inside the radius.
    /// Only the INPUT-AUTHORITY client deals damage (remote clients get damage=0).
    /// </summary>
    public class CrimsonUltimateEffect : MonoBehaviour
    {
        // ─── Configuration (set after instantiation) ─────────────────────
        private float _damage;
        private float _duration;
        private Fusion.PlayerRef _shooterRef;
        private bool _dealsDamage;
        private float _damageRadius;

        // 3D effect sound (heard by ALL clients)
        private static AudioClip _effectClip;
        private static bool _effectClipLoaded;

        // Tick interval for repeated damage
        private const float DAMAGE_TICK_INTERVAL = 0.3f;

        // Layer mask for Enemy (same layer FireWeapon uses for raycasts)
        private int _enemyLayerMask;

        // ─── Public API ──────────────────────────────────────────────────

        /// <summary>
        /// Called after instantiation to configure damage and lifetime.
        /// Pass damage=0 for visual-only instances (remote clients).
        /// </summary>
        public void Initialize(float damage, float duration, Fusion.PlayerRef shooterRef)
        {
            _damage       = damage;
            _duration     = duration;
            _shooterRef   = shooterRef;
            _dealsDamage  = damage > 0f;

            // Calculate damage radius from the CapsuleCollider's RADIUS (XZ spread),
            // NOT the height (the column is tall visually but damage should only reach
            // as far as the horizontal radius). Scale by the widest horizontal axis.
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                float horizontalScale = Mathf.Max(transform.localScale.x, transform.localScale.z);
                _damageRadius = capsule.radius * horizontalScale;
            }
            else
            {
                _damageRadius = 5f; // fallback
            }

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            _enemyLayerMask = enemyLayer >= 0 ? (1 << enemyLayer) : 0;

            Destroy(gameObject, _duration);

            // Play 3D explosion sound at effect center (all clients hear this)
            PlayEffectSound();

            StartCoroutine(GrowRoutine());

            if (_dealsDamage)
                StartCoroutine(DamageTickRoutine());
        }

        // ─── Growth animation ────────────────────────────────────────────

        private IEnumerator GrowRoutine()
        {
            const float growDuration = 0.5f;

            Vector3 endScale   = transform.localScale;
            Vector3 startScale = endScale * 0.1f;

            transform.localScale = startScale;

            float elapsed = 0f;
            while (elapsed < growDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / growDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                transform.localScale = Vector3.Lerp(startScale, endScale, eased);
                yield return null;
            }

            transform.localScale = endScale;
        }

        // ─── Repeated damage ticks ───────────────────────────────────────

        private IEnumerator DamageTickRoutine()
        {
            // Wait for grow animation to finish before first damage tick
            yield return new WaitForSeconds(0.5f);

            // Deal damage every DAMAGE_TICK_INTERVAL until destroyed
            while (true)
            {
                ScanAndDamage();
                yield return new WaitForSeconds(DAMAGE_TICK_INTERVAL);
            }
        }

        private void ScanAndDamage()
        {
            if (_enemyLayerMask == 0) return;

            Collider[] hits = Physics.OverlapSphere(transform.position, _damageRadius, _enemyLayerMask);

            // Track who we already hit THIS tick (one hit per player per tick)
            HashSet<int> hitThisTick = new HashSet<int>();

            foreach (var col in hits)
            {
                PlayerHealth victimHealth = col.GetComponentInParent<PlayerHealth>();
                if (victimHealth == null) continue;
                if (victimHealth.IsDead || victimHealth.PredictedDead) continue;
                if (victimHealth.IsImmune) continue;

                int instanceId = victimHealth.GetInstanceID();
                if (hitThisTick.Contains(instanceId)) continue;
                hitThisTick.Add(instanceId);

                PlayerHealth.DealDamage(
                    victimHealth,
                    _damage,
                    false,
                    1f,
                    _shooterRef,
                    "crimson_ultimate"
                );

                Debug.Log($"[CrimsonUltimateEffect] Tick damage {_damage} to {col.name}");
            }
        }

        // ─── 3D Effect Sound ─────────────────────────────────────────────

        private void PlayEffectSound()
        {
            if (!_effectClipLoaded)
            {
                _effectClip = Resources.Load<AudioClip>("Sounds/UltimateEffect");
                _effectClipLoaded = true;
            }
            if (_effectClip == null) return;

            GameObject sfxGO = new GameObject("UltEffectSFX");
            sfxGO.transform.position = transform.position;
            AudioSource src  = sfxGO.AddComponent<AudioSource>();
            src.clip         = _effectClip;
            src.spatialBlend = 1f;           // full 3D
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance  = 1f;
            src.maxDistance  = 30f;
            src.volume       = 1f;
            src.playOnAwake  = false;
            src.Play();
            Destroy(sfxGO, _effectClip.length + 0.1f);
        }
    }
}
