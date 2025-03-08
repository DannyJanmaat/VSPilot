using System;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Shell;

namespace VSPilot.Common.Commands
{
    public class RelayCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter)
        {
            ExecuteAsync().FireAndForget();
        }

        private async Task ExecuteAsync()
        {
            try
            {
                await _executeAsync();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex);
            }
        }

        private async Task HandleExceptionAsync(Exception ex)
        {
            // Switch to UI thread using JoinableTaskFactory
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            MessageBox.Show(
                $"An error occurred: {ex.Message}",
                "Command Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    // Extension method for fire and forget
    public static class TaskExtensions
    {
        public static void FireAndForget(this Task task, Action<Exception>? onException = null)
        {
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception?.InnerException != null)
                {
                    onException?.Invoke(t.Exception.InnerException);
                }
            }, TaskScheduler.Default);
        }
    }
}
