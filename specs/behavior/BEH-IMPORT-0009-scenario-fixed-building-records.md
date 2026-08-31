# BEH-IMPORT-0009：剧本固定建筑记录解析

- 状态：已确认
- 负责人：项目开发者
- 最后更新：2026-08-31
- 关联证据：[RE-SAN9PK-0014](../../research/findings/RE-SAN9PK-0014.md)
- 关联决策：[DEC-DATA-0001](../decisions/DEC-DATA-0001-content-pipeline.md)
- 关联渲染规格：[BEH-RENDER-0001](BEH-RENDER-0001-static-map-snapshot.md)

## 适用范围

本规格约束繁中 PK 1.0.1.0 正式布局剧本 `fbld` chunk 的记录边界、运行时身份、已确认字段映射、兵法代码引用和无损读取。输入必须先通过 [BEH-IMPORT-0001](BEH-IMPORT-0001-source-container-validation.md)。

本规格不要求导入器复现原版运行时对象布局。`+0x02/+0x0A` 在当前 EXE 没有玩法或渲染消费者，必须作为原始目录元数据保留，不据库存值推造规则。

## 原作基线

`fbld` chunk 位于正式剧本数据的 `0x13E`，包含 17 项，每项 28 字节，共 476 字节。运行时固定建筑 ID 使用记录序号 `0..16`。

| 偏移 | 类型 | 规格字段 | 已确认语义 |
|---:|---|---|---|
| `0x00` | `u16` | `serialized_id` | 原始序号槽位；原程序读出后丢弃 |
| `0x02` | `u16` | `catalog_code` | 目录代码；写入运行时对象但当前 EXE 无后续读取，不是身份来源 |
| `0x04` | `u16` | `visual_definition_code` | 视觉定义代码；经地域建筑地图快照进入建筑组合选择 |
| `0x06` | `u16` | `construction_cost` | 建造费用 |
| `0x08` | `u16` | `max_durability` | 最大耐久 |
| `0x0A` | `u16` | `opaque_field_0A` | 通用目录数值；仅有属性读取和十进制格式化，无玩法或渲染消费者 |
| `0x0C` | `u16` | `shape_length` | 外形长度；运行时作为 footprint 的 X 轴宽度 |
| `0x0E` | `u16` | `shape_width` | 外形宽度；运行时作为 footprint 的 Y 轴高度 |
| `0x10` | `u16` | `appearance_id` | 外形/外观 ID |
| `0x12` | `u16` | `recovery_per_turn` | 建筑每旬伤兵恢复系数；每一点贡献 100 基础恢复量 |
| `0x14` | `u16` | `tactic_code` | `hiho`/兵法表索引代码；`0..40` 可派生为 `tactic_id`，其他值保留但不解析为 ID |
| `0x16..0x1B` | `uint48` | `tactic_flags` | 兵法标志；当前 EXE 按兵法 ID `0..40` 查询 |

库存样本中的前 11 项和尾部值是默认数据，不是未确认字段的命名证据。

## 重制行为

导入器必须生成 17 个按文件顺序排列的固定建筑定义，并暴露：

- `FootprintWidth = ShapeLength`、`FootprintHeight = ShapeWidth`、`FootprintArea = width × height`；
- footprint 始终是以对象坐标为中心的轴对齐矩形；当前记录没有旋转或不规则占用编码。

- `Id`：记录序号 `0..16`；
- 所有已确认的可见字段；
- `SerializedId`、`CatalogCode`、`VisualDefinitionCode`、`OpaqueField0A` 和完整 48 位 `TacticFlags` 原值；
- `TacticCode` 的原始 `u16` 值，以及仅在 `0..40` 时存在的派生 `TacticId`。
- `HasTactic(id)` 只接受 `0..40`；库存箭楼/城塞记录分别通过 bit40/bit39 派生 `HasArrowPlatform`/`HasRockfall`。

导入器不得因为库存样本中的序号副本等于记录序号而丢弃 `SerializedId` 或 `CatalogCode`；不得把 `VisualDefinitionCode` 与记录顺序 ID、`AppearanceId` 或 footprint 合并。不得因为 `TacticCode` 越界而改写或拒绝整条记录，越界值必须保留为原始代码。`TacticFlags` 的高 7 位虽不在当前 EXE 查询域，仍须无损保留。

## 状态与不变量

- 输入与前置条件：已通过 `BEH-IMPORT-0001` 的完整剧本流。
- 状态变化：无；只生成 17 个只读记录。
- 始终成立的不变量：返回顺序等于文件顺序；每项完整消费 28 字节；整个 `fbld` chunk 恰好消费 476 字节；尾部 6 字节按 little-endian 48 位原值保留。
- 失败行为：容器或 `fbld` chunk 尺寸不正确时抛出格式错误。

## 原作每旬伤兵恢复语义

原程序每旬枚举内嵌建筑守军记录。对当前伤兵 `W > 0` 的建筑，从其固定建筑定义读取 `R = recovery_per_turn`，并按以下机器顺序计算：

```text
base = 100 * R + rnd(100) + min(1000, W / 10)
if building_vslot_0x90() == false:
    base = 2 * base
recovered = min(base, W)
```

`rnd(100)` 返回 `0..99`。提交时建筑伤兵减少 `recovered`，现役守兵增加同样数量；存在有效势力时还按恢复兵数参与士气混合。虚表 `+0x90` 返回假时翻倍，但该类别门的领域名称尚未确认。导入器只保留 `R`，不得在导入阶段提前执行恢复或按当前伤兵改写它。

## 验收标准

- [x] 17×28 字节边界、字段宽度和顺序身份由自动测试证明。
- [x] 11 个 `u16` 和 48 位兵法标志无损保留由自动测试证明。
- [x] 有效兵法代码的派生 ID 由自动测试证明；无效代码不伪造 ID。
- [x] `+0x12` 从文件记录到运行时 `+0x34`、再到每旬伤兵恢复公式的消费链由 IDA 数据流闭合。
- [x] 测试夹具不依赖原版文件、编辑器或 IDA 数据库。
- [x] `+0x04` 从文件到地域建筑、地图快照和建筑组合选择的视觉消费链由 IDA 数据流闭合。
- [x] 尾部 6 字节到兵法 ID `0..40` 位查询的消费链由 IDA 数据流和箭楼/城塞库存值交叉确认。
- [x] `+0x02/+0x0A` 的全程序负面审计确认当前版本没有玩法或渲染消费者。

## 非阻塞补证

- 编辑器单变量修改后的保存差分，以及其他版本对 `+0x0A` 的目录标签，统一归入 [`EDITOR-01`](../../research/scenario-field-research-checklist.md#范围外与后续扩展)；不得反向改变当前版本的负面审计结论。
- `D_Trst01.S9`–`D_Trst28.S9` 的特殊 `head` 布局适用性归入 [`EXT-SCENARIO-01`](../../research/scenario-field-research-checklist.md#范围外与后续扩展)。
