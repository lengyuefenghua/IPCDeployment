# Tasks

> 设计基线：Task 可选 `enabled`（缺省 true）；`enabled="false"` 的任务显示「已禁用」、不执行、不计入进度、跳过运行期预检；编辑器可勾选启用。复用现有 `DeploymentTask`、`OperationStatus`、`ConfigField`、`ConfigFieldSchema`，不新增服务或接口。

## UI 草图

```text
分组页 · 任务列表             选中任务步骤
───────────────────────     ─────────────────────
 –  数据库初始化   3/3 步     – 卸载旧 MySQL   已禁用
    已禁用         已禁用        任务已禁用。
 ○  规则导入       0/4 步
    等待           等待

总览：共 3 个分组 · 12 个任务（1 个已禁用） · 44 个步骤
分组行：8 个任务 · 32 个步骤（含 1 个禁用任务）
确认框：执行 3 个分组、11 个任务（另 1 个任务已禁用，不执行）、44 个步骤。
结果页统计：总任务 / 成功 / 失败 / 未执行 / 禁用（5 张卡片）

编辑器 · 任务属性
  任务名称 *  [数据库初始化      ]
  启用任务    [x] 启用          ← 取消勾选写 enabled="false"
  树节点：任务 · 已禁用
```

## 1. Tests（命中 tdd-cycle 漏测后果闸）

- [x] 1.1 在 `tests/IPCDeployment.Tests/ConfigLoaderTests.cs` 加失败测试：Task 缺省 enabled 为 true、`enabled="false"` 解析为禁用、非法布尔值报错。后果：破坏系统运行（配置写错时禁用任务被误执行，可能重置数据库）。seam：`ConfigLoader.Load`。
- [x] 1.2 运行 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~ConfigLoaderTests"`，确认 RED 因未知属性/缺字段失败。
- [x] 1.3 在 `tests/IPCDeployment.Tests/DeploymentRunnerTests.cs` 加失败测试：三个任务中中间一个禁用，只执行其余两个，禁用任务不发 TaskStarted/TaskCompleted、没有步骤结果、报告 `DisabledTaskCount=1`/`EnabledTaskCount=2`、TaskCompleted 的 TaskTotal 为 2。后果：破坏系统运行（禁用任务仍执行）。seam：`DeploymentRunner.RunAsync`。
- [x] 1.4 在 `tests/IPCDeployment.Tests/ConfigurationDocumentTests.cs` 加失败测试：Task 的 enabled 缺省读出 "true"、取消勾选写 `enabled="false"`、重新勾选移除属性。后果：静默错误结果（编辑器显示启用但文件仍禁用或反之）。seam：`ConfigurationDocument.GetValue/SetValue`。
- [x] 1.5 在 `tests/IPCDeployment.Tests/PreflightCheckerTests.cs` 加失败测试：禁用任务内缺失网卡的 IPv4 步骤不再产生错误。后果：破坏系统运行（选装任务缺网卡挡住整份配置执行）。seam：`PreflightChecker.Check`。
- [x] 1.6 运行 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~DeploymentRunnerTests|FullyQualifiedName~ConfigurationDocumentTests|FullyQualifiedName~PreflightCheckerTests"`，确认 RED 因 Disabled/Enabled 相关成员不存在或行为未实现失败。

## 2. Implementation

