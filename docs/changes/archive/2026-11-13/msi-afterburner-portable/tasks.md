# Tasks

> 设计基线：MSI Afterburner 改为便携解压部署（`data/software/MSIAfterburner-portable.zip`），不运行原安装器，从而跳过 DirectX 联网步骤与 RTSS；锁频参数由现场手动设置。计划任务自启保留。

## 1. Implementation

- [x] 1.1 用 7-Zip 解出 `MSIAfterburnerSetup466.exe` 载荷，排除 `$PLUGINSDIR`、`uninstall.exe`、`Redist`（含 RTSS）、`SDK`、`Doc`，打包为 `data/software/MSIAfterburner-portable.zip`（21.7 MB，含 `MSIAfterburner.exe` 与 `RTCore*.sys`）。（日志点：无，二进制打包）
- [x] 1.2 修改 `config/config.xml`、`config-lithium/{模切,卷绕,下料}工位.xml`：`msi-afterburner` 任务把 `Install` 换成 `Command`+PowerShell，解压到 `C:\Program Files (x86)\MSI Afterburner`、校验 exe、公共桌面建快捷方式；任务名统一为 `MSI Afterburner`；计划任务自启步骤不变。（日志点：Info，`Write-Output` 输出解压/快捷方式路径）
- [x] 1.3 更新 `使用说明.md`：标准配置清单与 MSI Afterburner 说明改为便携部署、不装 RTSS/DirectX、锁频现场手动设置。

## 2. Final verification

- [x] 2.1 四份配置内嵌 PowerShell 语法检查：48 段全部通过。
- [x] 2.2 四份配置不再引用 `MSIAfterburnerSetup466.exe`，均引用 `MSIAfterburner-portable.zip`。
- [x] 2.3 `dotnet test IPCDeployment.sln -c Debug`：87 通过。
- [x] 2.4 `scripts/build-debug.ps1` 通过，`build/debug/data/software/MSIAfterburner-portable.zip` 存在。

## 3. 实现后审查与自审

- [x] 3.1 实现后审查：patch 按 `review-change` 轻量清单，`reviewed = true`。
- [x] 3.2 自审：对照要求（便携部署、不碰 DirectX/RTSS、四份配置一致、计划任务保留、锁频手动）确认无遗漏。

## 4. 人工验收与提交

- [x] 4.1 [人工] 关闭程序后重跑 `scripts/build-debug.ps1`；在代表性机器执行标准配置，确认 MSI AB 解压成功、无 DirectX 下载、桌面快捷方式可用；手动设置锁频并勾选开机应用。
- [x] 4.2 [人工] 验收通过并处理 git 后提交：`git add -- config/config.xml config-lithium 使用说明.md docs/changes/msi-afterburner-portable` 后 `git commit -m "fix(msi-afterburner-portable): 改为便携解压部署"`（只 commit 不 push；data/ 不入库）。
