using UnityEngine;

namespace ArtisansGuns.AI
{
    /// <summary>
    /// Defines a bot's personality profile — skill level, behavior tendencies, reaction
    /// times, and human-like imperfections. Each bot gets a unique personality at spawn.
    /// </summary>
    [System.Serializable]
    public class BotPersonality
    {
        // ── Identity ────────────────────────────────────────────────────
        public string displayName;
        public string agentId;     // "CRIMSON", "PATO", etc.
        public string primaryWeaponId;
        public string secondaryWeaponId;

        // ── Skill tier (0 = beginner, 1 = pro) ─────────────────────────
        [Range(0f, 1f)] public float skillLevel;

        // ── Aiming ──────────────────────────────────────────────────────
        /// Base accuracy (0 = misses everything, 1 = pixel-perfect)
        [Range(0f, 1f)] public float baseAccuracy;
        /// How fast the crosshair tracks a moving target (deg/s)
        public float aimSpeed;
        /// Random aim offset in degrees — simulates human jitter
        public float aimJitter;
        /// Chance of aiming for the head (0–1)
        [Range(0f, 0.4f)] public float headshotTendency;

        // ── Reaction ────────────────────────────────────────────────────
        /// Seconds to react after spotting an enemy
        public float reactionTime;
        /// Seconds to react to taking damage from behind
        public float damageReactionTime;

        // ── Movement ────────────────────────────────────────────────────
        /// How often the bot strafes during combat (0–1)
        [Range(0f, 1f)] public float strafeFrequency;
        /// How often the bot crouches while shooting (0–1)
        [Range(0f, 1f)] public float crouchCombatChance;
        /// How often the bot jumps during combat (0–1)
        [Range(0f, 0.3f)] public float jumpCombatChance;
        /// Tendency to stay near cover vs push open (0 = open, 1 = camp)
        [Range(0f, 1f)] public float campiness;

        // ── Engagement ──────────────────────────────────────────────────
        /// Preferred engagement range (metres)
        public float preferredRange;
        /// Max distance at which bot will engage
        public float maxEngagementRange;
        /// Below this HP ratio, bot retreats (e.g. 0.3 = retreat at 30% HP)
        [Range(0f, 0.5f)] public float retreatThreshold;
        /// How long (sec) the bot waits after losing target before giving up
        public float searchDuration;

        // ── Weapon handling ─────────────────────────────────────────────
        /// Seconds of burst fire before releasing trigger (auto weapons)
        public float burstDuration;
        /// Seconds between bursts
        public float burstPause;
        /// Ammo ratio below which bot reloads proactively
        [Range(0f, 0.5f)] public float reloadThreshold;

