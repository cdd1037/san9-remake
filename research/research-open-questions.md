# 当前研究与规格疑点、集中处理清单

2026-09-06复核及后续补充；样本[SRC-SAN9PK-TC-101](sources/SRC-SAN9PK-TC-101.md)。本页索引裁决、保留边界和后置事项，详细证据维护于对应RE。历史“待验证”、产品未实现、未使用字段及原版异常分别处理。

## 本轮自行解决

| ID | 裁决与证据 |
| --- | --- |
| A01 | 新局增兵已有确认调用链，仅君主运行时编号700–799满足转换门，`INIT-01`调度关闭；见[RE-0061](findings/RE-SAN9PK-0061.md)、[RE-0140](findings/RE-SAN9PK-0140.md) |
| A02 | 伤兵恢复按内嵌守兵是否交战决定基数翻倍，再按伤兵数封顶；不是建筑类别加倍。见[RE-0014](findings/RE-SAN9PK-0014.md)、[BEH-IMPORT-0009](../specs/behavior/BEH-IMPORT-0009-scenario-fixed-building-records.md) |
| A03 | 兵法圆圈=本人习得，三角=本人未习得但宝物／根势力提供，空白=均无；不保证可发动。见[RE-0152](findings/RE-SAN9PK-0152.md)、[RE-0131](findings/RE-SAN9PK-0131.md) |
| A04 | 劝降读取双方当前信望及目标根势力城市数，含军团城市；见[RE-0118](findings/RE-SAN9PK-0118.md) |
| A05 | 忠诚50仅为特定随机化初始化，随后对当前势力覆盖80–100；普通剧本读取忠诚表。见[RE-0112](findings/RE-SAN9PK-0112.md)、[RE-0009](findings/RE-SAN9PK-0009.md) |
| A06 | 20剧本16,000人物／800,000忠诚字节已剖面，1,279值>100；库存熟练度最高600不等于玩法上限1000。`PRSN-05`关闭，原值导入；见[RE-0009](findings/RE-SAN9PK-0009.md)、[字段清单](scenario-field-research-checklist.md)、[脚本](scripts/analyze_scenario_persons.py) |
| A07 | HUD耐久／士气比例、城市图标、小地图几何和原色已补证；兵力显示数字。资金差额见E01；[RE-0157](findings/RE-SAN9PK-0157.md)、[RE-0158](findings/RE-SAN9PK-0158.md) |
| A08 | 攻击请求报价为俘虏、资金或宝物，非兵粮；已纠正文档，见[RE-0118](findings/RE-SAN9PK-0118.md)、[外交规格](../specs/behavior/BEH-DIPLOMACY-0002-command-negotiation-and-resolution.md) |
| A09 | 主要文本与主界面已有绑定，`CONTENT-01`改为调查中；剩余完整可替换契约及覆盖，见[字段清单](scenario-field-research-checklist.md)、[RE-0151](findings/RE-SAN9PK-0151.md)、[RE-0157](findings/RE-SAN9PK-0157.md) |
| A10 | 删除于`4b540ac`的历史路线已恢复归档并修复7处引用；147研究／419符号的旧验证器仅适用`6cbbfba`工作树。见[历史路线](archive/program-analysis/program-analysis-roadmap.md)、[验收限制](archive/program-analysis/program-analysis-r0-r12-acceptance.md) |
| A11 | 3份显示／UI规格补领域分类、章节及5项已批准决策门；依赖图删除易失计数，地图改引用当前契约（当时schema7）。见[领域契约](../specs/behavior/core-domain-contracts.json)、[依赖图](../specs/behavior/core-domain-dependency-map.md)、[地图规格](../specs/behavior/BEH-RENDER-0002-interactive-static-map.md) |

另删除RE-0151重复资源8436待办；实际IDA写入与回读见[账本](archive/ida-write-records/research-questions-audit-2026-09-06.json)。

## 已有结论，不再交给用户重复处理

