using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using Task = System.Threading.Tasks.Task;
using System.Windows.Forms;
using VSPilot.UI.Dialogs;

namespace VSPilot.UI.Commands
{
    internal sealed class SettingsCommand
    {
        private readonly AsyncPackage _package;

        private SettingsCommand(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            LogExtended($"SettingsCommand: Constructor called for package {_package.GetType().FullName}");
        }

        public static SettingsCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            LogExtendedStatic("SettingsCommand: InitializeAsync started");
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                LogExtendedStatic("SettingsCommand: Switched to main thread");

                // Get the command service directly from the package
                OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
                if (commandService == null)
                {
                    LogExtendedStatic("SettingsCommand: Failed to get IMenuCommandService!");
                    return;
                }
                LogExtendedStatic("SettingsCommand: Successfully got IMenuCommandService");

                // Create command instance with minimal dependencies
                Instance = new SettingsCommand(package);

                // Create command ID
                var menuCommandID = new CommandID(VSPilot.VSPilotGuids.CommandSet, VSPilot.VSPilotIds.SettingsCommandId);
                LogExtendedStatic($"SettingsCommand: Created CommandID with GUID={menuCommandID.Guid} and ID={menuCommandID.ID}");

                // Create menu item
                var menuItem = new MenuCommand(Instance.Execute, menuCommandID);
                LogExtendedStatic("SettingsCommand: Created MenuCommand with handler 'Execute'");

                // Add command to service
                commandService.AddCommand(menuItem);
                LogExtendedStatic("SettingsCommand: Successfully added command to service");
                LogExtendedStatic("SettingsCommand: Instance created successfully");
            }
            catch (Exception ex)
            {
                LogExtendedStatic($"SettingsCommand: Exception in InitializeAsync: {ex.Message}");
                LogExtendedStatic($"SettingsCommand: Stack trace: {ex.StackTrace}");
            }
            LogExtendedStatic("SettingsCommand: InitializeAsync completed");
        }

        private void Execute(object sender, EventArgs e)
        {
            LogExtended("SettingsCommand: Execute method called");
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                LogExtended("SettingsCommand: Creating SettingsDialog");

                // Use the simpler WinForms-based SettingsDialog
                var settingsDialog = new SettingsDialog();

                LogExtended("SettingsCommand: Showing SettingsDialog");
                settingsDialog.ShowDialog();

                LogExtended("SettingsCommand: SettingsDialog closed");
            }
            catch (Exception ex)
            {
                LogExtended($"SettingsCommand: Exception in Execute: {ex.Message}");
                LogExtended($"SettingsCommand: Stack trace: {ex.StackTrace}");
                MessageBox.Show(
                    $"Could not open settings: {ex.Message}",
                    "VSPilot Settings Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        // Extended logging helper that writes to both the Debug output and console.
        private void LogExtended(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
        }

        // Static version for use in static methods.
        private static void LogExtendedStatic(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
        }
    }
}
