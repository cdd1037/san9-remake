# BEH-IMPORT-0014：剧本地图方格记录解析

- 状态：已确认（区块边界、字段宽度、地图线性索引、城市/据点区域编码、地形/高差过渡及自动生成、桥方向、两类疆界线、寻路阻塞消费者、地图绘制与资源消费者、目标哨兵行为和无损读取；无当前消费者的辅助值按原始值保留）
- 负责人：项目开发者
- 最后更新：2026-08-31
- 关联证据：[RE-SAN9PK-0019](../../research/findings/RE-SAN9PK-0019.md)、[RE-SAN9PK-0032](../../research/findings/RE-SAN9PK-0032.md)、[RE-SAN9PK-0051](../../research/findings/RE-SAN9PK-0051.md)、[RE-SAN9PK-0052](../../research/findings/RE-SAN9PK-0052.md)、[RE-SAN9PK-0053](../../research/findings/RE-SAN9PK-0053.md)、[RE-SAN9PK-0054](../../research/findings/RE-SAN9PK-0054.md)、[RE-SAN9PK-0056](../../research/findings/RE-SAN9PK-0056.md)
- 深入证据：[RE-SAN9PK-0024](../../research/findings/RE-SAN9PK-0024.md)、[RE-SAN9PK-0025](../../research/findings/RE-SAN9PK-0025.md)、[RE-SAN9PK-0030](../../research/findings/RE-SAN9PK-0030.md)、[RE-SAN9PK-0031](../../research/findings/RE-SAN9PK-0031.md)、[RE-SAN9PK-0032](../../research/findings/RE-SAN9PK-0032.md)、[RE-SAN9PK-0033](../../research/findings/RE-SAN9PK-0033.md)、[RE-SAN9PK-0034](../../research/findings/RE-SAN9PK-0034.md)
- 关联决策：[DEC-DATA-0001](../decisions/DEC-DATA-0001-content-pipeline.md)
- 渲染决策：[DEC-RENDER-0001](../decisions/DEC-RENDER-0001-high-color-map-rendering.md)
- 关联渲染规格：[BEH-RENDER-0001](BEH-RENDER-0001-static-map-snapshot.md)

## 适用范围

本规格约束繁中 PK 1.0.1.0 正式剧本 `squa` chunk 的记录边界、字段宽度、运行时哨兵和无损读取。输入必须先通过 [BEH-IMPORT-0001](BEH-IMPORT-0001-source-container-validation.md)。

本规格不把未知字节直接转换为完整地形规则、Zone 规则或重制版地图逻辑；只有目标 getter、截图或明确运行时路径支持的字段才提供派生视图。当前已确认一条实际方格图寻路路径使用 `+0x02` 的 `0x1FE` 位段做阻塞判断，但这不等于已经确认该字段全部 bit 的正式业务名称。

正式与教学样本的 `squa` 区块逐字节相同，因此本规格把它视为共享地图模板输入。黑盒未知栏的 `0004/0006/0008` 是相邻原始 byte 的 16 位显示组合，不改变文件布局或导入字段宽度。`+0x02` 在已确认的方格图寻路链中加载到运行时 `CSquareData +0x34`，并以 `& 0x1FE` 判断硬阻塞；它仍可能包含未确认的其他 bit 义，不在本规格中扩展为完整地图规则。其余未知字段的候选用途也不进入本规格的玩法规则。

## 原作基线

`squa` chunk 位于正式剧本数据偏移 `0x1406`，包含 40,000 项，每项 14 字节，共 560,000 字节。每项前 6 字节为 3 个小端 `u16`，后 8 字节必须逐字节读取：

下表中的 `规格字段` 对已确认消费者较多的字节使用研究层名称；这些名称不改变原始字段宽度，也不等于已经确认了字段的全部玩法语义。导入器始终保留原始字节，已闭合字段不再使用 `OpaqueField*` 名称。

