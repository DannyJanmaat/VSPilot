using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VSPilot.Common.Models;
using Microsoft.Extensions.Logging;

namespace VSPilot.Common.Interfaces
{
    /// <summary>
    /// Defines operations for managing Visual Studio projects.
    /// </summary>
    public interface IProjectManager
    {
        /// <summary>
        /// Creates a new project structure with standard folders and files.
        /// </summary>
        /// <param name="projectType">Type of project to create (e.g., Console, WPF, Library).</param>
        /// <param name="name">Name of the project.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        Task CreateProjectStructureAsync(string projectType, string name, CancellationToken cancellationToken);

        /// <summary>
        /// Applies a set of changes to the project.
        /// </summary>
        /// <param name="changes">Changes to apply to the project.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        Task ApplyChangesAsync(ProjectChangeRequest changes, CancellationToken cancellationToken);

        /// <summary>
        /// Adds a reference to the project.
        /// </summary>
        /// <param name="referenceName">Name of the reference to add.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        Task AddProjectReferenceAsync(string referenceName, CancellationToken cancellationToken);

        /// <summary>
        /// Checks if a project exists in the solution.
        /// </summary>
        /// <param name="projectName">Name of the project to check.</param>
        /// <returns>True if the project exists, false otherwise.</returns>
        Task<bool> ProjectExistsAsync(string projectName);

        /// <summary>
        /// Gets project configuration details.
        /// </summary>
        /// <param name="projectName">Name of the project.</param>
        /// <returns>Project configuration information.</returns>
        Task<ProjectConfiguration> GetProjectConfigurationAsync(string projectName);

        /// <summary>
        /// Updates project configuration settings.
        /// </summary>
        /// <param name="projectName">Name of the project to update.</param>
        /// <param name="configuration">New configuration settings.</param>
        Task UpdateProjectConfigurationAsync(string projectName, ProjectConfiguration configuration);

        /// <summary>
        /// Gets the list of files in a project.
        /// </summary>
        /// <param name="projectName">Name of the project.</param>
        /// <returns>List of file information.</returns>
        Task<IEnumerable<ProjectFileInfo>> GetProjectFilesAsync(string projectName);

        /// <summary>
        /// Gets project dependencies.
        /// </summary>
        /// <param name="projectName">Name of the project.</param>
        /// <returns>List of project dependencies.</returns>
        Task<IEnumerable<ProjectDependency>> GetProjectDependenciesAsync(string projectName);

        /// <summary>
        /// Event raised when project changes are detected.
        /// </summary>
        event EventHandler<ProjectChangeEventArgs> ProjectChanged;
    }

    /// <summary>
    /// Represents project configuration settings.
    /// </summary>
    public class ProjectConfiguration
    {
        /// <summary>
        /// Gets or sets the output type (e.g., Library, Executable).
        /// </summary>
        public string OutputType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target framework.
        /// </summary>
        public string TargetFramework { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the platform target.
        /// </summary>
        public string Platform { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets build configuration settings.
        /// </summary>
        public Dictionary<string, string> BuildProperties { get; set; } = new();

        /// <summary>
        /// Gets or sets compiler settings.
        /// </summary>
        public Dictionary<string, string> CompilerSettings { get; set; } = new();

        /// <summary>
        /// Gets or sets project-specific settings.
        /// </summary>
        public Dictionary<string, string> CustomProperties { get; set; } = new();
    }

    /// <summary>
    /// Represents information about a project file.
    /// </summary>
    public class ProjectFileInfo
    {
        /// <summary>
        /// Gets or sets the file path relative to project root.
        /// </summary>
        public string RelativePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the full file path.
        /// </summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the build action (Compile, Content, None, etc.).
        /// </summary>
        public string BuildAction { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether to copy to output directory.
        /// </summary>
        public string CopyToOutput { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets custom metadata.
        /// </summary>
        public Dictionary<string, string> CustomMetadata { get; set; } = new();
    }

    /// <summary>
    /// Represents a project dependency.
    /// </summary>
    public class ProjectDependency
    {
        /// <summary>
        /// Gets or sets the name of the dependency.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the version of the dependency.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of dependency (Project, Package, Assembly).
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether this is a direct or transitive dependency.
        /// </summary>
        public bool IsDirectDependency { get; set; }
    }

    /// <summary>
    /// Event arguments for project changes.
    /// </summary>
    public class ProjectChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the name of the project that changed.
        /// </summary>
        public string ProjectName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of change that occurred.
        /// </summary>
        public ProjectChangeType ChangeType { get; set; }

        /// <summary>
        /// Gets or sets additional details about the change.
        /// </summary>
        public string ChangeDetails { get; set; } = string.Empty;
    }

    /// <summary>
    /// Enumeration of project change types.
    /// </summary>
    public enum ProjectChangeType
    {
        /// <summary>
        /// Files added to the project.
        /// </summary>
        FileAdded,

        /// <summary>
        /// Files modified in the project.
        /// </summary>
        FileModified,

        /// <summary>
        /// Files removed from the project.
        /// </summary>
        FileRemoved,

        /// <summary>
        /// References added to the project.
        /// </summary>
        ReferenceAdded,

        /// <summary>
        /// References removed from the project.
        /// </summary>
        ReferenceRemoved,

        /// <summary>
        /// Project configuration changed.
        /// </summary>
        ConfigurationChanged
    }
}
