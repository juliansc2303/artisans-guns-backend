using UnityEngine;
using Fusion;
using ArtisansGuns.Networking;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// Lives on every PlayerPrefab.  In Render() each client evaluates the relationship
    /// between this player and the local player, then sets layer recursively on the TPV
    /// hierarchy:
    ///   • Same team as local → layer "Teammate" (10)
    ///   • Different team      → layer "Enemy"    (9)
    ///   • Local player itself → no change (TPV hidden anyway)
    ///
    /// This is client-local — each client sees its own layer assignment.
    /// FireWeapon will use a LayerMask that includes only Default + Enemy.
    /// </summary>
    [RequireComponent(typeof(PlayerNetworkData))]
    public class TeamLayerAssigner : NetworkBehaviour
    {
        // Cache the layer indices once
        private static int EnemyLayer    = -1;
        private static int TeammateLayer = -1;
        private static int PlayerLayer   = -1;

        private PlayerNetworkData myNetData;
        private PlayerTPVController tpvController;
        private bool layerAssigned;

        public override void Spawned()
        {
            myNetData = GetComponent<PlayerNetworkData>();

            // Find TPV controller from PlayerSetup
            var setup = GetComponent<PlayerSetup>();
            if (setup != null)
                tpvController = setup.tpvController;

            if (EnemyLayer < 0)
            {
                EnemyLayer    = LayerMask.NameToLayer("Enemy");
                TeammateLayer = LayerMask.NameToLayer("Teammate");
                PlayerLayer   = LayerMask.NameToLayer("Player");
            }

            layerAssigned = false;
        }

        public override void Render()
        {
            if (layerAssigned) return;

            // Local player: set TPV to "Player" layer so the local camera doesn't render it
            if (Object.HasInputAuthority)
            {
                if (PlayerLayer < 0) return;
                GameObject tpvRoot = tpvController != null ? tpvController.playerTPVRoot : null;
                if (tpvRoot == null) return;

                SetLayerRecursive(tpvRoot, PlayerLayer);
                // Also set the weapon holder + any spawned weapon
                if (tpvController != null)
                    tpvController.SetLocalPlayerLayer(PlayerLayer);
                layerAssigned = true;
                Debug.Log($"[TeamLayerAssigner] Local player TPV → layer Player");
                return;
            }

            // Wait until this player's team is assigned
            if (!myNetData.TeamAssigned) return;

            // Find local player's team
            int localTeam = GetLocalPlayerTeam();
            if (localTeam < 0) return; // not ready yet

            // Determine layer
            int targetLayer = (myNetData.Team == localTeam) ? TeammateLayer : EnemyLayer;
            if (targetLayer < 0) return; // layers don't exist

            // Apply to TPV root + all children
            GameObject remoteTPVRoot = null;
            if (tpvController != null)
                remoteTPVRoot = tpvController.playerTPVRoot;

            if (remoteTPVRoot == null) return;

            SetLayerRecursive(remoteTPVRoot, targetLayer);
            layerAssigned = true;

            Debug.Log($"[TeamLayerAssigner] {myNetData.Username} → layer {LayerMask.LayerToName(targetLayer)} (team {myNetData.Team} vs local {localTeam})");
        }

        // ────────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────────

        private int GetLocalPlayerTeam()
        {
            if (!Runner) return -1;

            var localObj = Runner.GetPlayerObject(Runner.LocalPlayer);
            if (localObj == null) return -1;

            var localData = localObj.GetComponent<PlayerNetworkData>();
            if (localData == null || !localData.TeamAssigned) return -1;

            return localData.Team;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        /// <summary>
        /// Call this to force re-evaluation (e.g. after respawn if teams could change).
        /// </summary>
        public void ResetLayerAssignment()
        {
            layerAssigned = false;
        }
    }
}
