namespace VSPilot.Common.Models
{
    public class ErrorItem
    {
        public string Description { get; }
        public string FileName { get; }
        public int Line { get; }
        public int Column { get; }

        public ErrorItem(string description, string fileName, int line, int column)
        {
            Description = description;
            FileName = fileName;
            Line = line;
            Column = column;
        }
    }
}