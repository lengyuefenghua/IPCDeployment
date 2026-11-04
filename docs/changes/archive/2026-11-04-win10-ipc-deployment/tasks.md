# Tasks

> 设计基线（patch）：`config/` 下只保留三个工位配置（`模切工位.xml`、`下料工位.xml`、`卷绕工位.xml`）和 `config.sample.xml`；每个工位配置是一份完整部署流程，只在 ImageDataViewer 规则包、Meta 和卷绕的 `bmian` 库上不同。不改源码、不启动部署程序、不执行任何安装；真实安装由用户在 `build/debug` 自行测试。`README.md` 不更新（用户明确要求项目文档与部署配置分离）。无新增行为测试：本次为配置、文档、测试适配和二进制载荷变更；验证用仓库配置加载测试、PowerShell 语法静态检查和构建。

## 目标

- 目标机：Win10 工控机，i9-12900K，管理员权限。
- 三个工位：模切、下料、卷绕；各自导入对应规则包。
- 软件：ImageViewer、ImageDataViewer、Jinovision 日志分析工具、MSI Afterburner（占位）、MySqlConnector.dll。
- 数据库：强制重置 MySQL 8.4.7，数据目录 `C:\MySQL`，root/root，`inovance`，`root@'%'`，局域网 3306；卷绕额外创建 `bmian`。
- 系统：高性能 + 大小核异类策略；用 LGPO 写入本地组策略关闭 Defender，再 `Set-MpPreference` 立即生效。
- MSI Afterburner 先占位：安装 + 计划任务自启；Profile/锁频以后再补。

## 1. 准备载荷

- [x] 1.1 从外层安装器提取内层 `data/software/MSIAfterburnerSetup466.exe`，校验 MZ 头和长度 42663360。
- [x] 1.2 记录 ImageViewer、ImageDataViewer、MySQL MSI、Afterburner 内层安装包的 SHA-256，并写入配置。
- [x] 1.3 恢复被删除的示例载荷，保证 `config/config.sample.xml` 可加载。
- [x] 1.4 复制 `scripts/LGPO.exe` 到 `data/commands/LGPO.exe`（微软签名的本地组策略工具）。

## 2. 三个工位配置

- [x] 2.1 将 `config/config.xml` 改名为 `config/下料工位.xml`，新增 `config/模切工位.xml`、`config/卷绕工位.xml`；`config/config.xml` 不再存在，启动按非示例文件名排序回退到 `下料工位.xml`。
- [x] 2.2 三个配置的 `Meta` 名称分别为“模切工位部署”“下料工位部署”“卷绕工位部署”。
- [x] 2.3 软件安装、日志分析工具、MySqlConnector.dll、MSI Afterburner、高性能、Defender 步骤在三个配置中保持一致；ImageDataViewer 的 `skipIfFileExists` 修正为 `%ProgramFiles%/ImageDataViewer/ImageDataViewer.App.exe`。
- [x] 2.4 每个配置在 ImageDataViewer 安装后增加“导入规则包”任务：
  - 定位 `C:\Program Files\ImageDataViewer\ImageDataViewer.App.exe`；
  - 等待 2 秒，若安装器自动启动的 `ImageDataViewer.App` 已运行则强制关闭；导入返回 7 时再关闭并重试一次；
  - 用 `Start-Process -Wait -PassThru` 执行 `--import-package <包> --rule <规则>`，读取 `ExitCode`；
  - 退出码 0 成功，5 输出警告并继续，2/3/4/6/7 失败。
  - 模切：`模切规则.idvpack` / `模切规则`；下料：`下料规则.idvpack` / `下料规则`；卷绕：`卷绕规则.idvpack` / `卷绕规则`。
- [x] 2.5 MySQL 任务：强制清理、安装 8.4.7、`C:\MySQL`、root/root、`inovance`、`root@'%'`、防火墙 3306；卷绕配置额外创建 `bmian` 并在配置/验证步骤回读两个库。
- [x] 2.6 高性能与大小核：`powercfg` 设置高性能、最低处理器状态 100%、关闭核心停车、提升模式 2、提升策略 100、关闭电源节流；异类项 `93b8b6dc=2`、`bae08b81=2`、`7f2f5cfa=0`，缺失即失败并回读。
- [x] 2.7 关闭 Defender：生成 LGPO 文本，执行 `LGPO.exe /t`、`gpupdate /force`，回读实时保护策略；`Set-MpPreference` 立即生效；仍启用则任务失败。
- [x] 2.8 更新 `AGENTS.md` 第 8 行为“with the deployment steps”。
- [x] 2.9 数据库分组新增 HeidiSQL 任务：解压 `software/HeidiSQL.zip` 到 `%LOCALAPPDATA%\Programs\HeidiSQL`，校验 `heidisql.exe`，在当前用户桌面创建 `HeidiSQL.lnk`；已存在 `heidisql.exe` 时跳过解压，保留连接配置和备份。

## 3. 验证（只构建与测试，不运行部署）

- [x] 3.1 修改 `Repository_configs_load_from_config_directory`，加载 `config/` 下所有非示例 XML；三个工位配置和示例均可加载。
- [x] 3.2 对三个工位配置分别做内嵌 PowerShell 语法静态检查。
- [x] 3.3 运行 `dotnet test IPCDeployment.sln -c Debug`，76 通过。
- [x] 3.4 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`，0 警告 0 错误。
- [x] 3.5 检查 `build/debug/config/` 下三个工位配置、`build/debug/data/files/*.idvpack`、`build/debug/data/commands/LGPO.exe` 均存在；不启动 `IPCDeployment.exe`，不执行安装。

## 4. Review

- [x] 4.1 patch 轻量审查：配置/脚本正确性、危险操作边界、路径与回读。
- [x] 4.2 自审：逐条对照目标与三个配置，确认无遗漏；确认未启动程序、未执行安装。

## 5. Commit（人工验收后）

- [x] 5.1 [人工] 用户明确许可提交/推送后执行：
  `git add -A -- config AGENTS.md docs/changes/win10-ipc-deployment data`
  然后 `git commit -m "feat(win10-ipc-deployment): 配置 Win10 工控机三个工位真实部署"`；只 commit，不 push。
