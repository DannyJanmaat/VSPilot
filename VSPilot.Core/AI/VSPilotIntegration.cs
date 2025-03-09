using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VSPilot.Common.Exceptions;
using VSPilot.Common.Models;
using VSPilot.Core.Services;

namespace VSPilot.Core.AI
{
    public class VSPilotAIIntegration : IVSPilotAIIntegration
    {
        private readonly ILogger<VSPilotAIIntegration> _logger;
        private readonly HttpClient _httpClient;
        private readonly ConfigurationService _configService;
        private readonly GitHubCopilotService _copilotService;
        private readonly Queue<string> _analysisQueue = new Queue<string>();
        private bool _isProcessingQueue = false;
        private readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(1, 1);
        private VSPilotSettings _settings;
        private readonly List<ChatMessage> _conversationHistory = new List<ChatMessage>();
        private readonly SemaphoreSlim _settingsLock = new SemaphoreSlim(1, 1);

        public VSPilotAIIntegration(
            ILogger<VSPilotAIIntegration> logger,
            ConfigurationService configService,
            GitHubCopilotService copilotService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));

            _httpClient = new HttpClient();

            // Load settings asynchronously
            _ = LoadSettingsAsync();
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                await _settingsLock.WaitAsync();
                _settings = await _configService.LoadSettingsAsync();

