using System;
using System.Threading.Tasks;
using VSPilot.Common.Models;
using VSPilot.Core.AI;

namespace VSPilot.Core.Services
{
    public class OpenAIService : IAIService
    {
        private readonly VSPilotSettings _settings;

        public OpenAIService(VSPilotSettings settings)
        {
            _settings = settings;
        }

        public Task<string> GetCompletionAsync(string prompt, bool maintainContext = true)
        {
            // Implementation will be added later
            return Task.FromResult($"OpenAI response to: {prompt}");
        }

        public Task<bool> IsAuthenticatedAsync()
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(_settings.OpenAIApiKey));
        }

        public string GetProviderName() => "OpenAI";
    }
}
