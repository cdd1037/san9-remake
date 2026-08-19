# BEH-IMPORT-0014：剧本地图方格记录解析

- 状态：已确认（区块边界、字段宽度、地图线性索引、城市/据点区域编码、疆界线相关消费者、寻路阻塞消费者、地图绘制与资源消费者、目标哨兵行为和无损读取；部分字节字段正式业务名称未确认）
- 负责人：项目开发者
- 最后更新：2026-08-19
- 关联证据：[RE-SAN9PK-0019](../../research/findings/RE-SAN9PK-0019.md)、[RE-SAN9PK-0036](../../research/findings/RE-SAN9PK-0036.md)
- 深入证据：[RE-SAN9PK-0028](../../research/findings/RE-SAN9PK-0028.md)、[RE-SAN9PK-0029](../../research/findings/RE-SAN9PK-0029.md)、[RE-SAN9PK-0034](../../research/findings/RE-SAN9PK-0034.md)、[RE-SAN9PK-0035](../../research/findings/RE-SAN9PK-0035.md)、[RE-SAN9PK-0036](../../research/findings/RE-SAN9PK-0036.md)、[RE-SAN9PK-0037](../../research/findings/RE-SAN9PK-0037.md)、[RE-SAN9PK-0038](../../research/findings/RE-SAN9PK-0038.md)
- 关联决策：[DEC-DATA-0001](../decisions/DEC-DATA-0001-content-pipeline.md)

## 适用范围

本规格约束繁中 PK 1.0.1.0 正式剧本 `squa` chunk 的记录边界、字段宽度、运行时哨兵和无损读取。输入必须先通过 [BEH-IMPORT-0001](BEH-IMPORT-0001-source-container-validation.md)。

本规格不把未知字节直接转换为完整地形规则、Zone 规则或重制版地图逻辑；只有目标 getter、截图或明确运行时路径支持的字段才提供派生视图。当前已确认一条实际方格图寻路路径使用 `+0x02` 的 `0x1FE` 位段做阻塞判断，但这不等于已经确认该字段全部 bit 的正式业务名称。

正式与教学样本的 `squa` 区块逐字节相同，因此本规格把它视为共享地图模板输入。黑盒未知栏的 `0004/0006/0008` 是相邻原始 byte 的 16 位显示组合，不改变文件布局或导入字段宽度。`+0x02` 在已确认的方格图寻路链中加载到运行时 `CSquareData +0x34`，并以 `& 0x1FE` 判断硬阻塞；它仍可能包含未确认的其他 bit 义，不在本规格中扩展为完整地图规则。其余未知字段的候选用途也不进入本规格的玩法规则。

## 原作基线

`squa` chunk 位于正式剧本数据偏移 `0x1406`，包含 40,000 项，每项 14 字节，共 560,000 字节。每项前 6 字节为 3 个小端 `u16`，后 8 字节必须逐字节读取：

下表中的 `规格字段` 对已确认消费者较多的字节使用研究层候选名；这些候选名不改变原始字段宽度，也不等于已经确认了字段的全部玩法语义。导入器当前仍保留原始字节和 `OpaqueField*` 公开属性。

