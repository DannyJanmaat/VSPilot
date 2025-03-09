using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading.Tasks;
using System.Threading;
using VSPilot.Core.Automation;
using VSPilot.Common.Interfaces;
using Microsoft.VisualStudio.Shell;
using EnvDTE80;

namespace VSPilot.Tests.AutomationTests
{
    [TestClass]
    public class ProjectManagerTests
    {
        private Mock<IVsSolutionContext> _mockSolutionContext = null!;
        private Mock<FileManager> _mockFileManager = null!;
        private Mock<ILogger<ProjectManager>> _mockLogger = null!;
        private ProjectManager _projectManager = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockSolutionContext = new Mock<IVsSolutionContext>();
            _mockFileManager = new Mock<FileManager>(Mock.Of<DTE2>(), Mock.Of<ILogger<FileManager>>());
            _mockLogger = new Mock<ILogger<ProjectManager>>();

            _projectManager = new ProjectManager(
                _mockSolutionContext.Object,
                _mockFileManager.Object,
                _mockLogger.Object);
        }

        [TestMethod]
        public async Task ProjectExistsAsync_WhenProjectExists_ReturnsTrueAsync()
        {
            // Arrange
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _mockSolutionContext
                .Setup(x => x.ProjectExists("TestProject"))
                .Returns(true);

            // Act
            var result = await _projectManager.ProjectExistsAsync("TestProject");

            // Assert
            Assert.IsTrue(result);
            _mockSolutionContext.Verify(x => x.ProjectExists("TestProject"), Times.Once);
        }

        [TestMethod]
        public async Task CreateProjectStructureAsync_WhenCalled_CreatesAllRequiredFoldersAsync()
        {
            // Arrange
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string projectName = "TestProject";
            string projectType = "WPF";

            // Act
            await _projectManager.CreateProjectStructureAsync(projectType, projectName, CancellationToken.None);

            // Assert
            var expectedFolders = new[] { "Models", "ViewModels", "Views", "Services", "Interfaces", "Helpers", "Tests" };
            foreach (var folder in expectedFolders)
            {
                _mockSolutionContext.Verify(
                    x => x.AddFolder(projectName, folder),
                    Times.Once,
                    $"Folder {folder} was not created");
            }
        }
    }
}
