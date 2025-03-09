using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace VSPilot.Core.Services
{
    public class LoggingService : ILogger
    {
        private readonly IVsOutputWindowPane _outputPane;
        private readonly string _logFilePath;
        private readonly object _lockObj = new object();
        private readonly bool _isDetailedLoggingEnabled;

        public LoggingService(IVsOutputWindowPane outputPane, bool enableDetailedLogging = false)
        {
            _outputPane = outputPane ?? throw new ArgumentNullException(nameof(outputPane));
            _isDetailedLoggingEnabled = enableDetailedLogging;
            _logFilePath = Path.Combine(
                Path.GetTempPath(),
                "VSPilot",
                $"vspilot-log-{DateTime.Now:yyyy-MM-dd}.log"
            );

            EnsureLogDirectoryExists();
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var formattedMessage = FormatLogMessage(logLevel, message, exception);

            _ = WriteToOutputWindowAsync(formattedMessage);
            WriteToFile(formattedMessage);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None &&
                   (_isDetailedLoggingEnabled || logLevel >= LogLevel.Information);
        }

        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            return NullScope.Instance; // Return a non-null disposable instance
        }

        public async Task LogOperationAsync(
            string operation,
            Func<Task> action,
            [CallerMemberName] string caller = "")
        {
            try
            {
                LogInformation($"Starting {operation}", caller);
                await action();
                LogInformation($"Completed {operation}", caller);
            }
            catch (Exception ex)
            {
                LogError(ex, $"Failed {operation}", caller);
                throw;
            }
        }

        public async Task<T> LogOperationAsync<T>(
            string operation,
            Func<Task<T>> action,
            [CallerMemberName] string caller = "")
        {
            try
            {
                LogInformation($"Starting {operation}", caller);
                var result = await action();
                LogInformation($"Completed {operation}", caller);
                return result;
            }
            catch (Exception ex)
            {
                LogError(ex, $"Failed {operation}", caller);
                throw;
            }
        }

        public void LogInformation(
            string message,
            [CallerMemberName] string caller = "")
        {
            Log(LogLevel.Information, 0, message, null, (m, _) => FormatWithCaller(m, caller));
        }

        public void LogWarning(
            string message,
            [CallerMemberName] string caller = "")
        {
            Log(LogLevel.Warning, 0, message, null, (m, _) => FormatWithCaller(m, caller));
        }

        public void LogError(
            Exception ex,
            string message,
            [CallerMemberName] string caller = "")
        {
            Log(LogLevel.Error, 0, message, ex, (m, e) => FormatWithCaller(m, caller));
        }

        public void LogDebug(
            string message,
            [CallerMemberName] string caller = "")
        {
            if (_isDetailedLoggingEnabled)
            {
                Log(LogLevel.Debug, 0, message, null, (m, _) => FormatWithCaller(m, caller));
            }
        }

        private string FormatLogMessage(LogLevel logLevel, string message, Exception? exception)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] {message}");

            if (exception != null)
            {
                sb.AppendLine($"Exception: {exception.GetType().Name}");
                sb.AppendLine($"Message: {exception.Message}");
                sb.AppendLine($"Stack Trace: {exception.StackTrace}");

                if (exception.InnerException != null)
                {
                    sb.AppendLine($"Inner Exception: {exception.InnerException.Message}");
                    sb.AppendLine($"Inner Stack Trace: {exception.InnerException.StackTrace}");
                }
            }

            return sb.ToString();
        }

        private string FormatWithCaller(string message, string caller)
        {
            return string.IsNullOrEmpty(caller) ? message : $"[{caller}] {message}";
        }

        private async Task WriteToOutputWindowAsync(string message)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _outputPane.OutputStringThreadSafe(message);
            }
            catch (Exception ex)
            {
                // If output window fails, ensure we at least write to file
                WriteToFile($"Failed to write to output window: {ex.Message}");
                WriteToFile(message);
            }
        }

        private void WriteToFile(string message)
        {
            lock (_lockObj)
            {
                try
                {
                    File.AppendAllText(_logFilePath, message);
                }
                catch (Exception)
                {
                    // If file logging fails, we can't do much more
                }
            }
        }

        private void EnsureLogDirectoryExists()
        {
            try
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception)
            {
                // If we can't create the directory, we'll just fallback to output window
            }
        }

        public void Dispose()
        {
            // Cleanup if needed
        }
    }

    internal class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}
