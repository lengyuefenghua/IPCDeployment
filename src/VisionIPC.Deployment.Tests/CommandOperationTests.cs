using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisionIPC.Deployment.Core;

namespace VisionIPC.Deployment.Tests
{
    [TestFixture]
    public sealed class CommandOperationTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "VisionIPC-CommandTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task Command_passes_arguments_and_working_directory()
        {
            var file = CreateCommand();
            var runner = new FakeProcessRunner();
            var operation = new CommandOperation
            {
                Id = "command",
                Name = "command",
                Type = "Command",
                ResolvedFile = file,
                Arguments = "--check",
                TimeoutMinutes = 1,
                SuccessExitCodes = new[] { 0 }
            };

            var result = await new CommandOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(runner.CallCount, Is.EqualTo(1));
            Assert.That(runner.LastRequest.FileName, Is.EqualTo(file));
            Assert.That(runner.LastRequest.Arguments, Is.EqualTo("--check"));
            Assert.That(runner.LastRequest.WorkingDirectory, Is.EqualTo(Path.GetDirectoryName(file)));
        }

        [Test]
        public async Task Cmd_file_uses_cmd_interpreter()
        {
            var file = CreateScript("run.cmd");
            var runner = new FakeProcessRunner();
            var operation = new CommandOperation
            {
                Id = "command",
                Name = "command",
                Type = "Command",
                ResolvedFile = file,
                Language = CommandLanguage.Cmd,
                Arguments = "--check",
                TimeoutMinutes = 1,
                SuccessExitCodes = new[] { 0 }
            };

            var result = await new CommandOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(Path.GetFileName(runner.LastRequest.FileName), Is.EqualTo("cmd.exe"));
            Assert.That(runner.LastRequest.Arguments, Does.Contain("/d /s /c"));
            Assert.That(runner.LastRequest.Arguments, Does.Contain(file));
            Assert.That(runner.LastRequest.Environment["VISIONIPC_DATA_ROOT"], Is.EqualTo(root));
        }

        [Test]
        public async Task PowerShell_file_uses_powershell_and_bypass()
        {
            var file = CreateScript("run.ps1");
            var runner = new FakeProcessRunner();
            var operation = new CommandOperation
            {
                Id = "command",
                Name = "command",
                Type = "Command",
                ResolvedFile = file,
                Language = CommandLanguage.PowerShell,
                Arguments = "-Check",
                TimeoutMinutes = 1,
                SuccessExitCodes = new[] { 0 }
            };

            var result = await new CommandOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(Path.GetFileName(runner.LastRequest.FileName), Is.EqualTo("powershell.exe"));
            Assert.That(runner.LastRequest.Arguments, Does.Contain("-ExecutionPolicy Bypass"));
            Assert.That(runner.LastRequest.Arguments, Does.Contain("-File"));
            Assert.That(runner.LastRequest.Arguments, Does.Contain(file));
        }

        [Test]
        public async Task Inline_power_shell_writes_and_removes_a_temporary_script()
        {
            var runner = new FakeProcessRunner();
            var operation = new CommandOperation
            {
                Id = "command",
                Name = "command",
                Type = "Command",
                Language = CommandLanguage.PowerShell,
                Code = "Write-Output 'inline'",
                Strict = true,
                TimeoutMinutes = 1,
                SuccessExitCodes = new[] { 0 }
            };

            var result = await new CommandOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(Path.GetFileName(runner.LastRequest.FileName), Is.EqualTo("powershell.exe"));
            Assert.That(runner.LastRequest.Arguments, Does.Contain("-ExecutionPolicy Bypass"));
            Assert.That(runner.LastRequest.Arguments, Does.Contain("VisionIPC-Deployment"));
        }

        [Test]
        public async Task Non_success_exit_code_fails()
        {
            var file = CreateCommand();
            var runner = new FakeProcessRunner
            {
                Result = new ProcessResult { ExitCode = 3, Output = "", Error = "" }
            };
            var operation = new CommandOperation
            {
                Id = "command",
                Name = "command",
                Type = "Command",
                ResolvedFile = file,
                Arguments = string.Empty,
                TimeoutMinutes = 1,
                SuccessExitCodes = new[] { 0 }
            };

            var result = await new CommandOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
        }

        private string CreateCommand()
        {
            return CreateScript("run.exe");
        }

        private string CreateScript(string fileName)
        {
            var file = Path.Combine(root, fileName);
            File.WriteAllText(file, "not executed by this test");
            return file;
        }

        private OperationContext CreateContext(FakeProcessRunner runner)
        {
            return new OperationContext
            {
                PackageRoot = root,
                DataRoot = root,
                LogDirectory = "logs",
                Paths = new PathPolicy(),
                ProcessRunner = runner,
                Log = (level, message) => { }
            };
        }
    }
}
