# Tasks

> 设计基线（patch）：配置文件统一放到程序目录的 `config\`，只读取直属非示例 `*.xml`；主界面用下拉切换，不提供任意路径的文件对话框；编辑器打开/另存为同样限定在 `config\`。`ConfigLoader` 改为显式接收 `dataRoot`（固定 `<程序目录>\data`），避免配置移入子目录后源路径解析错误。界面验收由用户目视完成，不做截图或自动化界面操作。测试只适配既有签名与仓库配置用例，无新增行为测试：命中不了 `tdd-cycle` 漏测后果闸。

## 1. 目录迁移与打包

- [x] 1.1 先创建 `config` 目录，再 `git mv config.xml config/config.xml`、`git mv config.sample.xml config/config.sample.xml`，确认工作区根目录不再有 XML（无测试：文件迁移）。
- [x] 1.2 修改 `scripts/build-debug.ps1` 与 `scripts/package-portable.ps1`：校验并整体复制根目录 `config\` 到输出，删除原先复制根 `config.xml`/`config.sample.xml` 的两行（无测试：脚本变更，由 6.2–6.4 验证）。

## 2. 加载器数据根

- [x] 2.1 修改 `src/IPCDeployment/Core/ConfigLoader.cs`：`Load(string configPath, string dataRoot)`，删除由配置目录推导 `dataRoot` 的逻辑，改用传入值；缺失文案改为“未找到配置文件。”（无日志点：加载器只返回错误，由 Shell 记录）。
- [x] 2.2 修改 `src/IPCDeployment/Core/DeploymentShell.cs`：先计算 `dataRoot = Path.Combine(packageRoot, "data")` 再调用 `new ConfigLoader(paths).Load(fullPath, dataRoot)`；把第 152 行回退文案 `"data/config.xml"` 改为 `"config/config.xml"`（无日志点：日志文案仅修正路径）。
- [x] 2.3 修改 `src/IPCDeployment/Core/ConfigurationDocument.cs`：保存 `dataRoot`，`CreateNew(string dataRoot)`、`Open(string path, string dataRoot)`、`Save` 内部校验统一用它。修改 `src/IPCDeployment/ViewModels/EditorViewModel.cs`：构造函数改为 `EditorViewModel(string dataRoot)` 并保存字段，构造时 `CreateNew(dataRoot)`，`NewDocument`/`Open` 也传该 `dataRoot`（无日志点：编辑器已有 WARN/INFO 记录）。
- [x] 2.4 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs`：构造函数增加 `configDirectory` 与 `dataRoot`，用 `new EditorViewModel(dataRoot)` 创建视图模型；打开与另存为的初始目录为 `configDirectory`；打开或保存的目标不在 `configDirectory` 直属目录时拒绝并提示（日志点：WARN “配置编辑器拒绝 config 目录外的路径”，上下文为绝对路径）。

## 3. 多配置选择（界面）