| 偏移 | 类型 | 规格字段 | 已确认语义 |
| ---: | --- | --- | --- |
| `0x00` | `u16` | `serialized_id` | 原始序号槽位；原程序读取后丢弃，运行时编号使用记录序号 |
| `0x02` | `u16` | `square_flags_02` | 写入运行时 `CSquareData +0x34`；其中 `PathBlockingBits = value & 0x1FE`，在已确认的方格图寻路中非零时该格不可进入；bit0、bit9、bit11、bit12、bit13、bit15 另有城市/标记/运行时消费者，完整业务名称仍未确认 |
| `0x04` | `u16` | `zone_code` | 所属地域编码；`0..49` 对应城市，`50..249` 对应 `bldg` 记录 `zone_code - 50`，`0xFFFF` 表示无所属地域 |
| `0x06` | `byte` | `opaque_field_06` | 单字节原始字段；不得按 `u16` 重组；加载到 `CSquareData +0x18`，由通用 getter case 4 读取，并可由 `CSquareData` 的通用属性格式化接口按十六进制输出；尚未确认具体玩法名称或专用地图/寻路消费者 |
| `0x07` | `byte` | `tile_edge_geometry_mask` | 单字节原始字段；不得按 `u16` 重组；由 `adjust_square_visual_position`、`build_square_edge_overlays` 等地图绘制函数消费，影响八方向瓦片边缘几何 |
| `0x08` | `byte` | `offset_code` | 偏移代码；`0xFF` 派生为空，原始字节不变 |
| `0x09` | `byte` | `terrain_code` | 地形编号；经 `get_square_terrain_family` 转换后间接参与底图颜色/地形资源选择；`0xFF` 派生为空，原始字节不变 |
| `0x0A` | `byte` | `map_visual_variant` | 原始字节；`0xFF` 可提供空值派生。原作将其加载到运行时 `CSquareData +0x1C`，由 `create_square_mesh_visual` 参与 MESH/COV 覆盖资源编号、由 `build_square_boundary_segments` 参与疆界片段变体选择，并由 `calculate_square_edge_visual_position` 参与边缘锚点垂直步进；完整编码含义仍未确认 |
| `0x0B` | `byte` | `opaque_field_0B` | 原始字节；`0xFF` 可提供空值派生，业务名称未确认 |
| `0x0C` | `byte` | `opaque_field_0C` | 原始字节；目标路径对 `0xFF` 提供归一值 `0`，不覆盖原始值 |
| `0x0D` | `byte` | `boundary_edge_mask` | 原样写入运行时 `CSquareData +0x36`；`refresh_map_square_boundaries` 按 `k=0..3` 测试低四位与高四位方向掩码，并由 `build_square_boundary_segments` 选择疆界资源。bit0..3 已确认对应 `(x,y-1)`、`(x+1,y)`、`(x,y+1)`、`(x-1,y)`，两组掩码的正式线型含义仍未确认 |

### 疆界线相关运行时消费者

本节记录原作消费者，不要求重制版复刻原作内部对象结构。

`deserialize_scenario_chunks` 将 40,000 条 `squa` 记录加载到运行时方格数组 `g_square_data_grid`，每个运行时对象步长为 `0x38`。地图刷新函数 `refresh_map_square_boundaries` 对每个当前方格执行四方向循环 `k=0..3`：

1. 根据邻格以及邻格 `ZoneCode` 指向的城市/据点对象，计算该方向是否有候选边；
2. 由当前方格 `CSquareData +0x36`（文件 `+0x0D`）分别测试 `1 << k` 与 `0x10 << k`，形成两组四方向掩码；
3. 将当前方格、`CSquareData +0x14`（文件 `+0x08`）、`CSquareData +0x1C`（文件 `+0x0A`）和两组掩码传给 `build_square_boundary_segments`；
4. `build_square_boundary_segments` 根据硬编码的四方向/邻接表选择 `G_MapMkr.s9` 的资源变体 `10794..10805`，创建疆界线视觉片段。

因此，`opaque_field_0D` 不是“每格直接存一条完整边界线”，而是参与四方向边资格筛选的位字段；最终是否生成某边还依赖运行时邻格、城市/据点对象和代码中的方向规则。`opaque_field_0A` 已确认存在疆界片段选择消费者，但其正式字段语义仍保持不命名。

`k=0..3` 的坐标方向已经由刷新函数中的邻格表闭合：`k=0` 为 `(x,y-1)`，`k=1` 为 `(x+1,y)`，`k=2` 为 `(x,y+1)`，`k=3` 为 `(x-1,y)`。因此在通常的行列坐标约定下，依次是上、右、下、左；低四位使用 `0x01/0x02/0x04/0x08`，高四位使用 `0x10/0x20/0x40/0x80`。两组掩码的正式线型、颜色或边界类别仍未确认。

