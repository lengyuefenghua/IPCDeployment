using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace IPCDeployment.Core
{
    public sealed class InstallOperationExecutor : IOperationExecutor
    {
        public string Type
        {
            get { return "Install"; }
        }

        public async Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            var install = operation as InstallOperation;
            if (install == null)
            {
                return OperationResult.Failed(operation.Id, "操作类型不匹配。");
            }

            if (!string.IsNullOrWhiteSpace(install.SkipIfFileExists) && File.Exists(install.SkipIfFileExists))
            {
                return OperationResult.AlreadySatisfied(install.Id, "目标文件已存在，无需安装：" + install.SkipIfFileExists + "。");
            }

            // 启动安装器前重新校验哈希，避免预检后安装包被替换。
            if (!string.IsNullOrWhiteSpace(install.Sha256))
            {
                string actual;
                try
                {
                    actual = ComputeSha256(install.ResolvedFile);
                }
                catch (Exception exception)
                {
                    return OperationResult.Failed(install.Id, "无法读取安装包：" + exception.Message);
                }

                if (!string.Equals(actual, install.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult.Failed(install.Id, "安装包哈希不匹配，未启动安装器。");
                }
            }

            var target = install.ResolvedFile;
            var arguments = install.Arguments ?? string.Empty;
            if (string.Equals(Path.GetExtension(target), ".msi", StringComparison.OrdinalIgnoreCase))
            {
                target = SystemTools.Get("msiexec.exe");
                arguments = "/i \"" + install.ResolvedFile + "\" /qn /norestart" +
                    (string.IsNullOrWhiteSpace(arguments) ? string.Empty : " " + arguments);
            }

            context.Log("INFO", "开始安装：" + install.Name + "。");
            var result = await context.ProcessRunner.RunAsync(new ProcessRequest
            {
                FileName = target,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(install.ResolvedFile),
                TimeoutMinutes = install.TimeoutMinutes
            }, line => context.Log("INFO", line), cancellationToken);

            if (result.Cancelled)
            {
                return OperationResult.Cancelled(install.Id, ExecutorUtility.SummarizeProcess(result));
            }
            if (result.TimedOut || result.FailedToStart)
            {
                return OperationResult.Failed(install.Id, ExecutorUtility.SummarizeProcess(result));
            }
            if (!ExecutorUtility.IsSuccess(result.ExitCode, install.SuccessExitCodes))
            {
                return OperationResult.Failed(install.Id, ExecutorUtility.SummarizeProcess(result));
            }

            return OperationResult.Succeeded(install.Id, "安装完成：" + ExecutorUtility.SummarizeProcess(result),
                ExecutorUtility.RequiresRestart(result.ExitCode));
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
