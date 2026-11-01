# Tiny 变更记录

> 三行式：标题（日期 + 摘要）、改动、验证。最新在下。

## 2026-10-31 初始化项目文档与 Lazy Flow 状态

- 改动：重写 `AGENTS.md`（修正解决方案路径、启动配置/data root、无 Inno Setup）；创建 `docs/changes/tiny-log.md` 与 `.opencode/lazy/state.json`。
- 验证：`dotnet test` 58 通过；Release 构建 0 警告 0 错误；聚焦 ConfigLoaderTests 14 通过。
