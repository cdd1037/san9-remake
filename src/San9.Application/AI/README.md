# AI 工程边界

2026-10-16退出自主增强策略后，OAI-01已接入原版调度与经营。普通电脑、玩家委任和异民族运行对应工作表中的0～5／61／63；其余主动策略待后续阶段，生产没有旧策略回退或切换开关。

- `AutomaticTurnRunner`校验身份／稳定边界后调用`OriginalAiTurnRunner`，后者按直属军团准备策略、有序选择工作、处理公共提交反馈，再结束控制。
- `GameSession`继续负责事务、记录、命令水位、玩家暂停及连续／分步推进。旧缺粮目标、战役承诺、机会冷却、共同分配预算与诊断已删除，不为空缺的原版策略预建状态。
- `AutomaticResponseRunner`继续处理Core产生的电脑待决，并经公共命令提交；玩家仍等待输入。当前选择策略仅保证既有响应可运行，尚未逐项对齐原作，不属于原版AI验收通过。
- `AiPlanning`保留独立确定性分析随机和脚本预算。`DomesticObservation`、`DomesticPlanner`、`DomesticCommandSelection`仍有玩家脚本／Headless消费者，不由自动结束控制调用；训练选人辅助已收归该脚本。
- `OriginalDomesticWork`与`OriginalAiEconomy`分别实现0～5和61／63；`OriginalAiProjection`提供直属账户、到下一季度／七月的原版保留需求、当前能力与获准拓扑。`OriginalAiStrategy`计算模式、目标、分组及独立排名快照，不读取外国账户、任务载荷或单位命令。
- `OriginalAiState`由会话事务持有并保存；实际参数来自Content，Importer只转换源人物偏好及初始错峰标识。工作尝试与自然候选有限，没有旧搜索预算截断；`LastOriginalAiWork`区分资格、日历、无候选、准备／概率失败、空计划、公共拒绝和实际提交，成本诊断不进权威状态。
- Core持有公共资格、成本、效果、任务和每日部队响应；AI不得直接写世界。整编仍由公共入口拒绝。

保存112、日志116、录像24及规则`san9-core-g10-v118`保存原版经营策略状态，明确拒绝旧格式。Headless的`--strategy autonomous`运行同一生产入口，报告`proactiveAi: original-economy-v1`及每旬／各工作成本；不代表完整原版AI能力验收。

后续按[原版AI实施计划](../../../specs/original-ai/implementation-plan.md)接入人事组织、军事、计略外交及响应；不恢复旧共同评分器，不为挑战场景和区域模式增加空执行器。
