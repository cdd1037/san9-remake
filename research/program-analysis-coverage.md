# 核心程序分析来源覆盖账本

- 状态：`EVT-01` 最终范围路由已完成；R0–R7 来源处置保留为已验收基线
- 适用样本：`SRC-SAN9PK-TC-101`
- 关联记录：[`RE-SAN9PK-0091`](findings/RE-SAN9PK-0091.md)
- 更新日期：2026-08-25

本账本把 SanIX、电子书和 San9PKHard 的主张映射到现有证据与
[`program-analysis-roadmap.md`](program-analysis-roadmap.md) 的程序分析批次。`EVT-01` 在不改写既有来源上下文的前提下，把原先按 R0–R7 范围合并排除的复合条目拆成 R8–R12 原子主张；机器可读总数与 San9PKHard 逐候选路由见 [`program-analysis-evt-01-status.json`](program-analysis-evt-01-status.json)。本账本只回答“来源说了什么、目前证据到哪里、下一步归谁”，不把低优先级资料直接升格为原作规则。

## 账本模型

领域对象是“来源主张单元”，而不是文件或代码行。一个单元必须具有稳定 ID、来源位置、主题、证据状态、处置方式、关联记录或明确缺口。

- `status` 只使用路线图状态模型；`confirmed` 表示已有当前样本事实链，不表示来源全文正确。
- `disposition=baseline` 表示只链接已完成研究，不再次进入队列；`roadmap` 表示由指定批次继续；`duplicate` 表示只保留独立来源位置并复用原主张路由；`out-of-scope` 表示本轮明确不处理。
- SanIX 和电子书按可独立裁决的公式、流程或观察拆分；电子书的重复转载仍保留独立来源位置，但通过 `duplicate-of` 指向同一主张。
- San9PKHard 本批只覆盖主题与承载文件。地址字段、写入点、旧字节和 thunk 的逐项账本属于 `COV-02`，不能以本表的主题覆盖替代。

不变量：每个在途主张恰好归属一个后续批次；已完成公式与战术 AI 只能是 `baseline`；范围外条目不能出现在路线候选队列；第三方补丁意图不能覆盖当前样本机器事实。

## SanIX 主张覆盖

来源：`research/docs/程序分析、设计公式、优化方法/SanIX_程序分析.md`。

