using System;
using System.Threading.Tasks;
using Moq;

namespace VSPilot.Core.Build
{
    public class MockTestPlatform : ITestPlatform
    {
        public Task<ITestOperation> CreateTestOperationAsync()
        {
            var mockOperation = new Mock<ITestOperation>();

            // Set up basic behavior
            mockOperation.Setup(o => o.RunAsync()).Returns(Task.CompletedTask);

            // Create a mock implementation that handles the Context property
            var mockObj = mockOperation.Object;
            mockObj.Context = new object(); // Set a default value

            // Return the mock
            return Task.FromResult(mockObj);
        }
    }
}
