using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VSPilot.Common.Exceptions;
using VSPilot.Common.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using VSPilot.Core.Services;

namespace VSPilot.Core.AI
{
    public class VSPilotAIIntegration
    {
        private readonly ILogger<VSPilotAIIntegration> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string OPENAI_API_ENDPOINT = "https://api.openai.com/v1/chat/completions";
        private const string ANTHROPIC_API_ENDPOINT = "https://api.anthropic.com/v1/messages";
        private readonly Queue<string> _analysisQueue = new Queue<string>();
        private bool _isProcessingQueue = false;
        private readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(1, 1);
        private readonly GitHubCopilotService _copilotService;

        // AI provider enum
        private enum AIProvider
        {
            OpenAI,
            Anthropic,
            GitHubCopilot,
            Auto
        }

        public VSPilotAIIntegration(
            ILogger<VSPilotAIIntegration> logger,
            GitHubCopilotService copilotService,
            string apiKey = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
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

                // For project changes, OpenAI is usually best
                string response = await GetAIResponseAsync(prompt, AIProvider.OpenAI);

                // Parse the response into ProjectChanges
                return ParseProjectChanges(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get project changes");
                throw new AutomationException("Failed to get project changes from AI service", ex);
            }
        }

        public async Task<string> GetDirectResponseAsync(string prompt)
        {
            try
            {
                _logger.LogInformation("Getting direct response for chat prompt");

                // For chat responses, use Auto to pick the best provider
                return await GetAIResponseAsync(prompt, AIProvider.Auto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get direct response");
                return $"I encountered an error: {ex.Message}. Please try again with a different query.";
            }
        }

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
                // For project analysis, GitHub Copilot might be best if available
                string prompt = $"Analyze project {projectName} and provide recommendations for improvements.";

                // Get AI analysis
                string analysis = await GetAIResponseAsync(prompt, AIProvider.Auto);

                // Log the analysis results
                _logger.LogInformation("Analysis for project {0}: {1}", projectName, analysis);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze project: {0}", projectName);
                throw;
            }
        }

        private async Task<string> GetAIResponseAsync(string prompt, AIProvider provider = AIProvider.Auto)
        {
            // Determine which provider to use if Auto is specified
            if (provider == AIProvider.Auto)
            {
                provider = DetermineOptimalProvider(prompt);
            }

            // Check if the selected provider is available
            if (!IsProviderAvailable(provider))
            {
                // Fall back to any available provider
                provider = GetFirstAvailableProvider();
            }

            // If no providers are available, return an error message
            if (provider == AIProvider.Auto)
            {
                _logger.LogWarning("No API keys configured");
                return "API key is not configured. Please set the VSPILOT_API_KEY environment variable or configure it in VSPilot Settings.";
            }

            // Call the appropriate provider
            return provider switch
            {
                AIProvider.OpenAI => await GetOpenAIResponseAsync(prompt),
                AIProvider.Anthropic => await GetAnthropicResponseAsync(prompt),
                AIProvider.GitHubCopilot => await GetCopilotResponseAsync(prompt),
                _ => "No AI provider available. Please configure an API key in VSPilot Settings."
            };
        }

        private AIProvider DetermineOptimalProvider(string prompt)
        {
            // Analyze the prompt to determine the best provider
            // This is a simple implementation - you could make this more sophisticated

            // For code generation, GitHub Copilot might be best
            if (prompt.Contains("generate code") || prompt.Contains("write a function") ||
                prompt.Contains("create a class") || prompt.Contains("implement"))
            {
                if (IsProviderAvailable(AIProvider.GitHubCopilot))
                    return AIProvider.GitHubCopilot;
            }

            // For complex reasoning, Anthropic might be best
            if (prompt.Contains("explain") || prompt.Contains("why") ||
                prompt.Contains("how does") || prompt.Contains("analyze"))
            {
                if (IsProviderAvailable(AIProvider.Anthropic))
                    return AIProvider.Anthropic;
            }

            // Default to OpenAI for general queries
            if (IsProviderAvailable(AIProvider.OpenAI))
                return AIProvider.OpenAI;

            // If no specific provider is determined or available, return Auto
            // to let the fallback logic handle it
            return AIProvider.Auto;
        }

