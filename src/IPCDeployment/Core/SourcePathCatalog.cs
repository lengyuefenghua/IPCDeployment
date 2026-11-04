using System;
using System.Collections.Generic;
using System.IO;

namespace IPCDeployment.Core
{
    /// <summary>
    /// 为配置编辑器的路径字段列出 data 分区下现有的文件与目录，
    /// 让用户从已有条目里选，而不是凭记忆手写相对路径。
    /// </summary>
    public sealed class SourcePathCatalog
    {
        /// <summary>
        /// 返回 <paramref name="prefix"/>（如 software/）下的候选相对路径，正斜杠分隔；
        /// 目录项以 / 结尾。目录缺失或读取失败时返回空列表，不影响编辑。
        /// </summary>
        public IList<string> List(string dataRoot, string prefix, IList<string> extensions, bool directoriesOnly)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(dataRoot) || string.IsNullOrWhiteSpace(prefix))
            {
                return result;
            }

            var normalizedPrefix = prefix.Replace('\\', '/').TrimEnd('/');
            var root = Path.Combine(dataRoot, normalizedPrefix.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
            {
                return result;
            }

            try
            {
                Collect(root, normalizedPrefix + "/", extensions, directoriesOnly, result);
            }
            catch (Exception)
            {
                // 枚举失败时保留已收集到的条目，编辑器仍可手输。
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static void Collect(string directory, string relativePrefix, IList<string> extensions,
            bool directoriesOnly, List<string> result)
        {
            foreach (var childDirectory in Directory.GetDirectories(directory))
            {
                result.Add(relativePrefix + Path.GetFileName(childDirectory) + "/");
                Collect(childDirectory, relativePrefix + Path.GetFileName(childDirectory) + "/", extensions,
                    directoriesOnly, result);
            }

            if (directoriesOnly)
            {
                return;
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (!MatchesExtensions(name, extensions))
                {
                    continue;
                }
                result.Add(relativePrefix + name);
            }
        }

        private static bool MatchesExtensions(string fileName, IList<string> extensions)
        {
            if (extensions == null || extensions.Count == 0)
            {
                return true;
            }
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            foreach (var allowed in extensions)
            {
                if (string.Equals(allowed, extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
