using UnityEngine;

namespace ArtisansGuns.Weapons
{
    /// <summary>
    /// RecoilPattern — ScriptableObject defining a weapon's recoil kick sequence.
    /// Each entry is a (horizontal, vertical) camera kick in degrees applied per shot.
    /// Players who learn the pattern can counter-steer to reduce kicks.
    ///
    /// Design:
    ///   • Bullets always go where the crosshair points (NO spread).
    ///   • Recoil moves the CAMERA, not the bullet trajectory.
    ///   • Counter-steering (moving camera opposite to kick) rewards skilled players.
    ///   • Wrong-steering (moving camera same direction) punishes panic-steering.
    ///   • Pattern plays at the weapon's fire rate; resets after a pause.
    /// </summary>
    [CreateAssetMenu(fileName = "New Recoil Pattern", menuName = "Artisans Guns/Recoil Pattern")]
    public class RecoilPattern : ScriptableObject
    {
        // ──────────────────────── PATTERN DATA ────────────────────────

        [Header("Pattern Points")]
        [Tooltip("Sequence of camera kicks per shot.\n" +
                 "X = horizontal (positive = kick right)\n" +
                 "Y = vertical   (positive = kick up)\n" +
                 "Pattern plays one step per shot at the weapon's fire rate.")]
        public Vector2[] patternPoints = new Vector2[]
        {
            // Default 10-shot placeholder — override per weapon
            new Vector2( 0.05f, 1.0f),
            new Vector2( 0.10f, 0.9f),
            new Vector2( 0.15f, 0.8f),
            new Vector2( 0.20f, 0.7f),
            new Vector2( 0.25f, 0.6f),
            new Vector2( 0.15f, 0.5f),
            new Vector2(-0.10f, 0.5f),
            new Vector2(-0.20f, 0.4f),
            new Vector2(-0.25f, 0.4f),
            new Vector2(-0.15f, 0.3f),
        };

        [Tooltip("If true, pattern loops from the beginning after the last point.\n" +
                 "If false, the last point repeats indefinitely.")]
        public bool loopPattern = false;

        // ──────────────────── COUNTER-STEER SETTINGS ──────────────────

        [Header("Counter-Steer")]
        [Tooltip("Best-case kick multiplier when counter-steering perfectly.\n" +
                 "0 = full cancel, 1 = no reduction.  Typical: 0.25–0.4")]
        [Range(0f, 1f)]
        public float counterSteerMinMultiplier = 0.3f;

        [Tooltip("Worst-case kick multiplier when steering in the same direction as the kick.\n" +
                 "1 = no penalty, 2 = double kick.  Typical: 1.4–1.8")]
        [Range(1f, 3f)]
        public float wrongSteerAmplification = 1.5f;

        [Tooltip("Minimum alignment magnitude before counter-steer / wrong-steer is detected.\n" +
                 "Acts as a dead zone so tiny accidental touches don't trigger the system.")]
        [Range(0f, 0.5f)]
        public float steerDetectionThreshold = 0.15f;

        [Tooltip("Minimum camera-delta magnitude (pixels/frame) before counter-steer evaluation.\n" +
                 "If the player isn't actively dragging, no steer bonus/penalty is applied.")]
        public float minDeltaMagnitude = 0.3f;

        // ────────────────────── TIMING / RESET ──────────────────────

        [Header("Pattern Reset")]
        [Tooltip("How many fire-interval gaps (with no shot) before the pattern index resets to 0.\n" +
                 "E.g., 2.5 means if the player stops shooting for 2.5× the fire interval, " +
                 "the next burst starts from the beginning of the pattern.")]
        public float resetGapMultiplier = 2.5f;

        // ──────────────────── MOVEMENT MODIFIERS ──────────────────────

        [Header("Bullet Spread (movement / airborne)")]
        [Tooltip("Random bullet spread angle (degrees) when the player is moving on the ground.\n" +
                 "0 = perfectly accurate, 2 = moderate spread.")]
        [Range(0f, 100f)]
        public float movementSpreadAngle = 1.5f;

        [Tooltip("Random bullet spread angle (degrees) when the player is airborne (jumping/falling).\n" +
                 "Takes priority over movementSpreadAngle — they are NOT summed.")]
        [Range(0f, 100f)]
        public float jumpSpreadAngle = 3f;

