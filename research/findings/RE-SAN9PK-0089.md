# RE-SAN9PK-0089：交互与自动子任务 dispatcher 子系统覆盖

- 状态：已确认交互/自动两套工厂的有效代码全集，并分别闭合 AI、外交和部队命令子系统的 dispatcher、核心状态根与跨模块边界；未命名分支保持中性候选
- 日期：2026-08-24
- 分析者：Codex
- 结论类型：事实、推断和待验证项分开记录
- 置信度：高（工厂分支、虚表、RTTI、任务/命令类型、执行写回）；中（部分日文类名的简体中文业务译名）
- 关联来源：`SRC-SAN9PK-TC-101`；第三方线索为 `SRC-SAN9PKHARD`
- 前置记录：[RE-SAN9PK-0072](RE-SAN9PK-0072.md) 至 [RE-SAN9PK-0074](RE-SAN9PK-0074.md)、[RE-SAN9PK-0078](RE-SAN9PK-0078.md)、[RE-SAN9PK-0082](RE-SAN9PK-0082.md)、[RE-SAN9PK-0085](RE-SAN9PK-0085.md)

## 调查问题与证据优先级

铺底清单第 3 项要求分别找到 AI、外交、战斗/部队命令子系统的可靠 dispatcher、核心状态根和跨模块边界。本批不把“恢复全部有效工厂分支”误写成“所有玩法细节均已恢复”：先穷举两套工厂的非空分支，再以当前样本 RTTI、任务虚表和最终命令对象裁决可确认子系统；无法唯一命名的分支仍保留 code 与构造地址。

## 事实：两套有效工厂全集

### 交互工厂

`create_interactive_force_action_subtask(0x5125A0)` 接受 code `0..70`。非空集合仍为 `0–19、21–36、38、39、51、52`，共 40 项；空集合为 `20、37、40–50、53–70`，共 31 项。非空返回由 `dispatch_simulation_action_code` 入队并把交互任务阶段置 `1002`，空返回及越界输入不入队、不改阶段。

沿每项任务虚表的对话框/命令创建槽反查 MFC `CRuntimeClass` 后，29 项获得当前样本直接类名证据：

| code | RTTI 对话框 | code | RTTI 对话框 | code | RTTI 对话框 |
| ---: | --- | ---: | --- | ---: | --- |
| 0 | `COrderJunsatsuDlg` | 1 | `COrderShougyouDlg` | 2 | `COrderKaikonDlg` |
| 3 | `COrderKaishuuDlg` | 4 | `COrderChouheiDlg` | 5 | `COrderKunrenDlg` |
| 6 | `COrderKounyuuDlg` | 7 | `COrderBaikyakuDlg` | 13 | `COrderGunshiDlg` |
| 15 | `COrderHoubiDlg` | 16 | `COrderJuyoDlg` | 18 | `COrderShodanDlg` |
| 19 | `COrderTsuihouDlg` | 21 | `COrderYobiyoseDlg` | 22 | `COrderIdouDlg` |
| 23 | `COrderTansakuDlg` | 24 | `COrderTouyouDlg` | 25 | `COrderGihouDlg` |
| 26 | `COrderKakuranDlg` | 27 | `COrderGekireiDlg` | 28 | `COrderKyuuenDlg` |
| 29 | `COrderRikanDlg` | 30 | `COrderYakiuchiDlg` | 31 | `COrderDasshuDlg` |
| 32 | `COrderRyuugenDlg` | 33 | `COrderZouteiDlg` | 34 | `COrderYouseiDlg` |
| 35 | `COrderHenkanDlg` | 36 | `COrderKankokuDlg` |  |  |

这是 code→任务构造器→虚槽→对话框构造器→运行时类描述符的完整静态链，不是按日文罗马字或第三方函数名猜测。code `8、9、10、11、12、14、17` 尚无同等强度的正式业务类名；code `38、51` 的命令类型边界明确，但玩法标签仍保持中性。

### 自动工厂

`create_automatic_force_action_subtask(0x4BD030)` 接受 code `0..63`，当前样本恰有 39 个非空分支：

`0–5、9、10、12–17、22–36、51、55–63`。

完整 code→构造入口已写入 `simulation-action-status.json`。不在集合中的输入返回空。其生产者是 `select_automatic_force_action_work_batch` 选择的静态批表，消费者 `process_automatic_force_action_task_state` 逐项调用工厂；因此自动 code 与交互 `10000+code` 从生产、状态根到最终入队均为独立编号空间。

## 事实：三个子系统边界