| ID | 来源位置与主张 | 主题 | status | disposition | 关联证据 / 明确缺口 |
| --- | --- | --- | --- | --- | --- |
| `SIX-DOM-01` | 一.1：总人口、总兵役及逐城分配模型 | 城市人口周期 | `confirmed` | `baseline` | `FML-SAN9PK-0014`、`RE-SAN9PK-0067`；城市周期调度边界另见 `SIX-DOM-02` |
| `SIX-DOM-02` | 一.1：人口变动发生周期及与内政指令衔接 | 城市周期调度 | `confirmed` | `completed:DOM-04` | `RE-SAN9PK-0098` 已闭合 1/4/7/10 月 1 日、至少经过 30 日和普通模式门；内政效果即时写回，使用/行动位另在旬结算清除 |
| `SIX-DOM-03` | 一.2：巡查、商业、开垦、修筑共享效果模型 | 内政四指令 | `confirmed` | `baseline` | 当前样本整数顺序及资料公式冲突见 `RE-SAN9PK-0095`；原 `FML-SAN9PK-0029` 链接属于能力成长公式，不能支持本项 |
| `SIX-DOM-04` | 一.2：前四指令的参与者、资源、经验与执行写回 | 内政四指令 | `confirmed` | `completed:DOM-01` | 玩家/AI 共享命令、复验、费用、即时写回和修筑熟练度由 `RE-SAN9PK-0095` 闭合；旬级使用/行动位清除由 `RE-SAN9PK-0098` 闭合 |
| `SIX-DOM-05` | 一.2：征兵数量模型 | 征兵 | `confirmed` | `completed:DOM-02` | 当前样本整数顺序、兵役人口与资金夹取见 `RE-SAN9PK-0096`；原 `FML-SAN9PK-0029` 链接属于基础能力成长，不能支持本项 |
| `SIX-DOM-06` | 一.2：征兵与训练的资源、上限、经验和失败边界 | 征兵与训练 | `confirmed` | `completed:DOM-02` | 玩家/AI 共享命令、复验、资源、上限、熟练度与竞争窗口由 `RE-SAN9PK-0096` 闭合；旬级使用/行动位清除由 `RE-SAN9PK-0098` 闭合 |
| `SIX-DOM-07` | 一.2：训练规范统率与士气增量模型 | 训练 | `confirmed` | `completed:DOM-02` | 当前样本的信望输入、规范统率和机器整数顺序见 `RE-SAN9PK-0096`；原 `FML-SAN9PK-0029` 链接不能支持本项 |
| `SIX-DOM-08` | 一.2：买进与卖出属于八项内政指令 | 交易 | `confirmed` | `completed:DOM-03` | 玩家入口、商人条件、数量/资源边界见 `RE-SAN9PK-0097`；共享交易使用位清除及商人/市价旬刷新顺序见 `RE-SAN9PK-0098`；标准自动内政工厂没有动作码 6、7 |
| `SIX-STR-01` | 一.3：计略和外交为确定公式、无概率 | 计略与外交 | `conflict` | `baseline` | `RE-SAN9PK-0113..0116` 已否定八类计略“无概率”；`RE-SAN9PK-0117/0118` 确认赠与、攻击请求和劝降消费随机，俘虏返还虽保存最终随机字段却不消费；`RE-SAN9PK-0119` 确认联盟形成经过季度 `rnd(4)==0` 门；`RE-SAN9PK-0120` 又确认攻击请求来源 50% 门和俘虏返还规划随机，外交必须按阶段逐项裁决，DIP-04 已清账 |
| `SIX-DUEL-01` | 二.1：逐刀按看破、动画必杀、双倍必杀、普通攻击短路 | 单挑逐刀状态机 | `confirmed` | `completed:DUEL-02` | `RE-SAN9PK-0104` 确认 outcome `0/3/2/1` 的严格短路、核心 1/4 次随机抽样及看破后额外 `8%→4%` 表现随机 |
| `SIX-DUEL-02` | 二.1：规范武力、四类概率与伤害公式 | 单挑伤害 | `confirmed` | `completed:DUEL-02` | `RE-SAN9PK-0104` 从 `resolve_duel_strike` 闭合机器式；资料的看破括号、动画差值和基础随机分母分别与机器冲突 |
| `SIX-DUEL-03` | 二.1：吕布、赵云、马超特殊分支 | 单挑特殊人物 | `confirmed` | `completed:DUEL-02` | `RE-SAN9PK-0104/0106` 确认人物 `+0x3C` 列传资源码 `3/9/4` 的 40% 看破、33 基础上限和 24 双倍上限；该字段不是人物 ID，基础列传资源分别给出吕布、赵云、马超标签 |
| `SIX-DUEL-04` | 二.1：主动发起方固定先砍 | 单挑先手 | `confirmed` | `completed:DUEL-01` | `RE-SAN9PK-0103` 确认双方首选者置于表首，`0x50409C` 先扣接受方 HP，存活后才在 `0x50418F` 还击 |
| `SIX-DUEL-05` | 二.2：君主/总大将/四能力门槛与最大 30% 阵亡 | 单挑阵亡 | `conflict` | `completed:DUEL-03` | `RE-SAN9PK-0105` 确认主将、蛮族、场景/开关、四基础能力最大值 80 门与 `min(30,80-当前有效武力)`；当前样本没有独立君主免疫，资料的“君主不能阵亡”扩写与机器冲突 |
| `SIX-DUEL-06` | 二.2：失败、阵亡、伤势和人物状态最终写回 | 单挑结束 | `confirmed` | `completed:DUEL-03` | `RE-SAN9PK-0105/0108` 闭合胜负名单、部队士气派生的双方间隔、阵亡→俘虏→至少 7 伤势分区、俘虏状态/槽位/链接、临时位清零及士气 1..100 写回；错误成长门登记为原版 bug |
| `SIX-DUEL-07` | 二.3：阵位、阵形、性格和武力限制决定触发 | 单挑触发 | `confirmed` | `completed:DUEL-01` | `RE-SAN9PK-0103` 闭合发起/接受两套资格、最高当前武力遮蔽、`3×阵形倍率×前排人数`、性格阈值及普通兵法骰独立性 |
| `SIX-TAC-01` | 三.一.1.1：普通攻击次数产生兵法机会，地域/高昂修正 15–30% | 兵法机会 | `confirmed` | `completed:TAC-01` | `RE-SAN9PK-0099` 闭合每日类型 38 普攻入口、双机会任务、15+10+5 概率和成功候选列表写入；1–3 次/日仅为观察值 |
| `SIX-TAC-02` | 三.一.1.2：按健康、间隔、阵位、人数、阵形和类别分配机会 | 陆战候选分配 | `confirmed` | `completed:TAC-02` | `RE-SAN9PK-0100` 闭合当前间隔/健康谓词、前中后顺序、人数与阵位系数、四类有利阵形、谋略/策略无阵形乘数及逐项消费 |
| `SIX-TAC-03` | 三.一.1.2：特定编队只检查锋线并丢失机会 | 陆战提前终止 | `confirmed` | `completed:TAC-02` | `RE-SAN9PK-0100` 确认候选谓词会接纳无有效兵法及不受陆战工厂支持的类别；队首人物先弹出，空任务返回会立即结束整次机会并遮蔽后排 |
| `SIX-TAC-04` | 三.一.2：水军兵法发动 | 水战调度 | `confirmed` | `completed:TAC-03` | `RE-SAN9PK-0101` 确认健康/间隔候选、单次 10% 门、等概率发动者、水上类型到兵法任务映射及后置资格；没有陆战阵位/人数分配骰 |
| `SIX-TAC-05` | 三.一.3：设施兵法发动 | 设施调度 | `confirmed` | `completed:TAC-03` | `RE-SAN9PK-0101` 确认单次 10% 门、虚表 `+0xBC` 负责人、仅当前间隔门及固定/首个启用设施兵法工厂；原版不遍历全员选优 |
| `SIX-TAC-06` | 三.二.1–6：兵法伤害、抵挡、动画、成长、联动 | 兵法效果公式 | `confirmed` | `baseline` | 数值见 `FML-SAN9PK-0018..0024/0028`、`RE-SAN9PK-0068`；`RE-SAN9PK-0101/0102` 补齐结果任务、间隔、表现和成长顺序；`RE-SAN9PK-0125` 另闭合三项谋略结果到新武力任务的跨类别连锁、后续普通联动和 58 项特殊关系配对 |
| `SIX-TAC-07` | 三.二.2：普通攻击伤害 | 普通战斗 | `confirmed` | `baseline` | `FML-SAN9PK-0015/0016`、`RE-SAN9PK-0068` |
| `SIX-AI-01` | 四.一：防守战术 AI | 战术 AI | `confirmed` | `baseline` | `RE-SAN9PK-0075` 至 `RE-SAN9PK-0088` 覆盖矩阵；不重复入队 |
| `SIX-AI-02` | 四.二：攻击战术 AI | 战术 AI | `confirmed` | `baseline` | `RE-SAN9PK-0075..0090` 覆盖矩阵；不重复入队 |

