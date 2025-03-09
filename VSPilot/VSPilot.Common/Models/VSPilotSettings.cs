using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VSPilot.Common.Models
{
    public enum AIProvider
    {
        GitHubCopilot,
        Anthropic,
        OpenAI,
        Auto
    }

    public class VSPilotSettings : INotifyPropertyChanged
    {
        private bool _autoBuildAfterChanges = true;
        private bool _autoRunTests = true;
        private bool _autoFixErrors = true;
        private int _maxAutoFixAttempts = 3;
        private bool _showDetailedLogs = false;
        private string[] _preferredFolderStructure = new[]
        {
            "Models",
            "ViewModels",
            "Views",
            "Services",
            "Interfaces",
            "Helpers"
        };

        // AI Provider settings
        private AIProvider _selectedAIProvider = AIProvider.Auto;
        private bool _useGitHubCopilot = true;
        private string _openAIApiKey = string.Empty;
        private string _anthropicApiKey = string.Empty;
        private bool _autoSwitchAIProviders = true;
        private string _preferredModel = string.Empty;

        public bool AutoBuildAfterChanges
        {
            get => _autoBuildAfterChanges;
            set => SetProperty(ref _autoBuildAfterChanges, value);
        }

        public bool AutoRunTests
        {
            get => _autoRunTests;
            set => SetProperty(ref _autoRunTests, value);
        }

        public bool AutoFixErrors
        {
            get => _autoFixErrors;
            set => SetProperty(ref _autoFixErrors, value);
        }

        public int MaxAutoFixAttempts
        {
            get => _maxAutoFixAttempts;
            set => SetProperty(ref _maxAutoFixAttempts, value);
        }

        public bool ShowDetailedLogs
        {
            get => _showDetailedLogs;
            set => SetProperty(ref _showDetailedLogs, value);
        }

        public string[] PreferredFolderStructure
        {
            get => _preferredFolderStructure;
            set => SetProperty(ref _preferredFolderStructure, value);
        }

        // AI Provider properties
        public AIProvider SelectedAIProvider
        {
            get => _selectedAIProvider;
            set => SetProperty(ref _selectedAIProvider, value);
        }

        public bool UseGitHubCopilot
        {
            get => _useGitHubCopilot;
            set => SetProperty(ref _useGitHubCopilot, value);
        }

        public string OpenAIApiKey
        {
            get => _openAIApiKey;
            set => SetProperty(ref _openAIApiKey, value);
        }

        public string AnthropicApiKey
        {
            get => _anthropicApiKey;
            set => SetProperty(ref _anthropicApiKey, value);
        }

        public bool AutoSwitchAIProviders
        {
            get => _autoSwitchAIProviders;
            set => SetProperty(ref _autoSwitchAIProviders, value);
        }

        public string PreferredModel
        {
            get => _preferredModel;
            set => SetProperty(ref _preferredModel, value);
        }

        // Helper methods for AI provider validation
        public bool IsGitHubCopilotConfigured => UseGitHubCopilot;

        public bool IsOpenAIConfigured => !string.IsNullOrWhiteSpace(OpenAIApiKey);

        public bool IsAnthropicConfigured => !string.IsNullOrWhiteSpace(AnthropicApiKey);

        public bool HasAnyAIConfigured => IsGitHubCopilotConfigured || IsOpenAIConfigured || IsAnthropicConfigured;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(storage, value)) return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