### `square_flags_02` 的已确认部分位消费者

`square_flags_02` 是运行时复合标志字，当前不能整体解释为可通行字段。已确认的局部消费者如下：

- `bit1..bit8`（`0x01FE`）：由 `square_has_blocking_bits` 提供方格图寻路阻塞判断；
- `bit0`（`0x0001`）：参与 Zone/城市对象条件门和部分移动模式；
- `bit9`（`0x0200`）：参与类型 11 地图对象的标记资格及特殊对象邻格检查；
- `bit11`（`0x0800`）：由 `update_square_runtime_flags` 设置和整图清除，当前按运行时临时标记处理；
- `bit12/bit13`（`0x1000/0x2000`）：参与两组城市相关条件门和移动/选择模式，具体玩法名称未确认；
- `bit15`（`0x8000`）：城市路径相关列表设置，当前按运行时临时覆盖范围标记处理。

`bit10` 和 `bit14` 在当前覆盖的方格专用消费者中尚未发现直接位测试。上述局部结论不改变导入器对 `OpaqueField02` 的无损保存。

`+0x06..+0x0D` 的字段全部是单字节。目标汇编中对 `u8` 与 `0xFFFF` 的比较不可达，不能据此把这些字段解释成 16 位字段或改变原始文件布局。`+0x06` 虽然没有在当前寻路/地图绘制链中闭合出专用规则，但 `CSquareData` 的通用属性格式化接口会以属性编号 4 读取它并按 `0x%08x` 输出；这属于通用属性可见性，不是已确认的玩法语义。

### 寻路可通行消费者

坐标寻路入口 `find_square_grid_path` 将起点和终点转换为方格索引，构造 `init_square_path_graph` 的方格图，由 `solve_square_path_graph -> run_square_graph_search` 执行搜索。`expand_square_neighbors_4way` 对每个节点只扩展四个邻格：`index+1`、`index+right`、`index-1`、`index-right`，不扩展斜向邻格。

`build_square_path_costs` 初始化节点时调用 `square_has_blocking_bits` 检查运行时 `CSquareData +0x34` 的 `0x1FE` 位段：

- 非零：节点移动代价设为 `0xFF`，`expand_square_neighbors_4way` 跳过该邻格；
- 为零：节点可进入，基础代价为 10；地形族为 4 时基础代价为 12。

城市/据点、部队和距离因素会在之后调整代价。它们与 `squa +0x0D` 的疆界线掩码不是同一条判断链。上述结论只覆盖这条已确认的方格图寻路路径；其他移动类型入口仍需单独确认。

### 地图绘制与资源消费者

主地图刷新 `redraw_map_scene` 调用 `draw_visible_square_tiles/redraw_visible_square_tile_pairs -> draw_square_tile_records -> draw_square_tile_texture` 绘制可见方格。逻辑方格通过 `square_to_isometric_anchor/square_to_isometric_center` 转成 64×32 的等距投影位置；200×200 是逻辑地图尺寸，不是屏幕上的 200×200 个普通矩形。

底图绘制会由 `select_land_tile_resource`、`select_land_tile_subresource` 和 `select_map_mask_resource` 根据地形、方向及边缘状态计算资源编号，再由 `get_loaded_resource_by_id` 从已加载资源表取对象，最后由 `composite_land_tile_pixels/composite_mask_tile_pixels` 读取像素并合成纹理。`squa +0x09` 先经 `get_square_terrain_family` 转为地形族；`build_minimap_terrain_texture` 使用该地形族生成概览纹理颜色，而不是把该字段直接当成图片编号。

覆盖层中，`squa +0x0A` 被 `draw_square_mesh_overlays -> create_square_mesh_visual` 用作 MESH/COV 资源选择参数；同一字段还由 `calculate_square_edge_visual_position` 参与边缘锚点的垂直步进；`squa +0x0D` 被疆界线链用作四方向掩码并选择 `G_MapMkr.s9` 的资源变体。`squa +0x07` 参与八方向边缘几何修正，不是直接图片编号。资源编号与资源包在代码加载表中绑定，`SQUARE` 通常保存选择参数/偏移，而不是资源文件名。

