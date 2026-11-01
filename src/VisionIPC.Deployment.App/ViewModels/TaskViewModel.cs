using System.Collections.ObjectModel;
using System.Windows.Media;
using VisionIPC.Deployment.Core;

namespace VisionIPC.Deployment.App.ViewModels
{
    public sealed class TaskViewModel : ObservableObject
    {
        private static readonly Brush PendingBrush = CreateBrush("#8492A5");
        private static readonly Brush CurrentBrush = CreateBrush("#2563EB");
        private static readonly Brush CompletedBrush = CreateBrush("#15803D");
        private static readonly Brush FailedBrush = CreateBrush("#DC2626");
        private static readonly Brush NotExecutedBrush = CreateBrush("#6B7280");

        private OperationStatus status;
        private int completedSteps;
        private bool isCurrent;
        private string elapsedText = string.Empty;
        private string statusText = "等待";
        private string statusGlyph = "○";
        private Brush statusBrush = PendingBrush;

        public TaskViewModel(DeploymentTask task)
        {
            Key = task.Key;
            Name = task.Name;
            GroupKey = task.GroupKey;
            GroupName = task.GroupName;
            Steps = new ObservableCollection<OperationViewModel>();
            foreach (var step in task.Operations)
            {
                Steps.Add(new OperationViewModel(step));
            }
            status = OperationStatus.Pending;
        }

        public string Key { get; private set; }
        public string Name { get; private set; }
        public string GroupKey { get; private set; }
        public string GroupName { get; private set; }
        public ObservableCollection<OperationViewModel> Steps { get; private set; }

        public int TotalSteps
        {
            get { return Steps.Count; }
        }

        public int CompletedSteps
        {
            get { return completedSteps; }
        }

        public double ProgressPercent
        {
            get { return TotalSteps == 0 ? 0 : 100.0 * completedSteps / TotalSteps; }
        }

        public string ProgressText
        {
            get { return completedSteps + "/" + TotalSteps + " 步"; }
        }

        public OperationStatus Status
        {
            get { return status; }
            private set
            {
                if (SetProperty(ref status, value))
                {
                    UpdateDerived();
                }
            }
        }

        public bool IsCurrent
        {
            get { return isCurrent; }
            private set
            {
                if (SetProperty(ref isCurrent, value))
                {
                    UpdateDerived();
                }
            }
        }

        public string ElapsedText
        {
            get { return elapsedText; }
            private set { SetProperty(ref elapsedText, value); }
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

        public void SetCurrent(bool current)
        {
            IsCurrent = current;
        }

        public void SetElapsed(System.TimeSpan elapsed)
        {
            ElapsedText = FormatElapsed(elapsed);
        }

        public void RecordStepResult(OperationStatus stepStatus)
        {
            completedSteps++;
            OnPropertyChanged("CompletedSteps");
            OnPropertyChanged("ProgressPercent");
            OnPropertyChanged("ProgressText");
            UpdateDerived();
        }

        public void SetStatus(OperationStatus newStatus)
        {
            Status = newStatus;
            if (newStatus == OperationStatus.Failed || newStatus == OperationStatus.Cancelled ||
                newStatus == OperationStatus.NotExecuted)
            {
                foreach (var step in Steps)
                {
                    if (step.Status == OperationStatus.Pending || step.Status == OperationStatus.Running)
                    {
                        step.SetStatus(OperationStatus.NotExecuted,
                            newStatus == OperationStatus.NotExecuted ? "任务未执行。" : "任务已结束，未执行。",
                            null, false);
                    }
                }
            }
            UpdateDerived();
        }

        public void Reset()
        {
            completedSteps = 0;
            IsCurrent = false;
            ElapsedText = string.Empty;
            Status = OperationStatus.Pending;
            foreach (var step in Steps)
            {
                step.Reset();
            }
            OnPropertyChanged("CompletedSteps");
            OnPropertyChanged("ProgressPercent");
            OnPropertyChanged("ProgressText");
            UpdateDerived();
        }

        private void UpdateDerived()
        {
            if (IsCurrent)
            {
                StatusText = "运行中";
                StatusGlyph = "●";
                StatusBrush = CurrentBrush;
                return;
            }

            if (Status == OperationStatus.Succeeded)
            {
                StatusText = "成功";
                StatusGlyph = "✓";
                StatusBrush = CompletedBrush;
                return;
            }
            if (Status == OperationStatus.Failed)
            {
                StatusText = "失败";
                StatusGlyph = "×";
                StatusBrush = FailedBrush;
                return;
            }
            if (Status == OperationStatus.Cancelled)
            {
                StatusText = "已取消";
                StatusGlyph = "■";
                StatusBrush = NotExecutedBrush;
                return;
            }
            if (Status == OperationStatus.NotExecuted)
            {
                StatusText = "未执行";
                StatusGlyph = "–";
                StatusBrush = NotExecutedBrush;
                return;
            }
            if (completedSteps > 0)
            {
                StatusText = "进行中";
                StatusGlyph = "●";
                StatusBrush = CurrentBrush;
                return;
            }

            StatusText = "等待";
            StatusGlyph = "○";
            StatusBrush = PendingBrush;
        }

        private static string FormatElapsed(System.TimeSpan elapsed)
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