| 偏移 | 类型 | 规格字段 | 已确认语义 |
| ---: | --- | --- | --- |
| `0x00` | `u16` | `serialized_id` | 原始序号槽位；原程序读取后丢弃，运行时编号使用记录序号 |
| `0x02` | `u16` | `square_flags_02` | 初始化运行时可变复合标志 `CSquareData +0x34`；`value & 0x1FE` 在已确认寻路中非零即阻塞；bit10 排除地域对象计数，bit12/13 允许 3×1/1×3 设施，bit14 标记对象核心格；bit9 是通用 overlay 资格位，bit11/15 会被运行时流程复用 |
| `0x04` | `u16` | `zone_code` | 所属地域编码；`0..49` 对应城市，`50..249` 对应 `bldg` 记录 `zone_code - 50`，`0xFFFF` 表示无所属地域 |
| `0x06` | `byte` | `water_terrain_auxiliary_code` | 编辑器 `0004` 的低字节；非 `0xFF` 值仅分布在直接水面 terrain `9/10/15/16` 且范围为 `0..7`；加载到 `CSquareData +0x18` 并由通用 getter/格式化接口读取，当前程序未发现专用玩法或绘制消费者 |
| `0x07` | `byte` | `terrain_transition_mask` | 编辑器 `0004` 的高字节；地形/高度融合的八方向掩码，bit7..0 依次为北、南、西、东、西南、东南、东北、西北；由边缘覆盖和视觉位置函数消费 |
| `0x08` | `byte` | `offset_code` | 偏移代码；`0xFF` 派生为空，原始字节不变 |
| `0x09` | `byte` | `terrain_code` | 地形编号；经 `get_square_terrain_family` 转换后间接参与底图颜色/地形资源选择；`0xFF` 派生为空，原始字节不变 |
| `0x0A` | `byte` | `elevation_transition_mask` | 编辑器 `0006` 的低字节；按四角各自相邻三格中的更高偏移确定，进入底图图集索引、MESH/COV、疆界片段和边缘锚点计算 |
| `0x0B` | `byte` | `land_texture_variant` | 编辑器 `0006` 的高字节；进入底图 atlas 索引和 `select_land_tile_resource`。可信黑盒观测表现为同地形的颜色浓度变体，但代码未证明它是线性色彩参数 |
| `0x0C` | `byte` | `bridge_orientation_code` | 编辑器 `0008` 的低字节；桥地形覆盖链以 `10004 + value` 选择资源，正式地图中 `0` 为南北、`1` 为东西；`0xFF` 归一行为不覆盖原始值 |
| `0x0D` | `byte` | `boundary_edge_mask` | 编辑器 `0008` 的高字节；低四位是淡黄白地域线、高四位是红白都市线，两组均按北、东、南、西排列；最终绘制还依赖邻格和资源规则 |

`terrain_transition_mask` 与 `elevation_transition_mask` 都可由其余方格字段确定性重建：

- `elevation_transition_mask` 的 bit0/3/1/2 分别对应西北、东北、东南、西南角；某角相邻的三个格中只要有任一 `offset_code` 高于当前格，该角位置位，越界邻格视为 `-1`；
- `terrain_transition_mask` 使用 `calculate_land_tile_atlas_index` 的完整排序键，而不是只比较地形编号。北、南、西、东邻格的键大于当前格时设置 `0x80/0x40/0x20/0x10`；对角格除大于当前格外，还必须严格大于该角两侧的正交邻格，才设置 `0x01/0x02/0x04/0x08`；
- 该规则无需桥专用分支：桥、水岸与陆地的过渡由同一 atlas 基表、偏移、纹理变体和高差形状共同决定。现有逆向地图脚本在 20 个剧本的 800,000 格上对两个字段均逐字节完全重建。

### 疆界线相关运行时消费者

本节记录原作消费者，不要求重制版复刻原作内部对象结构。

`deserialize_scenario_chunks` 将 40,000 条 `squa` 记录加载到运行时方格数组 `g_square_data_grid`，每个运行时对象步长为 `0x38`。地图刷新函数 `refresh_map_square_boundaries` 对每个当前方格执行四方向循环 `k=0..3`：

