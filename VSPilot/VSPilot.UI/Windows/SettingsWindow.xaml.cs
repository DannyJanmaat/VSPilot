﻿using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.PlatformUI;
using System;
using System.ComponentModel;
using System.Windows;
using VSPilot.Core.Services;
using VSPilot.UI.ViewModels;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows.Navigation;

namespace VSPilot.UI.Windows
{
    public partial class SettingsWindow : DialogWindow
    {
        private readonly SettingsViewModel _viewModel;
        private readonly ILogger<SettingsWindow>? _logger;
        private bool _isInitializing = true;

        public SettingsWindow(
            ConfigurationService configService,
            GitHubCopilotService copilotService,
            ILogger<SettingsViewModel> viewModelLogger,
            ILogger<SettingsWindow>? windowLogger = null)
        {
            _logger = windowLogger;
            LogExtended("SettingsWindow: Constructor started");

            try
            {
                InitializeComponent();
                LogExtended("SettingsWindow: InitializeComponent completed");

                // Create ViewModel
                _viewModel = new SettingsViewModel(configService, copilotService, viewModelLogger);
                base.DataContext = _viewModel; // Use base.DataContext instead of this.DataContext
                LogExtended("SettingsWindow: ViewModel created and set as DataContext");

                // Subscribe to events
                base.Closed += (s, e) =>
                {
                    LogExtended("SettingsWindow: Closed event fired");
                    OnWindowClosing(s, new CancelEventArgs(false));
                }; // Use Closed event instead of Closing
                _viewModel.ErrorOccurred += OnViewModelError;
                LogExtended("SettingsWindow: Subscribed to events");

                // Initialize password boxes after ViewModel is loaded
                _viewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "IsLoaded" && _viewModel.IsLoaded)
                    {
                        InitializePasswordBoxes();
                    }
                };

                this.Loaded += (s, e) =>
                {
                    LogExtended("SettingsWindow: Window loaded");
                    _isInitializing = false;
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing SettingsWindow");
                LogExtended($"SettingsWindow: Exception in constructor: {ex.Message}");
                HandleInitializationError(ex);
            }
        }

        private void InitializePasswordBoxes()
        {
            LogExtended("SettingsWindow: Initializing password boxes");
            try
            {
                // Set initial values for password boxes
                if (!string.IsNullOrEmpty(_viewModel.Settings.OpenAIApiKey))
                {
                    OpenAIKeyBox.Password = _viewModel.Settings.OpenAIApiKey;
                }

                if (!string.IsNullOrEmpty(_viewModel.Settings.AnthropicApiKey))
                {
                    AnthropicKeyBox.Password = _viewModel.Settings.AnthropicApiKey;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing password boxes");
                LogExtended($"SettingsWindow: Exception in InitializePasswordBoxes: {ex.Message}");
            }
        }

        private void OpenAIKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            LogExtended("SettingsWindow: OpenAI API key changed");
            try
            {
                _viewModel.Settings.OpenAIApiKey = OpenAIKeyBox.Password;
                _viewModel.NotifySettingsChanged();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling OpenAI API key change");
                LogExtended($"SettingsWindow: Exception in OpenAIKeyBox_PasswordChanged: {ex.Message}");
            }
        }

        private void AnthropicKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            LogExtended("SettingsWindow: Anthropic API key changed");
            try
            {
                _viewModel.Settings.AnthropicApiKey = AnthropicKeyBox.Password;
                _viewModel.NotifySettingsChanged();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling Anthropic API key change");
                LogExtended($"SettingsWindow: Exception in AnthropicKeyBox_PasswordChanged: {ex.Message}");
            }
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            LogExtended($"SettingsWindow: Hyperlink navigation requested to {e.Uri}");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error navigating to URL: {Url}", e.Uri);
                LogExtended($"SettingsWindow: Exception in Hyperlink_RequestNavigate: {ex.Message}");
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            LogExtended("SettingsWindow: OnWindowClosing called");
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
                            LogExtended("SettingsWindow: User chose Yes and Save command executed");
                            break;
                        case MessageBoxResult.Cancel:
                            e.Cancel = true;
                            LogExtended("SettingsWindow: User cancelled closing the window");
                            break;
                        case MessageBoxResult.No:
                            _logger?.LogInformation("User discarded unsaved settings changes");
                            LogExtended("SettingsWindow: User chose No (discard unsaved changes)");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling window closing");
                LogExtended($"SettingsWindow: Exception in OnWindowClosing: {ex.Message}");
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
            LogExtended($"SettingsWindow: ViewModel error occurred: {errorMessage}");
            _ = MessageBox.Show(
                errorMessage,
                "VSPilot Settings Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void HandleInitializationError(Exception ex)
        {
            LogExtended($"SettingsWindow: Handling initialization error: {ex.Message}");
            _ = MessageBox.Show(
                $"Failed to initialize settings window: {ex.Message}",
                "VSPilot Initialization Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // Extended logging helper
        private void LogExtended(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
        }
    }
}