| 子系统 | 可靠 dispatcher | 核心状态根 | 跨模块边界与已确认副作用 |
| --- | --- | --- | --- |
| AI | 自动阶段机 `0x4BDE30` → 自动工厂 `0x4BD030`；批表生产由 `0x4BDD00` 闭合 | `automatic_force_action_task` 保存当前势力、阶段、批次游标和子任务；码 9/57/58 各自保存候选与计划 | 码 9 写共享出阵/建设命令，码 57 写共享类型 39 撤退命令，码 58 写共享类型 9 出阵命令；创建部队、扣兵及目标/事件写回见 0078、0082、0084、0085 |
| 外交 | `dispatch_simulation_action_code` → 交互工厂 code 33；同族 34–36 也由 RTTI 分别闭合为 `Yousei/Henkan/Kankoku` 对话框 | `interactive_gift_action_task` 从当前交互势力上下文保存执行势力、目标势力和赠与内容 | `COrderZouteiDlg` 确认后构造命令类型 33；`gift_command_ctor` 安装 `vftable_gift_command`，有效性槽检查双方势力与赠与内容，`execute_gift_command` 完成资源/宝物转移、外交关系、人物成长和消息写回 |
| 部队命令 | `dispatch_simulation_action_code` → 交互工厂 code 38/39/51/52 的共用响应任务族 | 任务基类保存当前 `CUnitData`、四个策略位、目标记录和阶段；选择器按 RTTI 限定部队或 `CChiikiBuildingData` | code 39 选目标建筑并构造共享类型 39 撤退命令；code 52 是同一内部类型 39 的上下文变体，不是类型 52 命令。code 38/51 分别进入单位目标构造器 `0x499AF0` 与建筑目标构造器 `0x48CE80`；自动码 57 与交互 code 39 共用撤退命令执行器 |

这里的“部队命令子系统”比“战斗 AI”更精确：交互 code 38/39/51/52 负责选择目标和构造命令，战斗/移动状态机再执行；不能因其中存在部队对象就把玩家交互层写成 AI 决策器。

## 事实：code 39 与 code 52 的编号冲突裁决

- 工厂 code 39 的构造器原先没有函数边界。本批恢复后明确显示它向共用基类传入内部类型 `39`，安装 `0x609E00`，状态机最终调用 `unit_retreat_command_ctor`。
- 工厂 code 52 的既有构造器 `0x4C45F0` 同样向基类传入 `39`，但安装另一张虚表并使用可兼容不同初始目标上下文的状态机；它最后仍构造相同类型 39 撤退命令。
- 因此“交互 factory code 52 等于命令/事件类型 52”不成立。数字只在各自 dispatcher 内解释；通用响应计划命令工厂支持类型 52，并不证明交互 code 52 生产类型 52。

## 文档与第三方边界

- San9PKHard 只提供少数精确旧地址入口；本批的 29 项类名来自当前样本 `CRuntimeClass`，工厂集合来自当前函数体，未采用补丁函数名反推原版行为。
- 旧记录把 `10000–10070` 逐项长期保留为“仅范围分派”已过时：40 个非空构造映射、29 个 RTTI 对话框及两个已闭合命令族现已登记。仍中性的 code 不获得猜测性中文名。

## 保留的待验证项

- code `8、9、10、11、12、14、17` 的正式业务类名；其中 code 12 的构造边界已恢复，但只命名为中性任务。
- code 38、51 的完整玩家可见语义和最终执行副作用；当前只确认目标类型、构造器边界及其与共用响应任务的关系。
- 29 个 RTTI 已确认分支中，除赠与外的逐项过滤、随机和执行细节可按后续行为规格需要继续下钻；它们不是本清单“每个子系统至少一个可靠闭环”的阻塞项。

## IDA 写入、回读与健康验证

- 写前健康摘要保持 `b7041aff...`：函数 `10978`、命名函数 `1212`、自动名 `9766`、函数注释 `720`、类型 `195`、反编译失败 `7`、可执行段未定义字节 `0`。
- dry-run 见 `interactive-dispatcher-coverage-batch-1.json`。撤销两个构造范围的错误数据定义后，按真实 `ret` 建立 `0x4C12D0..0x4C1377` 与 `0x4C4A20..0x4C4A51`；原始字节哈希写前写后不变。
- 写入 10 个高置信名称：两个新构造器、code 12/code 39/赠与任务虚表、code 39 状态机、赠与任务/命令构造器及赠与命令虚表；并写入 12 条事实/边界注释。主 IDB 保存后名称 `10/10`、边界 `2/2`、注释 `12/12` 回读一致。
- code 12 恢复为真实函数后吸收原先被 IDA 单列为 `___CxxFrameHandler_w_48` 的异常处理尾块，因此函数统计净增 1 而不是 2；新增/移除差分恰为两个构造入口增加、一个 EH 尾块并入所属函数，没有程序字节丢失。
- 代表性反编译改善：code 12 现在显示内部 code 12、`CForceData/CPersonData` 成员和虚表写入；code 39 显示内部类型 39 与阶段 1000；赠与创建槽传播 `gift_command_ctor`，控制流和条件未改变。
- 写后健康基线两轮一致：函数 `10979`、命名函数 `1218`、自动名 `9761`、函数注释 `728`、类型 `195`、反编译失败 `7`、可执行段未定义字节 `0`，摘要 `40b84486bf7682adff1f4062490d9a02dca8c96c995fda90ac092ecc4da4be32`。
- 两轮核心目录一致：运行时类 `400`、虚表候选 `35`、消息映射 `67/183`、全局根 `500`。虚表候选 `31→35` 恰来自本批新命名的四张任务/命令表，无新增冲突。
- 未修改程序字节、产品代码、玩法设计或平衡。
