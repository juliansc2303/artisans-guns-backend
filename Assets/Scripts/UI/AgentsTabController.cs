using System;
using UnityEngine;
using UnityEngine.UIElements;
using ArtisansGuns.Data;
using ArtisansGuns.Managers;
using System.Collections.Generic;

namespace ArtisansGuns.UI
{
    public class AgentsTabController : MonoBehaviour
    {
        [Header("UI Document (auto-detected if null)")]
        [SerializeField] private UIDocument uiDocument;
        
        private VisualElement agentsContent;
        private VisualElement agentsGrid;
        private Label selectedAgentNameTop;
        private Label selectedAgentNameBottom;
        private Button agentLockInButton;

        private string currentAgentId; // Agent that user has locked in (current loadout)
        private string selectedAgentId; // Agent that user has clicked on (preview)

        private Dictionary<string, VisualElement> agentCardElements = new Dictionary<string, VisualElement>();

        private void Awake()
        {
            if (uiDocument == null)
                uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            if (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                return;
            }

            var root = uiDocument.rootVisualElement;

            agentsContent = root.Q<VisualElement>("AgentsContent");
            
            if (agentsContent == null)
            {
                return;
            }

            CacheUIElements();
            InitializeAgents();
        }

        private void CacheUIElements()
        {
            agentsGrid = agentsContent.Q<VisualElement>("AgentsGrid");
            selectedAgentNameTop = agentsContent.Q<Label>("SelectedAgentNameTop");
            selectedAgentNameBottom = agentsContent.Q<Label>("SelectedAgentNameBottom");
            agentLockInButton = agentsContent.Q<Button>("AgentLockInButton");

            // Register lock in button click
            if (agentLockInButton != null)
            {
                agentLockInButton.clicked += OnLockInClicked;
            }
            else
            {
                Debug.LogError("[AgentsTabController] AgentLockInButton not found in UXML!");
            }
        }

        private void InitializeAgents()
        {
            // Load current agent from LoadoutManager
            var loadout = LoadoutManager.Instance?.GetLoadout();
            if (loadout != null && !string.IsNullOrEmpty(loadout.selectedCharacter))
            {
                currentAgentId = loadout.selectedCharacter.ToLower();
            }
            else
            {
                // Default agent
                var defaultAgent = AgentDefinition.GetDefaultAgent();
                currentAgentId = (defaultAgent?.agentId ?? "crimson").ToLower();
            }

            // Set selected to current
            selectedAgentId = currentAgentId;

            PopulateAgentsGrid();
            UpdateSelectedAgentDisplay();
            UpdateLockInButtonState();
        }

        private void PopulateAgentsGrid()
        {
            if (agentsGrid == null)
            {
                return;
            }

            var scrollView = agentsGrid as ScrollView;
            if (scrollView == null)
            {
                return;
            }

            var container = scrollView.contentContainer;
            container.Clear();
            agentCardElements.Clear();

            var allAgents = AgentDefinition.GetAllAgents();

            foreach (var agent in allAgents)
            {
                var card = CreateAgentCard(agent);
                container.Add(card);
                agentCardElements[agent.agentId] = card;
            }

            UpdateCardStates();
        }

        private VisualElement CreateAgentCard(Agent agent)
        {
            var card = new VisualElement();
            card.AddToClassList("agent-card");

            bool isUnlocked = IsAgentUnlocked(agent.agentId);

            // Name label (top center)
            var nameLabel = new Label(agent.displayName);
            nameLabel.AddToClassList("agent-card-name");
            card.Add(nameLabel);

            // Icon container
            var iconContainer = new VisualElement();
            iconContainer.AddToClassList("agent-card-icon");

            var iconTexture = Resources.Load<Texture2D>(agent.iconPath);
            if (iconTexture != null)
            {
                var iconElement = new VisualElement();
                iconElement.style.width = Length.Percent(100);
                iconElement.style.height = Length.Percent(100);
                iconElement.style.backgroundImage = new StyleBackground(iconTexture);
                iconElement.style.unityBackgroundScaleMode = ScaleMode.ScaleAndCrop;
                iconElement.style.scale = new Scale(new Vector2(1.6f, 1.6f));
                iconElement.style.translate = new Translate(0, Length.Percent(10));
                iconContainer.Add(iconElement);
            }

            card.Add(iconContainer);

            // Lock overlay if locked
            if (!isUnlocked)
            {
                var lockOverlay = new VisualElement();
                lockOverlay.AddToClassList("agent-card-lock-overlay");

                var lockIcon = new Label();
                lockIcon.AddToClassList("agent-card-lock-icon");
                lockOverlay.Add(lockIcon);

                card.Add(lockOverlay);
                card.AddToClassList("agent-card-locked");
            }

            // Click handler
            card.RegisterCallback<ClickEvent>(evt =>
            {
                OnAgentCardClicked(agent.agentId);
            });

            return card;
        }

        private bool IsAgentUnlocked(string agentId)
        {
            var loadout = LoadoutManager.Instance?.GetLoadout();
            if (loadout == null) return false;

            // Check if it's the default agent
            var agent = AgentDefinition.GetAgentById(agentId);
            if (agent != null && agent.isDefault) return true;

            // Check unlockedCharacters array
            if (loadout.unlockedCharacters != null)
            {
                return System.Array.Exists(loadout.unlockedCharacters, a => a == agentId);
            }

            return false;
        }

