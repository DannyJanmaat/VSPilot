using System.Collections.Generic;

namespace VSPilot.Common.Models
{
    public class ProjectChanges
    {
        public List<FileCreationInfo> NewFiles { get; set; } = new();
        public List<FileModificationInfo> ModifiedFiles { get; set; } = new();
        public List<string> RequiredReferences { get; set; } = new();
    }
}
