using System;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32;

namespace IPCDeployment.Core
{
    internal static class ExecutorUtility
    {
        public static bool IsSuccess(int exitCode, int[] successExitCodes)
        {
            if (exitCode == 3010 || exitCode == 1641)
            {
                return true;
            }
            if (successExitCodes == null)
            {
                return exitCode == 0;
            }
            return successExitCodes.Contains(exitCode);
        }

        public static bool RequiresRestart(int exitCode)
        {
            return exitCode == 3010 || exitCode == 1641;
        }

        public static string SummarizeProcess(ProcessResult result)
        {
            if (result == null)
            {
                return "没有进程结果。";
            }
            if (result.FailedToStart)
            {
                return "无法启动进程：" + Shorten(LogRedaction.Redact(result.Error), 400);
            }
            if (result.TimedOut)
            {
                return "进程超时，已终止进程树。";
            }
            if (result.Cancelled)
            {
                return "操作已取消，已终止进程树。";
            }

            var builder = new StringBuilder();
            builder.Append("退出码 ");
            builder.Append(result.ExitCode);
            var output = CombineOutput(result);
            if (!string.IsNullOrWhiteSpace(output))
            {
                builder.Append("；输出：");
                builder.Append(Shorten(LogRedaction.Redact(output), 400));
            }
            return builder.ToString();
        }

        public static string CombineOutput(ProcessResult result)
        {
            var text = ((result.Output ?? string.Empty) + " " + (result.Error ?? string.Empty)).Trim();
            return text.Replace("\r", " ").Replace("\n", " ");
        }

        public static string Shorten(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximum)
            {
                return value ?? string.Empty;
            }
            return value.Substring(0, maximum) + "...";
        }

        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            if (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))
            {
                return value;
            }
            return value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
        }
    }

    internal static class RegistryAccess
    {
        public static RegistryKey OpenBaseKey(RegistryHiveKind hive, int view)
        {
            var registryHive = RegistryHive.CurrentUser;
            if (hive == RegistryHiveKind.HKLM) registryHive = RegistryHive.LocalMachine;
            else if (hive == RegistryHiveKind.HKCR) registryHive = RegistryHive.ClassesRoot;
            else if (hive == RegistryHiveKind.HKU) registryHive = RegistryHive.Users;
            else if (hive == RegistryHiveKind.HKCC) registryHive = RegistryHive.CurrentConfig;

            var registryView = view == 32 ? RegistryView.Registry32 : RegistryView.Registry64;
            return RegistryKey.OpenBaseKey(registryHive, registryView);
        }

        public static object ReadValue(RegistryKey key, string valueName)
        {
            if (key == null)
            {
                return null;
            }
            return key.GetValue(valueName ?? string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        public static bool ValuesEqual(object current, string expected, RegistryValueType type)
        {
            if (current == null)
            {
                return false;
            }

            if (type == RegistryValueType.DWord)
            {
                int currentNumber;
                int expectedNumber;
                return TryConvertInt(current, out currentNumber) &&
                       int.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedNumber) &&
                       currentNumber == expectedNumber;
            }

            if (type == RegistryValueType.QWord)
            {
                long currentNumber;
                long expectedNumber;
                return TryConvertLong(current, out currentNumber) &&
                       long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedNumber) &&
                       currentNumber == expectedNumber;
            }

            return string.Equals(Convert.ToString(current, CultureInfo.InvariantCulture), expected ?? string.Empty, StringComparison.Ordinal);
        }

        public static RegistryValueKind ToKind(RegistryValueType type)
        {
            if (type == RegistryValueType.ExpandString) return RegistryValueKind.ExpandString;
            if (type == RegistryValueType.DWord) return RegistryValueKind.DWord;
            if (type == RegistryValueType.QWord) return RegistryValueKind.QWord;
            return RegistryValueKind.String;
        }

        public static object ToValue(string value, RegistryValueType type)
        {
            if (type == RegistryValueType.DWord)
            {
                return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            if (type == RegistryValueType.QWord)
            {
                return long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            return value ?? string.Empty;
        }

        public static void SetValue(RegistryKey key, string valueName, string value, RegistryValueType type)
        {
            key.SetValue(valueName ?? string.Empty, ToValue(value, type), ToKind(type));
        }

        public static int ReadServiceStart(string serviceName)
        {
            using (var key = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + serviceName, false))
            {
                if (key == null)
                {
                    return -1;
                }
                var value = key.GetValue("Start", -1);
                int number;
                return TryConvertInt(value, out number) ? number : -1;
            }
        }

        public static bool TryConvertInt(object value, out int number)
        {
            number = 0;
            if (value == null)
            {
                return false;
            }
            try
            {
                number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryConvertLong(object value, out long number)
        {
            number = 0;
            if (value == null)
            {
                return false;
            }
            try
            {
                number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal static class AdapterUtility
    {
        public static NetworkInterface Find(string adapterName)
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, adapterName, StringComparison.Ordinal));
        }

        public static string FindDriverKey(string adapterId)
        {
            const string classRoot = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e972-e325-11ce-bfc1-08002be10318}";
            using (var classKey = Registry.LocalMachine.OpenSubKey(classRoot, false))
            {
                if (classKey == null)
                {
                    return null;
                }

                foreach (var subKeyName in classKey.GetSubKeyNames())
                {
                    using (var candidate = classKey.OpenSubKey(subKeyName, false))
                    {
                        if (candidate == null)
                        {
                            continue;
                        }
                        var instanceId = Convert.ToString(candidate.GetValue("NetCfgInstanceId"), CultureInfo.InvariantCulture);
                        if (string.Equals(instanceId, adapterId, StringComparison.OrdinalIgnoreCase))
                        {
                            return classRoot + "\\" + subKeyName;
                        }
                    }
                }
            }
            return null;
        }

        public static string PrefixToMask(int prefixLength)
        {
            var mask = prefixLength == 0 ? 0u : 0xFFFFFFFFu << (32 - prefixLength);
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}",
                (mask >> 24) & 0xFF,
                (mask >> 16) & 0xFF,
                (mask >> 8) & 0xFF,
                mask & 0xFF);
        }
    }
}
