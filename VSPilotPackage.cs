#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        // Core package components
        private Microsoft.VisualStudio.Shell.ServiceProvider? _serviceProvider;
        private readonly ILogger<VSPilotPackage>? _logger;
        private Stopwatch? _initializationTimer;
        private readonly ServiceCollection _services;
        private readonly object _initializationLock = new object();
        private volatile bool _isInitialized = false;

        // Performance and telemetry
        private static readonly ActivitySource _activitySource = new ActivitySource("VSPilot.Package");

        /// <summary>
        /// Package constructor
        /// </summary>
        public VSPilotPackage()
        {
            Debug.WriteLine("VSPilotPackage: Constructor called");
            _services = new ServiceCollection();

            var serviceProvider = _services.BuildServiceProvider();
            _logger = serviceProvider.GetService<ILogger<VSPilotPackage>>();
        }

        /// <summary>
        /// Async package initialization
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Debug.WriteLine($"VSPilotPackage: InitializeAsync started at {DateTime.Now}");
            Debug.WriteLine($"Cancellation token can be canceled: {cancellationToken.CanBeCanceled}");

            using var activity = _activitySource.StartActivity("PackageInitialization");

            try
            {
                // Start initialization timer
                _initializationTimer = Stopwatch.StartNew();

                // Base initialization
                await base.InitializeAsync(cancellationToken, progress);

                // Create a cancellation token with a reasonable timeout
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60)); // Increased timeout
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutCts.Token
                );

                // Switch to main thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(linkedCts.Token);
                Debug.WriteLine("VSPilotPackage: Switched to main thread");

                // Initialize core services
                await InitializeCoreServicesAsync(linkedCts.Token);

                // Complete initialization
                _initializationTimer.Stop();
                Debug.WriteLine($"VSPilotPackage: Initialization complete in {_initializationTimer.ElapsedMilliseconds}ms");

                // Log performance metrics
                activity?.SetTag("InitializationTime", _initializationTimer.ElapsedMilliseconds);
                activity?.SetStatus(ActivityStatusCode.Ok, "Package initialization successful");
            }
            catch (OperationCanceledException ex)
            {
                Debug.WriteLine($"VSPilotPackage: Initialization timed out or canceled: {ex.Message}");
                _logger?.LogWarning("VSPilot package initialization timed out");

                activity?.SetStatus(ActivityStatusCode.Error, "Initialization timed out");
                await HandleInitializationCancellationAsync(ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VSPilotPackage: Initialization FAILED - {ex.GetType().Name}");
                Debug.WriteLine($"Error Message: {ex.Message}");
                Debug.WriteLine($"Stack Trace: {ex.StackTrace}");

                _logger?.LogError(ex, "Initialization failed");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                try
                {
                    await HandleInitializationErrorAsync(ex);
                }
                catch (Exception handlerEx)
                {
                    Debug.WriteLine($"Error in error handler: {handlerEx.Message}");
                }

                throw;
            }
            finally
            {
                _isInitialized = true;
                Debug.WriteLine("VSPilotPackage: Set _isInitialized to true");
                activity?.Dispose();
            }

            Debug.WriteLine("VSPilotPackage: InitializeAsync method completed");
        }

        /// <summary>
        /// Initializes core services for the package
        /// </summary>
        private async Task InitializeCoreServicesAsync(CancellationToken cancellationToken)
        {
            Debug.WriteLine("VSPilotPackage: Starting core services initialization");

            try
            {
                // Parallel service retrieval
                var dteTask = GetCriticalServiceAsync<DTE2>(typeof(SDTE), "DTE");
                var solutionTask = GetCriticalServiceAsync<IVsSolution>(typeof(SVsSolution), "Solution");
                var outputWindowTask = GetCriticalServiceAsync<IVsOutputWindow>(typeof(SVsOutputWindow), "OutputWindow");

                // Wait for critical services
                await Task.WhenAll(dteTask, solutionTask, outputWindowTask);

                // Retrieve service results
                var dte = await dteTask;
                var solution = await solutionTask;
                var outputWindow = await outputWindowTask;

                // Configure logging services first
                _services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Warning);
                    builder.AddFilter("VSPilot", LogLevel.Warning);
                });

                // Configure basic services
                _services.AddSingleton<AsyncPackage>(this);
                _services.AddSingleton<ConfigurationService>();
                _services.AddSingleton<TemplateManager>();
                _services.AddSingleton<VSPilot.Core.Services.TaskScheduler>();
                _services.AddSingleton<SolutionAnalyzer>();
                _services.AddSingleton<LanguageProcessor>();

                // Add critical services
                if (dte != null)
                    _services.AddSingleton(dte);

                if (solution != null)
                    _services.AddSingleton(solution);

                // Configure output window
                if (outputWindow != null)
                {
                    IVsOutputWindowPane outputPane = await CreateOutputWindowPaneAsync(outputWindow);
                    _services.AddSingleton(outputPane);
                    _services.AddSingleton<LoggingService>(sp =>
                        new LoggingService(outputPane, true)); // Enable detailed logging
                }

                // Register file manager before other services that depend on it
                _services.AddSingleton<FileManager>();

                // Register service interfaces to implementations
                _services.AddSingleton<IVsSolutionContext>(sp =>
                {
                    var dte = sp.GetRequiredService<DTE2>();
                    var fileManager = sp.GetRequiredService<FileManager>();
                    return new VsSolutionAdapter(dte, fileManager);
                });

                _services.AddSingleton<ITestPlatform, MockTestPlatform>();
                _services.AddSingleton<IProjectManager, ProjectManager>();
                _services.AddSingleton<IBuildManager, BuildManager>();
                _services.AddSingleton<IErrorHandler, ErrorHandler>();
                _services.AddSingleton<TestRunner>();

                // AI components
                _services.AddSingleton<AIRequestHandler>();
                _services.AddSingleton<VSPilotAIIntegration>();

                // Register AutomationService last since it depends on many other services
                _services.AddSingleton<AutomationService>();

                // Build service provider
                try
                {
                    _serviceProvider = new Microsoft.VisualStudio.Shell.ServiceProvider((Microsoft.VisualStudio.OLE.Interop.IServiceProvider)this);

                    // Create a new ServiceProvider for our services
                    var provider = _services.BuildServiceProvider();

                    // Important: Add the AutomationService to the VSPackage services
                    var automationService = provider.GetService<AutomationService>();
                    if (automationService != null)
                    {
                        Debug.WriteLine("VSPilotPackage: Adding AutomationService to ServiceContainer");
                        ((IServiceContainer)this).AddService(typeof(AutomationService), automationService, true);
                    }
                    else
                    {
                        Debug.WriteLine("VSPilotPackage: Failed to get AutomationService from service provider");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Service provider build failed: {ex.Message}");
                    throw;
                }

                // Initialize commands and UI
                await InitializeCommandsAsync(cancellationToken);

                // Initialize background services after UI is ready
                await InitializeBackgroundServicesAsync(cancellationToken);

                Debug.WriteLine("VSPilotPackage: Core services initialization completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VSPilotPackage: Core services initialization failed - {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Retrieves a critical Visual Studio service
        /// </summary>
        private async Task<T?> GetCriticalServiceAsync<T>(Type serviceType, string serviceName)
            where T : class
        {
            try
            {
                Debug.WriteLine($"VSPilotPackage: Getting critical service: {serviceName}");

                var service = await GetServiceAsync(serviceType) as T;

                if (service == null)
                {
                    Debug.WriteLine($"VSPilotPackage: Failed to retrieve service: {serviceName}");
                    throw new InvalidOperationException($"Failed to initialize {serviceName} service");
                }

                Debug.WriteLine($"VSPilotPackage: Successfully retrieved service: {serviceName}");
                return service;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VSPilotPackage: Failed to get {serviceName} service - {ex.Message}");
                throw new InvalidOperationException($"Critical service {serviceName} initialization failed", ex);
            }
        }

        /// <summary>
        /// Creates an output window pane for VSPilot
        /// </summary>
        private async Task<IVsOutputWindowPane> CreateOutputWindowPaneAsync(IVsOutputWindow outputWindow)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            Guid paneGuid = Guid.NewGuid();
            int hr = outputWindow.CreatePane(ref paneGuid, "VSPilot", 1, 1);
            Marshal.ThrowExceptionForHR(hr);

            hr = outputWindow.GetPane(ref paneGuid, out IVsOutputWindowPane pane);
            Marshal.ThrowExceptionForHR(hr);

            pane.OutputString("VSPilot output pane initialized" + Environment.NewLine);

            return pane;
        }

        /// <summary>
        /// Initializes package commands
        /// </summary>
        private async Task InitializeCommandsAsync(CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine("VSPilotPackage: Initializing Commands");

                await Task.WhenAll(
                    ChatWindowCommand.InitializeAsync(this),
                    SettingsCommand.InitializeAsync(this)
                );

                Debug.WriteLine("VSPilotPackage: Commands initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VSPilotPackage: Command initialization failed - {ex}");
                _logger?.LogError(ex, "Failed to initialize commands");
                throw;
            }
        }

        /// <summary>
        /// Initializes background services
        /// </summary>
        private async Task InitializeBackgroundServicesAsync(CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine("VSPilotPackage: Starting background services");

                await Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000, cancellationToken); // Wait a bit longer
                        var aiIntegration = _services.BuildServiceProvider().GetService<VSPilotAIIntegration>();
                        if (aiIntegration != null)
                        {
                            aiIntegration.QueueVSPilotProjectAnalysis("DefaultProject");
                            Debug.WriteLine("VSPilotPackage: Project analysis queued");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"VSPilotPackage: Project analysis failed - {ex}");
                        _logger?.LogError(ex, "Project analysis failed");
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VSPilotPackage: Background services initialization failed - {ex}");
                _logger?.LogError(ex, "Background services initialization failed");
            }
        }

        /// <summary>
        /// Handles initialization cancellation
        /// </summary>
        private async Task HandleInitializationCancellationAsync(OperationCanceledException ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var uiShell = await GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell != null)
            {
                Guid clsid = Guid.Empty;
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
        }

        /// <summary>
        /// Handles initialization errors
        /// </summary>
        private async Task HandleInitializationErrorAsync(Exception ex)
        {
            try
            {
                Debug.WriteLine($"VSPilotPackage: Starting error handling for {ex.GetType().Name}");

                await JoinableTaskFactory.SwitchToMainThreadAsync();

                // Log to Activity Log
                var activityLog = await GetServiceAsync(typeof(SVsActivityLog)) as IVsActivityLog;
                if (activityLog != null)
                {
                    Debug.WriteLine("VSPilotPackage: Logging to Activity Log");
                    var hResult = activityLog.LogEntry(
                        (uint)__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR,
                        ToString(),
                        $"VSPilot initialization failed: {ex.Message}\n{ex.StackTrace}"
                    );
                }
                else
                {
                    Debug.WriteLine("VSPilotPackage: Could not get Activity Log service");
                }

                // Show error message box
                var uiShell = await GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell != null)
                {
                    Debug.WriteLine("VSPilotPackage: Preparing to show error message");

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

                    Debug.WriteLine($"Error message box shown. Result: {result}");
                }
                else
                {
                    Debug.WriteLine("VSPilotPackage: Could not get UI Shell service");
                }
            }
            catch (Exception handlerEx)
            {
                // Fallback error logging
                Debug.WriteLine($"Critical error in error handler: {handlerEx.Message}");
                Debug.WriteLine($"Original exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Overrides the base GetService method to use our service provider for AutomationService
        /// </summary>
        protected override object GetService(Type serviceType)
        {
            Debug.WriteLine($"VSPilotPackage: GetService called for {serviceType.Name}");

            // Handle AutomationService specially
            if (serviceType == typeof(AutomationService))
            {
                var baseService = base.GetService(serviceType);
                if (baseService != null)
                {
                    Debug.WriteLine("VSPilotPackage: Got AutomationService from base");
                    return baseService;
                }
            }

            return base.GetService(serviceType);
        }

        #region Tool Window Support
        /// <summary>
        /// Gets the async tool window factory for the specified tool window type
        /// </summary>
        public override IVsAsyncToolWindowFactory GetAsyncToolWindowFactory(Guid toolWindowType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Debug.WriteLine($"VSPilotPackage: GetAsyncToolWindowFactory called for {toolWindowType}");

            return toolWindowType == typeof(ChatWindow).GUID ? this : base.GetAsyncToolWindowFactory(toolWindowType);
        }

        /// <summary>
        /// Gets the tool window title for the specified tool window type
        /// </summary>
        protected override string GetToolWindowTitle(Type toolWindowType, int id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return toolWindowType == typeof(ChatWindow) ? "VSPilot Chat" : base.GetToolWindowTitle(toolWindowType, id);
        }

        /// <summary>
        /// Initializes the tool window asynchronously
        /// </summary>
        protected override async Task<object> InitializeToolWindowAsync(Type toolWindowType, int id, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"InitializeToolWindowAsync called for {toolWindowType.Name}");

            // Make sure package is initialized before creating tool windows
            if (!_isInitialized)
            {
                Debug.WriteLine("Package not initialized, waiting 1000ms");
                await Task.Delay(1000, cancellationToken);
            }

            if (toolWindowType == typeof(ChatWindow))
            {
                Debug.WriteLine("Initializing ChatWindow");
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                var chatWindow = new ChatWindow();
                Debug.WriteLine($"ChatWindow created. Caption: {chatWindow.Caption}");

                return chatWindow;
            }

            return await base.InitializeToolWindowAsync(toolWindowType, id, cancellationToken);
        }
        #endregion
    }
}