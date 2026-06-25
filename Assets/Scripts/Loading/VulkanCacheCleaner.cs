using UnityEngine;
using UnityEngine.Rendering;

namespace ArtisansGuns.Loading
{
    /// <summary>
    /// Clears the Vulkan pipeline cache when the app version changes.
    /// Corrupted Mali caches are a known cause of crashes on MediaTek GPUs.
    /// Runs before the splash screen — no scene or MonoBehaviour needed.
    /// </summary>
    public static class VulkanCacheCleaner
    {
        private const string LastVersionKey = "vulkan_cache_version";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void CleanStaleCacheIfNeeded()
        {
            // Only relevant on Vulkan
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
                return;

            string currentVersion = Application.version;
            string lastVersion = PlayerPrefs.GetString(LastVersionKey, "");

            if (lastVersion == currentVersion)
                return;

            // Version changed — delete Vulkan pipeline cache files
            string cachePath = Application.temporaryCachePath;
            try
            {
                // Unity stores Vulkan pipeline caches as .vkpipelinecache files
                if (System.IO.Directory.Exists(cachePath))
                {
                    foreach (string file in System.IO.Directory.GetFiles(cachePath, "*.vkpipelinecache"))
                    {
                        System.IO.File.Delete(file);
                    }
                    // Also clean any generic pipeline cache blobs
                    foreach (string file in System.IO.Directory.GetFiles(cachePath, "vulkan_*"))
                    {
                        System.IO.File.Delete(file);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VulkanCacheCleaner] Failed to clear pipeline cache: {e.Message}");
            }

            PlayerPrefs.SetString(LastVersionKey, currentVersion);
            PlayerPrefs.Save();
            Debug.Log($"[VulkanCacheCleaner] Cleared Vulkan pipeline cache (version: {lastVersion} → {currentVersion})");
        }
    }
}
