# IPCDeployment XML 配置说明

本文件面向需要编写或让 AI 生成部署配置的人。实现契约以仓库 `docs/specs/deployment-config.md` 为准；本文件是可直接照着写的参考。

## 1. 文件位置

- 配置文件放在程序目录 `config/` 下，必须是该目录的直接子文件，扩展名 `.xml`。
- 程序启动时优先加载 `config/config.xml`；不存在时加载按文件名排序的第一个非示例 XML。
- `config/config.sample.xml` 只是示例，不会出现在下拉中，也不会自动加载。
- 配置文件编码建议 UTF-8；XML 中不要使用 DTD 或外部实体。
- 一个配置就是一个完整的部署流程，不能 include 其他 XML。

## 2. 最小骨架

```xml
<?xml version="1.0" encoding="utf-8"?>
<Deployment schemaVersion="2.0" mode="preview">
  <Meta name="示例部署" description="说明文字" />
  <Options>
    <LogDirectory>logs</LogDirectory>
  </Options>
  <Groups>
    <Group key="software" name="软件安装">
      <Task key="sample" name="示例任务">
        <Delay id="sample-delay" name="等待 1 秒" seconds="1" />
      </Task>
    </Group>
  </Groups>
</Deployment>
```

## 3. 根元素

| 元素/属性 | 必填 | 说明 |
| --- | --- | --- |
| `Deployment` | 是 | 根元素，不能带命名空间 |
| `schemaVersion` | 是 | 必须为 `2.0` |
| `mode` | 否 | `preview`（默认）或 `execute` |
| `Meta` | 否 | `name`、`description` 两个说明属性 |
| `Options/LogDirectory` | 否 | 程序目录内的相对路径，默认 `logs` |
| `Groups` | 是 | 至少一个 `Group` |

`preview` 只校验和展示，不创建进程、不修改系统；`execute` 才会真实执行，并且需要预检通过、任务摘要和界面二次确认。

## 4. 分组、任务、步骤

```xml
<Groups>
  <Group key="software" name="软件安装">
    <Task key="install-app" name="安装软件">
      <Install ... />
      <Command ... />
    </Task>
  </Group>
</Groups>
```

- `Group`：`key`、`name` 必填；`name` 去空格后忽略大小写必须唯一。
- `Task`：`key`、`name` 必填；`key` 在整个配置内唯一。
- 每个 `Group` 至少一个 `Task`，每个 `Task` 至少一个步骤。
- 步骤按 XML 文档顺序执行。
- 步骤元素只允许：`Install`、`Copy`、`Registry`、`Command`、`Service`、`Network`、`Power`、`AutoStart`、`Delay`。
- 未知元素、未知属性、`onError` 都会被拒绝。
- 每个步骤必须写 `id`（全局唯一）和 `name`。

## 5. 步骤类型

### Install

安装 EXE 或 MSI。

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `file` | 是 | `data/software/` 下的相对路径，`.exe` 或 `.msi`，文件必须存在 |
| `arguments` | 否 | 安装参数；MSI 会自动加 `/i ... /qn /norestart`，本属性追加在后面 |
| `sha256` | 否 | 64 位十六进制；预检和执行前各校验一次 |
| `timeoutMinutes` | 否 | 1–60，默认 15 |
| `successExitCodes` | 否 | 逗号分隔，默认 `0`；`3010`、`1641` 表示需重启 |
| `skipIfFileExists` | 否 | 绝对路径或环境变量路径；文件存在时报告“已满足” |

示例：

```xml
<Install id="app-install" name="安装应用"
         file="software/app-setup.exe"
         arguments="/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
         timeoutMinutes="15" successExitCodes="0,3010"
         skipIfFileExists="%ProgramFiles%/App/App.exe" />
```

MSI 属性参数需要转义引号：

```xml
<Install id="mysql-install" name="安装 MySQL"
         file="software/mysql.msi"
         arguments="INSTALLDIR=&quot;C:\Program Files\MySQL\MySQL Server 8.4&quot;" />
```

### Copy

复制文件或目录。

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `source` | 是 | `data/files/` 下的相对路径；文件或目录 |
| `target` | 是 | 绝对路径或环境变量展开后的绝对路径 |
| `overwrite` | 否 | `ifDifferent`（默认）、`always`、`ifNotExists` |
| `backup` | 否 | `true` 时覆盖前备份为 `.bak-时间戳`，默认 `false` |

- 文件源：`target` 写完整目标文件名。
- 目录源：`target` 写目标目录，递归复制。
- `ifDifferent` 比较文件大小和最后写入时间。

```xml
<Copy id="copy-config" name="复制配置"
      source="files/app/config.json"
      target="%ProgramData%/App/config.json"
      overwrite="ifDifferent" backup="true" />
```

### Registry

