# 项目重组与更名（IPCDeployment）

## 需求卡

- 目标：4 个项目收敛为 2 个；仓库名、解决方案名、程序集名统一为 `IPCDeployment`，中文名“工控机部署工具”。
- 范围：项目结构、命名空间、程序集名、解决方案、目录布局，以及随之更新的配置/脚本/文档。
- 不在范围：任何部署行为、配置 schema、UI 布局与交互。
- 验收：
  1. `src` 只含 `src/IPCDeployment`，测试位于根目录 `tests/IPCDeployment.Tests`。
  2. 解决方案只含两个项目，程序名为 `IPCDeployment.exe`。
  3. 全仓库（除归档历史）不再出现 `VisionIPC`。
  4. `dotnet build` 与 `dotnet test` 通过，`build/debug` 与 `dist/IPCDeployment.zip` 可生成。
  5. `SampleCommand` 项目与 `SampleCommand.exe` 已删除，示例改用 `data/commands` 下的脚本。

## 协作卡

- 主控 inline 执行（结构改动耦合度高，不拆分并行）。
- 用户确认点：目录结构、命名（`IPCDeployment`）、仓库目录改名，已在对话确认。

## 流程契约

- 正常线：重命名目录与项目 → 合并 Core 到 `src/IPCDeployment/Core` → 迁移 tests → 全局替换标识 → 更新配置/脚本/文档 → 构建、测试、打包。
- 异常线：目录被占用时用 robocopy `/MOVE` 迁移内容，再切换会话工作目录；替换产生叠加串（`IPCDeployment.Deployment`）时二次修正。
- 不变量：Core 仍不引用 WPF；步骤类型、执行语义、配置文件结构不变。

## 影响

- 无行为变更，无 spec 增量：`docs/specs/**` 与本次无关。
- 程序集与命名空间改变属于破坏性重命名，但没有外部使用者。
