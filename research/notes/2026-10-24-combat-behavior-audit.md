# 战斗行为扩大核对（2026-10-24）

**当前结论：目标变更生命周期、幻术／妖术次要对象外交筛选，以及后续确认的普通分量重复（S1）、同轮防守兵法机会未短路（S2）均已修复。**S1／S2修复见[专项记录v187](2026-10-24-normal-attack-scheduling-audit.md#用户确认后的修复v187)。另更正一处兵法规格中的旧状态表述。已采用差异、历史已修项和待验证线索分别维护。

作者Codex，日期2026-10-24，规则基线v179、交付v180。研究身份沿[SRC-SAN9PK-TC-101](../sources/SRC-SAN9PK-TC-101.md)：繁中PK1.0.1.0、x86、基址0x400000，EXE大小2636800、SHA-256 `d20794aeff67301ec2bf8c3becb1e9944c68c6c0588fbfd4bf04e8597f0e5028`，项目主库`research/analysis/San9PK-TC-101.i64`，IDA9.4／Hex-Rays9.4.0.260915。产品验证环境为Windows／.NET10。

**普攻分类更正：**[专项调查](2026-10-24-normal-attack-scheduling-audit.md)闭合了原版兵法结果返回普通交战的分支。S1／S2确认为实现bug，已于v187修复；撤销“是否保留待裁定”和整组调度后果属于已明确重制选择的表述。

**后续采用修正（v181）。**用户授权[合理性复审R1／R2／R3](2026-10-24-original-ai-rationality-review.md#用户确认后的实现v181)后，进攻估值与实际基础留兵统一，高昂返城队正常计入回援预算，目标变化不再清异常状态。v180的异常状态归一已被替代，外交目标复验仍保留。v181最终6113例通过，产物位于`local/generated/verification/rationality-fixes-20261024/`；S1／S2随后于v187独立修复。

## 已修复的遗漏

| 项 | 原作事实／修复前问题 | 最终行为与验证 |
| --- | --- | --- |
| F1a：目标易主后的旧攻击保留 | 公共强攻资格与自动延续资格分开；重制只检查前者，袁绍攻击公孙度的旧任务在襄平转属公孙瓒后仍保留，下一旬盟主背盟 | 目标变化通知以共享自动敌对规则复验保存攻击、临时迎敌及接触目标。盟友、关系≥40、双向攻击请求保护退出；仍是敌人时保留。显式强攻的资格与处罚保留 |
| F1b：异常状态跳过目标变更 | 原作清异常后复验保存命令；旧重制在伪报／溃败期间直接返回，可能留下失效攻击 | v181选择目标复验与状态计时分离：即时处理换属／删除后的旧命令，保持异常及计时，待允许自主行动后处理后续任务，不继承原作清状态 |
| F2：范围兵法的外交筛选不完整 | 原作范围候选也使用自动敌对门；重制只排除己方和联盟，可能波及关系40以上或攻击请求保护方 | 幻术／妖术次要对象复用`AutomaticEnemy`，同时覆盖单位和据点，不更改显式主目标。新增24组真实结算回归 |

F1事实、原档时间线、调用链和实际回放见[袁绍背盟定向核对](2026-10-24-allied-capture-stale-attack-audit.md)。F2的回调解码、原作边界和主库同步见[RE-0068](../findings/RE-SAN9PK-0068.md#2026-10-24范围兵法的外交筛选)。F1正式新增25例，包含归属变化22例及实际击破删除3例；生命周期测试合计38例通过。两类共新增49例正式回归。

**文档事实更正D1：**WAR-0005旧文写“部队重复混乱取较大值、切换为混乱采用新日数”，与现行实现、STRATEGY-0002及2026-10-21证据相反。本轮回读`463C80`确认：相同状态不刷新；切换状态取较大日数。代码及原有测试已正确，仅更正WAR-0005，不能算作新修产品缺陷。据点重复混乱的较大值规则另行保留。

## 扩大检查覆盖

这张表记录本轮实际读过的主要消费者与对照证据；表中的测试是当前解决方案已执行的相关测试族，不把测试数量当作原版等价性的证明，也不声称对每个函数都重新完成了一次逆向。

| 行为域 | 本轮重点条件／分支 | 原作与采用基线 | 当前实现及验证入口 | 结论 |
| --- | --- | --- | --- | --- |
| 目标与外交 | 新敌人／己方／盟友／中立、请求保护、保存／临时目标、异常状态、强攻与处罚 | [RE-0119](../findings/RE-SAN9PK-0119.md)、[RE-0137](../findings/RE-SAN9PK-0137.md)、WAR-0001 | `MilitaryOrders`、`MilitaryDiplomacy`、各换属入口；生命周期、外交、方针测试 | F1已修；旬开始背盟处罚本身正确 |
| 移动与接敌 | 陆水生效阵形、预算与余量、中继、接触／迎敌、碰撞、跨岸、强制返回 | WAR-0001、[寻路设计](../../specs/navigation-redesign.md)、[RE-0029](../findings/RE-SAN9PK-0029.md) | `MilitaryNavigation`、`MovementOccupancy`、`MilitaryContacts`、`MilitaryEngagements`、`UnitFormations`；Navigation、WaterCombatContact、Movement测试 | 未新增确认遗漏；几何与随机调度的明确选择保留 |
| 普攻与反击 | 能否发起、混乱／溃败、反向射程、同时伤害、野战／设施、零守军、墙剩1门、单次强度修正 | [RE-0068](../findings/RE-SAN9PK-0068.md)、WAR-0002／0004 | `CombatMath`、`MilitaryCombat`、`FacilityCombat`；CombatMath、MilitaryCombat、FacilityCombat、OriginalCombatAlignment测试 | 后续专项确认S1／S2，已于v187修复；动态转攻后的日内次数仍待验证 |
| 射程与地理修正 | 高差、射台、落石传播／水域／障碍、船型限制、都市地利 | RE-0068、WAR-0004／0005 | `FacilityAttackRange`、`UnitRange`及当前地理查询；RockfallGeography、WaterCombatContact、CombatApproach测试 | 未新增确认遗漏；先前已修设施防守50%门仍在 |
| 兵法资格与机会 | 建设／输送禁主动、健康／冷却、有效陆水阵、个人预选、船型先行后回退、防守50%、设施负责人 | [RE-0101](../findings/RE-SAN9PK-0101.md)、RE-0068、WAR-0005 | `TacticCombat`、`FacilityCombat`、`TacticFormation`；TacticFormation、NavalTacticOpportunity、TacticAvailability测试 | 未新增确认遗漏；旧研究中的来源用途／支援适配缺口已修，未重复报错 |
| 武力／智力／支援效果 | 公式截断、连携与加倍、抵抗、状态、范围、攻心容量、鼓舞／治疗候选与恢复、成长 | RE-0068、RE-0101、[RE-0125](../findings/RE-SAN9PK-0125.md)、WAR-0005 | `TacticMath`、`IntelligenceTactics`、`TacticSupport`、`BattleLearning`、`TacticOccurrence`；TacticCombat、IntelligenceTactic、TacticLink、TrapGeography测试 | F2已修，D1文档更正；已采用差异见下节 |
| 状态、粮食与士气 | 同状态／跨状态计时、高昂到期、治疗、围攻溃败40与8倍边界、断粮、伤兵恢复、普通士气衰减 | [RE-0029](../findings/RE-SAN9PK-0029.md)、[RE-0113](../findings/RE-SAN9PK-0113.md)、WAR-0004、STRATEGY-0002 | `MilitaryConditions`、`BattleRouting`、`MilitaryDailyCycle`、`MilitaryMorale`；MilitaryCondition、MilitarySupply、BattleRouting测试 | F1b已修；自然溃败生产者已于此前接入 |
| 单挑、狙击、混战与解救 | 机会、伤势／阵位、阵亡保护、宝物、君主继任暂停后续算、效果≥10、鹤翼、俘虏优先与释放 | RE-0068、RE-0101、WAR-0005及单挑规格 | `DuelCombat`／`DuelStrike`／`DuelAftermath`、`TacticSniping`、`MeleeDeaths`、`TacticRescue`；Duel、TacticSniping、SnipingSession、TacticRescue测试 | 未新增确认遗漏；规则平等与日版狙击修正保留 |
| 据点失守与易主 | 兵／士气／耐久三门、施工物拒绝、工作物销毁、设施分组、接收军团、当场归并、民心与99%发展损失 | [RE-0138](../findings/RE-SAN9PK-0138.md)、WAR-0002／0006 | `BattleAftermath`、`CityBattleEffects`、`ConstructionContentCompiler`；WarfareAuditAlignment、FacilityConstruction、CombatRuleCompletion测试 | 未新增确认遗漏；F1在同一换属结算维护旧目标 |
| 战败人物及兵员／钱粮 | 有／无退路、城市门与人物落点分离、马、捕获参与者、阵位、原有俘虏、伤兵半分、两阶段取整、容量和载荷 | [RE-0139](../findings/RE-SAN9PK-0139.md)、RE-0138、WAR-0003 | `BattleCaptureRules`、`BattlePersonnel`、`BattleRetreat`、`BattleTreasury`、`UnitTerminationService`；BattleCapture、CombatRuleCompletion、DuelAftermath测试 | 未新增确认遗漏；空安置池修复与兵役回收差异保留 |
| 战后治理与建设生命周期 | 服务地修复、归附／合并、跨军团入驻、最后城市灭亡、取消施工、引用与任务清理 | RE-0137／0138／0139、WAR-0001／0003／0006 | `ForceTransfer`、`UnitAffiliation`、`SurrenderMerge`、`ForceConsolidation`、`FacilityConstruction`及既有公共治理；相关Core／Application场景 | 新换属通知已接入；未对无关治理功能做扩展改造 |

## 保留的明确重制差异

以下均有现行规格依据，本次没有改回原版。列表只列本轮直接碰到、容易误报为遗漏的差别；完整采用行为仍由对应BEH维护。

- [WAR-0004](../../specs/behavior/BEH-WAR-0004-normal-combat.md)：每日一次主动攻击，反击独立；电脑强度每个伤害方向只修正一次。普通攻防统率取现场最高有效统率，不限主将。
- WAR-0001／0004：确定性的现代日级调度与独立导航随机；当前矩形、严格相交、接敌距离和水域连通规则，不声称复刻原作每个内部时间片或代理对象。
- [WAR-0005](../../specs/behavior/BEH-WAR-0005-tactics.md)：武力兵法同类别可连携，允许不同名；加倍概率取成长前主发动者，不取原作参与列表末项。
- WAR-0005：无驻将设施可受五种智力兵法；攻心超额回收目标地域都市兵役，不转伤兵；治疗保留高昂并取消独立到期扣士气标记。
- WAR-0005：范围效果以已选主目标为中心、细坐标矩形间距，治疗以来源为中心；水军选人／自动兵法顺序及水上阵位按既定规则，不因此撤销两阶段机会。
- WAR-0005及规则平等决策：单挑／狙击／混战不以是否存在玩家直属军团决定阵亡资格；保留狙击采用的日版伤势写回修正。
- [WAR-0002](../../specs/behavior/BEH-WAR-0002-building-defeat-and-capture.md)：实际触发击破的存活进攻者决定接收军团，不重建原版跨攻击者贡献累计；野战异常攻防使用显式状态映射。
- WAR-0001／0003：兵员归并溢出按既有兵役回收规则完整记账，回收不受自然增长上限截断；安置候选为空时保留最后地理位置并转在野，不留下原作悬挂单位引用；永久对象身份不复用源槽位。
- WAR-0006：建设公式中的能力采用主将有效统率，宽整数避免源机器溢出；能力源字段的正式名称尚未独立闭合，不影响已明确采用的产品规则。

## 验证与剩余边界

- 最终运行`dotnet test San9Remake.slnx --no-restore`，5个测试项目6103例全部通过：Core2995、Application1906、Content156、Importer510、Client536。TRX见本地`local/generated/verification/combat-audit-20261024/final-v180/`，汇总为`test-summary.json`。
- 从5月11日自动档精确恢复并推进至6月1日，验证袁绍旧攻击结束且联盟保留；最后代码状态下的定向回放也通过。临时私有存档探针已移出正式测试目录，用户存档未修改。
- 本轮没有改变内容契约、导入范围或存档／命令日志结构，无须重转二十份内容包；没有改变CLI和跨进程接线，未重复运行Headless专用进程测试。
- 原作侧为静态证据，不是同局面原游戏动态双端回放。完整原版调度次数、随机消费顺序、全部阵形×地形×目标组合的穷举、异常存档／强制内部调用不在“已等价”的结论内。
- 战斗画面、声音、镜头和逐帧表现没有在本轮重新验收；现有消费者测试通过不能代替画面验收。育成、教学、挑战等未支持模式也未因本次战斗核对自动接入。

S1／S2已独立修复，不将旧错误归为保留差异；没有证据支持“所有原版战斗分支均已穷尽、完全一致”的声明。
