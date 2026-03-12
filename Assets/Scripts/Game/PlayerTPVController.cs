using UnityEngine;
using Fusion;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// PlayerTPVController - Manages Third Person View components
    /// - Hides TPV model from local player (only visible to others)
    /// - Rotates spine bone to match camera pitch (look up/down)
    /// - Controls visibility of TPV weapon model
    /// </summary>
    public class PlayerTPVController : NetworkBehaviour
    {
        [Header("TPV Model References")]
        [Tooltip("Root GameObject of the third-person model (PlayerTPV). Hidden for local player.")]
        public GameObject playerTPVRoot;
        
        [Tooltip("GameObject containing the character mesh (SkinnedMeshRenderer). Change this when switching characters.")]
        public GameObject characterMeshObject;
        
        [Tooltip("Spine bone (mixamorig:Spine) - rotates up/down to match camera")]
        public Transform spineBone;
        
        [Tooltip("Spine2 bone (mixamorig:Spine2) - where upper body animator is located")]
        public Transform spine2Bone;
        
        [Tooltip("Hips bone (mixamorig:Hips) - where locomotion animator is located")]
        public Transform hipsBone;
        
        [Header("TPV Weapon Attachment")]
        [Tooltip("Transform where TPV weapon will be instantiated (attach point for third-person weapon)")]
        public Transform tpvWeaponHolder;

        [Header("Spine Rotation Settings")]
        [Tooltip("Multiplier for spine rotation. Reduce if spine rotates too much (e.g. 0.6).")]
        [Range(0.1f, 2f)]
        public float spineRotationMultiplier = 0.6f;
        
        [Tooltip("Maximum spine rotation angle (degrees)")]
        [Range(30f, 90f)]
        public float maxSpineRotation = 60f;
        
        [Tooltip("Speed at which spine rotates to target angle (Slerp speed, higher = snappier)")]
        [Range(1f, 30f)]
        public float spineRotationSpeed = 10f;

        [Header("Flash Feedback VFX")]
        [Tooltip("VisualEffect on the TPV head (FlashFeedback). Disabled by default; activated by Pato ultimate.")]
        public UnityEngine.VFX.VisualEffect flashFeedbackVFX;

        [Header("Spine Rotation Offset (fine-tuning)")]
        [Tooltip("Constant pitch offset added on top of the camera pitch. " +
                 "Use to compensate if the idle pose looks up or down by default.")]
        [Range(-45f, 45f)]
        public float pitchOffset = 0f;

        [Tooltip("Constant yaw offset (left/right) added to the spine rotation. " +
                 "Use if the weapon visually drifts left/right from the chest center.")]
        [Range(-30f, 30f)]
        public float yawOffset = 0f;

        [Tooltip("Constant roll offset. Usually 0; adjust only if the spine twists sideways.")]
        [Range(-30f, 30f)]
        public float rollOffset = 0f;
        
        // Runtime references
        private PlayerController playerController;
        private Quaternion spineOriginalRotation;
        private GameObject currentTPVWeaponInstance;

        /// <summary>Layer index to apply to locally-owned TPV objects so the local camera ignores them. -1 = not set.</summary>
        private int localPlayerLayer = -1;
        private bool hasSpawned = false; // Prevent multiple Spawned() calls
        
        // Weapon trigger tracking (detect networked changes)
        private int lastWeaponTrigger = 0;
        private Animator tpvWeaponAnimator; // Animator on the TPV weapon instance
        private bool isCurrentTPVWeaponKnife = false; // True when knife is equipped (changes attack behavior)
        
        // Muzzle flash data (set by PlayerSetup when weapon is equipped)
        private GameObject tpvMuzzleFlashPrefab;
        private float tpvMuzzleFlashDuration = 0.1f;
        private float tpvMuzzleFlashScale = 1f;
        private Transform tpvFirePoint; // FirePoint on the TPV weapon (barrel end)

        // TPV bullet trail
        private GameObject tpvTrailPrefab;
        private float tpvTrailSpeed = 80f;

        // TPV fire sound (played at the weapon position so remote players hear it in 3D)
        private AudioClip tpvFireSound;

        // TPV reload sounds (same array as FPV — played from weapon position so remote players hear)
        private AudioClip[] tpvReloadSounds;
        
        public override void Spawned()
        {
            // Prevent multiple executions
            if (hasSpawned)
            {
                Debug.LogWarning("⚠️ [PlayerTPVController] Spawned() called multiple times - ignoring");
                return;
            }
            hasSpawned = true;
            
            // Get PlayerController reference
            playerController = GetComponent<PlayerController>();

            // Save original spine rotation (rest pose) so we rotate ON TOP of it
            if (spineBone != null)
                spineOriginalRotation = spineBone.localRotation;
            
            // Hide TPV model for local player, show for others
            if (HasInputAuthority)
            {
                // This is the local player - hide TPV model
                SetTPVVisibility(false);
                // Debug.Log("🎮 [PlayerTPVController] Local player - TPV model hidden");
            }
            else
            {
                // This is a remote player - show TPV model
                SetTPVVisibility(true);
                // Debug.Log("👥 [PlayerTPVController] Remote player - TPV model visible");
            }
        }
        
        /// <summary>
        /// Render runs every Unity frame for ALL NetworkBehaviours (local + remote).
        /// Handles weapon trigger detection. Spine rotation is in LateUpdate.
        /// </summary>
        public override void Render()
        {
            // Only update for remote players (TPV model is hidden for local player)
            if (!hasSpawned || HasInputAuthority || playerController == null) return;
            
            // Weapon trigger sync (Shot/Reload TPV animations)
            int currentTrigger = playerController.NetworkWeaponTrigger;
            if (currentTrigger != lastWeaponTrigger)
            {
                HandleWeaponTriggerChanged(currentTrigger);
                lastWeaponTrigger = currentTrigger;
            }
        }
        
        /// <summary>
        /// LateUpdate runs AFTER the Animator has processed bone transforms.
        /// This is the only safe place to override bone rotations set by the Animator.
        /// </summary>
        private void LateUpdate()
        {
            if (!hasSpawned || HasInputAuthority || playerController == null || spineBone == null) return;
            UpdateSpineRotation();
        }

        /// <summary>
        /// Rotates spine bone to match camera pitch synced over the network.
        /// The three offset fields let you fine-tune the resting look direction
        /// without touching the base animator or rig.
        /// </summary>
        private void UpdateSpineRotation()
        {
            float cameraPitch = playerController.NetworkPitch;

            // Clamp to avoid extreme bending
            float clampedPitch = Mathf.Clamp(cameraPitch, -maxSpineRotation, maxSpineRotation);

            // Scale pitch and add per-axis offsets for fine-tuning
            float finalPitch = clampedPitch * spineRotationMultiplier + pitchOffset;

            Quaternion targetRotation =
                spineOriginalRotation *
                Quaternion.Euler(finalPitch, yawOffset, rollOffset);

            spineBone.localRotation = Quaternion.Slerp(
                spineBone.localRotation,
                targetRotation,
                Time.deltaTime * spineRotationSpeed
            );
        }

        /// <summary>Public shorthand — called by PlayerHealth on death.</summary>
        public void HideTPV()
        {
            SetTPVVisibility(false);
            SetTPVWeaponActive(false);
        }

        /// <summary>Public shorthand — called by PlayerHealth on respawn.</summary>
        public void ShowTPV()
        {
            SetTPVVisibility(true);
            SetTPVWeaponActive(true);
        }

        /// <summary>
        /// Called by TeamLayerAssigner on the local player to record the "Player" layer.
        /// Also applies it immediately to the weapon holder and any current weapon.
        /// </summary>
        public void SetLocalPlayerLayer(int layer)
        {
            localPlayerLayer = layer;
            if (tpvWeaponHolder != null)
                SetLayerRecursive(tpvWeaponHolder.gameObject, layer);
            if (currentTPVWeaponInstance != null)
                SetLayerRecursive(currentTPVWeaponInstance, layer);
        }

        /// <summary>Show / hide the TPV weapon (used during death/respawn).</summary>
        public void SetTPVWeaponActive(bool active)
        {
            if (currentTPVWeaponInstance != null)
                currentTPVWeaponInstance.SetActive(active);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        /// <summary>
        /// Sets visibility of the TPV model
        /// Only disables renderers, keeps GameObject active for network components
        /// </summary>
        private void SetTPVVisibility(bool visible)
        {
            // Try to use characterMeshObject if assigned, otherwise fallback to playerTPVRoot
            GameObject targetObject = characterMeshObject != null ? characterMeshObject : playerTPVRoot;
            
            if (targetObject == null)
            {
                Debug.LogWarning("⚠️ [PlayerTPVController] No character mesh object or TPV root assigned!");
                return;
            }
            
            // Find all SkinnedMeshRenderer and MeshRenderer in the target object
            SkinnedMeshRenderer[] skinnedRenderers = targetObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshRenderer[] meshRenderers = targetObject.GetComponentsInChildren<MeshRenderer>(true);
            
            // Disable/enable all renderers (this hides the model visually but keeps GameObjects active)
            foreach (SkinnedMeshRenderer renderer in skinnedRenderers)
            {
                renderer.enabled = visible;
            }
            
            foreach (MeshRenderer renderer in meshRenderers)
            {
                renderer.enabled = visible;
            }
            
            // Debug.Log($"🖼️ [PlayerTPVController] TPV renderers {(visible ? "enabled" : "disabled")} - Target: {targetObject.name} ({skinnedRenderers.Length} skinned, {meshRenderers.Length} mesh)");
        }
        
        
        /// <summary>
        /// Spawns TPV weapon at TPV weapon holder
        /// Called by PlayerSetup when weapon is equipped
        /// </summary>
        public void SpawnTPVWeapon(GameObject weaponPrefab)
        {
            // Destroy current TPV weapon if exists.
            // IMPORTANT: PlayerSetup.SafeDetachTPVIK() has already parked the IK targets
            // on a safe transform and called tpvRigBuilder.Build() before this method is
            // called. That schedules the new Burst graph for the NEXT frame. We must NOT
            // destroy the old weapon until that frame has passed, otherwise the current
            // frame's Burst job still holds handles to the old weapon's grips -> crash.
            if (currentTPVWeaponInstance != null)
            {
                GameObject oldWeapon = currentTPVWeaponInstance;
                currentTPVWeaponInstance = null;
                oldWeapon.SetActive(false);           // Invisible immediately
                oldWeapon.transform.SetParent(null);  // Free from hierarchy
                StartCoroutine(DeferredDestroy(oldWeapon)); // Actual Destroy next frame
            }
            
            // Spawn new TPV weapon
            if (weaponPrefab != null && tpvWeaponHolder != null)
            {
                currentTPVWeaponInstance = Instantiate(weaponPrefab, tpvWeaponHolder);
                currentTPVWeaponInstance.transform.localPosition = Vector3.zero;
                currentTPVWeaponInstance.transform.localRotation = Quaternion.identity;
                
                Debug.Log($"🔫 [PlayerTPVController] TPV weapon spawned: {weaponPrefab.name}");

                // If local player, put weapon on Player layer so camera ignores it
                if (localPlayerLayer >= 0)
                    SetLayerRecursive(currentTPVWeaponInstance, localPlayerLayer);

                // Cache TPV weapon animator for trigger sync
                tpvWeaponAnimator = currentTPVWeaponInstance.GetComponentInChildren<Animator>();
                
                // Find fire point for muzzle flash (case-insensitive search for FirePoint/Firepoint/Muzzle)
                tpvFirePoint = FindChildRecursiveIgnoreCase(currentTPVWeaponInstance.transform, "firepoint");
                if (tpvFirePoint == null)
                    tpvFirePoint = FindChildRecursiveIgnoreCase(currentTPVWeaponInstance.transform, "muzzle");

                if (tpvFirePoint != null)
                    Debug.Log($"✅ [PlayerTPVController] tpvFirePoint found: {tpvFirePoint.name}");
                else
                    Debug.LogWarning($"⚠️ [PlayerTPVController] tpvFirePoint NOT found on '{weaponPrefab.name}' - no child named 'FirePoint' or 'Muzzle'");
                
                // Find and setup IK grips
                SetupTPVWeaponGrips();

                // Attach sound relay so Animation Events on TPV weapon can call
                // PlayTPVReloadSound / PlayTPVFireSound through this bridge component.
                var relay = currentTPVWeaponInstance.AddComponent<TPVSoundRelay>();
                relay.Init(this);
            }
            else
            {
                Debug.LogWarning("⚠️ [PlayerTPVController] Cannot spawn TPV weapon - prefab or holder is null");
            }
        }

        /// <summary>
        /// Immediately destroys the current TPV weapon without replacing it.
        /// Caller (PlayerSetup) must call SafeDetachTPVIK() first.
        /// </summary>
        public void ClearCurrentTPVWeapon()
        {
            if (currentTPVWeaponInstance == null) return;
            Destroy(currentTPVWeaponInstance);
            currentTPVWeaponInstance = null;
        }

        /// <summary>
        /// Finds grip points in TPV weapon and connects them to IK constraints
        /// </summary>
        private void SetupTPVWeaponGrips()
        {
            if (currentTPVWeaponInstance == null) return;
            
            // Find grip transforms in weapon (LeftGrip is inside Charger object)
            Transform rightGrip = FindChildRecursive(currentTPVWeaponInstance.transform, "RightGrip");
            Transform leftGrip = FindChildRecursive(currentTPVWeaponInstance.transform, "LeftGrip");
            
            if (rightGrip == null || leftGrip == null)
            {
                Debug.LogWarning("⚠️ [PlayerTPVController] TPV weapon missing grip points! Add 'RightGrip' and 'LeftGrip' GameObjects (LeftGrip usually inside Charger object).");
                return;
            }
            
            // TODO: Connect grips to IK constraints
            // This will be handled by PlayerSetup since it has references to IK constraints
            Debug.Log($"✅ [PlayerTPVController] TPV weapon grips found: Right={rightGrip.name}, Left={leftGrip.name}");
        }
        
        /// <summary>
        /// Handles weapon trigger changes from network sync.
        /// Decodes action type from bits 0-1: 0 = Shot, 1 = Reload, 2 = FireSoundOnly
        /// </summary>
        private void HandleWeaponTriggerChanged(int triggerValue)
        {
            int actionType = triggerValue & 3; // bits 0-1

            switch (actionType)
            {
                case 0: // Shot
                    if (isCurrentTPVWeaponKnife)
                    {
                        // Knife attack: trigger Attack animation on the TPV weapon (knife model)
                        if (tpvWeaponAnimator != null)
                            tpvWeaponAnimator.SetTrigger("Attack");
                    }
                    else
                    {
                        // Gun shot: spawn muzzle flash + TPV trail + fire sound
                        Debug.Log($"[PlayerTPVController] HandleWeaponTriggerChanged -> SpawnTPVMuzzleFlash | prefab={(tpvMuzzleFlashPrefab != null ? tpvMuzzleFlashPrefab.name : "NULL")} firePoint={(tpvFirePoint != null ? tpvFirePoint.name : "NULL")}");
                        SpawnTPVMuzzleFlash();
                        SpawnTPVTrail();
                        PlayTPVFireSound();
                    }
                    break;

                case 1: // Reload
                    if (tpvWeaponAnimator != null)
                        tpvWeaponAnimator.SetTrigger("ReloadTPV");
                    break;

                case 2: // FireSoundOnly (knife swing sound from Animation Event)
                    PlayTPVFireSound();
                    break;
            }
        }
        
        /// <summary>
        /// Spawns muzzle flash effect on the TPV weapon for remote players.
        /// Called when a shot trigger is detected from network sync.
        /// </summary>
        private void SpawnTPVMuzzleFlash()
        {
            if (tpvMuzzleFlashPrefab == null || tpvFirePoint == null) return;
            
            GameObject flash = Instantiate(
                tpvMuzzleFlashPrefab,
                tpvFirePoint.position,
                tpvFirePoint.rotation,
                tpvFirePoint
            );
            flash.transform.localPosition = Vector3.zero;
            flash.transform.localRotation = Quaternion.identity;
            // Apply world-space scale: override localScale so the flash appears at the
            // configured size regardless of the parent hierarchy's scale.
            float ws = tpvMuzzleFlashScale;
            Vector3 parentWorldScale = tpvFirePoint.lossyScale;
            flash.transform.localScale = new Vector3(
                ws / (parentWorldScale.x != 0f ? parentWorldScale.x : 1f),
                ws / (parentWorldScale.y != 0f ? parentWorldScale.y : 1f),
                ws / (parentWorldScale.z != 0f ? parentWorldScale.z : 1f)
            );

            Destroy(flash, tpvMuzzleFlashDuration);
        }
        
        /// <summary>
        /// Sets whether the currently equipped TPV weapon is a knife.
        /// Changes how weapon trigger events are handled (Attack animation vs muzzle flash).
        /// Called by PlayerSetup.SpawnTPVWeapon.
        /// </summary>
        public void SetIsKnife(bool isKnife)
        {
            isCurrentTPVWeaponKnife = isKnife;
        }
        
        /// <summary>
        /// Sets muzzle flash data for the TPV weapon.
        /// Called by PlayerSetup after spawning the TPV weapon.
        /// </summary>
        public void SetMuzzleFlashData(GameObject muzzleFlashPrefab, float duration, float scale = 1f)
        {
            tpvMuzzleFlashPrefab = muzzleFlashPrefab;
            tpvMuzzleFlashDuration = duration;
            tpvMuzzleFlashScale = scale;
            Debug.Log($"[PlayerTPVController] SetMuzzleFlashData -> prefab={(muzzleFlashPrefab != null ? muzzleFlashPrefab.name : "NULL")} duration={duration} scale={scale}");
        }

        /// <summary>Sets TPV trail prefab and travel speed. Called by PlayerSetup.</summary>
        public void SetTrailData(GameObject trailPrefab, float speed)
        {
            tpvTrailPrefab = trailPrefab;
            tpvTrailSpeed  = speed > 0f ? speed : 80f;
        }

        /// <summary>Sets the fire sound clip for TPV. Called by PlayerSetup.</summary>
        public void SetFireSoundData(AudioClip clip)
        {
            tpvFireSound = clip;
        }

        /// <summary>Sets the reload sound clips for TPV. Called by PlayerSetup.</summary>
        public void SetReloadSoundData(AudioClip[] clips)
        {
            tpvReloadSounds = clips;
        }

        /// <summary>
        /// Plays a reload sound at the TPV weapon position in 3D space.
        /// Call this from Animation Events on the TPV reload animation clips
        /// (e.g. PlayTPVReloadSound(0), PlayTPVReloadSound(1), etc.)
        /// so remote players hear each reload phase at the correct time.
        /// </summary>
        /// <param name="index">Index into weaponConfig.reloadSounds array.</param>
        public void PlayTPVReloadSound(int index)
        {
            if (tpvReloadSounds == null || tpvReloadSounds.Length == 0) return;
            if (index < 0 || index >= tpvReloadSounds.Length) return;

            AudioClip clip = tpvReloadSounds[index];
            if (clip == null) return;

            // Use the weapon position (firepoint or weapon root) for 3D spatial audio
            Vector3 pos = tpvFirePoint != null
                ? tpvFirePoint.position
                : (currentTPVWeaponInstance != null ? currentTPVWeaponInstance.transform.position : transform.position);

            GameObject sfxGO = new GameObject("TPVReloadSound");
            sfxGO.transform.position = pos;
            AudioSource src = sfxGO.AddComponent<AudioSource>();
            src.clip         = clip;
            src.spatialBlend  = 1f;
            src.rolloffMode   = AudioRolloffMode.Linear;
            src.minDistance   = 1f;
            src.maxDistance   = 25f;
            src.playOnAwake   = false;
            src.Play();
            Destroy(sfxGO, clip.length + 0.1f);
        }

        /// <summary>
        /// Plays the TPV fire sound at the weapon's fire point in 3D space.
        /// Only runs on remote clients (the shooter hears their own FPV fire sound).
        /// </summary>
        private void PlayTPVFireSound()
        {
            if (tpvFireSound == null || tpvFirePoint == null) return;

            GameObject sfxGO = new GameObject("TPVFireSound");
            sfxGO.transform.position = tpvFirePoint.position;
            AudioSource src = sfxGO.AddComponent<AudioSource>();
            src.clip         = tpvFireSound;
            src.spatialBlend  = 1f;          // full 3D
            src.rolloffMode   = AudioRolloffMode.Linear;
            src.minDistance   = 1f;
            src.maxDistance   = 40f;
            src.playOnAwake   = false;
            src.Play();
            Destroy(sfxGO, tpvFireSound.length + 0.1f);
        }

        /// <summary>
        /// Public wrapper for PlayTPVFireSound. Called by TPVSoundRelay from
        /// Animation Events on the TPV weapon (e.g. knife attack TPV animation).
        /// </summary>
        public void PlayTPVFireSoundPublic()
        {
            PlayTPVFireSound();
        }

        /// <summary>
        /// Spawns the TPV TrailRenderer and moves it from the firepoint to the networked
        /// impact point. Runs only on !HasInputAuthority clients so the shooter never sees it.
        /// The GO is placed in world-space and given the same layer as the TPV weapon.
        /// </summary>
        private void SpawnTPVTrail()
        {
            if (tpvTrailPrefab == null || tpvFirePoint == null || playerController == null) return;

            Vector3 start  = tpvFirePoint.position;
            Vector3 impact = playerController.NetworkShotImpactPoint;
            if (impact == Vector3.zero) return; // not synced yet

            GameObject trailGO = Instantiate(tpvTrailPrefab, start, Quaternion.identity);

            // Match layer to TPV weapon so local player camera ignores it
            int layer = currentTPVWeaponInstance != null
                ? currentTPVWeaponInstance.layer
                : gameObject.layer;
            SetLayerRecursive(trailGO, layer);

            StartCoroutine(AnimateTPVTrail(trailGO, start, impact, tpvTrailSpeed));
        }

        private System.Collections.IEnumerator AnimateTPVTrail(
            GameObject trailGO, Vector3 start, Vector3 end, float speed)
        {
            float distance = Vector3.Distance(start, end);
            if (distance < 0.001f) { Destroy(trailGO); yield break; }

            float duration = distance / speed;
            float elapsed  = 0f;

            while (elapsed < duration)
            {
                if (trailGO == null) yield break;
                elapsed += Time.deltaTime;
                trailGO.transform.position = Vector3.Lerp(start, end, elapsed / duration);
                yield return null;
            }

            if (trailGO == null) yield break;
            trailGO.transform.position = end;

            // Wait for TrailRenderer to fade, then destroy
            TrailRenderer tr = trailGO.GetComponentInChildren<TrailRenderer>();
            if (tr != null) yield return new WaitForSeconds(tr.time);

            Destroy(trailGO);
        }
        
        /// <summary>
        /// Recursively searches for a child by name
        /// </summary>
        private Transform FindChildRecursive(Transform parent, string childName)
        {
            if (parent.name == childName)
                return parent;
            
            foreach (Transform child in parent)
            {
                Transform result = FindChildRecursive(child, childName);
                if (result != null)
                    return result;
            }
            
            return null;
        }

        private Transform FindChildRecursiveIgnoreCase(Transform parent, string childName)
        {
            if (parent.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
                return parent;

            foreach (Transform child in parent)
            {
                Transform result = FindChildRecursiveIgnoreCase(child, childName);
                if (result != null)
                    return result;
            }

            return null;
        }
        
        /// <summary>
        /// Get reference to locomotion animator (on Hips bone)
        /// </summary>
        public Animator GetLocomotionAnimator()
        {
            if (hipsBone != null)
            {
                return hipsBone.GetComponent<Animator>();
            }
            return null;
        }
        
        /// <summary>
        /// Get reference to upper body animator (on Spine2 bone)
        /// </summary>
        public Animator GetUpperBodyAnimator()
        {
            if (spine2Bone != null)
            {
                return spine2Bone.GetComponent<Animator>();
            }
            return null;
        }
        
        /// <summary>
        /// Change the character mesh object (used when switching characters)
        /// Updates visibility automatically based on local/remote player
        /// </summary>
        /// <param name="newMeshObject">GameObject containing the new character mesh (SkinnedMeshRenderer)</param>
        public void SetCharacterMesh(GameObject newMeshObject)
        {
            if (newMeshObject == null)
            {
                Debug.LogWarning("⚠️ [PlayerTPVController] Attempted to set null character mesh object!");
                return;
            }
            
            // Re-enable previous mesh if it was hidden
            if (characterMeshObject != null)
            {
                SetMeshVisibility(characterMeshObject, true);
            }
            
            // Set new mesh object
            characterMeshObject = newMeshObject;
            
            // Apply visibility based on local/remote player
            bool shouldBeVisible = !HasInputAuthority; // Visible for remote players only
            SetMeshVisibility(characterMeshObject, shouldBeVisible);
            
            Debug.Log($"🔄 [PlayerTPVController] Character mesh changed to: {newMeshObject.name} (visible: {shouldBeVisible})");
        }
        
        /// <summary>
        /// Helper method to set visibility of a specific mesh object
        /// </summary>
        private void SetMeshVisibility(GameObject meshObject, bool visible)
        {
            if (meshObject == null) return;
            
            SkinnedMeshRenderer[] skinnedRenderers = meshObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshRenderer[] meshRenderers = meshObject.GetComponentsInChildren<MeshRenderer>(true);
            
            foreach (SkinnedMeshRenderer renderer in skinnedRenderers)
            {
                renderer.enabled = visible;
            }
            
            foreach (MeshRenderer renderer in meshRenderers)
            {
                renderer.enabled = visible;
            }
        }

        /// <summary>
        /// Destroys a GameObject after waiting one frame.
        /// Gives Burst IK jobs time to release handles before the object is freed.
        /// </summary>
        private System.Collections.IEnumerator DeferredDestroy(GameObject obj)
        {
            yield return null;
            if (obj != null) Destroy(obj);
        }
    }
}
