using UnityEngine;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// CanvasAutoScaler - Script auxiliar para configurar UI Toolkit (UIDocument)
    /// Si necesitas Canvas UGUI, usa AspectRatioManager en su lugar
    /// </summary>
    public class CanvasAutoScaler : MonoBehaviour
    {
        private Camera mainCamera;

        private void OnEnable()
        {
            // Configurar la cÃ¡mara para aspect ratio 16:9
            mainCamera = Camera.main;
            if (mainCamera == null)
                mainCamera = GetComponent<Camera>();

            if (mainCamera != null)
            {
                ConfigureAspectRatio();
            }
        }

        private void ConfigureAspectRatio()
        {
            const float TARGET_ASPECT = 16f / 9f;
            float currentAspect = (float)Screen.width / Screen.height;

            // Asegurar background negro
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;

            // Aplicar letra/pillarbox si es necesario
            if (Mathf.Abs(currentAspect - TARGET_ASPECT) > 0.01f)
            {
                if (currentAspect > TARGET_ASPECT)
                {
                    // Pillarbox
                    float height = 1f;
                    float width = TARGET_ASPECT / currentAspect;
                    float x = (1f - width) / 2f;
                    mainCamera.rect = new Rect(x, 0, width, height);
                }
                else
                {
                    // Letterbox
                    float width = 1f;
                    float height = currentAspect / TARGET_ASPECT;
                    float y = (1f - height) / 2f;
                    mainCamera.rect = new Rect(0, y, width, height);
                }
            }
        }

        public void ResetToDefault()
        {
            ConfigureAspectRatio();
        }
    }
}
