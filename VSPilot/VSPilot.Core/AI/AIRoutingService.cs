using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VSPilot.Common.Models;
using VSPilot.Core.Services;

namespace VSPilot.Core.AI
{
    public class AIRoutingService
    {
        private readonly VSPilotSettings _settings;
        private readonly Dictionary<AIProvider, IAIService> _services;

        public AIRoutingService(
            VSPilotSettings settings,
            GitHubCopilotService copilotService,
            AnthropicService anthropicService,
            OpenAIService openAIService)
        {
            _settings = settings;
            _services = new Dictionary<AIProvider, IAIService>
            {
                { AIProvider.GitHubCopilot, copilotService },
                { AIProvider.Anthropic, anthropicService },
                { AIProvider.OpenAI, openAIService }
            };
        }

        public async Task<string> GetCompletionAsync(string prompt, bool maintainContext = true)
        {
            // Determine which provider to use
            AIProvider provider = _settings.SelectedAIProvider;

            if (provider == AIProvider.Auto)
            {
                // Auto-select based on availability
                if (_settings.UseGitHubCopilot && await _services[AIProvider.GitHubCopilot].IsAuthenticatedAsync())
                    provider = AIProvider.GitHubCopilot;
                else if (!string.IsNullOrEmpty(_settings.AnthropicApiKey))
                    provider = AIProvider.Anthropic;
                else if (!string.IsNullOrEmpty(_settings.OpenAIApiKey))
                    provider = AIProvider.OpenAI;
                else
                    return "No AI provider is configured. Please configure an API key in VSPilot Settings.";
            }

            // Check if the selected provider is available
            if (!_services.ContainsKey(provider) || !await _services[provider].IsAuthenticatedAsync())
            {
                return $"The selected AI provider ({provider}) is not available. Please check your settings.";
            }

            // Get completion from the selected provider
            return await _services[provider].GetCompletionAsync(prompt, maintainContext);
        }
    }
}