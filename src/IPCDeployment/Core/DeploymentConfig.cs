using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IPCDeployment.Core
{
    public enum DeploymentMode
    {
        Preview,
        Execute
    }

    public enum OperationStatus
    {
        Pending,
        Running,
        Succeeded,
        Failed,
        AlreadySatisfied,
        Cancelled,
        NotExecuted
    }

    public enum DataArea
    {
        Software,
        Files,
        Commands
    }

    public enum CopyOverwriteMode
    {
        IfDifferent,
        Always,
        IfNotExists
    }

    public enum RegistryHiveKind
    {
        HKCU,
        HKLM,
        HKCR,
        HKU,
        HKCC
    }

    public enum RegistryValueType
    {
        String,
        ExpandString,
        DWord,
        QWord
    }

    public enum NetworkOperationKind
    {
        AdvancedProperty,
        IPv4
    }

    public enum NetworkIpMode
    {
        Static,
        Dhcp
    }

    public enum ServiceStartupType
    {
        Automatic,
        Manual,
        Disabled
    }

    public enum ServiceState
    {
        Running,
        Stopped
    }

    public enum PowerSetting
    {
        Hibernate,
        FastStartup,
        PowerThrottling
    }

    public enum PowerValue
    {
        On,
        Off
    }

    public enum DeploymentEventKind
    {
        OperationStarted,
        OperationCompleted,
        TaskStarted,
        TaskCompleted,
        LogLine,
        QueueCompleted
    }

    public abstract class DeploymentOperation
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public int Order { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string GroupKey { get; set; }
        public string GroupName { get; set; }
        public string TaskKey { get; set; }
        public string TaskName { get; set; }
    }

    public sealed class InstallOperation : DeploymentOperation
    {
        public string File { get; set; }
        public string ResolvedFile { get; set; }
        public string Arguments { get; set; }
        public string Sha256 { get; set; }
        public int TimeoutMinutes { get; set; }
        public int[] SuccessExitCodes { get; set; }
        public string SkipIfFileExists { get; set; }
    }

    public sealed class CopyOperation : DeploymentOperation
    {
        public string Source { get; set; }
        public string ResolvedSource { get; set; }
        public string Target { get; set; }
        public string ResolvedTarget { get; set; }
        public CopyOverwriteMode Overwrite { get; set; }
        public bool Backup { get; set; }
    }

    public sealed class RegistryOperation : DeploymentOperation
    {
        public RegistryHiveKind Hive { get; set; }
        public string Key { get; set; }
        public string ValueName { get; set; }
        public RegistryValueType ValueType { get; set; }
        public string Value { get; set; }
        public int View { get; set; }
        public string ExpectedBefore { get; set; }
    }

    public enum CommandLanguage
    {
        Exe,
        Cmd,
        PowerShell
    }

    public sealed class CommandOperation : DeploymentOperation
    {
        public string File { get; set; }
        public string ResolvedFile { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public string ResolvedWorkingDirectory { get; set; }
        public int TimeoutMinutes { get; set; }
        public int[] SuccessExitCodes { get; set; }
        public CommandLanguage Language { get; set; }
        public string Code { get; set; }
        public bool Strict { get; set; }

        public CommandOperation()
        {
            Strict = true;
        }
    }

    public sealed class ServiceOperation : DeploymentOperation
    {
        public string ServiceName { get; set; }
        public ServiceStartupType? StartupType { get; set; }
        public ServiceState? State { get; set; }
    }

    public sealed class NetworkOperation : DeploymentOperation
    {
        public string Adapter { get; set; }
        public NetworkOperationKind Kind { get; set; }
        public string Keyword { get; set; }
        public string Value { get; set; }
        public RegistryValueType? ValueType { get; set; }
        public NetworkIpMode? IpMode { get; set; }
        public string Address { get; set; }
        public int? PrefixLength { get; set; }
        public string Gateway { get; set; }
        public string Dns { get; set; }
        public string ExpectedBefore { get; set; }
    }

    public sealed class PowerOperation : DeploymentOperation
    {
        public PowerSetting Setting { get; set; }
        public PowerValue Value { get; set; }
    }

    public sealed class AutoStartOperation : DeploymentOperation
    {
        public RegistryHiveKind Hive { get; set; }
        public string ValueName { get; set; }
        public string Command { get; set; }
        public string Arguments { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class DelayOperation : DeploymentOperation
    {
        public int Seconds { get; set; }
    }

    public sealed class DeploymentTask
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string GroupKey { get; set; }
        public string GroupName { get; set; }
        public List<DeploymentOperation> Operations { get; set; }

        public DeploymentTask()
        {
            Operations = new List<DeploymentOperation>();
        }
    }

    public sealed class DeploymentGroup
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public List<DeploymentTask> Tasks { get; set; }

        public DeploymentGroup()
        {
            Tasks = new List<DeploymentTask>();
        }
    }

    public sealed class DeploymentConfig
    {
        public string SchemaVersion { get; set; }
        public DeploymentMode Mode { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string LogDirectory { get; set; }
        public List<DeploymentGroup> Groups { get; set; }
        public List<DeploymentTask> Tasks { get; set; }
        public List<DeploymentOperation> Operations { get; set; }
        public ConfigSummary Summary { get; set; }

        public DeploymentConfig()
        {
            SchemaVersion = "2.0";
            Mode = DeploymentMode.Preview;
            Name = string.Empty;
            Description = string.Empty;
            LogDirectory = "logs";
            Groups = new List<DeploymentGroup>();
            Tasks = new List<DeploymentTask>();
            Operations = new List<DeploymentOperation>();
            Summary = new ConfigSummary();
        }
    }

    public sealed class ConfigSummary
    {
        public int GroupCount { get; set; }
        public int TaskCount { get; set; }
        public int TotalOperations { get; set; }
        public int InstallCount { get; set; }
        public int CopyCount { get; set; }
        public int RegistryCount { get; set; }
        public int CommandCount { get; set; }
        public int ServiceCount { get; set; }
        public int NetworkCount { get; set; }
        public int PowerCount { get; set; }
        public int AutoStartCount { get; set; }
        public int SystemOperationCount
        {
            get { return RegistryCount + ServiceCount + NetworkCount + PowerCount + AutoStartCount; }
        }
    }

    public sealed class ConfigIssue
    {
        public string Message { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public bool IsWarning { get; set; }

        public string ToDisplayString()
        {
            var location = Line > 0
                ? "第 " + Line + " 行" + (Column > 0 ? "第 " + Column + " 列" : string.Empty) + "："
                : string.Empty;
            return location + (Message ?? string.Empty);
        }
    }

    public sealed class ConfigLoadResult
    {
        public DeploymentConfig Config { get; set; }
        public List<ConfigIssue> Errors { get; private set; }
        public List<ConfigIssue> Warnings { get; private set; }
        public ConfigSummary Summary { get; set; }
        public bool CanExecute { get; set; }

        public ConfigLoadResult()
        {
            Errors = new List<ConfigIssue>();
            Warnings = new List<ConfigIssue>();
            Summary = new ConfigSummary();
        }

        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }

    public sealed class PreflightResult
    {
        public List<ConfigIssue> Errors { get; private set; }
        public List<ConfigIssue> Warnings { get; private set; }

        public PreflightResult()
        {
            Errors = new List<ConfigIssue>();
            Warnings = new List<ConfigIssue>();
        }
    }

    public interface IPreflightEnvironment
    {
        bool IsAdministrator { get; }
        bool IsAdapterPresent(string adapterName);
        bool IsServicePresent(string serviceName);
        bool CanWriteLogDirectory(string logDirectory);
    }

    public sealed class OperationResult
    {
        public string OperationId { get; set; }
        public OperationStatus Status { get; set; }
        public string Detail { get; set; }
        public bool RestartRequired { get; set; }
        public TimeSpan Elapsed { get; set; }

        public static OperationResult Succeeded(string operationId, string detail, bool restartRequired)
        {
            return new OperationResult
            {
                OperationId = operationId,
                Status = OperationStatus.Succeeded,
                Detail = detail,
                RestartRequired = restartRequired
            };
        }

        public static OperationResult Failed(string operationId, string detail)
        {
            return new OperationResult
            {
                OperationId = operationId,
                Status = OperationStatus.Failed,
                Detail = detail
            };
        }

        public static OperationResult AlreadySatisfied(string operationId, string detail)
        {
            return new OperationResult
            {
                OperationId = operationId,
                Status = OperationStatus.AlreadySatisfied,
                Detail = detail
            };
        }

        public static OperationResult Cancelled(string operationId, string detail)
        {
            return new OperationResult
            {
                OperationId = operationId,
                Status = OperationStatus.Cancelled,
                Detail = detail
            };
        }

        public static OperationResult NotExecuted(string operationId, string detail)
        {
            return new OperationResult
            {
                OperationId = operationId,
                Status = OperationStatus.NotExecuted,
                Detail = detail
            };
        }
    }

    public sealed class DeploymentTaskResult
    {
        public string GroupKey { get; set; }
        public string GroupName { get; set; }
        public string TaskKey { get; set; }
        public string TaskName { get; set; }
        public OperationStatus Status { get; set; }
        public TimeSpan Elapsed { get; set; }
        public List<OperationResult> Results { get; private set; }

        public DeploymentTaskResult()
        {
            Results = new List<OperationResult>();
        }
    }

    public sealed class DeploymentReport
    {
        public List<OperationResult> Results { get; private set; }
        public List<DeploymentTaskResult> Tasks { get; private set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public TimeSpan Elapsed { get; set; }
        public bool Cancelled { get; set; }
        public string LogPath { get; set; }

        public DeploymentReport()
        {
            Results = new List<OperationResult>();
            Tasks = new List<DeploymentTaskResult>();
        }

        public int TotalTaskCount
        {
            get { return Tasks.Count; }
        }

        public int CompletedTaskCount
        {
            get { return Tasks.Count(t => t.Status == OperationStatus.Succeeded || t.Status == OperationStatus.Failed || t.Status == OperationStatus.Cancelled); }
        }

        public int SucceededTaskCount
        {
            get { return Tasks.Count(t => t.Status == OperationStatus.Succeeded); }
        }

        public int FailedTaskCount
        {
            get { return Tasks.Count(t => t.Status == OperationStatus.Failed); }
        }

        public int CancelledTaskCount
        {
            get { return Tasks.Count(t => t.Status == OperationStatus.Cancelled); }
        }

        public int NotExecutedTaskCount
        {
            get { return Tasks.Count(t => t.Status == OperationStatus.NotExecuted); }
        }

        public int SucceededCount
        {
            get { return Count(OperationStatus.Succeeded); }
        }

        public int FailedCount
        {
            get { return Count(OperationStatus.Failed); }
        }

        public int AlreadySatisfiedCount
        {
            get { return Count(OperationStatus.AlreadySatisfied); }
        }

        public int CancelledCount
        {
            get { return Count(OperationStatus.Cancelled); }
        }

        public int NotExecutedCount
        {
            get { return Count(OperationStatus.NotExecuted); }
        }

        public int RestartRequiredCount
        {
            get
            {
                var count = 0;
                foreach (var result in Results)
                {
                    if (result.RestartRequired)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        private int Count(OperationStatus status)
        {
            var count = 0;
            foreach (var result in Results)
            {
                if (result.Status == status)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public sealed class DeploymentEvent
    {
        public DeploymentEventKind Kind { get; set; }
        public int Index { get; set; }
        public int Total { get; set; }
        public int CompletedCount { get; set; }
        public string OperationId { get; set; }
        public string OperationName { get; set; }
        public string OperationType { get; set; }
        public OperationStatus Status { get; set; }
        public string Detail { get; set; }
        public string LogLine { get; set; }
        public bool RestartRequired { get; set; }
        public TimeSpan Elapsed { get; set; }
        public DeploymentReport Report { get; set; }
        public string GroupKey { get; set; }
        public string GroupName { get; set; }
        public string TaskKey { get; set; }
        public string TaskName { get; set; }
        public int TaskIndex { get; set; }
        public int TaskTotal { get; set; }
        public int CompletedTasks { get; set; }
        public int TaskStepIndex { get; set; }
        public int TaskStepTotal { get; set; }
        public int CompletedTaskSteps { get; set; }
        public OperationStatus TaskStatus { get; set; }
    }

    public sealed class ProcessRequest
    {
        public string FileName { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public int TimeoutMinutes { get; set; }
        public Dictionary<string, string> Environment { get; set; }

        public ProcessRequest()
        {
            Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public bool Cancelled { get; set; }
        public bool FailedToStart { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    public interface IProcessRunner
    {
        Task<ProcessResult> RunAsync(ProcessRequest request, Action<string> onOutput, CancellationToken cancellationToken);
    }

    public sealed class OperationContext
    {
        public string PackageRoot { get; set; }
        public string DataRoot { get; set; }
        public string LogDirectory { get; set; }
        public string ConfigPath { get; set; }
        public PathPolicy Paths { get; set; }
        public IProcessRunner ProcessRunner { get; set; }
        public Action<string, string> Log { get; set; }

        public OperationContext WithLog(Action<string, string> log)
        {
            return new OperationContext
            {
                PackageRoot = PackageRoot,
                DataRoot = DataRoot,
                LogDirectory = LogDirectory,
                ConfigPath = ConfigPath,
                Paths = Paths,
                ProcessRunner = ProcessRunner,
                Log = log
            };
        }
    }

    public interface IOperationExecutor
    {
        string Type { get; }
        Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken);
    }

    public sealed class OperationRegistry
    {
        private readonly Dictionary<string, IOperationExecutor> executors =
            new Dictionary<string, IOperationExecutor>(StringComparer.OrdinalIgnoreCase);

        public void Register(IOperationExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException("executor");
            }
            if (string.IsNullOrEmpty(executor.Type))
            {
                throw new InvalidOperationException("执行器类型不能为空。");
            }
            executors[executor.Type] = executor;
        }

        public IOperationExecutor Resolve(DeploymentOperation operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException("operation");
            }
            return Resolve(operation.Type);
        }

        public IOperationExecutor Resolve(string type)
        {
            IOperationExecutor executor;
            if (string.IsNullOrEmpty(type) || !executors.TryGetValue(type, out executor))
            {
                throw new InvalidOperationException("未注册操作执行器：" + (type ?? string.Empty));
            }
            return executor;
        }
    }
}
