# 行为规格目录

本目录保存可供实现与测试直接使用的 `BEH-*` 文档。

跨领域实现从[核心领域依赖图与实现入口](core-domain-dependency-map.md)开始；其机器可校验源为 [`core-domain-contracts.json`](core-domain-contracts.json)。

- [BEH-AI-0001：AI 观察、规划与命令提交边界](BEH-AI-0001-planning-boundary.md)（已确认）
- [BEH-DOMESTIC-0001：四项内政基础增量](BEH-DOMESTIC-0001-base-development-effect.md)（已确认；不含完整命令与组合加倍）
- [BEH-IMPORT-0001：繁中 PK 1.0.1.0 源容器校验](BEH-IMPORT-0001-source-container-validation.md)（已确认）
- [BEH-IMPORT-0002：剧本宝物记录解析](BEH-IMPORT-0002-scenario-item-records.md)（已确认）
- [BEH-IMPORT-0003：剧本城市记录解析](BEH-IMPORT-0003-scenario-city-records.md)（已确认）
- [BEH-IMPORT-0004：剧本武将记录解析](BEH-IMPORT-0004-scenario-person-records.md)（已确认结构与核心字段）
- [BEH-IMPORT-0005：剧本官爵记录解析](BEH-IMPORT-0005-scenario-rank-records.md)（已确认）
- [BEH-IMPORT-0006：剧本官职记录解析](BEH-IMPORT-0006-scenario-office-records.md)（已确认）
- [BEH-IMPORT-0007：剧本州域记录解析](BEH-IMPORT-0007-scenario-area-records.md)（已确认三份序号、名称资源、访问器与无损读取）
- [BEH-IMPORT-0008：剧本地形记录解析](BEH-IMPORT-0008-scenario-terrain-records.md)（已确认序号副本、名称资源与无消费者槽位）
- [BEH-IMPORT-0009：剧本固定建筑记录解析](BEH-IMPORT-0009-scenario-fixed-building-records.md)（已确认边界、可见字段与无损读取）
- [BEH-IMPORT-0010：剧本兵法记录解析](BEH-IMPORT-0010-scenario-tactic-records.md)（已确认边界、可见字段与无损读取）
- [BEH-IMPORT-0011：剧本国号记录解析](BEH-IMPORT-0011-scenario-country-title-records.md)（已确认三份序号、名称资源、势力关联、引用与无损读取）
- [BEH-IMPORT-0012：剧本阵型记录解析](BEH-IMPORT-0012-scenario-formation-records.md)（已确认边界、可见字段与无损读取）
- [BEH-IMPORT-0013：剧本策略记录解析](BEH-IMPORT-0013-scenario-skrk-records.md)（已确认序号副本、兵法关系与无消费者槽位）
- [BEH-IMPORT-0014：剧本地图方格记录解析](BEH-IMPORT-0014-scenario-square-records.md)（已确认边界、地形/高差过渡、桥方向、疆界及洛阳/带方港 footprint 实例）
- [BEH-IMPORT-0015：剧本据点（`bldg`）记录解析](BEH-IMPORT-0015-scenario-zone-records.md)（已确认结构与核心字段）
- [BEH-IMPORT-0016：剧本势力（`forc`）记录解析](BEH-IMPORT-0016-scenario-force-records.md)（已确认结构与主要字段）
- [BEH-IMPORT-0017：剧本部队（`unit`）记录解析](BEH-IMPORT-0017-scenario-unit-records.md)（已确认结构与已知字段）
- [BEH-RENDER-0001：大地图静态视觉快照](BEH-RENDER-0001-static-map-snapshot.md)（已确认地貌、桥、静态建筑、可选边缘云、坐标与像素回归基线）
- [BEH-EVENT-0001：神兽事件的战斗判定](BEH-EVENT-0001-shenshou-event.md)（已确认）
- [BEH-EVENT-0002：事件选择、演出与原子提交](BEH-EVENT-0002-event-selection-and-commit.md)（已确认）
- [BEH-DUEL-0001：单挑触发与参战者选择](BEH-DUEL-0001-duel-trigger-and-participant-selection.md)（已确认）
- [BEH-DUEL-0002：单挑逐刀结算](BEH-DUEL-0002-duel-strike-resolution.md)（已确认）
- [BEH-DUEL-0003：单挑战后结果分区](BEH-DUEL-0003-duel-aftermath.md)（已确认范围）
- [BEH-PERSON-0001：人物发现、登用与归属初始化](BEH-PERSON-0001-discovery-recruitment-and-affiliation.md)（已确认）
- [BEH-PERSON-0002：人物奖惩与忠诚周期](BEH-PERSON-0002-rewards-exile-and-loyalty-cycle.md)（已确认范围）
- [BEH-PERSON-0003：人物召回、移动与行动占用](BEH-PERSON-0003-recall-movement-and-action-reservation.md)（已确认）
- [BEH-PERSON-LIFECYCLE-0001：能力、熟练度与兵法习得](BEH-PERSON-LIFECYCLE-0001-growth-and-learning.md)（已确认）
- [BEH-PERSON-LIFECYCLE-0002：登场、伤势与死亡](BEH-PERSON-LIFECYCLE-0002-appearance-injury-and-death.md)（已确认）
- [BEH-PERSON-LIFECYCLE-0003：培养人物状态机](BEH-PERSON-LIFECYCLE-0003-trainee-development.md)（已确认）
- [BEH-STRATEGY-0001：计略提交、军师意见与阶段结算](BEH-STRATEGY-0001-command-advice-and-resolution.md)（已确认）
- [BEH-STRATEGY-0002：部队计略状态与救援](BEH-STRATEGY-0002-unit-status-and-reinforcement.md)（已确认）
- [BEH-STRATEGY-0003：人物、建筑与势力计略效果](BEH-STRATEGY-0003-person-building-and-force-effects.md)（已确认）
- [BEH-DIPLOMACY-0001：势力关系与联盟状态](BEH-DIPLOMACY-0001-force-relations-and-alliance.md)（已确认）
- [BEH-DIPLOMACY-0002：外交命令谈判与结算](BEH-DIPLOMACY-0002-command-negotiation-and-resolution.md)（已确认）
- [BEH-DIPLOMACY-0003：自动外交规划与调度](BEH-DIPLOMACY-0003-automatic-diplomacy-planning.md)（已确认）
- [BEH-GOVERNANCE-0001：军团记录与对象归属](BEH-GOVERNANCE-0001-corps-record-and-ownership.md)（已确认）
- [BEH-GOVERNANCE-0002：委任控制与自动工作调度](BEH-GOVERNANCE-0002-delegation-control-and-automatic-scheduling.md)（已确认）
- [BEH-GOVERNANCE-0003：旬级委任人事与辖区归属变化](BEH-GOVERNANCE-0003-xun-personnel-and-territorial-transition.md)（已确认）
- [BEH-WAR-0001：战场移动与命令生命周期](BEH-WAR-0001-movement-and-command-lifecycle.md)（已确认）
- [BEH-WAR-0002：设施失守、占领与所有权转换](BEH-WAR-0002-building-defeat-and-capture.md)（已确认）
- [BEH-WAR-0003：部队返城、战败终止与势力清理](BEH-WAR-0003-unit-return-defeat-and-force-cleanup.md)（已确认）
- [BEH-SCENARIO-0001：旬终局判定与根结果路由](BEH-SCENARIO-0001-terminal-evaluation.md)（已确认）
- [BEH-SCENARIO-0002：玩家胜利结局解析](BEH-SCENARIO-0002-ending-resolution.md)（已确认）
- [BEH-SIMULATION-0001：时间推进、周期结算与状态可见性](BEH-SIMULATION-0001-time-progression-and-state-visibility.md)（已确认）

