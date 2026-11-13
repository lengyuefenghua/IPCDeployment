using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IPCDeployment.Core
{
    public sealed class DeploymentRunner
    {
        private readonly OperationRegistry registry;
        private readonly OperationContext baseContext;
        private readonly DeploymentLog log;

        public DeploymentRunner(OperationRegistry registry, OperationContext baseContext, DeploymentLog log)
        {
            this.registry = registry;
            this.baseContext = baseContext;
            this.log = log;
        }

        public async Task<DeploymentReport> RunAsync(DeploymentConfig config, bool confirmed,
            IProgress<DeploymentEvent> progress, CancellationToken cancellationToken)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            var report = new DeploymentReport
            {
                StartedAt = DateTime.Now,
                LogPath = log == null ? null : log.LogPath
            };
            var tasks = config.Tasks;
            // 禁用任务不进入执行队列：任务数、步骤数与进度分母都只算启用任务。
            var totalTasks = tasks.Count(task => task.Enabled);
            var totalSteps = tasks.Where(task => task.Enabled).Sum(task => task.Operations.Count);
            var disabledTasks = tasks.Count - totalTasks;

            // preview 和未确认的 execute 不进入执行队列，也不推进执行进度。
            if (config.Mode == DeploymentMode.Preview || !confirmed)
            {
                var reason = config.Mode == DeploymentMode.Preview ? "预览模式：未执行。" : "未确认执行。";
                foreach (var task in tasks)
                {
                    if (!task.Enabled)
                    {
                        report.Tasks.Add(CreateTaskResult(task, OperationStatus.Disabled));
                        continue;
                    }
                    var taskResult = CreateTaskResult(task, OperationStatus.NotExecuted);
                    foreach (var step in task.Operations)
                    {
                        var result = OperationResult.NotExecuted(step.Id, reason);
                        taskResult.Results.Add(result);
                        report.Results.Add(result);
                    }
                    report.Tasks.Add(taskResult);
                }
                Finish(report, false);
                PublishQueueCompleted(progress, report);
                return report;
            }

            PublishLog(progress, log == null ? null : log.Write("INFO", string.Empty,
                "开始执行队列，共 " + totalTasks + " 个任务" +
                (disabledTasks > 0 ? "，另有 " + disabledTasks + " 个任务已禁用。" : "。")));

            var completedTasks = 0;
            var completedSteps = 0;
            var cancelled = false;
            var taskIndex = 0;

            foreach (var group in config.Groups)
            {
                foreach (var task in group.Tasks)
                {
                    if (cancelled || cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    if (!task.Enabled)
                    {
                        // 禁用任务只留一条任务级记录，不发任务事件、不产生步骤结果、不占进度。
                        report.Tasks.Add(CreateTaskResult(task, OperationStatus.Disabled));
                        PublishLog(progress, log == null ? null : log.Write("INFO", task.Key,
                            "任务已禁用：" + task.GroupName + " / " + task.Name + "。"), task);
                        continue;
                    }

                    taskIndex++;
                    var taskResult = CreateTaskResult(task, OperationStatus.Pending);
                    var taskStepTotal = task.Operations.Count;
                    var completedTaskSteps = 0;
                    var taskFailed = false;
                    var taskCancelled = false;
                    var taskWarning = false;
                    var taskStopwatch = Stopwatch.StartNew();

                    PublishTaskStarted(progress, task, taskIndex, totalTasks, completedTasks, taskStepTotal);
                    PublishLog(progress, log == null ? null : log.Write("INFO", task.Key,
                        "任务开始：" + task.GroupName + " / " + task.Name + "。"), task);

                    for (var stepIndex = 0; stepIndex < task.Operations.Count; stepIndex++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            taskCancelled = true;
                            cancelled = true;
                            break;
                        }

                        var operation = task.Operations[stepIndex];
                        completedSteps++;
                        PublishOperationStarted(progress, operation, completedSteps, totalSteps,
                            taskIndex, totalTasks, completedTasks, stepIndex + 1, taskStepTotal, completedTaskSteps);

                        var operationContext = baseContext.WithLog((logLevel, message) =>
                        {
                            var line = log == null ? null : log.Write(logLevel, operation.Id, message);
                            if (line != null)
                            {
                                PublishLog(progress, line, operation, completedSteps, totalSteps,
                                    taskIndex, totalTasks, completedTasks, stepIndex + 1, taskStepTotal, completedTaskSteps);
                            }
                        });

                        var stopwatch = Stopwatch.StartNew();
                        OperationResult result;
                        try
                        {
                            var executor = registry.Resolve(operation);
                            result = await executor.ExecuteAsync(operation, operationContext, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            result = OperationResult.Cancelled(operation.Id, "操作已取消。");
                        }
                        catch (Exception exception)
                        {
                            result = OperationResult.Failed(operation.Id, "执行异常：" + exception.Message);
                        }
                        stopwatch.Stop();

                        if (result == null)
                        {
                            result = OperationResult.Failed(operation.Id, "执行器没有返回结果。");
                        }
                        result.Elapsed = stopwatch.Elapsed;
                        if (cancellationToken.IsCancellationRequested && result.Status != OperationStatus.Succeeded)
                        {
                            result.Status = OperationStatus.Cancelled;
                            if (string.IsNullOrWhiteSpace(result.Detail))
                            {
                                result.Detail = "操作已取消。";
                            }
                        }

                        if (result.Status == OperationStatus.Failed)
                        {
                            taskFailed = true;
                        }
                        else if (result.Status == OperationStatus.Cancelled)
                        {
                            taskCancelled = true;
                            cancelled = true;
                        }
                        else if (result.Status == OperationStatus.Warning)
                        {
                            // 警告不结束任务，继续执行后续步骤。
                            taskWarning = true;
                        }

                        taskResult.Results.Add(result);
                        report.Results.Add(result);
                        completedTaskSteps++;
                        PublishOperationCompleted(progress, operation, result, completedSteps, totalSteps,
                            taskIndex, totalTasks, completedTasks, stepIndex + 1, taskStepTotal, completedTaskSteps);

                        var resultLevel = result.Status == OperationStatus.Failed
                            ? "ERROR"
                            : result.Status == OperationStatus.Succeeded || result.Status == OperationStatus.AlreadySatisfied
                                ? "INFO"
                                : "WARN";
                        var detail = string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : "；" + result.Detail;
                        PublishLog(progress, log == null ? null : log.Write(resultLevel, operation.Id,
                            "状态：" + StatusText(result.Status) + detail + "；用时 " + FormatElapsed(result.Elapsed) + "。"),
                            operation, completedSteps, totalSteps, taskIndex, totalTasks, completedTasks,
                            stepIndex + 1, taskStepTotal, completedTaskSteps);

                        if (taskFailed || taskCancelled)
                        {
                            break;
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        taskCancelled = true;
                        cancelled = true;
                    }

                    // 任务内剩余步骤不再执行，也不计入子进度。
                    for (var stepIndex = completedTaskSteps; stepIndex < task.Operations.Count; stepIndex++)
                    {
                        var operation = task.Operations[stepIndex];
                        var detail = taskFailed ? "任务已失败，未执行。" : "执行已取消，未执行。";
                        var result = OperationResult.NotExecuted(operation.Id, detail);
                        taskResult.Results.Add(result);
                        report.Results.Add(result);
                    }

                    taskStopwatch.Stop();
                    taskResult.Elapsed = taskStopwatch.Elapsed;
                    taskResult.Status = taskFailed
                        ? OperationStatus.Failed
                        : taskCancelled
                            ? OperationStatus.Cancelled
                            : taskWarning ? OperationStatus.Warning : OperationStatus.Succeeded;
                    report.Tasks.Add(taskResult);
                    completedTasks++;
                    PublishTaskCompleted(progress, task, taskResult.Status, taskIndex, totalTasks,
                        completedTasks, completedTaskSteps, taskStepTotal, taskResult.Elapsed);

                    var taskLevel = taskResult.Status == OperationStatus.Failed
                        ? "ERROR"
                        : taskResult.Status == OperationStatus.Cancelled || taskResult.Status == OperationStatus.Warning
                            ? "WARN"
                            : "INFO";
                    PublishLog(progress, log == null ? null : log.Write(taskLevel, task.Key,
                        "任务结束：" + task.GroupName + " / " + task.Name + "；状态：" +
                        StatusText(taskResult.Status) + "；已执行步骤 " + completedTaskSteps + "/" + taskStepTotal +
                        "；用时 " + FormatElapsed(taskResult.Elapsed) + "。"), task);

                    if (cancelled)
                    {
                        break;
                    }
                }

                if (cancelled)
                {
                    break;
                }
            }

            // 取消后未开始的任务统一标记：禁用任务仍为已禁用，其余为未执行；都不计入全局进度。
            for (var index = report.Tasks.Count; index < tasks.Count; index++)
            {
                var task = tasks[index];
                if (!task.Enabled)
                {
                    report.Tasks.Add(CreateTaskResult(task, OperationStatus.Disabled));
                    continue;
                }
                var taskResult = CreateTaskResult(task, OperationStatus.NotExecuted);
                foreach (var step in task.Operations)
                {
                    var result = OperationResult.NotExecuted(step.Id, "执行已取消，未执行。");
                    taskResult.Results.Add(result);
                    report.Results.Add(result);
                }
                report.Tasks.Add(taskResult);
            }

            Finish(report, cancelled);
            PublishLog(progress, log == null ? null : log.Write("INFO", string.Empty,
                "执行队列结束：任务 " + report.CompletedTaskCount + "/" + report.EnabledTaskCount +
                " 已结束，成功 " + report.SucceededTaskCount + "，警告 " + report.WarningTaskCount +
                "，失败 " + report.FailedTaskCount + "，取消 " + report.CancelledTaskCount +
                "，未执行 " + report.NotExecutedTaskCount + "，禁用 " + report.DisabledTaskCount + "。"));
            PublishQueueCompleted(progress, report);
            return report;
        }

        private void Finish(DeploymentReport report, bool cancelled)
        {
            report.Cancelled = cancelled;
            report.FinishedAt = DateTime.Now;
            report.Elapsed = report.FinishedAt - report.StartedAt;
        }

        private static DeploymentTaskResult CreateTaskResult(DeploymentTask task, OperationStatus status)
        {
            return new DeploymentTaskResult
            {
                GroupKey = task.GroupKey,
                GroupName = task.GroupName,
                TaskKey = task.Key,
                TaskName = task.Name,
                Status = status
            };
        }

        private static void PublishTaskStarted(IProgress<DeploymentEvent> progress, DeploymentTask task,
            int taskIndex, int taskTotal, int completedTasks, int taskStepTotal)
        {
            if (progress == null)
            {
                return;
            }
            progress.Report(new DeploymentEvent
            {
                Kind = DeploymentEventKind.TaskStarted,
                Index = taskIndex,
                Total = taskTotal,
                CompletedCount = completedTasks,
                GroupKey = task.GroupKey,
                GroupName = task.GroupName,
                TaskKey = task.Key,
                TaskName = task.Name,
                TaskIndex = taskIndex,
                TaskTotal = taskTotal,
                CompletedTasks = completedTasks,
                TaskStepTotal = taskStepTotal,
                Status = OperationStatus.Running,
                TaskStatus = OperationStatus.Running
            });
        }

        private static void PublishTaskCompleted(IProgress<DeploymentEvent> progress, DeploymentTask task,
            OperationStatus status, int taskIndex, int taskTotal, int completedTasks,
            int completedTaskSteps, int taskStepTotal, TimeSpan elapsed)
        {
            if (progress == null)
            {
                return;
            }
            progress.Report(new DeploymentEvent
            {
                Kind = DeploymentEventKind.TaskCompleted,
                Index = taskIndex,
                Total = taskTotal,
                CompletedCount = completedTasks,
                GroupKey = task.GroupKey,
                GroupName = task.GroupName,
                TaskKey = task.Key,
                TaskName = task.Name,
                TaskIndex = taskIndex,
                TaskTotal = taskTotal,
                CompletedTasks = completedTasks,
                CompletedTaskSteps = completedTaskSteps,
                TaskStepTotal = taskStepTotal,
                Status = status,
                TaskStatus = status,
                Elapsed = elapsed
            });
        }

        private static void PublishOperationStarted(IProgress<DeploymentEvent> progress, DeploymentOperation operation,
            int stepIndex, int stepTotal, int taskIndex, int taskTotal, int completedTasks,
            int taskStepIndex, int taskStepTotal, int completedTaskSteps)
        {
            if (progress == null)
            {
                return;
            }
            progress.Report(new DeploymentEvent
            {
                Kind = DeploymentEventKind.OperationStarted,
                Index = stepIndex,
                Total = stepTotal,
                CompletedCount = completedTasks,
                OperationId = operation.Id,
                OperationName = operation.Name,
                OperationType = operation.Type,
                GroupKey = operation.GroupKey,
                GroupName = operation.GroupName,
                TaskKey = operation.TaskKey,
                TaskName = operation.TaskName,
                TaskIndex = taskIndex,
                TaskTotal = taskTotal,
                CompletedTasks = completedTasks,
                TaskStepIndex = taskStepIndex,
                TaskStepTotal = taskStepTotal,
                CompletedTaskSteps = completedTaskSteps,
                Status = OperationStatus.Running,
                TaskStatus = OperationStatus.Running
            });
        }

        private static void PublishOperationCompleted(IProgress<DeploymentEvent> progress, DeploymentOperation operation,
            OperationResult result, int stepIndex, int stepTotal, int taskIndex, int taskTotal, int completedTasks,
            int taskStepIndex, int taskStepTotal, int completedTaskSteps)
        {
            if (progress == null)
            {
                return;
            }
            progress.Report(new DeploymentEvent
            {
                Kind = DeploymentEventKind.OperationCompleted,
                Index = stepIndex,
                Total = stepTotal,
                CompletedCount = completedTasks,
                OperationId = operation.Id,
                OperationName = operation.Name,
                OperationType = operation.Type,
                GroupKey = operation.GroupKey,
                GroupName = operation.GroupName,
                TaskKey = operation.TaskKey,
                TaskName = operation.TaskName,
                TaskIndex = taskIndex,
                TaskTotal = taskTotal,
                CompletedTasks = completedTasks,
                TaskStepIndex = taskStepIndex,
                TaskStepTotal = taskStepTotal,
                CompletedTaskSteps = completedTaskSteps,
                Status = result.Status,
                TaskStatus = OperationStatus.Running,
                Detail = result.Detail,
                RestartRequired = result.RestartRequired,
                Elapsed = result.Elapsed
            });
        }

        private static void PublishLog(IProgress<DeploymentEvent> progress, string line)
        {
            if (progress == null || line == null)
            {
                return;
            }
            progress.Report(new DeploymentEvent
            {
                Kind = DeploymentEventKind.LogLine,
                LogLine = line
            });
        }

        private static void PublishLog(IProgress<DeploymentEvent> progress, string line, DeploymentTask task)
        {
            if (progress == null || line == null)
            {
                return;
            }
            progress.Report(new DeploymentEvent
            {
                Kind = DeploymentEventKind.LogLine,
                GroupKey = task.GroupKey,
                GroupName = task.GroupName,
                TaskKey = task.Key,
                TaskName = task.Name,
                LogLine = line
            });
        }

        private static void PublishLog(IProgress<DeploymentEvent> progress, string line, DeploymentOperation operation,
            int stepIndex, int stepTotal, int taskIndex, int taskTotal, int completedTasks,
            int taskStepIndex, int taskStepTotal, int completedTaskSteps)
        {
            if (progress == null || line == null)
            {
                return;
            }
            progress.Report(new DeploymentEvent
            {
                Kind = DeploymentEventKind.LogLine,
                Index = stepIndex,
                Total = stepTotal,
                CompletedCount = completedTasks,
                OperationId = operation.Id,
                OperationName = operation.Name,
                OperationType = operation.Type,
                GroupKey = operation.GroupKey,
                GroupName = operation.GroupName,
                TaskKey = operation.TaskKey,
                TaskName = operation.TaskName,
                TaskIndex = taskIndex,
                TaskTotal = taskTotal,
                CompletedTasks = completedTasks,
                TaskStepIndex = taskStepIndex,
                TaskStepTotal = taskStepTotal,
                CompletedTaskSteps = completedTaskSteps,
                LogLine = line
            });
        }

        private static void PublishQueueCompleted(IProgress<DeploymentEvent> progress, DeploymentReport report)
        {
            if (progress == null)
            {
                return;
            }
            progress.Report(new DeploymentEvent
            {
                Kind = DeploymentEventKind.QueueCompleted,
                Report = report,
                Total = report.Results.Count
            });
        }

        private static string StatusText(OperationStatus status)
        {
            if (status == OperationStatus.Succeeded) return "成功";
            if (status == OperationStatus.Failed) return "失败";
            if (status == OperationStatus.Cancelled) return "已取消";
            if (status == OperationStatus.AlreadySatisfied) return "已满足";
            if (status == OperationStatus.NotExecuted) return "未执行";
            if (status == OperationStatus.Disabled) return "已禁用";
            if (status == OperationStatus.Warning) return "警告";
            if (status == OperationStatus.Running) return "运行中";
            return "等待";
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            return elapsed.TotalMinutes >= 1
                ? ((int)elapsed.TotalMinutes) + " 分 " + elapsed.Seconds + " 秒"
                : elapsed.Seconds + "." + elapsed.Milliseconds.ToString("000") + " 秒";
        }
    }
}
