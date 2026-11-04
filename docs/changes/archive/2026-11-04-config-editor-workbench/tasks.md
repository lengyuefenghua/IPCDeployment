# Tasks — 配置编辑器工作台化

> 分级：patch（单子系统：配置编辑器；不改 XML 格式、加载器契约）。
> 设计基线（已确认）：
> - 「打开…」换成 `config/*.xml` 配置下拉（排除 `*.sample.xml`），切换前走未保存确认；保存/另存为仍限 `config` 直属目录。保存后主窗口 MUST 切到刚保存的那个配置。
> - 路径类字段（Install 安装包、Copy 源路径、Command 命令文件、Command 工作目录）改为「可编辑下拉」：候选项来自 `data/<前缀>` 下的实际文件/目录，文本框仍可自由输入。不做模态文件浏览器。
> - 加步骤收进树：任务行内 `+` 与右键「新增步骤」子菜单列出九类；删除左下角「类型下拉 + + 步骤」。**保留**「+ 任务」。
> - 新增分组/任务/步骤后，选中该节点、聚焦名称框并全选。
> - 属性区：分区可折叠（默认展开）；字段行距 10→6、标签列 112→96 提高密度；出错字段行首红色竖条 + 既有红字。
> - 保存前先做全文档轻校验（按字段可见条件过滤），未通过则不写盘；问题面板条目点击 = 选中节点 + 滚动到出错字段。
> - 不做：撤销/重做、复制克隆、拖拽排序、中文枚举标签、编辑器内预检、从磁盘任意位置导入文件、两列字段布局。
>   （两列取消的原因：MinWidth 1024 时属性区仅约 630px，固定宽度的两列项会被横向裁切，而本变更无法用截图/自动化验证布局，风险高于收益。）
> 验证命令：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`、`dotnet test IPCDeployment.sln -c Debug`。
> 控件一律 WPF-UI 或项目控件，布局容器仅 Grid/StackPanel；不截图、不做自动化界面操作，界面由用户目视验收。

## 界面草图

```
┌ 标题栏  配置编辑器 · 未保存 ─────────────────────────────────────────┐
├ 工具栏 ui:Card ──────────────────────────────────────────────────────┤
│ [配置 config.xml ▾] [+新建] [保存] [另存为…]   config.xml · 已保存   │
│                                                    [execute] [⚠ 3]    │
├ 左栏 320 ──────────────────────┬ 属性面板 ──────────────────────────┤
│ 🔍 搜索名称/键/类型…（Esc 清除）│ 配置 › 分组 › 任务 › 步骤 3         │
│ ▾ 配置                         │ 安装 VC++ 运行库      [Install] ●  │
│  ▾ 分组 · 软件安装      2/5    │ ▾ 基本信息 · 3 项   （可折叠）      │
│    ▾ 任务 · MySQL       3  [+] │   步骤名称 * [________________]     │
│      1 Install 安装 MySQL  ●   │ ▾ 安装来源 · 2 项                   │
│      2 Copy 复制程序           │   安装包 * [software/setup.exe ▾]   │
│ [+ 分组] [+ 任务] [↑] [↓][删除]│ ▾ 执行 · 2 项                       │
│                                │   超时（分钟）[15]                  │
├────────────────────────────────┴────────────────────────────────────┤
│ 7 分组 · 12 任务 · 34 步骤   已保存 14:32   Ctrl+S 保存 · Del 删除  │
└─────────────────────────────────────────────────────────────────────┘
  ▸ 问题面板（底部可折叠）：⛔ 步骤 3「安装包」… → 点击定位并滚到字段
