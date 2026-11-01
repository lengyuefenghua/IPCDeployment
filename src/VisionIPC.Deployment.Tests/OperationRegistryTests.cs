using System;
using NUnit.Framework;
using VisionIPC.Deployment.Core;

namespace VisionIPC.Deployment.Tests
{
    [TestFixture]
    public sealed class OperationRegistryTests
    {
        [TestCase("Install")]
        [TestCase("Copy")]
        [TestCase("Registry")]
        [TestCase("Command")]
        [TestCase("Service")]
        [TestCase("Network")]
        [TestCase("Power")]
        [TestCase("AutoStart")]
        [TestCase("Delay")]
        public void Built_in_registry_resolves_every_supported_type(string type)
        {
            var registry = BuiltInExecutors.CreateRegistry();

            var executor = registry.Resolve(type);

            Assert.That(executor, Is.Not.Null);
            Assert.That(executor.Type, Is.EqualTo(type));
        }

        [Test]
        public void Unknown_type_is_rejected()
        {
            var registry = BuiltInExecutors.CreateRegistry();

            Assert.Throws<InvalidOperationException>(() => registry.Resolve("Unknown"));
        }
    }
}
