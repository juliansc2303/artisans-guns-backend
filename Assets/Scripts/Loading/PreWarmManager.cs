using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.VFX;
using ArtisansGuns.Weapons;
using ArtisansGuns.Abilities;
using ArtisansGuns.Characters;

namespace ArtisansGuns.Loading
{
    /// <summary>
    /// PreWarmManager — Shows a loading screen and pre-warms ALL game assets
    /// (weapons, abilities, VFX, audio, shaders) before the player enters gameplay.
    ///
    /// This eliminates first-shot lag caused by:
    ///   - Shader compilation on first use
    ///   - First VFX instantiation overhead
    ///   - Audio clip decompression
    ///
    /// Assets pre-warmed:
    ///   - ALL WeaponConfigs in Resources/Weapons (all players might use them)
    ///   - ALL CharacterConfigs in Resources/Characters (abilities, death VFX)
    ///   - ALL sounds in Resources/Sounds
    ///
    /// Lifecycle:
    ///   1. ShowLoading() — called before Fusion StartGame
    ///   2. RunPreWarm()  — called from OnSceneLoadDone when Sandbox loads
    ///   3. HideLoading() — called when pre-warm completes
    ///
    /// Setup: Add to a GameObject in the first scene (e.g. LoginScene or LobbyScene).
    ///        Assign LoadingScreen.uxml as the visualTreeAsset.
    /// </summary>
    public class PreWarmManager : MonoBehaviour
    {
        public static PreWarmManager Instance { get; private set; }

        [Header("UI")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset loadingScreenAsset;
        [SerializeField] private StyleSheet loadingScreenStyle;

        [Header("Settings")]
        [Tooltip("Y offset for off-screen prefab instantiation")]
        [SerializeField] private float offScreenY = -1000f;

        /// <summary>True while pre-warm is in progress or loading screen is showing.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>Fired when pre-warm finishes and loading screen is about to hide.</summary>
        public event Action OnPreWarmComplete;

        // UI references
        private VisualElement root;
        private VisualElement loadingRoot;
        private VisualElement progressBarFill;
        private Label progressText;
        private Label loadingLabel;

        // Muted AudioSource for pre-warming audio clips
        private AudioSource muteSource;

        // Pre-warm state
        private bool preWarmDone;
        private int _step;
        private int _totalSteps;

        /// <summary>Max milliseconds per frame during pre-warm. Keeps frames short
        /// so NetworkRunner.Update() can send heartbeats to the Photon server.</summary>
        private const float FrameBudgetMs = 8f;
        private float _frameStartTime;

        // Tracks which character IDs have already been pre-warmed (avoids duplicate work).
        // When the game scales to many characters, startup only warms the local player's
        // character and common assets. Others are warmed on-demand when encountered.
        private readonly HashSet<string> _warmedCharacters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Tracks elements hidden during loading so we can restore them
        // IMPORTANT: We do NOT disable UIDocuments — that rebuilds the visual tree
        // and invalidates every cached VisualElement reference in other scripts.
        // Instead we hide the root VisualElement with CSS and disable cameras.
        private readonly List<VisualElement> hiddenRoots = new List<VisualElement>();
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
        private readonly List<Camera> hiddenCameras = new List<Camera>();

        // ──────────────────────────────────────────────
        // Unity lifecycle
        // ──────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Ensure UIDocument exists
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
                if (uiDocument == null)
                    uiDocument = gameObject.AddComponent<UIDocument>();
            }

            // Set sorting order very high so loading screen is always on top
            uiDocument.sortingOrder = 9999;

            // Create a dedicated muted AudioSource for decompressing clips
            muteSource = gameObject.AddComponent<AudioSource>();
            muteSource.volume = 0f;
            muteSource.mute = true;
            muteSource.playOnAwake = false;
            muteSource.spatialBlend = 0f; // 2D

