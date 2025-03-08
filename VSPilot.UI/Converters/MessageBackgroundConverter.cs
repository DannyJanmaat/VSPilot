using System;
using System.Windows.Data;
using System.Windows.Media;
using System.Globalization;

namespace VSPilot.UI.Converters
{
    [ValueConversion(typeof(bool), typeof(Brush))]
    public class MessageBackgroundConverter : IValueConverter
    {
        // Static brushes for performance optimization
        private static readonly SolidColorBrush UserMessageBrush =
            new SolidColorBrush(Color.FromRgb(230, 240, 250)); // Light blue for user messages

        private static readonly SolidColorBrush AiMessageBrush =
            new SolidColorBrush(Colors.White); // White for AI messages

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Defensive programming: handle different input scenarios
            if (value is bool isUser)
            {
                return isUser ? UserMessageBrush : AiMessageBrush;
            }

            // Fallback to default AI message brush if input is unexpected
            return AiMessageBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Since converting back doesn't make sense for this converter
            throw new NotSupportedException("Converting back is not supported for MessageBackgroundConverter.");
        }
    }
}