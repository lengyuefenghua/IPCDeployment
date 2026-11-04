# Tasks

> patch 级变更：把 6 个托管依赖 DLL 内嵌进 `IPCDeployment.exe`，运行期用 `AssemblyResolve` 从资源流加载；不引入新依赖。
> 已确认口径：只内嵌 DLL；Release 与打包产物不含 pdb；新增资源清单与加载器测试；`config/`、`data/`、文档仍留在 exe 外。
> 约束：内嵌后测试项目输出不再包含 Wpf.Ui，后续任何触碰 WPF-UI 类型的测试都会加载失败，不要新增这类测试。

## 1. 构建期嵌入

- [x] 1.1 `src/IPCDeployment/IPCDeployment.csproj` 增加目标 `EmbedManagedDependencies`：
  - `BeforeTargets="AssignTargetPaths"` + `DependsOnTargets="ResolveReferences"`（必须早于 AssignTargetPaths，否则资源项拿不到 Type/WithCulture 元数据，csc 不会编译进去）把 `@(ReferenceCopyLocalPaths)` 中 `'%(Extension)' == '.dll'` 的项加入 `EmbeddedResource`（`ReferenceCopyLocalPaths` 不保证带 `Culture` 元数据，实测会报 MSB4096，故只按扩展名过滤），`LogicalName` 为 `IPCDeployment.Resources.%(Filename)%(Extension)`；
  - `BeforeTargets="_CopyFilesMarkedCopyLocal"` 把这些项从 `ReferenceCopyLocalPaths` 移除，阻止复制到输出目录。
  不按 `Private` 元数据过滤（该元数据不一定存在，会导致静默不嵌）。
- [x] 1.2 同一 csproj 的 Release 配置设 `DebugType=None`（Release 不产出 pdb，Debug 保留）。

## 2. 运行期解析

- [x] 2.1 新增 `src/IPCDeployment/EmbeddedAssemblyLoader.cs`（public static，WPF 无关）：
  - `Load(string simpleName)`：先从 `AppDomain.CurrentDomain.GetAssemblies()` 找同名已加载程序集（避免同一 DLL 出现多副本导致主题/样式失效），未命中再从 `typeof(EmbeddedAssemblyLoader).Assembly` 的清单资源（不要用 typeof(App)，会拖入 WPF 程序集） `IPCDeployment.Resources.<simpleName>.dll` 读取并 `Assembly.Load(bytes)`；结果进 `ConcurrentDictionary` 缓存；资源不存在返回 `null`。
  - `Attach()`：注册 `AppDomain.CurrentDomain.AssemblyResolve`；`args.Name` 为 null 或简单名解析失败时返回 null。
- [x] 2.2 `src/IPCDeployment/App.xaml.cs` 增加静态构造函数调用 `EmbeddedAssemblyLoader.Attach()`。静态构造函数体内不得引用任何 `Wpf.Ui` 类型。
- [x] 2.3 在 `Attach` 调用处加中文注释：必须在任何 `Wpf.Ui` 类型被 JIT/使用之前完成注册，否则启动即抛 `FileNotFoundException`。
  日志点：无日志点。解析失败由现有 `DispatcherUnhandledException` 写入 `%TEMP%\IPCDeployment-startup-crash.log`；资源缺失属构建缺陷，不适合运行期刷日志。

## 3. 打包

- [x] 3.1 `scripts/package-portable.ps1` 复制构建输出后删除 `$publish` **顶层**的 `*.pdb` 与 `*.dll`（不递归，保留 `data/files/MySqlConnector.dll`），兜住历史构建残留。

## 4. 测试

- [x] 4.1 新增 `tests/IPCDeployment.Tests/EmbeddedAssemblyTests.cs`：
  - `Embedded_resources_cover_managed_dependencies`：取 `typeof(IPCDeployment.EmbeddedAssemblyLoader).Assembly` 的清单资源名，断言含 `IPCDeployment.Resources.Wpf.Ui.dll`、`Wpf.Ui.Abstractions.dll`、`System.Memory.dll`、`System.Buffers.dll`、`System.Numerics.Vectors.dll`、`System.Runtime.CompilerServices.Unsafe.dll`。
  - `Loader_loads_embedded_dependency`：调用 `EmbeddedAssemblyLoader.Load("Wpf.Ui")`，断言非空且 `AssemblyName.Name == "Wpf.Ui"`。
  后果维度：构建逻辑漏嵌或查找键不一致时，现场启动直接崩溃；两条断言分别覆盖“嵌了”和“能按名加载”。

## 5. 验证

- [x] 5.1 `dotnet clean IPCDeployment.sln -c Debug` 后 `dotnet build IPCDeployment.sln -c Debug`：成功；`src\IPCDeployment\bin\Debug\net472` 顶层无那 6 个 DLL，`IPCDeployment.exe` 约 7 MB。
- [x] 5.2 `dotnet test IPCDeployment.sln -c Debug`：全部通过（含 4.1 两条新测试）。
- [x] 5.3 资源清单抽查（不启动程序）：
  ```powershell
  $asm = [Reflection.Assembly]::LoadFrom('src\IPCDeployment\bin\Debug\net472\IPCDeployment.exe')
  $asm.GetManifestResourceNames() | Where-Object { $_ -like 'IPCDeployment.Resources.*' }
  ```
  期望输出 6 个资源名。
- [x] 5.4 `dotnet clean IPCDeployment.sln -c Release` 后 `dotnet build IPCDeployment.sln -c Release --no-restore` 与 `scripts/package-portable.ps1`：
  - `Get-ChildItem dist\IPCDeployment -File -Filter *.dll` 与 `-Filter *.pdb` 均为空（只看顶层，不递归）；
  - `dist\IPCDeployment\data\files\MySqlConnector.dll` 仍存在；
  - `src\IPCDeployment\bin\Release\net472` 顶层同样无 dll/pdb；`dist\IPCDeployment.zip` 重新生成且顶层无 dll/pdb（用 `[IO.Compression.ZipFile]` 列条目核对）。

## 6. Review

- [x] 6.1 加载 `review-change` 做轻量审查，修复 Critical/Important。
- [x] 6.2 自审：对照 1–5 逐条核对；同步把 `使用说明.md` 中“Wpf.Ui.dll 及其他依赖”的描述改为“依赖已内嵌于 exe”。

## 7. 人工验收（用户执行）

- [x] 7.1 [人工] 先跑 `scripts/build-debug.ps1` 刷新 `build\debug`（旧目录仍是内嵌前的产物），再启动 `build\debug\IPCDeployment.exe`（或 `dist\IPCDeployment\IPCDeployment.exe`），确认窗口、主题、图标、配置加载、预览功能正常。失败表现为启动即崩，细节见 `%TEMP%\IPCDeployment-startup-crash.log`。

## 8. Commit

- [x] 8.1 [人工] 人工验收通过并经用户明确许可后提交（只 commit 不 push）。
