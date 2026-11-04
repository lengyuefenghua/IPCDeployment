using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IPCDeployment.Core;
using NUnit.Framework;

namespace IPCDeployment.Tests
{
    [TestFixture]
    public class ConfigurationDocumentTests
    {
        private string tempDirectory;
        private string dataRoot;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "IPCDeploymentEditor-" + Guid.NewGuid().ToString("N"));
            dataRoot = Path.Combine(tempDirectory, "data");
            Directory.CreateDirectory(tempDirectory);
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
        public void 新建骨架保存后能被加载器接受()
        {
            var path = Path.Combine(tempDirectory, "config.xml");

            var document = ConfigurationDocument.CreateNew(dataRoot);
            var result = document.Save(path, false);

            Assert.That(result.Status, Is.EqualTo(EditorSaveStatus.Saved), result.Message);
            Assert.That(File.Exists(path), Is.True);

            var load = new ConfigLoader(new PathPolicy()).Load(path, dataRoot);
            Assert.That(load.Errors, Is.Empty);
        }

        [Test]
        public void 往返保真_九类步骤与分组排序保存后不变()
        {
            var path = Path.Combine(tempDirectory, "config.xml");
            CreateDataFiles();
            File.WriteAllText(path, CommentedConfig, new UTF8Encoding(false));

            var open = ConfigurationDocument.Open(path, dataRoot);
            Assert.That(open.Succeeded, Is.True, Describe(open.Issues));
            var document = open.Document;

            var secondGroup = document.Root.Children[1];
            var newTask = document.AddTask(secondGroup);
            // 新增任务会带一个占位 Delay，先移除，只测用户加入的九类。
            document.Remove(newTask.Children[0]);
            foreach (var type in ConfigFieldSchema.StepTypes)
            {
                var step = document.AddStep(newTask, type);
                Assert.That(step, Is.Not.Null, type);
                FillRequiredValues(document, step, type);
            }

            Assert.That(document.Move(secondGroup, -1), Is.True);

            var save = document.Save(path, false);
            Assert.That(save.Status, Is.EqualTo(EditorSaveStatus.Saved), Describe(save.Issues) + save.Message);

            var text = File.ReadAllText(path, Encoding.UTF8);
            Assert.That(text, Does.Contain("<!-- 分组注释 -->"), "注释必须保留");

            var reopened = ConfigurationDocument.Open(path, dataRoot);
            Assert.That(reopened.Succeeded, Is.True, Describe(reopened.Issues));
            var root = reopened.Document.Root;
            Assert.That(root.Children[0].Element.Attribute("name").Value, Is.EqualTo("第二组"), "分组顺序必须保留");
            Assert.That(root.Children[1].Element.Attribute("name").Value, Is.EqualTo("第一组"));

            var savedTask = root.Children[0].Children[1];
            Assert.That(savedTask.Children.Count, Is.EqualTo(9));
            foreach (var step in savedTask.Children)
            {
                foreach (var expected in ExpectedValues(step.ElementName))
                {
                    var field = reopened.Document.FieldsOf(step).FirstOrDefault(f => f.Name == expected[1]);
                    Assert.That(field, Is.Not.Null, step.ElementName + "." + expected[1]);
                    Assert.That(reopened.Document.GetValue(step, field), Is.EqualTo(expected[2]),
                        step.ElementName + "." + expected[1]);
                }
            }
        }

