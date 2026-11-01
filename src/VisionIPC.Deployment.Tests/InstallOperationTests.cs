using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VisionIPC.Deployment.Core;

namespace VisionIPC.Deployment.Tests
{
    [TestFixture]
    public sealed class InstallOperationTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "VisionIPC-InstallTests-" + Guid.NewGuid().ToString("N"));
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
        public async Task Hash_mismatch_does_not_start_process()
        {
            var installer = CreateInstaller("installer");
            var runner = new FakeProcessRunner();
            var operation = CreateOperation(installer.Path, new string('0', 64));

            var result = await new InstallOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(runner.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Matching_hash_starts_process()
        {
            var installer = CreateInstaller("installer");
            var runner = new FakeProcessRunner();
            var operation = CreateOperation(installer.Path, installer.Hash);

            var result = await new InstallOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(runner.CallCount, Is.EqualTo(1));
            Assert.That(runner.LastRequest.FileName, Is.EqualTo(installer.Path));
            Assert.That(runner.LastRequest.Arguments, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task SkipIfFileExists_skips_without_starting_process()
        {
            var installer = CreateInstaller("installer");
            var skipPath = Path.Combine(root, "already-installed.marker");
            File.WriteAllText(skipPath, "present");
            var runner = new FakeProcessRunner();
            var operation = CreateOperation(installer.Path, installer.Hash);
            operation.SkipIfFileExists = skipPath;

            var result = await new InstallOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.AlreadySatisfied));
            Assert.That(runner.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Exit_3010_marks_restart_required()
        {
            var installer = CreateInstaller("installer");
            var runner = new FakeProcessRunner
            {
                Result = new ProcessResult { ExitCode = 3010, Output = "", Error = "" }
            };
            var operation = CreateOperation(installer.Path, installer.Hash);

            var result = await new InstallOperationExecutor().ExecuteAsync(operation, CreateContext(runner), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(result.RestartRequired, Is.True);
        }

        private InstallOperation CreateOperation(string path, string hash)
        {
            return new InstallOperation
            {
                Id = "install",
                Name = "install",
                Type = "Install",
                ResolvedFile = path,
                Sha256 = hash,
                Arguments = string.Empty,
                TimeoutMinutes = 1,
                SuccessExitCodes = new[] { 0 }
            };
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

        private InstallerFile CreateInstaller(string content)
        {
            var path = Path.Combine(root, "setup-" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllText(path, content);
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return new InstallerFile
                {
                    Path = path,
                    Hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty)
                };
            }
        }

        private sealed class InstallerFile
        {
            public string Path { get; set; }
            public string Hash { get; set; }
        }
    }
}
