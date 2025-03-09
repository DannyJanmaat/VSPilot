using System;
using System.Threading.Tasks;
using System.Windows.Input;
using VSPilot.Common.Commands;
using VSPilot.Common.ViewModels;

namespace VSPilot.UI.ViewModels
{
    public class AuthViewModel : ViewModelBase
    {
        private string _authButtonText = "Sign In";
        private string _authButtonSubtext = string.Empty;
        private string _authHyperlinkText = string.Empty;
        private object? _authButtonIcon;
        private bool _isAuthenticated;

        public string AuthButtonText
        {
            get => _authButtonText;
            set => SetProperty(ref _authButtonText, value);
        }

        public string AuthButtonSubtext
        {
            get => _authButtonSubtext;
            set => SetProperty(ref _authButtonSubtext, value);
        }

        public string AuthHyperlinkText
        {
            get => _authHyperlinkText;
            set => SetProperty(ref _authHyperlinkText, value);
        }

        public object? AuthButtonIcon
        {
            get => _authButtonIcon;
            set => SetProperty(ref _authButtonIcon, value);
        }

        public bool IsAuthenticated
        {
            get => _isAuthenticated;
            private set => SetProperty(ref _isAuthenticated, value);
        }

        public ICommand AuthCommand { get; }

        public AuthViewModel()
        {
            AuthCommand = new RelayCommand(ExecuteAuthAsync, CanExecuteAuth);
        }

        private bool CanExecuteAuth() => !IsAuthenticated;

        private async Task ExecuteAuthAsync()
        {
            try
            {
                AuthButtonText = "Authenticating...";
                AuthButtonSubtext = "Please wait";

                // Simulate authentication (replace with actual auth logic)
                await Task.Delay(2000);

                IsAuthenticated = true;
                AuthButtonText = "Signed In";
                AuthButtonSubtext = "Welcome to VSPilot";
                AuthHyperlinkText = "Manage Account";
            }
            catch (Exception ex)
            {
                IsAuthenticated = false;
                AuthButtonText = "Sign In Failed";
                AuthButtonSubtext = ex.Message;
                AuthHyperlinkText = "Retry";
            }
        }
    }
}