using System;
using System.Threading;
using System.Threading.Tasks;

namespace VisionIPC.Deployment.Core
{
    public sealed class DelayOperationExecutor : IOperationExecutor
    {
        public string Type
        {
            get { return "Delay"; }
        }

        public async Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context,
            CancellationToken cancellationToken)
        {
            var delay = operation as DelayOperation;
            if (delay == null)
            {
                return OperationResult.Failed(operation.Id, "操作类型不匹配。");
            }
            if (delay.Seconds < 1 || delay.Seconds > 600)
            {
                return OperationResult.Failed(delay.Id, "Delay 的 seconds 必须是 1 到 600。");
            }

            context.Log("INFO", "延时开始：" + delay.Seconds + " 秒。");
            await Task.Delay(TimeSpan.FromSeconds(delay.Seconds), cancellationToken);
            context.Log("INFO", "延时结束：" + delay.Seconds + " 秒。");
            return OperationResult.Succeeded(delay.Id, "已等待 " + delay.Seconds + " 秒。", false);
        }
    }
}
