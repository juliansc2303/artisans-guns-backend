using UnityEngine;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// GameSceneAspectRatio - Configura automÃ¡ticamente aspect ratio 16:9 en GameScene
    /// Coloca esto en GameManager o en cualquier GameObject que exista en GameScene
    /// </summary>
    public class GameSceneAspectRatio : MonoBehaviour
    {
        private const float TARGET_ASPECT_RATIO = 16f / 9f;

        private void Awake()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                // Debug.LogWarning("âš ï¸ GameSceneAspectRatio: MainCamera not found");
                return;
            }

            ApplyAspectRatio(mainCamera);
        }

        private void ApplyAspectRatio(Camera camera)
        {
            // Asegurar background negro
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            float currentAspect = (float)Screen.width / Screen.height;

            // Debug.Log($"ðŸ“± GameScene - Screen: {Screen.width}x{Screen.height} (aspect: {currentAspect:F3})");
            // Debug.Log($"ðŸŽ¯ Target aspect: {TARGET_ASPECT_RATIO:F3} (16:9)");

            if (Mathf.Abs(currentAspect - TARGET_ASPECT_RATIO) < 0.01f)
            {
                // Es 16:9 exacta
                camera.rect = new Rect(0, 0, 1, 1);
                // Debug.Log("âœ… Screen is 16:9 - Full viewport");
            }
            else if (currentAspect > TARGET_ASPECT_RATIO)
            {
                // Pantalla ultra-wide - pillarbox
                float height = 1f;
                float width = TARGET_ASPECT_RATIO / currentAspect;
                float x = (1f - width) / 2f;
                camera.rect = new Rect(x, 0, width, height);
                // Debug.Log($"ðŸŽ¬ Ultra-wide screen detected - Adding pillarbox");
            }
            else
            {
                // Pantalla narrow - letterbox
                float width = 1f;
                float height = currentAspect / TARGET_ASPECT_RATIO;
                float y = (1f - height) / 2f;
                camera.rect = new Rect(0, y, width, height);
                // Debug.Log($"ðŸŽ¬ Narrow screen detected - Adding letterbox");
            }
        }
    }
}
