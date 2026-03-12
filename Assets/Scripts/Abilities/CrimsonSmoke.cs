using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ArtisansGuns.Game;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Placed on the CrimsonSmoke prefab that is spawned when a smoke grenade detonates.
    ///
    /// Prefab structure expected:
    ///   CrimsonSmoke (this script + SphereCollider[trigger] + optional particle system)
    ///   └─ InteriorSmoke   (child with Renderer — its shader has an "Opacity" float property)
    ///
    /// The Vision Pulse ability (Ability 2) can only be used when
    /// IsLocalPlayerInside is true.
    /// </summary>
    public class CrimsonSmoke : MonoBehaviour
    {
        // ------------------------------------------------------------------
        // All live smoke clouds (multiple can coexist)
        // ------------------------------------------------------------------
        private static readonly List<CrimsonSmoke> _allSmokes = new List<CrimsonSmoke>();

        /// <summary>Returns the most recently spawned live smoke cloud, or null.</summary>
        public static CrimsonSmoke ActiveSmoke
        {
            get
            {
                // Clean up destroyed entries, return the newest surviving smoke
                for (int i = _allSmokes.Count - 1; i >= 0; i--)
                {
                    if (_allSmokes[i] == null) { _allSmokes.RemoveAt(i); continue; }
                    return _allSmokes[i];
                }
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Inspector
        // ------------------------------------------------------------------
        [Tooltip("Renderer on the 'InteriorSmoke' child — must have an 'Opacity' float shader property")]
        [SerializeField] private Renderer interiorSmokeRenderer;

        [Tooltip("Reference name of the Opacity float in the Shader Graph (check the property's 'Reference' field in the Shader Graph — usually _Opacity)")]
        [SerializeField] private string opacityPropertyName = "_Opacity";

        // ------------------------------------------------------------------
        // Public state (read by AbilitySystem)
        // ------------------------------------------------------------------
        public bool IsLocalPlayerInside { get; private set; }

        // ------------------------------------------------------------------
        // Private
        // ------------------------------------------------------------------
        private Material interiorMat;   // MaterialPropertyBlock-friendly cached instance

        // ── Unity lifecycle ──────────────────────────────────────────────

        private void Awake()
        {
            // Cache the interior smoke material instance so shader property changes
            // don't create extra allocations at runtime.
            if (interiorSmokeRenderer != null)
                interiorMat = interiorSmokeRenderer.material; // creates an instance
        }

        private void OnDestroy()
        {
            IsLocalPlayerInside = false;
            _allSmokes.Remove(this);
        }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Called by GrenadeProjectile after instantiation.
        /// Registers this instance as the global active smoke and
        /// schedules self-destruction after <paramref name="duration"/> seconds.
        /// Starts a grow animation from scale 1 → 12 over 0.9 seconds.
        /// </summary>
        public void Initialize(float duration)
        {
            _allSmokes.Add(this);
            Destroy(gameObject, duration);

            StartCoroutine(GrowRoutine());
        }

        private IEnumerator GrowRoutine()
        {
            const float growDuration = 0.9f;
            const float targetScale  = 12f;

            transform.localScale = Vector3.one; // start at scale 1

            float elapsed = 0f;
            while (elapsed < growDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / growDuration);
                // EaseOut: fast start, smooth finish
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                float s = Mathf.Lerp(1f, targetScale, eased);
                transform.localScale = new Vector3(s, s, s);
                yield return null;
            }

            transform.localScale = new Vector3(targetScale, targetScale, targetScale);
        }

        /// <summary>
        /// Pulses the interior smoke visibility — briefly shows full opacity,
        /// holds, then fades back to 0.  Called by AbilitySystem for Vision Pulse.
        /// </summary>
        public void TriggerVisionPulse(VisionPulseAbilityConfig config)
        {
            if (config == null) return;
            StartCoroutine(VisionPulseRoutine(config));
        }

        // ── Trigger detection ────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (IsLocalPlayer(other))
                IsLocalPlayerInside = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsLocalPlayer(other))
                IsLocalPlayerInside = false;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static bool IsLocalPlayer(Collider other)
        {
            // Check for the PlayerSetup NetworkBehaviour; it's only InputAuthority on the local machine.
            var setup = other.GetComponentInParent<PlayerSetup>();
            return setup != null && setup.Object != null && setup.Object.HasInputAuthority;
        }

        private IEnumerator VisionPulseRoutine(VisionPulseAbilityConfig cfg)
        {
            if (interiorMat == null)
            {
                Debug.LogWarning("[CrimsonSmoke] interiorSmokeRenderer not assigned — Vision Pulse has no visual effect");
                yield break;
            }

            int opacityId = Shader.PropertyToID(opacityPropertyName);

            const float restingOpacity = 1f; // Material always rests at full opacity

            // Subtle dip: 1 → pulseTargetOpacity (e.g. 0.9)
            float elapsed = 0f;
            while (elapsed < cfg.pulseFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / cfg.pulseFadeDuration);
                interiorMat.SetFloat(opacityId, Mathf.Lerp(restingOpacity, cfg.pulseTargetOpacity, t));
                yield return null;
            }
            interiorMat.SetFloat(opacityId, cfg.pulseTargetOpacity);

            // Hold
            yield return new WaitForSeconds(cfg.pulseHoldDuration);

            // Return: pulseTargetOpacity → 1
            elapsed = 0f;
            while (elapsed < cfg.pulseFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / cfg.pulseFadeDuration);
                interiorMat.SetFloat(opacityId, Mathf.Lerp(cfg.pulseTargetOpacity, restingOpacity, t));
                yield return null;
            }
            interiorMat.SetFloat(opacityId, restingOpacity);
        }
    }
}
