using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VisionIPC.Deployment.Core
{
    public sealed class ProcessRunner : IProcessRunner
    {
        private const int OutputLimit = 64 * 1024;

        public async Task<ProcessResult> RunAsync(ProcessRequest request, Action<string> onOutput, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            var result = new ProcessResult { ExitCode = -1, Output = string.Empty, Error = string.Empty };
            if (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                return result;
            }

            var startedAt = DateTime.UtcNow;
            var output = new StringBuilder();
            var error = new StringBuilder();
            var outputLock = new object();

            Action<string> emit = line =>
            {
                if (line == null)
                {
                    return;
                }
                lock (outputLock)
                {
                    AppendCapped(output, line + Environment.NewLine);
                }
                if (onOutput != null)
                {
                    try
                    {
                        onOutput(line);
                    }
                    catch (Exception)
                    {
                    }
                }
            };

            // 受控进程不经过 shell；超时或取消时必须清理整个进程树。
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = request.FileName,
                    Arguments = request.Arguments ?? string.Empty,
                    WorkingDirectory = request.WorkingDirectory ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                if (request.Environment != null)
                {
                    foreach (var pair in request.Environment)
                    {
                        process.StartInfo.EnvironmentVariables[pair.Key] = pair.Value;
                    }
                }
                process.EnableRaisingEvents = true;

                var exitCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cancellationCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                process.Exited += (sender, args) => exitCompletion.TrySetResult(true);
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        emit(args.Data);
                    }
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data == null)
                    {
                        return;
                    }
                    lock (outputLock)
                    {
                        AppendCapped(error, args.Data + Environment.NewLine);
                    }
                    if (onOutput != null)
                    {
                        try
                        {
                            onOutput("stderr: " + args.Data);
                        }
                        catch (Exception)
                        {
                        }
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Exception exception)
                {
                    result.FailedToStart = true;
                    result.Error = exception.Message;
                    result.Elapsed = DateTime.UtcNow - startedAt;
                    return result;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (process.HasExited)
                {
                    exitCompletion.TrySetResult(true);
                }

                using (cancellationToken.Register(() =>
                {
                    cancellationCompletion.TrySetResult(true);
                    KillTree(process);
                }))
                {
                    var timeout = TimeSpan.FromMinutes(Math.Max(1, request.TimeoutMinutes));
                    var timeoutTask = Task.Delay(timeout);
                    var completed = await Task.WhenAny(exitCompletion.Task, timeoutTask, cancellationCompletion.Task);

                    if (completed == cancellationCompletion.Task)
                    {
                        KillTree(process);
                        WaitForExitQuietly(process);
                        result.Cancelled = true;
                        result.ExitCode = process.HasExited ? SafeExitCode(process) : -1;
                        result.Output = output.ToString();
                        result.Error = error.ToString();
                        result.Elapsed = DateTime.UtcNow - startedAt;
                        return result;
                    }

                    if (completed == timeoutTask && !process.HasExited)
                    {
                        KillTree(process);
                        WaitForExitQuietly(process);
                        result.TimedOut = true;
                        result.ExitCode = process.HasExited ? SafeExitCode(process) : -1;
                        result.Output = output.ToString();
                        result.Error = error.ToString();
                        result.Elapsed = DateTime.UtcNow - startedAt;
                        return result;
                    }

                    // 进程已退出；有限等待只用于刷新异步输出，避免子进程持有管道导致无限等待。
                    WaitForExitQuietly(process);
                    result.ExitCode = SafeExitCode(process);
                    result.Output = output.ToString();
                    result.Error = error.ToString();
                    result.Elapsed = DateTime.UtcNow - startedAt;
                    return result;
                }
            }
        }

        private static void KillTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception)
            {
            }

            try
            {
                var taskkill = SystemTools.Get("taskkill.exe");
                using (var killer = new Process())
                {
                    killer.StartInfo = new ProcessStartInfo
                    {
                        FileName = taskkill,
                        Arguments = "/PID " + process.Id + " /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    killer.Start();
                    killer.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void WaitForExitQuietly(Process process)
        {
            try
            {
                process.WaitForExit(5000);
            }
            catch (Exception)
            {
            }
        }

        private static int SafeExitCode(Process process)
        {
            try
            {
                return process.ExitCode;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static void AppendCapped(StringBuilder builder, string value)
        {
            if (builder.Length >= OutputLimit)
            {
                return;
            }
            var remaining = OutputLimit - builder.Length;
            if (value.Length <= remaining)
            {
                builder.Append(value);
            }
            else
            {
                builder.Append(value.Substring(0, remaining));
            }
        }
    }
}
