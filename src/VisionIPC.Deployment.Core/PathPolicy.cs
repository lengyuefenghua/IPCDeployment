using System;
using System.IO;

namespace VisionIPC.Deployment.Core
{
    public sealed class PathPolicy
    {
        private static readonly char[] Separators =
        {
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        };

        public string ResolveSource(string dataRoot, string relativePath, DataArea area)
        {
            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                throw new InvalidOperationException("数据目录不能为空。");
            }
            EnsureRelativeSafe(relativePath);

            var folder = area == DataArea.Software
                ? "software"
                : area == DataArea.Files ? "files" : "commands";
            // 源路径必须显式落在 data/<area>/ 下，防止目录逃逸。
            var dataRootFull = Path.GetFullPath(dataRoot);
            var areaRoot = Path.GetFullPath(Path.Combine(dataRootFull, folder));
            var candidate = Path.GetFullPath(Path.Combine(dataRootFull, relativePath));
            if (!IsUnderRoot(candidate, areaRoot))
            {
                throw new InvalidOperationException("源路径必须位于 data/" + folder + " 下。");
            }
            return candidate;
        }

        public string ResolveDestination(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("目标路径不能为空。");
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!Path.IsPathRooted(expanded))
            {
                throw new InvalidOperationException("目标路径必须是绝对路径或环境变量展开后的绝对路径。");
            }

            var full = Path.GetFullPath(expanded);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) ||
                string.Equals(full.TrimEnd(Separators), root.TrimEnd(Separators), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("拒绝把盘符根作为目标路径。");
            }
            return full;
        }

        public string ResolveLogDirectory(string packageRoot, string logDirectory)
        {
            var value = string.IsNullOrWhiteSpace(logDirectory) ? "logs" : logDirectory.Trim();
            EnsureRelativeSafe(value);
            var root = Path.GetFullPath(packageRoot);
            var full = Path.GetFullPath(Path.Combine(root, value));
            if (!IsUnderRoot(full, root))
            {
                throw new InvalidOperationException("日志目录必须位于应用目录内。");
            }
            return full;
        }

        public bool IsSafeRelativePath(string path)
        {
            try
            {
                EnsureRelativeSafe(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool IsUnderRoot(string fullPath, string root)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Separators);
            var normalizedFull = Path.GetFullPath(fullPath).TrimEnd(Separators);
            return string.Equals(normalizedFull, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                normalizedFull.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureRelativeSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("源路径不能为空。");
            }
            if (Path.IsPathRooted(path))
            {
                throw new InvalidOperationException("源路径必须是 data/ 下的相对路径。");
            }
            if (path.IndexOf(':') >= 0 ||
                path.IndexOf('"') >= 0 ||
                path.IndexOf('&') >= 0 ||
                path.IndexOf('|') >= 0 ||
                path.IndexOf('*') >= 0 ||
                path.IndexOf('?') >= 0)
            {
                throw new InvalidOperationException("源路径包含不支持的字符。");
            }

            var segments = path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("源路径不能包含 ..。");
                }
            }
        }
    }
}
