using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using IPCDeployment.Core;

namespace IPCDeployment.ViewModels
{
    public sealed class DeploymentViewModel : ObservableObject
    {
        private readonly DeploymentShell shell;
        private readonly string packageRoot;
        private readonly string configDirectory;
        private readonly string defaultConfigPath;
        private readonly DispatcherTimer timer;
        private readonly List<string> logLines;
        private readonly Dictionary<string, List<string>> taskLogLines;
        private CancellationTokenSource cancellation;
        private string currentOperationId;
        private string currentStepPrefix;
        private DateTime currentStarted;
        private ConfigLoadResult loadResult;
        private string configPath;
        private string selectedConfigFile;
        private bool refreshingConfigFiles;
        private NavigationItemViewModel selectedNavigation;
        private GroupViewModel selectedGroup;
        private GroupViewModel selectedReportGroup;
        private TaskViewModel selectedTask;
        private DeploymentReport currentReport;

        private string configPathText;
        private string modeDetail;
        private string summaryText;
        private string validationText;
        private string groupCountText;
        private string taskCountText;
        private string stepCountText;
        private string progressText;

        private string currentStepText;
        private string logText;
        private string selectedTaskLogText;
        private string reportSummary;
        private string logPath;
        private string footerText;
        private bool canStart;
        private bool isRunning;
        private bool isEditorOpen;
        private bool showOverview;
        private bool showGroup;
        private bool showReport;
        private bool hasValidationIssues;
        private bool canReload;
        private bool canShowReport;
        private bool canOpenLog;

        public DeploymentViewModel(string packageRoot)
        {
            this.packageRoot = Path.GetFullPath(packageRoot);
            shell = new DeploymentShell(packageRoot);
            configDirectory = Path.Combine(this.packageRoot, "config");
            defaultConfigPath = Path.Combine(configDirectory, "config.xml");
            configPath = defaultConfigPath;
            logLines = new List<string>();
            taskLogLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            ConfigFiles = new ObservableCollection<string>();
            Groups = new ObservableCollection<GroupViewModel>();
            NavigationItems = new ObservableCollection<NavigationItemViewModel>();
            CompletionTasks = new ObservableCollection<TaskViewModel>();
            PageLogLines = new ObservableCollection<string>();
            AllTasks = new ObservableCollection<TaskViewModel>();
            SelectedTaskSteps = new ObservableCollection<OperationViewModel>();
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += Timer_Tick;

            configPathText = ConfigRelativePath(configPath);
            modeDetail = "正在读取配置...";
            summaryText = "尚未加载配置";
            validationText = string.Empty;
            groupCountText = "0 个分组";
            taskCountText = "0 个任务";
            stepCountText = "0 个步骤";
            progressText = "0/0 任务";
            currentStepText = "等待开始";
            logText = string.Empty;
            selectedTaskLogText = "请选择任务。";
            reportSummary = "尚未执行";
            logPath = string.Empty;
            footerText = ".NET Framework 4.7.2 或更高版本";
            canReload = true;
            showOverview = true;
        }

        public ObservableCollection<GroupViewModel> Groups { get; private set; }
        public ObservableCollection<string> ConfigFiles { get; private set; }
        public ObservableCollection<NavigationItemViewModel> NavigationItems { get; private set; }
        public ObservableCollection<TaskViewModel> CompletionTasks { get; private set; }
        public ObservableCollection<string> PageLogLines { get; private set; }
        public ObservableCollection<TaskViewModel> AllTasks { get; private set; }

        public GroupViewModel SelectedReportGroup
        {
            get { return selectedReportGroup; }
            set
            {
                if (!SetProperty(ref selectedReportGroup, value))
                {
                    return;
                }
                CompletionTasks.Clear();
                if (value == null)
                {
                    return;
                }
                foreach (var task in value.Tasks)
                {
                    CompletionTasks.Add(task);
                }
            }
        }
        public ObservableCollection<OperationViewModel> SelectedTaskSteps { get; private set; }

