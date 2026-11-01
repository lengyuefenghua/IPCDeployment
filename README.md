# 工控机部署工具（IPCDeployment）

解压即用的 Windows 部署工具。程序读取与 exe 同目录的 `config.xml`，按“分组 → 任务 → 步骤”执行安装、复制、注册表、命令、服务、网络、电源、开机自启和延时操作，并用两级进度、日志和执行结果展示真实情况。

## 运行前置

- Windows 10/11，64 位优先。
- .NET Framework 4.7.2 或更高版本。
- 程序以管理员权限启动；Windows UAC 会提示确认。
- 不要从只读目录运行；日志默认写入程序目录下的 `logs/`。

## 快速开始

1. 解压 `dist/IPCDeployment.zip` 到可写目录。
2. 双击 `IPCDeployment.exe`，在 UAC 中确认管理员权限。
3. 程序默认加载同目录的 `config.xml`，显示分组、任务和步骤的总览。
4. 点击“校验配置”查看预检结果；`preview` 模式只校验和展示，不执行。
5. 默认 `config.xml` 为 `mode="execute"`，只运行示例 CMD/PowerShell、内嵌代码和 `Delay` 这类只读、无副作用任务；点击“开始部署”并在确认框中确认。
6. 需要完整功能示例时查看 `config.sample.xml`；它保持 `mode="preview"`，不会自动加载。

## 目录

    IPCDeployment/
      IPCDeployment.exe
      Wpf.Ui.dll 及其他依赖
      README.md
      config.xml              当前生效配置
      config.sample.xml       配置示例
      data/                   安装载荷
        software/             安装包
        files/                配置文件、SQL 脚本、模板
        commands/             自定义命令 .exe/.cmd/.bat/.ps1
      logs/                   运行时生成

## 配置

根元素：

    <Deployment schemaVersion="2.0" mode="preview">
      <Meta name="名称" description="说明" />
      <Options><LogDirectory>logs</LogDirectory></Options>
      <Groups>
        <Group key="software" name="软件安装">
          <Task key="mysql" name="MySQL 数据库">
            <Install ... />
            <Command ... />
          </Task>
        </Group>
      </Groups>
    </Deployment>

规则：

- `schemaVersion` 必须为 `2.0`。
- `mode` 为 `preview` 或 `execute`，默认 `preview`；preview 不执行任何步骤。
- 分组、任务、步骤按 XML 定义顺序展示和执行。
- 分组名必须唯一且非空；每个分组至少一个任务，每个任务至少一个步骤。
- 源路径必须写成 `data/` 相对路径并带区域前缀：`software/...`、`files/...`、`commands/...`。
- 目标路径必须是绝对路径，或使用 `%ProgramFiles%`、`%ProgramData%`、`%TEMP%` 等环境变量。
- 日志目录必须是程序目录内的相对路径，默认 `logs`。

### 步骤类型

| 类型 | 用途 | 关键属性 |
| --- | --- | --- |
| Install | 安装 EXE 或 MSI | file、arguments、sha256、timeoutMinutes、successExitCodes、skipIfFileExists |
| Copy | 复制文件或目录 | source、target、overwrite、backup |
| Registry | 写注册表 | hive、key、valueName、valueType、value、view、expectedBefore |
| Command | 运行 .exe/.cmd/.bat/.ps1 或内嵌 cmd/PowerShell | file、Code、language、strict、arguments、workingDirectory、timeoutMinutes、successExitCodes |
| Service | 服务启动类型或状态 | serviceName、startupType、state |
| Network | 网卡高级属性或 IPv4 | adapter、kind、keyword、value、valueType；或 mode、address、prefixLength、gateway、dns |
| Power | 电源设置 | setting（Hibernate、FastStartup、PowerThrottling）、value（On、Off） |
| AutoStart | 开机自启 | hive、valueName、command、arguments、enabled |
| Delay | 延时等待 | seconds |

规则：

