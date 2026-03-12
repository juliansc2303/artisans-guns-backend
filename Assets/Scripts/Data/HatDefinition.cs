using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ArtisansGuns.Data
{
    /// <summary>
    /// Per-agent transform offset so a hat fits each character model correctly.
    /// </summary>
    [Serializable]
    public class HatAgentOffset
    {
        public string agentId; // "crimson", "pato", etc.
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale = Vector3.one;
    }

    /// <summary>
    /// ScriptableObject that defines a single hat cosmetic.
    /// Place instances in Resources/Hats/ so they can be loaded at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "NewHat", menuName = "Ryvalen/Hat Definition")]
    public class HatDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string hatId;          // "mad_hat"
        public string displayName;    // "MAD HAT"

        [Header("Visuals")]
        [Tooltip("Icon shown in lobby UI (Resources/Icons/)")]
        public string iconPath;       // "Icons/MadHatIcon"
        [Tooltip("Prefab to instantiate on HatSpawn")]
        public GameObject hatPrefab;

        [Header("Shop")]
        public int price;
        public ShopItemDefinition.CurrencyType currency = ShopItemDefinition.CurrencyType.RivalCoins;

        [Header("Per-Agent Offsets")]
        [Tooltip("Transform offsets for each agent so the hat fits correctly")]
        public HatAgentOffset[] agentOffsets;

        /// <summary>
        /// Returns the transform offset for a specific agent, or default if not found.
        /// </summary>
        public HatAgentOffset GetOffsetForAgent(string agentId)
        {
            if (agentOffsets == null) return null;
            var lower = agentId.ToLower();
            return Array.Find(agentOffsets, o => o.agentId.ToLower() == lower);
        }

        // ─── Static catalog (loaded from Resources/Hats/) ─────────────────

        private static HatDefinition[] _allHats;

        public static HatDefinition[] GetAllHats()
        {
            if (_allHats == null)
                _allHats = Resources.LoadAll<HatDefinition>("Hats");
            return _allHats;
        }

        public static HatDefinition GetHatById(string hatId)
        {
            if (string.IsNullOrEmpty(hatId) || hatId == "none") return null;
            return GetAllHats().FirstOrDefault(h => h.hatId == hatId);
        }
    }
}
