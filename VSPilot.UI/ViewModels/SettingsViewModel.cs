using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using VSPilot.Common.Commands;
using VSPilot.Common.Models;
using VSPilot.Common.ViewModels;
using VSPilot.Core.Services;

namespace VSPilot.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ConfigurationService _configService;
        private readonly GitHubCopilotService _copilotService;
        private readonly ILogger<SettingsViewModel> _logger;

        private VSPilotSettings _settings;
        private bool _hasUnsavedChanges;
        private bool _isLoading;
        private bool _isLoaded;
        private string _copilotStatus = "Unknown (service not detected)";
        private bool _isCopilotLoginEnabled;

        public VSPilotSettings Settings
        {
            get => _settings;
            private set
            {
                LogExtended("SettingsViewModel: Setting new Settings value");
                if (SetProperty(ref _settings, value))
                {
                    if (!_isLoading)
                    {
                        HasUnsavedChanges = true;
                        LogExtended("SettingsViewModel: Marked as unsaved due to change in Settings");
                    }

                    // Subscribe to settings property changes
                    if (_settings != null)
                    {
                        _settings.PropertyChanged += Settings_PropertyChanged;
                    }
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                LogExtended($"SettingsViewModel: HasUnsavedChanges set to {value}");
                _ = SetProperty(ref _hasUnsavedChanges, value);
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                LogExtended($"SettingsViewModel: IsLoading set to {value}");
                _ = SetProperty(ref _isLoading, value);
            }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            private set
            {
                LogExtended($"SettingsViewModel: IsLoaded set to {value}");
                _ = SetProperty(ref _isLoaded, value);
            }
        }

        public string CopilotStatus
        {
            get => _copilotStatus;
            private set
            {
                LogExtended($"SettingsViewModel: CopilotStatus set to {value}");
                _ = SetProperty(ref _copilotStatus, value);
            }
        }

        public bool IsCopilotLoginEnabled
        {
            get => _isCopilotLoginEnabled;
            private set
            {
                LogExtended($"SettingsViewModel: IsCopilotLoginEnabled set to {value}");
                _ = SetProperty(ref _isCopilotLoginEnabled, value);
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand LogInToCopilotCommand { get; private set; }
        public ICommand OpenApiDocsCommand { get; private set; }

        public event EventHandler<string>? ErrorOccurred;

        public SettingsViewModel(
            ConfigurationService configService,
            GitHubCopilotService copilotService,
            ILogger<SettingsViewModel> logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            LogExtended("SettingsViewModel: Initializing default settings");
            _settings = new VSPilotSettings();

            LogExtended("SettingsViewModel: Creating commands");
            SaveCommand = new RelayCommand(async () => await SaveSettingsAsync(), CanSaveSettings);
            ResetCommand = new RelayCommand(async () => await ResetSettingsAsync(), CanResetSettings);
            LogInToCopilotCommand = new RelayCommand(LogInToCopilotAsync, () => IsCopilotLoginEnabled);
            OpenApiDocsCommand = new RelayCommand(OpenApiDocsAsync);

            LogExtended("SettingsViewModel: Starting asynchronous settings initialization");
            _ = InitializeSettingsAsync();
        }

        private bool CanSaveSettings()
        {
            return HasUnsavedChanges && !IsLoading;
        }

        private bool CanResetSettings()
        {
            return !IsLoading;
        }

        private async Task InitializeSettingsAsync()
        {
            LogExtended("SettingsViewModel: InitializeSettingsAsync started");
            try
            {
                IsLoading = true;
                _logger.LogInformation("Loading settings asynchronously.");
                VSPilotSettings loadedSettings = await _configService.LoadSettingsAsync();
                LogExtended("SettingsViewModel: Settings loaded from configuration service");
                Settings = loadedSettings;
                HasUnsavedChanges = false;

                // Check GitHub Copilot status
                await CheckCopilotStatusAsync();

                IsLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load initial settings");
                LogExtended($"SettingsViewModel: Exception while loading settings: {ex.Message}");
                OnError($"Failed to load settings: {ex.Message}");
                Settings = new VSPilotSettings();
                IsLoaded = true;
            }
            finally
            {
                IsLoading = false;
                LogExtended("SettingsViewModel: InitializeSettingsAsync completed");
            }
        }

        private async Task CheckCopilotStatusAsync()
        {
            LogExtended("SettingsViewModel: CheckCopilotStatusAsync started");
            try
            {
                bool isInstalled = await _copilotService.IsCopilotInstalledAsync();
                if (!isInstalled)
                {
                    CopilotStatus = "Not installed";
                    IsCopilotLoginEnabled = false;
                    return;
                }

                bool isLoggedIn = await _copilotService.IsCopilotLoggedInAsync();
                if (isLoggedIn)
                {
                    CopilotStatus = "Authenticated";
                    IsCopilotLoginEnabled = false;
                }
                else
                {
                    CopilotStatus = "Not authenticated";
                    IsCopilotLoginEnabled = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check Copilot status");
                LogExtended($"SettingsViewModel: Exception while checking Copilot status: {ex.Message}");
                CopilotStatus = $"Error: {ex.Message}";
                IsCopilotLoginEnabled = false;
            }
            finally
            {
                LogExtended("SettingsViewModel: CheckCopilotStatusAsync completed");
            }
        }

        public async Task LogInToCopilotAsync()
        {
            LogExtended("SettingsViewModel: LogInToCopilot started");
            try
            {
                _copilotService.OpenCopilotLoginPage();
                LogExtended("SettingsViewModel: Opened Copilot login page");

                // Schedule a status check after a delay to see if login was successful
                _ = Task.Delay(5000).ContinueWith(async _ =>
                {
                    await CheckCopilotStatusAsync();
                }, System.Threading.Tasks.TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open Copilot login page");
                LogExtended($"SettingsViewModel: Exception while opening Copilot login page: {ex.Message}");
                OnError($"Failed to open GitHub Copilot login page: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        public async Task OpenApiDocsAsync()
        {
            LogExtended("SettingsViewModel: OpenApiDocs started");
            try
            {
                // Open documentation for API keys
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/DannyJanmaat/VSPilot/wiki/API-Keys",
                    UseShellExecute = true
                });
                LogExtended("SettingsViewModel: Opened API documentation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open API documentation");
                LogExtended($"SettingsViewModel: Exception while opening API documentation: {ex.Message}");
                OnError($"Failed to open API documentation: {ex.Message}");
            }
            await Task.CompletedTask;
        }

        private async Task SaveSettingsAsync()
        {
            LogExtended("SettingsViewModel: SaveSettingsAsync started");
            try
            {
                _logger.LogInformation("Saving settings.");

                // Validate API keys if they're being used
                if (Settings.SelectedAIProvider == AIProvider.OpenAI &&
                    !string.IsNullOrWhiteSpace(Settings.OpenAIApiKey))
                {
                    // TODO: Add validation for OpenAI API key
                }

                if (Settings.SelectedAIProvider == AIProvider.Anthropic &&
                    !string.IsNullOrWhiteSpace(Settings.AnthropicApiKey))
                {
                    // TODO: Add validation for Anthropic API key
                }

                await _configService.SaveSettingsAsync(Settings);
                HasUnsavedChanges = false;
                _logger.LogInformation("Settings saved successfully");
                LogExtended("SettingsViewModel: Settings saved successfully");

                // Refresh Copilot status after saving
                await CheckCopilotStatusAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                LogExtended($"SettingsViewModel: Exception while saving settings: {ex.Message}");
                OnError($"Failed to save settings: {ex.Message}");
            }
            finally
            {
                LogExtended("SettingsViewModel: SaveSettingsAsync completed");
            }
        }

        private async Task ResetSettingsAsync()
        {
            LogExtended("SettingsViewModel: ResetSettingsAsync started");
            try
            {
                LogExtended("SettingsViewModel: Creating default settings instance");
                Settings = new VSPilotSettings();
                await SaveSettingsAsync();
                _logger.LogInformation("Settings reset to defaults");
                LogExtended("SettingsViewModel: Settings reset to defaults");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset settings");
                LogExtended($"SettingsViewModel: Exception while resetting settings: {ex.Message}");
                OnError($"Failed to reset settings: {ex.Message}");
            }
            finally
            {
                LogExtended("SettingsViewModel: ResetSettingsAsync completed");
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!_isLoading)
            {
                HasUnsavedChanges = true;
                LogExtended($"SettingsViewModel: Settings property '{e.PropertyName}' changed, marked as unsaved");
            }
        }

        public void NotifySettingsChanged()
        {
            if (!_isLoading)
            {
                HasUnsavedChanges = true;
                LogExtended("SettingsViewModel: Settings changed via external notification");
            }
        }

        private void OnError(string message)
        {
            _logger.LogWarning(message);
            LogExtended($"SettingsViewModel: OnError raised with message: {message}");
            ErrorOccurred?.Invoke(this, message);
        }

        // Extended logging helper that writes to both Debug output and Console.
        private void LogExtended(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
        }
    }
}