1. 根据邻格以及邻格 `ZoneCode` 指向的城市/据点对象，计算该方向是否有候选边；
2. 由当前方格 `CSquareData +0x36`（文件 `+0x0D`）分别测试 `1 << k` 与 `0x10 << k`，形成两组四方向掩码；
3. 将当前方格、`CSquareData +0x14`（文件 `+0x08`）、`CSquareData +0x1C`（文件 `+0x0A`）和两组掩码传给 `build_square_boundary_segments`；
4. `build_square_boundary_segments` 根据硬编码的四方向/邻接表选择 `G_MapMkr.s9` 的资源变体 `10794..10805`，创建疆界线视觉片段。

因此，`opaque_field_0D` 不是“每格直接存一条完整边界线”，而是参与四方向边资格筛选的位字段；最终是否生成某边还依赖运行时邻格、城市/据点对象和代码中的方向规则。`opaque_field_0A` 已确认存在疆界片段选择消费者，但其正式字段语义仍保持不命名。

`k=0..3` 的坐标方向已经由刷新函数中的邻格表闭合：`k=0` 为 `(x,y-1)`，`k=1` 为 `(x+1,y)`，`k=2` 为 `(x,y+1)`，`k=3` 为 `(x-1,y)`。因此依次是北、东、南、西；低四位 `0x01/0x02/0x04/0x08` 对应淡黄白地域线，高四位 `0x10/0x20/0x40/0x80` 对应红白都市线。同一方向重合时都市线资源优先。每个方向再由 `+0x0A` 中两个方向相关位选择平面、主侧抬高或次侧抬高三种形态；两侧同时抬高回到平面形态。四方向、两种样式与完整形态表均有自动测试。

### `square_flags_02` 的已确认部分位消费者

`square_flags_02` 是“文件模板初值 + 运行时复用”的可变复合标志，不能整体解释为可通行字段。已确认或可信观测到的局部语义如下：

- `bit1..bit8`（`0x01FE`）：由 `square_has_blocking_bits` 提供方格图寻路阻塞判断；
- `bit0`（`0x0001`）：工作物格模板位，不等同于“当前已有建筑”。玩家建设阵、砦、箭楼、城塞时，以候选中心生成 3×3 footprint，九格必须同属一个地域对象且全部置有 bit0；已存在对象与预留的空建设点都可在静态模板中置位，动态占用由地域对象状态另行判断；
- `bit9`（`0x0200`）：通用 overlay/地图块资格位；洛阳 5×5 与带方港 3×3 的全部格都置有该位，不能整体命名为桥标志。仅在 `terrain_code == 11` 的桥覆盖链中，`1` 表示该格具备桥覆盖锚点资格；
- `bit10`（`0x0400`）：排除于地域对象计数；方格仍保留 `ZoneCode`，该位也不被疆界绘制函数读取；
- `bit11`（`0x0800`）：由 `update_square_runtime_flags` 设置和整图清除，当前按运行时临时标记处理；
- `bit12/bit13`（`0x1000/0x2000`）：地图模板中的方向化设施建设资格；玩家模式中 bit12 允许代码 11/13/15 的 3×1 设施，bit13 允许代码 10/12/14 的 1×3 设施。自动设施候选过滤可让代码 16 同时接受两位，但代码 16 的玩家选格模式使用 3×3 bit0；
- `bit14`（`0x4000`）：对象核心区位；洛阳内部 3×3 九格、带方港正中心一格为 `0x4201`。它不是所有对象都只有一格的“中心点”布尔值；
- `bit15`（`0x8000`）：城市路径相关列表设置，当前按运行时临时覆盖范围标记处理。