- Install 会自动处理 MSI 的 `/i`、`/qn`、`/norestart`；EXE 的参数由配置提供。
- Install 的 sha256 可省略；填写时会在预检和启动安装器前校验。
- Copy 的 overwrite 可选 `ifDifferent`、`always`、`ifNotExists`；`backup=true` 时覆盖前备份。
- Command 只能引用 `data/commands` 下的 `.exe`、`.cmd`、`.bat`、`.ps1`，或使用 `<Code>` 写内嵌 cmd/PowerShell；每次都会运行，没有自动幂等。
- 文件模式由扩展名决定解释器；内嵌模式必须写 `language="cmd"` 或 `language="powershell"`。
- 内嵌 PowerShell 默认注入 `$ErrorActionPreference = 'Stop'` 和 try/catch；`strict="false"` 可关闭。
- 内嵌 CMD 默认注入 `@echo off`、`setlocal` 和 `exit /b %errorlevel%`；错误处理仍需脚本显式 `exit /b N`。
- 脚本可以使用 `IPCDEPLOYMENT_APP_ROOT`、`IPCDEPLOYMENT_DATA_ROOT`、`IPCDEPLOYMENT_COMMANDS_DIR`、`IPCDEPLOYMENT_FILES_DIR`、`IPCDEPLOYMENT_CONFIG_PATH` 环境变量定位目录。
- Network 的高级属性按网卡 NetCfgInstanceId 定位驱动项，不硬编码驱动编号；同一网卡的 SpeedDuplex 必须排在其他高级属性之前。
- Power 当前支持休眠、快速启动和电源节流；部分设置需要重启。
- AutoStart 写入 HKCU/HKLM 的 Run 项，下次登录生效。
- Delay 只等待指定秒数（1–600），不创建进程；取消时立即结束等待。
- 配置不支持 `onError`；失败策略固定为任务级。

命令示例：

```xml
<Command id="ps-file" name="运行 PowerShell 脚本"
         file="commands/sample.ps1" timeoutMinutes="5" successExitCodes="0" />

<Command id="inline-ps" name="内嵌 PowerShell" language="powershell">
  <Code><![CDATA[
    Write-Output "checking..."
  ]]></Code>
</Command>

<Command id="inline-cmd" name="内嵌 CMD" language="cmd">
  <Code><![CDATA[
    @echo off
    echo checking...
  ]]></Code>
</Command>
```

### 回读与幂等

- Registry、Network、Power、AutoStart 会先读当前值；已等于目标时返回“已满足”；写入后回读不一致按失败处理。
- Copy 的 `ifDifferent` 会比较文件大小和最后写入时间；相同则返回“已满足”。
- Install 可用 `skipIfFileExists` 避免重复安装。
- 不提供自动回滚。部分失败可能留下部分更改。

## 进度与失败

- **全局进度**：已结束任务数 / 总任务数。任务成功、失败或取消都算已结束。
- **任务子进度**：已执行步骤数 / 该任务步骤总数。失败任务只统计已经执行的步骤。
- 任务内任一步骤失败时，立即结束该任务，剩余步骤标记为“未执行”，自动开始下一个任务。
- 后续任务和后续分组继续执行；没有“部署整体失败”状态。
- 界面和报告不出现“跳过”；未运行的步骤统一显示“未执行”。
- 完成页显示每个任务的成功/失败、失败步骤、原因、未执行步骤和耗时。

## 安全边界

- 程序自身不写注册表、不创建卸载项；配置的步骤可以修改目标机。
- 只执行 `data/` 下的文件；拒绝绝对源路径、`..` 和逃逸 `data/` 的路径。
- 自定义命令运行 `data/commands` 下的 `.exe`、`.cmd`、`.bat`、`.ps1`，或显式内嵌 cmd/PowerShell；不做下载执行。
- 系统工具由内部执行器从 Windows System 目录解析，配置不能直接指定任意系统工具。
- 日志会截断过长输出，并对 password、pwd、token、secret 等值脱敏。
- preview 不会创建外部进程；execute 必须经过预检、任务摘要和二次确认。

## 日志与报告

- 日志默认写入程序目录 `logs/deploy-时间戳.log`。
- 主日志目录不可写时会回退到 `%TEMP%/IPCDeployment/logs`，并在界面提示。
- 每页底部显示全宽运行日志：总览和完成页显示全局日志，分组页显示当前任务日志。
- 界面显示当前任务、当前步骤、全局任务进度和任务子进度。
- 执行结果按分组、任务、步骤展示成功/失败/未执行、耗时、需重启项和日志路径。

## 开发与打包

    dotnet test IPCDeployment.sln -c Release
    dotnet build IPCDeployment.sln -c Release --no-restore
    powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1
    powershell -ExecutionPolicy Bypass -File scripts/package-portable.ps1

- 项目结构：`src/IPCDeployment`（程序，`Core/` 为不依赖 WPF 的逻辑）与 `tests/IPCDeployment.Tests`（NUnit）。
- 开发运行目录：`build/debug`，不生成 ZIP。
- 发布目录：`dist/IPCDeployment`；压缩包：`dist/IPCDeployment.zip`。
- 发布包包含程序、根目录 XML、`data/` 和 README。
- 界面验收由用户自行启动程序目视确认，不依赖截图或自动化界面操作。

## 已知边界

- 不提供回滚、系统还原、自动重启、并行执行或远程多机部署。
- 不支持在程序外通过命令行静默执行部署。
- 安装包内部进度不可见；一个 Install 步骤仍是一个进度点。
- 真实安装、注册表、网卡、电源和服务操作需要在代表性测试机上由用户确认执行。
