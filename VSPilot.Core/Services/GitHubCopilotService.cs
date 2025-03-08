using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using System;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using System.IO;

namespace VSPilot.Core.Services
{
    public class GitHubCopilotService
    {
        private readonly ILogger<GitHubCopilotService> _logger;
        private readonly DTE2 _dte;
        private bool? _isCopilotInstalled;
        private bool? _isCopilotLoggedIn;

        public GitHubCopilotService(ILogger<GitHubCopilotService> logger, DTE2 dte)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
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

                // Check if GitHub Copilot extension is installed using a different approach
                // Instead of using the obsolete AddIns collection, check for extension files
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

                // Check if the user is logged in to GitHub Copilot
                // This is a heuristic approach since there's no official API
                // We'll check for the existence of Copilot credential files

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

        public async Task<string> GetCopilotCompletionAsync(string prompt)
        {
            try
            {
                bool isLoggedIn = await IsCopilotLoggedInAsync();
                if (!isLoggedIn)
                {
                    _logger.LogWarning("Cannot get Copilot completion: not logged in");
                    return null;
                }

                // This is a placeholder for actual GitHub Copilot integration
                // In a real implementation, you would use the GitHub Copilot API
                // or find a way to communicate with the VS extension

                _logger.LogInformation("Getting completion from GitHub Copilot");

                // For now, return a simulated response
                return $"[GitHub Copilot] Response to: {prompt}\n\nThis is a simulated GitHub Copilot response. In a real implementation, this would use the GitHub Copilot API.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get GitHub Copilot completion");
                return null;
            }
        }

        public void OpenCopilotLoginPage()
        {
            try
            {
                // Open the GitHub Copilot login page or trigger the login flow
                // This is a placeholder - in a real implementation, you would
                // find a way to trigger the Copilot login flow

                _logger.LogInformation("Opening GitHub Copilot login page");

                // For now, just open the GitHub Copilot website
                System.Diagnostics.Process.Start("https://github.com/features/copilot");
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
    }
}
