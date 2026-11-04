using System.Reflection;
using NUnit.Framework;

namespace IPCDeployment.Tests
{
    [TestFixture]
    public sealed class EmbeddedAssemblyTests
    {
        private static readonly string[] ManagedDependencies =
        {
            "Wpf.Ui.dll",
            "Wpf.Ui.Abstractions.dll",
            "System.Memory.dll",
            "System.Buffers.dll",
            "System.Numerics.Vectors.dll",
            "System.Runtime.CompilerServices.Unsafe.dll"
        };

        [Test]
        public void Embedded_resources_cover_managed_dependencies()
        {
            var assembly = typeof(EmbeddedAssemblyLoader).Assembly;

            foreach (var dependency in ManagedDependencies)
            {
                Assert.That(
                    assembly.GetManifestResourceNames(),
                    Does.Contain("IPCDeployment.Resources." + dependency),
                    "缺少内嵌依赖：" + dependency);
            }
        }

        [Test]
        public void Loader_loads_embedded_dependency()
        {
            var assembly = EmbeddedAssemblyLoader.Load("Wpf.Ui");

            Assert.That(assembly, Is.Not.Null, "未能从内嵌资源加载 Wpf.Ui");
            Assert.That(assembly.GetName().Name, Is.EqualTo("Wpf.Ui"));
        }
    }
}
