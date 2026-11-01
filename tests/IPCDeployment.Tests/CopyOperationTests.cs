using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using IPCDeployment.Core;

namespace IPCDeployment.Tests
{
    [TestFixture]
    public sealed class CopyOperationTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "IPCDeployment-CopyTests-" + Guid.NewGuid().ToString("N"));
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
        public async Task Copy_copies_file_and_skips_identical_second_run()
        {
            var source = Path.Combine(root, "source.txt");
            var target = Path.Combine(root, "target.txt");
            File.WriteAllText(source, "hello");
            var operation = new CopyOperation
            {
                Id = "copy",
                Name = "copy",
                Type = "Copy",
                ResolvedSource = source,
                ResolvedTarget = target,
                Overwrite = CopyOverwriteMode.IfDifferent,
                Backup = false
            };
            var executor = new CopyOperationExecutor();

            var first = await executor.ExecuteAsync(operation, CreateContext(), CancellationToken.None);
            var second = await executor.ExecuteAsync(operation, CreateContext(), CancellationToken.None);

            Assert.That(first.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(File.ReadAllText(target), Is.EqualTo("hello"));
            Assert.That(second.Status, Is.EqualTo(OperationStatus.AlreadySatisfied));
        }

        [Test]
        public async Task Copy_backs_up_existing_target_when_overwriting()
        {
            var source = Path.Combine(root, "source.txt");
            var target = Path.Combine(root, "target.txt");
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            var operation = new CopyOperation
            {
                Id = "copy",
                Name = "copy",
                Type = "Copy",
                ResolvedSource = source,
                ResolvedTarget = target,
                Overwrite = CopyOverwriteMode.Always,
                Backup = true
            };

            var result = await new CopyOperationExecutor().ExecuteAsync(operation, CreateContext(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(File.ReadAllText(target), Is.EqualTo("new"));
            Assert.That(Directory.GetFiles(root, "target.txt.bak-*").Length, Is.EqualTo(1));
        }

        [Test]
        public async Task Copy_skips_when_target_exists_and_ifNotExists_is_selected()
        {
            var source = Path.Combine(root, "source.txt");
            var target = Path.Combine(root, "target.txt");
            File.WriteAllText(source, "new");
            File.WriteAllText(target, "old");
            var operation = new CopyOperation
            {
                Id = "copy",
                Name = "copy",
                Type = "Copy",
                ResolvedSource = source,
                ResolvedTarget = target,
                Overwrite = CopyOverwriteMode.IfNotExists,
                Backup = false
            };

            var result = await new CopyOperationExecutor().ExecuteAsync(operation, CreateContext(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.AlreadySatisfied));
            Assert.That(File.ReadAllText(target), Is.EqualTo("old"));
        }

        [Test]
        public async Task Copy_copies_directory_tree()
        {
            var source = Path.Combine(root, "source");
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            File.WriteAllText(Path.Combine(source, "nested", "b.txt"), "b");
            var operation = new CopyOperation
            {
                Id = "copy",
                Name = "copy",
                Type = "Copy",
                ResolvedSource = source,
                ResolvedTarget = target,
                Overwrite = CopyOverwriteMode.IfDifferent,
                Backup = false
            };

            var result = await new CopyOperationExecutor().ExecuteAsync(operation, CreateContext(), CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(File.ReadAllText(Path.Combine(target, "a.txt")), Is.EqualTo("a"));
            Assert.That(File.ReadAllText(Path.Combine(target, "nested", "b.txt")), Is.EqualTo("b"));
        }

        private OperationContext CreateContext()
        {
            return new OperationContext
            {
                PackageRoot = root,
                DataRoot = root,
                LogDirectory = "logs",
                Paths = new PathPolicy(),
                ProcessRunner = new FakeProcessRunner(),
                Log = (level, message) => { }
            };
        }
    }
}
