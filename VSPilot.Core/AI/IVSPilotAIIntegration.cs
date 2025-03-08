using System.Threading.Tasks;
using VSPilot.Common.Models;

namespace VSPilot.Core.AI
{
    public interface IVSPilotAIIntegration
    {
        Task<ProjectChanges> GetProjectChangesAsync(string prompt);
        Task<string> GetDirectResponseAsync(string prompt);
        void QueueVSPilotProjectAnalysis(string projectName);
    }
}