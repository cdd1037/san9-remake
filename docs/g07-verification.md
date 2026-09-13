# G07军事规则验收

日期：2026-09-13。当前规则`san9-core-g07-v31`，初始态schema49、组合包schema53、存档／操作日志24。本文保存当前验证入口和边界；历史迭代、临时哈希与逐轮执行日志交给Git及忽略目录，不用旧验收结论替代新规则验证。

## 当前验证

`dotnet test San9Remake.slnx --no-restore --verbosity quiet`通过1258项：Core774、Content35、Importer238、Application104、Client97、Headless10。当前版本已运行完整解集，包含保存／恢复、记录重放、独立Headless及跨层内容契约测试。未启动游戏，未进行画面验收。

正式内容包`local/generated/content/scenario-190-live`已原位重转；复用独立纹理包，组合schema53、剧本schema49。核对28阵型、41兵法、17设施、94据点、700人物、0初始野战部队；远距削墙仅阵型10，逐日熟练排除13／14，水战关联学习21／22／23→12／13／14，工作物毁坏学习源10–15→19、16→20。旧规则存档拒绝加载。转换入口见[内容管线](client-content.md)。

## 行为与验证入口

| 行为 | 当前回归入口 |
| --- | --- |
| 出征、方针、接敌、返城、整编 | UnitPolicyTests、MilitaryMovementTests、UnitReformationTests、MilitarySessionTests、ShoreMilitarySessionTests |
| 普通攻击与异常状态、周期士气、地利、后勤 | OriginalCombatAlignmentTests、CombatRuleCompletionTests、MilitaryCombatTests、MilitarySupplyTests |
| 执行能力公式、智力副效果与武力连锁 | TacticMathTests、IntelligenceMartialChainTests、IntelligenceTacticTests、TacticCombatTests |
| 鼓舞来源对象、治疗与高昂到期 | IntelligenceTacticTests、FacilityCombatTests、WarfareAuditAlignmentTests |
| 近距削墙、发石留1与接近收尾、斜向范围 | WarfareAuditAlignmentTests、AttackRangeTests、MilitaryCombatTests |
| 城市逐次交战损失、信望民心、半份额资源 | WarfareAuditAlignmentTests、BattleCaptureTests、G06CampaignTests |
| 战斗成长、工作物销毁、设施反作用 | TacticCombatTests、WarfareAuditAlignmentTests、FacilityConstructionTests、FacilityReprisalTests |
| 伤兵、俘虏、战败返回及空池安置 | WoundedRecoveryTests、PrisonerEscapeTests、CombatRuleCompletionTests、BattleCaptureTests、Application.PrisonerCycleTests |
| 内容覆盖、查询与持久化 | MilitaryContentCompilerTests、MilitarySessionTests、ConstructionSessionTests、Headless.Tests |

`WarfareAuditAlignmentTests`新增24项边界验证（包含三种失守入口的耐久四分之一下限）；原测试中固化“正常目标攻心／陷阱不连锁”“鼓舞范围加士气”“每击按比例扣士气”的断言已按采用规则重写。测试通过表示上述明确行为与不变量受验收，不表示原作所有战斗分支已复现。

## 2026-09-13 主将选定对齐

出征与整编按身份、官职、当前统率稳定排序；离队继任复用相同优先级并保留成员顺序。对应UnitLeadershipTests及军事会话测试。原作证据保留在[RE-0081](../research/findings/RE-SAN9PK-0081.md)，不因本文删去过程日志而丢弃证据。

## 采用规则与边界

公式、副效果、鼓舞、治疗、高昂到期、成长入口、削墙与移动收尾、城市战损及占领资源的采用行为已并入[普通战斗](../specs/behavior/BEH-WAR-0004-normal-combat.md)、[兵法](../specs/behavior/BEH-WAR-0005-tactics.md)、[占领](../specs/behavior/BEH-WAR-0002-building-defeat-and-capture.md)与[状态](../specs/behavior/BEH-STRATEGY-0002-unit-status-and-reinforcement.md)。研究依据按主题分见[战斗公式](../research/findings/RE-SAN9PK-0068.md)、[连锁](../research/findings/RE-SAN9PK-0125.md)、[成长](../research/findings/RE-SAN9PK-0131.md)、[占领](../research/findings/RE-SAN9PK-0138.md)和[射程](../research/findings/RE-SAN9PK-0172.md)。独立审计过程文档已合并删除。

保留用户确认的同类不同名武力连携、主发动者决定倍击、普通攻防取队内最高有效统率、电脑伤害加成一次、无主将守军不免疫智力兵法、溢出兵员回归兵役人口、空安置池转在野，以及治疗保留高昂但清除后续惩罚。

驻军固定系数原作取值仍待溯源，本轮未凭推测替换。G05移动预算与占地仍按已声明的重制模型；完整人物生命周期／单挑、AI与所有消息文本分类展示留给后续阶段。本轮没有重新对旧190年临时战役录像做跨版本重放，旧规则录像不冒充当前规则证据。
