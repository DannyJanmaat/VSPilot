using System.Threading.Tasks;

namespace VSPilot.Core.Abstractions
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
    }
}