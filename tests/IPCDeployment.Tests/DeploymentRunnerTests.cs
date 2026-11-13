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
    public sealed class DeploymentRunnerTests
    {
        [Test]
        public async Task Preview_does_not_call_executor()
        {
            var executor = new RecordingExecutor("Fake");
            var config = CreateConfig(DeploymentMode.Preview, Task("g", "组", "t1", "任务", new TestOperation("one", "one")));
            var progress = new RecordingProgress();

            var report = await CreateRunner(executor).RunAsync(config, true, progress, CancellationToken.None);

            Assert.That(executor.CallCount, Is.EqualTo(0));
            Assert.That(report.Results.Single().Status, Is.EqualTo(OperationStatus.NotExecuted));
            Assert.That(report.Tasks.Single().Status, Is.EqualTo(OperationStatus.NotExecuted));
            Assert.That(report.CompletedTaskCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Unconfirmed_execute_does_not_call_executor()
        {
            var executor = new RecordingExecutor("Fake");
            var config = CreateConfig(DeploymentMode.Execute, Task("g", "组", "t1", "任务", new TestOperation("one", "one")));

            var report = await CreateRunner(executor).RunAsync(config, false, null, CancellationToken.None);

            Assert.That(executor.CallCount, Is.EqualTo(0));
            Assert.That(report.Results.Single().Status, Is.EqualTo(OperationStatus.NotExecuted));
            Assert.That(report.Tasks.Single().Status, Is.EqualTo(OperationStatus.NotExecuted));
        }

        [Test]
        public async Task Confirmed_execute_runs_tasks_in_order_and_publishes_task_progress()
        {
            var executor = new RecordingExecutor("Fake");
            var config = CreateConfig(DeploymentMode.Execute,
                Task("g", "组", "t1", "任务一", new TestOperation("one", "one"), new TestOperation("two", "two")),
                Task("g", "组", "t2", "任务二", new TestOperation("three", "three")));
            var progress = new RecordingProgress();

            var report = await CreateRunner(executor).RunAsync(config, true, progress, CancellationToken.None);

            Assert.That(executor.ExecutedIds, Is.EqualTo(new[] { "one", "two", "three" }));
            Assert.That(report.Tasks.Count, Is.EqualTo(2));
            Assert.That(report.CompletedTaskCount, Is.EqualTo(2));
            Assert.That(report.SucceededTaskCount, Is.EqualTo(2));
            Assert.That(report.Tasks[0].Results.Count, Is.EqualTo(2));
            Assert.That(progress.Events.Count(e => e.Kind == DeploymentEventKind.TaskStarted), Is.EqualTo(2));
            Assert.That(progress.Events.Count(e => e.Kind == DeploymentEventKind.TaskCompleted), Is.EqualTo(2));
            Assert.That(progress.Events.Last(e => e.Kind == DeploymentEventKind.TaskCompleted).CompletedTasks, Is.EqualTo(2));
        }

        [Test]
        public async Task Step_failure_ends_current_task_and_starts_next_task()
        {
            var executor = new RecordingExecutor("Fake") { FailOperationId = "first" };
            var config = CreateConfig(DeploymentMode.Execute,
                Task("g1", "组一", "t1", "任务一", new TestOperation("first", "first"), new TestOperation("second", "second")),
                Task("g2", "组二", "t2", "任务二", new TestOperation("third", "third")));

            var report = await CreateRunner(executor).RunAsync(config, true, null, CancellationToken.None);

            Assert.That(executor.ExecutedIds, Is.EqualTo(new[] { "first", "third" }));
            Assert.That(report.Results.Single(r => r.OperationId == "second").Status, Is.EqualTo(OperationStatus.NotExecuted));
            Assert.That(report.Tasks[0].Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(report.Tasks[1].Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(report.CompletedTaskCount, Is.EqualTo(2));
            Assert.That(report.FailedTaskCount, Is.EqualTo(1));
            Assert.That(report.Results.Single(r => r.OperationId == "first").Status, Is.EqualTo(OperationStatus.Failed));
        }

        [Test]
        public async Task Global_task_progress_does_not_advance_until_task_ends()
        {
            var executor = new RecordingExecutor("Fake");
            var config = CreateConfig(DeploymentMode.Execute,
                Task("g", "组", "t1", "任务", new TestOperation("one", "one"), new TestOperation("two", "two")));
            var progress = new RecordingProgress();

            await CreateRunner(executor).RunAsync(config, true, progress, CancellationToken.None);

            var firstStep = progress.Events.Single(e => e.Kind == DeploymentEventKind.OperationCompleted && e.OperationId == "one");
            var taskDone = progress.Events.Single(e => e.Kind == DeploymentEventKind.TaskCompleted);
            Assert.That(firstStep.CompletedTasks, Is.EqualTo(0));
            Assert.That(firstStep.CompletedTaskSteps, Is.EqualTo(1));
            Assert.That(firstStep.TaskStepTotal, Is.EqualTo(2));
            Assert.That(taskDone.CompletedTasks, Is.EqualTo(1));
            Assert.That(taskDone.CompletedTaskSteps, Is.EqualTo(2));
        }

        [Test]
        public async Task Cancelled_token_marks_everything_not_executed()
        {
            var executor = new RecordingExecutor("Fake");
            var config = CreateConfig(DeploymentMode.Execute,
                Task("g", "组", "t1", "任务一", new TestOperation("one", "one")),
                Task("g", "组", "t2", "任务二", new TestOperation("two", "two")));
            var token = new CancellationToken(true);

            var report = await CreateRunner(executor).RunAsync(config, true, null, token);

            Assert.That(executor.CallCount, Is.EqualTo(0));
            Assert.That(report.Cancelled, Is.True);
            Assert.That(report.Results.All(r => r.Status == OperationStatus.NotExecuted), Is.True);
            Assert.That(report.CompletedTaskCount, Is.EqualTo(0));
            Assert.That(report.NotExecutedTaskCount, Is.EqualTo(2));
        }

        [Test]
        public async Task Disabled_task_is_skipped_and_excluded_from_task_progress()
        {
            var executor = new RecordingExecutor("Fake");
            var disabled = Task("g", "组", "t2", "禁用任务", new TestOperation("skip", "skip"));
            disabled.Enabled = false;
            var config = CreateConfig(DeploymentMode.Execute,
                Task("g", "组", "t1", "任务一", new TestOperation("one", "one")),
                disabled,
                Task("g", "组", "t3", "任务三", new TestOperation("three", "three")));
            var progress = new RecordingProgress();

            var report = await CreateRunner(executor).RunAsync(config, true, progress, CancellationToken.None);

            Assert.That(executor.ExecutedIds, Is.EqualTo(new[] { "one", "three" }));
            var disabledResult = report.Tasks.Single(t => t.TaskKey == "t2");
            Assert.That(disabledResult.Status, Is.EqualTo(OperationStatus.Disabled));
            Assert.That(disabledResult.Results, Is.Empty);
            Assert.That(report.DisabledTaskCount, Is.EqualTo(1));
            Assert.That(report.EnabledTaskCount, Is.EqualTo(2));
            Assert.That(report.CompletedTaskCount, Is.EqualTo(2));
            Assert.That(progress.Events.Any(e => e.Kind == DeploymentEventKind.TaskStarted && e.TaskKey == "t2"), Is.False);
            var lastCompleted = progress.Events.Last(e => e.Kind == DeploymentEventKind.TaskCompleted);
            Assert.That(lastCompleted.CompletedTasks, Is.EqualTo(2));
            Assert.That(lastCompleted.TaskTotal, Is.EqualTo(2));
        }

        [Test]
        public async Task Warning_step_keeps_task_running_and_counts_as_ended()
        {
            var executor = new RecordingExecutor("Fake") { WarnOperationId = "warn" };
            var config = CreateConfig(DeploymentMode.Execute,
                Task("g", "组", "t1", "任务一", new TestOperation("warn", "warn"), new TestOperation("after", "after")),
                Task("g", "组", "t2", "任务二", new TestOperation("next", "next")));

            var report = await CreateRunner(executor).RunAsync(config, true, null, CancellationToken.None);

            Assert.That(executor.ExecutedIds, Is.EqualTo(new[] { "warn", "after", "next" }));
            Assert.That(report.Tasks[0].Status, Is.EqualTo(OperationStatus.Warning));
            Assert.That(report.Tasks[1].Status, Is.EqualTo(OperationStatus.Succeeded));
            Assert.That(report.WarningTaskCount, Is.EqualTo(1));
            Assert.That(report.FailedTaskCount, Is.EqualTo(0));
            Assert.That(report.CompletedTaskCount, Is.EqualTo(2));
        }

        private static DeploymentRunner CreateRunner(RecordingExecutor executor)
        {
            var registry = new OperationRegistry();
            registry.Register(executor);
            var context = new OperationContext
            {
                PackageRoot = Path.GetTempPath(),
                DataRoot = Path.GetTempPath(),
                LogDirectory = "logs",
                Paths = new PathPolicy(),
                ProcessRunner = new FakeProcessRunner(),
                Log = null
            };
            return new DeploymentRunner(registry, context, null);
        }

        private static DeploymentConfig CreateConfig(DeploymentMode mode, params DeploymentTask[] tasks)
        {
            var config = new DeploymentConfig { Mode = mode };
            foreach (var task in tasks)
            {
                var group = config.Groups.FirstOrDefault(g => string.Equals(g.Key, task.GroupKey, StringComparison.Ordinal));
                if (group == null)
                {
                    group = new DeploymentGroup { Key = task.GroupKey, Name = task.GroupName };
                    config.Groups.Add(group);
                }

                foreach (var step in task.Operations)
                {
                    step.Order = config.Operations.Count;
                    step.GroupKey = group.Key;
                    step.GroupName = group.Name;
                    step.TaskKey = task.Key;
                    step.TaskName = task.Name;
                    config.Operations.Add(step);
                }

                group.Tasks.Add(task);
                config.Tasks.Add(task);
            }
            return config;
        }

        private static DeploymentTask Task(string groupKey, string groupName, string key, string name,
            params DeploymentOperation[] steps)
        {
            var task = new DeploymentTask
            {
                Key = key,
                Name = name,
                GroupKey = groupKey,
                GroupName = groupName
            };
            task.Operations.AddRange(steps);
            return task;
        }
    }
}
