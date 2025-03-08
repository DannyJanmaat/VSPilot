using System.Collections.Generic;

namespace VSPilot.Common.Models
{
    public class SolutionInfo
    {
        public string Name { get; set; } = string.Empty;
        public List<ProjectInfo> Projects { get; set; } = new List<ProjectInfo>();
        public string SolutionPath { get; set; } = string.Empty;
        public bool HasMultipleProjects => Projects.Count > 1;
    }

    public class ProjectInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new List<string>();
        public string ProjectPath { get; set; } = string.Empty;
        public Dictionary<string, string> ProjectProperties { get; set; } = new Dictionary<string, string>();
    }
}

