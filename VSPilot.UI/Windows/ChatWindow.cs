using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VSPilot.Core.Automation;
using VSPilot.UI.ViewModels;
using Microsoft.VisualStudio.Threading;
using System.Diagnostics;

namespace VSPilot.UI.Windows
{
    [Guid("E4B45B2F-0DAC-495F-A4F1-8E8EFB53834D")]
    public class ChatWindow : ToolWindowPane
    {
        private ChatWindowControl? _control;
        private ChatViewModel? _viewModel;
        private AutomationService? _automationService;
        private readonly AsyncManualResetEvent _initializationComplete = new AsyncManualResetEvent();
        private bool _isInitializing = false;

        // Default constructor required for Visual Studio tool window support
        public ChatWindow() : base(null)
        {
            Debug.WriteLine("ChatWindow: Constructor called");
            try
            {
                this.Caption = "VSPilot Chat";

                // Don't create the control here - defer to Initialize
                Debug.WriteLine("ChatWindow: Constructor completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in constructor: {ex.Message}");
                Debug.WriteLine($"ChatWindow: Stack trace: {ex.StackTrace}");
            }
        }

        // This is called when the window is created
        protected override void Initialize()
        {
            Debug.WriteLine("ChatWindow: Initialize method called");
            try
            {
                base.Initialize();

                // Create a simple control with a loading message
                _control = new ChatWindowControl(package: this.Package as AsyncPackage);
                Content = _control;

                // Add event handlers
                _control.Loaded += OnControlLoaded;
                _control.Unloaded += OnControlUnloaded;

                Debug.WriteLine("ChatWindow: Control created and set as Content");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in Initialize: {ex.Message}");
                Debug.WriteLine($"ChatWindow: Stack trace: {ex.StackTrace}");

                // Create a minimal control that shows the error
                var grid = new System.Windows.Controls.Grid();
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = $"Error initializing VSPilot Chat: {ex.Message}",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new Thickness(20)
                };
                grid.Children.Add(textBlock);
                Content = grid;
            }
        }

        protected override void OnClose()
        {
            Debug.WriteLine("ChatWindow: OnClose called");
            try
            {
                _viewModel?.Dispose();

                if (_control != null)
                {
                    _control.Loaded -= OnControlLoaded;
                    _control.Unloaded -= OnControlUnloaded;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in OnClose: {ex.Message}");
            }
            finally
            {
                base.OnClose();
            }
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("ChatWindow: OnControlLoaded event fired");

            // Prevent multiple initializations
            if (_isInitializing)
            {
                Debug.WriteLine("ChatWindow: Already initializing, skipping");
                return;
            }

            _isInitializing = true;

            // Use FireAndForget pattern to avoid deadlocks
            _ = Task.Run(async () =>
            {
                try
                {
                    Debug.WriteLine("ChatWindow: Starting background initialization");

                    // Give the UI time to render
                    await Task.Delay(500);

                    // Initialize the ViewModel
                    await InitializeViewModelAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ChatWindow: Error during background initialization: {ex.Message}");

                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        await ShowErrorMessageAsync("Failed to initialize chat window", ex);
                    }
                    catch
                    {
                        // Ignore errors in error handling
                    }
                }
                finally
                {
                    _isInitializing = false;
                }
            }, CancellationToken.None)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Debug.WriteLine($"ChatWindow: Unhandled exception in initialization task: {t.Exception}");
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        private async Task InitializeViewModelAsync()
        {
            Debug.WriteLine("ChatWindow: InitializeViewModelAsync method started");
            try
            {
                // Get the AutomationService on a background thread
                _automationService = await GetAutomationServiceAsync();

                // Switch to UI thread for UI updates
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (_automationService == null)
                {
                    Debug.WriteLine("ChatWindow: Failed to get AutomationService");

                    // Create a placeholder ViewModel to prevent binding errors
                    if (_control != null)
                    {
                        _viewModel = new ChatViewModel(null, _control.chatInput);
                        _control.DataContext = _viewModel;

                        // Update the status message in the control
                        _control.UpdateStatusMessage("AutomationService is not available. Some features may not work.");
                    }
                    return;
                }

                Debug.WriteLine("ChatWindow: AutomationService retrieved successfully");

                // Initialize the view model on the UI thread
                if (_control != null)
                {
                    _viewModel = new ChatViewModel(_automationService, _control.chatInput);
                    _control.DataContext = _viewModel;
                    Debug.WriteLine("ChatWindow: ViewModel created and set as DataContext");

                    // Remove the status message
                    _control.RemoveStatusMessage();
                }

                Debug.WriteLine("ChatWindow: ViewModel initialization complete");
                _initializationComplete.Set();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in InitializeViewModelAsync: {ex.Message}");

                // Switch to UI thread for error handling
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                _control?.UpdateStatusMessage($"Error initializing chat: {ex.Message}");

                await ShowErrorMessageAsync("Failed to initialize chat window", ex);
            }
        }

        private async Task<AutomationService?> GetAutomationServiceAsync()
        {
            Debug.WriteLine("ChatWindow: GetAutomationServiceAsync called");

            try
            {
                var package = Package as AsyncPackage;
                if (package == null)
                {
                    Debug.WriteLine("ChatWindow: Package is null or not AsyncPackage");
                    return null;
                }

                // Try to get the service asynchronously first
                Debug.WriteLine("ChatWindow: Trying GetServiceAsync");
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Use the package's GetService method
                Debug.WriteLine("ChatWindow: Using GetService");
                var service = package.GetService(typeof(AutomationService)) as AutomationService;

                if (service != null)
                {
                    Debug.WriteLine("ChatWindow: Successfully got AutomationService");
                    return service;
                }

                Debug.WriteLine("ChatWindow: AutomationService not found");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in GetAutomationServiceAsync: {ex.Message}");
                return null;
            }
        }

        private void OnControlUnloaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("ChatWindow: OnControlUnloaded event fired");
            if (_control != null)
            {
                _control.Loaded -= OnControlLoaded;
                _control.Unloaded -= OnControlUnloaded;
            }
        }

        private async Task ShowErrorMessageAsync(string message, Exception ex)
        {
            Debug.WriteLine($"ChatWindow: ShowErrorMessageAsync - {message}, Exception: {ex.Message}");
            try
            {
                // Make sure we're on the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Use a simple MessageBox instead of IVsUIShell to avoid potential deadlocks
                MessageBox.Show(
                    $"{message}\n\nDetails: {ex.Message}",
                    "VSPilot Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                Debug.WriteLine("ChatWindow: Error message box displayed");
            }
            catch (Exception msgEx)
            {
                Debug.WriteLine($"ChatWindow: Error in ShowErrorMessageAsync: {msgEx.Message}");
            }
        }
    }
}