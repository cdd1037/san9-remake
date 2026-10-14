# AI 工程边界

本目录归属 `San9.Application.AI`，集中保存唯一生产调度、观察、规划与响应。AI-01～AI-05 已完成；AI-06 的生产调度与评测入口已接入，但仍在验收中，不能标记完成。长期运行与最终强度分别归 G12 和 AI-07～AI-10。

- `AiPlanning.cs` 定义观察、分析上下文、预算、候选及诊断协议。目前观察仅包含势力、日期和分析种子，尚无完整局势输入。
- `DomesticObservation` 从有效世界提取获准据点、实际账户和空闲人物，使用 Core 的 `CorpsControl.CanControlSettlement` 与终验共用权限判断。观察不包含完整世界、敌方状态、任务载荷或正式随机。
- `DomesticPlanner.Plan` 只消费不可变经营观察与工作预算，返回无命令身份的经营意图、已消费工作量及是否预算耗尽。玩家脚本仍使用该入口；自动经营在共享分配中使用经济观察与粮耗时间线。
- `DomesticCommandSelection` 为玩家脚本／Headless 工具绑定命令身份与当前序号，经公共验证选择。自动经营由 `EconomicObservation`、`EconomicPlanner` 和 `AllocationTurnRunner` 负责，优先补足实际账户供养缺口；`AutomaticTurnRunner` 统一自动工作与结束控制，电脑根军团仅由势力批次处理，玩家委任军团仍按独立权限执行。
- `GameSession` 拥有事务发布；Headless 默认只为玩家席位生成经营脚本，自主评测通过 `playerForceController: Automatic` 在创建会话时指定席位控制类型，随后全部使用同一生产调度。`MilitaryPlanner`、`DiplomacyPlanner`、`StrategyPlanner` 与 `AiPlanningWorld` 已移入测试工程的 `LegacyScripts`，仅保存历史命令接线证据，不随产品发布；未使用的 `PersonnelAllocation` 已删除。生产军事使用不可变 `CampaignObservation` 和纯 `CampaignPlanner`；生产机会由 `OpportunityObservationCapture` 投影，纯 `OpportunityPlanner` 比较军师意见可靠性、公开需求和成本；`PersonnelConfiguration` 投影独立的咨询、褒美及长期官职／宝物配置候选。
- `AutomaticResponseRunner` 接入被俘君主处置和处决继任，读取 Core 的待决身份、势力所有者与合法提示，以独立有界求值和命名分析随机选择，经公共命令续行；玩家仍等待输入，未知或无进展电脑响应明确失败并回滚会话事务。AI-05 扩展至求仕、普通搜索、外交报价与接受、联盟、拥立、国号及单挑／狙击继任，支持的日任务按类型选择合法参数；资格例外仍由 Core 决定。
- 整编指令全局禁用，现有及后续策略不得生成、评分或模拟 `ReformUnitCommand` 候选；公共命令入口返回 `UnsupportedCommand`。
- Core 拥有命令资格、成本、效果、领域状态和确定性推进。AI 可以复用通用只读规则计算，但不得直接写领域状态；Core 不依赖本模块。
- 生产策略按各领域独立观察投影参与对象。休眠对象及已毁据点不能占用候选预算。派生观察不进入保存或替代 Core 资格终验。
- `AiSessionState` 由 Application 会话持有，保存策略配置、跨旬缺粮目标、战役阶段／承诺和机会尝试／短期冷却，与世界一起发布或回滚；规划器通过显式输入输出参与更新。账户预测和购粮报价在查询前扣除逻辑工作额度，部分观察保留未观察账户的记忆，完成购粮根据实际接受的效果清理目标。基础权限投影及公共查询内部扫描不属于搜索节点额度，不宣称全世界逐对象处理已受该额度限制。

- `CampaignObservationCapture` 投影公开军事事实、可控制行动及共享到达预测；`CampaignCombatForecast` 用普通战斗公式作有界估算，`CampaignPlanner` 比较准备、会师、供养和净价值，维护巩固、退出及条件冷却。完整当前行为与限制见权威设计的 AI-04 条目。

实施范围、系统设计、阶段及验收统一见[游戏 AI 文档](../../../specs/ai/README.md)。本目录只说明当前代码职责，不另行维护接入计划。

- `MilitarySituation` 用公开兵力和最快地形形成稀疏的最早到达威胁假设；忽略障碍的下界不能证明本方可达。`SupportObservation` 生成公开威胁、自己在途的到达与粮耗投影，计入调将准备和离队后的源城防御；`SharedAllocator` 只消费不可变经济观察和候选，分配当前账户、人物与供养余量。`AllocationObservations` 保留整批候选并在成功命令后更新本方资源及承诺；`GarrisonAssessment` 按当前驻军重算冻结到达波次，不重复寻路，不把未来物资当作现款。
- `AllocationWork` 在全部自动控制范围内共用；路线复用 Core 显式 `NavigationArrivalInput`，路线成本键覆盖地图、占地、移动模式、占用、起终点及抵达范围；途经点拆成路线段，准备期、速度和余量只参与时间换算，缓存命中仍计查询工作。额度约束候选查询与导航扩展，基础权限投影及公共查询内部扫描不属于搜索额度。
- `AllocationDiagnostics` 在外层事务成功后累计逐势力工作、完整性、额度耗尽和命令接受／拒绝原因；详细批次与执行诊断仅保留最近 128 条，累计指标不因明细淘汰丢失。运输可对照实际到达、送达兵力和账户耗粮；不参与分配、保存或会话摘要。有限威胁假设与阶段证据见权威设计的 AI-03 条目。
