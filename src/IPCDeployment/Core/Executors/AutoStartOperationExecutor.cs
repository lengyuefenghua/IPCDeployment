using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace IPCDeployment.Core
{
    public sealed class AutoStartOperationExecutor : IOperationExecutor
    {
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        public string Type
        {
            get { return "AutoStart"; }
        }

        public Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            var autoStart = operation as AutoStartOperation;
            if (autoStart == null)
            {
                return Task.FromResult(OperationResult.Failed(operation.Id, "操作类型不匹配。"));
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hive = autoStart.Hive == RegistryHiveKind.HKLM ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                {
                    if (autoStart.Enabled)
                    {
                        var target = ExecutorUtility.Quote(autoStart.Command) +
                            (string.IsNullOrWhiteSpace(autoStart.Arguments) ? string.Empty : " " + autoStart.Arguments);
                        using (var key = baseKey.CreateSubKey(RunKey))
                        {
                            if (key == null)
                            {
                                return Task.FromResult(OperationResult.Failed(autoStart.Id, "无法打开 Run 注册表项。"));
                            }

                            var current = RegistryAccess.ReadValue(key, autoStart.ValueName);
                            context.Log("INFO", "自启写前值：" + Format(current));
                            if (string.Equals(Format(current), target, StringComparison.Ordinal))
                            {
                                return Task.FromResult(OperationResult.AlreadySatisfied(autoStart.Id, "自启值已存在，已满足。"));
                            }

                            key.SetValue(autoStart.ValueName, target, RegistryValueKind.ExpandString);
                            var after = RegistryAccess.ReadValue(key, autoStart.ValueName);
                            if (!string.Equals(Format(after), target, StringComparison.Ordinal))
                            {
                                return Task.FromResult(OperationResult.Failed(autoStart.Id, "自启值写入后回读不一致。"));
                            }

                            context.Log("INFO", "自启写后回读：" + Format(after));
                            return Task.FromResult(OperationResult.Succeeded(autoStart.Id, "开机自启已写入；下次登录生效。", false));
                        }
                    }

                    using (var key = baseKey.OpenSubKey(RunKey, true))
                    {
                        if (key == null || key.GetValue(autoStart.ValueName, null) == null)
                        {
                            return Task.FromResult(OperationResult.AlreadySatisfied(autoStart.Id, "自启值不存在，无需删除。"));
                        }

                        key.DeleteValue(autoStart.ValueName, false);
                        var after = RegistryAccess.ReadValue(key, autoStart.ValueName);
                        if (after != null)
                        {
                            return Task.FromResult(OperationResult.Failed(autoStart.Id, "自启值删除后仍然存在。"));
                        }

                        context.Log("INFO", "已删除自启值：" + autoStart.ValueName);
                        return Task.FromResult(OperationResult.Succeeded(autoStart.Id, "开机自启已删除。", false));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(OperationResult.Failed(autoStart.Id, "没有修改开机自启的权限。"));
            }
            catch (System.Security.SecurityException)
            {
                return Task.FromResult(OperationResult.Failed(autoStart.Id, "开机自启写入被安全策略拒绝。"));
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(OperationResult.Cancelled(autoStart.Id, "开机自启操作已取消。"));
            }
            catch (Exception exception)
            {
                return Task.FromResult(OperationResult.Failed(autoStart.Id, "开机自启操作失败：" + exception.Message));
            }
        }

        private static string Format(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
