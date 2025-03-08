using System;
using System.Diagnostics;
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

        public ChatWindowControl(ILogger<ChatWindowControl>? logger = null)
        {
            _logger = logger;

            InitializeComponent();

            // Handle UI events
            Loaded += OnControlLoaded;
            chatInput.KeyDown += OnChatInputKeyDown;
            clearButton.Click += OnClearButtonClick;
        }

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                chatInput.Focus();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during control loading");
                Debug.WriteLine($"ChatWindowControl loading error: {ex.Message}");
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
                ThreadHelper.ThrowIfNotOnUIThread();

                if (automationService == null)
                {
                    _logger?.LogWarning("AutomationService is null in ChatWindowControl.Initialize()");
                    Debug.WriteLine("Warning: AutomationService is null in ChatWindowControl.Initialize()");
                }

                // Create ViewModel with optional parameters
                DataContext = new ChatViewModel(
                    automationService,
                    chatInput,
                    viewModelLogger
                );
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing ChatWindowControl");
                Debug.WriteLine($"ChatWindowControl initialization error: {ex.Message}");

                // Fallback to a minimal ViewModel
                DataContext = new ChatViewModel(null, chatInput);
            }
        }
    }
}