        public NavigationItemViewModel SelectedNavigation
        {
            get { return selectedNavigation; }
            set
            {
                if (!SetProperty(ref selectedNavigation, value) || value == null)
                {
                    return;
                }
                if (value.Kind == NavigationKind.Overview)
                {
                    ShowOverviewPage();
                    return;
                }
                if (value.Kind == NavigationKind.Completion)
                {
                    if (CanShowReport)
                    {
                        ShowReportPage();
                    }
                    else
                    {
                        var overview = NavigationItems.FirstOrDefault(item => item.Kind == NavigationKind.Overview);
                        if (overview != null)
                        {
                            SelectedNavigation = overview;
                        }
                    }
                    return;
                }
                if (value.Group != null)
                {
                    SelectedGroup = value.Group;
                }
            }
        }

        public GroupViewModel SelectedGroup
        {
            get { return selectedGroup; }
            set
            {
                if (!SetProperty(ref selectedGroup, value))
                {
                    return;
                }
                if (value != null)
                {
                    ShowOverview = false;
                    ShowGroup = true;
                    ShowReport = false;
                    var navigation = NavigationItems.FirstOrDefault(item => ReferenceEquals(item.Group, value));
                    if (navigation != null && !ReferenceEquals(SelectedNavigation, navigation))
                    {
                        SelectedNavigation = navigation;
                    }
                    if (selectedTask == null || !value.Tasks.Contains(selectedTask))
                    {
                        SelectedTask = value.Tasks.FirstOrDefault();
                    }
                    else
                    {
                        RebuildTaskSteps();
                        RefreshSelectedTaskLog();
                    }
                }
            }
        }

        public TaskViewModel SelectedTask
        {
            get { return selectedTask; }
            set
            {
                if (!SetProperty(ref selectedTask, value))
                {
                    return;
                }
                RebuildTaskSteps();
                RefreshSelectedTaskLog();
                OnPropertyChanged("PageLogText");
                RefreshPageLogLines();
                if (value != null)
                {
                    ShowOverview = false;
                    ShowGroup = true;
                    ShowReport = false;
                }
            }
        }

        public string ConfigPathText { get { return configPathText; } private set { SetProperty(ref configPathText, value); } }
        public string ConfigPath { get { return configPath; } }
        public string ModeDetail { get { return modeDetail; } private set { SetProperty(ref modeDetail, value); } }
        public string SummaryText { get { return summaryText; } private set { SetProperty(ref summaryText, value); } }
        public string ValidationText { get { return validationText; } private set { SetProperty(ref validationText, value); } }
        public string GroupCountText { get { return groupCountText; } private set { SetProperty(ref groupCountText, value); } }
        public string TaskCountText { get { return taskCountText; } private set { SetProperty(ref taskCountText, value); } }
        public string StepCountText { get { return stepCountText; } private set { SetProperty(ref stepCountText, value); } }
        public string ProgressText { get { return progressText; } private set { SetProperty(ref progressText, value); } }
        public string CurrentStepText { get { return currentStepText; } private set { SetProperty(ref currentStepText, value); } }
        public string LogText
        {
            get { return logText; }
            private set
            {
                if (SetProperty(ref logText, value))
                {
                    OnPropertyChanged("PageLogText");
                    RefreshPageLogLines();
                }
            }
        }

        public string SelectedTaskLogText
        {
            get { return selectedTaskLogText; }
            private set
            {
                if (SetProperty(ref selectedTaskLogText, value))
                {
                    OnPropertyChanged("PageLogText");
                    RefreshPageLogLines();
                }
            }
        }

        public string PageLogText
        {
            get { return ShowGroup && SelectedTask != null ? SelectedTaskLogText : LogText; }
        }

        private void RefreshPageLogLines()
        {
            PageLogLines.Clear();
            var text = PageLogText;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                PageLogLines.Add(line);
            }
        }
        public string ReportSummary { get { return reportSummary; } private set { SetProperty(ref reportSummary, value); } }

        public string OverviewStatusGlyph
        {
            get { return ShowOverview ? "●" : HasCurrentReport ? "✓" : "○"; }
        }

        public Brush OverviewStatusBrush
        {
            get { return ShowOverview ? Brushes.SteelBlue : HasCurrentReport ? Brushes.SeaGreen : Brushes.Gray; }
        }

        public string CompletionStatusGlyph
        {
            get
            {
                if (!HasCurrentReport)
                {
                    return "○";
                }
                return CurrentReport.FailedTaskCount > 0 || CurrentReport.Cancelled ? "×" : "✓";
            }
        }

