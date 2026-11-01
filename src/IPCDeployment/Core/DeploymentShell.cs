using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IPCDeployment.Core
{
    public sealed class DeploymentShell
    {
        private readonly string packageRoot;
        private readonly PathPolicy paths;
        private DeploymentConfig config;
        private DeploymentLog log;
        private ConfigLoadResult loadResult;
        private string dataRoot;
        private string logDirectory;
        private string configPathDisplay;

        public DeploymentShell(string packageRoot)
        {
            this.packageRoot = Path.GetFullPath(packageRoot);
            paths = new PathPolicy();
            logDirectory = "logs";
        }

        public ConfigLoadResult LoadConfig(string configPath)
        {
            var fullPath = Path.GetFullPath(configPath);
            configPathDisplay = fullPath;
            // 配置与 exe 同级；data/ 固定为程序目录下的载荷目录。
            dataRoot = Path.Combine(packageRoot, "data");
            var result = new ConfigLoader(paths).Load(fullPath);
            config = result.Config;
            logDirectory = config == null ? "logs" : config.LogDirectory;

            if (config == null)
            {
                log = CreateLog("logs");
                WriteIssues(result);
                loadResult = result;
                return result;
            }

            try
            {
                log = new DeploymentLog(packageRoot, logDirectory);
            }
            catch (Exception)
            {
                logDirectory = "logs";
                log = CreateLog(logDirectory);
            }

            var preflight = new PreflightChecker(paths).Check(config, packageRoot, new WindowsPreflightEnvironment());
            result.Errors.AddRange(preflight.Errors);
            result.Warnings.AddRange(preflight.Warnings);
            result.CanExecute = config.Mode == DeploymentMode.Execute && result.Errors.Count == 0;
            WriteIssues(result);
            loadResult = result;
            return result;
        }

        public async Task<DeploymentReport> StartAsync(bool confirmed, IProgress<DeploymentEvent> progress,
            CancellationToken cancellationToken)
        {
            if (config == null || loadResult == null || !loadResult.IsValid)
            {
                throw new InvalidOperationException("配置未通过预检。");
            }

            if (log == null)
            {
                log = CreateLog(logDirectory);
            }

            var registry = BuiltInExecutors.CreateRegistry();
            var context = new OperationContext
            {
                PackageRoot = packageRoot,
                DataRoot = dataRoot,
                LogDirectory = logDirectory,
                ConfigPath = configPathDisplay,
                Paths = paths,
                ProcessRunner = new ProcessRunner(),
                Log = null
            };
            var runner = new DeploymentRunner(registry, context, log);
            return await runner.RunAsync(config, confirmed, progress, cancellationToken);
        }

        /// <summary>配置编辑器等非部署操作记一行运行日志，不另建日志文件。</summary>
        public void WriteNote(string level, string message)
        {
            if (log == null)
            {
                log = CreateLog(logDirectory);
            }
            if (log != null)
            {
                log.Write(level, string.Empty, message);
            }
        }

        public DeploymentConfig CurrentConfig
        {
            get { return config; }
        }        public ConfigLoadResult LoadResult
        {
            get { return loadResult; }
        }

        public IList<string> RuntimeLogLines
        {
            get { return log == null ? new List<string>() : log.Lines; }
        }

        public string LogPath
        {
            get { return log == null ? null : log.LogPath; }
        }

        public bool HasLogWriteFailure
        {
            get { return log != null && log.HasWriteFailure; }
        }

        public bool UsedLogFallback
        {
            get { return log != null && log.UsedFallback; }
        }

        private DeploymentLog CreateLog(string directory)
        {
            try
            {
                return new DeploymentLog(packageRoot, directory);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void WriteIssues(ConfigLoadResult result)
        {
            if (log == null)
            {
                return;
            }

            log.Write("INFO", string.Empty, "配置文件：" + (configPathDisplay ?? "data/config.xml") + "；模式：" +
                (config == null ? "未知" : ModeText(config.Mode)) + "。");
            foreach (var error in result.Errors)
            {
                log.Write("ERROR", string.Empty, error.ToDisplayString());
            }
            foreach (var warning in result.Warnings)
            {
                log.Write("WARN", string.Empty, warning.ToDisplayString());
            }
            if (result.Errors.Count == 0)
            {
                log.Write("INFO", string.Empty, "预检通过，共 " + result.Summary.TotalOperations + " 个操作。");
            }
        }

        private static string ModeText(DeploymentMode mode)
        {
            return mode == DeploymentMode.Execute ? "execute" : "preview";
        }
    }
}
