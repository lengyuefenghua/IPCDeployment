@echo off
setlocal EnableExtensions
echo IPCDeployment sample CMD
echo Step 1/3
ping -n 2 127.0.0.1 >nul
echo Step 2/3
ping -n 2 127.0.0.1 >nul
echo Step 3/3
echo Sample CMD finished.
exit /b 0
