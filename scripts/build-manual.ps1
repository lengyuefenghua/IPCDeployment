[CmdletBinding()]
param(
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'IPCDeployment-使用手册.html'

if (-not (Test-Path -LiteralPath $source)) {
    throw ('Manual source was not found: ' + $source)
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $root 'build\manual\IPCDeployment-使用手册.pdf'
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

# PDF 由本机 Edge/Chrome 无头打印生成；不引入额外的 PDF 依赖。
$edge = Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'
$edgeX86 = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'
$chrome = Join-Path $env:ProgramFiles 'Google\Chrome\Application\chrome.exe'
$chromeX86 = Join-Path ${env:ProgramFiles(x86)} 'Google\Chrome\Application\chrome.exe'
$browser = @($edge, $edgeX86, $chrome, $chromeX86) |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $browser) {
    throw 'Microsoft Edge or Google Chrome was not found; it is required to build IPCDeployment-使用手册.pdf.'
}

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Force
}

$profileDirectory = Join-Path $env:TEMP ('ipcdeployment-manual-' + [Guid]::NewGuid().ToString('N'))
$sourceUri = ([System.Uri]$source).AbsoluteUri
try {
    & $browser '--headless=new' '--disable-gpu' '--no-pdf-header-footer' `
        ('--user-data-dir=' + $profileDirectory) `
        ('--print-to-pdf=' + $OutputPath) $sourceUri | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw ('PDF generation failed with exit code ' + $LASTEXITCODE + '.')
    }
} finally {
    if (Test-Path -LiteralPath $profileDirectory) {
        Remove-Item -LiteralPath $profileDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not (Test-Path -LiteralPath $OutputPath)) {
    throw ('PDF was not generated: ' + $OutputPath)
}
$pdf = Get-Item -LiteralPath $OutputPath
if ($pdf.Length -le 0) {
    throw ('PDF is empty: ' + $OutputPath)
}
$header = [System.IO.File]::ReadAllBytes($OutputPath)[0..3]
if ([System.Text.Encoding]::ASCII.GetString($header) -ne '%PDF') {
    throw ('Generated file is not a PDF: ' + $OutputPath)
}

Write-Output $OutputPath