```

## 1. 路径候选（Core）

- [x] 1.1 新增 `src/IPCDeployment/Core/SourcePathCatalog.cs`：`IList<string> List(string dataRoot, string prefix, IList<string> extensions, bool directoriesOnly)` 返回 `data/<prefix>` 下的候选相对路径（正斜杠；目录项以 `/` 结尾；目录不存在或读失败返回空列表，不抛异常）（无日志点：纯查询，失败由字段校验提示）。
- [x] 1.2 新增 `tests/IPCDeployment.Tests/SourcePathCatalogTests.cs`：临时目录造 `software/a.exe`、`software/sub/b.msi`、`software/note.txt`、目录 `software/sub`，断言扩展名过滤、目录项以 `/` 结尾、`directoriesOnly` 只返回目录、前缀目录缺失返回空、每个返回值都能通过 `PathPolicy.ResolveSource`；运行 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~SourcePathCatalogTests"` 确认通过。
- [x] 1.3 修改 `src/IPCDeployment/Core/ConfigFieldSchema.cs`：`ConfigField` 增加 `HasPicker`、`PickDirectoriesOnly` 与 `WithPicker(bool directoriesOnly)`；给 Install `file`、Copy `source`、Command `file`、Command `workingDirectory` 打标记（无测试：声明式元数据，由 1.2 覆盖枚举规则）。

## 2. config 目录规则共享 + 编辑器数据

