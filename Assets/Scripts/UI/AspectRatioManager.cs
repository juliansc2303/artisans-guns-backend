using UnityEngine;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// AspectRatioManager - Asegura que la cÃ¡mara mantenga aspect ratio 16:9
    /// AÃ±ade letterbox (cuadros negros) si es necesario
    /// Compatible con UIDocument (UI Toolkit) y Canvas (UGUI)
    /// </summary>
    public class AspectRatioManager : MonoBehaviour
    {
        private const float TARGET_ASPECT_RATIO = 16f / 9f; // 1.777...
        private Camera mainCamera;

        private void Awake()
        {
            // Obtener la cÃ¡mara principal
            mainCamera = Camera.main;
            if (mainCamera == null)
                mainCamera = GetComponent<Camera>();

            // Aplicar aspect ratio a la cÃ¡mara
            if (mainCamera != null)
            {
                SetCameraAspectRatio();
            }
            else
            {
                // Debug.LogWarning("âš ï¸ AspectRatioManager: No Camera found in scene");
            }
        }

        private void SetCameraAspectRatio()
        {
            float currentAspect = (float)Screen.width / Screen.height;
            
            // Debug.Log($"ðŸ“± Current Screen: {Screen.width}x{Screen.height} (aspect: {currentAspect:F3})");
            // Debug.Log($"ðŸŽ¯ Target Aspect: {TARGET_ASPECT_RATIO:F3} (16:9)");

            if (Mathf.Abs(currentAspect - TARGET_ASPECT_RATIO) < 0.01f)
            {
                // Aspect ratio es casi 16:9, usar viewport completo
                mainCamera.rect = new Rect(0, 0, 1, 1);
                // Debug.Log("âœ… Screen aspect ratio matches 16:9, no letterbox needed");
            }
            else if (currentAspect > TARGET_ASPECT_RATIO)
            {
                // Pantalla mÃ¡s ancha que 16:9 - aÃ±adir barras negras a los lados (pillarbox)
                float targetHeight = 1f;
                float targetWidth = TARGET_ASPECT_RATIO / currentAspect;
                float xOffset = (1f - targetWidth) / 2f;

                mainCamera.rect = new Rect(xOffset, 0, targetWidth, targetHeight);
                // Debug.Log($"ðŸŽ¬ Adding pillarbox (barras a los lados): viewport width = {targetWidth:F3}");
            }
            else
            {
                // Pantalla mÃ¡s estrecha que 16:9 - aÃ±adir barras negras arriba/abajo (letterbox)
                float targetWidth = 1f;
                float targetHeight = currentAspect / TARGET_ASPECT_RATIO;
                float yOffset = (1f - targetHeight) / 2f;

                mainCamera.rect = new Rect(0, yOffset, targetWidth, targetHeight);
                // Debug.Log($"ðŸŽ¬ Adding letterbox (barras arriba/abajo): viewport height = {targetHeight:F3}");
            }

            // Asegurar que el background sea negro para el letterbox
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;
        }

        // MÃ©todo pÃºblico para resetear aspect ratio si es necesario
        public void RefreshAspectRatio()
        {
            if (mainCamera != null)
            {
                SetCameraAspectRatio();
            }
        }
    }
}
