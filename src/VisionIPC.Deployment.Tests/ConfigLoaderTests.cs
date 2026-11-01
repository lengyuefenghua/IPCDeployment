using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VisionIPC.Deployment.Core;

namespace VisionIPC.Deployment.Tests
{
    [TestFixture]
    public sealed class ConfigLoaderTests
    {
        private string root;
        private string configPath;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "VisionIPC-ConfigTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "data", "software"));
            Directory.CreateDirectory(Path.Combine(root, "data", "files"));
            Directory.CreateDirectory(Path.Combine(root, "data", "commands"));
            File.WriteAllText(Path.Combine(root, "data", "software", "setup.exe"), "exe");
            File.WriteAllText(Path.Combine(root, "data", "files", "sample.txt"), "sample");
            File.WriteAllText(Path.Combine(root, "data", "commands", "run.exe"), "exe");
            File.WriteAllText(Path.Combine(root, "data", "commands", "run.cmd"), "cmd");
            File.WriteAllText(Path.Combine(root, "data", "commands", "run.ps1"), "ps1");
            File.WriteAllText(Path.Combine(root, "data", "commands", "run.txt"), "txt");
            configPath = Path.Combine(root, "config.xml");
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
        public void Load_accepts_all_supported_operation_types()
        {
            var result = Load(AllTypesConfig);

            Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors.Select(e => e.ToDisplayString())));
            Assert.That(result.Config.Operations.Count, Is.EqualTo(9));
            Assert.That(result.Summary.GroupCount, Is.EqualTo(1));
            Assert.That(result.Summary.TaskCount, Is.EqualTo(1));
            Assert.That(result.Summary.InstallCount, Is.EqualTo(1));
            Assert.That(result.Summary.CopyCount, Is.EqualTo(1));
            Assert.That(result.Summary.RegistryCount, Is.EqualTo(1));
            Assert.That(result.Summary.CommandCount, Is.EqualTo(1));
            Assert.That(result.Summary.ServiceCount, Is.EqualTo(1));
            Assert.That(result.Summary.NetworkCount, Is.EqualTo(1));
            Assert.That(result.Summary.PowerCount, Is.EqualTo(1));
            Assert.That(result.Summary.AutoStartCount, Is.EqualTo(1));
            Assert.That(result.Config.Operations[0].Order, Is.EqualTo(0));
            Assert.That(result.Config.Operations[8].Order, Is.EqualTo(8));
            Assert.That(result.Config.Operations.All(o => !string.IsNullOrWhiteSpace(o.Type)), Is.True);
            Assert.That(result.Config.Operations[0].Type, Is.EqualTo("Install"));
            Assert.That(result.Config.Operations[7].Type, Is.EqualTo("AutoStart"));
            Assert.That(result.Config.Operations[8].Type, Is.EqualTo("Delay"));
        }

        [Test]
        public void Load_accepts_command_scripts_and_inline_code()
        {
            var result = Load(WrapTask(
                "<Command id=\"cmd\" name=\"cmd\" file=\"commands/run.cmd\" />" +
                "<Command id=\"ps\" name=\"ps\" file=\"commands/run.ps1\" />" +
                "<Command id=\"inline-cmd\" name=\"inline-cmd\" language=\"cmd\"><Code><![CDATA[echo hi]]></Code></Command>" +
                "<Command id=\"inline-ps\" name=\"inline-ps\" language=\"powershell\"><Code><![CDATA[Write-Output hi]]></Code></Command>"));

            Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors.Select(e => e.ToDisplayString())));
            Assert.That(result.Config.Operations.OfType<CommandOperation>().Select(c => c.Language),
                Is.EqualTo(new[] { CommandLanguage.Cmd, CommandLanguage.PowerShell, CommandLanguage.Cmd, CommandLanguage.PowerShell }));
            Assert.That(result.Config.Operations.OfType<CommandOperation>().Last().Code, Does.Contain("Write-Output"));
        }

        [Test]
        public void Load_rejects_file_and_code_together()
        {
            var result = Load(WrapTask(
                "<Command id=\"both\" name=\"both\" file=\"commands/run.ps1\" language=\"powershell\"><Code><![CDATA[Write-Output hi]]></Code></Command>"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("不能同时")), Is.True);
        }

        [Test]
        public void Load_rejects_inline_code_without_language()
        {
            var result = Load(WrapTask(
                "<Command id=\"inline\" name=\"inline\"><Code><![CDATA[Write-Output hi]]></Code></Command>"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("language")), Is.True);
        }

        [Test]
        public void Load_rejects_inline_code_with_arguments()
        {
            var result = Load(WrapTask(
                "<Command id=\"inline\" name=\"inline\" language=\"powershell\" arguments=\"--x\"><Code><![CDATA[Write-Output hi]]></Code></Command>"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("arguments")), Is.True);
        }

        [Test]
        public void Load_rejects_unsupported_command_extension()
        {
            var result = Load(WrapTask(
                "<Command id=\"bad\" name=\"bad\" file=\"commands/run.txt\" />"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("只支持")), Is.True);
        }

        [Test]
        public void Load_parses_service_name_separately_from_display_name()
        {
            var result = Load(AllTypesConfig);

            var service = result.Config.Operations.OfType<ServiceOperation>().Single();
            Assert.That(service.Name, Is.EqualTo("服务"));
            Assert.That(service.ServiceName, Is.EqualTo("MySQL"));
            Assert.That(service.Type, Is.EqualTo("Service"));
        }

        [Test]
        public void Load_preserves_group_task_and_step_order()
        {
            var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Deployment schemaVersion=""2.0"" mode=""preview"">
  <Groups>
    <Group key=""g1"" name=""第一组"">
      <Task key=""t1"" name=""任务一"">
        <Copy id=""a1"" name=""a1"" source=""files/sample.txt"" target=""%TEMP%/a1.txt"" />
        <Copy id=""a2"" name=""a2"" source=""files/sample.txt"" target=""%TEMP%/a2.txt"" />
      </Task>
      <Task key=""t2"" name=""任务二"">
        <Copy id=""a3"" name=""a3"" source=""files/sample.txt"" target=""%TEMP%/a3.txt"" />
      </Task>
    </Group>
    <Group key=""g2"" name=""第二组"">
      <Task key=""t3"" name=""任务三"">
        <Copy id=""b1"" name=""b1"" source=""files/sample.txt"" target=""%TEMP%/b1.txt"" />
      </Task>
    </Group>
  </Groups>
</Deployment>";

            var result = Load(xml);

            Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors.Select(e => e.ToDisplayString())));
            Assert.That(result.Config.Groups.Select(g => g.Name), Is.EqualTo(new[] { "第一组", "第二组" }));
            Assert.That(result.Config.Groups[0].Tasks.Select(t => t.Key), Is.EqualTo(new[] { "t1", "t2" }));
            Assert.That(result.Config.Operations.Select(o => o.Id), Is.EqualTo(new[] { "a1", "a2", "a3", "b1" }));
            Assert.That(result.Config.Operations[2].TaskKey, Is.EqualTo("t2"));
            Assert.That(result.Config.Operations[3].GroupName, Is.EqualTo("第二组"));
        }

        [Test]
        public void Load_rejects_duplicate_group_names()
        {
            var result = Load(WrapGroups(
                "<Group key=\"g1\" name=\"重复\"><Task key=\"t1\" name=\"t1\"><Copy id=\"a\" name=\"a\" source=\"files/sample.txt\" target=\"%TEMP%/a.txt\" /></Task></Group>" +
                "<Group key=\"g2\" name=\" 重复 \"><Task key=\"t2\" name=\"t2\"><Copy id=\"b\" name=\"b\" source=\"files/sample.txt\" target=\"%TEMP%/b.txt\" /></Task></Group>"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("分组名称重复")), Is.True);
        }

        [Test]
        public void Load_rejects_duplicate_group_keys()
        {
            var result = Load(WrapGroups(
                "<Group key=\"same\" name=\"一\"><Task key=\"t1\" name=\"t1\"><Copy id=\"a\" name=\"a\" source=\"files/sample.txt\" target=\"%TEMP%/a.txt\" /></Task></Group>" +
                "<Group key=\"same\" name=\"二\"><Task key=\"t2\" name=\"t2\"><Copy id=\"b\" name=\"b\" source=\"files/sample.txt\" target=\"%TEMP%/b.txt\" /></Task></Group>"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("分组 key 重复")), Is.True);
        }

        [Test]
        public void Load_rejects_an_empty_group()
        {
            var result = Load(WrapGroups("<Group key=\"g\" name=\"空组\"></Group>"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("分组至少需要一个任务")), Is.True);
        }

        [Test]
        public void Load_rejects_an_empty_task()
        {
            var result = Load(WrapGroups("<Group key=\"g\" name=\"组\"><Task key=\"t\" name=\"空任务\"></Task></Group>"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("任务至少需要一个步骤")), Is.True);
        }

        [Test]
        public void Load_rejects_an_unknown_operation_element()
        {
            var result = Load(WrapTask("<Unknown id=\"x\" name=\"x\" />"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("未知操作类型")), Is.True);
        }

        [Test]
        public void Load_rejects_an_unknown_attribute()
        {
            var result = Load(WrapTask("<Copy id=\"copy\" name=\"copy\" source=\"files/sample.txt\" target=\"%TEMP%/x.txt\" unexpected=\"1\" />"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("未知属性")), Is.True);
        }

        [Test]
        public void Load_rejects_legacy_onError_attribute()
        {
            var result = Load(WrapTask("<Copy id=\"copy\" name=\"copy\" source=\"files/sample.txt\" target=\"%TEMP%/x.txt\" onError=\"continue\" />"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("未知属性")), Is.True);
        }

        [Test]
        public void Load_rejects_a_source_path_that_escapes_data()
        {
            var result = Load(WrapTask("<Copy id=\"copy\" name=\"copy\" source=\"files/../outside.txt\" target=\"%TEMP%/x.txt\" />"));

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Load_rejects_a_relative_copy_target()
        {
            var result = Load(WrapTask("<Copy id=\"copy\" name=\"copy\" source=\"files/sample.txt\" target=\"relative/x.txt\" />"));

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public void Load_rejects_duplicate_operation_ids()
        {
            var steps = "<Copy id=\"same\" name=\"one\" source=\"files/sample.txt\" target=\"%TEMP%/one.txt\" />" +
                        "<Copy id=\"same\" name=\"two\" source=\"files/sample.txt\" target=\"%TEMP%/two.txt\" />";
            var result = Load(WrapTask(steps));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("重复")), Is.True);
        }

        [Test]
        public void Load_rejects_schema_1_0()
        {
            var result = Load(AllTypesConfig.Replace("schemaVersion=\"2.0\"", "schemaVersion=\"1.0\""));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("schemaVersion")), Is.True);
        }

        [Test]
        public void Load_rejects_an_unknown_mode()
        {
            var result = Load(AllTypesConfig.Replace("mode=\"preview\"", "mode=\"demo\""));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("mode")), Is.True);
        }

        [Test]
        public void Load_rejects_a_malformed_install_hash()
        {
            var result = Load(WrapTask("<Install id=\"install\" name=\"install\" file=\"software/setup.exe\" sha256=\"xyz\" />"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("sha256")), Is.True);
        }

        [Test]
        public void Load_rejects_a_service_without_service_name()
        {
            var result = Load(WrapTask("<Service id=\"service\" name=\"服务\" state=\"Running\" />"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("serviceName")), Is.True);
        }

        [Test]
        public void Load_rejects_a_dtd()
        {
            var xml = "<?xml version=\"1.0\"?><!DOCTYPE Deployment [<!ENTITY xxe SYSTEM \"file:///C:/Windows/win.ini\">]>" +
                      "<Deployment schemaVersion=\"2.0\" mode=\"preview\"><Groups><Group key=\"g\" name=\"组\"><Task key=\"t\" name=\"任务\">" +
                      "<Copy id=\"c\" name=\"c\" source=\"files/sample.txt\" target=\"%TEMP%/x.txt\" />" +
                      "</Task></Group></Groups></Deployment>";
            var result = Load(xml);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Errors.Any(e => e.Message.Contains("XML")), Is.True);
        }

        [Test]
        public void Load_accepts_execute_mode()
        {
            var result = Load(AllTypesConfig.Replace("mode=\"preview\"", "mode=\"execute\""));

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Config.Mode, Is.EqualTo(DeploymentMode.Execute));
        }

        [Test]
        public void Repository_configs_load_from_root()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "config.xml")))
            {
                directory = directory.Parent;
            }
            Assert.That(directory, Is.Not.Null, "repository root not found");
            var loader = new ConfigLoader(new PathPolicy());

            var active = loader.Load(Path.Combine(directory.FullName, "config.xml"));
            var reference = loader.Load(Path.Combine(directory.FullName, "config.sample.xml"));

            Assert.That(active.IsValid, Is.True, string.Join("; ", active.Errors.Select(e => e.ToDisplayString())));
            Assert.That(active.Config.Mode, Is.EqualTo(DeploymentMode.Execute));
            Assert.That(reference.IsValid, Is.True, string.Join("; ", reference.Errors.Select(e => e.ToDisplayString())));
            Assert.That(reference.Config.Mode, Is.EqualTo(DeploymentMode.Preview));
            Assert.That(reference.Config.Operations.Count, Is.GreaterThan(0));
        }

        private ConfigLoadResult Load(string xml)
        {
            File.WriteAllText(configPath, xml);
            return new ConfigLoader(new PathPolicy()).Load(configPath);
        }

        private static string WrapGroups(string groups)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Deployment schemaVersion=\"2.0\" mode=\"preview\">" +
                "<Groups>" + groups + "</Groups>" +
                "</Deployment>";
        }

        private static string WrapTask(string operations)
        {
            return WrapGroups("<Group key=\"test\" name=\"测试\"><Task key=\"task\" name=\"任务\">" +
                operations + "</Task></Group>");
        }

        private const string AllTypesConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Deployment schemaVersion=""2.0"" mode=""preview"">
  <Meta name=""Test"" description=""Test"" />
  <Options><LogDirectory>logs</LogDirectory></Options>
  <Groups>
    <Group key=""all"" name=""全部"">
      <Task key=""all-task"" name=""全部任务"">
        <Install id=""install"" name=""安装"" file=""software/setup.exe"" arguments=""/S"" timeoutMinutes=""5"" successExitCodes=""0,3010"" />
        <Copy id=""copy"" name=""复制"" source=""files/sample.txt"" target=""%TEMP%/VisionIPC-Tests/target.txt"" overwrite=""ifDifferent"" backup=""false"" />
        <Registry id=""registry"" name=""注册表"" hive=""HKCU"" key=""Software/Test"" valueName=""X"" valueType=""String"" value=""1"" view=""64"" />
        <Command id=""command"" name=""命令"" file=""commands/run.exe"" arguments=""--check"" timeoutMinutes=""5"" successExitCodes=""0"" />
        <Service id=""service"" name=""服务"" serviceName=""MySQL"" startupType=""Automatic"" state=""Running"" />
        <Network id=""network"" name=""网络"" adapter=""以太网 4"" kind=""AdvancedProperty"" keyword=""*JumboPacket"" value=""9014"" valueType=""String"" expectedBefore=""1514"" />
        <Power id=""power"" name=""电源"" setting=""Hibernate"" value=""Off"" />
        <AutoStart id=""autostart"" name=""自启"" hive=""HKCU"" valueName=""Test"" command=""%ProgramFiles%/Test/test.exe"" arguments="""" enabled=""true"" />
        <Delay id=""delay"" name=""等待"" seconds=""1"" />
      </Task>
    </Group>
  </Groups>
</Deployment>";
    }
}
