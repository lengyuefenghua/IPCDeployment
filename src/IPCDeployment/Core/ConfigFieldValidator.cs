using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace IPCDeployment.Core
{
    /// <summary>
    /// 输入提示用的轻校验，规则与 <see cref="ConfigLoader"/> 对齐但不替代它：
    /// 保存仍由加载器做完整校验，这里只负责让用户尽早看到明显错误。
    /// </summary>
    public static class ConfigFieldValidator
    {
        public static string Validate(ConfigField field, string value, IDictionary<string, string> values)
        {
            if (field == null)
            {
                return null;
            }

            var text = (value ?? string.Empty).Trim();
            if (field.Required && !field.AllowEmpty && text.Length == 0)
            {
                return "必填项不能为空。";
            }

            var crossField = ValidateCrossField(field, text, values);
            if (crossField != null)
            {
                return crossField;
            }

            if (text.Length == 0)
            {
                return null;
            }

            switch (field.Check)
            {
                case FieldValidationKind.Integer:
                    return ValidateInteger(field, text);
                case FieldValidationKind.Sha256:
                    return IsHex64(text) ? null : "必须是 64 位十六进制。";
                case FieldValidationKind.ExitCodes:
                    return IsExitCodeList(text) ? null : "必须是逗号分隔的整数，例如 0,3010。";
                case FieldValidationKind.SourcePath:
                    return ValidateSourcePath(field, text);
                case FieldValidationKind.DestinationPath:
                    return ValidateDestinationPath(text);
                case FieldValidationKind.Ipv4:
                    return IsIPv4(text) ? null : "必须是 IPv4 地址，例如 192.168.1.10。";
                case FieldValidationKind.Ipv4List:
                    return IsIpv4List(text) ? null : "必须是逗号分隔的 IPv4 地址。";
                case FieldValidationKind.RelativePath:
                    return new PathPolicy().IsSafeRelativePath(text) ? null : "必须是程序目录内的相对路径。";
                case FieldValidationKind.RegistryValue:
                    return ValidateRegistryValue(text, values);
                default:
                    return null;
            }
        }

        /// <summary>Command 与 Service 的跨字段规则；只有对应字段集存在时才生效。</summary>
        private static string ValidateCrossField(ConfigField field, string text, IDictionary<string, string> values)
        {
            string code;
            var isCommand = values != null && values.ContainsKey("code");
            if (isCommand)
            {
                values.TryGetValue("code", out code);
                var hasCode = HasValue(code);
                var hasText = HasValue(text);

                if (field.Name == "file" && hasCode && hasText)
                {
                    return "命令文件与内嵌代码只能二选一。";
                }
                if (field.Name == "file" && !hasCode && !hasText)
                {
                    return "命令文件与内嵌代码至少要填一项。";
                }
                if (field.Name == "arguments" && hasCode && hasText)
                {
                    return "内嵌代码模式不接受参数，请把参数写入代码。";
                }
                if (field.Name == "language" && !hasCode && hasText)
                {
                    return "只有内嵌代码模式才填写内嵌语言。";
                }
                if (field.Name == "language" && hasCode && !hasText)
                {
                    return "内嵌代码需要选择 cmd 或 powershell。";
                }
                if (field.Name == "code" && hasText)
                {
                    string file;
                    values.TryGetValue("file", out file);
                    if (HasValue(file))
                    {
                        return "命令文件与内嵌代码只能二选一。";
                    }
                }
            }

            if (values != null && values.ContainsKey("startupType") && values.ContainsKey("state"))
            {
                string startupType;
                string state;
                values.TryGetValue("startupType", out startupType);
                values.TryGetValue("state", out state);
                if ((field.Name == "startupType" || field.Name == "state") &&
                    !HasValue(startupType) && !HasValue(state))
                {
                    return "启动类型与运行状态至少填一项。";
                }
            }

            return null;
        }

        private static string ValidateInteger(ConfigField field, string text)
        {
            int number;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return "必须是整数。";
            }
            if (field.MinValue.HasValue && field.MaxValue.HasValue)
            {
                return number < field.MinValue.Value || number > field.MaxValue.Value
                    ? "必须在 " + field.MinValue.Value + " 到 " + field.MaxValue.Value + " 之间。"
                    : null;
            }
            if (field.MinValue.HasValue && number < field.MinValue.Value)
            {
                return "不能小于 " + field.MinValue.Value + "。";
            }
            if (field.MaxValue.HasValue && number > field.MaxValue.Value)
            {
                return "不能大于 " + field.MaxValue.Value + "。";
            }
            return null;
        }

        private static string ValidateSourcePath(ConfigField field, string text)
        {
            if (!new PathPolicy().IsSafeRelativePath(text))
            {
                return "必须是 data 下的相对路径，不能是绝对路径或包含 ..。";
            }

            var normalized = text.Replace('\\', '/');
            if (!string.IsNullOrEmpty(field.PathPrefix) &&
                !normalized.StartsWith(field.PathPrefix.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return "必须以 " + field.PathPrefix + " 开头。";
            }

            if (field.Extensions.Count > 0)
            {
                var extension = Path.GetExtension(normalized).ToLowerInvariant();
                if (!Contains(field.Extensions, extension))
                {
                    return "只支持 " + string.Join("、", field.Extensions) + "。";
                }
            }

            return null;
        }

        private static string ValidateDestinationPath(string text)
        {
            try
            {
                new PathPolicy().ResolveDestination(text);
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        private static string ValidateRegistryValue(string text, IDictionary<string, string> values)
        {
            string valueType;
            if (values == null || !values.TryGetValue("valueType", out valueType))
            {
                return null;
            }

            if (string.Equals(valueType, "DWord", StringComparison.OrdinalIgnoreCase))
            {
                int number;
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                    ? null
                    : "DWord 值必须是 32 位整数。";
            }
            if (string.Equals(valueType, "QWord", StringComparison.OrdinalIgnoreCase))
            {
                long number;
                return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                    ? null
                    : "QWord 值必须是 64 位整数。";
            }
            return null;
        }

        private static bool HasValue(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        private static bool Contains(IList<string> items, string value)
        {
            foreach (var item in items)
            {
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsHex64(string value)
        {
            if (value.Length != 64)
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

        private static bool IsExitCodeList(string value)
        {
            var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }
            foreach (var part in parts)
            {
                int code;
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
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

        private static bool IsIpv4List(string value)
        {
            var parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }
            foreach (var part in parts)
            {
                if (!IsIPv4(part.Trim()))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
