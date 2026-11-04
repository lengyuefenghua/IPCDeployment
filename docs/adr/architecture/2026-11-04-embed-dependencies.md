---
Status: accepted
---

# 托管依赖内嵌进 exe

## 背景

应用以 net472 便携包分发，现场只需要拷贝目录即可运行。net472 没有官方的单文件发布，`Wpf.Ui.dll` 等 6 个依赖必须随 exe 一起分发，容易在拷贝时漏文件。目标机不保证联网或安装 .NET 8 运行时，因此不迁移到 .NET 8 单文件发布。

## 决策

- 构建期用 MSBuild 目标 `EmbedManagedDependencies` 把 `ReferenceCopyLocalPaths` 中的托管 DLL 加进 `EmbeddedResource`（`LogicalName` 为 `IPCDeployment.Resources.<文件名>`），并在 `_CopyFilesMarkedCopyLocal` 前从复制列表移除。
- 运行期由 `EmbeddedAssemblyLoader` 注册 `AppDomain.AssemblyResolve`，按简单名从资源流 `Assembly.Load(bytes)`；先查已加载程序集再查缓存，避免同一程序集出现多份副本。
- 不引入 Costura.Fody 等编织器；Release 与打包产物不产出 pdb。
- 用两条测试兜底：清单资源齐全、能按名加载 `Wpf.Ui`。

## 后果

- `dist/IPCDeployment` 只剩 exe + `config/` + `data/` + 文档，少 6 个文件。
- exe 从约 300 KB 涨到约 7 MB；单独替换某个依赖 DLL 不再可行，必须重新编译。
- 内嵌程序集可能触发部分杀软的启发式报警，现场以关闭 Defender 为前提，影响可控。
- 新增依赖若带本地化卫星程序集，需要在嵌入目标里按 `DestinationSubDirectory` 处理，否则会因同名资源编译失败。
