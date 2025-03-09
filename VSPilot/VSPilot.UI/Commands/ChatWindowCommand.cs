using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Threading.Tasks;
using VSPilot.UI.Windows;

namespace VSPilot.UI.Commands
{
    /// <summary>
    /// Command handler for opening the VSPilot chat window.
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
            LogExtended($"ChatWindowCommand: Constructor called for package {_package.GetType().FullName}");
        }

        private IServiceProvider ServiceProvider => _package;

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            LogExtendedStatic("ChatWindowCommand: InitializeAsync started");
            try
            {
                // Switch to the UI thread as adding the command requires it.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                LogExtendedStatic("ChatWindowCommand: Successfully switched to UI thread");

                // Retrieve the OleMenuCommandService from the package.
                OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
                if (commandService == null)
                {
                    throw new InvalidOperationException("Failed to get IMenuCommandService");
                }
                LogExtendedStatic("ChatWindowCommand: Successfully got IMenuCommandService");

                // Create the command ID using the VSCT file's GUID and ID.
                CommandID menuCommandID = new CommandID(VSPilotGuids.CommandSet, VSPilotIds.ChatWindowCommandId);
                LogExtendedStatic($"ChatWindowCommand: Created CommandID with GUID={menuCommandID.Guid} and ID={menuCommandID.ID}");

                // Create the menu item with the Execute method as handler.
                Instance = new ChatWindowCommand(package);
                MenuCommand menuItem = new MenuCommand(Instance.Execute, menuCommandID);
                LogExtendedStatic("ChatWindowCommand: Created MenuCommand with handler 'Execute'");

                // Add the command to the service.
                commandService.AddCommand(menuItem);
                LogExtendedStatic("ChatWindowCommand: Successfully added command to service");
                LogExtendedStatic("ChatWindowCommand: Instance created successfully");
            }
            catch (Exception ex)
            {
                LogExtendedStatic($"ChatWindowCommand: Exception in InitializeAsync: {ex.Message}");
                LogExtendedStatic($"ChatWindowCommand: Stack trace: {ex.StackTrace}");
            }
            LogExtendedStatic("ChatWindowCommand: InitializeAsync completed");
        }

        /// <summary>
        /// Callback for executing the command when the menu item is clicked.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            LogExtended("ChatWindowCommand: Execute method called");

            try
            {
                LogExtended("ChatWindowCommand: Starting async window creation");

                // Use the package's JoinableTaskFactory for thread safety.
                var joinableTask = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        LogExtended("ChatWindowCommand: Yielding to avoid blocking");
                        await Task.Yield();
                        LogExtended("ChatWindowCommand: After Task.Yield()");

                        // Break long operations into small delays to allow message pumping.
                        for (int i = 0; i < 5; i++)
                        {
                            await Task.Delay(10);
                            LogExtended($"ChatWindowCommand: Small delay {i} complete");
                        }

                        // Switch back to UI thread.
                        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                        LogExtended("ChatWindowCommand: Switched to UI thread for window creation");

                        // Create or find the tool window.
                        ToolWindowPane window = _package.FindToolWindow(typeof(ChatWindow), 0, true);
                        LogExtended("ChatWindowCommand: Found/created tool window");

                        if (window == null || window.Frame == null)
                        {
                            LogExtended("ChatWindowCommand: Window or frame is null");
                            throw new NotSupportedException("Cannot create tool window");
                        }

                        IVsWindowFrame frame = (IVsWindowFrame)window.Frame;
                        LogExtended("ChatWindowCommand: Got window frame, now showing window");

                        // Attempt to show the window.
                        int hr = frame.Show();
                        if (Microsoft.VisualStudio.ErrorHandler.Failed(hr))
                        {
                            LogExtended($"ChatWindowCommand: Failed to show window, hr=0x{hr:X}");
                            throw new InvalidOperationException($"Failed to show window, hr=0x{hr:X}");
                        }

                        LogExtended("ChatWindowCommand: Window shown successfully");
                    }
                    catch (Exception ex)
                    {
                        LogExtended($"ChatWindowCommand: Async exception: {ex.Message}");
                        await ShowErrorMessageAsync("Error opening chat window", ex);
                    }
                });

                // Observe the underlying Task by calling Forget() on the Task property.
                joinableTask.Task.Forget();
                LogExtended("ChatWindowCommand: Execute completed - window creation proceeding asynchronously");
            }
            catch (Exception ex)
            {
                LogExtended($"ChatWindowCommand: Exception in Execute: {ex.Message}");
                LogExtended($"ChatWindowCommand: Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Displays an error message using a message box.
        /// </summary>
        private async Task ShowErrorMessageAsync(string message, Exception ex)
        {
            LogExtended($"ChatWindowCommand: ShowErrorMessageAsync - About to display error: {message}, Exception: {ex.Message}");
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                IVsUIShell uiShell = await _package.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell != null)
                {
                    Guid clsid = Guid.Empty;
                    int result = uiShell.ShowMessageBox(
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
                        out int _
                    );
                    LogExtended($"ChatWindowCommand: ShowErrorMessageAsync - MessageBox displayed with result: 0x{result:X}");
                }
                else
                {
                    LogExtended("ChatWindowCommand: ShowErrorMessageAsync - Failed to retrieve IVsUIShell service");
                }
            }
            catch (Exception msgEx)
            {
                LogExtended($"ChatWindowCommand: Error in ShowErrorMessageAsync: {msgEx.Message}");
            }
            LogExtended("ChatWindowCommand: ShowErrorMessageAsync - Finished");
        }

        // Extended logging helper that writes to both Debug and Console.
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
