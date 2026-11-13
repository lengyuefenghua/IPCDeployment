# Tasks

> 设计基线：Wireshark 静默安装不会装 Npcap；从 Wireshark 安装包内解出 `npcap-1.88.exe` 放到 `data/software`，在标准配置的 `wireshark` 任务里先装 Npcap。仅改配置与载荷，不改代码。

## 1. Implementation

- [x] 1.1 用 7-Zip 从 `Wireshark-4.6.9-x64.exe` 解出 `npcap-1.88.exe`（1.32 MB，版本 1.88）到 `data/software/`。
- [x] 1.2 修改 `config/config.xml` 的 `wireshark` 任务：新增 `Install id="npcap-install"`，`/S` 静默安装，`skipIfFileExists="%SystemRoot%/System32/Npcap/wpcap.dll"`，成功码 `0,3010`，超时 10 分钟；Wireshark 安装步骤保持在后。（日志点：沿用 Install 执行器日志）
- [x] 1.3 更新 `使用说明.md`：标准配置清单注明 Wireshark 前先安装 Npcap 抓包驱动。

## 2. Final verification

- [x] 2.1 `config.xml` 内嵌 PowerShell 语法检查：9 段通过。
- [x] 2.2 `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~Repository_configs_load_from_config_directory"`：1 通过（Npcap 文件存在，配置可加载）。
- [x] 2.3 `dotnet test IPCDeployment.sln -c Debug`：87 通过。
- [x] 2.4 `scripts/build-debug.ps1` 通过，`build/debug/data/software/npcap-1.88.exe` 与 `MSIAfterburner-portable.zip` 均存在。

## 3. 实现后审查与自审

- [x] 3.1 实现后审查：patch 按 `review-change` 轻量清单，`reviewed = true`。
- [x] 3.2 自审：对照要求（离线 Npcap、先于 Wireshark、已存在则跳过、文档同步）确认无遗漏。

## 4. 修订：Npcap 改为 UI 自动化安装

- [x] 4.1 `npcap-install` 由 `Install /S` 改为 `Command`：先检查 `wpcap.dll`；未安装则启动 `npcap-1.88.exe`，枚举安装器进程的可见窗口，聚焦默认按钮（ID 1）并发送回车，顺序 `I Agree → Install → Next → Finish`；回车未推进时兜底 `BM_CLICK`；记录窗口/按钮文本；5 分钟超时后结束进程。（日志点：Info，窗口标题与按钮文本）
- [x] 4.2 安装后回读 `%SystemRoot%\System32\Npcap\wpcap.dll`：成功退出 0；失败 `exit 201` + `warnExitCodes="201"`，提示手动运行安装包。
- [x] 4.3 验证：内嵌脚本语法通过、嵌入 C# `Add-Type` 编译通过、配置加载测试通过、`dotnet test` 91 通过。
- [x] 4.4 更新 `使用说明.md`：说明免费版不支持静默、自动点击、失败降级为警告。
- [x] 4.5 `build-debug.ps1` 通过，`build/debug` 已更新，`data/software/npcap-1.88.exe` 存在。

## 5. 人工验收与提交

- [x] 5.1 [人工] 关闭程序后重跑 `scripts/build-debug.ps1`；在代表性机器执行标准配置，确认 Npcap 自动点击安装成功、Wireshark 能列出网卡（必要时重启一次）；失败时应显示警告并提示手动安装路径。
- [x] 5.2 [人工] 验收通过并处理 git 后提交：`git add -- config/config.xml 使用说明.md docs/changes/wireshark-npcap` 后 `git commit -m "fix(wireshark-npcap): 自动点击安装 Npcap"`（只 commit 不 push；data/ 不入库）。
