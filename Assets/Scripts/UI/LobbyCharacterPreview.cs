using UnityEngine;
using UnityEngine.Animations.Rigging;
using System.Collections;
using ArtisansGuns.Characters;
using ArtisansGuns.Data;
using ArtisansGuns.Weapons;
using ArtisansGuns.Managers;

namespace ArtisansGuns.UI
{
    /// <summary>
    /// Instantiates ONLY the PlayerTPV sub-hierarchy for the lobby preview.
    /// Applies character mesh/materials, spawns the TPV weapon, sets the
    /// upper-body animator controller (handsAnimatorControllerTPV), and
    /// connects IK grips (RightGrip / LeftGrip) so hands hold the weapon.
    /// Everything is set to the "Character" layer for the preview camera.
    /// </summary>
    public class LobbyCharacterPreview : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The PlayerPrefab from Resources — PlayerTPV is extracted from it")]
        [SerializeField] private GameObject playerPrefab;

        [Header("Spawn Settings")]
        [Tooltip("World-space position for the preview model (far from gameplay)")]
        [SerializeField] private Vector3 spawnPosition = new Vector3(0, -100, 0);

        [Tooltip("Euler rotation of the preview model")]
        [SerializeField] private Vector3 spawnRotation = new Vector3(0, 180, 0);

        // ── Cached runtime references ──
        private GameObject _previewRoot;               // the detached PlayerTPV
        private SkinnedMeshRenderer _tpvSMR;
        private Transform _tpvWeaponHolder;
        private Transform _spine2Bone;
        private Animator _upperBodyAnimator;
        private RigBuilder _rigBuilder;
        private TwoBoneIKConstraint _rightHandIK;
        private TwoBoneIKConstraint _leftHandIK;
        private GameObject _currentWeaponInstance;
        private GameObject _currentHatInstance;
        private string _currentCharacterId;
        private string _currentWeaponId;
        private string _currentHatId;

        private void Start()
        {
            if (LoadoutManager.Instance != null && LoadoutManager.Instance.IsInitialized())
            {
                var loadout = LoadoutManager.Instance.GetLoadout();
                Refresh(
                    loadout.selectedCharacter ?? "crimson",
                    loadout.primaryWeapon?.weaponId ?? "talon_ar",
                    loadout.selectedHat ?? "none"
                );
            }
            else
            {
                Refresh("crimson", "talon_ar", "none");
            }
        }

        /// <summary>
        /// Public API — call whenever the loadout changes.
        /// </summary>
        public void Refresh(string characterId, string weaponId, string hatId = "none")
        {
            if (string.IsNullOrEmpty(characterId)) characterId = "crimson";
            if (string.IsNullOrEmpty(weaponId))    weaponId    = "talon_ar";
            if (string.IsNullOrEmpty(hatId))        hatId       = "none";

            bool charChanged   = characterId != _currentCharacterId;
            bool weaponChanged = weaponId    != _currentWeaponId;
            bool hatChanged    = hatId       != _currentHatId;

            if (!charChanged && !weaponChanged && !hatChanged && _previewRoot != null)
                return;

            if (charChanged || _previewRoot == null)
                RebuildModel(characterId);

            if (weaponChanged || charChanged)
                EquipWeapon(weaponId);

            if (hatChanged || charChanged)
                EquipHat(hatId, characterId);

            _currentCharacterId = characterId;
            _currentWeaponId    = weaponId;
            _currentHatId       = hatId;
        }

        // ================================================================
        // Model — extract PlayerTPV from PlayerPrefab
        // ================================================================

