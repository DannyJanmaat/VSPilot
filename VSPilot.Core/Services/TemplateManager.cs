using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace VSPilot.Core.Services
{
    /// <summary>
    /// Manages code templates for various programming constructs.
    /// </summary>
    public class TemplateManager
    {
        /// <summary>
        /// Represents a code template with additional metadata.
        /// </summary>
        public class CodeTemplate
        {
            /// <summary>
            /// Unique identifier for the template.
            /// </summary>
            public string Id { get; set; } = string.Empty;

            /// <summary>
            /// Name of the template.
            /// </summary>
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// Content of the template.
            /// </summary>
            public string Content { get; set; } = string.Empty;

            /// <summary>
            /// Programming language the template is for.
            /// </summary>
            public string Language { get; set; } = string.Empty;

            /// <summary>
            /// Type of code construct (Class, Interface, Method, etc.).
            /// </summary>
            public string Type { get; set; } = string.Empty;

            /// <summary>
            /// Author or source of the template.
            /// </summary>
            public string Author { get; set; } = string.Empty;

            /// <summary>
            /// Description of the template.
            /// </summary>
            public string Description { get; set; } = string.Empty;

            /// <summary>
            /// Tags to help categorize and search templates.
            /// </summary>
            public List<string> Tags { get; set; } = new List<string>();
        }


        private readonly string _templatePath;
        private readonly ILogger<TemplateManager> _logger;
        private readonly ConcurrentDictionary<string, CodeTemplate> _templates;
        private const string TemplateFileExtension = ".template.json";

        /// <summary>
        /// Initializes a new instance of the TemplateManager.
        /// </summary>
        /// <param name="logger">Logger for recording template-related events.</param>
        public TemplateManager(ILogger<TemplateManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _templates = new ConcurrentDictionary<string, CodeTemplate>();

            // Determine template directory
            _templatePath = Path.Combine(
                Path.GetDirectoryName(GetType().Assembly.Location),
                "Templates"
            );

            // Ensure template directory exists
            EnsureTemplateDirectoryExists();

            // Load initial templates
            LoadTemplates();
        }

        /// <summary>
        /// Ensures the template directory exists, creating it if necessary.
        /// </summary>
        private void EnsureTemplateDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(_templatePath))
                {
                    Directory.CreateDirectory(_templatePath);
                    _logger.LogInformation($"Created template directory: {_templatePath}");
                    CreateDefaultTemplates();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create template directory");
                throw;
            }
        }

        /// <summary>
        /// Creates default templates if none exist.
        /// </summary>
        private void CreateDefaultTemplates()
        {
            var defaultTemplates = new[]
            {
                new CodeTemplate
                {
                    Id = "csharp-class",
                    Name = "C# Class Template",
                    Language = "C#",
                    Type = "Class",
                    Content = @"using System;

namespace {0}
{
    public class {1}
    {
        /// <summary>
        /// Creates a new instance of {1}.
        /// </summary>
        public {1}()
        {
        }

        {2}
    }
}",
                    Description = "Standard C# class template",
                    Tags = new List<string> { "class", "basic", "csharp" }
                },
                new CodeTemplate
                {
                    Id = "csharp-interface",
                    Name = "C# Interface Template",
                    Language = "C#",
                    Type = "Interface",
                    Content = @"using System;

namespace {0}
{
    /// <summary>
    /// {2}
    /// </summary>
    public interface {1}
    {
        {3}
    }
}",
                    Description = "Standard C# interface template",
                    Tags = new List<string> { "interface", "basic", "csharp" }
                }
            };

            // Save each default template
            foreach (var template in defaultTemplates)
            {
                SaveTemplate(template);
            }
        }

        /// <summary>
        /// Loads all templates from the template directory.
        /// </summary>
        private void LoadTemplates()
        {
            try
            {
                var templateFiles = Directory.GetFiles(_templatePath, $"*{TemplateFileExtension}");

                foreach (var file in templateFiles)
                {
                    try
                    {
                        var templateJson = File.ReadAllText(file);
                        var template = System.Text.Json.JsonSerializer.Deserialize<CodeTemplate>(templateJson);

                        if (template != null)
                        {
                            _templates[template.Id] = template;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to load template from {file}");
                    }
                }

                _logger.LogInformation($"Loaded {_templates.Count} templates");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load templates");
            }
        }

        /// <summary>
        /// Retrieves a template by its identifier.
        /// </summary>
        /// <param name="templateId">Unique identifier of the template.</param>
        /// <returns>The requested template or null if not found.</returns>
        public CodeTemplate GetTemplate(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                throw new ArgumentException("Template ID cannot be empty", nameof(templateId));
            }

            _templates.TryGetValue(templateId, out var template);
            return template;
        }

        /// <summary>
        /// Finds templates matching specified criteria.
        /// </summary>
        /// <param name="language">Programming language to filter by.</param>
        /// <param name="type">Type of code construct.</param>
        /// <param name="tags">Tags to match.</param>
        /// <returns>Matching templates.</returns>
        public IEnumerable<CodeTemplate> FindTemplates(
            string? language = null,
            string? type = null,
            params string[] tags)
        {
            return _templates.Values
                .Where(t =>
                    (string.IsNullOrEmpty(language) || t.Language == language) &&
                    (string.IsNullOrEmpty(type) || t.Type == type) &&
                    (tags == null || tags.Length == 0 ||
                     tags.All(tag => t.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                )
                .ToList();
        }

        /// <summary>
        /// Saves a new or updates an existing template.
        /// </summary>
        /// <param name="template">The template to save.</param>
        public void SaveTemplate(CodeTemplate template)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            if (string.IsNullOrWhiteSpace(template.Id))
            {
                template.Id = GenerateTemplateId();
            }

            // Serialize and save template
            var templateJson = System.Text.Json.JsonSerializer.Serialize(template,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            var filePath = Path.Combine(_templatePath, $"{template.Id}{TemplateFileExtension}");

            try
            {
                File.WriteAllText(filePath, templateJson);
                _templates[template.Id] = template;
                _logger.LogInformation($"Saved template: {template.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to save template: {template.Id}");
                throw;
            }
        }

        /// <summary>
        /// Generates a unique template identifier.
        /// </summary>
        /// <returns>A unique template ID.</returns>
        private string GenerateTemplateId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        /// <summary>
        /// Formats a template with provided parameters.
        /// </summary>
        /// <param name="templateId">ID of the template to format.</param>
        /// <param name="parameters">Parameters to inject into the template.</param>
        /// <returns>Formatted template content.</returns>
        public string FormatTemplate(string templateId, params string[] parameters)
        {
            var template = GetTemplate(templateId);
            if (template == null)
            {
                throw new InvalidOperationException($"Template not found: {templateId}");
            }

            try
            {
                return string.Format(template.Content, parameters);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to format template: {templateId}");
                throw new InvalidOperationException($"Failed to format template {templateId}", ex);
            }
        }

        /// <summary>
        /// Deletes a template by its identifier.
        /// </summary>
        /// <param name="templateId">ID of the template to delete.</param>
        public void DeleteTemplate(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                throw new ArgumentException("Template ID cannot be empty", nameof(templateId));
            }

            try
            {
                // Remove from in-memory dictionary
                _templates.TryRemove(templateId, out _);

                // Delete the template file
                var filePath = Path.Combine(_templatePath, $"{templateId}{TemplateFileExtension}");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogInformation($"Deleted template: {templateId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to delete template: {templateId}");
                throw;
            }
        }
    }
}