            // Start hidden
            if (uiDocument.rootVisualElement != null)
                uiDocument.rootVisualElement.style.display = DisplayStyle.None;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ──────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────

        /// <summary>
        /// Show the loading screen. Call BEFORE Fusion scene transition.
        /// </summary>
        public void ShowLoading()
        {
            IsLoading = true;
            preWarmDone = false;

            // Hide every other UIDocument and UGUI Canvas so nothing renders above us
            HideAllOtherUI();

            SetupUI();

            if (loadingRoot != null)
                loadingRoot.style.display = DisplayStyle.Flex;

            if (root != null)
                root.style.display = DisplayStyle.Flex;

            UpdateProgress(0f, "Connecting...");
            Debug.Log("[PreWarmManager] Loading screen shown");
        }

        /// <summary>
        /// Start the pre-warm coroutine. Called from OnSceneLoadDone when Sandbox loads.
        /// When the coroutine finishes, the loading screen is hidden automatically.
        /// </summary>
        public void RunPreWarm(Action onComplete = null)
        {
            // The new scene just loaded — hide any UIDocuments/Canvases that came with it
            // so they don't render above the loading screen.
            HideAllOtherUI();

            StartCoroutine(PreWarmCoroutine(onComplete));
        }

        /// <summary>
        /// Hide loading screen immediately (e.g. on error or cancel).
        /// </summary>
        public void HideLoading()
        {
            if (loadingRoot != null)
                loadingRoot.style.display = DisplayStyle.None;

            if (root != null)
                root.style.display = DisplayStyle.None;

            // Restore every UIDocument and Canvas we disabled
            RestoreAllOtherUI();

            IsLoading = false;
            Debug.Log("[PreWarmManager] Loading screen hidden");
        }

        // ──────────────────────────────────────────────
        // UI setup
        // ──────────────────────────────────────────────

        private void SetupUI()
        {
            if (uiDocument == null) return;

            // Clone the visual tree from asset if needed
            if (loadingScreenAsset != null)
            {
                uiDocument.visualTreeAsset = loadingScreenAsset;

                // Force rebuild
                root = uiDocument.rootVisualElement;
            }
            else
            {
                root = uiDocument.rootVisualElement;
            }

            if (root == null) return;

            // Apply stylesheet
            if (loadingScreenStyle != null && !root.styleSheets.Contains(loadingScreenStyle))
                root.styleSheets.Add(loadingScreenStyle);

            // Cache elements
            loadingRoot = root.Q<VisualElement>("LoadingRoot");
            progressBarFill = root.Q<VisualElement>("ProgressBarFill");
            progressText = root.Q<Label>("ProgressText");
            loadingLabel = root.Q<Label>("LoadingLabel");
        }

        /// <summary>
        /// Hide all other UI and cameras so only the loading screen is visible.
        /// Uses CSS display:none on UIDocument roots (preserves cached references)
        /// and disables cameras so no 3D content renders.
        /// </summary>
        private void HideAllOtherUI()
        {
            // Hide UIDocument roots via CSS (NOT by disabling the component)
            foreach (var doc in FindObjectsOfType<UIDocument>(true))
            {
                if (doc == uiDocument) continue;
                var r = doc.rootVisualElement;
                if (r == null) continue;
                if (r.resolvedStyle.display == DisplayStyle.None) continue;
                r.style.display = DisplayStyle.None;
                if (!hiddenRoots.Contains(r))
                    hiddenRoots.Add(r);
            }

            // Hide UGUI Canvases
            foreach (var canvas in FindObjectsOfType<Canvas>(true))
            {
                if (!canvas.enabled) continue;
                canvas.enabled = false;
                if (!hiddenCanvases.Contains(canvas))
                    hiddenCanvases.Add(canvas);
            }

            // Disable all cameras so the 3D world doesn't render
            foreach (var cam in FindObjectsOfType<Camera>(true))
            {
                if (!cam.enabled) continue;
                cam.enabled = false;
                if (!hiddenCameras.Contains(cam))
                    hiddenCameras.Add(cam);
            }

            Debug.Log($"[PreWarmManager] Hidden {hiddenRoots.Count} UI roots + {hiddenCanvases.Count} Canvases + {hiddenCameras.Count} Cameras");
        }

