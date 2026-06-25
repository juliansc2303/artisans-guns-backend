using UnityEngine;

namespace ArtisansGuns.Game
{
    /// <summary>
    /// Procedural animation for the TPV weapon holder:
    /// idle breathing bob, gentle sway, and fire recoil kick.
    /// Added at runtime by PlayerTPVController for remote players only.
    /// </summary>
    public class WeaponHolderTPVAnimator : MonoBehaviour
    {
        // ── Breathing (vertical sine wave) ──
        private const float BREATH_AMP  = 0.003f;   // metres
        private const float BREATH_FREQ = 1.2f;     // Hz

        // ── Idle Sway (horizontal drift + subtle rotation) ──
        private const float SWAY_X_AMP   = 0.002f;  // metres
        private const float SWAY_ROT_AMP = 0.6f;    // degrees
        private const float SWAY_FREQ    = 0.7f;    // Hz

        // ── Fire Recoil ──
        private const float RECOIL_BACK    = 0.035f; // metres backward (-Z)
        private const float RECOIL_UP_DEG  = 2.5f;   // degrees pitch up
        private const float RECOIL_KICK_T  = 0.08f;  // kick phase duration (sec)
        private const float RECOIL_RETURN_T = 0.12f;  // return phase duration (sec)

        // ── State ──
        private Vector3    _basePos;
        private Quaternion _baseRot;
        private float _breathPhase;
        private float _swayPhase;
        private float _recoilTime = -1f;

        private void Awake()
        {
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            // Random offset so multiple remote players don't breathe in sync
            _breathPhase = Random.Range(0f, Mathf.PI * 2f);
            _swayPhase   = Random.Range(0f, Mathf.PI * 2f);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // ── Breathing ──
            _breathPhase += dt * BREATH_FREQ * Mathf.PI * 2f;
            float breathY = Mathf.Sin(_breathPhase) * BREATH_AMP;

            // ── Idle Sway ──
            _swayPhase += dt * SWAY_FREQ * Mathf.PI * 2f;
            float swayX    = Mathf.Sin(_swayPhase) * SWAY_X_AMP;
            float swayRotZ = Mathf.Sin(_swayPhase * 0.8f) * SWAY_ROT_AMP;
            float swayRotY = Mathf.Cos(_swayPhase * 0.6f) * SWAY_ROT_AMP * 0.5f;

            // ── Fire Recoil ──
            float recoilFactor = 0f;
            if (_recoilTime >= 0f)
            {
                _recoilTime += dt;
                if (_recoilTime <= RECOIL_KICK_T)
                {
                    // Kick: fast ease-out to full recoil
                    float p = _recoilTime / RECOIL_KICK_T;
                    recoilFactor = 1f - (1f - p) * (1f - p);
                }
                else if (_recoilTime <= RECOIL_KICK_T + RECOIL_RETURN_T)
                {
                    // Return: ease back to zero
                    float p = (_recoilTime - RECOIL_KICK_T) / RECOIL_RETURN_T;
                    recoilFactor = 1f - p * p;
                }
                else
                {
                    _recoilTime = -1f;
                }
            }

            // ── Combine ──
            Vector3 posOffset = new Vector3(swayX, breathY, -RECOIL_BACK * recoilFactor);
            Quaternion rotOffset = Quaternion.Euler(-RECOIL_UP_DEG * recoilFactor, swayRotY, swayRotZ);

            transform.localPosition = _basePos + posOffset;
            transform.localRotation = _baseRot * rotOffset;
        }

        /// <summary>Start a recoil kick. Called by PlayerTPVController on shot detection.</summary>
        public void TriggerRecoil()
        {
            _recoilTime = 0f;
        }
    }
}