        private void OnAgentCardClicked(string agentId)
        {
            ArtisansGuns.Managers.SoundManager.Instance?.PlayClick();
            
            selectedAgentId = agentId.ToLower();
            UpdateCardStates();
            UpdateSelectedAgentDisplay();
            UpdateLockInButtonState();
        }

        private void UpdateCardStates()
        {
            foreach (var kvp in agentCardElements)
            {
                var agentId = kvp.Key;
                var card = kvp.Value;

                // Remove all state classes
                card.RemoveFromClassList("agent-card-selected");
                card.RemoveFromClassList("agent-card-current");

                // Add appropriate class (case-insensitive comparison)
                if (agentId.Equals(selectedAgentId, StringComparison.OrdinalIgnoreCase))
                {
                    card.AddToClassList("agent-card-selected");
                }
                
                if (agentId.Equals(currentAgentId, StringComparison.OrdinalIgnoreCase))
                {
                    card.AddToClassList("agent-card-current");
                }
            }
        }

        private void UpdateSelectedAgentDisplay()
        {
            var selectedAgent = AgentDefinition.GetAgentById(selectedAgentId);
            var currentAgent = AgentDefinition.GetAgentById(currentAgentId);
            var loadout = LoadoutManager.Instance?.GetLoadout();

            if (selectedAgent != null)
            {
                bool isUnlocked = IsAgentUnlocked(selectedAgentId);
                bool isCurrent = selectedAgentId.Equals(currentAgentId, StringComparison.OrdinalIgnoreCase);

                if (selectedAgentNameTop != null)
                {
                    selectedAgentNameTop.text = selectedAgent.displayName;
                }

                if (selectedAgentNameBottom != null)
                {
                    if (isCurrent)
                    {
                        selectedAgentNameBottom.text = $"CURRENT: {currentAgent.displayName}";
                    }
                    else
                    {
                        selectedAgentNameBottom.text = $"CURRENT: {currentAgent?.displayName ?? "NONE"}";
                    }
                }

                // Update button
                if (agentLockInButton != null)
                {
                    if (!isUnlocked)
                    {
                        // Locked agent - show BUY button
                        agentLockInButton.text = $"BUY ({selectedAgent.bluePointCost} BP)";
                        agentLockInButton.SetEnabled(loadout != null && loadout.bluePoints >= selectedAgent.bluePointCost);
                    }
                    else if (isCurrent)
                    {
                        // Current agent - disable button
                        agentLockInButton.text = "LOCK IN";
                        agentLockInButton.SetEnabled(false);
                    }
                    else
                    {
                        // Unlocked but not current - show LOCK IN
                        agentLockInButton.text = "LOCK IN";
                        agentLockInButton.SetEnabled(true);
                    }
                }
            }
        }

        private void OnLockInClicked()
        {
            var selectedAgent = AgentDefinition.GetAgentById(selectedAgentId);
            if (selectedAgent == null) return;

            bool isUnlocked = IsAgentUnlocked(selectedAgentId);

            if (!isUnlocked)
            {
                // Try to buy the agent
                BuyAgent(selectedAgentId);
            }
            else if (!selectedAgentId.Equals(currentAgentId, StringComparison.OrdinalIgnoreCase))
            {
                // Lock in the agent
                ArtisansGuns.Managers.SoundManager.Instance?.PlaySelect();
                Debug.Log($"[AgentsTabController] Locking in agent: {selectedAgentId}");
                SaveAgentToLoadout(selectedAgentId);
            }
        }

        private void BuyAgent(string agentId)
        {
            var agent = AgentDefinition.GetAgentById(agentId);
            var loadout = LoadoutManager.Instance?.GetLoadout();

            if (agent == null || loadout == null)
            {
                return;
            }

            if (loadout.bluePoints < agent.bluePointCost)
            {
                return;
            }

            // TODO: Backend purchase endpoint not yet implemented
            Debug.LogWarning($"[AgentsTabController] Purchase not available yet for agent: {agentId} (cost: {agent.bluePointCost} BP)");
        }

        private void SaveAgentToLoadout(string agentId)
        {
            currentAgentId = agentId;
            
            LoadoutManager.Instance?.UpdateAgent(agentId, (success) =>
            {
                if (success)
                {
                    Debug.Log($"[AgentsTabController] Agent saved: {agentId}");
                }
            });

            UpdateCardStates();
            UpdateSelectedAgentDisplay();
            UpdateLockInButtonState();
        }

        private void UpdateLockInButtonState()
        {
            if (agentLockInButton == null) return;

            bool isCurrent = selectedAgentId.Equals(currentAgentId, StringComparison.OrdinalIgnoreCase);
            bool isUnlocked = IsAgentUnlocked(selectedAgentId);

            if (!isUnlocked)
            {
                var agent = AgentDefinition.GetAgentById(selectedAgentId);
                agentLockInButton.text = $"BUY ({agent?.bluePointCost ?? 0} BP)";
                var loadout = LoadoutManager.Instance?.GetLoadout();
                agentLockInButton.SetEnabled(loadout != null && agent != null && loadout.bluePoints >= agent.bluePointCost);
            }
            else if (isCurrent)
            {
                agentLockInButton.text = "SELECTED";
                agentLockInButton.SetEnabled(false);
            }
            else
            {
                agentLockInButton.text = "LOCK IN";
                agentLockInButton.SetEnabled(true);
            }
        }
    }
}
