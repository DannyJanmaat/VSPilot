// File: VSPilot\VSPilot.Common\Models\ErrorAnalysis.cs
using System.Collections.Generic;

namespace VSPilot.Common.Models
{
    public class ErrorAnalysis
    {
        public string ProbableCause { get; set; } = string.Empty;
        public string SuggestedFix { get; set; } = string.Empty;
        public string AdditionalContext { get; set; } = string.Empty;
        public bool CanAutoFix { get; set; }
        public IList<string> RelatedFiles { get; set; } = new List<string>();
        public IList<string> RequiredReferences { get; set; } = new List<string>();
    }
}
