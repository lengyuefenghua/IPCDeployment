# Configuration Editor Specification

## Purpose
定义在程序内可视化编辑配置文件、保存前校验、往返保真与失败保护的可观察行为。

## Requirements

### Requirement: 进入编辑器并保存生效
编辑器 MUST 作为独立窗口从主窗口打开，支持增删改分组、任务与步骤；保存成功后主界面 MUST 立即显示新配置。

#### Scenario: 修改分组名后保存
- When 用户从主窗口打开编辑器、修改一个分组名并保存, the system SHALL 重载配置并在主界面显示新名称。

#### Scenario: 在编辑器里切换配置后保存
- When 用户用编辑器工具栏的配置下拉切到另一份配置并保存, the system SHALL 让主界面切到刚保存的那份配置。

#### Scenario: 新增节点自动生成标识
- When 用户新增分组、任务或步骤, the system SHALL 自动生成未被占用的键或 id，并以只读方式展示；名称不参与标识生成。

### Requirement: 往返保真
保存 MUST 就地修改原有文档，保留字段值、节点顺序与手写注释；保存结果 MUST 能通过配置加载器。

#### Scenario: 九类步骤与分组排序
- When 用户在一个任务下新增九类步骤各一个、填写必填项并上移一个分组后保存, the system SHALL 在重新打开后保持字段值、节点顺序与原有注释不变。

### Requirement: 保存前校验与字段级错误
保存 MUST 先做全文档字段级轻校验（只校验按可见条件显示的字段），未通过时 MUST NOT 写入文件并列出可定位到字段的错误；写盘前 MUST 再经配置加载器校验，未通过时 MUST 保留编辑内容。

#### Scenario: 非法源路径
- When 用户把源路径填成 `C:\x.exe` 后保存, the system SHALL 不写文件、保留编辑内容，并列出可定位到该步骤该字段的错误。

#### Scenario: 点击问题条目
- When 用户点击问题面板中的一条错误, the system SHALL 选中对应节点并滚动到出错字段。

### Requirement: 字段输入助手
源路径类字段 MUST 提供 `data/` 下现有文件或目录的候选下拉，且仍允许自由输入；候选值 MUST 满足路径策略的前缀与扩展名规则。

#### Scenario: 选择已存在的安装包
- When 用户在安装包字段展开候选, the system SHALL 只列出 `data/software` 下的 .exe 与 .msi。

### Requirement: 只打开合法配置
打开文件 MUST 先经配置加载器校验；不合法时 MUST NOT 进入编辑，并 MUST 显示带行列的错误。编辑器 MUST 用配置下拉切换 `config/` 直属的非示例 XML，其他路径 MUST 拒绝并提示。

#### Scenario: schemaVersion 不是 2.0
- When 用户打开 schemaVersion 不是 2.0 的文件, the system SHALL 拒绝进入编辑并显示行列错误。

#### Scenario: 配置下拉里没有示例文件
- When 用户展开编辑器的配置下拉, the system SHALL 只列出 `config/` 直属 XML，且不包含 `*.sample.xml`。

### Requirement: 外部修改检测
保存前 MUST 比较打开时记录的时间与长度；不一致时 MUST 提示「文件已被外部修改」，并允许覆盖或取消。

#### Scenario: 文件被外部改动后保存
- When 编辑器打开后文件被其他程序改写、用户点保存, the system SHALL 提示外部修改并提供覆盖与取消两项选择。

### Requirement: 失败不留半写文件
写入 MUST 先落到同目录临时文件并校验通过后才替换目标文件；失败时 MUST 保留原文件与备份副本。

#### Scenario: 目标文件被占用
- When 保存时目标文件被其他进程占用, the system SHALL 保留原文件、提示写入失败原因，且不产生半写文件。

### Requirement: 未保存修改的保护
存在未保存修改时，关闭窗口、新建或打开其他文件 MUST 提示保存、不保存、取消三项选择。

#### Scenario: 关闭窗口
- When 存在未保存修改时关闭编辑器, the system SHALL 提供保存、不保存、取消三项选择。
