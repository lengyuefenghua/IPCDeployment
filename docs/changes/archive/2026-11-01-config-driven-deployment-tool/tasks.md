# Tasks

> 设计基线：`change.md` 的协作卡与流程契约；`specs/deployment-operation-framework/spec.md`。
> 测试 seam：`ConfigLoader.Load(path)`、`DeploymentRunner.RunAsync(config, confirmed, progress, cancellationToken)`、`DeploymentShell.StartSimulationAsync(progress, cancellationToken)`。

## 1. 配置根与 Group/Task/Step 模型

- [x] 1.1 在 `src/VisionIPC.Deployment.Tests/ConfigLoaderTests.cs` 增加失败测试：根目录配置可加载、分组/任务顺序保持、重复组名/空分组/空任务/旧 schema 被拒绝。后果：破坏系统运行（错误配置或乱序步骤会执行系统变更）。运行 `dotnet test VisionIPC.Deployment.sln -c Release --filter "FullyQualifiedName~ConfigLoaderTests"`，确认 RED。
- [x] 1.2 将 `data/config.xml`、`data/config.sample.xml` 移到仓库根目录，按 Group/Task/Step 与 `schemaVersion="2.0"` 重写示例；`config.sample.xml` 只保留示例，不提供专用加载入口。
- [x] 1.3 在 `src/VisionIPC.Deployment.Core/DeploymentConfig.cs` 增加分组、任务模型，保留扁平的步骤集合供执行器和预检使用；移除 `onError` 与 `Skipped` 语义。
- [x] 1.4 修改 `src/VisionIPC.Deployment.Core/ConfigLoader.cs` 解析 `<Groups>/<Group>/<Task>`，校验组名唯一/非空、空组/空任务、旧 schema、未知元素/属性，并保留行号列号。日志：加载失败 ERROR（路径/行号），成功 INFO。
- [x] 1.5 修改 `src/VisionIPC.Deployment.Core/DeploymentShell.cs` 与 `src/VisionIPC.Deployment.App/ViewModels/DeploymentViewModel.cs`：数据根固定为 `<packageRoot>/data`，默认加载根目录 `config.xml`，删除示例快捷入口。
- [x] 1.6 修改 `scripts/package-portable.ps1` 复制根目录 `config.xml`、`config.sample.xml`；运行 `powershell -ExecutionPolicy Bypass -File scripts/package-portable.ps1`，检查 ZIP 根目录含 XML、`data/` 只含载荷。
- [x] 1.7 运行聚焦测试 GREEN，再运行 `dotnet test VisionIPC.Deployment.sln -c Release`，修复模型改动导致的失败测试。

## 2. Runner 任务级执行、进度与失败

- [x] 2.1 在 `src/VisionIPC.Deployment.Tests/DeploymentRunnerTests.cs` 增加失败测试：任一步骤失败结束当前任务、剩余步骤不执行、下一个任务开始；取消后未开始任务不执行。后果：破坏系统运行（失败后继续执行或错误跳过后续任务）。运行 `dotnet test VisionIPC.Deployment.sln -c Release --filter "FullyQualifiedName~DeploymentRunnerTests"`，确认 RED。
- [x] 2.2 修改 `src/VisionIPC.Deployment.Core/DeploymentRunner.cs`：按分组/任务/步骤三层执行；全局进度按已结束任务数，任务子进度按已结束步骤数；步骤失败立即结束任务并继续下一个任务；移除全局 stop/skip 语义。日志：步骤失败 ERROR（任务/步骤/原因），任务结束 INFO，取消 WARN。
- [x] 2.3 修改 `src/VisionIPC.Deployment.Core/DeploymentConfig.cs` 的事件与报告结构：增加分组/任务/步骤上下文、任务与步骤两级计数；完成结果不设置“部署整体失败”。无测试：报告展示数据由完成页人工验收。
- [x] 2.4 更新 `src/VisionIPC.Deployment.Core/Simulation.cs` 与 `src/VisionIPC.Deployment.Tests/SimulationTests.cs`：模拟演练按任务/步骤发布事件，保持不创建进程、不写业务文件、不改注册表。后果：安全边界被突破（模拟产生真实副作用）。
- [x] 2.5 运行 `dotnet test VisionIPC.Deployment.sln -c Release --filter "FullyQualifiedName~DeploymentRunnerTests|FullyQualifiedName~SimulationTests"`，确认 GREEN；再运行全量测试。

## 3. UI 分组导航、任务页与完成页

- [x] 3.1 新增 `src/VisionIPC.Deployment.App/ViewModels/GroupViewModel.cs`、`TaskViewModel.cs`，替换/退役 `StageViewModel.cs`；无测试：界面状态由人工验收。
- [x] 3.2 修改 `src/VisionIPC.Deployment.App/ViewModels/DeploymentViewModel.cs`：分组导航、组内任务列表、选中任务步骤、自动跟随、两级进度、失败/未执行状态。无测试：界面交互由人工验收。
- [x] 3.3 修改 `src/VisionIPC.Deployment.App/MainWindow.xaml` 与 `MainWindow.xaml.cs`：左侧分组栏、右侧任务列表与步骤详情、全局任务进度、任务子进度、完成页详情；全部可见控件使用 WPF-UI。无测试：外观与交互由人工验收。
- [x] 3.4 修改确认框与完成页文案：说明任务级失败会继续下一个任务、无自动回滚、完成不代表成功；界面和报告不出现“跳过”。无测试：文案与展示由人工验收。
- [x] 3.5 [人工] 用户启动程序，目视验收分组导航、任务列表、步骤子进度、失败后下一任务继续、取消、完成页详情和底部日志；不使用截图或自动化界面操作。

