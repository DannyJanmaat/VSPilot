using System.Collections.Generic;

namespace VSPilot.Common.Models
{
    public class ProjectChangeRequest
    {
        public List<FileCreationInfo> FilesToCreate { get; set; } = new();
        public List<FileModificationInfo> FilesToModify { get; set; } = new();
        public List<string> References { get; set; } = new();
        public string ProjectType { get; set; } = string.Empty;
        public bool RequiresBuild { get; set; } = true;
        public bool RequiresTests { get; set; } = false;
        public string SolutionPath { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public Dictionary<string, string> AdditionalSettings { get; set; } = new Dictionary<string, string>();
        public List<string> Dependencies { get; set; } = new List<string>();
    }

    public class FileCreationInfo
    {
        public string Path { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public bool Overwrite { get; set; }
        public Dictionary<string, string> TemplateParameters { get; set; } = new();
    }

    public class FileModificationInfo
    {
        public string Path { get; set; } = string.Empty;
        public List<CodeChange> Changes { get; set; } = new();
        public bool CreateBackup { get; set; } = true;
        public bool IgnoreWhitespace { get; set; } = true;
    }

    public class CodeChange
    {
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string NewContent { get; set; } = string.Empty;
        public string SearchPattern { get; set; } = string.Empty;
        public bool IsRegex { get; set; }
    }
}
