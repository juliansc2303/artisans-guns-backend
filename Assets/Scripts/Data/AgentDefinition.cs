using System.Collections.Generic;
using System.Linq;

namespace ArtisansGuns.Data
{
    public enum AgentRole
    {
        Duelist,
        Controller,
        Initiator,
        Sentinel
    }

    public class Agent
    {
        public string agentId;
        public string displayName;
        public AgentRole role;
        public string iconPath;
        public bool isDefault;
        public int bluePointCost;

        public Agent(string agentId, string displayName, AgentRole role, string iconPath, bool isDefault = false, int bluePointCost = 0)
        {
            this.agentId = agentId;
            this.displayName = displayName;
            this.role = role;
            this.iconPath = iconPath;
            this.isDefault = isDefault;
            this.bluePointCost = bluePointCost;
        }
    }

    public static class AgentDefinition
    {
        private static readonly List<Agent> agents = new List<Agent>
        {
            new Agent("crimson", "CRIMSON", AgentRole.Duelist, "Icons/CrimsonIcon", true),
            new Agent("pato", "PATO", AgentRole.Duelist, "Icons/PatoIcon", true)
            // Add more agents here as they become available
        };

        public static List<Agent> GetAllAgents()
        {
            return new List<Agent>(agents);
        }

        public static Agent GetAgentById(string agentId)
        {
            return agents.FirstOrDefault(a => a.agentId == agentId);
        }

        public static Agent GetDefaultAgent()
        {
            return agents.FirstOrDefault(a => a.isDefault) ?? agents.FirstOrDefault();
        }
    }
}
