using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using ArtisansGuns.Game;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Static helper that applies the flash/blind effect to the LOCAL player.
    /// 
    /// Effect:
    ///   1. Fog — enables Unity fog with color #002E73, density 0.3 for the duration.
    ///   2. Underwater audio — adds AudioLowPassFilter on the AudioListener to muffle all sounds.
    ///   3. TPV FlashFeedback VFX — activated separately by PatoUltimateWave on all
    ///      foreign clients (this class does NOT handle the VFX — only the local FPV flash).
    ///
    /// Multiple calls stack gracefully: a new flash replaces the current one
    /// (extends duration, doesn't double-apply).
    /// </summary>
    public static class FlashEffect
    {
        // ── Active flash state ─────────────────────────────────────────
        private static Coroutine _activeCoroutine;
        private static MonoBehaviour _coroutineRunner;

        // Saved pre-flash fog settings (restored when flash ends)
        private static bool  _savedFogEnabled;
        private static Color _savedFogColor;
        private static float _savedFogDensity;
        private static FogMode _savedFogMode;

        // Saved AudioLowPassFilter state
        private static AudioLowPassFilter _lowPass;

        // Flash settings
        private static readonly Color FLASH_FOG_COLOR = new Color(0f, 0.180f, 0.451f, 1f); // #002E73
        private const float FLASH_FOG_DENSITY = 0.5f;
        private const float UNDERWATER_CUTOFF = 800f; // Hz — muffled underwater sound

        /// <summary>
        /// Applies the flash effect to the local player for the given duration.
        /// Safe to call multiple times — previous flash is cleanly replaced.
        /// </summary>
        /// <param name="duration">Seconds the flash lasts.</param>
        /// <param name="localSetup">The local player's PlayerSetup (used as coroutine runner).</param>
        public static void ApplyFlash(float duration, PlayerSetup localSetup)
        {
            if (localSetup == null) return;

            // Cancel previous flash if still active
            if (_activeCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_activeCoroutine);
                // Don't restore yet — the new flash replaces it
            }

            _coroutineRunner = localSetup;
            _activeCoroutine = localSetup.StartCoroutine(FlashCoroutine(duration));
        }

        private static IEnumerator FlashCoroutine(float duration)
        {
            // ── Save current fog state ─────────────────────────────────
            _savedFogEnabled = RenderSettings.fog;
            _savedFogColor   = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;
            _savedFogMode    = RenderSettings.fogMode;

            // ── Apply flash fog ────────────────────────────────────────
            RenderSettings.fog        = true;
            RenderSettings.fogMode    = FogMode.ExponentialSquared;
            RenderSettings.fogColor   = FLASH_FOG_COLOR;
            RenderSettings.fogDensity = FLASH_FOG_DENSITY;

            // ── Apply underwater audio (low-pass filter on AudioListener) ──
            var listener = Object.FindAnyObjectByType<AudioListener>();
            if (listener != null)
            {
                _lowPass = listener.GetComponent<AudioLowPassFilter>();
                if (_lowPass == null)
                    _lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>();
                _lowPass.cutoffFrequency = UNDERWATER_CUTOFF;
                _lowPass.enabled = true;
            }

            // ── Wait for flash duration ────────────────────────────────
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                // Gradually clear the fog in the last 0.8 seconds for a smooth exit
                float fadeStart = duration - 0.8f;
                if (elapsed > fadeStart && duration > 1f)
                {
                    float t = Mathf.InverseLerp(fadeStart, duration, elapsed);
                    RenderSettings.fogDensity = Mathf.Lerp(FLASH_FOG_DENSITY, 0f, t);

                    // Also fade low-pass back to normal
                    if (_lowPass != null)
                        _lowPass.cutoffFrequency = Mathf.Lerp(UNDERWATER_CUTOFF, 22000f, t);
                }

                yield return null;
            }

            // ── Restore original state ─────────────────────────────────
            RestoreOriginalState();
            _activeCoroutine = null;
        }

        private static void RestoreOriginalState()
        {
            RenderSettings.fog        = _savedFogEnabled;
            RenderSettings.fogColor   = _savedFogColor;
            RenderSettings.fogDensity = _savedFogDensity;
            RenderSettings.fogMode    = _savedFogMode;

            if (_lowPass != null)
            {
                _lowPass.cutoffFrequency = 22000f; // fully open
                _lowPass.enabled = false;
            }
        }
    }
}
