using Microsoft.VisualStudio.Shell;
using Microsoft.Extensions.Logging;
using EnvDTE;
using EnvDTE80;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;
using VSPilot.Core.AI;
using VSPilot.Common.Models;
using VSPilot.Common.Interfaces;
using VSPilot.Common.Exceptions;

namespace VSPilot.Core.Build
{
    public class ErrorHandler : IErrorHandler, IDisposable
    {
        private readonly DTE2 _dte;
        private readonly AIRequestHandler _aiHandler;
        private readonly ILogger<ErrorHandler> _logger;
        private readonly Dictionary<string, string> _commonErrorPatterns = new Dictionary<string, string>();
        private bool _disposed;

        public ErrorHandler(DTE2 dte, AIRequestHandler aiHandler, ILogger<ErrorHandler> logger)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _aiHandler = aiHandler ?? throw new ArgumentNullException(nameof(aiHandler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeErrorPatterns();
        }

        private void InitializeErrorPatterns()
        {
            _commonErrorPatterns.Add(@"CS\d{4}", "Compiler Error");
            _commonErrorPatterns.Add(@"MSB\d{4}", "MSBuild Error");
            _commonErrorPatterns.Add(@"NETSDK\d{4}", ".NET SDK Error");
            _commonErrorPatterns.Add(@"NU\d{4}", "NuGet Error");
            _commonErrorPatterns.Add(@"VSTHRD\d{3}", "Threading Error");
        }

        public async Task<IEnumerable<VSPilotErrorItem>> GetBuildErrorsAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Retrieving build errors");

                var errorItems = _dte.ToolWindows.ErrorList.ErrorItems;
                var errorList = new List<VSPilotErrorItem>();

                for (int i = 1; i <= errorItems.Count; i++)
                {
                    var error = errorItems.Item(i);
                    if (IsErrorSeverity(error))
                    {
                        var errorItem = new VSPilotErrorItem(
                            description: error.Description,
                            fileName: error.FileName,
                            line: error.Line,
                            column: error.Column);

                        errorList.Add(errorItem);
                        _logger.LogDebug("Found error: {Description} in {File} at line {Line}",
                            error.Description, error.FileName, error.Line);
                    }
                }

                _logger.LogInformation("Found {Count} build errors", errorList.Count);
                return errorList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve build errors");
                throw new AutomationException("Failed to retrieve build errors", ex);
            }
        }