## 电子书主张覆盖

来源：`research/docs/三国志9电子书.md`。下表先列 R0–R3 相关或已有证据的章节，再对其余章节做显式范围处置。

| ID | 来源章节 | 主题 | status | disposition | 关联证据 / 明确缺口 |
| --- | --- | --- | --- | --- | --- |
| `EBK-TAC-01` | 2-7-1、6-14：普通攻击/人物日处理产生兵法机会 | 兵法机会 | `conflict` | `completed:TAC-01` | `RE-SAN9PK-0099` 确认陆战机会由每日类型 38 普攻事件产生并按源部队掷一次；“逐项扫描 850 人直接给机会”不适用于当前样本 |
| `EBK-TAC-02` | 2-7-1、6-14：健康、间隔和随机门筛选陆战发动者 | 陆战候选分配 | `confirmed` | `completed:TAC-02` | `RE-SAN9PK-0100` 确认人物 `+0x80/+0x98 <= 0`、阵位升序、队首消费及类别概率；无效/不支持类别不是普通概率失败，而会终止整次机会 |
| `EBK-TAC-08` | 2-7-2、6-12：部队兵法最小间隔表 | 陆战最小间隔 | `confirmed` | `completed:TAC-04` | `RE-SAN9PK-0102` 以加载路径和库存 `hiho` 表确认逐项 0/6/9/15 值、每日预命令饱和递减及最小日期差；旬次数仍由普通攻击事件与随机门决定 |
| `EBK-TAC-09` | 2-7-2、6-12：设施及联动最小间隔 | 设施/发动后写回 | `confirmed` | `completed:TAC-04` | `RE-SAN9PK-0101/0102` 确认设施负责人承载间隔、设施武力固定 3、其余读取实际结果记录；联动是基础值整数除 3，故数值为 2/3/5 而非恒为 3 |
| `EBK-TAC-10` | 2-7-2、6-12：单挑间隔及三类间隔总结 | 资料校核 | `conflict` | `completed:TAC-04` | `RE-SAN9PK-0102/0108` 确认单挑写回为所属部队当前士气的非线性分值派生式并上限夹 10，不是固定 5 或 6；完整参与者与战后链归 `DUEL-03/04` |
| `EBK-SIX-01` | 2-7-3：SanIX 程序分析全文转载 | 内政、单挑、兵法、AI | `completed` | `duplicate` | `duplicate-of:SIX-*`；单挑部分已拆为下列七项，其余按 SanIX 表逐项复用，不生成第二套候选 |
| `EBK-DUEL-01` | 2-7-3.二.1：逐刀四类结果短路 | 单挑逐刀状态机 | `confirmed` | `duplicate` | `duplicate-of:SIX-DUEL-01`；`RE-SAN9PK-0104/0107` |
| `EBK-DUEL-02` | 2-7-3.二.1：规范武力、概率与伤害公式 | 单挑伤害 | `confirmed` | `duplicate` | `duplicate-of:SIX-DUEL-02`；当前行为已闭合，但转载的看破括号、动画差值和基础随机分母与机器冲突，见 `RE-SAN9PK-0104/0107` |
| `EBK-DUEL-03` | 2-7-3.二.1：吕布、赵云、马超特殊分支 | 单挑特殊人物 | `confirmed` | `duplicate` | `duplicate-of:SIX-DUEL-03`；分支成立但身份域是列传资源码，见 `RE-SAN9PK-0106/0107` |
| `EBK-DUEL-04` | 2-7-3.二.1：主动方固定先砍 | 单挑先手 | `confirmed` | `duplicate` | `duplicate-of:SIX-DUEL-04`；`RE-SAN9PK-0103/0107` |
| `EBK-DUEL-05` | 2-7-3.二.2：君主/总大将/四能力与阵亡上限 | 单挑阵亡 | `conflict` | `duplicate` | `duplicate-of:SIX-DUEL-05`；独立君主免疫与当前样本冲突，见 `RE-SAN9PK-0105/0107` |
| `EBK-DUEL-06` | 2-7-3.二.2：失败、阵亡与最终人物状态 | 单挑结束 | `confirmed` | `duplicate` | `duplicate-of:SIX-DUEL-06`；来源覆盖不足的俘虏、重伤和士气链由 `RE-SAN9PK-0105/0107` 补齐 |
| `EBK-DUEL-07` | 2-7-3.二.3：阵位、阵形、性格和武力限制 | 单挑触发 | `confirmed` | `duplicate` | `duplicate-of:SIX-DUEL-07`；当前行为已闭合，三类武力门成立但接受方间隔推广与机器冲突，见 `RE-SAN9PK-0103/0107` |
| `EBK-DOM-01` | 3-1：基础心得中的内政收益与城市经营观察 | 内政经验观察 | `inferred` | `completed:DOM-04` | `RE-SAN9PK-0098` 已完成 R1 清账；按旬感知行动恢复与静态旬清除相容，其余攻略、委任和因果主张不升格 |
| `EBK-DOM-02` | 3-2：投资贸易和粮食回报 | 探索投资事件 | `confirmed` | `completed:DOM-03` | `RE-SAN9PK-0097` 确认它不是日常买卖；事件 `0x5801CC` 直接实现净资金 `floor(a/2)+4b`、粮食 `P(floor(3a/2)+4b)` 和最多现有资金 20% 的选择边界 |
| `EBK-DOM-03` | 3-6、6-3：兵役人口影响因素 | 人口与兵役周期 | `conflict` | `completed:DOM-04` | 3-6 的“每一季”和全局兵力观察与 `RE-SAN9PK-0098` 相容；6-3 的“80.5 万后每旬递减”与季度唯一入口及 100 万写回上限冲突 |
| `EBK-GROW-01` | 3-9：能力上升公式与发生条件 | 能力成长 | `completed` | `RE-SAN9PK-0131/0134` | 普通 3% 成长、直接奖励旁路、生产者和写回已闭合；单挑低武力胜者成长在正常状态下不可达，人物与地点建议排除 |
| `EBK-PRES-01` | 3-10：信望计算公式 | 信望 | `confirmed` | `baseline` | `FML-SAN9PK-0029`、`RE-SAN9PK-0067` |
| `EBK-TAC-03` | 6-1：兵法习得熟练度要求 | 兵法数据 | `confirmed` | `baseline` | 记录布局见 `RE-SAN9PK-0015`，成长边界见 `RE-SAN9PK-0068` |
| `EBK-TAC-04` | 6-2：兵法阻挡 | 兵法抵挡 | `confirmed` | `baseline` | `FML-SAN9PK-0019/0023`、`RE-SAN9PK-0068` |
| `EBK-TAC-05` | 6-4：兵法之极意中的伤害、联动、成长主张 | 兵法效果 | `confirmed` | `baseline` | `FML-SAN9PK-0018..0024/0028`；频率与调度子主张仍随 `EBK-TAC-01/02` |
| `EBK-TAC-06` | 6-5、6-6：实战兵法与阵法经验 | 战术经验观察 | `inferred` | `completed:TAC-04` | `RE-SAN9PK-0102` 将“一旬约一次”等频率保留为经验观察；当前状态机没有固定次数发生器，实际值由普攻事件、机会门、候选、冷却和激励共同产生 |
| `EBK-TAC-07` | 6-13：联动伤害实验 | 兵法联动伤害 | `confirmed` | `baseline` | 机器式见 `FML-SAN9PK-0020/0021`、`RE-SAN9PK-0068`；实验数值仅为佐证 |

