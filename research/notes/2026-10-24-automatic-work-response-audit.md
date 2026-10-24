# 公共自主响应与电脑待决选择续核（2026-10-24）

**记录：AI-RESPONSE-AUDIT-20261024，作者Codex。**继续对照当前工作区与繁中PK原作，范围为每日撤退／追击、旬级敌接近、事后命令，以及RE-0181登记的待决族及自动提交入口。工作57的旬级调兵与每日自主撤退分开处理；保留[B26～B30](../../specs/original-ai/behavior.md)及[G07](../../specs/behavior/BEH-WAR-0001-movement-and-command-lifecycle.md#g07-出征方针)明确采用的几何、同距、等待和确定性规则。近期[RE-0137生命周期对齐](../findings/RE-SAN9PK-0137.md#military-command-lifecycle-alignment)已经实施的目标失效、入城、返回与转属处理不重算新发现。本轮只研究与诊断，未修改产品代码、正式测试或玩法规格。

## 两类新增偏差

### 普通返回与自主返回身份合并，影响两个消费者

**原作事实，静态高置信。**`464910`敌接近主分支允许本队事件38或39，未排除普通返回39；自主返回52不进入该分支。`465160`追击响应则要求对方当前为事件52且带`0x40000`标记，不能把任意普通返回39当作自主撤退。`4642B0`正是将可转换的38～52事件清除后，建立指向服务据点的52并设置该标记的公共自主返回入口。原作状态和调用依据见[RE-0081](../findings/RE-SAN9PK-0081.md)、[RE-0090](../findings/RE-SAN9PK-0090.md)。

**实现事实。**当前普通`OrderUnitCommand`返回与`MilitaryOrders.Return`自主返回都写成同一`MilitaryOrderKind.Return`。`UnitState`没有保存这两类返回的语义区别；`HasForcedReturn`只由伪报／溃败异常状态派生，不表示普通状态下的自主撤退。`MilitaryPolicies.ApproachXun`据此排除所有Return，而每日追击又将目标的所有Return视为触发条件。没有在已采用差异中找到明确将原作这两类语义合并的决策；不能仅因运行时共用一个枚举便视为已接受差异。

**公开输入诊断事实。**复用`UnitPolicyTests`的现有规则和世界，先用真实`OrderUnitCommand`创建对应普通命令，断言公共准备资格`None`且提交接受，再在该世界上调用正式公共响应处理器。

| 场景 | 原作分支对应的预期 | 当前观察 |
| --- | --- | --- |
| 本队正常进军，有合格邻敌且允许迎击 | 保留主命令，临时目标设为敌队 | 正常通过 |
| 本队普通返回，其他条件相同 | 同样可临时迎击 | 临时目标仍为空，偏差断言失败 |
| 本队攻击原目标、不允许追击，对方由公共命令普通返回 | 尚未满足自主返回52门，保持攻击 | 本队错误改为返城，偏差断言失败 |
| 同样攻击关系，对方在本日因低士气实际触发自主撤退 | 满足追击响应门，本队按性格返城 | 正常通过；另断言目标确有`autonomous-retreat`改令事件 |

两处后果统一登记为一类返回状态契约遗漏，不拆成两个独立根因。对照没有把溃败状态、目标删除或无返回地引入场景。

### 敌接近裁决遗漏本队已交战门

**原作事实，静态高置信。**`464910`在收集邻敌及维护提示标志后，只有`flags & 0x300000 == 0`、本队交战虚槽为假、事件38／39且原始状态1／8才进入改指裁决。单位虚表`606490+0x90 → 461730`是`is_unit_engaged`，对外部部队读取主动或被动交战；不是“已有临时目标”这一条件的别名，见[RE-0029](../findings/RE-SAN9PK-0029.md)。

**实现事实。**`ApproachXun`只因已有`EngagementTargetId`跳过，没有以实际交战状态阻止裁决。正在被近接攻击但未建立临时迎击目标的部队仍可能再次执行迎击响应。

**公开输入诊断事实。**先通过公共命令给本队下达正常进军，保留允许迎击；对照改变敌队是否处于普通攻击的近接范围。调用正式`MilitaryCombat.AdvanceDay`真实产生攻击：交战场景断言本队受到`BattleDamageEvent`且`PassiveEngagement`为真，临时目标仍为空；非交战场景两项均为假。再次调用`ApproachXun`，未交战对照正确设敌队为临时目标；已交战场景仍设同一临时目标，预期跳过的断言失败。这样排除只手工置交战标志、实际不交战的夹具解释。

## 本轮验证

临时诊断为原Core测试项目中的`ResponseAuditProbeTests`，没有另建工程。最终有效输入共6项：3项正常对照通过、3项偏差断言失败；这是缺陷复现，不是修复后的回归通过。所有场景均有公共命令准备资格和实际接受前置断言，交战场景另经过真实战斗处理。

忽略目录`local/generated/verification/automatic-work-audit/`保留以下证据：

- `response-audit.trx`的两个`OrdinaryReturningUnitCanRespondToEnemyApproach`结果。该日志另有四项初始快照仍处于Setup边界、在恢复前即被拒绝的无效夹具，不采用。
- `response-pursuit-engagement-audit.trx`的四项追击／交战对照。初始边界改为合法Stable后，四项均通过全部前置断言，2项正常对照通过、2项偏差断言失败。
- `ResponseAuditProbeTests.cs`最终诊断源，已移出正式测试目录，不遗留失败正式用例。

运行入口为`dotnet test tests/San9.Core.Tests/San9.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ResponseAuditProbeTests`，第二次只筛选首次无效的四项，没有重跑已有效的普通返回／进军两项。没有重启用户暂缓的原作动态实验，也没有运行全解决方案、Headless、长期对局或画面验收。

## 公共响应逐分支覆盖与限制

下表区分本轮新增原作回读、与现有权威研究的实现对照，以及近期已完成的对齐；并非所有行都新增运行了测试。

| 分支 | 本轮核对与结论 |
| --- | --- |
| 旬级入口与每日顺序 | `ResolutionPipeline`分别调用`ApproachXun`和`AdvanceDay`，每日仍先全体撤退后全体追击；未与57混用 |
| 自主撤退四组严格门 | `UnitPolicyDecisions.Retreat`保留许可／禁止、四档性格、士气优先及`E>T`；返城失败不发成功响应，无随机或冷却 |
| 返回转换 | `MilitaryOrders.Return`保留公共资格、服务地修复、施工取消及导航清理；原39／52身份区别的缺口见本报告第一项 |
| 追击六分支 | 对照主目标／临时目标、许可／禁止、性格0和邻敌分支；核心返回身份门有新增偏差，不能据现有分支数量判完全对齐 |
| 敌接近 | 性格／许可与整数战力阈值保留；候选几何和最近排序是既定重制选择；普通返回和交战入口门的新增偏差分别见上文 |
| 原目标失效、临时目标恢复 | 读取当前`MilitaryOrders`、`MilitaryContacts`及RE-0137；土地／船型配对资格、临时接触恢复、目标变己方入城及待返回意图属于近期对齐，未重复记项 |
| 事后目标选择 | 只新选部队，沿用四方针裁决；没有恢复原作未填充建筑表。失败返城保留待续行状态，按既定几何与同距规则处理 |
| 归属接收后的动作重选 | 与RE-0137新对齐对照：修复服务地、异常恢复、建设／输送／异族返回，普通战斗队沿独立士气和战力门选单位／建筑；不冒充失效攻击的事后同一规则 |

**撤退邻敌E的补充静态边界。**本轮回读`464E08`确认其通过`474CD0`枚举通用单位域，并非事后命令的`461D50`集合。内嵌来源另要求有效且已占领的服务建筑，并通过既有排除／障碍过滤；以`45E1B0`拒绝同根，再经`465860`判断敌方来源能否以普通攻击范围触及本队。野战来源由`462FE0`扩展其占地，与`463190`取得的本队有效矩形作`IntersectRect`，边缘相接不算相交；内嵌来源另走`438690`。这条链没有显式要求每日执行史或出发等待为零。本轮未新增该输入域和几何的公共对照，不将其与既定几何差异或上述两类偏差混为第三项已复现缺陷。

## 电脑待决族覆盖

对照[RE-0181](../findings/RE-SAN9PK-0181.md)及[AI待决政策](../ai.md#computer-prompt-policy)，读取`AutomaticResponseRunner`、`ResponseRequest`、`PendingResponseCoordinator`与各公共消费者。未发现本轮新增已复现的自动答复遗漏，不为只有玩家资格的界面补造电脑回答。

| RE-0181序号／族 | 当前自动路径与核对 |
| --- | --- |
| 1 CountryTitle | 公共`GetAutomaticCountryTitle`，人物偏好先于血缘，再控州及剩余国号；候选和占用沿公共规则，身份破平按既定内容契约 |
| 2 EmperorTransfer | 普通电脑支持；异族、皇帝爵位、强制废黜国号在Core `Begin/Respond`强制覆盖，不依赖Runner再重复判断 |
| 3 OfficeRequest | 独立规划随机，`100-相性距离²`接受；公共结算维护招募／拒绝忠诚，不与探索登用合并 |
| 4 SearchRecruitment | 自动答复`Accept=true`，公共结算先加招募忠诚再判登用，失败49；不使用求官接受率 |
| 5 ShanyangGong | 玩家探索资格；`PersonnelMissionRule.Direct`前置控制进入，不交普通电脑答复器 |
| 6／7 MysticBookOffer／Return | 中央玩家直辖探索门及各自资格；无普通电脑待决消费者 |
| 8／9 ScholarInsult／Longevity | 同上，保留各自不同公共选项与效果 |
| 10／11 TreasureMonument／ScholarReturn | 同上，不把默认确认当成电脑政策缺口 |
| 12 MerchantInvestment | 同上，不为电脑编造投资预算／选项 |
| 13 CommunitySearch（8项） | 中央玩家探索门；八项公共选择仍沿各自选项规则 |
| 14 SearchConflict（7项） | 中央玩家探索门；七项不是自动势力待决库存 |
| 15／16 TigerSearch／BeastSearch | 中央玩家探索门；Beast局部函数不另查玩家，不代表可绕过上游调用链 |
| 17 TacticStudy | `PlayerCity`只玩家直辖城市，不进入普通电脑选择 |
| 18／19／21 Sniping／Duel／ExecutionSuccession | 三者自动提交后继为空的公共命令，由同一`RulerSuccession.Automatic`按已采用C07规则选任；不新增三套策略 |
| 20 CapturedRuler | 联盟讨伐对象必杀，其余按基础信望、当前最大能力及关系概率；实际处置通过公共命令，合法选项检查保留 |
| 22／23 AttackRequest／PrisonerReturn | 通过`GetAutomaticDiplomacyResponse`复用公共报价与接受；免费／交换／付款分别处理，不把有效报价等同于本方一定接受 |
| 24 AllianceParticipation | 合格即参加，公共命令再验证提议；玩家仍按响应所有权选择 |
| 25 Ending | 终局界面，无自动势力选择策略；不属于本轮待补分支 |

`AutomaticResponseRunner.Run`按当前响应所有者仅处理Automatic，成功后检查决策身份／步骤确实推进，拒绝或无进展报错；`ResponseRequest`对委任军团自己的日任务映射自动控制者，其他接收方仍保留其响应权限。`GameSession.Operations`、自动工作和结束控制调用同一答复器。外层事务负责失败回滚，未引入第二条自动提交入口。

## 取证身份与主库

沿用[SRC-SAN9PK-TC-101](../sources/SRC-SAN9PK-TC-101.md)及主库`research/analysis/San9PK-TC-101.i64`：繁中PK1.0.1、x86、基址`0x400000`，磁盘EXE为2,636,800字节，SHA-256 `d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028`；IDA9.4、Hex-Rays9.4.0.260915。

本轮合并取证统一核对21函数主段7544字节，包含尾块去重后为33段8105字节，全部与登记EXE一致；这21函数同时覆盖其他代理分工，不全归本报告。合并追加12条解释性注释，全文回读成功，逐项重新反编译、去注释后的函数体一致，前后字节哈希不变并原位保存。与本报告直接相关的是`464910/465160/4642B0`三处实际追加，`461730`回读而未改注释；没有更名、修改类型／边界或程序字节。各段摘要、原注释保留与实际回读结果见[统一主库账本](../archive/ida-write-records/automatic-work-remaining-20261024.json)。
