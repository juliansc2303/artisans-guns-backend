using UnityEngine;
using Fusion;
using ArtisansGuns.Abilities;
using ArtisansGuns.Loading;
using ArtisansGuns.Managers;

namespace ArtisansGuns.Characters
{
    /// <summary>
    /// Reads the character selection from the network and applies:
    ///   • TPV mesh swap  (third-person view body)
    ///   • Arms mesh swap (first-person view arms)
    ///   • Ability HUD initialisation (local player only)
    ///
    /// Add this component to the same player prefab that has PlayerSetup + AbilitySystem.
    /// It must sit AFTER PlayerSetup and AbilitySystem in the script execution order
    /// (or rely on Spawned() ordering — Fusion calls Spawned() after Start()).
    ///
    /// CharacterConfig assets must live at:
    ///   Resources/Characters/<characterId>   (case-insensitive match)
    ///   e.g.  Resources/Characters/crimson
    /// </summary>
    public class CharacterSetupHandler : NetworkBehaviour
    {
        // True once TPV + Arms meshes have been applied for this instance.
        private bool _meshesApplied = false;

        public override void Spawned()
        {
            if (Object.HasInputAuthority)
            {
                // Fast path for local player — LoadoutManager is usually ready here.
                TryApplyLocal();
            }
            // Remote players: SelectedAgent arrives via RPC AFTER Spawned().
            // Both local (if fast path failed) and remote are retried in Render().
        }

        public override void Render()
        {
            if (_meshesApplied) return;

            if (Object.HasInputAuthority)
                TryApplyLocal();
            else
                TryApplyRemote();
        }

        private void TryApplyLocal()
        {
            string characterId = ResolveLocalCharacterId();

            // Ensure this character's abilities are pre-warmed (no-op if already done at startup)
            PreWarmManager.Instance?.EnsureCharacterPreWarmed(characterId);

            CharacterConfig cfg = LoadConfig(characterId);
            if (cfg == null)
            {
                Debug.LogWarning($"[CharacterSetupHandler] LOCAL config not found for '{characterId}' — will retry in Render()");
                return;
            }

            ApplyMeshes(cfg, applyArms: true);

            var abilitySystem = GetComponent<AbilitySystem>();
            if (abilitySystem != null)
                abilitySystem.Initialize(cfg);

            _meshesApplied = true;
            Debug.Log($"[CharacterSetupHandler] LOCAL mesh applied: {characterId}");
        }

        private void TryApplyRemote()
        {
            var netData = GetComponent<ArtisansGuns.Networking.PlayerNetworkData>();
            if (netData == null) return;

            string agentStr = netData.SelectedAgent.ToString();
            if (string.IsNullOrEmpty(agentStr)) return;

            // Ensure this character's abilities are pre-warmed before they can be used
            PreWarmManager.Instance?.EnsureCharacterPreWarmed(agentStr.ToLower());

            CharacterConfig cfg = LoadConfig(agentStr.ToLower());
            if (cfg == null) return;

            ApplyMeshes(cfg, applyArms: false);
            _meshesApplied = true;
            Debug.Log($"[CharacterSetupHandler] REMOTE mesh applied: {agentStr}");
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private string ResolveLocalCharacterId()
        {
            var lm = LoadoutManager.Instance;
            if (lm != null && lm.IsInitialized())
                return lm.GetLoadout().selectedCharacter?.ToLower() ?? "crimson";
            return "crimson";
        }

        private CharacterConfig LoadConfig(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) characterId = "crimson";

            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{characterId}");
            if (cfg == null)
            {
                string capitalised = char.ToUpper(characterId[0]) + characterId.Substring(1).ToLower();
                cfg = Resources.Load<CharacterConfig>($"Characters/{capitalised}");
            }
            if (cfg == null)
                Debug.LogWarning($"[CharacterSetupHandler] No CharacterConfig found for '{characterId}' in Resources/Characters/");
            return cfg;
        }

