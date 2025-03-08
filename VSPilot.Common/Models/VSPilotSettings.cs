using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VSPilot.Common.Models
{
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