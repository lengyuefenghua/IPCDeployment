# 任务清单

- [x] 1.1 删除 `SampleCommand` 项目与 `data/commands/SampleCommand.exe`。
- [x] 1.2 `src/VisionIPC.Deployment.App` → `src/IPCDeployment`，Core 源码并入 `src/IPCDeployment/Core`。
- [x] 1.3 测试迁到根目录 `tests/IPCDeployment.Tests`。
- [x] 1.4 解决方案改为 `IPCDeployment.sln`，只含两个项目。
- [x] 2.1 全局替换命名空间、程序集名、临时目录名和环境变量前缀为 `IPCDeployment` / `IPCDEPLOYMENT_`。
- [x] 2.2 窗口标题改为“工控机部署工具”。
- [x] 2.3 配置示例改用 `sample.cmd` / `sample.ps1` / 内嵌脚本。
- [x] 3.1 更新 `README.md`、`AGENTS.md`、`scripts/*.ps1` 的路径与项目说明。
- [x] 3.2 仓库目录改为 `C:\Users\wtg\Desktop\agent\IPCDeployment`。
- [x] 4.1 干净重建：`dotnet build IPCDeployment.sln -c Release`。
- [x] 4.2 `dotnet test IPCDeployment.sln -c Release` 全量通过（70）。
- [x] 4.3 `scripts/build-debug.ps1` 与 `scripts/package-portable.ps1` 生成 `build/debug`、`dist/IPCDeployment.zip`。
- [x] 5.1 [人工] 用户指示推送远程，视为界面验收通过；启动 `build/debug/IPCDeployment.exe` 可随时复核标题、分组导航与示例脚本执行。
