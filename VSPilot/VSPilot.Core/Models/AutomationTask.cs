using System;
using System.Threading.Tasks;
using VSPilot.Common.Models;

namespace VSPilot.Core.Models
{
    public class AutomationTask
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Description { get; set; } = string.Empty;
        public Func<Action<ProgressInfo>, Task> Action { get; set; } = _ => Task.CompletedTask;

        public async Task ExecuteAsync(Action<ProgressInfo> progressCallback)
        {
            progressCallback(new ProgressInfo
            {
                Stage = "Starting",
                Progress = 0,
                Detail = Description
            });

            await Action(progressCallback);

            progressCallback(new ProgressInfo
            {
                Stage = "Complete",
                Progress = 100,
                IsComplete = true
            });
        }
    }
}
