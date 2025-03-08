using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

// Microsoft CodeAnalysis
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Workspaces;

// Visual Studio Integrations
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

// EnvDTE Namespaces
using global::EnvDTE;
using EnvDTE80;

// VSPilot Namespaces
using VSPilot.Common.Models;
using VSPilot.Common.Exceptions;
using VSPilot.Core.Services;

namespace VSPilot.Core.AI
{
    /// <summary>
    /// Enterprise-grade AI-powered project automation and analysis service
    /// </summary>
    public class VSPilotAIIntegration : IDisposable
    {
        // Core dependencies
        private readonly ILogger<VSPilotAIIntegration> _logger;
        private readonly DTE2 _dte;
        private readonly JoinableTaskFactory _jtf;
        private readonly ConfigurationService _configService;
        private readonly TemplateManager _templateManager;

        // Concurrent collections for thread-safe operations
        private readonly ConcurrentDictionary<string, ProjectAnalysisCache> _projectAnalysisCache = new ConcurrentDictionary<string, ProjectAnalysisCache>();
        private readonly ConcurrentQueue<AnalysisTask> _analysisQueue = new ConcurrentQueue<AnalysisTask>();

        // Cancellation and synchronization
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly SemaphoreSlim _analysisLock = new SemaphoreSlim(1, 1);

        // Advanced analysis configuration
        private const int MAX_FILE_SIZE_BYTES = 1024 * 1024; // 1MB
        private static readonly HashSet<string> SUPPORTED_SOURCE_EXTENSIONS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".cshtml", ".xaml", ".json", ".config", ".xml",
            ".ts", ".js", ".html", ".css", ".razor", ".md"
        };

        // Advanced AI context templates
        private static class PromptTemplates
        {
            public const string COMPREHENSIVE_PROJECT_ANALYSIS = @"
Perform a comprehensive analysis of the project with the following considerations:
- Architectural patterns and potential improvements
- Dependency management and architectural boundaries
- Code quality and potential refactoring opportunities
- Security vulnerability assessment
- Performance optimization suggestions

Project Context:
{0}

Detailed File Analysis:
{1}

Please provide a structured analysis report with actionable insights.";

            public const string PROJECT_GENERATION_PROMPT = @"
Generate a complete project structure based on the following requirements:
{0}

Architectural Considerations:
- Implement clean architecture principles
- Ensure separation of concerns
- Provide scalable and maintainable design
- Include necessary layers: Domain, Application, Infrastructure, Presentation

Generate:
- Project structure
- Key architectural components
- Recommended design patterns
- Initial implementation strategy";
        }

        // Constructors and initialization
        public VSPilotAIIntegration(
            ILogger<VSPilotAIIntegration> logger,
            DTE2 dte,
            ConfigurationService configService,
            TemplateManager templateManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _jtf = ThreadHelper.JoinableTaskFactory;
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _templateManager = templateManager ?? throw new ArgumentNullException(nameof(templateManager));

            // Start background analysis worker
            StartAnalysisWorker();
        }

