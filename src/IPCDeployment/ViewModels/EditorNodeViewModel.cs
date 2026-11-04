using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using IPCDeployment.Core;

namespace IPCDeployment.ViewModels
{
    public sealed class EditorNodeViewModel : ObservableObject
    {
        private bool isExpanded = true;
        private bool isSelected;
        private bool isDirty;

        public EditorNodeViewModel(EditorNode node)
        {
            Node = node;
        }

        public EditorNode Node { get; private set; }
        public ObservableCollection<EditorNodeViewModel> Children { get; } = new ObservableCollection<EditorNodeViewModel>();

        public string DisplayName
        {
            get { return Node.DisplayName; }
        }

        public string NameText
        {
            get
            {
                var name = Node.Element == null ? null : (string)Node.Element.Attribute("name");
                return string.IsNullOrEmpty(name) ? Node.DisplayName : name;
            }
        }

        public string TypeText
        {
            get
            {
                switch (Node.Kind)
                {
                    case EditorNodeKind.Config:
                        return "配置";
                    case EditorNodeKind.Group:
                        return "分组";
                    case EditorNodeKind.Task:
                        return "任务";
                    default:
                        return Node.ElementName;
                }
            }
        }

        public string Glyph
        {
            get
            {
                switch (Node.Kind)
                {
                    case EditorNodeKind.Config:
                        return "⚙";
                    case EditorNodeKind.Group:
                        return "▤";
                    case EditorNodeKind.Task:
                        return "▸";
                    default:
                        return "·";
                }
            }
        }

        /// <summary>步骤在所属任务内的序号，从 1 开始；非步骤为 0。</summary>
        public int Sequence { get; set; }

        public string SequenceText
        {
            get { return Sequence > 0 ? Sequence + "." : string.Empty; }
        }

        public bool IsStep
        {
            get { return Node.Kind == EditorNodeKind.Step; }
        }

        public bool IsTask
        {
            get { return Node.Kind == EditorNodeKind.Task; }
        }

        /// <summary>自动生成的只读标识，只在属性区右上角灰显，不参与编辑。</summary>
        public string IdentityText
        {
            get
            {
                if (Node.Element == null)
                {
                    return string.Empty;
                }
                if (Node.Kind == EditorNodeKind.Step)
                {
                    var id = (string)Node.Element.Attribute("id");
                    return string.IsNullOrEmpty(id) ? string.Empty : "id: " + id;
                }
                if (Node.Kind == EditorNodeKind.Group || Node.Kind == EditorNodeKind.Task)
                {
                    var key = (string)Node.Element.Attribute("key");
                    return string.IsNullOrEmpty(key) ? string.Empty : "key: " + key;
                }
                return string.Empty;
            }
        }

        public string ChildCountText
        {
            get
            {
                if (Node.Kind == EditorNodeKind.Group)
                {
                    var tasks = Node.Children.Count(child => child.Kind == EditorNodeKind.Task);
                    var steps = Node.Children.Sum(task => task.Children.Count);
                    return tasks + "/" + steps;
                }
                if (Node.Kind == EditorNodeKind.Task)
                {
                    return Node.Children.Count.ToString();
                }
                return string.Empty;
            }
        }

        public bool HasChildCount
        {
            get { return ChildCountText.Length > 0; }
        }

        public bool IsExpanded
        {
            get { return isExpanded; }
            set { SetProperty(ref isExpanded, value); }
        }

        public bool IsSelected
        {
            get { return isSelected; }
            set { SetProperty(ref isSelected, value); }
        }

        public bool IsDirty
        {
            get { return isDirty; }
            set { SetProperty(ref isDirty, value); }
        }

        public void RefreshName()
        {
            OnPropertyChanged("DisplayName");
            OnPropertyChanged("NameText");
            OnPropertyChanged("TypeText");
        }
    }

    public sealed class EditorFieldViewModel : ObservableObject
    {
        private const string IrrelevantMessage = "当前操作类型不使用该字段，保存前请清空。";

        private readonly Action<EditorFieldViewModel> changed;
        private readonly IList<string> pickerOptions;
        private string value;
        private string errorMessage;
        private bool isVisible = true;
        private bool isIrrelevant;
        private bool isHighlighted;

        public EditorFieldViewModel(ConfigField field, string value, Action<EditorFieldViewModel> changed,
            IList<string> pickerOptions = null)
        {
            Field = field;
            this.value = value ?? string.Empty;
            this.changed = changed;
            this.pickerOptions = pickerOptions ?? new List<string>();
        }

        public ConfigField Field { get; private set; }

        public string Name
        {
            get { return Field.Name; }
        }

        public string LabelText
        {
            get { return Field.Required ? Field.Label + " *" : Field.Label; }
        }

        public string Hint
        {
            get { return Field.Hint; }
        }

        public bool ShowHint
        {
            get { return !string.IsNullOrEmpty(Field.Hint); }
        }

        public bool ShowCombo
        {
            get { return Field.Kind == FieldInputKind.Enum; }
        }

        public bool ShowCheck
        {
            get { return Field.Kind == FieldInputKind.Boolean; }
        }

