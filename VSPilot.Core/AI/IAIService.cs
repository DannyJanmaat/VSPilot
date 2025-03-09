using System.Threading.Tasks;

namespace VSPilot.Core.AI
{
    public interface IAIService
    {
        Task<string> GetCompletionAsync(string prompt, bool maintainContext = true);
        Task<bool> IsAuthenticatedAsync();
        string GetProviderName();
    }
}