        public Brush CompletionStatusBrush
        {
            get
            {
                if (!HasCurrentReport)
                {
                    return Brushes.Gray;
                }
                return CurrentReport.FailedTaskCount > 0 || CurrentReport.Cancelled ? Brushes.Firebrick : Brushes.SeaGreen;
            }
        }
        public string LogPath { get { return logPath; } private set { SetProperty(ref logPath, value); } }
        public string FooterText { get { return footerText; } private set { SetProperty(ref footerText, value); } }
        public bool CanStart { get { return canStart; } private set { SetProperty(ref canStart, value); OnPropertyChanged("CanStartNow"); } }
        public bool IsRunning { get { return isRunning; } private set { SetProperty(ref isRunning, value); } }
        public bool IsEditorOpen
        {
            get { return isEditorOpen; }
            set
            {
                if (SetProperty(ref isEditorOpen, value))
                {
                    OnPropertyChanged("CanStartNow");
                }
            }
        }
        public bool CanStartNow { get { return canStart && !isEditorOpen; } }
        public bool ShowOverview
        {
            get { return showOverview; }
            private set
            {
                if (SetProperty(ref showOverview, value))
                {
                    NotifyNavigationState();
                }
            }
        }
        public bool ShowGroup
        {
            get { return showGroup; }
            private set
            {
                if (SetProperty(ref showGroup, value))
                {
                    OnPropertyChanged("PageLogText");
                    RefreshPageLogLines();
                }
            }
        }
        public bool ShowReport { get { return showReport; } private set { SetProperty(ref showReport, value); } }
        public bool HasValidationIssues { get { return hasValidationIssues; } private set { SetProperty(ref hasValidationIssues, value); } }
        public bool CanReload { get { return canReload; } private set { SetProperty(ref canReload, value); } }
        public bool CanShowReport { get { return canShowReport; } private set { SetProperty(ref canShowReport, value); } }
        public bool CanOpenLog { get { return canOpenLog; } private set { SetProperty(ref canOpenLog, value); } }
        public DeploymentReport CurrentReport
        {
            get { return currentReport; }
            private set
            {
                if (SetProperty(ref currentReport, value))
                {
                    OnPropertyChanged("HasCurrentReport");
                    NotifyNavigationState();
                }
            }
        }
        public bool HasCurrentReport { get { return CurrentReport != null; } }

        public string SelectedConfigFile
        {
            get { return selectedConfigFile; }
            set
            {
                var previous = selectedConfigFile;
                if (value == null)
                {
                    // 刷新列表会短暂清空 ItemsSource，此时不要把选中项或配置路径改空。
                    if (refreshingConfigFiles)
                    {
                        return;
                    }
                    RestoreSelectedConfig(previous);
                    return;
                }
                if (!SetProperty(ref selectedConfigFile, value))
                {
                    return;
                }
                if (!CanReload || IsRunning || IsEditorOpen)
                {
                    RestoreSelectedConfig(previous);
                    return;
                }
                _ = ApplySelectedConfigAsync(previous, value);
            }
        }

