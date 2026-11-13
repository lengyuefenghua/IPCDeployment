using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace IPCDeployment.ViewModels
{
    /// <summary>
    /// 配置方案下拉项：显示 Meta 名称，选择仍按文件名。
    /// 读取失败或 Meta 为空时回退为文件名，不影响加载与选择。
    /// </summary>
    public sealed class ConfigFileItem
    {
        private ConfigFileItem(string fileName, string displayName)
        {
            FileName = fileName;
            DisplayName = displayName;
        }

        public string FileName { get; private set; }
        public string DisplayName { get; private set; }

        public static ConfigFileItem Create(string fullPath)
        {
            var fileName = Path.GetFileName(fullPath);
            var metaName = ReadMetaName(fullPath);
            var displayName = string.IsNullOrWhiteSpace(metaName)
                ? fileName
                : metaName.Trim() + "（" + fileName + "）";
            return new ConfigFileItem(fileName, displayName);
        }

        public override string ToString()
        {
            return DisplayName;
        }

        /// <summary>
        /// 按文件名增量同步下拉项，避免 Clear() 让 ComboBox 在展开下拉时丢失选中项。
        /// 列表未变化时返回 false 且不触碰集合；仍有同名项时保留原实例。
        /// </summary>
        public static bool Sync(ObservableCollection<ConfigFileItem> target, IList<ConfigFileItem> desired)
        {
            var unchanged = target.Count == desired.Count;
            if (unchanged)
            {
                for (var index = 0; index < desired.Count; index++)
                {
                    if (!SameFile(target[index], desired[index]) ||
                        !string.Equals(target[index].DisplayName, desired[index].DisplayName, StringComparison.Ordinal))
                    {
                        unchanged = false;
                        break;
                    }
                }
            }
            if (unchanged)
            {
                return false;
            }

            for (var index = target.Count - 1; index >= 0; index--)
            {
                if (!desired.Any(item => SameFile(item, target[index])))
                {
                    target.RemoveAt(index);
                }
            }

            for (var index = 0; index < desired.Count; index++)
            {
                var existing = -1;
                for (var scan = 0; scan < target.Count; scan++)
                {
                    if (SameFile(target[scan], desired[index]))
                    {
                        existing = scan;
                        break;
                    }
                }
                if (existing < 0)
                {
                    target.Insert(index, desired[index]);
                    continue;
                }
                if (existing != index)
                {
                    target.Move(existing, index);
                }
                if (!string.Equals(target[index].DisplayName, desired[index].DisplayName, StringComparison.Ordinal))
                {
                    target[index] = desired[index];
                }
            }
            return true;
        }

        private static bool SameFile(ConfigFileItem left, ConfigFileItem right)
        {
            return string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>只取根元素下的 Meta@name；配置损坏或读到空名时静默回退。</summary>
        private static string ReadMetaName(string fullPath)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true
                };
                using (var reader = XmlReader.Create(fullPath, settings))
                {
                    var document = XDocument.Load(reader);
                    var root = document.Root;
                    if (root == null || root.Name.LocalName != "Deployment")
                    {
                        return null;
                    }
                    var meta = root.Element("Meta");
                    return meta == null ? null : (string)meta.Attribute("name");
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
