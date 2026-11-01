using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Linq;
using IPCDeployment.Core;

namespace IPCDeployment.ViewModels
{
    /// <summary>
    /// 配置编辑器的界面状态：结构树、字段表单、校验错误与保存提示。
    /// XML 读写与校验全部委托给 <see cref="ConfigurationDocument"/>。
    /// </summary>
    public sealed class EditorViewModel : ObservableObject
    {
        private ConfigurationDocument document;
        private EditorNodeViewModel selectedNode;
        private EditorIssueViewModel selectedIssue;
        private string filePathText = "（未保存的新配置）";
        private string statusText = "就绪";
        private string newStepType = "Delay";
        private bool isDirty;
        private Action<string, string> note;

        public EditorViewModel()
        {
            document = ConfigurationDocument.CreateNew();
            AttachDocument(document);
        }

        public ObservableCollection<EditorNodeViewModel> Roots { get; } = new ObservableCollection<EditorNodeViewModel>();
        public ObservableCollection<EditorFieldViewModel> Fields { get; } = new ObservableCollection<EditorFieldViewModel>();
        public ObservableCollection<EditorIssueViewModel> Issues { get; } = new ObservableCollection<EditorIssueViewModel>();

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

        public IList<string> StepTypes
        {
            get { return ConfigFieldSchema.StepTypes; }
        }

        public string NewStepType
        {
            get { return newStepType; }
            set { SetProperty(ref newStepType, value); }
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

        public bool ShowIssues
        {
            get { return Issues.Count > 0; }
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
                    target.IsSelected = true;
                }
            }
        }

        public string SelectedTypeText
        {
            get
            {
                if (selectedNode == null)
                {
                    return string.Empty;
                }
                switch (selectedNode.Node.Kind)
                {
                    case EditorNodeKind.Config:
                        return "配置";
                    case EditorNodeKind.Group:
                        return "分组";
                    case EditorNodeKind.Task:
                        return "任务";
                    default:
                        return "步骤 · " + selectedNode.Node.ElementName;
                }
            }
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
            AttachDocument(ConfigurationDocument.CreateNew());
            FilePathText = "（未保存的新配置）";
            StatusText = "已新建配置骨架";
        }

        public EditorOpenResult Open(string path)
        {
            var result = ConfigurationDocument.Open(path);
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
            AttachDocument(result.Document);
            FilePathText = result.Document.TargetPath;
            StatusText = "已打开 " + result.Document.TargetPath;
            return result;
        }

        public EditorSaveResult Save(string path, bool overwriteExternal)
        {
            ClearIssues();
            var result = document.Save(path, overwriteExternal);
            IsDirty = document.IsDirty;
            if (result.Status == EditorSaveStatus.ValidationFailed)
            {
                FillIssues(result.Issues);
                StatusText = result.Message;
                return result;
            }
            if (result.Status == EditorSaveStatus.Saved)
            {
                FilePathText = document.TargetPath;
                StatusText = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
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
            Rebuild(node == null ? null : node.Element);
            StatusText = "已新增分组";
        }

        public void AddTask()
        {
            var group = selectedNode == null ? null : AncestorOfKind(selectedNode.Node, EditorNodeKind.Group);
            if (group == null)
            {
                return;
            }
            var node = document.AddTask(group);
            Rebuild(node == null ? null : node.Element);
            StatusText = "已新增任务";
        }

        public void AddStep()
        {
            if (selectedNode == null || selectedNode.Node.Kind != EditorNodeKind.Task)
            {
                return;
            }
            var node = document.AddStep(selectedNode.Node, NewStepType);
            Rebuild(node == null ? null : node.Element);
            StatusText = "已新增步骤";
        }

        public void RemoveSelected()
        {
            if (selectedNode == null || selectedNode.Node.Kind == EditorNodeKind.Config)
            {
                return;
            }
            var parentElement = selectedNode.Node.Element.Parent;
            document.Remove(selectedNode.Node);
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
                Rebuild(element);
                StatusText = "已调整顺序";
            }
        }

        private void AttachDocument(ConfigurationDocument attached)
        {
            document = attached;
            document.Note = note;
            Rebuild(null);
            FilePathText = document.TargetPath ?? "（未保存的新配置）";
            IsDirty = document.IsDirty;
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
        }

        private static EditorNodeViewModel BuildViewModel(EditorNode node)
        {
            var viewModel = new EditorNodeViewModel(node);
            foreach (var child in node.Children)
            {
                viewModel.Children.Add(BuildViewModel(child));
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
            Fields.Clear();
            if (selectedNode == null)
            {
                OnPropertyChanged("SelectedTypeText");
                return;
            }
            foreach (var field in document.FieldsOf(selectedNode.Node))
            {
                var capturedField = field;
                var capturedNode = selectedNode.Node;
                Fields.Add(new EditorFieldViewModel(capturedField, document.GetValue(capturedNode, capturedField),
                    capturedField.ReadOnly ? null : new Action<EditorFieldViewModel>(
                        changed => OnFieldChanged(changed, capturedNode, capturedField))));
            }
            OnPropertyChanged("SelectedTypeText");
        }

        private void OnFieldChanged(EditorFieldViewModel changed, EditorNode node, ConfigField field)
        {
            document.SetValue(node, field, changed.Value);
            IsDirty = document.IsDirty;
            if (field.Name == "name" || field.Name == "key")
            {
                var viewModel = selectedNode != null && ReferenceEquals(selectedNode.Node.Element, node.Element)
                    ? selectedNode
                    : null;
                if (viewModel != null)
                {
                    viewModel.RefreshName();
                }
            }
        }

        private void RefreshSelectionState()
        {
            OnPropertyChanged("SelectedTypeText");
            OnPropertyChanged("CanAddTask");
            OnPropertyChanged("CanAddStep");
            OnPropertyChanged("CanRemove");
            OnPropertyChanged("CanMoveUp");
            OnPropertyChanged("CanMoveDown");
        }

        private void FillIssues(IEnumerable<ConfigIssue> issues)
        {
            Issues.Clear();
            foreach (var issue in issues)
            {
                Issues.Add(new EditorIssueViewModel(issue, document.FindNodeByLine(issue.Line)));
            }
            OnPropertyChanged("ShowIssues");
            OnPropertyChanged("IssuesTitle");
        }

        private void ClearIssues()
        {
            Issues.Clear();
            OnPropertyChanged("ShowIssues");
            OnPropertyChanged("IssuesTitle");
        }
    }
}
