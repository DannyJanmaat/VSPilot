using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace VSPilot.Common.Extensions
{
    public static class TaskExtensions
    {
        public static void Forget(
            this Task task,
            ILogger? logger = null,
            TaskScheduler? scheduler = null)
        {
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    logger?.LogError(
                        t.Exception,
                        "An unhandled exception occurred in a fire-and-forget task"
                    );
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            scheduler ?? TaskScheduler.Default);
        }
    }
}