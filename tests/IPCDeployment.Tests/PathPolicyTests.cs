using System;
using System.IO;
using NUnit.Framework;
using IPCDeployment.Core;

namespace IPCDeployment.Tests
{
    [TestFixture]
    public sealed class PathPolicyTests
    {
        private readonly PathPolicy paths = new PathPolicy();
        private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "IPCDeployment-Data");

        [Test]
        public void ResolveSource_accepts_nested_data_file()
        {
            var result = paths.ResolveSource(dataRoot, "files/sample.txt", DataArea.Files);

            StringAssert.EndsWith(Path.Combine("files", "sample.txt"), result);
        }

        [Test]
        public void ResolveSource_rejects_absolute_path()
        {
            Assert.Throws<InvalidOperationException>(() =>
                paths.ResolveSource(dataRoot, "C:/Windows/System32/cmd.exe", DataArea.Files));
        }

        [Test]
        public void ResolveSource_rejects_traversal()
        {
            Assert.Throws<InvalidOperationException>(() =>
                paths.ResolveSource(dataRoot, "files/../outside.txt", DataArea.Files));
        }

        [Test]
        public void ResolveDestination_expands_environment_variables()
        {
            var result = paths.ResolveDestination("%TEMP%/ipcdeployment-test.txt");

            Assert.That(Path.IsPathRooted(result), Is.True);
            StringAssert.EndsWith("ipcdeployment-test.txt", result);
        }

        [Test]
        public void ResolveDestination_rejects_relative_path()
        {
            Assert.Throws<InvalidOperationException>(() => paths.ResolveDestination("relative.txt"));
        }

        [Test]
        public void ResolveDestination_rejects_drive_root()
        {
            Assert.Throws<InvalidOperationException>(() => paths.ResolveDestination("C:/"));
        }

        [Test]
        public void ResolveLogDirectory_accepts_relative_directory()
        {
            var result = paths.ResolveLogDirectory(Path.GetTempPath(), "logs");

            Assert.That(result, Is.EqualTo(Path.Combine(Path.GetTempPath(), "logs")));
        }

        [Test]
        public void ResolveLogDirectory_rejects_rooted_directory()
        {
            Assert.Throws<InvalidOperationException>(() => paths.ResolveLogDirectory(Path.GetTempPath(), "C:/logs"));
        }

        [Test]
        public void ResolveLogDirectory_rejects_traversal()
        {
            Assert.Throws<InvalidOperationException>(() => paths.ResolveLogDirectory(Path.GetTempPath(), "../logs"));
        }
    }
}
