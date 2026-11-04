# Tasks

> patch 级变更：在三份工位配置中新增 ApowerREC 与 Snipaste 两个免安装软件的解压 + 公共桌面快捷方式任务。无代码改动。
> 前提（已核实）：两个 zip 都没有顶层文件夹，`ApowerREC.exe` / `Snipaste.exe` 直接在压缩包根目录。

## 1. 资源准备

- [x] 1.1 把 `dist/IPCDeployment/data/software/Snipaste-2.11.3-x64.zip` 移动到 `data/software/Snipaste-2.11.3-x64.zip`；移动后确认 `data/software/` 下存在该文件、`dist/` 下已不存在（`dist/` 是打包输出目录，会被 `scripts/package-portable.ps1` 覆盖，不能作为源目录）。

## 2. 配置实现

- [x] 2.1 在 `config/卷绕工位.xml` 的 `software`（软件安装）组内、`log-analyzer` 任务之后插入两个任务：
  - `apowerrec`：`<Command id="apowerrec-install" name="解压 ApowerREC 并创建桌面快捷方式" language="powershell" timeoutMinutes="10" successExitCodes="0">`
  - `snipaste`：`<Command id="snipaste-install" name="解压 Snipaste 并创建桌面快捷方式" language="powershell" timeoutMinutes="10" successExitCodes="0">`

  两个步骤的 PowerShell 逻辑（照抄现有 `heidi-sql-install` 的写法）：
  1. 包路径一律用 `Join-Path $env:IPCDEPLOYMENT_DATA_ROOT 'software\<包名>'`（执行器只注入此变量，不得用相对路径或当前目录）。
  2. 包不存在 → throw。
  3. 目标 exe（`C:\Program Files\ApowerREC\ApowerREC.exe` / `C:\Program Files\Snipaste\Snipaste.exe`）已存在 → 输出“已安装，跳过解压”，继续建快捷方式。
  4. 目标目录存在但 exe 缺失（含解压不完整、其他文件缺失）→ 删目录后重新解压。
  5. `Expand-Archive -LiteralPath $zip -DestinationPath $target -Force` → 回读 exe 存在，否则 throw。
  6. 用 `New-Object -ComObject WScript.Shell` 在 `[Environment]::GetFolderPath('CommonDesktopDirectory')` 建 `ApowerREC.lnk` / `Snipaste.lnk`，`TargetPath` 指向 exe，`WorkingDirectory` 设为安装目录，`Description` 分别为「录屏工具 ApowerREC」「截图工具 Snipaste」→ 回读 lnk 存在，否则 throw。
  7. 不做任何自启/计划任务。

  日志点（Info，`Write-Output`）：包路径与解压目标、跳过原因、exe 与 lnk 回读结果。无测试：仅新增配置步骤，复用既有 Command 执行器，无崩溃/数据损坏风险。
- [x] 2.2 更新 `config/卷绕工位.xml` 的 `<Meta description>`：把“安装看图、日志分析与显卡工具”改为“安装看图、日志分析、录屏、截图与显卡工具”。
- [x] 2.3 对 `config/模切工位.xml` 重复 2.1、2.2（任务内容、id、exe 名、快捷方式名完全一致）。
- [x] 2.4 对 `config/下料工位.xml` 重复 2.1、2.2（任务内容、id、exe 名、快捷方式名完全一致）。
- [x] 2.5 更新 `使用说明.md` 的“三个配置的公共部署内容”清单，补一条：解压 ApowerREC（录屏）与 Snipaste（截图）到 `C:\Program Files`，仅在公共桌面建快捷方式，不开机自启。
- [x] 2.6 在 `config/卷绕工位.xml` 的 `image-data-viewer` 任务内、`image-data-viewer-install` 之后插入命令步 `image-data-viewer-shortcut`（`language="powershell" timeoutMinutes="5" successExitCodes="0"`）：校验 `C:\Program Files\ImageDataViewer\ImageDataViewer.App.exe` 存在（不存在 throw），在 `[Environment]::GetFolderPath('CommonDesktopDirectory')` 建 `ImageDataViewer.lnk`，`TargetPath` 指向该 exe、`WorkingDirectory` 为安装目录、`Description` 为「图像数据查看器」，保存后回读 lnk 存在。日志点（Info，`Write-Output`）：快捷方式路径。无测试：仅新增配置步骤，复用既有 Command 执行器，无崩溃/数据损坏风险。
- [x] 2.7 对 `config/模切工位.xml` 重复 2.6（内容、id、快捷方式名完全一致）。
- [x] 2.8 对 `config/下料工位.xml` 重复 2.6（内容、id、快捷方式名完全一致）。
- [x] 2.9 更新 `使用说明.md` 第一条公共部署内容，注明 ImageDataViewer 在公共桌面创建快捷方式。

## 3. 验证

- [x] 3.1 一次性确认两个包结构未变：列出 zip 内 `ApowerREC.exe`、`Snipaste.exe` 均在根目录（如换成带顶层目录的包，此步会失败并暴露 2.1 的前提失效）。
- [x] 3.2 运行 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`，确认构建成功、`build/debug/data/software/` 含 `ApowerREC.zip` 与 `Snipaste-2.11.3-x64.zip`，且 `build/debug/config/` 下三份 XML 都包含 `apowerrec` 与 `snipaste` 任务。
- [x] 3.3 运行 `dotnet test IPCDeployment.sln -c Debug`，确认全部通过（`ConfigLoaderTests` 的仓库载荷测试会加载 `config/*.xml`，即可证明三份配置能被解析）。
- [x] 3.4 运行 `dotnet test IPCDeployment.sln -c Debug` 与 `powershell -ExecutionPolicy Bypass -File scripts/build-debug.ps1`，确认 `build/debug/config/` 三份 XML 都含 `image-data-viewer-shortcut`。
- [x] 3.5 运行 `dotnet build IPCDeployment.sln -c Release --no-restore` 与 `powershell -ExecutionPolicy Bypass -File scripts/package-portable.ps1`，确认重新生成的 `dist/IPCDeployment.zip` 内三份 XML 均含新步骤。

## 4. Review

- [x] 4.1 加载 `review-change` 做轻量审查（改动仅三份 XML、`使用说明.md` 与一个文件移动），修复 Critical/Important。
- [x] 4.2 自审（ApowerREC/Snipaste 部分）：解压到 `C:\Program Files`、公共桌面快捷方式、都不自启、三个工位都加、`使用说明.md` 已同步、MSI Afterburner 与 RTSS 相关步骤一字未改。
- [x] 4.3 加载 `review-change` 对 2.6–2.9 的增量做轻量审查，修复 Critical/Important。
- [x] 4.4 自审：对照已确认方案逐条核对——ImageDataViewer 快捷方式在公共桌面、名称 `ImageDataViewer.lnk`、三份配置一致、`使用说明.md` 已同步——确认无遗漏。

## 5. Commit

- [ ] 5.1 [人工] 人工验收通过并经用户明确许可后提交：`git add -- config/卷绕工位.xml config/模切工位.xml config/下料工位.xml 使用说明.md docs/changes/2026-11-04-add-apowerrec-snipaste`（`data/` 被 gitignore，不入库；只 commit 不 push）。
