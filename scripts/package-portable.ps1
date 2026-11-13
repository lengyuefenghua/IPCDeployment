[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root 'src\IPCDeployment\IPCDeployment.csproj'
$build = Join-Path $root ("src\IPCDeployment\bin\{0}\net472" -f $Configuration)
$dataSource = Join-Path $root 'data'
$configSource = Join-Path $root 'config'

# 版本号唯一源是 csproj 的 <Version>；发布包只允许从同版本 tag 上生成，保证产物可追溯。
[xml]$project = Get-Content -LiteralPath $projectPath
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
$version = if ($versionNode) { $versionNode.InnerText.Trim() } else { '' }
if (-not $version) {
    throw ('Version was not found in ' + $projectPath)
}

$tagsAtHead = @(& git -C $root tag --points-at HEAD 2>$null)
if ($tagsAtHead.Count -eq 0) {
    throw ('Current commit has no tag. Tag it with ' + $version + ' before packaging.')
}
if ($tagsAtHead -notcontains $version) {
    throw ('Current commit tags (' + ($tagsAtHead -join ', ') + ') do not include csproj version "' + $version + '".')
}

$packageName = 'IPCDeployment-{0}-win-x64' -f $version
$publish = Join-Path $root ('dist\{0}' -f $packageName)
$zip = Join-Path $root ('dist\{0}.zip' -f $packageName)
$lithiumPackageName = 'IPCDeployment-{0}-lithium-config' -f $version
$lithiumPublish = Join-Path $root ('dist\{0}' -f $lithiumPackageName)
$lithiumZip = Join-Path $root ('dist\{0}.zip' -f $lithiumPackageName)

if (-not (Test-Path -LiteralPath $build)) {
    throw ('Build output was not found: ' + $build + '. Run dotnet build IPCDeployment.sln --configuration ' + $Configuration + '.')
}
if (-not (Test-Path -LiteralPath $dataSource)) {
    throw ('Data directory was not found: ' + $dataSource)
}
if (-not (Test-Path -LiteralPath $configSource)) {
    throw ('Config directory was not found: ' + $configSource)
}

# 锂电三工位配置单独发布：仓库放在 config-lithium/，不进标准包。
$lithiumSource = Join-Path $root 'config-lithium'
$lithiumConfigs = @('模切工位.xml', '卷绕工位.xml', '下料工位.xml')
if (-not (Test-Path -LiteralPath $lithiumSource)) {
    throw ('Lithium config directory was not found: ' + $lithiumSource)
}
foreach ($lithiumConfig in $lithiumConfigs) {
    $lithiumPath = Join-Path $lithiumSource $lithiumConfig
    if (-not (Test-Path -LiteralPath $lithiumPath)) {
        throw ('Lithium config was not found: ' + $lithiumPath)
    }
}

if (Test-Path -LiteralPath $publish) {
    Remove-Item -LiteralPath $publish -Recurse -Force
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
if (Test-Path -LiteralPath $lithiumPublish) {
    Remove-Item -LiteralPath $lithiumPublish -Recurse -Force
}
if (Test-Path -LiteralPath $lithiumZip) {
    Remove-Item -LiteralPath $lithiumZip -Force
}

New-Item -ItemType Directory -Path $publish -Force | Out-Null
Copy-Item -Path (Join-Path $build '*') -Destination $publish -Recurse -Force

$dataTarget = Join-Path $publish 'data'
New-Item -ItemType Directory -Path $dataTarget -Force | Out-Null
Get-ChildItem -LiteralPath $dataSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $dataTarget -Recurse -Force
}

Copy-Item -LiteralPath (Join-Path $root '使用说明.md') -Destination (Join-Path $publish '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'XML配置说明.md') -Destination (Join-Path $publish 'XML配置说明.md') -Force

# IPCDeployment-使用手册以 HTML 维护在仓库，发布包只带生成的 PDF。
$manualPdf = & (Join-Path $PSScriptRoot 'build-manual.ps1') | Select-Object -Last 1
if (-not $manualPdf -or -not (Test-Path -LiteralPath $manualPdf)) {
    throw 'IPCDeployment-使用手册 PDF generation failed.'
}
Copy-Item -LiteralPath $manualPdf -Destination (Join-Path $publish 'IPCDeployment-使用手册.pdf') -Force

Copy-Item -LiteralPath $configSource -Destination $publish -Recurse -Force

if (-not (Get-ChildItem -LiteralPath $dataTarget -Force | Select-Object -First 1)) {
    throw 'The packaged data directory is empty.'
}

# 依赖 DLL 已内嵌进 exe；清掉历史构建可能残留的顶层副本，避免现场仍从磁盘加载。
Get-ChildItem -LiteralPath $publish -File -Filter '*.pdb' | Remove-Item -Force
Get-ChildItem -LiteralPath $publish -File -Filter '*.dll' | Remove-Item -Force

Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Output ('Created portable package: ' + $zip)

# 锂电配置包：解压后把 config/*.xml 复制进标准包的 config/ 即可在界面选择。
$lithiumConfigTarget = Join-Path $lithiumPublish 'config'
New-Item -ItemType Directory -Path $lithiumConfigTarget -Force | Out-Null
foreach ($lithiumConfig in $lithiumConfigs) {
    Copy-Item -LiteralPath (Join-Path $lithiumSource $lithiumConfig) -Destination (Join-Path $lithiumConfigTarget $lithiumConfig) -Force
}
Compress-Archive -Path (Join-Path $lithiumPublish '*') -DestinationPath $lithiumZip -CompressionLevel Optimal
Write-Output ('Created lithium config package: ' + $lithiumZip)
