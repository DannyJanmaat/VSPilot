using Microsoft.VisualStudio.Shell;
using EnvDTE;
using EnvDTE80;
using System;
using System.Linq;
using VSPilot.Common.Models;
using System.Collections.Generic;
using VSPilot.Common.Interfaces;
using VSPilot.Core.Automation;
using System.IO;

namespace VSPilot.Core.Adapters
{
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
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                return FindProject(projectName) != null;
            });
        }

        public void AddFolder(string projectName, string folderName)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var project = FindProject(projectName);

                if (project == null)
                {
                    throw new InvalidOperationException($"Project not found: {projectName}");
                }

                project.ProjectItems.AddFolder(folderName);
            });
        }

        public void AddReference(string projectName, string referenceName)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var project = FindProject(projectName);

                if (project == null)
                {
                    throw new InvalidOperationException($"Project not found: {projectName}");
                }

                var vsProject = project.Object as VSLangProj.VSProject;
                vsProject?.References.Add(referenceName);
            });
        }

        public ProjectConfiguration GetProjectConfiguration(string projectName)
        {
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var project = FindProject(projectName);

                if (project == null)
                {
                    throw new InvalidOperationException($"Project not found: {projectName}");
                }

                return new ProjectConfiguration
                {
                    OutputType = GetProjectProperty(project, "OutputType"),
                    TargetFramework = GetProjectProperty(project, "TargetFramework"),
                    Platform = GetProjectPlatform(project)
                };
            });
        }

        public void UpdateProjectConfiguration(string projectName, ProjectConfiguration configuration)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var project = FindProject(projectName);

                if (project == null)
                {
                    throw new InvalidOperationException($"Project not found: {projectName}");
                }

                SetProjectProperty(project, "OutputType", configuration.OutputType);
                SetProjectProperty(project, "TargetFramework", configuration.TargetFramework);
            });
        }

        public IEnumerable<ProjectFileInfo> GetProjectFiles(string projectName)
        {
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var project = FindProject(projectName);

                if (project == null)
                {
                    return Enumerable.Empty<ProjectFileInfo>();
                }

                return GetProjectFilesRecursive(project.ProjectItems);
            });
        }

        public IEnumerable<ProjectDependency> GetProjectDependencies(string projectName)
        {
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var project = FindProject(projectName);

                if (project == null)
                {
                    return Enumerable.Empty<ProjectDependency>();
                }

                var vsProject = project.Object as VSLangProj.VSProject;
                return vsProject?.References.Cast<VSLangProj.Reference>()
                    .Select(r => new ProjectDependency
                    {
                        Name = r.Name,
                        Version = GetReferenceVersion(r),
                        Type = GetReferenceType(r),
                        IsDirectDependency = true
                    }) ?? Enumerable.Empty<ProjectDependency>();
            });
        }

        private Project FindProject(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return string.IsNullOrEmpty(projectName)
                ? _dte.Solution.Projects.Cast<Project>().FirstOrDefault()
                : _dte.Solution.Projects.Cast<Project>()
                    .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
        }

        private string GetProjectProperty(Project project, string propertyName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return project.Properties.Item(propertyName).Value.ToString();
            }
            catch
            {
                return "Unknown";
            }
        }

        private void SetProjectProperty(Project project, string propertyName, string value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                project.Properties.Item(propertyName).Value = value;
            }
            catch
            {
                // Log or handle silently
            }
        }

        private string GetProjectPlatform(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var activeConfig = project.ConfigurationManager?.ActiveConfiguration;
                return activeConfig?.PlatformName ?? "AnyCPU";
            }
            catch
            {
                return "AnyCPU";
            }
        }

        private string GetReferenceVersion(VSLangProj.Reference reference)
        {
            try
            {
                return reference.Version;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetReferenceType(VSLangProj.Reference reference)
        {
            try
            {
                if (reference.SourceProject != null) return "Project";
                if (reference.Path?.EndsWith(".dll") == true) return "Assembly";
                return "Package";
            }
            catch
            {
                return "Unknown";
            }
        }

        private IEnumerable<ProjectFileInfo> GetProjectFilesRecursive(ProjectItems items)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var files = new List<ProjectFileInfo>();

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
                                files.Add(new ProjectFileInfo
                                {
                                    FullPath = fileName,
                                    RelativePath = GetRelativePath(fileName),
                                    BuildAction = GetBuildAction(item),
                                    CopyToOutput = GetCopyToOutput(item)
                                });
                            }
                        }
                    }

                    if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                    {
                        files.AddRange(GetProjectFilesRecursive(item.ProjectItems));
                    }
                }
                catch
                {
                    // Silently handle any errors during file traversal
                }
            }

            return files;
        }

        private string GetRelativePath(string fullPath)
        {
            return ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var firstProject = _dte.Solution.Projects.Cast<Project>().FirstOrDefault();
                if (firstProject == null) return fullPath;

                var project = FindProject(firstProject.Name);
                if (project == null) return fullPath;

                var projectPath = Path.GetDirectoryName(project.FullName);
                return fullPath.StartsWith(projectPath)
                    ? fullPath.Substring(projectPath.Length).TrimStart('\\', '/')
                    : fullPath;
            });
        }

        private string GetBuildAction(ProjectItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return item.Properties.Item("BuildAction").Value.ToString();
            }
            catch
            {
                return "None";
            }
        }

        private string GetCopyToOutput(ProjectItem item)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return item.Properties.Item("CopyToOutputDirectory").Value.ToString();
            }
            catch
            {
                return "DoNotCopy";
            }
        }
    }
}