using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace IPCDeployment.Core
{
    public enum FieldInputKind
    {
        Text,
        Multiline,
        Integer,
        Boolean,
        Enum
    }

    /// <summary>
    /// 字段级轻校验类型。只用于输入提示，保存仍以配置加载器为准。
    /// </summary>
    public enum FieldValidationKind
    {
        None,
        Integer,
        Sha256,
        ExitCodes,
        SourcePath,
        DestinationPath,
        Ipv4,
        Ipv4List,
        RelativePath,
        RegistryValue
    }

    /// <summary>字段可见条件：同一字段上的多个条件取「与」。</summary>
    public sealed class FieldCondition
    {
        public FieldCondition(string field, string value)
        {
            Field = field;
            Value = value;
        }

        public string Field { get; private set; }
        public string Value { get; private set; }
    }

    public sealed class ConfigField
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string Hint { get; set; }
        public FieldInputKind Kind { get; set; }
        public IList<string> Options { get; set; }
        public bool Required { get; set; }

        /// <summary>属性面板中的分区标题。</summary>
        public string Section { get; set; }
        /// <summary>允许空字符串（加载器只要求属性存在，例如注册表默认值）。</summary>
        public bool AllowEmpty { get; set; }
        public int? MinValue { get; set; }
        public int? MaxValue { get; set; }
        public FieldValidationKind Check { get; set; }
        public string PathPrefix { get; set; }
        /// <summary>源路径字段是否提供 data 下现有条目的候选下拉。</summary>
        public bool HasPicker { get; private set; }
        /// <summary>候选只列目录（工作目录这类字段）。</summary>
        public bool PickDirectoriesOnly { get; private set; }
        /// <summary>源路径允许的扩展名（含点，小写）；为空表示不限。</summary>
        public IList<string> Extensions { get; private set; }
        public IList<FieldCondition> VisibleWhen { get; private set; }

        public ConfigField(string name, string label, FieldInputKind kind, bool required)
        {
            Name = name;
            Label = label;
            Kind = kind;
            Required = required;
            Options = new List<string>();
            Extensions = new List<string>();
            VisibleWhen = new List<FieldCondition>();
            Section = "基本信息";
            Check = FieldValidationKind.None;
        }

        public ConfigField WithHint(string hint)
        {
            Hint = hint;
            return this;
        }

        public ConfigField WithOptions(params string[] options)
        {
            foreach (var option in options)
            {
                Options.Add(option);
            }
            return this;
        }

        public ConfigField WithSection(string section)
        {
            Section = section;
            return this;
        }

        public ConfigField WithAllowEmpty()
        {
            AllowEmpty = true;
            return this;
        }

        public ConfigField WithRange(int min, int max)
        {
            MinValue = min;
            MaxValue = max;
            Check = FieldValidationKind.Integer;
            return this;
        }

        public ConfigField WithCheck(FieldValidationKind check)
        {
            Check = check;
            return this;
        }

        public ConfigField WithSourcePath(string prefix, params string[] extensions)
        {
            Check = FieldValidationKind.SourcePath;
            PathPrefix = prefix;
            foreach (var extension in extensions)
            {
                Extensions.Add(extension.ToLowerInvariant());
            }
            return this;
        }

        /// <summary>让界面为该路径字段提供 data 下现有条目的候选下拉。</summary>
        public ConfigField WithPicker(bool directoriesOnly = false)
        {
            HasPicker = true;
            PickDirectoriesOnly = directoriesOnly;
            return this;
        }

        /// <summary>只有条件字段等于指定值时才显示；已有值的字段仍会显示（由界面标为不适用）。</summary>
        public ConfigField WithVisibleWhen(string field, string value)
        {
            VisibleWhen.Add(new FieldCondition(field, value));
            return this;
        }

        public bool IsVisibleFor(IDictionary<string, string> values)
        {
            if (VisibleWhen.Count == 0)
            {
                return true;
            }
            foreach (var condition in VisibleWhen)
            {
                string current;
                if (values == null || !values.TryGetValue(condition.Field, out current) ||
                    !string.Equals((current ?? string.Empty).Trim(), condition.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// 九类步骤与配置级字段的唯一定义处；字段名必须与加载器接受的元素/属性一一对应。
    /// </summary>
    public static class ConfigFieldSchema
    {
        public static readonly string[] StepTypes =
        {
            "Install", "Copy", "Registry", "Command", "Service", "Network", "Power", "AutoStart", "Delay"
        };

        public static IList<ConfigField> ConfigFields()
        {
            return new List<ConfigField>
            {
                new ConfigField("mode", "模式", FieldInputKind.Enum, false).WithSection("发布").WithOptions("preview", "execute")
                    .WithHint("preview 只预检和展示；execute 才会真实执行。"),
                new ConfigField("name", "配置名称", FieldInputKind.Text, false).WithSection("发布"),
                new ConfigField("description", "配置说明", FieldInputKind.Text, false).WithSection("发布"),
                new ConfigField("logDirectory", "日志目录", FieldInputKind.Text, false).WithSection("日志")
                    .WithCheck(FieldValidationKind.RelativePath).WithHint("程序目录内的相对路径，默认 logs。")
            };
        }

        public static IList<ConfigField> GroupFields()
        {
            return new List<ConfigField>
            {
                new ConfigField("name", "分组名称", FieldInputKind.Text, true).WithSection("基本信息").WithHint("名称不能重复。")
            };
        }

        public static IList<ConfigField> TaskFields()
        {
            return new List<ConfigField>
            {
                new ConfigField("name", "任务名称", FieldInputKind.Text, true).WithSection("基本信息"),
                new ConfigField("enabled", "启用任务", FieldInputKind.Boolean, false).WithSection("基本信息")
                    .WithHint("取消勾选表示禁用：该任务不执行，也不计入进度。")
            };
        }

        public static IList<ConfigField> StepFields(string stepType)
        {
            var fields = new List<ConfigField>
            {
                new ConfigField("name", "步骤名称", FieldInputKind.Text, true).WithSection("基本信息")
            };

            switch (stepType)
            {
                case "Install":
                    fields.Add(new ConfigField("file", "安装包", FieldInputKind.Text, true)
                        .WithSection("安装来源").WithSourcePath("software/", ".exe", ".msi").WithPicker()
                        .WithHint("data/software 下的 .exe 或 .msi，例如 software/setup.exe。"));
                    fields.Add(new ConfigField("arguments", "安装参数", FieldInputKind.Text, false)
                        .WithSection("安装来源").WithHint("例如 /VERYSILENT /NORESTART。"));
                    fields.Add(new ConfigField("sha256", "SHA-256", FieldInputKind.Text, false)
                        .WithSection("安装来源").WithCheck(FieldValidationKind.Sha256)
                        .WithHint("64 位十六进制，留空则不校验。"));
                    fields.Add(new ConfigField("timeoutMinutes", "超时（分钟）", FieldInputKind.Integer, false)
                        .WithSection("执行").WithRange(1, 60).WithHint("1 到 60，默认 15。"));
                    fields.Add(new ConfigField("successExitCodes", "成功退出码", FieldInputKind.Text, false)
                        .WithSection("执行").WithCheck(FieldValidationKind.ExitCodes)
                        .WithHint("逗号分隔，例如 0,3010。"));
                    fields.Add(new ConfigField("skipIfFileExists", "已存在则跳过", FieldInputKind.Text, false)
                        .WithSection("执行").WithCheck(FieldValidationKind.DestinationPath)
                        .WithHint("绝对路径或带环境变量的路径；该文件存在时不重复安装。"));
                    break;
                case "Copy":
                    fields.Add(new ConfigField("source", "源路径", FieldInputKind.Text, true)
                        .WithSection("复制路径").WithSourcePath("files/").WithPicker()
                        .WithHint("data/files 下的相对路径，例如 files/app。"));
                    fields.Add(new ConfigField("target", "目标路径", FieldInputKind.Text, true)
                        .WithSection("复制路径").WithCheck(FieldValidationKind.DestinationPath)
                        .WithHint("绝对路径或 %TEMP% 等环境变量路径。"));
                    fields.Add(new ConfigField("overwrite", "覆盖方式", FieldInputKind.Enum, false)
                        .WithSection("行为").WithOptions("ifDifferent", "always", "ifNotExists").WithHint("默认 ifDifferent。"));
                    fields.Add(new ConfigField("backup", "先备份目标", FieldInputKind.Boolean, false)
                        .WithSection("行为").WithHint("true 或 false，默认 false。"));
                    break;
                case "Registry":
                    fields.Add(new ConfigField("hive", "注册表根", FieldInputKind.Enum, true)
                        .WithSection("注册表位置").WithOptions("HKCU", "HKLM", "HKCR", "HKU", "HKCC"));
                    fields.Add(new ConfigField("key", "项路径", FieldInputKind.Text, true)
                        .WithSection("注册表位置").WithHint("例如 Software/IPCDeployment。"));
                    fields.Add(new ConfigField("view", "注册表视图", FieldInputKind.Enum, false)
                        .WithSection("注册表位置").WithOptions("64", "32").WithHint("默认 64。"));
                    fields.Add(new ConfigField("valueName", "值名称", FieldInputKind.Text, true)
                        .WithSection("注册表值").WithAllowEmpty().WithHint("可留空字符串表示默认值。"));
                    fields.Add(new ConfigField("valueType", "值类型", FieldInputKind.Enum, true)
                        .WithSection("注册表值").WithOptions("String", "ExpandString", "DWord", "QWord"));
                    fields.Add(new ConfigField("value", "值", FieldInputKind.Text, true)
                        .WithSection("注册表值").WithAllowEmpty().WithCheck(FieldValidationKind.RegistryValue)
                        .WithHint("DWord/QWord 必须是整数。"));
                    fields.Add(new ConfigField("expectedBefore", "修改前期望值", FieldInputKind.Text, false)
                        .WithSection("期望").WithHint("当前值不等于它时按失败处理。"));
                    break;
                case "Command":
                    fields.Add(new ConfigField("file", "命令文件", FieldInputKind.Text, false)
                        .WithSection("命令").WithSourcePath("commands/", ".exe", ".cmd", ".bat", ".ps1").WithPicker()
                        .WithHint("data/commands 下的 .exe/.cmd/.bat/.ps1；与内嵌代码二选一。"));
                    fields.Add(new ConfigField("arguments", "参数", FieldInputKind.Text, false)
                        .WithSection("命令").WithHint("内嵌代码模式不接受参数，请写入代码。"));
                    fields.Add(new ConfigField("workingDirectory", "工作目录", FieldInputKind.Text, false)
                        .WithSection("命令").WithSourcePath("commands/").WithPicker(true)
                        .WithHint("data/commands 下的相对路径。"));
                    fields.Add(new ConfigField("timeoutMinutes", "超时（分钟）", FieldInputKind.Integer, false)
                        .WithSection("执行").WithRange(1, 60).WithHint("1 到 60，默认 10。"));
                    fields.Add(new ConfigField("successExitCodes", "成功退出码", FieldInputKind.Text, false)
                        .WithSection("执行").WithCheck(FieldValidationKind.ExitCodes)
                        .WithHint("逗号分隔，默认 0。"));
                    fields.Add(new ConfigField("warnExitCodes", "警告退出码", FieldInputKind.Text, false)
                        .WithSection("执行").WithCheck(FieldValidationKind.ExitCodes)
                        .WithHint("逗号分隔；命中时步骤标记警告，但不结束任务。"));
                    fields.Add(new ConfigField("language", "内嵌语言", FieldInputKind.Enum, false)
                        .WithSection("内嵌代码").WithOptions("cmd", "powershell").WithHint("仅内嵌代码模式填写。"));
                    fields.Add(new ConfigField("strict", "严格模式", FieldInputKind.Boolean, false)
                        .WithSection("内嵌代码").WithHint("内嵌 PowerShell 默认严格，填 false 关闭。"));
                    fields.Add(new ConfigField("code", "内嵌代码", FieldInputKind.Multiline, false)
                        .WithSection("内嵌代码")
                        .WithHint("填写后不要再填命令文件；脚本在临时文件中执行，结束后删除。"));
                    break;
                case "Service":
                    fields.Add(new ConfigField("serviceName", "服务名", FieldInputKind.Text, true)
                        .WithSection("服务"));
                    fields.Add(new ConfigField("startupType", "启动类型", FieldInputKind.Enum, false)
                        .WithSection("服务").WithOptions("Automatic", "Manual", "Disabled"));
                    fields.Add(new ConfigField("state", "运行状态", FieldInputKind.Enum, false)
                        .WithSection("服务").WithOptions("Running", "Stopped").WithHint("启动类型与运行状态至少要填一项。"));
                    break;
                case "Network":
                    fields.Add(new ConfigField("adapter", "网卡名称", FieldInputKind.Text, true)
                        .WithSection("网卡"));
                    fields.Add(new ConfigField("kind", "操作类型", FieldInputKind.Enum, true)
                        .WithSection("网卡").WithOptions("AdvancedProperty", "IPv4"));
                    fields.Add(new ConfigField("keyword", "高级属性名", FieldInputKind.Text, true)
                        .WithSection("高级属性").WithVisibleWhen("kind", "AdvancedProperty")
                        .WithHint("例如 *JumboPacket。"));
                    fields.Add(new ConfigField("value", "高级属性值", FieldInputKind.Text, true)
                        .WithSection("高级属性").WithAllowEmpty().WithCheck(FieldValidationKind.RegistryValue)
                        .WithVisibleWhen("kind", "AdvancedProperty"));
                    fields.Add(new ConfigField("valueType", "属性值类型", FieldInputKind.Enum, false)
                        .WithSection("高级属性").WithOptions("String", "ExpandString", "DWord", "QWord")
                        .WithVisibleWhen("kind", "AdvancedProperty"));
                    fields.Add(new ConfigField("expectedBefore", "高级属性期望值", FieldInputKind.Text, false)
                        .WithSection("高级属性").WithVisibleWhen("kind", "AdvancedProperty"));
                    fields.Add(new ConfigField("mode", "IPv4 模式", FieldInputKind.Enum, true)
                        .WithSection("IPv4").WithOptions("Static", "Dhcp")
                        .WithVisibleWhen("kind", "IPv4"));
                    fields.Add(new ConfigField("address", "IP 地址", FieldInputKind.Text, true)
                        .WithSection("IPv4 静态").WithCheck(FieldValidationKind.Ipv4)
                        .WithVisibleWhen("kind", "IPv4").WithVisibleWhen("mode", "Static")
                        .WithHint("IPv4 点分格式，例如 192.168.1.10。"));
                    fields.Add(new ConfigField("prefixLength", "前缀长度", FieldInputKind.Integer, true)
                        .WithSection("IPv4 静态").WithRange(1, 32)
                        .WithVisibleWhen("kind", "IPv4").WithVisibleWhen("mode", "Static"));
                    fields.Add(new ConfigField("gateway", "网关", FieldInputKind.Text, false)
                        .WithSection("IPv4 静态").WithCheck(FieldValidationKind.Ipv4)
                        .WithVisibleWhen("kind", "IPv4").WithVisibleWhen("mode", "Static"));
                    fields.Add(new ConfigField("dns", "DNS", FieldInputKind.Text, false)
                        .WithSection("IPv4 静态").WithCheck(FieldValidationKind.Ipv4List)
                        .WithVisibleWhen("kind", "IPv4").WithVisibleWhen("mode", "Static")
                        .WithHint("逗号分隔的 IPv4 地址。"));
                    break;
                case "Power":
                    fields.Add(new ConfigField("setting", "电源设置", FieldInputKind.Enum, true)
                        .WithSection("电源").WithOptions("Hibernate", "FastStartup", "PowerThrottling"));
                    fields.Add(new ConfigField("value", "目标值", FieldInputKind.Enum, true)
                        .WithSection("电源").WithOptions("On", "Off"));
                    break;
                case "AutoStart":
                    fields.Add(new ConfigField("hive", "注册表根", FieldInputKind.Enum, true)
                        .WithSection("自启动位置").WithOptions("HKCU", "HKLM"));
                    fields.Add(new ConfigField("valueName", "值名称", FieldInputKind.Text, true)
                        .WithSection("自启动位置"));
                    fields.Add(new ConfigField("command", "启动命令", FieldInputKind.Text, true)
                        .WithSection("启动命令").WithCheck(FieldValidationKind.DestinationPath)
                        .WithHint("绝对路径或带环境变量的路径。"));
                    fields.Add(new ConfigField("arguments", "命令参数", FieldInputKind.Text, false)
                        .WithSection("启动命令"));
                    fields.Add(new ConfigField("enabled", "启用自启", FieldInputKind.Boolean, false)
                        .WithSection("启动命令").WithHint("true 或 false，默认 true。"));
                    break;
                case "Delay":
                    fields.Add(new ConfigField("seconds", "等待秒数", FieldInputKind.Integer, true)
                        .WithSection("等待").WithRange(1, 600).WithHint("1 到 600。"));
                    break;
            }
            return fields;
        }

        public static XElement BuildStepElement(string stepType, string id, string name)
        {
            // 只填 id/name 与无害默认值；具体目标由用户填写，避免默认值被误当真。
            var element = new XElement(stepType,
                new XAttribute("id", id),
                new XAttribute("name", name));
            if (stepType == "Delay")
            {
                element.SetAttributeValue("seconds", "1");
            }
            if (stepType == "Install")
            {
                element.SetAttributeValue("timeoutMinutes", "15");
                element.SetAttributeValue("successExitCodes", "0");
            }
            if (stepType == "Command")
            {
                element.SetAttributeValue("timeoutMinutes", "10");
                element.SetAttributeValue("successExitCodes", "0");
            }
            if (stepType == "Copy")
            {
                element.SetAttributeValue("overwrite", "ifDifferent");
                element.SetAttributeValue("backup", "false");
            }
            if (stepType == "Registry")
            {
                element.SetAttributeValue("view", "64");
            }
            if (stepType == "AutoStart")
            {
                element.SetAttributeValue("enabled", "false");
            }
            return element;
        }
    }
}
