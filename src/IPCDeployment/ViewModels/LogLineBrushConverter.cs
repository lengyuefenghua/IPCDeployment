using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IPCDeployment.ViewModels
{
    /// <summary>
    /// 日志行按级别着色：WARN 橙色、ERROR 红色，其余保持默认前景色。
    /// 日志文本本身已带 “[WARN]”/“[ERROR]” 级别标记，不需要额外结构。
    /// </summary>
    public sealed class LogLineBrushConverter : IValueConverter
    {
        private static readonly Brush WarningBrush = CreateBrush("#B45309");
        private static readonly Brush ErrorBrush = CreateBrush("#DC2626");
        private static readonly Brush DefaultBrush = CreateBrush("#354258");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var line = value as string ?? string.Empty;
            if (line.IndexOf("[ERROR]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ErrorBrush;
            }
            if (line.IndexOf("[WARN]", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return WarningBrush;
            }
            return DefaultBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static Brush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}