- [BEH-RENDER-0002：可交互静态地图与现代地图包](BEH-RENDER-0002-interactive-static-map.md)（已确认）
- [BEH-RENDER-0003：地图水面、建筑旗帜与守兵动画](BEH-RENDER-0003-map-ambient-animation.md)（已确认）
- [BEH-RENDER-0004：单份头像与统一裁剪视图](BEH-RENDER-0004-shared-portrait-views.md)（已确认，已接入，画面待验收）
- [BEH-UI-0001：190年董卓势力初始主界面](BEH-UI-0001-initial-strategy-screen.md)（已确认）

维护条件与状态含义见[规格索引](../README.md#规格维护)；新增时按[行为规格模板](../templates/behavior-spec.md)裁剪栏目。原作事实的确认遵循[研究规范](../research-method.md#从证据到规格)。

- [BEH-UI-0002：武将运行态与右侧武将名单](BEH-UI-0002-officer-roster.md)（已确认）
- [BEH-UI-0003：战略全图面板](BEH-UI-0003-strategy-overview.md)（已确认）

- [BEH-UI-0004：情报目录与详情浏览](BEH-UI-0004-information-browser.md)（已确认）

- [BEH-UI-0005：大地图建筑悬停](BEH-UI-0005-map-building-hover.md)（采用）

- [BEH-UI-0006：统一消息与图文显示](BEH-UI-0006-unified-messages.md)（已确认）