### 电子书 R4–R7 路由

以下主张原在 `EBK-EXC-03` 中整体标为 R0–R3 范围外。`PERS-01` 按可独立裁决的状态与行为重新拆分；状态仍为来源观察，不能据此确认原作规则。

| ID | 章节与主张 | 主题 | status | disposition | 关联证据 / 明确缺口 |
| --- | --- | --- | --- | --- | --- |
| `EBK-GOV-01` | 3-7：都督选择与城市辖区负责人 | 军团负责人和辖区控制 | `confirmed` | `completed:GOV-02` | `RE-SAN9PK-0122` 确认默认都督从所选城市在职人物产生、排除君主，并按身份、武官官职顺序和统率排序；城市、地域建筑、人物有效军团和部队归属链已闭合 |
| `EBK-GOV-02` | 3-7：委任方针、武将分布与自动内政观察 | 委任与旬级自动治理 | `conflict` | `completed:GOV-04` | `RE-SAN9PK-0123/0124` 确认委任自动工作、人事码和直属范围；方针不直接等于工作表，自动调动按建筑人员盈余/短缺与人物评分，当前链不支持“按兵力简单比例分配武将” |
| `EBK-PER-01` | 3-8：政治、忠诚、信望、义理、相性和俘虏状态影响登用 | 登用判定 | `confirmed` | `completed:PERS-03` | `RE-SAN9PK-0067/0111` 闭合公式 2 的精确整数式、code 24 实时阈值和严格 `random < threshold`；攻略方向成立，精确式不得简化为“政治对忠诚” |
| `EBK-PER-02` | 3-11：对各势力忠诚、失败登用好感及再次登用门槛 | 登用与归属初始化 | `confirmed` | `completed:PERS-03` | `RE-SAN9PK-0111` 确认成功后旧势力忠诚 45；失败仅以条件概率对执行势力忠诚 +1，特定在职目标还可能抵消，故“失败必然增加好感”已修正 |
| `EBK-PER-03` | 3-11：俘虏忠诚恢复、没收宝物、叛离后忠诚和新仕官忠诚 | 忠诚周期与奖惩 | `confirmed` | `completed:PERS-04` | `RE-SAN9PK-0111/0112` 确认新仕官初始化、旧势力 45，以及俘虏每旬按 `15*d²+10` 阈值成功后对当前势力忠诚 +1；放逐非俘虏写 45、俘虏改为 +义理/5。“放逐必没收宝物”和通用默认 50 无机器支持，明确排除出已确认规格 |
| `EBK-GOV-03` | 3-12：委任军团登用后的召回、控制权切换和低忠诚处置 | 委任军团人事权限 | `confirmed` | `completed:GOV-04` | `RE-SAN9PK-0111/0112/0124` 确认子军团自动登用/调动、共享召回写回和直属控制范围；“忠诚 90”“过一旬安全”只保留为攻略建议，不是机器保证 |
| `EBK-GOV-04` | 3-7：低忠诚大将被登用或自立后连带城市跨势力 | 旬级人事与辖区失控 | `confirmed` | `completed:GOV-04` | `RE-SAN9PK-0124` 确认既有对象叛离和城市自立可带城；城市自立还要求概率成功、对象负责人、城市类型和空闲势力槽，同根调动或普通低忠诚人物不会自行带城 |

