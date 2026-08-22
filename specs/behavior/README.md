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
- [BEH-EVENT-0001：神兽事件的战斗判定](BEH-EVENT-0001-shenshou-event.md)（草案）

新增规格必须使用[行为规格模板](../templates/behavior-spec.md)，分别描述原作基线、重制行为和依据。若规格声称描述原作行为，没有本项目可复验证据时不得设为“已确认”；原创或现代化设计可以依据明确决策、原型验证和可测试的验收标准进入“已确认”。
