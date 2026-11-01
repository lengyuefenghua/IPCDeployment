using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IPCDeployment.Core
{
    public sealed class CopyOperationExecutor : IOperationExecutor
    {
        public string Type
        {
            get { return "Copy"; }
        }

        public async Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            var copy = operation as CopyOperation;
            if (copy == null)
            {
                return OperationResult.Failed(operation.Id, "操作类型不匹配。");
            }

            try
            {
                return await Task.Run(() => Execute(copy, context, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Cancelled(copy.Id, "复制已取消。");
            }
            catch (Exception exception)
            {
                return OperationResult.Failed(copy.Id, "复制失败：" + exception.Message);
            }
        }

        private static OperationResult Execute(CopyOperation copy, OperationContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(copy.ResolvedSource))
            {
                return CopyDirectory(copy, context, cancellationToken);
            }
            return CopyFile(copy, context);
        }

        private static OperationResult CopyFile(CopyOperation copy, OperationContext context)
        {
            var source = copy.ResolvedSource;
            var target = copy.ResolvedTarget;
            if (Directory.Exists(target))
            {
                return OperationResult.Failed(copy.Id, "目标路径是目录，单个文件复制需要完整文件名：" + target + "。");
            }

            if (File.Exists(target))
            {
                if (copy.Overwrite == CopyOverwriteMode.IfNotExists)
                {
                    return OperationResult.AlreadySatisfied(copy.Id, "目标文件已存在，按 ifNotExists 已满足。");
                }
                if (copy.Overwrite == CopyOverwriteMode.IfDifferent && FilesEqual(source, target))
                {
                    return OperationResult.AlreadySatisfied(copy.Id, "目标文件内容一致，无需复制。");
                }
                if (copy.Backup)
                {
                    var backup = BackupFile(target);
                    context.Log("INFO", "覆盖前已备份：" + backup);
                }
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(source, target, true);
            context.Log("INFO", "已复制文件：" + target);
            return OperationResult.Succeeded(copy.Id, "已复制文件：" + target, false);
        }

        private static OperationResult CopyDirectory(CopyOperation copy, OperationContext context, CancellationToken cancellationToken)
        {
            var source = copy.ResolvedSource;
            var target = copy.ResolvedTarget;
            if (File.Exists(target))
            {
                return OperationResult.Failed(copy.Id, "目标路径是文件，目录复制需要目录路径：" + target + "。");
            }

            if (Directory.Exists(target))
            {
                if (copy.Overwrite == CopyOverwriteMode.IfNotExists)
                {
                    return OperationResult.AlreadySatisfied(copy.Id, "目标目录已存在，按 ifNotExists 已满足。");
                }
                if (copy.Backup)
                {
                    var backup = BackupDirectory(target);
                    context.Log("INFO", "覆盖前已备份目录：" + backup);
                }
            }

            Directory.CreateDirectory(target);

            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            var sourceRoot = source.TrimEnd(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            var copied = 0;
            var alreadySatisfied = 0;

            foreach (var sourceFile in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = sourceFile.Substring(sourceRoot.Length)
                    .TrimStart(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                var destination = Path.Combine(target, relative);
                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                if (copy.Overwrite == CopyOverwriteMode.IfNotExists && File.Exists(destination))
                {
                    alreadySatisfied++;
                    continue;
                }
                if (copy.Overwrite == CopyOverwriteMode.IfDifferent && FilesEqual(sourceFile, destination))
                {
                    alreadySatisfied++;
                    continue;
                }

                File.Copy(sourceFile, destination, true);
                copied++;
            }

            if (files.Length == 0)
            {
                return OperationResult.AlreadySatisfied(copy.Id, "源目录为空，未复制文件。");
            }
            if (copied == 0)
            {
                return OperationResult.AlreadySatisfied(copy.Id, "所有文件已满足条件；共 " + alreadySatisfied + " 个。");
            }

            context.Log("INFO", "目录复制完成：已复制 " + copied + " 个，已满足 " + alreadySatisfied + " 个。");
            return OperationResult.Succeeded(copy.Id, "已复制 " + copied + " 个文件，已满足 " + alreadySatisfied + " 个。", false);
        }

        private static bool FilesEqual(string source, string target)
        {
            if (!File.Exists(target))
            {
                return false;
            }

            var sourceInfo = new FileInfo(source);
            var targetInfo = new FileInfo(target);
            if (sourceInfo.Length != targetInfo.Length)
            {
                return false;
            }

            return Math.Abs((sourceInfo.LastWriteTimeUtc - targetInfo.LastWriteTimeUtc).TotalSeconds) < 2;
        }

        private static string BackupFile(string target)
        {
            var backup = UniqueBackupPath(target);
            File.Copy(target, backup, true);
            return backup;
        }

        private static string BackupDirectory(string target)
        {
            var backup = UniqueBackupPath(target);
            Directory.Move(target, backup);
            return backup;
        }

        private static string UniqueBackupPath(string target)
        {
            var candidate = target + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                candidate += "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            return candidate;
        }
    }
}
