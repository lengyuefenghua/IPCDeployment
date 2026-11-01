# Agent guidance

## Project facts

- VisionIPC Deployment is a portable Windows deployment tool: a net472 WPF-UI desktop app that reads an XML config and executes install, copy, registry, command, service, network, power, autostart, and delay steps grouped into groups and tasks.
- Solution: `VisionIPC.Deployment.sln` at the repo root (not under `src/`). `Core` has no WPF reference, `App` holds the WPF-UI 4.3.0 view and view models, `Tests` uses NUnit, and building `SampleCommand` copies `SampleCommand.exe` into `data/commands`.
- Packaging is the app plus `scripts/package-portable.ps1` producing a ZIP. There is no Inno Setup script or self-installer/uninstall entry for the tool itself; do not add one.
- The app requests administrator privileges. Both shipped configs are `mode="preview"`; execute requires `mode="execute"`, preflight, the task/step summary, and a second confirmation in the UI.
- `config.xml` and `config.sample.xml` sit next to the exe; startup loads `config.xml`, and the sample is reference only. `data/` holds software, files, and commands; `logs/` is runtime output. Config structure is Groups → Group → Task → Step, and group names must be unique.

## Commands

    dotnet test VisionIPC.Deployment.sln -c Release
    dotnet build VisionIPC.Deployment.sln -c Release --no-restore
    powershell -ExecutionPolicy Bypass -File scripts/package-portable.ps1

- Focused test: `dotnet test VisionIPC.Deployment.sln -c Release --filter "FullyQualifiedName~ConfigLoaderTests"`.
- `dotnet test` does not build `App`; run the build before packaging. Building (including tests) rewrites `data/commands/SampleCommand.exe`.
- Tests are Windows net472 and use fake process runners plus temp dirs: no admin rights or real system changes needed.
- Do not launch the app or run deployment operations during verification; build, test, package, and inspect files only. Real changes happen manually on a representative machine.
- `dist/VisionIPC-Deployment` and `dist/VisionIPC-Deployment.zip` hold the package; the script copies the App `bin/<Configuration>/net472` output, root `config.xml`/`config.sample.xml`, `data/`, and the root `README.md`. `dist/` is gitignored.

## Architecture and safety

- Keep Core free of WPF. The UI calls `DeploymentShell.LoadConfig` and `StartAsync`; executors implement `IOperationExecutor` and are resolved by `OperationRegistry`.
- The runner publishes TaskStarted/TaskCompleted and OperationStarted/OperationCompleted events. Global progress counts ended tasks; task sub-progress counts ended steps. Preview is inert; execute requires preflight, a visible task summary, and a second confirmation.
- Source paths are data-relative and must include the area prefix (`software/`, `files/`, `commands/`); reject absolute paths, `..`, and escapes. Commands are `.exe`, `.cmd`, `.bat`, or `.ps1` under `data/commands`, or explicit inline `<Code>` with `language="cmd|powershell"`. Use only the fixed Windows `cmd.exe`/`powershell.exe`; no arbitrary interpreter paths or downloads. Inline scripts use temporary files under `%TEMP%` and are deleted after execution.
- Write operations must read the current value, report already satisfied when equal, and read back after writing. Install may verify SHA-256. Timeouts must kill the process tree. Logs must be capped and redact secrets.
- Any step failure ends its task, marks the remaining steps not executed, and starts the next task; later groups are unaffected. There is no skipped state for failure, no global failure state, and no automatic rollback. Report restart-required results truthfully.
- Network advanced properties must be located by `NetCfgInstanceId`, never a hardcoded driver index. `SpeedDuplex` must be ordered before other advanced properties on the same adapter.
- The config loader prohibits DTD/external resolution and rejects unknown elements and attributes with line/column errors. PowerShell file and inline steps use `-ExecutionPolicy Bypass`; inline PowerShell defaults to `$ErrorActionPreference='Stop'` with `strict="false"` as opt-out.
- Command steps display `EXE`, `CMD`, or `PowerShell`; do not add separate inline/file labels.

## UI rules

- All user-visible controls must be WPF-UI controls or project controls that inherit from WPF-UI controls. Do not add stock WPF Button, TextBox, ListBox, ProgressBar, Window, Border, or TextBlock. Grid and StackPanel are layout containers only.
- Keep the Fluent-light direction: light title bar, light content, group rail, task list, step list, light full-width bottom log, report.
- WPF-UI templates need `ApplicationAccentColorManager.ApplySystemAccent()` in `App.OnStartup` before any window is created.
- UI changes need a textual layout sketch before code; tiny style tweaks are exempt. Acceptance is manual: never use screenshots, automated window/control-tree inspection, or keyboard/mouse automation, and never promise screenshot evidence.

## Lazy Flow

- Load `using-lazy-flow` and classify every code/doc change as tiny, patch, or full before acting. Tests default to none unless the `tdd-cycle` gate requires one.
- No active change. `config-driven-deployment-tool` is archived at `docs/changes/archive/2026-11-01-config-driven-deployment-tool`; start a new change with `/start <change-name>` when needed.
- Do not commit or archive before manual UI and real-deployment acceptance; both `git commit` and `git push` need explicit user permission. The workspace is a Git repository with no remote; push requires separate permission.
- Reply and write comments in Chinese. Write `docs/adr/` records only for real long-lived tradeoffs.
