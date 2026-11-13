# Tiny 变更记录

> 三行式：标题（日期 + 摘要）、改动、验证。最新在下。

## 2026-10-31 初始化项目文档与 Lazy Flow 状态

- 改动：重写 `AGENTS.md`（修正解决方案路径、启动配置/data root、无 Inno Setup）；创建 `docs/changes/tiny-log.md` 与 `.opencode/lazy/state.json`。
- 验证：`dotnet test` 58 通过；Release 构建 0 警告 0 错误；聚焦 ConfigLoaderTests 14 通过。

## 2026-11-01 增加 Debug 开发构建输出

- 改动：新增 `scripts/build-debug.ps1`，输出到 `build/debug` 且不生成 ZIP；`build/` 加入 `.gitignore`；`SampleCommand` 只在 Release 构建时复制到 `data/commands`。
- 验证：`build-debug.ps1` 构建成功，`build/debug` 含程序、根目录 XML、`data/` 和 README；`dotnet test` 70 通过。

## 2026-11-01 集成 logo.ico 图标

- 改动：`logo.ico` 移入 `src/IPCDeployment/`；csproj 增加 `ApplicationIcon` 与 `<Resource>`；`MainWindow` 的窗口和 `ui:TitleBar` 设置 `Icon` 包资源。
- 验证：Release 构建 0 警告 0 错误；exe 可提取 32x32 关联图标，`IPCDeployment.g.resources` 含 `logo.ico`；`dotnet test` 70 通过。

## 2026-11-01 默认验证改为 Debug

- 改动：`AGENTS.md` 的 Commands 段把日常构建/测试改为 `scripts/build-debug.ps1` 与 `dotnet test -c Debug`，Release 只用于打包前。
- 验证：`build-debug.ps1` 构建 0 警告 0 错误并生成 `build/debug`；`dotnet test -c Debug` 70 通过。

## 2026-11-01 修复图标不显示

- 改动：`TitleBar.Icon` 由字符串改为显式 `ui:ImageIcon`（IconElement 转换器会把字符串转成 null）；窗口图标改为在 `MainWindow` 构造时取 ICO 的 32x32 帧，避免 WPF 默认取 16x16 帧。
- 验证：加载 `IPCDeployment.MainWindow` 检查，`Window.Icon` 为 32x32 `BitmapFrameDecode`，`TitleBar.Icon` 为 `ImageIcon` 且 Source 解析成功；`dotnet test -c Debug` 70 通过。




## 2026-11-04 移除安装包 SHA-256 校验

- 改动：删除 config/模切工位.xml、卷绕工位.xml、下料工位.xml、config.sample.xml 中全部 13 处 sha256 属性（ConfigLoader 视空白为不校验）；预检/执行逻辑与 UI 的 SHA-256 字段保持不变。
- 验证：Select-String -Path config\*.xml -Pattern 'sha256=' 0 命中；dotnet test -c Debug 76 通过；package-portable.ps1 重新出包，dist/IPCDeployment/config 同样 0 命中。


## 2026-11-04 修复预览页步骤行横向滚动条

