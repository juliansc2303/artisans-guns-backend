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

        /// <summary>
        /// Returns true if the line segment from→to passes through (or has an endpoint inside)
        /// any active smoke cloud.  Used by BotBrain to block LoS through smoke.
        /// </summary>
        public static bool IsLineObscuredBySmoke(Vector3 from, Vector3 to)
        {
            for (int i = _allSmokes.Count - 1; i >= 0; i--)
            {
                if (_allSmokes[i] == null) { _allSmokes.RemoveAt(i); continue; }

                var smoke = _allSmokes[i];
                var col = smoke.GetComponent<SphereCollider>();
                if (col == null) continue;

                // The trigger sphere's world radius = collider.radius * uniform scale
                // Use TransformPoint to account for any SphereCollider.center offset
                Vector3 center = smoke.transform.TransformPoint(col.center);
                float radius = col.radius * smoke.transform.lossyScale.x;

                // If BOTH endpoints are inside the same cloud, skip it —
                // two people sharing the same smoke can see each other.
                // If only one is inside, the cloud still blocks LoS.
                bool fromInside = Vector3.Distance(from, center) < radius;
                bool toInside   = Vector3.Distance(to,   center) < radius;
                if (fromInside && toInside) continue;
                if (fromInside || toInside) return true;

                // Check if any point of the line segment passes through the sphere.
                // Closest-point on segment to sphere center → if dist < radius, obscured.
                Vector3 seg = to - from;
                float segLen = seg.magnitude;
                if (segLen < 0.001f)
                {
                    if (Vector3.Distance(from, center) < radius) return true;
                    continue;
                }
                Vector3 segDir = seg / segLen;
                float t = Mathf.Clamp(Vector3.Dot(center - from, segDir), 0f, segLen);
                Vector3 closest = from + segDir * t;
                if (Vector3.Distance(closest, center) < radius)
                    return true;
            }
            return false;
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


    }
}