`bit1..8` 虽被前人描述为八方向阻塞形态，但当前 EXE 的完整消费者审计只测试整个 `0x01FE` 是否非零：专用 helper 的 6 个调用者、通用 getter/格式化出口以及所有已知方格访问链均未选择单独方向位。因此产品只提供 `PathBlockingBits = SquareFlags & 0x01FE` 和 `IsPathBlocked = PathBlockingBits != 0`，不得实现方向化移动；完整 `SquareFlags` 仍无损保存。

`+0x06..+0x0D` 的字段全部是单字节。目标汇编中对 `u8` 与 `0xFFFF` 的比较不可达，不能据此把这些字段解释成 16 位字段或改变原始文件布局。`+0x06` 虽然没有在当前寻路/地图绘制链中闭合出专用规则，但 `CSquareData` 的通用属性格式化接口会以属性编号 4 读取它并按 `0x%08x` 输出；这属于通用属性可见性，不是已确认的玩法语义。

### 寻路可通行消费者

坐标寻路入口 `find_square_grid_path` 将起点和终点转换为方格索引，构造 `init_square_path_graph` 的方格图，由 `solve_square_path_graph -> run_square_graph_search` 执行搜索。`expand_square_neighbors_4way` 对每个节点只扩展四个邻格：`index+1`、`index+right`、`index-1`、`index-right`，不扩展斜向邻格。

`build_square_path_costs` 初始化节点时调用 `square_has_blocking_bits` 检查运行时 `CSquareData +0x34` 的 `0x1FE` 位段：

- 非零：节点移动代价设为 `0xFF`，`expand_square_neighbors_4way` 跳过该邻格；
- 为零：节点可进入，基础代价为 10；地形族为 4 时基础代价为 12。

城市/据点、部队和距离因素会在之后调整代价。它们与 `squa +0x0D` 的疆界线掩码不是同一条判断链。上述结论只覆盖这条已确认的方格图寻路路径；其他移动类型入口仍需单独确认。

### 地图绘制与资源消费者

主地图刷新 `redraw_map_scene` 调用 `draw_visible_square_tiles/redraw_visible_square_tile_pairs -> draw_square_tile_records -> draw_square_tile_texture` 绘制可见方格；瓦片记录再分派到 `composite_mask_tile_pixels` 或 `composite_land_tile_pixels`。当前格路径按 G_MASK 每行首尾非零点连续复制源图横段，八方向边缘路径才按 P_MASK 权重逐像素混合。方格记录绘制完成后依次经过特殊标记检查、桥覆盖、地图边缘/边框装饰、疆界刷新、MESH/COV 覆盖和更高层地图对象。逻辑方格通过 `square_to_isometric_anchor/square_to_isometric_center` 转成 64×32 的等距投影位置；图库循环取样使用减去目标缓存原点前的全图等距坐标。200×200 是逻辑地图尺寸，不是屏幕上的 200×200 个普通矩形。

底图绘制会由 `select_land_tile_resource`、`select_land_tile_subresource` 和 `select_map_mask_resource` 根据地形、偏移、`+0x0A`、`+0x0B` 及边缘状态计算资源编号，再由 `get_loaded_resource_by_id` 从已加载资源表取对象，最后由 `composite_land_tile_pixels/composite_mask_tile_pixels` 读取像素并合成纹理。`+0x07` 在 `build_square_edge_overlays` 中决定邻接/对角边缘记录的展开，不作为像素资源选择器的四字段参数。`calculate_square_land_tile_atlas_index` 的结果写入 `record+0x0C`，并在记录构造时折入 `record+0x20` 排序键；`draw_square_tile_texture` 不再直接读取 `record+0x0C` 选择资源。`squa +0x09` 先经 `get_square_terrain_family` 转为地形族；`build_minimap_terrain_texture` 使用该地形族生成概览纹理颜色，而不是把该字段直接当成图片编号。