写注册表。

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `hive` | 是 | `HKCU`、`HKLM`、`HKCR`、`HKU`、`HKCC` |
| `key` | 是 | 项路径，例如 `Software/App` |
| `valueName` | 是 | 值名称；可为空字符串表示默认值 |
| `valueType` | 是 | `String`、`ExpandString`、`DWord`、`QWord` |
| `value` | 是 | 与类型匹配的值；DWord/QWord 必须为整数 |
| `view` | 否 | `64`（默认）或 `32` |
| `expectedBefore` | 否 | 当前值不等于它时失败，不写入 |

写入前会读当前值，相同则“已满足”；写入后回读，不一致按失败处理。

```xml
<Registry id="set-locale" name="写入配置"
          hive="HKLM" key="Software/App"
          valueName="Mode" valueType="String" value="production"
          view="64" />
```

### Command

运行 `data/commands` 下的脚本/程序，或内嵌 cmd/PowerShell。

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `file` | 二选一 | `data/commands/` 下 `.exe`、`.cmd`、`.bat`、`.ps1`，文件必须存在 |
| `Code` | 二选一 | 子元素，内嵌代码，不能为空；与 `file` 互斥 |
| `language` | 内嵌必填 | `cmd` 或 `powershell` |
| `arguments` | 否 | 仅文件模式；内嵌模式不接受 |
| `workingDirectory` | 否 | `data/commands/` 下的相对目录 |
| `timeoutMinutes` | 否 | 1–60，默认 10 |
| `successExitCodes` | 否 | 逗号分隔，默认 `0` |
| `strict` | 否 | 内嵌 PowerShell 默认 `true`；`false` 关闭 `$ErrorActionPreference='Stop'` 和 try/catch |

- 内嵌代码写在 `<Code><![CDATA[ ... ]]></Code>` 中，代码里不能出现 `]]>`。
- 内嵌 PowerShell 默认注入 `$ErrorActionPreference = 'Stop'` 和 try/catch；未捕获异常退出码 1。
- 内嵌 CMD 默认加 `@echo off`、`setlocal EnableExtensions`、`exit /b %errorlevel%`。
- 脚本可以使用环境变量：`IPCDEPLOYMENT_APP_ROOT`、`IPCDEPLOYMENT_DATA_ROOT`、`IPCDEPLOYMENT_COMMANDS_DIR`、`IPCDEPLOYMENT_FILES_DIR`、`IPCDEPLOYMENT_CONFIG_PATH`。
- Command 每次都会执行，没有自动幂等。

```xml
<Command id="inline-ps" name="检查环境" language="powershell"
         timeoutMinutes="5" successExitCodes="0">
  <Code><![CDATA[
    Write-Output "data root: $env:IPCDEPLOYMENT_DATA_ROOT"
    if (-not (Test-Path 'C:\Windows')) { throw 'Windows 目录不存在' }
  ]]></Code>
</Command>
```

```xml
<Command id="run-script" name="运行脚本"
         file="commands/setup.ps1" timeoutMinutes="10" successExitCodes="0" />
```

### Service

设置服务启动类型或运行状态。

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `serviceName` | 是 | 精确服务名 |
| `startupType` | 否 | `Automatic`、`Manual`、`Disabled` |
| `state` | 否 | `Running`、`Stopped` |

`startupType` 和 `state` 至少填一个。修改后会回读，不一致按失败处理。

### Network

网卡高级属性或 IPv4。

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `adapter` | 是 | 精确网卡名称，例如 `以太网 4` |
| `kind` | 是 | `AdvancedProperty` 或 `IPv4` |
| `keyword` | Advanced 必填 | 驱动属性名，例如 `*JumboPacket`、`*SpeedDuplex` |
| `value` | Advanced 必填 | 目标值 |
| `valueType` | 否 | `String`（默认）、`ExpandString`、`DWord`、`QWord` |
| `expectedBefore` | 否 | 当前值不匹配时失败 |
| `mode` | IPv4 必填 | `Static` 或 `Dhcp` |
| `address` | Static 必填 | IPv4 地址 |
| `prefixLength` | Static 必填 | 1–32 |
| `gateway` | 否 | IPv4 网关 |
| `dns` | 否 | 逗号分隔的 IPv4 列表 |

- 高级属性通过网卡 `NetCfgInstanceId` 定位，不按驱动编号。
- 同一网卡的 `*SpeedDuplex` 必须排在其他高级属性之前。
- `Dhcp` 不接受地址、前缀、网关、DNS。

### Power

电源设置。

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `setting` | 是 | `Hibernate`、`FastStartup`、`PowerThrottling` |
| `value` | 是 | `On` 或 `Off` |

当前版本只支持这三项。高性能计划、处理器状态、大小核异类策略等需要高级电源设置的内容，请用内嵌 PowerShell 的 `powercfg` / 注册表实现。

### AutoStart

