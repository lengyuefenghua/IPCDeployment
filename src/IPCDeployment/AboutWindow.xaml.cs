using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace IPCDeployment
{
    public partial class AboutWindow : FluentWindow
    {
        public AboutWindow()
        {
            InitializeComponent();

            AppIcon.Source = LoadAppIcon();
            var version = FileVersionInfo.GetVersionInfo(typeof(AboutWindow).Assembly.Location).ProductVersion;
            VersionText.Text = "版本 " + (string.IsNullOrWhiteSpace(version) ? "1.0.0" : version.Trim());
            InfoText.Text =
                "发布形态：便携版（免安装）" + Environment.NewLine +
                "开发者：汇川技术-智能机器人事业部-解决方案中心-系统集成部" + Environment.NewLine +
                "授权：MIT License（含第三方组件许可）";
        }

        private static ImageSource LoadAppIcon()
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/logo.ico"));
            using (var stream = resource.Stream)
            {
                var decoder = new IconBitmapDecoder(
                    stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapFrame best = null;
                foreach (var frame in decoder.Frames)
                {
                    if (best == null || frame.PixelWidth > best.PixelWidth)
                    {
                        best = frame;
                    }
                }
                return best;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
