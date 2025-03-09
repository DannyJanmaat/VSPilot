using System;
using System.Threading.Tasks;
using VSPilot.Common.Models;
using VSPilot.Core.AI;

namespace VSPilot.Core.Services
{
    public class AnthropicService : IAIService
    {
        private readonly VSPilotSettings _settings;

        public AnthropicService(VSPilotSettings settings)
        {
            _settings = settings;
        }

        public Task<string> GetCompletionAsync(string prompt, bool maintainContext = true)
        {
            // Implementation will be added later
            return Task.FromResult($"Anthropic Claude response to: {prompt}");
        }

        public Task<bool> IsAuthenticatedAsync()
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(_settings.AnthropicApiKey));
        }

        public string GetProviderName() => "Anthropic Claude";
    }
}
