using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace VisionIPC.Deployment.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // WPF-UI 的控件模板使用 SystemAccentColorPrimary 等资源，必须在窗口创建前应用系统重音色。
            ApplicationAccentColorManager.ApplySystemAccent();
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "VisionIPC-Deployment-startup-crash.log"),
                    e.Exception == null ? "unknown exception" : e.Exception.ToString());
            }
            catch (Exception)
            {
            }

            e.Handled = true;
            Shutdown(1);
        }
    }
}
