using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VSPilot.Common.Exceptions;
using VSPilot.Common.Models;
using System.Collections.Generic;
using System.Threading;

namespace VSPilot.Core.AI
{
    public class VSPilotAIIntegration : IVSPilotAIIntegration
    {
        private readonly ILogger<VSPilotAIIntegration> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string API_ENDPOINT = "https://api.openai.com/v1/chat/completions";
        private readonly Queue<string> _analysisQueue = new Queue<string>();
        private bool _isProcessingQueue = false;
        private readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(1, 1);

        public VSPilotAIIntegration(ILogger<VSPilotAIIntegration> logger, string apiKey = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("VSPILOT_API_KEY") ?? string.Empty;

            _httpClient = new HttpClient();

            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            }
        }

        public async Task<ProjectChanges> GetProjectChangesAsync(string prompt)
        {
            try
            {
                _logger.LogInformation("Getting project changes for prompt");

                // Call the AI service to get project changes
                string response = await GetAIResponseAsync(prompt);

                // Parse the response into ProjectChanges
                return ParseProjectChanges(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get project changes");
                throw new AutomationException("Failed to get project changes from AI service", ex);
            }
        }

        // Add this new method for direct chat responses
        public async Task<string> GetDirectResponseAsync(string prompt)
        {
            try
            {
                _logger.LogInformation("Getting direct response for chat prompt");

                // Format the prompt for chat
                string chatPrompt = $"Respond to this user query in the context of a Visual Studio extension for code automation: {prompt}";

                // Get the AI response
                return await GetAIResponseAsync(chatPrompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get direct response");
                return $"I encountered an error: {ex.Message}. Please try again with a different query.";
            }
        }

        // Add the QueueVSPilotProjectAnalysis method
        public void QueueVSPilotProjectAnalysis(string projectName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    _logger.LogWarning("Cannot queue analysis for empty project name");
                    return;
                }

                _logger.LogInformation("Queuing project analysis for: {0}", projectName);

                // Add to queue
                _analysisQueue.Enqueue(projectName);

                // Start processing if not already running
                // Fix for VSTHRD110 - store the task or use FireAndForget extension
                _ = Task.Run(ProcessAnalysisQueueAsync);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue project analysis for {0}", projectName);
            }
        }

        private async Task ProcessAnalysisQueueAsync()
        {
            // Ensure only one instance is processing the queue
            if (_isProcessingQueue)
            {
                return;
            }

            try
            {
                await _queueSemaphore.WaitAsync();

                if (_isProcessingQueue)
                {
                    return;
                }

                _isProcessingQueue = true;
            }
            finally
            {
                _queueSemaphore.Release();
            }

            try
            {
                while (_analysisQueue.Count > 0)
                {
                    string projectName;

                    lock (_analysisQueue)
                    {
                        if (_analysisQueue.Count == 0)
                        {
                            break;
                        }

                        projectName = _analysisQueue.Dequeue();
                    }

                    try
                    {
                        _logger.LogInformation("Processing project analysis for: {0}", projectName);

                        // Perform the analysis
                        await AnalyzeProjectAsync(projectName);

                        _logger.LogInformation("Completed project analysis for: {0}", projectName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error analyzing project: {0}", projectName);
                    }
                }
            }
            finally
            {
                _isProcessingQueue = false;
            }
        }

        private async Task AnalyzeProjectAsync(string projectName)
        {
            try
            {
                // This is a placeholder for the actual project analysis logic
                // In a real implementation, you would analyze the project structure,
                // code quality, potential improvements, etc.

                string prompt = $"Analyze project {projectName} and provide recommendations for improvements.";

                // Get AI analysis
                string analysis = await GetAIResponseAsync(prompt);

                // Log the analysis results
                _logger.LogInformation("Analysis for project {0}: {1}", projectName, analysis);

                // In a real implementation, you might store the analysis results,
                // display them to the user, or take automated actions based on the analysis
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze project: {0}", projectName);
                throw;
            }
        }

        private async Task<string> GetAIResponseAsync(string prompt)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("API key is not set");
                return "API key is not configured. Please set the VSPILOT_API_KEY environment variable.";
            }

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

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync(API_ENDPOINT, content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var responseObject = JsonSerializer.Deserialize<JsonElement>(responseBody);

                // Extract the message content from the response
                if (responseObject.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content_value))
                {
                    return content_value.GetString();
                }

                return "Failed to parse AI response.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get AI response");
                throw new AutomationException("Failed to get AI response", ex);
            }
        }

        private ProjectChanges ParseProjectChanges(string response)
        {
            // This is a simplified implementation
            // In a real implementation, you would parse the AI response to extract
            // file creation and modification instructions

            try
            {
                // For now, just create a basic ProjectChanges object
                return new ProjectChanges
                {
                    NewFiles = new List<FileCreationInfo>(),
                    ModifiedFiles = new List<FileModificationInfo>(),
                    RequiredReferences = new List<string>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse project changes");
                throw new AutomationException("Failed to parse project changes", ex);
            }
        }
    }
}
