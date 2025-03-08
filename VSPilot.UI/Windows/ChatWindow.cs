using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using VSPilot.Common.Extensions;
using VSPilot.Core.Automation;
using VSPilot.UI.ViewModels;

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
        // Initialize _logger inline to avoid CS0649
        private readonly ILogger<ChatWindow> _logger = NullLogger<ChatWindow>.Instance;

        public ChatWindow() : base(null)
        {
            Debug.WriteLine("ChatWindow: Constructor called");
            try
            {
                this.Caption = "VSPilot Chat";
                Debug.WriteLine("ChatWindow: Constructor completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in constructor: {ex.Message}");
                Debug.WriteLine($"ChatWindow: Stack trace: {ex.StackTrace}");
            }
        }

        protected override void Initialize()
        {
            // Unconditional thread affinity check
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("ChatWindow: Initialize method called");
            try
            {
                base.Initialize();
                _control = new ChatWindowControl(package: this.Package as AsyncPackage);
                Content = _control;
                _control.Loaded += OnControlLoaded;
                _control.Unloaded += OnControlUnloaded;
                Debug.WriteLine("ChatWindow: Control created and set as Content");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in Initialize: {ex.Message}");
                var grid = new System.Windows.Controls.Grid();
                var textBlock = new System.Windows.Controls.TextBlock
                {
                    Text = $"Error initializing VSPilot Chat: {ex.Message}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20)
                };
                grid.Children.Add(textBlock);
                Content = grid;
            }
        }

        protected override void OnClose()
        {
            // Unconditional thread affinity check
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("ChatWindow: OnClose called");
            try
            {
                _viewModel?.Dispose();
                if (_control is not null)
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

        // At the very start of UI-bound methods like OnControlLoaded, add an unconditional UI thread check:
        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // Unconditional check per VSTHRD108
            Debug.WriteLine("ChatWindow: OnControlLoaded event fired");
            if (_isInitializing)
            {
                Debug.WriteLine("ChatWindow: Already initializing, skipping");
                return;
            }
            _isInitializing = true;
            if (Package is AsyncPackage asyncPackage)
            {
                asyncPackage.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        Debug.WriteLine("ChatWindow: Starting background initialization");
                        await Task.Yield();
                        await Task.Delay(500);
                        await InitializeViewModelAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChatWindow: Error during background initialization: {ex.Message}");
                        try
                        {
                            await asyncPackage.JoinableTaskFactory.SwitchToMainThreadAsync();
                            await ShowErrorMessageAsync("Failed to initialize chat window", ex);
                        }
                        catch { }
                    }
                    finally
                    {
                        _isInitializing = false;
                    }
                }).Task.Forget();
            }
            else
            {
                // No additional ThreadHelper call required here since we already did one
                try
                {
                    InitializeViewModel();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ChatWindow: Error during fallback initialization: {ex.Message}");
                    ShowErrorMessage("Failed to initialize chat window", ex);
                }
                finally
                {
                    _isInitializing = false;
                }
            }
        }

        private void InitializeViewModel()
        {
            // Unconditional thread affinity check
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("ChatWindow: InitializeViewModel called");
            try
            {
                var service = ((IServiceProvider)Package).GetService(typeof(AutomationService));
                _automationService = service as AutomationService;
                if (_control is not null)
                {
                    _viewModel = new ChatViewModel(_automationService, _control.chatInput);
                    _control.DataContext = _viewModel;
                    Debug.WriteLine("ChatWindow: ViewModel created and set as DataContext");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in InitializeViewModel: {ex.Message}");
                ShowErrorMessage("Failed to initialize chat window", ex);
            }
        }

        private async Task InitializeViewModelAsync()
        {
            Debug.WriteLine("ChatWindow: InitializeViewModelAsync method started");
            try
            {
                _automationService = await GetAutomationServiceAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_automationService == null)
                {
                    Debug.WriteLine("ChatWindow: Failed to get AutomationService");
                    if (_control is not null)
                    {
                        _viewModel = new ChatViewModel(null, _control.chatInput);
                        _control.DataContext = _viewModel;
                        _control.UpdateStatusMessage("AutomationService is not available. Some features may not work.");
                    }
                    return;
                }
                Debug.WriteLine("ChatWindow: AutomationService retrieved successfully");
                if (_control is not null)
                {
                    _viewModel = new ChatViewModel(_automationService, _control.chatInput);
                    _control.DataContext = _viewModel;
                    Debug.WriteLine("ChatWindow: ViewModel created and set as DataContext");
                    _control.RemoveStatusMessage();
                }
                Debug.WriteLine("ChatWindow: ViewModel initialization complete");
                _initializationComplete.Set();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in InitializeViewModelAsync: {ex.Message}");
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
                if (Package is not AsyncPackage package)
                {
                    Debug.WriteLine("ChatWindow: Package is null or not AsyncPackage");
                    return null;
                }
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                var service = ((IServiceProvider)package).GetService(typeof(AutomationService));
                if (service is AutomationService automationService)
                {
                    Debug.WriteLine("ChatWindow: Successfully got AutomationService");
                    return automationService;
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
            if (_control is not null)
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
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
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

        private void ShowErrorMessage(string message, Exception ex)
        {
            // Unconditional thread affinity check
            ThreadHelper.ThrowIfNotOnUIThread("ShowErrorMessage must be called on the UI thread");
            Debug.WriteLine($"ChatWindow: ShowErrorMessage - {message}, Exception: {ex.Message}");
            try
            {
                MessageBox.Show(
                    $"{message}\n\nDetails: {ex.Message}",
                    "VSPilot Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            catch (Exception msgEx)
            {
                Debug.WriteLine($"ChatWindow: Error in ShowErrorMessage: {msgEx.Message}");
            }
        }

        // Change RunBackgroundTask to return Task instead of async void
        private async Task RunBackgroundTaskAsync(Func<Task> taskFunc)
        {
            if (Package is AsyncPackage asyncPackage)
            {
                try
                {
                    await asyncPackage.JoinableTaskFactory.RunAsync(taskFunc).Task;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background task failed");
                }
            }
            else
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.RunAsync(taskFunc).Task;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background task failed");
                }
            }
        }
    }
}
