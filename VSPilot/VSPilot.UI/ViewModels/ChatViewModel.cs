using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using VSPilot.Common.Models;
using VSPilot.Common.Commands;
using VSPilot.Common.ViewModels;
using VSPilot.Core.Automation;
using System.Collections.Specialized;
using System.Windows.Controls;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VSPilot.UI.ViewModels
{
    public class ChatViewModel : ViewModelBase, IDisposable
    {
        private readonly AutomationService? _automationService;
        private readonly TextBox _chatInput;
        private readonly ILogger<ChatViewModel>? _logger;
        private string _userInput = string.Empty;
        private bool _isProcessing;
        private bool _disposed;

        public ObservableCollection<ChatMessage> ChatHistory { get; } = new();

        public string UserInput
        {
            get => _userInput;
            set => SetProperty(ref _userInput, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set => SetProperty(ref _isProcessing, value);
        }

        public ICommand SendMessageCommand { get; }
        public ICommand ClearCommand { get; }

        public event EventHandler<string>? ErrorOccurred;

        public ChatViewModel(
            AutomationService? automationService,
            TextBox chatInput,
            ILogger<ChatViewModel>? logger = null)
        {
            _automationService = automationService;
            _chatInput = chatInput;
            _logger = logger;

            // Commands
            SendMessageCommand = new RelayCommand(SendMessageAsync, CanSendMessage);
            ClearCommand = new RelayCommand(async () => await Task.Run(ClearChat));

            // Initial welcome message
            AddInitialWelcomeMessage();

            // Listen for collection changes
            ((INotifyCollectionChanged)ChatHistory).CollectionChanged += OnChatHistoryChanged;
        }

        private void AddInitialWelcomeMessage()
        {
            string welcomeMessage = _automationService == null
                ? "AutomationService not available. Please restart Visual Studio and try again."
                : "VSPilot AI is ready to assist with your development tasks. What would you like to automate today?";

            AddMessage(new ChatMessage
            {
                Content = welcomeMessage,
                IsUser = false,
                Timestamp = DateTime.Now
            });
        }

        private void ClearChat()
        {
            try
            {
                if (IsProcessing)
                {
                    _logger?.LogWarning("Cannot clear chat while processing");
                    return;
                }

                ChatHistory.Clear();
                AddInitialWelcomeMessage();
                _chatInput.Clear();
            }
            catch (Exception ex)
            {
                HandleError($"Error clearing chat: {ex.Message}");
                _logger?.LogError(ex, "Failed to clear chat");
            }
        }

        private bool CanSendMessage() =>
            !string.IsNullOrWhiteSpace(UserInput) && !IsProcessing;

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserInput)) return;

            var messageText = UserInput.Trim();
            UserInput = string.Empty;

            try
            {
                IsProcessing = true;

                // Add user message
                AddMessage(new ChatMessage
                {
                    Content = messageText,
                    IsUser = true,
                    Timestamp = DateTime.Now
                });

                await ProcessUserRequestAsync(messageText);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing message");
                HandleError($"Error processing request: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ProcessUserRequestAsync(string message)
        {
            try
            {
                // Add processing message
                var processingMessage = new ChatMessage
                {
                    Content = "Processing your request...",
                    IsUser = false,
                    Timestamp = DateTime.Now
                };
                AddMessage(processingMessage);

                if (_automationService == null)
                {
                    // Remove the processing message
                    ChatHistory.Remove(processingMessage);

                    AddMessage(new ChatMessage
                    {
                        Content = "Error: AutomationService is not available. Please restart Visual Studio.",
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });
                    return;
                }

                try
                {
                    // Get a direct chat response
                    string response = await _automationService.GetChatResponseAsync(message);

                    // Remove the processing message
                    ChatHistory.Remove(processingMessage);

                    // Add the AI response
                    AddMessage(new ChatMessage
                    {
                        Content = response,
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });
                }
                catch (Exception ex)
                {
                    // Remove the processing message
                    ChatHistory.Remove(processingMessage);

                    // Add error message
                    AddMessage(new ChatMessage
                    {
                        Content = $"Error processing request: {ex.Message}",
                        IsUser = false,
                        Timestamp = DateTime.Now
                    });
                    _logger?.LogError(ex, "Error processing chat request");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Automation service request failed");
                AddMessage(new ChatMessage
                {
                    Content = $"Error: {ex.Message}",
                    IsUser = false,
                    Timestamp = DateTime.Now
                });
            }
        }

        private void AddMessage(ChatMessage message)
        {
            ChatHistory.Add(message);
        }

        private void OnChatHistoryChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                OnPropertyChanged(nameof(ChatHistory));
            }
        }

        private void HandleError(string errorMessage)
        {
            AddMessage(new ChatMessage
            {
                Content = errorMessage,
                IsUser = false,
                Timestamp = DateTime.Now
            });
            ErrorOccurred?.Invoke(this, errorMessage);
            _logger?.LogWarning(errorMessage);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    ((INotifyCollectionChanged)ChatHistory).CollectionChanged -= OnChatHistoryChanged;
                }
                _disposed = true;
            }
        }
    }
}
