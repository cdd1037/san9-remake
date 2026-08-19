# RE-SAN9PK-0033：SQUARE 字段候选语义命名

- 状态：候选命名已写入 IDA 注释；完整业务名称仍分层确认
- 日期：2026-08-19
- 分析者：Codex
- 结论类型：事实与候选语义名分开记录
- 置信度：高（字段到运行时偏移和直接消费者）；中（候选名称作为正式玩法字段名）
- 关联证据：[RE-SAN9PK-0024](RE-SAN9PK-0024.md)、[RE-SAN9PK-0031](RE-SAN9PK-0031.md)、[RE-SAN9PK-0032](RE-SAN9PK-0032.md)
- 关联规格：[BEH-IMPORT-0014](../../specs/behavior/BEH-IMPORT-0014-scenario-square-records.md)

## 命名原则

候选名只描述已经确认的直接消费者，不把一个字段在所有系统中的完整业务含义提前定死。导入器现有 `OpaqueField*` 原始属性暂不改名；以下名称首先作为 IDA/研究层语义别名。

| `squa` 偏移 | 候选研究名 | 当前确认用途 | 置信度 |
| ---: | --- | --- | --- |
| `+0x02` | `square_flags_02` | 写入运行时 `CSquareData +0x34`；其中 `PathBlockingBits = value & 0x1FE`，在方格图寻路中非零表示不可进入；其他 bit 仍有未确认消费者 | 高（局部用途）/中（整体字段名） |
| `+0x07` | `tile_edge_geometry_mask` | 写入运行时 `+0x24`；8 个 bit 触发相邻/对角边缘覆盖，并参与瓦片视觉位置修正 | 高 |
| `+0x09` | `terrain_code` | 通过 `get_square_terrain_family` 映射为地形族，间接参与底图颜色、地形资源和概览纹理 | 高 |
| `+0x0A` | `map_visual_variant` | 写入运行时 `+0x1C`；作为 MESH/COV 覆盖视觉的资源变体/资源偏移，并参与疆界片段变体选择 | 中高 |
| `+0x0D` | `boundary_edge_mask` | 写入运行时 `+0x36`；低四位和高四位两组掩码由疆界刷新分别消费 | 高 |

## 直接证据

- `deserialize_squa_record` 将 `+0x02/+0x07/+0x09/+0x0A/+0x0D` 分别写入运行时 `CSquareData +0x34/+0x24/+0x10/+0x1C/+0x36`。
- `square_has_blocking_bits` 与通用 getter 的 bit 读取路径确认 `+0x02` 的 `0x1FE` 派生位段；`build_square_path_costs` 将其转为 `0xFF` 节点代价，`expand_square_neighbors_4way` 跳过该节点。
- `build_square_edge_overlays` 逐 bit 使用运行时 `+0x24` 的 `0..3` 与 `4..7` 位，生成八方向边缘覆盖；`adjust_square_visual_position` 使用同一字段修正瓦片边缘位置。
- `get_square_terrain_family` 消费运行时 `+0x10`，`build_minimap_terrain_texture` 使用地形族选择概览颜色，底图绘制链使用该分类参与资源选择。
- `create_square_mesh_visual` 将运行时 `+0x1C` 作为资源选择参数；`build_square_boundary_segments` 将同一值作为疆界片段变体输入。
- `test_square_edge_mask_high/test_square_edge_mask_low` 和 `refresh_map_square_boundaries` 消费运行时 `+0x36` 的两组四方向位；`build_square_boundary_segments` 最终选择 `G_MapMkr.s9` 的疆界资源。

## 结论边界

- `square_flags_02` 不应直接改名为 `passability`：目前只确认 `0x1FE` 位段在已确认寻路路径中的阻塞用途，不能排除该字段承载其他地图状态。
- `map_visual_variant` 比 `resource_offset` 更稳妥：在 MESH/COV 链中表现为资源偏移，但在疆界片段链中还参与变体/形状选择。
- `boundary_edge_mask` 可以作为 `+0x0D` 的稳定研究名；低四位/高四位的正式线型含义和四方向顺序仍未确认。
- `tile_edge_geometry_mask` 可以作为 `+0x07` 的稳定研究名；它描述的是视觉几何/覆盖消费者，不等于寻路邻接规则。

## IDA 写入与验证

已在项目主数据库 `research/analysis/San9PK-TC-101.i64` 的以下函数注释中写入候选字段名：

- `deserialize_squa_record`
- `get_csquare_value`
- `square_has_blocking_bits`
- `build_square_edge_overlays`
- `create_square_mesh_visual`
- `build_square_boundary_segments`

IDB 已保存，并通过 IDA MCP 回读注释确认。本轮未创建 `CSquareData` 结构字段类型，也未修改程序字节、函数边界或导入器公开属性名。
