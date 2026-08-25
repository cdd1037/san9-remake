# 行为规格目录

本目录保存可供实现与测试直接使用的 `BEH-*` 文档。

- [BEH-IMPORT-0001：繁中 PK 1.0.1.0 源容器校验](BEH-IMPORT-0001-source-container-validation.md)（已确认）
- [BEH-IMPORT-0002：剧本宝物记录解析](BEH-IMPORT-0002-scenario-item-records.md)（已确认）
- [BEH-IMPORT-0003：剧本城市记录解析](BEH-IMPORT-0003-scenario-city-records.md)（已确认）
- [BEH-IMPORT-0004：剧本武将记录解析](BEH-IMPORT-0004-scenario-person-records.md)（已确认结构与核心字段）
- [BEH-IMPORT-0005：剧本官爵记录解析](BEH-IMPORT-0005-scenario-rank-records.md)（已确认）
- [BEH-IMPORT-0006：剧本官职记录解析](BEH-IMPORT-0006-scenario-office-records.md)（已确认）
- [BEH-IMPORT-0007：剧本州域记录解析](BEH-IMPORT-0007-scenario-area-records.md)（已确认边界与无损读取）
- [BEH-IMPORT-0008：剧本地形记录解析](BEH-IMPORT-0008-scenario-terrain-records.md)（已确认边界与无损读取）
- [BEH-IMPORT-0009：剧本固定建筑记录解析](BEH-IMPORT-0009-scenario-fixed-building-records.md)（已确认边界、可见字段与无损读取）
- [BEH-IMPORT-0010：剧本兵法记录解析](BEH-IMPORT-0010-scenario-tactic-records.md)（已确认边界、可见字段与无损读取）
- [BEH-IMPORT-0011：剧本国号记录解析](BEH-IMPORT-0011-scenario-country-title-records.md)（已确认边界、引用与无损读取）
- [BEH-IMPORT-0012：剧本阵型记录解析](BEH-IMPORT-0012-scenario-formation-records.md)（已确认边界、可见字段与无损读取）
- [BEH-IMPORT-0013：剧本 `skrk` 记录解析](BEH-IMPORT-0013-scenario-skrk-records.md)（已确认边界与无损读取）
- [BEH-IMPORT-0014：剧本地图方格记录解析](BEH-IMPORT-0014-scenario-square-records.md)（已确认边界、地形/高差过渡、桥方向、疆界及洛阳/带方港 footprint 实例）
- [BEH-IMPORT-0015：剧本据点（`bldg`）记录解析](BEH-IMPORT-0015-scenario-zone-records.md)（已确认结构与核心字段）
- [BEH-IMPORT-0016：剧本势力（`forc`）记录解析](BEH-IMPORT-0016-scenario-force-records.md)（已确认结构与主要字段）
- [BEH-IMPORT-0017：剧本部队（`unit`）记录解析](BEH-IMPORT-0017-scenario-unit-records.md)（已确认结构与已知字段）
- [BEH-RENDER-0001：大地图静态视觉快照](BEH-RENDER-0001-static-map-snapshot.md)（已确认地貌、桥、静态建筑、坐标与像素回归基线）
- [BEH-EVENT-0001：神兽事件的战斗判定](BEH-EVENT-0001-shenshou-event.md)（草案）
- [BEH-DUEL-0001：单挑触发与参战者选择](BEH-DUEL-0001-duel-trigger-and-participant-selection.md)（已确认）
- [BEH-DUEL-0002：单挑逐刀结算](BEH-DUEL-0002-duel-strike-resolution.md)（已确认）
- [BEH-DUEL-0003：单挑战后结果分区](BEH-DUEL-0003-duel-aftermath.md)（已确认范围）
- [BEH-PERSON-0001：人物发现、登用与归属初始化](BEH-PERSON-0001-discovery-recruitment-and-affiliation.md)（已确认）
- [BEH-PERSON-0002：人物奖惩与忠诚周期](BEH-PERSON-0002-rewards-exile-and-loyalty-cycle.md)（已确认范围）
- [BEH-PERSON-0003：人物召回、移动与行动占用](BEH-PERSON-0003-recall-movement-and-action-reservation.md)（已确认）
- [BEH-STRATEGY-0001：计略提交、军师意见与阶段结算](BEH-STRATEGY-0001-command-advice-and-resolution.md)（已确认）
- [BEH-STRATEGY-0002：部队计略状态与救援](BEH-STRATEGY-0002-unit-status-and-reinforcement.md)（已确认）
- [BEH-STRATEGY-0003：人物、建筑与势力计略效果](BEH-STRATEGY-0003-person-building-and-force-effects.md)（已确认）
- [BEH-DIPLOMACY-0001：势力关系与联盟状态](BEH-DIPLOMACY-0001-force-relations-and-alliance.md)（已确认）
- [BEH-DIPLOMACY-0002：外交命令谈判与结算](BEH-DIPLOMACY-0002-command-negotiation-and-resolution.md)（已确认）
- [BEH-DIPLOMACY-0003：自动外交规划与调度](BEH-DIPLOMACY-0003-automatic-diplomacy-planning.md)（已确认）
- [BEH-GOVERNANCE-0001：军团记录与对象归属](BEH-GOVERNANCE-0001-corps-record-and-ownership.md)（已确认）
- [BEH-GOVERNANCE-0002：委任控制与自动工作调度](BEH-GOVERNANCE-0002-delegation-control-and-automatic-scheduling.md)（已确认）

新增规格必须使用[行为规格模板](../templates/behavior-spec.md)，分别描述原作基线、重制行为和依据。若规格声称描述原作行为，没有本项目可复验证据时不得设为“已确认”；原创或现代化设计可以依据明确决策、原型验证和可测试的验收标准进入“已确认”。
