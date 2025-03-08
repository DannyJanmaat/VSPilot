using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using VSPilot.Core.Automation;
using VSPilot.UI.ViewModels;

namespace VSPilot.UI.Windows
{
    public partial class ChatWindowControl : UserControl
    {
        private readonly ILogger<ChatWindowControl>? _logger;
        private ChatViewModel? ViewModel => DataContext as ChatViewModel;
        private Grid? _statusOverlay;
        private TextBlock? _statusMessage;

        public ChatWindowControl(ILogger<ChatWindowControl>? logger = null)
        {
            try
            {
                Debug.WriteLine("ChatWindowControl: Constructor starting");
                _logger = logger;

                // Initialize the component
                InitializeComponent();
                Debug.WriteLine("ChatWindowControl: InitializeComponent completed");

                // Add a status message overlay
                AddStatusMessage("VSPilot Chat is initializing...");
                Debug.WriteLine("ChatWindowControl: Added status message");

                // Handle UI events
                Loaded += OnControlLoaded;
                chatInput.KeyDown += OnChatInputKeyDown;
                clearButton.Click += OnClearButtonClick;
                Debug.WriteLine("ChatWindowControl: Event handlers registered");
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

                _statusOverlay.Children.Add(_statusMessage);

                // Replace the entire content with the overlay
                // We'll store the original content and restore it later
                var originalContent = Content;
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

        private void UpdateStatusMessage(string message)
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

        private void RemoveStatusMessage()
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

                // Initialize in the background to prevent UI freezing
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        chatInput.Focus();
                        Debug.WriteLine("ChatWindowControl: Focus set to chat input");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error during control loading");
                        Debug.WriteLine($"ChatWindowControl loading error: {ex.Message}");

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        UpdateStatusMessage($"Error loading chat: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in OnControlLoaded");
                Debug.WriteLine($"OnControlLoaded error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StartInitializationTimeout()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait 10 seconds
                    await Task.Delay(10000);

                    // If we still have the status message, the initialization might be hanging
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
                chatInput.Focus();
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
                ThreadHelper.ThrowIfNotOnUIThread();

                if (automationService == null)
                {
                    _logger?.LogWarning("AutomationService is null in ChatWindowControl.Initialize()");
                    Debug.WriteLine("Warning: AutomationService is null in ChatWindowControl.Initialize()");
                    UpdateStatusMessage("Warning: AutomationService is not available. Some features may not work.");
                }

                // Create ViewModel with optional parameters
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
