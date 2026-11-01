using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Xml;
using System.Xml.Linq;

namespace IPCDeployment.Core
{
    public sealed class ConfigLoader
    {
        private readonly PathPolicy paths;

        public ConfigLoader(PathPolicy pathPolicy)
        {
            paths = pathPolicy ?? new PathPolicy();
        }

        public ConfigLoadResult Load(string configPath)
        {
            var result = new ConfigLoadResult();
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                AddError(result, null, "未找到 config.xml。");
                return result;
            }

            XDocument document;
            try
            {
                // 禁止 DTD 和外部实体，避免配置读取触发外部解析。
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true
                };
                using (var reader = XmlReader.Create(configPath, settings))
                {
                    document = XDocument.Load(reader, LoadOptions.SetLineInfo);
                }
            }
            catch (XmlException exception)
            {
                AddError(result, exception.LineNumber, exception.LinePosition, "XML 格式错误：" + exception.Message);
                return result;
            }
            catch (Exception exception)
            {
                AddError(result, null, "无法读取配置文件：" + exception.Message);
                return result;
            }

            var root = document.Root;
            if (root == null || root.Name.Namespace != XNamespace.None || root.Name.LocalName != "Deployment")
            {
                AddError(result, root, "根元素必须是 Deployment。");
                return result;
            }

            CheckAttributes(root, result, "schemaVersion", "mode");
            var schemaVersion = GetAttribute(root, "schemaVersion");
            if (!string.Equals(schemaVersion, "2.0", StringComparison.Ordinal))
            {
                AddError(result, root, "schemaVersion 必须为 2.0。");
            }

            var mode = DeploymentMode.Preview;
            var modeValue = GetAttribute(root, "mode");
            if (!string.IsNullOrWhiteSpace(modeValue) && !TryParseMode(modeValue, out mode))
            {
                AddError(result, root, "mode 必须是 preview 或 execute。");
            }

            var config = new DeploymentConfig
            {
                SchemaVersion = schemaVersion ?? string.Empty,
                Mode = mode
            };

