using System.Collections.Generic;

namespace VSPilot.Common.Models
{
    public class ProjectChanges
    {
        public List<FileCreationInfo> NewFiles { get; set; }
        public List<FileModificationInfo> ModifiedFiles { get; set; }
        public List<string> RequiredReferences { get; set; }
    }
}