        /// <summary>
        /// Generates a random personality within a given skill tier.
        /// Lower tier = more human mistakes, higher = more competent.
        /// </summary>
        public static BotPersonality Generate(float skillTier)
        {
            skillTier = Mathf.Clamp01(skillTier);

            var p = new BotPersonality
            {
                skillLevel         = skillTier,
                baseAccuracy       = Mathf.Lerp(0.10f, 0.48f, skillTier) + Random.Range(-0.08f, 0.05f),
                aimSpeed           = Mathf.Lerp(40f,  130f, skillTier)   + Random.Range(-15f, 15f),
                aimJitter          = Mathf.Lerp(8.0f, 2.5f, skillTier)  + Random.Range(-0.5f, 0.5f),
                headshotTendency   = Mathf.Lerp(0.005f, 0.08f, skillTier),
                reactionTime       = Mathf.Lerp(1.10f, 0.45f, skillTier) + Random.Range(-0.05f, 0.15f),
                damageReactionTime = Mathf.Lerp(0.80f, 0.25f, skillTier) + Random.Range(-0.03f, 0.10f),
                strafeFrequency    = Mathf.Lerp(0.15f, 0.65f, skillTier) + Random.Range(-0.1f, 0.1f),
                crouchCombatChance = Mathf.Lerp(0.05f, 0.30f, skillTier),
                jumpCombatChance   = Mathf.Lerp(0.0f, 0.08f, skillTier),
                campiness          = Random.Range(0.1f, 0.7f),
                preferredRange     = Mathf.Lerp(10f, 18f, skillTier) + Random.Range(-3f, 3f),
                maxEngagementRange = Mathf.Lerp(30f, 50f, skillTier),
                retreatThreshold   = Mathf.Lerp(0.40f, 0.18f, skillTier),
                searchDuration     = Mathf.Lerp(3f, 6f, skillTier),
                burstDuration      = Mathf.Lerp(0.15f, 0.40f, skillTier) + Random.Range(-0.05f, 0.05f),
                burstPause         = Mathf.Lerp(0.90f, 0.40f, skillTier) + Random.Range(-0.10f, 0.10f),
                reloadThreshold    = Mathf.Lerp(0.35f, 0.20f, skillTier),
            };

            // Clamp everything to valid ranges
            p.baseAccuracy       = Mathf.Clamp01(p.baseAccuracy);
            p.aimSpeed           = Mathf.Max(30f, p.aimSpeed);
            p.aimJitter          = Mathf.Max(0.3f, p.aimJitter);
            p.reactionTime       = Mathf.Max(0.08f, p.reactionTime);
            p.damageReactionTime = Mathf.Max(0.06f, p.damageReactionTime);
            p.strafeFrequency    = Mathf.Clamp01(p.strafeFrequency);
            p.crouchCombatChance = Mathf.Clamp(p.crouchCombatChance, 0f, 0.5f);
            p.burstDuration      = Mathf.Max(0.15f, p.burstDuration);
            p.burstPause         = Mathf.Max(0.05f, p.burstPause);

            return p;
        }

        // ── Fake player names (Latin American style) ────────────────────
        private static readonly string[] _names = new string[]
        {
            "xDarkKnight", "ElMaster99", "NightFury_", "ShadowFNx",
            "ProSniper23", "ElDiablo666", "DragonSlayR", "KingCobra_",
            "ViperX_", "ToxicWolf", "GhostRider7", "DarkAngel_",
            "ElBoss_CR", "NinjaX420", "CyberPunk_", "PhantomOps",
            "IronMan_GT", "ElTigre_", "WolfPack99", "SilentKill_",
            "AceMaster", "BlazeKing", "ColdShot_", "DeathNote7",
            "EagleEye_", "FireStorm1", "GodMode_On", "HunterX_",
            "IceBreaker", "JaguarX_", "KillerBee7", "LightningX",
            "MegaBoss_", "NovaStar_", "OmegaForce", "PredatorX_",
            "QuantumX7", "RaptorElite", "StrikeForce", "ThunderGod",
            "UltraKill_", "VenomX_", "WarHawk_", "Xtreme_Pro",
            "ZeroGrav_", "AlphaDog7", "BetaTest_", "ChronoX_",
            "DeltaForce7", "EchoX_One", "FrostBite7", "GammaRay_",
            "HyperX_Pro", "InfinityX", "JokerWild_", "KryptonX7",
        };

        private static readonly string[] _agents = { "CRIMSON", "PATO" };

        private static readonly string[] _primaryWeapons =
        {
            "talon_ar"
        };

        private static readonly string[] _secondaryWeapons =
        {
            "bolt"
        };

        /// <summary>
        /// Creates a fully randomized bot personality with name, weapon loadout, and skill tier.
        /// </summary>
        public static BotPersonality CreateRandom(float skillTier = -1f)
        {
            if (skillTier < 0f)
                skillTier = Random.Range(0.15f, 0.75f);

            var p = Generate(skillTier);
            p.displayName      = _names[Random.Range(0, _names.Length)];
            p.agentId          = _agents[Random.Range(0, _agents.Length)];
            p.primaryWeaponId  = _primaryWeapons[Random.Range(0, _primaryWeapons.Length)];
            p.secondaryWeaponId = _secondaryWeapons[Random.Range(0, _secondaryWeapons.Length)];

            return p;
        }
    }
}