        /// <summary>
        /// Starts background worker for continuous project analysis
        /// </summary>
        private void StartAnalysisWorker()
        {
            _jtf.RunAsync(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessAnalysisQueueAsync();
                        await Task.Delay(TimeSpan.FromMinutes(5), _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background analysis worker encountered an error");
                    }
                }
            }).FileAndForget("VSPilotAIIntegration/StartAnalysisWorker");
        }

        /// <summary>
        /// Processes the analysis queue with intelligent prioritization
        /// </summary>
        private async Task ProcessAnalysisQueueAsync()
        {
            await _analysisLock.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                while (_analysisQueue.TryDequeue(out var analysisTask))
                {
                    try
                    {
                        await PerformProjectAnalysisAsync(analysisTask);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing analysis task for {analysisTask.ProjectName}");
                    }
                }
            }
            finally
            {
                _analysisLock.Release();
            }
        }

        /// <summary>
        /// Comprehensive project analysis with advanced AI-driven insights
        /// </summary>
        private async Task PerformProjectAnalysisAsync(AnalysisTask analysisTask)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var workspaceContext = await GatherWorkspaceContextAsync(analysisTask.ProjectName);
                var projectContext = workspaceContext.Projects
                    .FirstOrDefault(p => p.Name == analysisTask.ProjectName);

                if (projectContext == null)
                {
                    _logger.LogWarning($"Could not find project {analysisTask.ProjectName} for analysis");
                    return;
                }

                // Perform multi-dimensional analysis
                var codeQualityAnalysis = await AnalyzeCodeQualityAsync(projectContext);
                var architectureAnalysis = AnalyzeArchitecture(projectContext); // Updated line
                var securityAnalysis = await AnalyzeSecurityAsync(projectContext);

                // Combine insights
                var comprehensiveAnalysis = new ProjectAnalysisReport
                {
                    ProjectName = projectContext.Name,
                    CodeQualityInsights = codeQualityAnalysis,
                    ArchitectureInsights = architectureAnalysis,
                    SecurityInsights = securityAnalysis,
                    AnalysisTimestamp = DateTime.UtcNow
                };

                // Cache and potentially trigger automated improvements
                _projectAnalysisCache[projectContext.Name] = new ProjectAnalysisCache
                {
                    LastAnalysis = comprehensiveAnalysis,
                    AnalysisTimestamp = DateTime.UtcNow
                };

                // Optional: Trigger improvement suggestions
                if (analysisTask.ShouldSuggestImprovements)
                {
                    await SuggestProjectImprovementsAsync(comprehensiveAnalysis);
                }

                stopwatch.Stop();
                _logger.LogInformation($"Project analysis completed for {projectContext.Name} in {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Comprehensive project analysis failed for {analysisTask.ProjectName}");
            }
        }

        /// <summary>
        /// Advanced code quality analysis using Roslyn
        /// </summary>
        private async Task<List<CodeQualityInsight>> AnalyzeCodeQualityAsync(ProjectContext projectContext)
        {
            var insights = new ConcurrentBag<CodeQualityInsight>();

            await Task.WhenAll(projectContext.Files
                .Where(f => f.FileType == ".cs")
                .Select(async file =>
                {
                    try
                    {
                        var syntaxTree = CSharpSyntaxTree.ParseText(file.Content);
                        var root = await syntaxTree.GetRootAsync();

                        // Complexity analysis
                        var methodComplexity = root.DescendantNodes()
                            .OfType<MethodDeclarationSyntax>()
                            .Select(method => new
                            {
                                Method = method,
                                CyclomaticComplexity = CalculateCyclomaticComplexity(method)
                            })
                            .Where(m => m.CyclomaticComplexity > 10);

                        foreach (var complexMethod in methodComplexity)
                        {
                            insights.Add(new CodeQualityInsight
                            {
                                Type = CodeQualityIssueType.HighMethodComplexity,
                                Description = $"High cyclomatic complexity in method {complexMethod.Method.Identifier} (Complexity: {complexMethod.CyclomaticComplexity})",
                                Severity = CodeQualitySeverity.Warning,
                                FilePath = file.RelativePath
                            });
                        }

                        // Additional analysis dimensions can be added here
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Code quality analysis failed for file {file.RelativePath}");
                    }
                }));

            return insights.ToList();
        }

        /// <summary>
        /// Calculates cyclomatic complexity for a method
        /// </summary>
        private int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
        {
            int complexity = 1;
            complexity += method.DescendantNodes().OfType<IfStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<SwitchStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<WhileStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<ForStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<ForEachStatementSyntax>().Count();
            complexity += method.DescendantNodes().OfType<CatchClauseSyntax>().Count();
            complexity += method.DescendantNodes().OfType<ConditionalExpressionSyntax>().Count();
            return complexity;
        }

        /// <summary>
        /// Advanced architectural analysis
        /// </summary>
        private List<ArchitectureInsight> AnalyzeArchitecture(ProjectContext projectContext)
        {
            var insights = new List<ArchitectureInsight>();

            // Identify potential layer separation issues
            var layerTypes = projectContext.Files
                .Where(f => f.FileType == ".cs")
                .GroupBy(f => DetermineFileLayer(f.RelativePath));

            foreach (var layer in layerTypes)
            {
                insights.Add(new ArchitectureInsight
                {
                    Description = $"Layer: {layer.Key}",
                    Recommendation = $"Contains {layer.Count()} files"
                });
            }

            return insights;
        }

        /// <summary>
        /// Determines the layer of a file based on its relative path
        /// </summary>
        private string DetermineFileLayer(string relativePath)
        {
            if (relativePath.Contains("Controllers"))
                return "Presentation";
            if (relativePath.Contains("Services") || relativePath.Contains("Managers"))
                return "Application";
            if (relativePath.Contains("Repositories") || relativePath.Contains("Data"))
                return "Infrastructure";
            if (relativePath.Contains("Models") || relativePath.Contains("Entities"))
                return "Domain";

            return "Unknown";
        }

        /// <summary>
        /// Security vulnerability analysis
        /// </summary>
        private async Task<List<SecurityInsight>> AnalyzeSecurityAsync(ProjectContext projectContext)
        {
            return await Task.Run(() =>
            {
                var insights = new List<SecurityInsight>();

                // Scan for potential security vulnerabilities
                foreach (var file in projectContext.Files.Where(f => f.FileType == ".cs"))
                {
                    var securityIssues = DetectSecurityVulnerabilities(file.Content);
                    insights.AddRange(securityIssues.Select(issue => new SecurityInsight
                    {
                        Type = issue.Type,
                        Description = issue.Description,
                        FilePath = file.RelativePath,
                        Severity = issue.Severity
                    }));
                }

                return insights;
            });
        }

        /// <summary>
        /// Detects potential security vulnerabilities in source code
        /// </summary>
        private List<SecurityVulnerability> DetectSecurityVulnerabilities(string fileContent)
        {
            var vulnerabilities = new List<SecurityVulnerability>();

            // SQL Injection potential detection
            if (Regex.IsMatch(fileContent, @"\.ExecuteReader\(|\.ExecuteNonQuery\(|\.ExecuteScalar\(", RegexOptions.IgnoreCase))
            {
                vulnerabilities.Add(new SecurityVulnerability
                {
                    Type = SecurityVulnerabilityType.SqlInjection,
                    Description = "Potential SQL injection vulnerability detected. Use parameterized queries.",
                    Severity = SecuritySeverity.High
                });
            }

            // Potential hardcoded credentials
            if (Regex.IsMatch(fileContent, @"(password|connectionstring|apikey)\s*=\s*[""'][^""']+[""']", RegexOptions.IgnoreCase))
            {
                vulnerabilities.Add(new SecurityVulnerability
                {
                    Type = SecurityVulnerabilityType.HardcodedCredentials,
                    Description = "Potential hardcoded credentials detected. Use secure configuration or secret management.",
                    Severity = SecuritySeverity.Critical
                });
            }

            // More vulnerability checks can be added here

            return vulnerabilities;
        }

        /// <summary>
        /// Automated project improvement suggestions
        /// </summary>
        private async Task SuggestProjectImprovementsAsync(ProjectAnalysisReport analysisReport)
        {
            // Potential improvement strategies based on analysis
            var improvements = new List<ProjectImprovement>();

            // Code complexity improvements
            improvements.AddRange(
                analysisReport.CodeQualityInsights
                    .Where(insight => insight.Severity == CodeQualitySeverity.Warning)
                    .Select(insight => new ProjectImprovement
                    {
                        Type = ImprovementType.CodeRefactoring,
                        Description = $"Refactor method with high complexity: {insight.Description}",
                        FilePath = insight.FilePath
                    })
            );

            // Security vulnerability mitigation
            improvements.AddRange(
                analysisReport.SecurityInsights
                    .Where(insight => insight.Severity == SecuritySeverity.High || insight.Severity == SecuritySeverity.Critical)
                    .Select(insight => new ProjectImprovement
                    {
                        Type = ImprovementType.SecurityEnhancement,
                        Description = $"Address security vulnerability: {insight.Description}",
                        FilePath = insight.FilePath
                    })
            );

            // Trigger improvement process
            await ProcessProjectImprovementsAsync(improvements);
        }

        /// <summary>
        /// Process and potentially apply project improvements
        /// </summary>
        private async Task ProcessProjectImprovementsAsync(List<ProjectImprovement> improvements)
        {
            foreach (var improvement in improvements)
            {
                try
                {
                    switch (improvement.Type)
                    {
                        case ImprovementType.CodeRefactoring:
                            await RefactorCodeAsync(improvement);
                            break;
                        case ImprovementType.SecurityEnhancement:
                            await EnhanceSecurityAsync(improvement);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to apply improvement: {improvement.Description}");
                }
            }
        }

        // Placeholder methods for improvement implementation
        private async Task RefactorCodeAsync(ProjectImprovement improvement)
        {
            // AI-driven code refactoring logic
            await _jtf.RunAsync(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();

                try
                {
                    // Read the file content
                    var fileContent = File.ReadAllText(improvement.FilePath);

                    // Use Roslyn to parse and analyze the code
                    var syntaxTree = CSharpSyntaxTree.ParseText(fileContent);
                    var root = await syntaxTree.GetRootAsync();

                    // Find methods with high complexity
                    var complexMethods = root.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>()
                        .Where(m => CalculateCyclomaticComplexity(m) > 10);

                    // Generate refactoring suggestions
                    var refactoredContent = GenerateRefactoredCode(fileContent, complexMethods);

                    // Write back the refactored code
                    File.WriteAllText(improvement.FilePath, refactoredContent);

                    _logger.LogInformation($"Refactored code in {improvement.FilePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to refactor code in {improvement.FilePath}");
                }
            });
        }

        /// <summary>
        /// Generates refactored code by breaking down complex methods
        /// </summary>
        private string GenerateRefactoredCode(string originalContent, IEnumerable<MethodDeclarationSyntax> complexMethods)
        {
            var refactoredContent = originalContent;

            foreach (var method in complexMethods)
            {
                // Extract complex method into smaller, more focused methods
                var extractedMethods = ExtractMethodParts(method);

                // Replace the original method with refactored version
                refactoredContent = refactoredContent.Replace(
                    method.ToString(),
                    string.Join(Environment.NewLine, extractedMethods)
                );
            }

            return refactoredContent;
        }

        /// <summary>
        /// Breaks down complex methods into smaller, more manageable methods
        /// </summary>
        private List<string> ExtractMethodParts(MethodDeclarationSyntax method)
        {
            var extractedMethods = new List<string>();
            var methodName = method.Identifier.Text;

            // Example strategy: Extract complex conditional blocks
            var conditionalBlocks = method.DescendantNodes()
                .OfType<IfStatementSyntax>()
                .Where(i => CalculateBlockComplexity(i) > 5)
                .ToList();

            for (int i = 0; i < conditionalBlocks.Count; i++)
            {
                var extractedMethodName = $"{methodName}Extract{i + 1}";
                var extractedMethod = $@"
        private {method.ReturnType} {extractedMethodName}({string.Join(", ", method.ParameterList.Parameters)})
        {{
            {conditionalBlocks[i]}
            // Additional extracted logic
            throw new NotImplementedException();
        }}";

                extractedMethods.Add(extractedMethod);
            }

            return extractedMethods;
        }

        /// <summary>
        /// Calculates complexity of a specific code block
        /// </summary>
        private int CalculateBlockComplexity(SyntaxNode block)
        {
            int complexity = 1; // Base complexity

            // Count various control flow and decision points
            complexity += block.DescendantNodes().OfType<IfStatementSyntax>().Count();
            complexity += block.DescendantNodes().OfType<SwitchStatementSyntax>().Count();
            complexity += block.DescendantNodes().OfType<WhileStatementSyntax>().Count();
            complexity += block.DescendantNodes().OfType<ForStatementSyntax>().Count();
            complexity += block.DescendantNodes().OfType<ForEachStatementSyntax>().Count();
            complexity += block.DescendantNodes().OfType<ConditionalExpressionSyntax>().Count(); // Ternary operators

            // Handle logical expressions more robustly
            complexity += block.DescendantNodes()
                .OfType<BinaryExpressionSyntax>()
                .Count(be =>
                    be.Kind() == SyntaxKind.LogicalAndExpression ||
                    be.Kind() == SyntaxKind.LogicalOrExpression
                );

            // Additional complexity from try-catch blocks
            complexity += block.DescendantNodes().OfType<TryStatementSyntax>().Count() * 2;

            return complexity;
        }

        /// <summary>
        /// Enhance security for identified vulnerabilities
        /// </summary>
        private async Task EnhanceSecurityAsync(ProjectImprovement improvement)
        {
            await _jtf.RunAsync(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();

                try
                {
                    var fileContent = File.ReadAllText(improvement.FilePath);
                    var securedContent = SecureCodeTransformation(fileContent);

                    File.WriteAllText(improvement.FilePath, securedContent);

                    _logger.LogInformation($"Applied security enhancements to {improvement.FilePath}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to enhance security in {improvement.FilePath}");
                }
            });
        }

        /// <summary>
        /// Transforms code to improve security
        /// </summary>
        private string SecureCodeTransformation(string originalContent)
        {
            // Replace potential security vulnerabilities
            var securedContent = originalContent;

            // Replace direct SQL queries with parameterized queries
            securedContent = Regex.Replace(
                securedContent,
                @"\.ExecuteReader\(""([^""]+)""\)|\.ExecuteNonQuery\(""([^""]+)""\)",
                match => GenerateSecureQueryMethod(match.Value)
            );

            // Remove hardcoded credentials
            securedContent = Regex.Replace(
                securedContent,
                @"(password|connectionstring|apikey)\s*=\s*[""'][^""']+[""']",
                match => GenerateSecureConfigurationAccess(match.Value)
            );

            return securedContent;
        }

        /// <summary>
        /// Generates secure parameterized query method
        /// </summary>
        private string GenerateSecureQueryMethod(string originalQuery)
        {
            return $@"ExecuteReaderWithParameters(
                    new Dictionary<string, object> {{
                        {{ ""@Param1"", value1 }},
                        {{ ""@Param2"", value2 }}
                    }})";
        }

        /// <summary>
        /// Generates secure configuration access
        /// </summary>
        private string GenerateSecureConfigurationAccess(string originalCredential)
        {
            return $"GetSecureConfiguration(\"{Guid.NewGuid()}\")";
        }

        /// <summary>
        /// Gathers comprehensive workspace context
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD010:Accessing \"Project\" should only be done on the main thread.", Justification = "Accessing project properties on a background thread is necessary for this implementation.")]
        public async Task<WorkspaceContext> GatherWorkspaceContextAsync(string? specificProjectName = null)
        {
            return await _jtf.RunAsync(async () =>
            {
                await _jtf.SwitchToMainThreadAsync();

                var context = new WorkspaceContext
                {
                    SolutionName = _dte.Solution.FullName,
                    Projects = new List<ProjectContext>()
                };

                // Filter projects if specific project name is provided
                var projectsToAnalyze = specificProjectName != null
                    ? _dte.Solution.Projects.Cast<EnvDTE.Project>()
                        .Where(p => p.Name == specificProjectName)
                    : _dte.Solution.Projects.Cast<EnvDTE.Project>();

                // Scan through selected projects
                foreach (EnvDTE.Project project in projectsToAnalyze)
                {
                    var projectContext = await ScanProjectAsync(project);
                    context.Projects.Add(projectContext);
                }

                return context;
            });
        }


        /// <summary>
        /// Scans a specific project and gathers comprehensive context
        /// </summary>
        private async Task<ProjectContext> ScanProjectAsync(EnvDTE.Project project)
        {
            await _jtf.SwitchToMainThreadAsync();

            var projectContext = new ProjectContext
            {
                Name = project.Name,
                FullPath = project.FullName,
                Files = new List<FileContext>()
            };

            ScanProjectItems(project.ProjectItems, projectContext.Files);

            return projectContext;
        }

        /// <summary>
        /// Recursively scans project items and collects file contexts
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD010:Accessing \"ProjectItems\" should only be done on the main thread.", Justification = "Accessing project properties on a background thread is necessary for this implementation.")]
        private void ScanProjectItems(ProjectItems items, List<FileContext> fileContexts)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // Ensure this method runs on the main thread

            foreach (ProjectItem item in items)
            {
                if (item.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile)
                {
                    var fileName = item.FileNames[1];
                    if (IsValidSourceFile(fileName))
                    {
                        fileContexts.Add(new FileContext
                        {
                            RelativePath = GetRelativePath(fileName),
                            FullPath = fileName,
                            Content = ReadFileContent(fileName),
                            FileType = Path.GetExtension(fileName)
                        });
                    }
                }
                else if (item.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFolder)
                {
                    ScanProjectItems(item.ProjectItems, fileContexts); // Fixed line
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD010:Accessing \"ProjectItems\" should only be done on the main thread.", Justification = "Accessing project properties on a background thread is necessary for this implementation.")]
        private void ProcessNestedProjectItems(ProjectItems items, List<FileContext> fileContexts)
        {
            ScanProjectItems(items, fileContexts);
        }

        /// <summary>
        /// Checks if a file is a valid source file for analysis
        /// </summary>
        private bool IsValidSourceFile(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (!SUPPORTED_SOURCE_EXTENSIONS.Contains(extension)) return false;

            var fileInfo = new FileInfo(fileName);

            // Limit file size
            return fileInfo.Exists && fileInfo.Length < MAX_FILE_SIZE_BYTES;
        }

        /// <summary>
        /// Reads file content safely
        /// </summary>
        private string ReadFileContent(string fileName)
        {
            try
            {
                return File.ReadAllText(fileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Could not read file: {fileName}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets relative path for a file within the project
        /// </summary>
        private string GetRelativePath(string fullPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = _dte.Solution.Projects.Cast<EnvDTE.Project>().FirstOrDefault();
            if (project == null) return fullPath;
            var projectPath = Path.GetDirectoryName(project.FullName);
            return fullPath.StartsWith(projectPath)
                ? fullPath.Substring(projectPath.Length).TrimStart('\\', '/')
                : fullPath;
        }

        /// <summary>
        /// Queues a project for analysis
        /// </summary>
        public void QueueVSPilotProjectAnalysis(string projectName, bool suggestImprovements = true)
        {
            _analysisQueue.Enqueue(new AnalysisTask
            {
                ProjectName = projectName,
                ShouldSuggestImprovements = suggestImprovements
            });
        }

        /// <summary>
        /// Disposes of resources
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _analysisLock.Dispose();
        }

        // Supporting model classes for analysis
        private class AnalysisTask
        {
            public string ProjectName { get; set; } = string.Empty;
            public bool ShouldSuggestImprovements { get; set; }
        }

        private class ProjectAnalysisCache
        {
            public ProjectAnalysisReport LastAnalysis { get; set; } = new ProjectAnalysisReport();
            public DateTime AnalysisTimestamp { get; set; }
        }

        public async Task<VSPilot.Common.Models.ProjectChanges> GetProjectChangesAsync(string aiPrompt)
        {
            // Implement the logic to get project changes based on the aiPrompt
            // This is a placeholder implementation
            return await Task.FromResult(new VSPilot.Common.Models.ProjectChanges
            {
                NewFiles = new List<FileCreationInfo>(),
                ModifiedFiles = new List<FileModificationInfo>(),
                RequiredReferences = new List<string>()
            });
        }

        // Additional supporting enums and model classes defined earlier...
    }

    // Supporting enums and classes
    public enum CodeQualityIssueType
    {
        HighMethodComplexity,
        DuplicateCode,
        LongMethod,
        LargeClass
    }

    public enum CodeQualitySeverity
    {
        Info,
        Warning,
        Critical
    }

    public enum SecurityVulnerabilityType
    {
        SqlInjection,
        HardcodedCredentials,
        CrossSiteScripting,
        InsecureDeserialization
    }

    public enum SecuritySeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum ImprovementType
    {
        CodeRefactoring,
        SecurityEnhancement,
        PerformanceOptimization,
        ArchitecturalImprovement
    }

    // Data transfer objects for analysis
    public class ProjectAnalysisReport
    {
        public string ProjectName { get; set; } = string.Empty;
        public List<CodeQualityInsight> CodeQualityInsights { get; set; } = new List<CodeQualityInsight>();
        public List<ArchitectureInsight> ArchitectureInsights { get; set; } = new List<ArchitectureInsight>();
        public List<SecurityInsight> SecurityInsights { get; set; } = new List<SecurityInsight>();
        public DateTime AnalysisTimestamp { get; set; }
    }

    public class CodeQualityInsight
    {
        public CodeQualityIssueType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public CodeQualitySeverity Severity { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }

    public class ArchitectureInsight
    {
        public string Description { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
    }

    public class SecurityInsight
    {
        public SecurityVulnerabilityType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public SecuritySeverity Severity { get; set; }
    }

    public class ProjectImprovement
    {
        public ImprovementType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    public class SecurityVulnerability
    {
        public SecurityVulnerabilityType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public SecuritySeverity Severity { get; set; }
    }

    // Workspace and project context classes
    public class WorkspaceContext
    {
        public string SolutionName { get; set; } = string.Empty;
        public List<ProjectContext> Projects { get; set; } = new List<ProjectContext>();
    }

    public class ProjectContext
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public List<FileContext> Files { get; set; } = new List<FileContext>();
    }

    public class FileContext
    {
        public string RelativePath { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
    }
}