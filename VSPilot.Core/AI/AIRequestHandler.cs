using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.Extensions.Logging;
using VSPilot.Common.Models;
using VSPilot.Common.Exceptions;
using System.IO;
using System.Text.Json;
using System.Net.Http;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;
using VSPilot.Core.Services;
using System.IO.Packaging;

namespace VSPilot.Core.AI
{
    public class AIRequestHandler
    {
        private readonly VSPilotAIIntegration _aiIntegration;
        private readonly VSPilotAIIntegration _integration; // Added to match your constructor
        private readonly ILogger<AIRequestHandler> _logger;
        private readonly LanguageProcessor _languageProcessor;
        private readonly HttpClient _httpClient;
        private readonly DTE2 _dte; // Added for DTE service
        private AsyncPackage _package; // Added for AsyncPackage service
        private readonly ILogger<LanguageProcessor> _languageProcessorLogger; // Added for language processor logger
        private string _apiKey; // Added for API key
        private const string API_ENDPOINT = "https://api.openai.com/v1/completions";

        public AIRequestHandler(ILogger<AIRequestHandler> logger, VSPilotAIIntegration aiIntegration)
        {
            // Validate parameters
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _integration = aiIntegration ?? throw new ArgumentNullException(nameof(aiIntegration));
            _aiIntegration = aiIntegration; // Set this field too for consistency

            ThreadHelper.ThrowIfNotOnUIThread();

            // Get DTE service
            _dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;

            // Try to get AsyncPackage service, but don't throw if it's not available
            _package = ServiceProvider.GlobalProvider.GetService(typeof(AsyncPackage)) as AsyncPackage;

            // Set up logger
            _languageProcessorLogger = logger as ILogger<LanguageProcessor>;

            // Don't throw exceptions for missing services, just log warnings
            if (_dte == null)
            {
                _logger.LogWarning("DTE service not available");
            }

            if (_package == null)
            {
                _logger.LogWarning("AsyncPackage service not available");
            }

            // Continue with initialization that doesn't depend on _package

            // Initialize other properties
            _apiKey = GetApiKey();

            // Only proceed with API-dependent initialization if we have a key
            if (!string.IsNullOrEmpty(_apiKey))
            {
                // Initialize API client
                // ...
            }
        }

        public async Task<ProjectChangeRequest> AnalyzeRequestAsync(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest))
            {
                throw new ArgumentException("Request cannot be empty", nameof(userRequest));
            }
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Analyzing request: {Request}", userRequest);

                // Process through language processor
                var processedRequest = await _languageProcessor.ProcessRequestAsync(userRequest);

                // Get context from current solution
                var solutionContext = await GetSolutionContextAsync();

                // Combine processed request with solution context
                var aiPrompt = CombineRequestWithContext(processedRequest, solutionContext);

                // Try to get AI suggestions from VSPilotAIIntegration
                VSPilot.Common.Models.ProjectChanges? changes = null;
                try
                {
                    changes = await _aiIntegration.GetProjectChangesAsync(aiPrompt);
                }
                catch (Exception aiEx)
                {
                    _logger.LogWarning(aiEx, "VSPilotAIIntegration suggestion failed, falling back to OpenAI");
                }

                // If VSPilotAIIntegration fails, use OpenAI
                if (changes == null)
                {
                    var aiResponse = await GetAIResponseAsync(aiPrompt);
                    changes = await ParseAIResponseAsync(aiResponse);
                }

                // Validate and enhance changes
                var projectRequest = CreateProjectChangeRequest(changes);

                _logger.LogInformation("Request analysis complete. Files to create: {CreateCount}, Files to modify: {ModifyCount}",
                    projectRequest.FilesToCreate?.Count ?? 0,
                    projectRequest.FilesToModify?.Count ?? 0);

