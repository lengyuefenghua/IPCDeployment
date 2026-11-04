using System;
using System.IO;
using IPCDeployment.Core;
using NUnit.Framework;

namespace IPCDeployment.Tests
{
    [TestFixture]
    public class SourcePathCatalogTests
    {
        private string tempDirectory;
        private string dataRoot;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "IPCDeploymentCatalog-" + Guid.NewGuid().ToString("N"));
            dataRoot = Path.Combine(tempDirectory, "data");
            Directory.CreateDirectory(Path.Combine(dataRoot, "software", "sub"));
            File.WriteAllText(Path.Combine(dataRoot, "software", "a.exe"), string.Empty);
            File.WriteAllText(Path.Combine(dataRoot, "software", "sub", "b.msi"), string.Empty);
            File.WriteAllText(Path.Combine(dataRoot, "software", "note.txt"), string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public void 按扩展名过滤_目录项以斜杠结尾()
        {
            var items = new SourcePathCatalog().List(dataRoot, "software/", new[] { ".exe", ".msi" }, false);

            Assert.That(items, Does.Contain("software/a.exe"));
            Assert.That(items, Does.Contain("software/sub/b.msi"));
            Assert.That(items, Does.Contain("software/sub/"));
            Assert.That(items, Does.Not.Contain("software/note.txt"));
        }

        [Test]
        public void 只列目录时不返回文件()
        {
            var items = new SourcePathCatalog().List(dataRoot, "commands/", null, true);

            Assert.That(items, Is.Empty);
            items = new SourcePathCatalog().List(dataRoot, "software/", null, true);
            Assert.That(items, Is.EquivalentTo(new[] { "software/sub/" }));
        }

        [Test]
        public void 前缀目录不存在时返回空列表()
        {
            var items = new SourcePathCatalog().List(dataRoot, "files/", null, false);

            Assert.That(items, Is.Empty);
        }

        [Test]
        public void 候选值都能通过源路径校验()
        {
            var policy = new PathPolicy();
            foreach (var item in new SourcePathCatalog().List(dataRoot, "software/", null, false))
            {
                Assert.DoesNotThrow(() => policy.ResolveSource(dataRoot, item, DataArea.Software), item);
            }
        }
    }
}
