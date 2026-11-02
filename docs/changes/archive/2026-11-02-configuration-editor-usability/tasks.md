# Tasks — 配置编辑器可用性优化

> 分级：patch（单子系统：配置编辑器；不改 XML 格式、加载器契约与主窗口接口）。
> 设计基线：本轮对话已确认的 UI 草图和五个痛点；下方「界面草图」为验收要点。
> 验证命令：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`、`dotnet test IPCDeployment.sln -c Debug`。
> 控件一律用 WPF-UI（`ui:FluentWindow` / `ui:Card` / `ui:Button` / `ui:TextBox` / `ui:ComboBox` / `ui:CheckBox` / `ui:ListView` / `ui:InfoBar` / `ui:Badge` / `ui:TextBlock`）或项目控件，布局容器仅 Grid/StackPanel；不截图、不做自动化界面操作。

## 界面草图

```
┌ ui:TitleBar「配置编辑器 · 未保存」                                        ─ □ ✕ ┐
├ 工具栏 ui:Card ────────────────────────────────────────────────────────────────┤
│ [新建][打开][保存][另存] │ config.xml · 已保存 14:30 │ [preview] │ [⚠ 3 处问题] │
├ 左栏 320 ───────────────────────┬ 属性面板 ────────────────────────────────────┤
│ 🔍 搜索名称/键/类型…（Esc 清除） │ 面包屑：配置 › 软件安装 › MySQL               │
│ ────────────────────────────────│ 标题：MySQL 安装 [Install] ● 未保存  id: step-3 │
│ ▾ 配置                          │
│  ▾ 分组 · 软件安装       2/5    │ ── 基本信息 ────────────────────────────────── │
│    ▾ 任务 · MySQL        3      │    名称     [____________________]             │
│      Install  1 安装 MySQL  ●   │ ── 安装来源 ────────────────────────────────── │
│      Command  2 初始化          │    安装包   [data/software/setup.exe] ⓘ        │
│      Delay    3 等待 5 秒       │    参数     [____________________]             │
│    · 任务 · 示例命令     2      │    SHA-256  [____________________]             │
│  ▸ 分组 · 文件复制       3/8    │ ── 执行 ─────────────────────────────────────── │
│                                 │    超时(分)[15]   退出码 [0]                   │
│ ────────────────────────────────│    已存在则跳过 [____________________]         │
│ [+ 分组] [+ 任务] [步骤类型 ▾][+ 步骤] │                                              │
│ （按钮随选中层级启用）           │ ⓘ 必填/格式在输入后即时提示，保存仍走完整校验 │
├ 状态栏 ui:Card ────────────────────────────────────────────────────────────────┤
│ 12 分组 · 43 任务 · 128 步骤 │ 3 处问题 ▸ │ Ctrl+S 保存  Alt+↑↓ 移动  Del 删除 │
├ 问题抽屉（默认收起，保存失败自动展开）─────────────────────────────────────────┤
│ ⚠ 第 42 行 · Install「安装包」不存在：data/software/setup.exe    [定位]        │
└────────────────────────────────────────────────────────────────────────────────┘
```

交互要点：
- 操作条在树下方，只启用当前层级可做的动作；右键菜单与快捷键等价。
- 字段按语义分区平铺（不折叠）、标签左值右；`id`/`key` 不在表单里，改为标题行右侧灰色等宽文字；Network 按 `kind`/`mode` 显示适用字段，不适用的已有值仍显示并标红提示清空。
- 字段失焦即做轻校验并在控件下报错；保存仍走 `ConfigLoader`，失败自动展开问题抽屉并定位首条。
- 搜索时显示扁平结果列表（带路径），选中后清空搜索并定位到树。
- `mode=execute` 用危险色徽章提示「将真实执行」。

## 1. Tests

本变更不写测试：改动集中在 UI 布局、交互与纯提示性校验，不阻断保存、不改变文件格式与保存契约；保存/往返保真已由既有 `tests/IPCDeployment.Tests/ConfigurationDocumentTests.cs` 覆盖。各实现任务注明「无测试」原因。

## 2. Implementation

- [x] 2.1 [改] `src/IPCDeployment/Core/ConfigFieldSchema.cs`：为 `ConfigField` 增加 `Section`、`AllowEmpty`、`MinValue`/`MaxValue`、`Validation`（None/Integer/Sha256/ExitCodes/SourcePath/DestinationPath/Ipv4/Ipv4List/RelativePath/RegistryValue）与 `VisibleWhen` 条件；把配置/分组/任务/九类步骤字段归入语义分区（基本信息、标识、安装来源、执行、注册表位置、注册表值、网卡、高级属性、IPv4、IPv4 静态、内嵌代码等）；Registry 的 `valueName`/`value`、Network 的 `value` 标 `AllowEmpty`；Network 的 IPv4 字段按 `kind=IPv4`、静态字段再按 `mode=Static` 添加可见条件；Command 的 file/Code 保持同时可见，互斥交给 2.2 提示。无测试：纯元数据与展示定义，保存契约不变。验证：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`
- [x] 2.2 [增] `src/IPCDeployment/Core/ConfigFieldValidator.cs`：`ValidateField(ConfigField, string value, IDictionary<string,string> values)` 返回中文错误或 `null`——必填非空（`AllowEmpty` 豁免）、整数范围、SHA-256 64 位十六进制、退出码逗号整数、源路径前缀与 `..`/盘符/非法字符（复用 `PathPolicy.IsSafeRelativePath` 与区域前缀）、目标路径绝对（复用 `PathPolicy.ResolveDestination`）、IPv4/逗号列表、日志目录相对；`ValidateCrossField` 覆盖 Command（file 与 Code 互斥、language 仅内嵌代码、arguments 仅文件模式）与 Network（`kind`/`mode` 不适用的字段仍有值）。无测试：仅提示、不阻断保存，真实数据风险仍由保存时的 `ConfigLoader` 承担。验证：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`
- [x] 2.3 [改] `src/IPCDeployment/ViewModels/EditorNodeViewModel.cs`：`EditorNodeViewModel` 增加 `IsDirty`、`ChildCountText`、`Sequence`、`TypeText`、`Glyph`；`EditorFieldViewModel` 增加 `ErrorMessage`/`HasError`、`IsVisible`、`IsIrrelevant`、`RefreshVisibility(values)`、`Validate(values)`；新增 `EditorFieldSectionViewModel`（标题、字段集合、`HasVisibleFields`）与 `EditorSearchResultViewModel`（节点、路径、类型文本）。无测试：视图模型状态承载，无独立业务规则。验证：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`
- [x] 2.4 [改] `src/IPCDeployment/ViewModels/EditorViewModel.cs`：`Fields` 改为 `Sections`；字段变更后重算可见性与脏元素集合、刷新 `BreadcrumbText`/`SelectedTitle`/`ModeBadgeText`/`IsExecuteMode`；失焦或保存时跑 2.2 校验；保存失败时展开问题抽屉并选中首条错误的节点；新增 `SearchText`/`SearchResults`/`IsSearching`、`IsIssuePanelOpen`、`HasSelection`、`SelectNode`、节点/步骤计数；保存成功后清空脏标记并收起问题抽屉。无测试：UI 编排层，核心保存链路由既有 `ConfigurationDocumentTests` 覆盖。验证：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`
- [x] 2.5 [改] `src/IPCDeployment/ConfigurationEditorWindow.xaml`：按「界面草图」重排为工具栏、左栏（搜索+树+树底操作）、右栏（面包屑+标题+分区字段表单）、状态栏、问题抽屉；字段模板为标签左值右、`ⓘ` 悬浮提示、错误行内红字；树节点显示类型/序号/计数/脏点；搜索态显示扁平结果 `ui:ListView`；窗口 1280×820、最小 1024×680；`mode=execute` 徽章用危险色。无测试：纯布局与绑定。验证：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`
- [x] 2.6 [改] `src/IPCDeployment/ConfigurationEditorWindow.xaml.cs`：接上搜索文本变化与结果定位（定位后清空搜索）、右键菜单（按节点类型启用新增/上移/下移/删除）、快捷键（Ctrl+S/N/O、Alt+↑↓、Delete 仅在焦点非输入框时、Esc 清搜索）、问题数按钮切换抽屉、删除确认文案按节点类型区分、保存失败后自动展开抽屉并定位首条。无日志点：沿用现有打开/保存日志，界面交互不新增运行日志。验证：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`
- [x] 2.7 [改] 按用户确认的修订：`src/IPCDeployment/ConfigurationEditorWindow.xaml` 去掉全部分区的 `Expander`，改为静态标题平铺；`key`/`id` 不再渲染为只读输入框，改为标题行右侧灰色等宽文字；`src/IPCDeployment/Core/ConfigFieldSchema.cs` 删除分组/任务/步骤的 `key`/`id` 表单字段与 `ReadOnly` 机制；`src/IPCDeployment/ViewModels/EditorNodeViewModel.cs` 新增 `IdentityText`、移除分区的 `IsExpanded` 与字段的 `IsReadOnly`；`src/IPCDeployment/ViewModels/EditorViewModel.cs` 去掉只读字段分支。无测试：纯展示与字段表裁剪，`key`/`id` 仍由文档模型自动生成，保存契约不变。验证：`powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`

## 3. Review

- [x] 3.1 加载 `review-change` 做轻量审查（patch），修复 Critical 与 Important
- [x] 3.2 最终验证：`dotnet test IPCDeployment.sln -c Debug`
- [x] 3.3 [人工] 用户启动程序目视验收：树上类型/序号/计数/脏点与搜索、树底操作层级、字段分区平铺与右上角灰色标识、失焦即时提示、保存失败抽屉定位、execute 徽章、快捷键；不截图、不自动化操作

## 4. Self-review (完成前)

- [x] 4.1 自审：逐条对照本变更确认的五个痛点与「界面草图」交互要点、`docs/specs/configuration-editor/spec.md`（保存校验、往返保真、失败保护不得回退）、`tasks.md`，确认完整实现、无遗漏；有缺口先修复再重审

## 5. Commit (人工验收通过且用户明确许可后才执行)

- [x] 5.1 [人工] 提交：`git add -- docs/changes/archive/2026-11-02-configuration-editor-usability src/IPCDeployment/ConfigurationEditorWindow.xaml src/IPCDeployment/ConfigurationEditorWindow.xaml.cs src/IPCDeployment/Core/ConfigFieldSchema.cs src/IPCDeployment/Core/ConfigFieldValidator.cs src/IPCDeployment/ViewModels/EditorViewModel.cs src/IPCDeployment/ViewModels/EditorNodeViewModel.cs && git commit -m "feat(configuration-editor-usability): 优化配置编辑器交互与表单"`（用户已明确许可 commit 与 push）