覆盖层中，`squa +0x0A` 被 `draw_square_mesh_overlays -> create_square_mesh_visual` 用作 MESH/COV 资源选择参数；同一字段还由 `calculate_square_edge_visual_position` 参与边缘锚点的垂直步进，并作为像素合成前的资源选择参数。MESH/COV 是否出现另由运行时覆盖网格决定：`apply_square_overlay_states` 将选中的 `CSquareData` 列表写入网格并传入状态 `0..5`，`draw_square_mesh_overlays` 再读取该状态；因此不能仅凭 `squa +0x0A` 在裸地图上自动铺满 MESH/COV。`squa +0x0B` 与地形、偏移、`+0x0A` 一起进入底图预计算索引。`squa +0x0C` 在桥覆盖链中选择 `10004/10005`；`squa +0x0D` 被疆界线链用作四方向掩码并选择 `G_MapMkr.s9` 资源。`squa +0x07` 参与八方向地形融合与边缘记录展开。资源编号与资源包在代码加载表中绑定，`SQUARE` 通常保存选择参数/偏移，而不是资源文件名。

为避免把字段用途混在一起，当前确认的直接映射单列如下：

- `squa +0x09`：地形类型；经地形族转换后，间接决定底图颜色/地形资源；
- `squa +0x07`：八方向地形/高差融合掩码，方向顺序已确认；
- `squa +0x0A`：高差过渡掩码，参与底图、MESH/COV、疆界片段和边缘锚点；
- `squa +0x0B`：底图纹理变体；按 `0→0、1→1、2→2、3→3、4→2、5→1` 选择资源变体，并在 terrain `9/10/15/16` 的 atlas 排序索引中使用另一张已确认重排表；“颜色浓度”只是部分资源的视觉表现，不是字段定义；
- `squa +0x0C`：桥覆盖方向，`0` 南北、`1` 东西；
- `squa +0x0D`：地域线/都市线两组北东南西掩码，并选择 `G_MapMkr.s9` 的 `10794..10805` 图片资源。

### 地图对象 footprint

可信原作观测给出都市以中心坐标为锚点覆盖 5×5 方格、港口覆盖 3×3 方格。运行时代码的 `get_map_object_rect -> quantize_map_rect_to_square_grid -> enumerate_map_object_squares` 与此建模方式相容：对象矩形先按 5 个地图坐标单位量化成 SQUARE 半开矩形，再按 `x + y × 200` 逐格枚举；出征覆盖层通过 `expand_map_objects_to_squares` 使用实际对象矩形，而不是只读取中心格或 `ZoneCode`。

`D_Sce002.S9` 的具体实例已闭合：洛阳 `city[15]` 的 `(89,64)` 对应 `x=87..91/y=62..66`，25 格 `ZoneCode` 均为 15，内部 3×3 为 `square_flags_02=0x4201`；带方港 `bldg[0]` 的 `(184,26)` 对应 `x=183..185/y=25..27`，9 格 `ZoneCode` 均为 50，仅中心为 `0x4201`。完整矩阵见 [RE-SAN9PK-0052](../../research/findings/RE-SAN9PK-0052.md)。

因此，重制版领域模型应把对象 footprint 与方格记录分开：方格保存地形、过渡和区域等模板属性，都市/港口/工作物从固定建筑定义取得占用几何。`fbld +0x0C/+0x0E` 分别控制 X/Y 尺寸，运行时统一乘 5 后以对象坐标为中心构造轴对齐半开矩形；17 类没有旋转或不规则分支。完整尺寸只有 5×5、3×3、1×3、3×1、1×5、5×1。`square_flags bit0/14` 是静态模板标志，不能替代对象矩形；例如代码 4 的运行时 footprint 是 1×5，但静态样本只有中间 3 格置 bit0。

### 重制渲染颜色精度

原作将调色板颜色和混合结果写入 RGB555 目标画面。重制版正式地图渲染依 [DEC-RENDER-0001](../decisions/DEC-RENDER-0001-high-color-map-rendering.md) 使用 RGB888 或更高精度的 GPU 中间目标，不执行 RGB555 写回量化；RGB555 仅用于研究和可选兼容对照。该选择只提升颜色精度，不改变本规格已确认的资源选择、遮罩形状和过渡规则。

### 水面动态覆盖