        public bool ShowMultiline
        {
            get { return Field.Kind == FieldInputKind.Multiline; }
        }

        public bool ShowText
        {
            get
            {
                return !Field.HasPicker &&
                    Field.Kind != FieldInputKind.Enum &&
                    Field.Kind != FieldInputKind.Boolean &&
                    Field.Kind != FieldInputKind.Multiline;
            }
        }

        /// <summary>路径类字段用可编辑下拉展示，候选来自 data 下已有条目。</summary>
        public bool ShowPicker
        {
            get { return Field.HasPicker; }
        }

        public IList<string> PickerOptions
        {
            get { return pickerOptions; }
        }

        public IList<string> Options
        {
            get { return Field.Options; }
        }

        public string Value
        {
            get { return value; }
            set
            {
                var text = value ?? string.Empty;
                if (!SetProperty(ref this.value, text))
                {
                    return;
                }
                if (changed != null)
                {
                    changed(this);
                }
            }
        }

        public bool BoolValue
        {
            get { return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase); }
            set { Value = value ? "true" : "false"; }
        }

        public string ErrorMessage
        {
            get { return errorMessage; }
            private set
            {
                if (SetProperty(ref errorMessage, value))
                {
                    OnPropertyChanged("HasError");
                }
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(errorMessage); }
        }

        /// <summary>问题面板定位到该字段时短暂高亮，帮助用户找到出错位置。</summary>
        public bool IsHighlighted
        {
            get { return isHighlighted; }
            set { SetProperty(ref isHighlighted, value); }
        }

        /// <summary>条件不满足但已有值时仍显示，避免把用户填过的值藏起来。</summary>
        public bool IsVisible
        {
            get { return isVisible; }
            private set { SetProperty(ref isVisible, value); }
        }

        public bool IsIrrelevant
        {
            get { return isIrrelevant; }
            private set { SetProperty(ref isIrrelevant, value); }
        }

        public bool HasValue
        {
            get { return !string.IsNullOrWhiteSpace(value); }
        }

        public void RefreshVisibility(IDictionary<string, string> values)
        {
            var applies = Field.IsVisibleFor(values);
            IsIrrelevant = !applies && HasValue;
            IsVisible = applies || HasValue;
            if (IsIrrelevant)
            {
                ErrorMessage = IrrelevantMessage;
            }
            else if (string.Equals(errorMessage, IrrelevantMessage, StringComparison.Ordinal))
            {
                ErrorMessage = null;
            }
        }

        public void Validate(IDictionary<string, string> values)
        {
            if (IsIrrelevant)
            {
                ErrorMessage = IrrelevantMessage;
                return;
            }
            if (!IsVisible)
            {
                ErrorMessage = null;
                return;
            }
            ErrorMessage = ConfigFieldValidator.Validate(Field, value, values);
        }

        public void ClearError()
        {
            ErrorMessage = null;
        }
    }

    public sealed class EditorFieldSectionViewModel : ObservableObject
    {
        private bool isExpanded = true;

        public EditorFieldSectionViewModel(string title)
        {
            Title = title;
            Fields = new ObservableCollection<EditorFieldViewModel>();
        }

        public string Title { get; private set; }
        public ObservableCollection<EditorFieldViewModel> Fields { get; private set; }

        /// <summary>分区默认展开；折叠头可点击切换。</summary>
        public bool IsExpanded
        {
            get { return isExpanded; }
            set
            {
                if (SetProperty(ref isExpanded, value))
                {
                    OnPropertyChanged("ToggleGlyph");
                }
            }
        }

        public string ToggleGlyph
        {
            get { return isExpanded ? "▾" : "▸"; }
        }

        public string HeaderText
        {
            get { return Title + " · " + Fields.Count(field => field.IsVisible) + " 项"; }
        }

        public bool HasVisibleFields
        {
            get { return Fields.Any(field => field.IsVisible); }
        }

        public void Refresh()
        {
            OnPropertyChanged("HasVisibleFields");
            OnPropertyChanged("HeaderText");
        }
    }

    public sealed class EditorSearchResultViewModel
    {
        public EditorSearchResultViewModel(EditorNodeViewModel node, string path)
        {
            Node = node;
            Path = path;
        }

        public EditorNodeViewModel Node { get; private set; }
        public string Path { get; private set; }

        public string Title
        {
            get { return Node.NameText; }
        }

        public string TypeText
        {
            get { return Node.TypeText; }
        }

        public string PathText
        {
            get { return string.IsNullOrEmpty(Path) ? TypeText : Path + " › " + TypeText; }
        }
    }

    public sealed class EditorIssueViewModel
    {
        public EditorIssueViewModel(ConfigIssue issue, EditorNode node)
            : this(issue.ToDisplayString(), node, null)
        {
        }

        /// <summary>本地全文档校验产生的条目，直接带字段名便于定位。</summary>
        public EditorIssueViewModel(string text, EditorNode node, string fieldName)
        {
            Text = text;
            Node = node;
            FieldName = fieldName;
        }

        public EditorNode Node { get; private set; }
        public string FieldName { get; private set; }

        public string Text { get; private set; }
    }
}