- [x] 3.1 修改 `src/IPCDeployment/ViewModels/DeploymentViewModel.cs`：新增 `ConfigFiles`（`config\*.xml` 文件名，按名称排序，排除 `*.sample.xml`）、`SelectedConfigFile`、`RefreshConfigFiles()`；`LoadAsync` 刷新后默认加载 `config\config.xml`，缺失取首个非示例 XML，目录缺失、为空或只有示例时提示并禁止开始；`SelectConfigAsync` 的路径校验改为“必须是 `config\` 直属的非示例文件”，并对加载成功后的文件名同步 `SelectedConfigFile` 与相对路径文本；运行中或编辑器打开时切换直接回退选中项；更新“配置文件必须位于程序目录”“请选择与 IPCDeployment.exe 同目录的 XML 配置”等旧文案；删除 `LoadDefaultAsync`（无日志点：加载结果沿用既有 Shell 日志）。
- [x] 3.2 修改 `src/IPCDeployment/MainWindow.xaml`：配置文件卡片第一行改为 `ui:ComboBox`（配置方案）+“编辑配置”“校验配置”“开始部署”，删除“选择配置文件”按钮；第二行弱化显示相对路径 `config\<文件名>`，删除“重新加载默认配置”按钮；下拉展开刷新列表，运行中 `IsEnabled` 绑定 `CanReload`。文字 UI 草图：

  ```
  ┌ 配置文件 ────────────────────────────────────────────────────────────┐
  │ [ 配置方案：config.xml       ▾ ]  [编辑配置] [校验配置] [开始部署 →]   │
  │ config\config.xml                                   （弱化相对路径）    │
  └─────────────────────────────────────────────────────────────────────┘
  ```

- [x] 3.3 修改 `src/IPCDeployment/MainWindow.xaml.cs`：删除 `SelectConfigButton_Click`、`DefaultButton_Click`；新增下拉展开刷新与选中切换处理（切换失败时恢复原选中项）；`ConfigurationEditorWindow` 构造时传入 `Path.Combine(BaseDirectory, "config")` 与 `Path.Combine(BaseDirectory, "data")`；更新第 106 行“共享同一份 config.xml”注释（无日志点：无新增用户可见日志）。
- [x] 3.4 修改 `src/IPCDeployment/ViewModels/DeploymentViewModel.cs` 的 `BuildConfirmationText`，在确认文案中带上当前配置文件名（无测试：文案变更）。

## 4. 文档与示例文案

- [x] 4.1 更新 `AGENTS.md`：第 8/9 行改为 `config\config.xml` 与 `config\config.sample.xml`、启动读取 `config\` 下配置；第 23 行改为脚本复制 `config\` 目录。
- [x] 4.2 更新 `README.md` 与 `CONTEXT.md`：目录树、启动说明、多配置切换说明（示例文件不参与选择）。
- [x] 4.3 更新 `docs/specs/deployment-config.md`：配置位于 `config\`，只读取该目录直属 XML，`config.sample.xml` 仅作示例、不参与选择与回退，`data\` 固定为程序目录下。同步改写 `docs/specs/deployment-operation-framework/spec.md` 的「配置文件与数据目录」Requirement 与两条 Scenario：启动优先 `config\config.xml`，缺失时加载 `config\` 下首个非示例 XML，不得加载 `config.sample.xml`。
- [x] 4.4 按新目录与新默认模式整段改写 `data/files/sample-guide.txt`（`data/config.xml` → `config\config.xml`，默认配置描述改为 `config\config.xml` 为 execute、示例为 preview；无测试：示例文案）。
- [x] 4.5 更新 `docs/specs/configuration-editor/spec.md` 的「只打开合法配置」Requirement：打开与另存为的目标 MUST 位于程序目录 `config\` 直属目录，否则拒绝并提示（无测试：规范同步）。

## 5. 测试适配

- [x] 5.1 修改 `tests/IPCDeployment.Tests/ConfigLoaderTests.cs`：`Load` 统一传 `Path.Combine(root, "data")`；`Repository_configs_load_from_root` 改名并改为从仓库根逐级查找 `config\config.xml` 定位仓库，加载 `config\config.xml`、`config\config.sample.xml`，`dataRoot` 为仓库根 `data`（无新增测试：签名适配）。
- [x] 5.2 修改 `tests/IPCDeployment.Tests/ConfigurationDocumentTests.cs`：`CreateNew(Path.Combine(tempDirectory, "data"))`、`Open(path, Path.Combine(tempDirectory, "data"))`、`Load(path, Path.Combine(tempDirectory, "data"))`（与 `CreateDataFiles()` 的建目录位置一致；无新增测试：签名适配）。

## 6. 验证与收尾

- [x] 6.1 运行 `dotnet test IPCDeployment.sln -c Debug`，确认全量通过。
- [x] 6.2 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`，确认构建成功。
- [x] 6.3 检查 `build/debug/config/config.xml`、`build/debug/config/config.sample.xml` 存在，`build/debug` 根目录无 XML，`build/debug/data` 载荷完整；不启动程序。
- [x] 6.4 运行 `dotnet build IPCDeployment.sln -c Release --no-restore` 后执行 `powershell -ExecutionPolicy Bypass -File scripts/package-portable.ps1`，检查 `dist/IPCDeployment` 与 ZIP 内含 `config/config.xml`、`config/config.sample.xml` 且根部无 XML。
- [x] 6.5 加载 `review-change` 做 patch 轻量审查，修复 Critical/Important；加载 `finish-change` 完成收尾。
- [x] 6.6 自审：逐条对照已确认的设计（config 目录、无文件对话框、编辑器限定、dataRoot 显式传入、文档/脚本同步），确认无遗漏。

## 7. Commit（人工验收后）

- [x] 7.1 [人工] 用户目视验收通过并明确许可后提交：`git add -- config src/IPCDeployment tests scripts AGENTS.md README.md CONTEXT.md docs/specs docs/changes/multi-config-selection data/files/sample-guide.txt`，`git commit -m "feat(multi-config-selection): 支持 config 目录多配置切换"`；只 commit 不 push。
