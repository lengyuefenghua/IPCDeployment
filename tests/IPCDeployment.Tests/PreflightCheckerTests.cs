using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using IPCDeployment.Core;

namespace IPCDeployment.Tests
{
    [TestFixture]
    public sealed class PreflightCheckerTests
    {
        private readonly string packageRoot = Path.GetTempPath();

        [Test]
        public void Check_reports_missing_admin()
        {
            var config = CreateConfig();
            var environment = new FakePreflightEnvironment { IsAdministrator = false };

            var result = new PreflightChecker(new PathPolicy()).Check(config, packageRoot, environment);

            Assert.That(result.Errors.Count, Is.GreaterThan(0));
            Assert.That(result.Errors[0].Message.Contains("管理员"), Is.True);
        }

        [Test]
        public void Check_reports_missing_adapter()
        {
            var config = CreateConfig();
            config.Operations.Add(new NetworkOperation
            {
                Id = "network",
                Name = "network",
                Type = "Network",
                Adapter = "missing",
                Kind = NetworkOperationKind.AdvancedProperty,
                Keyword = "*JumboPacket",
                Value = "9014",
                ValueType = RegistryValueType.String,
                Order = 0
            });
            var environment = new FakePreflightEnvironment { AdapterPresent = false };

            var result = new PreflightChecker(new PathPolicy()).Check(config, packageRoot, environment);

            Assert.That(result.Errors.Exists(e => e.Message.Contains("未找到网卡")), Is.True);
        }

        [Test]
        public void Check_rejects_speed_duplex_after_another_advanced_property()
        {
            var config = CreateConfig();
            config.Operations.Add(new NetworkOperation
            {
                Id = "jumbo",
                Name = "jumbo",
                Type = "Network",
                Adapter = "NIC",
                Kind = NetworkOperationKind.AdvancedProperty,
                Keyword = "*JumboPacket",
                Value = "9014",
                ValueType = RegistryValueType.String,
                Order = 0
            });
            config.Operations.Add(new NetworkOperation
            {
                Id = "speed",
                Name = "speed",
                Type = "Network",
                Adapter = "NIC",
                Kind = NetworkOperationKind.AdvancedProperty,
                Keyword = "*SpeedDuplex",
                Value = "6",
                ValueType = RegistryValueType.String,
                Order = 1
            });

            var result = new PreflightChecker(new PathPolicy()).Check(config, packageRoot, new FakePreflightEnvironment());

            Assert.That(result.Errors.Exists(e => e.Message.Contains("SpeedDuplex")), Is.True);
        }

        [Test]
        public void Check_warns_when_service_is_not_present()
        {
            var config = CreateConfig();
            config.Operations.Add(new ServiceOperation
            {
                Id = "service",
                Name = "service",
                Type = "Service",
                ServiceName = "Missing",
                State = ServiceState.Running,
                Order = 0
            });
            var environment = new FakePreflightEnvironment { ServicePresent = false };

            var result = new PreflightChecker(new PathPolicy()).Check(config, packageRoot, environment);

            Assert.That(result.Warnings.Exists(w => w.Message.Contains("服务当前不存在")), Is.True);
        }

        [Test]
        public void Check_warns_when_autostart_target_is_missing()
        {
            var config = CreateConfig();
            config.Operations.Add(new AutoStartOperation
            {
                Id = "autostart",
                Name = "autostart",
                Type = "AutoStart",
                Hive = RegistryHiveKind.HKCU,
                ValueName = "Test",
                Command = Path.Combine(packageRoot, "definitely-missing-" + Guid.NewGuid().ToString("N") + ".exe"),
                Enabled = true,
                Order = 0
            });

            var result = new PreflightChecker(new PathPolicy()).Check(config, packageRoot, new FakePreflightEnvironment());

            Assert.That(result.Warnings.Exists(w => w.Message.Contains("自启目标当前不存在")), Is.True);
        }

        [Test]
        public void Check_warns_when_power_is_not_last()
        {
            var config = CreateConfig();
            config.Operations.Add(new PowerOperation
            {
                Id = "power",
                Name = "power",
                Type = "Power",
                Setting = PowerSetting.Hibernate,
                Value = PowerValue.Off,
                Order = 0
            });
            config.Operations.Add(new CopyOperation
            {
                Id = "copy",
                Name = "copy",
                Type = "Copy",
                Order = 1
            });

            var result = new PreflightChecker(new PathPolicy()).Check(config, packageRoot, new FakePreflightEnvironment());

            Assert.That(result.Warnings.Exists(w => w.Message.Contains("Power")), Is.True);
        }

        [Test]
        public void Check_rejects_an_installer_hash_mismatch()
        {
            var file = Path.Combine(packageRoot, "ipcdeployment-hash-" + Guid.NewGuid().ToString("N") + ".exe");
            File.WriteAllText(file, "not the expected installer");
            try
            {
                var hash = ComputeSha256(file);
                var config = CreateConfig();
                config.Operations.Add(new InstallOperation
                {
                    Id = "install",
                    Name = "install",
                    Type = "Install",
                    ResolvedFile = file,
                    Sha256 = new string('0', 64),
                    TimeoutMinutes = 1,
                    SuccessExitCodes = new[] { 0 },
                    Order = 0
                });

                var result = new PreflightChecker(new PathPolicy()).Check(config, packageRoot, new FakePreflightEnvironment());

                Assert.That(result.Errors.Exists(e => e.Message.Contains("哈希")), Is.True);
                Assert.That(hash.Length, Is.EqualTo(64));
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static DeploymentConfig CreateConfig()
        {
            return new DeploymentConfig
            {
                Mode = DeploymentMode.Execute,
                LogDirectory = "logs"
            };
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
