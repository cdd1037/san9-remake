# 核心程序分析来源覆盖账本

- 状态：`COV-01` 已完成
- 适用样本：`SRC-SAN9PK-TC-101`
- 关联记录：[`RE-SAN9PK-0091`](findings/RE-SAN9PK-0091.md)
- 更新日期：2026-08-24

本账本把 SanIX、电子书和 San9PKHard 的主张映射到现有证据与
[`program-analysis-roadmap.md`](program-analysis-roadmap.md) 的 R0–R3 批次。它只回答“来源说了什么、目前证据到哪里、下一步归谁”，不把低优先级资料直接升格为原作规则。

## 账本模型

领域对象是“来源主张单元”，而不是文件或代码行。一个单元必须具有稳定 ID、来源位置、主题、证据状态、处置方式、关联记录或明确缺口。

- `status` 只使用路线图状态模型；`confirmed` 表示已有当前样本事实链，不表示来源全文正确。
- `disposition=baseline` 表示只链接已完成研究，不再次进入队列；`roadmap` 表示由 R0–R3 的指定批次继续；`duplicate` 表示只保留独立来源位置并复用原主张路由；`out-of-scope` 表示本轮明确不处理。
- SanIX 和电子书按可独立裁决的公式、流程或观察拆分；电子书的重复转载仍保留独立来源位置，但通过 `duplicate-of` 指向同一主张。
- San9PKHard 本批只覆盖主题与承载文件。地址字段、写入点、旧字节和 thunk 的逐项账本属于 `COV-02`，不能以本表的主题覆盖替代。

不变量：每个在途主张恰好归属一个后续批次；已完成公式与战术 AI 只能是 `baseline`；范围外条目不能出现在 R0–R3 候选队列；第三方补丁意图不能覆盖当前样本机器事实。

## SanIX 主张覆盖

来源：`research/docs/程序分析、设计公式、优化方法/SanIX_程序分析.md`。