- [x] 2.1 新增 `src/IPCDeployment/Core/ConfigFileRules.cs`：`IsSampleConfig(string fileName)`（`.sample.xml` 结尾，忽略大小写）与 `IsDirectChild(string configDirectory, string fullPath)`（纯函数，无日志点）。
- [x] 2.2 修改 `src/IPCDeployment/ViewModels/DeploymentViewModel.cs`：私有 `IsSampleConfig`/`IsInConfigDirectory` 改为调用 `ConfigFileRules`，行为不变（无测试：等价替换，全量 `dotnet test` 回归）。
- [x] 2.3 修改 `src/IPCDeployment/ViewModels/EditorViewModel.cs`：构造函数改为 `EditorViewModel(string dataRoot, string configDirectory)`；新增 `ConfigFiles`（`config` 直属 `*.xml`、排除示例、按名称排序）、`SelectedConfigFile`（**唯一切换入口**：setter 把旧值存进 `PreviousConfigFile` 后只触发 `ConfigSwitchRequested(string name)` 事件，不自行打开；`switchingConfig` 守卫挡住程序回写）、公开只读 `PreviousConfigFile`（回滚用）、`RefreshConfigFiles()`、`RevertSelectedConfig(string name)`（直写 backing field + `OnPropertyChanged`，不重入 setter）、`SyncSelectedConfig(string path)`；`Open` 成功后调用 `SyncSelectedConfig`，`Save` 成功后 `RefreshConfigFiles()` 并 `SyncSelectedConfig`（无日志点：打开与保存已有 WARN/INFO 记录）。
- [x] 2.4 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs`：构造时传入 `configDirectory`；把私有 `IsInConfigDirectory` 删除，改用 `ConfigFileRules.IsDirectChild`（无测试：等价替换）。

## 3. 字段输入助手

- [x] 3.1 修改 `src/IPCDeployment/ViewModels/EditorNodeViewModel.cs`：`EditorFieldViewModel` 增加 `ShowPicker`、`PickerOptions`（非 picker 字段为空列表）（无日志点：界面状态）。
- [x] 3.2 修改 `src/IPCDeployment/ViewModels/EditorViewModel.cs` 的 `RefreshFields`：picker 字段用 `SourcePathCatalog.List(dataRoot, field.PathPrefix, field.Extensions, field.PickDirectoriesOnly)` 生成候选注入字段视图模型（无日志点：界面状态）。
- [x] 3.3 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml` 的 `FieldTemplate`：picker 字段渲染原生 `<ComboBox IsEditable="True" IsTextSearchEnabled="False" StaysOpenOnEdit="True">`（WPF-UI 4.3.0 无 `ui:ComboBox`，仓库既有写法就是被主题化的原生 `ComboBox`），`Text` 双向绑 `Value`、`ItemsSource` 绑 `PickerOptions`、`LostFocus="FieldInput_LostFocus"`；字段行 Grid 增加 8px 首列放 `ui:TextBlock Text="▌"`（`Foreground=#B91C1C`，`Visibility` 绑 `HasError`），不用 stock Border（无测试：纯界面，用户目视验收）。
- [x] 3.4 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`，确认编译通过。

## 4. 工具栏、配置下拉与主窗口同步

- [x] 4.1 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml` 工具栏：最左原生 `<ComboBox>`（宽 220）绑定 `ConfigFiles`/`SelectedConfigFile`，挂 `DropDownOpened="ConfigCombo_DropDownOpened"`；删除「打开…」按钮；保留「新建/保存/另存为…」（无测试：纯界面）。
- [x] 4.2 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs`：删除 `OpenButton_Click`、`OpenAsync` 与 `Ctrl+O` 分支；新增 `ConfigCombo_DropDownOpened` 调用 `viewModel.RefreshConfigFiles()`；构造时订阅 `viewModel.ConfigSwitchRequested`，在 `SwitchConfigAsync(name)` 里 `ConfirmDiscardAsync()` → `viewModel.Open(path)`；打开失败先 `ShowIssuesAsync("无法打开配置文件", …)`，取消或失败都调用 `viewModel.RevertSelectedConfig(viewModel.PreviousConfigFile)` 回滚（日志点：WARN「配置编辑器拒绝 config 目录外的路径」保留在保存路径）。
- [x] 4.3 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs` 与 `src/IPCDeployment/MainWindow.xaml.cs`：保存成功回调改为 `Action<string>`（传入刚保存的完整路径）；主窗口 `ReloadAfterEditorSave(string savedPath)` 先 `RefreshConfigFiles()`，再 `SelectConfigAsync(savedPath)`，失败时回退 `ReloadAsync()`（无测试：交互逻辑，用户目视验收）。
- [x] 4.4 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs`：`SaveAsAsync` 成功后调用 `viewModel.RefreshConfigFiles()` 与 `viewModel.SyncSelectedConfig(dialog.FileName)`（无测试：界面状态）。

## 5. 布局、加步骤与聚焦

- [x] 5.1 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs`：新增统一通道 `FocusField(EditorFieldViewModel field, bool selectAll)`——先把该字段所在分区 `IsExpanded` 置 `true`，再 `Dispatcher.BeginInvoke(DispatcherPriority.Loaded)` 在 `FieldScroll` 可见树中按 `DataContext` 找到字段容器，`BringIntoView()` 并聚焦其首个 `TextBoxBase`（`selectAll` 为真时 `SelectAll()`），找不到则只滚动到顶部（无测试：纯界面）。
- [x] 5.2 修改 `src/IPCDeployment/ViewModels/EditorNodeViewModel.cs`：`EditorFieldSectionViewModel` 增加 `IsExpanded`（默认 true）、`ToggleGlyph`、`HeaderText`；`EditorNodeViewModel` 增加 `IsTask`（无日志点：界面状态）。
- [x] 5.3 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml`：分区标题改为可点击折叠头（`ui:Button` Appearance=Transparent，内容「▾ 基本信息 · 3 项」），内容区 `Visibility` 绑 `IsExpanded`；字段标签列 112→96、字段行 `Margin` 10→6（无测试：纯界面）。
- [x] 5.4 修改 `src/IPCDeployment/ViewModels/EditorViewModel.cs`：`AddStep()` 改为 `AddStep(string stepType)`；删除 `StepTypes`、`NewStepType`（`ConfigFieldSchema.StepTypes` 保留，`ConfigurationDocument.AddStep` 与测试仍用它）；新增 `PendingFocusField` + `FieldFocusRequested` 事件，`AddGroup`/`AddTask`/`AddStep` 成功后设为目标节点的 `name` 字段（无日志点：界面状态）。
- [x] 5.5 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml` 与 `.xaml.cs`：`NodeTemplate` 在 `IsTask` 行加 `+` 按钮，与右键「新增步骤」子菜单共用同一套九类步骤菜单（中文名 + 英文类型）→ `AddStep(type)`；删除左下角步骤类型下拉与「+ 步骤」按钮；底部按钮保留「+ 分组 / + 任务 / ↑ / ↓ / 删除」；快捷键文案改为「Ctrl+S 保存 · Ctrl+N 新建 · Alt+↑↓ 移动 · Del 删除 · Ctrl+F 搜索」（无测试：纯界面）。
- [x] 5.6 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs`：订阅 `FieldFocusRequested` → `FocusField(field, selectAll: true)`，覆盖新增节点后自动命名（无日志点：界面状态）。
- [x] 5.7 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`，确认编译通过。

