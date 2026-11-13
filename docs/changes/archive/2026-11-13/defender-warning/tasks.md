# Tasks

> 设计基线：`Command` 增加 `warnExitCodes`，命中时步骤为“警告”、不结束任务；Defender 任务在篡改保护开启时以 201 退出并显示操作指引；完成页红色 InfoBar 显示第一条警告详情，后续任务继续执行。

## 1. Tests（命中 tdd-cycle 漏测后果闸）

- [x] 1.1 `CommandOperationTests`：`WarnExitCodes` 命中返回 `Warning`（后果：静默错误结果——把警告当成功或失败）。RED 至编译失败，实现后 GREEN。
- [x] 1.2 `ConfigLoaderTests`：解析 `warnExitCodes`、拒绝与 `successExitCodes` 重叠。
- [x] 1.3 `DeploymentRunnerTests`：警告步骤不结束任务、后续步骤与下一任务继续、`WarningTaskCount=1`、计入已结束任务。
- [x] 1.4 `TestDoubles` 增加 `WarnOperationId` 与 `OperationResult.Warning` 返回值。

## 2. Implementation

- [x] 2.1 `Core/DeploymentConfig.cs`：`OperationStatus.Warning`、`OperationResult.Warning`、`CommandOperation.WarnExitCodes`、报告 `WarningTaskCount`/`WarningCount`，`CompletedTaskCount` 计入警告。（日志点：无，数据模型）
- [x] 2.2 `Core/ConfigLoader.cs`：Command 接受 `warnExitCodes`，复用退出码解析；与 `successExitCodes` 重叠报错。
- [x] 2.3 `Core/Executors/CommandOperationExecutor.cs`：非成功且命中警告码 → `OperationResult.Warning`，其余保持失败。
- [x] 2.4 `Core/DeploymentRunner.cs`：警告不结束任务；任务有失败优先失败，仅有警告则警告；任务日志用 WARN；队列结束日志追加警告数；`StatusText` 增加“警告”。
- [x] 2.5 `Core/ConfigFieldSchema.cs`：Command 增加 `warnExitCodes` 字段（编辑器可编辑）。
- [x] 2.6 界面：`OperationViewModel`/`TaskViewModel`/`GroupViewModel` 增加“警告”文案、`!` 图标与琥珀色；`DeploymentViewModel` 增加 `WarningMessage`/`HasWarningMessage`、完成页汇总与导航状态；`MainWindow.xaml` 增加红色 `InfoBar` 与“警告任务”统计卡片（布局 `Auto/Auto/Auto/*`，6 列统计）。
- [x] 2.7 四份配置的 `disable-defender-apply` 加 `warnExitCodes="201"`；篡改保护开启时输出处理步骤并 `exit 201`，其他失败仍抛错。
- [x] 2.8 文档：`docs/specs/deployment-config.md`、`deployment-operation-framework/spec.md`、`XML配置说明.md`、`使用说明.md`、`AGENTS.md`。

## 3. Final verification

- [x] 3.1 `dotnet build IPCDeployment.sln -c Debug`：0 警告 0 错误。
- [x] 3.2 聚焦测试（Command/ConfigLoader/DeploymentRunner）：43 通过。
- [x] 3.3 `dotnet test IPCDeployment.sln -c Debug`：91 通过。
- [x] 3.4 四份配置内嵌 PowerShell 语法：48 段通过。
- [x] 3.5 `scripts/build-debug.ps1`：通过。

## 4. 实现后审查与自审

- [x] 4.1 实现后审查：patch 按 `review-change` 轻量清单，`reviewed = true`。
- [x] 4.2 自审：对照要求（警告不结束任务、红色提示含操作步骤、篡改保护场景、其他失败仍为失败、文档与字段同步）确认无遗漏。
- [x] 4.3 修复启动崩溃：完成页 `InfoBar` 的 `Severity="Danger"` 改为 `Error`（WPF-UI 4.3.0 的 `InfoBarSeverity` 只有 Informational/Success/Warning/Error）；用反射确认枚举值并重新 `build-debug.ps1`。
- [x] 4.4 按用户要求移除完成页红色横幅：删除 `InfoBar` 与 `WarningMessage/HasWarningMessage`；新增 `LogLineBrushConverter`，`[WARN]` 行橙色、`[ERROR]` 行红色，警告处理说明只出现在日志与任务详情中。验证：`dotnet build`、`dotnet test`（91 通过）、`build-debug.ps1`。

## 5. 人工验收与提交

- [x] 5.1 [人工] 在开启篡改保护的机器执行配置：`disable-defender` 应显示“警告”，完成页出现红色提示条并说明关闭篡改保护/重跑；后续任务继续执行。关闭篡改保护后重跑应显示成功。
- [x] 5.2 [人工] 验收通过并处理 git 后提交：`git add -- src/IPCDeployment tests/IPCDeployment.Tests config/config.xml config-lithium 使用说明.md XML配置说明.md AGENTS.md docs/specs docs/changes/defender-warning` 后 `git commit -m "feat(defender-warning): 新增警告状态与 Defender 处理提示"`（只 commit 不 push）。
