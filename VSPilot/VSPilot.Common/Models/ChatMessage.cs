using System;

namespace VSPilot.Common.Models
{
    public class ChatMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public bool IsUser { get; set; }
        public DateTime Timestamp { get; set; }

        public ChatMessage()
        {
            Timestamp = DateTime.Now;
        }
    }
}