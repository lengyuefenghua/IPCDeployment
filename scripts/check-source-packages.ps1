[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$workspace = Split-Path -Parent $root
$dataDirectory = Join-Path $root 'data\software'

# 标准配置实际安装的自研软件。源仓库与安装包命名保持一一对应。
$components = @(
    @{ Name = 'ImageDataViewer'; Repo = 'ImageDataViewer'; Prefix = 'ImageDataViewer' },
    @{ Name = 'JinovisionLogAnalyzer'; Repo = 'jinovison-logs-parser'; Prefix = 'JinovisionLogAnalyzer' },
    @{ Name = 'JinoMonitor'; Repo = 'JinoMonitor'; Prefix = 'JinoMonitor' }
)

$configFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $root 'config') -Filter '*.xml' -File -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath (Join-Path $root 'config-lithium') -Filter '*.xml' -File -ErrorAction SilentlyContinue
)

function Get-LatestSourcePackage {
    param([string]$Repo, [string]$Prefix)

    $dist = Join-Path $workspace (Join-Path $Repo 'dist')
    if (-not (Test-Path -LiteralPath $dist)) {
        Write-Warning ("源仓库不存在，跳过：" + $dist)
        return $null
    }

    $result = $null
    foreach ($file in Get-ChildItem -LiteralPath $dist -File -Filter ($Prefix + '-*-win-x64-setup.exe') -ErrorAction SilentlyContinue) {
        if ($file.Name -notmatch ('^' + [regex]::Escape($Prefix) + '-(\d+\.\d+\.\d+)-win-x64-setup\.exe$')) {
            continue
        }
        $version = [Version]$Matches[1]
        if ($null -eq $result -or $version -gt $result.Version) {
            $result = [pscustomobject]@{
                Version = $version
                Path = $file.FullName
                Name = $file.Name
            }
        }
    }

    if ($null -eq $result) {
        Write-Warning ("源仓库没有可识别的安装包：" + $dist)
    }
    return $result
}

function Get-ConfigVersions {
    param([string]$Prefix)

    $versions = @()
    foreach ($file in $configFiles) {
        $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
        foreach ($match in [regex]::Matches($text, 'software/' + [regex]::Escape($Prefix) + '-(\d+\.\d+\.\d+)-win-x64-setup\.exe')) {
            $versions += [Version]$match.Groups[1].Value
        }
    }
    return $versions | Sort-Object -Unique
}

function Update-ConfigReferences {
    param([string]$Prefix, [Version]$OldVersion, [Version]$NewVersion)

    $oldText = 'software/' + $Prefix + '-' + $OldVersion.ToString() + '-win-x64-setup.exe'
    $newText = 'software/' + $Prefix + '-' + $NewVersion.ToString() + '-win-x64-setup.exe'
    $updated = @()
    foreach ($file in $configFiles) {
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        if ($hasBom) { $text = $text.TrimStart([char]0xFEFF) }
        if ($text.Contains($oldText)) {
            $text = $text.Replace($oldText, $newText)
            [System.IO.File]::WriteAllText($file.FullName, $text, (New-Object System.Text.UTF8Encoding($hasBom)))
            $updated += $file.Name
        }
    }
    return $updated
}

function Remove-UnreferencedPackages {
    param([string]$Prefix, [string]$KeepName)

    $allText = ($configFiles | ForEach-Object {
        [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8)
    }) -join "`n"

    foreach ($file in Get-ChildItem -LiteralPath $dataDirectory -File -Filter ($Prefix + '-*-win-x64-setup.exe') -ErrorAction SilentlyContinue) {
        if ($file.Name -eq $KeepName) { continue }
        if ($allText.Contains('software/' + $file.Name)) { continue }
        if ($Apply) {
            Remove-Item -LiteralPath $file.FullName -Force
            Write-Output ("  删除旧包：" + $file.Name)
        }
    }
}

function Test-Configs {
    $exe = Join-Path $root ("src\IPCDeployment\bin\{0}\net472\IPCDeployment.exe" -f $Configuration)
    if (-not (Test-Path -LiteralPath $exe)) {
        Write-Warning ("未找到构建产物，跳过配置校验：" + $exe)
        return
    }

    $assembly = [Reflection.Assembly]::LoadFrom($exe)
    $loader = New-Object IPCDeployment.Core.ConfigLoader((New-Object IPCDeployment.Core.PathPolicy))
    $dataRoot = Join-Path $root 'data'
    foreach ($file in $configFiles) {
        $result = $loader.Load($file.FullName, $dataRoot)
        if ($result.Errors.Count -gt 0) {
            $messages = ($result.Errors | ForEach-Object { $_.Message }) -join '; '
            throw ("配置校验失败：" + $file.Name + "：" + $messages)
        }
    }
    Write-Output ("配置文件校验通过：" + $configFiles.Count + " 份")
}

$pending = @()
foreach ($component in $components) {
    $source = Get-LatestSourcePackage -Repo $component.Repo -Prefix $component.Prefix
    if ($null -eq $source) { continue }

    $configVersions = @(Get-ConfigVersions -Prefix $component.Prefix)
    $currentVersion = if ($configVersions.Count -gt 0) { $configVersions[0] } else { $null }
    $currentText = if ($null -eq $currentVersion) { '未配置' } else { $currentVersion.ToString() }

    if ($null -eq $currentVersion -or $source.Version -gt $currentVersion) {
        $pending += [pscustomobject]@{
            Component = $component.Name
            Prefix = $component.Prefix
            Current = $currentText
            Source = $source.Version.ToString()
            SourcePath = $source.Path
            SourceName = $source.Name
            ConfigVersions = $configVersions
        }
        Write-Output ($component.Name + "：当前 " + $currentText + "，源仓库 " + $source.Version + "，需要更新")
    }
    else {
        Write-Output ($component.Name + "：已是最新 " + $currentText)
    }
}

if ($pending.Count -eq 0) {
    if ($Apply) { Test-Configs }
    Write-Output '所有标准配置组件均为最新。'
    exit 0
}

if (-not $Apply) {
    Write-Output ''
    Write-Output '存在待更新组件。执行以下命令同步并校验：'
    Write-Output ('  powershell -ExecutionPolicy Bypass -File ' + $PSCommandPath + ' -Apply')
    exit 1
}

foreach ($item in $pending) {
    Copy-Item -LiteralPath $item.SourcePath -Destination (Join-Path $dataDirectory $item.SourceName) -Force
    Write-Output ("已复制：" + $item.SourceName)

    if ($item.ConfigVersions.Count -eq 0) {
        Write-Warning ($item.Component + " 在配置中没有引用，需要手工新增任务。")
        continue
    }

    foreach ($oldVersion in $item.ConfigVersions) {
        $updatedFiles = Update-ConfigReferences -Prefix $item.Prefix -OldVersion $oldVersion -NewVersion ([Version]$item.Source)
        if ($updatedFiles.Count -gt 0) {
            Write-Output ("  更新配置：" + ($updatedFiles -join ', '))
        }
    }
    Remove-UnreferencedPackages -Prefix $item.Prefix -KeepName $item.SourceName
}

Test-Configs
Write-Output '源仓库安装包同步完成。'
