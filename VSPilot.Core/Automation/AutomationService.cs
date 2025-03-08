using EnvDTE;
using EnvDTE80;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSPilot.Common.Exceptions;
using VSPilot.Common.Interfaces;
using VSPilot.Common.Models;
using VSPilot.Core.AI;
using VSPilot.Core.Build;

namespace VSPilot.Core.Automation
{
    public class AutomationService : IDisposable
    {
        private readonly DTE2 _dte;
        private readonly IVsSolution _solution;
        private readonly IProjectManager _projectManager;
        private readonly IBuildManager _buildManager;
        private readonly IErrorHandler _errorHandler;
        private readonly AIRequestHandler _aiHandler;
        private readonly VSPilotAIIntegration _aiIntegration;
        private readonly ILogger<AutomationService> _logger;
        private bool _disposed;

        public AutomationService(
            DTE2 dte,
            IVsSolution solution,
            IProjectManager projectManager,
            IBuildManager buildManager,
            IErrorHandler errorHandler,
            AIRequestHandler aiHandler,
            VSPilotAIIntegration aiIntegration,
            ILogger<AutomationService> logger)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _solution = solution ?? throw new ArgumentNullException(nameof(solution));
            _projectManager = projectManager ?? throw new ArgumentNullException(nameof(projectManager));
            _buildManager = buildManager ?? throw new ArgumentNullException(nameof(buildManager));
            _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            _aiHandler = aiHandler ?? throw new ArgumentNullException(nameof(aiHandler));
            _aiIntegration = aiIntegration ?? throw new ArgumentNullException(nameof(aiIntegration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("AutomationService initialized successfully");
        }

        // New method that returns a string response for the chat window
        public async Task<string> GetChatResponseAsync(string request)
        {
            if (string.IsNullOrWhiteSpace(request))
            {
                return "Please provide a valid request.";
            }

            try
            {
                // Get a direct response from the AI for chat purposes
                var response = await _aiIntegration.GetDirectResponseAsync(request);

                if (!string.IsNullOrEmpty(response))
                {
                    return response;
                }

                // Fallback to a generic response if AI integration fails
                return "I've processed your request. If you'd like me to make changes to your project, please provide more specific instructions.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get chat response");
                return $"I encountered an error processing your request: {ex.Message}";
            }
        }

        public async Task ProcessRequestAsync(string request)
        {
            if (string.IsNullOrWhiteSpace(request))
            {
                throw new ArgumentException("Request cannot be empty", nameof(request));
            }

            _logger.LogInformation("Processing request: {0}", request);

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // 1. Analyze request and plan changes using AIRequestHandler
                var changes = await _aiHandler.AnalyzeRequestAsync(request);
                _logger.LogInformation("Request analyzed, applying changes");

                // 2. Make project changes
                await _projectManager.ApplyChangesAsync(changes, CancellationToken.None);
                _logger.LogInformation("Changes applied successfully");

                // 3. Build and handle errors
                bool buildSuccess = await BuildAndResolveErrorsAsync();
                _logger.LogInformation("Build completed with success: {0}", buildSuccess);

                // 4. Run tests if required
                if (changes.References?.Count > 0)
                {
                    bool testsSuccess = await RunTestsAsync();
                    _logger.LogInformation("Tests completed with success: {0}", testsSuccess);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process request");
                throw new AutomationException($"Failed to process request: {ex.Message}", ex);
            }
        }

        private async Task<bool> BuildAndResolveErrorsAsync()
        {
            bool buildSuccess = false;
            int attempts = 0;
            const int maxAttempts = 3;

            while (!buildSuccess && attempts < maxAttempts)
            {
                attempts++;
                _logger.LogInformation("Build attempt {0}/{1}", attempts, maxAttempts);

                try
                {
                    await _buildManager.CleanSolutionAsync();
                    buildSuccess = await _buildManager.BuildSolutionAsync();

                    if (!buildSuccess)
                    {
                        var errors = await _errorHandler.GetBuildErrorsAsync();
                        _logger.LogWarning("Build failed with {0} errors", errors.Count());

                        // Try to resolve errors using AIRequestHandler
                        foreach (var error in errors)
                        {
                            var fixSuggestion = await _aiHandler.GetErrorFixAsync(error);

                            if (!string.IsNullOrEmpty(fixSuggestion))
                            {
                                // Apply the suggested fix
                                var fileModification = new FileModificationInfo
                                {
                                    Path = error.FileName,
                                    Changes = new List<CodeChange>
                                    {
                                        new CodeChange
                                        {
                                            StartLine = error.Line,
                                            EndLine = error.Line,
                                            NewContent = fixSuggestion
                                        }
                                    }
                                };

                                await _projectManager.ApplyChangesAsync(new ProjectChangeRequest
                                {
                                    FilesToModify = new List<FileModificationInfo> { fileModification }
                                }, CancellationToken.None);

                                _logger.LogInformation("Applied fix for error in file: {0}", error.FileName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during build attempt {0}", attempts);

                    if (attempts == maxAttempts)
                    {
                        throw new AutomationException("Failed to resolve build errors after maximum attempts", ex);
                    }
                }
            }

            return buildSuccess;
        }

        private async Task<bool> RunTestsAsync()
        {
            try
            {
                _logger.LogInformation("Running tests");
                var testSuccess = await _buildManager.RunTestsAsync();

                if (!testSuccess)
                {
                    _logger.LogWarning("Tests failed after automation changes");
                }

                return testSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test execution failed");
                return false;
            }
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
                    (_buildManager as IDisposable)?.Dispose();
                    (_projectManager as IDisposable)?.Dispose();
                    (_errorHandler as IDisposable)?.Dispose();
                    _logger.LogInformation("AutomationService disposed");
                }
                _disposed = true;
            }
        }
    }
}
