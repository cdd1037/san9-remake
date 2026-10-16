# AI 工程边界

2026-10-16按用户决定立即退出自主增强策略，原版AI尚未接入。电脑及玩家委任军团暂时不生成经营、人事、外交、计略或军事命令；结束控制、日期推进及已有任务继续运行。生产没有旧策略回退或切换开关。

- `AutomaticTurnRunner`保留结束控制的身份／稳定边界校验和公共响应接线，是后续原版工作调度的接入位置。
- `GameSession`继续负责事务、记录、命令水位、玩家暂停及连续／分步推进。旧缺粮目标、战役承诺、机会冷却、共同分配预算与诊断已删除，不为空缺的原版策略预建状态。
- `AutomaticResponseRunner`继续处理Core产生的电脑待决，并经公共命令提交；玩家仍等待输入。当前选择策略仅保证既有响应可运行，尚未逐项对齐原作，不属于原版AI验收通过。
- `AiPlanning`保留独立确定性分析随机和脚本预算。`DomesticObservation`、`DomesticPlanner`、`DomesticCommandSelection`仍有玩家脚本／Headless消费者，不由自动结束控制调用；训练选人辅助已收归该脚本。
- Core持有公共资格、成本、效果、任务和每日部队响应；AI不得直接写世界。整编仍由公共入口拒绝。

保存111、日志115、录像23及规则`san9-core-g10-v117`不再携带`AiSessionState`／`domains-v1`；旧格式明确拒绝。玩家势力控制类型仍保存和校验。Headless移除`--ai-budget`和旧工作／战役统计，`--strategy autonomous`只验证全自动席位的推进、必要响应与回放，报告`proactiveAi: unavailable`，不代表AI能力验收。

后续按[原版AI实施计划](../../../specs/original-ai/implementation-plan.md)逐阶段实现有序工作、显式战略状态和实际命令反馈；不恢复旧共同评分器，不为挑战场景和区域模式增加空执行器。
