using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// Kill Feed — shows a floating notification when a player eliminates another.
    /// Format: [KILLER] [WeaponIcon] [SkullIcon] [VICTIM]
    ///
    /// Singleton — auto-creates itself on first use.
    /// Entries slide in from the right, stack downward (newest on top, max 3 visible).
    /// </summary>
    public class KillFeedManager : MonoBehaviour
    {
        // ────────────────────────────────────────────────────────────────────
        // Singleton
        // ────────────────────────────────────────────────────────────────────
        private static KillFeedManager _instance;
        public static KillFeedManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[KillFeedManager]");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<KillFeedManager>();
                }
                return _instance;
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Constants
        // ────────────────────────────────────────────────────────────────────
        private const int    MAX_ENTRIES      = 3;
        private const float  DISPLAY_DURATION  = 4.5f;   // seconds visible
        private const float  FADE_IN_DURATION  = 0.3f;
        private const float  FADE_OUT_DURATION = 0.5f;
        private const string SKULL_ICON_PATH   = "Icons/DeathshotIcon";

        // Maps weaponId → Resources path for weapon icon
        private static readonly Dictionary<string, string> WeaponIconMap = new Dictionary<string, string>
        {
            { "talon_ar",      "Icons/Talon-ARWhiteIcon"      },
            { "bolt",          "Icons/BoltWhiteIcon"           },
            { "knife",         "Icons/WhiteIconDefaultKnife"   },
            { "default_knife", "Icons/WhiteIconDefaultKnife"   },
        };

        // ────────────────────────────────────────────────────────────────────
        // State
        // ────────────────────────────────────────────────────────────────────
        private VisualElement           killFeedContainer;
        private readonly List<VisualElement> activeEntries = new List<VisualElement>();
        private bool                    initialized;

        // Pre-loaded textures
        private Texture2D skullTex;
        private readonly Dictionary<string, Texture2D> weaponTexCache = new Dictionary<string, Texture2D>();

        // ────────────────────────────────────────────────────────────────────
        // Unity lifecycle
        // ────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            TryInitialize();
        }

        private void Update()
        {
            // Retry init each frame until successful (UIDocument may load after us)
            if (!initialized)
                TryInitialize();
        }

        // ────────────────────────────────────────────────────────────────────
        // Init
        // ────────────────────────────────────────────────────────────────────

        private void TryInitialize()
        {
            // Find the GameplayHUD UIDocument (the one that contains KillFeedContainer)
            var allDocs = FindObjectsOfType<UIDocument>();
            foreach (var doc in allDocs)
            {
                var container = doc.rootVisualElement?.Q<VisualElement>("KillFeedContainer");
                if (container != null)
                {
                    killFeedContainer = container;
                    break;
                }
            }

            if (killFeedContainer == null) return;

            // Pre-load skull icon
            skullTex = Resources.Load<Texture2D>(SKULL_ICON_PATH);

            // Pre-load weapon icons
            foreach (var kv in WeaponIconMap)
            {
                if (!weaponTexCache.ContainsKey(kv.Key))
                    weaponTexCache[kv.Key] = Resources.Load<Texture2D>(kv.Value);
            }

            initialized = true;
        }

        // ────────────────────────────────────────────────────────────────────
        // Public API
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Show a kill feed entry. Call on ALL clients (via RPC_Die broadcast).
        /// </summary>
        /// <param name="killerName">Display name of the player who got the kill</param>
        /// <param name="weaponId">WeaponConfig.weaponId — used to pick the icon</param>
        /// <param name="victimName">Display name of the eliminated player</param>
        public void ShowKill(string killerName, string weaponId, string victimName,
                             bool isHeadshot = false, int killerTeam = 0, int victimTeam = 1)
        {
            if (!initialized)
            {
                TryInitialize();
                if (!initialized) return;
            }

            var entry = BuildEntry(killerName, weaponId, victimName, isHeadshot, killerTeam, victimTeam);

            // Insert at index 0 so newest entry is always first (top)
            killFeedContainer.Insert(0, entry);
            activeEntries.Insert(0, entry);

            // Trim oldest entries if we exceed the max
            while (activeEntries.Count > MAX_ENTRIES)
            {
                var oldest = activeEntries[activeEntries.Count - 1];
                activeEntries.RemoveAt(activeEntries.Count - 1);
                if (oldest.parent != null)
                    killFeedContainer.Remove(oldest);
            }

            StartCoroutine(AnimateEntry(entry));
        }

        // ────────────────────────────────────────────────────────────────────
        // Entry construction
        // ────────────────────────────────────────────────────────────────────

        private VisualElement BuildEntry(string killerName, string weaponId, string victimName,
                                         bool isHeadshot, int killerTeam, int victimTeam)
        {
            // Row container
            var entry = new VisualElement();
            entry.AddToClassList("kill-feed-entry");

            // Team color helper: Team 0 = A = Orange-Red, Team 1 = B = Blue
            UnityEngine.Color TeamColor(int team) =>
                team == 0
                    ? new UnityEngine.Color(1.00f, 0.31f, 0.16f, 1f)   // Team A — orange-red (255/255, 80/255, 40/255)
                    : new UnityEngine.Color(0.16f, 0.69f, 1.00f, 1f);  // Team B — blue (40/255, 175/255, 255/255)

            // ── Killer name ──────────────────────────────────────────────
            var killerLabel = new Label(killerName.ToUpper());
            killerLabel.AddToClassList("kill-feed-name");
            killerLabel.AddToClassList("kill-feed-killer");
            killerLabel.style.color = TeamColor(killerTeam);
            entry.Add(killerLabel);

            // ── Weapon icon ──────────────────────────────────────────────
            var weaponIcon = new VisualElement();
            weaponIcon.AddToClassList("kill-feed-weapon-icon");
            Texture2D weaponTex = GetWeaponTexture(weaponId);
            if (weaponTex != null)
                weaponIcon.style.backgroundImage = new StyleBackground(weaponTex);
            entry.Add(weaponIcon);

            // ── Headshot icon (only when it was a headshot) ──────────────
            if (isHeadshot)
            {
                var skullIcon = new VisualElement();
                skullIcon.AddToClassList("kill-feed-skull-icon");
                if (skullTex != null)
                    skullIcon.style.backgroundImage = new StyleBackground(skullTex);
                entry.Add(skullIcon);
            }

            // ── Victim name ──────────────────────────────────────────────
            var victimLabel = new Label(victimName.ToUpper());
            victimLabel.AddToClassList("kill-feed-name");
            victimLabel.AddToClassList("kill-feed-victim");
            victimLabel.style.color = TeamColor(victimTeam);
            entry.Add(victimLabel);

            // Start invisible (coroutine will animate in)
            entry.style.opacity = 0f;

            return entry;
        }

        // ────────────────────────────────────────────────────────────────────
        // Animation (coroutine-driven opacity + translate)
        // ────────────────────────────────────────────────────────────────────

        private IEnumerator AnimateEntry(VisualElement entry)
        {
            // ── Slide + fade IN ──────────────────────────────────────────
            float elapsed = 0f;
            float startX  = 40f;   // slide in from right (px)

            while (elapsed < FADE_IN_DURATION)
            {
                elapsed += Time.deltaTime;
                float t    = Mathf.Clamp01(elapsed / FADE_IN_DURATION);
                float ease = EaseOutCubic(t);

                entry.style.opacity   = ease;
                entry.style.translate = new Translate(Mathf.Lerp(startX, 0f, ease), 0f);
                yield return null;
            }

            entry.style.opacity   = 1f;
            entry.style.translate = new Translate(0f, 0f);

            // ── Hold ─────────────────────────────────────────────────────
            yield return new WaitForSeconds(DISPLAY_DURATION);

            // ── Fade OUT ─────────────────────────────────────────────────
            elapsed = 0f;

            while (elapsed < FADE_OUT_DURATION)
            {
                if (entry.parent == null) yield break;   // already removed
                elapsed += Time.deltaTime;
                float t  = Mathf.Clamp01(elapsed / FADE_OUT_DURATION);
                entry.style.opacity   = 1f - EaseInCubic(t);
                entry.style.translate = new Translate(Mathf.Lerp(0f, -20f, t), 0f);
                yield return null;
            }

            // ── Remove ───────────────────────────────────────────────────
            if (activeEntries.Contains(entry))
                activeEntries.Remove(entry);
            if (entry.parent != null)
                killFeedContainer.Remove(entry);
        }

        // ────────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────────

        private Texture2D GetWeaponTexture(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;

            if (weaponTexCache.TryGetValue(weaponId, out var cached)) return cached;

            // Fallback: try loading by ID directly
            var tex = Resources.Load<Texture2D>($"Icons/{weaponId}");
            if (tex != null) weaponTexCache[weaponId] = tex;
            return tex;
        }

        private bool IsLocalPlayer(string name)
        {
            // Compare against UGS/PlayerNetworkData username of local player
            var localData = FindLocalPlayerData();
            if (localData == null) return false;

            string localName = localData.CharacterName.ToString();
            if (string.IsNullOrEmpty(localName))
                localName = localData.Username.ToString();

            return string.Equals(name, localName, System.StringComparison.OrdinalIgnoreCase);
        }

        private ArtisansGuns.Networking.PlayerNetworkData FindLocalPlayerData()
        {
            var all = FindObjectsOfType<ArtisansGuns.Networking.PlayerNetworkData>();
            foreach (var p in all)
                if (p.Object != null && p.Object.HasInputAuthority)
                    return p;
            return null;
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float EaseInCubic(float t)  => t * t * t;
    }
}