### 电子书范围外章节

下列章节均已覆盖登记，但路线图明确不处理其主张；状态只描述来源性质，不表示已确认原作行为。

| ID | 章节 | 主题 | status | disposition | 理由 |
| --- | --- | --- | --- | --- | --- |
| `EBK-EXC-01` | 2-1、2-2 | 头像制作与导入 | `inferred` | `out-of-scope` | 资源/UI，不属核心模拟；已由 `RE-SAN9PK-0127` 明确转入非核心内容/工具边界 |
| `EBK-EVT-01` | 3-3、3-4：内政对话与猛兽探索观察 | 探索事件族 | `conflict` | `completed:EVT-04` | 内政组合对话属于表现内容；猛兽条目的奋斗/鼓舞门成立，但“苍梧”与当前样本及本书 5-2 的“建安”冲突，“90/100 以上反而易败”也与单调不减的 `floor(有效武力/5)` 阈值冲突；见 `RE-SAN9PK-0027/0130` |
| `EBK-DATA-01` | 3-5：宝物表 | 静态内容数据 | `inferred` | `out-of-scope` | 宝物运行时记录已有 `RE-SAN9PK-0006`；逐项表值属内容导入，不是事件引擎规则 |
| `EBK-LIFE-01` | 4-1：小兵培养阶段、能力与兵法结果 | 小兵培养 | `completed` | `RE-SAN9PK-0133/0134` | 机器已闭合触发、激活、九旬阶段、成长和最终归属；建设完成触发无当前静态生产者，人物评价、配对建议和逐项事件文本不提升为规则 |
| `EBK-ADV-01` | 4-2、4-3、4-4、4-5：人物评价、联动配对和武将攻略 | 玩家攻略 | `inferred` | `out-of-scope` | 排行、组合优劣和推荐阵容是主观内容；4-3 的可验证关系只复用 `FML-SAN9PK-0018`，名单不产生隐藏规则；见 `RE-SAN9PK-0134` |
| `EBK-EVT-02` | 5-1：十二个历史事件的触发与效果清单 | 历史事件族 | `inferred` | `completed:EVT-04` | 12 个名称和逐条条件是内容目录，不等同于 39 个可构造对象类型；三顾之礼多阶段对象、山阳公及季度/探索生产边界已有当前样本佐证，其余逐条文本与参数转内容导入；见 `RE-SAN9PK-0028/0129/0130` |
| `EBK-EVT-03` | 5-2：探索事件优先度、条件、一次性与选择效果 | 探索事件引擎 | `inferred` | `completed:EVT-04` | 表序、短路随机、一次性位和提交阶段已确认；32 个玩家条目不是对象类型表，三顾之礼多个阶段解释了开头 `2,3,9` 与“先三顾、后山阳公”的关系；逃走无奖励但仍在提交阶段消费地点位；见 `RE-SAN9PK-0128/0130` |
| `EBK-EVT-04` | 5-3、5-4：山阳公与蔡琰归汉 | 代表性历史/探索事件 | `conflict` | `completed:EVT-04` | 山阳公的信望、领地、献帝、选择和外交方向有机器支持，但处斩分支对筛选人物调用忠诚 `+20`，与资料“忠诚下降”冲突；蔡琰的逐项能力/兵法顺序未作为核心引擎规格，转内容验证；见 `RE-SAN9PK-0129/0130` |
| `EBK-DATA-02` | 5-5、5-6：训练与地域表 | 静态内容数据 | `inferred` | `out-of-scope` | 表值属于内容导入；只有被成长或事件引擎消费的字段语义进入对应批次 |
| `EBK-WAR-01` | 6-7..6-11、6-15..6-22、6-24：移动、攻城、撤退、四夷和微操观察 | 战争观察 | `completed` | `RE-SAN9PK-0136` | 设施占地阻挡、路点与每日重寻路方向由当前样本支持；目标消失后的具体命令结果不得从单次玩家观察泛化，转 `WAR-02..04`；纯战术建议排除 |
| `EBK-WAR-02` | 同上：攻城、占领、撤退、异民族与战后结果 | 战争收尾观察 | `completed` | `RE-SAN9PK-0137/0138/0139` | 到达、占领、资源、人物和引用清理由当前样本裁决；异民族失势非俘虏直接死亡得到机器支持；阵容、微操和四夷打法属于玩家攻略，不提升为规则 |
| `EBK-GROW-02` | 6-23、7-3：全兵法与精通习得经验 | 兵法习得 | `completed` | `RE-SAN9PK-0131/0134` | 精通三倍、1000 上限、记录门槛、30%–70% 学习概率、成员随机顺序和直接授予已闭合；不存在 31 项总量门，人物/地点路线排除 |
| `EBK-SCN-01` | 7-2：结局条件与结果组合 | 结局路由 | `conflict` | `completed:SCN-04` | 两方针、四阶段、八结果、阈值与继任谓词已由当前样本确认；“统一前消灭”实际读取当前异民族存续，反推的大陆分裂条件有一处熟练度不等号冲突；结果只写临时视图，逐条文案转内容层；见 `RE-SAN9PK-0143` |
| `EBK-EXC-08` | 10-1、10-2 | 自制剧本 | `inferred` | `out-of-scope` | 工具与内容创作，不属本轮；已由 `RE-SAN9PK-0127` 明确转入非核心边界 |

