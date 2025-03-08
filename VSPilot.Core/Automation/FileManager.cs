using Microsoft.VisualStudio.Shell;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using EnvDTE80;
using VSPilot.Common.Exceptions;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using EnvDTE;

namespace VSPilot.Core.Automation
{
    /// <summary>
    /// Manages file operations within Visual Studio projects.
    /// </summary>
    public class FileManager : IDisposable
    {
        private readonly DTE2 _dte;
        private readonly ILogger<FileManager> _logger;
        private bool _disposed;
        private const string BackupExtension = ".bak";
        private readonly Encoding _defaultEncoding = new UTF8Encoding(false); // UTF-8 without BOM

        /// <summary>
        /// Initializes a new instance of the FileManager class.
        /// </summary>
        /// <param name="dte">The Visual Studio DTE2 automation object.</param>
        /// <param name="logger">The logger for recording file operations.</param>
        public FileManager(DTE2 dte, ILogger<FileManager> logger)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }



        /// <summary>
        /// Creates a new file with the specified content.
        /// </summary>
        /// <param name="path">The path to the file, relative to the project root.</param>
        /// <param name="content">The content to write to the file.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CreateFileAsync(string path, string content)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Creating file: {Path}", path);

                string fullPath = GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);

                // Ensure directory exists
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogDebug("Created directory: {Directory}", directory);
                }

                // Write file content with proper encoding
                File.WriteAllText(fullPath, content, _defaultEncoding);

                // Add to project
                _dte.ItemOperations.AddExistingItem(fullPath);

                _logger.LogInformation("File created and added to project: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create file: {Path}", path);
                throw new AutomationException($"Failed to create file: {path}", ex);
            }
        }

        /// <summary>
        /// Modifies an existing file with the specified content.
        /// </summary>
        /// <param name="path">The path to the file, relative to the project root.</param>
        /// <param name="content">The new content for the file.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task ModifyFileAsync(string path, string content)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Modifying file: {Path}", path);

                string fullPath = GetFullPath(path);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"File not found: {path}");
                }

                // Create backup before making changes
                await CreateBackupAsync(path);

                // Write file content with proper encoding
                File.WriteAllText(fullPath, content, _defaultEncoding);
                _logger.LogInformation("File modified: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to modify file: {Path}", path);
                throw new AutomationException($"Failed to modify file: {path}", ex);
            }
        }

        /// <summary>
        /// Creates a new folder in the project.
        /// </summary>
        /// <param name="path">The path to the folder, relative to the project root.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CreateFolderAsync(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Creating folder: {Path}", path);

                string fullPath = GetFullPath(path);
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                    _logger.LogInformation("Folder created: {Path}", path);
                }
                else
                {
                    _logger.LogDebug("Folder already exists: {Path}", path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create folder: {Path}", path);
                throw new AutomationException($"Failed to create folder: {path}", ex);
            }
        }

        /// <summary>
        /// Creates a backup of the specified file.
        /// </summary>
        /// <param name="path">The path to the file to backup, relative to the project root.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task CreateBackupAsync(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                string fullPath = GetFullPath(path);

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"File not found: {path}");
                }

                string backupPath = $"{fullPath}{BackupExtension}";
                File.Copy(fullPath, backupPath, true);
                _logger.LogInformation("Backup created for file: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create backup: {Path}", path);
                throw new AutomationException($"Failed to create backup: {path}", ex);
            }
        }

        /// <summary>
        /// Restores a file from its backup.
        /// </summary>
        /// <param name="path">The path to the file to restore, relative to the project root.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task RestoreBackupAsync(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                string fullPath = GetFullPath(path);
                string backupPath = $"{fullPath}{BackupExtension}";

                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException($"Backup not found for: {path}");
                }

                File.Copy(backupPath, fullPath, true);
                File.Delete(backupPath);
                _logger.LogInformation("Backup restored for file: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restore backup: {Path}", path);
                throw new AutomationException($"Failed to restore backup: {path}", ex);
            }
        }

        /// <summary>
        /// Checks if a file exists in the project.
        /// </summary>
        /// <param name="path">The path to check, relative to the project root.</param>
        /// <returns>True if the file exists, false otherwise.</returns>
        public async Task<bool> FileExistsAsync(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                string fullPath = GetFullPath(path);
                return File.Exists(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check file existence: {Path}", path);
                throw new AutomationException($"Failed to check file existence: {path}", ex);
            }
        }

        /// <summary>
        /// Reads the content of a file.
        /// </summary>
        /// <param name="path">The path to the file, relative to the project root.</param>
        /// <returns>The content of the file.</returns>
        public async Task<string> ReadFileAsync(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                string fullPath = GetFullPath(path);

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"File not found: {path}");
                }

                return File.ReadAllText(fullPath, _defaultEncoding);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read file: {Path}", path);
                throw new AutomationException($"Failed to read file: {path}", ex);
            }
        }

        /// <summary>
        /// Deletes a file from the project.
        /// </summary>
        /// <param name="path">The path to the file, relative to the project root.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task DeleteFileAsync(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Deleting file: {Path}", path);

                string fullPath = GetFullPath(path);
                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("File does not exist: {Path}", path);
                    return;
                }

                // Create backup before deletion (just in case)
                await CreateBackupAsync(path);

                // Remove from project if it's part of the project
                await RemoveFromProjectAsync(path);

                // Delete the file
                File.Delete(fullPath);
                _logger.LogInformation("File deleted: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file: {Path}", path);
                throw new AutomationException($"Failed to delete file: {path}", ex);
            }
        }

        /// <summary>
        /// Removes a file from the project without deleting it from disk.
        /// </summary>
        /// <param name="path">The path to the file, relative to the project root.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task RemoveFromProjectAsync(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                string fullPath = GetFullPath(path);

                // Find the project item and remove it
                foreach (EnvDTE.Project project in _dte.Solution.Projects)
                {
                    var projectItem = FindProjectItem(project.ProjectItems, fullPath);
                    if (projectItem != null)
                    {
                        projectItem.Remove();
                        _logger.LogDebug("Removed file from project: {Path}", path);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove file from project: {Path}", path);
                // Continue with the operation even if removing from project fails
            }
        }

        /// <summary>
        /// Recursively searches for a project item with the specified path.
        /// </summary>
        /// <param name="items">The project items to search.</param>
        /// <param name="fullPath">The full path of the file to find.</param>
        /// <returns>The project item if found, null otherwise.</returns>
        private EnvDTE.ProjectItem FindProjectItem(EnvDTE.ProjectItems items, string fullPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread(); // Add this line at the start of the method

            if (items == null)
                return null;

            foreach (EnvDTE.ProjectItem item in items)
            {
                try
                {
                    // No need to add more ThreadHelper calls for item access
                    // since we've already verified we're on the UI thread
                    if (item.FileCount > 0)
                    {
                        for (short i = 1; i <= item.FileCount; i++)
                        {
                            if (string.Equals(item.FileNames[i], fullPath, StringComparison.OrdinalIgnoreCase))
                            {
                                return item;
                            }
                        }
                    }

                    // Recursively search in nested items
                    var nestedItem = FindProjectItem(item.ProjectItems, fullPath);
                    if (nestedItem != null)
                    {
                        return nestedItem;
                    }
                }
                catch (Exception)
                {
                    // Skip items that cause exceptions
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads a file as binary data.
        /// </summary>
        /// <param name="path">The path to the file, relative to the project root.</param>
        /// <returns>The binary content of the file.</returns>
        public async Task<byte[]> ReadFileBinaryAsync(string path)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                string fullPath = GetFullPath(path);

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"File not found: {path}");
                }

                return File.ReadAllBytes(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read binary file: {Path}", path);
                throw new AutomationException($"Failed to read binary file: {path}", ex);
            }
        }

        /// <summary>
        /// Writes binary data to a file.
        /// </summary>
        /// <param name="path">The path to the file, relative to the project root.</param>
        /// <param name="data">The binary data to write.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task WriteFileBinaryAsync(string path, byte[] data)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _logger.LogInformation("Writing binary file: {Path}", path);

                string fullPath = GetFullPath(path);
                string directory = Path.GetDirectoryName(fullPath);

                // Ensure directory exists
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogDebug("Created directory: {Directory}", directory);
                }

                // Create backup if the file already exists
                if (File.Exists(fullPath))
                {
                    await CreateBackupAsync(path);
                }

                // Write binary data
                File.WriteAllBytes(fullPath, data);

                // Add to project if not already in project
                _dte.ItemOperations.AddExistingItem(fullPath);

                _logger.LogInformation("Binary file written: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write binary file: {Path}", path);
                throw new AutomationException($"Failed to write binary file: {path}", ex);
            }
        }

        /// <summary>
        /// Gets the list of all files in a specific folder.
        /// </summary>
        /// <param name="folderPath">The folder path, relative to the project root.</param>
        /// <param name="searchPattern">Optional search pattern (default: "*.*").</param>
        /// <param name="recursive">Whether to search recursively in subfolders (default: false).</param>
        /// <returns>A list of file paths relative to the project root.</returns>
        public async Task<List<string>> GetFilesInFolderAsync(string folderPath, string searchPattern = "*.*", bool recursive = false)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                string fullFolderPath = GetFullPath(folderPath);

                if (!Directory.Exists(fullFolderPath))
                {
                    throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
                }

                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(fullFolderPath, searchPattern, searchOption);

                // Convert to project-relative paths
                var projectRoot = GetProjectRootPath();
                return files
                    .Select(f => f.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                        ? f.Substring(projectRoot.Length).TrimStart('\\', '/')
                        : f)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get files in folder: {FolderPath}", folderPath);
                throw new AutomationException($"Failed to get files in folder: {folderPath}", ex);
            }
        }

        /// <summary>
        /// Gets the full path of a file or folder relative to the project root.
        /// </summary>
        /// <param name="relativePath">The path relative to the project root.</param>
        /// <returns>The full path.</returns>
        private string GetFullPath(string relativePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Try to find the active project first
                EnvDTE.Project activeProject = null;

                if (_dte.ActiveSolutionProjects is Array activeSolutionProjects &&
                    activeSolutionProjects.Length > 0)
                {
                    activeProject = activeSolutionProjects.GetValue(0) as EnvDTE.Project;
                }

                if (activeProject == null)
                {
                    throw new AutomationException("No project found in the solution.");
                }

                string projectPath = Path.GetDirectoryName(activeProject.FullName);
                return Path.Combine(projectPath, relativePath);
            }
            catch (Exception ex)
            {
                throw new AutomationException("Failed to get project path", ex);
            }
        }

        /// <summary>
        /// Gets the root path of the active project.
        /// </summary>
        /// <returns>The project root path.</returns>
        private string GetProjectRootPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Try to find the active project first
                EnvDTE.Project? activeProject = null;

                if (_dte.ActiveSolutionProjects is Array activeSolutionProjects &&
                    activeSolutionProjects.Length > 0)
                {
                    activeProject = activeSolutionProjects.GetValue(0) as EnvDTE.Project;
                }

                if (activeProject == null)
                {
                    throw new AutomationException("No project found in the solution.");
                }

                string? projectPath = Path.GetDirectoryName(activeProject.FullName);
                if (projectPath == null)
                {
                    throw new AutomationException("Failed to get project root path");
                }
                return projectPath;
            }
            catch (Exception ex)
            {
                throw new AutomationException("Failed to get project root path", ex);
            }
        }

        /// <summary>
        /// Disposes resources used by the FileManager.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources used by the FileManager.
        /// </summary>
        /// <param name="disposing">Whether to dispose managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Clean up managed resources if needed
                }
                _disposed = true;
            }
        }
    }
}