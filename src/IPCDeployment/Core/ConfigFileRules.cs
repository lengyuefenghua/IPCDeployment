using System;
using System.IO;

namespace IPCDeployment.Core
{
    /// <summary>
    /// config 目录的共享判断规则：主窗口与配置编辑器必须用同一套，
    /// 避免示例文件过滤和“直属子文件”判定各写一份而走样。
    /// </summary>
    public static class ConfigFileRules
    {
        /// <summary>示例配置不参与选择、也不能被编辑。</summary>
        public static bool IsSampleConfig(string fileName)
        {
            return !string.IsNullOrEmpty(fileName) &&
                fileName.EndsWith(".sample.xml", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>只接受 config 目录直属的文件，子目录与外层路径一律拒绝。</summary>
        public static bool IsDirectChild(string configDirectory, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(configDirectory) || string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(fullPath));
                return directory != null && string.Equals(
                    directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    configDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