        [Test]
        public void 校验未通过时不写文件()
        {
            var path = Path.Combine(tempDirectory, "config.xml");
            CreateDataFiles();
            File.WriteAllText(path, CommentedConfig, new UTF8Encoding(false));
            var before = File.ReadAllBytes(path);

            var open = ConfigurationDocument.Open(path, dataRoot);
            Assert.That(open.Succeeded, Is.True, Describe(open.Issues));
            var document = open.Document;
            var task = document.Root.Children[0].Children[0];
            var copy = document.AddStep(task, "Copy");
            FillRequiredValues(document, copy, "Copy");
            var sourceField = document.FieldsOf(copy).First(field => field.Name == "source");
            document.SetValue(copy, sourceField, @"C:\windows\notepad.exe");

            var save = document.Save(path, false);

            Assert.That(save.Status, Is.EqualTo(EditorSaveStatus.ValidationFailed), save.Message);
            Assert.That(save.Issues, Is.Not.Empty);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "校验失败不得改动目标文件");
            Assert.That(File.Exists(path + ".bak"), Is.False, "校验失败不得产生备份");
            Assert.That(new ConfigLoader(new PathPolicy()).Load(path, dataRoot).Errors, Is.Empty);
        }

        [Test]
        public void 文件被外部修改时保存中止()
        {
            var path = Path.Combine(tempDirectory, "config.xml");
            CreateDataFiles();
            File.WriteAllText(path, CommentedConfig, new UTF8Encoding(false));

            var open = ConfigurationDocument.Open(path, dataRoot);
            Assert.That(open.Succeeded, Is.True, Describe(open.Issues));
            var document = open.Document;

            const string externalContent = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Deployment schemaVersion=\"2.0\" mode=\"preview\" />";
            File.WriteAllText(path, externalContent, new UTF8Encoding(false));

            var blocked = document.Save(path, false);
            Assert.That(blocked.Status, Is.EqualTo(EditorSaveStatus.ExternalModified), blocked.Message);
            Assert.That(File.ReadAllText(path, Encoding.UTF8), Is.EqualTo(externalContent), "未确认覆盖时不得改动文件");

            var document2 = ConfigurationDocument.Open(path, dataRoot);
            Assert.That(document2.Succeeded, Is.False, "被外部改坏的文件不应进入编辑");
        }

        [Test]
        public void 覆盖保存先写备份副本()
        {
            var path = Path.Combine(tempDirectory, "config.xml");
            CreateDataFiles();
            File.WriteAllText(path, CommentedConfig, new UTF8Encoding(false));
            var original = File.ReadAllBytes(path);

            var open = ConfigurationDocument.Open(path, dataRoot);
            Assert.That(open.Succeeded, Is.True, Describe(open.Issues));
            var document = open.Document;

            var save = document.Save(path, false);

            Assert.That(save.Status, Is.EqualTo(EditorSaveStatus.Saved), save.Message);
            Assert.That(File.Exists(path + ".bak"), Is.True);
            Assert.That(File.ReadAllBytes(path + ".bak"), Is.EqualTo(original));
            var load = new ConfigLoader(new PathPolicy()).Load(path, dataRoot);
            Assert.That(load.Errors, Is.Empty);
        }

        [Test]
        public void 目标文件被占用时保存失败且原文件不变()
        {
            var path = Path.Combine(tempDirectory, "config.xml");
            CreateDataFiles();
            File.WriteAllText(path, CommentedConfig, new UTF8Encoding(false));
            var original = File.ReadAllBytes(path);

            var open = ConfigurationDocument.Open(path, dataRoot);
            Assert.That(open.Succeeded, Is.True, Describe(open.Issues));
            var document = open.Document;
            var delay = document.Root.Children[0].Children[0].Children[0];
            document.SetValue(delay, document.FieldsOf(delay).First(field => field.Name == "seconds"), "5");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var save = document.Save(path, false);
                Assert.That(save.Status, Is.EqualTo(EditorSaveStatus.IoFailed), save.Message);
            }

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original), "写入失败不得留下半写文件");
            Assert.That(Directory.GetFiles(tempDirectory, "*.tmp-*"), Is.Empty, "临时文件必须清理");
        }

        private static string Describe(IEnumerable<ConfigIssue> issues)        {
            return string.Join("; ", issues.Select(issue => issue.ToDisplayString()));
        }

        private void CreateDataFiles()
        {
            var data = Path.Combine(tempDirectory, "data");
            Directory.CreateDirectory(Path.Combine(data, "software"));
            Directory.CreateDirectory(Path.Combine(data, "files"));
            Directory.CreateDirectory(Path.Combine(data, "commands"));
            File.WriteAllText(Path.Combine(data, "software", "app.exe"), "stub");
            File.WriteAllText(Path.Combine(data, "files", "a.txt"), "stub");
            File.WriteAllText(Path.Combine(data, "commands", "run.cmd"), "@echo off");
        }

        private static void FillRequiredValues(ConfigurationDocument document, EditorNode step, string type)
        {
            foreach (var expected in ExpectedValues(type))
            {
                var field = document.FieldsOf(step).FirstOrDefault(f => f.Name == expected[1]);
                Assert.That(field, Is.Not.Null, type + "." + expected[1]);
                document.SetValue(step, field, expected[2]);
            }
        }

        private static IEnumerable<string[]> ExpectedValues(string type)
        {
            return StepValues.Where(values => values[0] == type);
        }

        private static readonly string[][] StepValues =
        {
            new[] { "Install", "file", "software/app.exe" },
            new[] { "Copy", "source", "files/a.txt" },
            new[] { "Copy", "target", "%TEMP%/ipc-editor-copy.txt" },
            new[] { "Registry", "hive", "HKCU" },
            new[] { "Registry", "key", "Software/IPCDeployment/Editor" },
            new[] { "Registry", "valueName", "sample" },
            new[] { "Registry", "valueType", "String" },
            new[] { "Registry", "value", "9" },
            new[] { "Command", "file", "commands/run.cmd" },
            new[] { "Service", "serviceName", "Dnscache" },
            new[] { "Service", "state", "Running" },
            new[] { "Network", "adapter", "以太网" },
            new[] { "Network", "kind", "AdvancedProperty" },
            new[] { "Network", "keyword", "*JumboPacket" },
            new[] { "Network", "value", "9014" },
            new[] { "Power", "setting", "Hibernate" },
            new[] { "Power", "value", "Off" },
            new[] { "AutoStart", "hive", "HKCU" },
            new[] { "AutoStart", "valueName", "IPCDeployment-Editor" },
            new[] { "AutoStart", "command", "%SystemRoot%/System32/notepad.exe" },
            new[] { "Delay", "seconds", "7" }
        };

        private const string CommentedConfig = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Deployment schemaVersion=""2.0"" mode=""preview"">
  <Meta name=""编辑器测试"" description="""" />
  <Options><LogDirectory>logs</LogDirectory></Options>
  <Groups>
    <!-- 分组注释 -->
    <Group key=""g1"" name=""第一组"">
      <Task key=""t1"" name=""任务一"">
        <Delay id=""d1"" name=""等待"" seconds=""1"" />
      </Task>
    </Group>
    <Group key=""g2"" name=""第二组"">
      <Task key=""t2"" name=""任务二"">
        <Delay id=""d2"" name=""等待"" seconds=""2"" />
      </Task>
    </Group>
  </Groups>
</Deployment>
";
    }
}
