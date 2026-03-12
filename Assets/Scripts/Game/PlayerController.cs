using System.Linq;
using UnityEngine;
using Fusion;
using ArtisansGuns.Auth;
using ArtisansGuns.Networking;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// PlayerController - Controla el personaje del jugador en el juego
    /// Maneja movimiento FPS con joystick, camera look tÃƒÂ¡ctil, y sincronizaciÃƒÂ³n de red
    /// 
    /// OPTIMIZACIONES DE RED IMPLEMENTADAS:
    /// - Client-side prediction con reconciliaciÃƒÂ³n
    /// - ExtrapolaciÃƒÂ³n basada en velocidad para lag compensation
    /// - Snap thresholds para movimiento instantÃƒÂ¡neo vs smooth
    /// - InterpolaciÃƒÂ³n de alta velocidad (20 units/s) para responsive movement
    /// - OnChanged callbacks para actualizaciÃƒÂ³n eficiente de estado
    /// - MoveTowards en lugar de Lerp para velocidad constante
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        /// <summary>
        /// Static flag — when true, ALL local player input (movement, camera, shooting) is frozen.
        /// Set by the ceremony countdown system during the 3-2-1 phase.
        /// </summary>
        public static bool InputFrozen { get; set; } = false;

        [Header("Movement Settings")]
        [SerializeField] private float baseSpeed = 5f; // Base movement speed (modified by weapon weight)
        private float moveSpeed = 5f; // Current movement speed (baseSpeed * weapon multiplier)
        [SerializeField] private float jumpForce = 3f;

        // ─── Damage slow ────────────────────────────────────────────────
        private float _damageSlowMultiplier = 1f;    // 1 = normal, <1 = slowed
        private Coroutine _damageSlowCoroutine;

        [Header("Camera Settings")]
        [SerializeField] private float lookSensitivity = 2f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;


        [Header("Character Info")]
        public string characterType = "CRIMSON"; // Will be set from network data

        // Hit effects are read at runtime from the active FireWeapon's WeaponConfig

        // Components
        private CharacterController characterController;
        private Transform cameraTransform;

        // Network state (public set for OnBeforeSpawned initialization)
        [Networked] public Vector3 NetworkPosition { get; set; }
        [Networked] public Quaternion NetworkRotation { get; set; }
        
        // TPV animation sync (visible to remote players)
        [Networked] public float NetworkPitch { get; set; }
        [Networked] public byte NetworkAnimState { get; set; } // 0=Idle, 1=Walking, 2=WalkingBackwards
        [Networked] public int NetworkWeaponTrigger { get; set; } // Incremented on shot/reload to detect changes
        [Networked] public Vector3 NetworkShotImpactPoint { get; set; } // World-space impact of last shot (for TPV trail)

        // Camera rotation
        private float currentPitch = 0f; // Vertical rotation (up/down)
        private float currentYaw = 0f; // Horizontal rotation (left/right)
        
        // Camera rotation delta (for weapon sway)
        private Vector2 cameraDelta = Vector2.zero; // x = yaw delta, y = pitch delta
        
        // Weapon recoil offset (applied on top of camera rotation)
        private float recoilPitchOffset = 0f;
        private float recoilYawOffset = 0f;

        // Crouch / jump
        private Vector2 moveInput;
        private bool jumpInput;
        private bool isCrouching;            // local crouch state
        private float _crouchCooldown    = -1f; // Time.time before which CrouchButton is blocked

        // Camera smooth-crouch
        private float _crouchCamTargetY  = 1.6f;  // destination Y for smooth tween
        private float _crouchCamCurrentY = 1.6f;  // tracked separately from transform to avoid fighting
        private const float CROUCH_CAM_Y  = 0.8f;
        private const float STAND_CAM_Y   = 1.6f;
        private const float CROUCH_CAM_SPEED = 5.33f; // units/s — reaches target in ~0.15 s

        // CharacterController smooth-crouch (height + center, top-down reduction)
        private float _standHeight;                 // default CC height (cached on Spawned)
        private Vector3 _standCenter;               // default CC center
        private const float CROUCH_CC_HEIGHT = 2.33f;
        private const float CROUCH_CC_SPEED  = 8f;  // units/s — smooth transition

        // Camera look touch tracking
        private int lookTouchId = -1;
        private Vector2 lastLookTouchPosition;
        private bool isLookingAround = false;
        private Vector2 lookTouchStartPosition; // PosiciÃƒÂ³n inicial del touch para camera look

        // Physics
        private Vector3 velocity;
        private bool isGrounded;
        private bool _wasGrounded = true;  // track previous frame for land detection
        private float gravity = -33f;
        private PlayerTPVLocomotion _tpvLocomotion;

        // Fall-height tracking: only play land sound after a meaningful fall
        private float _highestYSinceGrounded;       // peak Y while airborne
        private const float MIN_FALL_FOR_LAND_SFX = 0.45f; // metres — below this, no land sound

        /// <summary>
        /// The vertical distance of the most recent landing (metres).
        /// Set on every ground→air→ground transition; WeaponSway reads this to
        /// decide whether a land impulse should fire.
        /// </summary>
        public float LastFallDistance { get; private set; }

        public override void Spawned()
        {
            // Get character controller from the prefab (do not modify its properties)
            characterController = GetComponent<CharacterController>();
            _tpvLocomotion = GetComponentInChildren<PlayerTPVLocomotion>();


            string currentScene = SceneManager.GetActiveScene().name;
            bool isLobby = currentScene == "LobbyScene";

            // Setup camera for local player
            if (HasInputAuthority)
            {
                // Initialize [Networked] position so remote clients get correct spawn position immediately
                // CRITICAL FIX: Do NOT overwrite NetworkPosition with transform.position here!
                // NetworkPosition was validly set by NetworkManager in the OnBeforeSpawned callback.
                // Instead, we should force our local transform to match the assigned NetworkPosition
                // to ensure we start at the correct spawn point, overcoming any CharacterController interference.
                
                // Disable CC briefly to force position update
                if (characterController != null) characterController.enabled = false;
                transform.position = NetworkPosition;
                transform.rotation = NetworkRotation;
                if (characterController != null) characterController.enabled = true;
                
                // Debug.Log($"[PlayerController] Spawned at NetworkPosition={NetworkPosition} (forced transform update)");

                if (isLobby)
                {
                    // In LobbyScene: disable ALL cameras to prevent the FPVCamera from being
                    // added to the URP camera stack. When this lobby player is later destroyed
                    // on scene transition, a disabled camera leaves NO stale stack reference.
                    // The lobby only needs 2D UI - no 3D cameras required.
                    var lobbyCameras = GetComponentsInChildren<Camera>(true);
                    foreach (var cam in lobbyCameras)
                        cam.enabled = false;
                    if (characterController != null)
                        characterController.enabled = false;
                    // Debug.Log($"[PlayerController] LobbyScene: disabled {lobbyCameras.Length} camera(s) - skipping SetupLocalPlayer");
                }
                else
                {
                    SetupLocalPlayer();
                }
                
                // Enable Enhanced Touch for camera look
                EnhancedTouchSupport.Enable();
                
                // Apply persisted sensitivity from SettingsManager (loaded from backend)
                if (ArtisansGuns.Managers.SettingsManager.Instance != null)
                {
                    SetLookSensitivity(ArtisansGuns.Managers.SettingsManager.Instance.GetMouseSensitivity());
                }

                // Cache default camera height for crouch restore
                if (cameraTransform != null)
                {
                    _crouchCamCurrentY = cameraTransform.localPosition.y;
                    _crouchCamTargetY  = _crouchCamCurrentY;
                }

                // Cache default CharacterController dimensions for crouch
                if (characterController != null)
                {
                    _standHeight = characterController.height;
                    _standCenter = characterController.center;
                }

                // Subscribe mobile-button events (local player only)
                ArtisansGuns.UI.MobileControlsController.OnJump   += OnJumpButton;
                ArtisansGuns.UI.MobileControlsController.OnCrouch += OnCrouchButton;
                ArtisansGuns.UI.MobileControlsController.OnStand  += OnStandButton;
                
            }
            else
            {
                // CharacterController must be disabled on remotes (blocks NetworkTransform
                // position sync), but we need a collision volume so the local player
                // can't walk through remote players.  Add a CapsuleCollider + kinematic
                // Rigidbody that mirrors the CC dimensions.
                if (characterController != null)
                {
                    var capsule    = gameObject.AddComponent<CapsuleCollider>();
                    capsule.radius = characterController.radius;
                    capsule.height = characterController.height;
                    capsule.center = characterController.center;

                    var rb          = gameObject.AddComponent<Rigidbody>();
                    rb.isKinematic  = true;
                    rb.useGravity   = false;

                    characterController.enabled = false;
                }
                
                // Disable ALL cameras on remote players to prevent rendering interference.
                // In URP, overlay cameras in the stack would render on the local screen
                // if left active, potentially overwriting the local player's FPV layer.
                var remoteCameras = GetComponentsInChildren<Camera>(true);
                foreach (var cam in remoteCameras)
                {
                    cam.enabled = false; // Disable the Camera component (preserves URP stack references)
                }
                // Debug.Log($"[PlayerController] Disabled {remoteCameras.Length} cameras on remote player");
            }

            // Load character type from network data
            var networkData = GetComponent<PlayerNetworkData>();
            if (networkData != null)
            {
                characterType = networkData.SelectedAgent.ToString();
                // Debug.Log($"Character type: {characterType}");
            }
        }

        private void SetupLocalPlayer()
        {
            // Clean old background cameras
            var allBgCams = GameObject.FindGameObjectsWithTag("Untagged")
                .Where(go => go.name.Contains("BackgroundCamera") || go.name.Contains("LetterboxOverlay"))
                .ToArray();
            
            foreach (var oldCam in allBgCams)
            {
                Destroy(oldCam);
            }
            
            // Find ALL cameras in player prefab.
            // URP Camera Stacking: PlayerCamera (Base) has FPVCamera (Overlay) in its stack.
            // Overlay cameras render ON TOP of the Base camera in stack order.
            // Setting cam.depth has NO EFFECT on URP overlay cameras.
            var allCameras = GetComponentsInChildren<Camera>(true);
            Camera baseCamera = null;
            Camera fpvCamera = null;
            
            foreach (var cam in allCameras)
            {
                cam.gameObject.SetActive(true);
                
                var urpData = cam.GetComponent<UniversalAdditionalCameraData>();
                if (urpData != null && urpData.renderType == CameraRenderType.Overlay)
                {
                    fpvCamera = cam;
                    // FORCE layer 6 (FPV) in culling mask just in case Fusion masked it out
                    if ((fpvCamera.cullingMask & (1 << 6)) == 0)
                    {
                        // Debug.LogWarning($"[PlayerController] FPVCamera culling mask {fpvCamera.cullingMask} was missing bit 6. Fixing.");
                        fpvCamera.cullingMask |= (1 << 6);
                    }
                    // Debug.Log($"[PlayerController] Found Overlay camera: {cam.gameObject.name} mask={cam.cullingMask}");
                }
                else
                {
                    baseCamera = cam;
                    // Debug.Log($"[PlayerController] Found Base camera: {cam.gameObject.name} mask={cam.cullingMask}");
                }
                
                // Apply 16:9 aspect ratio to ALL cameras for consistent letterbox/pillarbox
                ApplyAspectRatio(cam);
            }
            
            // CRITICAL: First, disable stale cameras from old player objects (e.g. LobbyScene player
            // that was despawned but not yet destroyed). Must do this BEFORE rebuilding our stack,
            // so stale FPVCameras are disabled before URP tries to render the new frame.
            Camera[] allSceneCameras = Camera.allCameras;
            int staleCamCount = 0;
            foreach (var sceneCam in allSceneCameras)
            {
                if (sceneCam.transform.IsChildOf(transform)) continue; // Our own cameras
                var parentNO = sceneCam.GetComponentInParent<Fusion.NetworkObject>();
                if (parentNO != null && parentNO != Object && sceneCam.enabled)
                {
                    sceneCam.enabled = false;
                    staleCamCount++;
                    // Debug.LogWarning($"[PlayerController] Disabled stale camera '{sceneCam.name}' (instanceID={sceneCam.GetInstanceID()}) from {parentNO.name}");
                }
            }
            // if (staleCamCount > 0)
            //    Debug.Log($"[PlayerController] Cleaned {staleCamCount} stale camera(s) before stack rebuild");
            
            // CRITICAL: Ensure the overlay camera is in the base camera's stack.
            // Clear the stack completely to remove any stale references from previous player
            // instances (e.g. LobbyScene player's FPVCamera that is still alive in memory).
            // Just checking Contains() is NOT enough: a different FPVCamera instance (same name)
            // would pass the Contains() check as false and get added, leaving 2 overlays active.
            if (baseCamera != null && fpvCamera != null)
            {
                var baseCamData = baseCamera.GetComponent<UniversalAdditionalCameraData>();
                if (baseCamData != null)
                {
                    var stack = baseCamData.cameraStack;
                    // Log stale entries before clearing
                    // if (stack.Count > 0)
                    // {
                    //     foreach (var staleCam in stack)
                    //        Debug.LogWarning($"[PlayerController] Removing stale stack entry: {(staleCam != null ? staleCam.name + " id=" + staleCam.GetInstanceID() : "NULL")}");
                    // }
                    stack.Clear(); // Remove ALL stale overlay cameras
                    stack.Add(fpvCamera); // Add ONLY our FPVCamera instance
                    // Debug.Log($"[PlayerController] Camera stack rebuilt: {fpvCamera.gameObject.name} id={fpvCamera.GetInstanceID()} | stack.Count={stack.Count}");
                }
            }
            
            if (baseCamera != null)
            {
                cameraTransform = baseCamera.transform;
            }
            else if (allCameras.Length > 0)
            {
                cameraTransform = allCameras[0].transform;
            }
            else
            {
                // Fallback: create camera
                var cameraObj = new GameObject("PlayerCamera");
                var cam = cameraObj.AddComponent<Camera>();
                cameraObj.tag = "MainCamera";
                cameraTransform = cameraObj.transform;
                
                cameraTransform.SetParent(transform);
                cameraTransform.localPosition = new Vector3(0, 1.6f, 0);
                cameraTransform.localRotation = Quaternion.identity;
                
                ApplyAspectRatio(cam);
            }
            
            // Debug.Log($"[PlayerController] SetupLocalPlayer: {allCameras.Length} cameras configured");

            // Initialize camera rotation from current transform
            currentPitch = cameraTransform.localEulerAngles.x;
            currentYaw = transform.eulerAngles.y;
            // Initialize moveSpeed with baseSpeed
            moveSpeed = baseSpeed;
        }

        private void ApplyAspectRatio(Camera camera)
        {
            const float TARGET_ASPECT = 16f / 9f;
            float currentAspect = (float)Screen.width / Screen.height;

            if (Mathf.Abs(currentAspect - TARGET_ASPECT) < 0.01f)
            {
                // Screen is 16:9 - full viewport
                camera.rect = new Rect(0, 0, 1, 1);
            }
            else if (currentAspect > TARGET_ASPECT)
            {
                // Ultra-wide screen - add pillarbox (black bars on sides)
                float height = 1f;
                float width = TARGET_ASPECT / currentAspect;
                float x = (1f - width) / 2f;
                camera.rect = new Rect(x, 0, width, height);
                // Debug.Log($"Ã°Å¸â€œÂ± Ultra-wide detected ({currentAspect:F3}) - Adding pillarbox");
                CreateBackgroundCamera();
            }
            else
            {
                // Narrow screen - add letterbox (black bars top/bottom)
                float width = 1f;
                float height = currentAspect / TARGET_ASPECT;
                float y = (1f - height) / 2f;
                camera.rect = new Rect(0, y, width, height);
                // Debug.Log($"Ã°Å¸â€œÂ± Narrow screen detected ({currentAspect:F3}) - Adding letterbox");
                CreateBackgroundCamera();
            }
        }

        private void CreateBackgroundCamera()
        {
            // NO persistir entre escenas - destruir y recrear cada vez
            // Esto evita que muestre contenido de escena anterior
            var existing = GameObject.Find("BackgroundCamera_Letterbox");
            if (existing != null)
            {
                // Debug.Log("Ã°Å¸â€œÂ· BackgroundCamera ya existe, no recrear");
                return;
            }

            // Crear una cÃƒÂ¡mara que SOLO limpia el fondo con negro
            var bgCamObj = new GameObject("BackgroundCamera_Letterbox");
            var bgCam = bgCamObj.AddComponent<Camera>();
            
            // ConfiguraciÃƒÂ³n crÃƒÂ­tica para limpiar correctamente
            bgCam.depth = -100; // Renderiza ANTES que todas
            bgCam.clearFlags = CameraClearFlags.SolidColor;
            bgCam.backgroundColor = Color.black;
            bgCam.cullingMask = 0; // NO renderiza nada
            bgCam.rect = new Rect(0, 0, 1, 1);
            bgCam.allowHDR = false;
            bgCam.allowMSAA = false;
            bgCam.allowDynamicResolution = false;
            bgCam.useOcclusionCulling = false;
            
            // PosiciÃƒÂ³n fija lejos de todo
            bgCamObj.transform.SetPositionAndRotation(new Vector3(0, -1000, 0), Quaternion.identity);
            
            // NO usar DontDestroyOnLoad - dejar que se destruya al cambiar escena
            // Esto evita ver contenido de escena anterior
            
            // Debug.Log($"Ã°Å¸â€œÂ· BackgroundCamera creada: depth={bgCam.depth}, clearFlags={bgCam.clearFlags}, cullingMask={bgCam.cullingMask}");
        }

        private void Update()
        {
            // Solo procesar input para el jugador local
            if (!HasInputAuthority) return;

            // Ceremony freeze: skip camera look when inputs are frozen
            if (InputFrozen)
            {
                // Reset touch tracking so stale touch IDs don't block camera
                // after unfreeze. Without this, a touch that started before the
                // freeze and ended during it leaves lookTouchId set to a now-dead
                // ID, causing ProcessCameraLook to skip every new touch forever.
                if (lookTouchId != -1)
                {
                    lookTouchId = -1;
                    isLookingAround = false;
                    cameraDelta = Vector2.zero;
                }
                return;
            }

            // Process camera look (touch input on right side of screen)
            ProcessCameraLook();
        }

        private void ProcessCameraLook()
        {
            if (Touch.activeTouches.Count == 0) return;


            foreach (var touch in Touch.activeTouches)
            {
                // Si ya estamos trackeando un touch, solo procesar ese (sin importar su posiciÃƒÂ³n actual)
                if (lookTouchId != -1 && touch.touchId != lookTouchId)
                    continue;

                switch (touch.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        // Solo iniciar camera look si el touch estÃ¡ en el lado DERECHO de la pantalla
                        if (touch.screenPosition.x < Screen.width * 0.5f)
                        {
                            // Lado izquierdo (joystick / UI), ignorar
                            continue;
                        }

                        if (lookTouchId == -1) // No hay touch activo
                        {
                            lookTouchId = touch.touchId;
                            lookTouchStartPosition = touch.screenPosition;
                            lastLookTouchPosition = touch.screenPosition;
                            isLookingAround = true;
                            // Debug.Log($"Ã°Å¸Å½Â¯ Camera look iniciado en: {touch.screenPosition}");
                        }
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                        if (lookTouchId == touch.touchId)
                        {
                            Vector2 delta = touch.screenPosition - lastLookTouchPosition;
                            
                            // Store camera delta for weapon sway
                            cameraDelta = delta;
                            
                            // Aplicar rotaciÃƒÂ³n basada en delta (delta ya es por frame, no multiplicar por Time.deltaTime)
                            currentYaw += delta.x * lookSensitivity * 0.005f;
                            currentPitch -= delta.y * lookSensitivity * 0.005f;
                            
                            // Normalizar yaw para evitar problemas de wrapping (mantener en 0-360)
                            while (currentYaw < 0f) currentYaw += 360f;
                            while (currentYaw >= 360f) currentYaw -= 360f;
                            
                            // Clamp pitch (vertical look)
                            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
                            
                            lastLookTouchPosition = touch.screenPosition;
                        }
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        if (lookTouchId == touch.touchId)
                        {
                            lookTouchId = -1;
                            isLookingAround = false;
                            cameraDelta = Vector2.zero; // Reset camera delta when touch ends
                            // Debug.Log($"Ã°Å¸Å½Â¯ Camera look terminado");
                        }
                        break;
                }
            }

            // Decay camera delta towards zero (for smooth weapon sway return)
            // If no camera movement this frame, delta gradually reduces
            if (!isLookingAround)
            {
                cameraDelta = Vector2.Lerp(cameraDelta, Vector2.zero, Time.deltaTime * 15f);
            }

            // NO aplicar rotaciÃƒÂ³n aquÃƒÂ­ - se hace en Render() para evitar conflictos
        }

        public override void FixedUpdateNetwork()
        {
            // En Shared Mode, cada cliente controla su propio player (HasInputAuthority)
            // NO usar HasStateAuthority (solo el server lo tiene en Shared Mode)
            if (!HasInputAuthority) return;

            // Ceremony freeze: skip all movement when inputs are frozen (3-2-1 countdown)
            if (InputFrozen) return;

            // Guard: if CharacterController is disabled (e.g. lobby player that is pending
            // despawn but FixedUpdateNetwork still fires), skip all movement logic.
            // Without this guard, cc.Move() throws errors and gravity accumulates incorrectly.
            if (characterController == null || !characterController.enabled) return;

            // Ground check
            isGrounded = characterController.isGrounded;

            // Track highest point while airborne
            if (!isGrounded)
            {
                if (transform.position.y > _highestYSinceGrounded)
                    _highestYSinceGrounded = transform.position.y;
            }

            // Detect landing: was airborne last tick, now grounded
            if (isGrounded && !_wasGrounded)
            {
                float fallDistance = _highestYSinceGrounded - transform.position.y;
                LastFallDistance = fallDistance;   // expose for WeaponSway
                if (fallDistance >= MIN_FALL_FOR_LAND_SFX)
                {
                    _tpvLocomotion?.PlayLandSound();
                }
                // Reset peak tracker on landing
                _highestYSinceGrounded = transform.position.y;
            }

            // Reset peak when firmly grounded (handles ramp/slope flicker)
            if (isGrounded)
                _highestYSinceGrounded = transform.position.y;

            _wasGrounded = isGrounded;

            // Apply gravity
            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;
            }
            else
            {
                velocity.y += gravity * Runner.DeltaTime;
            }

            // Get movement input from UIToolkit virtual joystick
            {
                moveInput = ArtisansGuns.UI.MobileControlsController.Instance != null
                    ? ArtisansGuns.UI.MobileControlsController.Instance.MoveInput
                    : Vector2.zero;

                if (moveInput.magnitude > 0.01f)
                    moveInput = moveInput.normalized;
                else
                    moveInput = Vector2.zero;
            }

            // Movement - relative to camera direction
            Vector3 move = new Vector3(moveInput.x, 0, moveInput.y);
            
            // Transform movement to world space based on player's Y rotation
            move = transform.TransformDirection(move);

            // Crouch speed penalty: 45% of total speed while crouching
            // Damage slow: applied on top of crouch penalty
            float effectiveSpeed = moveSpeed * (isCrouching ? 0.45f : 1f) * _damageSlowMultiplier;
            characterController.Move(move * effectiveSpeed * Runner.DeltaTime);

            // Jump — blocked while crouching (press Stand to stand up first)
            DidJump = false; // clear from previous tick
            if (jumpInput && isGrounded && !isCrouching)
            {
                velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
                _tpvLocomotion?.PlayJumpSound();
                DidJump = true;
            }
            jumpInput = false; // consume once per tick

            // Apply velocity
            characterController.Move(velocity * Runner.DeltaTime);

            // Aplicar rotaciÃƒÂ³n de cÃƒÂ¡mara para sincronizaciÃƒÂ³n de red
            // TambiÃƒÂ©n se aplica en Render() para visualizaciÃƒÂ³n suave entre ticks
            if (cameraTransform != null)
            {
                // CÃƒÂ¡mara: pitch (arriba/abajo) + recoil offset
                cameraTransform.localRotation = Quaternion.Euler(currentPitch + recoilPitchOffset, 0, 0);
                
                // Jugador: yaw (izquierda/derecha) + recoil offset
                transform.rotation = Quaternion.Euler(0, currentYaw + recoilYawOffset, 0);
            }

            // Update network state (despuÃƒÂ©s de aplicar rotaciÃƒÂ³n)
            NetworkPosition = transform.position;
            NetworkRotation = transform.rotation;            NetworkPitch = currentPitch;
            
            // Update locomotion state for TPV sync
            UpdateNetworkAnimState();        }

        public override void Render()
        {
            if (HasStateAuthority)
            {
                // Jugador local: aplicar rotación directamente desde input
                if (cameraTransform != null)
                {
                    // Smooth crouch camera tween (runs every frame → ~0.1 s to reach target)
                    _crouchCamCurrentY = Mathf.MoveTowards(
                        _crouchCamCurrentY, _crouchCamTargetY,
                        CROUCH_CAM_SPEED * Time.deltaTime);
                    cameraTransform.localPosition = new Vector3(0f, _crouchCamCurrentY, 0f);

                    // Smooth CharacterController height/center (top-down only)
                    if (characterController != null)
                    {
                        float targetH = isCrouching ? CROUCH_CC_HEIGHT : _standHeight;
                        float newH = Mathf.MoveTowards(
                            characterController.height, targetH,
                            CROUCH_CC_SPEED * Time.deltaTime);
                        float deltaH = newH - _standHeight; // negative when crouching
                        characterController.height = newH;
                        // Shift center down by half the height reduction so the
                        // capsule shrinks from the top and feet stay on the ground.
                        characterController.center = new Vector3(
                            _standCenter.x,
                            _standCenter.y + deltaH * 0.5f,
                            _standCenter.z);
                    }

                    // Cámara: pitch (arriba/abajo) + recoil offset
                    cameraTransform.localRotation = Quaternion.Euler(currentPitch + recoilPitchOffset, 0, 0);
                    
                    // Jugador: yaw (izquierda/derecha) + recoil offset
                    transform.rotation = Quaternion.Euler(0, currentYaw + recoilYawOffset, 0);
                }

                // Update anim state every rendered frame for max responsiveness
                // Re-read moveInput fresh so we don't use stale data
                if (ArtisansGuns.UI.MobileControlsController.Instance != null)
                {
                    var freshInput = ArtisansGuns.UI.MobileControlsController.Instance.MoveInput;
                    if (freshInput.magnitude > 0.01f)
                        moveInput = freshInput.normalized;
                    else
                        moveInput = Vector2.zero;
                }
                UpdateNetworkAnimState();
            }
            else
            {
                // Jugadores remotos: interpolación simple
                transform.position = Vector3.Lerp(transform.position, NetworkPosition, Time.deltaTime * 10f);
                transform.rotation = Quaternion.Lerp(transform.rotation, NetworkRotation, Time.deltaTime * 10f);
            }
        }

        // Called by NetworkManager to set input (legacy - now using joystick directly)
        public void SetInput(Vector2 move, bool jump)
        {
            moveInput = move;
            jumpInput = jump;
        }

        private void OnDestroy()
        {
            if (HasInputAuthority)
            {
                EnhancedTouchSupport.Disable();
                ArtisansGuns.UI.MobileControlsController.OnJump   -= OnJumpButton;
                ArtisansGuns.UI.MobileControlsController.OnCrouch -= OnCrouchButton;
                ArtisansGuns.UI.MobileControlsController.OnStand  -= OnStandButton;
            }
        }

        // ─── Crouch / Jump button handlers ────────────────────────────────────
        private void OnJumpButton()  { if (Object == null) return; if (!isCrouching) jumpInput = true; }
        // Block crouch while airborne OR during stand-up cooldown
        private void OnCrouchButton()
        {
            if (Object == null) return;                   // not spawned yet
            if (isCrouching) return;
            if (!isGrounded) return;                      // airborne (jumping or falling)
            if (Time.time < _crouchCooldown) return;     // just stood up
            SetCrouch(true);
        }
        private void OnStandButton()
        {
            if (Object == null) return;
            if (!isCrouching) return;                     // already standing — nothing to do
            SetCrouch(false);
        }

        private void SetCrouch(bool crouching)
        {
            if (isCrouching == crouching) return;
            isCrouching = crouching;
            // Only tween the camera — never touch CharacterController height/center
            _crouchCamTargetY = crouching ? CROUCH_CAM_Y : STAND_CAM_Y;

            // ---------- Immediate NetworkAnimState write (sync this tick) ----------
            if (crouching)
            {
                NetworkAnimState = 6;             // CrunchIdle — refined next frame if moving
            }
            else
            {
                _crouchCooldown = Time.time + 0.15f;  // prevent re-crouch spam
                // Write standing state immediately so Fusion syncs it this tick.
                // NoCrunch (11) is now handled locally by the remote TPVLocomotion.
                NetworkAnimState = 0;             // Idle — refined next frame if moving
            }

            ArtisansGuns.UI.MobileControlsController.Instance?.SetCrouchMode(crouching);
        }

        private void OnDrawGizmos()
        {
            // Debug visualization
            if (HasInputAuthority)
            {
                Gizmos.color = Color.green;
            }
            else
            {
                Gizmos.color = Color.red;
            }

            Gizmos.DrawWireSphere(transform.position + Vector3.up * 1f, 0.5f);
        }
        
        /// <summary>
        /// Apply weapon recoil to camera rotation (DEPRECATED - old offset system)
        /// Called by WeaponRecoil component
        /// </summary>
        public void ApplyRecoilOffset(float pitchOffset, float yawOffset)
        {
            recoilPitchOffset = pitchOffset;
            recoilYawOffset = yawOffset;
        }
        
        /// <summary>
        /// Add permanent recoil impulse to camera pitch
        /// This does NOT auto-recover - player must counter manually by moving camera down
        /// </summary>
        public void AddRecoilImpulse(float pitchKick)
        {
            // Add to currentPitch (negative because up is negative in Unity camera)
            currentPitch -= pitchKick;
            
            // Clamp to prevent looking too far up or down
            currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        }
        
        /// <summary>
        /// Add horizontal recoil impulse to camera yaw
        /// Used when moving to add erratic horizontal kick
        /// </summary>
        public void AddHorizontalRecoilImpulse(float yawKick)
        {
            // Add to currentYaw (horizontal rotation)
            currentYaw += yawKick;
        }
        
        /// <summary>
        /// Check if player is currently moving
        /// Used by weapon recoil system to apply movement penalties
        /// </summary>
        public bool IsMoving()
        {
            return moveInput.magnitude > 0.01f;
        }
        
        /// <summary>
        /// Get current movement input (for weapon sway)
        /// Returns Vector2 with x (horizontal) and y (vertical) input
        /// </summary>
        public Vector2 GetMoveInput()
        {
            return moveInput;
        }
        
        /// <summary>
        /// Get camera rotation delta (for weapon sway)
        /// Returns Vector2 with x (yaw/horizontal) and y (pitch/vertical) delta from last frame
        /// </summary>
        public Vector2 GetCameraDelta()
        {
            return cameraDelta;
        }

        /// <summary>True while the local player is crouching (for weapon-sway impulse).</summary>
        public bool IsCrouching => isCrouching;

        /// <summary>True while the character controller reports being on the ground.</summary>
        public bool IsGrounded => isGrounded;

        /// <summary>
        /// True for exactly one physics tick right after the player presses Jump.
        /// WeaponSway reads this to fire the jump impulse immediately without debounce.
        /// </summary>
        public bool DidJump { get; private set; }

        /// <summary>
        /// Directly sets the vertical velocity component.
        /// Used by abilities (e.g. Water Super Jump) to launch the player upward.
        /// </summary>
        public void SetVelocityY(float yVelocity)
        {
            velocity.y = yVelocity;
        }
        
        /// <summary>
        /// Update movement speed based on weapon speed multiplier
        /// Called by PlayerSetup when weapon is equipped
        /// </summary>
        /// <param name="speedMultiplier">Speed multiplier from WeaponConfig (1.0 = normal, 1.2 = faster, 0.8 = slower)</param>
        public void UpdateWeaponSpeedModifier(float speedMultiplier)
        {
            moveSpeed = baseSpeed * speedMultiplier;
            // Debug.Log($"âš¡ Movement speed updated: {baseSpeed} * {speedMultiplier} = {moveSpeed}");
        }
        
        /// <summary>
        /// Get current camera pitch (vertical rotation)
        /// Used by PlayerTPVController to rotate spine bone for third-person view
        /// </summary>
        /// <returns>Camera pitch angle in degrees (negative = looking down, positive = looking up)</returns>
        public float GetCameraPitch()
        {
            return currentPitch;
        }
        
        /// <summary>
        /// Set look sensitivity (camera rotation speed)
        /// Called from SettingsManager when player changes sensitivity
        /// </summary>
        public void SetLookSensitivity(float value)
        {
            lookSensitivity = Mathf.Max(0.1f, value); // Prevent zero/negative values
            // Debug.Log($"[PlayerController] Look sensitivity set to: {lookSensitivity}");
        }
        
        /// <summary>
        /// <summary>
        /// Updates NetworkAnimState based on current movement + crouch + jump state.
        /// 0=Idle 1=Walking 2=WalkingBackwards 3=StrafeLeft 4=StrafeRight
        /// 5=Jump 6=CrunchIdle 7=CrunchWalking 8=CrunchWalkingBackwards
        /// 9=CrunchStrafeRight 10=CrunchStrafeLeft 11=NoCrunch
        /// </summary>
        private void UpdateNetworkAnimState()
        {
            // Jump: only when actually ascending with significant velocity
            // DO NOT use (!isGrounded) alone — CC.isGrounded flickers and would override
            // crouching states randomly, making CrunchWalking etc. never show.
            if (velocity.y > 1.0f)
            {
                NetworkAnimState = 5;
                return;
            }
            // Crouching sub-states
            if (isCrouching)
            {
                if (moveInput.magnitude < 0.01f)
                    NetworkAnimState = 6; // CrunchIdle
                else if (moveInput.y > 0.3f && Mathf.Abs(moveInput.y) >= Mathf.Abs(moveInput.x))
                    NetworkAnimState = 7; // CrunchWalking
                else if (moveInput.y < -0.3f && Mathf.Abs(moveInput.y) >= Mathf.Abs(moveInput.x))
                    NetworkAnimState = 8; // CrunchWalkingBackwards
                else if (moveInput.x > 0f)
                    NetworkAnimState = 9; // CrunchStrafeRight
                else
                    NetworkAnimState = 10; // CrunchStrafeLeft
                return;
            }
            // Standing states
            if (moveInput.magnitude < 0.01f)
                NetworkAnimState = 0; // Idle
            else if (moveInput.y > 0.3f && Mathf.Abs(moveInput.y) >= Mathf.Abs(moveInput.x))
                NetworkAnimState = 1; // Walking
            else if (moveInput.y < -0.3f && Mathf.Abs(moveInput.y) >= Mathf.Abs(moveInput.x))
                NetworkAnimState = 2; // WalkingBackwards
            else if (moveInput.x > 0f)
                NetworkAnimState = 4; // StrafeRight
            else
                NetworkAnimState = 3; // StrafeLeft
        }
        
        // Counter for encoding weapon trigger changes
        private int weaponTriggerCounter = 0;
        
        /// <summary>
        /// Notify that a shot was fired (for TPV weapon animation sync).
        /// Called by FireWeapon after a successful shot.
        /// Encodes action type in bit 0: 0 = Shot
        /// </summary>
        public void NotifyTPVShot(Vector3 impactPoint = default)
        {
            weaponTriggerCounter++;
            NetworkShotImpactPoint = impactPoint;
            NetworkWeaponTrigger = (weaponTriggerCounter << 2) | 0; // Shot (bits: ...00)
        }
        
        /// <summary>
        /// Notify that a reload started (for TPV weapon animation sync).
        /// Called by FireWeapon when reload begins.
        /// Encodes action type in bit 0: 1 = Reload
        /// </summary>
        public void NotifyTPVReload()
        {
            weaponTriggerCounter++;
            NetworkWeaponTrigger = (weaponTriggerCounter << 2) | 1; // Reload (bits: ...01)
        }

        /// <summary>
        /// Notify remote clients to play only the TPV fire sound (no muzzle flash/trail).
        /// Used for knife/melee weapons via Animation Events.
        /// Encodes action type in bits 0-1: 2 = FireSoundOnly
        /// </summary>
        public void NotifyTPVFireSound()
        {
            weaponTriggerCounter++;
            NetworkWeaponTrigger = (weaponTriggerCounter << 2) | 2; // FireSoundOnly (bits: ...10)
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Hit effects (synchronized to all clients)
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Spawns the impact effect on ALL clients at the hit position (non-enemy surfaces only).
        /// Reads impactEffectPrefab from the shooter's active WeaponConfig via PlayerSetup
        /// (works on remote clients where FireWeapon doesn't exist).
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_SpawnBulletImpact(Vector3 position, Vector3 normal, string hitTag = "")
        {
            var setup = GetComponent<PlayerSetup>();
            var cfg   = setup?.GetActiveWeaponConfig();
            if (cfg == null) return;

            // Check for tag-specific override (e.g. Water → water sparks)
            var tagOverride = cfg.GetImpactOverride(hitTag);

            GameObject effectPrefab = tagOverride?.impactEffectPrefab ?? cfg.impactEffectPrefab;
            float      effectDur   = tagOverride != null ? tagOverride.impactEffectDuration : cfg.impactEffectDuration;
            AudioClip  effectSound = tagOverride?.impactSound ?? cfg.impactSound;

            // Visual impact effect
            if (effectPrefab != null)
            {
                Quaternion rot = normal != Vector3.zero
                    ? Quaternion.LookRotation(normal)
                    : Quaternion.identity;
                GameObject fx = Instantiate(effectPrefab, position, rot);
                Destroy(fx, effectDur);
            }

            // Impact sound in 3D space (heard by all players)
            if (effectSound != null)
            {
                GameObject sfxGO = new GameObject("ImpactSound");
                sfxGO.transform.position = position;
                AudioSource src = sfxGO.AddComponent<AudioSource>();
                src.clip         = effectSound;
                src.spatialBlend  = 1f;          // full 3D
                src.rolloffMode   = AudioRolloffMode.Linear;
                src.minDistance   = 1f;
                src.maxDistance   = 30f;
                src.playOnAwake   = false;
                src.Play();
                Destroy(sfxGO, effectSound.length + 0.1f);
            }
        }

        /// <summary>
        /// Spawns HeadBlood or BodyBlood on all clients EXCEPT the victim's.
        /// The victim never sees their own blood VFX (cleaner FPV experience).
        /// Each client reads the prefab from the SHOOTER's active WeaponConfig.
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_SpawnBloodEffect(Vector3 worldPos, bool isHeadshot, PlayerRef victimRef)
        {
            // Skip spawning on the victim's own client — they should not see blood on themselves
            if (Runner != null && victimRef == Runner.LocalPlayer) return;

            // Read prefab from the shooter's currently active weapon config
            var setup = GetComponent<PlayerSetup>();
            var cfg   = setup?.GetActiveWeaponConfig();
            GameObject prefab = isHeadshot ? cfg?.headBloodPrefab : cfg?.bodyBloodPrefab;
            if (prefab == null) return;

            // Find victim's TPV to parent the effect so the victim can't see it
            Transform parent = null;
            if (Runner != null)
            {
                var victimObj = Runner.GetPlayerObject(victimRef);
                if (victimObj != null)
                {
                    var victimSetup = victimObj.GetComponent<ArtisansGuns.Game.PlayerSetup>();
                    if (victimSetup != null && victimSetup.tpvController != null)
                        parent = victimSetup.tpvController.transform;
                    if (parent == null)
                        parent = victimObj.transform;
                }
            }

            float duration = cfg != null ? cfg.bloodEffectDuration : 3f;
            GameObject fx = Instantiate(prefab, worldPos, Quaternion.identity, parent);
            Destroy(fx, duration);
        }

        // ────────────────────────────────────────────────────────────────
        // Damage Slow (69% speed reduction for 2 seconds on hit)
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Temporarily reduces movement speed by 69% for 2 seconds.
        /// Called by PlayerHealth.RPC_TakeDamage on the victim's client.
        /// Subsequent hits refresh the timer.
        /// </summary>
        public void ApplyDamageSlow()
        {
            if (_damageSlowCoroutine != null)
                StopCoroutine(_damageSlowCoroutine);

            _damageSlowMultiplier = 0.31f;   // 100% − 69% = 31% of normal speed
            _damageSlowCoroutine  = StartCoroutine(DamageSlowRoutine());
        }

        private System.Collections.IEnumerator DamageSlowRoutine()
        {
            yield return new WaitForSeconds(2f);
            _damageSlowMultiplier = 1f;
            _damageSlowCoroutine  = null;
        }
    }
}
