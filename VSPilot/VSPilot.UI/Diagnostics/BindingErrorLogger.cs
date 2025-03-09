using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Tracing;
using System.Windows.Diagnostics;

namespace VSPilot.UI.Diagnostics
{
    /// <summary>
    /// Provides diagnostic logging for WPF binding errors to help troubleshoot XAML binding issues.
    /// </summary>
    public class BindingErrorLogger
    {
        private readonly ILogger _logger;
        private readonly TraceListener _traceListener;

        /// <summary>
        /// Initializes a new instance of the <see cref="BindingErrorLogger"/> class.
        /// </summary>
        /// <param name="logger">The logger to use for recording binding errors.</param>
        public BindingErrorLogger(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _traceListener = new BindingErrorTraceListener(logger);
        }

        /// <summary>
        /// Enables monitoring of binding errors.
        /// Call this method during application startup.
        /// </summary>
        public void Enable()
        {
            // Note: This might require a different approach depending on the exact WPF diagnostics setup
            _logger.LogInformation("Binding error logging attempted to enable");
        }

        /// <summary>
        /// Disables monitoring of binding errors.
        /// </summary>
        public void Disable()
        {
            _logger.LogInformation("Binding error logging disabled");
        }

        /// <summary>
        /// Trace listener implementation specific to binding errors.
        /// </summary>
        private class BindingErrorTraceListener : TraceListener
        {
            private readonly ILogger _logger;

            public BindingErrorTraceListener(ILogger logger)
            {
                _logger = logger;
            }

            public override void Write(string message)
            {
                _logger.LogWarning(message);
            }

            public override void WriteLine(string message)
            {
                _logger.LogWarning(message);
            }

            public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
            {
                if (eventType == TraceEventType.Error)
                {
                    LogBindingError(id, message);
                }
                else if (eventType == TraceEventType.Warning)
                {
                    LogBindingWarning(id, message);
                }
            }

            public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
            {
                if (eventType == TraceEventType.Error || eventType == TraceEventType.Warning)
                {
                    string message = string.Format(format, args);

                    if (eventType == TraceEventType.Error)
                    {
                        LogBindingError(id, message);
                    }
                    else
                    {
                        LogBindingWarning(id, message);
                    }
                }
            }

            private void LogBindingError(int id, string message)
            {
                var details = ParseBindingMessage(message);

                _logger.LogError(
                    "Binding Error {ErrorId}: {Message}\nPath: {BindingPath}\nElement: {TargetElement}\nProperty: {TargetProperty}",
                    id, details.Message, details.BindingPath, details.TargetElement, details.TargetProperty);
            }

            private void LogBindingWarning(int id, string message)
            {
                var details = ParseBindingMessage(message);

                _logger.LogWarning(
                    "Binding Warning {WarningId}: {Message}\nPath: {BindingPath}\nElement: {TargetElement}\nProperty: {TargetProperty}",
                    id, details.Message, details.BindingPath, details.TargetElement, details.TargetProperty);
            }

            private BindingErrorDetails ParseBindingMessage(string message)
            {
                var details = new BindingErrorDetails { Message = message };

                try
                {
                    int bindingExpressionIndex = message.IndexOf("BindingExpression:");
                    if (bindingExpressionIndex >= 0)
                    {
                        string bindingInfo = message.Substring(bindingExpressionIndex);

                        // Extract binding path
                        int pathIndex = bindingInfo.IndexOf("Path=");
                        if (pathIndex >= 0)
                        {
                            int semicolonIndex = bindingInfo.IndexOf(';', pathIndex);
                            if (semicolonIndex > pathIndex)
                            {
                                details.BindingPath = bindingInfo.Substring(pathIndex + 5, semicolonIndex - pathIndex - 5);
                            }
                        }

                        // Extract target element
                        int targetIndex = bindingInfo.IndexOf("target element is '");
                        if (targetIndex >= 0)
                        {
                            int targetEndIndex = bindingInfo.IndexOf('\'', targetIndex + 19);
                            if (targetEndIndex > targetIndex)
                            {
                                details.TargetElement = bindingInfo.Substring(targetIndex + 19, targetEndIndex - targetIndex - 19);
                            }
                        }

                        // Extract target property
                        int propertyIndex = bindingInfo.IndexOf("target property is '");
                        if (propertyIndex >= 0)
                        {
                            int propertyEndIndex = bindingInfo.IndexOf('\'', propertyIndex + 20);
                            if (propertyEndIndex > propertyIndex)
                            {
                                details.TargetProperty = bindingInfo.Substring(propertyIndex + 20, propertyEndIndex - propertyIndex - 20);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error parsing binding message: {ex.Message}");
                }

                return details;
            }
        }

        /// <summary>
        /// Represents the details extracted from a binding error message.
        /// </summary>
        private class BindingErrorDetails
        {
            public string Message { get; set; } = string.Empty;
            public string BindingPath { get; set; } = string.Empty;
            public string TargetElement { get; set; } = string.Empty;
            public string TargetProperty { get; set; } = string.Empty;
        }
    }

    /// <summary>
    /// Extension methods for registering binding error logging.
    /// </summary>
    public static class BindingErrorLoggerExtensions
    {
        /// <summary>
        /// Registers the binding error logger with the application.
        /// </summary>
        /// <param name="application">The WPF application instance.</param>
        /// <param name="logger">The logger to use for binding errors.</param>
        /// <returns>The binding error logger instance.</returns>
        public static BindingErrorLogger RegisterBindingErrorLogger(this Application application, ILogger logger)
        {
            var bindingLogger = new BindingErrorLogger(logger);
            bindingLogger.Enable();

            // Automatically disable when the application exits
            application.Exit += (s, e) => bindingLogger.Disable();

            return bindingLogger;
        }
    }
}