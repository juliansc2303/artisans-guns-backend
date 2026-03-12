using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using ArtisansGuns.Weapons;
using ArtisansGuns.Audio;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// Displays the Kill UI overlay when the local player gets a kill.
    /// Shows the weapon's killUISprite (or default) centered on screen,
    /// with the current kill streak number in the circle below.
    /// Appears with a quick scale-up animation, holds for 3 seconds, then fades out.
    /// </summary>
    public class KillStreakUIManager : MonoBehaviour
    {
        private static KillStreakUIManager _instance;
        public static KillStreakUIManager Instance => _instance;

        private const float DISPLAY_DURATION = 3f;
        private const float FADE_IN_DURATION = 0.15f;
        private const float FADE_OUT_DURATION = 0.4f;

        // UI elements
        private VisualElement _killUIContainer;
        private VisualElement _killUIImage;
        private Label _killUIStreakLabel;
        private Coroutine _displayCoroutine;

        // Default KillUI texture fallback
        private static Texture2D _defaultKillUITexture;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Shows the Kill UI for a confirmed kill.
        /// Called from ComboKillManager.OnKillConfirmed.
        /// </summary>
        public void ShowKillUI(WeaponConfig weaponCfg, int streakCount)
        {
            EnsureKillUIOverlay();
            if (_killUIContainer == null) return;

            // Set the kill UI image
            Texture2D tex = GetKillUITexture(weaponCfg);
            if (tex != null)
                _killUIImage.style.backgroundImage = new StyleBackground(tex);

            // Set streak number
            _killUIStreakLabel.text = streakCount.ToString();

            // Restart display coroutine
            if (_displayCoroutine != null)
                StopCoroutine(_displayCoroutine);
            _displayCoroutine = StartCoroutine(AnimateKillUI());
        }

        private Texture2D GetKillUITexture(WeaponConfig weaponCfg)
        {
            // Try weapon-specific sprite first
            if (weaponCfg != null && weaponCfg.killUISprite != null)
                return weaponCfg.killUISprite.texture;

            // Fallback to default
            if (_defaultKillUITexture == null)
                _defaultKillUITexture = Resources.Load<Texture2D>("KillUI/DefaultKillUI");

            return _defaultKillUITexture;
        }

        private IEnumerator AnimateKillUI()
        {
            // Snap visible — start small and scale up
            _killUIContainer.style.display = DisplayStyle.Flex;
            _killUIContainer.style.opacity = 0f;
            _killUIContainer.style.scale = new Scale(new Vector3(0.3f, 0.3f, 1f));

            // Quick scale-up + fade-in
            float elapsed = 0f;
            while (elapsed < FADE_IN_DURATION)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / FADE_IN_DURATION);
                // Overshoot ease for snappy feel
                float ease = 1f + 0.3f * Mathf.Sin(t * Mathf.PI);
                float scale = Mathf.LerpUnclamped(0.3f, 1f, t) * (t < 0.7f ? ease : 1f);
                _killUIContainer.style.opacity = t;
                _killUIContainer.style.scale = new Scale(new Vector3(scale, scale, 1f));
                yield return null;
            }
            _killUIContainer.style.opacity = 1f;
            _killUIContainer.style.scale = new Scale(Vector3.one);

            // Hold for display duration
            yield return new WaitForSecondsRealtime(DISPLAY_DURATION);

            // Fade out
            elapsed = 0f;
            while (elapsed < FADE_OUT_DURATION)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / FADE_OUT_DURATION);
                _killUIContainer.style.opacity = 1f - t;
                yield return null;
            }

            _killUIContainer.style.display = DisplayStyle.None;
            _displayCoroutine = null;
        }

        private void EnsureKillUIOverlay()
        {
            // If cached container is stale (panel destroyed/scene reload), discard it
            if (_killUIContainer != null && _killUIContainer.panel == null)
            {
                _killUIContainer = null;
                _killUIImage = null;
                _killUIStreakLabel = null;
            }

            if (_killUIContainer != null) return;

            // Find GameplayHUD UIDocument
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

            // Reuse if already exists
            var existing = root.Q<VisualElement>("KillStreakUIContainer");
            if (existing != null)
            {
                _killUIContainer = existing;
                _killUIImage = existing.Q<VisualElement>("KillStreakUIImage");
                _killUIStreakLabel = existing.Q<Label>("KillStreakUILabel");
                _killUIContainer.style.display = DisplayStyle.None;
                return;
            }

            // Create container — centered on screen
            _killUIContainer = new VisualElement();
            _killUIContainer.name = "KillStreakUIContainer";
            _killUIContainer.pickingMode = PickingMode.Ignore;
            _killUIContainer.style.position = Position.Absolute;
            _killUIContainer.style.left = 0;
            _killUIContainer.style.top = 0;
            _killUIContainer.style.right = 0;
            _killUIContainer.style.bottom = 0;
            _killUIContainer.style.alignItems = Align.Center;
            _killUIContainer.style.justifyContent = Justify.FlexEnd;
            _killUIContainer.style.paddingBottom = 90;
            _killUIContainer.style.display = DisplayStyle.None;

            // Inner wrapper — holds the image + streak label
            var inner = new VisualElement();
            inner.pickingMode = PickingMode.Ignore;
            inner.style.alignItems = Align.Center;
            inner.style.justifyContent = Justify.Center;

            // Kill UI image (the skull/logo asset)
            _killUIImage = new VisualElement();
            _killUIImage.name = "KillStreakUIImage";
            _killUIImage.pickingMode = PickingMode.Ignore;
            _killUIImage.style.width = 300;
            _killUIImage.style.height = 300;
            _killUIImage.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            inner.Add(_killUIImage);

            // Streak number label — positioned over the circle at the bottom of the image
            _killUIStreakLabel = new Label("1");
            _killUIStreakLabel.name = "KillStreakUILabel";
            _killUIStreakLabel.pickingMode = PickingMode.Ignore;
            _killUIStreakLabel.style.position = Position.Absolute;
            _killUIStreakLabel.style.bottom = 36;
            _killUIStreakLabel.style.left = 0;
            _killUIStreakLabel.style.right = 0;
            _killUIStreakLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _killUIStreakLabel.style.fontSize = 32;
            _killUIStreakLabel.style.color = new StyleColor(Color.white);
            _killUIStreakLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _killUIStreakLabel.style.textShadow = new TextShadow
            {
                offset = new Vector2(0, 2),
                blurRadius = 4,
                color = new Color(0, 0, 0, 0.7f)
            };
            inner.Add(_killUIStreakLabel);

            _killUIContainer.Add(inner);

            // Insert into the Root container, before overlay panels but after gameplay elements
            var rootContainer = root.Q<VisualElement>("Root");
            if (rootContainer != null)
            {
                var scoresOverlay = rootContainer.Q<VisualElement>("ScoresOverlay");
                if (scoresOverlay != null)
                    rootContainer.Insert(rootContainer.IndexOf(scoresOverlay), _killUIContainer);
                else
                    rootContainer.Add(_killUIContainer);
            }
            else
            {
                root.Add(_killUIContainer);
            }
        }
    }
}
