# Deployment configuration contract

## File and directory

- `config/` holds selectable configurations as direct child `*.xml` files; `config/config.xml` is the default configuration and `config/config.sample.xml` is a reference example.
- The program loads `config/config.xml` on startup; when it is missing, the first non-sample XML in `config/` is loaded. `config.sample.xml` is never listed, selected, or loaded automatically.
- The configuration file must be a direct child of `config/`; a path outside it is rejected.
- The configuration editor may only open and save files inside `config/`.
- `data/software` stores installers, `data/files` stores copy sources and scripts, and `data/commands` stores executable commands.
- Data sources resolve under `<application>/data`, regardless of which XML is selected.
- `logs/` is created at runtime under the application directory, or falls back to `%TEMP%/IPCDeployment/logs`.

## Root

    <?xml version="1.0" encoding="utf-8"?>
    <Deployment schemaVersion="2.0" mode="preview">
      <Meta name="IPCDeployment 部署" description="示例" />
      <Options>
        <LogDirectory>logs</LogDirectory>
      </Options>
      <Groups>
        <Group key="software" name="软件安装">
          <Task key="mysql" name="MySQL 数据库">
            <Install id="mysql.install" name="安装数据库"
                     file="software/mysql.msi" arguments="" />
            <Command id="mysql.config" name="配置数据库"
                     file="commands/mysql-config.exe" />
          </Task>
        </Group>
      </Groups>
    </Deployment>

- `schemaVersion` MUST be `2.0`.
- `mode` is `preview` or `execute`; default `preview`. Preview performs no external process and no system change.
- `Meta` is optional; name and description are informational.
- `Options/LogDirectory` is optional; it must be a relative path inside the application directory. Default `logs`.
- `Groups` is required and must contain at least one `Group`.
- Group, task, and step order is document order. The engine does not reorder them.

## Groups, tasks, and steps

- `Group` has required `key` and `name`. Name is unique after trimming and must not be empty.
- `Group` contains one or more `Task` elements.
- `Task` has required `key` and `name`; task keys are unique across the configuration.
- `Task` contains one or more step elements.
- The supported step elements are `Install`, `Copy`, `Registry`, `Command`, `Service`, `Network`, `Power`, `AutoStart`, and `Delay`.
- Unknown elements and unknown attributes are rejected. The legacy `onError` attribute is not supported.

## Common step attributes

- `id`: required, unique across the configuration.
- `name`: required display name.
- Step order is document order.
- Failure policy is fixed: any failed step ends its task and starts the next task. There is no per-step continue/stop policy.

## Step types

### Install

Attributes: `file`, `arguments`, `sha256`, `timeoutMinutes`, `successExitCodes`, `skipIfFileExists`.

- `file` is a data-relative path under `software/`, for example `software/ImageViewer-1.2.0-win-x64-setup.exe`.
- The file must exist and use `.exe` or `.msi`.
- For `.msi` the program invokes msiexec with `/i`, `/qn`, and `/norestart`, then appends arguments.
- `sha256` is optional; when present it must be 64 hexadecimal characters and is verified before the installer starts.
- `timeoutMinutes` is 1 to 60; default 15.
- `successExitCodes` is a comma-separated integer list; default 0. 3010 and 1641 are success with restart required.
- `skipIfFileExists` is an absolute or environment-expanded file path; if it exists the step reports already satisfied.

### Copy

Attributes: `source`, `target`, `overwrite`, `backup`.

- `source` is a data-relative path under `files/`, for example `files/license.dat` or `files/configs`.
- `target` is an absolute path or an environment-expanded absolute path.
- For a file source, `target` is the full destination file path.
- For a directory source, `target` is the destination directory root; the tree is copied recursively.
- `overwrite` is `ifDifferent` (default), `always`, or `ifNotExists`. `ifDifferent` compares size and last write time.
- `backup=true` copies an existing file, or renames an existing directory, to a `.bak-时间戳` sibling before overwrite.
- Directories are created as needed.

### Registry

Attributes: `hive`, `key`, `valueName`, `valueType`, `value`, `view`, `expectedBefore`.

