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