## San9PKHard 主题覆盖

来源：`research/upsteam/San9PKHard/`。这里登记源码所承载的候选主题，不能据此断言补丁描述就是原版语义。

| ID | 承载位置 | 主题 | status | disposition | 关联证据 / 明确缺口 |
| --- | --- | --- | --- | --- | --- |
| `HARD-INFRA-01` | `San9PKHard.cpp`、`San9PKHardDlg.cpp/.h`、`ReadWriteError.h` | 进程附着、配置装载与写入编排 | `completed` | `completed:COV-02` | `RE-SAN9PK-0092/0093` 已完成全文件抽取、写入方式分类与当前样本代表性核验；纯工具路径由队列归入非核心 |
| `HARD-PARA-01` | `Parasettingdlg.cpp/.h`、`San9PKHardPara.txt` | 全局难度、系统和数值参数 | `completed` | `completed:COV-02` | `RE-SAN9PK-0092..0094` 已按 DDX、成员、写入点和队列主题完成候选登记与路由 |
| `HARD-PERSON-01` | `PersonConfigDlg.cpp/.h`、`San9PKHardPerson.txt` | 人物能力、兵法与关系配置 | `completed` | `completed:COV-02` | `RE-SAN9PK-0092..0094` 已区分配置、静态数据与运行时补丁候选并完成路由 |
| `HARD-CITY-01` | `CityConfigDlg.cpp/.h`、`San9PKHardCity.txt` | 城市战法、收益、耐久和商人配置 | `completed` | `completed:COV-02` | `RE-SAN9PK-0092..0094` 已登记并路由至内政、战争或非核心处置；相关核心批次均已闭合 |
| `HARD-ARRAY-01` | `ArrayConfigDlg.cpp/.h`、`San9PKHardArray.txt` | 阵形参数 | `completed` | `completed:COV-02` | `RE-SAN9PK-0092..0094` 已登记并路由；阵形、战术 AI 与兵法调度核心项均已有闭合记录 |
| `HARD-SKILL-01` | `San9PKHardSkill.txt`、相关 DDX/写入调用 | 兵法参数 | `completed` | `completed:COV-02` | `RE-SAN9PK-0092..0094` 已恢复候选索引、写入类别与路线处置；核心兵法项由公式与 R2 闭合 |
| `HARD-PATCH-01` | `InsertAsm.h`、`InsertAsmSrc.h`、`InsertAsmSrc2.h`、`InsertAsmSrc3.h` | 地址字段、直接写入、旧字节、thunk 和注入体 | `completed` | `completed:COV-02` | `RE-SAN9PK-0092/0093` 已完成逐候选抽取、旧字节/地址异常检查和当前样本代表性回读 |
| `HARD-INIT-01` | `InitData.h` | 原始关系、爵位/俸禄、阵形图像及帮助文本 | `inferred` | `out-of-scope` | 静态替换数据、图像和帮助文本不属核心模拟；其中可解析写入候选已由 `COV-02/03` 纳入守恒账本 |
| `HARD-DOM-01` | `InsertAsm*.h` 中 commerce、cultivate、draft、population、city update 等组 | 内政与城市周期补丁候选 | `completed` | `completed:COV-02` | `COV-02/03` 已定位并路由，R1 已按当前样本控制流闭合对应核心项；第三方注入语义未直接采用 |
| `HARD-TAC-01` | `InsertAsm*.h` 中 tactic/killer/linkage/proficiency/water/facility 等组 | 兵法效果与发动补丁候选 | `completed` | `completed:COV-02` | `COV-02/03` 已定位并路由，公式基线与 R2 已闭合核心效果和调度项 |
| `HARD-DUEL-01` | `InsertAsm*.h` 中 duel/dead/general/person-death 等组 | 单挑与阵亡补丁候选 | `completed` | `completed:COV-02` | `COV-02/03` 已定位并路由，R3 已闭合触发、逐刀和战后写回 |
| `HARD-AI-01` | `InsertAsm*.h` 中 attack/defence/dispatch/array/retreat 等组 | 战术 AI 补丁候选 | `confirmed` | `baseline` | `RE-SAN9PK-0075` 至 `RE-SAN9PK-0090`；COV-02 仍登记地址，但不重新研究 AI 决策 |
| `HARD-OTHER-01` | `InsertAsm*.h` 中 UI、快捷键、外交、事件、军团、战争迷雾等组 | 路线范围外补丁候选 | `inferred` | `out-of-scope` | 2635 项候选已由 `COV-02/03` 全量登记；核心交集已进入后续批次，其余 UI、注入基础设施和不改变核心状态者明确非核心 |
| `HARD-BUILD-01` | `.dsp`、`.dsw`、`.clw`、`.aps`、`.ncb`、`.opt`、`.plg`、`.rc`、`resource.h`、`res/`、`StdAfx.*` | MFC 构建、IDE 状态与界面资源 | `inferred` | `out-of-scope` | 构建、IDE 与界面资源不承载核心程序规则；其中地址/旧字节字面量已由 `COV-02` 校验器覆盖 |

