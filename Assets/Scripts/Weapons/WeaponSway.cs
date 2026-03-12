using UnityEngine;
using ArtisansGuns.Game;

namespace ArtisansGuns.Weapons
{
    /// <summary>
    /// WeaponSway - Adds realistic weapon movement for immersive FPS feel
    /// Similar to Valorant's weapon system
    /// Applied to WeaponHolder transform
    /// Only active when NOT firing (recoil has priority)
    /// 
    /// THREE COMPONENTS:
    /// 1. Camera Sway: Weapon lags behind camera rotation
    /// 2. Idle Breathing: Subtle up/down movement when standing still
    /// 3. Walk Movement: Directional tilt + bobbing + horizontal zigzag when walking
    /// </summary>
    public class WeaponSway : MonoBehaviour
    {
        [Header("Camera Sway Settings")]
        [Tooltip("Amount of camera sway rotation (degrees)")]
        [SerializeField] private float cameraSwayAmount = 3f;
        
        [Tooltip("Amount of camera sway position offset")]
        [SerializeField] private float cameraSwayPositionAmount = 0.015f;
        
        [Tooltip("Speed of camera sway interpolation")]
        [SerializeField] private float cameraSwaySmooth = 8f;
        
        [Header("Idle Breathing Settings")]
        [Tooltip("Vertical breathing amplitude when idle")]
        [SerializeField] private float idleBreathingAmount = 0.005f;
        
        [Tooltip("Speed of breathing cycle")]
        [SerializeField] private float idleBreathingSpeed = 1.5f;
        
        [Header("Walk Movement Settings")]
        [Tooltip("Rotation tilt when moving sideways (degrees)")]
        [SerializeField] private float walkTiltAmount = 3f;
        
        [Tooltip("Vertical bobbing amplitude when walking")]
        [SerializeField] private float walkBobbingVertical = 0.03f;
        
        [Tooltip("Horizontal zigzag amplitude when walking")]
        [SerializeField] private float walkBobbingHorizontal = 0.02f;
        
        [Tooltip("Speed of walk bobbing cycle")]
        [SerializeField] private float walkBobbingSpeed = 10f;
        
        [Tooltip("Smoothness of directional tilt transition")]
        [SerializeField] private float walkTiltSmooth = 8f;
        
        [Header("Reset Settings")]
        [Tooltip("Speed of sway return to center")]
        [SerializeField] private float swayResetSmooth = 10f;

        [Header("Action Impulse Settings")]
        [Tooltip("How fast the impulse decays back to zero (higher = snappier)")]
        [SerializeField] private float impulseDecaySpeed = 9f;
        [Tooltip("Position kick applied when jumping")]
        [SerializeField] private Vector3 jumpImpulsePos    = new Vector3(0f, -0.025f, -0.006f);
        [Tooltip("Rotation kick (euler) applied when jumping")]
        [SerializeField] private Vector3 jumpImpulseRot    = new Vector3(4f,  0f,  0f);
        [Tooltip("Position kick applied when crouching")]
        [SerializeField] private Vector3 crouchImpulsePos  = new Vector3(0f, -0.018f, 0f);
        [Tooltip("Rotation kick (euler) applied when crouching")]
        [SerializeField] private Vector3 crouchImpulseRot  = new Vector3(3f,  0f,  0f);
        [Tooltip("Position kick applied when standing up")]
        [SerializeField] private Vector3 standImpulsePos   = new Vector3(0f,  0.018f, 0f);
        [Tooltip("Rotation kick (euler) applied when standing up")]
        [SerializeField] private Vector3 standImpulseRot   = new Vector3(-3f, 0f,  0f);
        [Tooltip("Position kick applied when landing")]
        [SerializeField] private Vector3 landImpulsePos    = new Vector3(0f, -0.018f, 0f);
        [Tooltip("Rotation kick (euler) applied when landing")]
        [SerializeField] private Vector3 landImpulseRot    = new Vector3(3f,  0f,  0f);
        