- `hive` is HKCU, HKLM, HKCR, HKU, or HKCC.
- `valueName` may be empty for the default value.
- `valueType` is String, ExpandString, DWord, or QWord.
- `view` is 64 or 32; default 64.
- The current value is read first. If it already equals the target, the step reports already satisfied.
- If `expectedBefore` is present and the current value does not match, the step fails without writing.
- After writing, the value is read back; a mismatch is a failure.

### Command

Attributes: `file`, `arguments`, `workingDirectory`, `timeoutMinutes`, `successExitCodes`, `language`, `strict`.
Child element: `Code`.

- `file` is a data-relative path under `commands/`, must exist, and must use `.exe`, `.cmd`, `.bat`, or `.ps1`.
- `Code` is an alternative to `file` and must contain non-empty text. `file` and `Code` are mutually exclusive.
- Inline code requires `language` (`cmd` or `powershell`) and does not accept `arguments`.
- `workingDirectory` is optional and, when present, is a data-relative directory under `commands/`.
- `.exe` is started directly. `.cmd`/`.bat` use the fixed Windows `cmd.exe`; `.ps1` uses the fixed Windows `powershell.exe` with `-NoProfile -NonInteractive -ExecutionPolicy Bypass -File`.
- Inline code is written to a temporary script under `%TEMP%\IPCDeployment\scripts`, executed by the same fixed interpreter, and deleted after execution.
- Inline PowerShell uses `$ErrorActionPreference = 'Stop'` and try/catch with exit code 1 by default; `strict="false"` disables that wrapper.
- Inline CMD receives `@echo off`, `setlocal EnableExtensions`, and `exit /b %errorlevel%`; CMD errors still require explicit `exit /b`.
- Scripts receive `IPCDEPLOYMENT_APP_ROOT`, `IPCDEPLOYMENT_DATA_ROOT`, `IPCDEPLOYMENT_COMMANDS_DIR`, `IPCDEPLOYMENT_FILES_DIR`, and `IPCDEPLOYMENT_CONFIG_PATH`.
- `timeoutMinutes` is 1 to 60; default 10.
- `successExitCodes` is a comma-separated integer list; default 0.
- Output is streamed to the log, capped, and redacted. Commands always run; they have no automatic idempotency.
- Downloads and arbitrary interpreter paths are not supported.

### Service

Attributes: `serviceName`, `startupType`, `state`.

- `startupType` is Automatic, Manual, or Disabled.
- `state` is Running or Stopped.
- At least one of `startupType` and `state` is required.
- Current startup type and status are read first; satisfied steps report already satisfied.
- The startup type is changed through the fixed Windows `sc.exe`; state changes use the Windows service controller.
- After a change, startup type and status are read back; a mismatch is a failure.
- If the service is not present, preflight warns and execution fails if it is still missing.

### Network

Attributes: `adapter`, `kind`, `keyword`, `value`, `valueType`, `expectedBefore`, `mode`, `address`, `prefixLength`, `gateway`, `dns`.

- `adapter` MUST match the exact Windows adapter name, for example `以太网 4`.
- `kind` is AdvancedProperty or IPv4.

AdvancedProperty:

- `keyword` is the driver property keyword, for example `*JumboPacket` or `*SpeedDuplex`.
- `value` and `valueType` (String, ExpandString, DWord, QWord; default String) are required.
- The driver key is located by the adapter's NetCfgInstanceId; a hardcoded driver index is never used.
- The current value is read first; equal values report already satisfied; `expectedBefore` mismatch fails without writing; writes are read back.
- Advanced properties are reported as requiring a restart when the driver may need one.
- On the same adapter, a `*SpeedDuplex` step MUST appear before other advanced-property steps; preflight rejects a violation.

IPv4:

- `mode` is Static or Dhcp.
- Static requires `address` (IPv4) and `prefixLength` (1 to 32); `gateway` and `dns` are optional. `dns` is a comma-separated IPv4 list.
- Dhcp does not accept `address`, `prefixLength`, `gateway`, or `dns`.
- The program uses the fixed Windows netsh for the change and reads the resulting state through the network adapter configuration.
- A readback mismatch is a failure.

### Power

Attributes: `setting`, `value`.

