using System.Threading.Tasks;
using VSPilot.Core.AI;

namespace VSPilot.Core.Automation
{
    public class ProjectCreationService
    {
        private readonly AIRoutingService _aiService;
        private readonly ProjectManager _projectManager;

        public ProjectCreationService(AIRoutingService aiService, ProjectManager projectManager)
        {
            _aiService = aiService;
            _projectManager = projectManager;
        }

        public async Task CreateProjectFromDescriptionAsync(string description)
        {
            // This will be implemented later
            await Task.CompletedTask;
        }
    }
}