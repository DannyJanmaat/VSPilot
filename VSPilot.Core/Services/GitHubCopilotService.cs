using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using System.IO;
using VSPilot.Core.AI;
using VSPilot.Common.Models;
using System.Collections.Generic;
using Microsoft.VisualStudio.Shell.Interop;
using System.Reflection;

namespace VSPilot.Core.Services
{
    public class GitHubCopilotService : IAIService
    {
        private readonly ILogger<GitHubCopilotService> _logger;
        private readonly DTE2 _dte;
        private readonly VSPilotSettings _settings;
        private bool? _isCopilotInstalled;
        private bool? _isCopilotLoggedIn;
        private readonly List<ChatMessage> _conversationHistory = new List<ChatMessage>();
        public GitHubCopilotService(ILogger<GitHubCopilotService> logger, DTE2 dte, VSPilotSettings settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<bool> IsCopilotInstalledAsync()
        {
            if (_isCopilotInstalled.HasValue)
            {
                return _isCopilotInstalled.Value;
            }

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Try to get the Copilot service from Visual Studio
                var serviceProvider = new ServiceProvider((Microsoft.VisualStudio.OLE.Interop.IServiceProvider)_dte);

                // Look for Copilot service types
                Type copilotServiceType = null;

                // Try to find the Copilot service type by common names
                string[] possibleTypeNames = new[]
                {
                    "Microsoft.VisualStudio.CopilotService.ICopilotService",
                    "GitHub.VisualStudio.Copilot.ICopilotService",
                    "GitHub.Copilot.VisualStudio.ICopilotService"
                };

                foreach (var typeName in possibleTypeNames)
                {
                    try
                    {
                        copilotServiceType = Type.GetType(typeName, false);
                        if (copilotServiceType != null)
                            break;

                        // Try to load from loaded assemblies
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                var type = assembly.GetType(typeName, false);
                                if (type != null)
                                {
                                    copilotServiceType = type;
                                    break;
                                }
                            }
                            catch
                            {
                                // Ignore assembly load errors
                            }
                        }

                        if (copilotServiceType != null)
                            break;
                    }
                    catch
                    {
                        // Ignore type load errors
                    }
                }

                // If we found a type, try to get the service
                if (copilotServiceType != null)
                {
                    try
                    {
                        var service = serviceProvider.GetService(copilotServiceType);
                        _isCopilotInstalled = service != null;
                        _logger.LogInformation($"GitHub Copilot service {(_isCopilotInstalled.Value ? "found" : "not found")}");
                        return _isCopilotInstalled.Value;
                    }
                    catch
                    {
                        // Fallback to file-based detection
                    }
                }

                // Fallback: Check for extension files
                string vsExtensionsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "VisualStudio");

                // Look for Copilot extension in common VS2022 locations
                bool found = false;
                if (Directory.Exists(vsExtensionsPath))
                {
                    // Look for Copilot extension directories
                    string[] dirs = Directory.GetDirectories(vsExtensionsPath, "*", SearchOption.AllDirectories);
                    foreach (string dir in dirs)
                    {
                        if (dir.Contains("GitHub.Copilot") || dir.Contains("GitHub Copilot"))
                        {
                            found = true;
                            break;
                        }
                    }

                    // Also check for extension manifest files
                    if (!found)
                    {
                        string[] files = Directory.GetFiles(vsExtensionsPath, "extension.vsixmanifest", SearchOption.AllDirectories);
                        foreach (string file in files)
                        {
                            try
                            {
                                string content = File.ReadAllText(file);
                                if (content.Contains("GitHub.Copilot") || content.Contains("GitHub Copilot"))
                                {
                                    found = true;
                                    break;
                                }
                            }
                            catch
                            {
                                // Ignore file read errors
                            }
                        }
                    }
                }