| ID | 来源位置与主张 | 主题 | status | disposition | 关联证据 / 明确缺口 |
| --- | --- | --- | --- | --- | --- |
| `SIX-DOM-01` | 一.1：总人口、总兵役及逐城分配模型 | 城市人口周期 | `confirmed` | `baseline` | `FML-SAN9PK-0014`、`RE-SAN9PK-0067`；城市周期调度边界另见 `SIX-DOM-02` |
| `SIX-DOM-02` | 一.1：人口变动发生周期及与内政指令衔接 | 城市周期调度 | `planned` | `roadmap:DOM-04` | 缺入口、执行日期和与旬刷新先后顺序 |
| `SIX-DOM-03` | 一.2：巡查、商业、开垦、修筑共享效果模型 | 内政四指令 | `confirmed` | `baseline` | 公式值见 `FML-SAN9PK-0029`、`RE-SAN9PK-0067`；命令生命周期见 `SIX-DOM-04` |
| `SIX-DOM-04` | 一.2：前四指令的参与者、资源、经验与执行写回 | 内政四指令 | `planned` | `roadmap:DOM-01` | 公式研究未闭合命令创建、验证、结算时点和全部写回 |
| `SIX-DOM-05` | 一.2：征兵数量模型 | 征兵 | `confirmed` | `baseline` | `FML-SAN9PK-0029`、`RE-SAN9PK-0067`；命令边界见 `SIX-DOM-06` |
| `SIX-DOM-06` | 一.2：征兵与训练的资源、上限、经验和失败边界 | 征兵与训练 | `planned` | `roadmap:DOM-02` | 缺完整调用/写回链；原文自述训练细节记忆不完整 |
| `SIX-DOM-07` | 一.2：训练规范统率与士气增量模型 | 训练 | `confirmed` | `baseline` | `FML-SAN9PK-0029`、`RE-SAN9PK-0067` |
| `SIX-DOM-08` | 一.2：买进与卖出属于八项内政指令 | 交易 | `planned` | `roadmap:DOM-03` | 原文没有价格、数量夹取、资源转移和商人条件 |
| `SIX-STR-01` | 一.3：计略和外交为确定公式、无概率 | 计略与外交 | `conflict` | `out-of-scope` | `RE-SAN9PK-0067` 已确认多条 `kakuritsu` 随机比较；本轮不扩展完整计略/外交 |
| `SIX-DUEL-01` | 二.1：逐刀按看破、动画必杀、双倍必杀、普通攻击短路 | 单挑逐刀状态机 | `planned` | `roadmap:DUEL-02` | 公式 25 只闭合数值，缺调用顺序、随机顺序和 HP 写回 |
| `SIX-DUEL-02` | 二.1：规范武力、四类概率与伤害公式 | 单挑伤害 | `confirmed` | `baseline` | `FML-SAN9PK-0025`、`RE-SAN9PK-0068`；机器式已校核原文差异 |
| `SIX-DUEL-03` | 二.1：吕布、赵云、马超特殊分支 | 单挑特殊人物 | `confirmed` | `baseline` | `FML-SAN9PK-0025`、`RE-SAN9PK-0068`；在 `DUEL-02` 验证其调度位置 |
| `SIX-DUEL-04` | 二.1：主动发起方固定先砍 | 单挑先手 | `planned` | `roadmap:DUEL-01` | 缺参与者选择至首击的控制流 |
| `SIX-DUEL-05` | 二.2：君主/总大将/四能力门槛与最大 30% 阵亡 | 单挑阵亡 | `confirmed` | `baseline` | `FML-SAN9PK-0027`、`RE-SAN9PK-0068/0071` 已修正最大基础能力门槛及共同开关；战后状态链见 `SIX-DUEL-06` |
| `SIX-DUEL-06` | 二.2：失败、阵亡、伤势和人物状态最终写回 | 单挑结束 | `planned` | `roadmap:DUEL-03` | 缺胜负出口到完整人物/部队写回链 |
| `SIX-DUEL-07` | 二.3：阵位、阵形、性格和武力限制决定触发 | 单挑触发 | `planned` | `roadmap:DUEL-01` | 缺候选遍历、遮蔽和概率调用的当前样本证据 |
| `SIX-TAC-01` | 三.一.1.1：普通攻击次数产生兵法机会，地域/高昂修正 15–30% | 兵法机会 | `inferred` | `roadmap:TAC-01` | `FML-SAN9PK-0017` 已推翻无条件每日次数解释；缺每日攻击事件来源与机会写入 |
| `SIX-TAC-02` | 三.一.1.2：按健康、间隔、阵位、人数、阵形和类别分配机会 | 陆战候选分配 | `planned` | `roadmap:TAC-02` | 缺真实遍历顺序、提前终止和失败移除边界 |
| `SIX-TAC-03` | 三.一.1.2：特定编队只检查锋线并丢失机会 | 陆战提前终止 | `experiment-required` | `roadmap:TAC-02` | 单次跟踪和一次游戏观察不足；需静态分支闭合或可重复编队实验 |
| `SIX-TAC-04` | 三.一.2：水军兵法发动 | 水战调度 | `planned` | `roadmap:TAC-03` | 原文仅给简述；缺候选、发动者、间隔和写回 |
| `SIX-TAC-05` | 三.一.3：设施兵法发动 | 设施调度 | `planned` | `roadmap:TAC-03` | 原文仅给简述；缺主将/候选、间隔和写回 |
| `SIX-TAC-06` | 三.二.1–6：兵法伤害、抵挡、动画、成长、联动 | 兵法效果公式 | `confirmed` | `baseline` | `FML-SAN9PK-0018..0024/0028`、`RE-SAN9PK-0068`；发动后调度写回仍归 `TAC-03` |
| `SIX-TAC-07` | 三.二.2：普通攻击伤害 | 普通战斗 | `confirmed` | `baseline` | `FML-SAN9PK-0015/0016`、`RE-SAN9PK-0068` |
| `SIX-AI-01` | 四.一：防守战术 AI | 战术 AI | `confirmed` | `baseline` | `RE-SAN9PK-0075` 至 `RE-SAN9PK-0088` 覆盖矩阵；不重复入队 |
| `SIX-AI-02` | 四.二：攻击战术 AI | 战术 AI | `confirmed` | `baseline` | `RE-SAN9PK-0075..0090` 覆盖矩阵；不重复入队 |

