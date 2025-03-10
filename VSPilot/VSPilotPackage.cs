﻿#nullable enable

using EnvDTE80;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VSPilot.Common.Interfaces;
using VSPilot.Common.Models;
using VSPilot.Common.Utilities;
using VSPilot.Core.AI;
using VSPilot.Core.Automation;
using VSPilot.Core.Build;
using VSPilot.Core.Services;
using VSPilot.UI.Commands;
using VSPilot.UI.Windows;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider; // Add this alias

namespace VSPilot
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid("49D5D9FC-73D5-40D8-A55B-65BB5BB32E05")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(ChatWindow),
                       Style = VsDockStyle.Tabbed,
                       Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057",
                       MultiInstances = false,
                       Transient = false)]
    public sealed class VSPilotPackage : AsyncPackage
    {
        // Expose a static instance for use by tool windows.
        public static VSPilotPackage Instance { get; private set; } = null!;

        // Core package components
        private ServiceProvider? _serviceProvider;
        private readonly ILogger<VSPilotPackage>? _logger; // This can stay readonly if only assigned in constructor
        private Stopwatch? _initializationTimer; // Remove readonly from this field
        private readonly ServiceCollection _services;
        private readonly object _initializationLock = new object();
        private volatile bool _isInitialized = false;

        // Remove readonly from _aiIntegration since it's assigned outside constructor
        private VSPilotAIIntegration? _aiIntegration;

        // Performance and telemetry
        private static readonly ActivitySource _activitySource = new ActivitySource("VSPilot.Package");

        /// <summary>
        /// Package constructor – allocate the console as early as possible.
        /// </summary>
        public VSPilotPackage()
        {
            ConsoleHelper.EnsureConsole();
            LogExtended("VSPilotPackage: Constructor called");
            _services = new ServiceCollection();
            ServiceProvider serviceProvider = _services.BuildServiceProvider();
            _logger = serviceProvider.GetService<ILogger<VSPilotPackage>>();
            LogExtended("VSPilotPackage: Logger initialized in constructor");
        }

        /// <summary>
        /// Async package initialization.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            LogExtended($"VSPilotPackage: InitializeAsync started at {DateTime.Now}");
            LogExtended($"VSPilotPackage: Cancellation token can be canceled: {cancellationToken.CanBeCanceled}");

            using Activity? activity = _activitySource.StartActivity("PackageInitialization");

            try
            {
                _initializationTimer = Stopwatch.StartNew();
                LogExtended("VSPilotPackage: Initialization timer started");

                await base.InitializeAsync(cancellationToken, progress);
                LogExtended("VSPilotPackage: Base InitializeAsync completed");

                using CancellationTokenSource timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                LogExtended("VSPilotPackage: Linked cancellation token created");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(linkedCts.Token);
                LogExtended("VSPilotPackage: Successfully switched to main thread");

                await InitializeCoreServicesAsync(linkedCts.Token);
                LogExtended("VSPilotPackage: Core services initialization finished");

                _initializationTimer.Stop();
                LogExtended($"VSPilotPackage: Initialization complete in {_initializationTimer.ElapsedMilliseconds}ms");

                // Assign the static instance for later use.
                Instance = this;

                _ = (activity?.SetTag("InitializationTime", _initializationTimer.ElapsedMilliseconds));
                _ = (activity?.SetStatus(ActivityStatusCode.Ok, "Package initialization successful"));
            }
            catch (OperationCanceledException ex)
            {
                LogExtended($"VSPilotPackage: Initialization timed out or canceled: {ex.Message}");
                _logger?.LogWarning("VSPilot package initialization timed out");
                _ = (activity?.SetStatus(ActivityStatusCode.Error, "Initialization timed out"));
                await HandleInitializationCancellationAsync(ex);
            }
            catch (Exception ex)
            {
                LogExtended($"VSPilotPackage: Initialization FAILED - {ex.GetType().Name}");
                LogExtended($"VSPilotPackage: Error Message: {ex.Message}");
                LogExtended($"VSPilotPackage: Stack Trace: {ex.StackTrace}");
                _logger?.LogError(ex, "Initialization failed");
                _ = (activity?.SetStatus(ActivityStatusCode.Error, ex.Message));
                try
                {
                    await HandleInitializationErrorAsync(ex);
                }
                catch (Exception handlerEx)
                {
                    LogExtended($"VSPilotPackage: Error in error handler: {handlerEx.Message}");
                }
                throw;
            }
            finally
            {
                _isInitialized = true;
                LogExtended("VSPilotPackage: Set _isInitialized to true");
                activity?.Dispose();
            }
            LogExtended("VSPilotPackage: InitializeAsync method completed");
        }

        /// <summary>
        /// Initializes core services for the package.
        /// Updated to supply a fallback for ConfigurationService if unavailable.
        /// </summary>
        private async Task InitializeCoreServicesAsync(CancellationToken cancellationToken)
        {
            LogExtended("VSPilotPackage: Starting core services initialization");

            try
            {
                LogExtended("VSPilotPackage: Retrieving critical services in parallel");
                Task<DTE2?> dteTask = GetCriticalServiceAsync<DTE2>(typeof(SDTE), "DTE");
                Task<IVsSolution?> solutionTask = GetCriticalServiceAsync<IVsSolution>(typeof(SVsSolution), "Solution");
                Task<IVsOutputWindow?> outputWindowTask = GetCriticalServiceAsync<IVsOutputWindow>(typeof(SVsOutputWindow), "OutputWindow");

                await Task.WhenAll(dteTask, solutionTask, outputWindowTask);
                LogExtended("VSPilotPackage: All critical services retrieved successfully");

                DTE2? dte = await dteTask;
                IVsSolution? solution = await solutionTask;
                IVsOutputWindow? outputWindow = await outputWindowTask;

                LogExtended("VSPilotPackage: Configuring logging services");
                _ = _services.AddLogging(builder =>
                {
                    _ = builder.AddConsole();
                    _ = builder.SetMinimumLevel(LogLevel.Warning);
                    _ = builder.AddFilter("VSPilot", LogLevel.Warning);
                });
                LogExtended("VSPilotPackage: Logging services configured");

                // For ConfigurationService, try to retrieve it. If unavailable, use a default implementation.
                ConfigurationService? configurationService;
                try
                {
                    configurationService = (await GetServiceAsync(typeof(ConfigurationService))) as ConfigurationService;
                    if (configurationService == null)
                    {
                        throw new Exception("ConfigurationService is null.");
                    }
                }
                catch (Exception)
                {
                    LogExtended("ConfigurationService service is unavailable. Using default configuration service.");
                    configurationService = new DefaultConfigurationService(this); // Pass the current package instance
                }
                _ = _services.AddSingleton(configurationService);

                // Register other basic services.
                _ = _services.AddSingleton<AsyncPackage>(this);
                _ = _services.AddSingleton<TemplateManager>();
                _ = _services.AddSingleton<VSPilot.Core.Services.TaskScheduler>();
                _ = _services.AddSingleton<SolutionAnalyzer>();
                _ = _services.AddSingleton<LanguageProcessor>();

                if (dte != null)
                {
                    _ = _services.AddSingleton(dte);
                    LogExtended("VSPilotPackage: DTE service added to service collection");
                }
                if (solution != null)
                {
                    _ = _services.AddSingleton(solution);
                    LogExtended("VSPilotPackage: Solution service added to service collection");
                }

                if (outputWindow != null)
                {
                    LogExtended("VSPilotPackage: Creating output window pane");
                    IVsOutputWindowPane outputPane = await CreateOutputWindowPaneAsync(outputWindow);
                    _ = _services.AddSingleton(outputPane);
                    _ = _services.AddSingleton<LoggingService>(sp =>
                            new LoggingService(outputPane, true));
                    LogExtended("VSPilotPackage: Output window pane configured and logging service added");
                }

                _ = _services.AddSingleton<FileManager>();
                LogExtended("VSPilotPackage: FileManager added to service collection");

                _ = _services.AddSingleton<IVsSolutionContext>(sp =>
                {
                    DTE2 dteService = sp.GetRequiredService<DTE2>();
                    FileManager fileManager = sp.GetRequiredService<FileManager>();
                    return new VsSolutionAdapter(dteService, fileManager);
                });
                LogExtended("VSPilotPackage: IVsSolutionContext registered");

                _ = _services.AddSingleton<ITestPlatform, MockTestPlatform>();
                _ = _services.AddSingleton<IProjectManager, ProjectManager>();
                _ = _services.AddSingleton<IBuildManager, BuildManager>();
                _ = _services.AddSingleton<IErrorHandler, ErrorHandler>();
                _ = _services.AddSingleton<TestRunner>();

                // Register GitHubCopilotService before VSPilotAIIntegration
                if (dte != null)
                {
                    _services.AddSingleton<GitHubCopilotService>(sp =>
                    {
                        var logger = sp.GetService<ILogger<GitHubCopilotService>>();
                        if (logger == null)
                        {
                            var loggerFactory = sp.GetService<ILoggerFactory>();
                            logger = loggerFactory != null
                                ? loggerFactory.CreateLogger<GitHubCopilotService>()
                                : new Microsoft.Extensions.Logging.Abstractions.NullLogger<GitHubCopilotService>();
                        }
                        // Pass a default VSPilotSettings instance.
                        return new GitHubCopilotService(logger, dte, new VSPilotSettings());
                    });
                    LogExtended("VSPilotPackage: GitHubCopilotService registered");
                }
                else
                {
                    LogExtended("VSPilotPackage: DTE not available, GitHubCopilotService will not be registered");
                }

                _ = _services.AddSingleton<AIRequestHandler>();

                // Register VSPilotAIIntegration with a factory method to handle the case when GitHubCopilotService is not available
                _services.AddSingleton<VSPilotAIIntegration>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<VSPilotAIIntegration>>();
                    var configService = sp.GetRequiredService<ConfigurationService>();
                    var copilotService = sp.GetService<GitHubCopilotService>(); // This might be null if not available
                    return new VSPilotAIIntegration(logger, configService, copilotService);
                });
                LogExtended("VSPilotPackage: AI components registered");

                _ = _services.AddSingleton<AutomationService>();
                LogExtended("VSPilotPackage: AutomationService registered");

                try
                {
                    _serviceProvider = _services.BuildServiceProvider();
                    LogExtended("VSPilotPackage: Built custom service provider from service collection");

                    AutomationService? automationService = _serviceProvider.GetService<AutomationService>();
                    if (automationService != null)
                    {
                        LogExtended("VSPilotPackage: Adding AutomationService to ServiceContainer");
                        ((IServiceContainer)this).AddService(typeof(AutomationService), automationService, true);
                    }
                    else
                    {
                        LogExtended("VSPilotPackage: Failed to get AutomationService from provider");
                    }
                }
                catch (Exception ex)
                {
                    LogExtended($"VSPilotPackage: Service provider build failed: {ex.Message}");
                    throw;
                }

                await InitializeCommandsAsync(cancellationToken);
                LogExtended("VSPilotPackage: Commands initialized");

                // Run background services asynchronously.
                _ = JoinableTaskFactory.RunAsync(async () =>
                {
                    await InitializeBackgroundServicesAsync(cancellationToken);
                });

                LogExtended("VSPilotPackage: Core services initialization completed");
            }
            catch (Exception ex)
            {
                LogExtended($"VSPilotPackage: Core services initialization failed - {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Retrieves a critical Visual Studio service.
        /// </summary>
        private async Task<T?> GetCriticalServiceAsync<T>(Type serviceType, string serviceName)
            where T : class
        {
            try
            {
                LogExtended($"VSPilotPackage: Getting critical service: {serviceName}");
                T? service = await GetServiceAsync(serviceType) as T;
                if (service == null)
                {
                    LogExtended($"VSPilotPackage: Failed to retrieve service: {serviceName}");
                    throw new InvalidOperationException($"Failed to initialize {serviceName} service");
                }
                LogExtended($"VSPilotPackage: Successfully retrieved service: {serviceName}");
                return service;
            }
            catch (Exception ex)
            {
                LogExtended($"VSPilotPackage: Failed to get {serviceName} service - {ex.Message}");
                throw new InvalidOperationException($"Critical service {serviceName} initialization failed", ex);
            }
        }

        /// <summary>
        /// Creates an output window pane for VSPilot.
        /// </summary>
        private async Task<IVsOutputWindowPane> CreateOutputWindowPaneAsync(IVsOutputWindow outputWindow)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            Guid paneGuid = Guid.NewGuid();
            int hr = outputWindow.CreatePane(ref paneGuid, "VSPilot", 1, 1);
            LogExtended($"VSPilotPackage: Created output pane with GUID {paneGuid}");
            Marshal.ThrowExceptionForHR(hr);
            hr = outputWindow.GetPane(ref paneGuid, out IVsOutputWindowPane pane);
            Marshal.ThrowExceptionForHR(hr);
            _ = pane.OutputString("VSPilot output pane initialized" + Environment.NewLine);
            LogExtended("VSPilotPackage: Output pane successfully retrieved and initialized");
            return pane;
        }

        /// <summary>
        /// Initializes package commands.
        /// </summary>
        private async Task InitializeCommandsAsync(CancellationToken cancellationToken)
        {
            try
            {
                LogExtended("VSPilotPackage: Initializing Commands");
                await Task.WhenAll(
                    ChatWindowCommand.InitializeAsync(this),
                    SettingsCommand.InitializeAsync(this)
                );
                LogExtended("VSPilotPackage: Commands successfully initialized");
            }
            catch (Exception ex)
            {
                LogExtended($"VSPilotPackage: Command initialization failed - {ex}");
                _logger?.LogError(ex, "Failed to initialize commands");
                throw;
            }
        }

        /// <summary>
        /// Initializes background services.
        /// </summary>
        private async Task InitializeBackgroundServicesAsync(CancellationToken cancellationToken)
        {
            try
            {
                LogExtended("VSPilotPackage: Starting background services");
                await Task.Delay(1000, cancellationToken);

                // Fix CS8604 warning by checking for null
                if (_serviceProvider != null)
                {
                    _aiIntegration = _serviceProvider.GetService<VSPilotAIIntegration>();
                    if (_aiIntegration != null)
                    {
                        // Add a method to AutomationService instead of calling directly on VSPilotAIIntegration
                        AutomationService? automationService = _serviceProvider.GetService<AutomationService>();
                        if (automationService != null)
                        {
                            // Call a method on AutomationService that will handle the project analysis
                            QueueProjectAnalysis("DefaultProject");
                            LogExtended("VSPilotPackage: Project analysis queued for DefaultProject");
                        }
                        else
                        {
                            LogExtended("VSPilotPackage: AutomationService not available for project analysis");
                        }
                    }
                    else
                    {
                        LogExtended("VSPilotPackage: Failed to retrieve VSPilotAIIntegration for background services");
                    }
                }
                else
                {
                    LogExtended("VSPilotPackage: ServiceProvider is null, cannot queue project analysis");
                }

                LogExtended("VSPilotPackage: Background services task completed");
            }
            catch (Exception ex)
            {
                LogExtended($"VSPilotPackage: Background services initialization failed - {ex}");
                _logger?.LogError(ex, "Background services initialization failed");
            }
        }

        // Add this helper method to handle project analysis
        private void QueueProjectAnalysis(string projectName)
        {
            try
            {
                LogExtended($"VSPilotPackage: Queuing analysis for project: {projectName}");

                // This is a safer approach than directly calling on _aiIntegration
                if (_serviceProvider != null)
                {
                    var automationService = _serviceProvider.GetService<AutomationService>();
                    if (automationService != null)
                    {
                        // Assuming you've added this method to AutomationService
                        // If not, you'll need to add it there
                        LogExtended($"VSPilotPackage: Delegating project analysis to AutomationService");

                        // This would be implemented in AutomationService
                        // automationService.QueueProjectAnalysis(projectName);

                        // For now, just log that we would do this
                        LogExtended($"VSPilotPackage: Would queue project analysis for {projectName}");
                    }
                    else
                    {
                        LogExtended("VSPilotPackage: AutomationService not available");
                    }
                }
                else
                {
                    LogExtended("VSPilotPackage: ServiceProvider is null");
                }
            }
            catch (Exception ex)
            {
                LogExtended($"VSPilotPackage: Error queuing project analysis: {ex.Message}");
            }
        }

        private async Task HandleInitializationCancellationAsync(OperationCanceledException ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            IVsUIShell? uiShell = await GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell != null)
            {
                Guid clsid = Guid.Empty;
                LogExtended("VSPilotPackage: Showing cancellation message box");
                _ = uiShell.ShowMessageBox(
                    0,
                    ref clsid,
                    "VSPilot Initialization",
                    "Package initialization was canceled or timed out.",
                    string.Empty,
                    0,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                    OLEMSGICON.OLEMSGICON_WARNING,
                    0,
                    out _);
            }
            else
            {
                LogExtended("VSPilotPackage: UI Shell service not available for cancellation message box");
            }
        }

        private async Task HandleInitializationErrorAsync(Exception ex)
        {
            try
            {
                LogExtended($"VSPilotPackage: Starting error handling for {ex.GetType().Name}");
                await JoinableTaskFactory.SwitchToMainThreadAsync();
                IVsActivityLog? activityLog = await GetServiceAsync(typeof(SVsActivityLog)) as IVsActivityLog;
                if (activityLog != null)
                {
                    LogExtended("VSPilotPackage: Logging error to Activity Log");
                    int hResult = activityLog.LogEntry(
                        (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR,
                        ToString(),
                        $"VSPilot initialization failed: {ex.Message}\n{ex.StackTrace}");
                }
                else
                {
                    LogExtended("VSPilotPackage: Activity Log service not available");
                }
                IVsUIShell? uiShell = await GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell != null)
                {
                    LogExtended("VSPilotPackage: Preparing to show initialization error message box");
                    Guid clsid = Guid.Empty;
                    int result = uiShell.ShowMessageBox(
                        0,
                        ref clsid,
                        "VSPilot Initialization Error",
                        $"Failed to initialize VSPilot extension.\n\nError Details:\n{ex.Message}\n\nPlease check the Activity Log for more information.",
                        string.Empty,
                        0,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        0,
                        out int _);
                    LogExtended($"VSPilotPackage: Error message box shown. Result: {result}");
                }
                else
                {
                    LogExtended("VSPilotPackage: UI Shell service not available to show error message");
                }
            }
            catch (Exception handlerEx)
            {
                LogExtended($"VSPilotPackage: Critical error in error handler: {handlerEx.Message}");
                LogExtended($"VSPilotPackage: Original exception: {ex.Message}");
            }
        }

        protected override object GetService(Type serviceType)
        {
            LogExtended($"VSPilotPackage: GetService called for {serviceType.Name}");
            if (serviceType == typeof(AutomationService))
            {
                object? baseService = base.GetService(serviceType);
                if (baseService != null)
                {
                    LogExtended("VSPilotPackage: Got AutomationService from base");
                    return baseService;
                }
            }
            return base.GetService(serviceType);
        }

        #region Tool Window Support
        public override IVsAsyncToolWindowFactory GetAsyncToolWindowFactory(Guid toolWindowType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            LogExtended($"VSPilotPackage: GetAsyncToolWindowFactory called for {toolWindowType}");
            return toolWindowType == typeof(ChatWindow).GUID ? this : base.GetAsyncToolWindowFactory(toolWindowType);
        }

        protected override string GetToolWindowTitle(Type toolWindowType, int id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return toolWindowType == typeof(ChatWindow) ? "VSPilot Chat" : base.GetToolWindowTitle(toolWindowType, id);
        }

        protected override async Task<object> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken)
        {
            LogExtended($"VSPilotPackage: InitializeToolWindowAsync called for {toolWindowType.Name}");
            if (!_isInitialized)
            {
                LogExtended("VSPilotPackage: Package not initialized yet, waiting 1000ms");
                await Task.Delay(1000, cancellationToken);
            }
            if (toolWindowType == typeof(ChatWindow))
            {
                LogExtended("VSPilotPackage: Initializing ChatWindow");
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                ChatWindow chatWindow = new ChatWindow();
                LogExtended($"VSPilotPackage: ChatWindow created. Caption: {chatWindow.Caption}");
                return chatWindow;
            }
            return await base.InitializeToolWindowAsync(toolWindowType, id, cancellationToken);
        }
        #endregion

        // Extended logging helpers.
        private void LogExtended(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
        }

        private static void LogExtendedStatic(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
        }
    }

    // Updated fallback implementation for ConfigurationService.
    // The parameterless constructor calls the base constructor
    // with a null-forgiving AsyncPackage and a NullLogger instance.
    public class DefaultConfigurationService : ConfigurationService
    {
        public DefaultConfigurationService(AsyncPackage package)
            : base(package, NullLogger<ConfigurationService>.Instance)
        {
        }

        // Implement additional members as appropriate.
    }
}
