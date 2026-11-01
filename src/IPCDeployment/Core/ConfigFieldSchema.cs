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

    public sealed class ConfigField
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string Hint { get; set; }
        public FieldInputKind Kind { get; set; }
        public IList<string> Options { get; set; }
        public bool Required { get; set; }
        public bool ReadOnly { get; set; }

        public ConfigField(string name, string label, FieldInputKind kind, bool required)
        {
            Name = name;
            Label = label;
            Kind = kind;
            Required = required;
            Options = new List<string>();
        }

        public ConfigField WithHint(string hint)
        {
            Hint = hint;
            return this;
        }

        public ConfigField WithReadOnly()
        {
            ReadOnly = true;
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
                new ConfigField("mode", "模式", FieldInputKind.Enum, false).WithOptions("preview", "execute")
                    .WithHint("preview 只预检和展示；execute 才会真实执行。"),
                new ConfigField("name", "配置名称", FieldInputKind.Text, false),
                new ConfigField("description", "配置说明", FieldInputKind.Text, false),
                new ConfigField("logDirectory", "日志目录", FieldInputKind.Text, false)
                    .WithHint("程序目录内的相对路径，默认 logs。")
            };
        }

        public static IList<ConfigField> GroupFields()
        {
            return new List<ConfigField>
            {
                new ConfigField("key", "分组键", FieldInputKind.Text, true)
                    .WithHint("新增时自动生成，只读；用于与日志对应。").WithReadOnly(),
                new ConfigField("name", "分组名称", FieldInputKind.Text, true).WithHint("名称不能重复。")
            };
        }

        public static IList<ConfigField> TaskFields()
        {
            return new List<ConfigField>
            {
                new ConfigField("key", "任务键", FieldInputKind.Text, true)
                    .WithHint("新增时自动生成，只读；全局唯一。").WithReadOnly(),
                new ConfigField("name", "任务名称", FieldInputKind.Text, true)
            };
        }

        public static IList<ConfigField> StepFields(string stepType)
        {
            var fields = new List<ConfigField>
            {
                new ConfigField("id", "步骤 id", FieldInputKind.Text, true)
                    .WithHint("新增时自动生成，只读；全局唯一。").WithReadOnly(),
                new ConfigField("name", "步骤名称", FieldInputKind.Text, true)
            };

            switch (stepType)
            {
                case "Install":
                    fields.Add(new ConfigField("file", "安装包", FieldInputKind.Text, true)
                        .WithHint("data 相对路径，例如 software/setup.exe；支持 .exe 和 .msi。"));
                    fields.Add(new ConfigField("arguments", "安装参数", FieldInputKind.Text, false)
                        .WithHint("例如 /VERYSILENT /NORESTART。"));
                    fields.Add(new ConfigField("sha256", "SHA-256", FieldInputKind.Text, false)
                        .WithHint("64 位十六进制，留空则不校验。"));
                    fields.Add(new ConfigField("timeoutMinutes", "超时（分钟）", FieldInputKind.Integer, false)
                        .WithHint("1 到 60，默认 15。"));
                    fields.Add(new ConfigField("successExitCodes", "成功退出码", FieldInputKind.Text, false)
                        .WithHint("逗号分隔，例如 0,3010。"));
                    fields.Add(new ConfigField("skipIfFileExists", "已存在则跳过", FieldInputKind.Text, false)
                        .WithHint("绝对路径或带环境变量的路径；该文件存在时不重复安装。"));
                    break;
                case "Copy":
                    fields.Add(new ConfigField("source", "源路径", FieldInputKind.Text, true)
                        .WithHint("data 相对路径，以 files/ 开头。"));
                    fields.Add(new ConfigField("target", "目标路径", FieldInputKind.Text, true)
                        .WithHint("绝对路径或 %TEMP% 等环境变量路径。"));
                    fields.Add(new ConfigField("overwrite", "覆盖方式", FieldInputKind.Enum, false)
                        .WithOptions("ifDifferent", "always", "ifNotExists").WithHint("默认 ifDifferent。"));
                    fields.Add(new ConfigField("backup", "先备份目标", FieldInputKind.Boolean, false)
                        .WithHint("true 或 false，默认 false。"));
                    break;
                case "Registry":
                    fields.Add(new ConfigField("hive", "注册表根", FieldInputKind.Enum, true)
                        .WithOptions("HKCU", "HKLM", "HKCR", "HKU", "HKCC"));
                    fields.Add(new ConfigField("key", "项路径", FieldInputKind.Text, true)
                        .WithHint("例如 Software/IPCDeployment。"));
                    fields.Add(new ConfigField("valueName", "值名称", FieldInputKind.Text, true)
                        .WithHint("可留空字符串表示默认值。"));
                    fields.Add(new ConfigField("valueType", "值类型", FieldInputKind.Enum, true)
                        .WithOptions("String", "ExpandString", "DWord", "QWord"));
                    fields.Add(new ConfigField("value", "值", FieldInputKind.Text, true)
                        .WithHint("DWord/QWord 必须是整数。"));
                    fields.Add(new ConfigField("view", "注册表视图", FieldInputKind.Enum, false)
                        .WithOptions("64", "32").WithHint("默认 64。"));
                    fields.Add(new ConfigField("expectedBefore", "修改前期望值", FieldInputKind.Text, false)
                        .WithHint("当前值不等于它时按失败处理。"));
                    break;
                case "Command":
                    fields.Add(new ConfigField("file", "命令文件", FieldInputKind.Text, false)
                        .WithHint("data/commands 下的 .exe/.cmd/.bat/.ps1；与内嵌代码二选一。"));
                    fields.Add(new ConfigField("arguments", "参数", FieldInputKind.Text, false)
                        .WithHint("内嵌代码模式不接受参数，请写入代码。"));
                    fields.Add(new ConfigField("workingDirectory", "工作目录", FieldInputKind.Text, false)
                        .WithHint("data/commands 下的相对路径。"));
                    fields.Add(new ConfigField("timeoutMinutes", "超时（分钟）", FieldInputKind.Integer, false)
                        .WithHint("1 到 60，默认 10。"));
                    fields.Add(new ConfigField("successExitCodes", "成功退出码", FieldInputKind.Text, false)
                        .WithHint("逗号分隔，默认 0。"));
                    fields.Add(new ConfigField("language", "内嵌语言", FieldInputKind.Enum, false)
                        .WithOptions("cmd", "powershell").WithHint("仅内嵌代码模式填写。"));
                    fields.Add(new ConfigField("strict", "严格模式", FieldInputKind.Boolean, false)
                        .WithHint("内嵌 PowerShell 默认严格，填 false 关闭。"));
                    fields.Add(new ConfigField("code", "内嵌代码", FieldInputKind.Multiline, false)
                        .WithHint("填写后不要再填命令文件；脚本在临时文件中执行，结束后删除。"));
                    break;
                case "Service":
                    fields.Add(new ConfigField("serviceName", "服务名", FieldInputKind.Text, true));
                    fields.Add(new ConfigField("startupType", "启动类型", FieldInputKind.Enum, false)
                        .WithOptions("Automatic", "Manual", "Disabled"));
                    fields.Add(new ConfigField("state", "运行状态", FieldInputKind.Enum, false)
                        .WithOptions("Running", "Stopped").WithHint("启动类型与运行状态至少要填一项。"));
                    break;
                case "Network":
                    fields.Add(new ConfigField("adapter", "网卡名称", FieldInputKind.Text, true));
                    fields.Add(new ConfigField("kind", "操作类型", FieldInputKind.Enum, true)
                        .WithOptions("AdvancedProperty", "IPv4"));
                    fields.Add(new ConfigField("keyword", "高级属性名", FieldInputKind.Text, false)
                        .WithHint("AdvancedProperty 必填，例如 *JumboPacket。"));
                    fields.Add(new ConfigField("value", "高级属性值", FieldInputKind.Text, false)
                        .WithHint("AdvancedProperty 必填。"));
                    fields.Add(new ConfigField("valueType", "属性值类型", FieldInputKind.Enum, false)
                        .WithOptions("String", "ExpandString", "DWord", "QWord"));
                    fields.Add(new ConfigField("mode", "IPv4 模式", FieldInputKind.Enum, false)
                        .WithOptions("Static", "Dhcp").WithHint("IPv4 必填。"));
                    fields.Add(new ConfigField("address", "IP 地址", FieldInputKind.Text, false)
                        .WithHint("Static 模式必填，IPv4 点分格式。"));
                    fields.Add(new ConfigField("prefixLength", "前缀长度", FieldInputKind.Integer, false)
                        .WithHint("Static 模式必填，1 到 32。"));
                    fields.Add(new ConfigField("gateway", "网关", FieldInputKind.Text, false));
                    fields.Add(new ConfigField("dns", "DNS", FieldInputKind.Text, false)
                        .WithHint("逗号分隔的 IPv4 地址。"));
                    fields.Add(new ConfigField("expectedBefore", "高级属性期望值", FieldInputKind.Text, false));
                    break;
                case "Power":
                    fields.Add(new ConfigField("setting", "电源设置", FieldInputKind.Enum, true)
                        .WithOptions("Hibernate", "FastStartup", "PowerThrottling"));
                    fields.Add(new ConfigField("value", "目标值", FieldInputKind.Enum, true)
                        .WithOptions("On", "Off"));
                    break;
                case "AutoStart":
                    fields.Add(new ConfigField("hive", "注册表根", FieldInputKind.Enum, true)
                        .WithOptions("HKCU", "HKLM"));
                    fields.Add(new ConfigField("valueName", "值名称", FieldInputKind.Text, true));
                    fields.Add(new ConfigField("command", "启动命令", FieldInputKind.Text, true)
                        .WithHint("绝对路径或带环境变量的路径。"));
                    fields.Add(new ConfigField("arguments", "命令参数", FieldInputKind.Text, false));
                    fields.Add(new ConfigField("enabled", "启用自启", FieldInputKind.Boolean, false)
                        .WithHint("true 或 false，默认 true。"));
                    break;
                case "Delay":
                    fields.Add(new ConfigField("seconds", "等待秒数", FieldInputKind.Integer, true)
                        .WithHint("1 到 600。"));
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
