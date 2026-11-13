using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Resources;
using IPCDeployment.ViewModels;
using Wpf.Ui.Controls;

namespace IPCDeployment
{
    public partial class MainWindow : FluentWindow
    {
        private readonly DeploymentViewModel viewModel;
        private bool closeConfirmed;
        private ConfigurationEditorWindow editorWindow;

        public MainWindow()
        {
            InitializeComponent();
            ApplyVersionTitle();
            // ICO 多帧时 WPF 默认取 16x16 帧，任务栏与 Alt-Tab 会模糊，这里显式取 32x32 帧。
            Icon = LoadWindowIcon();
            viewModel = new DeploymentViewModel(AppDomain.CurrentDomain.BaseDirectory);
            DataContext = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void ApplyVersionTitle()
        {
            // 版本号来自程序集产品版本（csproj 的 <Version>），不在此处另写字面量。
            string version = FileVersionInfo.GetVersionInfo(typeof(MainWindow).Assembly.Location).ProductVersion;
            Title = string.IsNullOrWhiteSpace(version) ? "工控机部署工具" : "工控机部署工具 " + version.Trim();
            MainTitleBar.Title = Title;
        }

        private static ImageSource LoadWindowIcon()
        {
            StreamResourceInfo resource =
                Application.GetResourceStream(new Uri("pack://application:,,,/logo.ico", UriKind.Absolute));
            IconBitmapDecoder decoder =
                new IconBitmapDecoder(resource.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            foreach (BitmapFrame frame in decoder.Frames)
            {
                if (frame.PixelWidth == 32)
                {
                    return frame;
                }
            }

            return decoder.Frames[decoder.Frames.Count - 1];
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

        private async void ValidateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            await viewModel.ReloadAsync();
        }

        private void ConfigCombo_DropDownOpened(object sender, EventArgs e)
        {
            viewModel.RefreshConfigFiles();
        }

        private void EditConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (editorWindow != null)
            {
                editorWindow.Activate();
                return;
            }

            // 非模态打开：编辑器与主界面共享同一份配置，保存后主界面立即重载。
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var window = new ConfigurationEditorWindow(
                viewModel.ConfigPath,
                Path.Combine(baseDirectory, "config"),
                Path.Combine(baseDirectory, "data"),
                viewModel.WriteNote,
                ReloadAfterEditorSave)
            {
                Owner = this
            };
            editorWindow = window;
            viewModel.IsEditorOpen = true;
            window.Closed += (s, args) =>
            {
                viewModel.IsEditorOpen = false;
                editorWindow = null;
            };
            window.Show();
        }

        private async void ReloadAfterEditorSave(string savedPath)
        {
            viewModel.RefreshConfigFiles();
            // 编辑器可能已经切到另一份配置，主界面要跟到刚保存的那份。
            if (string.IsNullOrEmpty(savedPath) || !await viewModel.SelectConfigAsync(savedPath))
            {
                await viewModel.ReloadAsync();
            }
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