## 电子书主张覆盖

来源：`research/docs/三国志9电子书.md`。下表先列 R0–R3 相关或已有证据的章节，再对其余章节做显式范围处置。

| ID | 来源章节 | 主题 | status | disposition | 关联证据 / 明确缺口 |
| --- | --- | --- | --- | --- | --- |
| `EBK-TAC-01` | 2-7-1、6-14：普通攻击/人物日处理产生兵法机会 | 兵法机会 | `inferred` | `roadmap:TAC-01` | 两处重复转载；人物逐项日调度说法与 `FML-SAN9PK-0017` 的普攻事件入口需裁决 |
| `EBK-TAC-02` | 2-7-1、6-14：健康、间隔和随机门筛选陆战发动者 | 陆战候选分配 | `inferred` | `roadmap:TAC-02` | 缺候选构造、遍历顺序和机会失败边界 |
| `EBK-TAC-08` | 2-7-2、6-12：部队兵法最小间隔表 | 陆战最小间隔 | `experiment-required` | `roadmap:TAC-02` | 两处重复转载；缺数据表读取和发动重置写回 |
| `EBK-TAC-09` | 2-7-2、6-12：设施及联动最小间隔 | 设施/发动后写回 | `experiment-required` | `roadmap:TAC-03` | 缺设施主将边界、联动者写回和每日递减链 |
| `EBK-TAC-10` | 2-7-2、6-12：单挑间隔及三类间隔总结 | 资料校核 | `experiment-required` | `roadmap:TAC-04` | 单挑语境数值不确定；在调度清账中登记与 R3 的交叉缺口 |
| `EBK-SIX-01` | 2-7-3：SanIX 程序分析全文转载 | 内政、单挑、兵法、AI | `planned` | `duplicate` | `duplicate-of:SIX-*`；按上表逐项复用状态和批次，不生成第二套候选 |
| `EBK-DOM-01` | 3-1：基础心得中的内政收益与城市经营观察 | 内政经验观察 | `inferred` | `roadmap:DOM-04` | 在 R1 清账时作独立观察候选；其中委任与攻略内容范围外 |
| `EBK-DOM-02` | 3-2：投资贸易和粮食回报 | 买进与卖出 | `experiment-required` | `roadmap:DOM-03` | 经验数据可作独立黑盒佐证；缺价格来源、夹取与机器整数顺序 |
| `EBK-DOM-03` | 3-6、6-3：兵役人口影响因素 | 人口与兵役周期 | `inferred` | `roadmap:DOM-04` | 两章为经验讨论；与 `FML-SAN9PK-0014` 静态事实逐项校核 |
| `EBK-GROW-01` | 3-9：能力上升法 | 能力成长 | `confirmed` | `baseline` | 公式部分由 `FML-SAN9PK-0028/0029`、`RE-SAN9PK-0067/0068` 覆盖；攻略建议不入队 |
| `EBK-PRES-01` | 3-10：信望计算公式 | 信望 | `confirmed` | `baseline` | `FML-SAN9PK-0029`、`RE-SAN9PK-0067` |
| `EBK-TAC-03` | 6-1：兵法习得熟练度要求 | 兵法数据 | `confirmed` | `baseline` | 记录布局见 `RE-SAN9PK-0015`，成长边界见 `RE-SAN9PK-0068` |
| `EBK-TAC-04` | 6-2：兵法阻挡 | 兵法抵挡 | `confirmed` | `baseline` | `FML-SAN9PK-0019/0023`、`RE-SAN9PK-0068` |
| `EBK-TAC-05` | 6-4：兵法之极意中的伤害、联动、成长主张 | 兵法效果 | `confirmed` | `baseline` | `FML-SAN9PK-0018..0024/0028`；频率与调度子主张仍随 `EBK-TAC-01/02` |
| `EBK-TAC-06` | 6-5、6-6：实战兵法与阵法经验 | 战术经验观察 | `inferred` | `roadmap:TAC-04` | 只用作调度结论的独立观察候选，不覆盖静态证据 |
| `EBK-TAC-07` | 6-13：联动伤害实验 | 兵法联动伤害 | `confirmed` | `baseline` | 机器式见 `FML-SAN9PK-0020/0021`、`RE-SAN9PK-0068`；实验数值仅为佐证 |

