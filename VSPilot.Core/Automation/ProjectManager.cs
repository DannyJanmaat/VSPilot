using Microsoft.VisualStudio.Shell;
using EnvDTE;
using EnvDTE80;
using VSLangProj;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using VSPilot.Common.Models;
using VSPilot.Common.Interfaces;
using VSPilot.Common.Exceptions;
using Microsoft.VisualStudio.Shell.Interop;

namespace VSPilot.Core.Automation
{
    /// <summary>
    /// Abstraction for Visual Studio solution interactions
    /// </summary>
    public interface IVsSolutionContext
    {
        /// <summary>
        /// Checks if a project exists in the solution
        /// </summary>
        /// <param name="projectName">Name of the project to check</param>
        /// <returns>True if the project exists, false otherwise</returns>
        bool ProjectExists(string projectName);

        /// <summary>
        /// Adds a folder to a project
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <param name="folderName">Name of the folder to add</param>
        void AddFolder(string projectName, string folderName);

        /// <summary>
        /// Adds a reference to a project
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <param name="referenceName">Name of the reference to add</param>
        void AddReference(string projectName, string referenceName);

        /// <summary>
        /// Gets project configuration
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <returns>Project configuration details</returns>
        ProjectConfiguration GetProjectConfiguration(string projectName);

        /// <summary>
        /// Updates project configuration
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <param name="configuration">New configuration details</param>
        void UpdateProjectConfiguration(string projectName, ProjectConfiguration configuration);

        /// <summary>
        /// Gets project files
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <returns>List of project files</returns>
        IEnumerable<ProjectFileInfo> GetProjectFiles(string projectName);

        /// <summary>
        /// Gets project dependencies
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <returns>List of project dependencies</returns>
        IEnumerable<ProjectDependency> GetProjectDependencies(string projectName);
    }

