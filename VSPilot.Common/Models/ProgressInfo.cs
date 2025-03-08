namespace VSPilot.Common.Models
{
    public class ProgressInfo
    {
        public string Stage { get; set; } = string.Empty;
        public int Progress { get; set; }
        public string Detail { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public static ProgressInfo CreateError(string message)
        {
            return new ProgressInfo
            {
                HasError = true,
                ErrorMessage = message,
                IsComplete = true
            };
        }
    }
}