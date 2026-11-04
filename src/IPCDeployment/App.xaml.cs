using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace IPCDeployment
{
    public partial class App : Application
    {
        static App()
        {
            // 依赖已内嵌在 exe 里，磁盘上没有对应 DLL；必须在任何 Wpf.Ui 类型被 JIT/使用之前注册，
            // 否则 App.xaml 的资源字典或 OnStartup 里的类型引用会先抛 FileNotFoundException。
            EmbeddedAssemblyLoader.Attach();
        }

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
                    Path.Combine(Path.GetTempPath(), "IPCDeployment-startup-crash.log"),
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
