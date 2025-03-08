using EnvDTE;
using EnvDTE80;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VSPilot.Common.Models;
using VSPilot.Core.Services;

namespace VSPilot.Core.Services
{
    /// <summary>
    /// Provides advanced analysis and inspection capabilities for Visual Studio solutions.
    /// </summary>
    public class SolutionAnalyzer
    {
        private readonly DTE2 _dte;
        private readonly LoggingService _logger;

        /// <summary>
        /// Initializes a new instance of the SolutionAnalyzer class.
        /// </summary>
        /// <param name="dte">The DTE2 service for accessing Visual Studio automation.</param>
        /// <param name="logger">The logging service for recording analysis events.</param>
        public SolutionAnalyzer(DTE2 dte, LoggingService logger)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Performs a comprehensive analysis of the current solution asynchronously.
        /// </summary>
        /// <returns>Detailed information about the solution and its projects.</returns>
        public async Task<SolutionInfo> AnalyzeSolutionAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Starting solution analysis");

                var info = new SolutionInfo
                {
                    Name = _dte.Solution.FileName,
                    SolutionPath = _dte.Solution.FullName,
                    Projects = new List<ProjectInfo>()
                };

                // Analyze each project in the solution
                foreach (Project project in _dte.Solution.Projects)
                {
                    try
                    {
                        var projectInfo = await AnalyzeProjectAsync(project);
                        info.Projects.Add(projectInfo);
                    }
                    catch (Exception projectEx)
                    {
                        _logger.LogWarning($"Failed to analyze project {project.Name}: {projectEx.Message}");
                    }
                }

                _logger.LogInformation($"Solution analysis complete. Found {info.Projects.Count} projects.");
                return info;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to analyze solution", ex);
                throw;
            }
        }

        /// <summary>
        /// Analyzes a specific project within the solution.
        /// </summary>
        /// <param name="project">The project to analyze.</param>
        /// <returns>Detailed information about the project.</returns>
        private async Task<ProjectInfo> AnalyzeProjectAsync(Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var projectInfo = new ProjectInfo
            {
                Name = project.Name,
                Type = GetProjectType(project),
                ProjectPath = project.FullName,
                Files = new List<string>(),
                ProjectProperties = new Dictionary<string, string>()
            };

            try
            {
                // Get project files
                projectInfo.Files = await GetProjectFilesAsync(project);

                // Get project properties
                projectInfo.ProjectProperties = GetProjectProperties(project);

                return projectInfo;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Partial project analysis failed for {project.Name}: {ex.Message}");
                return projectInfo;
            }
        }

        /// <summary>
        /// Retrieves all files associated with a project, including nested files.
        /// </summary>
        /// <param name="project">The project to analyze.</param>
        /// <returns>A list of file paths in the project.</returns>
        private async Task<List<string>> GetProjectFilesAsync(Project project)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var files = new List<string>();

            void TraverseProjectItems(ProjectItems items)
            {
                foreach (ProjectItem item in items)
                {
                    try
                    {
                        // Handle files
                        if (item.FileCount > 0)
                        {
                            for (short i = 1; i <= item.FileCount; i++)
                            {
                                string fileName = item.FileNames[i];
                                if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
                                {
                                    files.Add(fileName);
                                }
                            }
                        }

                        // Recursively handle nested project items
                        if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                        {
                            TraverseProjectItems(item.ProjectItems);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error traversing project item: {ex.Message}");
                    }
                }
            }

            TraverseProjectItems(project.ProjectItems);
            return files;
        }

        /// <summary>
        /// Determines the type of project.
        /// </summary>
        /// <param name="project">The project to analyze.</param>
        /// <returns>A string representing the project type.</returns>
        private string GetProjectType(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                return project.Kind switch
                {
                    "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}" => "C# Project",
                    "{F2A71F9B-5D33-465A-A702-920D77279786}" => "F# Project",
                    "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}" => ".NET Core Project",
                    "{778DAE3C-4631-46EA-AA77-4E1C43FC5A15}" => "Windows Phone Project",
                    "{EFBA0AD7-5A72-4C68-AF49-83D6D673AD48}" => "Xamarin.Android Project",
                    "{6BC8ED88-2882-458C-8E55-DFD12B67127B}" => "Xamarin.iOS Project",
                    _ => "Unknown Project Type"
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not determine project type: {ex.Message}");
                return "Unidentified Project";
            }
        }

        /// <summary>
        /// Retrieves additional project properties.
        /// </summary>
        /// <param name="project">The project to analyze.</param>
        /// <returns>A dictionary of project properties.</returns>
        private Dictionary<string, string> GetProjectProperties(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var properties = new Dictionary<string, string>();

            try
            {
                // Get common project properties
                properties["TargetFramework"] = GetPropertyValue(project, "TargetFramework");
                properties["OutputType"] = GetPropertyValue(project, "OutputType");
                properties["RootNamespace"] = GetPropertyValue(project, "RootNamespace");
                properties["ProjectGuid"] = GetPropertyValue(project, "ProjectGuid");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error retrieving project properties: {ex.Message}");
            }

            return properties;
        }

        /// <summary>
        /// Safely retrieves a project property value.
        /// </summary>
        /// <param name="project">The project to analyze.</param>
        /// <param name="propertyName">The name of the property to retrieve.</param>
        /// <returns>The property value or "N/A" if not found.</returns>
        private string GetPropertyValue(Project project, string propertyName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var property = project.Properties.Item(propertyName);
                return property?.Value?.ToString() ?? "N/A";
            }
            catch
            {
                return "N/A";
            }
        }
    }
}