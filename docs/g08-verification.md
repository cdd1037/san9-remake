# G08 外交与计略验收

状态：G08已完成。当前规则`san9-core-g08-v30`，保存／日志45，剧本schema58／组合62。运行任务布局与规则变化不改变内容字段及导入范围，无需重转内容包；旧规则存档和录像明确拒绝。未启动游戏，画面待验收。保留的原作疑点见[疑点清单](../research/research-open-questions.md#g08保留疑点与证据边界)。

## 公共规划与规则边界

`StrategyPlanner`接入八类公共计略命令，自动势力及自动军团在军事规划后执行，CLI脚本复用同一入口。每个控制者最多一个计略去程，最多8名执行者、每类16个目标，按日期轮换优先类别；候选仍须通过公共权限、外交、地理、状态及资源验证。敌军部队候选只来自己方接敌／攻击命令引用，不扫描隐藏位置；救援保留3000驻军，并要求至少1000缺额。这是已采用的阶段策略，完整自主AI归G11。

缺省关系已定义为0，敌对计略不再要求显式零记录；离间第三方排序、流言资格及效果统一使用关系服务。六类敌对计略的省略零项／显式零项执行结果一致。异民族分类与联盟／请求配对保护分别验证：伪报和扰乱不因异民族身份单独禁用，另外四种敌对计略保留各自特殊势力资格；联盟成员之间六类均禁止。

成长与附加随机边界：八类计略成功时使用对应能力3%成长门，失败不抽成长流、100封顶；烧夷3%兵粮门、夺取50%宝物门、流言忠诚门使用固定随机向量检验严格比较，且不改变主骰和其他成功效果。来源任务仅一名执行者，熟练度不扩散到驻地其他人员。

## 行为覆盖与证据

| 范围 | 已实现行为及回归入口 | 依据 |
| --- | --- | --- |
| 对称关系与联盟 | 稀疏关系、五档边界；初始联盟、36旬期限、灭亡清理；季度候选、玩家逐一选择、形成与冲突清理。`AllianceTests`、`AllianceEligibilityTests`、`AllianceFormationTests`及对应Application会话测试 | [DIPLOMACY-0001](../specs/behavior/BEH-DIPLOMACY-0001-force-relations-and-alliance.md)、[RE-0119](../research/findings/RE-SAN9PK-0119.md) |
| 赠与 | 资金／兵粮／宝物互斥载荷、提交预扣、接受、拒收及取消退款、上限损耗和返程。`GiftSessionTests` | [DIPLOMACY-0002](../specs/behavior/BEH-DIPLOMACY-0002-command-negotiation-and-resolution.md)、[RE-0117](../research/findings/RE-SAN9PK-0117.md) |
| 俘虏返还 | 免费／交换／资金／宝物／拒绝，军师与报价共用可行条件，两阶段报价，严格接受边界，当前持有权及余额复验，免费信望与野战归队。`PrisonerCompensationTests`、`DiplomacyTreasureTests`、`PrisonerReturnSessionTests` | DIPLOMACY-0002、[RE-0118](../research/findings/RE-SAN9PK-0118.md) |
| 请求攻打 | 正式谈判、19旬约定、到期／双向毁约责任、实际攻城履约一次性、重复请求不重复支付、易主清理、军事规划优先目标。`AttackRequestDiplomacyTests`、`AttackRequestTests`、`AttackRequestPlannerTests`、`AttackRequestNegotiationSessionTests` | DIPLOMACY-0002、RE-0118 |
| 劝降 | 玩家决定及自动阈值，任务预留先清算，根／军团半份额资源，随降／离开人员、空／非空部队、俘虏、据点及军团归并，势力身份退出。`SurrenderDiplomacyTests`、`SurrenderSessionTests` | DIPLOMACY-0002、RE-0118 |
| 外交调度 | 四类去程向上取整至10天倍数、最少10天；旬边界谈判；日内／旬边界暂停位置、阶段号、实际响应方及剩余队列保存，回答后不重跑已完成阶段。`DiplomacyTimingTests`、`DailyTaskDecisionTests`、`StagedDailyTaskTests` | DIPLOMACY-0002、RE-0118时序补证 |
| 伪报／扰乱 | 资格、方圆减半、教论与阵型免疫、严格阈值、实时能力；伪报持续覆盖返城、旧路线清理、到达吸收及新命令到期生效；扰乱持续与士气。`UnitStrategyTests`、`StrategyDiplomacyTests` | [STRATEGY-0001](../specs/behavior/BEH-STRATEGY-0001-command-advice-and-resolution.md)、[STRATEGY-0002](../specs/behavior/BEH-STRATEGY-0002-unit-status-and-reinforcement.md)、[RE-0113](../research/findings/RE-SAN9PK-0113.md) |
| 激励 | 成功士气／高昂／兵法间隔，失败士气损失，高昂到期惩罚与治疗免罚标记。`UnitStrategyTests`、`IntelligenceTacticTests`及G07状态回归 | STRATEGY-0002、[RE-0114](../research/findings/RE-SAN9PK-0114.md) |
| 救援 | 去程扣3000、在途唯一持有，成功／失败实际交付、余兵返程、实际人数混合士气、满员与来源失守、执行者被俘、无归队点及终局遣散。`UnitStrategyTests`、`MilitarySessionTests` | STRATEGY-0002、RE-0114；兵力守恒为已采用重制选择 |
| 烧夷 | 有向直接邻城、非工作物与完成资格，实时守将／耐久，耐久留10，独立兵粮损失，失败伤势不倒退与关系代价。`BurnStrategyTests` | [STRATEGY-0003](../specs/behavior/BEH-STRATEGY-0003-person-building-and-force-effects.md)、[RE-0115](../research/findings/RE-SAN9PK-0115.md) |
| 夺取 | 同一主骰按宝物→己方俘虏→资金→失败顺序，宝物独立50%门与候选排序，守恒支付、俘虏归队、失败伤势和关系。`SeizeStrategyTests` | STRATEGY-0003、RE-0115 |
| 离间／流言 | 人物／双势力资格、实时能力、忠诚及关系效果、独立忠诚概率；保留已采用流言关系公式，未采纳后续修改备忘。`AlienateStrategyTests`、`GossipStrategyTests` | STRATEGY-0003、[RE-0116](../research/findings/RE-SAN9PK-0116.md) |
| 公共计略边界 | 单执行者、固定条件提交骰、实时复验、只读意见、成功成长／两分支熟练度、类型化载荷、失效返程与去返程保存。`StrategyMathTests`、各计略测试、`MilitarySessionTests`、`StrategyDiplomacyTests` | STRATEGY-0001、RE-0113至RE-0116、[随机约定](randomness.md) |
| 军团与自动入口 | 四种方针，关系40、联盟、请求、灭亡／毁坏导致的定向清理；自动外交和八计略使用公共命令。`CorpsDelegationTests`、`CorpsControlTests`、`DiplomacyPlannerTests`、`StrategyPlannerTests` | [RE-0123](../research/findings/RE-SAN9PK-0123.md)、各BEH的G08阶段策略 |

阶段退出链已闭合：联盟形成→禁止自动攻击／敌对计略→到期恢复；使者途中对象失效→取消并清算／返程；八计略分别写回部队、兵员、人物、建筑及势力效果；意见查询无玩法随机副作用；多阶段谈判与存读档不能重复付款。G10数据化事件系统尚未实施，未来通过同一公共命令接入，不在此预建另一套结算器。

## 内容与查询覆盖

复用已核对的190年包：25势力、25军团、700人物、94据点、100件宝物定义及120条宝物状态；有效定义与状态集合不以本次可见／可用目标裁剪。初始联盟为目标5、盟主7、13成员、36旬。50城州归属、40000格地域出口、94据点地域及736条有向地域边的源／转换／Core核对已在RE-0119记录。

TC101剧本没有攻击请求元组，Importer显式输出空集合；独立内容可以创作初始请求，由建局推导签约目标归属。四类军团方针进入共享契约，TC101剧本未保存运行时委任方针，导入为一般；50条源军团记录与25条有效记录的身份和引用核对已完成。人物外交特性、100件宝物保留类别、38条偏好配对及3项自动报价排除沿已有转换完整保留，证据见RE-0118和RE-0123。

当前快照提供关系、联盟、请求、军团方针、人物占用和类型化任务；Application提供军师意见、联盟候选／参与提示、俘虏与攻打报价。命令可用性由当前世界复验，不把初始内容或显示资源当作动态规则。当前内容字段保持不变，没有新增私有资源或显示资源。

## 实际验证

当前代码各项目测试合计1771项通过：Core1175、Content42、Importer254、Application192、Client97、Headless11。验证使用`dotnet test San9Remake.slnx --no-restore --verbosity quiet`及Core项目重跑；覆盖保存、录像和公共命令。

| 回归边界 | 验证入口 |
| --- | --- |
| 同条件军师意见稳定、低概率命中及准确度边界；流言仅选在职非君主 | `G08RegressionTests` |
| 当前设施坐标、路径快照与恢复；四类外交正常返程、去程中断及返程改道 | `G08RegressionTests`、`DiplomacyTimingTests` |
| 外交使者返程继续占用预算，普通返程及获释俘虏不占用 | `DiplomaticReturnPlannerTests` |
| 拒收退款、容量溢出、守恒、重复回答及恢复不重复退款 | `GiftSessionTests` |
| 免费返还意见的关系79／80／100边界、交换优先、玩家目标和只读查询 | `PrisonerReturnTests` |

### 历史CLI基线（v28／保存43）

以下独立CLI公开场景及190年36旬记录属于`g08-v28`／保存43基线，未在v30重跑，其哈希不代表当前版本：

公开场景由`StrategyPlannerTests.Fixture`生成，包含3势力、7人物、3据点及2部队，省略零关系记录。八个日期变体各运行2旬独立CLI：合计168次操作、16次保存恢复、5304毫秒，完整录像和命令日志重放及重复前缀检查全部通过。实际事件合计伪报1、扰乱1、激励10、救援4、烧夷4、夺取4、离间16、流言8；这些是结算次数，不等同于成功次数。另将八类Application命令录像交给独立CLI重放，8份均通过。输出：`local/generated/verification/g08-public/`。

190年正式全地图使用种子190运行36旬，到191年1月1日正常完成；2703次操作、693次经营、16次保存恢复，耗时159067毫秒。连续运行和恢复最终存档逐字节相同，命令日志、完整录像及重复日志前缀对照通过，无首个失败。最终会话哈希：

`CDB7A28605574E863F237C89E3275B267DCB8F634BC9F0FE95A2C9B9CE99AB8E`

内容指纹：`99E4330A6C7AD4176DBA473A71F6306F6E74613C69490418C740992C547598EF`。

实际结算为赠与54、俘虏返还19、请求攻打21、劝降268、烧夷211、夺取47、离间157、流言38、激励22、救援6；联盟到期1次，另有760次战斗伤害、6次占领、15次兵法发动。赠与接受10次、俘虏返还接受1次；本次劝降全部拒绝，成功归并由公开命令场景验证。伪报／扰乱未在本次190年策略中触发，由上述公开CLI覆盖；季度新联盟由公开资格及玩家选择场景覆盖，不把一年运行中没有出现的分支算作已触发。报告记录260单位资源不足，这是正式不足结算，非验证失败。

最终仍有25势力身份、25军团、700人物、94据点、120宝物状态及4部队。输出：`local/generated/verification/g08-final-190/`，包含报告、内容指纹、命令／事件日志、检查点、连续／恢复存档及完整录像。产物均忽略，不提交原版资源。

### 复现入口

```powershell
dotnet test San9Remake.slnx --no-restore --verbosity quiet
$env:SAN9_G08_REPORT_DIR = Join-Path (Get-Location) 'local/generated/verification/g08-public'
dotnet test tests/San9.Application.Tests --no-build --filter EveryCategoryUsesPublicCommand
Remove-Item Env:SAN9_G08_REPORT_DIR
0..7 | ForEach-Object {
    dotnet run --project src/San9.Headless --no-build -- verify --scenario "local/generated/verification/g08-public/scenario-$_.json" --output "local/generated/verification/g08-public/run-$_" --xuns 2 --seed 190
    dotnet run --project src/San9.Headless --no-build -- replay --scenario "local/generated/verification/g08-public/scenario-$_.json" --recording "local/generated/verification/g08-public/strategy-$_.recording.json" --report "local/generated/verification/g08-public/replay-$_.json"
}
dotnet run --project src/San9.Headless --no-build -- verify --scenario local/generated/content/scenario-190-live/scenario.json --output local/generated/verification/g08-final-190 --xuns 36 --seed 190
```

## 后续边界

G09人物完整生命周期与单挑、G10通用事件／完整结局、G11完整自主AI、G12十年长期对局及G13表现接入按路线图推进；本次未启动下一阶段。当前没有外交／联盟选择界面，核心待决协议支持内存续行；G09按统一保存边界移除待答存档，改为稳定起点录像。全游戏消息文本、动画和画面验收仍留在表现阶段。

## 2026-09-23联盟讨伐成功结算

依据[RE-0119补证](../research/findings/RE-SAN9PK-0119.md#2026-09-23讨伐成功与普通解散的区分)接入独立成功结算：联盟成员击破导致目标灭亡，或在日结处置中处决目标君主，由完成方获得15点基础信望（上限1000），记录`AllianceVictoryEvent`并以`SubjugationSucceeded`结束联盟。普通战略处决、非成员击破、释放及普通死亡不发成功奖励；目标君主继任不撤销已完成的日结讨伐。等待玩家继任答复期间不提前发奖，死亡／势力清理不重复结算。

战斗完成方由本次击破显式传递，最后城市占领后在攻击部队吸收入城前结清灭亡；范围攻击先完成同一效果的设施占领。两个只验证吸收／伤兵恢复的旧夹具增加防守方剩余城市，使其继续隔离非灭亡场景，没有修改原有数值断言。

- `AllianceVictoryTests`覆盖盟主、普通成员、第三方、仍有城市、信望995／1000上限、源部队吸收入城及重复清理，结果通过世界不变量并可恢复。
- `AllianceVictorySessionTests`通过公共出征和旬推进打下最后城市，核对完成方、奖励、成功事件、存档恢复及完整录像。
- `CapturedRulerDispositionTests`覆盖玩家继任选择／自动回退、无继任灭亡、普通成员完成讨伐、外部处置方、释放、稳定起点恢复及录像；`AllianceRulerDeathTests`保留普通战略处决和自然死亡不奖励的对照。
- 实际执行`dotnet test San9Remake.slnx --no-restore --verbosity quiet`：3370项全部通过（Core2375、Application464、Content74、Importer343、Client98、Headless16）。`git diff --check`通过。

当前规则`san9-core-g10-v60`；剧本109／组合113、保存82不变，日志85必填电脑伤害加成，旧规则存档／录像拒绝加载。新增领域事件已注册序列化并接入Headless事件输出，无新增内容对象、导入筛选或原版运行时依赖，现有内容包无需重转。专用联盟消息与Godot画面仍待表现阶段，本次验证使用公共会话、确定性测试及录像，未启动游戏进行画面验收。
