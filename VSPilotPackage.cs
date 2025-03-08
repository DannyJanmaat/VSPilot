// VSPilotPackage.cs
#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VSPilot.Core.AI;
using VSPilot.Core.Automation;
using VSPilot.Core.Build;
using VSPilot.Core.Services;
using VSPilot.UI.Commands;
using VSPilot.UI.Windows;
using EnvDTE80;
using VSPilot.Common.Interfaces;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.ComponentModel.Design;
using Moq;
using static VSPilot.Core.Build.TestRunner;
// Import the console helper for debugging output
using VSPilot.Common.Utilities;
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
        private readonly ILogger<VSPilotPackage>? _logger;
        private Stopwatch? _initializationTimer;
        private readonly ServiceCollection _services;
        private readonly object _initializationLock = new object();
        private volatile bool _isInitialized = false;

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
            var serviceProvider = _services.BuildServiceProvider();
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

            using var activity = _activitySource.StartActivity("PackageInitialization");

            try
            {
                _initializationTimer = Stopwatch.StartNew();
                LogExtended("VSPilotPackage: Initialization timer started");

                await base.InitializeAsync(cancellationToken, progress);
                LogExtended("VSPilotPackage: Base InitializeAsync completed");

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                LogExtended("VSPilotPackage: Linked cancellation token created");

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(linkedCts.Token);
                LogExtended("VSPilotPackage: Successfully switched to main thread");

                await InitializeCoreServicesAsync(linkedCts.Token);
                LogExtended("VSPilotPackage: Core services initialization finished");

                _initializationTimer.Stop();
                LogExtended($"VSPilotPackage: Initialization complete in {_initializationTimer.ElapsedMilliseconds}ms");

                // Assign the static instance for later use.
                Instance = this;

                activity?.SetTag("InitializationTime", _initializationTimer.ElapsedMilliseconds);
                activity?.SetStatus(ActivityStatusCode.Ok, "Package initialization successful");
            }
            catch (OperationCanceledException ex)
            {
                LogExtended($"VSPilotPackage: Initialization timed out or canceled: {ex.Message}");
                _logger?.LogWarning("VSPilot package initialization timed out");
                activity?.SetStatus(ActivityStatusCode.Error, "Initialization timed out");
                await HandleInitializationCancellationAsync(ex);
            }
            catch (Exception ex)
            {
                LogExtended($"VSPilotPackage: Initialization FAILED - {ex.GetType().Name}");
                LogExtended($"VSPilotPackage: Error Message: {ex.Message}");
                LogExtended($"VSPilotPackage: Stack Trace: {ex.StackTrace}");
                _logger?.LogError(ex, "Initialization failed");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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
                var dteTask = GetCriticalServiceAsync<DTE2>(typeof(SDTE), "DTE");
                var solutionTask = GetCriticalServiceAsync<IVsSolution>(typeof(SVsSolution), "Solution");
                var outputWindowTask = GetCriticalServiceAsync<IVsOutputWindow>(typeof(SVsOutputWindow), "OutputWindow");

                await Task.WhenAll(dteTask, solutionTask, outputWindowTask);
                LogExtended("VSPilotPackage: All critical services retrieved successfully");

                var dte = await dteTask;
                var solution = await solutionTask;
                var outputWindow = await outputWindowTask;

                LogExtended("VSPilotPackage: Configuring logging services");
                _services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Warning);
                    builder.AddFilter("VSPilot", LogLevel.Warning);
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
                    configurationService = new DefaultConfigurationService();
                }
                _services.AddSingleton(configurationService);

                // Register other basic services.
                _services.AddSingleton<AsyncPackage>(this);
                _services.AddSingleton<TemplateManager>();
                _services.AddSingleton<VSPilot.Core.Services.TaskScheduler>();
                _services.AddSingleton<SolutionAnalyzer>();
                _services.AddSingleton<LanguageProcessor>();

                if (dte != null)
                {
                    _services.AddSingleton(dte);
                    LogExtended("VSPilotPackage: DTE service added to service collection");
                }
                if (solution != null)
                {
                    _services.AddSingleton(solution);
                    LogExtended("VSPilotPackage: Solution service added to service collection");
                }

                if (outputWindow != null)
                {
                    LogExtended("VSPilotPackage: Creating output window pane");
                    IVsOutputWindowPane outputPane = await CreateOutputWindowPaneAsync(outputWindow);
                    _services.AddSingleton(outputPane);
                    _services.AddSingleton<LoggingService>(sp =>
                            new LoggingService(outputPane, true));
                    LogExtended("VSPilotPackage: Output window pane configured and logging service added");
                }

                _services.AddSingleton<FileManager>();
                LogExtended("VSPilotPackage: FileManager added to service collection");

                _services.AddSingleton<IVsSolutionContext>(sp =>
                {
                    var dteService = sp.GetRequiredService<DTE2>();
                    var fileManager = sp.GetRequiredService<FileManager>();
                    return new VsSolutionAdapter(dteService, fileManager);
                });
                LogExtended("VSPilotPackage: IVsSolutionContext registered");

                _services.AddSingleton<ITestPlatform, MockTestPlatform>();
                _services.AddSingleton<IProjectManager, ProjectManager>();
                _services.AddSingleton<IBuildManager, BuildManager>();
                _services.AddSingleton<IErrorHandler, ErrorHandler>();
                _services.AddSingleton<TestRunner>();

                _services.AddSingleton<AIRequestHandler>();
                _services.AddSingleton<VSPilotAIIntegration>();
                LogExtended("VSPilotPackage: AI components registered");

                _services.AddSingleton<AutomationService>();
                LogExtended("VSPilotPackage: AutomationService registered");

                try
                {
                    _serviceProvider = _services.BuildServiceProvider();
                    LogExtended("VSPilotPackage: Built custom service provider from service collection");

                    var automationService = _serviceProvider.GetService<AutomationService>();
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
                var service = await GetServiceAsync(serviceType) as T;
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
            pane.OutputString("VSPilot output pane initialized" + Environment.NewLine);
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
                var aiIntegration = _services.BuildServiceProvider().GetService<VSPilotAIIntegration>();
                if (aiIntegration != null)
                {
                    aiIntegration.QueueVSPilotProjectAnalysis("DefaultProject");
                    LogExtended("VSPilotPackage: Project analysis queued for DefaultProject");
                }
                else
                {
                    LogExtended("VSPilotPackage: Failed to retrieve VSPilotAIIntegration for background services");
                }
                LogExtended("VSPilotPackage: Background services task completed");
            }
            catch (Exception ex)
            {
                LogExtended($"VSPilotPackage: Background services initialization failed - {ex}");
                _logger?.LogError(ex, "Background services initialization failed");
            }
        }

        private async Task HandleInitializationCancellationAsync(OperationCanceledException ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var uiShell = await GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell != null)
            {
                Guid clsid = Guid.Empty;
                LogExtended("VSPilotPackage: Showing cancellation message box");
                uiShell.ShowMessageBox(
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
                    out int _
                );
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
                var activityLog = await GetServiceAsync(typeof(SVsActivityLog)) as IVsActivityLog;
                if (activityLog != null)
                {
                    LogExtended("VSPilotPackage: Logging error to Activity Log");
                    var hResult = activityLog.LogEntry(
                        (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR,
                        ToString(),
                        $"VSPilot initialization failed: {ex.Message}\n{ex.StackTrace}"
                    );
                }
                else
                {
                    LogExtended("VSPilotPackage: Activity Log service not available");
                }
                var uiShell = await GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
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
                        out int _
                    );
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
                var baseService = base.GetService(serviceType);
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
                var chatWindow = new ChatWindow();
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
        public DefaultConfigurationService()
            : base(null!, NullLogger<ConfigurationService>.Instance)
        {
        }

        // Implement additional members as appropriate.
    }
}
