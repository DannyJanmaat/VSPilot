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
    // Inline converter class
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
                sendButton.Click += OnSendButtonClick; // Add this line
                Debug.WriteLine("ChatWindowControl: Event handlers registered");
            }
            catch (Exception ex)
            {
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

        private void OnSendButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                TrySendMessage();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Send button click error: {ex.Message}");
            }
        }

        private void InitializeInBackground()
        {
            if (_package != null)
            {
                var joinableTask = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await Task.Yield();
                        Debug.WriteLine("ChatWindowControl: After Task.Yield() in background initialization");

                        await Task.Delay(500);
                        Debug.WriteLine("ChatWindowControl: Delay completed in background initialization");

                        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                        Debug.WriteLine("ChatWindowControl: Switched to UI thread for background initialization");

                        AutomationService? automationService = null;
                        try
                        {
                            var service = ((IServiceProvider)_package).GetService(typeof(AutomationService));
                            automationService = service as AutomationService;
                            Debug.WriteLine("ChatWindowControl: AutomationService retrieved successfully");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ChatWindowControl: Failed to get AutomationService: {ex.Message}");
                        }

                        Initialize(automationService);
                        Debug.WriteLine("ChatWindowControl: Initialize method called from background task");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChatWindowControl: Background initialization error: {ex.Message}\n{ex.StackTrace}");
                        try
                        {
                            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                            UpdateStatusMessage($"Error initializing chat: {ex.Message}");
                        }
                        catch { }
                    }
                });

                joinableTask.Task.ContinueWith(t =>
                {
                    if (t.IsFaulted && _logger != null)
                    {
                        _logger.LogError(t.Exception, "Background initialization failed");
                    }
                }, TaskScheduler.Default).Forget();

                joinableTask.JoinAsync().Forget();
            }
            else
            {
                var backgroundTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Yield();
                        await Task.Delay(500);
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        Initialize(null);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChatWindowControl: Background initialization error: {ex.Message}\n{ex.StackTrace}");
                        try
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            UpdateStatusMessage($"Error initializing chat: {ex.Message}");
                        }
                        catch { }
                    }
                });

                backgroundTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.WriteLine($"ChatWindowControl: Background initialization failed: {t.Exception}");
                    }
                }, TaskScheduler.Default).Forget();
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
                return Task.Run(async () =>
                {
                    await taskFunc().ConfigureAwait(false);
                });
            }
        }

        private void AddStatusMessage(string message)
        {
            try
            {
                _statusOverlay = new Grid
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 240, 240, 240))
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

                object originalContent = Content;
                Content = _statusOverlay;

                if (originalContent != null)
                {
                    _statusOverlay.Tag = originalContent;
                }
                Debug.WriteLine($"ChatWindowControl: Status message added: {message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindowControl: Failed to add status message: {ex.Message}");
            }
        }

        public void UpdateStatusMessage(string message)
        {
            try
            {
                if (_statusMessage != null)
                {
                    _statusMessage.Text = message;
                    Debug.WriteLine($"ChatWindowControl: Status message updated: {message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindowControl: Failed to update status message: {ex.Message}");
            }
        }

        public void RemoveStatusMessage()
        {
            try
            {
                if (_statusOverlay != null && Content == _statusOverlay)
                {
                    if (_statusOverlay.Tag is UIElement originalContent)
                    {
                        Content = originalContent;
                    }
                    _statusOverlay = null;
                    _statusMessage = null;
                    Debug.WriteLine("ChatWindowControl: Status message removed");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindowControl: Failed to remove status message: {ex.Message}");
            }
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("ChatWindowControl: OnControlLoaded called");

                StartInitializationTimeout();

                _ = chatInput.Focus();
                Debug.WriteLine("ChatWindowControl: Focus set to chat input");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in OnControlLoaded");
                Debug.WriteLine($"ChatWindowControl: OnControlLoaded error: {ex.Message}\n{ex.StackTrace}");
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
                        await Task.Delay(10000);
                        if (_statusMessage != null)
                        {
                            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                            UpdateStatusMessage("Initialization is taking longer than expected. You may need to restart Visual Studio.");
                            Debug.WriteLine("ChatWindowControl: Initialization timeout detected");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChatWindowControl: Timeout task error: {ex.Message}");
                    }
                });
                joinableTask.JoinAsync().Forget();
            }
            else
            {
                var timeoutTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(10000);
                        if (_statusMessage != null)
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            UpdateStatusMessage("Initialization is taking longer than expected. You may need to restart Visual Studio.");
                            Debug.WriteLine("ChatWindowControl: Initialization timeout detected");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChatWindowControl: Timeout task error: {ex.Message}");
                    }
                });

                timeoutTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.WriteLine($"ChatWindowControl: Timeout task failed: {t.Exception}");
                    }
                }, TaskScheduler.Default).Forget();
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
                        Debug.WriteLine($"ChatWindowControl: Exception in background task: {ex}");
                    }
                });
                joinableTask.JoinAsync().Forget();
            }
            else
            {
                Debug.WriteLine("ChatWindowControl: AsyncPackage not available, using Task.Run as fallback");
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await taskFunc().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChatWindowControl: Exception in background Task.Run: {ex}");
                    }
                });
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Debug.WriteLine($"ChatWindowControl: Background task failed: {t.Exception}");
                    }
                }, TaskScheduler.Default).Forget();
            }
        }

        private void OnChatInputKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    e.Handled = true;
                    TrySendMessage();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing chat input key");
                Debug.WriteLine($"ChatWindowControl: Chat input key error: {ex.Message}");
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
                Debug.WriteLine($"ChatWindowControl: Chat input clear error: {ex.Message}");
            }
        }

        private void TrySendMessage()
        {
            Debug.WriteLine("ChatWindowControl: TrySendMessage called");

            if (ViewModel?.SendMessageCommand == null)
            {
                Debug.WriteLine("ChatWindowControl: SendMessageCommand is not available");
                UpdateStatusMessage("Chat is not fully initialized yet. Please try again in a moment.");
                return;
            }

            if (string.IsNullOrWhiteSpace(chatInput.Text))
            {
                Debug.WriteLine("ChatWindowControl: Chat input is empty");
                return;
            }

            Debug.WriteLine($"ChatWindowControl: Sending message: {chatInput.Text}");

            if (ViewModel.SendMessageCommand.CanExecute(null))
            {
                ViewModel.SendMessageCommand.Execute(null);
                chatInput.Clear();
                chatInput.Focus();
            }
            else
            {
                Debug.WriteLine("ChatWindowControl: SendMessageCommand cannot execute");
                UpdateStatusMessage("Cannot send message at this time. Please try again later.");
            }
        }


        public void Initialize(AutomationService? automationService, ILogger<ChatViewModel>? viewModelLogger = null)
        {
            try
            {
                Debug.WriteLine("ChatWindowControl: Initialize method called");

                if (!ThreadHelper.CheckAccess())
                {
                    Debug.WriteLine("ChatWindowControl: WARNING: Initialize called from non-UI thread!");
                    throw new InvalidOperationException("Initialize must be called from the UI thread");
                }

                if (automationService == null)
                {
                    _logger?.LogWarning("AutomationService is null in ChatWindowControl.Initialize()");
                    Debug.WriteLine("ChatWindowControl: Warning - AutomationService is null");
                    UpdateStatusMessage("Warning: AutomationService is not available. Some features may not work.");
                }

                Debug.WriteLine("ChatWindowControl: Creating ViewModel");
                DataContext = new ChatViewModel(automationService, chatInput, viewModelLogger);
                Debug.WriteLine("ChatWindowControl: ViewModel created successfully");

                RemoveStatusMessage();
                Debug.WriteLine("ChatWindowControl: Initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing ChatWindowControl");
                Debug.WriteLine($"ChatWindowControl: Initialization error: {ex.Message}\n{ex.StackTrace}");
                UpdateStatusMessage($"Error initializing chat: {ex.Message}");
                try
                {
                    DataContext = new ChatViewModel(null, chatInput);
                    Debug.WriteLine("ChatWindowControl: Created fallback ViewModel");
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"ChatWindowControl: Fallback ViewModel creation failed: {fallbackEx.Message}");
                }
            }
        }
    }
}