        [Header("References")]
        private PlayerController playerController;
        private FireWeapon fireWeapon;
        private WeaponRecoil weaponRecoil;
        private Transform weaponHolder;
        private bool weaponHolderAssigned = false; // Flag to know if it was set externally
        
        [Header("Sway State")]
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        
        // Camera sway
        private Vector3 cameraSwayPosition;
        private Quaternion cameraSwayRotation;
        
        // Idle breathing
        private float idleBreathingTimer = 0f;
        
        // Walk movement
        private float walkBobbingTimer = 0f;
        private float currentWalkTilt = 0f; // Current Z-axis rotation based on movement direction
        private Vector3 walkMovementOffset;
        private Quaternion walkTiltRotation;

        // Action impulse (jump / crouch / stand / land) — decays to zero each frame
        private Vector3    _impulsePos = Vector3.zero;
        private Quaternion _impulseRot = Quaternion.identity;
        // State tracking for edge detection
        private bool _wasCrouching = false;
        private bool _wasGrounded  = true;

        // Debounce: require a minimum airborne time before recognising a
        // jump/land transition.  Prevents ramp/stair flicker spam.
        private float _airborneStartTime = -1f;
        private const float MIN_AIRBORNE_FOR_IMPULSE = 0.12f; // seconds
        
        private void Start()
        {
            // If WeaponHolder was not assigned externally, determine it automatically
            if (!weaponHolderAssigned)
            {
                // Check if this script is directly on the WeaponHolder
                if (gameObject.name.Contains("Holder") || gameObject.name.Contains("holder"))
                {
                    // This script is ON the WeaponHolder
                    weaponHolder = transform;
                    // Debug.Log($"âœ… [WeaponSway] Script is on WeaponHolder: {weaponHolder.name}");
                }
                else
                {
                    // This script is on the weapon, parent should be WeaponHolder
                    weaponHolder = transform.parent;
                    // Debug.LogWarning($"âš ï¸ [WeaponSway] Script on weapon, using parent as WeaponHolder: {(weaponHolder != null ? weaponHolder.name : "NULL")}");
                }
            }
            
            if (weaponHolder == null)
            {
                // Debug.LogError("âŒ [WeaponSway] WeaponHolder not found!");
                enabled = false;
                return;
            }
            
            // DEBUG: Verify correct assignment
            // Debug.LogWarning($"ðŸ” [WeaponSway] This GameObject: {gameObject.name}");
            // Debug.LogWarning($"ðŸ” [WeaponSway] WeaponHolder found: {weaponHolder.name}");
            // Debug.LogWarning($"ðŸ” [WeaponSway] WeaponHolder parent: {(weaponHolder.parent != null ? weaponHolder.parent.name : "NULL")}");
            
            // Store original WeaponHolder transform
            originalLocalPosition = weaponHolder.localPosition;
            originalLocalRotation = weaponHolder.localRotation;
            
            // Only fallback to FindObjectOfType if not already set by PlayerSetup.
            // In multiplayer, FindObjectOfType can return the REMOTE player's controller.
            if (playerController == null)
            {
                playerController = FindObjectOfType<PlayerController>();
            }
            
            // Get FireWeapon component (might be on this object or children if script is on WeaponHolder)
            fireWeapon = GetComponent<FireWeapon>();
            if (fireWeapon == null)
            {
                fireWeapon = GetComponentInChildren<FireWeapon>();
            }
            if (fireWeapon == null)
            {
                // Debug.LogWarning("âš ï¸ [WeaponSway] FireWeapon component not found!");
            }
            
            // Get WeaponRecoil component (might be on this object or children if script is on WeaponHolder)
            weaponRecoil = GetComponent<WeaponRecoil>();
            if (weaponRecoil == null)
            {
                weaponRecoil = GetComponentInChildren<WeaponRecoil>();
            }
            if (weaponRecoil == null)
            {
                // Debug.LogWarning("âš ï¸ [WeaponSway] WeaponRecoil component not found!");
            }
            
            // Debug.Log($"âœ… [WeaponSway] Initialized on {gameObject.name}");
        }
        
