# Tasks

> 设计基线：`config/` 只保留标准配置与示例；三份锂电工位配置移到 `config-lithium/`；`scripts/package-portable.ps1` 在生成标准包的同时生成 `dist/IPCDeployment-<版本>-lithium-config.zip`（ZIP 根为 `config/`，现场复制进标准包 `config/`）。不新增可执行代码，不改加载器。

## 1. Implementation

- [x] 1.1 移动 `config/模切工位.xml`、`config/卷绕工位.xml`、`config/下料工位.xml` 到 `config-lithium/`；`config/` 保留 `config.xml` 与 `config.sample.xml`。（日志点：无，文件移动）
- [x] 1.2 修改 `scripts/package-portable.ps1`：校验 `config-lithium/` 与三份 XML 存在；清理并生成 `dist/IPCDeployment-<版本>-lithium-config/` 与同名 ZIP；沿用同一版本号与 HEAD tag 校验。（日志点：`Write-Output` 输出锂电包路径）
- [x] 1.3 修改 `tests/IPCDeployment.Tests/ConfigLoaderTests.cs` 的 `Repository_configs_load_from_config_directory`：不依赖 data/ 时也校验 `config-lithium/` 存在三份 XML 且为 execute；data/ 存在时一并加载校验标准配置与锂电配置。
- [x] 1.4 更新文档：`使用说明.md`（快速开始、锂电专用配置与复制步骤、目录树）、`XML配置说明.md`（文件位置）、`docs/开发说明.md`（结构、打包产物）、`AGENTS.md`（config-lithium、打包产物）、`CONTEXT.md`、`docs/specs/deployment-config.md`（文件与目录约定）。

## 2. Final verification

- [x] 2.1 `dotnet build IPCDeployment.sln -c Debug`：0 警告 0 错误。
- [x] 2.2 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~Repository_configs_load_from_config_directory"`：1 通过。
- [x] 2.3 `dotnet test IPCDeployment.sln -c Debug`：87 通过。
- [x] 2.4 `scripts/package-portable.ps1` 通过 PowerShell 解析器语法检查。
- [x] 2.5 `scripts/build-debug.ps1` 通过，`build/debug/config/` 只有 `config.sample.xml` 与 `config.xml`。
- [x] 2.6 用临时目录模拟锂电包结构并压缩：ZIP 内为 `config/{模切,卷绕,下料}工位.xml`。未实际执行 `package-portable.ps1`：脚本要求 HEAD 带 `1.0.0` tag，当前提交没有 tag。

## 3. 实现后审查与自审

- [x] 3.1 实现后审查：patch 按 `review-change` 轻量清单，`reviewed = true`。
- [x] 3.2 自审：对照要求（标准包不含三份锂电配置、锂电包只含三份 XML、测试仍覆盖锂电配置、文档同步）确认无遗漏。

## 4. 人工验收与提交

- [x] 4.1 [人工] 关闭正在运行的 IPCDeployment，重跑 `scripts/build-debug.ps1`，确认 `build/debug/config/` 不含三份锂电配置；解压锂电包后把 XML 复制进 `config/` 能在下拉中选择。
- [x] 4.2 [人工] 验收通过并处理 git `dubious ownership` 后，按需提交：`git add -- config config-lithium scripts/package-portable.ps1 tests/IPCDeployment.Tests/ConfigLoaderTests.cs 使用说明.md XML配置说明.md AGENTS.md CONTEXT.md docs/开发说明.md docs/specs/deployment-config.md docs/changes/split-lithium-package` 后 `git commit -m "feat(split-lithium-package): 拆分标准包与锂电配置包"`（只 commit 不 push）。
