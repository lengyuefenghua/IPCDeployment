# Tasks

> patch 级变更：修正 `关闭 Windows Defender` 任务的最终判定，消除 `AntivirusEnabled=True` 造成的误报失败。
> 背景（已核实）：Win10 22H2 专业版忽略 `DisableAntiSpyware`，gpedit 显示「关闭 Microsoft Defender 防病毒 = 未配置」属正常；「关闭实时保护 = 已启用」已生效，日志中 `RealTimeProtectionEnabled=False`，但脚本按 `AntivirusEnabled` 判失败。
> 验收口径（已与用户确认）：只要实时保护关闭即通过；`AntivirusEnabled` 永不因该策略变 False，不作为失败依据。
> 定位一律用文本锚点，不用行号（三份配置因其他任务存在 1 行偏移）。

## 1. 配置实现

- [x] 1.1 三份配置（`config/模切工位.xml`、`config/卷绕工位.xml`、`config/下料工位.xml`）中，把 `disable-defender-apply` 的 Command 内、从文本锚点 `$status = Get-MpComputerStatus -ErrorAction SilentlyContinue` 起、到 `Write-Output 'Windows Defender 已关闭。'` 止的整段（含这两行）替换为：

  ```powershell
  $status = Get-MpComputerStatus -ErrorAction SilentlyContinue
  if ($null -eq $status) {
      $service = Get-Service -Name 'WinDefend' -ErrorAction SilentlyContinue
      if ($service -and $service.Status -eq 'Stopped') {
          Write-Output 'Windows Defender 服务已停止。'
          exit 0
      }
      throw '无法读取 Defender 状态，且 WinDefend 服务未停止。'
  }

  Write-Output ("Defender 状态：实时保护=" + $status.RealTimeProtectionEnabled +
      "，行为监控=" + $status.BehaviorMonitorEnabled +
      "，IOAV=" + $status.IoavProtectionEnabled +
      "，按访问保护=" + $status.OnAccessProtectionEnabled +
      "，AntivirusEnabled=" + $status.AntivirusEnabled +
      "，运行模式=" + $status.AMRunningMode +
      "，篡改保护=" + $status.IsTamperProtected)

  if ($status.RealTimeProtectionEnabled -ne $false) {
      throw ("Defender 实时保护未确认关闭（实时保护=" + $status.RealTimeProtectionEnabled +
          "，篡改保护=" + $status.IsTamperProtected +
          "，运行模式=" + $status.AMRunningMode + "）。请先关闭篡改保护后重试。")
  }
  Write-Output 'Windows Defender 实时保护已关闭。'
  ```

  边界：`RealTimeProtectionEnabled` 为 `$null` 时按未确认关闭处理（失败），避免假成功。
  日志点：判定前一条 Info 输出完整状态快照，判定后一条 Info 输出结论。无测试：改动是 XML 内嵌 PowerShell 的判定条件，无测试宿主，也不涉及 Core 代码；由 2.1–2.4 验证。
- [x] 1.2 三份配置中，把文本锚点 `Write-Output '提示：DisableAntiSpyware 未写入实时注册表；Win10 1903+ 可能忽略此策略，但本地组策略已写入。'` 整行替换为：
  `Write-Output '提示：DisableAntiSpyware 未写入实时注册表；Win10 1903+ 客户端版忽略此策略，属正常，不影响关闭实时保护。'`
- [x] 1.3 不改：LGPO 文本内容、`gpupdate /force`、策略回读、`Set-MpPreference`、任务名与 Command id、C# 代码、`使用说明.md`。

## 2. 验证

- [x] 2.1 `dotnet build IPCDeployment.sln -c Debug` 成功。
- [x] 2.2 `dotnet test IPCDeployment.sln -c Debug` 全部通过（`ConfigLoaderTests` 遍历 `config/*.xml` 加载，可证明三份配置合法）。
- [x] 2.3 三份片段一致性比对：

  ```powershell
  foreach ($f in 'config\卷绕工位.xml','config\模切工位.xml','config\下料工位.xml') {
      $t = Get-Content $f -Raw -Encoding UTF8
      $m = [regex]::Match($t, '(?s)\$status = Get-MpComputerStatus.*?Windows Defender 实时保护已关闭。')
      "{0} len={1} sha={2}" -f $f, $m.Length, (Get-FileHash -InputStream ([IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($m.Value))) -Algorithm SHA256).Hash
  }
  ```

  期望：三行 `len` 与 `sha` 完全一致，且 `len` 非 0。
- [x] 2.4 静态检查：

  ```powershell
  Select-String -Path config\*.xml -Pattern '-or \$status\.AntivirusEnabled'
  Select-String -Path config\*.xml -Pattern 'if \(\$status\.RealTimeProtectionEnabled -ne \$false\)'
  ```

  期望：第一条 0 命中；第二条 3 命中（三份配置各一处）。

## 3. Review

- [x] 3.1 加载 `review-change` 做轻量审查，修复 Critical/Important。
- [x] 3.2 自审：对照 1.1–1.3 逐条核对，确认三份一致、未误改相邻步骤（尤其 CDATA 收尾 `]]>` 与 `</Command>`）。

## 4. 人工验收（用户执行，不由本流程代替）

- [x] 4.1 [人工] 在目标机手动跑一次「关闭 Windows Defender」任务，日志应出现 `Defender 状态：实时保护=False，行为监控=…，IOAV=…，按访问保护=…，AntivirusEnabled=True，运行模式=…，篡改保护=…` 与 `Windows Defender 实时保护已关闭。`，任务状态为成功。
- [x] 4.2 [人工] 重启后复查 `(Get-MpComputerStatus).RealTimeProtectionEnabled` 仍为 `False`。

## 5. Commit

- [x] 5.1 [人工] 人工验收通过并经用户明确许可后提交（只 commit 不 push）。