                // Configure HTTP client based on settings
                ConfigureHttpClient();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
                _settings = new VSPilotSettings();
            }
            finally
            {
                _ = _settingsLock.Release();
            }
        }

        private void ConfigureHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Clear();

            // We'll set specific headers when making requests to each provider
            // This is just for any common configuration
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<ProjectChanges> GetProjectChangesAsync(string prompt)
        {
            try
            {
                _logger.LogInformation("Getting project changes for prompt");

                // For project changes, we need structured output, so use the best provider for that
                string response = await GetAIResponseAsync(prompt, AIProvider.Auto, true);

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

                // Add to conversation history
                _conversationHistory.Add(new ChatMessage { Role = "user", Content = prompt });

                // For chat responses, use the selected provider or Auto
                string response = await GetAIResponseAsync(prompt, _settings?.SelectedAIProvider ?? AIProvider.Auto);

                // Add response to conversation history
                _conversationHistory.Add(new ChatMessage { Role = "assistant", Content = response });

                return response;
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
                _ = _queueSemaphore.Release();
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

        private async Task<string> GetAIResponseAsync(string prompt, AIProvider provider = AIProvider.Auto, bool requireStructuredOutput = false)
        {
            // Reload settings to ensure we have the latest
            await EnsureSettingsLoadedAsync();

            // Determine which provider to use if Auto is specified
            if (provider == AIProvider.Auto)
            {
                provider = DetermineOptimalProvider(prompt, requireStructuredOutput);
            }

            // Check if the selected provider is available
            if (!await IsProviderAvailableAsync(provider))
            {
                // Fall back to any available provider
                provider = await GetFirstAvailableProviderAsync();
            }

            // If no providers are available, return an error message
            if (provider == AIProvider.Auto)
            {
                _logger.LogWarning("No AI providers configured");
                return "No AI providers are configured. Please configure an API key in VSPilot Settings.";
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

        private async Task EnsureSettingsLoadedAsync()
        {
            if (_settings == null)
            {
                await LoadSettingsAsync();
            }
        }

        private AIProvider DetermineOptimalProvider(string prompt, bool requireStructuredOutput = false)
        {
            // If auto-switching is disabled, use the selected provider
            if (!_settings.AutoSwitchAIProviders && _settings.SelectedAIProvider != AIProvider.Auto)
            {
                return _settings.SelectedAIProvider;
            }

            // If structured output is required, prefer OpenAI
            if (requireStructuredOutput)
            {
                if (IsProviderConfigured(AIProvider.OpenAI))
                {
                    return AIProvider.OpenAI;
                }
            }

            // Analyze the prompt to determine the best provider
            // This is a simple implementation - you could make this more sophisticated

            // For code generation, GitHub Copilot might be best
            if (prompt.Contains("generate code") || prompt.Contains("write a function") ||
                prompt.Contains("create a class") || prompt.Contains("implement"))
            {
                if (IsProviderConfigured(AIProvider.GitHubCopilot))
                {
                    return AIProvider.GitHubCopilot;
                }
            }

            // For complex reasoning, Anthropic might be best
            if (prompt.Contains("explain") || prompt.Contains("why") ||
                prompt.Contains("how does") || prompt.Contains("analyze"))
            {
                if (IsProviderConfigured(AIProvider.Anthropic))
                {
                    return AIProvider.Anthropic;
                }
            }

            // Default to the selected provider if configured
            if (_settings.SelectedAIProvider != AIProvider.Auto &&
                IsProviderConfigured(_settings.SelectedAIProvider))
            {
                return _settings.SelectedAIProvider;
            }

            // Otherwise, check each provider in order of preference
            if (IsProviderConfigured(AIProvider.OpenAI))
            {
                return AIProvider.OpenAI;
            }

            if (IsProviderConfigured(AIProvider.Anthropic))
            {
                return AIProvider.Anthropic;
            }

            if (IsProviderConfigured(AIProvider.GitHubCopilot))
            {
                return AIProvider.GitHubCopilot;
            }

            // If no specific provider is determined or available, return Auto
            // to let the fallback logic handle it
            return AIProvider.Auto;
        }

        private bool IsProviderConfigured(AIProvider provider)
        {
            return _settings != null && provider switch
            {
                AIProvider.OpenAI => !string.IsNullOrEmpty(_settings.OpenAIApiKey),
                AIProvider.Anthropic => !string.IsNullOrEmpty(_settings.AnthropicApiKey),
                AIProvider.GitHubCopilot => _settings.UseGitHubCopilot,
                _ => false
            };
        }

        private async Task<bool> IsProviderAvailableAsync(AIProvider provider)
        {
            if (!IsProviderConfigured(provider))
            {
                return false;
            }

            // For GitHub Copilot, we also need to check if it's logged in
            return provider != AIProvider.GitHubCopilot || await _copilotService.IsCopilotLoggedInAsync();
        }

        private async Task<AIProvider> GetFirstAvailableProviderAsync()
        {
            foreach (AIProvider provider in new[] { AIProvider.OpenAI, AIProvider.Anthropic, AIProvider.GitHubCopilot })
            {
                if (await IsProviderAvailableAsync(provider))
                {
                    return provider;
                }
            }
            return AIProvider.Auto;
        }
        private async Task<string> GetOpenAIResponseAsync(string prompt)
        {
            try
            {
                if (string.IsNullOrEmpty(_settings.OpenAIApiKey))
                {
                    _logger.LogWarning("OpenAI API key is not set");
                    return "OpenAI API key is not configured.";
                }

                // Prepare conversation history for context
                List<object> messages = new List<object>
                {
                    new { role = "system", content = "You are a Visual Studio extension helping with code automation and deployment." }
                };

                // Add conversation history for context (limited to last 10 messages)
                int historyCount = Math.Min(_conversationHistory.Count, 10);
                for (int i = _conversationHistory.Count - historyCount; i < _conversationHistory.Count; i++)
                {
                    ChatMessage historyMessage = _conversationHistory[i];
                    messages.Add(new { role = historyMessage.Role, content = historyMessage.Content });
                }

                // Add the current prompt
                messages.Add(new { role = "user", content = prompt });

                var request = new
                {
                    model = "gpt-4",
                    messages,
                    temperature = 0.7,
                    max_tokens = 2000
                };

                // Set the authorization header
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.OpenAIApiKey);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
                _ = response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                JsonElement responseObject = JsonSerializer.Deserialize<JsonElement>(responseBody);

                // Extract the message content from the response
                return responseObject.TryGetProperty("choices", out JsonElement choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out JsonElement message) &&
                    message.TryGetProperty("content", out JsonElement content_value)
                    ? content_value.GetString()
                    : "Failed to parse OpenAI response.";
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
                if (string.IsNullOrEmpty(_settings.AnthropicApiKey))
                {
                    _logger.LogWarning("Anthropic API key is not set");
                    return "Anthropic API key is not configured.";
                }

                // Prepare conversation history for context
                List<object> messages = new List<object>();

                // Add conversation history for context (limited to last 10 messages)
                int historyCount = Math.Min(_conversationHistory.Count, 10);
                for (int i = _conversationHistory.Count - historyCount; i < _conversationHistory.Count; i++)
                {
                    ChatMessage message = _conversationHistory[i];
                    messages.Add(new { role = message.Role, content = message.Content });
                }

                // Add the current prompt
                messages.Add(new { role = "user", content = prompt });

                var request = new
                {
                    model = "claude-3-opus-20240229",
                    messages,
                    max_tokens = 2000,
                    temperature = 0.7
                };

                // Set the Anthropic API key in the header
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-api-key", _settings.AnthropicApiKey);
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync("https://api.anthropic.com/v1/messages", content);
                _ = response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                JsonElement responseObject = JsonSerializer.Deserialize<JsonElement>(responseBody);

                // Extract the message content from the response
                return responseObject.TryGetProperty("content", out JsonElement contentArray) &&
                    contentArray.GetArrayLength() > 0 &&
                    contentArray[0].TryGetProperty("text", out JsonElement text)
                    ? text.GetString()
                    : "Failed to parse Anthropic response.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Anthropic response");
                throw new AutomationException("Failed to get Anthropic response", ex);
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
                if (!_settings.UseGitHubCopilot)
                {
                    _logger.LogInformation("GitHub Copilot is disabled in settings");
                    return null; // This will trigger the fallback to other providers
                }

                // Check if GitHub Copilot is logged in
                bool isLoggedIn = await _copilotService.IsCopilotLoggedInAsync();
                if (!isLoggedIn)
                {
                    _logger.LogWarning("GitHub Copilot is not logged in");
                    return null; // This will trigger the fallback to other providers
                }

                // Get completion from GitHub Copilot
                string response = await _copilotService.GetCompletionAsync(prompt);

                // If we got a null or empty response, fall back to other providers
                if (string.IsNullOrEmpty(response))
                {
                    _logger.LogWarning("GitHub Copilot returned empty response");
                    return null;
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Copilot response");
                return null; // Fall back to other providers
            }
        }

        private ProjectChanges ParseProjectChanges(string response)
        {
            // This is a simplified implementation
            // In a real implementation, you would parse the AI response to extract
            // file creation and modification instructions

            try
            {
                _logger.LogInformation("Parsing project changes from AI response");

                // Try to parse as JSON first
                try
                {
                    JsonElement responseObject = JsonSerializer.Deserialize<JsonElement>(response);

                    // If we have a valid JSON response, try to extract the project changes
                    ProjectChanges projectChanges = new ProjectChanges
                    {
                        NewFiles = new List<FileCreationInfo>(),
                        ModifiedFiles = new List<FileModificationInfo>(),
                        RequiredReferences = new List<string>()
                    };

                    // Extract new files
                    if (responseObject.TryGetProperty("newFiles", out JsonElement newFiles) &&
                        newFiles.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement file in newFiles.EnumerateArray())
                        {
                            if (file.TryGetProperty("path", out JsonElement path) &&
                                file.TryGetProperty("content", out JsonElement content))
                            {
                                projectChanges.NewFiles.Add(new FileCreationInfo
                                {
                                    Path = path.GetString(),
                                    Content = content.GetString()
                                });
                            }
                        }
                    }

                    // Extract modified files
                    if (responseObject.TryGetProperty("modifiedFiles", out JsonElement modifiedFiles) &&
                        modifiedFiles.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement file in modifiedFiles.EnumerateArray())
                        {
                            if (file.TryGetProperty("path", out JsonElement path) &&
                                file.TryGetProperty("content", out JsonElement content))
                            {
                                projectChanges.ModifiedFiles.Add(new FileModificationInfo
                                {
                                    Path = path.GetString(),
                                    Changes = new List<CodeChange>
                   {
                       new CodeChange { NewContent = content.GetString() }
                   }
                                });
                            }
                        }
                    }

                    // Extract required references
                    if (responseObject.TryGetProperty("requiredReferences", out JsonElement requiredReferences) &&
                        requiredReferences.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement reference in requiredReferences.EnumerateArray())
                        {
                            projectChanges.RequiredReferences.Add(reference.GetString());
                        }
                    }

                    return projectChanges;
                }
                catch (JsonException)
                {
                    _logger.LogInformation("Response is not valid JSON, attempting to parse as text");

                    // If JSON parsing fails, try to extract information from the text
                    ProjectChanges projectChanges = new ProjectChanges
                    {
                        NewFiles = new List<FileCreationInfo>(),
                        ModifiedFiles = new List<FileModificationInfo>(),
                        RequiredReferences = new List<string>()
                    };

                    // Simple text parsing logic - this could be much more sophisticated
                    string[] lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    string currentFilePath = null;
                    StringBuilder currentContent = new StringBuilder();
                    bool isCollectingContent = false;

                    foreach (string line in lines)
                    {
                        // Look for file path indicators
                        if (line.StartsWith("File:") || line.StartsWith("Create file:") ||
                            line.StartsWith("New file:") || line.StartsWith("Path:"))
                        {
                            // If we were collecting content for a previous file, add it
                            if (currentFilePath != null && currentContent.Length > 0)
                            {
                                projectChanges.NewFiles.Add(new FileCreationInfo
                                {
                                    FilePath = currentFilePath,
                                    Content = currentContent.ToString().Trim()
                                });

                                _ = currentContent.Clear();
                            }

                            // Extract the new file path
                            int colonIndex = line.IndexOf(':');
                            if (colonIndex >= 0 && colonIndex < line.Length - 1)
                            {
                                currentFilePath = line.Substring(colonIndex + 1).Trim();
                                isCollectingContent = true;
                            }
                        }
                        // Look for content markers
                        else if (line.StartsWith("```") || line.StartsWith("'''"))
                        {
                            // Toggle content collection
                            isCollectingContent = !isCollectingContent;

                            // If we're starting to collect, clear any previous content
                            if (isCollectingContent)
                            {
                                _ = currentContent.Clear();
                            }
                        }
                        // Collect content if we're in collection mode
                        else if (isCollectingContent && currentFilePath != null)
                        {
                            _ = currentContent.AppendLine(line);
                        }
                    }

                    // Add the last file if we were collecting one
                    if (currentFilePath != null && currentContent.Length > 0)
                    {
                        projectChanges.NewFiles.Add(new FileCreationInfo
                        {
                            Path = currentFilePath,
                            Content = currentContent.ToString().Trim()
                        });
                    }

                    return projectChanges;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse project changes");
                throw new AutomationException("Failed to parse project changes", ex);
            }
        }

        public void ClearConversationHistory()
        {
            _conversationHistory.Clear();
            _logger.LogInformation("Conversation history cleared");
        }

        public async Task RefreshSettingsAsync()
        {
            await LoadSettingsAsync();
            _logger.LogInformation("Settings refreshed");
        }
    }
}