        /// <summary>
        /// Restore everything that was hidden during loading.
        /// </summary>
        private void RestoreAllOtherUI()
        {
            foreach (var r in hiddenRoots)
            {
                if (r != null) r.style.display = DisplayStyle.Flex;
            }

            foreach (var canvas in hiddenCanvases)
            {
                if (canvas != null) canvas.enabled = true;
            }

            foreach (var cam in hiddenCameras)
            {
                if (cam != null) cam.enabled = true;
            }

            Debug.Log($"[PreWarmManager] Restored {hiddenRoots.Count} UI roots + {hiddenCanvases.Count} Canvases + {hiddenCameras.Count} Cameras");
            hiddenRoots.Clear();
            hiddenCanvases.Clear();
            hiddenCameras.Clear();
        }

        private void UpdateProgress(float normalized, string statusText = null)
        {
            int pct = Mathf.RoundToInt(normalized * 100f);

            if (progressBarFill != null)
                progressBarFill.style.width = new Length(pct, LengthUnit.Percent);

            if (progressText != null)
                progressText.text = $"{pct}%";

            if (loadingLabel != null && statusText != null)
                loadingLabel.text = statusText;
        }

        // ──────────────────────────────────────────────
        // Pre-warm coroutine
        // ──────────────────────────────────────────────

