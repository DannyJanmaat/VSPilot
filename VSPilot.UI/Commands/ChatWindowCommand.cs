// File: VSPilot.UI/Commands/ChatWindowCommand.cs

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
                if (commandService == null)
                {
                    throw new InvalidOperationException("Failed to get IMenuCommandService");
                }
                if (commandService != null)
                {
                    Debug.WriteLine("ChatWindowCommand: Successfully got IMenuCommandService");

                    // Create the command ID with the GUID from the VSCT file
                    CommandID menuCommandID = new CommandID(VSPilotGuids.CommandSet, VSPilotIds.ChatWindowCommandId);
                    Debug.WriteLine($"ChatWindowCommand: Created CommandID with GUID={menuCommandID.Guid} and ID={menuCommandID.ID}");

                    // Create the menu item and handler
                    Instance = new ChatWindowCommand(package);
                    MenuCommand menuItem = new MenuCommand(Instance.Execute, menuCommandID);
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
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine("ChatWindowCommand: Execute method called");

            try
            {
                Debug.WriteLine("ChatWindowCommand: Starting async window creation");

                // Use the package's JoinableTaskFactory for thread safety and proper async
                var task = _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        // First yield control to ensure non-blocking behavior
                        await Task.Yield();
                        Debug.WriteLine("ChatWindowCommand: After Task.Yield()");

                        // Break up long operations into smaller chunks to prevent UI freezing
                        for (int i = 0; i < 5; i++)
                        {
                            await Task.Delay(10);
                            Debug.WriteLine($"ChatWindowCommand: Small delay {i} complete");
                        }

                        // Switch back to UI thread safely
                        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                        Debug.WriteLine("ChatWindowCommand: Switched to UI thread for window creation");

                        // Now create the window
                        ToolWindowPane window = _package.FindToolWindow(typeof(ChatWindow), 0, true);
                        Debug.WriteLine("ChatWindowCommand: Found/created tool window");

                        if ((null == window) || (null == window.Frame))
                        {
                            Debug.WriteLine("ChatWindowCommand: Window or frame is null");
                            throw new NotSupportedException("Cannot create tool window");
                        }

                        IVsWindowFrame frame = (IVsWindowFrame)window.Frame;
                        Debug.WriteLine("ChatWindowCommand: Got window frame, showing window");

                        // Show the window, but handle any error
                        int hr = frame.Show();
                        if (Microsoft.VisualStudio.ErrorHandler.Failed(hr))
                        {
                            Debug.WriteLine($"ChatWindowCommand: Failed to show window, hr=0x{hr:X}");
                            throw new InvalidOperationException($"Failed to show window, hr=0x{hr:X}");
                        }

                        Debug.WriteLine("ChatWindowCommand: Window shown successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ChatWindowCommand: Async exception: {ex.Message}");
                        await ShowErrorMessageAsync("Error opening chat window", ex);
                    }
                });

                // Use Task.Forget() extension method from Microsoft.VisualStudio.Threading
                task.Task.Forget();

                Debug.WriteLine("ChatWindowCommand: Execute completed - window creation proceeding asynchronously");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChatWindowCommand: Exception in Execute: {ex.Message}");
                Debug.WriteLine($"ChatWindowCommand: Stack trace: {ex.StackTrace}");
            }
        }

        private async Task ShowErrorMessageAsync(string message, Exception ex)
        {
            Debug.WriteLine($"ChatWindowCommand: ShowErrorMessageAsync - Showing error message box: {message}, Exception: {ex.Message}");
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                IVsUIShell uiShell = await _package.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell != null)
                {
                    Guid clsid = Guid.Empty;
                    _ = uiShell.ShowMessageBox(
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