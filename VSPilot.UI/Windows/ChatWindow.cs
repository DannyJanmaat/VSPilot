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

        // Default constructor required for Visual Studio tool window support
        public ChatWindow() : base(null)
        {
            Caption = "VSPilot Chat";
            BitmapResourceID = 301;
            BitmapIndex = 1;

            // Log initialization for diagnostics
            Debug.WriteLine("ChatWindow: Constructor called");
        }

        // This is called when the window is created
        protected override void Initialize()
        {
            Debug.WriteLine("ChatWindow: Initialize method called");
            base.Initialize();

            // Initialize UI - Create the control but leave ViewModel initialization for later
            _control = new ChatWindowControl();
            Content = _control;

            // Add event handlers
            _control.Loaded += OnControlLoaded;
            _control.Unloaded += OnControlUnloaded;

            Debug.WriteLine("ChatWindow: Control created and set as Content");
        }

        protected override void OnClose()
        {
            Debug.WriteLine("ChatWindow: OnClose called");
            _viewModel?.Dispose();
            base.OnClose();
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("ChatWindow: OnControlLoaded event fired");

            // Fix VSSDK007 warning by accessing the AsyncPackage JoinableTaskFactory directly
            if (Package is AsyncPackage asyncPackage)
            {
                var task = asyncPackage.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await InitializeViewModelAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChatWindow: Error during ViewModel initialization: {ex.Message}");
                        await ShowErrorMessageAsync("Failed to initialize chat window", ex);
                    }
                });
                task.FileAndForget("VSPilot/ChatWindow");
            }
            else
            {
                // Fallback if package is not AsyncPackage (should not happen in practice)
                InitializeViewModel();
            }
        }

        // Synchronous fallback method
        private void InitializeViewModel()
        {
            try
            {
                Debug.WriteLine("ChatWindow: InitializeViewModel fallback called");
                ThreadHelper.ThrowIfNotOnUIThread();

                // Create a dummy ViewModel
                if (_control != null)
                {
                    _viewModel = new ChatViewModel(null, _control.chatInput);
                    _control.DataContext = _viewModel;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Error in fallback initialization: {ex.Message}");
            }
        }

        private async Task InitializeViewModelAsync()
        {
            Debug.WriteLine("ChatWindow: InitializeViewModelAsync method started");
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Clear any existing initialization
                _automationService = null;
                _viewModel = null;

                // Get the AutomationService directly from the package
                _automationService = await GetAutomationServiceAsync();

                if (_automationService == null)
                {
                    Debug.WriteLine("ChatWindow: Failed to get AutomationService");

                    // Show error message and create a dummy ViewModel
                    await ShowErrorMessageAsync("Could not initialize VSPilot",
                        new InvalidOperationException("AutomationService is not available. The extension may not be properly loaded."));

                    // Create a placeholder ViewModel to prevent binding errors
                    if (_control != null)
                    {
                        _viewModel = new ChatViewModel(null, _control.chatInput);
                        _control.DataContext = _viewModel;
                    }
                    return;
                }

                Debug.WriteLine("ChatWindow: AutomationService retrieved successfully");

                // Initialize the view model
                if (_control != null)
                {
                    _viewModel = new ChatViewModel(_automationService, _control.chatInput);
                    _control.DataContext = _viewModel;
                    Debug.WriteLine("ChatWindow: ViewModel created and set as DataContext");
                }

                Debug.WriteLine("ChatWindow: ViewModel initialization complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindow: Exception in InitializeViewModelAsync: {ex.Message}");
                await ShowErrorMessageAsync("Failed to initialize chat window", ex);
            }
        }

        private async Task<AutomationService> GetAutomationServiceAsync()
        {
            Debug.WriteLine("ChatWindow: GetAutomationServiceAsync called");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var package = Package as AsyncPackage;
                if (package == null)
                {
                    Debug.WriteLine("ChatWindow: Package is null or not AsyncPackage");
                    return null;
                }

                // Use base method call with explicit cast (no extension method)
                Debug.WriteLine("ChatWindow: Using base GetService with cast");
                object service = ((System.IServiceProvider)package).GetService(typeof(AutomationService));
                if (service != null)
                {
                    Debug.WriteLine("ChatWindow: Successfully got AutomationService");
                    return (AutomationService)service;
                }

                // Try GetServiceAsync (this is built-in, not an extension method)
                Debug.WriteLine("ChatWindow: Trying GetServiceAsync");
                object asyncService = await package.GetServiceAsync(typeof(AutomationService));
                if (asyncService != null)
                {
                    Debug.WriteLine("ChatWindow: Successfully got AutomationService using GetServiceAsync");
                    return (AutomationService)asyncService;
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
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Use base IServiceProvider interface to avoid extension method ambiguity
                Debug.WriteLine("ChatWindow: Using IServiceProvider directly");
                object shellService = ((System.IServiceProvider)Package).GetService(typeof(SVsUIShell));
                if (shellService != null)
                {
                    IVsUIShell uiShell = (IVsUIShell)shellService;
                    Guid clsid = Guid.Empty;
                    uiShell.ShowMessageBox(
                        0,
                        ref clsid,
                        "VSPilot Error",
                        $"{message}\n\nDetails: {ex.Message}",
                        string.Empty,
                        0,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        0,
                        out int result
                    );
                    Debug.WriteLine("ChatWindow: Error message box displayed");
                }
                else
                {
                    Debug.WriteLine("ChatWindow: Failed to get IVsUIShell service for error display");
                }
            }
            catch (Exception msgEx)
            {
                Debug.WriteLine($"ChatWindow: Error in ShowErrorMessageAsync: {msgEx.Message}");
            }
        }
    }
}