using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using VSPilot.UI.Windows;
using System.Diagnostics;
using System.Windows;
using System.Linq;

namespace VSPilot.UI.Commands
{
    /// <summary>
    /// Command handler for opening the VSPilot chat window
    /// </summary>
    internal sealed class ChatWindowCommand
    {
        private readonly AsyncPackage _package;

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static ChatWindowCommand Instance { get; private set; }

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private ChatWindowCommand(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            Debug.WriteLine($"ChatWindowCommand: Constructor called for package {package.GetType().FullName}");
        }

        private IServiceProvider ServiceProvider => _package;

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            Debug.WriteLine("ChatWindowCommand: InitializeAsync started");
            try
            {
                // Switch to the main thread - the call to AddCommand in the constructor requires the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                Debug.WriteLine("ChatWindowCommand: Successfully switched to UI thread");

                // Get the OleMenuCommandService directly from the package
                OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
                if (commandService != null)
                {
                    Debug.WriteLine("ChatWindowCommand: Successfully got IMenuCommandService");

                    // Create the command ID with the GUID from the VSCT file
                    var menuCommandID = new CommandID(VSPilotGuids.CommandSet, VSPilotIds.ChatWindowCommandId);
                    Debug.WriteLine($"ChatWindowCommand: Created CommandID with GUID={menuCommandID.Guid} and ID={menuCommandID.ID}");

                    // Create the menu item and handler
                    Instance = new ChatWindowCommand(package);
                    var menuItem = new MenuCommand(Instance.Execute, menuCommandID);
                    Debug.WriteLine($"ChatWindowCommand: Created MenuCommand with handler 'Execute'");

                    // Add the command to the service
                    commandService.AddCommand(menuItem);
                    Debug.WriteLine($"ChatWindowCommand: Successfully added command to service");
                    Debug.WriteLine("ChatWindowCommand: Instance created successfully");
                }
                else
                {
                    Debug.WriteLine("ChatWindowCommand: Failed to get IMenuCommandService!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindowCommand: Exception in InitializeAsync: {ex.Message}");
                Debug.WriteLine($"ChatWindowCommand: Stack trace: {ex.StackTrace}");
            }
            Debug.WriteLine("ChatWindowCommand: InitializeAsync completed");
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            Debug.WriteLine("ChatWindowCommand: Execute method called");

            _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // Simply show the tool window without diagnostic message boxes
                    ToolWindowPane window = await _package.ShowToolWindowAsync(
                        typeof(ChatWindow),
                        0,
                        true,
                        _package.DisposalToken);

                    if (window?.Frame == null)
                    {
                        Debug.WriteLine("ChatWindowCommand: Window or frame is null");
                        return;
                    }

                    var windowFrame = (IVsWindowFrame)window.Frame;
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ChatWindowCommand: Exception in Execute - {ex.Message}");
                    await ShowErrorMessageAsync("Error opening chat window", ex);
                }
            }).FileAndForget("ChatWindowCommand/Execute");
        }

        private async Task ExecuteAsync()
        {
            Debug.WriteLine("ChatWindowCommand: ExecuteAsync method started");

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                ToolWindowPane window = await _package.ShowToolWindowAsync(
                    typeof(ChatWindow),
                    0,
                    true,
                    _package.DisposalToken);

                if (window == null)
                {
                    Debug.WriteLine("ChatWindowCommand: ToolWindowPane 'window' is NULL after ShowToolWindowAsync!");
                    throw new NotSupportedException("Cannot create chat window (ToolWindowPane is null)");
                }

                if (window.Frame == null)
                {
                    Debug.WriteLine("ChatWindowCommand: window.Frame is NULL!");
                    throw new NotSupportedException("Cannot create chat window (window.Frame is null)");
                }

                var windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
                Debug.WriteLine("ChatWindowCommand: Tool window shown successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindowCommand: Failed to show tool window - {ex.Message}");
                await ShowErrorMessageAsync("Error opening chat window", ex);
            }
        }

        private async Task ShowErrorMessageAsync(string message, Exception ex)
        {
            Debug.WriteLine($"ChatWindowCommand: ShowErrorMessageAsync - Showing error message box: {message}, Exception: {ex.Message}");
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var uiShell = await _package.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell != null)
                {
                    Guid clsid = Guid.Empty;
                    uiShell.ShowMessageBox(
                        0,
                        ref clsid,
                        "VSPilot Error",
                        $"{message}\n\nDetails: {ex.Message}",
                        string.Empty,
                        0,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        0,
                        out int result
                    );
                    Debug.WriteLine("ChatWindowCommand: ShowErrorMessageAsync - MessageBox displayed");
                }
                else
                {
                    Debug.WriteLine("ChatWindowCommand: ShowErrorMessageAsync - Failed to get IVsUIShell service!");
                }
            }
            catch (Exception msgEx)
            {
                Debug.WriteLine($"ChatWindowCommand: Error in ShowErrorMessageAsync: {msgEx.Message}");
            }
            Debug.WriteLine("ChatWindowCommand: ShowErrorMessageAsync - Finished");
        }
    }
}