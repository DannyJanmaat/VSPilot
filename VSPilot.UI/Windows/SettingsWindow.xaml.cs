using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.PlatformUI;
using System;
using System.Windows;
using VSPilot.Core.Services;
using VSPilot.UI.ViewModels;
using System.ComponentModel;
using System.Windows.Controls;

namespace VSPilot.UI.Windows
{
    public partial class SettingsWindow : DialogWindow
    {
        private readonly SettingsViewModel _viewModel;
        private readonly ILogger<SettingsWindow>? _logger;

        public SettingsWindow(
            ConfigurationService configService,
            ILogger<SettingsViewModel> viewModelLogger,
            ILogger<SettingsWindow>? windowLogger = null)
        {
            _logger = windowLogger;

            try
            {
                InitializeComponent();

                // Create ViewModel
                _viewModel = new SettingsViewModel(configService, viewModelLogger);
                base.DataContext = _viewModel; // Use base.DataContext instead of this.DataContext

                // Subscribe to events
                base.Closed += (s, e) => OnWindowClosing(s, new CancelEventArgs(false)); // Use Closed event instead of Closing
                _viewModel.ErrorOccurred += OnViewModelError;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing SettingsWindow");
                HandleInitializationError(ex);
            }
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (_viewModel.HasUnsavedChanges)
                {
                    MessageBoxResult result = MessageBox.Show(
                        "You have unsaved changes. Do you want to save them?",
                        "VSPilot Settings",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    switch (result)
                    {
                        case MessageBoxResult.Yes:
                            _viewModel.SaveCommand.Execute(null);
                            break;
                        case MessageBoxResult.Cancel:
                            e.Cancel = true;
                            break;
                        case MessageBoxResult.No:
                            // User chose to discard changes
                            _logger?.LogInformation("User discarded unsaved settings changes");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling window closing");
                _ = MessageBox.Show(
                    $"An error occurred while closing the settings window: {ex.Message}",
                    "VSPilot Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnViewModelError(object sender, string errorMessage)
        {
            _logger?.LogWarning("ViewModel error: {ErrorMessage}", errorMessage);
            _ = MessageBox.Show(
                errorMessage,
                "VSPilot Settings Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void HandleInitializationError(Exception ex)
        {
            _ = MessageBox.Show(
                $"Failed to initialize settings window: {ex.Message}",
                "VSPilot Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}