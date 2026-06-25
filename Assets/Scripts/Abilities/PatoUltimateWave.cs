using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Fusion;
using ArtisansGuns.Game;
using ArtisansGuns.Networking;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Placed on the TsunamiUltimate prefab root.
    /// Handles:
    ///   1. Travelling forward in the spawn direction (XZ only, always at Y = 0).
    ///   2. OnTriggerEnter with Enemy-layer players → applies flash effect.
    ///   3. Auto-destroying after the configured duration.
    ///
    /// Spawned via plain Instantiate inside an RPC (same pattern as TsunamiWave / CrimsonSmoke).
    /// </summary>
    public class PatoUltimateWave : MonoBehaviour
    {
        // ── Runtime config (set by AbilitySystem before Launch) ────────
        [HideInInspector] public Vector3 moveDirection;   // horizontal XZ forward (normalized)
        [HideInInspector] public float   waveSpeed;       // units/s
        [HideInInspector] public float   waveDuration;    // seconds
        [HideInInspector] public float   flashDuration;   // seconds of flash effect on hit enemies
        [HideInInspector] public Fusion.PlayerRef casterRef; // who cast this ultimate

        // ── Internal ───────────────────────────────────────────────────
        private float _timer;
        private readonly HashSet<int> _alreadyFlashed = new HashSet<int>(); // PlayerId set (avoid double-flash)

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>
        /// Called right after Instantiate by AbilitySystem.
        /// Forces Y=0 and starts the wave moving.
        /// </summary>
        public void Launch(Vector3 spawnOrigin)
        {
            // Ensure a Kinematic Rigidbody exists so OnTriggerEnter fires
            // when this trigger collider moves through player capsules.
            var rb = GetComponent<Rigidbody>();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic  = true;
            rb.useGravity   = false;

            // Always spawn at Y=0 (wave covers full map height)
            Vector3 pos = spawnOrigin;
            pos.y = 0f;
            transform.position = pos;

            // Face the movement direction
            if (moveDirection != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        }

        // ── Tick ───────────────────────────────────────────────────────

        private void Update()
        {
            // Use unscaledDeltaTime so the wave is immune to hitpause timeScale changes
            _timer += Time.unscaledDeltaTime;

            if (_timer >= waveDuration)
            {
                Destroy(gameObject);
                return;
            }

            // Move forward (XZ only, Y stays fixed)
            Vector3 delta = moveDirection * waveSpeed * Time.unscaledDeltaTime;
            delta.y = 0f;
            transform.position += delta;
        }

        // ── Trigger detection ──────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            // Only process on clients that have a local player (everyone runs this)
            if (other == null) return;

            // We need the root PlayerPrefab (the one with PlayerHealth / PlayerNetworkData)
            var health = other.GetComponentInParent<PlayerHealth>();
            if (health == null) return;

            var netData = health.GetComponent<PlayerNetworkData>();
            if (netData == null || netData.Object == null) return;

            // Use instance ID for bots (all share PlayerId 0) so each gets a unique key
            bool isBot = netData.Object.InputAuthority == PlayerRef.None;
            int uniqueId = isBot ? health.gameObject.GetInstanceID()
                                 : netData.Object.InputAuthority.PlayerId;

            // Skip the caster — they don't flash themselves
            if (!isBot && netData.Object.InputAuthority == casterRef) return;

            // Skip if already flashed by this wave
            if (_alreadyFlashed.Contains(uniqueId)) return;
            _alreadyFlashed.Add(uniqueId);

            // Skip same-team players (only flash ENEMIES)
            var casterData = FindCasterNetworkData();
            if (casterData != null && netData.Team == casterData.Team) return;

            // ── Apply flash effect on the HIT player's local client ────
            // Only the victim's own client runs the FPV flash logic.
            if (netData.Object.HasInputAuthority)
            {
                FlashEffect.ApplyFlash(flashDuration, health.GetComponent<PlayerSetup>());
            }

            // ── Apply flash to bot AI (bots have no local client for FPV flash) ──
            var botBrain = health.GetComponent<ArtisansGuns.AI.BotBrain>();
            if (botBrain != null)
                botBrain.ApplyFlashBlind(flashDuration);

            // ── Activate FlashFeedback VFX on all clients for this victim
            // (except the victim — they shouldn't see their own feedback VFX)
            var runner = NetworkManager.Instance?.Runner;
            bool isVictimLocal = runner != null && netData.Object.InputAuthority == runner.LocalPlayer;
            if (!isVictimLocal)
            {
                ActivateFlashFeedbackVFX(health.GetComponent<PlayerSetup>(), flashDuration);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────

        private PlayerNetworkData FindCasterNetworkData()
        {
            if (NetworkManager.Instance?.Runner == null) return null;
            var casterObj = NetworkManager.Instance.Runner.GetPlayerObject(casterRef);
            return casterObj?.GetComponent<PlayerNetworkData>();
        }

        /// <summary>
        /// Activates the FlashFeedback VisualEffect on the victim's TPV head.
        /// Uses the direct reference from PlayerTPVController.flashFeedbackVFX
        /// (assigned in the prefab inspector).
        /// </summary>
        private void ActivateFlashFeedbackVFX(PlayerSetup setup, float duration)
        {
            if (setup == null || setup.tpvController == null) return;

            var vfx = setup.tpvController.flashFeedbackVFX;
            if (vfx == null) return;

            // Ensure the VFX GameObject is active (it may be disabled in the prefab)
            if (!vfx.gameObject.activeSelf)
                vfx.gameObject.SetActive(true);

            // Force VFX to render on top of the immune material by disabling depth test.
            // The immune material is opaque and writes to the depth buffer, occluding
            // VFX particles that sit inside the head mesh geometry.
            var vfxRenderer = vfx.GetComponent<Renderer>();
            if (vfxRenderer != null)
            {
                foreach (var mat in vfxRenderer.materials)
                {
                    mat.SetInt("_ZTest", (int)CompareFunction.Always);
                }
            }

            // Set VFX Duration property to match flash duration
            if (vfx.HasFloat("Duration"))
                vfx.SetFloat("Duration", duration);

            vfx.enabled = true;
            vfx.Play();

            // Run disable coroutine on PlayerSetup (which persists even if wave is destroyed)
            setup.StartCoroutine(DisableVFXAfterDelay(vfx, duration));
        }

        private static System.Collections.IEnumerator DisableVFXAfterDelay(
            UnityEngine.VFX.VisualEffect vfx, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (vfx != null)
            {
                vfx.Stop();
                vfx.enabled = false;
                vfx.gameObject.SetActive(false);
            }
        }
    }
}
