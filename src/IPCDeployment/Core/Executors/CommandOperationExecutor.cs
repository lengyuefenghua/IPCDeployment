using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IPCDeployment.Core
{
    public sealed class CommandOperationExecutor : IOperationExecutor
    {
        public string Type
        {
            get { return "Command"; }
        }

        public async Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            var command = operation as CommandOperation;
            if (command == null)
            {
                return OperationResult.Failed(operation.Id, "操作类型不匹配。");
            }

            string temporaryScript = null;
            try
            {
                var request = BuildRequest(command, context, ref temporaryScript);
                if (request.Error != null)
                {
                    return OperationResult.Failed(command.Id, request.Error);
                }

                var result = await context.ProcessRunner.RunAsync(request.Request,
                    line => context.Log("INFO", line), cancellationToken);

                if (result.Cancelled)
                {
                    return OperationResult.Cancelled(command.Id, ExecutorUtility.SummarizeProcess(result));
                }
                if (result.TimedOut || result.FailedToStart)
                {
                    return OperationResult.Failed(command.Id, ExecutorUtility.SummarizeProcess(result));
                }
                if (!ExecutorUtility.IsSuccess(result.ExitCode, command.SuccessExitCodes))
                {
                    return OperationResult.Failed(command.Id, ExecutorUtility.SummarizeProcess(result));
                }

                return OperationResult.Succeeded(command.Id, "命令完成：" + ExecutorUtility.SummarizeProcess(result), false);
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryScript))
                {
                    try
                    {
                        File.Delete(temporaryScript);
                    }
                    catch (Exception exception)
                    {
                        context.Log("WARN", "临时脚本删除失败：" + exception.Message);
                    }
                }
            }
        }

        private static CommandRequest BuildRequest(CommandOperation command, OperationContext context, ref string temporaryScript)
        {
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["IPCDEPLOYMENT_APP_ROOT"] = context.PackageRoot ?? string.Empty,
                ["IPCDEPLOYMENT_DATA_ROOT"] = context.DataRoot ?? string.Empty,
                ["IPCDEPLOYMENT_COMMANDS_DIR"] = Path.Combine(context.DataRoot ?? string.Empty, "commands"),
                ["IPCDEPLOYMENT_FILES_DIR"] = Path.Combine(context.DataRoot ?? string.Empty, "files"),
                ["IPCDEPLOYMENT_CONFIG_PATH"] = context.ConfigPath ?? string.Empty
            };

            var workingDirectory = command.ResolvedWorkingDirectory;
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                workingDirectory = string.IsNullOrEmpty(command.ResolvedFile)
                    ? Path.Combine(context.DataRoot ?? string.Empty, "commands")
                    : Path.GetDirectoryName(command.ResolvedFile);
            }

            if (!string.IsNullOrEmpty(command.Code))
            {
                var extension = command.Language == CommandLanguage.Cmd ? ".cmd" : ".ps1";
                var directory = Path.Combine(Path.GetTempPath(), "IPCDeployment", "scripts");
                Directory.CreateDirectory(directory);
                temporaryScript = Path.Combine(directory, "run-" + Guid.NewGuid().ToString("N") + extension);
                var content = command.Language == CommandLanguage.Cmd
                    ? BuildCmdCode(command.Code)
                    : BuildPowerShellCode(command.Code, command.Strict);
                File.WriteAllText(temporaryScript, content,
                    command.Language == CommandLanguage.Cmd ? Encoding.Default : new UTF8Encoding(true));

                var codeHash = Hash(content);
                context.Log("INFO", "运行内嵌 " + LanguageText(command.Language) + "（" + content.Length +
                    " 字符，SHA-256 " + codeHash + "）。");
                return CreateRequest(command, context, temporaryScript, string.Empty, workingDirectory, environment);
            }

            context.Log("INFO", "运行 " + LanguageText(command.Language) + "： " + command.ResolvedFile +
                " " + (command.Arguments ?? string.Empty));
            return CreateRequest(command, context, command.ResolvedFile, command.Arguments ?? string.Empty,
                workingDirectory, environment);
        }

        private static CommandRequest CreateRequest(CommandOperation command, OperationContext context, string scriptPath,
            string arguments, string workingDirectory, Dictionary<string, string> environment)
        {
            if (command.Language == CommandLanguage.Exe)
            {
                return CommandRequest.Success(new ProcessRequest
                {
                    FileName = scriptPath,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    TimeoutMinutes = command.TimeoutMinutes,
                    Environment = environment
                });
            }

            if (command.Language == CommandLanguage.Cmd)
            {
                var cmd = CommandInterpreter.CmdPath;
                if (!File.Exists(cmd))
                {
                    return CommandRequest.Failure("未找到 cmd.exe：" + cmd);
                }
                return CommandRequest.Success(new ProcessRequest
                {
                    FileName = cmd,
                    Arguments = BuildCmdArguments(scriptPath, arguments),
                    WorkingDirectory = workingDirectory,
                    TimeoutMinutes = command.TimeoutMinutes,
                    Environment = environment
                });
            }

            var powershell = CommandInterpreter.PowerShellPath;
            if (!File.Exists(powershell))
            {
                return CommandRequest.Failure("未找到 powershell.exe：" + powershell);
            }
            return CommandRequest.Success(new ProcessRequest
            {
                FileName = powershell,
                Arguments = BuildPowerShellArguments(scriptPath, arguments),
                WorkingDirectory = workingDirectory,
                TimeoutMinutes = command.TimeoutMinutes,
                Environment = environment
            });
        }

        private static string BuildCmdArguments(string scriptPath, string arguments)
        {
            var builder = new StringBuilder("/d /s /c \"\"");
            builder.Append(scriptPath);
            builder.Append('"');
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                builder.Append(' ');
                builder.Append(arguments);
            }
            builder.Append('"');
            return builder.ToString();
        }

        private static string BuildPowerShellArguments(string scriptPath, string arguments)
        {
            var builder = new StringBuilder("-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"");
            builder.Append(scriptPath);
            builder.Append('"');
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                builder.Append(' ');
                builder.Append(arguments);
            }
            return builder.ToString();
        }

        private static string BuildCmdCode(string code)
        {
            return "@echo off\r\nsetlocal EnableExtensions\r\n" + code + "\r\nexit /b %errorlevel%\r\n";
        }

        private static string BuildPowerShellCode(string code, bool strict)
        {
            if (!strict)
            {
                return code;
            }
            return "$ErrorActionPreference = 'Stop'\r\n\r\ntry {\r\n" + code +
                "\r\n}\r\ncatch {\r\n    [Console]::Error.WriteLine($_.Exception.ToString())\r\n    exit 1\r\n}\r\n";
        }

        private static string LanguageText(CommandLanguage language)
        {
            if (language == CommandLanguage.Cmd) return "CMD";
            if (language == CommandLanguage.PowerShell) return "PowerShell";
            return "EXE";
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(bytes).Replace("-", string.Empty);
            }
        }

        private sealed class CommandRequest
        {
            public ProcessRequest Request { get; private set; }
            public string Error { get; private set; }

            public static CommandRequest Success(ProcessRequest request)
            {
                return new CommandRequest { Request = request };
            }

            public static CommandRequest Failure(string error)
            {
                return new CommandRequest { Error = error };
            }
        }
    }
}
