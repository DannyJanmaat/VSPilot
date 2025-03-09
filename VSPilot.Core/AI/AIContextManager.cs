using System.Collections.Generic;
using VSPilot.Common.Models;

namespace VSPilot.Core.AI
{
    public class AIContextManager
    {
        private readonly Dictionary<AIProvider, List<ChatMessage>> _contextByProvider = new Dictionary<AIProvider, List<ChatMessage>>();

        public void AddToContext(AIProvider provider, string role, string content)
        {
            if (!_contextByProvider.ContainsKey(provider))
            {
                _contextByProvider[provider] = new List<ChatMessage>();
            }

            _contextByProvider[provider].Add(new ChatMessage { Role = role, Content = content });
        }

        public List<ChatMessage> GetContext(AIProvider provider)
        {
            if (_contextByProvider.ContainsKey(provider))
            {
                return _contextByProvider[provider];
            }

            return new List<ChatMessage>();
        }

        public void SynchronizeContexts()
        {
            // Implementation to sync context between providers
        }
    }
}