原作在静态 `G_LLand` 水域底图之后创建独立 `G_LMask` 水面动画。terrain `9/10/15/16` 直接参与；terrain `12` 只在邻近合格 terrain `11` 桥锚点时参与。方格以 `10000 + (x & 1) + 2 × (y & 1)` 选择四种奇偶资源，每项 `384×32` 横向切为六个 `64×32` 帧，并由 motion sprite 以每帧 `200 ms`、六帧标称周期 `1.2 s` 循环推进。该计时值沿更新链直接与 WinMM `timeGetTime()` 的差值比较，单位已确认。已知创建链不读取 `squa +0x06`，因此该动画不消费 `water_terrain_auxiliary_code`。证据见 [RE-SAN9PK-0054](../../research/findings/RE-SAN9PK-0054.md)。

## 重制行为

导入器必须生成 40,000 个按文件顺序排列的方格定义，并保留所有原始字段。正式地图尺寸为 200×200，方格 `Id` 使用行优先线性索引：`Id = y × 200 + x`。`SerializedId`、`SquareFlags`、`ZoneCode` 和 8 个原始字节均可逐项回读；后 8 字节尤其不能重新合并成编辑器显示的 16 位字段。

导入器提供以下地图拓扑派生视图：

- `GridX`、`GridY`：由运行时记录序号计算，不从 `SerializedId` 推导；
- `CityId`：区域编码 `0..49` 的城市 ID；
- `ZoneRecordId`：区域编码 `50..249` 减去 50 后的 `bldg` 记录 ID。
- `OpaqueField0DLowEdgeMask`：`opaque_field_0D & 0x0F`，原作地域线的北、东、南、西掩码；
- `OpaqueField0DHighEdgeMask`：`(opaque_field_0D >> 4) & 0x0F`，原作都市线的北、东、南、西掩码。

导入器还提供跨表拓扑校验：城市坐标必须映射到对应城市区域编码；有坐标的据点必须映射到 `50 + bldg` 记录 ID；城市和据点不得占用同一格。未知方格字节不参与规则推断。

产品层将原 `OpaqueField02` 命名为 `SquareFlags` 并完整保留 `u16`。`PathBlockingBits`/`IsPathBlocked` 提供 `0x01FE` 的整体阻塞视图；`IsFacilityTemplateSquare`、`IsExcludedFromRegionSquareCount`、`AllowsFacilityShape3By1`、`AllowsFacilityShape1By3`、`IsRegionBuildingCoreSquare` 分别提供 bit0、10、12、13、14 的只读视图；`HasSquareFlagBit(0..15)` 可查询原始位，越界参数必须拒绝。导入器不生成原作运行时 bit11/15。

导入器只提供以下不改变原始值的派生视图：`RegionCode` 将 `0xFFFF` 映射为空；`OffsetValue`、`TerrainId`、`ElevationTransitionMaskValue` 和 `OpaqueField0BValue` 将 `0xFF` 映射为空；`NormalizedField0C` 将 `0xFF` 映射为目标路径观察到的 `0`。已确认字段使用 `WaterTerrainAuxiliaryCode`、`TerrainTransitionMask` 和 `ElevationTransitionMask`；规格语义不授权丢弃原始值或直接复刻原作运行时复合标志。

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

其中 `SquareFlags=966`、`ZoneCode=118`、`OffsetCode=4`、`TerrainCode=2`；`square.png` 和 `地形.png` 分别支持未知栏、地域、偏移和地形的界面对照。测试还覆盖 `0xFFFF` 与 `0xFF` 哨兵，原始值必须保持不变。

## 验收标准