            // 配置文件与 exe 同级，data/ 固定为其下的载荷目录。
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath));
            var dataRoot = Path.Combine(configDirectory, "data");
            var operationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var taskKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasGroups = false;

            foreach (var child in root.Elements())
            {
                if (child.Name.Namespace != XNamespace.None)
                {
                    AddError(result, child, "不支持带 XML 命名空间的元素。");
                    continue;
                }

                switch (child.Name.LocalName)
                {
                    case "Meta":
                        ParseMeta(child, config, result);
                        break;
                    case "Options":
                        ParseOptions(child, config, result);
                        break;
                    case "Groups":
                        hasGroups = true;
                        ParseGroups(child, config, result, dataRoot, operationIds, groupKeys, groupNames, taskKeys);
                        break;
                    default:
                        AddError(result, child, "未知根元素：" + child.Name.LocalName + "。");
                        break;
                }
            }

            if (!hasGroups)
            {
                AddError(result, root, "缺少 Groups 元素。");
            }
            if (config.Operations.Count == 0)
            {
                AddError(result, root, "至少需要声明一个步骤。");
            }

            for (var index = 0; index < config.Operations.Count; index++)
            {
                config.Operations[index].Order = index;
            }
            BuildSummary(config);
            result.Config = config;
            result.Summary = config.Summary;
            return result;
        }

        private void ParseMeta(XElement element, DeploymentConfig config, ConfigLoadResult result)
        {
            CheckAttributes(element, result, "name", "description");
            if (element.Elements().Any())
            {
                AddError(result, element, "Meta 不能包含子元素。");
            }
            config.Name = GetAttribute(element, "name") ?? string.Empty;
            config.Description = GetAttribute(element, "description") ?? string.Empty;
        }

        private void ParseOptions(XElement element, DeploymentConfig config, ConfigLoadResult result)
        {
            CheckAttributes(element, result);
            var logDirectoryCount = 0;

            foreach (var child in element.Elements())
            {
                if (child.Name.Namespace != XNamespace.None || child.Name.LocalName != "LogDirectory")
                {
                    AddError(result, child, "Options 只支持 LogDirectory。");
                    continue;
                }

                logDirectoryCount++;
                CheckAttributes(child, result);
                var value = (child.Value ?? string.Empty).Trim();
                if (!paths.IsSafeRelativePath(value))
                {
                    AddError(result, child, "LogDirectory 必须是应用目录内的相对路径。");
                    continue;
                }
                config.LogDirectory = value;
            }

            if (logDirectoryCount > 1)
            {
                AddError(result, element, "LogDirectory 只能声明一次。");
            }
        }

        private void ParseGroups(XElement element, DeploymentConfig config, ConfigLoadResult result,
            string dataRoot, HashSet<string> operationIds, HashSet<string> groupKeys,
            HashSet<string> groupNames, HashSet<string> taskKeys)
        {
            CheckAttributes(element, result);
            if (!element.Elements().Any())
            {
                AddError(result, element, "Groups 不能为空。");
                return;
            }

            foreach (var groupElement in element.Elements())
            {
                if (groupElement.Name.Namespace != XNamespace.None || groupElement.Name.LocalName != "Group")
                {
                    AddError(result, groupElement, "Groups 只支持 Group。");
                    continue;
                }

                CheckAttributes(groupElement, result, "key", "name");
                var key = RequireAttribute(groupElement, "key", result, "Group");
                var name = RequireAttribute(groupElement, "name", result, "Group");
                if (key == null || name == null)
                {
                    continue;
                }
                if (!groupKeys.Add(key))
                {
                    AddError(result, groupElement, "分组 key 重复：" + key + "。");
                }
                if (!groupNames.Add(name))
                {
                    AddError(result, groupElement, "分组名称重复：" + name + "。");
                }

                var group = new DeploymentGroup
                {
                    Key = key,
                    Name = name,
                    Line = LineOf(groupElement),
                    Column = ColumnOf(groupElement)
                };

                foreach (var taskElement in groupElement.Elements())
                {
                    if (taskElement.Name.Namespace != XNamespace.None || taskElement.Name.LocalName != "Task")
                    {
                        AddError(result, taskElement, "Group 只支持 Task。");
                        continue;
                    }

                    CheckAttributes(taskElement, result, "key", "name");
                    var taskKey = RequireAttribute(taskElement, "key", result, "Task");
                    var taskName = RequireAttribute(taskElement, "name", result, "Task");
                    if (taskKey == null || taskName == null)
                    {
                        continue;
                    }
                    if (!taskKeys.Add(taskKey))
                    {
                        AddError(result, taskElement, "任务 key 重复：" + taskKey + "。");
                    }

                    var task = new DeploymentTask
                    {
                        Key = taskKey,
                        Name = taskName,
                        GroupKey = group.Key,
                        GroupName = group.Name,
                        Line = LineOf(taskElement),
                        Column = ColumnOf(taskElement)
                    };

                    foreach (var operationElement in taskElement.Elements())
                    {
                        var operation = ParseStep(operationElement, result, dataRoot, operationIds);
                        if (operation != null)
                        {
                            operation.Type = operationElement.Name.LocalName;
                            operation.GroupKey = group.Key;
                            operation.GroupName = group.Name;
                            operation.TaskKey = task.Key;
                            operation.TaskName = task.Name;
                            task.Operations.Add(operation);
                            config.Operations.Add(operation);
                        }
                    }

                    if (task.Operations.Count == 0)
                    {
                        AddError(result, taskElement, "任务至少需要一个步骤。");
                    }
                    group.Tasks.Add(task);
                    config.Tasks.Add(task);
                }

                if (group.Tasks.Count == 0)
                {
                    AddError(result, groupElement, "分组至少需要一个任务。");
                }
                config.Groups.Add(group);
            }
        }

        private DeploymentOperation ParseStep(XElement operationElement, ConfigLoadResult result,
            string dataRoot, HashSet<string> operationIds)
        {
            if (operationElement.Name.Namespace != XNamespace.None)
            {
                AddError(result, operationElement, "不支持带 XML 命名空间的元素。");
                return null;
            }

            switch (operationElement.Name.LocalName)
            {
                case "Install":
                    return ParseInstall(operationElement, result, dataRoot, operationIds);
                case "Copy":
                    return ParseCopy(operationElement, result, dataRoot, operationIds);
                case "Registry":
                    return ParseRegistry(operationElement, result, operationIds);
                case "Command":
                    return ParseCommand(operationElement, result, dataRoot, operationIds);
                case "Service":
                    return ParseService(operationElement, result, operationIds);
                case "Network":
                    return ParseNetwork(operationElement, result, operationIds);
                case "Power":
                    return ParsePower(operationElement, result, operationIds);
                case "AutoStart":
                    return ParseAutoStart(operationElement, result, operationIds);
                case "Delay":
                    return ParseDelay(operationElement, result, operationIds);
                default:
                    AddError(result, operationElement, "未知操作类型：" + operationElement.Name.LocalName + "。");
                    return null;
            }
        }

        private DeploymentOperation ParseInstall(XElement element, ConfigLoadResult result, string dataRoot, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "file", "arguments", "sha256",
                "timeoutMinutes", "successExitCodes", "skipIfFileExists");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            var file = RequireAttribute(element, "file", result, "Install");
            if (file == null)
            {
                return null;
            }

            string resolvedFile;
            try
            {
                resolvedFile = paths.ResolveSource(dataRoot, file, DataArea.Software);
            }
            catch (Exception exception)
            {
                AddError(result, element, exception.Message);
                return null;
            }

            if (!System.IO.File.Exists(resolvedFile))
            {
                AddError(result, element, "安装包不存在：" + file + "。");
                return null;
            }

            var extension = Path.GetExtension(resolvedFile);
            if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase))
            {
                AddError(result, element, "Install 只支持 .exe 或 .msi。");
                return null;
            }

            var sha256 = GetAttribute(element, "sha256");
            if (!string.IsNullOrWhiteSpace(sha256) && !IsHex64(sha256))
            {
                AddError(result, element, "sha256 必须是 64 位十六进制。");
            }

            var timeout = ReadTimeout(element, 15, result, "Install");
            var successCodes = ReadSuccessCodes(element, result);
            var skipIf = GetAttribute(element, "skipIfFileExists");
            if (!string.IsNullOrWhiteSpace(skipIf))
            {
                try
                {
                    skipIf = paths.ResolveDestination(skipIf);
                }
                catch (Exception exception)
                {
                    AddError(result, element, "skipIfFileExists 无效：" + exception.Message);
                    skipIf = null;
                }
            }

            var operation = new InstallOperation
            {
                File = file,
                ResolvedFile = resolvedFile,
                Arguments = GetAttribute(element, "arguments") ?? string.Empty,
                Sha256 = string.IsNullOrWhiteSpace(sha256) ? null : sha256.Trim(),
                TimeoutMinutes = timeout,
                SuccessExitCodes = successCodes,
                SkipIfFileExists = string.IsNullOrWhiteSpace(skipIf) ? null : skipIf
            };
            ApplyCommon(operation, common);
            return operation;
        }

        private DeploymentOperation ParseCopy(XElement element, ConfigLoadResult result, string dataRoot, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "source", "target", "overwrite", "backup");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            var source = RequireAttribute(element, "source", result, "Copy");
            var target = RequireAttribute(element, "target", result, "Copy");
            if (source == null || target == null)
            {
                return null;
            }

            string resolvedSource;
            string resolvedTarget;
            try
            {
                resolvedSource = paths.ResolveSource(dataRoot, source, DataArea.Files);
                resolvedTarget = paths.ResolveDestination(target);
            }
            catch (Exception exception)
            {
                AddError(result, element, exception.Message);
                return null;
            }

            if (!System.IO.File.Exists(resolvedSource) && !Directory.Exists(resolvedSource))
            {
                AddError(result, element, "复制源不存在：" + source + "。");
                return null;
            }

            var overwrite = CopyOverwriteMode.IfDifferent;
            var overwriteValue = GetAttribute(element, "overwrite");
            if (!string.IsNullOrWhiteSpace(overwriteValue) && !TryParseOverwrite(overwriteValue, out overwrite))
            {
                AddError(result, element, "overwrite 必须是 ifDifferent、always 或 ifNotExists。");
            }

            var backup = false;
            var backupValue = GetAttribute(element, "backup");
            if (!string.IsNullOrWhiteSpace(backupValue) && !bool.TryParse(backupValue, out backup))
            {
                AddError(result, element, "backup 必须是 true 或 false。");
            }

            var operation = new CopyOperation
            {
                Source = source,
                ResolvedSource = resolvedSource,
                Target = target,
                ResolvedTarget = resolvedTarget,
                Overwrite = overwrite,
                Backup = backup
            };
            ApplyCommon(operation, common);
            return operation;
        }

        private DeploymentOperation ParseRegistry(XElement element, ConfigLoadResult result, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "hive", "key", "valueName", "valueType",
                "value", "view", "expectedBefore");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            var hiveValue = RequireAttribute(element, "hive", result, "Registry");
            var key = RequireAttribute(element, "key", result, "Registry");
            var valueName = RequireAttributePresent(element, "valueName", result, "Registry");
            var valueTypeValue = RequireAttribute(element, "valueType", result, "Registry");
            var value = RequireAttributePresent(element, "value", result, "Registry");
            if (hiveValue == null || key == null || valueName == null || valueTypeValue == null || value == null)
            {
                return null;
            }

            RegistryHiveKind hive;
            RegistryValueType valueType;
            if (!TryParseHive(hiveValue, out hive))
            {
                AddError(result, element, "hive 必须是 HKCU、HKLM、HKCR、HKU 或 HKCC。");
                return null;
            }
            if (!TryParseRegistryValueType(valueTypeValue, out valueType))
            {
                AddError(result, element, "valueType 必须是 String、ExpandString、DWord 或 QWord。");
                return null;
            }
            if (!ValidateRegistryValue(valueType, value, element, result))
            {
                return null;
            }

            var view = 64;
            var viewValue = GetAttribute(element, "view");
            if (!string.IsNullOrWhiteSpace(viewValue) &&
                !(int.TryParse(viewValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out view) && (view == 32 || view == 64)))
            {
                AddError(result, element, "view 必须是 64 或 32。");
                view = 64;
            }

            var operation = new RegistryOperation
            {
                Hive = hive,
                Key = key,
                ValueName = valueName,
                ValueType = valueType,
                Value = value,
                View = view,
                ExpectedBefore = GetAttribute(element, "expectedBefore")
            };
            ApplyCommon(operation, common);
            return operation;
        }

        private DeploymentOperation ParseCommand(XElement element, ConfigLoadResult result, string dataRoot, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "file", "arguments", "workingDirectory",
                "timeoutMinutes", "successExitCodes", "language", "strict");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            XElement codeElement = null;
            foreach (var child in element.Elements())
            {
                if (child.Name.Namespace != XNamespace.None || child.Name.LocalName != "Code")
                {
                    AddError(result, child, "Command 只支持 Code 子元素。");
                    continue;
                }
                if (codeElement != null)
                {
                    AddError(result, child, "Command 只能包含一个 Code 元素。");
                    continue;
                }
                codeElement = child;
            }

            var file = GetAttribute(element, "file");
            var languageValue = GetAttribute(element, "language");
            var hasFile = !string.IsNullOrWhiteSpace(file);
            var hasCode = codeElement != null;
            if (hasFile && hasCode)
            {
                AddError(result, element, "Command 不能同时使用 file 和 Code。");
                return null;
            }
            if (!hasFile && !hasCode)
            {
                AddError(result, element, "Command 必须提供 file 或 Code。");
                return null;
            }

            var strict = true;
            var strictValue = GetAttribute(element, "strict");
            if (!string.IsNullOrWhiteSpace(strictValue) && !bool.TryParse(strictValue, out strict))
            {
                AddError(result, element, "strict 必须是 true 或 false。");
                return null;
            }

            CommandLanguage language;
            string resolvedFile = null;
            string code = null;

            if (hasFile)
            {
                if (!string.IsNullOrWhiteSpace(languageValue))
                {
                    AddError(result, element, "文件模式由扩展名决定类型，不接受 language。");
                    return null;
                }
                try
                {
                    resolvedFile = paths.ResolveSource(dataRoot, file, DataArea.Commands);
                }
                catch (Exception exception)
                {
                    AddError(result, element, exception.Message);
                    return null;
                }
                if (!System.IO.File.Exists(resolvedFile))
                {
                    AddError(result, element, "命令文件不存在：" + file + "。");
                    return null;
                }

                var extension = Path.GetExtension(resolvedFile);
                if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    language = CommandLanguage.Exe;
                }
                else if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
                {
                    language = CommandLanguage.Cmd;
                }
                else if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    language = CommandLanguage.PowerShell;
                }
                else
                {
                    AddError(result, element, "Command 只支持 .exe、.cmd、.bat 或 .ps1。");
                    return null;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(languageValue) ||
                    !TryParseCommandLanguage(languageValue, out language) ||
                    language == CommandLanguage.Exe)
                {
                    AddError(result, element, "内嵌代码的 language 必须是 cmd 或 powershell。");
                    return null;
                }
                if (element.Attribute("arguments") != null)
                {
                    AddError(result, element, "内嵌代码不接受 arguments，请把参数写入代码。");
                    return null;
                }
                CheckAttributes(codeElement, result);
                if (codeElement.HasElements)
                {
                    AddError(result, codeElement, "Code 只支持文本内容。");
                    return null;
                }
                code = (codeElement.Value ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(code))
                {
                    AddError(result, codeElement, "Code 不能为空。");
                    return null;
                }
            }

            var workingDirectory = GetAttribute(element, "workingDirectory");
            string resolvedWorkingDirectory = null;
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                try
                {
                    resolvedWorkingDirectory = paths.ResolveSource(dataRoot, workingDirectory, DataArea.Commands);
                }
                catch (Exception exception)
                {
                    AddError(result, element, "workingDirectory 无效：" + exception.Message);
                    return null;
                }
                if (!Directory.Exists(resolvedWorkingDirectory))
                {
                    AddError(result, element, "workingDirectory 不存在：" + workingDirectory + "。");
                    return null;
                }
            }

            var operation = new CommandOperation
            {
                File = file,
                ResolvedFile = resolvedFile,
                Arguments = GetAttribute(element, "arguments") ?? string.Empty,
                WorkingDirectory = workingDirectory,
                ResolvedWorkingDirectory = resolvedWorkingDirectory,
                TimeoutMinutes = ReadTimeout(element, 10, result, "Command"),
                SuccessExitCodes = ReadSuccessCodes(element, result),
                Language = language,
                Code = code,
                Strict = strict
            };
            ApplyCommon(operation, common);
            return operation;
        }

        private static bool TryParseCommandLanguage(string value, out CommandLanguage language)
        {
            if (string.Equals(value, "cmd", StringComparison.OrdinalIgnoreCase))
            {
                language = CommandLanguage.Cmd;
                return true;
            }
            if (string.Equals(value, "powershell", StringComparison.OrdinalIgnoreCase))
            {
                language = CommandLanguage.PowerShell;
                return true;
            }
            if (string.Equals(value, "exe", StringComparison.OrdinalIgnoreCase))
            {
                language = CommandLanguage.Exe;
                return true;
            }
            language = CommandLanguage.Exe;
            return false;
        }

        private DeploymentOperation ParseService(XElement element, ConfigLoadResult result, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "serviceName", "startupType", "state");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            var serviceName = RequireAttribute(element, "serviceName", result, "Service");
            if (serviceName == null)
            {
                return null;
            }

            ServiceStartupType? startupType = null;
            var startupValue = GetAttribute(element, "startupType");
            if (!string.IsNullOrWhiteSpace(startupValue))
            {
                ServiceStartupType parsed;
                if (!TryParseStartupType(startupValue, out parsed))
                {
                    AddError(result, element, "startupType 必须是 Automatic、Manual 或 Disabled。");
                }
                else
                {
                    startupType = parsed;
                }
            }

            ServiceState? state = null;
            var stateValue = GetAttribute(element, "state");
            if (!string.IsNullOrWhiteSpace(stateValue))
            {
                ServiceState parsed;
                if (!TryParseServiceState(stateValue, out parsed))
                {
                    AddError(result, element, "state 必须是 Running 或 Stopped。");
                }
                else
                {
                    state = parsed;
                }
            }

            if (startupType == null && state == null)
            {
                AddError(result, element, "Service 至少需要 startupType 或 state。");
                return null;
            }

            var operation = new ServiceOperation
            {
                ServiceName = serviceName,
                StartupType = startupType,
                State = state
            };
            ApplyCommon(operation, common);
            return operation;
        }

        private DeploymentOperation ParseNetwork(XElement element, ConfigLoadResult result, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "adapter", "kind", "keyword", "value",
                "valueType", "mode", "address", "prefixLength", "gateway", "dns", "expectedBefore");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            var adapter = RequireAttribute(element, "adapter", result, "Network");
            var kindValue = RequireAttribute(element, "kind", result, "Network");
            if (adapter == null || kindValue == null)
            {
                return null;
            }

            NetworkOperationKind kind;
            if (!TryParseNetworkKind(kindValue, out kind))
            {
                AddError(result, element, "kind 必须是 AdvancedProperty 或 IPv4。");
                return null;
            }

            if (kind == NetworkOperationKind.AdvancedProperty)
            {
                if (HasAnyAttribute(element, "mode", "address", "prefixLength", "gateway", "dns"))
                {
                    AddError(result, element, "AdvancedProperty 不接受 mode、address、prefixLength、gateway 或 dns。");
                    return null;
                }

                var keyword = RequireAttribute(element, "keyword", result, "Network");
                var value = RequireAttributePresent(element, "value", result, "Network");
                if (keyword == null || value == null)
                {
                    return null;
                }

                var valueType = RegistryValueType.String;
                var valueTypeValue = GetAttribute(element, "valueType");
                if (!string.IsNullOrWhiteSpace(valueTypeValue) && !TryParseRegistryValueType(valueTypeValue, out valueType))
                {
                    AddError(result, element, "valueType 必须是 String、ExpandString、DWord 或 QWord。");
                    return null;
                }
                if (!ValidateRegistryValue(valueType, value, element, result))
                {
                    return null;
                }

                var operation = new NetworkOperation
                {
                    Adapter = adapter,
                    Kind = kind,
                    Keyword = keyword,
                    Value = value,
                    ValueType = valueType,
                    ExpectedBefore = GetAttribute(element, "expectedBefore")
                };
                ApplyCommon(operation, common);
                return operation;
            }

            if (HasAnyAttribute(element, "keyword", "value", "valueType", "expectedBefore"))
            {
                AddError(result, element, "IPv4 不接受 keyword、value、valueType 或 expectedBefore。");
                return null;
            }

            var modeValue = RequireAttribute(element, "mode", result, "Network");
            if (modeValue == null)
            {
                return null;
            }
            NetworkIpMode mode;
            if (!TryParseIpMode(modeValue, out mode))
            {
                AddError(result, element, "mode 必须是 Static 或 Dhcp。");
                return null;
            }

            if (mode == NetworkIpMode.Dhcp)
            {
                if (HasAnyAttribute(element, "address", "prefixLength", "gateway", "dns"))
                {
                    AddError(result, element, "Dhcp 模式不接受 address、prefixLength、gateway 或 dns。");
                    return null;
                }

                var dhcpOperation = new NetworkOperation
                {
                    Adapter = adapter,
                    Kind = kind,
                    IpMode = mode
                };
                ApplyCommon(dhcpOperation, common);
                return dhcpOperation;
            }

            var address = RequireAttribute(element, "address", result, "Network");
            var prefixValue = RequireAttribute(element, "prefixLength", result, "Network");
            if (address == null || prefixValue == null)
            {
                return null;
            }
            if (!IsIPv4(address))
            {
                AddError(result, element, "address 必须是 IPv4 地址。");
                return null;
            }

            int prefixLength;
            if (!int.TryParse(prefixValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out prefixLength) ||
                prefixLength < 1 || prefixLength > 32)
            {
                AddError(result, element, "prefixLength 必须是 1 到 32。");
                return null;
            }

            var gateway = GetAttribute(element, "gateway");
            if (!string.IsNullOrWhiteSpace(gateway) && !IsIPv4(gateway))
            {
                AddError(result, element, "gateway 必须是 IPv4 地址。");
                return null;
            }

            var dns = GetAttribute(element, "dns");
            if (!string.IsNullOrWhiteSpace(dns))
            {
                string normalizedDns;
                if (!TryNormalizeDns(dns, out normalizedDns))
                {
                    AddError(result, element, "dns 必须是逗号分隔的 IPv4 地址。");
                    return null;
                }
                dns = normalizedDns;
            }

            var staticOperation = new NetworkOperation
            {
                Adapter = adapter,
                Kind = kind,
                IpMode = mode,
                Address = address,
                PrefixLength = prefixLength,
                Gateway = string.IsNullOrWhiteSpace(gateway) ? null : gateway,
                Dns = string.IsNullOrWhiteSpace(dns) ? null : dns
            };
            ApplyCommon(staticOperation, common);
            return staticOperation;
        }

        private DeploymentOperation ParsePower(XElement element, ConfigLoadResult result, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "setting", "value");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            var settingValue = RequireAttribute(element, "setting", result, "Power");
            var valueValue = RequireAttribute(element, "value", result, "Power");
            if (settingValue == null || valueValue == null)
            {
                return null;
            }

            PowerSetting setting;
            PowerValue value;
            if (!TryParsePowerSetting(settingValue, out setting))
            {
                AddError(result, element, "setting 必须是 Hibernate、FastStartup 或 PowerThrottling。");
                return null;
            }
            if (!TryParsePowerValue(valueValue, out value))
            {
                AddError(result, element, "value 必须是 On 或 Off。");
                return null;
            }

            var operation = new PowerOperation
            {
                Setting = setting,
                Value = value
            };
            ApplyCommon(operation, common);
            return operation;
        }

        private DeploymentOperation ParseDelay(XElement element, ConfigLoadResult result, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "seconds");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            var secondsValue = RequireAttribute(element, "seconds", result, "Delay");
            if (secondsValue == null)
            {
                return null;
            }
            int seconds;
            if (!int.TryParse(secondsValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) ||
                seconds < 1 || seconds > 600)
            {
                AddError(result, element, "Delay 的 seconds 必须是 1 到 600。");
                return null;
            }

            var operation = new DelayOperation
            {
                Seconds = seconds
            };
            ApplyCommon(operation, common);
            return operation;
        }

        private DeploymentOperation ParseAutoStart(XElement element, ConfigLoadResult result, HashSet<string> ids)
        {
            CheckAttributes(element, result, "id", "name", "hive", "valueName", "command",
                "arguments", "enabled");
            var common = ReadCommon(element, result, ids);
            if (!common.Valid)
            {
                return null;
            }

            var hiveValue = RequireAttribute(element, "hive", result, "AutoStart");
            var valueName = RequireAttribute(element, "valueName", result, "AutoStart");
            var command = RequireAttribute(element, "command", result, "AutoStart");
            if (hiveValue == null || valueName == null || command == null)
            {
                return null;
            }

            RegistryHiveKind hive;
            if (!TryParseHive(hiveValue, out hive) || (hive != RegistryHiveKind.HKCU && hive != RegistryHiveKind.HKLM))
            {
                AddError(result, element, "AutoStart 的 hive 必须是 HKCU 或 HKLM。");
                return null;
            }

            try
            {
                paths.ResolveDestination(command);
            }
            catch (Exception exception)
            {
                AddError(result, element, "command 无效：" + exception.Message);
                return null;
            }

            var enabled = true;
            var enabledValue = GetAttribute(element, "enabled");
            if (!string.IsNullOrWhiteSpace(enabledValue) && !bool.TryParse(enabledValue, out enabled))
            {
                AddError(result, element, "enabled 必须是 true 或 false。");
                return null;
            }

            var operation = new AutoStartOperation
            {
                Hive = hive,
                ValueName = valueName,
                Command = command,
                Arguments = GetAttribute(element, "arguments") ?? string.Empty,
                Enabled = enabled
            };
            ApplyCommon(operation, common);
            return operation;
        }

        private CommonAttributes ReadCommon(XElement element, ConfigLoadResult result, HashSet<string> ids)
        {
            var id = RequireAttribute(element, "id", result, element.Name.LocalName);
            var name = RequireAttribute(element, "name", result, element.Name.LocalName);

            var valid = id != null && name != null;
            if (id != null && !ids.Add(id))
            {
                AddError(result, element, "操作 id 重复：" + id + "。");
                valid = false;
            }

            return new CommonAttributes
            {
                Id = id,
                Name = name,
                Valid = valid
            };
        }

        private static void ApplyCommon(DeploymentOperation operation, CommonAttributes common)
        {
            operation.Id = common.Id;
            operation.Name = common.Name;
        }

        private static void BuildSummary(DeploymentConfig config)
        {
            var summary = new ConfigSummary
            {
                GroupCount = config.Groups.Count,
                TaskCount = config.Tasks.Count
            };
            foreach (var operation in config.Operations)
            {
                summary.TotalOperations++;
                if (operation is InstallOperation) summary.InstallCount++;
                else if (operation is CopyOperation) summary.CopyCount++;
                else if (operation is RegistryOperation) summary.RegistryCount++;
                else if (operation is CommandOperation) summary.CommandCount++;
                else if (operation is ServiceOperation) summary.ServiceCount++;
                else if (operation is NetworkOperation) summary.NetworkCount++;
                else if (operation is PowerOperation) summary.PowerCount++;
                else if (operation is AutoStartOperation) summary.AutoStartCount++;
            }
            config.Summary = summary;
        }

        private static void CheckAttributes(XElement element, ConfigLoadResult result, params string[] allowed)
        {
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }
                if (attribute.Name.Namespace != XNamespace.None ||
                    Array.IndexOf(allowed, attribute.Name.LocalName) < 0)
                {
                    AddError(result, element, "未知属性：" + attribute.Name.LocalName + "。");
                }
            }
        }

        private static bool HasAnyAttribute(XElement element, params string[] names)
        {
            return names.Any(name => element.Attribute(name) != null);
        }

        private static string GetAttribute(XElement element, string name)
        {
            var attribute = element.Attribute(name);
            return attribute == null ? null : attribute.Value;
        }

        private static string RequireAttribute(XElement element, string name, ConfigLoadResult result, string context)
        {
            var value = GetAttribute(element, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                AddError(result, element, context + " 缺少必填属性：" + name + "。");
                return null;
            }
            return value.Trim();
        }

        private static string RequireAttributePresent(XElement element, string name, ConfigLoadResult result, string context)
        {
            var attribute = element.Attribute(name);
            if (attribute == null)
            {
                AddError(result, element, context + " 缺少必填属性：" + name + "。");
                return null;
            }
            return attribute.Value;
        }

        private static int ReadTimeout(XElement element, int defaultValue, ConfigLoadResult result, string context)
        {
            var value = GetAttribute(element, "timeoutMinutes");
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }
            int timeout;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out timeout) ||
                timeout < 1 || timeout > 60)
            {
                AddError(result, element, context + " 的 timeoutMinutes 必须在 1 到 60 之间。");
                return defaultValue;
            }
            return timeout;
        }

        private static int[] ReadSuccessCodes(XElement element, ConfigLoadResult result)
        {
            var value = GetAttribute(element, "successExitCodes");
            if (string.IsNullOrWhiteSpace(value))
            {
                return new[] { 0 };
            }

            var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var codes = new List<int>();
            foreach (var part in parts)
            {
                int code;
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                {
                    AddError(result, element, "successExitCodes 必须是逗号分隔的整数。");
                    return new[] { 0 };
                }
                codes.Add(code);
            }
            if (codes.Count == 0)
            {
                AddError(result, element, "successExitCodes 至少需要一个值。");
                return new[] { 0 };
            }
            return codes.ToArray();
        }

        private static bool ValidateRegistryValue(RegistryValueType type, string value, XElement element, ConfigLoadResult result)
        {
            if (type == RegistryValueType.DWord)
            {
                int number;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    AddError(result, element, "DWord 值必须是 32 位整数。");
                    return false;
                }
            }
            else if (type == RegistryValueType.QWord)
            {
                long number;
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                {
                    AddError(result, element, "QWord 值必须是 64 位整数。");
                    return false;
                }
            }
            return true;
        }

        private static bool TryParseMode(string value, out DeploymentMode mode)
        {
            if (string.Equals(value, "execute", StringComparison.OrdinalIgnoreCase))
            {
                mode = DeploymentMode.Execute;
                return true;
            }
            if (string.Equals(value, "preview", StringComparison.OrdinalIgnoreCase))
            {
                mode = DeploymentMode.Preview;
                return true;
            }
            mode = DeploymentMode.Preview;
            return false;
        }

        private static bool TryParseOverwrite(string value, out CopyOverwriteMode mode)
        {
            if (string.Equals(value, "always", StringComparison.OrdinalIgnoreCase))
            {
                mode = CopyOverwriteMode.Always;
                return true;
            }
            if (string.Equals(value, "ifNotExists", StringComparison.OrdinalIgnoreCase))
            {
                mode = CopyOverwriteMode.IfNotExists;
                return true;
            }
            if (string.Equals(value, "ifDifferent", StringComparison.OrdinalIgnoreCase))
            {
                mode = CopyOverwriteMode.IfDifferent;
                return true;
            }
            mode = CopyOverwriteMode.IfDifferent;
            return false;
        }

        private static bool TryParseHive(string value, out RegistryHiveKind hive)
        {
            if (string.Equals(value, "HKCU", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHiveKind.HKCU;
                return true;
            }
            if (string.Equals(value, "HKLM", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHiveKind.HKLM;
                return true;
            }
            if (string.Equals(value, "HKCR", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHiveKind.HKCR;
                return true;
            }
            if (string.Equals(value, "HKU", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHiveKind.HKU;
                return true;
            }
            if (string.Equals(value, "HKCC", StringComparison.OrdinalIgnoreCase))
            {
                hive = RegistryHiveKind.HKCC;
                return true;
            }
            hive = RegistryHiveKind.HKCU;
            return false;
        }

        private static bool TryParseRegistryValueType(string value, out RegistryValueType type)
        {
            if (string.Equals(value, "String", StringComparison.OrdinalIgnoreCase))
            {
                type = RegistryValueType.String;
                return true;
            }
            if (string.Equals(value, "ExpandString", StringComparison.OrdinalIgnoreCase))
            {
                type = RegistryValueType.ExpandString;
                return true;
            }
            if (string.Equals(value, "DWord", StringComparison.OrdinalIgnoreCase))
            {
                type = RegistryValueType.DWord;
                return true;
            }
            if (string.Equals(value, "QWord", StringComparison.OrdinalIgnoreCase))
            {
                type = RegistryValueType.QWord;
                return true;
            }
            type = RegistryValueType.String;
            return false;
        }

        private static bool TryParseStartupType(string value, out ServiceStartupType type)
        {
            if (string.Equals(value, "Automatic", StringComparison.OrdinalIgnoreCase))
            {
                type = ServiceStartupType.Automatic;
                return true;
            }
            if (string.Equals(value, "Manual", StringComparison.OrdinalIgnoreCase))
            {
                type = ServiceStartupType.Manual;
                return true;
            }
            if (string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                type = ServiceStartupType.Disabled;
                return true;
            }
            type = ServiceStartupType.Automatic;
            return false;
        }

        private static bool TryParseServiceState(string value, out ServiceState state)
        {
            if (string.Equals(value, "Running", StringComparison.OrdinalIgnoreCase))
            {
                state = ServiceState.Running;
                return true;
            }
            if (string.Equals(value, "Stopped", StringComparison.OrdinalIgnoreCase))
            {
                state = ServiceState.Stopped;
                return true;
            }
            state = ServiceState.Running;
            return false;
        }

        private static bool TryParseNetworkKind(string value, out NetworkOperationKind kind)
        {
            if (string.Equals(value, "AdvancedProperty", StringComparison.OrdinalIgnoreCase))
            {
                kind = NetworkOperationKind.AdvancedProperty;
                return true;
            }
            if (string.Equals(value, "IPv4", StringComparison.OrdinalIgnoreCase))
            {
                kind = NetworkOperationKind.IPv4;
                return true;
            }
            kind = NetworkOperationKind.AdvancedProperty;
            return false;
        }

        private static bool TryParseIpMode(string value, out NetworkIpMode mode)
        {
            if (string.Equals(value, "Static", StringComparison.OrdinalIgnoreCase))
            {
                mode = NetworkIpMode.Static;
                return true;
            }
            if (string.Equals(value, "Dhcp", StringComparison.OrdinalIgnoreCase))
            {
                mode = NetworkIpMode.Dhcp;
                return true;
            }
            mode = NetworkIpMode.Static;
            return false;
        }

        private static bool TryParsePowerSetting(string value, out PowerSetting setting)
        {
            if (string.Equals(value, "Hibernate", StringComparison.OrdinalIgnoreCase))
            {
                setting = PowerSetting.Hibernate;
                return true;
            }
            if (string.Equals(value, "FastStartup", StringComparison.OrdinalIgnoreCase))
            {
                setting = PowerSetting.FastStartup;
                return true;
            }
            if (string.Equals(value, "PowerThrottling", StringComparison.OrdinalIgnoreCase))
            {
                setting = PowerSetting.PowerThrottling;
                return true;
            }
            setting = PowerSetting.Hibernate;
            return false;
        }

        private static bool TryParsePowerValue(string value, out PowerValue powerValue)
        {
            if (string.Equals(value, "On", StringComparison.OrdinalIgnoreCase))
            {
                powerValue = PowerValue.On;
                return true;
            }
            if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
            {
                powerValue = PowerValue.Off;
                return true;
            }
            powerValue = PowerValue.Off;
            return false;
        }

        private static bool IsHex64(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            foreach (var character in value)
            {
                var isHex = (character >= '0' && character <= '9') ||
                            (character >= 'a' && character <= 'f') ||
                            (character >= 'A' && character <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsIPv4(string value)
        {
            IPAddress address;
            return IPAddress.TryParse(value, out address) && address.AddressFamily == AddressFamily.InterNetwork;
        }

        private static bool TryNormalizeDns(string value, out string normalized)
        {
            var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var addresses = new List<string>();
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (!IsIPv4(trimmed))
                {
                    normalized = null;
                    return false;
                }
                addresses.Add(trimmed);
            }
            if (addresses.Count == 0)
            {
                normalized = null;
                return false;
            }
            normalized = string.Join(",", addresses);
            return true;
        }

        private static void AddError(ConfigLoadResult result, XObject element, string message)
        {
            AddIssue(result, element, message, false);
        }

        private static void AddError(ConfigLoadResult result, int line, int column, string message)
        {
            AddIssue(result, line, column, message, false);
        }

        private static void AddIssue(ConfigLoadResult result, XObject element, string message, bool warning)
        {
            AddIssue(result, LineOf(element), ColumnOf(element), message, warning);
        }

        private static void AddIssue(ConfigLoadResult result, int line, int column, string message, bool warning)
        {
            var issue = new ConfigIssue
            {
                Message = message,
                Line = line,
                Column = column,
                IsWarning = warning
            };
            if (warning)
            {
                result.Warnings.Add(issue);
            }
            else
            {
                result.Errors.Add(issue);
            }
        }

        private static int LineOf(XObject element)
        {
            var info = element as IXmlLineInfo;
            return info == null ? 0 : info.LineNumber;
        }

        private static int ColumnOf(XObject element)
        {
            var info = element as IXmlLineInfo;
            return info == null ? 0 : info.LinePosition;
        }

        private sealed class CommonAttributes
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool Valid { get; set; }
        }
    }
}
