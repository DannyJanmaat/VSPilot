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

        private async Task<string> GetCopilotResponseAsync(string prompt)
        {
            try
            {
                // Check if GitHub Copilot is enabled
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VSPilot",
                    "settings.txt");

                bool useCopilot = false;
                if (File.Exists(settingsPath))
                {
                    string[] lines = File.ReadAllLines(settingsPath);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("UseGitHubCopilot=true"))
                        {
                            useCopilot = true;
                            break;
                        }
                    }
                }

                if (!useCopilot)
                {
                    return null;
                }

                // This is a placeholder for actual GitHub Copilot integration
                // In a real implementation, you would use the GitHub Copilot API
                // or integrate with the Visual Studio GitHub Copilot extension

                _logger.LogInformation("Using GitHub Copilot for response");

                // For now, just return a message indicating we would use Copilot
                return "GitHub Copilot integration is not yet implemented. This would use the GitHub Copilot API in a real implementation.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Copilot response");
                return null;
            }
        }

        public async Task<string> GetDirectResponseAsync(string prompt)
        {
            try
            {
                _logger.LogInformation("Getting direct response for chat prompt");

                // Try GitHub Copilot first if enabled
                string copilotResponse = await GetCopilotResponseAsync(prompt);
                if (!string.IsNullOrEmpty(copilotResponse))
                {
                    return copilotResponse;
                }

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
            // Check for OpenAI API key
            string apiKey = _apiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable("VSPILOT_API_KEY");
            }

            // Check for Anthropic API key as a fallback
            string anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

            if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(anthropicKey))
            {
                _logger.LogWarning("No API keys configured");
                return "API key is not configured. Please set the VSPILOT_API_KEY environment variable or configure it in VSPilot Settings.";
            }

            try
            {
                // Try OpenAI first if the key is available
                if (!string.IsNullOrEmpty(apiKey))
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

                    // Set the authorization header
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                    var content = new StringContent(
                        JsonSerializer.Serialize(request),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync(API_ENDPOINT, content);
                    if (response.IsSuccessStatusCode)
                    {
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
                    }
                }

                // Try Anthropic if OpenAI failed or key not available
                if (!string.IsNullOrEmpty(anthropicKey))
                {
                    _logger.LogInformation("Using Anthropic API");

                    // This is a placeholder for Anthropic API integration
                    // In a real implementation, you would use the Anthropic API

                    return "Anthropic API integration is not yet implemented. This would use the Anthropic API in a real implementation.";
                }

                return "Failed to get AI response. Please check your API keys.";
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
