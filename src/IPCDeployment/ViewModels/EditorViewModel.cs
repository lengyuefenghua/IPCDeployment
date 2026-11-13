using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using IPCDeployment.Core;

namespace IPCDeployment.ViewModels
{
    /// <summary>
    /// 配置编辑器的界面状态：结构树、分区字段表单、搜索、校验提示与保存提示。
    /// XML 读写与校验全部委托给 <see cref="ConfigurationDocument"/>。
    /// </summary>
    public sealed class EditorViewModel : ObservableObject
    {
        private readonly HashSet<XElement> dirtyElements = new HashSet<XElement>();
        private readonly string dataRoot;
        private readonly string configDirectory;
        private readonly SourcePathCatalog sourcePathCatalog = new SourcePathCatalog();
        private ConfigurationDocument document;
        private EditorNodeViewModel selectedNode;
        private EditorIssueViewModel selectedIssue;
        private Dictionary<string, string> currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string filePathText = "（未保存的新配置）";
        private string statusText = "就绪";
        private string savedAtText = "未保存";
        private string searchText = string.Empty;
        private string modeBadgeText = "preview · 只预检";
        private string summaryText = "0 分组 · 0 任务 · 0 步骤";
        private string selectedConfigFile;
        private string previousConfigFile;
        private bool switchingConfig;
        private bool isDirty;
        private bool isExecuteMode;
        private bool isIssuePanelOpen;
        private Action<string, string> note;

        public EditorViewModel(string dataRoot, string configDirectory)
        {
            this.dataRoot = dataRoot;
            this.configDirectory = configDirectory;
            document = ConfigurationDocument.CreateNew(dataRoot);
            AttachDocument(document);
        }

        public ObservableCollection<EditorNodeViewModel> Roots { get; } = new ObservableCollection<EditorNodeViewModel>();
        public ObservableCollection<EditorFieldSectionViewModel> Sections { get; } = new ObservableCollection<EditorFieldSectionViewModel>();
        public ObservableCollection<EditorIssueViewModel> Issues { get; } = new ObservableCollection<EditorIssueViewModel>();
        public ObservableCollection<EditorSearchResultViewModel> SearchResults { get; } = new ObservableCollection<EditorSearchResultViewModel>();
        public ObservableCollection<ConfigFileItem> ConfigFiles { get; } = new ObservableCollection<ConfigFileItem>();

        /// <summary>用户在下拉里选了另一份配置时触发，由窗口负责未保存确认与实际打开。</summary>
        public event Action<string> ConfigSwitchRequested;

        /// <summary>请求把某个字段滚入视野；<c>selectAll</c> 为真时同时全选文本（新增节点后命名）。</summary>
        public event Action<EditorFieldViewModel, bool> FieldFocusRequested;

        /// <summary>运行日志出口，由窗口转发给部署外壳。</summary>
        public Action<string, string> Note
        {
            get { return note; }
            set
            {
                note = value;
                document.Note = value;
            }
        }

        /// <summary>九类步骤的中文菜单项，供树里的「+」与右键菜单共用。</summary>
        public IList<EditorStepTypeOption> StepTypeOptions { get; } = new List<EditorStepTypeOption>
        {
            new EditorStepTypeOption("Install", "安装程序 · Install"),
            new EditorStepTypeOption("Copy", "复制文件 · Copy"),
            new EditorStepTypeOption("Registry", "写注册表 · Registry"),
            new EditorStepTypeOption("Command", "运行命令 · Command"),
            new EditorStepTypeOption("Service", "配置服务 · Service"),
            new EditorStepTypeOption("Network", "配置网络 · Network"),
            new EditorStepTypeOption("Power", "电源设置 · Power"),
            new EditorStepTypeOption("AutoStart", "开机自启 · AutoStart"),
            new EditorStepTypeOption("Delay", "延时等待 · Delay")
        };

