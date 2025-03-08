using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using VSPilot.Core.Services;
using VSPilot.UI.Windows;
using VSPilot.UI.ViewModels;
using Task = System.Threading.Tasks.Task;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using VSPilot.Common.Utilities; // Add the ConsoleHelper namespace

namespace VSPilot.UI.Commands
{
    internal sealed class SettingsCommand
    {
        private readonly AsyncPackage _package;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConfigurationService _configService;
        private readonly ILoggerFactory _loggerFactory;

        public SettingsCommand(
            AsyncPackage package,
            IServiceProvider serviceProvider,
            ConfigurationService configService,
            ILoggerFactory loggerFactory)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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

                // Get required services
                var serviceProvider = await package.GetServiceAsync(typeof(IServiceProvider)) as IServiceProvider;
                var configService = package.GetService<ConfigurationService, ConfigurationService>();
                var loggerFactory = package.GetService<ILoggerFactory, ILoggerFactory>();

                if (serviceProvider == null || configService == null || loggerFactory == null)
                {
                    LogExtendedStatic("SettingsCommand: Failed to resolve required services!");
                    return;
                }

                // Create command instance
                Instance = new SettingsCommand(package, serviceProvider, configService, loggerFactory);

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
                LogExtended("SettingsCommand: Creating SettingsViewModel");
                var settingsLogger = _loggerFactory.CreateLogger<SettingsViewModel>();

                LogExtended("SettingsCommand: Creating SettingsWindow");
                var window = new SettingsWindow(_configService, settingsLogger)
                {
                    DataContext = new SettingsViewModel(_configService, settingsLogger)
                };

                LogExtended("SettingsCommand: Showing SettingsWindow dialog");
                window.ShowDialog();
                LogExtended("SettingsCommand: SettingsWindow dialog closed");
            }
            catch (Exception ex)
            {
                LogExtended($"SettingsCommand: Exception in Execute: {ex.Message}");
                LogExtended($"SettingsCommand: Stack trace: {ex.StackTrace}");
                var logger = _loggerFactory.CreateLogger<SettingsCommand>();
                logger?.LogError(ex, "Error opening settings window");
                MessageBox.Show(
                    $"Could not open settings: {ex.Message}",
                    "VSPilot Settings Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
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
