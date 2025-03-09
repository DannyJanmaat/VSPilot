using Microsoft.VisualStudio.Shell;
using EnvDTE;
using EnvDTE80;
using System;
using System.Threading.Tasks;
using VSPilot.Common.Models;
using VSPilot.Core.Services;

namespace VSPilot.Core.Automation
{
    public class SolutionManager
    {
        private readonly DTE2 _dte;
        private readonly LoggingService _logger;
        private readonly SolutionAnalyzer _analyzer;

        public SolutionManager(DTE2 dte, LoggingService logger)
        {
            _dte = dte;
            _logger = logger;
            _analyzer = new SolutionAnalyzer(dte, logger);
        }

        public async Task<bool> CreateSolutionAsync(string solutionPath, string solutionName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                _dte.Solution.Create(solutionPath, solutionName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to create solution: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddProjectAsync(string projectName, string projectTemplate, string projectPath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var solution = (Solution2)_dte.Solution;
                solution.AddFromTemplate(projectTemplate, projectPath, projectName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to add project: {ex.Message}");
                return false;
            }
        }

        public async Task<SolutionInfo> GetSolutionInfoAsync()
        {
            return await _analyzer.AnalyzeSolutionAsync();
        }

        public async Task<bool> SaveSolutionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                _dte.Solution.SaveAs(_dte.Solution.FullName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to save solution: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> CloseSolutionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                _dte.Solution.Close();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to close solution: {ex.Message}");
                return false;
            }
        }
    }
}