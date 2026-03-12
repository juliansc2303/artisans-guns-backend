using UnityEngine;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// SceneAspectRatioSetup - ConfiguraciÃ³n automÃ¡tica de aspect ratio 16:9
    /// Compatible con UIDocument (UI Toolkit) y Canvas (UGUI)
    /// Coloca este script en cualquier GameObject de la escena
    /// </summary>
    public class SceneAspectRatioSetup : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool debugMode = false;
        
        private const float TARGET_ASPECT_RATIO = 16f / 9f;

        private void Start()
        {
            SetupAspectRatio();
        }

        private void SetupAspectRatio()
        {
            // Buscar y configurar CÃ¡mara Principal
            Camera camera = Camera.main;
            if (camera != null)
            {
                ConfigureCamera(camera);
            }
            else
            {
                // if (debugMode)
                    // Debug.LogWarning("âš ï¸ SceneAspectRatioSetup: No main camera found in scene");
            }
        }

        private void ConfigureCamera(Camera camera)
        {
            // Configurar background color para letterbox (negro)
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            // Aplicar aspect ratio
            float screenAspect = (float)Screen.width / Screen.height;

            if (Mathf.Abs(screenAspect - TARGET_ASPECT_RATIO) < 0.01f)
            {
                // Es 16:9 exacta, viewport completo
                camera.rect = new Rect(0, 0, 1, 1);
                // if (debugMode) Debug.Log($"📷 Camera: Full viewport (16:9 match)");
            }
            else if (screenAspect > TARGET_ASPECT_RATIO)
            {
                // Pantalla mÃ¡s ancha - pillarbox (barras a los lados)
                float targetHeight = 1f;
                float targetWidth = TARGET_ASPECT_RATIO / screenAspect;
                float xOffset = (1f - targetWidth) / 2f;
                camera.rect = new Rect(xOffset, 0, targetWidth, targetHeight);
                // if (debugMode) Debug.Log($"📷 Pillarbox applied (ultra-wide screen)");
            }
            else
            {
                // Pantalla mÃ¡s estrecha - letterbox (barras arriba/abajo)
                float targetWidth = 1f;
                float targetHeight = screenAspect / TARGET_ASPECT_RATIO;
                float yOffset = (1f - targetHeight) / 2f;
                camera.rect = new Rect(0, yOffset, targetWidth, targetHeight);
                // if (debugMode) Debug.Log($"📷 Letterbox applied (narrow screen)");
            }

            if (debugMode)
            {
                // Debug.Log($"âœ… SceneAspectRatioSetup complete");
                // Debug.Log($"   Screen: {Screen.width}x{Screen.height} ({screenAspect:F3})");
                // Debug.Log($"   Target: 16:9 ({TARGET_ASPECT_RATIO:F3})");
            }
        }
    }
}
