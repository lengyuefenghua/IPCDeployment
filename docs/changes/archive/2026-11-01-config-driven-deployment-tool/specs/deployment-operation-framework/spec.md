## MODIFIED Requirements

### Requirement: 配置文件与数据目录
程序 MUST 从 exe 同目录加载 config.xml；config.sample.xml MUST 只作为示例，不自动加载；data/ MUST 只存放安装载荷。

#### Scenario: 启动加载
- When 程序启动, the system SHALL 加载 exe 同目录的 config.xml 并执行预检。

#### Scenario: 示例不自动加载
- When config.xml 不存在, the system SHALL 报错并禁止执行，不得改用 config.sample.xml。

### Requirement: 分组、任务与步骤结构
配置 MUST 以 Groups → Group → Task → Step 组织，分组和任务 MUST 按 XML 顺序展示和执行；分组名 MUST 唯一且非空，每个分组至少一个任务，每个任务至少一个步骤。

#### Scenario: 按 XML 顺序
- When 配置包含多个分组和任务, the system SHALL 按 XML 定义顺序显示和执行。

#### Scenario: 九类步骤
- When 任务包含步骤, the system SHALL 支持 Install、Copy、Registry、Command、Service、Network、Power、AutoStart、Delay。

#### Scenario: 受控命令脚本
- When Command 使用 .cmd/.bat/.ps1 或内嵌 Code, the system SHALL 用固定的 cmd.exe/powershell.exe 执行，脚本来源限于 data/commands，内嵌代码使用临时脚本并在结束后删除。

### Requirement: 两级执行进度
全局进度 MUST 按已结束任务数/总任务数计算；任务内进度 MUST 按已结束步骤数/任务步骤总数计算；失败任务 MUST 计入全局进度。

#### Scenario: 任务未结束时全局不推进
- When 任务内步骤仍在运行, the system SHALL 保持全局任务进度不变，只推进任务子进度。

#### Scenario: 失败任务计入全局
- When 任务因步骤失败结束, the system SHALL 将任务计入全局已结束任务并继续下一个任务。

### Requirement: 任务级失败与后续任务
任务内任一 Step 失败时，系统 MUST 立即结束当前任务，将剩余步骤标记为“未执行”，并自动开始下一个任务；后续分组 MUST 不受影响。

#### Scenario: 步骤失败
- When 任一步骤失败, the system SHALL 结束当前任务、保留失败详情，并继续下一个任务。

#### Scenario: 无跳过状态
- When 步骤或任务未运行, the system SHALL 显示“未执行”，不得显示“跳过”或“skipped”。

### Requirement: 完成页执行结果
完成页 MUST 展示每个任务的成功/失败、失败步骤、失败原因、未执行步骤和耗时；完成 MUST 只表示执行进度，不代表成功。

#### Scenario: 失败任务详情
- When 任务失败, the completion page SHALL 显示失败步骤、原因和未执行步骤。

#### Scenario: 全局结果
- When 所有任务结束, the completion page SHALL 显示已结束任务数、成功任务数、失败任务数、已取消数量和未执行任务数。

### Requirement: 配置校验
系统 MUST 拒绝重复分组名、空分组、空任务、旧 schemaVersion 以及未知元素或属性，并在错误中提供行号和列号。

#### Scenario: 重复分组名
- When 两个分组使用同名, the system SHALL 在第二个分组位置报错并禁止执行。

#### Scenario: 旧配置
- When schemaVersion 不是 2.0, the system SHALL 拒绝配置并提示版本不兼容。

### Requirement: 真实执行门
真实执行 MUST 经过预检、操作摘要和二次确认；预览模式 MUST 只校验和展示，不创建进程。

#### Scenario: 预览模式
- When 配置为 preview, the system SHALL 只展示任务概况，不启动任何任务或步骤。

#### Scenario: 未确认执行
- When execute 模式未获得二次确认, the system SHALL 不启动任何任务或步骤。