- [x] 2.1 修改 `src/IPCDeployment/Core/DeploymentConfig.cs`：`DeploymentTask.Enabled`（默认 true）、`OperationStatus.Disabled`（注释标明仅任务级使用）、`DeploymentReport.EnabledTaskCount/DisabledTaskCount`、`ConfigSummary.DisabledTaskCount`。无测试：属性与计数为纯数据，由 1.1/1.3 覆盖。（日志点：无，数据模型）
- [x] 2.2 修改 `src/IPCDeployment/Core/ConfigLoader.cs`：Task 接受 `enabled`，bool.TryParse 失败报行列错误；全部任务禁用时加 warning。无测试：1.1 已覆盖。日志点：无，加载期问题经现有 ConfigIssue 输出。
- [x] 2.3 修改 `src/IPCDeployment/Core/PreflightChecker.cs`：跳过禁用任务的操作做运行期预检（哈希、服务、网卡、自启目标、cmd/powershell、SpeedDuplex/Power 顺序）；全部任务禁用时 `ConfigSummary` 日志沿用现有输出。无测试：1.5 已覆盖。日志点：无，沿用现有预检错误/警告。
- [x] 2.4 修改 `src/IPCDeployment/Core/DeploymentRunner.cs`：totalTasks/totalSteps 只计启用任务与步骤；按文档顺序为禁用任务写 `Status=Disabled` 结果（不发事件、不含步骤结果）；taskIndex 只对启用任务递增；取消补记按 `task.Enabled` 区分 Disabled/NotExecuted；队列结束日志追加禁用数。无测试：1.3 已覆盖。日志点：Info，禁用任务写「任务已禁用：组/任务」。
- [x] 2.5 修改 `src/IPCDeployment/ViewModels/OperationViewModel.cs` 与 `TaskViewModel.cs`：增加 Disabled 文案/图形/灰色、禁用任务初始与 Reset 后保持禁用、ProgressText 显示「已禁用」。无测试：界面外观与状态展示，人工验收。
- [x] 2.6 修改 `src/IPCDeployment/ViewModels/GroupViewModel.cs` 与 `DeploymentViewModel.cs`：分组进度分母只含启用任务；总览/任务数/步骤数/确认文案包含禁用数量；全局进度用 `EnabledTaskCount`；全部任务禁用时禁用开始按钮并给出提示；结果页新增禁用统计字段绑定。无测试：界面展示，人工验收。
- [x] 2.7 修改 `src/IPCDeployment/MainWindow.xaml`：结果页统计区 4 列改 5 列并新增「禁用任务」卡片。无测试：布局，人工验收。
- [x] 2.8 修改 `src/IPCDeployment/Core/ConfigFieldSchema.cs`：`TaskFields()` 增加 `enabled`（Boolean，标签「启用任务」）。无测试：字段定义由 1.4 间接覆盖。
- [x] 2.9 修改 `src/IPCDeployment/Core/ConfigurationDocument.cs`：Task 的 enabled 缺省读出 "true"，勾选移除属性、取消勾选写 `enabled="false"`。无测试：1.4 已覆盖。
- [x] 2.10 修改 `src/IPCDeployment/ViewModels/EditorViewModel.cs` 与 `EditorNodeViewModel.cs`：enabled 变化后刷新树节点名称；禁用任务类型显示「任务 · 已禁用」。无测试：界面展示，人工验收。
- [x] 2.11 更新文档：`docs/specs/deployment-config.md`（Task 属性/预检/事件计数）、`docs/specs/deployment-operation-framework/spec.md`（禁用任务不计进度、不发任务事件）、`XML配置说明.md`、`使用说明.md`、`AGENTS.md`。无测试：文档。
- [x] 2.12 运行 `dotnet test IPCDeployment.sln -c Debug` 确认全绿。

## 3. Final verification

- [x] 3.1 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1` 确认 Debug 构建通过。
- [x] 3.2 运行 `dotnet test IPCDeployment.sln -c Debug` 确认全部测试通过。

## 4. 实现后审查与自审

- [x] 4.1 实现后审查：patch 按 `review-change` 轻量清单，`reviewed = true` 才继续。
- [x] 4.2 自审：逐条对照已确认方案（属性、执行语义、UI 草图、编辑器、文档、测试）确认无遗漏。
- [x] 4.3 [人工] 停下等待用户启动程序做界面人工验收（本任务不截图、不自动化操作）。

## 5. Commit（人工验收通过且用户明确许可后）

- [x] 5.1 [人工] 提交：`git add -- docs/changes/task-disable-flag .opencode/lazy/state.json src/IPCDeployment tests/IPCDeployment.Tests docs/specs XML配置说明.md 使用说明.md AGENTS.md` 后 `git commit -m "feat(task-disable-flag): 增加任务启用开关"`（只 commit 不 push）。
