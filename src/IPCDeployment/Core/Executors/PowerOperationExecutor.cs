using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace IPCDeployment.Core
{
    public sealed class PowerOperationExecutor : IOperationExecutor
    {
        public string Type
        {
            get { return "Power"; }
        }

        public async Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            var power = operation as PowerOperation;
            if (power == null)
            {
                return OperationResult.Failed(operation.Id, "操作类型不匹配。");
            }

            try
            {
                if (power.Setting == PowerSetting.Hibernate)
                {
                    return await SetHibernateAsync(power, context, cancellationToken);
                }
                if (power.Setting == PowerSetting.FastStartup)
                {
                    return SetDword(power, context, "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power",
                        "HiberbootEnabled", power.Value == PowerValue.On, false, true, "快速启动");
                }
                // PowerThrottlingOff 是反向值：On 对应 0，Off 对应 1。
                return SetDword(power, context, "SYSTEM\\CurrentControlSet\\Control\\Power\\PowerThrottling",
                    "PowerThrottlingOff", power.Value == PowerValue.On, true, true, "电源节流");
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult.Failed(power.Id, "没有修改电源设置的权限。");
            }
            catch (System.Security.SecurityException)
            {
                return OperationResult.Failed(power.Id, "电源设置被安全策略拒绝。");
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Cancelled(power.Id, "电源操作已取消。");
            }
            catch (Exception exception)
            {
                return OperationResult.Failed(power.Id, "电源操作失败：" + exception.Message);
            }
        }

        private static async Task<OperationResult> SetHibernateAsync(PowerOperation power, OperationContext context,
            CancellationToken cancellationToken)
        {
            var target = power.Value == PowerValue.On ? 1 : 0;
            var current = ReadDword("SYSTEM\\CurrentControlSet\\Control\\Power", "HibernateEnabled");
            if (current.HasValue && current.Value == target)
            {
                return OperationResult.AlreadySatisfied(power.Id, "休眠设置已满足。");
            }

            context.Log("INFO", "执行 powercfg /hibernate " + (target == 1 ? "on" : "off"));
            var result = await context.ProcessRunner.RunAsync(new ProcessRequest
            {
                FileName = SystemTools.Get("powercfg.exe"),
                Arguments = "/hibernate " + (target == 1 ? "on" : "off"),
                TimeoutMinutes = 2
            }, line => context.Log("INFO", line), cancellationToken);

            if (result.Cancelled)
            {
                return OperationResult.Cancelled(power.Id, ExecutorUtility.SummarizeProcess(result));
            }
            if (result.TimedOut || result.FailedToStart || result.ExitCode != 0)
            {
                return OperationResult.Failed(power.Id, "powercfg 执行失败：" + ExecutorUtility.SummarizeProcess(result));
            }

            var after = ReadDword("SYSTEM\\CurrentControlSet\\Control\\Power", "HibernateEnabled");
            if (!after.HasValue || after.Value != target)
            {
                var written = WriteDword("SYSTEM\\CurrentControlSet\\Control\\Power", "HibernateEnabled", target);
                if (!written)
                {
                    return OperationResult.Failed(power.Id, "休眠设置写入后回读不一致。");
                }
            }

            return OperationResult.Succeeded(power.Id, target == 1 ? "休眠已开启。" : "休眠已关闭。", false);
        }

        private static OperationResult SetDword(PowerOperation power, OperationContext context, string keyPath,
            string valueName, bool enabled, bool invertRawValue, bool restartRequired, string label)
        {
            var target = invertRawValue ? (enabled ? 0 : 1) : (enabled ? 1 : 0);
            var current = ReadDword(keyPath, valueName);
            if (current.HasValue && current.Value == target)
            {
                return OperationResult.AlreadySatisfied(power.Id, label + "设置已满足。");
            }

            if (!WriteDword(keyPath, valueName, target))
            {
                return OperationResult.Failed(power.Id, label + "写入失败。");
            }

            var after = ReadDword(keyPath, valueName);
            if (!after.HasValue || after.Value != target)
            {
                return OperationResult.Failed(power.Id, label + "写入后回读不一致。");
            }

            context.Log("INFO", label + " 已设置为" + (enabled ? "开启" : "关闭") + "。");
            return OperationResult.Succeeded(power.Id, label + "已设置为" + (enabled ? "开启" : "关闭") + "。",
                restartRequired);
        }

        private static int? ReadDword(string keyPath, string valueName)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = baseKey.OpenSubKey(keyPath, false))
            {
                if (key == null)
                {
                    return null;
                }
                var value = key.GetValue(valueName, null);
                int number;
                return RegistryAccess.TryConvertInt(value, out number) ? number : (int?)null;
            }
        }

        private static bool WriteDword(string keyPath, string valueName, int value)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = baseKey.CreateSubKey(keyPath))
            {
                if (key == null)
                {
                    return false;
                }
                key.SetValue(valueName, value, RegistryValueKind.DWord);
                return true;
            }
        }
    }
}
