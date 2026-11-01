using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using VisionIPC.Deployment.App.ViewModels;
using Wpf.Ui.Controls;

namespace VisionIPC.Deployment.App
{
    public partial class MainWindow : FluentWindow
    {
        private readonly DeploymentViewModel viewModel;
        private bool closeConfirmed;

        public MainWindow()
        {
            InitializeComponent();
            viewModel = new DeploymentViewModel(AppDomain.CurrentDomain.BaseDirectory);
            DataContext = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await viewModel.LoadAsync();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (string.Equals(eventArgs.PropertyName, "PageLogText", StringComparison.Ordinal) &&
                LogList != null && LogList.IsLoaded && LogList.IsVisible)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (LogList.IsVisible && LogList.Items.Count > 0)
                    {
                        LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                    }
                }));
            }
        }

        private async void SelectConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var initialDirectory = Path.GetDirectoryName(viewModel.ConfigPathText);
            if (string.IsNullOrEmpty(initialDirectory) || !Directory.Exists(initialDirectory))
            {
                initialDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "XML 配置 (*.xml)|*.xml|所有文件 (*.*)|*.*",
                InitialDirectory = initialDirectory,
                FileName = Path.GetFileName(viewModel.ConfigPathText),
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true)
            {
                await viewModel.SelectConfigAsync(dialog.FileName);
            }
        }

        private async void ValidateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            await viewModel.ReloadAsync();
        }

        private async void DefaultButton_Click(object sender, RoutedEventArgs e)
        {
            await viewModel.LoadDefaultAsync();
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!viewModel.CanStart)
            {
                return;
            }

            // 只有用户在应用中确认后，才进入 execute 队列。
            var result = await new Wpf.Ui.Controls.MessageBox
            {
                Owner = this,
                Title = "确认执行部署",
                Content = viewModel.BuildConfirmationText(),
                PrimaryButtonText = "确认执行",
                SecondaryButtonText = "取消",
                IsCloseButtonEnabled = false,
                PrimaryButtonAppearance = ControlAppearance.Danger,
                SecondaryButtonAppearance = ControlAppearance.Secondary
            }.ShowDialogAsync(false, CancellationToken.None);

            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                await viewModel.StartAsync();
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (!viewModel.IsRunning)
            {
                return;
            }

            var result = await new Wpf.Ui.Controls.MessageBox
            {
                Owner = this,
                Title = "停止部署",
                Content = "停止当前步骤并结束当前任务？\n\n剩余步骤将标记为未执行；已完成的更改不会自动回滚，可能留下部分更改。",
                PrimaryButtonText = "停止部署",
                SecondaryButtonText = "继续运行",
                IsCloseButtonEnabled = false,
                PrimaryButtonAppearance = ControlAppearance.Danger,
                SecondaryButtonAppearance = ControlAppearance.Secondary
            }.ShowDialogAsync(false, CancellationToken.None);

            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                viewModel.Cancel();
            }
        }

        private async void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            if (!viewModel.CanOpenLog || string.IsNullOrEmpty(viewModel.LogPath) || !File.Exists(viewModel.LogPath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = viewModel.LogPath,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                await new Wpf.Ui.Controls.MessageBox
                {
                    Owner = this,
                    Title = "打开日志失败",
                    Content = exception.Message,
                    PrimaryButtonText = "确定",
                    IsCloseButtonEnabled = false,
                    PrimaryButtonAppearance = ControlAppearance.Danger
                }.ShowDialogAsync(false, CancellationToken.None);
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void MainWindow_Closing(object sender, CancelEventArgs eventArgs)
        {
            if (closeConfirmed || !viewModel.IsRunning)
            {
                return;
            }

            eventArgs.Cancel = true;
            var result = await new Wpf.Ui.Controls.MessageBox
            {
                Owner = this,
                Title = "部署正在运行",
                Content = "关闭窗口会停止当前步骤并结束当前任务，剩余步骤将标记为未执行；可能留下部分更改。\n\n确定停止并退出？",
                PrimaryButtonText = "停止并退出",
                SecondaryButtonText = "继续运行",
                IsCloseButtonEnabled = false,
                PrimaryButtonAppearance = ControlAppearance.Danger,
                SecondaryButtonAppearance = ControlAppearance.Secondary
            }.ShowDialogAsync(false, CancellationToken.None);

            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                closeConfirmed = true;
                viewModel.Cancel();
                Close();
            }
        }
    }
}