        /// <summary>刷新 config 目录下的配置列表（示例文件不参与选择）。</summary>
        public void RefreshConfigFiles()
        {
            var current = selectedConfigFile;
            refreshingConfigFiles = true;
            try
            {
                ConfigFiles.Clear();
                if (Directory.Exists(configDirectory))
                {
                    foreach (var file in Directory.GetFiles(configDirectory, "*.xml")
                        .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                    {
                        var name = Path.GetFileName(file);
                        if (IsSampleConfig(name))
                        {
                            continue;
                        }
                        ConfigFiles.Add(name);
                    }
                }
            }
            finally
            {
                refreshingConfigFiles = false;
            }
            if (current != null && ConfigFiles.Contains(current))
            {
                RestoreSelectedConfig(current);
            }
        }

        public async Task LoadAsync()
        {
            RefreshConfigFiles();
            if (ConfigFiles.Count == 0)
            {
                ShowNoConfigState();
                return;
            }
            var name = ConfigFiles.FirstOrDefault(file => string.Equals(file, "config.xml", StringComparison.OrdinalIgnoreCase))
                ?? ConfigFiles[0];
            RestoreSelectedConfig(name);
            await SelectConfigAsync(Path.Combine(configDirectory, name));
        }

        public void WriteNote(string level, string message)
        {
            shell.WriteNote(level, message);
        }

        public async Task<bool> SelectConfigAsync(string path)
        {
            if (IsRunning || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(path);
            var name = Path.GetFileName(fullPath);
            if (!IsInConfigDirectory(fullPath) || IsSampleConfig(name))
            {
                loadResult = null;
                Groups.Clear();
                NavigationItems.Clear();
                AllTasks.Clear();
                SelectedTaskSteps.Clear();
                taskLogLines.Clear();
                SelectedNavigation = null;
                SelectedGroup = null;
                SelectedTask = null;
                ModeDetail = "配置文件必须位于 config 文件夹。";
                ValidationText = "请选择程序目录 config 文件夹下的 XML 配置。";
                HasValidationIssues = true;
                CanStart = false;
                CanShowReport = false;
                CanOpenLog = false;
                CurrentReport = null;
                ShowOverview = true;
                ShowGroup = false;
                ShowReport = false;
                FooterText = "配置文件路径无效。";
                return false;
            }

            configPath = fullPath;
            ConfigPathText = ConfigRelativePath(fullPath);
            RestoreSelectedConfig(name);
            CanStart = false;
            CanReload = false;
            CanShowReport = false;
            CanOpenLog = false;
            ModeDetail = "正在读取并预检 " + Path.GetFileName(path) + "...";
            FooterText = "正在读取配置...";

            try
            {
                var result = await Task.Run(() => shell.LoadConfig(fullPath));
                ApplyResult(result);
                return true;
            }
            catch (Exception exception)
            {
                loadResult = null;
                Groups.Clear();
                NavigationItems.Clear();
                AllTasks.Clear();
                SelectedTaskSteps.Clear();
                taskLogLines.Clear();
                SelectedNavigation = null;
                SelectedGroup = null;
                SelectedTask = null;
                ModeDetail = "无法读取 " + Path.GetFileName(path) + "。";
                ValidationText = exception.Message;
                HasValidationIssues = true;
                CanStart = false;
                CanShowReport = false;
                CanOpenLog = false;
                CurrentReport = null;
                ShowOverview = true;
                ShowGroup = false;
                ShowReport = false;
                FooterText = "配置加载失败。";
                return false;
            }
            finally
            {
                CanReload = true;
            }
        }

        public Task ReloadAsync()
        {
            return SelectConfigAsync(configPath);
        }

        private async Task ApplySelectedConfigAsync(string previous, string name)
        {
            var ok = await SelectConfigAsync(Path.Combine(configDirectory, name));
            if (!ok)
            {
                RestoreSelectedConfig(previous);
            }
        }

        private void RestoreSelectedConfig(string name)
        {
            selectedConfigFile = name;
            OnPropertyChanged("SelectedConfigFile");
        }

        private bool IsInConfigDirectory(string fullPath)
        {
            var directory = Path.GetDirectoryName(fullPath);
            return directory != null && string.Equals(
                directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                configDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSampleConfig(string name)
        {
            return name.EndsWith(".sample.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static string ConfigRelativePath(string fullPath)
        {
            return Path.Combine("config", Path.GetFileName(fullPath));
        }

        private void ShowNoConfigState()
        {
            loadResult = null;
            Groups.Clear();
            NavigationItems.Clear();
            AllTasks.Clear();
            SelectedTaskSteps.Clear();
            taskLogLines.Clear();
            SelectedNavigation = null;
            SelectedGroup = null;
            SelectedTask = null;
            configPath = defaultConfigPath;
            ConfigPathText = Path.Combine("config", "（无配置）");
            ModeDetail = "config 文件夹中没有可用的 XML 配置。";
            ValidationText = "请把 XML 配置放入程序目录的 config 文件夹。";
            HasValidationIssues = true;
            CanStart = false;
            CanShowReport = false;
            CanOpenLog = false;
            CurrentReport = null;
            ShowOverview = true;
            ShowGroup = false;
            ShowReport = false;
            FooterText = "未找到配置。";
        }

        public void ShowOverviewPage()
        {
            if (IsRunning)
            {
                return;
            }
            var overview = NavigationItems.FirstOrDefault(item => item.Kind == NavigationKind.Overview);
            if (overview != null && !ReferenceEquals(SelectedNavigation, overview))
            {
                SelectedNavigation = overview;
                return;
            }
            SelectedTask = null;
            SelectedGroup = null;
            ShowOverview = true;
            ShowGroup = false;
            ShowReport = false;
        }

        public void ShowReportPage()
        {
            if (!CanShowReport)
            {
                return;
            }
            var completion = NavigationItems.FirstOrDefault(item => item.Kind == NavigationKind.Completion);
            if (completion != null && !ReferenceEquals(SelectedNavigation, completion))
            {
                SelectedNavigation = completion;
                return;
            }
            SelectedTask = null;
            SelectedGroup = null;
            ShowOverview = false;
            ShowGroup = false;
            ShowReport = true;
        }

        public async Task StartAsync()
        {
            if (!CanStart || IsRunning || loadResult == null)
            {
                return;
            }
            PrepareRun();
            try
            {
                var report = await shell.StartAsync(true, new Progress<DeploymentEvent>(OnDeploymentEvent), cancellation.Token);
                ApplyReport(report);
            }
            catch (OperationCanceledException)
            {
                ReportSummary = "执行已取消";
            }
            catch (Exception exception)
            {
                ReportSummary = "执行异常：" + exception.Message;
            }
            finally
            {
                FinishRun();
            }
        }

        public void Cancel()
        {
            if (cancellation != null && !cancellation.IsCancellationRequested)
            {
                cancellation.Cancel();
                FooterText = "正在停止当前步骤...";
            }
        }

        public string BuildConfirmationText()
        {
            return "即将使用 " + (string.IsNullOrEmpty(selectedConfigFile) ? "当前配置" : selectedConfigFile) +
                " 执行 " + Groups.Count + " 个分组、" + AllTasks.Count + " 个任务、" +
                AllTasks.Sum(t => t.TotalSteps) + " 个步骤。" + Environment.NewLine + Environment.NewLine +
                "任一任务步骤失败会结束该任务，并继续下一个任务。" + Environment.NewLine +
                "本程序将以管理员权限修改本机，且不提供自动回滚。" + Environment.NewLine + Environment.NewLine +
                "确认执行？";
        }

        private void PrepareRun()
        {
            IsRunning = true;
            CanStart = false;
            CanReload = false;
            CanShowReport = false;
            CanOpenLog = false;
            ShowOverview = false;
            ShowGroup = true;
            ShowReport = false;
            ResetRunState();
            SelectedGroup = Groups.FirstOrDefault();
            SelectedTask = SelectedGroup == null ? null : SelectedGroup.Tasks.FirstOrDefault();
            cancellation = new CancellationTokenSource();
            FooterText = "执行中；任一任务步骤失败会结束该任务并继续下一个任务。";
        }

        private void FinishRun()
        {
            IsRunning = false;
            CanReload = true;
            timer.Stop();
            foreach (var group in Groups)
            {
                group.SetCurrent(false);
            }
            foreach (var task in AllTasks)
            {
                task.SetCurrent(false);
            }
            SelectedTask = null;
            SelectedGroup = null;
            CanShowReport = CurrentReport != null;
            SelectedReportGroup = Groups.FirstOrDefault(group =>
                group.Tasks.Any(task => task.Status == OperationStatus.Failed)) ?? Groups.FirstOrDefault();
            var completion = NavigationItems.FirstOrDefault(item => item.Kind == NavigationKind.Completion);
            if (CanShowReport && completion != null)
            {
                SelectedNavigation = completion;
            }
            else
            {
                ShowOverview = false;
                ShowGroup = false;
                ShowReport = true;
            }
            CanStart = loadResult != null && loadResult.CanExecute;
            CanOpenLog = !string.IsNullOrEmpty(LogPath) && File.Exists(LogPath);
            var logState = shell.HasLogWriteFailure
                ? "日志写入失败，当前会话日志保留在内存。"
                : shell.UsedLogFallback
                    ? "日志已回退到临时目录：" + shell.LogPath
                    : "日志：" + (string.IsNullOrEmpty(shell.LogPath) ? "不可用" : shell.LogPath);
            FooterText = ReportSummary + "；" + logState;
        }

        private void ApplyResult(ConfigLoadResult result)
        {
            loadResult = result;
            Groups.Clear();
            SelectedReportGroup = null;
            AllTasks.Clear();
            SelectedTaskSteps.Clear();
            taskLogLines.Clear();
            CurrentReport = null;
            CanShowReport = false;
            CanOpenLog = false;

            var config = result.Config;
            if (config != null)
            {
                foreach (var group in config.Groups)
                {
                    var groupViewModel = new GroupViewModel(group.Key, group.Name);
                    foreach (var task in group.Tasks)
                    {
                        var taskViewModel = new TaskViewModel(task);
                        groupViewModel.Tasks.Add(taskViewModel);
                        AllTasks.Add(taskViewModel);
                        taskLogLines[task.Key] = new List<string>();
                    }
                    Groups.Add(groupViewModel);
                }
            }

            GroupCountText = Groups.Count + " 个分组";
            TaskCountText = AllTasks.Count + " 个任务";
            StepCountText = (config == null ? 0 : config.Operations.Count) + " 个步骤";
            SummaryText = "共 " + Groups.Count + " 个分组 · " + AllTasks.Count + " 个任务 · " +
                (config == null ? 0 : config.Operations.Count) + " 个步骤";
            ModeDetail = config == null
                ? "配置未加载；请选择并校验 XML 配置。"
                : config.Mode == DeploymentMode.Execute
                    ? "执行模式：开始前会显示任务摘要并再次确认；无自动回滚。"
                    : "预览模式：只检查配置，不执行。";

            ValidationText = BuildValidationText(result);
            HasValidationIssues = result.Errors.Count > 0 || result.Warnings.Count > 0;
            CanStart = result.CanExecute;
            ProgressText = "0/" + AllTasks.Count + " 任务";
            CurrentStepText = "等待开始";
            SelectedTaskLogText = "请选择任务。";
            ReportSummary = "尚未执行";
            timer.Stop();
            currentOperationId = null;

            logLines.Clear();
            logLines.AddRange(shell.RuntimeLogLines);
            TrimLog(logLines);
            LogText = string.Join(Environment.NewLine, logLines);
            LogPath = shell.LogPath ?? string.Empty;
            FooterText = config == null
                ? "配置加载失败；请选择 XML 配置。"
                : "配置校验完成；日志：" + (string.IsNullOrEmpty(LogPath) ? "不可用" : LogPath);

            BuildNavigationItems();
            SelectedNavigation = NavigationItems.FirstOrDefault(item => item.Kind == NavigationKind.Overview);
            NotifyNavigationState();
        }

        private string BuildValidationText(ConfigLoadResult result)
        {
            var lines = new List<string>();
            foreach (var error in result.Errors)
            {
                lines.Add("错误 · " + error.ToDisplayString());
            }
            foreach (var warning in result.Warnings)
            {
                lines.Add("警告 · " + warning.ToDisplayString());
            }
            if (result.Config != null && result.Config.Mode == DeploymentMode.Preview)
            {
                lines.Add("提示：当前为 preview 模式，不会执行任何步骤。");
            }
            if (lines.Count == 0)
            {
                lines.Add("配置校验通过。");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private void ResetRunState()
        {
            foreach (var group in Groups)
            {
                group.Reset();
            }
            foreach (var task in AllTasks)
            {
                task.Reset();
            }
            foreach (var pair in taskLogLines)
            {
                pair.Value.Clear();
            }

            ProgressText = "0/" + AllTasks.Count + " 任务";
            CurrentStepText = "准备开始...";
            SelectedTaskLogText = "该任务尚未运行。";
            ReportSummary = "执行中";
            CurrentReport = null;
            LogText = string.Empty;
            logLines.Clear();
            currentOperationId = null;
            currentStepPrefix = string.Empty;
            timer.Stop();
            NotifyNavigationState();
        }

        private void OnDeploymentEvent(DeploymentEvent value)
        {
            if (value == null)
            {
                return;
            }

            if (value.Kind == DeploymentEventKind.LogLine)
            {
                AppendLog(value.LogLine, value.OperationId, value.TaskKey);
                return;
            }

            if (value.Kind == DeploymentEventKind.TaskStarted)
            {
                var task = FindTask(value.TaskKey);
                if (task != null)
                {
                    task.SetCurrent(true);
                    var group = FindGroup(task.GroupKey);
                    if (group != null)
                    {
                        group.SetCurrent(true);
                        SelectedGroup = group;
                    }
                    SelectedTask = task;
                    NotifyNavigationState();
                }
                return;
            }

            if (value.Kind == DeploymentEventKind.OperationStarted)
            {
                var operation = FindOperation(value.OperationId);
                if (operation != null)
                {
                    operation.SetStatus(OperationStatus.Running, string.Empty, null, false);
                }
                currentOperationId = value.OperationId;
                currentStarted = DateTime.UtcNow;
                currentStepPrefix = "第 " + value.TaskStepIndex + "/" + value.TaskStepTotal + " 步 · " + value.OperationName;
                CurrentStepText = currentStepPrefix;
                timer.Start();
                return;
            }

            if (value.Kind == DeploymentEventKind.OperationCompleted)
            {
                var operation = FindOperation(value.OperationId);
                if (operation != null)
                {
                    operation.SetStatus(value.Status, value.Detail, value.Elapsed, value.RestartRequired);
                    var task = FindTask(operation.TaskKey);
                    if (task != null)
                    {
                        task.RecordStepResult(value.Status);
                    }
                }
                if (string.Equals(currentOperationId, value.OperationId, StringComparison.Ordinal))
                {
                    timer.Stop();
                    currentOperationId = null;
                    CurrentStepText = "等待下一步...";
                }
                return;
            }

            if (value.Kind == DeploymentEventKind.TaskCompleted)
            {
                var task = FindTask(value.TaskKey);
                if (task != null)
                {
                    task.SetStatus(value.TaskStatus);
                    task.SetElapsed(value.Elapsed);
                    task.SetCurrent(false);
                    var group = FindGroup(task.GroupKey);
                    if (group != null)
                    {
                        group.RecordTaskResult(value.TaskStatus);
                        group.SetCurrent(false);
                    }
                }
                ProgressText = value.CompletedTasks + "/" + value.TaskTotal + " 任务";
                CurrentStepText = "任务已结束";
                timer.Stop();
                currentOperationId = null;
                NotifyNavigationState();
                return;
            }

            if (value.Kind == DeploymentEventKind.QueueCompleted)
            {
                ApplyReport(value.Report);
            }
        }

        private void ApplyReport(DeploymentReport report)
        {
            if (report == null)
            {
                return;
            }

            foreach (var result in report.Results)
            {
                var operation = FindOperation(result.OperationId);
                if (operation != null)
                {
                    operation.SetStatus(result.Status, result.Detail, result.Elapsed, result.RestartRequired);
                }
            }
            foreach (var taskResult in report.Tasks)
            {
                var task = FindTask(taskResult.TaskKey);
                if (task != null)
                {
                    task.SetStatus(taskResult.Status);
                    task.SetElapsed(taskResult.Elapsed);
                }
            }
            foreach (var group in Groups)
            {
                group.Refresh();
            }

            CurrentReport = report;
            CanShowReport = true;
            CanOpenLog = !string.IsNullOrEmpty(report.LogPath) && File.Exists(report.LogPath);
            ProgressText = report.CompletedTaskCount + "/" + report.TotalTaskCount + " 任务";

            ReportSummary = report.Cancelled ? "执行已取消" : "执行结束";

            LogPath = report.LogPath ?? string.Empty;
            CurrentStepText = "执行结束";
            FooterText = ReportSummary + "；日志：" + (string.IsNullOrEmpty(LogPath) ? "不可用" : LogPath);
            NotifyNavigationState();
        }

        private void Timer_Tick(object sender, EventArgs eventArgs)
        {
            if (!IsRunning || string.IsNullOrEmpty(currentOperationId))
            {
                return;
            }

            var operation = FindOperation(currentOperationId);
            if (operation == null)
            {
                return;
            }

            var elapsed = DateTime.UtcNow - currentStarted;
            operation.SetElapsed(elapsed);
            CurrentStepText = currentStepPrefix + " · " + FormatElapsed(elapsed);
        }

        private void RebuildTaskSteps()
        {
            SelectedTaskSteps.Clear();
            if (SelectedTask == null)
            {
                return;
            }
            foreach (var step in SelectedTask.Steps)
            {
                SelectedTaskSteps.Add(step);
            }
        }

        private void RefreshSelectedTaskLog()
        {
            if (SelectedTask == null)
            {
                SelectedTaskLogText = "请选择任务。";
                return;
            }
            List<string> lines;
            if (!taskLogLines.TryGetValue(SelectedTask.Key, out lines) || lines.Count == 0)
            {
                SelectedTaskLogText = "该任务尚未运行。";
                return;
            }
            SelectedTaskLogText = string.Join(Environment.NewLine, lines);
        }

        private OperationViewModel FindOperation(string operationId)
        {
            foreach (var task in AllTasks)
            {
                foreach (var step in task.Steps)
                {
                    if (string.Equals(step.Id, operationId, StringComparison.Ordinal))
                    {
                        return step;
                    }
                }
            }
            return null;
        }

        private TaskViewModel FindTask(string taskKey)
        {
            return AllTasks.FirstOrDefault(task => string.Equals(task.Key, taskKey, StringComparison.Ordinal));
        }

        private GroupViewModel FindGroup(string groupKey)
        {
            return Groups.FirstOrDefault(group => string.Equals(group.Key, groupKey, StringComparison.Ordinal));
        }

        private void AppendLog(string line, string operationId, string taskKey)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            logLines.Add(line);
            TrimLog(logLines);
            LogText = string.Join(Environment.NewLine, logLines);

            if (string.IsNullOrEmpty(taskKey))
            {
                var operation = FindOperation(operationId);
                taskKey = operation == null ? null : operation.TaskKey;
            }
            if (string.IsNullOrEmpty(taskKey))
            {
                return;
            }
            List<string> lines;
            if (!taskLogLines.TryGetValue(taskKey, out lines))
            {
                lines = new List<string>();
                taskLogLines[taskKey] = lines;
            }
            lines.Add(line);
            TrimLog(lines);
            if (SelectedTask != null && string.Equals(SelectedTask.Key, taskKey, StringComparison.Ordinal))
            {
                SelectedTaskLogText = string.Join(Environment.NewLine, lines);
            }
        }

        private static void TrimLog(List<string> lines)
        {
            if (lines.Count > 2000)
            {
                lines.RemoveRange(0, lines.Count - 2000);
            }
        }

        private void BuildNavigationItems()
        {
            NavigationItems.Clear();
            NavigationItems.Add(new NavigationItemViewModel(NavigationKind.Overview, "任务总览"));

            foreach (var group in Groups)
            {
                NavigationItems.Add(new NavigationItemViewModel(NavigationKind.Group, group.Name)
                {
                    Group = group
                });
            }

            NavigationItems.Add(new NavigationItemViewModel(NavigationKind.Completion, "部署完成"));
            SyncNavigationItems();
        }

        private void SyncNavigationItems()
        {
            foreach (var item in NavigationItems)
            {
                if (item.Kind == NavigationKind.Overview)
                {
                    item.Name = "任务总览";
                    item.StatusGlyph = OverviewStatusGlyph;
                    item.StatusBrush = OverviewStatusBrush;
                    item.ProgressText = string.Empty;
                }
                else if (item.Kind == NavigationKind.Completion)
                {
                    item.Name = "部署完成";
                    item.StatusGlyph = CompletionStatusGlyph;
                    item.StatusBrush = CompletionStatusBrush;
                    item.ProgressText = ProgressText;
                }
                else if (item.Group != null)
                {
                    item.Name = item.Group.Name;
                    item.StatusGlyph = item.Group.StatusGlyph;
                    item.StatusBrush = item.Group.StatusBrush;
                    item.ProgressText = item.Group.ProgressText;
                }
            }
        }

        private void NotifyNavigationState()
        {
            SyncNavigationItems();
            OnPropertyChanged("OverviewStatusGlyph");
            OnPropertyChanged("OverviewStatusBrush");
            OnPropertyChanged("CompletionStatusGlyph");
            OnPropertyChanged("CompletionStatusBrush");
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalMinutes >= 1)
            {
                return (int)elapsed.TotalMinutes + " 分 " + elapsed.Seconds + " 秒";
            }
            return elapsed.TotalSeconds.ToString("0.0") + " 秒";
        }
    }
}