为避免把字段用途混在一起，当前确认的直接映射单列如下：

- `squa +0x09`：地形类型；经地形族转换后，间接决定底图颜色/地形资源；
- `squa +0x0A`：参与 MESH/COV 覆盖图片编号、疆界片段变体和边缘锚点垂直步进；
- `squa +0x0D`：决定疆界线四方向掩码，并选择 `G_MapMkr.s9` 的 `10794..10805` 图片资源；
- `squa +0x07`：影响瓦片边缘几何位置，不是图片编号。

## 重制行为

导入器必须生成 40,000 个按文件顺序排列的方格定义，并保留所有原始字段。正式地图尺寸为 200×200，方格 `Id` 使用行优先线性索引：`Id = y × 200 + x`。`SerializedId`、`OpaqueField02`、`ZoneCode` 和 8 个原始字节均可逐项回读。上述 `square_flags_02`、`tile_edge_geometry_mask`、`map_visual_variant`、`boundary_edge_mask` 是研究层别名，当前不替换导入器的原始属性名。

导入器提供以下地图拓扑派生视图：

- `GridX`、`GridY`：由运行时记录序号计算，不从 `SerializedId` 推导；
- `CityId`：区域编码 `0..49` 的城市 ID；
- `ZoneRecordId`：区域编码 `50..249` 减去 50 后的 `bldg` 记录 ID。
- `OpaqueField0DLowEdgeMask`：`opaque_field_0D & 0x0F`，仅作为原作兼容的四方向低位掩码视图，不赋予具体方向名称；
- `OpaqueField0DHighEdgeMask`：`(opaque_field_0D >> 4) & 0x0F`，仅作为原作兼容的四方向高位掩码视图，不赋予具体方向名称。

导入器还提供跨表拓扑校验：城市坐标必须映射到对应城市区域编码；有坐标的据点必须映射到 `50 + bldg` 记录 ID；城市和据点不得占用同一格。未知方格字节不参与规则推断。

导入器只提供以下不改变原始值的派生视图：`RegionCode` 将 `0xFFFF` 映射为空；`OffsetValue`、`TerrainId`、`OpaqueField0AValue` 和 `OpaqueField0BValue` 将 `0xFF` 映射为空；`NormalizedField0C` 将 `0xFF` 映射为目标路径观察到的 `0`。上述 `OpaqueField0D` 位视图只表达原作已确认的掩码消费方式，不声明四个 bit 的方向顺序或完整玩法语义。

## 状态与不变量

- 输入与前置条件：已通过 `BEH-IMPORT-0001` 的完整 `S9SC` 剧本流。
- 状态变化：读取 `squa` chunk 后产生 40,000 条方格记录；导入过程不修改原始字节。
- 始终成立的不变量：chunk 大小为 `560,000` 字节；每项完整消费 14 字节；记录 `Id` 为 `0..39999`；`Id` 与 200×200 行优先坐标一一对应；非空 `ZoneCode` 小于 250；字段宽度固定为 3 个 `u16` 加 8 个 `byte`。
- 失败或无效输入行为：chunk 大小异常、记录截断或游标不能恰好填满 chunk 时抛出 `InvalidDataException`。

## 示例与边界

正式样本第 0 条的原始字节为：

```text
00 00 C6 03 76 00 FF 00 04 02 00 02 00 00
```

其中 `OpaqueField02=966`、`ZoneCode=118`、`OffsetCode=4`、`TerrainCode=2`；`square.png` 和 `地形.png` 分别支持未知栏、地域、偏移和地形的界面对照。测试还覆盖 `0xFFFF` 与 `0xFF` 哨兵，原始值必须保持不变。

## 验收标准

