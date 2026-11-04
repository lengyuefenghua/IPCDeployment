[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'build\debug'
$build = Join-Path $root ("src\IPCDeployment\bin\{0}\net472" -f $Configuration)
$dataSource = Join-Path $root 'data'
$configSource = Join-Path $root 'config'

dotnet build (Join-Path $root 'IPCDeployment.sln') -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw ('dotnet build failed with exit code ' + $LASTEXITCODE)
}
if (-not (Test-Path -LiteralPath $build)) {
    throw ('Build output was not found: ' + $build)
}
if (-not (Test-Path -LiteralPath $dataSource)) {
    throw ('Data directory was not found: ' + $dataSource)
}
if (-not (Test-Path -LiteralPath $configSource)) {
    throw ('Config directory was not found: ' + $configSource)
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -Path (Join-Path $build '*') -Destination $output -Recurse -Force

$dataTarget = Join-Path $output 'data'
New-Item -ItemType Directory -Path $dataTarget -Force | Out-Null
Get-ChildItem -LiteralPath $dataSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $dataTarget -Recurse -Force
}

Copy-Item -LiteralPath (Join-Path $root '使用说明.md') -Destination (Join-Path $output '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'XML配置说明.md') -Destination (Join-Path $output 'XML配置说明.md') -Force
Copy-Item -LiteralPath $configSource -Destination $output -Recurse -Force

Write-Output ('Created development build: ' + $output)
