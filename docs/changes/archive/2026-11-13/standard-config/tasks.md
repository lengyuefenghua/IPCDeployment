# Tasks

> 设计基线：新增 `config/config.xml` 作为启动默认配置，内容为通用基线（保留看图软件、日志分析、录屏/截图/显卡、电源与 Defender，新增 Wireshark、Process Explorer、Everything），移除规则包导入与数据库相关任务。复用现有三个工位配置里的任务写法和内嵌 PowerShell，不新增代码、不改加载器。

## 1. Implementation

- [x] 1.1 新增 `config/config.xml`：复用 `下料工位.xml` 的通用任务（ImageViewer、ImageDataViewer、日志分析、ApowerREC、Snipaste、MSI Afterburner、高性能、关闭 Defender），删除规则导入与 file-copy/database 分组，新增 Wireshark、Process Explorer、Everything 三个任务，`Meta` 改为标准配置说明，`mode="execute"`。（日志点：沿用各任务内嵌 PowerShell 的 Write-Output；无新增日志点）
- [x] 1.2 更新 `使用说明.md`：简介、快速开始与启动默认说明、新增“标准配置”章节、目录树加入 `config.xml`。

## 2. Final verification

- [x] 2.1 对 `config/config.xml` 全部 `<Code>` 执行 `[scriptblock]::Create()` 语法检查：8 段通过。
- [x] 2.2 运行 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~Repository_configs_load_from_config_directory"`：1 通过。
- [x] 2.3 运行 `dotnet test IPCDeployment.sln -c Debug`：87 通过。
- [x] 2.4 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`，并确认 `build/debug/config/config.xml` 存在。
- [x] 2.5 检查 `config/config.xml` 无 `mysql|HeidiSQL|idvpack|rules|共享库` 残留。

## 3. 实现后审查与自审

- [x] 3.1 实现后审查：patch 按 `review-change` 轻量清单，`reviewed = true`。
- [x] 3.2 自审：逐条对照已确认方案（软件清单、默认启用、默认配置文件、文档同步、验证命令）确认无遗漏。

## 4. 人工验收与提交

- [x] 4.1 [人工] 在代表性机器上用 Debug 包打开程序，确认启动默认选中 `config.xml`，校验通过，任务列表与步骤符合预期；真实安装（尤其 Wireshark/Npcap、Everything 安装目录、Process Explorer EULA）由用户目视/实际执行确认。
- [x] 4.2 [人工] 人工验收通过且用户明确许可后提交：`git add -- config/config.xml docs/changes/standard-config 使用说明.md` 后 `git commit -m "feat(standard-config): 新增通用标准配置"`（只 commit 不 push）。