- [x] 40,000×14 字节边界和逐字节布局由自动测试证明。
- [x] `ZoneCode`、偏移、地形和三个字节哨兵的派生视图由自动测试覆盖。
- [x] `0x06`、`0x07` 等单字节字段不会被按 16 位读取或重组。
- [x] 异常 chunk 大小会被拒绝。
- [x] 测试夹具不依赖原版文件、编辑器或 IDA 数据库。
- [x] 地图线性索引、城市/据点区域编码和坐标占用关系由自动测试证明。
- [x] `OpaqueField0DLowEdgeMask` 与 `OpaqueField0DHighEdgeMask` 在不改变原始字节的前提下提供，并保持方向顺序未知。
- [x] 已记录实际方格图寻路的四邻格规则、`opaque_field_02 & 0x1FE` 阻塞条件和 10/12 基础代价；其他移动类型入口另行验证。
- [x] 已记录地图方格的 64×32 等距投影、底图资源编号选择、像素合成链和 `+0x0A/+0x0D` 覆盖资源消费者。

## 未决问题

- `square_flags_02` 除 `PathBlockingBits` 外的完整 bit 语义和消费者；
- `map_visual_variant` 的正式业务名称、编码值含义，以及它在 `build_square_boundary_segments` 中对应的具体片段变体规则；
- `boundary_edge_mask` 低四位/高四位的正式业务名称，以及两组掩码分别对应的完整线型语义；
- `+0x02` 已确认的城市/标记/临时运行时位与地图块/占用描述的完整关系，以及其与城市、据点 footprint 的完整关系；这是当前 `squa` 的 P0 未决项。
- `+0x06/+0x07`、`+0x0B`、`+0x0C` 的完整业务名称和专用消费者；在获得编辑器单变量差分或明确专用消费者前暂缓。`+0x06` 的通用属性格式化接口已确认，但其上层调用者和正式标签仍未确认。
- 原作 `ZoneCode` 之外的重制版地图对象关系仍需单独设计；当前只确认正式剧本的城市/据点编码映射。
- 偏移代码、地形代码和未知字节对编辑器保存行为及玩法规则的影响。

## IDA 同步记录

2026-08-19 已将本规格涉及的已确认语义同步到项目主数据库 `research/analysis/San9PK-TC-101.i64`：

- 寻路函数：`find_square_grid_path`、`init_square_path_graph`、`solve_square_path_graph`、`run_square_graph_search`、`expand_square_neighbors_4way`、`build_square_path_costs`、`square_has_blocking_bits`、`square_has_slow_terrain`；
- 地图绘制函数：`redraw_map_scene`、`draw_visible_square_tiles`、`draw_square_tile_records`、`draw_square_tile_texture`、`square_to_isometric_anchor`、`square_to_isometric_center`、`screen_to_square_coords`、`adjust_square_visual_position`、`calculate_square_edge_visual_position`；
- 资源函数：`get_square_terrain_family`、`select_land_tile_resource`、`select_land_tile_subresource`、`select_map_mask_resource`、`get_loaded_resource_by_id`、`composite_land_tile_pixels`、`composite_mask_tile_pixels`、`draw_square_mesh_overlays`、`create_square_mesh_visual`、`build_minimap_terrain_texture`。
- `CSquareData` 字段接口：`deserialize_squa_record`、`get_csquare_value`、`format_square_property_value`；其中 `+0x06` 的 getter/格式化证据见 [RE-SAN9PK-0038](../../research/findings/RE-SAN9PK-0038.md)。
- 标志/标记函数：`square_has_overlay_marker_bit`、`update_square_runtime_flags`、`square_has_runtime_bit12`、`square_has_runtime_bit13`。

已追加字段来源、阻塞条件、四邻格扩展、等距投影和资源编号链注释；IDB 保存成功并通过 IDA MCP 回读函数名称与注释。本次未修改程序字节、函数边界或结构类型。

本轮还写入了研究层候选字段名：`square_flags_02`、`tile_edge_geometry_mask`、`map_visual_variant`、`boundary_edge_mask`；新增位消费者、方向顺序和函数名证据见 [RE-SAN9PK-0038](../../research/findings/RE-SAN9PK-0038.md)。
