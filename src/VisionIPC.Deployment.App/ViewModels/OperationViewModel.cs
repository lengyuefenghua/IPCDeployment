using System;
using System.Windows.Media;
using VisionIPC.Deployment.Core;

namespace VisionIPC.Deployment.App.ViewModels
{
    public sealed class OperationViewModel : ObservableObject
    {
        private static readonly Brush PendingBrush = CreateBrush("#718098");
        private static readonly Brush RunningBrush = CreateBrush("#2563EB");
        private static readonly Brush SucceededBrush = CreateBrush("#15803D");
        private static readonly Brush FailedBrush = CreateBrush("#DC2626");
        private static readonly Brush AlreadySatisfiedBrush = CreateBrush("#B45309");
        private static readonly Brush CancelledBrush = CreateBrush("#6B7280");
        private static readonly Brush NotExecutedBrush = CreateBrush("#6B7280");

        private OperationStatus status;
        private string statusText = "等待";
        private string statusGlyph = "○";
        private Brush statusBrush = PendingBrush;
        private string detail = string.Empty;
        private string elapsedText = string.Empty;
        private string restartText = string.Empty;

        public OperationViewModel(DeploymentOperation operation)
        {
            Index = operation.Order + 1;
            Id = operation.Id;
            Name = operation.Name;
            TaskKey = operation.TaskKey;
            GroupKey = operation.GroupKey;
            var command = operation as CommandOperation;
            TypeText = command != null ? CommandTypeTextFor(command.Language) : TypeTextFor(operation.Type);
            Status = OperationStatus.Pending;
        }

        public int Index { get; private set; }
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string TaskKey { get; private set; }
        public string GroupKey { get; private set; }
        public string TypeText { get; private set; }

        public OperationStatus Status
        {
            get { return status; }
            private set
            {
                if (SetProperty(ref status, value))
                {
                    StatusText = TextFor(value);
                    StatusGlyph = GlyphFor(value);
                    StatusBrush = BrushFor(value);
                }
            }
        }

        public string StatusText
        {
            get { return statusText; }
            private set { SetProperty(ref statusText, value); }
        }

        public string StatusGlyph
        {
            get { return statusGlyph; }
            private set { SetProperty(ref statusGlyph, value); }
        }

        public Brush StatusBrush
        {
            get { return statusBrush; }
            private set { SetProperty(ref statusBrush, value); }
        }

        public string Detail
        {
            get { return detail; }
            private set { SetProperty(ref detail, value); }
        }

        public string ElapsedText
        {
            get { return elapsedText; }
            private set { SetProperty(ref elapsedText, value); }
        }

        public string RestartText
        {
            get { return restartText; }
            private set { SetProperty(ref restartText, value); }
        }

        public void SetStatus(OperationStatus newStatus, string newDetail, TimeSpan? elapsed, bool restartRequired)
        {
            Status = newStatus;
            Detail = newDetail ?? string.Empty;
            if (elapsed.HasValue)
            {
                ElapsedText = FormatElapsed(elapsed.Value);
            }
            RestartText = restartRequired ? "需重启" : string.Empty;
        }

        public void SetElapsed(TimeSpan elapsed)
        {
            ElapsedText = FormatElapsed(elapsed);
        }

        public void Reset()
        {
            Status = OperationStatus.Pending;
            Detail = string.Empty;
            ElapsedText = string.Empty;
            RestartText = string.Empty;
        }

        private static string CommandTypeTextFor(CommandLanguage language)
        {
            if (language == CommandLanguage.Cmd) return "CMD";
            if (language == CommandLanguage.PowerShell) return "PowerShell";
            return "EXE";
        }

        private static string TypeTextFor(string type)
        {
            if (string.Equals(type, "Install", StringComparison.OrdinalIgnoreCase)) return "安装";
            if (string.Equals(type, "Copy", StringComparison.OrdinalIgnoreCase)) return "复制";
            if (string.Equals(type, "Registry", StringComparison.OrdinalIgnoreCase)) return "注册表";
            if (string.Equals(type, "Command", StringComparison.OrdinalIgnoreCase)) return "命令";
            if (string.Equals(type, "Service", StringComparison.OrdinalIgnoreCase)) return "服务";
            if (string.Equals(type, "Network", StringComparison.OrdinalIgnoreCase)) return "网络";
            if (string.Equals(type, "Power", StringComparison.OrdinalIgnoreCase)) return "电源";
            if (string.Equals(type, "AutoStart", StringComparison.OrdinalIgnoreCase)) return "自启";
            if (string.Equals(type, "Delay", StringComparison.OrdinalIgnoreCase)) return "延时";
            return type ?? string.Empty;
        }

        private static string TextFor(OperationStatus value)
        {
            if (value == OperationStatus.Running) return "运行中";
            if (value == OperationStatus.Succeeded) return "成功";
            if (value == OperationStatus.Failed) return "失败";
            if (value == OperationStatus.AlreadySatisfied) return "已满足";
            if (value == OperationStatus.Cancelled) return "已取消";
            if (value == OperationStatus.NotExecuted) return "未执行";
            return "等待";
        }

        private static string GlyphFor(OperationStatus value)
        {
            if (value == OperationStatus.Running) return "◐";
            if (value == OperationStatus.Succeeded) return "✓";
            if (value == OperationStatus.Failed) return "×";
            if (value == OperationStatus.AlreadySatisfied) return "–";
            if (value == OperationStatus.Cancelled) return "■";
            if (value == OperationStatus.NotExecuted) return "–";
            return "○";
        }

        private static Brush BrushFor(OperationStatus value)
        {
            if (value == OperationStatus.Running) return RunningBrush;
            if (value == OperationStatus.Succeeded) return SucceededBrush;
            if (value == OperationStatus.Failed) return FailedBrush;
            if (value == OperationStatus.AlreadySatisfied) return AlreadySatisfiedBrush;
            if (value == OperationStatus.Cancelled) return CancelledBrush;
            if (value == OperationStatus.NotExecuted) return NotExecutedBrush;
            return PendingBrush;
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalMinutes >= 1)
            {
                return (int)elapsed.TotalMinutes + " 分 " + elapsed.Seconds + " 秒";
            }
            return elapsed.TotalSeconds.ToString("0.0") + " 秒";
        }

        private static Brush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}
