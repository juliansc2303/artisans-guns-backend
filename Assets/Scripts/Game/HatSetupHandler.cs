using UnityEngine;
using Fusion;
using ArtisansGuns.Data;
using ArtisansGuns.Networking;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// Reads SelectedHat from PlayerNetworkData and instantiates the hat prefab
    /// on the TPV model's head bone with per-agent transform offsets.
    /// Add this to the same player prefab that has CharacterSetupHandler.
    /// </summary>
    public class HatSetupHandler : NetworkBehaviour
    {
        [Tooltip("Override: drag a specific Transform to parent hats to. " +
                 "If left empty, the script searches for 'mixamorig:Head' under the TPV model.")]
        public Transform hatSpawnPoint;

        private GameObject _hatInstance;
        private bool _hatApplied;
        private string _lastHatId;

        public override void Spawned()
        {
            _hatApplied = false;
            _lastHatId = null;
        }

        public override void Render()
        {
            var netData = GetComponent<PlayerNetworkData>();
            if (netData == null) return;

            string hatId = netData.SelectedHat.ToString();
            if (hatId == _lastHatId) return; // no change

            _lastHatId = hatId;
            _hatApplied = false;

            // Destroy previous hat
            if (_hatInstance != null)
            {
                Destroy(_hatInstance);
                _hatInstance = null;
            }

            if (string.IsNullOrEmpty(hatId) || hatId == "none") { _hatApplied = true; return; }

            HatDefinition hatDef = HatDefinition.GetHatById(hatId);
            if (hatDef == null || hatDef.hatPrefab == null) { _hatApplied = true; return; }

            Transform parent = ResolveSpawnPoint();
            if (parent == null) return; // retry next frame

            string agentId = netData.SelectedAgent.ToString().ToLower();
            var offset = hatDef.GetOffsetForAgent(agentId);

            _hatInstance = Instantiate(hatDef.hatPrefab, parent);
            _hatInstance.transform.localPosition = offset?.position ?? Vector3.zero;
            _hatInstance.transform.localRotation = Quaternion.Euler(offset?.rotation ?? Vector3.zero);
            _hatInstance.transform.localScale = offset?.scale ?? Vector3.one;

            _hatApplied = true;
        }

        private Transform ResolveSpawnPoint()
        {
            if (hatSpawnPoint != null) return hatSpawnPoint;

            // Search for head bone in TPV skeleton
            var playerSetup = GetComponent<PlayerSetup>();
            if (playerSetup == null || playerSetup.tpvSkinnedMeshRenderer == null) return null;

            Transform root = playerSetup.tpvSkinnedMeshRenderer.transform.parent ?? playerSetup.tpvSkinnedMeshRenderer.transform;
            hatSpawnPoint = FindDeepChild(root, "mixamorig:Head");
            return hatSpawnPoint;
        }

        private static Transform FindDeepChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var result = FindDeepChild(child, name);
                if (result != null) return result;
            }
            return null;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_hatInstance != null)
            {
                Destroy(_hatInstance);
                _hatInstance = null;
            }
        }
    }
}