                _isCopilotInstalled = found;
                _logger.LogInformation($"GitHub Copilot extension is {(found ? "installed" : "not installed")}");
                return found;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if GitHub Copilot is installed");
                return false;
            }
        }

        public async Task<bool> IsCopilotLoggedInAsync()
        {
            if (_isCopilotLoggedIn.HasValue)
            {
                return _isCopilotLoggedIn.Value;
            }

            try
            {
                // First check if Copilot is installed
                bool isInstalled = await IsCopilotInstalledAsync();
                if (!isInstalled)
                {
                    _isCopilotLoggedIn = false;
                    return false;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Try to get the Copilot service and check authentication status
                var serviceProvider = new ServiceProvider((Microsoft.VisualStudio.OLE.Interop.IServiceProvider)_dte);

                // Look for Copilot service types
                Type copilotServiceType = null;

                // Try to find the Copilot service type by common names
                string[] possibleTypeNames = new[]
                {
                    "Microsoft.VisualStudio.CopilotService.ICopilotService",
                    "GitHub.VisualStudio.Copilot.ICopilotService",
                    "GitHub.Copilot.VisualStudio.ICopilotService"
                };

                foreach (var typeName in possibleTypeNames)
                {
                    try
                    {
                        copilotServiceType = Type.GetType(typeName, false);
                        if (copilotServiceType != null)
                            break;

                        // Try to load from loaded assemblies
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                var type = assembly.GetType(typeName, false);
                                if (type != null)
                                {
                                    copilotServiceType = type;
                                    break;
                                }
                            }
                            catch
                            {
                                // Ignore assembly load errors
                            }
                        }

                        if (copilotServiceType != null)
                            break;
                    }
                    catch
                    {
                        // Ignore type load errors
                    }
                }

                // If we found a type, try to get the service and check authentication
                if (copilotServiceType != null)
                {
                    try
                    {
                        var service = serviceProvider.GetService(copilotServiceType);
                        if (service != null)
                        {
                            // Try to call IsAuthenticated or similar method using reflection
                            var isAuthenticatedMethod = copilotServiceType.GetMethod("IsAuthenticated") ??
                                                       copilotServiceType.GetMethod("IsAuthenticatedAsync") ??
                                                       copilotServiceType.GetMethod("GetAuthenticationStatus");

                            if (isAuthenticatedMethod != null)
                            {
                                var result = isAuthenticatedMethod.Invoke(service, null);

                                // Handle potential Task return type
                                if (result is Task<bool> taskBool)
                                {
                                    _isCopilotLoggedIn = await taskBool;
                                }
                                else if (result is Task task)
                                {
                                    await task;
                                    var resultProperty = task.GetType().GetProperty("Result");
                                    if (resultProperty != null)
                                    {
                                        var taskResult = resultProperty.GetValue(task);
                                        if (taskResult is bool boolResult)
                                        {
                                            _isCopilotLoggedIn = boolResult;
                                        }
                                    }
                                }
                                else if (result is bool boolResult)
                                {
                                    _isCopilotLoggedIn = boolResult;
                                }
                                else
                                {
                                    // If we can't determine the result, fall back to file-based detection
                                    _isCopilotLoggedIn = null;
                                }

                                if (_isCopilotLoggedIn.HasValue)
                                {
                                    _logger.LogInformation($"GitHub Copilot is {(_isCopilotLoggedIn.Value ? "authenticated" : "not authenticated")}");
                                    return _isCopilotLoggedIn.Value;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Fallback to file-based detection
                    }
                }

                // Fallback: Check for credential files
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string githubPath = Path.Combine(appDataPath, "GitHub");

                if (Directory.Exists(githubPath))
                {
                    // Look for Copilot credential files
                    string[] files = Directory.GetFiles(githubPath, "copilot-*", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        _isCopilotLoggedIn = true;
                        _logger.LogInformation("GitHub Copilot appears to be logged in");
                        return true;
                    }
                }

                _isCopilotLoggedIn = false;
                _logger.LogInformation("GitHub Copilot does not appear to be logged in");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if GitHub Copilot is logged in");
                return false;
            }
        }

        // IAIService implementation
        public async Task<string> GetCompletionAsync(string prompt, bool maintainContext = true)
        {
            try
            {
                bool isLoggedIn = await IsCopilotLoggedInAsync();
                if (!isLoggedIn)
                {
                    _logger.LogWarning("Cannot get Copilot completion: not logged in");
                    return "GitHub Copilot is not authenticated. Please log in to use this service.";
                }

                // Add to conversation history if maintaining context
                if (maintainContext)
                {
                    _conversationHistory.Add(new ChatMessage { Role = "user", Content = prompt });
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Try to use the actual Copilot service if available
                var serviceProvider = new ServiceProvider((Microsoft.VisualStudio.OLE.Interop.IServiceProvider)_dte);

                // Look for Copilot service types
                Type copilotServiceType = null;

                // Try to find the Copilot service type by common names
                string[] possibleTypeNames = new[]
                {
                    "Microsoft.VisualStudio.CopilotService.ICopilotService",
                    "GitHub.VisualStudio.Copilot.ICopilotService",
                    "GitHub.Copilot.VisualStudio.ICopilotService"
                };

                foreach (var typeName in possibleTypeNames)
                {
                    try
                    {
                        copilotServiceType = Type.GetType(typeName, false);
                        if (copilotServiceType != null)
                            break;

                        // Try to load from loaded assemblies
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try
                            {
                                var type = assembly.GetType(typeName, false);
                                if (type != null)
                                {
                                    copilotServiceType = type;
                                    break;
                                }
                            }
                            catch
                            {
                                // Ignore assembly load errors
                            }
                        }

                        if (copilotServiceType != null)
                            break;
                    }
                    catch
                    {
                        // Ignore type load errors
                    }
                }

                // If we found a type, try to get the service and use it
                if (copilotServiceType != null)
                {
                    try
                    {
                        var service = serviceProvider.GetService(copilotServiceType);
                        if (service != null)
                        {
                            // Look for methods to get completions
                            var getChatCompletionMethod = copilotServiceType.GetMethod("GetChatCompletionAsync") ??
                                                         copilotServiceType.GetMethod("GetChatCompletion") ??
                                                         copilotServiceType.GetMethod("ChatAsync") ??
                                                         copilotServiceType.GetMethod("Chat");

                            if (getChatCompletionMethod != null)
                            {
                                // Prepare parameters - this depends on the actual API
                                object[] parameters;

                                // Check parameter count to determine which overload we're using
                                var methodParams = getChatCompletionMethod.GetParameters();
                                if (methodParams.Length == 1)
                                {
                                    // Likely just takes the prompt
                                    parameters = new object[] { prompt };
                                }
                                else if (methodParams.Length == 2 && methodParams[1].ParameterType == typeof(bool))
                                {
                                    // Likely takes prompt and a boolean flag
                                    parameters = new object[] { prompt, maintainContext };
                                }
                                else
                                {
                                    // Unknown parameter structure, try with just the prompt
                                    parameters = new object[] { prompt };
                                }

                                var result = getChatCompletionMethod.Invoke(service, parameters);

                                // Handle potential Task return type
                                string response = null;

                                if (result is Task<string> taskString)
                                {
                                    response = await taskString;
                                }
                                else if (result is Task task)
                                {
                                    await task;
                                    var resultProperty = task.GetType().GetProperty("Result");
                                    if (resultProperty != null)
                                    {
                                        var taskResult = resultProperty.GetValue(task);
                                        response = taskResult?.ToString();
                                    }
                                }
                                else if (result is string stringResult)
                                {
                                    response = stringResult;
                                }

                                if (!string.IsNullOrEmpty(response))
                                {
                                    // Add to conversation history if maintaining context
                                    if (maintainContext)
                                    {
                                        _conversationHistory.Add(new ChatMessage { Role = "assistant", Content = response });
                                    }

                                    return response;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error using GitHub Copilot service directly");
                        // Fall back to simulated response
                    }
                }

                _logger.LogInformation("Getting completion from GitHub Copilot (simulated)");

                // For now, return a simulated response
                string simulatedResponse = $"[GitHub Copilot] Response to: {prompt}\n\nThis is a simulated GitHub Copilot response. In a real implementation, this would use the GitHub Copilot API.";

                // Add to conversation history if maintaining context
                if (maintainContext)
                {
                    _conversationHistory.Add(new ChatMessage { Role = "assistant", Content = simulatedResponse });
                }

                return simulatedResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get GitHub Copilot completion");
                return $"Error getting completion from GitHub Copilot: {ex.Message}";
            }
        }

        public async Task<bool> IsAuthenticatedAsync()
        {
            return await IsCopilotLoggedInAsync();
        }

        public string GetProviderName()
        {
            return "GitHub Copilot";
        }

        public void OpenCopilotLoginPage()
        {
            try
            {
                _logger.LogInformation("Opening GitHub Copilot login page");

                // Try to find and execute the Copilot login command in VS
                ThreadHelper.JoinableTaskFactory.Run(async delegate
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    try
                    {
                        // Try to find the Copilot login command
                        var command = _dte.Commands.Item("GitHub.Copilot.Login") ??
                                     _dte.Commands.Item("GitHub.Copilot.Authenticate") ??
                                     _dte.Commands.Item("GitHub.Copilot.SignIn");

                        if (command != null)
                        {
                            _dte.ExecuteCommand(command.Name);
                            return;
                        }
                    }
                    catch
                    {
                        // Command not found or failed to execute
                    }

                    // Fallback: Open the GitHub Copilot website
                    System.Diagnostics.Process.Start("https://github.com/features/copilot");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open GitHub Copilot login page");
            }
        }

        public void ResetCache()
        {
            _isCopilotInstalled = null;
            _isCopilotLoggedIn = null;
        }

        public void ClearConversationHistory()
        {
            _conversationHistory.Clear();
        }
    }
}