        /// <summary>下拉里当前选中的配置文件名；切换请求交给窗口处理。</summary>
        public string SelectedConfigFile
        {
            get { return selectedConfigFile; }
            set
            {
                if (switchingConfig || string.IsNullOrEmpty(value) ||
                    string.Equals(selectedConfigFile, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                // 先记下旧值，窗口确认取消或打开失败时据此回滚。
                previousConfigFile = selectedConfigFile;
                selectedConfigFile = value;
                OnPropertyChanged("SelectedConfigFile");
                var handler = ConfigSwitchRequested;
                if (handler != null)
                {
                    handler(value);
                }
            }
        }

        public string PreviousConfigFile
        {
            get { return previousConfigFile; }
        }

        /// <summary>刷新 config 目录下的配置列表（示例文件不参与选择）。</summary>
        public void RefreshConfigFiles()
        {
            var desired = new List<ConfigFileItem>();
            if (Directory.Exists(configDirectory))
            {
                foreach (var file in Directory.GetFiles(configDirectory, "*.xml")
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(file);
                    if (ConfigFileRules.IsSampleConfig(name))
                    {
                        continue;
                    }
                    desired.Add(ConfigFileItem.Create(file));
                }
            }
            // 增量同步：列表没变时不碰集合，避免展开下拉时选中项被清空。
            ConfigFileItem.Sync(ConfigFiles, desired);
            // 列表变化后把文档对应的文件名推回去，避免下拉短暂失去选中项。
            OnPropertyChanged("SelectedConfigFile");
        }

        /// <summary>回滚下拉选中项；直写字段，不触发切换事件。</summary>
        public void RevertSelectedConfig(string name)
        {
            selectedConfigFile = name;
            OnPropertyChanged("SelectedConfigFile");
        }

        /// <summary>文档当前对应的文件名同步到下拉；直写字段，不触发切换事件。</summary>
        public void SyncSelectedConfig(string path)
        {
            selectedConfigFile = string.IsNullOrEmpty(path) ? null : Path.GetFileName(path);
            previousConfigFile = selectedConfigFile;
            switchingConfig = true;
            try
            {
                OnPropertyChanged("SelectedConfigFile");
            }
            finally
            {
                switchingConfig = false;
            }
        }

        public string FilePathText
        {
            get { return filePathText; }
            private set { SetProperty(ref filePathText, value); }
        }

        public string StatusText
        {
            get { return statusText; }
            private set { SetProperty(ref statusText, value); }
        }

        public string SavedAtText
        {
            get { return savedAtText; }
            private set { SetProperty(ref savedAtText, value); }
        }

        public string SummaryText
        {
            get { return summaryText; }
            private set { SetProperty(ref summaryText, value); }
        }

        public string ModeBadgeText
        {
            get { return modeBadgeText; }
            private set { SetProperty(ref modeBadgeText, value); }
        }

        public bool IsExecuteMode
        {
            get { return isExecuteMode; }
            private set { SetProperty(ref isExecuteMode, value); }
        }

        public string SearchText
        {
            get { return searchText; }
            set
            {
                if (!SetProperty(ref searchText, value ?? string.Empty))
                {
                    return;
                }
                OnPropertyChanged("IsSearching");
                OnPropertyChanged("ShowTree");
                RefreshSearch();
            }
        }

        public bool IsSearching
        {
            get { return !string.IsNullOrWhiteSpace(searchText); }
        }

        public bool ShowTree
        {
            get { return !IsSearching; }
        }

        public bool IsIssuePanelOpen
        {
            get { return isIssuePanelOpen; }
            set
            {
                if (SetProperty(ref isIssuePanelOpen, value))
                {
                    OnPropertyChanged("ShowIssuePanel");
                }
            }
        }

        public bool HasIssues
        {
            get { return Issues.Count > 0; }
        }

        public string IssueCountText
        {
            get { return Issues.Count == 0 ? "无问题" : Issues.Count + " 处问题"; }
        }

        public bool ShowIssuePanel
        {
            get { return IsIssuePanelOpen && HasIssues; }
        }

        public bool IsDirty
        {
            get { return isDirty; }
            private set
            {
                if (SetProperty(ref isDirty, value))
                {
                    OnPropertyChanged("TitleText");
                }
            }
        }

        public string TitleText
        {
            get { return IsDirty ? "配置编辑器 · 未保存" : "配置编辑器"; }
        }

        public string IssuesTitle
        {
            get { return Issues.Count == 0 ? string.Empty : "校验未通过：" + Issues.Count + " 处问题"; }
        }

        public EditorNodeViewModel SelectedNode
        {
            get { return selectedNode; }
            set
            {
                if (!SetProperty(ref selectedNode, value))
                {
                    return;
                }
                RefreshFields();
                RefreshSelectionState();
            }
        }

        public EditorIssueViewModel SelectedIssue
        {
            get { return selectedIssue; }
            set
            {
                if (!SetProperty(ref selectedIssue, value) || value == null)
                {
                    return;
                }
                var target = FindViewModel(Roots.FirstOrDefault(), value.Node);
                if (target != null)
                {
                    SelectNode(target, false);
                }
                LocateIssueField(value);
            }
        }

        public string SelectedTitle
        {
            get { return selectedNode == null ? string.Empty : selectedNode.NameText; }
        }

        public string SelectedTypeText
        {
            get { return selectedNode == null ? string.Empty : selectedNode.TypeText; }
        }

        public string BreadcrumbText
        {
            get
            {
                if (selectedNode == null)
                {
                    return string.Empty;
                }
                var names = new List<string>();
                var current = selectedNode.Node;
                while (current != null)
                {
                    names.Insert(0, current.Kind == EditorNodeKind.Config ? "配置" : NodeName(current));
                    current = current.Parent;
                }
                return string.Join(" › ", names);
            }
        }

        public bool HasSelection
        {
            get { return selectedNode != null; }
        }

        public bool CanAddTask
        {
            get
            {
                return selectedNode != null &&
                    (selectedNode.Node.Kind == EditorNodeKind.Group || selectedNode.Node.Kind == EditorNodeKind.Task);
            }
        }

        public bool CanAddStep
        {
            get
            {
                return selectedNode != null && selectedNode.Node.Kind == EditorNodeKind.Task;
            }
        }

        public bool CanRemove
        {
            get { return selectedNode != null && selectedNode.Node.Kind != EditorNodeKind.Config; }
        }

        public bool CanMoveUp
        {
            get { return selectedNode != null && document.CanMove(selectedNode.Node, -1); }
        }

        public bool CanMoveDown
        {
            get { return selectedNode != null && document.CanMove(selectedNode.Node, 1); }
        }

        public string CurrentFilePath
        {
            get { return document.TargetPath; }
        }

        public void NewDocument()
        {
            ClearIssues();
            IsIssuePanelOpen = false;
            SearchText = string.Empty;
            AttachDocument(ConfigurationDocument.CreateNew(dataRoot));
            FilePathText = "（未保存的新配置）";
            StatusText = "已新建配置骨架";
            SavedAtText = "未保存";
            SyncSelectedConfig(null);
        }

        public EditorOpenResult Open(string path)
        {
            var result = ConfigurationDocument.Open(path, dataRoot);
            if (!result.Succeeded)
            {
                var note = this.note;
                if (note != null)
                {
                    note("WARN", "配置编辑器无法打开：" + path + "。");
                }
                return result;
            }
            ClearIssues();
            IsIssuePanelOpen = false;
            SearchText = string.Empty;
            AttachDocument(result.Document);
            FilePathText = result.Document.TargetPath;
            StatusText = "已打开 " + result.Document.TargetPath;
            SavedAtText = "已保存";
            SyncSelectedConfig(result.Document.TargetPath);
            return result;
        }

        public EditorSaveResult Save(string path, bool overwriteExternal)
        {
            // 先做全文档字段级轻校验，尽早把问题摆出来；写盘仍以加载器为最终关口。
            ValidateVisibleFields();
            var localIssues = ValidateDocument();
            if (localIssues.Count > 0)
            {
                FillEditorIssues(localIssues);
                IsIssuePanelOpen = true;
                SelectFirstIssue();
                StatusText = "校验未通过，共 " + localIssues.Count + " 处问题。";
                SavedAtText = "未保存";
                var rejected = new EditorSaveResult
                {
                    Status = EditorSaveStatus.ValidationFailed,
                    Message = StatusText
                };
                return rejected;
            }

            ClearIssues();
            var result = document.Save(path, overwriteExternal);
            IsDirty = document.IsDirty;
            if (result.Status == EditorSaveStatus.ValidationFailed)
            {
                FillLoaderIssues(result.Issues);
                IsIssuePanelOpen = true;
                SelectFirstIssue();
                StatusText = result.Message;
                SavedAtText = "未保存";
                return result;
            }
            if (result.Status == EditorSaveStatus.Saved)
            {
                FilePathText = document.TargetPath;
                StatusText = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
                SavedAtText = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
                ClearDirtyFlags();
                ClearFieldErrors();
                IsIssuePanelOpen = false;
                RefreshSummary();
                RefreshConfigFiles();
                SyncSelectedConfig(document.TargetPath);
            }
            else
            {
                StatusText = result.Message;
            }
            return result;
        }

        public void AddGroup()
        {
            var node = document.AddGroup();
            if (node != null && node.Element != null)
            {
                dirtyElements.Add(node.Element);
            }
            Rebuild(node == null ? null : node.Element);
            StatusText = "已新增分组";
            RequestFieldFocus("name");
        }

        public void AddTask()
        {
            var group = selectedNode == null ? null : AncestorOfKind(selectedNode.Node, EditorNodeKind.Group);
            if (group == null)
            {
                return;
            }
            var node = document.AddTask(group);
            if (node != null && node.Element != null)
            {
                dirtyElements.Add(node.Element);
                if (group.Element != null)
                {
                    dirtyElements.Add(group.Element);
                }
            }
            Rebuild(node == null ? null : node.Element);
            StatusText = "已新增任务";
            RequestFieldFocus("name");
        }

        public void AddStep(string stepType)
        {
            if (selectedNode == null || selectedNode.Node.Kind != EditorNodeKind.Task)
            {
                return;
            }
            var node = document.AddStep(selectedNode.Node, stepType);
            if (node != null && node.Element != null)
            {
                dirtyElements.Add(node.Element);
                if (selectedNode.Node.Element != null)
                {
                    dirtyElements.Add(selectedNode.Node.Element);
                }
            }
            Rebuild(node == null ? null : node.Element);
            StatusText = "已新增步骤";
            RequestFieldFocus("name");
        }

        public void RemoveSelected()
        {
            if (selectedNode == null || selectedNode.Node.Kind == EditorNodeKind.Config)
            {
                return;
            }
            var parentElement = selectedNode.Node.Element.Parent;
            RemoveDirtyElements(selectedNode.Node.Element);
            document.Remove(selectedNode.Node);
            if (parentElement != null)
            {
                dirtyElements.Add(parentElement);
            }
            Rebuild(parentElement);
            StatusText = "已删除所选节点";
        }

        public void MoveSelected(int offset)
        {
            if (selectedNode == null)
            {
                return;
            }
            var element = selectedNode.Node.Element;
            if (document.Move(selectedNode.Node, offset))
            {
                if (element != null)
                {
                    dirtyElements.Add(element);
                }
                Rebuild(element);
                StatusText = "已调整顺序";
            }
        }

        public void SelectNode(EditorNodeViewModel node, bool fromSearch)
        {
            if (node == null)
            {
                return;
            }
            if (fromSearch)
            {
                SearchText = string.Empty;
            }
            ExpandPath(Roots.FirstOrDefault(), node);
            SelectedNode = node;
            node.IsSelected = true;
        }

        public void SelectSearchResult(EditorSearchResultViewModel result)
        {
            if (result != null)
            {
                SelectNode(result.Node, true);
            }
        }

        public void ClearSearch()
        {
            SearchText = string.Empty;
        }

        public void ValidateField(EditorFieldViewModel field)
        {
            if (field == null)
            {
                return;
            }
            field.Validate(currentValues);
            foreach (var section in Sections)
            {
                section.Refresh();
            }
        }

        public void ValidateVisibleFields()
        {
            foreach (var section in Sections)
            {
                foreach (var field in section.Fields)
                {
                    if (field.IsVisible)
                    {
                        field.Validate(currentValues);
                    }
                    else
                    {
                        field.ClearError();
                    }
                }
                section.Refresh();
            }
        }

        public void ToggleIssuePanel()
        {
            if (!HasIssues)
            {
                return;
            }
            IsIssuePanelOpen = !IsIssuePanelOpen;
        }

        /// <summary>把某个字段设为待聚焦目标，并通知窗口在布局完成后处理。</summary>
        public void RequestFieldFocus(string fieldName)
        {
            var field = Sections.SelectMany(section => section.Fields)
                .FirstOrDefault(item => string.Equals(item.Name, fieldName, StringComparison.Ordinal));
            if (field == null)
            {
                return;
            }
            var handler = FieldFocusRequested;
            if (handler != null)
            {
                handler(field, true);
            }
        }

        /// <summary>
        /// 遍历全文档做字段级轻校验。只校验可见字段，避免条件不满足的必填项被误判；
        /// 返回的问题带字段名，供问题面板定位。
        /// </summary>
        public IList<EditorIssueViewModel> ValidateDocument()
        {
            var issues = new List<EditorIssueViewModel>();
            foreach (var node in document.Nodes)
            {
                var fields = document.FieldsOf(node);
                if (fields.Count == 0)
                {
                    continue;
                }
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in fields)
                {
                    values[field.Name] = document.GetValue(node, field);
                }
                foreach (var field in fields)
                {
                    if (!field.IsVisibleFor(values))
                    {
                        continue;
                    }
                    var error = ConfigFieldValidator.Validate(field, values[field.Name], values);
                    if (error != null)
                    {
                        issues.Add(new EditorIssueViewModel(
                            BuildIssueText(node, field, error), node, field.Name));
                    }
                }
            }
            return issues;
        }

        private static string BuildIssueText(EditorNode node, ConfigField field, string error)
        {
            var name = node.Element == null ? null : (string)node.Element.Attribute("name");
            var label = string.IsNullOrEmpty(name) ? node.DisplayName : name;
            return label + " › " + field.Label + "：" + error;
        }

        /// <summary>定位问题对应的字段：先按字段名，再按消息里的名称/标签，最后退回首个出错字段。</summary>
        private void LocateIssueField(EditorIssueViewModel issue)
        {
            var fields = Sections.SelectMany(section => section.Fields).ToList();
            foreach (var field in fields)
            {
                field.IsHighlighted = false;
            }

            EditorFieldViewModel target = null;
            if (!string.IsNullOrEmpty(issue.FieldName))
            {
                target = fields.FirstOrDefault(field =>
                    string.Equals(field.Name, issue.FieldName, StringComparison.Ordinal));
            }
            if (target == null && !string.IsNullOrEmpty(issue.Text))
            {
                target = fields.FirstOrDefault(field =>
                    issue.Text.IndexOf(field.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (field.LabelText.Length > 0 &&
                     issue.Text.IndexOf(field.LabelText.TrimEnd(' ', '*'), StringComparison.OrdinalIgnoreCase) >= 0));
            }
            if (target == null)
            {
                // 当前节点可能还没显示错误，先跑一遍再取第一个。
                ValidateVisibleFields();
                target = fields.FirstOrDefault(field => field.HasError);
            }
            if (target == null)
            {
                return;
            }

            target.IsHighlighted = true;
            var handler = FieldFocusRequested;
            if (handler != null)
            {
                handler(target, false);
            }
        }

        private void AttachDocument(ConfigurationDocument attached)
        {
            document = attached;
            document.Note = note;
            dirtyElements.Clear();
            currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Rebuild(null);
            FilePathText = document.TargetPath ?? "（未保存的新配置）";
            IsDirty = document.IsDirty;
            RefreshModeBadge();
            RefreshSummary();
        }

        private static EditorNode AncestorOfKind(EditorNode node, EditorNodeKind kind)
        {
            var current = node;
            while (current != null)
            {
                if (current.Kind == kind)
                {
                    return current;
                }
                current = current.Parent;
            }
            return null;
        }

        private void Rebuild(XElement selectElement)
        {
            var previous = selectElement ?? (selectedNode == null ? null : selectedNode.Node.Element);
            Roots.Clear();
            var root = BuildViewModel(document.Root);
            Roots.Add(root);
            var target = FindViewModel(root, previous) ?? root;
            selectedNode = null;
            SelectedNode = target;
            target.IsSelected = true;
            IsDirty = document.IsDirty;
            if (IsDirty)
            {
                SavedAtText = "未保存";
            }
            RefreshModeBadge();
            RefreshSummary();
        }

        private EditorNodeViewModel BuildViewModel(EditorNode node)
        {
            var viewModel = new EditorNodeViewModel(node)
            {
                IsDirty = dirtyElements.Contains(node.Element)
            };
            var stepIndex = 0;
            foreach (var child in node.Children)
            {
                var childViewModel = BuildViewModel(child);
                if (child.Kind == EditorNodeKind.Step)
                {
                    stepIndex++;
                    childViewModel.Sequence = stepIndex;
                }
                viewModel.Children.Add(childViewModel);
            }
            return viewModel;
        }

        private static EditorNodeViewModel FindViewModel(EditorNodeViewModel current, EditorNode node)
        {
            if (current == null)
            {
                return null;
            }
            if (node != null && ReferenceEquals(current.Node.Element, node.Element))
            {
                return current;
            }
            foreach (var child in current.Children)
            {
                var found = FindViewModel(child, node);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static EditorNodeViewModel FindViewModel(EditorNodeViewModel current, XElement element)
        {
            if (current == null)
            {
                return null;
            }
            if (element != null && ReferenceEquals(current.Node.Element, element))
            {
                return current;
            }
            foreach (var child in current.Children)
            {
                var found = FindViewModel(child, element);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private void RefreshFields()
        {
            Sections.Clear();
            currentValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            OnPropertyChanged("HasSelection");
            if (selectedNode == null)
            {
                return;
            }

            var fields = document.FieldsOf(selectedNode.Node);
            foreach (var field in fields)
            {
                currentValues[field.Name] = document.GetValue(selectedNode.Node, field);
            }

            EditorFieldSectionViewModel section = null;
            var capturedNode = selectedNode.Node;
            foreach (var field in fields)
            {
                if (section == null || !string.Equals(section.Title, field.Section, StringComparison.Ordinal))
                {
                    section = new EditorFieldSectionViewModel(field.Section);
                    Sections.Add(section);
                }
                var capturedField = field;
                var pickerOptions = field.HasPicker
                    ? sourcePathCatalog.List(dataRoot, field.PathPrefix, field.Extensions, field.PickDirectoriesOnly)
                    : null;
                var fieldViewModel = new EditorFieldViewModel(
                    capturedField,
                    document.GetValue(capturedNode, capturedField),
                    changed => OnFieldChanged(changed, capturedNode, capturedField),
                    pickerOptions);
                fieldViewModel.RefreshVisibility(currentValues);
                section.Fields.Add(fieldViewModel);
            }
            foreach (var item in Sections)
            {
                item.Refresh();
            }
        }

        private void OnFieldChanged(EditorFieldViewModel changed, EditorNode node, ConfigField field)
        {
            document.SetValue(node, field, changed.Value);
            IsDirty = document.IsDirty;
            currentValues[field.Name] = changed.Value;
            if (node.Element != null)
            {
                dirtyElements.Add(node.Element);
            }

            var viewModel = FindViewModel(Roots.FirstOrDefault(), node);
            if (viewModel != null)
            {
                viewModel.IsDirty = true;
            }
            SavedAtText = "未保存";

            if (field.Name == "name" || field.Name == "key" || field.Name == "enabled")
            {
                if (viewModel != null)
                {
                    viewModel.RefreshName();
                }
                OnPropertyChanged("SelectedTitle");
                OnPropertyChanged("SelectedTypeText");
                OnPropertyChanged("BreadcrumbText");
            }

            RefreshVisibility();
            ValidateErroredFields();
            if (field.Name == "mode")
            {
                RefreshModeBadge();
            }
            RefreshSummary();
        }

        private void RefreshVisibility()
        {
            foreach (var section in Sections)
            {
                foreach (var field in section.Fields)
                {
                    field.RefreshVisibility(currentValues);
                }
                section.Refresh();
            }
        }

        private void ValidateErroredFields()
        {
            foreach (var section in Sections)
            {
                foreach (var field in section.Fields)
                {
                    if (field.HasError || field.IsIrrelevant)
                    {
                        field.Validate(currentValues);
                    }
                }
                section.Refresh();
            }
        }

        private void RefreshSelectionState()
        {
            OnPropertyChanged("SelectedTitle");
            OnPropertyChanged("SelectedTypeText");
            OnPropertyChanged("BreadcrumbText");
            OnPropertyChanged("HasSelection");
            OnPropertyChanged("CanAddTask");
            OnPropertyChanged("CanAddStep");
            OnPropertyChanged("CanRemove");
            OnPropertyChanged("CanMoveUp");
            OnPropertyChanged("CanMoveDown");
        }

        private void RefreshModeBadge()
        {
            var modeField = document.FieldsOf(document.Root).FirstOrDefault(field => field.Name == "mode");
            var mode = modeField == null ? "preview" : document.GetValue(document.Root, modeField);
            IsExecuteMode = string.Equals((mode ?? string.Empty).Trim(), "execute", StringComparison.OrdinalIgnoreCase);
            ModeBadgeText = IsExecuteMode ? "execute · 将真实执行" : "preview · 只预检";
        }

        private void RefreshSummary()
        {
            var groups = document.Root.Children.Count(node => node.Kind == EditorNodeKind.Group);
            var tasks = document.Root.Children.Sum(group => group.Children.Count(node => node.Kind == EditorNodeKind.Task));
            var steps = document.Root.Children
                .SelectMany(group => group.Children)
                .Where(task => task.Kind == EditorNodeKind.Task)
                .Sum(task => task.Children.Count(node => node.Kind == EditorNodeKind.Step));
            SummaryText = groups + " 分组 · " + tasks + " 任务 · " + steps + " 步骤";
        }

        private void RefreshSearch()
        {
            SearchResults.Clear();
            var query = (searchText ?? string.Empty).Trim();
            if (query.Length > 0)
            {
                foreach (var node in FlattenViewModel(Roots.FirstOrDefault()))
                {
                    if (Matches(node, query))
                    {
                        SearchResults.Add(new EditorSearchResultViewModel(node, PathOf(node)));
                    }
                }
            }
            OnPropertyChanged("IsSearching");
            OnPropertyChanged("ShowTree");
        }

        private static IEnumerable<EditorNodeViewModel> FlattenViewModel(EditorNodeViewModel current)
        {
            if (current == null)
            {
                yield break;
            }
            yield return current;
            foreach (var child in current.Children)
            {
                foreach (var descendant in FlattenViewModel(child))
                {
                    yield return descendant;
                }
            }
        }

        private static bool Matches(EditorNodeViewModel node, string query)
        {
            if (Contains(node.NameText, query) || Contains(node.TypeText, query) ||
                Contains(node.Node.ElementName, query) || Contains(node.DisplayName, query))
            {
                return true;
            }
            var key = node.Node.Element == null ? null : (string)node.Node.Element.Attribute("key");
            return Contains(key, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string PathOf(EditorNodeViewModel node)
        {
            var names = new List<string>();
            var parent = node.Node.Parent;
            while (parent != null)
            {
                if (parent.Kind != EditorNodeKind.Config)
                {
                    names.Insert(0, NodeName(parent));
                }
                parent = parent.Parent;
            }
            return string.Join(" › ", names);
        }

        private static string NodeName(EditorNode node)
        {
            var name = node.Element == null ? null : (string)node.Element.Attribute("name");
            return string.IsNullOrEmpty(name) ? node.DisplayName : name;
        }

        private static bool ExpandPath(EditorNodeViewModel current, EditorNodeViewModel target)
        {
            if (current == null)
            {
                return false;
            }
            if (ReferenceEquals(current, target))
            {
                return true;
            }
            foreach (var child in current.Children)
            {
                if (ExpandPath(child, target))
                {
                    current.IsExpanded = true;
                    return true;
                }
            }
            return false;
        }

        private void ClearFieldErrors()
        {
            foreach (var section in Sections)
            {
                foreach (var field in section.Fields)
                {
                    field.ClearError();
                }
            }
        }

        private void ClearDirtyFlags()
        {
            dirtyElements.Clear();
            foreach (var node in FlattenViewModel(Roots.FirstOrDefault()))
            {
                node.IsDirty = false;
            }
        }

        private void RemoveDirtyElements(XElement element)
        {
            if (element == null)
            {
                return;
            }
            dirtyElements.Remove(element);
            foreach (var child in element.Elements())
            {
                RemoveDirtyElements(child);
            }
        }

        private void FillEditorIssues(IEnumerable<EditorIssueViewModel> issues)
        {
            Issues.Clear();
            foreach (var issue in issues)
            {
                Issues.Add(issue);
            }
            NotifyIssuesChanged();
        }

        private void FillLoaderIssues(IEnumerable<ConfigIssue> issues)
        {
            Issues.Clear();
            foreach (var issue in issues)
            {
                Issues.Add(new EditorIssueViewModel(issue, document.FindNodeByLine(issue.Line)));
            }
            NotifyIssuesChanged();
        }

        private void ClearIssues()
        {
            Issues.Clear();
            NotifyIssuesChanged();
        }

        private void NotifyIssuesChanged()
        {
            OnPropertyChanged("HasIssues");
            OnPropertyChanged("IssueCountText");
            OnPropertyChanged("IssuesTitle");
            OnPropertyChanged("ShowIssuePanel");
        }

        private void SelectFirstIssue()
        {
            if (Issues.Count == 0)
            {
                return;
            }
            selectedIssue = null;
            SelectedIssue = Issues[0];
        }
    }

    /// <summary>步骤类型菜单项：类型用于生成 XML 元素，标签用于显示。</summary>
    public sealed class EditorStepTypeOption
    {
        public EditorStepTypeOption(string type, string label)
        {
            Type = type;
            Label = label;
        }

        public string Type { get; private set; }
        public string Label { get; private set; }
    }
}
