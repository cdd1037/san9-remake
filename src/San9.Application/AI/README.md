# AI 工程边界

本目录归属 `San9.Application.AI`，集中保存规划协议与现有有限自动策略。经营观察和自动调度基础已接入，不表示 G11 自主 AI 已完成。

- `AiPlanning.cs` 定义观察、分析上下文、预算、候选及诊断协议。目前观察仅包含势力、日期和分析种子，尚无完整局势输入。
- `DomesticObservation` 从有效世界提取获准据点、实际账户和空闲人物，使用 Core 的 `CorpsControl.CanControlSettlement` 与终验共用权限判断。观察不包含完整世界、敌方状态、任务载荷或正式随机。
- `DomesticPlanner.Plan` 只消费不可变经营观察与工作预算，返回无命令身份的经营意图、已消费工作量及是否预算耗尽。现阶段仍是有限经营策略，非统一资源分配或完整经济预测。
- `DomesticCommandSelection` 在会话侧绑定命令身份与当前序号，经公共验证选择；`AutomaticTurnRunner` 统一自动工作与结束控制，电脑根军团仅由势力批次处理，玩家委任军团仍按独立权限执行。
- `GameSession` 拥有事务发布；Headless 只为玩家席位生成脚本命令，电脑席位交给同一会话调度。`MilitaryPlanner`、`DiplomacyPlanner`、`StrategyPlanner` 和有限登用仍消费快照或引擎查询，完整观察隔离尚未完成。
- 整编指令全局禁用，现有及后续策略不得生成、评分或模拟 `ReformUnitCommand` 候选；公共命令入口返回 `UnsupportedCommand`。
- Core 拥有命令资格、成本、效果、领域状态和确定性推进。AI 可以复用通用只读规则计算，但不得直接写领域状态；Core 不依赖本模块。
- 未迁移阶段策略通过`AiPlanningWorld`从完整快照提取参与对象；经营使用独立观察。休眠对象及已毁据点不能占用候选预算。派生观察不进入保存或替代Core资格终验。
- 后续策略记忆由 Application 会话持有，规划器通过显式输入输出参与更新；本次不预建记忆、搜索或训练接口，不新增工程和兼容转发类型。

实施范围、系统设计、阶段及验收统一见[游戏 AI 系统设计](../../../specs/ai-system-design.md)。本目录只说明当前代码职责，不另行维护接入计划。