### 电子书范围外章节

下列章节均已覆盖登记，但路线图明确不处理其主张；状态只描述来源性质，不表示已确认原作行为。

| ID | 章节 | 主题 | status | disposition | 理由 |
| --- | --- | --- | --- | --- | --- |
| `EBK-EXC-01` | 2-1、2-2 | 头像制作与导入 | `planned` | `out-of-scope` | 资源/UI，不属核心模拟 R0–R3 |
| `EBK-EXC-02` | 3-3、3-4、3-5 | 内政对话、猛兽、宝物表 | `planned` | `out-of-scope` | 外围事件与数据表 |
| `EBK-EXC-03` | 3-7、3-8、3-11、3-12 | 委任、登用、忠诚与军团处理 | `planned` | `out-of-scope` | 军团治理、完整人事/外交不在本轮 |
| `EBK-EXC-04` | 4-1、4-2、4-3、4-4、4-5 | 小兵培养、人物评价、配对攻略 | `inferred` | `out-of-scope` | 人物培养与攻略观察 |
| `EBK-EXC-05` | 5-1、5-2、5-3、5-4、5-5、5-6 | 历史/探索事件、训练表、地域表 | `planned` | `out-of-scope` | 外围事件和静态资料表 |
| `EBK-EXC-06` | 6-7、6-8、6-9、6-10、6-11、6-15、6-16、6-17、6-18、6-19、6-20、6-21、6-22、6-23、6-24 | 战术攻略、微操、多人玩法与四夷攻略 | `inferred` | `out-of-scope` | 玩家策略/玩法，不是本轮程序状态机 |
| `EBK-EXC-07` | 7-2、7-3 | 结局与兵法精通攻略 | `planned` | `out-of-scope` | 结局明确排除；重复成长内容由基线覆盖 |
| `EBK-EXC-08` | 10-1、10-2 | 自制剧本 | `planned` | `out-of-scope` | 工具与内容创作，不属本轮 |

## San9PKHard 主题覆盖

来源：`research/upsteam/San9PKHard/`。这里登记源码所承载的候选主题，不能据此断言补丁描述就是原版语义。

