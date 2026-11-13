# Agent guidance

## Project facts

- IPCDeployment (工控机部署工具) is a portable Windows deployment tool: a net472 WPF-UI desktop app that reads an XML config and executes install, copy, registry, command, service, network, power, autostart, and delay steps grouped into groups and tasks.
- Solution: `IPCDeployment.sln` at the repo root (not under `src/`). It has two projects: `src/IPCDeployment` (WPF-UI 4.3.0 app; `Core/` holds the WPF-free logic) and `tests/IPCDeployment.Tests` (NUnit). Example commands ship as `.cmd`/`.ps1` in `data/commands`.
- Packaging is the app plus `scripts/package-portable.ps1` producing the standard ZIP and the lithium config ZIP. There is no Inno Setup script or self-installer/uninstall entry for the tool itself; do not add one.
- The app requests administrator privileges. `config/config.xml` ships as `mode="execute"` with the deployment steps and `config/config.sample.xml` stays `mode="preview"`; execute requires `mode="execute"`, preflight, the task/step summary, and a second confirmation in the UI.
- `config/` holds selectable configurations as direct child `*.xml` files; startup loads `config/config.xml`, falling back to the first non-sample XML, and the UI switches configs with a drop-down. `config/config.sample.xml` is reference only: it is never listed or loaded automatically. `data/` holds software, files, and commands; `logs/` is runtime output. Config structure is Groups → Group → Task → Step, and group names must be unique. The three lithium line configs (模切/下料/卷绕) live in `config-lithium/` and ship in a separate `IPCDeployment-<version>-lithium-config.zip`; the standard package only carries `config/config.xml`.

## Commands

    powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1
    dotnet test IPCDeployment.sln -c Debug
    dotnet build IPCDeployment.sln -c Release --no-restore
    powershell -ExecutionPolicy Bypass -File scripts/package-portable.ps1

- Debug is the default for daily work: build with `scripts/build-debug.ps1` and test with `-c Debug`; Release is only needed before packaging.
- Focused test: `dotnet test IPCDeployment.sln -c Debug --filter "FullyQualifiedName~ConfigLoaderTests"`.
- `dotnet test` builds the app project as a dependency; run the build before packaging. Tests use temp directories and do not touch `data/`.
- Tests are Windows net472 and use fake process runners plus temp dirs: no admin rights or real system changes needed.
- Do not launch the app or run deployment operations during verification; build, test, package, and inspect files only. Real changes happen manually on a representative machine.
- `dist/IPCDeployment-<version>-win-x64` and `dist/IPCDeployment-<version>-win-x64.zip` hold the standard package; the script copies the `src/IPCDeployment/bin/<Configuration>/net472` output, the root `config/` directory, `data/`, and the root `使用说明.md` / `XML配置说明.md`, then builds `dist/IPCDeployment-<version>-lithium-config.zip` from the three `config-lithium/` XMLs (ZIP root `config/`, copied into the package's `config/` on site). The version comes from the app csproj `<Version>` and the script requires the current commit to carry the same tag. `dist/` is gitignored. `scripts/build-debug.ps1` copies the standard set to `build/debug` without a ZIP.

## Architecture and safety

- Keep `src/IPCDeployment/Core` free of WPF: it must not reference `System.Windows`. The UI calls `DeploymentShell.LoadConfig` and `StartAsync`; executors implement `IOperationExecutor` and are resolved by `OperationRegistry`.
- The runner publishes TaskStarted/TaskCompleted and OperationStarted/OperationCompleted events. Global progress counts ended tasks; task sub-progress counts ended steps. Preview is inert; execute requires preflight, a visible task summary, and a second confirmation.
- Source paths are data-relative and must include the area prefix (`software/`, `files/`, `commands/`); reject absolute paths, `..`, and escapes. Commands are `.exe`, `.cmd`, `.bat`, or `.ps1` under `data/commands`, or explicit inline `<Code>` with `language="cmd|powershell"`. Use only the fixed Windows `cmd.exe`/`powershell.exe`; no arbitrary interpreter paths or downloads. Inline scripts use temporary files under `%TEMP%` and are deleted after execution.
- Write operations must read the current value, report already satisfied when equal, and read back after writing. Install may verify SHA-256. Timeouts must kill the process tree. Logs must be capped and redact secrets.
- Any step failure ends its task, marks the remaining steps not executed, and starts the next task; later groups are unaffected. There is no skipped state for failure, no global failure state, and no automatic rollback. Report restart-required results truthfully.
- A task may declare `enabled="false"`; it is shown as 已禁用, is not executed, publishes no task events, produces no step results, is excluded from progress, and is skipped by runtime preflight checks while still passing structural validation.
- `Command` may declare `warnExitCodes`; a matching exit code marks the step as 警告 instead of failure, later steps continue, and a task with warnings ends as 警告 (counted as ended, not successful). Warning details appear in the log as a colored WARN line.
- Network advanced properties must be located by `NetCfgInstanceId`, never a hardcoded driver index. `SpeedDuplex` must be ordered before other advanced properties on the same adapter.
- The config loader prohibits DTD/external resolution and rejects unknown elements and attributes with line/column errors. PowerShell file and inline steps use `-ExecutionPolicy Bypass`; inline PowerShell defaults to `$ErrorActionPreference='Stop'` with `strict="false"` as opt-out.
- Command steps display `EXE`, `CMD`, or `PowerShell`; do not add separate inline/file labels.

## UI rules

- All user-visible controls must be WPF-UI controls or project controls that inherit from WPF-UI controls. Do not add stock WPF Button, TextBox, ListBox, ProgressBar, Window, Border, or TextBlock. Grid and StackPanel are layout containers only.
- Keep the Fluent-light direction: light title bar, light content, group rail, task list, step list, light full-width bottom log, report.
- WPF-UI templates need `ApplicationAccentColorManager.ApplySystemAccent()` in `App.OnStartup` before any window is created.
- UI changes need a textual layout sketch before code; tiny style tweaks are exempt. Acceptance is manual: never use screenshots, automated window/control-tree inspection, or keyboard/mouse automation, and never promise screenshot evidence.
- wpfui source code:C:\Users\wtg\Documents\wpfui-4.3.0\src

## Lazy Flow

- Load `using-lazy-flow` and classify every code/doc change as tiny, patch, or full before acting. Tests default to none unless the `tdd-cycle` gate requires one.
- No active change. `project-restructure` is archived at `docs/changes/archive/2026-11-01-project-restructure` (earlier: `docs/changes/archive/2026-11-01-config-driven-deployment-tool`); start a new change with `/start <change-name>` when needed.
- Do not commit or archive before manual UI and real-deployment acceptance; both `git commit` and `git push` need explicit user permission. The workspace is a Git repository with `origin` = `git@github.com:lengyuefenghua/IPCDeployment.git` on branch `main`; push requires separate permission.
- Reply and write comments in Chinese. Write `docs/adr/` records only for real long-lived tradeoffs.
