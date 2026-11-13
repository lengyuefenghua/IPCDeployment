using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IPCDeployment.Core;

namespace IPCDeployment.Tests
{
    internal sealed class FakeProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }
        public ProcessRequest LastRequest { get; private set; }
        public ProcessResult Result { get; set; }

        public FakeProcessRunner()
        {
            Result = new ProcessResult { ExitCode = 0, Output = string.Empty, Error = string.Empty };
        }

        public Task<ProcessResult> RunAsync(ProcessRequest request, Action<string> onOutput, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }

    internal sealed class FakePreflightEnvironment : IPreflightEnvironment
    {
        public bool IsAdministrator { get; set; }
        public bool AdapterPresent { get; set; }
        public bool ServicePresent { get; set; }
        public bool LogDirectoryWritable { get; set; }

        public FakePreflightEnvironment()
        {
            IsAdministrator = true;
            AdapterPresent = true;
            ServicePresent = true;
            LogDirectoryWritable = true;
        }

        public bool IsAdapterPresent(string adapterName)
        {
            return AdapterPresent;
        }

        public bool IsServicePresent(string serviceName)
        {
            return ServicePresent;
        }

        public bool CanWriteLogDirectory(string logDirectory)
        {
            return LogDirectoryWritable;
        }
    }

    internal sealed class RecordingExecutor : IOperationExecutor
    {
        public string Type { get; private set; }
        public int CallCount { get; private set; }
        public string FailOperationId { get; set; }
        public string CancelOperationId { get; set; }
        public string WarnOperationId { get; set; }
        public List<string> ExecutedIds { get; private set; }

        public RecordingExecutor(string type)
        {
            Type = type;
            ExecutedIds = new List<string>();
        }

        public Task<OperationResult> ExecuteAsync(DeploymentOperation operation, OperationContext context, CancellationToken cancellationToken)
        {
            CallCount++;
            ExecutedIds.Add(operation.Id);
            if (string.Equals(operation.Id, CancelOperationId, StringComparison.Ordinal))
            {
                return Task.FromResult(OperationResult.Cancelled(operation.Id, "cancelled"));
            }
            if (string.Equals(operation.Id, WarnOperationId, StringComparison.Ordinal))
            {
                return Task.FromResult(OperationResult.Warning(operation.Id, "warning"));
            }
            if (string.Equals(operation.Id, FailOperationId, StringComparison.Ordinal))
            {
                return Task.FromResult(OperationResult.Failed(operation.Id, "failed"));
            }
            return Task.FromResult(OperationResult.Succeeded(operation.Id, "ok", false));
        }
    }

    internal sealed class TestOperation : DeploymentOperation
    {
        public TestOperation(string id, string name)
        {
            Id = id;
            Name = name;
            Type = "Fake";
        }
    }

    internal sealed class RecordingProgress : IProgress<DeploymentEvent>
    {
        public List<DeploymentEvent> Events { get; private set; }

        public RecordingProgress()
        {
            Events = new List<DeploymentEvent>();
        }

        public void Report(DeploymentEvent value)
        {
            Events.Add(value);
        }
    }
}
