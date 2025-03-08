using System;
using System.Threading.Tasks;
using System.Windows.Input;
using VSPilot.Common.Models;
using VSPilot.Common.Commands;
using VSPilot.Common.ViewModels;
using VSPilot.Core.Services;
using Microsoft.Extensions.Logging;

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
                if (SetProperty(ref _settings, value))
                {
                    // Only mark as unsaved if not during initial loading
                    if (!_isLoading)
                    {
                        HasUnsavedChanges = true;
                    }
                }
            }
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => SetProperty(ref _hasUnsavedChanges, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
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

            // Initialize settings with default values
            _settings = new VSPilotSettings();

            // Create commands
            SaveCommand = new RelayCommand(async () => await SaveSettingsAsync(), CanSaveSettings);
            ResetCommand = new RelayCommand(async () => await ResetSettingsAsync(), CanResetSettings);

            // Load initial settings asynchronously
            _ = InitializeSettingsAsync();
        }

        private bool CanSaveSettings() =>
            HasUnsavedChanges && !IsLoading;

        private bool CanResetSettings() =>
            !IsLoading;

        private async Task InitializeSettingsAsync()
        {
            try
            {
                IsLoading = true;
                _settings = await _configService.LoadSettingsAsync();
                HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load initial settings");
                OnError($"Failed to load settings: {ex.Message}");

                // Fallback to default settings
                _settings = new VSPilotSettings();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                await _configService.SaveSettingsAsync(Settings);
                HasUnsavedChanges = false;
                _logger.LogInformation("Settings saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings");
                OnError($"Failed to save settings: {ex.Message}");
            }
        }

        private async Task ResetSettingsAsync()
        {
            try
            {
                // Create a fresh instance of default settings
                Settings = new VSPilotSettings();

                // Save the reset settings
                await SaveSettingsAsync();

                _logger.LogInformation("Settings reset to defaults");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset settings");
                OnError($"Failed to reset settings: {ex.Message}");
            }
        }

        private void OnError(string message)
        {
            // Log the error
            _logger.LogWarning(message);

            // Raise the error event
            ErrorOccurred?.Invoke(this, message);
        }
    }
}