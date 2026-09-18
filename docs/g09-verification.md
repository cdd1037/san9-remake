# G09 人物生命周期与单挑实施与验证

状态：已完成，2026-09-19。范围与退出条件见[路线图](../specs/roadmap.md)。人才提拔培养是独立支线；事件生产者的接入仍按G10执行，不能将共享成长函数视为事件已经实现。

当前规则`san9-core-g09-v13`，保存／日志54，剧本schema65／组合69。190年运行包已原位重转；旧规则存档／录像明确拒绝。本阶段未启动Godot，交互验收使用公共会话API和独立Headless进程，画面留到表现阶段。

## 生命周期与状态所有权

- `OfficerAppearance`保留全部支持人物的登场年、出生年、血缘及亲代；新局只将到年未登场者转为隐藏，跨年按实时亲代／君主亲属安置。被俘亲代使用原势力，野战亲代使用服务据点，不把孩子加入野战队。来源与重制安置选择见[RE-0133](../research/findings/RE-SAN9PK-0133.md#2026-09-18g09登场分支与数据核对)。
- `OfficerInjuryRecovery`在旬更新中先对非零伤势减1，再恢复行动与褒美等状态；不修改基础能力、不消费随机。`FreeOfficerMigration`经过30日后逐人物执行50%旬迁移，使用有效邻接地域，取消以迁走人物为目标的任务并安排返程。
- `NaturalMortality`按季度执行360日门、寿命曲线与豁免；`OfficerDeath`统一自然死亡、处决、单挑及既有战争死亡的人员、任务、治理和宝物事务。保留人物身份与家谱，死亡清理和宝物结算幂等。自动继任按关系与优先级排序，子军团继任合并原直属引用及资金，按新君主亲和调整忠诚。证据见[RE-0132](../research/findings/RE-SAN9PK-0132.md)、[RE-0141](../research/findings/RE-SAN9PK-0141.md#2026-09-18g09继任规则复核)。
- `ExecutePrisonerCommand`验证据点／部队拘押及军团权限；被俘玩家君主死亡前以`GetExecutionSuccessionPrompt`／`ChooseExecutionSuccessorCommand`选择继任者。不提前死亡、转移宝物或投外交骰；无候选时完成势力退出及终局。
- 季度课程来自必填`TacticCourses`城市／兵法引用。报名经过30日门，先报名、后完成上期90日修行，再执行自然死亡。修行是正式人员任务，直接授予不投普通学习骰；死亡、被俘、改属及课程城市失效通过共享取消处理。证据见[RE-0131季度修行](../research/findings/RE-SAN9PK-0131.md#quarterly-study-enrollment)。

`OfficerAppearanceTests`、`OfficerInjuryRecoveryTests`、`NaturalMortalityTests`、`OfficerDeathTests`、`NaturalDeathLocationTests`以及对应Application测试覆盖新局／跨年、未知年龄／特殊身份／玩家君主／异民族豁免、0与100概率的消费、死亡幂等和恢复。自然死亡通过实际年度推进覆盖驻城、野战主将、野战最后一员、旅行途中、据点俘虏及部队俘虏，验证任务、主将／成员、宝物及保存恢复。处决测试包含无归属、玩家选择、子军团继承、忠诚、玉玺及最终势力退出。

## 成长入口审计

证据依据[RE-0131](../research/findings/RE-SAN9PK-0131.md)完整生产者清单及各命令／战争BEH；成长算术、熟练度和学习是不同结果，不因某个共享函数存在就声称入口覆盖。

| 生产者 | 已采用行为与实际验证 |
| --- | --- |
| 计略 | 八类成功结果分别增长规定能力，类别7／8各+2；失败不奖励。`StrategyDiplomacyTests`通过真实命令覆盖严格3%与100上限 |
| 四类外交 | 接受赠与、返还俘虏、请求攻击及劝降时仅使者政治尝试成长；报价、拒绝不成长。`DiplomacyTimingTests`、`SurrenderDiplomacyTests`覆盖成功／失败／满值、两段回答仅最终接受奖励、恢复事件及哈希一致 |
| 人事与普通探索 | 普通登用成功、探索发现人物或宝物奖励政治；资金与无结果不奖励，发现人物后拒绝登用不撤销发现奖励。`PersonnelMissionTests`、`SearchOutcomeTests`覆盖公共入口、执行人范围、满值与超限 |
| 内政 | 修复全员攻城／谋略类别5／6各+1，训练类别0..4各+1；其他普通内政不凭空增加四维成长。`EconomyTests`覆盖费用、参与人、精通、饱和与失败无副作用 |
| 普通交战／削墙 | 普攻本身没有额外四维成长；有效日交战按水军或所选兵法类别+1，削墙后仍有耐久且来源存活时全员攻城+1。`WarfareAuditAlignmentTests`及`MilitaryCombatTests`区分日交战、削墙、占领／击破入口 |
| 部队击败／据点占领 | 击败部队只奖励胜方主将统率；占领有效他势力据点按人物ID升序候选随机选一人尝试统率，全员攻城+3并按有效阵型技术学习。无主目标不奖励。`MilitaryCombatTests`覆盖真实占领、随机副将、学习、无主目标及恢复 |
| 工作物击破 | 按人物ID升序候选随机一人尝试统率，全员攻城+3并逐人学习设施定义的技术。新增真实攻打场景以主将不在ID首位的场景覆盖受益人、学习消费、满值不重选和稳定恢复 |
| 建设施工 | 按WAR-0006明确采用的ID顺序选择受益人及学习，在规定里程碑增长；暂停／取消不奖励。`FacilityConstructionTests`覆盖多人、满值不重选、精通、饱和及恢复 |
| 主动／被动兵法 | 存活目标先学习，存活来源参与者随机一人尝试能力（类别7／8智力，其余武力），来源全员熟练度+3，再学习；水战读阵型关联技术，陷阱归选择兵法。`TacticCombatTests`、`IntelligenceTacticTests`、`FacilityCombatTests`覆盖实际战斗、双方学习、间隔、反制、成长与失效成员 |
| 季度教学 | 公共报名、90日占用、到期直接授予及取消，Application课程测试覆盖多城市续行、死亡顺序、保存和录像 |
| 单挑强制成长 | 只按每人第一次赢组时的双方有效武力判定，低武力胜者基础武力+1，每人至多一次，不投成长骰。`DuelAftermathTests`覆盖比较、去重、饱和及超限不降值 |

普通能力达到或超过100直接跳过、不消费成长骰，低于100严格3%；抽中满值人物不重新选人，这是2026-09-18用户确认的重制选择。`OfficerGrowthTests`覆盖普通／强制能力、精通三倍、1000熟练度上限、学习30%／70%严格边界和每5点台阶、资格失败无骰及直接授予旁路。

2026-09-19统一人物顺序：删除`UnitState.MemberOrder`，成员由人物`UnitId`查询；主将与阵位独立。普通批处理及均匀随机候选按人物ID升序，有玩法优先级的选任保留规则，最后以ID升序破同分。阵位仍按出征／整编输入绑定，单挑首发及真实交锋顺序保留。没有为列表排序重选初始主将或据点大将的初始化入口。`OfficerOrderingTests`验证候选与集合逆序后事件及哈希一致，既有水战、工作物击破、主将继任及保存测试同步覆盖。详见[统一人物顺序](../specs/behavior/BEH-PERSON-LIFECYCLE-0001-growth-and-learning.md#统一人物顺序2026-09-19用户确认)。

## 单挑与交互边界

`MilitaryCombat`在普通伤害完成、双方未溃灭后调用`DuelCombat`：先抽普通兵法机会骰，独立尝试单挑，再按机会结果与战后实时资格尝试普通兵法。被动装备不阻断单挑；反击方须先通过反击机会门。`DuelParticipants`、`DuelContest`与`DuelStrike`覆盖候选筛选、稳定排序、追加关系及概率、逐刀随机顺序、连续配对HP和特殊人物语义属性。Core不依赖人物或源阵型编号。

`DuelAftermath`先按双方士气写间隔，再按败方名单进行阵亡与俘虏两遍处理，死亡／被俘／伤势互斥，随后结算HP派生士气与一次成长。主将、既往胜组者、异民族、四基础能力80门和逃脱马各按适用阶段排除。战斗阵亡默认关闭；`GameSession.Start(..., battleDeathsEnabled: true)`显式开启，另受内容`BattleDeathsAllowed`约束，俘虏由`DuelCapturesAllowed`独立约束。

玩家君主阵亡通过`GetDuelSuccessionPrompt`／`ChooseDuelSuccessorCommand`等待选择。仅在确需回答时保存内存中的战后游标、剩余单位和已攻击集合；响应后继续同日战斗及余下日阶段，不重新移动、伤害、投阵亡骰或提前完成旬。`DuelCombatIntegrationTests`覆盖正反机会、权限／无效响应、同日剩余战斗、旬末最后一天，以及选择另一个参败人物继任后重新检查其君主／主将资格。`DuelSessionTests`验证公开会话、稳定起点恢复和完整录像；独立CLI结果见下文。

外交谈判、联盟邀请、修行报名、处决及单挑继任等待只在内存续行。普通存档、录像起点及导出要求完整结算边界；Application事务复制使用运行时快照，诊断哈希仍包含待答进度。正式旅行、货物、已建立协议及90日课程可跨旬保存；保存／日志54拒绝旧交互字段及成员排序状态。对应约定见[SAVE-0001](../specs/behavior/BEH-SAVE-0001-session-persistence-replay-and-headless.md)和[哈希约定](hash-and-fingerprint.md)。

## 数据、查询与命令

190年schema65／组合69已原位转换，内容指纹：

`989AF665B1CEB6E7546B3825229923CC91388F9A9654BED9A918FFE7E9503FA9`

源800条中700条支持人物完整进入内容与世界，100条为未配置槽。逐身份对比登场年、出生年、亲代、血缘、寿命档、继任优先值及单挑优待；保留397名未登场人物、17名非零继任优先人物、3名单挑优待人物。九组城市／兵法课程与源表一致；登场、自然死亡、迁移、阵亡允许及俘虏允许语义开关均匹配190年模式。没有裁剪人物定义、文本或图像资源。

当前可见查询210人，与世界可见身份集合完全相同；隐藏与未来人物仍在世界，登场后由现有查询和资格生效。周期登场、迁移、自然死亡没有伪造玩家命令；教学和继任使用上述公共响应命令。报告为`local/generated/verification/officer-order/data.json`，核对程序为`local/generated/verification/g09-data-audit/`。

## 当前v13统一人物顺序验证

全量`dotnet test San9Remake.slnx --no-restore --verbosity quiet`2047项通过：Core1379、Content62、Importer260、Application238、Client97、Headless11。保存字段删除及旧内容版本拒绝已覆盖，无兼容分支。700人物与九组课程已重新核对，数据覆盖和当前可见210人物保持完整。产物位于`local/generated/verification/officer-order/`。

公开单挑独立进程录像重放成功：2操作、1场单挑、9刀、君主阵亡及继任选择各1次，最终哈希`159D76E472E517F7EA7D1CBF4AF7C3C23F1D2217A6852A2078B413812594C2FB`，`firstFailure=null`。

190年完整地图、种子190、36旬验证成功：2622操作、756经营、12次恢复，连续／恢复、命令日志、重复前缀及完整录像一致；耗时158605ms。最终哈希`38A50C798754E89B69F26DF4EA7AE1D8F69E578525396ABE6F696EA892FA9553`，`success=true`且`firstFailure=null`。实际发生12次登场、247次迁移、773次伤势恢复、1497次熟练度增长、1次能力增长、621次战斗伤害、8次占领和15次兵法发动；其他分支仍由定向测试覆盖。报告为`officer-order/190/report.json`。本轮未启动游戏，未声称画面验收。

复现入口（产物保持在忽略目录）：

```powershell
$env:SAN9_G09_REPORT_DIR = Join-Path $PWD 'local/generated/verification/officer-order/public'
dotnet test tests/San9.Application.Tests --filter FullyQualifiedName~DuelSessionTests
Remove-Item Env:SAN9_G09_REPORT_DIR
dotnet run --project src/San9.Headless -- replay --scenario local/generated/verification/officer-order/public/duel-scenario.json --recording local/generated/verification/officer-order/public/duel.recording.json --report local/generated/verification/officer-order/public/replay.json
dotnet run --project src/San9.Headless -- verify --scenario local/generated/content/scenario-190-live/scenario.json --output local/generated/verification/officer-order/190 --xuns 36 --seed 190
```

单挑夹具只有在队人物，不满足通用`verify`的经营活动要求，因此使用精确`replay`；全年运行使用完整190年内容。验证器不放宽活动门槛。

## 阶段边界与已有资料问题

G09完成普通人物生命周期、普通成长生产者与单挑。G10继续实现数据化事件：主动求官、空城起兵、城市自立／失败下野、事件创建势力的忠诚初始化，以及探索特殊事件的直接成长奖励、封禅奖励等。它们使用已完成的人员转换／成长能力，但尚无完整事件触发和选择链，不能算作G09已实现功能。该分工细化治理规格原有G09/G10归属，与路线图G10周期事件、条件／效果／消费键一致。G11负责完整自主AI，G12负责十年长期对局，G13负责表现接入。

此前`validate_core_domain_contracts.py`的章节／元数据缺失、5份UI未分类及独立培养草案残留问题，在HEAD基线复跑相同，报告`local/generated/verification/g09-domain-contracts.json`及`g09-baseline/report.json`。本次不把该机器文档校验声称为全绿；这些资料分类问题不改变已采用的培养支线范围。
