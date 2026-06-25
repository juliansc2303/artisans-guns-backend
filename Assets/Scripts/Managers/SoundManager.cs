using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ArtisansGuns.Managers
{
    /// <summary>
    /// SoundManager — Global audio singleton (DontDestroyOnLoad).
    ///
    /// SFX:
    ///   SoundManager.Instance.PlayClick()   → all button taps
    ///   SoundManager.Instance.PlaySelect()  → agent/weapon/skin cell selection
    ///
    /// BGM:
    ///   SetMusicVolume(float) → set volume from settings slider (persisted)
    ///   SetMusicMuted(bool)   → toggle mute (preserves state)
    ///
    /// UI:
    ///   RegisterGlobalClickSounds(root) → TrickleDown click sounds on UIDocument root
    /// </summary>
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance { get; private set; }

        // ─── SFX ──────────────────────────────────────────────────────────────
        private AudioSource sfxSource;
        private AudioClip clickClip;
        private AudioClip selectClip;

        // ─── BGM ──────────────────────────────────────────────────────────────
        [Header("Background Music")]
        [SerializeField] private AudioClip introMusic;

        private AudioSource bgmSource;
        private bool isBgmMuted = false;

        public const float DEFAULT_MUSIC_VOLUME = 0.12f;
        private const string MUSIC_VOL_KEY = "music_volume";
        private float _musicVolume = DEFAULT_MUSIC_VOLUME;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // SFX source — stays on this GameObject, untouched by any filter
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            // BGM source lives on a CHILD GameObject.
            var bgmChild = new GameObject("BGM");
            bgmChild.transform.SetParent(transform, false);

            bgmSource = bgmChild.AddComponent<AudioSource>();
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;
            bgmSource.loop = true;
            bgmSource.ignoreListenerVolume = true;  // BGM volume controlled only by us
            bgmSource.ignoreListenerPause  = true;  // Also immune to AudioListener.pause

            // Load persisted volume (default 0.12)
            _musicVolume = PlayerPrefs.GetFloat(MUSIC_VOL_KEY, DEFAULT_MUSIC_VOLUME);
            bgmSource.volume = _musicVolume;

            LoadClips();

            if (introMusic != null)
            {
                bgmSource.clip = introMusic;
                bgmSource.Play();
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
        }

        // Mute music in game scenes; restore user volume on menu scenes.
        // The track NEVER stops — it keeps playing silently during gameplay
        // so it resumes seamlessly when returning to lobby.
        private static readonly string[] GameSceneNames = { "Sandbox", "GameScene" };

        private bool IsInGameScene()
        {
            string current = SceneManager.GetActiveScene().name;
            foreach (var name in GameSceneNames)
                if (current == name) return true;
            return false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[SoundManager] OnSceneLoaded: {scene.name} | bgm.isPlaying={bgmSource?.isPlaying} bgm.volume={bgmSource?.volume} bgm.time={bgmSource?.time:F1} musicVol={_musicVolume}");

            foreach (var name in GameSceneNames)
            {
                if (scene.name == name)
                {
                    if (bgmSource != null) bgmSource.volume = 0f;
                    return;
                }
            }

            // Returning to a menu scene — restore global audio state.
            AudioListener.volume = 1f;
            AudioListener.pause  = false;
            
            if (bgmSource != null && introMusic != null)
            {
                if (!bgmSource.isPlaying)
                {
                    bgmSource.clip = introMusic;
                    bgmSource.Play();
                }
                bgmSource.volume = _musicVolume;
                Debug.Log($"[SoundManager] Menu scene — BGM restored: vol={_musicVolume} time={bgmSource.time:F1}");
            }
        }

        private void LoadClips()
        {
            clickClip  = Resources.Load<AudioClip>("Sounds/ClickSound");
            selectClip = Resources.Load<AudioClip>("Sounds/SelectSound");
        }

        // ─── BGM volume API ───────────────────────────────────────────────────

        /// <summary>Set music volume from the settings slider (0..1). Persists to PlayerPrefs.</summary>
        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(MUSIC_VOL_KEY, _musicVolume);
            PlayerPrefs.Save();

            // Apply immediately unless we're in a game scene (where volume is forced to 0)
            if (bgmSource != null && !IsInGameScene())
                bgmSource.volume = _musicVolume;
        }

        public float GetMusicVolume() => _musicVolume;

        public void SetMusicMuted(bool muted)
        {
            isBgmMuted = muted;
            if (bgmSource != null) bgmSource.mute = muted;
        }

        public bool IsMusicMuted => isBgmMuted;

        // ─── Public play methods ──────────────────────────────────────────────

        public void PlayClick()
        {
            Play(clickClip);
        }

        public void PlaySelect()
        {
            Play(selectClip);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null || sfxSource == null) return;
            sfxSource.PlayOneShot(clip);
        }

        /// <summary>Play a one-shot SFX at a custom pitch (resets to 1 after).</summary>
        public void PlaySFXWithPitch(AudioClip clip, float pitch = 1f)
        {
            if (clip == null || sfxSource == null) return;
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip);
            sfxSource.pitch = 1f;
        }

        // ─── Global UI button hook ────────────────────────────────────────────

        /// <summary>
        /// Registers a TrickleDown ClickEvent listener on the provided root element.
        /// Fires PlayClick() for every Button click (including tab-nav buttons).
        /// Non-Button selection cells are excluded automatically (they use PlaySelect).
        /// Call once per UIDocument root in OnEnable.
        /// </summary>
        public void RegisterGlobalClickSounds(VisualElement root)
        {
            if (root == null) return;
            // Use TrickleDown so we catch the event before it reaches the target
            root.RegisterCallback<ClickEvent>(OnAnyClickTrickle, TrickleDown.TrickleDown);
        }

        public void UnregisterGlobalClickSounds(VisualElement root)
        {
            if (root == null) return;
            root.UnregisterCallback<ClickEvent>(OnAnyClickTrickle, TrickleDown.TrickleDown);
        }

        private void OnAnyClickTrickle(ClickEvent evt)
        {
            // Check if the target OR any of its parents is a Button.
            // This is necessary because clicking on a child element (like a Label or shine overlay)
            // inside a Button makes the child element the evt.target, not the Button itself.
            VisualElement target = evt.target as VisualElement;
            bool isButton = false;
            
            while (target != null)
            {
                if (target is Button)
                {
                    isButton = true;
                    break;
                }
                target = target.parent;
            }

            if (!isButton) return;

            PlayClick();
        }
    }
}
