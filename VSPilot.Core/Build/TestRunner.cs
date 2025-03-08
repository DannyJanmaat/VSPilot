using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Microsoft.Extensions.Logging;
using EnvDTE;
using EnvDTE80;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using VSPilot.Common.Exceptions;
using Microsoft.VisualStudio.Threading;

namespace VSPilot.Core.Build
{
    public class TestRunner : IDisposable
    {
        private readonly DTE2 _dte;
        private readonly ILogger<TestRunner> _logger;
        private readonly IVsTestWindow _testWindow;
        private readonly JoinableTaskFactory _jtf;
        private bool _disposed;

        public TestRunner(
            DTE2 dte,
            ILogger<TestRunner> logger,
            IVsTestWindow testWindow)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _testWindow = testWindow ?? throw new ArgumentNullException(nameof(testWindow));
            _jtf = ThreadHelper.JoinableTaskFactory;
        }

        public async Task<bool> RunAllTestsAsync()
        {
            try
            {
                await _jtf.SwitchToMainThreadAsync();
                _logger.LogInformation("Starting test run for all tests");

                var testResults = await ExecuteTestRunAsync();
                return AnalyzeTestResults(testResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run tests");
                throw new AutomationException("Failed to run tests", ex);
            }
        }

        public async Task<bool> RunTestsForProjectAsync(string projectName)
        {
            try
            {
                await _jtf.SwitchToMainThreadAsync();
                _logger.LogInformation("Starting test run for project: {Project}", projectName);

                var project = GetTestProject(projectName);
                if (project == null)
                {
                    _logger.LogWarning("Test project not found: {Project}", projectName);
                    return false;
                }

                var testResults = await ExecuteTestRunAsync(project);
                return AnalyzeTestResults(testResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run tests for project: {Project}", projectName);
                throw new AutomationException($"Failed to run tests for project: {projectName}", ex);
            }
        }

        public async Task<bool> RunTestsInClassAsync(string className)
        {
            try
            {
                await _jtf.SwitchToMainThreadAsync();
                _logger.LogInformation("Starting test run for class: {Class}", className);

                var testClass = await FindTestClassAsync(className);
                if (testClass == null)
                {
                    _logger.LogWarning("Test class not found: {Class}", className);
                    return false;
                }

                var testResults = await ExecuteTestRunAsync(testClass);
                return AnalyzeTestResults(testResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run tests for class: {Class}", className);
                throw new AutomationException($"Failed to run tests for class: {className}", ex);
            }
        }

        private async Task<IEnumerable<TestResult>> ExecuteTestRunAsync(object? scope = null)
        {
            return await _jtf.RunAsync(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();

                // Create test operation
                var controller = _testWindow.GetTestWindowController();
                if (controller == null)
                {
                    throw new AutomationException("Failed to create test operation");
                }

                // Run tests
                var results = new List<TestResult>();
                var tcs = new TaskCompletionSource<IEnumerable<TestResult>>();

                void ResultHandler(object sender, TestRunResultEventArgs args)
                {
                    foreach (var result in args.NewTestResults)
                    {
                        results.Add(new TestResult
                        {
                            TestCase = new TestCase
                            {
                                FullyQualifiedName = result.TestCase.FullyQualifiedName
                            },
                            Outcome = ConvertTestOutcome(result.Outcome),
                            ErrorMessage = result.ErrorMessage,
                            DisplayName = result.TestCase.DisplayName
                        });
                    }
                }

                void CompleteHandler(object sender, TestRunCompleteEventArgs args)
                {
                    controller.TestRunCompleted -= CompleteHandler;
                    controller.TestRunResultUpdated -= ResultHandler;
                    tcs.TrySetResult(results);
                }

                controller.TestRunResultUpdated += ResultHandler;
                controller.TestRunCompleted += CompleteHandler;

                // Run the tests
                if (scope != null)
                {
                    controller.RunTests(scope);
                }
                else
                {
                    controller.RunAllTests();
                }

                return await tcs.Task;
            });
        }

        // Interfaces without default implementation
        public interface IVsTestWindow
        {
            ITestController GetTestWindowController();
        }

        public interface ITestController
        {
            event EventHandler<TestRunResultEventArgs> TestRunResultUpdated;
            event EventHandler<TestRunCompleteEventArgs> TestRunCompleted;
            void RunTests(object scope);
            void RunAllTests();
        }

        // Event argument classes
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

        public class TestRunResultEventArgs : EventArgs
        {
            public IEnumerable<TestResult> NewTestResults { get; }

            public TestRunResultEventArgs(IEnumerable<TestResult> newTestResults)
            {
                NewTestResults = newTestResults ?? throw new ArgumentNullException(nameof(newTestResults));
            }
        }

        private int ConvertTestOutcome(object outcome)
        {
            // Map test window outcome to our constants
            // Passed = 1, Failed = 2, Skipped = 3
            const int Passed = 1;
            const int Failed = 2;
            const int Skipped = 3;

            // This is a simplification - actual implementation would convert properly based on your test framework
            if (outcome.ToString().Contains("Passed")) return Passed;
            if (outcome.ToString().Contains("Failed")) return Failed;
            if (outcome.ToString().Contains("Skipped")) return Skipped;

            return Failed; // Default to failed for unknown outcomes
        }

        private bool AnalyzeTestResults(IEnumerable<TestResult> results)
        {
            if (results == null || !results.Any())
            {
                _logger.LogWarning("No test results to analyze");
                return false;
            }

            // Define test outcomes
            const int Passed = 1;
            const int Failed = 2;
            const int Skipped = 3;

            var passed = results.Count(r => r.Outcome == Passed);
            var failed = results.Count(r => r.Outcome == Failed);
            var skipped = results.Count(r => r.Outcome == Skipped);
            var total = results.Count();

            _logger.LogInformation(
                "Test run completed. Total: {Total}, Passed: {Passed}, Failed: {Failed}, Skipped: {Skipped}",
                total, passed, failed, skipped);

            foreach (var failure in results.Where(r => r.Outcome == Failed))
            {
                _logger.LogError("Test failed: {Test}\nError: {Error}",
                    failure.TestCase.FullyQualifiedName,
                    failure.ErrorMessage);
            }

            return failed == 0;
        }

        private void UpdateTestProgress(IList<TestResult> results)
        {
            // Define test outcomes
            const int Passed = 1;
            const int Failed = 2;

            var passed = results.Count(r => r.Outcome == Passed);
            var failed = results.Count(r => r.Outcome == Failed);
            var total = results.Count;

            _logger.LogInformation(
                "Running tests... (Passed: {Passed}, Failed: {Failed}, Total: {Total})",
                passed, failed, total);
        }

        private Project GetTestProject(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return _dte.Solution.Projects.Cast<Project>()
                .FirstOrDefault(p =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    return IsTestProject(p) &&
                           p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase);
                });
        }

        private bool IsTestProject(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Check project type or references for test framework
            return project.Name.Contains("Test") ||
                   project.Name.Contains("Tests") ||
                   HasTestFrameworkReference(project);
        }

        private bool HasTestFrameworkReference(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var vsproject = project.Object as VSLangProj.VSProject;
                if (vsproject?.References == null) return false;

                return vsproject.References.Cast<VSLangProj.Reference>()
                    .Any(r => r.Name.Contains("MSTest") ||
                             r.Name.Contains("NUnit") ||
                             r.Name.Contains("xUnit"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check project references");
                return false;
            }
        }

        private async Task<object?> FindTestClassAsync(string className)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Implementation would depend on the test framework being used
            // This is a placeholder for the actual implementation
            return null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cleanup managed resources if needed
                }
                _disposed = true;
            }
        }
    }

    // Simple test result class to match the expected structure
    public class TestResult
    {
        public TestCase TestCase { get; set; } = new TestCase();
        public int Outcome { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public Exception? TestFailureException { get; set; }
    }

    public class TestCase
    {
        public string FullyQualifiedName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
