using System;
using System.Threading;
using System.Threading.Tasks;
using VSPilot.Common.Models;

namespace VSPilot.Common.Interfaces
{
    /// <summary>
    /// Defines operations for managing solution and project builds.
    /// </summary>
    public interface IBuildManager
    {
        /// <summary>
        /// Builds the entire solution.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token to cancel the build.</param>
        /// <returns>True if the build succeeds, false otherwise.</returns>
        Task<bool> BuildSolutionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Cleans the solution by removing all build outputs.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token to cancel the clean operation.</param>
        Task CleanSolutionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs all tests in the solution.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token to cancel the test run.</param>
        /// <returns>True if all tests pass, false otherwise.</returns>
        Task<bool> RunTestsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds a specific project in the solution.
        /// </summary>
        /// <param name="projectName">Name of the project to build.</param>
        /// <param name="cancellationToken">Optional cancellation token to cancel the build.</param>
        /// <returns>True if the build succeeds, false otherwise.</returns>
        Task<bool> BuildProjectAsync(string projectName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current build status.
        /// </summary>
        /// <returns>Current build status information.</returns>
        Task<BuildStatus> GetBuildStatusAsync();

        /// <summary>
        /// Event raised when build progress is updated.
        /// </summary>
        event EventHandler<BuildProgressEventArgs> BuildProgressUpdated;

        /// <summary>
        /// Event raised when the build completes.
        /// </summary>
        event EventHandler<BuildCompletedEventArgs> BuildCompleted;
    }

    /// <summary>
    /// Represents the current status of a build operation.
    /// </summary>
    public class BuildStatus
    {
        public bool IsBuilding { get; set; }
        public string Configuration { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public DateTime? BuildStartTime { get; set; }
        public int SuccessfulProjects { get; set; }
        public int FailedProjects { get; set; }
        public string CurrentBuildStep { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool IsSuccessful { get; set; }
    }

    /// <summary>
    /// Event arguments for build progress updates.
    /// </summary>
    public class BuildProgressEventArgs : EventArgs
    {
        public int ProgressPercentage { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public string CurrentProject { get; set; } = string.Empty;
        public bool CanCancel { get; set; }
    }

    /// <summary>
    /// Event arguments for build completion.
    /// </summary>
    public class BuildCompletedEventArgs : EventArgs
    {
        public bool Succeeded { get; set; }
        public TimeSpan BuildTime { get; set; }
        public string BuildSummary { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public bool WasCancelled { get; set; }
    }
}
