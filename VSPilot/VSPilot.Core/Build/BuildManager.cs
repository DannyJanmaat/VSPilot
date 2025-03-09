using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.Extensions.Logging;
using EnvDTE;
using EnvDTE80;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using VSPilot.Common.Interfaces;
using VSPilot.Common.Models;
using VSPilot.Core.Build;
using VSPilot.Core.AI;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VSPilot.Core.Build
{
    public class BuildManager : IBuildManager, IDisposable
    {
        private readonly DTE2 _dte;
        private readonly ILogger<BuildManager> _logger;
        private readonly IVsSolution _solution;
        private readonly ITestPlatform _testPlatform;
        private readonly VSPilotAIIntegration _aiIntegration;
        private DateTime _buildStartTime;
        private bool _disposed;

        public event EventHandler<BuildProgressEventArgs>? BuildProgressUpdated;
        public event EventHandler<BuildCompletedEventArgs>? BuildCompleted;

        public BuildManager(
            DTE2 dte,
            ILogger<BuildManager> logger,
            IVsSolution solution,
            ITestPlatform testPlatform,
            VSPilotAIIntegration aiIntegration)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _testPlatform = testPlatform ?? throw new ArgumentNullException(nameof(testPlatform));
            _aiIntegration = aiIntegration ?? throw new ArgumentNullException(nameof(aiIntegration));
        }

        public async Task<bool> BuildSolutionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Check if a solution is open
                object isOpenObj = null;
                int hr = _solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen, out isOpenObj);
                bool isSolutionOpen = hr == 0 && isOpenObj is bool isOpen && isOpen;

                if (!isSolutionOpen)
                {
                    _logger.LogWarning("No solution is currently open. Cannot build.");
                    OnBuildCompleted(new BuildStatus
                    {
                        ErrorMessage = "No solution is currently open",
                        IsSuccessful = false
                    });
                    return false;
                }

                _buildStartTime = DateTime.Now;

                _logger.LogInformation("Starting solution build");
                OnBuildProgressUpdated(0, "Starting build...");

                // Save all documents
                _dte.Documents.SaveAll();

                // Clean solution first
                await CleanSolutionAsync(cancellationToken);

                // Start the build
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                _dte.Solution.SolutionBuild.Build(true);

                // Monitor build progress
                var buildStatus = await MonitorBuildProgressAsync(cancellationToken);

                // If build fails, attempt AI-assisted error resolution
                if (!buildStatus.IsSuccessful)
                {
                    buildStatus = await TryAIAssistedBuildRepairAsync(buildStatus, cancellationToken);
                }

                OnBuildCompleted(buildStatus);
                return buildStatus.IsSuccessful;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Build failed");
                OnBuildCompleted(new BuildStatus { ErrorMessage = ex.Message });
                return false;
            }
        }


        private async Task<BuildStatus> TryAIAssistedBuildRepairAsync(BuildStatus currentStatus, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogWarning("Build failed. Attempting AI-assisted error resolution.");

                // Ensure we are on the main thread before accessing DTE2 properties
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Retrieve build errors
                var errorItems = _dte.ToolWindows.ErrorList.ErrorItems;
                var repairAttempts = 0;
                const int MaxRepairAttempts = 3;

                while (!currentStatus.IsSuccessful && repairAttempts < MaxRepairAttempts)
                {
                    for (int i = 1; i <= errorItems.Count; i++)
                    {
                        var error = errorItems.Item(i);
                        var aiFixSuggestion = await GetErrorSolutionAsync(
                            new VSPilotErrorItem(
                                error.Description,
                                error.FileName,
                                error.Line,
                                error.Column
                            )
                        );

                        if (!string.IsNullOrEmpty(aiFixSuggestion))
                        {
                            // Attempt to apply AI-suggested fix
                            _logger.LogInformation($"Attempting AI fix for error: {error.Description}");
                            // Apply fix logic would be implemented here
                        }
                    }

                    // Retry build after applying fixes
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                    _dte.Solution.SolutionBuild.Build(true);
                    currentStatus = await MonitorBuildProgressAsync(cancellationToken);
                    repairAttempts++;
                }

                return currentStatus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI-assisted build repair failed");
                return currentStatus;
            }
        }


        private async Task<string> GetErrorSolutionAsync(VSPilotErrorItem errorItem)
        {
            // Implement the logic to get the error solution using AI
            // For now, return a dummy task
            return await Task.FromResult("Dummy AI fix suggestion");
        }

        public async Task<bool> RunTestsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                _logger.LogInformation("Starting test execution");

                var operation = await _testPlatform.CreateTestOperationAsync();
                if (operation == null)
                {
                    throw new InvalidOperationException("Failed to create test operation");
                }

                var results = new List<TestResult>();
                operation.TestResultsUpdated += (sender, args) =>
                {
                    results.AddRange(args.Results);
                    UpdateTestProgress(results);
                };

                await operation.RunAsync();

                var success = AnalyzeTestResults(results);
                _logger.LogInformation($"Test run completed. Success: {success}");
                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test execution failed");
                return false;
            }
        }

        public async Task CleanSolutionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Check if a solution is open
                object isOpenObj = null;
                int hr = _solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen, out isOpenObj);
                bool isSolutionOpen = hr == 0 && isOpenObj is bool isOpen && isOpen;

                if (!isSolutionOpen)
                {
                    _logger.LogWarning("No solution is currently open. Skipping solution clean.");
                    return;
                }

                _logger.LogInformation("Cleaning solution");

                _dte.Solution.SolutionBuild.Clean(true);
                while (_dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Solution clean failed");
                throw;
            }
        }


        public async Task<bool> BuildProjectAsync(
            string projectName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Check if a solution is open
                object isOpenObj = null;
                int hr = _solution.GetProperty((int)__VSPROPID.VSPROPID_IsSolutionOpen, out isOpenObj);
                bool isSolutionOpen = hr == 0 && isOpenObj is bool isOpen && isOpen;

                if (!isSolutionOpen)
                {
                    _logger.LogWarning($"No solution is currently open. Cannot build project: {projectName}");
                    return false;
                }

                _logger.LogInformation($"Building project: {projectName}");

                var project = FindProject(projectName);
                if (project == null)
                {
                    throw new InvalidOperationException($"Project not found: {projectName}");
                }

                project.Save();
                _dte.Solution.SolutionBuild.BuildProject(
                    _dte.Solution.SolutionBuild.ActiveConfiguration.Name,
                    project.UniqueName,
                    true
                );

                return await MonitorProjectBuildAsync(project, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Project build failed: {projectName}");
                return false;
            }
        }


        private Project FindProject(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _dte.Solution.Projects.Cast<Project>()
                .FirstOrDefault(p =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase);
                });
        }

        private async Task<bool> MonitorProjectBuildAsync(Project project, CancellationToken cancellationToken)
        {
            // Implement project build monitoring logic
            // Similar to solution build monitoring, but focused on a single project
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var solutionBuild = _dte.Solution.SolutionBuild;
                while (solutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
                {
                    await Task.Delay(100, cancellationToken);
                }

                return solutionBuild.LastBuildInfo == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Project build monitoring failed: {project.Name}");
                return false;
            }
        }

        public async Task<BuildStatus> GetBuildStatusAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var solutionBuild = _dte.Solution.SolutionBuild;
            var activeConfig = solutionBuild.ActiveConfiguration;

            return new BuildStatus
            {
                IsBuilding = solutionBuild.BuildState == vsBuildState.vsBuildStateInProgress,
                Configuration = activeConfig.Name,
                Platform = GetPlatformName(activeConfig),
                BuildStartTime = _buildStartTime,
                SuccessfulProjects = GetSuccessfulProjectCount(solutionBuild),
                FailedProjects = solutionBuild.LastBuildInfo,
                CurrentBuildStep = solutionBuild.BuildState.ToString()
            };
        }

        private string GetPlatformName(SolutionConfiguration configuration)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                // SolutionConfiguration may not have PlatformName property directly
                // Try to extract from configuration name (usually in format "Debug|AnyCPU")
                var configName = configuration.Name;

                // Extract platform from configuration name
                if (configName.Contains("|"))
                {
                    return configName.Split('|')[1];
                }

                return "AnyCPU"; // Default platform if not specified
            }
            catch
            {
                return "Unknown";
            }
        }

        private int GetSuccessfulProjectCount(SolutionBuild solutionBuild)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int successCount = 0;
            foreach (Project project in _dte.Solution.Projects)
            {
                try
                {
                    // Check if the project has been built successfully
                    // Use a different approach to check build state
                    if (solutionBuild.BuildState != vsBuildState.vsBuildStateInProgress &&
                        !solutionBuild.LastBuildInfo.ToString().Contains(project.UniqueName))
                    {
                        successCount++;
                    }
                }
                catch
                {
                    // Skip this project if there's an error accessing its configuration
                    continue;
                }
            }
            return successCount;
        }

        private async Task<BuildStatus> MonitorBuildProgressAsync(CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var solutionBuild = _dte.Solution.SolutionBuild;
            var totalProjects = GetTotalProjectCount();
            var status = new BuildStatus { BuildStartTime = _buildStartTime };

            while (solutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
            {
                await Task.Delay(100, cancellationToken);

                var projectsDone = GetSuccessfulProjectCount(solutionBuild);
                var progress = (int)((double)projectsDone / totalProjects * 100);

                OnBuildProgressUpdated(progress, $"Building... ({projectsDone}/{totalProjects} projects)");
            }

            status.IsSuccessful = solutionBuild.LastBuildInfo == 0;
            status.SuccessfulProjects = GetSuccessfulProjectCount(solutionBuild);
            status.FailedProjects = solutionBuild.LastBuildInfo;

            return status;
        }

        private int GetTotalProjectCount()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int count = 0;
            foreach (Project project in _dte.Solution.Projects)
            {
                count++;
            }
            return count;
        }

        private void UpdateTestProgress(IList<TestResult> results)
        {
            var passed = results.Count(r => r.Outcome.ToString() == UnitTestOutcome.Passed.ToString());
            var failed = results.Count(r => r.Outcome.ToString() == UnitTestOutcome.Failed.ToString());
            var total = results.Count;

            OnBuildProgressUpdated(
                (int)((double)(passed + failed) / total * 100),
                $"Running tests... (Passed: {passed}, Failed: {failed}, Total: {total})"
            );
        }

        private bool AnalyzeTestResults(IList<TestResult> results)
        {
            if (!results.Any()) return false;

            var passed = results.Count(r => r.Outcome.ToString() == UnitTestOutcome.Passed.ToString());
            var failed = results.Count(r => r.Outcome.ToString() == UnitTestOutcome.Failed.ToString());
            var total = results.Count;

            foreach (var failure in results.Where(r => r.Outcome.ToString() == UnitTestOutcome.Failed.ToString()))
            {
                _logger.LogError($"Test failed: {failure.DisplayName}\nError: {failure.TestFailureException?.Message}");
            }

            return failed == 0;
        }

        private void OnBuildProgressUpdated(int progress, string currentOperation)
        {
            BuildProgressUpdated?.Invoke(this, new BuildProgressEventArgs
            {
                ProgressPercentage = progress,
                CurrentOperation = currentOperation,
                CanCancel = true
            });
        }

        private void OnBuildCompleted(BuildStatus status)
        {
            BuildCompleted?.Invoke(this, new BuildCompletedEventArgs
            {
                Succeeded = status.IsSuccessful,
                BuildTime = DateTime.Now - _buildStartTime,
                BuildSummary = $"Build {(status.IsSuccessful ? "succeeded" : "failed")}. " +
                             $"Successful projects: {status.SuccessfulProjects}, " +
                             $"Failed projects: {status.FailedProjects}",
                ErrorMessage = status.ErrorMessage
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cleanup any managed resources
                }
                _disposed = true;
            }
        }
    }
}