# Tasks

> 设计基线：见 `change.md` 的协作卡与流程契约。UI 一律用 WPF-UI 控件（`ui:FluentWindow` / `ui:Card` / `ui:TreeView` / `ui:TextBox` / `ui:NumberBox` / `ui:ComboBox` / `ui:CheckBox` / `ui:Button` / `ui:TextBlock` / `ui:InfoBar`），布局照 `## UI 草图`；不写截图、不做自动化界面操作。
> 验证命令统一用 Debug：`dotnet build IPCDeployment.sln -c Debug`、`dotnet test IPCDeployment.sln -c Debug`。

## 1. Tests（仅保存链路命中 `tdd-cycle` 漏测后果闸：数据损坏/丢失，可稳定复现）

- [x] 1.1 新建 `tests/IPCDeployment.Tests/ConfigurationDocumentTests.cs`，写「新建最小骨架保存后能被 `ConfigLoader` 接受」（后果：数据损坏；触发：新建文件保存后加载器拒绝）；运行 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~ConfigurationDocumentTests"` 确认 RED 因 `ConfigurationDocument` 不存在而失败
- [x] 1.2 追加「往返保真」测试：含注释的配置打开后新增步骤、上移分组并保存，重新加载后注释、节点顺序、字段值均不变（后果：数据丢失；触发：保存覆盖 config.xml 时丢注释或顺序）
- [x] 1.3 追加「校验未通过不写文件」测试：把源路径改成绝对路径后保存，返回字段级错误且文件字节未变、原配置仍可加载（后果：数据损坏；触发：非法内容被写入）
- [x] 1.4 追加「外部修改检测」测试：打开后由测试代码改写文件再保存，返回外部已修改状态且磁盘内容仍是外部版本（后果：静默覆盖他人改动；触发：编辑器停留期间文件被改写）

## 2. Implementation（垂直切片，按序执行；骨架先行）

- [x] 2.1 [增] `src/IPCDeployment/Core/ConfigurationDocument.cs`：文档模型骨架——打开（读源文件、用 `ConfigLoader` 校验、不通过返回加载错误）、最小骨架新建、节点树（配置/分组/任务/步骤）、字段读写、序列化到同目录临时文件、校验、备份副本、原子替换、打开时记录时间与长度；先只覆盖配置/分组/任务公共字段与 Delay。日志点：保存成/败各一行（经 `DeploymentShell.WriteNote`）。跑 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~ConfigurationDocumentTests"`，1.1 GREEN
- [x] 2.2 [增] `src/IPCDeployment/Core/ConfigFieldSchema.cs`：九类步骤（Install/Copy/Registry/Command/Service/Network/Power/AutoStart/Delay）字段表，与 `ConfigLoader` 接受的元素/属性名逐一对齐，含内嵌 `Code` 的多行文本；`ConfigurationDocument` 按表暴露字段、按表写入属性。跑 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~ConfigurationDocumentTests"`，1.2 转为 GREEN（先看到因字段缺失而失败的 RED 记录在任务备注）
- [x] 2.3 [改] `ConfigurationDocument` 的增删与排序：新增/删除分组、任务、九类步骤，上移/下移只在本层内生效，搬移时整段搬运元素及其注释与缩进；保存链路补齐校验门→备份→原子替换与四类失败返回（校验未通过、外部已修改、IO 失败、打开即不合法）。无日志点：纯模型层，日志由 2.1 的保存入口统一记录。跑 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~ConfigurationDocumentTests"`，1.3、1.4 GREEN
- [x] 2.4 [增] `src/IPCDeployment/ViewModels/EditorViewModel.cs`、`EditorNodeViewModel.cs`、`EditorFieldViewModel.cs`：结构树数据、数据驱动表单（按字段表生成输入控件类型与选项）、脏标记、新建/打开/另存/保存命令、错误列表经行区间映射回节点。无测试：UI 编排层，行为由 2.1–2.3 的公共接口测试覆盖。验证：`dotnet build IPCDeployment.sln -c Debug`
- [x] 2.5 [增] `src/IPCDeployment/ConfigurationEditorWindow.xaml` 与 `.xaml.cs`：按 `## UI 草图` 搭工具栏、结构树、属性卡片、操作条、校验 `ui:InfoBar`；接上未保存三选、打开失败错误对话框、错误条目点击跳转节点、删除确认。[人工] 界面外观由用户启动程序目视确认，不截图
- [x] 2.6 [改] `src/IPCDeployment/MainWindow.xaml`（在「选择配置文件」旁加「编辑配置」`ui:Button`，列定义顺延）与 `MainWindow.xaml.cs`：打开编辑器窗口并传入当前配置路径，保存成功后调 `viewModel.ReloadAsync()`，把编辑器日志转给 `DeploymentShell.WriteNote`。验证：`dotnet build IPCDeployment.sln -c Debug`
- [x] 2.7 [改] `src/IPCDeployment/ViewModels/DeploymentViewModel.cs`：新增编辑器打开标记并入可开始判断（编辑器打开期间「开始部署」置灰）；`src/IPCDeployment/Core/DeploymentShell.cs`：新增一行日志入口并转发到现有 `DeploymentLog`。验证：`dotnet build IPCDeployment.sln -c Debug`
- [x] 2.8 [改] 键与 id 自动生成并只读：`ConfigFieldSchema` 给分组的 key、任务的 key、步骤的 id 标注只读；`ConfigurationDocument` 新增分组/任务时生成未被占用的 key 与不重名的名称，步骤 id 沿用 `step-N`；`EditorViewModel` 对只读字段不接写回，`ConfigurationEditorWindow` 的文本框绑 `IsReadOnly`。验证：`dotnet test IPCDeployment.sln -c Debug`

## 3. Review

- [x] 3.1 加载 `review-change` 做三轴审查（Spec / 契约 / Standards），修复 Critical 与 Important
- [x] 3.2 最终验证：`dotnet test IPCDeployment.sln -c Debug`
- [x] 3.3 [人工] 用户自行启动程序，按需求卡 7 条验收标准目视确认编辑器

## 4. Self-review (完成前)

- [x] 4.1 自审：逐条对照需求卡验收标准、协作卡节点、流程契约（含异常线）与 `specs/configuration-editor/spec.md`、`tasks.md`，确认完整实现、无遗漏；有缺口先修复再重审

## 5. Commit (人工验收通过且用户明确许可后才执行)

- [x] 5.1 [人工] 提交：`git add -- docs/changes/configuration-editor CONTEXT.md src/IPCDeployment tests/IPCDeployment.Tests && git commit -m "feat(configuration-editor): 新增配置编辑器"`（只 commit 不 push）