        private bool IsProviderAvailable(AIProvider provider)
        {
            switch (provider)
            {
                case AIProvider.OpenAI:
                    return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VSPILOT_API_KEY"));
                case AIProvider.Anthropic:
                    return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
                case AIProvider.GitHubCopilot:
                    // Check if GitHub Copilot is enabled in settings
                    string settingsPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "VSPilot",
                        "settings.txt");

                    if (File.Exists(settingsPath))
                    {
                        string[] lines = File.ReadAllLines(settingsPath);
                        foreach (string line in lines)
                        {
                            if (line.StartsWith("UseGitHubCopilot=true"))
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                default:
                    return false;
            }
        }

        private AIProvider GetFirstAvailableProvider()
        {
            // Check each provider in order of preference
            foreach (var provider in new[] { AIProvider.OpenAI, AIProvider.Anthropic, AIProvider.GitHubCopilot })
            {
                if (IsProviderAvailable(provider))
                    return provider;
            }

            // If no provider is available, return Auto
            return AIProvider.Auto;
        }

        private async Task<string> GetOpenAIResponseAsync(string prompt)
        {
            try
            {
                string apiKey = Environment.GetEnvironmentVariable("VSPILOT_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("OpenAI API key is not set");
                    return "OpenAI API key is not configured.";
                }

                var request = new
                {
                    model = "gpt-4",
                    messages = new[]
                    {
                        new { role = "system", content = "You are a Visual Studio extension helping with code automation and deployment." },
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

                var response = await _httpClient.PostAsync(OPENAI_API_ENDPOINT, content);
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

                return "Failed to parse OpenAI response.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get OpenAI response");
                throw new AutomationException("Failed to get OpenAI response", ex);
            }
        }

        private async Task<string> GetAnthropicResponseAsync(string prompt)
        {
            try
            {
                string apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("Anthropic API key is not set");
                    return "Anthropic API key is not configured.";
                }

                var request = new
                {
                    model = "claude-3-opus-20240229",
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 2000,
                    temperature = 0.7
                };

                // Set the Anthropic API key in the header
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync(ANTHROPIC_API_ENDPOINT, content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var responseObject = JsonSerializer.Deserialize<JsonElement>(responseBody);

                // Extract the message content from the response
                if (responseObject.TryGetProperty("content", out var contentArray) &&
                    contentArray.GetArrayLength() > 0 &&
                    contentArray[0].TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }

                return "Failed to parse Anthropic response.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Anthropic response");
                throw new AutomationException("Failed to get Anthropic response", ex);
            }
            finally
            {
                // Reset the headers for other API calls
                _httpClient.DefaultRequestHeaders.Clear();
            }
        }

        private async Task<string> GetCopilotResponseAsync(string prompt)
        {
            try
            {
                // If the Copilot service is not available, return null to fall back to other providers
                if (_copilotService == null)
                {
                    _logger.LogWarning("GitHub Copilot service is not available");
                    return null;
                }

                // Check if GitHub Copilot is enabled in settings
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VSPilot",
                    "settings.txt");

                bool useCopilot = false;
                if (File.Exists(settingsPath))
                {
                    string[] lines = await Task.Run(() => File.ReadAllLines(settingsPath));
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

                // Check if GitHub Copilot is logged in
                bool isLoggedIn = await _copilotService.IsCopilotLoggedInAsync();
                if (!isLoggedIn)
                {
                    _logger.LogWarning("GitHub Copilot is not logged in");
                    return null; // This will trigger the fallback to other providers
                }

                // Get completion from GitHub Copilot
                return await _copilotService.GetCopilotCompletionAsync(prompt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Copilot response");
                return null;
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