- [x] 40,000×14 字节边界和逐字节布局由自动测试证明。
- [x] `ZoneCode`、偏移、地形和三个字节哨兵的派生视图由自动测试覆盖。
- [x] `0x06`、`0x07` 等单字节字段不会被按 16 位读取或重组。
- [x] 异常 chunk 大小会被拒绝。
- [x] 测试夹具不依赖原版文件、编辑器或 IDA 数据库。
- [x] 地图线性索引、城市/据点区域编码和坐标占用关系由自动测试证明。
- [x] `OpaqueField0DLowEdgeMask` 与 `OpaqueField0DHighEdgeMask` 在不改变原始字节的前提下提供；原作地域线/都市线语义和北东南西位序已有证据。
- [x] 已记录实际方格图寻路的四邻格规则、`opaque_field_02 & 0x1FE` 阻塞条件和 10/12 基础代价；其他移动类型入口另行验证。
- [x] 已记录地图方格的 64×32 等距投影、底图资源编号选择、像素合成链和 `+0x0A/+0x0D` 覆盖资源消费者。
- [x] `land_texture_variant` 的资源变体表、特殊 atlas 排序重排，以及疆界四方向/两样式/三形态和重合优先级由自动测试覆盖。

## 未决问题

无阻塞正式剧本方格导入的字段未决项。既有 `SQUA-01..05` 已在[统一研究清单](../../research/scenario-field-research-checklist.md#已关闭字段队列)关闭。

GPU shader 与混合精度、原作 `ZoneCode` 之外的重制地图对象关系属于产品设计，若改变当前规格须更新目标行为与验收，是否新增 DEC 按[文档规则](../README.md)判断；编辑器保存行为归入 [`EDITOR-01`](../../research/scenario-field-research-checklist.md#范围外与后续扩展)，不作为本规格未决字段。

## IDA 同步记录

2026-08-19 至 2026-08-22 已将本规格涉及的已确认语义同步到项目主数据库 `research/analysis/San9PK-TC-101.i64`：

- 寻路函数：`find_square_grid_path`、`init_square_path_graph`、`solve_square_path_graph`、`run_square_graph_search`、`expand_square_neighbors_4way`、`build_square_path_costs`、`square_has_blocking_bits`、`square_has_slow_terrain`；
- 地图绘制函数：`redraw_map_scene`、`draw_visible_square_tiles`、`redraw_visible_square_tile_pairs`、`draw_square_tile_records`、`draw_square_tile_texture`、`build_square_edge_overlays`、`square_to_isometric_anchor`、`square_to_isometric_center`、`screen_to_square_coords`、`adjust_square_visual_position`、`calculate_square_edge_visual_position`、`draw_visible_bridge_overlays`、`refresh_map_square_boundaries`；
- 资源函数：`load_map_visual_resources`、`get_square_terrain_family`、`terrain_uses_edge_transition_variants`、`calculate_land_tile_atlas_index`、`calculate_square_land_tile_atlas_index`、`select_land_tile_resource`、`select_land_tile_subresource`、`select_map_mask_resource`、`get_loaded_resource_by_id`、`composite_land_tile_pixels`、`composite_mask_tile_pixels`、`draw_square_mesh_overlays`、`create_square_mesh_visual`、`build_minimap_terrain_texture`。
- `CSquareData` 字段接口：`deserialize_squa_record`、`get_csquare_value`、`format_square_property_value`；其中 `+0x06` 的 getter/格式化证据见 [RE-SAN9PK-0034](../../research/findings/RE-SAN9PK-0034.md)。
- 标志/标记函数：`square_has_overlay_marker_bit`、`update_runtime_flags_34`、`square_allows_facility_shape_3x1`、`square_allows_facility_shape_1x3`。

字段来源、阻塞条件、四邻格扩展、等距投影、地貌合成和覆盖层资源链已同步并回读确认；bit9 helper 保持保守名称 `square_has_overlay_marker_bit`。未修改程序字节、函数边界或结构类型。详细证据见 [RE-SAN9PK-0051](../../research/findings/RE-SAN9PK-0051.md)、[RE-SAN9PK-0052](../../research/findings/RE-SAN9PK-0052.md)、[RE-SAN9PK-0053](../../research/findings/RE-SAN9PK-0053.md) 与 [RE-SAN9PK-0056](../../research/findings/RE-SAN9PK-0056.md)。
