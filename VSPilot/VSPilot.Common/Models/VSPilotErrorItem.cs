namespace VSPilot.Common.Models
{
    public class VSPilotErrorItem
    {
        public string Description { get; }
        public string FileName { get; }
        public int Line { get; }
        public int Column { get; }

        public VSPilotErrorItem(string description, string fileName, int line, int column)
        {
            Description = description;
            FileName = fileName;
            Line = line;
            Column = column;
        }
    }
}