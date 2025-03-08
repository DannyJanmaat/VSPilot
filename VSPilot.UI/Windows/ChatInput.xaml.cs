using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace VSPilot.UI.Controls
{
    public partial class ChatInput : UserControl
    {
        // SendCommand Dependency Property
        public static readonly DependencyProperty SendCommandProperty =
            DependencyProperty.Register(
                nameof(SendCommand),
                typeof(ICommand),
                typeof(ChatInput),
                new PropertyMetadata(null));

        // Text Dependency Property
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(ChatInput),
                new PropertyMetadata(string.Empty));

        // IsProcessing Dependency Property
        public static readonly DependencyProperty IsProcessingProperty =
            DependencyProperty.Register(
                nameof(IsProcessing),
                typeof(bool),
                typeof(ChatInput),
                new PropertyMetadata(false));

        // Optional logger for diagnostics
        private readonly ILogger<ChatInput>? _logger;

        // Dependency Property Accessors
        public ICommand? SendCommand
        {
            get => (ICommand?)GetValue(SendCommandProperty);
            set => SetValue(SendCommandProperty, value);
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public bool IsProcessing
        {
            get => (bool)GetValue(IsProcessingProperty);
            set => SetValue(IsProcessingProperty, value);
        }

        public ChatInput(ILogger<ChatInput>? logger = null)
        {
            _logger = logger;

            InitializeComponent();

            // Set DataContext to self for direct dependency property binding
            DataContext = this;
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                // Send message on Enter (without Shift)
                if (e.Key == Key.Return &&
                    (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
                {
                    e.Handled = true;
                    TrySendMessage();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing chat input key");
                HandleError(ex);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Prevent clearing during processing
                if (IsProcessing) return;

                InputBox.Clear();
                InputBox.Focus();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error clearing chat input");
                HandleError(ex);
            }
        }

        private void TrySendMessage()
        {
            // Prevent sending during processing
            if (IsProcessing) return;

            if (string.IsNullOrWhiteSpace(Text)) return;

            try
            {
                if (SendCommand?.CanExecute(Text) == true)
                {
                    IsProcessing = true;
                    SendCommand.Execute(Text);
                    InputBox.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error sending message");
                HandleError(ex);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void HandleError(Exception ex)
        {
            // Default error handling if no logger is provided
            MessageBox.Show(
                $"An error occurred: {ex.Message}",
                "Chat Input Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}