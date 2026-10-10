# AI 工程边界

本目录归属 `San9.Application.AI`，集中保存规划协议与现有有限自动策略。此次归拢只调整工程归属，不表示 G11 自主 AI 已实现。

- `AiPlanning.cs` 定义观察、分析上下文、预算、候选及诊断协议。目前观察仅包含势力、日期和分析种子，尚无完整局势输入。
- `DomesticPlanner`、`MilitaryPlanner`、`DiplomacyPlanner`、`StrategyPlanner` 是现有阶段策略；`AutomaticCorpsPlanner` 组织有限自动工作。
- `GameSession` 拥有调用时机和事务发布，Headless 经同一会话运行。现有策略仍有快照、验证器和引擎入口；G11 再按真实能力逐步替换为受限观察，不把当前入口视为完整隔离已经完成。
- Core 拥有命令资格、成本、效果、领域状态和确定性推进。AI 可以复用通用只读规则计算，但不得直接写领域状态；Core 不依赖本模块。
- 现有阶段策略通过`AiPlanningWorld`从完整快照提取参与对象，再生成和限额筛选候选；休眠人物、势力、据点及已毁据点不能占用候选预算。该临时投影不进入保存或替代Core资格终验，不表示G11完整受限观察已经实现。
- 后续策略记忆由 Application 会话持有，规划器通过显式输入输出参与更新；本次不预建记忆、搜索或训练接口，不新增工程和兼容转发类型。

实施范围、阶段及验收见[电脑 AI 实现规划](../../../specs/ai-implementation-plan.md)。