                return projectRequest;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze request: {Request}", userRequest);
                throw new AutomationException($"Failed to analyze request: {ex.Message}", ex);
            }
        }

        private async Task<VSPilot.Common.Models.ProjectChanges> ParseAIResponseAsync(string aiResponse)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Basic implementation of parsing AI response
                    var changes = new VSPilot.Common.Models.ProjectChanges
                    {
                        NewFiles = new List<FileCreationInfo>(),
                        ModifiedFiles = new List<FileModificationInfo>(),
                        RequiredReferences = new List<string>()
                    };

                    // Basic parsing logic - you might want to implement more sophisticated parsing
                    // This is a placeholder implementation
                    if (!string.IsNullOrWhiteSpace(aiResponse))
                    {
                        // Example: If response contains specific markers or JSON
                        if (aiResponse.Contains("new file"))
                        {
                            changes.NewFiles.Add(new FileCreationInfo
                            {
                                Path = "Generated/NewFile.cs",
                                Content = "// Generated content",
                                Overwrite = true
                            });
                        }

                        if (aiResponse.Contains("modify"))
                        {
                            changes.ModifiedFiles.Add(new FileModificationInfo
                            {
                                Path = "Existing/File.cs",
                                Changes = new List<CodeChange>
                                {
                                    new CodeChange
                                    {
                                        StartLine = 1,
                                        EndLine = 5,
                                        NewContent = "// Modified content"
                                    }
                                }
                            });
                        }
                    }

                    return changes;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse AI response");
                    throw new AutomationException("Failed to parse AI response", ex);
                }
            });
        }

        private async Task<string> GetAIResponseAsync(string prompt)
        {
            try
            {
                var request = new
                {
                    model = "gpt-4",
                    messages = new[]
                    {
                        new { role = "system", content = "You are a Visual Studio extension helping with code automation." },
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.7,
                    max_tokens = 2000
                };

                var response = await _httpClient.PostAsync(
                    API_ENDPOINT,
                    new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json")
                );

                response.EnsureSuccessStatusCode();
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseObject = JsonSerializer.Deserialize<dynamic>(responseContent);

                return responseObject?.choices?[0]?.message?.content?.ToString() ?? throw new AutomationException("Invalid AI response format");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get AI response");
                throw new AutomationException("Failed to get AI response", ex);
            }
        }

        private string GetApiKey()
        {
            // Implement your API key retrieval logic here
            // For example:
            return ""; // Return an empty string or retrieve from settings
        }

        private string GetApiKeyFromSettings()
        {
            try
            {
                // Read from secure storage or environment
                return Environment.GetEnvironmentVariable("VSPILOT_API_KEY") ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get API key");
                return string.Empty;
            }
        }

        private async Task<string> GetSolutionContextAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // Enhance solution context gathering using VSPilotAIIntegration
            try
            {
                var solution = ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution)) as IVsSolution;
                if (solution != null)
                {
                    var contextBuilder = new System.Text.StringBuilder();
                    contextBuilder.AppendLine($"Solution: {solution.GetSolutionInfo(out _, out _, out _)}");

                    var solutionProjects = await GetProjectsInSolutionAsync(solution);
                    foreach (var project in solutionProjects)
                    {
                        contextBuilder.AppendLine($"Project: {project.Name}");
                        contextBuilder.AppendLine($"Project Type: {project.Kind}");
                    }

                    return contextBuilder.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Detailed solution context gathering failed");
            }

            return "Solution context placeholder";
        }

        private async Task<List<EnvDTE.Project>> GetProjectsInSolutionAsync(IVsSolution solution)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var projects = new List<EnvDTE.Project>();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE;
            if (dte != null)
            {
                foreach (EnvDTE.Project project in dte.Solution.Projects)
                {
                    projects.Add(project);
                }
            }
            return projects;
        }

        private string CombineRequestWithContext(string request, string context)
        {
            return $"Context:\n{context}\n\nRequest:\n{request}";
        }

        public async Task<string> GetErrorFixAsync(VSPilotErrorItem error)
        {
            await Task.CompletedTask; // Or remove async if no async work is needed
                                      // Or use Task.Run if doing CPU-bound work
            return "Error fix suggestion";
        }

        private async Task<string> FormatErrorFixPromptAsync(VSPilotErrorItem error)
        {
            // Get file content for context
            string fileContent;
            using (var reader = new StreamReader(error.FileName))
            {
                fileContent = await reader.ReadToEndAsync();
            }

            return $@"Fix the following compilation error:

File: {error.FileName}
Line: {error.Line}
Column: {error.Column}
Error: {error.Description}

Relevant file content:
{fileContent}

Please provide a specific code fix that addresses this error.";
        }

        private ProjectChangeRequest CreateProjectChangeRequest(VSPilot.Common.Models.ProjectChanges changes)
        {
            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            return new ProjectChangeRequest
            {
                FilesToCreate = changes.NewFiles ?? new List<FileCreationInfo>(),
                FilesToModify = changes.ModifiedFiles ?? new List<FileModificationInfo>(),
                References = changes.RequiredReferences ?? new List<string>(),
                RequiresBuild = true,
                RequiresTests = ShouldRunTests(changes)
            };
        }

        private bool ShouldRunTests(VSPilot.Common.Models.ProjectChanges changes)
        {
            if (changes == null) return false;

            // Run tests if modifying existing files
            if (changes.ModifiedFiles?.Count > 0) return true;

            // Run tests if creating new test files
            return changes.NewFiles?.Exists(f =>
                f.Path.Contains("Test") ||
                f.Path.Contains("test") ||
                f.Content.Contains("[TestClass]") ||
                f.Content.Contains("[Fact]") ||
                f.Content.Contains("[Theory]")) ?? false;
        }
    }
}