        private IEnumerator PreWarmCoroutine(Action onComplete)
        {
            Debug.Log("[PreWarmManager] Pre-warm started");
            UpdateProgress(0.05f, "LOADING WEAPONS...");
            yield return null; // Let UI render

            // ── 1. Collect all WeaponConfigs ──
            WeaponConfig[] allWeapons = Resources.LoadAll<WeaponConfig>("Weapons");
            Debug.Log($"[PreWarmManager] Found {allWeapons.Length} weapon configs to pre-warm");

            // ── 2. Collect all CharacterConfigs ──
            CharacterConfig[] allCharacters = Resources.LoadAll<CharacterConfig>("Characters");
            Debug.Log($"[PreWarmManager] Found {allCharacters.Length} character configs to pre-warm");

            // ── 3. Collect all loose audio clips in Sounds folder ──
            AudioClip[] allSounds = Resources.LoadAll<AudioClip>("Sounds");
            Debug.Log($"[PreWarmManager] Found {allSounds.Length} sound clips to pre-warm");

            // Calculate total steps for progress
            _totalSteps = 0;
            foreach (var w in allWeapons)
                _totalSteps += CountWeaponPrefabs(w) + CountWeaponAudio(w);
            foreach (var c in allCharacters)
                _totalSteps += CountCharacterAssets(c);
            _totalSteps += allSounds.Length;
            _totalSteps = Mathf.Max(_totalSteps, 1);

            _step = 0;

            // ── 4. Pre-warm weapon prefabs ──
            UpdateProgress(0.1f, "LOADING WEAPONS...");
            yield return null;
            _frameStartTime = Time.realtimeSinceStartup * 1000f;

            foreach (var weapon in allWeapons)
            {
                if (weapon == null) continue;
                Debug.Log($"[PreWarmManager] Pre-warming weapon: {weapon.weaponName}");

                // Prefabs: instantiate off-screen, force shader compile + mesh upload, destroy
                WarmPrefabSync(weapon.weaponPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(weapon.muzzleFlashPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(weapon.tpvMuzzleFlashPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(weapon.impactEffectPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(weapon.headBloodPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(weapon.bodyBloodPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(weapon.prefabWeaponTPV);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(weapon.tpvTrailPrefab);
                yield return StartCoroutine(BudgetYield());

                // Audio clips: play at volume 0 to force decompression
                WarmAudio(weapon.fireSound);
                WarmAudio(weapon.fireSoundTPV);
                WarmAudio(weapon.emptyMagazineSound);
                WarmAudio(weapon.impactSound);
                if (weapon.reloadSounds != null)
                {
                    foreach (var clip in weapon.reloadSounds)
                        WarmAudio(clip);
                }

                // Kill combo sounds
                WarmAudio(weapon.killSound);
                WarmAudio(weapon.climaxSound);

                // Tag impact overrides (e.g. Water → water sparks)
                if (weapon.tagImpactOverrides != null)
                {
                    foreach (var ov in weapon.tagImpactOverrides)
                    {
                        if (ov == null) continue;
                        WarmPrefabSync(ov.impactEffectPrefab);
                        yield return StartCoroutine(BudgetYield());
                        WarmAudio(ov.impactSound);
                    }
                }

                // Material: force shader warmup
                if (weapon.bulletTrailMaterial != null)
                {
                    // Accessing shader forces compilation
                    var shader = weapon.bulletTrailMaterial.shader;
                    _ = shader.name;
                }
            }

            // ── 5. Pre-warm character assets (abilities, death VFX) ──
            UpdateProgress(_step / (float)_totalSteps, "LOADING ABILITIES...");
            yield return null;
            _frameStartTime = Time.realtimeSinceStartup * 1000f;

            foreach (var character in allCharacters)
            {
                if (character == null) continue;
                Debug.Log($"[PreWarmManager] Pre-warming character: {character.characterId}");

                yield return StartCoroutine(WarmCharacterBudgeted(character));
                _warmedCharacters.Add(character.characterId);
            }

            // ── 6. Pre-warm all loose sound clips ──
            UpdateProgress(_step / (float)_totalSteps, "LOADING SOUNDS...");
            yield return null;

            foreach (var clip in allSounds)
            {
                WarmAudio(clip);
            }

            // ── 7. Final frame to let GPU finish ──
            UpdateProgress(1f, "READY");
            yield return null;
            yield return null; // Extra frame for GPU flush

            preWarmDone = true;
            Debug.Log("[PreWarmManager] Pre-warm complete — all assets loaded");

            // Invoke callbacks FIRST (spawns the player, assigns team, etc.)
            // so the player is fully ready before the loading screen hides.
            OnPreWarmComplete?.Invoke();
            onComplete?.Invoke();

            // Give the spawned player a few frames to finish setup
            // (PlayerSetup, CharacterController teleport, team assignment, etc.)
            yield return null;
            yield return null;
            yield return null;

            HideLoading();
        }

        // ──────────────────────────────────────────────
        // On-demand character pre-warm
        // ──────────────────────────────────────────────

        /// <summary>
        /// Pre-warm a character's abilities/meshes/materials if not already done.
        /// Call this when a remote player joins with a character that wasn't loaded at startup.
        /// Safe to call from anywhere — silently no-ops if already warmed.
        /// </summary>
        public void EnsureCharacterPreWarmed(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return;
            if (_warmedCharacters.Contains(characterId)) return;

            // Load the CharacterConfig from Resources
            var config = Resources.Load<CharacterConfig>($"Characters/{characterId.ToLower()}");
            if (config == null)
            {
                // Try capitalized fallback
                string capitalized = char.ToUpper(characterId[0]) + characterId.Substring(1).ToLower();
                config = Resources.Load<CharacterConfig>($"Characters/{capitalized}");
            }
            if (config == null)
            {
                Debug.LogWarning($"[PreWarmManager] Cannot pre-warm unknown character: {characterId}");
                return;
            }

            _warmedCharacters.Add(characterId);
            Debug.Log($"[PreWarmManager] On-demand pre-warm for character: {characterId}");
            StartCoroutine(WarmCharacterCoroutine(config));
        }

        /// <summary>
        /// Reusable coroutine that pre-warms all assets for a single CharacterConfig:
        /// death VFX, ability prefabs/sounds, meshes, and materials.
        /// </summary>
        private IEnumerator WarmCharacterCoroutine(CharacterConfig character)
        {
            // Death VFX
            yield return StartCoroutine(WarmPrefab(character.deathVFXPrefab));

            // Ability 1 (SmokeGrenade — Crimson)
            if (character.ability1 is SmokeGrenadeAbilityConfig smoke)
            {
                yield return StartCoroutine(WarmPrefab(smoke.grenadeFPVPrefab));
                yield return StartCoroutine(WarmPrefab(smoke.grenadeProjectilePrefab));
                yield return StartCoroutine(WarmPrefab(smoke.smokePrefab));
                yield return StartCoroutine(WarmPrefab(smoke.grenadePrefabTPV));
            }

            // Ability 2 (Dash) — no prefabs, nothing to pre-warm

            // Ability 1 (TsunamiWave — Pato)
            if (character.ability1 is TsunamiWaveAbilityConfig tsunami)
            {
                yield return StartCoroutine(WarmPrefab(tsunami.wavePrefab));
                if (tsunami.spawnSound != null) WarmAudio(tsunami.spawnSound);
            }

            // Ultimate (CrimsonUltimate — Crimson)
            if (character.ultimate is CrimsonUltimateAbilityConfig ult)
            {
                yield return StartCoroutine(WarmPrefab(ult.ultimateFPVPrefab));
                yield return StartCoroutine(WarmPrefab(ult.ultimateProjectilePrefab));
                yield return StartCoroutine(WarmPrefab(ult.ultimateEffectPrefab));
                yield return StartCoroutine(WarmPrefab(ult.ultimatePrefabTPV));
            }

            // Meshes — access to force GPU upload
            if (character.tpvMesh != null) _ = character.tpvMesh.vertexCount;
            if (character.armsMesh != null) _ = character.armsMesh.vertexCount;

            // Materials — force shader compile
            if (character.tpvMaterials != null)
                foreach (var mat in character.tpvMaterials)
                    if (mat != null) _ = mat.shader.name;
            if (character.armsMaterials != null)
                foreach (var mat in character.armsMaterials)
                    if (mat != null) _ = mat.shader.name;
        }

        // ──────────────────────────────────────────────
        // Warm helpers
        // ──────────────────────────────────────────────

        /// <summary>
        /// Yields a frame ONLY if the current frame has exceeded FrameBudgetMs.
        /// This keeps the main thread responsive for NetworkRunner.Update() heartbeats
        /// while still batching lightweight prefabs in the same frame.
        /// </summary>
        private IEnumerator BudgetYield()
        {
            float elapsed = Time.realtimeSinceStartup * 1000f - _frameStartTime;
            if (elapsed >= FrameBudgetMs)
            {
                yield return null; // actually yield a frame
                _frameStartTime = Time.realtimeSinceStartup * 1000f;
            }
        }

        /// <summary>
        /// Synchronous prefab warm: instantiate, force shader compile, destroy immediately.
        /// No per-prefab frame wait — the caller manages frame yields via BudgetYield().
        /// </summary>
        private void WarmPrefabSync(GameObject prefab)
        {
            if (prefab == null) return;

            Vector3 offScreen = new Vector3(0f, offScreenY, 0f);
            GameObject instance = null;

            try
            {
                instance = Instantiate(prefab, offScreen, Quaternion.identity);
                instance.name = $"[PreWarm] {prefab.name}";

                foreach (var src in instance.GetComponentsInChildren<AudioSource>(true))
                {
                    src.mute = true;
                    src.volume = 0f;
                    src.enabled = false;
                }

                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                }

                foreach (var vfx in instance.GetComponentsInChildren<VisualEffect>(true))
                {
                    vfx.Play();
                    vfx.Simulate(0.016f);
                }

                foreach (var rend in instance.GetComponentsInChildren<Renderer>(true))
                    rend.enabled = false;

                foreach (var nb in instance.GetComponentsInChildren<Fusion.NetworkBehaviour>(true))
                    nb.enabled = false;
                foreach (var no in instance.GetComponentsInChildren<Fusion.NetworkObject>(true))
                    no.enabled = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PreWarmManager] Failed to instantiate {prefab.name}: {e.Message}");
            }

            if (instance != null) Destroy(instance);

            _step++;
            UpdateProgress(_step / (float)_totalSteps);
        }

        /// <summary>
        /// Budgeted version of WarmCharacterCoroutine for use during startup pre-warm.
        /// Uses WarmPrefabSync + BudgetYield instead of coroutine yields per prefab.
        /// </summary>
        private IEnumerator WarmCharacterBudgeted(CharacterConfig character)
        {
            WarmPrefabSync(character.deathVFXPrefab);
            yield return StartCoroutine(BudgetYield());

            if (character.ability1 is SmokeGrenadeAbilityConfig smoke)
            {
                WarmPrefabSync(smoke.grenadeFPVPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(smoke.grenadeProjectilePrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(smoke.smokePrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(smoke.grenadePrefabTPV);
                yield return StartCoroutine(BudgetYield());
            }

            if (character.ability1 is TsunamiWaveAbilityConfig tsunami)
            {
                WarmPrefabSync(tsunami.wavePrefab);
                yield return StartCoroutine(BudgetYield());
                if (tsunami.spawnSound != null) WarmAudio(tsunami.spawnSound);
            }

            if (character.ultimate is CrimsonUltimateAbilityConfig ult)
            {
                WarmPrefabSync(ult.ultimateFPVPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(ult.ultimateProjectilePrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(ult.ultimateEffectPrefab);
                yield return StartCoroutine(BudgetYield());
                WarmPrefabSync(ult.ultimatePrefabTPV);
                yield return StartCoroutine(BudgetYield());
            }

            if (character.tpvMesh != null) _ = character.tpvMesh.vertexCount;
            if (character.armsMesh != null) _ = character.armsMesh.vertexCount;

            if (character.tpvMaterials != null)
                foreach (var mat in character.tpvMaterials)
                    if (mat != null) _ = mat.shader.name;
            if (character.armsMaterials != null)
                foreach (var mat in character.armsMaterials)
                    if (mat != null) _ = mat.shader.name;
        }

        /// <summary>
        /// Instantiate a prefab far off-screen, wait one frame (shader compile + GPU upload),
        /// then destroy it. Updates _step class field.
        /// Used by on-demand character warming (EnsureCharacterPreWarmed).
        /// </summary>
        private IEnumerator WarmPrefab(GameObject prefab)
        {
            if (prefab == null)
                yield break;

            // Instantiate off-screen so nothing is visible or audible
            Vector3 offScreen = new Vector3(0f, offScreenY, 0f);
            GameObject instance = null;

            try
            {
                instance = Instantiate(prefab, offScreen, Quaternion.identity);
                instance.name = $"[PreWarm] {prefab.name}";

                // Disable ALL AudioSources so no sound is produced
                foreach (var src in instance.GetComponentsInChildren<AudioSource>(true))
                {
                    src.mute = true;
                    src.volume = 0f;
                    src.enabled = false;
                }

                // Disable ALL ParticleSystems emission
                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var emission = ps.emission;
                    emission.enabled = false;
                }

                // Force VFX Graph shader compilation: Play + Simulate
                foreach (var vfx in instance.GetComponentsInChildren<VisualEffect>(true))
                {
                    vfx.Play();
                    vfx.Simulate(0.016f);
                }

                // Hide renderers so nothing is visually drawn (GPU still compiles shaders)
                foreach (var rend in instance.GetComponentsInChildren<Renderer>(true))
                    rend.enabled = false;

                // Disable any NetworkBehaviour / Fusion components to avoid network errors
                foreach (var nb in instance.GetComponentsInChildren<Fusion.NetworkBehaviour>(true))
                    nb.enabled = false;
                foreach (var no in instance.GetComponentsInChildren<Fusion.NetworkObject>(true))
                    no.enabled = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PreWarmManager] Failed to instantiate {prefab.name}: {e.Message}");
                if (instance != null) Destroy(instance);
                yield break;
            }

            // Wait one frame — the MeshRenderer/SkinnedMeshRenderer will trigger shader compilation
            yield return null;

            // Destroy the temp object
            if (instance != null)
                Destroy(instance);

            _step++;
            UpdateProgress(_step / (float)_totalSteps);
        }

        /// <summary>
        /// Play an AudioClip at volume 0 via a muted source to force decompression.
        /// Synchronous (does not wait a frame).
        /// </summary>
        private void WarmAudio(AudioClip clip)
        {
            if (clip == null) return;

            try
            {
                // LoadAudioData forces decompression
                clip.LoadAudioData();

                // Also play at zero volume to ensure the audio subsystem processes it
                muteSource.PlayOneShot(clip, 0f);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PreWarmManager] Failed to pre-warm audio {clip.name}: {e.Message}");
            }

            _step++;
            UpdateProgress(_step / (float)_totalSteps);
        }

        // ──────────────────────────────────────────────
        // Asset count helpers (for progress calculation)
        // ──────────────────────────────────────────────

        private int CountWeaponPrefabs(WeaponConfig w)
        {
            if (w == null) return 0;
            int count = 0;
            if (w.weaponPrefab != null) count++;
            if (w.muzzleFlashPrefab != null) count++;
            if (w.tpvMuzzleFlashPrefab != null) count++;
            if (w.impactEffectPrefab != null) count++;
            if (w.headBloodPrefab != null) count++;
            if (w.bodyBloodPrefab != null) count++;
            if (w.prefabWeaponTPV != null) count++;
            if (w.tpvTrailPrefab != null) count++;
            if (w.tagImpactOverrides != null)
                foreach (var ov in w.tagImpactOverrides)
                    if (ov?.impactEffectPrefab != null) count++;
            return count;
        }

        private int CountWeaponAudio(WeaponConfig w)
        {
            if (w == null) return 0;
            int count = 0;
            if (w.fireSound != null) count++;
            if (w.fireSoundTPV != null) count++;
            if (w.emptyMagazineSound != null) count++;
            if (w.impactSound != null) count++;
            if (w.killSound != null) count++;
            if (w.climaxSound != null) count++;
            if (w.reloadSounds != null) count += w.reloadSounds.Length;
            if (w.tagImpactOverrides != null)
                foreach (var ov in w.tagImpactOverrides)
                    if (ov?.impactSound != null) count++;
            return count;
        }

        private int CountCharacterAssets(CharacterConfig c)
        {
            if (c == null) return 0;
            int count = 0;
            if (c.deathVFXPrefab != null) count++;
            if (c.ability1 is SmokeGrenadeAbilityConfig smoke)
            {
                if (smoke.grenadeFPVPrefab != null) count++;
                if (smoke.grenadeProjectilePrefab != null) count++;
                if (smoke.smokePrefab != null) count++;
                if (smoke.grenadePrefabTPV != null) count++;
            }
            if (c.ability1 is TsunamiWaveAbilityConfig tsunami)
            {
                if (tsunami.wavePrefab != null) count++;
            }
            if (c.ultimate is CrimsonUltimateAbilityConfig ult)
            {
                if (ult.ultimateFPVPrefab != null) count++;
                if (ult.ultimateProjectilePrefab != null) count++;
                if (ult.ultimateEffectPrefab != null) count++;
                if (ult.ultimatePrefabTPV != null) count++;
            }
            return count;
        }
    }
}