        /// <summary>
        /// Applies mesh + materials from cfg.
        /// applyArms = true for local player (FPV arms), false for remote (TPV body only).
        /// </summary>
        private void ApplyMeshes(CharacterConfig cfg, bool applyArms)
        {
            var playerSetup = GetComponent<ArtisansGuns.Game.PlayerSetup>();
            if (playerSetup == null)
            {
                Debug.LogWarning("[CharacterSetupHandler] PlayerSetup component is null!");
                return;
            }

            // --- Diagnostic: show exactly what the config contains ---
            Debug.Log($"[CharacterSetupHandler] ApplyMeshes cfg='{cfg.characterId}' " +
                      $"tpvMesh={(cfg.tpvMesh != null ? cfg.tpvMesh.name : "NULL")} " +
                      $"tpvMats={(cfg.tpvMaterials != null ? cfg.tpvMaterials.Length.ToString() : "NULL")} " +
                      $"armsMesh={(cfg.armsMesh != null ? cfg.armsMesh.name : "NULL")} " +
                      $"armsMats={(cfg.armsMaterials != null ? cfg.armsMaterials.Length.ToString() : "NULL")} " +
                      $"applyArms={applyArms}");

            // TPV body: all instances (everyone sees other players' bodies).
            if (playerSetup.tpvSkinnedMeshRenderer != null)
            {
                var tpvSMR = playerSetup.tpvSkinnedMeshRenderer;
                Debug.Log($"[CharacterSetupHandler] TPV SMR GO='{tpvSMR.gameObject.name}' " +
                          $"beforeMesh={(tpvSMR.sharedMesh != null ? tpvSMR.sharedMesh.name : "NULL")} " +
                          $"beforeMats={tpvSMR.sharedMaterials.Length}");

                if (cfg.tpvMesh != null)
                    tpvSMR.sharedMesh = cfg.tpvMesh;
                if (cfg.tpvMaterials != null && cfg.tpvMaterials.Length > 0)
                    tpvSMR.sharedMaterials = cfg.tpvMaterials;

                Debug.Log($"[CharacterSetupHandler] TPV SMR afterMesh={(tpvSMR.sharedMesh != null ? tpvSMR.sharedMesh.name : "NULL")} " +
                          $"afterMats={tpvSMR.sharedMaterials.Length}");
            }
            else
            {
                Debug.LogWarning($"[CharacterSetupHandler] tpvSkinnedMeshRenderer is null — assign it in the PlayerSetup Inspector.");
            }

            if (!applyArms)
            {
                // If immunity is active, the TPV mesh swap above overwrote the immunity material.
                // Re-snapshot the correct character materials and re-apply immunity visual.
                var healthRemote = GetComponent<ArtisansGuns.Game.PlayerHealth>();
                if (healthRemote != null && healthRemote.IsImmune)
                    healthRemote.RefreshImmunityMaterials();
                return;
            }

            // Arms (FPV): local player only.
            if (playerSetup.armsSkinnedMeshRenderer != null)
            {
                var armsSMR = playerSetup.armsSkinnedMeshRenderer;
                Debug.Log($"[CharacterSetupHandler] Arms SMR GO='{armsSMR.gameObject.name}' " +
                          $"beforeMesh={(armsSMR.sharedMesh != null ? armsSMR.sharedMesh.name : "NULL")} " +
                          $"beforeMats={armsSMR.sharedMaterials.Length}");

                if (cfg.armsMesh != null)
                    armsSMR.sharedMesh = cfg.armsMesh;
                if (cfg.armsMaterials != null && cfg.armsMaterials.Length > 0)
                    armsSMR.sharedMaterials = cfg.armsMaterials;

                Debug.Log($"[CharacterSetupHandler] Arms SMR afterMesh={(armsSMR.sharedMesh != null ? armsSMR.sharedMesh.name : "NULL")} " +
                          $"afterMats={armsSMR.sharedMaterials.Length}");
            }
            else
            {
                Debug.LogWarning("[CharacterSetupHandler] armsSkinnedMeshRenderer is null — assign it in the PlayerSetup Inspector.");
            }

            // If immunity is active, the mesh swap above overwrote the immunity material.
            // Re-snapshot the correct character materials and re-apply immunity visual.
            var health = GetComponent<ArtisansGuns.Game.PlayerHealth>();
            if (health != null && health.IsImmune)
                health.RefreshImmunityMaterials();
        }
    }
}