        /// <summary>
        /// Get current sway position offset (for WeaponRecoil to combine with recoil)
        /// </summary>
        public Vector3 GetCurrentSwayPositionOffset()
        {
            return cameraSwayPosition + walkMovementOffset + _impulsePos;
        }
        
        /// <summary>
        /// Get current sway rotation offset (for WeaponRecoil to combine with recoil)
        /// </summary>
        public Quaternion GetCurrentSwayRotationOffset()
        {
            return cameraSwayRotation * walkTiltRotation * _impulseRot;
        }
        
        /// <summary>Add a one-shot positional + rotational impulse (additive, decays automatically).</summary>
        private void AddImpulse(Vector3 posKick, Vector3 eulerKick)
        {
            _impulsePos += posKick;
            _impulseRot  = _impulseRot * Quaternion.Euler(eulerKick);
        }

        private void Update()
        {
            if (weaponHolder == null || playerController == null) return;

            // — Detect action events and inject impulse —
            bool crouching = playerController.IsCrouching;
            bool grounded  = playerController.IsGrounded;

            if (crouching && !_wasCrouching)             // just crouched
                AddImpulse(crouchImpulsePos, crouchImpulseRot);
            else if (!crouching && _wasCrouching)        // just stood up
                AddImpulse(standImpulsePos, standImpulseRot);

            // — Jump impulse: fires immediately when the player presses Jump —
            // No debounce needed — DidJump is only true for real key-press jumps.
            if (playerController.DidJump)
                AddImpulse(jumpImpulsePos, jumpImpulseRot);

            // Track how long the player has been airborne to filter slope/stair flicker.
            if (!grounded && _wasGrounded)
                _airborneStartTime = Time.time;          // just left the ground

            bool wasAirborneEnough = grounded
                && _airborneStartTime >= 0f
                && (Time.time - _airborneStartTime) >= MIN_AIRBORNE_FOR_IMPULSE;

            // — Land impulse: only after confirmed meaningful airborne time + fall distance —
            if (grounded && !_wasGrounded && wasAirborneEnough)
            {
                if (playerController.LastFallDistance >= 0.45f)
                    AddImpulse(landImpulsePos, landImpulseRot);

                _airborneStartTime = -1f;
            }

            _wasCrouching = crouching;
            _wasGrounded  = grounded;

            // Decay impulse toward neutral
            float t = Time.deltaTime * impulseDecaySpeed;
            _impulsePos = Vector3.Lerp(_impulsePos, Vector3.zero, t);
            _impulseRot = Quaternion.Slerp(_impulseRot, Quaternion.identity, t);

            // Don't apply sway while firing
            if (fireWeapon != null && fireWeapon.IsFiring())
            {
                // Reset to original when firing
                ReturnToOriginal();
                return;
            }
            
            // === 1. CAMERA SWAY ===
            ApplyCameraSway();
            
            // === 2. CHECK IF MOVING ===
            Vector2 moveInput = playerController.GetMoveInput();
            bool isWalking = moveInput.magnitude > 0.01f;
            
            if (isWalking)
            {
                // === 3A. WALK MOVEMENT (bobbing + tilt) ===
                ApplyWalkMovement(moveInput);
                
                // Reset idle breathing when walking
                idleBreathingTimer = 0f;
            }
            else
            {
                // === 3B. IDLE BREATHING ===
                ApplyIdleBreathing();
                
                // Smoothly return walk effects to zero
                walkBobbingTimer = 0f;
                walkMovementOffset = Vector3.Lerp(walkMovementOffset, Vector3.zero, Time.deltaTime * swayResetSmooth);
                currentWalkTilt = Mathf.Lerp(currentWalkTilt, 0f, Time.deltaTime * walkTiltSmooth);
                walkTiltRotation = Quaternion.Slerp(walkTiltRotation, Quaternion.identity, Time.deltaTime * walkTiltSmooth);
            }
            
            // === 4. COMBINE ALL EFFECTS ===
            // NOTE: Los valores se calculan pero NO se aplican aquí
            // WeaponRecoil.Update() obtiene estos offsets y los combina con recoil
            // Solo aplicamos si NO hay WeaponRecoil (weapon sin recoil, como melee)
            
            if (weaponRecoil == null)
            {
                // Sin recoil, aplicar sway directamente
                Vector3 finalPosition = originalLocalPosition + cameraSwayPosition + walkMovementOffset + _impulsePos;
                Quaternion finalRotation = originalLocalRotation * cameraSwayRotation * walkTiltRotation * _impulseRot;
                
                weaponHolder.localPosition = finalPosition;
                weaponHolder.localRotation = finalRotation;
            }
            // Si hay recoil, WeaponRecoil se encarga de aplicar la combinación
        }
        
