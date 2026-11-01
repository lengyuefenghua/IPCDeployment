---
Status: accepted
---

# 允许受控 CMD/PowerShell 脚本

## 背景

部署步骤经常需要 cmd 或 PowerShell 脚本。只允许预编译 `.exe` 会迫使每个小改动都重新编译，无法满足实际部署配置。

## 决策

- `Command` 支持 `data/commands` 下的 `.exe`、`.cmd`、`.bat`、`.ps1`，以及显式 `<Code>` 内嵌 cmd/PowerShell。
- 解释器固定为 Windows 自带的 `cmd.exe` 和 `powershell.exe`，不接受任意解释器路径。
- PowerShell 文件和内嵌代码使用 `-ExecutionPolicy Bypass`。
- 内嵌代码写入 `%TEMP%\VisionIPC-Deployment\scripts`，执行后删除；PowerShell 默认 `$ErrorActionPreference='Stop'`，可用 `strict="false"` 关闭。
- 仍然禁止下载执行、绝对脚本路径和 `data/commands` 外的脚本。

## 后果

- 配置可以自包含，也可以复用脚本文件。
- 脚本是任意代码，安全边界从“无 shell”变为“受控解释器 + 受控脚本目录”。
- 组策略、AppLocker 或 WDAC 仍可能阻止 PowerShell；兼容性优先时使用 `.cmd`/`.bat` 或编译 `.exe`。
- 日志只记录语言、长度和代码哈希，不打印完整内嵌代码。
