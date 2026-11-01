using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IPCDeployment.Core;

namespace IPCDeployment.ViewModels
{
    public sealed class EditorNodeViewModel : ObservableObject
    {
        private bool isExpanded = true;
        private bool isSelected;

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

        public void RefreshName()
        {
            OnPropertyChanged("DisplayName");
        }
    }

    public sealed class EditorFieldViewModel : ObservableObject
    {
        private readonly Action<EditorFieldViewModel> changed;
        private string value;

        public EditorFieldViewModel(ConfigField field, string value, Action<EditorFieldViewModel> changed)
        {
            Field = field;
            this.value = value ?? string.Empty;
            this.changed = changed;
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
            get { return Field.Kind != FieldInputKind.Enum && Field.Kind != FieldInputKind.Boolean && Field.Kind != FieldInputKind.Multiline; }
        }

        public bool IsReadOnly
        {
            get { return Field.ReadOnly; }
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
    }

    public sealed class EditorIssueViewModel
    {
        public EditorIssueViewModel(ConfigIssue issue, EditorNode node)
        {
            Issue = issue;
            Node = node;
        }

        public ConfigIssue Issue { get; private set; }
        public EditorNode Node { get; private set; }

        public string Text
        {
            get { return Issue.ToDisplayString(); }
        }
    }
}
