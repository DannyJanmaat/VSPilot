// ChatWindow.cs
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
        // Inline logger initialization to avoid CS0649
        private readonly ILogger<ChatWindow> _logger = NullLogger<ChatWindow>.Instance;

        public ChatWindow() : base(null)
        {
            Debug.WriteLine("ChatWindow: Constructor called");
            try
            {
                this.Caption = "VSPilot Chat";
                Debug.WriteLine("ChatWindow: Caption set to 'VSPilot Chat'");
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
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("ChatWindow: Initialize method called");
            try
            {
                base.Initialize();
                Debug.WriteLine("ChatWindow: base.Initialize() completed");
                // Use the valid package instance from VSPilotPackage
                _control = new ChatWindowControl(package: VSPilotPackage.Instance);
                Debug.WriteLine("ChatWindow: ChatWindowControl created");
                Content = _control;
                _control.Loaded += OnControlLoaded;
                _control.Unloaded += OnControlUnloaded;
                Debug.WriteLine("ChatWindow: Control set as Content and event handlers attached");
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
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("ChatWindow: OnClose called");
            try
            {
                _viewModel?.Dispose();
                Debug.WriteLine("ChatWindow: ViewModel disposed");
                if (_control != null)
                {
                    _control.Loaded -= OnControlLoaded;
                    _control.Unloaded -= OnControlUnloaded;
                    Debug.WriteLine("ChatWindow: Event handlers detached from control");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in OnClose: {ex.Message}");
            }
            finally
            {
                base.OnClose();
                Debug.WriteLine("ChatWindow: base.OnClose() called");
            }
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("ChatWindow: OnControlLoaded event fired");
            if (_isInitializing)
            {
                Debug.WriteLine("ChatWindow: Already initializing, skipping further initialization");
                return;
            }
            _isInitializing = true;
            if (Package is AsyncPackage asyncPackage)
            {
                asyncPackage.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        Debug.WriteLine("ChatWindow: Starting background initialization of view model");
                        await Task.Yield();
                        Debug.WriteLine("ChatWindow: After Task.Yield() in background init");
                        await Task.Delay(500);
                        Debug.WriteLine("ChatWindow: Delay completed, switching to UI thread to initialize view model asynchronously");
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
                        catch
                        {
                            Debug.WriteLine("ChatWindow: Exception while showing error message");
                        }
                    }
                    finally
                    {
                        _isInitializing = false;
                        Debug.WriteLine("ChatWindow: Background initialization flag set to false");
                    }
                }).Task.Forget();
            }
            else
            {
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
                    Debug.WriteLine("ChatWindow: Fallback initialization completed");
                }
            }
        }

        private void InitializeViewModel()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("ChatWindow: InitializeViewModel called");
            try
            {
                Debug.WriteLine("ChatWindow: Attempting to retrieve AutomationService synchronously");
                var service = ((IServiceProvider)Package).GetService(typeof(AutomationService));
                _automationService = service as AutomationService;
                if (_control != null)
                {
                    _viewModel = new ChatViewModel(_automationService, _control.chatInput);
                    _control.DataContext = _viewModel;
                    Debug.WriteLine("ChatWindow: ViewModel created and associated with control");
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
                    Debug.WriteLine("ChatWindow: Failed to retrieve AutomationService asynchronously");
                    if (_control != null)
                    {
                        _viewModel = new ChatViewModel(null, _control.chatInput);
                        _control.DataContext = _viewModel;
                        _control.UpdateStatusMessage("AutomationService is not available. Some features may not work.");
                    }
                    return;
                }
                Debug.WriteLine("ChatWindow: AutomationService retrieved successfully");
                if (_control != null)
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
                    Debug.WriteLine("ChatWindow: Package is null or not of type AsyncPackage");
                    return null;
                }
                await package.JoinableTaskFactory.SwitchToMainThreadAsync();
                var service = ((IServiceProvider)package).GetService(typeof(AutomationService));
                if (service is AutomationService automationService)
                {
                    Debug.WriteLine("ChatWindow: Successfully obtained AutomationService");
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
            if (_control != null)
            {
                _control.Loaded -= OnControlLoaded;
                _control.Unloaded -= OnControlUnloaded;
                Debug.WriteLine("ChatWindow: Event handlers detached from control during unload");
            }
        }

        private async Task ShowErrorMessageAsync(string message, Exception ex)
        {
            Debug.WriteLine($"ChatWindow: ShowErrorMessageAsync - {message}, Exception: {ex.Message}");
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                MessageBox.Show($"{message}\n\nDetails: {ex.Message}",
                                "VSPilot Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                Debug.WriteLine("ChatWindow: Error message box displayed");
            }
            catch (Exception msgEx)
            {
                Debug.WriteLine($"ChatWindow: Exception in ShowErrorMessageAsync: {msgEx.Message}");
            }
        }

        private void ShowErrorMessage(string message, Exception ex)
        {
            ThreadHelper.ThrowIfNotOnUIThread("ShowErrorMessage must be called on the UI thread");
            Debug.WriteLine($"ChatWindow: ShowErrorMessage - {message}, Exception: {ex.Message}");
            try
            {
                MessageBox.Show($"{message}\n\nDetails: {ex.Message}",
                                "VSPilot Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
            }
            catch (Exception msgEx)
            {
                Debug.WriteLine($"ChatWindow: Exception in ShowErrorMessage: {msgEx.Message}");
            }
        }
    }
}