        // ──────────────────── KICK APPLICATION ──────────────────────

        [Header("Kick Application")]
        [Tooltip("How fast pending kick is drained into the camera each frame.\n" +
                 "Higher = snappier kicks.  20–30 feels responsive.")]
        public float kickApplicationSpeed = 25f;

        // ──────────────────────── HELPERS ────────────────────────────

        /// <summary>Number of steps in the pattern.</summary>
        public int Length => patternPoints != null ? patternPoints.Length : 0;

        /// <summary>
        /// Get the kick for a given pattern index, handling loop / clamp.
        /// </summary>
        public Vector2 GetKick(int index)
        {
            if (patternPoints == null || patternPoints.Length == 0)
                return Vector2.zero;

            if (loopPattern)
                index = index % patternPoints.Length;
            else
                index = Mathf.Min(index, patternPoints.Length - 1);

            return patternPoints[index];
        }

        // ──────────────────── EDITOR UTILITIES ──────────────────────

#if UNITY_EDITOR
        [ContextMenu("Generate Default AR Pattern (30 shots)")]
        private void GenerateDefaultARPattern()
        {
            patternPoints = new Vector2[]
            {
                // Shots 1-5: Heavy upward, slight right drift
                new Vector2( 0.05f, 1.20f),
                new Vector2( 0.10f, 1.15f),
                new Vector2( 0.12f, 1.05f),
                new Vector2( 0.15f, 1.00f),
                new Vector2( 0.18f, 0.95f),
                // Shots 6-10: Medium upward, drifting right
                new Vector2( 0.25f, 0.80f),
                new Vector2( 0.30f, 0.70f),
                new Vector2( 0.35f, 0.65f),
                new Vector2( 0.40f, 0.60f),
                new Vector2( 0.45f, 0.55f),
                // Shots 11-15: Light upward, peak right then centering
                new Vector2( 0.50f, 0.50f),
                new Vector2( 0.40f, 0.45f),
                new Vector2( 0.25f, 0.42f),
                new Vector2( 0.10f, 0.40f),
                new Vector2(-0.05f, 0.38f),
                // Shots 16-20: Light upward, drifting left
                new Vector2(-0.20f, 0.40f),
                new Vector2(-0.30f, 0.42f),
                new Vector2(-0.40f, 0.45f),
                new Vector2(-0.45f, 0.48f),
                new Vector2(-0.50f, 0.50f),
                // Shots 21-25: Moderate upward, reversing back right
                new Vector2(-0.40f, 0.45f),
                new Vector2(-0.25f, 0.42f),
                new Vector2(-0.10f, 0.40f),
                new Vector2( 0.10f, 0.38f),
                new Vector2( 0.20f, 0.35f),
                // Shots 26-30: Erratic finish
                new Vector2( 0.30f, 0.50f),
                new Vector2(-0.15f, 0.55f),
                new Vector2( 0.25f, 0.45f),
                new Vector2(-0.20f, 0.50f),
                new Vector2( 0.10f, 0.40f),
            };
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"✅ [RecoilPattern] Generated 30-shot AR pattern for '{name}'");
        }

        [ContextMenu("Generate Default Pistol Pattern (8 shots)")]
        private void GenerateDefaultPistolPattern()
        {
            patternPoints = new Vector2[]
            {
                new Vector2( 0.10f, 2.00f),
                new Vector2(-0.15f, 1.80f),
                new Vector2( 0.20f, 1.60f),
                new Vector2(-0.10f, 1.50f),
                new Vector2( 0.25f, 1.40f),
                new Vector2(-0.20f, 1.30f),
                new Vector2( 0.15f, 1.50f),
                new Vector2(-0.10f, 1.40f),
            };
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"✅ [RecoilPattern] Generated 8-shot Pistol pattern for '{name}'");
        }

        [ContextMenu("Generate Default Sniper Pattern (5 shots)")]
        private void GenerateDefaultSniperPattern()
        {
            patternPoints = new Vector2[]
            {
                new Vector2( 0.30f, 3.00f),
                new Vector2(-0.20f, 2.50f),
                new Vector2( 0.40f, 2.00f),
                new Vector2(-0.50f, 2.20f),
                new Vector2( 0.10f, 2.80f),
            };
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"✅ [RecoilPattern] Generated 5-shot Sniper pattern for '{name}'");
        }
#endif
    }
}
