using System.Collections.Generic;
using System.Threading.Tasks;
using VSPilot.Common.Models;

namespace VSPilot.Common.Interfaces
{
    /// <summary>
    /// Defines operations for handling build and compilation errors.
    /// </summary>
    public interface IErrorHandler
    {
        /// <summary>
        /// Retrieves all current build errors from the error list.
        /// </summary>
        /// <returns>A collection of error items found in the current build.</returns>
        Task<IEnumerable<VSPilotErrorItem>> GetBuildErrorsAsync();

        /// <summary>
        /// Attempts to fix the specified build errors using AI assistance.
        /// </summary>
        /// <param name="errors">The collection of errors to fix.</param>
        /// <returns>A task representing the error fixing operation.</returns>
        Task FixErrorsAsync(IEnumerable<VSPilotErrorItem> errors);

        /// <summary>
        /// Gets the error severity level for a specific error.
        /// </summary>
        /// <param name="error">The error to check.</param>
        /// <returns>True if the error is a critical error, false if it's a warning or info.</returns>
        Task<bool> IsErrorCriticalAsync(VSPilotErrorItem error);

        /// <summary>
        /// Gets detailed error analysis including potential causes and fix suggestions.
        /// </summary>
        /// <param name="error">The error to analyze.</param>
        /// <returns>Detailed analysis of the error.</returns>
        Task<ErrorAnalysis> GetErrorAnalysisAsync(VSPilotErrorItem error);
    }
}