## 后续批次入口

- `COV-02`：已由 [`san9pkhard-catalog-status.json`](patch-points/san9pkhard-catalog-status.json)、[`san9pkhard-ida-audit.json`](patch-points/san9pkhard-ida-audit.json)、[`RE-SAN9PK-0092`](findings/RE-SAN9PK-0092.md) 与 [`RE-SAN9PK-0093`](findings/RE-SAN9PK-0093.md) 完成全文件抽取及 IDA MCP 当前样本核验；完整逐项账本可按状态清单中的命令重建到 `tmp/`。
- `COV-03`：已由 [`program-analysis-queue-status.json`](program-analysis-queue-status.json)、[`program-analysis-queue-ida-audit.json`](program-analysis-queue-ida-audit.json) 与 [`RE-SAN9PK-0094`](findings/RE-SAN9PK-0094.md) 完成机器可校验的候选队列；完整队列可按状态清单中的命令重建到 `tmp/`。
- `DOM-01`：从 `SIX-DOM-03/04`、`EBK-DOM-01` 和 `HARD-DOM-01` 开始，不重复公式 29。
- `TAC-01`：从 `SIX-TAC-01`、`EBK-TAC-01` 开始，以 `FML-SAN9PK-0017` 的普攻事件事实为入口。
- `DUEL-04`：已由 `RE-SAN9PK-0107/0108` 完成七项 SanIX/电子书重复主张清账和动态补充裁决，并形成 `BEH-DUEL-0001..0003`；间隔输入已闭合为部队士气，错误成长门明确留在研究层作为原版 bug。