| 主题 | 有效结论与入口 |
| --- | --- |
| AI独立10／60、许攸前置 | 12表155项未请求独立10／60，工作9内部建设保留；官渡要求曹操属玩家根势力。见[入口复核](ai.md#standalone-entry-audit)、[官渡回读](findings/RE-SAN9PK-0130.md#guandu-ai-readback) |
| 登用失败忠诚 | 原作对招募方同一项先加后减；错传对象仍为推断。重制改扣所属势力已批准，见[RE-0111](findings/RE-SAN9PK-0111.md)、[DEC-PERSON-0001](../specs/decisions/DEC-PERSON-0001-recruit-failure-affiliation-loyalty.md) |
| 军师、俘虏、地利与士气 | 旧漏判、平方／平方根及泛化说法已由定向证据纠正，见[RE-0112](findings/RE-SAN9PK-0112.md)、[RE-0099](findings/RE-SAN9PK-0099.md)、[RE-0090](findings/RE-SAN9PK-0090.md) |
| 单挑及人物身份 | 低武力胜者成长按`DEC-DUEL-0001`采用；君主免死、列传码／ID、接受方间隔分别裁决，见[RE-0105](findings/RE-SAN9PK-0105.md)、[0106](findings/RE-SAN9PK-0106.md)、[0107](findings/RE-SAN9PK-0107.md)、[0108](findings/RE-SAN9PK-0108.md)、[0165](findings/RE-SAN9PK-0165.md)。出生／登场年、勇猛、官职／官爵按[RE-0009](findings/RE-SAN9PK-0009.md)、[0133](findings/RE-SAN9PK-0133.md)的生产消费链纠正 |
| 已关闭字段 | 33项部队状态、目标编码、地图阻挡／过渡问题沿[字段清单](scenario-field-research-checklist.md)，原始保留位不等于规则遗漏 |
| 时机与显示 | 命令时机保留至联机重审，见[DEC-SIM-0001](../specs/decisions/DEC-SIM-0001-determinism-and-saves.md)；现代字体／Alpha已批准，标注随缩放暂定，见[BEH-UI-0001](../specs/behavior/BEH-UI-0001-initial-strategy-screen.md)、[RE-0158](findings/RE-SAN9PK-0158.md) |

## 仍保留的证据边界与后续工作

下表保留具体未闭合项，不自动启动全库调查；当前实现确需消费时定向补证。历史R0–R12通过只证明当时批次、来源及所选状态机边界；产品验收复选框不等于欠缺原作实验。

| 主题 | 边界与处理入口 |
| --- | --- |
| 自动规划 | 静态基线已收口；唯一AI未决表见[专题](ai.md#open-questions) |
| 外交／联盟 | 劝降前置谓词、固定值19生命周期、联盟资格／通知及谈判竞争；见[RE-0118](findings/RE-SAN9PK-0118.md)、[0119](findings/RE-SAN9PK-0119.md)、[0120](findings/RE-SAN9PK-0120.md)。正常UI竞争需受控存档；重制仍复验资源与引用 |
| 战争输入 | 路径代价完整关系、输送资源槽、占领权重附加槽及特殊终态类别；见[RE-0136](findings/RE-SAN9PK-0136.md)、[0137](findings/RE-SAN9PK-0137.md)、[0138](findings/RE-SAN9PK-0138.md) |
| 方格缓存与拾取 | 城市变化后地利刷新时序、高度／边缘拾取差异；见[RE-0099](findings/RE-SAN9PK-0099.md)、[0158](findings/RE-SAN9PK-0158.md)。当前保持既定拾取，反例记录格坐标、相机、缩放及预期 |
| 建筑与资源 | 组合31、面板可见性／遮蔽、宝物裁剪、其他窗口布局及按钮资格；见[RE-0035](findings/RE-SAN9PK-0035.md)、[0055](findings/RE-SAN9PK-0055.md)、[0057](findings/RE-SAN9PK-0057.md)、[地图规格](../specs/behavior/BEH-RENDER-0002-interactive-static-map.md)。未选组合不导出 |
| 通用列表 | 重复首页间接引用、三个编辑首页操作名、调用方候选／禁用／人数／默认选择及辅助文本／保留字；见[RE-0151](findings/RE-SAN9PK-0151.md)、[0153](findings/RE-SAN9PK-0153.md) |
| 函数恢复 | 间接分派、六共享尾块／数据操作数及过期缓存引用；[RE-0155](findings/RE-SAN9PK-0155.md)的901项不是全EXE完整性证明。缓存与实际call互证后采信 |
| 文本与内容 | 完整事件参数、列传／结局、替换键及Big5扩展字形；见[字段清单](scenario-field-research-checklist.md)、[RE-0026](findings/RE-SAN9PK-0026.md)、[0157](findings/RE-SAN9PK-0157.md)。Unicode投影不自动证明原字形身份 |
| 扩展范围 | `EXT-PRSN-01/EXT-ITEM-01/EXT-SCENARIO-01/EDITOR-01/RESOURCE-01`沿[字段清单](scenario-field-research-checklist.md)及[非核心归档](archive/program-analysis/program-analysis-non-core-archive.md)处理，不隐式扩大范围 |

## 本轮验证

以下均为2026-09-06历史验证，不能认证当前工作区：

- 主库身份、调用／虚表／指令回查；首批14处、资金兵粮5处、没收3处注释保存回读，重反编译及字节／边界不变检查。
- 20剧本人物剖面；Python单元测试90项通过。领域契约48份规格、13领域／13决策门通过，无依赖环；仅证明结构登记。
- 研究／规格／说明270份Markdown、1,486本地链接目标零缺失，`git diff --check`通过；未覆盖外网及全部锚点。
- 用户操作原游戏，断点捕捉27笔俸禄合计444，断点已删除保存，见RE-0157。

产物在`local/generated/research/audit/`。未执行重复开局、兵粮／没收动态对照、画面、当前全EXE反编译或.NET产品回归。复现命令：

```powershell
python research/scripts/analyze_scenario_persons.py private/游戏文件 local/generated/research/audit/person-profile.json
python -m unittest discover -s research/scripts
python research/scripts/validate_core_domain_contracts.py --contracts specs/behavior/core-domain-contracts.json --map specs/behavior/core-domain-dependency-map.md --behavior-dir specs/behavior --decision-dir specs/decisions --repo-root . --output local/generated/research/audit/core-contract-validation.json
git diff --check
```

## 集中处理结果与后置事项

### 实现前新增复核：内政证据仍有遗漏

RE-0095旧能力字段、相性概率及修筑资金上限已纠正：巡查用当前智力，商业／开垦用政治，修筑用统率；相性后备概率`5*(最大同相性人数+1)`，少于两人返回0；特殊组合另有逐对10%、根君主相性及身份门。29有序组合、最多五人、稳定降序和修筑加倍后仍每人扣50但不再受资金上限已静态闭合。

数量计算、组合契约、多人判定、周期经济及稳定存档已接入，见[BEH-DOMESTIC-0001](../specs/behavior/BEH-DOMESTIC-0001-base-development-effect.md)、[G03](../docs/g03-verification.md)、[G02](../docs/g02-verification.md)、[运行时架构](../docs/runtime-state.md)。自创君主增兵仍按D02排除。

### 决策 D01：繁中与日版1.3差异如何进入重制

**2026-09-06已决定：**繁中基线逐项吸收日版修正，见[DEC-PROJECT-0002](../specs/decisions/DEC-PROJECT-0002-selective-inheritance.md)。[RE-0150](findings/RE-SAN9PK-0150.md)的研究建议不等于整包批准；引用清理、零速率和空对象保护按领域不变量设计，离间公式／AI候选、混乱设施出阵、狙击同帧伤势、虚拟称帝及撤退优先级逐项明确取舍与验收，沿[计略规格](../specs/behavior/BEH-STRATEGY-0003-person-building-and-force-effects.md)版本／平衡门。已批准的登用失败调整有效。

### 决策 D02：自创武将君主的新局增兵是否纳入近期范围

**2026-09-06已决定：**暂不考虑，后续研究统一自创／历史规则；[DEC-SCOPE-0001](../specs/decisions/DEC-SCOPE-0001-scenario-scope.md)。原作700–799自创槽须担任君主，仅新局确认时对君主所在城增兵；当前历史快照不烘焙。若纳入，另定多城、兵役人口、上限及读档不重放验收。

### 实验 E01：定位截图资金9520→9076的444差额

**原因已闭合：**2026-09-06捕捉加载9520、27笔俸禄合计444、结算后9076；[RE-0157动态记录](findings/RE-SAN9PK-0157.md#资金差额受控调试2026-09-06第一轮)。用户未推进时间，首次下令前普通旬循环由RE-0145及RE-0098补证，非开局专属固定扣款。原截图完整来源、重复开局、兵粮日扣及年度收获尚未动态重建。

后续复验固定剧本／势力／设置，记录初次及下一旬下令前日期、钱粮和逐笔写入；显示包保持既定源值，真实模拟按周期规则执行，不烘焙444。

### 可选实验 E02：没收宝物与特定事件内容

- **没收不再需用户实验。**独立code17及3/2忠诚惩罚、候选／身份／菜单与执行复验、宝物79信望重算已闭合，见[RE-0112纠错](findings/RE-SAN9PK-0112.md#2026-09-06-独立没收命令的旧待办纠正)及BEH-PERSON-0002；区别于放逐自动没收。
- **蔡琰实验后置。**2026-09-06用户指定正式接入时再做`EXP-EVT-CONTENT-01`：195/207年、资金3000/3001、能力99/100及兵法组合，每例固定随机重复三次，方案见[RE-0130](findings/RE-SAN9PK-0130.md)；不自动启动。

## 战争后续证据边界（2026-09-13）

G05–G07确认差异已入规格并实施。[驻军默认阵型](findings/RE-SAN9PK-0068.md#garrison-default-formation)初始化／加载及普通攻防系数10来源已闭合；异常命令／状态的全部自然可达性未证明。重制采用独立驻军配置，TC101导入读取正阵攻防，不复制原作有效命令／状态查询；采用边界见WAR-0002，阶段分工见[G07验收](../docs/g07-verification.md)。

## 2026-09-13新增：地形与实际移动速度

**开放，待原作动态对照。**每节点扣10已有静态证据；不同地形仍可能通过输入机动力、局部步长、阵型切换或特殊减半分支影响实际速度，不能由同循环扣费推为地形同速。实验控制见[RE-0136](findings/RE-SAN9PK-0136.md#不同地形实际速度是否相同2026-09-13待验证)。当前固定步进保留，不自动启动实验或推进G08。

## 基础能力资格门与成长上限

**2026-09-20决定仅记录。**蔡琰探索资格要求基础智力、政治均<115，奖励成长上限为100，二者用途不同；115设计意图未明，不能判原作BUG或通用成长上限。见[RE-0130](findings/RE-SAN9PK-0130.md)、[事件规格](../specs/behavior/BEH-EVENT-0002-event-selection-and-commit.md#蔡琰归还)。提高能力上限时统一盘点资格门、奖励截断、通用成长、内容校验和边界测试，区分玩法资格与防越界限制。

## G08保留疑点与证据边界

- **2026-09-18决定保留：**不随降者对接受方忠诚L>45时增加`min(L-45,5)`，仍在野；设计意图未明。随降概率另读原势力忠诚，见[RE-0118](findings/RE-SAN9PK-0118.md)。
- **2026-09-16决定保留：**流言关系减少`min(20,100-关系)`，80减20、99减1、100不减；见[RE-0115](findings/RE-SAN9PK-0115.md)。
- **外交途中君主移动待验证：**提交时目的地已确认，后续是否追踪未闭合；重制保存路径快照。

拒收退款与免费返还军师意见已采用修正，见[外交规格](../specs/behavior/BEH-DIPLOMACY-0002-command-negotiation-and-resolution.md)、[G08验收](../docs/g08-verification.md)；原作事实保留于RE-0117／0118。
