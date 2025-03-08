using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.VisualStudio.Threading;
using System.Reflection;
using static VSPilot.Core.Build.TestRunner;
using Moq;

namespace VSPilot.Core.Build
{
    public interface ITestPlatform
    {
        Task<ITestOperation> CreateTestOperationAsync();
    }

    public interface ITestOperation
    {
        event EventHandler<TestResultsUpdatedEventArgs> TestResultsUpdated;
        object Context { get; set; }
        Task RunAsync();
    }

    public class TestResultsUpdatedEventArgs : EventArgs
    {
        public TestResult[] Results { get; }

        public TestResultsUpdatedEventArgs(TestResult[] results)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }
    }

    public class VSTestPlatform : ITestPlatform
    {
        private readonly IVsTestWindow _testWindow;

        public VSTestPlatform(IVsTestWindow testWindow)
        {
            _testWindow = testWindow ?? throw new ArgumentNullException(nameof(testWindow));
        }

        public async Task<ITestOperation> CreateTestOperationAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var controller = _testWindow.GetTestWindowController();
            return new VSTestOperation(new TestControllerWrapper(controller));
        }
    }

    public class TestControllerWrapper : IDisposable
    {
        private readonly object _originalController;
        private readonly EventInfo _resultUpdatedEvent;
        private readonly EventInfo _completedEvent;
        private readonly MethodInfo _runTestsMethod;
        private readonly MethodInfo _runAllTestsMethod;
        private bool _disposed;

        private event EventHandler<TestRunResultEventArgs>? _testRunResultUpdated;
        private event EventHandler<VSTestOperation.TestRunCompleteEventArgs>? _testRunCompleted;

        public event EventHandler<TestRunResultEventArgs> TestRunResultUpdated
        {
            add
            {
                ThrowIfDisposed();
                _testRunResultUpdated += value;
            }
            remove
            {
                ThrowIfDisposed();
                _testRunResultUpdated -= value;
            }
        }

        public event EventHandler<VSTestOperation.TestRunCompleteEventArgs> TestRunCompleted
        {
            add
            {
                ThrowIfDisposed();
                _testRunCompleted += value;
            }
            remove
            {
                ThrowIfDisposed();
                _testRunCompleted -= value;
            }
        }

        public TestControllerWrapper(object controller)
        {
            _originalController = controller ?? throw new ArgumentNullException(nameof(controller));

            var controllerType = controller.GetType();

            // Cache reflection info
            _resultUpdatedEvent = controllerType.GetEvent("TestRunResultUpdated");
            _completedEvent = controllerType.GetEvent("TestRunCompleted");
            _runTestsMethod = controllerType.GetMethod("RunTests");
            _runAllTestsMethod = controllerType.GetMethod("RunAllTests");

            // Subscribe to original events
            SubscribeToControllerEvents();
        }

        private void SubscribeToControllerEvents()
        {
            if (_resultUpdatedEvent != null)
            {
                var resultHandler = Delegate.CreateDelegate(
                    _resultUpdatedEvent.EventHandlerType,
                    this,
                    typeof(TestControllerWrapper).GetMethod(nameof(OnTestRunResultUpdated),
                        BindingFlags.NonPublic | BindingFlags.Instance));
                _resultUpdatedEvent.AddEventHandler(_originalController, resultHandler);
            }

            if (_completedEvent != null)
            {
                var completedHandler = Delegate.CreateDelegate(
                    _completedEvent.EventHandlerType,
                    this,
                    typeof(TestControllerWrapper).GetMethod(nameof(OnTestRunCompleted),
                        BindingFlags.NonPublic | BindingFlags.Instance));
                _completedEvent.AddEventHandler(_originalController, completedHandler);
            }
        }

        public void RunTests(object context)
        {
            ThrowIfDisposed();
            _runTestsMethod?.Invoke(_originalController, new[] { context });
        }

        public void RunAllTests()
        {
            ThrowIfDisposed();
            _runAllTestsMethod?.Invoke(_originalController, null);
        }

        private void OnTestRunResultUpdated(object sender, object e)
        {
            if (_disposed) return;

            if (e is TestRunResultEventArgs args)
            {
                _testRunResultUpdated?.Invoke(sender, args);
            }
        }

        private void OnTestRunCompleted(object sender, object e)
        {
            if (_disposed) return;

            // Convert the event args
            bool isCanceled = false;
            Exception? error = null;

            // Extract values using reflection if needed
            if (e != null)
            {
                var eventType = e.GetType();
                var canceledProp = eventType.GetProperty("IsCanceled");
                var errorProp = eventType.GetProperty("Error");

                if (canceledProp != null)
                    isCanceled = (bool)canceledProp.GetValue(e);
                if (errorProp != null)
                    error = errorProp.GetValue(e) as Exception;
            }

            var args = new VSTestOperation.TestRunCompleteEventArgs(isCanceled, error);
            _testRunCompleted?.Invoke(sender, args);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TestControllerWrapper));
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Unsubscribe from events
                if (_resultUpdatedEvent != null && _originalController != null)
                {
                    foreach (Delegate d in _testRunResultUpdated?.GetInvocationList() ?? Array.Empty<Delegate>())
                    {
                        _resultUpdatedEvent.RemoveEventHandler(_originalController, d);
                    }
                }

                if (_completedEvent != null && _originalController != null)
                {
                    foreach (Delegate d in _testRunCompleted?.GetInvocationList() ?? Array.Empty<Delegate>())
                    {
                        _completedEvent.RemoveEventHandler(_originalController, d);
                    }
                }

                _disposed = true;
            }
        }
    }

    public class VSTestOperation : ITestOperation, IDisposable
    {
        public class TestRunCompleteEventArgs : EventArgs
        {
            public bool IsCanceled { get; }
            public Exception? Error { get; }

            public TestRunCompleteEventArgs(bool isCanceled = false, Exception? error = null)
            {
                IsCanceled = isCanceled;
                Error = error;
            }
        }

        private readonly TestControllerWrapper _controller;
        private readonly JoinableTaskFactory _jtf;
        private bool _disposed;
        private object _context = new object();
        private EventHandler<TestResultsUpdatedEventArgs>? _testResultsUpdated;

        public VSTestOperation(TestControllerWrapper controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _jtf = ThreadHelper.JoinableTaskFactory;

            _controller.TestRunResultUpdated += OnTestRunResultUpdated;
            _controller.TestRunCompleted += OnTestRunCompleted;
        }

        public event EventHandler<TestResultsUpdatedEventArgs> TestResultsUpdated
        {
            add => _testResultsUpdated += value;
            remove => _testResultsUpdated -= value;
        }

        public object Context
        {
            get => _context;
            set => _context = value;
        }

        public async Task RunAsync()
        {
            try
            {
                await _jtf.SwitchToMainThreadAsync();

                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(VSTestOperation));
                }

                var tcs = new TaskCompletionSource<bool>();

                void CompletedHandler(object sender, TestRunCompleteEventArgs e)
                {
                    _controller.TestRunCompleted -= CompletedHandler;
                    tcs.TrySetResult(!e.IsCanceled);
                }

                _controller.TestRunCompleted += CompletedHandler;

                if (Context != null)
                {
                    _controller.RunTests(Context);
                }
                else
                {
                    _controller.RunAllTests();
                }

                await tcs.Task;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to run tests", ex);
            }
        }

        private void OnTestRunResultUpdated(object sender, TestRunResultEventArgs e)
        {
            if (_disposed) return;

            var results = e.NewTestResults?.ToArray();
            if (results != null && results.Length > 0)
            {
                _testResultsUpdated?.Invoke(this, new TestResultsUpdatedEventArgs(results));
            }
        }

        private void OnTestRunCompleted(object sender, TestRunCompleteEventArgs e)
        {
            // Implementation now handled by completion callback in RunAsync
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _controller.TestRunResultUpdated -= OnTestRunResultUpdated;
                _controller.TestRunCompleted -= OnTestRunCompleted;
                _controller.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
