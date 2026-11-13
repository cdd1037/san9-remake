# 行为规格目录

本目录按领域定位采用行为与验收。文档状态见各页元数据；实现进度归计划，历史测试归验收记录。跨领域依赖见[领域依赖图](core-domain-dependency-map.md)，机器登记见[契约数据](core-domain-contracts.json)。维护条件见[规格索引](../README.md#规格维护)。

## 内容与导入

- [BEH-CONTENT-0001：共享内容与剧本初始状态](BEH-CONTENT-0001-shared-scenario-authoring.md)
- [BEH-IMPORT-0001：繁中 PK 1.0.1.0 源容器校验](BEH-IMPORT-0001-source-container-validation.md)
- [BEH-IMPORT-0002：剧本宝物记录解析](BEH-IMPORT-0002-scenario-item-records.md)
- [BEH-IMPORT-0003：剧本城市记录解析](BEH-IMPORT-0003-scenario-city-records.md)
- [BEH-IMPORT-0004：剧本武将记录解析](BEH-IMPORT-0004-scenario-person-records.md)
- [BEH-IMPORT-0005：剧本官爵记录解析](BEH-IMPORT-0005-scenario-rank-records.md)
- [BEH-IMPORT-0006：剧本官职记录解析](BEH-IMPORT-0006-scenario-office-records.md)
- [BEH-IMPORT-0007：剧本州域记录解析](BEH-IMPORT-0007-scenario-area-records.md)
- [BEH-IMPORT-0008：剧本地形记录解析](BEH-IMPORT-0008-scenario-terrain-records.md)
- [BEH-IMPORT-0009：剧本固定建筑记录解析](BEH-IMPORT-0009-scenario-fixed-building-records.md)
- [BEH-IMPORT-0010：剧本兵法记录解析](BEH-IMPORT-0010-scenario-tactic-records.md)
- [BEH-IMPORT-0011：剧本国号记录解析](BEH-IMPORT-0011-scenario-country-title-records.md)
- [BEH-IMPORT-0012：剧本阵型记录解析](BEH-IMPORT-0012-scenario-formation-records.md)
- [BEH-IMPORT-0013：剧本策略记录解析](BEH-IMPORT-0013-scenario-skrk-records.md)
- [BEH-IMPORT-0014：剧本地图方格记录解析](BEH-IMPORT-0014-scenario-square-records.md)
- [BEH-IMPORT-0015：剧本据点（`bldg`）记录解析](BEH-IMPORT-0015-scenario-zone-records.md)
- [BEH-IMPORT-0016：剧本势力（`forc`）记录解析](BEH-IMPORT-0016-scenario-force-records.md)
- [BEH-IMPORT-0017：剧本部队（`unit`）记录解析](BEH-IMPORT-0017-scenario-unit-records.md)

## 创作与玩家资料

- [BEH-COMPANION-0001：优化伴侣设定与玩家自动内政](BEH-COMPANION-0001-settings-and-player-domestic.md)
- [BEH-COMPANION-0002：优化伴侣基础战斗规则](BEH-COMPANION-0002-combat-rules.md)
- [BEH-COMPANION-0003：优化伴侣兵法发动、联动与成长](BEH-COMPANION-0003-tactic-rules.md)
- [BEH-COMPANION-0004：优化伴侣熟练度、阻击与人数系数](BEH-COMPANION-0004-proficiency-and-tactic-coefficients.md)
- [BEH-COMPANION-0005：玩家自动探索所在地](BEH-COMPANION-0005-player-local-search.md)
- [BEH-COMPANION-0006：能力成长与设施、舰船兵法预设](BEH-COMPANION-0006-ability-growth-and-naval-presets.md)
- [BEH-EDITOR-0001：局内世界编辑器](BEH-EDITOR-0001-world-editors.md)
- [BEH-EDITOR-0002：独立新武将人物库](BEH-EDITOR-0002-created-officers.md)
- [BEH-PROFILE-0001：跨局玩家进度基础](BEH-PROFILE-0001-player-progress.md)

## 模拟与存档

- [BEH-SIMULATION-0002：随机判定、SL与精确恢复](BEH-SIMULATION-0002-randomness.md)
- [BEH-SAVE-0001：玩家存档、命令脚本与无界面运行](BEH-SAVE-0001-session-persistence-replay-and-headless.md)
- [BEH-SCENARIO-0001：旬终局判定与根结果路由](BEH-SCENARIO-0001-terminal-evaluation.md)
- [BEH-SCENARIO-0002：玩家胜利结局解析](BEH-SCENARIO-0002-ending-resolution.md)
- [BEH-SIMULATION-0001：时间推进、周期结算与状态可见性](BEH-SIMULATION-0001-time-progression-and-state-visibility.md)

## 经济与人事

- [BEH-DOMESTIC-0001：四项内政基础增量](BEH-DOMESTIC-0001-base-development-effect.md)
- [BEH-ECONOMY-0001：经营命令与周期经济](BEH-ECONOMY-0001-domestic-and-economic-cycle.md)
- [BEH-GOVERNANCE-0001：军团记录与对象归属](BEH-GOVERNANCE-0001-corps-record-and-ownership.md)
- [BEH-GOVERNANCE-0002：委任控制与自动工作调度](BEH-GOVERNANCE-0002-delegation-control-and-automatic-scheduling.md)
- [BEH-GOVERNANCE-0003：旬级委任人事与辖区归属变化](BEH-GOVERNANCE-0003-xun-personnel-and-territorial-transition.md)
- [BEH-PERSON-0001：人物发现、登用与归属初始化](BEH-PERSON-0001-discovery-recruitment-and-affiliation.md)
- [BEH-PERSON-0002：人物奖惩与忠诚周期](BEH-PERSON-0002-rewards-exile-and-loyalty-cycle.md)
- [BEH-PERSON-0003：人物召回、移动与行动占用](BEH-PERSON-0003-recall-movement-and-action-reservation.md)
- [BEH-PERSON-0004：装备、成长与制造（草案）](BEH-PERSON-0004-equipment.md)
- [BEH-PERSON-LIFECYCLE-0001：能力、熟练度与兵法习得](BEH-PERSON-LIFECYCLE-0001-growth-and-learning.md)
- [BEH-PERSON-LIFECYCLE-0002：登场、伤势与死亡](BEH-PERSON-LIFECYCLE-0002-appearance-injury-and-death.md)
- [BEH-PERSON-LIFECYCLE-0003：培养人物状态机](BEH-PERSON-LIFECYCLE-0003-trainee-development.md)

## 计略外交与事件

- [BEH-DIPLOMACY-0001：势力关系与联盟状态](BEH-DIPLOMACY-0001-force-relations-and-alliance.md)
- [BEH-DIPLOMACY-0002：外交命令谈判与结算](BEH-DIPLOMACY-0002-command-negotiation-and-resolution.md)
- [BEH-DIPLOMACY-0003：自动外交规划与调度](BEH-DIPLOMACY-0003-automatic-diplomacy-planning.md)
- [BEH-DUEL-0001：单挑触发与参战者选择](BEH-DUEL-0001-duel-trigger-and-participant-selection.md)
- [BEH-DUEL-0002：单挑逐刀结算](BEH-DUEL-0002-duel-strike-resolution.md)
- [BEH-DUEL-0003：单挑战后结果分区](BEH-DUEL-0003-duel-aftermath.md)
- [BEH-EVENT-0001：神兽事件的战斗判定](BEH-EVENT-0001-shenshou-event.md)
- [BEH-EVENT-0002：事件选择、演出与原子提交](BEH-EVENT-0002-event-selection-and-commit.md)
- [BEH-STRATEGY-0001：计略提交、军师意见与阶段结算](BEH-STRATEGY-0001-command-advice-and-resolution.md)
- [BEH-STRATEGY-0002：部队计略状态与救援](BEH-STRATEGY-0002-unit-status-and-reinforcement.md)
- [BEH-STRATEGY-0003：人物、建筑与势力计略效果](BEH-STRATEGY-0003-person-building-and-force-effects.md)

## 战争与自动规划

- [BEH-AI-0001：AI 观察、规划与命令提交边界](BEH-AI-0001-planning-boundary.md)
- [BEH-WAR-0001：战场移动与命令生命周期](BEH-WAR-0001-movement-and-command-lifecycle.md)
- [BEH-WAR-0002：设施失守、占领与所有权转换](BEH-WAR-0002-building-defeat-and-capture.md)
- [BEH-WAR-0003：部队返城、战败终止与势力清理](BEH-WAR-0003-unit-return-defeat-and-force-cleanup.md)
- [BEH-WAR-0004：普通战斗与兵员损失](BEH-WAR-0004-normal-combat.md)
- [BEH-WAR-0005：兵法配置、机会与效果](BEH-WAR-0005-tactics.md)
- [BEH-WAR-0006：设施内容、选址与建设](BEH-WAR-0006-facility-construction.md)
- [BEH-WAR-0007：混编兵种、阵形与研究（草案）](BEH-WAR-0007-troop-formations-research.md)

## 界面与显示

- [BEH-RENDER-0001：大地图静态视觉快照](BEH-RENDER-0001-static-map-snapshot.md)
- [BEH-RENDER-0002：可交互静态地图与现代地图包](BEH-RENDER-0002-interactive-static-map.md)
- [BEH-RENDER-0003：地图水面、建筑与野外部队动画](BEH-RENDER-0003-map-ambient-animation.md)
- [BEH-RENDER-0004：单份头像与独立裁剪视图](BEH-RENDER-0004-shared-portrait-views.md)
- [BEH-UI-0001：战略界面与地图操作](BEH-UI-0001-initial-strategy-screen.md)
- [BEH-UI-0002：武将运行态与右侧武将名单](BEH-UI-0002-officer-roster.md)
- [BEH-UI-0003：战略全图面板](BEH-UI-0003-strategy-overview.md)
- [BEH-UI-0004：情报目录与详情浏览](BEH-UI-0004-information-browser.md)
- [BEH-UI-0005：大地图建筑悬停](BEH-UI-0005-map-building-hover.md)
- [BEH-UI-0006：统一消息与图文显示](BEH-UI-0006-unified-messages.md)
- [BEH-UI-0007：存档与读档界面](BEH-UI-0007-save-load-browser.md)
- [BEH-UI-0008：地图战斗、兵法与单挑演出](BEH-UI-0008-battle-presentation.md)
- [BEH-UI-0009：PK设定](BEH-UI-0009-pk-settings.md)
- [BEH-UI-0010：世界事件演出](BEH-UI-0010-world-event-presentation.md)
- [BEH-UI-0011：新游戏创建与开局设定](BEH-UI-0011-new-game.md)