## 6. 反馈闭环

- [x] 6.1 修改 `src/IPCDeployment/ViewModels/EditorNodeViewModel.cs`：`EditorIssueViewModel` 增加可选 `FieldName`；`EditorFieldViewModel` 增加 `IsHighlighted`（无日志点：界面状态）。
- [x] 6.2 修改 `src/IPCDeployment/ViewModels/EditorViewModel.cs`：新增 `ValidateDocument()`——遍历全部节点，用 `ConfigurationDocument.FieldsOf/GetValue` 构造该节点的值字典，只校验 `field.IsVisibleFor(values)` 为真的字段（保留 `ValidateVisibleFields` 对当前节点字段 VM 的写入，其它节点只进问题面板），产出带节点与字段名的 `EditorIssueViewModel`（无日志点：保存失败沿用 WARN「配置编辑器保存中止：校验未通过」）。
- [x] 6.3 修改 `Save` 顺序与问题填充：新增 `FillEditorIssues(IEnumerable<EditorIssueViewModel>)` 供 6.2 使用；原 `FillIssues(IEnumerable<ConfigIssue>)` 改名 `FillLoaderIssues` 并保留按行号 `FindNodeByLine` 映射（字段名留空）；`Save` 先 `ValidateDocument()`，有错则 `FillEditorIssues`、打开面板、选中首条、`StatusText` 提示并直接返回 `ValidationFailed`，不写盘；无错时清空问题面板再 `document.Save`，loader 报错走 `FillLoaderIssues`（无测试：由 6.6 与用户目视验收）。
- [x] 6.4 修改 `SelectedIssue` 定位：选中目标节点后按 `FieldName` 匹配字段并置 `IsHighlighted`；无字段名时依次用消息包含字段 `Name`、`Label` 或元素名匹配，仍无匹配则取该节点首个校验失败的字段（无测试：界面状态）。
- [x] 6.5 修改 `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs`：`SelectedIssue` 定位后调用 5.1 的 `FocusField(field, selectAll: false)`（无测试：纯界面）。
- [x] 6.6 运行 `dotnet test IPCDeployment.sln -c Debug`，确认全量通过。

## 7. 规范同步与验证

- [x] 7.1 修改 `docs/specs/configuration-editor/spec.md`：「只打开合法配置」补充「编辑器通过配置下拉切换 `config` 直属非示例 XML」；新增「字段输入助手」Requirement（路径字段提供 `data/` 下实际路径候选，选中值必须满足 `PathPolicy` 前缀与扩展名规则）；「保存前校验」补充「保存前先做全文档字段级轻校验（按可见条件过滤），未通过不写盘」；「进入编辑器并保存生效」补充「保存后主界面切到刚保存的配置」（无测试：规范同步；`docs/specs/deployment-config.md` 已覆盖目录限制，不改）。
- [x] 7.2 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1` 与 `dotnet test IPCDeployment.sln -c Debug`，确认构建成功、测试全绿，并检查 `build/debug` 载荷完整（不启动程序）。
- [x] 7.3 [人工] 停下等待用户启动 `build/debug/IPCDeployment.exe` 目视验收界面（不截图、不做自动化操作），收集反馈。

## 8. Review 与收尾

- [x] 8.1 加载 `review-change` 做 patch 轻量审查，修复 Critical/Important。
- [x] 8.2 自审：逐条对照本文件「设计基线」与「界面草图」，确认完整实现、无遗漏；有缺口先修复再重审。
- [x] 8.3 [人工] 人工验收通过且用户明确许可后提交：`git add -- src tests docs`，`git commit -m "feat(config-editor-workbench): 配置编辑器工作台化"`；只 commit 不 push。
