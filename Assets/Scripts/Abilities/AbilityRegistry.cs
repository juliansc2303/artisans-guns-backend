using System.Collections.Generic;
using UnityEngine;

namespace ArtisansGuns.Abilities
{
    /// <summary>
    /// Central registry that loads all AbilityConfig assets from Resources/Abilities
    /// and provides look-up by abilityId. Auto-initializes on first access.
    /// </summary>
    public static class AbilityRegistry
    {
        private static Dictionary<string, AbilityConfig> _byId;
        private static AbilityConfig[] _all;

        /// <summary>All loaded ability configs.</summary>
        public static AbilityConfig[] All
        {
            get
            {
                EnsureInitialized();
                return _all;
            }
        }

        /// <summary>All tactical (non-ultimate) abilities.</summary>
        public static List<AbilityConfig> Tacticals
        {
            get
            {
                EnsureInitialized();
                var list = new List<AbilityConfig>();
                foreach (var cfg in _all)
                    if (!cfg.IsUltimate) list.Add(cfg);
                return list;
            }
        }

        /// <summary>All ultimate abilities.</summary>
        public static List<AbilityConfig> Ultimates
        {
            get
            {
                EnsureInitialized();
                var list = new List<AbilityConfig>();
                foreach (var cfg in _all)
                    if (cfg.IsUltimate) list.Add(cfg);
                return list;
            }
        }

        /// <summary>
        /// Get an AbilityConfig by its abilityId (e.g. "smoke_grenade").
        /// Returns null if not found.
        /// </summary>
        public static AbilityConfig Get(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId)) return null;
            EnsureInitialized();
            _byId.TryGetValue(abilityId, out var cfg);
            return cfg;
        }

        /// <summary>
        /// Returns the ability ID that MUST be equipped when this ability is selected, or null.
        /// Currently no abilities have dependencies.
        /// </summary>
        public static string GetLinkedAbilityId(string abilityId)
        {
            return null;
        }

        /// <summary>
        /// Check if two ability IDs form a linked pair.
        /// Returns true if either depends on the other.
        /// </summary>
        public static bool AreLinked(string id1, string id2)
        {
            return GetLinkedAbilityId(id1) == id2 || GetLinkedAbilityId(id2) == id1;
        }

        private static void EnsureInitialized()
        {
            if (_byId != null) return;
            _all = Resources.LoadAll<AbilityConfig>("Abilities");
            _byId = new Dictionary<string, AbilityConfig>(_all.Length);
            foreach (var cfg in _all)
            {
                if (!string.IsNullOrEmpty(cfg.abilityId))
                    _byId[cfg.abilityId] = cfg;
            }
            Debug.Log($"[AbilityRegistry] Loaded {_all.Length} ability configs");
        }
    }
}