        public async Task FixErrorsAsync(IEnumerable<VSPilotErrorItem> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Starting error fix process for {Count} errors", errors.Count());

                // Group errors by file to minimize file operations
                var errorsByFile = errors.GroupBy(e => e.FileName);

                foreach (var fileGroup in errorsByFile)
                {
                    await FixFileErrorsAsync(fileGroup.Key, fileGroup);
                }

                _logger.LogInformation("Completed error fix process");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fix errors");
                throw new AutomationException("Failed to fix errors", ex);
            }
        }

        public async Task<ErrorAnalysis> GetErrorAnalysisAsync(VSPilotErrorItem error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));

            try
            {
                // Use fully qualified name to avoid ambiguity
                var analysis = new VSPilot.Common.Models.ErrorAnalysis
                {
                    RelatedFiles = new List<string>(),
                    RequiredReferences = new List<string>()
                };

                var fileContent = File.ReadAllLines(error.FileName);
                var contextLines = GetErrorContext(fileContent, error.Line);

                var errorType = AnalyzeErrorType(error.Description);
                analysis.ProbableCause = DetermineProbableCause(errorType, error.Description);
                analysis.SuggestedFix = await GetSuggestedFixAsync(error, contextLines);
                analysis.CanAutoFix = CanAutoFix(errorType);
                analysis.RelatedFiles = await FindRelatedFilesAsync(error.FileName);
                analysis.RequiredReferences = await CheckRequiredReferencesAsync(error);

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze error");
                throw new AutomationException("Failed to analyze error", ex);
            }
        }

        private async Task FixFileErrorsAsync(string fileName, IEnumerable<VSPilotErrorItem> errors)
        {
            try
            {
                Document? document = null;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                foreach (Document doc in _dte.Documents)
                {
                    if (doc.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        document = doc;
                        break;
                    }
                }

                if (document == null)
                {
                    var window = _dte.ItemOperations.OpenFile(fileName);
                    document = window as Document;
                    if (document == null && window != null)
                    {
                        document = _dte.Documents.Cast<Document>()
                            .FirstOrDefault(d =>
                            {
                                ThreadHelper.ThrowIfNotOnUIThread();
                                return d.FullName.Equals(fileName, StringComparison.OrdinalIgnoreCase);
                            });
                    }
                }

                if (document == null)
                {
                    throw new InvalidOperationException($"Failed to open or find document: {fileName}");
                }

                var selection = (TextSelection)document.Selection;

                foreach (var error in errors)
                {
                    var fix = await _aiHandler.GetErrorFixAsync(error);
                    if (!string.IsNullOrEmpty(fix))
                    {
                        selection.GotoLine(error.Line);
                        selection.SelectLine();
                        selection.Insert(fix);
                    }
                }

                document.Save();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fix errors in file: {File}", fileName);
                throw;
            }
        }

        private bool IsErrorSeverity(EnvDTE80.ErrorItem error)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return error.ErrorLevel == vsBuildErrorLevel.vsBuildErrorLevelHigh ||
                   error.ErrorLevel == vsBuildErrorLevel.vsBuildErrorLevelLow;
        }

        public async Task<bool> IsErrorCriticalAsync(VSPilotErrorItem error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));

            return await Task.Run(() =>
            {
                // Check error patterns for severity
                foreach (var pattern in _commonErrorPatterns)
                {
                    if (Regex.IsMatch(error.Description, pattern.Key))
                    {
                        return pattern.Value == "Compiler Error" ||
                               pattern.Value == "MSBuild Error";
                    }
                }

                // Analyze error message for critical keywords
                var criticalKeywords = new[] { "fatal", "critical", "security", "crash", "deadlock" };
                return criticalKeywords.Any(k => error.Description.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
            });
        }

        private string[] GetErrorContext(string[] fileContent, int errorLine)
        {
            // Add bounds checking
            if (errorLine <= 0 || errorLine > fileContent.Length)
            {
                return Array.Empty<string>();
            }

            var startLine = Math.Max(0, errorLine - 3);
            var endLine = Math.Min(fileContent.Length, errorLine + 3);
            return fileContent.Skip(startLine).Take(endLine - startLine).ToArray();
        }

        private string AnalyzeErrorType(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return "Unknown";
            }

            foreach (var pattern in _commonErrorPatterns)
            {
                if (Regex.IsMatch(description, pattern.Key, RegexOptions.Compiled | RegexOptions.IgnoreCase))
                {
                    return pattern.Value;
                }
            }
            return "Unknown";
        }

        private string DetermineProbableCause(string errorType, string description)
        {
            return errorType switch
            {
                "Compiler Error" => AnalyzeCompilerError(description),
                "MSBuild Error" => AnalyzeMSBuildError(description),
                "NuGet Error" => AnalyzeNuGetError(description),
                "Threading Error" => AnalyzeThreadingError(description),
                _ => "Unknown error cause"
            };
        }

        private string AnalyzeCompilerError(string description)
        {
            if (description.Contains("not found")) return "Missing type or namespace";
            if (description.Contains("cannot be converted")) return "Type mismatch";
            if (description.Contains("is inaccessible")) return "Access level mismatch";
            return "Syntax or semantic error";
        }

        private string AnalyzeMSBuildError(string description)
        {
            if (description.Contains("target")) return "Missing or invalid target";
            if (description.Contains("reference")) return "Reference issue";
            return "Build configuration error";
        }

        private string AnalyzeNuGetError(string description)
        {
            if (description.Contains("restore")) return "Package restore failure";
            if (description.Contains("version")) return "Version conflict";
            return "Package management error";
        }

        private string AnalyzeThreadingError(string description)
        {
            if (description.Contains("async")) return "Async/await usage issue";
            if (description.Contains("deadlock")) return "Potential deadlock";
            return "Threading pattern error";
        }

        private async Task<string> GetSuggestedFixAsync(VSPilotErrorItem error, string[] context)
        {
            // Use AI to generate fix based on error and context
            var fix = await _aiHandler.GetErrorFixAsync(error);
            return fix ?? "No automatic fix available";
        }

        private bool CanAutoFix(string errorType)
        {
            return errorType switch
            {
                "Compiler Error" => true,
                "MSBuild Error" => false,
                "NuGet Error" => true,
                "Threading Error" => false,
                _ => false
            };
        }

        private async Task<List<string>> FindRelatedFilesAsync(string fileName)
        {
            return await Task.Run(() =>
            {
                var relatedFiles = new List<string>();
                var currentFile = Path.GetFileName(fileName);
                var directory = Path.GetDirectoryName(fileName);

                var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var content = File.ReadAllText(file);
                    if (content.Contains(currentFile))
                    {
                        relatedFiles.Add(file);
                    }
                }

                return relatedFiles;
            });
        }

        private async Task<List<string>> CheckRequiredReferencesAsync(VSPilotErrorItem error)
        {
            var references = new List<string>();

            if (error.Description.Contains("not found") || error.Description.Contains("undefined"))
            {
                // Extract potential type names from error
                var typeNames = Regex.Matches(error.Description, @"'([^']+)'")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value);

                foreach (var type in typeNames)
                {
                    // Check common NuGet packages for the type
                    var potentialPackage = await FindPackageForTypeAsync(type);
                    if (potentialPackage != null)
                    {
                        references.Add(potentialPackage);
                    }
                }
            }

            return references;
        }

        private async Task<string?> FindPackageForTypeAsync(string typeName)
        {
            // Simulate an asynchronous operation
            await Task.Delay(100);
            // Add logic to search NuGet packages
            // This is a placeholder implementation
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
}
