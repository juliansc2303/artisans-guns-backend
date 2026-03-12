using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;
using Fusion;
using ArtisansGuns.Networking;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// Creates a world-space Canvas above the player showing nickname + health bar.
    /// Visible only to teammates, rendered through walls (ZTest Always), billboards toward camera.
    /// Attach to the PlayerPrefab root alongside PlayerHealth, PlayerNetworkData, etc.
    /// </summary>
    public class TeammateIndicator : NetworkBehaviour
    {
        [Header("Position")]
        [Tooltip("Height above the player's pivot (feet) where the indicator appears")]
        [SerializeField] private float heightOffset = 2.3f;

        [Header("Scale")]
        [Tooltip("World-space canvas scale (smaller = smaller on screen)")]
        [SerializeField] private float canvasScale = 0.008f;

        [Header("Font")]
        [Tooltip("TMP Font Asset for the nickname text (drag WhirlyBirdie-WideBold SDF here)")]
        [SerializeField] private TMP_FontAsset nicknameFont;

        [Header("Debug")]
        [Tooltip("When true, show indicator to ALL players regardless of team (for testing)")]
        [SerializeField] private bool debugShowAlways = false;

        // ── Runtime refs ────────────────────────────────────────────────
        private PlayerNetworkData netData;
        private PlayerHealth playerHealth;

        private Canvas canvas;
        private TextMeshProUGUI nicknameText;
        private Image healthFill;
        private Image healthBG;

        private Transform cam;
        private bool initialized;
        private string lastNickname;

        // ── Fusion lifecycle ────────────────────────────────────────────
        public override void Spawned()
        {
            netData      = GetComponent<PlayerNetworkData>();
            playerHealth = GetComponent<PlayerHealth>();

            // Local player never sees their own indicator
            if (HasInputAuthority)
            {
                enabled = false;
                return;
            }

            BuildUI();
            initialized = true;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (canvas != null)
                Destroy(canvas.gameObject);
        }

        public override void Render()
        {
            if (!initialized) return;

            bool shouldShow = ShouldShow();
            if (canvas.gameObject.activeSelf != shouldShow)
                canvas.gameObject.SetActive(shouldShow);

            if (!shouldShow) return;

            UpdateHealth();
            UpdateNickname();
        }

        private void LateUpdate()
        {
            if (!initialized || canvas == null || !canvas.gameObject.activeSelf) return;
            Billboard();
        }

        // ── Visibility logic ────────────────────────────────────────────
        private bool ShouldShow()
        {
            // Hide while dead
            if (playerHealth != null && playerHealth.IsDead) return false;

            // Debug: show for all players regardless of team
            if (debugShowAlways) return true;

            // Need team info
            if (!netData.TeamAssigned) return false;

            int localTeam = GetLocalPlayerTeam();
            if (localTeam < 0) return false;

            return netData.Team == localTeam;
        }

        // ── Billboard ───────────────────────────────────────────────────
        private void Billboard()
        {
            if (cam == null)
            {
                // Camera.main may be null (base camera isn't always tagged MainCamera).
                // Find the local player's base camera instead.
                cam = FindLocalPlayerCamera();
                if (cam == null) return;
            }

            // Face toward the camera — instant, no lerp
            // cam.rotation faces away; flip 180° on Y so the canvas front faces the viewer
            canvas.transform.rotation = cam.rotation;
        }

        private Transform FindLocalPlayerCamera()
        {
            // Try Camera.main first
            Camera main = Camera.main;
            if (main != null) return main.transform;

            // Fallback: find the local player's camera via Fusion
            if (Runner == null) return null;
            var localObj = Runner.GetPlayerObject(Runner.LocalPlayer);
            if (localObj == null) return null;

            // The base camera is a child of the player prefab
            foreach (var c in localObj.GetComponentsInChildren<Camera>(true))
            {
                var urpData = c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (urpData == null || urpData.renderType == UnityEngine.Rendering.Universal.CameraRenderType.Base)
                    return c.transform;
            }

            return null;
        }

        // ── Health ──────────────────────────────────────────────────────
        private void UpdateHealth()
        {
            float ratio = Mathf.Clamp01(playerHealth.HP / PlayerHealth.MAX_HP);

            // Shrink fill from the right
            healthFill.rectTransform.anchorMax = new Vector2(ratio, 1f);

            // Green → Yellow → Red
            if (ratio > 0.5f)
                healthFill.color = new Color(0.18f, 0.85f, 0.25f, 0.95f);
            else if (ratio > 0.25f)
                healthFill.color = new Color(0.92f, 0.85f, 0.15f, 0.95f);
            else
                healthFill.color = new Color(0.92f, 0.20f, 0.15f, 0.95f);
        }

        // ── Nickname ────────────────────────────────────────────────────
        private void UpdateNickname()
        {
            string name = netData.CharacterName.ToString();
            if (string.IsNullOrEmpty(name) || name == lastNickname) return;
            nicknameText.text = name;
            lastNickname = name;
        }

        // ── Helper: local player's team ─────────────────────────────────
        private int GetLocalPlayerTeam()
        {
            if (!Runner) return -1;
            var localObj = Runner.GetPlayerObject(Runner.LocalPlayer);
            if (localObj == null) return -1;
            var data = localObj.GetComponent<PlayerNetworkData>();
            if (data == null || !data.TeamAssigned) return -1;
            return data.Team;
        }

        // ── UI construction (all programmatic — no prefab needed) ───────
        private void BuildUI()
        {
            // Root
            GameObject root = new GameObject("TeammateIndicator");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(0f, heightOffset, 0f);
            root.transform.localScale    = Vector3.one * canvasScale;

            canvas = root.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            // No GraphicRaycaster — indicator should never intercept input

            RectTransform canvasRT = canvas.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(400f, 80f);

            // ── Nickname ────────────────────────────────────────────────
            GameObject textGO = new GameObject("Nickname");
            textGO.transform.SetParent(root.transform, false);
            nicknameText = textGO.AddComponent<TextMeshProUGUI>();
            nicknameText.text      = "";
            nicknameText.fontSize  = 28;
            nicknameText.alignment = TextAlignmentOptions.Center;
            nicknameText.color     = Color.white;
            nicknameText.outlineWidth = 0.25f;
            nicknameText.outlineColor = Color.black;
            nicknameText.enableWordWrapping = false;
            nicknameText.overflowMode = TextOverflowModes.Ellipsis;

            // Use the project font if assigned in Inspector
            if (nicknameFont != null)
                nicknameText.font = nicknameFont;

            RectTransform textRT = nicknameText.rectTransform;
            textRT.anchorMin        = new Vector2(0f, 0.35f);
            textRT.anchorMax        = new Vector2(1f, 1f);
            textRT.offsetMin        = Vector2.zero;
            textRT.offsetMax        = Vector2.zero;

            // ── Health bar background ───────────────────────────────────
            GameObject bgGO = new GameObject("HealthBG");
            bgGO.transform.SetParent(root.transform, false);
            healthBG = bgGO.AddComponent<Image>();
            healthBG.color = new Color(0.15f, 0.15f, 0.15f, 0.75f);

            RectTransform bgRT = healthBG.rectTransform;
            bgRT.anchorMin = new Vector2(0.1f, 0.02f);
            bgRT.anchorMax = new Vector2(0.9f, 0.3f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            // ── Health bar fill ─────────────────────────────────────────
            GameObject fillGO = new GameObject("HealthFill");
            fillGO.transform.SetParent(bgGO.transform, false);
            healthFill = fillGO.AddComponent<Image>();
            healthFill.color = new Color(0.18f, 0.85f, 0.25f, 0.95f);

            RectTransform fillRT = healthFill.rectTransform;
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = Vector2.zero;
            fillRT.offsetMax = Vector2.zero;
            fillRT.pivot     = new Vector2(0f, 0.5f);

            // ── ZTest Always (render through walls) ─────────────────────
            ApplyAlwaysOnTop(healthBG);
            ApplyAlwaysOnTop(healthFill);
            ApplyAlwaysOnTopTMP(nicknameText);

            canvas.gameObject.SetActive(false);
        }

        // ── Material helpers for through-wall rendering ─────────────────
        private static void ApplyAlwaysOnTop(Image img)
        {
            Material mat = new Material(Shader.Find("UI/Default"));
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            img.material = mat;
        }

        private static void ApplyAlwaysOnTopTMP(TextMeshProUGUI tmp)
        {
            // Instantiate so we don't modify the shared font asset material
            Material mat = new Material(tmp.fontSharedMaterial);
            mat.SetFloat("_ZTestMode", (float)CompareFunction.Always);
            // Belt-and-suspenders: also try the GUI property name
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            tmp.fontMaterial = mat;
        }
    }
}
