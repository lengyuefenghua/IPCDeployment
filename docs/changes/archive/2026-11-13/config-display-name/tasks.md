# Tasks

> 设计基线：配置方案下拉显示 `Meta name（文件名）`，选择仍以文件名为准；不加 XML 属性，读取失败静默回退文件名。复用 `DeploymentTask` 无改动，仅新增视图项与绑定方式。

## 1. Implementation

- [x] 1.1 新增 `src/IPCDeployment/ViewModels/ConfigFileItem.cs`：`FileName`、`DisplayName`、`Create(fullPath)` 用安全 XML 读取 `Meta@name`，空名或异常回退文件名，`ToString()` 返回显示名。（日志点：无，显示层静默回退，避免每次展开下拉刷日志）
- [x] 1.2 修改 `src/IPCDeployment/ViewModels/DeploymentViewModel.cs`：`ConfigFiles` 改为 `ConfigFileItem` 集合；刷新保留选中项改按 `FileName` 比较；`LoadAsync` 仍优先 `config.xml`。
- [x] 1.3 修改 `src/IPCDeployment/ViewModels/EditorViewModel.cs`：`ConfigFiles` 改为 `ConfigFileItem` 集合；`SelectedConfigFile`、`PreviousConfigFile`、`SyncSelectedConfig` 仍按文件名工作。
- [x] 1.4 修改 `src/IPCDeployment/MainWindow.xaml` 与 `ConfigurationEditorWindow.xaml`：ComboBox 增加 `DisplayMemberPath="DisplayName"`、`SelectedValuePath="FileName"`，绑定改为 `SelectedValue`。
- [x] 1.5 更新 `XML配置说明.md`（Meta name 用作显示名、回退规则、不影响选择）与 `使用说明.md`（快速开始示例）。

## 2. Final verification

- [x] 2.1 运行 `dotnet build IPCDeployment.sln -c Debug`：0 警告 0 错误。
- [x] 2.2 运行 `dotnet test IPCDeployment.sln -c Debug`：87 通过。
- [x] 2.3 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`，确认 `build/debug/config/config.xml` 存在。
- 无新增单元测试：仅显示文本与下拉绑定，未命中 `tdd-cycle` 漏测后果闸；选择与加载行为由现有测试覆盖。

## 3. 实现后审查与自审

- [x] 3.1 实现后审查：patch 按 `review-change` 轻量清单，`reviewed = true`。
- [x] 3.2 自审：逐条对照需求（两个下拉显示 Meta 名+文件名、回退、仍按文件名选择、config.xml 仍默认首位）确认无遗漏。

## 4. 修复：展开下拉时选中项被清空

- [x] 4.1 `ConfigFileItem` 增加 `Sync`：列表未变化时返回 false 且不触碰集合；变化时按文件名增量增删/移动，保留仍存在项的实例（日志点：无，界面状态修复）。
- [x] 4.2 `DeploymentViewModel`/`EditorViewModel` 的 `RefreshConfigFiles` 改用 `Sync`，不再 `Clear()`；仅在列表变化且选中文件仍存在时恢复选中。
- [x] 4.3 验证：`dotnet build`（0 警告）、`dotnet test`（87 通过）、`scripts/build-debug.ps1`；用反射脚本确认未变化同步返回 false、删除项后剩余实例保留。
- [x] 4.4 实现后审查重新通过（`reviewed = true`）。

## 5. 人工验收与提交

- [x] 5.1 [人工] 启动 Debug 程序，确认主窗口与编辑器下拉显示 `标准配置（config.xml）` 等名称、切换配置正常；点开下拉但不选择任何项直接关闭，选中项必须保持不变；在 `config/` 放一份损坏 XML，确认该项回退显示文件名且不影响其它配置切换。
- [x] 5.2 [人工] 人工验收通过且用户明确许可后提交：`git add -- src/IPCDeployment/ViewModels/ConfigFileItem.cs src/IPCDeployment/ViewModels/DeploymentViewModel.cs src/IPCDeployment/ViewModels/EditorViewModel.cs src/IPCDeployment/MainWindow.xaml src/IPCDeployment/ConfigurationEditorWindow.xaml XML配置说明.md 使用说明.md docs/changes/config-display-name` 后 `git commit -m "feat(config-display-name): 配置方案下拉显示 Meta 名称"`（只 commit 不 push）。
