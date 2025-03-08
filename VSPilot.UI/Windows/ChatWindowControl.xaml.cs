using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using VSPilot.Common.Commands;
using VSPilot.Common.Extensions;
using VSPilot.Core.Automation;
using VSPilot.UI.ViewModels;

namespace VSPilot.UI.Windows
{
    // Inline converter klasse
    public class InverseBooleanConverterLocal : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool boolValue ? !boolValue : value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool boolValue ? !boolValue : value;
        }
    }

    public partial class ChatWindowControl : UserControl
    {
        private readonly ILogger<ChatWindowControl>? _logger;
        private ChatViewModel? ViewModel => DataContext as ChatViewModel;
        private Grid? _statusOverlay;
        private TextBlock? _statusMessage;
        private readonly AsyncPackage? _package;

        public ChatWindowControl(ILogger<ChatWindowControl>? logger = null, AsyncPackage? package = null)
        {
            try
            {
                Debug.WriteLine("ChatWindowControl: Constructor starting");
                _logger = logger;
                _package = package;

                // Initialize the component
                InitializeComponent();
                Debug.WriteLine("ChatWindowControl: InitializeComponent completed");

                // Add a status message overlay immediately
                AddStatusMessage("VSPilot Chat is initializing...");
                Debug.WriteLine("ChatWindowControl: Added status message");

                // Handle UI events
                Loaded += OnControlLoaded;
                chatInput.KeyDown += OnChatInputKeyDown;
                clearButton.Click += OnClearButtonClick;
                Debug.WriteLine("ChatWindowControl: Event handlers registered");

                // Start initialization in the background to avoid deadlocks
                InitializeInBackground();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in ChatWindowControl constructor");
                Debug.WriteLine($"ChatWindowControl constructor error: {ex.Message}\n{ex.StackTrace}");

                // Try to show error in UI
                try
                {
                    AddStatusMessage($"Error initializing chat: {ex.Message}");
                }
                catch
                {
                    // Last resort if even the status message fails
                }
            }
        }

        private void InitializeInBackground()
        {
            // Use AsyncPackage's JoinableTaskFactory if available
            if (_package != null)
            {
                // Fixed CS1998: Add the missing method body that was removed
                var joinableTask = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        // Switch to background thread
                        await Task.Yield();

                        // Give UI time to render
                        await Task.Delay(500);

                        // Switch to UI thread for initialization
                        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // Get automation service
                        AutomationService? automationService = null;
                        if (_package != null)
                        {
                            try
                            {
                                // Use IServiceProvider explicit cast to avoid type inference problems
                                var service = ((IServiceProvider)_package).GetService(typeof(AutomationService));
                                automationService = service as AutomationService;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Failed to get AutomationService: {ex.Message}");
                            }
                        }

                        // Initialize the control
                        Initialize(automationService);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Background initialization error: {ex.Message}\n{ex.StackTrace}");

                        try
                        {
                            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                            UpdateStatusMessage($"Error initializing chat: {ex.Message}");
                        }
                        catch
                        {
                            // Ignore errors in error handling
                        }
                    }
                });

                // Properly observe results to fix VSTHRD110
                var continuationTask = joinableTask.Task.ContinueWith(t =>
                {
                    if (t.IsFaulted && _logger != null)
                    {
                        _logger.LogError(t.Exception, "Background initialization failed");
                    }
                }, TaskScheduler.Default);

                // Join the task to avoid fire-and-forget
                joinableTask.JoinAsync().Forget();
            }
            else
            {
                // Use a safer approach with Task.Run to avoid VSSDK007
                var backgroundTask = Task.Run(async () =>
                {
                    try
                    {
                        // Switch to background thread
                        await Task.Yield();

                        await Task.Delay(500);

                        // Remove ConfigureAwait(true) - it's not supported on MainThreadAwaitable
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        Initialize(null);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Background initialization error: {ex.Message}\n{ex.StackTrace}");

                        try
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            UpdateStatusMessage($"Error initializing chat: {ex.Message}");
                        }
                        catch { }
                    }
                });

                // Observe the task result to avoid unhandled exceptions
                var continuationResult = backgroundTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.WriteLine($"Background initialization failed: {t.Exception}");
                    }
                }, TaskScheduler.Default);
            }
        }


        private Task RunBackgroundTaskAsync(Func<Task> taskFunc)
        {
            if (_package != null)
            {
                return _package.JoinableTaskFactory.RunAsync(taskFunc).Task;
            }
            else
            {
                // Fix: Replace ThreadHelper.JoinableTaskFactory with Task.Run
                return Task.Run(async () =>
                {
                    // Use ConfigureAwait(false) to avoid returning to the UI thread unnecessarily
                    await taskFunc().ConfigureAwait(false);
                });
            }
        }

        private void AddStatusMessage(string message)
        {
            try
            {
                // Create a semi-transparent overlay with the status message
                _statusOverlay = new Grid
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(200, 240, 240, 240))
                };

                _statusMessage = new TextBlock
                {
                    Text = message,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(20)
                };

                _ = _statusOverlay.Children.Add(_statusMessage);

                // Replace the entire content with the overlay
                // We'll store the original content and restore it later
                object originalContent = Content;
                Content = _statusOverlay;

                // Store the original content in the overlay's Tag property
                if (originalContent != null)
                {
                    _statusOverlay.Tag = originalContent;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to add status message: {ex.Message}");
            }
        }

        public void UpdateStatusMessage(string message)
        {
            try
            {
                if (_statusMessage != null)
                {
                    _statusMessage.Text = message;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update status message: {ex.Message}");
            }
        }

        public void RemoveStatusMessage()
        {
            try
            {
                if (_statusOverlay != null && Content == _statusOverlay)
                {
                    // Restore the original content if we saved it
                    if (_statusOverlay.Tag is UIElement originalContent)
                    {
                        Content = originalContent;
                    }

                    _statusOverlay = null;
                    _statusMessage = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to remove status message: {ex.Message}");
            }
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("ChatWindowControl: OnControlLoaded called");

                // Start a timeout timer to detect hangs
                StartInitializationTimeout();

                // Focus the input field - do this directly on the UI thread
                _ = chatInput.Focus();
                Debug.WriteLine("ChatWindowControl: Focus set to chat input");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in OnControlLoaded");
                Debug.WriteLine($"OnControlLoaded error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StartInitializationTimeout()
        {
            if (_package != null)
            {
                var joinableTask = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        // Wait 10 seconds
                        await Task.Delay(10000);

                        // If we still have the status message, initialization might be hanging
                        if (_statusMessage != null)
                        {
                            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                            UpdateStatusMessage("Initialization is taking longer than expected. You may need to restart Visual Studio.");
                            Debug.WriteLine("ChatWindowControl: Initialization timeout detected");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Timeout task error: {ex.Message}");
                    }
                });

                // Join the task to prevent fire-and-forget issues
                joinableTask.JoinAsync().Forget();
            }
            else
            {
                // Fix: Store the Task.Run result in a variable to observe it
                var timeoutTask = Task.Run(async () =>
                {
                    try
                    {
                        // Wait 10 seconds
                        await Task.Delay(10000);

                        // If we still have the status message, initialization might be hanging
                        if (_statusMessage != null)
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            UpdateStatusMessage("Initialization is taking longer than expected. You may need to restart Visual Studio.");
                            Debug.WriteLine("ChatWindowControl: Initialization timeout detected");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Timeout task error: {ex.Message}");
                    }
                });

                // Add this to silence VSTHRD110 warning
                _ = timeoutTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.WriteLine($"Timeout task failed: {t.Exception}");
                    }
                }, TaskScheduler.Default);
            }
        }

        private void RunBackgroundTask(Func<Task> taskFunc)
        {
            if (_package != null)
            {
                var joinableTask = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await taskFunc();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("RunBackgroundTask: Exception in background task: " + ex);
                    }
                });

                // Join it to avoid fire-and-forget issues
                joinableTask.JoinAsync().Forget();
            }
            else
            {
                Debug.WriteLine("Warning: AsyncPackage not available, using Task.Run as fallback.");
                // Fix: Store Task.Run result in a variable to observe it
                var task = Task.Run(async () =>
                {
                    try
                    {
                        // ConfigureAwait(false) to avoid deadlocks
                        await taskFunc().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("RunBackgroundTask (fallback): Exception in background task: " + ex);
                    }
                });

                // Add this to silence VSTHRD110 warning
                _ = task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.WriteLine($"Background task failed: {t.Exception}");
                    }
                }, TaskScheduler.Default);
            }
        }

        private void OnChatInputKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // Send message on Enter (without Shift)
                if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    e.Handled = true;
                    TrySendMessage();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing chat input key");
                Debug.WriteLine($"Chat input key error: {ex.Message}");
            }
        }

        private void OnClearButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                chatInput.Clear();
                _ = chatInput.Focus();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error clearing chat input");
                Debug.WriteLine($"Chat input clear error: {ex.Message}");
            }
        }

        private void TrySendMessage()
        {
            if (ViewModel?.SendMessageCommand == null)
            {
                _logger?.LogWarning("SendMessageCommand is not available");
                Debug.WriteLine("SendMessageCommand is not available");
                return;
            }

            if (ViewModel.SendMessageCommand.CanExecute(null))
            {
                ViewModel.SendMessageCommand.Execute(null);
                chatInput.Clear();
            }
        }

        public void Initialize(AutomationService? automationService, ILogger<ChatViewModel>? viewModelLogger = null)
        {
            try
            {
                Debug.WriteLine("ChatWindowControl: Initialize method called");

                // We should be on the UI thread here, but let's check to be sure
                if (!ThreadHelper.CheckAccess())
                {
                    Debug.WriteLine("WARNING: Initialize called from non-UI thread!");
                    throw new InvalidOperationException("Initialize must be called from the UI thread");
                }

                if (automationService == null)
                {
                    _logger?.LogWarning("AutomationService is null in ChatWindowControl.Initialize()");
                    Debug.WriteLine("Warning: AutomationService is null in ChatWindowControl.Initialize()");
                    UpdateStatusMessage("Warning: AutomationService is not available. Some features may not work.");
                }

                // Create ViewModel with optional parameters - this should be lightweight
                Debug.WriteLine("ChatWindowControl: Creating ViewModel");
                DataContext = new ChatViewModel(
                    automationService,
                    chatInput,
                    viewModelLogger
                );
                Debug.WriteLine("ChatWindowControl: ViewModel created successfully");

                // Remove the status message if initialization was successful
                RemoveStatusMessage();
                Debug.WriteLine("ChatWindowControl: Initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing ChatWindowControl");
                Debug.WriteLine($"ChatWindowControl initialization error: {ex.Message}\n{ex.StackTrace}");

                // Update the status message with the error
                UpdateStatusMessage($"Error initializing chat: {ex.Message}");

                // Fallback to a minimal ViewModel
                try
                {
                    DataContext = new ChatViewModel(null, chatInput);
                    Debug.WriteLine("ChatWindowControl: Created fallback ViewModel");
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"Failed to create fallback ViewModel: {fallbackEx.Message}");
                }
            }
        }
    }
}
