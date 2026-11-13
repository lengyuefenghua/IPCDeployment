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
