using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VSPilot.Common.Models;
using VSPilot.Common.Exceptions;
using System.Linq;

namespace VSPilot.Core.AI
{
    public class LanguageProcessor
    {
        private readonly ILogger<LanguageProcessor> _logger;

        public LanguageProcessor(ILogger<LanguageProcessor> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> ProcessRequestAsync(string userRequest)
        {
            if (string.IsNullOrWhiteSpace(userRequest))
            {
                throw new ArgumentException("Request cannot be empty", nameof(userRequest));
            }

            try
            {
                _logger.LogInformation("Processing request: {Request}", userRequest);

                // Analyze the request type
                var requestType = AnalyzeRequestType(userRequest);
                _logger.LogDebug("Request type identified: {RequestType}", requestType);

                // Clean and enhance the request
                var enhancedRequest = await EnhanceRequestAsync(userRequest, requestType);
                _logger.LogDebug("Request enhanced: {EnhancedRequest}", enhancedRequest);

                return enhancedRequest;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process request: {Request}", userRequest);
                throw new AutomationException($"Failed to process request: {ex.Message}", ex);
            }
        }

        private RequestType AnalyzeRequestType(string request)
        {
            request = request.ToLowerInvariant();

            // Creation patterns
            if (ContainsAny(request, "create", "new", "add", "generate"))
                return RequestType.Creation;

            // Modification patterns
            if (ContainsAny(request, "modify", "change", "update", "refactor", "rename", "move"))
                return RequestType.Modification;

            // Deletion patterns
            if (ContainsAny(request, "delete", "remove", "clean"))
                return RequestType.Deletion;

            // Analysis patterns
            if (ContainsAny(request, "analyze", "check", "review", "find", "search"))
                return RequestType.Analysis;

            // Testing patterns
            if (ContainsAny(request, "test", "unit test", "integration test"))
                return RequestType.Testing;

            return RequestType.Unknown;
        }

        private async Task<string> EnhanceRequestAsync(string request, RequestType requestType)
        {
            // Build enhanced prompt based on request type
            var enhancedRequest = requestType switch
            {
                RequestType.Creation => await EnhanceCreationRequestAsync(request),
                RequestType.Modification => await EnhanceModificationRequestAsync(request),
                RequestType.Deletion => await EnhanceDeletionRequestAsync(request),
                RequestType.Analysis => await EnhanceAnalysisRequestAsync(request),
                RequestType.Testing => await EnhanceTestingRequestAsync(request),
                _ => await EnhanceGenericRequestAsync(request)
            };

            return enhancedRequest;
        }

        private async Task<string> EnhanceCreationRequestAsync(string request)
        {
            return await Task.FromResult($@"Create new code elements with the following specifications:

Request: {request}

Please consider:
- Appropriate namespaces and class structure
- Required interfaces and base classes
- Unit test coverage requirements
- Documentation requirements
- Best practices and design patterns
- Error handling and logging
- Any necessary configurations");
        }

        private async Task<string> EnhanceModificationRequestAsync(string request)
        {
            return await Task.FromResult($@"Modify existing code with the following changes:

Request: {request}

Please consider:
- Impact on existing functionality
- Backward compatibility
- Required refactoring
- Test updates needed
- Documentation updates
- Error handling implications");
        }

        private async Task<string> EnhanceDeletionRequestAsync(string request)
        {
            return await Task.FromResult($@"Remove code elements with the following considerations:

Request: {request}

Please analyze:
- Dependencies and usages
- Impact on other components
- Required cleanup tasks
- Test modifications needed
- Documentation updates");
        }

        private async Task<string> EnhanceAnalysisRequestAsync(string request)
        {
            return await Task.FromResult($@"Analyze code with the following focus:

Request: {request}

Please provide:
- Code quality assessment
- Potential improvements
- Performance considerations
- Security implications
- Best practice adherence");
        }

        private async Task<string> EnhanceTestingRequestAsync(string request)
        {
            return await Task.FromResult($@"Create or modify tests with the following scope:

Request: {request}

Please consider:
- Test coverage requirements
- Edge cases and error conditions
- Mocking and dependency handling
- Test data requirements
- Integration test considerations");
        }

        private async Task<string> EnhanceGenericRequestAsync(string request)
        {
            return await Task.FromResult($@"Process the following development request:

Request: {request}

Please provide:
- Clear action items
- Required changes
- Impact assessment
- Implementation considerations");
        }

        private bool ContainsAny(string text, params string[] patterns)
        {
            return patterns.Any(pattern =>
                Regex.IsMatch(text, $@"\b{Regex.Escape(pattern)}\b", RegexOptions.IgnoreCase));
        }

        private enum RequestType
        {
            Creation,
            Modification,
            Deletion,
            Analysis,
            Testing,
            Unknown
        }
    }
}