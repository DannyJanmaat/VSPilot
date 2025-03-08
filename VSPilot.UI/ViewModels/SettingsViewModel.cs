using System;
using System.Threading.Tasks;
using System.Windows.Input;
using VSPilot.Common.Models;
using VSPilot.Common.Commands;
using VSPilot.Common.ViewModels;
using VSPilot.Core.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace VSPilot.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ConfigurationService _configService;
        private readonly ILogger<SettingsViewModel> _logger;

        private VSPilotSettings _settings;
        private bool _hasUnsavedChanges;
        private bool _isLoading;

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
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                LogExtended($"SettingsViewModel: HasUnsavedChanges set to {value}");
                SetProperty(ref _hasUnsavedChanges, value);
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                LogExtended($"SettingsViewModel: IsLoading set to {value}");
                SetProperty(ref _isLoading, value);
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }

        public event EventHandler<string>? ErrorOccurred;

        public SettingsViewModel(
            ConfigurationService configService,
            ILogger<SettingsViewModel> logger)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            LogExtended("SettingsViewModel: Initializing default settings");
            _settings = new VSPilotSettings();

            LogExtended("SettingsViewModel: Creating commands");
            SaveCommand = new RelayCommand(async () => await SaveSettingsAsync(), CanSaveSettings);
            ResetCommand = new RelayCommand(async () => await ResetSettingsAsync(), CanResetSettings);

            LogExtended("SettingsViewModel: Starting asynchronous settings initialization");
            _ = InitializeSettingsAsync();
        }

        private bool CanSaveSettings() => HasUnsavedChanges && !IsLoading;
        private bool CanResetSettings() => !IsLoading;

        private async Task InitializeSettingsAsync()
        {
            LogExtended("SettingsViewModel: InitializeSettingsAsync started");
            try
            {
                IsLoading = true;
                _logger.LogInformation("Loading settings asynchronously.");
                var loadedSettings = await _configService.LoadSettingsAsync();
                LogExtended("SettingsViewModel: Settings loaded from configuration service");
                Settings = loadedSettings;
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load initial settings");
                LogExtended($"SettingsViewModel: Exception while loading settings: {ex.Message}");
                OnError($"Failed to load settings: {ex.Message}");
                Settings = new VSPilotSettings();
            }
            finally
            {
                IsLoading = false;
                LogExtended("SettingsViewModel: InitializeSettingsAsync completed");
            }
        }

        private async Task SaveSettingsAsync()
        {
            LogExtended("SettingsViewModel: SaveSettingsAsync started");
            try
            {
                _logger.LogInformation("Saving settings.");
                await _configService.SaveSettingsAsync(Settings);
                HasUnsavedChanges = false;
                _logger.LogInformation("Settings saved successfully");
                LogExtended("SettingsViewModel: Settings saved successfully");
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