开机自启（HKCU/HKLM Run 项）。

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `hive` | 是 | `HKCU` 或 `HKLM` |
| `valueName` | 是 | Run 项值名称 |
| `command` | 是 | 绝对路径或环境变量路径 |
| `arguments` | 否 | 启动参数 |
| `enabled` | 否 | 默认 `true`；`false` 删除该项 |

写入后回读；下次登录生效。

### Delay

| 属性 | 必填 | 说明 |
| --- | --- | --- |
| `seconds` | 是 | 1–600 的整数 |

只等待，不创建进程；取消时立即结束。

## 6. 路径规则

- 源路径必须相对 `data/` 并带区域前缀：
  - Install：`software/...`
  - Copy：`files/...`
  - Command：`commands/...`
- 源路径不能是绝对路径、不能包含 `..`、不能逃出对应区域。
- 目标路径必须是绝对路径，或 `%ProgramFiles%`、`%ProgramData%`、`%TEMP%` 等环境变量展开后的绝对路径；不能是盘符根。
- 配置文件的 `LogDirectory` 必须是程序目录内的相对路径。

## 7. 幂等、回读与失败

- Registry、Network、Power、AutoStart 会先读当前值；已满足则返回“已满足”，写入后回读，不一致失败。
- Copy 的 `ifDifferent` 比较大小和最后写入时间；一致则“已满足”。
- Install 用 `skipIfFileExists` 避免重复安装；不填则每次都执行。
- Command 每次都会执行，不自动幂等。
- 任一步骤失败会结束当前任务，剩余步骤标记“未执行”，然后继续下一个任务。
- 后续任务和分组继续执行；没有整体失败状态，没有自动回滚。
- `preview` 不创建进程、不修改系统。

## 8. 完整示例

```xml
<?xml version="1.0" encoding="utf-8"?>
<Deployment schemaVersion="2.0" mode="execute">
  <Meta name="示例工位部署" description="安装软件、复制配置、写注册表并等待" />
  <Options>
    <LogDirectory>logs</LogDirectory>
  </Options>
  <Groups>
    <Group key="software" name="软件安装">
      <Task key="app" name="安装应用">
        <Install id="app-install" name="安装应用"
                 file="software/app-setup.exe"
                 arguments="/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
                 sha256="0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF"
                 timeoutMinutes="15" successExitCodes="0,3010"
                 skipIfFileExists="%ProgramFiles%/App/App.exe" />
      </Task>
    </Group>
    <Group key="configuration" name="文件与配置">
      <Task key="copy-config" name="复制配置">
        <Copy id="copy-config-file" name="复制配置文件"
              source="files/app/config.json"
              target="%ProgramData%/App/config.json"
              overwrite="ifDifferent" backup="true" />
      </Task>
      <Task key="registry" name="写注册表">
        <Registry id="set-mode" name="写入运行模式"
                  hive="HKLM" key="Software/App"
                  valueName="Mode" valueType="String" value="production"
                  view="64" />
      </Task>
    </Group>
    <Group key="finish" name="收尾">
      <Task key="delay" name="等待配置稳定">
        <Delay id="wait" name="等待 2 秒" seconds="2" />
      </Task>
    </Group>
  </Groups>
</Deployment>
```

## 9. AI 生成配置检查清单

1. 文件放在 `config/` 直属目录，扩展名 `.xml`；编码 UTF-8。
2. 根元素 `Deployment`，`schemaVersion="2.0"`，`mode` 只能是 `preview` 或 `execute`。
3. 每个 `Group` 的 `key`/`name` 必填，`name` 唯一；每个 `Task` 的 `key`/`name` 必填，`key` 全局唯一；每个步骤 `id` 全局唯一。
4. 每个 `Group` 至少一个 `Task`，每个 `Task` 至少一个步骤。
5. 源路径必须是 `software/`、`files/`、`commands/` 下的相对路径，且文件真实存在。
6. 目标路径必须是绝对路径或环境变量展开后的绝对路径；不要写盘符根。
7. 不要发明属性或元素；未知属性会被加载器拒绝。
8. 内嵌代码必须放在 `CDATA` 中，不能包含 `]]>`；内嵌 PowerShell/CMD 必须写 `language`。
9. `Command` 的 `file` 和 `Code` 只能二选一；内嵌模式不能写 `arguments`。
10. MSI 属性参数在 XML 中用 `&quot;` 转义引号。
11. 安装步骤建议写 `skipIfFileExists`；写注册表/网络/服务/电源/自启建议依赖内置回读。
12. 破坏性步骤（删除数据、卸载、关闭安全软件）单独放在任务里，并在 `Meta`/任务名中写清楚。
13. 先用 `mode="preview"` 校验，确认预检和任务摘要后再改 `execute`。
14. 真实执行前在代表性目标机上确认；不要在本机直接跑生产配置。