## 4. 文档、规格与打包同步

- [x] 4.1 更新 `README.md` 与 `AGENTS.md`：根目录配置、`data/` 载荷、Group/Task/Step、两级进度、任务级失败、无跳过、无 Inno Setup。
- [x] 4.2 更新 `docs/specs/deployment-config.md` 到 `schemaVersion 2.0` 的新契约；无测试：文档与 delta spec 对照检查。
- [x] 4.3 运行 `powershell -ExecutionPolicy Bypass -File scripts/package-portable.ps1` 重新打包，检查 `dist/VisionIPC-Deployment` 与 ZIP 只含程序、根目录 XML、`data/` 和 README。无测试：产物用脚本与文件检查。

## 5. Review、自审与验收

- [x] 5.1 加载 `review-change`，执行 full 三轴审查（行为/契约/标准），修复 Critical/Important。
- [x] 5.2 运行最终验证：`dotnet test VisionIPC.Deployment.sln -c Release`、`dotnet build VisionIPC.Deployment.sln -c Release --no-restore`、`powershell -ExecutionPolicy Bypass -File scripts/package-portable.ps1`。
- [x] 5.3 自审：逐条对照需求卡验收标准、协作卡节点、流程契约（含异常线）与 `specs/deployment-operation-framework/spec.md`、本清单，确认无遗漏；有缺口先修复再重审。
- [x] 5.4 [人工] 在代表性测试机上验收真实安装、复制、注册表、命令、服务、网络、电源或自启操作；AI 不在终端运行这些操作。
- [x] 5.5 [人工] 当前工作区没有 Git 仓库，无法执行提交；未初始化 Git、未推送。

## 6. 追加示例、Delay 步骤与底部日志

- [x] 6.1 新增 `Delay` 步骤：模型、`ConfigLoader` 校验、`DelayOperationExecutor`、执行器注册、模拟执行器、UI 类型标签；新增加载器测试。
- [x] 6.2 扩展 `config.xml` 与 `config.sample.xml`：增加更多分组、组内任务和 `Delay` 步骤示例，保持 preview。
- [x] 6.3 将运行日志移到底部全宽面板：总览/完成显示全局日志，分组页显示当前任务日志；移除任务详情内的重复日志。
- [x] 6.4 同步 README、AGENTS、CONTEXT、主 spec 与 delta spec 的 Delay 步骤和日志布局说明。
- [x] 6.5 运行 `dotnet test`、Release 构建和打包；加载 `review-change` 复审新增范围，修复 Critical/Important。

## 7. 最终 UI 简化与移除模拟

- [x] 7.1 删除模拟演练：UI 按钮、视图模型、`DeploymentShell.StartSimulationAsync`、模拟执行器、`BlockedProcessRunner`、模拟报告字段和测试。
- [x] 7.2 左侧导航统一为“状态点 + 标题 + 进度文字”，移除圆环、大卡片和“大阶段”；任务总览/部署完成与分组行同构。
- [x] 7.3 任务总览页只保留配置选择、任务概况和开始部署；分组页/完成页去掉圆环，只保留文字进度和步骤状态点。
- [x] 7.4 将 `config.xml` 改为 `mode="execute"`，只保留 `SampleCommand` 与 `Delay` 只读任务；`config.sample.xml` 保持 preview。
- [x] 7.5 同步 README、AGENTS、CONTEXT、主 spec、delta spec 和需求卡，删除模拟演练表述，更新导航/进度说明。
- [x] 7.6 运行 `dotnet test`、Release 构建和打包；复审并修复 Critical/Important。

## 8. Command 支持 CMD/PowerShell 脚本与内嵌代码

- [x] 8.1 扩展 `CommandOperation`：`Language`（Exe/Cmd/PowerShell）、`Code`、`Strict`；加载器支持 `file` 或 `<Code>`，拒绝二者同时存在。
- [x] 8.2 执行器支持 `.exe/.cmd/.bat/.ps1` 和临时内嵌脚本；注入 `VISIONIPC_*` 环境变量；PowerShell 走 Bypass；内嵌 PowerShell 默认 Strict 包装，`strict="false"` 可关闭。
- [x] 8.3 预检检查 `cmd.exe`/`powershell.exe`，UI 类型只显示 `CMD`/`PowerShell`/`EXE`。
- [x] 8.4 新增 `data/commands/sample.cmd`、`sample.ps1`，在 `config.xml`/`config.sample.xml` 增加脚本和内嵌示例。
- [x] 8.5 更新 README、AGENTS、CONTEXT、主 spec、delta spec、需求卡；增加受控脚本 ADR。
- [x] 8.6 更新加载器/执行器测试，运行全量测试、构建、打包和复审。
