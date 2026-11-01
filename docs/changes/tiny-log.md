# Tiny 变更记录

> 三行式：标题（日期 + 摘要）、改动、验证。最新在下。

## 2026-10-31 初始化项目文档与 Lazy Flow 状态

- 改动：重写 `AGENTS.md`（修正解决方案路径、启动配置/data root、无 Inno Setup）；创建 `docs/changes/tiny-log.md` 与 `.opencode/lazy/state.json`。
- 验证：`dotnet test` 58 通过；Release 构建 0 警告 0 错误；聚焦 ConfigLoaderTests 14 通过。

## 2026-11-01 增加 Debug 开发构建输出

- 改动：新增 `scripts/build-debug.ps1`，输出到 `build/debug` 且不生成 ZIP；`build/` 加入 `.gitignore`；`SampleCommand` 只在 Release 构建时复制到 `data/commands`。
- 验证：`build-debug.ps1` 构建成功，`build/debug` 含程序、根目录 XML、`data/` 和 README；`dotnet test` 70 通过。
