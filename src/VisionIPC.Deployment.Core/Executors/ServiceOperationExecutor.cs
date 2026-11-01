using System;
using System.Collections.Generic;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace VisionIPC.Deployment.Core
{
    public sealed class ServiceOperationExecutor : IOperationExecutor
    {
        public string Type
        {
            get { return "Service"; }
        }

        public async Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            var service = operation as ServiceOperation;
            if (service == null)
            {
                return OperationResult.Failed(operation.Id, "操作类型不匹配。");
            }

            try
            {
                using (var controller = new ServiceController(service.ServiceName))
                {
                    controller.Refresh();
                    var changed = false;
                    var details = new List<string>();

                    if (service.StartupType.HasValue)
                    {
                        var targetStart = MapStart(service.StartupType.Value);
                        var currentStart = RegistryAccess.ReadServiceStart(service.ServiceName);
                        if (currentStart != targetStart)
                        {
                            var result = await context.ProcessRunner.RunAsync(new ProcessRequest
                            {
                                FileName = SystemTools.Get("sc.exe"),
                                Arguments = "config \"" + service.ServiceName + "\" start= " + MapStartArgument(service.StartupType.Value),
                                TimeoutMinutes = 2
                            }, line => context.Log("INFO", line), cancellationToken);

                            if (result.Cancelled)
                            {
                                return OperationResult.Cancelled(service.Id, ExecutorUtility.SummarizeProcess(result));
                            }
                            if (result.TimedOut || result.FailedToStart || result.ExitCode != 0)
                            {
                                return OperationResult.Failed(service.Id, "设置服务启动类型失败：" + ExecutorUtility.SummarizeProcess(result));
                            }

                            var afterStart = RegistryAccess.ReadServiceStart(service.ServiceName);
                            if (afterStart != targetStart)
                            {
                                return OperationResult.Failed(service.Id, "服务启动类型回读不匹配；当前=" + afterStart + "。");
                            }

                            changed = true;
                            details.Add("启动类型=" + service.StartupType.Value);
                            context.Log("INFO", "服务启动类型已设置为 " + service.StartupType.Value + "。");
                        }
                    }

                    if (service.State.HasValue)
                    {
                        controller.Refresh();
                        var targetStatus = service.State.Value == ServiceState.Running
                            ? ServiceControllerStatus.Running
                            : ServiceControllerStatus.Stopped;

                        if (controller.Status != targetStatus)
                        {
                            var stateValue = service.State.Value;
                            await Task.Run(() =>
                            {
                                if (stateValue == ServiceState.Running)
                                {
                                    controller.Start();
                                }
                                else
                                {
                                    controller.Stop();
                                }
                                controller.WaitForStatus(targetStatus, TimeSpan.FromMinutes(2));
                            }, cancellationToken);

                            changed = true;
                            details.Add("状态=" + service.State.Value);
                            context.Log("INFO", "服务状态已设置为 " + service.State.Value + "。");
                        }
                    }

                    if (!changed)
                    {
                        return OperationResult.AlreadySatisfied(service.Id, "服务状态已满足。");
                    }

                    return OperationResult.Succeeded(service.Id, string.Join("；", details), false);
                }
            }
            catch (InvalidOperationException)
            {
                return OperationResult.Failed(service.Id, "服务不存在或无法访问：" + service.ServiceName + "。");
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult.Failed(service.Id, "没有修改服务的权限。");
            }
            catch (System.TimeoutException)
            {
                return OperationResult.Failed(service.Id, "等待服务状态超时。");
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Cancelled(service.Id, "服务操作已取消。");
            }
            catch (Exception exception)
            {
                return OperationResult.Failed(service.Id, "服务操作失败：" + exception.Message);
            }
        }

        private static int MapStart(ServiceStartupType type)
        {
            if (type == ServiceStartupType.Manual) return 3;
            if (type == ServiceStartupType.Disabled) return 4;
            return 2;
        }

        private static string MapStartArgument(ServiceStartupType type)
        {
            if (type == ServiceStartupType.Manual) return "demand";
            if (type == ServiceStartupType.Disabled) return "disabled";
            return "auto";
        }
    }
}
