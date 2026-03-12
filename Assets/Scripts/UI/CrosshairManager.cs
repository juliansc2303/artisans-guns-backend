using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// CrosshairManager — static crosshair + hit-marker feedback.
    ///
    /// Crosshair styles (selectable later):
    ///   0 = Dot  (default, small white dot / black outline)
    ///   1 = X    (4 diagonal arms, gap at centre — cannot be changed by player)
    ///
    /// Hit-marker: 4 red diagonal arms that flash on any hit, yellow on headshot.
    /// Always visible regardless of active reticle style.
    /// </summary>
    public class CrosshairManager : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────────────
        private static CrosshairManager _instance;
        public static CrosshairManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[CrosshairManager]");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<CrosshairManager>();
                }
                return _instance;
            }
        }

        // ── Constants ────────────────────────────────────────────────────────
        private const float HIT_MARKER_DURATION  = 0.45f;   // seconds visible
        private const float HIT_MARKER_FADE      = 0.25f;   // fade-out portion

        // ── State ────────────────────────────────────────────────────────────
        private VisualElement crosshairDot;
        private VisualElement reticleX;          // X reticle container
        private VisualElement[] hitmarkerArms;   // 4 red arms
        private bool initialized;
        private Coroutine fadeCoroutine;
        private int currentStyle = 0;            // 0=dot, 1=X

        // ── Unity ────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()    => TryInit();
        private void Update()   { if (!initialized) TryInit(); }

        // ── Init ─────────────────────────────────────────────────────────────
        private void TryInit()
        {
            var docs = FindObjectsOfType<UIDocument>();
            foreach (var doc in docs)
            {
                var root = doc.rootVisualElement;
                if (root == null) continue;

                crosshairDot = root.Q<VisualElement>("CrosshairDot");
                reticleX     = root.Q<VisualElement>("ReticleX");
                hitmarkerArms = new VisualElement[]
                {
                    root.Q<VisualElement>("HitmarkerTL"),
                    root.Q<VisualElement>("HitmarkerTR"),
                    root.Q<VisualElement>("HitmarkerBL"),
                    root.Q<VisualElement>("HitmarkerBR"),
                };

                if (crosshairDot != null)
                {
                    // CRITICAL: force PickingMode.Ignore in C# code.
                    // CSS picking-mode is not reliably inherited in all Unity versions.
                    // The CrosshairContainer is full-screen and above all buttons in DOM order,
                    // so without this it silently swallows every tap/click.
                    var container = root.Q<VisualElement>("CrosshairContainer");
                    SetIgnorePickingRecursive(container);

                    initialized = true;
                    ApplyStyle(currentStyle);
                    break;
                }
            }
        }

        // Recursively set PickingMode.Ignore on element and all its descendants
        private static void SetIgnorePickingRecursive(VisualElement el)
        {
            if (el == null) return;
            el.pickingMode = PickingMode.Ignore;
            foreach (var child in el.Children())
                SetIgnorePickingRecursive(child);
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Called by FireWeapon every time a bullet connects with an enemy.</summary>
        public void ShowHitMarker(bool isHeadshot = false)
        {
            if (!initialized) { TryInit(); if (!initialized) return; }

            // Cancel any running fade
            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            fadeCoroutine = StartCoroutine(AnimateHitMarker(isHeadshot));
        }

        /// <summary>Switch reticle style (0 = Dot, 1 = X).</summary>
        public void SetStyle(int style)
        {
            currentStyle = style;
            if (initialized) ApplyStyle(style);
        }

        // ── Internal ─────────────────────────────────────────────────────────

        private void ApplyStyle(int style)
        {
            // Dot
            if (crosshairDot != null)
                crosshairDot.style.display = style == 0
                    ? DisplayStyle.Flex : DisplayStyle.None;

            // X reticle
            if (reticleX != null)
                reticleX.style.display = style == 1
                    ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private IEnumerator AnimateHitMarker(bool isHeadshot)
        {
            if (hitmarkerArms == null) yield break;

            // Colour: red for normal hit, yellow for headshot
            var color = isHeadshot
                ? new Color(1.00f, 0.82f, 0.10f, 1f)   // gold
                : new Color(0.86f, 0.12f, 0.12f, 1f);  // red

            foreach (var arm in hitmarkerArms)
            {
                if (arm == null) continue;
                arm.style.opacity = 1f;
                arm.style.unityBackgroundImageTintColor = color;
            }

            // Hold
            float hold = HIT_MARKER_DURATION - HIT_MARKER_FADE;
            yield return new WaitForSeconds(hold);

            // Fade out
            float elapsed = 0f;
            while (elapsed < HIT_MARKER_FADE)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / HIT_MARKER_FADE);
                foreach (var arm in hitmarkerArms)
                    if (arm != null) arm.style.opacity = 1f - t;
                yield return null;
            }

            foreach (var arm in hitmarkerArms)
                if (arm != null) arm.style.opacity = 0f;
        }
    }
}