        /// <summary>
        /// Apply camera sway - weapon lags behind camera rotation
        /// </summary>
        private void ApplyCameraSway()
        {
            Vector2 cameraDelta = playerController.GetCameraDelta();
            
            // Calculate camera sway (weapon moves opposite to camera for lag effect)
            float targetCameraSwayRotationX = cameraDelta.y * cameraSwayAmount * 0.01f; // Pitch
            float targetCameraSwayRotationZ = -cameraDelta.x * cameraSwayAmount * 0.01f; // Yaw â†’ Roll
            
            float targetCameraSwayPosX = -cameraDelta.x * cameraSwayPositionAmount * 0.001f;
            float targetCameraSwayPosY = -cameraDelta.y * cameraSwayPositionAmount * 0.001f;
            
            // Smooth interpolation
            cameraSwayRotation = Quaternion.Slerp(
                cameraSwayRotation,
                Quaternion.Euler(targetCameraSwayRotationX, 0f, targetCameraSwayRotationZ),
                Time.deltaTime * cameraSwaySmooth
            );
            
            cameraSwayPosition = Vector3.Lerp(
                cameraSwayPosition,
                new Vector3(targetCameraSwayPosX, targetCameraSwayPosY, 0f),
                Time.deltaTime * cameraSwaySmooth
            );
        }
        
        /// <summary>
        /// Apply idle breathing - subtle up/down movement when standing still
        /// </summary>
        private void ApplyIdleBreathing()
        {
            idleBreathingTimer += Time.deltaTime * idleBreathingSpeed;
            
            // Simple sine wave for breathing
            float breathingOffset = Mathf.Sin(idleBreathingTimer) * idleBreathingAmount;
            
            // Apply to walk movement offset (Y only)
            walkMovementOffset = Vector3.Lerp(
                walkMovementOffset,
                new Vector3(0f, breathingOffset, 0f),
                Time.deltaTime * 5f
            );
        }
        
        /// <summary>
        /// Apply walk movement - directional tilt + vertical bobbing + horizontal zigzag
        /// Similar to Valorant's walk animation
        /// </summary>
        private void ApplyWalkMovement(Vector2 moveInput)
        {
            float movementMagnitude = moveInput.magnitude;
            
            // === DIRECTIONAL TILT ===
            // When moving right, weapon tilts right (negative Z rotation)
            // When moving left, weapon tilts left (positive Z rotation)
            // Forward/backward movement has minimal tilt
            float targetTilt = -moveInput.x * walkTiltAmount; // Inverted: positive input (right) = negative tilt
            currentWalkTilt = Mathf.Lerp(currentWalkTilt, targetTilt, Time.deltaTime * walkTiltSmooth);
            walkTiltRotation = Quaternion.Euler(0f, 0f, currentWalkTilt);
            
            // === BOBBING AND ZIGZAG ===
            // Advance timer based on movement speed
            walkBobbingTimer += Time.deltaTime * walkBobbingSpeed * movementMagnitude;
            
            // Vertical bobbing (up/down) - always present when moving
            float verticalBob = Mathf.Sin(walkBobbingTimer) * walkBobbingVertical;
            
            // Horizontal zigzag (side to side) - uses different frequency for natural feel
            // Cos at half speed creates figure-8 pattern with vertical bob
            float horizontalZigzag = Mathf.Cos(walkBobbingTimer * 0.5f) * walkBobbingHorizontal;
            
            // Combine bobbing offsets
            walkMovementOffset = new Vector3(horizontalZigzag, verticalBob, 0f);
        }
        
