using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace IPCDeployment.Core
{
    public sealed class RegistryOperationExecutor : IOperationExecutor
    {
        public string Type
        {
            get { return "Registry"; }
        }

        public Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            var registry = operation as RegistryOperation;
            if (registry == null)
            {
                return Task.FromResult(OperationResult.Failed(operation.Id, "操作类型不匹配。"));
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var keyPath = (registry.Key ?? string.Empty).Replace('/', '\\');
                using (var baseKey = RegistryAccess.OpenBaseKey(registry.Hive, registry.View))
                using (var key = baseKey.CreateSubKey(keyPath, true))
                {
                    if (key == null)
                    {
                        return Task.FromResult(OperationResult.Failed(registry.Id, "无法打开或创建注册表项：" + keyPath + "。"));
                    }

                    var current = RegistryAccess.ReadValue(key, registry.ValueName);
                    context.Log("INFO", "注册表写前值：" + Format(current));
                    if (RegistryAccess.ValuesEqual(current, registry.Value, registry.ValueType))
                    {
                        return Task.FromResult(OperationResult.AlreadySatisfied(registry.Id, "注册表值已等于目标值，已满足。"));
                    }

                    if (registry.ExpectedBefore != null &&
                        !RegistryAccess.ValuesEqual(current, registry.ExpectedBefore, registry.ValueType))
                    {
                        return Task.FromResult(OperationResult.Failed(registry.Id,
                            "注册表写前值不匹配；当前=" + Format(current) + "，期望=" + registry.ExpectedBefore + "。"));
                    }

                    RegistryAccess.SetValue(key, registry.ValueName, registry.Value, registry.ValueType);
                    var after = RegistryAccess.ReadValue(key, registry.ValueName);
                    if (!RegistryAccess.ValuesEqual(after, registry.Value, registry.ValueType))
                    {
                        return Task.FromResult(OperationResult.Failed(registry.Id,
                            "注册表写入后回读不一致；当前=" + Format(after) + "。"));
                    }

                    context.Log("INFO", "注册表写后回读：" + Format(after));
                    return Task.FromResult(OperationResult.Succeeded(registry.Id, "注册表已写入并回读一致。", false));
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult(OperationResult.Failed(registry.Id, "没有写入注册表的权限。"));
            }
            catch (System.Security.SecurityException)
            {
                return Task.FromResult(OperationResult.Failed(registry.Id, "注册表写入被安全策略拒绝。"));
            }
            catch (OperationCanceledException)
            {
                return Task.FromResult(OperationResult.Cancelled(registry.Id, "注册表操作已取消。"));
            }
            catch (Exception exception)
            {
                return Task.FromResult(OperationResult.Failed(registry.Id, "注册表操作失败：" + exception.Message));
            }
        }

        private static string Format(object value)
        {
            return value == null ? "不存在" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
