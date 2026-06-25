using UnityEngine;
using Fusion;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// PlayerTPVLocomotion - Controls locomotion animations for third-person view.
    /// Also plays networked-synced footstep sounds via Animation Events.
    /// Call PlayFootstep() from walk/run animation clips at each foot-plant frame.
    /// </summary>
    public class PlayerTPVLocomotion : NetworkBehaviour
    {
        [Header("Animator References")]
        [Tooltip("Locomotion animator on mixamorig:Hips (controls lower body)")]
        public Animator locomotionAnimator;

        [Header("Footstep Sounds")]
        [SerializeField] private AudioClip step1;
        [SerializeField] private AudioClip step2;
        [SerializeField] private AudioClip jumpSound;
        [SerializeField] private AudioClip landSound;
        [Tooltip("Volume for enemy footsteps")]
        [Range(0f, 1f)] [SerializeField] private float enemyVolume = 1.0f;
        [Tooltip("Volume for teammate footsteps (softer so you can tell friend from foe)")]
        [Range(0f, 1f)] [SerializeField] private float teammateVolume = 0.6f;

        [Header("Animation Settings")]
        [Tooltip("Minimum movement speed to trigger walking animation")]
        [Range(0.1f, 2f)]
        public float walkingThreshold = 0.5f;

        [Tooltip("Minimum backward input to trigger walking backwards animation")]
        [Range(0.1f, 1f)]
        public float backwardsThreshold = 0.3f;
        
        // Animation parameter names (must match animator controller)
        private const string PARAM_IDLE                     = "Idle";
        private const string PARAM_WALKING                   = "Walking";
        private const string PARAM_WALKING_BACKWARDS         = "WalkingBackwards";
        private const string PARAM_STRAFE_LEFT               = "StrafeLeft";
        private const string PARAM_STRAFE_RIGHT              = "StrafeRight";
        private const string PARAM_JUMP                      = "Jump";
        private const string PARAM_CRUNCH                    = "Crunch";
        private const string PARAM_CRUNCH_IDLE               = "CrunchIdle";
        private const string PARAM_CRUNCH_WALKING            = "CrunchWalking";
        private const string PARAM_CRUNCH_WALKING_BACKWARDS  = "CrunchWalkingBackwards";
        private const string PARAM_CRUNCH_STRAFE_RIGHT       = "CrunchStrafeRight";
        private const string PARAM_CRUNCH_STRAFE_LEFT        = "CrunchStrafeLeft";
        private const string PARAM_NO_CRUNCH                 = "NoCrunch";

        // Runtime references
        private PlayerController playerController;
        private CharacterController characterController;
        private AudioSource audioSource;
        private Networking.PlayerNetworkData netData;
        private int _nextStep; // alternates 0/1

        // Previous state tracking (single byte — 255 = uninitialised)
        private byte _lastAnimState = 255;

        // Local NoCrunch handling: The stand-up animation transition is managed
        // entirely on the remote to avoid Fusion tick-timing issues.
        // When we detect a crouch→standing change, we play NoCrunch locally
        // for a brief period before applying the standing state.
        private bool  _wasCrouchState  = false;
        private float _noCrunchUntil   = -1f;
        private const float LOCAL_NOCRUNCH_DURATION = 0.15f; // seconds

        // Timer-based footstep system
        // States 1-4 (Walking, WalkingBackwards, StrafeLeft, StrafeRight) trigger footsteps.
        // A short delay after movement starts prevents sounds on tiny movements.
        private const float STEP_INTERVAL          = 0.45f; // seconds between each step
        private const float MOVEMENT_START_DELAY   = 0.30f; // gap before first step fires
        private float _movementStartTime = -1f;    // Time.time when walking state began
        private float _nextStepTime      = -1f;    // Time.time when next step should play
        private float _lastStepTime      = -999f;  // Time.time of the most recent footstep played
        private bool  _isWalkingState    = false; // true while in states 1-4

        public override void Spawned()
        {
            playerController  = GetComponentInParent<PlayerController>();
            characterController = GetComponentInParent<CharacterController>();
            netData           = GetComponentInParent<Networking.PlayerNetworkData>();

            if (playerController == null)
                Debug.LogWarning("⚠️ [PlayerTPVLocomotion] PlayerController not found in parent hierarchy!");

            // AudioSource — non-spatial blend so it reaches the local listener regardless of distance
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend   = 1f;   // full 3-D so position matters
            audioSource.rolloffMode    = AudioRolloffMode.Linear;
            audioSource.minDistance    = 1f;
            audioSource.maxDistance    = 25f;
            audioSource.playOnAwake    = false;
            audioSource.loop           = false;

            // Load clips from Resources/Sounds if not assigned in Inspector
            if (step1 == null) step1 = Resources.Load<AudioClip>("Sounds/Step1");
            if (step2 == null) step2 = Resources.Load<AudioClip>("Sounds/Step2");
            if (jumpSound == null) jumpSound = Resources.Load<AudioClip>("Sounds/JumpSound");
            if (landSound == null) landSound = Resources.Load<AudioClip>("Sounds/LandSound");

            if (locomotionAnimator == null)
            {
                Debug.LogWarning("⚠️ [PlayerTPVLocomotion] Locomotion animator not assigned!");
                enabled = false;
                return;
            }

            ValidateAnimatorParameters();
            SetAnimationState(0); // start in Idle
            Debug.Log("✅ [PlayerTPVLocomotion] Locomotion system initialized");
        }
        
        /// <summary>
        /// Render runs every Unity frame for ALL NetworkBehaviours (local + remote).
        /// FixedUpdateNetwork only runs for StateAuthority in Shared Mode,
        /// so remote players would never get animation updates there.
        /// </summary>
        public override void Render()
        {
            if (locomotionAnimator == null || playerController == null) return;

            byte animState = playerController.NetworkAnimState;

            // ── Local NoCrunch transition ──────────────────────────────────
            // Detect crouch→standing transition (state goes from 6-10 to 0-5).
            // When this happens, inject a local NoCrunch phase so the Animator
            // can smoothly transition out of crouch without relying on the
            // NoCrunch state surviving Fusion's tick timing.
            bool isCrouchNow = (animState >= 6 && animState <= 10);

            if (_wasCrouchState && !isCrouchNow)
            {
                // Just left crouch — start local NoCrunch timer
                _noCrunchUntil = Time.time + LOCAL_NOCRUNCH_DURATION;
            }
            _wasCrouchState = isCrouchNow;

            // Override the effective state during the NoCrunch window
            byte effectiveState = animState;
            if (Time.time < _noCrunchUntil && !isCrouchNow)
            {
                effectiveState = 11; // NoCrunch (local-only transition)
            }

            // ── Animation state change ─────────────────────────────────────
            if (effectiveState != _lastAnimState)
            {
                _lastAnimState = effectiveState;
                SetAnimationState(effectiveState);

                // Check if we entered or left a walking state (1=Walking 2=WalkingBackwards 3=StrafeLeft 4=StrafeRight)
                bool nowWalking = (effectiveState >= 1 && effectiveState <= 4);
                if (nowWalking && !_isWalkingState)
                {
                    // Just started walking — skip startup delay if we stepped recently
                    _movementStartTime = Time.time;
                    bool recentlyMoved = (Time.time - _lastStepTime) < STEP_INTERVAL * 2f;
                    _nextStepTime = recentlyMoved ? Time.time : Time.time + MOVEMENT_START_DELAY;
                }
                else if (!nowWalking)
                {
                    // Stopped walking — clear timer
                    _movementStartTime = -1f;
                    _nextStepTime      = -1f;
                }
                _isWalkingState = nowWalking;
            }

            // — Footstep ticker (runs every frame while walking AND grounded) —
            bool grounded = playerController != null && playerController.IsGrounded;
            if (_isWalkingState && grounded && _nextStepTime > 0f && Time.time >= _nextStepTime)
            {
                PlayFootstep();
                _nextStepTime = Time.time + STEP_INTERVAL;
            }
        }
        
        /// <summary>
        /// Sets all animator bool params to false then enables the one matching <paramref name="state"/>.
        /// 0=Idle 1=Walking 2=WalkingBackwards 3=StrafeLeft 4=StrafeRight
        /// 5=Jump 6=CrunchIdle 7=CrunchWalking 8=CrunchWalkingBackwards
        /// 9=CrunchStrafeRight 10=CrunchStrafeLeft 11=NoCrunch
        /// </summary>
        private void SetAnimationState(byte state)
        {
            if (locomotionAnimator == null) return;

            // Clear every managed param
            locomotionAnimator.SetBool(PARAM_IDLE,                    false);
            locomotionAnimator.SetBool(PARAM_WALKING,                 false);
            locomotionAnimator.SetBool(PARAM_WALKING_BACKWARDS,       false);
            locomotionAnimator.SetBool(PARAM_STRAFE_LEFT,             false);
            locomotionAnimator.SetBool(PARAM_STRAFE_RIGHT,            false);
            locomotionAnimator.SetBool(PARAM_JUMP,                    false);
            locomotionAnimator.SetBool(PARAM_CRUNCH,                  false);
            locomotionAnimator.SetBool(PARAM_CRUNCH_IDLE,             false);
            locomotionAnimator.SetBool(PARAM_CRUNCH_WALKING,          false);
            locomotionAnimator.SetBool(PARAM_CRUNCH_WALKING_BACKWARDS,false);
            locomotionAnimator.SetBool(PARAM_CRUNCH_STRAFE_RIGHT,     false);
            locomotionAnimator.SetBool(PARAM_CRUNCH_STRAFE_LEFT,      false);
            locomotionAnimator.SetBool(PARAM_NO_CRUNCH,               false);

            // Set the active param(s) — crouching states need Crunch=true + specific substate
            switch (state)
            {
                case 0:  locomotionAnimator.SetBool(PARAM_IDLE,                     true); break;
                case 1:  locomotionAnimator.SetBool(PARAM_WALKING,                  true); break;
                case 2:  locomotionAnimator.SetBool(PARAM_WALKING_BACKWARDS,        true); break;
                case 3:  locomotionAnimator.SetBool(PARAM_STRAFE_LEFT,              true); break;
                case 4:  locomotionAnimator.SetBool(PARAM_STRAFE_RIGHT,             true); break;
                case 5:  locomotionAnimator.SetBool(PARAM_JUMP,                     true); break;
                // Crouching states → set general Crunch flag + specific substate
                case 6:  locomotionAnimator.SetBool(PARAM_CRUNCH, true);
                         locomotionAnimator.SetBool(PARAM_CRUNCH_IDLE,              true); break;
                case 7:  locomotionAnimator.SetBool(PARAM_CRUNCH, true);
                         locomotionAnimator.SetBool(PARAM_CRUNCH_WALKING,           true); break;
                case 8:  locomotionAnimator.SetBool(PARAM_CRUNCH, true);
                         locomotionAnimator.SetBool(PARAM_CRUNCH_WALKING_BACKWARDS, true); break;
                case 9:  locomotionAnimator.SetBool(PARAM_CRUNCH, true);
                         locomotionAnimator.SetBool(PARAM_CRUNCH_STRAFE_RIGHT,      true); break;
                case 10: locomotionAnimator.SetBool(PARAM_CRUNCH, true);
                         locomotionAnimator.SetBool(PARAM_CRUNCH_STRAFE_LEFT,       true); break;
                case 11: locomotionAnimator.SetBool(PARAM_NO_CRUNCH,                true); break;
            }
        }
        
        /// <summary>
        /// Validates that animator has required parameters
        /// </summary>
        private void ValidateAnimatorParameters()
        {
            if (locomotionAnimator == null) return;

            // Warn about any missing bool parameters (non-fatal — they just won’t drive those states)
            string[] required =
            {
                PARAM_IDLE, PARAM_WALKING, PARAM_WALKING_BACKWARDS,
                PARAM_STRAFE_LEFT, PARAM_STRAFE_RIGHT, PARAM_JUMP,
                PARAM_CRUNCH, PARAM_CRUNCH_IDLE, PARAM_CRUNCH_WALKING,
                PARAM_CRUNCH_WALKING_BACKWARDS, PARAM_CRUNCH_STRAFE_RIGHT,
                PARAM_CRUNCH_STRAFE_LEFT, PARAM_NO_CRUNCH
            };
            var missing = new System.Collections.Generic.List<string>();
            foreach (var p in required)
                if (!HasParameter(p, AnimatorControllerParameterType.Bool)) missing.Add(p);

            if (missing.Count > 0)
                Debug.LogWarning($"⚠️ [PlayerTPVLocomotion] Animator missing params: {string.Join(", ", missing)}");
            else
                Debug.Log("✅ [PlayerTPVLocomotion] All animator parameters found");
        }
        
        /// <summary>
        /// Checks if animator has a parameter of specified name and type
        /// </summary>
        private bool HasParameter(string paramName, AnimatorControllerParameterType paramType)
        {
            if (locomotionAnimator == null) return false;
            
            foreach (AnimatorControllerParameter param in locomotionAnimator.parameters)
            {
                if (param.name == paramName && param.type == paramType)
                    return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Plays the next footstep sound (alternates Step1/Step2).
        /// Called automatically by the timer in Render() while in a walking state.
        /// Volume is auto-adjusted: enemy = 100 %, teammate = 60 %.
        /// </summary>
        public void PlayFootstep()
        {
            if (audioSource == null) return;

            // Pick clip — alternate Step1 / Step2 each call
            AudioClip clip = (_nextStep == 0) ? step1 : step2;
            _nextStep = 1 - _nextStep;
            if (clip == null) return;

            // Determine volume based on team relationship
            float vol = GetFootstepVolume();
            audioSource.PlayOneShot(clip, vol);
            _lastStepTime = Time.time;
        }

        /// <summary>
        /// Plays the jump sound spatially (3D), same config as footsteps.
        /// Hook this to an Animation Event on the Jump animation clip,
        /// or call directly from PlayerController when jump starts.
        /// </summary>
        public void PlayJumpSound()
        {
            if (audioSource == null || jumpSound == null) return;
            float vol = GetFootstepVolume();
            audioSource.PlayOneShot(jumpSound, vol);
        }

        /// <summary>
        /// Plays the land sound spatially (3D), same config as footsteps.
        /// Hook this to an Animation Event on the landing animation clip,
        /// or call directly from PlayerController when the player lands.
        /// </summary>
        public void PlayLandSound()
        {
            if (audioSource == null || landSound == null) return;
            float vol = GetFootstepVolume();
            audioSource.PlayOneShot(landSound, vol);
        }

        private float GetFootstepVolume()
        {
            // Local player controls their own character via FPV, not TPV — TPV is only for remotes.
            // But guard anyway: if this IS the local player, use teammate volume.
            if (HasInputAuthority) return teammateVolume;

            if (netData == null) return enemyVolume;

            // Find local player's team
            int localTeam = -1;
            foreach (var nd in FindObjectsByType<Networking.PlayerNetworkData>(FindObjectsSortMode.None))
            {
                if (nd.HasInputAuthority) { localTeam = nd.Team; break; }
            }

            if (localTeam < 0) return enemyVolume; // couldn't determine — default to enemy

            return (netData.Team == localTeam) ? teammateVolume : enemyVolume;
        }

        /// <summary>
        /// Force set animation state (useful for debugging or special states)
        /// </summary>
        public void ForceAnimationState(byte state)
        {
            _lastAnimState = state;
            SetAnimationState(state);
        }
    }
}
