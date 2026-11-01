using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using IPCDeployment.Core;
using IPCDeployment.ViewModels;
using Wpf.Ui.Controls;
using UiMessageBox = Wpf.Ui.Controls.MessageBox;
using UiMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace IPCDeployment
{
    public partial class ConfigurationEditorWindow : FluentWindow
    {
        private readonly EditorViewModel viewModel;
        private readonly Action onSaved;
        private bool closeConfirmed;

        public ConfigurationEditorWindow(string configPath, Action<string, string> note, Action onSaved)
        {
            InitializeComponent();
            viewModel = new EditorViewModel { Note = note };
            DataContext = viewModel;
            this.onSaved = onSaved;
            Loaded += (sender, args) => LoadInitial(configPath);
            Closing += EditorWindow_Closing;
        }

        private async void LoadInitial(string configPath)
        {
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return;
            }
            var opened = viewModel.Open(configPath);
            if (opened.Succeeded)
            {
                return;
            }
            // 打开失败时不进入编辑，保留可用的新建骨架。
            await ShowIssuesAsync("无法打开配置文件", opened.Issues.Select(issue => issue.ToDisplayString()))
                .ConfigureAwait(true);
        }

        private void StructureTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> eventArgs)
        {
            var node = eventArgs.NewValue as EditorNodeViewModel;
            if (node != null)
            {
                viewModel.SelectedNode = node;
            }
        }

        private async void NewButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (!await ConfirmDiscardAsync().ConfigureAwait(true))
            {
                return;
            }
            viewModel.NewDocument();
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (!await ConfirmDiscardAsync().ConfigureAwait(true))
            {
                return;
            }

            var directory = viewModel.CurrentFilePath == null
                ? AppDomain.CurrentDomain.BaseDirectory
                : Path.GetDirectoryName(viewModel.CurrentFilePath);
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "XML 配置 (*.xml)|*.xml|所有文件 (*.*)|*.*",
                InitialDirectory = string.IsNullOrEmpty(directory) || !Directory.Exists(directory)
                    ? AppDomain.CurrentDomain.BaseDirectory
                    : directory,
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            var opened = viewModel.Open(dialog.FileName);
            if (!opened.Succeeded)
            {
                await ShowIssuesAsync("无法打开配置文件", opened.Issues.Select(issue => issue.ToDisplayString()))
                    .ConfigureAwait(true);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            await SaveAsync().ConfigureAwait(true);
        }

        private async void SaveAsButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            await SaveAsAsync().ConfigureAwait(true);
        }

        private void AddGroupButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            viewModel.AddGroup();
        }

        private void AddTaskButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            viewModel.AddTask();
        }

        private void AddStepButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            viewModel.AddStep();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (viewModel.SelectedNode == null || !viewModel.CanRemove)
            {
                return;
            }
            var result = await new UiMessageBox
            {
                Owner = this,
                Title = "确认删除",
                Content = "删除「" + viewModel.SelectedNode.DisplayName + "」会连带删除其下的任务和步骤。",
                PrimaryButtonText = "删除",
                SecondaryButtonText = "取消",
                IsCloseButtonEnabled = false,
                PrimaryButtonAppearance = ControlAppearance.Danger,
                SecondaryButtonAppearance = ControlAppearance.Secondary
            }.ShowDialogAsync(false, CancellationToken.None).ConfigureAwait(true);

            if (result == UiMessageBoxResult.Primary)
            {
                viewModel.RemoveSelected();
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            viewModel.MoveSelected(-1);
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            viewModel.MoveSelected(1);
        }

        private async void EditorWindow_Closing(object sender, CancelEventArgs eventArgs)
        {
            if (closeConfirmed || !viewModel.IsDirty)
            {
                return;
            }
            eventArgs.Cancel = true;
            if (!await ConfirmDiscardAsync().ConfigureAwait(true))
            {
                return;
            }
            closeConfirmed = true;
            Close();
        }

        private async System.Threading.Tasks.Task<bool> SaveAsync()
        {
            var path = viewModel.CurrentFilePath;
            if (string.IsNullOrEmpty(path))
            {
                return await SaveAsAsync().ConfigureAwait(true);
            }
            return await SaveToAsync(path).ConfigureAwait(true);
        }

        private async System.Threading.Tasks.Task<bool> SaveAsAsync()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "XML 配置 (*.xml)|*.xml",
                DefaultExt = ".xml",
                FileName = viewModel.CurrentFilePath == null ? "config.xml" : Path.GetFileName(viewModel.CurrentFilePath),
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }
            return await SaveToAsync(dialog.FileName).ConfigureAwait(true);
        }

        private async System.Threading.Tasks.Task<bool> SaveToAsync(string path)
        {
            var result = viewModel.Save(path, false);
            if (result.Status == EditorSaveStatus.ExternalModified)
            {
                var choice = await new UiMessageBox
                {
                    Owner = this,
                    Title = "文件已被外部修改",
                    Content = "文件在编辑器打开期间被其他程序改动。覆盖会丢失外部修改（旧内容保存在 .bak 中）。",
                    PrimaryButtonText = "覆盖",
                    SecondaryButtonText = "取消",
                    IsCloseButtonEnabled = false,
                    PrimaryButtonAppearance = ControlAppearance.Danger,
                    SecondaryButtonAppearance = ControlAppearance.Secondary
                }.ShowDialogAsync(false, CancellationToken.None).ConfigureAwait(true);
                if (choice != UiMessageBoxResult.Primary)
                {
                    return false;
                }
                result = viewModel.Save(path, true);
            }

            switch (result.Status)
            {
                case EditorSaveStatus.Saved:
                    if (onSaved != null)
                    {
                        onSaved();
                    }
                    return true;
                case EditorSaveStatus.ValidationFailed:
                    return false;
                default:
                    await ShowIssuesAsync("保存失败", new[] { result.Message }).ConfigureAwait(true);
                    return false;
            }
        }

        private async System.Threading.Tasks.Task<bool> ConfirmDiscardAsync()
        {
            if (!viewModel.IsDirty)
            {
                return true;
            }
            var dialog = new ContentDialog(DialogHost)
            {
                Title = "未保存的修改",
                Content = "当前配置有未保存的修改。",
                PrimaryButtonText = "保存",
                SecondaryButtonText = "不保存",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            var choice = await dialog.ShowAsync(CancellationToken.None).ConfigureAwait(true);

            if (choice == ContentDialogResult.Primary)
            {
                return await SaveAsync().ConfigureAwait(true);
            }
            return choice == ContentDialogResult.Secondary;
        }

        private async System.Threading.Tasks.Task ShowIssuesAsync(string title, System.Collections.Generic.IEnumerable<string> lines)
        {
            var content = string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrEmpty(line)).Take(12));
            await new UiMessageBox
            {
                Owner = this,
                Title = title,
                Content = string.IsNullOrEmpty(content) ? title : content,
                PrimaryButtonText = "知道了",
                IsCloseButtonEnabled = false
            }.ShowDialogAsync(false, CancellationToken.None).ConfigureAwait(true);
        }
    }
}