- `setting` is Hibernate, FastStartup, or PowerThrottling.
- `value` is On or Off.
- Current state is read first; equal values report already satisfied; writes are read back; mismatches are failures.
- FastStartup and PowerThrottling may require a restart. Hibernate uses powercfg plus a registry readback.
- USB selective suspend, heterogeneous power policy, and other advanced power settings are not part of this version.

### Delay

Attributes: `seconds`.

- `seconds` is required and must be an integer from 1 to 600.
- The step waits for the requested duration without launching a process or changing the machine.
- Cancellation ends the wait immediately.

### AutoStart

Attributes: `hive`, `valueName`, `command`, `arguments`, `enabled`.

- `hive` is HKCU or HKLM.
- The value is written under `Software/Microsoft/Windows/CurrentVersion/Run`.
- `command` is an absolute or environment-expanded executable path; arguments are optional.
- `enabled=true` writes an ExpandString value; `enabled=false` deletes the value if present.
- The current raw value is compared first; equal values report already satisfied; after writing, the raw value is read back.
- The setting takes effect at the next logon.

## Paths

- Source paths are data-relative and include the area prefix: `software/`, `files/`, or `commands/`.
- Absolute source paths, `..` segments, and paths outside the area are rejected.
- Target paths are absolute or environment-expanded absolute paths; a drive root is rejected.
- LogDirectory is a relative path inside the application directory.
- The exact adapter name and service name are matched; no role-to-name inference is performed.

## Validation

- XML is loaded with DTD processing prohibited and no external resolver.
- Root element, schemaVersion, mode, groups, tasks, steps, ids, required attributes, enums, timeouts, hashes, exit codes, IPv4 values, and paths are validated.
- Unknown elements, unknown attributes, duplicate group keys, duplicate group names, duplicate task keys, duplicate step ids, empty groups, empty tasks, and empty configurations are rejected.
- Group names are compared after trimming and ignoring case.
- Errors include line and column. Errors block execution; warnings are displayed but do not block a preview.
- Source files are checked for existence. Installer hashes are checked during preflight and again before launching.
- Administrator status, log directory writability, adapter existence, service presence, autostart target presence, and SpeedDuplex order are checked.
- Service absence and autostart target absence are warnings because an earlier step may create them.

## Execution and failure

- preview never creates a process or modifies the system.
- execute requires confirmed=true, a valid configuration, and a user confirmation in the UI.
- Groups, tasks, and steps run in document order.
- Each task publishes TaskStarted and TaskCompleted; each executed step publishes OperationStarted and OperationCompleted.
- Any failed step ends its task immediately. Remaining steps in that task are reported as not executed.
- The next task starts automatically, including tasks in later groups. There is no deployment-wide failure state.
- Cancellation ends the current task, marks remaining steps and tasks not executed, and does not count unstarted tasks as ended.
- Timeouts terminate the process tree. Output is capped; secrets matching password, pwd, token, or secret are redacted.
- The report lists task and step success, failure, cancellation, not-executed, elapsed time, restart-required, and the log path.
- There is no automatic rollback. A failed step may leave partial changes.

## Progress

- Global progress is ended tasks divided by total tasks. A task ends when all its steps are terminal, or when a step failure or cancellation ends it early.
- Task progress is executed steps divided by that task's total steps. Not-executed steps do not advance task progress.
- Failed tasks count as ended for global progress, but their task status remains failed.
- Groups do not have a separate progress ring; group status is derived from task results.

## Logs

- Log files are written under the configured log directory as `deploy-yyyyMMdd-HHmmss-fff.log`.
- If the primary log directory is not writable, the log falls back to `%TEMP%/IPCDeployment/logs` and the UI shows a warning.
- The UI keeps the most recent 2000 log lines; the file keeps the complete run log.
- Every page shows a full-width log panel at the bottom: overview and completion show the global log, while the group page shows the selected task log.
- The log includes timestamps, levels, task/step ids, exit codes, before/after values, and capped process output.

## Out of scope for this version

- JSON configuration.
- Rollback, system restore, automatic reboot, parallel execution, remote multi-machine deployment, and a configuration editor.
- Command-line or silent execution outside the app.
- Arbitrary interpreter paths and downloaded commands.
- Scheduled-task autostart, USB selective suspend, heterogeneous power policy, and firewall rules.