        /// <summary>
        /// Smoothly return WeaponHolder to original position
        /// Called when firing
        /// </summary>
        private void ReturnToOriginal()
        {
            // Reset camera sway
            cameraSwayRotation = Quaternion.Slerp(
                cameraSwayRotation,
                Quaternion.identity,
                Time.deltaTime * swayResetSmooth
            );
            
            cameraSwayPosition = Vector3.Lerp(
                cameraSwayPosition,
                Vector3.zero,
                Time.deltaTime * swayResetSmooth
            );
            
            // Reset walk movement
            walkMovementOffset = Vector3.Lerp(
                walkMovementOffset,
                Vector3.zero,
                Time.deltaTime * swayResetSmooth
            );
            
            // Reset walk tilt
            currentWalkTilt = Mathf.Lerp(currentWalkTilt, 0f, Time.deltaTime * swayResetSmooth);
            walkTiltRotation = Quaternion.Slerp(
                walkTiltRotation,
                Quaternion.identity,
                Time.deltaTime * swayResetSmooth
            );
            
            // Reset timers
            walkBobbingTimer = 0f;
            idleBreathingTimer = 0f;
            
            // Only apply directly if there's no WeaponRecoil.
            // When WeaponRecoil exists, IT is the single writer to weaponHolder
            // (combining sway offsets + recoil). Writing here would overwrite recoil.
            if (weaponRecoil == null)
            {
                weaponHolder.localPosition = originalLocalPosition + cameraSwayPosition + walkMovementOffset + _impulsePos;
                weaponHolder.localRotation = originalLocalRotation * cameraSwayRotation * walkTiltRotation * _impulseRot;
            }
        }
        
        /// <summary>
        /// Reset original transform (called after recoil updates WeaponHolder)
        /// </summary>
        public void ResetOriginalTransform()
        {
            if (weaponHolder != null)
            {
                originalLocalPosition = weaponHolder.localPosition;
                originalLocalRotation = weaponHolder.localRotation;
            }
        }
        
        /// <summary>
        /// Set the correct WeaponHolder reference (called by PlayerSetup)
        /// This ensures we modify the right transform, not the parent camera
        /// </summary>
        public void SetWeaponHolder(Transform holder)
        {
            weaponHolder = holder;
            weaponHolderAssigned = true;
            
            if (weaponHolder != null)
            {
                originalLocalPosition = weaponHolder.localPosition;
                originalLocalRotation = weaponHolder.localRotation;
            }
        }
        
        /// <summary>
        /// Set the correct PlayerController reference (called by PlayerSetup).
        /// Avoids FindObjectOfType which can return the REMOTE player's controller
        /// in a multiplayer scene.
        /// </summary>
        public void SetPlayerController(PlayerController pc)
        {
            playerController = pc;
        }
        
        /// <summary>
        /// Update the WeaponRecoil reference after a weapon switch.
        /// Start() only runs once, so the old weapon's WeaponRecoil becomes null on destroy.
        /// Called by WeaponRecoil.SetWeaponHolder() to keep cross-references in sync.
        /// </summary>
        public void SetWeaponRecoil(WeaponRecoil recoil)
        {
            weaponRecoil = recoil;
        }
    }
}