        private void RebuildModel(string characterId)
        {
            // Destroy previous preview
            if (_previewRoot != null)
            {
                // Disable RigBuilder BEFORE destroying to stop Burst IK jobs
                if (_rigBuilder != null) _rigBuilder.enabled = false;

                Destroy(_previewRoot);
                _previewRoot           = null;
                _tpvSMR                = null;
                _tpvWeaponHolder       = null;
                _spine2Bone            = null;
                _upperBodyAnimator     = null;
                _rigBuilder            = null;
                _rightHandIK           = null;
                _leftHandIK            = null;
                _currentWeaponInstance = null;
                _currentHatInstance    = null;
            }

            if (playerPrefab == null)
            {
                Debug.LogWarning("[LobbyCharacterPreview] playerPrefab not assigned!");
                return;
            }

            // ── 1. Instantiate full prefab but IMMEDIATELY deactivate it ──
            // This prevents Update() from running on WeaponDropSystem, etc.
            GameObject temp = Instantiate(playerPrefab, spawnPosition, Quaternion.Euler(spawnRotation));
            temp.SetActive(false);

            // ── 2. Find and detach the PlayerTPV child ──
            Transform tpvChild = temp.transform.Find("PlayerTPV");
            if (tpvChild == null)
            {
                foreach (Transform c in temp.transform)
                    if (c.name.Contains("TPV")) { tpvChild = c; break; }
            }

            if (tpvChild == null)
            {
                Debug.LogError("[LobbyCharacterPreview] PlayerTPV not found in prefab!");
                Destroy(temp);
                return;
            }

            // Detach TPV from the prefab root (keeps its full sub-hierarchy intact)
            tpvChild.SetParent(null);
            _previewRoot = tpvChild.gameObject;
            _previewRoot.name = "LobbyCharacterPreview_TPV";
            _previewRoot.transform.position = spawnPosition;
            _previewRoot.transform.rotation = Quaternion.Euler(spawnRotation);

            // ── 3. Destroy the rest of the prefab (WeaponDropSystem, PlayerSetup, etc.) ──
            // temp is inactive so no Update() will ever fire on it.
            Destroy(temp);

            // ── 4. Strip only gameplay/network scripts from the TPV ──
            StripUnwantedComponents(_previewRoot);

            // ── 5. Activate and enable renderers ──
            _previewRoot.SetActive(true);
            foreach (var smr in _previewRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.enabled = true;
            foreach (var mr in _previewRoot.GetComponentsInChildren<MeshRenderer>(true))
                mr.enabled = true;

            // ── 6. Cache key references that live INSIDE the PlayerTPV hierarchy ──
            _tpvSMR = _previewRoot.GetComponentInChildren<SkinnedMeshRenderer>(true);

            _tpvWeaponHolder = FindChildRecursive(_previewRoot.transform, "WeaponHolderTPV");
            if (_tpvWeaponHolder == null)
                _tpvWeaponHolder = FindChildRecursive(_previewRoot.transform, "tpvWeaponHolder");

            _spine2Bone = FindChildRecursive(_previewRoot.transform, "mixamorig:Spine2");
            if (_spine2Bone != null)
                _upperBodyAnimator = _spine2Bone.GetComponent<Animator>();

            // RigBuilder lives on the bone hierarchy (usually on Spine2 or a Rig child)
            _rigBuilder = _previewRoot.GetComponentInChildren<RigBuilder>(true);

            // TwoBoneIKConstraints — find by name (RightHand / LeftHand)
            var ikConstraints = _previewRoot.GetComponentsInChildren<TwoBoneIKConstraint>(true);
            foreach (var ik in ikConstraints)
            {
                string n = ik.gameObject.name.ToLower();
                if (n.Contains("right")) _rightHandIK = ik;
                else if (n.Contains("left")) _leftHandIK = ik;
            }

            // Log what we found for debugging
            Debug.Log($"[LobbyCharacterPreview] TPV extracted — " +
                      $"SMR={(_tpvSMR != null)} " +
                      $"WeaponHolder={(_tpvWeaponHolder != null ? _tpvWeaponHolder.name : "NULL")} " +
                      $"Spine2Animator={(_upperBodyAnimator != null)} " +
                      $"RigBuilder={(_rigBuilder != null)} " +
                      $"RightIK={(_rightHandIK != null)} " +
                      $"LeftIK={(_leftHandIK != null)}");

            // ── 7. Apply character mesh/materials ──
            ApplyCharacterConfig(characterId);

            // ── 8. Set to Character layer ──
            int layer = LayerMask.NameToLayer("Character");
            if (layer >= 0)
                SetLayerRecursive(_previewRoot, layer);
        }

        private void ApplyCharacterConfig(string characterId)
        {
            CharacterConfig cfg = Resources.Load<CharacterConfig>($"Characters/{characterId}");
            if (cfg == null)
            {
                string cap = char.ToUpper(characterId[0]) + characterId.Substring(1).ToLower();
                cfg = Resources.Load<CharacterConfig>($"Characters/{cap}");
            }
            if (cfg == null || _tpvSMR == null) return;

            if (cfg.tpvMesh != null)
                _tpvSMR.sharedMesh = cfg.tpvMesh;
            if (cfg.tpvMaterials != null && cfg.tpvMaterials.Length > 0)
                _tpvSMR.sharedMaterials = cfg.tpvMaterials;
        }

        // ================================================================
        // Weapon + Animator + IK
        // ================================================================

        private void EquipWeapon(string weaponId)
        {
            // Safely detach IK BEFORE destroying the old weapon.
            // Burst IK jobs run on worker threads and will crash if they
            // read a destroyed grip transform.
            SafeDetachIK();

            // Destroy old weapon
            if (_currentWeaponInstance != null)
            {
                Destroy(_currentWeaponInstance);
                _currentWeaponInstance = null;
            }

            if (_tpvWeaponHolder == null) return;

            // Load weapon config
            string resName = ConvertIdToResourceName(weaponId);
            WeaponConfig cfg = Resources.Load<WeaponConfig>($"Weapons/{resName}");
            if (cfg == null) return;

            GameObject prefab = cfg.prefabWeaponTPV != null ? cfg.prefabWeaponTPV : cfg.weaponPrefab;
            if (prefab == null) return;

            // ── Spawn the TPV weapon ──
            _currentWeaponInstance = Instantiate(prefab, _tpvWeaponHolder);
            _currentWeaponInstance.transform.localPosition = Vector3.zero;
            _currentWeaponInstance.transform.localRotation = Quaternion.identity;

            // Layer
            int layer = LayerMask.NameToLayer("Character");
            if (layer >= 0) SetLayerRecursive(_currentWeaponInstance, layer);

            // Strip gameplay scripts from weapon (FireWeapon, WeaponRecoil, etc.)
            foreach (var m in _currentWeaponInstance.GetComponentsInChildren<MonoBehaviour>(true))
                if (m != null) Destroy(m);
            foreach (var src in _currentWeaponInstance.GetComponentsInChildren<AudioSource>(true))
                Destroy(src);

            // ── Set the upper-body animator to this weapon's TPV controller ──
            if (_upperBodyAnimator != null && cfg.handsAnimatorControllerTPV != null)
            {
                _upperBodyAnimator.enabled = false;
                _upperBodyAnimator.runtimeAnimatorController = cfg.handsAnimatorControllerTPV;
                _upperBodyAnimator.enabled = true;
                _upperBodyAnimator.Rebind();
                _upperBodyAnimator.Update(0f);
                Debug.Log($"[LobbyCharacterPreview] Spine2 animator set to: {cfg.handsAnimatorControllerTPV.name}");
            }

            // ── Connect IK grips (wait 1 frame for animator to settle) ──
            StartCoroutine(ConnectGripsCoroutine());
        }

        /// <summary>
        /// Parks IK targets away from the weapon grips and rebuilds the rig
        /// so Burst jobs don't read a destroyed transform during weapon swap.
        /// </summary>
        private void SafeDetachIK()
        {
            if (_rightHandIK != null)
            {
                _rightHandIK.weight = 0f;
            }
            if (_leftHandIK != null)
            {
                _leftHandIK.weight = 0f;
            }
            if (_rigBuilder != null)
            {
                _rigBuilder.enabled = false;
                _rigBuilder.enabled = true;
                _rigBuilder.Build();
            }
        }

        private IEnumerator ConnectGripsCoroutine()
        {
            // Wait one frame so the new AnimatorController processes its first pose
            yield return null;

            if (_currentWeaponInstance == null) yield break;

            // TPV weapons use "RightGrip" / "LeftGrip" as grip transforms
            Transform rightGrip = FindChildRecursive(_currentWeaponInstance.transform, "RightGrip");
            Transform leftGrip  = FindChildRecursive(_currentWeaponInstance.transform, "LeftGrip");

            // Fallback names used by some weapons
            if (rightGrip == null)
                rightGrip = FindChildRecursive(_currentWeaponInstance.transform, "RightHandGrip");
            if (leftGrip == null)
                leftGrip = FindChildRecursive(_currentWeaponInstance.transform, "LeftHandGrip");

            Debug.Log($"[LobbyCharacterPreview] Grips found — Right={rightGrip != null} Left={leftGrip != null}");

            // Wire grips into the TwoBoneIKConstraints
            if (_rightHandIK != null && rightGrip != null)
            {
                _rightHandIK.data.target = rightGrip;
                _rightHandIK.weight = 1f;
            }

            if (_leftHandIK != null && leftGrip != null)
            {
                _leftHandIK.data.target = leftGrip;
                _leftHandIK.weight = 1f;
            }

            // Rebuild the rig so IK constraints take effect
            if (_rigBuilder != null)
            {
                _rigBuilder.enabled = false;
                _rigBuilder.enabled = true;
                _rigBuilder.Build();
                yield return null; // let Burst graph settle
                Debug.Log("[LobbyCharacterPreview] Rig rebuilt — IK active");
            }
        }

        // ================================================================
        // Hat
        // ================================================================

        private void EquipHat(string hatId, string characterId)
        {
            // Destroy old hat
            if (_currentHatInstance != null)
            {
                Destroy(_currentHatInstance);
                _currentHatInstance = null;
            }

            if (_previewRoot == null) return;
            if (string.IsNullOrEmpty(hatId) || hatId == "none") return;

            HatDefinition hatDef = HatDefinition.GetHatById(hatId);
            if (hatDef == null || hatDef.hatPrefab == null) return;

            // Find HatSpawnPoint (child of mixamorig:Head), fall back to Head itself
            Transform spawnPoint = FindChildRecursive(_previewRoot.transform, "HatSpawnPoint");
            if (spawnPoint == null)
                spawnPoint = FindChildRecursive(_previewRoot.transform, "mixamorig:Head");
            if (spawnPoint == null)
            {
                Debug.LogWarning("[LobbyCharacterPreview] HatSpawnPoint / mixamorig:Head not found — cannot attach hat");
                return;
            }

            _currentHatInstance = Instantiate(hatDef.hatPrefab, spawnPoint);

            // Apply per-agent offset
            HatAgentOffset offset = hatDef.GetOffsetForAgent(characterId);
            if (offset != null)
            {
                _currentHatInstance.transform.localPosition = offset.position;
                _currentHatInstance.transform.localRotation = Quaternion.Euler(offset.rotation);
                _currentHatInstance.transform.localScale = offset.scale;
            }
            else
            {
                _currentHatInstance.transform.localPosition = Vector3.zero;
                _currentHatInstance.transform.localRotation = Quaternion.identity;
                _currentHatInstance.transform.localScale = Vector3.one;
            }

            // Set to Character layer
            int layer = LayerMask.NameToLayer("Character");
            if (layer >= 0)
                SetLayerRecursive(_currentHatInstance, layer);

            // Strip any scripts on the hat prefab
            foreach (var m in _currentHatInstance.GetComponentsInChildren<MonoBehaviour>(true))
                if (m != null) Destroy(m);

            Debug.Log($"[LobbyCharacterPreview] Hat '{hatId}' equipped on {characterId}");
        }

        // ================================================================
        // Component Stripping
        // ================================================================

        /// <summary>
        /// Removes networking, gameplay, physics, audio, and camera components.
        /// KEEPS: Animator, RigBuilder, Rig, TwoBoneIKConstraint (IRigConstraint),
        /// and all renderers so the visual + rigging pipeline stays intact.
        /// </summary>
        private static void StripUnwantedComponents(GameObject go)
        {
            // Remove gameplay MonoBehaviours, but keep rigging components
            foreach (var m in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (m == null) continue;

                // Keep Animation Rigging constraints (TwoBoneIKConstraint, etc.)
                if (m is IRigConstraint) continue;

                // Keep RigBuilder (drives the constraint pipeline)
                if (m is RigBuilder) continue;

                // Keep Rig (container for constraints, referenced by RigBuilder)
                if (m is Rig) continue;

                Destroy(m);
            }

            // Remove audio, cameras, physics — not needed in preview
            foreach (var src in go.GetComponentsInChildren<AudioSource>(true))
                Destroy(src);
            foreach (var cam in go.GetComponentsInChildren<Camera>(true))
                Destroy(cam);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                Destroy(rb);
            foreach (var cc in go.GetComponentsInChildren<CharacterController>(true))
                Destroy(cc);
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Destroy(col);
        }

        // ================================================================
        // Helpers
        // ================================================================

        private static string ConvertIdToResourceName(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return "TalonAR";
            if (weaponId == "talon_ar") return "TalonAR";
            if (weaponId == "bolt") return "Bolt";
            if (weaponId == "default" || weaponId == "default_knife") return "DefaultKnife";
            var parts = weaponId.Split('_');
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts)
                if (part.Length > 0)
                    sb.Append(char.ToUpper(part[0])).Append(part.Substring(1));
            return sb.ToString();
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            string lower = name.ToLower();
            foreach (Transform child in parent)
            {
                if (child.name.ToLower() == lower) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
