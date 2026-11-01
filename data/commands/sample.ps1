Write-Output "VisionIPC sample PowerShell"
for ($index = 1; $index -le 3; $index++) {
    Write-Output "Step $index/3"
    Start-Sleep -Milliseconds 350
}
Write-Output "Sample PowerShell finished."
exit 0
