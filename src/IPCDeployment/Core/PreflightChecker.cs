using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;

namespace IPCDeployment.Core
{
    public sealed class PreflightChecker
    {
        private readonly PathPolicy paths;

        public PreflightChecker(PathPolicy pathPolicy)
        {
            paths = pathPolicy ?? new PathPolicy();
        }

        public PreflightResult Check(DeploymentConfig config, string packageRoot, IPreflightEnvironment environment)
        {
            var result = new PreflightResult();
            if (!environment.IsAdministrator)
            {
                AddError(result, 0, 0, "未以管理员身份运行；execute 模式需要管理员权限。");
            }

            try
            {
                var logDirectory = paths.ResolveLogDirectory(packageRoot, config.LogDirectory);
                if (!environment.CanWriteLogDirectory(logDirectory))
                {
                    AddWarning(result, 0, 0, "日志目录不可写；运行时将回退到临时目录。");
                }
            }
            catch (Exception exception)
            {
                AddError(result, 0, 0, "日志目录无效：" + exception.Message);
            }

            // 禁用任务不进入执行队列，其运行期预检（哈希、服务/网卡/自启目标等）也不参与，
            // 避免已停用的选装任务因本机环境缺失挡住整份配置执行。
            var disabledTaskKeys = new HashSet<string>(
                config.Tasks.Where(task => !task.Enabled && task.Key != null).Select(task => task.Key),
                StringComparer.OrdinalIgnoreCase);
            var checkedOperations = config.Operations
                .Where(operation => !IsDisabled(operation, disabledTaskKeys))
                .ToList();

            foreach (var operation in checkedOperations)
            {
                var install = operation as InstallOperation;
                if (install != null)
                {
                    CheckInstallerHash(install, result);
                }

                var service = operation as ServiceOperation;
                if (service != null && !environment.IsServicePresent(service.ServiceName))
                {
                    AddWarning(result, operation, "服务当前不存在：" + service.ServiceName + "；可能由前面的安装创建，执行时仍不存在会失败。");
                }

                var autoStart = operation as AutoStartOperation;
                if (autoStart != null && autoStart.Enabled && !File.Exists(Environment.ExpandEnvironmentVariables(autoStart.Command)))
                {
                    AddWarning(result, operation, "自启目标当前不存在：" + autoStart.Command + "；可能在后续安装后创建。");
                }

                var network = operation as NetworkOperation;
                if (network != null && !environment.IsAdapterPresent(network.Adapter))
                {
                    AddError(result, operation, "未找到网卡：" + network.Adapter + "。");
                }

                var command = operation as CommandOperation;
                if (command != null)
                {
                    if (command.Language == CommandLanguage.Cmd && !File.Exists(CommandInterpreter.CmdPath))
                    {
                        AddError(result, operation, "未找到 cmd.exe：" + CommandInterpreter.CmdPath + "。");
                    }
                    if (command.Language == CommandLanguage.PowerShell &&
                        !File.Exists(CommandInterpreter.PowerShellPath))
                    {
                        AddError(result, operation, "未找到 powershell.exe：" + CommandInterpreter.PowerShellPath + "。");
                    }
                }
            }

            CheckAdvancedPropertyOrder(checkedOperations, result);
            CheckPowerOrder(checkedOperations, result);
            return result;
        }

        private static bool IsDisabled(DeploymentOperation operation, ISet<string> disabledTaskKeys)
        {
            return operation != null && !string.IsNullOrEmpty(operation.TaskKey) &&
                disabledTaskKeys.Contains(operation.TaskKey);
        }

        private static void CheckInstallerHash(InstallOperation operation, PreflightResult result)
        {
            if (string.IsNullOrWhiteSpace(operation.Sha256) || string.IsNullOrWhiteSpace(operation.ResolvedFile))
            {
                return;
            }
            try
            {
                using (var stream = File.OpenRead(operation.ResolvedFile))
                using (var sha = SHA256.Create())
                {
                    var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                    if (!string.Equals(actual, operation.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        AddError(result, operation, "安装包哈希不匹配；期望 " + operation.Sha256 + "，实际 " + actual + "。");
                    }
                }
            }
            catch (Exception exception)
            {
                AddError(result, operation, "无法校验安装包哈希：" + exception.Message);
            }
        }

        private static void CheckAdvancedPropertyOrder(IList<DeploymentOperation> operations, PreflightResult result)
        {
            var networks = operations.OfType<NetworkOperation>()
                .Where(n => n.Kind == NetworkOperationKind.AdvancedProperty)
                .ToList();

            foreach (var speed in networks)
            {
                if (!string.Equals(speed.Keyword, "*SpeedDuplex", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var earlier = networks.FirstOrDefault(n =>
                    n.Order < speed.Order &&
                    string.Equals(n.Adapter, speed.Adapter, StringComparison.Ordinal) &&
                    !string.Equals(n.Keyword, "*SpeedDuplex", StringComparison.OrdinalIgnoreCase));
                if (earlier != null)
                {
                    AddError(result, speed, "同一网卡的 SpeedDuplex 必须排在其他高级属性之前；当前前面还有 " + earlier.Id + "。");
                }
            }
        }

        private static void CheckPowerOrder(IList<DeploymentOperation> operations, PreflightResult result)
        {
            var firstPower = operations.FirstOrDefault(op => op is PowerOperation);
            if (firstPower == null)
            {
                return;
            }

            var later = operations.FirstOrDefault(op => op.Order > firstPower.Order && !(op is PowerOperation));
            if (later != null)
            {
                AddWarning(result, firstPower, "Power 操作之后仍有其他操作；推荐把电源设置放在队列末尾。");
            }
        }

        private static void AddError(PreflightResult result, DeploymentOperation operation, string message)
        {
            Add(result, operation, message, false);
        }

        private static void AddWarning(PreflightResult result, DeploymentOperation operation, string message)
        {
            Add(result, operation, message, true);
        }

        private static void AddError(PreflightResult result, int line, int column, string message)
        {
            Add(result, line, column, message, false);
        }

        private static void AddWarning(PreflightResult result, int line, int column, string message)
        {
            Add(result, line, column, message, true);
        }

        private static void Add(PreflightResult result, DeploymentOperation operation, string message, bool warning)
        {
            Add(result, operation == null ? 0 : operation.Line, operation == null ? 0 : operation.Column, message, warning);
        }

        private static void Add(PreflightResult result, int line, int column, string message, bool warning)
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
    }

    public sealed class WindowsPreflightEnvironment : IPreflightEnvironment
    {
        public bool IsAdministrator
        {
            get
            {
                try
                {
                    using (var identity = WindowsIdentity.GetCurrent())
                    {
                        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public bool IsAdapterPresent(string adapterName)
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Any(n => string.Equals(n.Name, adapterName, StringComparison.Ordinal));
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool IsServicePresent(string serviceName)
        {
            try
            {
                return ServiceController.GetServices()
                    .Any(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool CanWriteLogDirectory(string logDirectory)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                var probe = Path.Combine(logDirectory, ".write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