    /// <summary>
    /// Adapts Visual Studio solution interactions with thread safety
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD010:Invoke single-threaded types on Main thread",
        Justification = "Visual Studio automation APIs require main thread access, properly handled via ThreadHelper")]
    public class VsSolutionAdapter : IVsSolutionContext
    {
        private readonly DTE2 _dte;
        private readonly FileManager _fileManager;

        public VsSolutionAdapter(DTE2 dte, FileManager fileManager)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
        }

        public bool ProjectExists(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _dte.Solution.Projects.Cast<Project>()
                .Any(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
        }

        public void AddFolder(string projectName, string folderName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = _dte.Solution.Projects.Cast<Project>()
                .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                throw new InvalidOperationException($"Project not found: {projectName}");
            }

            project.ProjectItems.AddFolder(folderName);
        }

        public void AddReference(string projectName, string referenceName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = _dte.Solution.Projects.Cast<Project>()
                .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                throw new InvalidOperationException($"Project not found: {projectName}");
            }

            var vsProject = project.Object as VSProject;
            if (vsProject == null)
            {
                throw new InvalidOperationException("Could not access project references");
            }

            vsProject.References.Add(referenceName);
        }

        public ProjectConfiguration GetProjectConfiguration(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = _dte.Solution.Projects.Cast<Project>()
                .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                throw new InvalidOperationException($"Project not found: {projectName}");
            }

            var config = new ProjectConfiguration
            {
                BuildProperties = new Dictionary<string, string>(),
                CompilerSettings = new Dictionary<string, string>(),
                CustomProperties = new Dictionary<string, string>()
            };

            try { config.OutputType = project.Properties.Item("OutputType").Value.ToString(); }
            catch { config.OutputType = "Unknown"; }

            try { config.TargetFramework = project.Properties.Item("TargetFramework").Value.ToString(); }
            catch { config.TargetFramework = "Unknown"; }

            try
            {
                if (project.ConfigurationManager?.ActiveConfiguration != null)
                {
                    var platformName = project.ConfigurationManager.ActiveConfiguration.PlatformName;
                    config.Platform = !string.IsNullOrEmpty(platformName) ? platformName : "AnyCPU";
                }
                else
                {
                    config.Platform = "AnyCPU";
                }
            }
            catch { config.Platform = "AnyCPU"; }

            return config;
        }

        public void UpdateProjectConfiguration(string projectName, ProjectConfiguration configuration)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = _dte.Solution.Projects.Cast<Project>()
                .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                throw new InvalidOperationException($"Project not found: {projectName}");
            }

            if (!string.IsNullOrEmpty(configuration.OutputType))
            {
                try { project.Properties.Item("OutputType").Value = configuration.OutputType; }
                catch (Exception) { /* Log or handle as needed */ }
            }

            if (!string.IsNullOrEmpty(configuration.TargetFramework))
            {
                try { project.Properties.Item("TargetFramework").Value = configuration.TargetFramework; }
                catch (Exception) { /* Log or handle as needed */ }
            }

            foreach (var prop in configuration.CustomProperties)
            {
                try { project.Properties.Item(prop.Key).Value = prop.Value; }
                catch (Exception) { /* Log or handle as needed */ }
            }
        }

        public IEnumerable<ProjectFileInfo> GetProjectFiles(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = _dte.Solution.Projects.Cast<Project>()
                .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                throw new InvalidOperationException($"Project not found: {projectName}");
            }

            var files = new List<ProjectFileInfo>();
            var projectPath = Path.GetDirectoryName(project.FullName);

            void TraverseProjectItems(ProjectItems items, string folderPath = "")
            {
                foreach (ProjectItem item in items)
                {
                    try
                    {
                        if (item.FileCount > 0)
                        {
                            for (short i = 1; i <= item.FileCount; i++)
                            {
                                string fileName = item.FileNames[i];
                                if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
                                {
                                    var relPath = fileName;
                                    if (fileName.StartsWith(projectPath))
                                    {
                                        relPath = fileName.Substring(projectPath.Length).TrimStart('\\', '/');
                                    }

                                    files.Add(new ProjectFileInfo
                                    {
                                        FullPath = fileName,
                                        RelativePath = relPath,
                                        BuildAction = GetBuildAction(item),
                                        CopyToOutput = GetCopyToOutput(item)
                                    });
                                }
                            }
                        }

                        if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                        {
                            TraverseProjectItems(item.ProjectItems, Path.Combine(folderPath, item.Name));
                        }
                    }
                    catch (Exception)
                    {
                        // Log or handle error
                    }
                }
            }

            TraverseProjectItems(project.ProjectItems);
            return files;
        }

        public IEnumerable<ProjectDependency> GetProjectDependencies(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var project = _dte.Solution.Projects.Cast<Project>()
                .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
                throw new InvalidOperationException($"Project not found: {projectName}");
            }

            var dependencies = new List<ProjectDependency>();
            var vsProject = project.Object as VSProject;

            if (vsProject?.References != null)
            {
                foreach (Reference reference in vsProject.References)
                {
                    dependencies.Add(new ProjectDependency
                    {
                        Name = reference.Name,
                        Version = GetReferenceVersion(reference),
                        Type = GetReferenceType(reference),
                        IsDirectDependency = true
                    });
                }
            }

            return dependencies;
        }

        private string GetBuildAction(ProjectItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return item.Properties.Item("BuildAction").Value.ToString(); }
            catch { return "None"; }
        }

        private string GetCopyToOutput(ProjectItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { return item.Properties.Item("CopyToOutputDirectory").Value.ToString(); }
            catch { return "DoNotCopy"; }
        }

        private string GetReferenceVersion(Reference reference)
        {
            try
            {
                var ver = reference.GetType().GetProperty("Version")?.GetValue(reference);
                return ver?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private string GetReferenceType(Reference reference)
        {
            try
            {
                if (reference.SourceProject != null) return "Project";
                if (reference.Path?.EndsWith(".dll") == true) return "Assembly";
                return "Package";
            }
            catch { return "Unknown"; }
        }
    }

    public class ProjectManager : IProjectManager, IDisposable
    {
        private readonly IVsSolutionContext _solutionContext;
        private readonly FileManager _fileManager;
        private readonly ILogger<ProjectManager> _logger;
        private bool _disposed;

        public event EventHandler<ProjectChangeEventArgs> ProjectChanged
        {
            add { /* Optional: Add logging or diagnostics */ }
            remove { /* Optional: Add logging or diagnostics */ }
        }

        public ProjectManager(
            IVsSolutionContext solutionContext,
            FileManager fileManager,
            ILogger<ProjectManager> logger)
        {
            _solutionContext = solutionContext ?? throw new ArgumentNullException(nameof(solutionContext));
            _fileManager = fileManager ?? throw new ArgumentNullException(nameof(fileManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task CreateProjectStructureAsync(string projectType, string name, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _logger.LogInformation("Creating project structure for {ProjectType}: {Name}", projectType, name);

            var folders = GetPreferredFolderStructure();

            foreach (var folder in folders)
            {
                _solutionContext.AddFolder(name, folder);
                _logger.LogDebug("Created folder: {Folder}", folder);
            }

            await CreateStandardProjectItemsAsync(projectType);
            _logger.LogInformation("Project structure created successfully");
        }

        public async Task<bool> ProjectExistsAsync(string projectName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return _solutionContext.ProjectExists(projectName);
        }

        public async Task AddProjectReferenceAsync(string referenceName, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Attempt to get the current project's name
            var currentProjects = await GetCurrentProjectsAsync();
            if (currentProjects.Any())
            {
                _solutionContext.AddReference(currentProjects.First(), referenceName);
                _logger.LogInformation("Added reference: {Reference}", referenceName);
            }
            else
            {
                throw new AutomationException("No project found to add reference");
            }
        }

        public async Task<ProjectConfiguration> GetProjectConfigurationAsync(string projectName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return _solutionContext.GetProjectConfiguration(projectName);
        }

        public async Task UpdateProjectConfigurationAsync(string projectName, ProjectConfiguration configuration)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _solutionContext.UpdateProjectConfiguration(projectName, configuration);
        }

        public async Task<IEnumerable<ProjectFileInfo>> GetProjectFilesAsync(string projectName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return _solutionContext.GetProjectFiles(projectName);
        }

        public async Task<IEnumerable<ProjectDependency>> GetProjectDependenciesAsync(string projectName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return _solutionContext.GetProjectDependencies(projectName);
        }

        private async Task<List<string>> GetCurrentProjectsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return _solutionContext.GetProjectFiles(string.Empty)
                .Select(p => p.FullPath)
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();
        }

        private async Task CreateStandardProjectItemsAsync(string projectType)
        {
            // Implementation depends on project type
            await Task.CompletedTask;
        }

        private string[] GetPreferredFolderStructure()
        {
            return new[]
            {
                "Models",
                "ViewModels",
                "Views",
                "Services",
                "Interfaces",
                "Helpers",
                "Tests"
            };
        }

        public async Task ApplyChangesAsync(ProjectChangeRequest changes, CancellationToken cancellationToken = default)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));

            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                _logger.LogInformation("Applying project changes");

                // Create new files
                if (changes.FilesToCreate?.Any() == true)
                {
                    foreach (var file in changes.FilesToCreate)
                    {
                        await _fileManager.CreateFileAsync(file.Path, file.Content);
                        _logger.LogInformation("Created file: {File}", file.Path);
                    }
                }

                // Modify existing files
                if (changes.FilesToModify?.Any() == true)
                {
                    foreach (var file in changes.FilesToModify)
                    {
                        string content = string.Join(Environment.NewLine,
                            file.Changes.Select(c => c.NewContent));

                        await _fileManager.ModifyFileAsync(file.Path, content);
                        _logger.LogInformation("Modified file: {File}", file.Path);
                    }
                }

                // Add references
                if (changes.References?.Any() == true)
                {
                    var currentProjects = await GetCurrentProjectsAsync();
                    if (currentProjects.Any())
                    {
                        foreach (var reference in changes.References)
                        {
                            _solutionContext.AddReference(currentProjects.First(), reference);
                            _logger.LogInformation("Added reference: {Reference}", reference);
                        }
                    }
                }

                _logger.LogInformation("Project changes applied successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply project changes");
                throw new AutomationException("Failed to apply project changes", ex);
            }
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
                    (_fileManager as IDisposable)?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}