- 改动：MainWindow.xaml 的 OperationRowTemplate 把类型/详情/重启标记的横向 StackPanel 换成 Auto/*/Auto 三列 Grid，详情去掉 MaxWidth=360 改由星号列压缩并省略；选中任务的步骤 ListView 设 ScrollViewer.HorizontalScrollBarVisibility=Disabled。
- 验证：`build-debug.ps1` 0 警告 0 错误；`dotnet test -c Debug` 76 通过；窗口 1180/1040 宽度下的视觉效果待用户目视确认（不截图）。

- 补充：预览页任务列表（`SelectedGroup.Tasks`）同样加 `HorizontalScrollBarVisibility=Disabled`，避免长任务名撑出横向滚动条。

## 2026-11-04 删除示例素材并让示例配置自包含

- 改动：删除 `data/commands/{.keep,sample.cmd,sample.ps1}`、`data/files/{sample.txt,sample-guide.txt}`（`data/` 被 gitignore，仅影响本地与打包产物）；`config/config.sample.xml` 删掉引用这些文件的三个任务与「自定义命令」组，两处 Copy 示例源改为 `files/MySqlConnector.dll`。
- 验证：`dotnet test -c Debug` 78 通过（加载器会在解析期校验引用文件，示例配置因此必须同步）；`package-portable.ps1` 重新出包，`dist` 内无 sample 文件。

## 2026-11-06 版本号 1.0.0 与发布包名绑定 tag

- 改动：\src/IPCDeployment/IPCDeployment.csproj\ 加 \<Version>1.0.0</Version>\、\IncludeSourceRevisionInInformationalVersion=false\、\PlatformTarget=x64\；主窗口标题栏改为显示程序集产品版本（\工控机部署工具 1.0.0\）；\scripts/package-portable.ps1\ 从 csproj 读版本并要求当前提交带同名 tag，产物改为 \dist/IPCDeployment-1.0.0-win-x64\ 与同名 ZIP；同步 \使用说明.md\、\docs/开发说明.md\、\AGENTS.md\。
- 验证：无 tag 时打包报错退出；临时 tag \1.0.0\ 打包成功后已删除（正式 tag 待人工验收后创建）；包内 exe ProductVersion=1.0.0、PE machine=0x8664；Release 构建 0 警告 0 错误；\dotnet test -c Debug\ 82 通过；标题栏显示待用户目视确认（不截图）。

## 2026-11-13 标准模式一线操作手册

- 改动：新增根目录 `操作手册.html`（单文件离线，仅覆盖标准配置 `config/config.xml`）；新增 `scripts/build-manual.ps1` 用本机 Edge/Chrome 无头模式生成 PDF；`scripts/package-portable.ps1` 在标准包内增加 `操作手册.pdf`，HTML 不随包。
- 验证：`build-manual.ps1` 生成 767468 字节 PDF（文件头 `%PDF`）；HTML 内部锚点 15 个全部可达、无外部 http(s) 引用；两个 PowerShell 脚本语法解析 0 错误。

## 2026-11-13 手册更名与软件变化同步

- 改动：`操作手册.html` 更名为 `IPCDeployment-使用手册.html`，PDF 输出改为 `build/manual/IPCDeployment-使用手册.pdf`，发布包内为 `IPCDeployment-使用手册.pdf`；手册补充 MSI Afterburner 便携包、Npcap 前置步骤、运行时“警告”状态/警告任务卡片/WARN 日志；删除未使用的 DBX 安装包。
- 验证：`build-manual.ps1` 生成 783126 字节 PDF（`%PDF`）；HTML 锚点 15 个可达、无外部 http(s)；两个脚本语法解析 0 错误。

## 2026-11-13 标题栏帮助按钮

- 改动：主窗口 `TitleBar` 启用 WPF-UI 内置帮助按钮（最小化左侧 `?`），点击打开程序目录下的 `IPCDeployment-使用手册.pdf`；缺失或打开失败时弹提示。`build-debug.ps1` 同步生成并复制 PDF 到 `build/debug`；手册 2.1 补充按钮说明。
- 验证：`dotnet build` 0 警告 0 错误；`dotnet test -c Debug --no-build` 91 通过；`build/debug/IPCDeployment-使用手册.pdf` 存在（784452 字节）；按钮点击待用户目视确认（不截图）。

## 2026-11-13 图数查看器整合看图、日志分析改安装包

- 改动：4 份配置删除独立 ImageViewer 任务，ImageDataViewer 升级到 1.3.42（含 imageviewer 组件），日志分析改为 1.7.0 Inno Setup 安装包；同步 `config.sample.xml`；更新 `data/software` 并删除旧包；删除 `使用说明.md`，打包/调试脚本、README、开发说明、AGENTS 和手册引用同步；重新生成 PDF。
- 验证：5 份配置用 `ConfigLoader` 加载 0 错误；`dotnet test -c Debug --no-build` 91 通过；`scripts/build-debug.ps1` 0 警告 0 错误，`build/debug` 内只有 `IPCDeployment-使用手册.pdf`、无 `使用说明.md`。

## 2026-11-13 标题栏帮助与关于按钮

- 改动：`TitleBar.TrailingContent` 增加两个透明按钮，位于最小化/最大化/关闭左侧：「? 帮助」（`Question24`）打开使用手册 PDF，「! 关于」（`Alert24`）显示版本和手册文件名；移除内置 `ShowHelp`/`HelpClicked`。
- 验证：`dotnet build` 0 警告 0 错误；`dotnet test -c Debug --no-build` 91 通过；PDF 782071 字节；按钮位置与样式待用户目视确认（不截图）。

## 2026-11-13 标题栏按钮透明与对齐修正

- 改动：两个标题栏按钮显式设置 `Background`/`BorderBrush` 透明、`BorderThickness=0`、悬停/按下边框透明；外层 `StackPanel` 改为 `VerticalAlignment=Top`、`Height=30`，与最小化/最大化/关闭按钮同排。
- 验证：`dotnet build` 0 警告 0 错误；`dotnet test -c Debug --no-build` 91 通过；透明与对齐效果待用户目视确认（不截图）。

## 2026-11-13 关于窗口改成图示样式

- 改动：新增 `AboutWindow`，沿用 FluentWindow + TitleBar（只保留关闭），内容为图标、应用名、版本、发布形态、运行环境、开发者、授权和右下角“确定”；主窗口“关于”按钮改为打开该窗口。
- 验证：`dotnet build` 0 警告 0 错误；`dotnet test -c Debug --no-build` 91 通过；关闭运行中的程序后重跑 `scripts/build-debug.ps1` 成功，`build/debug` 已更新。

## 2026-11-13 关于窗口精修与版本跟随 tag

- 改动：关于窗口去掉“运行环境”，图标改为读取 ICO 最大帧（256×256）避免缩放模糊；版本号改为构建时读取 HEAD 精确 tag（无 tag 回退 csproj `<Version>`），打包版本源改为 tag 并校验 exe ProductVersion 一致；同步 AGENTS 和开发说明。
- 验证：临时 tag `9.9.9` 构建后 ProductVersion=9.9.9，删除 tag 后重建回退 1.0.0；`dotnet build` 0 警告 0 错误；`dotnet test -c Debug --no-build` 91 通过；`build/debug` 已更新。

## 2026-11-13 发布前源仓库检查与组件更新

- 改动：新增 `scripts/check-source-packages.ps1`，检查 `ImageDataViewer`、`JinovisionLogAnalyzer`、`JinoMonitor` 三个源仓库 `dist`；`-Apply` 自动复制新包、更新 5 份配置、清理旧包并校验；`package-portable.ps1` 打包前做只读检查并在落后时失败；本次同步 ImageDataViewer 1.3.43、日志分析 1.8.0，新增 JinoMonitor 1.1.0；手册/AGENTS/开发说明同步。
- 验证：检查脚本输出三个组件均为最新；5 份配置 `ConfigLoader` 校验通过；`dotnet build` 0 警告 0 错误；`dotnet test -c Debug --no-build` 91 通过；PDF 784157 字节；`build/debug` 已更新。

## 2026-11-13 发布 1.0.1

- 改动：发布前检查更新 JinoMonitor 至 1.2.0（ImageDataViewer 1.3.43、日志分析 1.8.0 保持最新）；新增 `.github/workflows/release.yml`，tag 推送后在 GitHub Actions 构建并发布不含 data 的 build-only 包；本地仍用 `package-portable.ps1` 生成完整包。
- 验证：源包检查全部最新；Debug 构建 0 警告 0 错误；`dotnet test -c Debug --no-build` 91 通过；Release 构建、标准包、锂电配置包和 GitHub Action 结果见发布记录。