| ID | 承载位置 | 主题 | status | disposition | 关联证据 / 明确缺口 |
| --- | --- | --- | --- | --- | --- |
| `HARD-INFRA-01` | `San9PKHard.cpp`、`San9PKHardDlg.cpp/.h`、`ReadWriteError.h` | 进程附着、配置装载与写入编排 | `planned` | `roadmap:COV-02` | 需把每个配置项关联到实际地址/写入/thunk，排除仅 UI 状态 |
| `HARD-PARA-01` | `Parasettingdlg.cpp/.h`、`San9PKHardPara.txt` | 全局难度、系统和数值参数 | `planned` | `roadmap:COV-02` | 数值序列无自描述；需由 DDX、成员和写入点恢复语义 |
| `HARD-PERSON-01` | `PersonConfigDlg.cpp/.h`、`San9PKHardPerson.txt` | 人物能力、兵法与关系配置 | `planned` | `roadmap:COV-02` | 需区分初始数据改写、运行时补丁与纯配置存档 |
| `HARD-CITY-01` | `CityConfigDlg.cpp/.h`、`San9PKHardCity.txt` | 城市战法、收益、耐久和商人配置 | `planned` | `roadmap:COV-02` | 城市配置与八内政/人口周期的交集路由至 R1 |
| `HARD-ARRAY-01` | `ArrayConfigDlg.cpp/.h`、`San9PKHardArray.txt` | 阵形参数 | `planned` | `roadmap:COV-02` | 已闭合阵形/战术 AI 候选只链接基线；发动调度交集路由 R2 |
| `HARD-SKILL-01` | `San9PKHardSkill.txt`、相关 DDX/写入调用 | 兵法参数 | `planned` | `roadmap:COV-02` | 需恢复数组索引、字段意义及写入方式；调度交集路由 R2 |
| `HARD-PATCH-01` | `InsertAsm.h`、`InsertAsmSrc.h`、`InsertAsmSrc2.h`、`InsertAsmSrc3.h` | 地址字段、直接写入、旧字节、thunk 和注入体 | `planned` | `roadmap:COV-02` | 本批只确认四个承载文件；逐候选无遗漏登记是 COV-02 退出条件 |
| `HARD-INIT-01` | `InitData.h` | 原始关系、爵位/俸禄、阵形图像及帮助文本 | `planned` | `out-of-scope` | 静态替换数据不属于 R1–R3；COV-02 仍须判定是否产生代码写入候选 |
| `HARD-DOM-01` | `InsertAsm*.h` 中 commerce、cultivate、draft、population、city update 等组 | 内政与城市周期补丁候选 | `planned` | `roadmap:COV-02` | 先由 COV-02 定位原字节/宿主控制流，再按主张拆分进入 R1；不复制注入实现 |
| `HARD-TAC-01` | `InsertAsm*.h` 中 tactic/killer/linkage/proficiency/water/facility 等组 | 兵法效果与发动补丁候选 | `planned` | `roadmap:COV-02` | 公式类命中链接 `RE-SAN9PK-0068`，只把调度缺口拆分进入 R2 |
| `HARD-DUEL-01` | `InsertAsm*.h` 中 duel/dead/general/person-death 等组 | 单挑与阵亡补丁候选 | `planned` | `roadmap:COV-02` | 公式 27 命中链接基线；触发、逐刀和战后写回再拆分进入 R3 |
| `HARD-AI-01` | `InsertAsm*.h` 中 attack/defence/dispatch/array/retreat 等组 | 战术 AI 补丁候选 | `confirmed` | `baseline` | `RE-SAN9PK-0075` 至 `RE-SAN9PK-0090`；COV-02 仍登记地址，但不重新研究 AI 决策 |
| `HARD-OTHER-01` | `InsertAsm*.h` 中 UI、快捷键、外交、事件、军团、战争迷雾等组 | 路线范围外补丁候选 | `planned` | `out-of-scope` | COV-02 为无遗漏仍逐地址登记，COV-03 分类时不得进入 R1–R3 |
| `HARD-BUILD-01` | `.dsp`、`.dsw`、`.clw`、`.aps`、`.ncb`、`.opt`、`.plg`、`.rc`、`resource.h`、`res/`、`StdAfx.*` | MFC 构建、IDE 状态与界面资源 | `planned` | `out-of-scope` | 不承载原版程序规则；若含地址/旧字节字面量，COV-02 校验器仍应报告 |

## 后续批次入口

- `COV-02`：从 `HARD-INFRA-01` 至 `HARD-PATCH-01` 的承载文件全量抽取地址字段、直接写入、旧字节、thunk、配置语义和排除理由。
- `COV-03`：把本账本与 COV-02 地址账本转换为机器可校验的候选队列；校验重复来源、状态枚举、批次路由、地址格式和无静默遗漏。
- `DOM-01`：从 `SIX-DOM-03/04`、`EBK-DOM-01` 和 `HARD-DOM-01` 开始，不重复公式 29。
- `TAC-01`：从 `SIX-TAC-01`、`EBK-TAC-01` 开始，以 `FML-SAN9PK-0017` 的普攻事件事实为入口。
- `DUEL-01`：从 `SIX-DUEL-04/07` 开始；公式 25/27 只作已完成数值基线。
