[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'dist\IPCDeployment'
$zip = Join-Path $root 'dist\IPCDeployment.zip'
$build = Join-Path $root ("src\IPCDeployment\bin\{0}\net472" -f $Configuration)
$dataSource = Join-Path $root 'data'

if (-not (Test-Path -LiteralPath $build)) {
    throw ('Build output was not found: ' + $build + '. Run dotnet build IPCDeployment.sln --configuration ' + $Configuration + '.')
}
if (-not (Test-Path -LiteralPath $dataSource)) {
    throw ('Data directory was not found: ' + $dataSource)
}

if (Test-Path -LiteralPath $publish) {
    Remove-Item -LiteralPath $publish -Recurse -Force
}
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}

New-Item -ItemType Directory -Path $publish -Force | Out-Null
Copy-Item -Path (Join-Path $build '*') -Destination $publish -Recurse -Force

$dataTarget = Join-Path $publish 'data'
New-Item -ItemType Directory -Path $dataTarget -Force | Out-Null
Get-ChildItem -LiteralPath $dataSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $dataTarget -Recurse -Force
}

Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $publish 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'config.xml') -Destination (Join-Path $publish 'config.xml') -Force
Copy-Item -LiteralPath (Join-Path $root 'config.sample.xml') -Destination (Join-Path $publish 'config.sample.xml') -Force

if (-not (Get-ChildItem -LiteralPath $dataTarget -Force | Select-Object -First 1)) {
    throw 'The packaged data directory is empty.'
}

Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Output ('Created portable package: ' + $zip)
