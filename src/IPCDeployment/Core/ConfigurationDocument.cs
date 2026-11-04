using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace IPCDeployment.Core
{
    public enum EditorNodeKind
    {
        Config,
        Group,
        Task,
        Step
    }

    public enum EditorSaveStatus
    {
        Saved,
        ValidationFailed,
        ExternalModified,
        IoFailed
    }

    public sealed class EditorSaveResult
    {
        public EditorSaveStatus Status { get; set; }
        public string Message { get; set; }
        public IList<ConfigIssue> Issues { get; set; }

        public EditorSaveResult()
        {
            Issues = new List<ConfigIssue>();
        }
    }

    public sealed class EditorOpenResult
    {
        public ConfigurationDocument Document { get; set; }
        public IList<ConfigIssue> Issues { get; set; }

        public bool Succeeded
        {
            get { return Document != null; }
        }

        public EditorOpenResult()
        {
            Issues = new List<ConfigIssue>();
        }
    }

    public sealed class EditorNode
    {
        private readonly List<EditorNode> children = new List<EditorNode>();

        public EditorNodeKind Kind { get; set; }
        public string ElementName { get; set; }
        public XElement Element { get; set; }
        public EditorNode Parent { get; set; }
        public int Line { get; set; }

        public IList<EditorNode> Children
        {
            get { return children; }
        }

        public string DisplayName
        {
            get
            {
                if (Kind == EditorNodeKind.Config)
                {
                    return "配置";
                }
                var name = Element == null ? null : (string)Element.Attribute("name");
                if (string.IsNullOrEmpty(name))
                {
                    name = Element == null ? string.Empty : (string)Element.Attribute("key") ?? ElementName;
                }
                return Kind == EditorNodeKind.Step ? ElementName + " · " + name : name;
            }
        }
    }

    /// <summary>
    /// 配置编辑器的文档模型：在原文档上就地改节点，保存前交给加载器校验，
    /// 校验通过才备份并原子替换目标文件。不含任何 WPF 依赖。
    /// </summary>
    public sealed class ConfigurationDocument
    {
        private const string BackupSuffix = ".bak";

        private readonly XDocument document;
        private readonly string dataRoot;
        private EditorNode configNode;
        private DateTime lastWriteTimeUtc;
        private long lastLength;

        private ConfigurationDocument(XDocument document, string targetPath, string dataRoot)
        {
            this.document = document;
            this.dataRoot = dataRoot;
            TargetPath = targetPath;
            RebuildTree();
        }

        /// <summary>写运行日志的出口，由调用方接上；不接则不记录。</summary>
        public Action<string, string> Note { get; set; }

        public string TargetPath { get; private set; }

        public bool IsDirty { get; private set; }

        public EditorNode Root
        {
            get { return configNode; }
        }

        /// <summary>按文档顺序遍历全部节点（含配置根），供全文档校验使用。</summary>
        public IEnumerable<EditorNode> Nodes
        {
            get { return Flatten(configNode); }
        }

        public static ConfigurationDocument CreateNew(string dataRoot)
        {
            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Deployment",
                    new XAttribute("schemaVersion", "2.0"),
                    new XAttribute("mode", "preview"),
                    new XElement("Meta", new XAttribute("name", string.Empty), new XAttribute("description", string.Empty)),
                    new XElement("Options", new XElement("LogDirectory", "logs")),
                    new XElement("Groups",
                        new XElement("Group",
                            new XAttribute("key", "group-1"),
                            new XAttribute("name", "新分组"),
                            new XElement("Task",
                                new XAttribute("key", "task-1"),
                                new XAttribute("name", "新任务"),
                                new XElement("Delay",
                                    new XAttribute("id", "step-1"),
                                    new XAttribute("name", "等待 1 秒"),
                                    new XAttribute("seconds", "1")))))));
            var created = new ConfigurationDocument(document, null, dataRoot);
            created.IsDirty = true;
            return created;
        }

        public static EditorOpenResult Open(string path, string dataRoot)
        {
            var result = new EditorOpenResult();
            var fullPath = Path.GetFullPath(path);
            XDocument document;
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using (var reader = XmlReader.Create(fullPath, settings))
                {
                    // 保留行号：加载器报错行要映射回具体节点。
                    document = XDocument.Load(reader, LoadOptions.SetLineInfo);
                }
            }
            catch (XmlException exception)
            {
                result.Issues.Add(new ConfigIssue
                {
                    Message = "XML 格式错误：" + exception.Message,
                    Line = exception.LineNumber,
                    Column = exception.LinePosition
                });
                return result;
            }
            catch (Exception exception)
            {
                result.Issues.Add(new ConfigIssue { Message = "无法读取配置文件：" + exception.Message });
                return result;
            }

            var load = RunLoader(fullPath, dataRoot);
            if (!load.IsValid)
            {
                foreach (var issue in load.Errors)
                {
                    result.Issues.Add(issue);
                }
                return result;
            }

            var created = new ConfigurationDocument(document, fullPath, dataRoot);
            created.RecordFileState(fullPath);
            result.Document = created;
            return result;
        }

        public IList<ConfigField> FieldsOf(EditorNode node)
        {
            if (node == null)
            {
                return new List<ConfigField>();
            }
            switch (node.Kind)
            {
                case EditorNodeKind.Config:
                    return ConfigFieldSchema.ConfigFields();
                case EditorNodeKind.Group:
                    return ConfigFieldSchema.GroupFields();
                case EditorNodeKind.Task:
                    return ConfigFieldSchema.TaskFields();
                default:
                    return ConfigFieldSchema.StepFields(node.ElementName);
            }
        }

        public string GetValue(EditorNode node, ConfigField field)
        {
            if (node == null || field == null)
            {
                return string.Empty;
            }
            if (node.Kind == EditorNodeKind.Config)
            {
                var root = document.Root;
                switch (field.Name)
                {
                    case "mode":
                        return (string)root.Attribute("mode") ?? "preview";
                    case "name":
                        return (string)MetaElement(false)?.Attribute("name") ?? string.Empty;
                    case "description":
                        return (string)MetaElement(false)?.Attribute("description") ?? string.Empty;
                    case "logDirectory":
                        return LogDirectoryElement(false) == null ? "logs" : LogDirectoryElement(false).Value;
                }
                return string.Empty;
            }
            if (node.Kind == EditorNodeKind.Step && field.Name == "code")
            {
                var code = node.Element.Element("Code");
                return code == null ? string.Empty : code.Value;
            }
            var attribute = node.Element.Attribute(field.Name);
            return attribute == null ? string.Empty : attribute.Value;
        }

        public void SetValue(EditorNode node, ConfigField field, string value)
        {
            if (node == null || field == null)
            {
                return;
            }
            var text = value ?? string.Empty;
            if (node.Kind == EditorNodeKind.Config)
            {
                var root = document.Root;
                switch (field.Name)
                {
                    case "mode":
                        root.SetAttributeValue("mode", string.IsNullOrWhiteSpace(text) ? "preview" : text);
                        break;
                    case "name":
                        MetaElement(true).SetAttributeValue("name", text);
                        break;
                    case "description":
                        MetaElement(true).SetAttributeValue("description", text);
                        break;
                    case "logDirectory":
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            var existing = LogDirectoryElement(false);
                            if (existing != null)
                            {
                                existing.Remove();
                            }
                        }
                        else
                        {
                            var options = document.Root.Element("Options");
                            if (options == null)
                            {
                                options = new XElement("Options");
                                document.Root.Add(options);
                            }
                            var element = LogDirectoryElement(false);
                            if (element == null)
                            {
                                options.Add(new XElement("LogDirectory", text));
                            }
                            else
                            {
                                element.Value = text;
                            }
                        }
                        break;
                }
                IsDirty = true;
                return;
            }

            if (node.Kind == EditorNodeKind.Step && field.Name == "code")
            {
                var code = node.Element.Element("Code");
                if (string.IsNullOrWhiteSpace(text))
                {
                    if (code != null)
                    {
                        code.Remove();
                    }
                }
                else if (code == null)
                {
                    node.Element.Add(new XElement("Code", new XCData(text)));
                }
                else
                {
                    code.ReplaceNodes(new XCData(text));
                }
                IsDirty = true;
                return;
            }

            // 空值表示「未填写」，移除属性，交给加载器报缺少必填项。
            if (string.IsNullOrWhiteSpace(text) && !field.Required)
            {
                node.Element.SetAttributeValue(field.Name, null);
            }
            else
            {
                node.Element.SetAttributeValue(field.Name, text);
            }
            IsDirty = true;
        }

        public EditorNode AddGroup()
        {
            var groups = GroupsElement(true);
            var group = new XElement("Group",
                new XAttribute("key", NextKey("group", groups.Elements("Group").Select(e => (string)e.Attribute("key")))),
                new XAttribute("name", NextName("新分组", groups.Elements("Group").Select(e => (string)e.Attribute("name")))));
            group.Add(BuildTask());
            groups.Add(group);
            RebuildTree();
            IsDirty = true;
            return FindByElement(group);
        }

        public EditorNode AddTask(EditorNode groupNode)
        {
            if (groupNode == null || groupNode.Kind != EditorNodeKind.Group)
            {
                return null;
            }
            var task = BuildTask();
            groupNode.Element.Add(task);
            RebuildTree();
            IsDirty = true;
            return FindByElement(task);
        }

        public EditorNode AddStep(EditorNode taskNode, string stepType)
        {
            if (taskNode == null || taskNode.Kind != EditorNodeKind.Task || string.IsNullOrEmpty(stepType))
            {
                return null;
            }
            if (Array.IndexOf(ConfigFieldSchema.StepTypes, stepType) < 0)
            {
                return null;
            }
            var step = ConfigFieldSchema.BuildStepElement(stepType, NextOperationId(), "新步骤");
            taskNode.Element.Add(step);
            RebuildTree();
            IsDirty = true;
            return FindByElement(step);
        }

        public void Remove(EditorNode node)
        {
            if (node == null || node.Kind == EditorNodeKind.Config || node.Element == null)
            {
                return;
            }
            var parent = node.Element.Parent;
            if (parent == null)
            {
                return;
            }
            // 连同紧跟的注释一起删，避免留下孤立说明。
            var comments = LeadingComments(node.Element);
            node.Element.Remove();
            foreach (var comment in comments)
            {
                comment.Remove();
            }
            RebuildTree();
            IsDirty = true;
        }

        public bool CanMove(EditorNode node, int offset)
        {
            return SiblingIndex(node, offset) >= 0;
        }

        public bool Move(EditorNode node, int offset)
        {
            if (node == null || node.Element == null || node.Element.Parent == null)
            {
                return false;
            }
            var siblings = node.Element.Parent.Elements().ToList();
            var index = siblings.IndexOf(node.Element);
            var targetIndex = index + offset;
            if (index < 0 || targetIndex < 0 || targetIndex >= siblings.Count)
            {
                return false;
            }

            var target = siblings[targetIndex];
            var block = new List<XNode>();
            block.AddRange(LeadingComments(node.Element));
            block.Add(node.Element);
            foreach (var item in block)
            {
                item.Remove();
            }
            if (offset < 0)
            {
                target.AddBeforeSelf(block);
            }
            else
            {
                target.AddAfterSelf(block);
            }
            RebuildTree();
            IsDirty = true;
            return true;
        }

        public EditorSaveResult Save(string path, bool overwriteExternal)
        {
            var result = new EditorSaveResult();
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception)
            {
                result.Status = EditorSaveStatus.IoFailed;
                result.Message = "路径无效：" + exception.Message;
                NoteLine("ERROR", "配置编辑器保存失败：" + result.Message);
                return result;
            }

            var sameTarget = TargetPath != null &&
                string.Equals(fullPath, TargetPath, StringComparison.OrdinalIgnoreCase);
            if (sameTarget && File.Exists(fullPath) && !overwriteExternal && ChangedOnDisk(fullPath))
            {
                result.Status = EditorSaveStatus.ExternalModified;
                result.Message = "文件已被外部修改，覆盖前请确认。";
                NoteLine("WARN", "配置编辑器保存中止：目标文件已被外部修改（" + fullPath + "）。");
                return result;
            }

            var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                WriteTo(tempPath);
            }
            catch (Exception exception)
            {
                TryDelete(tempPath);
                result.Status = EditorSaveStatus.IoFailed;
                result.Message = "写入临时文件失败：" + exception.Message;
                NoteLine("ERROR", "配置编辑器保存失败：" + result.Message);
                return result;
            }

            var validation = RunLoader(tempPath, dataRoot);
            if (!validation.IsValid)
            {
                // 校验在序列化后的临时文件上做，行号需要映射回编辑中的文档。
                RemapIssueLines(tempPath, validation.Errors);
                TryDelete(tempPath);
                foreach (var issue in validation.Errors)
                {
                    result.Issues.Add(issue);
                }
                result.Status = EditorSaveStatus.ValidationFailed;
                result.Message = "配置校验未通过，共 " + validation.Errors.Count + " 处问题。";
                NoteLine("WARN", "配置编辑器保存中止：校验未通过（" + validation.Errors.Count + " 处）。");
                return result;
            }

            try
            {
                if (File.Exists(fullPath))
                {
                    // 先留一份备份副本，再原子替换；替换失败时原文件仍在。
                    File.Copy(fullPath, fullPath + BackupSuffix, true);
                    File.Replace(tempPath, fullPath, null);
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }
            }
            catch (Exception exception)
            {
                TryDelete(tempPath);
                result.Status = EditorSaveStatus.IoFailed;
                result.Message = "写入配置失败：" + exception.Message;
                NoteLine("ERROR", "配置编辑器保存失败：" + result.Message);
                return result;
            }

            TargetPath = fullPath;
            RecordFileState(fullPath);
            IsDirty = false;
            result.Status = EditorSaveStatus.Saved;
            result.Message = fullPath;
            NoteLine("INFO", "配置编辑器已保存：" + fullPath + "。");
            return result;
        }

        public EditorNode FindNodeByLine(int line)
        {
            EditorNode match = null;
            foreach (var node in Flatten(configNode))
            {
                if (node.Line > 0 && node.Line <= line && (match == null || node.Line >= match.Line))
                {
                    match = node;
                }
            }
            return match;
        }

        /// <summary>
        /// 校验结果来自序列化后的临时文件，行号可能与编辑中的文档不同；
        /// 按元素路径把行号改回原文档，界面才能定位到正确的节点。
        /// </summary>
        private void RemapIssueLines(string serializedPath, IList<ConfigIssue> issues)
        {
            XDocument serialized;
            try
            {
                serialized = XDocument.Load(serializedPath, LoadOptions.SetLineInfo);
            }
            catch (Exception)
            {
                return;
            }

            var byPath = new Dictionary<string, EditorNode>(StringComparer.Ordinal);
            foreach (var node in Flatten(configNode))
            {
                byPath[PathKey(node.Element)] = node;
            }

            foreach (var issue in issues)
            {
                if (issue.Line <= 0)
                {
                    continue;
                }
                var element = ElementAtLine(serialized.Root, issue.Line);
                if (element == null)
                {
                    continue;
                }
                EditorNode node;
                if (byPath.TryGetValue(PathKey(element), out node) && node.Line > 0)
                {
                    issue.Line = node.Line;
                }
            }
        }

        private static XElement ElementAtLine(XElement root, int line)
        {
            XElement found = null;
            foreach (var element in root.DescendantsAndSelf())
            {
                var lineInfo = element as IXmlLineInfo;
                if (lineInfo != null && lineInfo.HasLineInfo() && lineInfo.LineNumber <= line)
                {
                    found = element;
                }
            }
            return found;
        }

        private static string PathKey(XElement element)
        {
            var parts = new List<string>();
            var current = element;
            while (current != null && current.Parent != null)
            {
                parts.Insert(0, current.Parent.Elements().ToList().IndexOf(current).ToString(CultureInfo.InvariantCulture));
                current = current.Parent;
            }
            return string.Join("/", parts);
        }

        private int SiblingIndex(EditorNode node, int offset)        {
            if (node == null || node.Element == null || node.Element.Parent == null)
            {
                return -1;
            }
            var siblings = node.Element.Parent.Elements().ToList();
            var index = siblings.IndexOf(node.Element);
            var target = index + offset;
            return index >= 0 && target >= 0 && target < siblings.Count ? target : -1;
        }

        private static IEnumerable<EditorNode> Flatten(EditorNode node)
        {
            if (node == null)
            {
                yield break;
            }
            yield return node;
            foreach (var child in node.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }

        private static List<XComment> LeadingComments(XElement element)
        {
            var comments = new List<XComment>();
            var previous = element.PreviousNode;
            while (previous is XComment)
            {
                comments.Insert(0, (XComment)previous);
                previous = previous.PreviousNode;
            }
            return comments;
        }

        private void WriteTo(string path)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\r\n",
                Encoding = new UTF8Encoding(false)
            };
            if (document.Declaration == null)
            {
                document.Declaration = new XDeclaration("1.0", "utf-8", null);
            }
            using (var writer = XmlWriter.Create(path, settings))
            {
                document.Save(writer);
            }
        }

        private static ConfigLoadResult RunLoader(string path, string dataRoot)
        {
            return new ConfigLoader(new PathPolicy()).Load(path, dataRoot);
        }

        private void RecordFileState(string path)
        {
            var info = new FileInfo(path);
            lastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            lastLength = info.Exists ? info.Length : -1;
        }

        private bool ChangedOnDisk(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }
            return info.LastWriteTimeUtc != lastWriteTimeUtc || info.Length != lastLength;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }

        private void NoteLine(string level, string message)
        {
            var note = Note;
            if (note != null)
            {
                note(level, message);
            }
        }

        private XElement MetaElement(bool create)
        {
            var meta = document.Root.Element("Meta");
            if (meta == null && create)
            {
                meta = new XElement("Meta");
                document.Root.AddFirst(meta);
            }
            return meta;
        }

        private XElement LogDirectoryElement(bool create)
        {
            var options = document.Root.Element("Options");
            if (options == null)
            {
                if (!create)
                {
                    return null;
                }
                options = new XElement("Options");
                document.Root.Add(options);
            }
            var element = options.Element("LogDirectory");
            if (element == null && create)
            {
                element = new XElement("LogDirectory", "logs");
                options.Add(element);
            }
            return element;
        }

        private XElement GroupsElement(bool create)
        {
            var groups = document.Root.Element("Groups");
            if (groups == null && create)
            {
                groups = new XElement("Groups");
                document.Root.Add(groups);
            }
            return groups;
        }

        private XElement BuildTask()
        {
            var task = new XElement("Task",
                new XAttribute("key", NextKey("task", AllElements("Task").Select(e => (string)e.Attribute("key")))),
                new XAttribute("name", NextName("新任务", AllElements("Task").Select(e => (string)e.Attribute("name")))));
            task.Add(ConfigFieldSchema.BuildStepElement("Delay", NextOperationId(), "等待 1 秒"));
            return task;
        }

        /// <summary>分组名与任务名必须唯一，新增时按序号规避重名。</summary>
        private static string NextName(string prefix, IEnumerable<string> used)
        {
            var taken = new HashSet<string>(used.Where(name => name != null), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(prefix))
            {
                return prefix;
            }
            for (var index = 2; index < 10000; index++)
            {
                var candidate = prefix + " " + index.ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
            return prefix + " " + Guid.NewGuid().ToString("N");
        }

        private IEnumerable<XElement> AllElements(string localName)
        {
            return document.Root.Descendants(localName);
        }

        private string NextKey(string prefix, IEnumerable<string> used)
        {
            var taken = new HashSet<string>(used.Where(k => k != null), StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < 10000; index++)
            {
                var candidate = prefix + "-" + index.ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
            return prefix + "-" + Guid.NewGuid().ToString("N");
        }

        private string NextOperationId()
        {
            var taken = new HashSet<string>(
                AllElements("Group").Elements().Elements()
                    .Select(e => (string)e.Attribute("id"))
                    .Where(id => id != null),
                StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < 100000; index++)
            {
                var candidate = "step-" + index.ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
            return "step-" + Guid.NewGuid().ToString("N");
        }

        private EditorNode FindByElement(XElement element)
        {
            if (element == null)
            {
                return null;
            }
            return Flatten(configNode).FirstOrDefault(node => ReferenceEquals(node.Element, element));
        }

        private void RebuildTree()
        {
            var root = document.Root;
            configNode = new EditorNode
            {
                Kind = EditorNodeKind.Config,
                ElementName = "Deployment",
                Element = root,
                Line = LineOf(root)
            };
            var groups = root.Element("Groups");
            if (groups == null)
            {
                return;
            }
            foreach (var groupElement in groups.Elements("Group"))
            {
                var groupNode = new EditorNode
                {
                    Kind = EditorNodeKind.Group,
                    ElementName = "Group",
                    Element = groupElement,
                    Parent = configNode,
                    Line = LineOf(groupElement)
                };
                configNode.Children.Add(groupNode);

                foreach (var taskElement in groupElement.Elements("Task"))
                {
                    var taskNode = new EditorNode
                    {
                        Kind = EditorNodeKind.Task,
                        ElementName = "Task",
                        Element = taskElement,
                        Parent = groupNode,
                        Line = LineOf(taskElement)
                    };
                    groupNode.Children.Add(taskNode);

                    foreach (var stepElement in taskElement.Elements())
                    {
                        if (Array.IndexOf(ConfigFieldSchema.StepTypes, stepElement.Name.LocalName) < 0)
                        {
                            continue;
                        }
                        taskNode.Children.Add(new EditorNode
                        {
                            Kind = EditorNodeKind.Step,
                            ElementName = stepElement.Name.LocalName,
                            Element = stepElement,
                            Parent = taskNode,
                            Line = LineOf(stepElement)
                        });
                    }
                }
            }
        }

        private static int LineOf(XElement element)
        {
            var lineInfo = element as IXmlLineInfo;
            return lineInfo != null && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
        }
    }
}
