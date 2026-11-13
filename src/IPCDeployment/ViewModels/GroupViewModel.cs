using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using IPCDeployment.Core;

namespace IPCDeployment.ViewModels
{
    public sealed class GroupViewModel : ObservableObject
    {
        private static readonly Brush PendingBrush = CreateBrush("#8492A5");
        private static readonly Brush CurrentBrush = CreateBrush("#2563EB");
        private static readonly Brush CompletedBrush = CreateBrush("#15803D");
        private static readonly Brush FailedBrush = CreateBrush("#DC2626");
        private static readonly Brush NotExecutedBrush = CreateBrush("#6B7280");
        private static readonly Brush WarningBrush = CreateBrush("#B45309");

        private int completed;
        private int failed;
        private int warnings;
        private int notExecuted;
        private bool isCurrent;
        private string statusText = "等待";
        private string statusGlyph = "○";
        private Brush statusBrush = PendingBrush;

        public GroupViewModel(string key, string name)
        {
            Key = key;
            Name = name;
            Tasks = new ObservableCollection<TaskViewModel>();
        }

        public string Key { get; private set; }
        public string Name { get; private set; }
        public ObservableCollection<TaskViewModel> Tasks { get; private set; }

        public int TotalTaskCount
        {
            get { return Tasks.Count; }
        }

        /// <summary>进入执行队列的任务数；进度分母只算这些任务。</summary>
        public int EnabledTaskCount
        {
            get { return Tasks.Count(task => !task.IsDisabled); }
        }

        public string ProgressText
        {
            get { return completed + "/" + EnabledTaskCount + " 任务"; }
        }

        public string TaskSummaryText
        {
            get
            {
                var disabled = Tasks.Count(task => task.IsDisabled);
                var suffix = disabled > 0 ? "（含 " + disabled + " 个禁用任务）" : string.Empty;
                return Tasks.Count + " 个任务 · " + Tasks.Sum(task => task.TotalSteps) + " 个步骤" + suffix;
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

        public void RecordTaskResult(OperationStatus status)
        {
            if (status == OperationStatus.Succeeded || status == OperationStatus.Warning ||
                status == OperationStatus.Failed || status == OperationStatus.Cancelled)
            {
                completed++;
            }
            else if (status == OperationStatus.NotExecuted)
            {
                notExecuted++;
            }

            if (status == OperationStatus.Failed)
            {
                failed++;
            }
            else if (status == OperationStatus.Warning)
            {
                warnings++;
            }
            OnPropertyChanged("ProgressText");
            UpdateDerived();
        }

        public void Reset()
        {
            completed = 0;
            failed = 0;
            warnings = 0;
            notExecuted = 0;
            IsCurrent = false;
            OnPropertyChanged("ProgressText");
            UpdateDerived();
        }

        public void Refresh()
        {
            completed = 0;
            failed = 0;
            warnings = 0;
            notExecuted = 0;
            foreach (var task in Tasks)
            {
                if (task.Status == OperationStatus.Succeeded || task.Status == OperationStatus.Warning ||
                    task.Status == OperationStatus.Failed || task.Status == OperationStatus.Cancelled)
                {
                    completed++;
                    if (task.Status == OperationStatus.Failed)
                    {
                        failed++;
                    }
                    else if (task.Status == OperationStatus.Warning)
                    {
                        warnings++;
                    }
                }
                else if (task.Status == OperationStatus.NotExecuted)
                {
                    notExecuted++;
                }
            }
            OnPropertyChanged("ProgressText");
            UpdateDerived();
        }

        private void UpdateDerived()
        {
            if (IsCurrent)
            {
                StatusText = "进行中";
                StatusGlyph = "●";
                StatusBrush = CurrentBrush;
                return;
            }

            if (Tasks.Count > 0 && EnabledTaskCount == 0)
            {
                StatusText = "已禁用";
                StatusGlyph = "–";
                StatusBrush = NotExecutedBrush;
                return;
            }

            if (failed > 0)
            {
                StatusText = "失败";
                StatusGlyph = "×";
                StatusBrush = FailedBrush;
                return;
            }

            if (warnings > 0)
            {
                StatusText = "警告";
                StatusGlyph = "!";
                StatusBrush = WarningBrush;
                return;
            }

            if (EnabledTaskCount > 0 && completed >= EnabledTaskCount)
            {
                StatusText = "成功";
                StatusGlyph = "✓";
                StatusBrush = CompletedBrush;
                return;
            }

            if (completed > 0)
            {
                StatusText = "进行中";
                StatusGlyph = "●";
                StatusBrush = CurrentBrush;
                return;
            }

            if (notExecuted > 0)
            {
                StatusText = "未执行";
                StatusGlyph = "–";
                StatusBrush = NotExecutedBrush;
                return;
            }

            StatusText = "等待";
            StatusGlyph = "○";
            StatusBrush = PendingBrush;
        }

        private static Brush CreateBrush(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
    }
}
