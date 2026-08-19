# RE-SAN9PK-0032：寻路可通行条件与地图方格资源绘制链

- 状态：核心数据流已确认；具体移动类型对成本字段的全部差异仍待验证；IDA 标注已同步
- 日期：2026-08-19
- 分析者：Codex
- 结论类型：IDA 直接证据；部分资源包对应关系为推断
- 置信度：高（寻路阻塞条件、四邻接、方格到资源编号）；中（各资源编号在画面上的具体图案）
- 关联来源：无
- 关联实验：无
- 关联规格：`BEH-IMPORT-0014-scenario-square-records.md`

## 调查问题

1. 真正的地图寻路如何判断一个方格能否进入。
2. 地图绘制如何从方格数据得到屏幕位置、瓦片/纹理和叠加图片资源。
3. 方格地形编号如何归并为地形族，以及这些名称分别来自哪一层。

## 只读证据

### 1. 寻路链与可通行判断

坐标寻路入口 `find_square_grid_path` 将起点、终点坐标换成方格索引，构造 `init_square_path_graph` 的方格图，随后由 `solve_square_path_graph -> run_square_graph_search` 执行图搜索。图扩展函数 `expand_square_neighbors_4way` 明确只检查四个邻格：`index+1`、`index+right`、`index-1`、`index-right`，没有斜向邻接。

节点初始化在 `build_square_path_costs`：

```c
if (square->word_34 & 0x1FE) {
    node->move_cost = 0xFF;
} else {
    node->move_cost = (terrain_family(square) == 4) ? 12 : 10;
}
```

其中 `square_has_blocking_bits` 直接实现 `square->word_34 & 0x1FE != 0`；`expand_square_neighbors_4way` 对每个邻格检查 `node->move_cost != 0xFF`，等于 `0xFF` 就不加入开放表。因此在这条实际方格图搜索中：

- `CSquareData +0x34` 的 `0x1FE` 位段是“不能进入”的硬阻塞条件；
- 该运行时字段来自 `squa` 记录的 `+0x02` 两字节字段；
- 未阻塞方格可进入，基础步长为 10，地形族为 4 时为 12；
- 城市/据点、部队和距离因素随后修改代价，但本段代码中它们不是把普通方格改成 `0xFF` 的主条件。

因此，疆界线使用的 `squa +0x0D` 四方向掩码不能当作通行标志；它在 `refresh_map_square_boundaries`/`build_square_boundary_segments` 的疆界线绘制链中消费，和寻路阻塞字段是两条不同数据流。

### 1.1 地形编号到地形族的完整映射

`get_square_terrain_family` 是依据函数行为写入 IDA 的研究层名称，不是原程序保留的符号。函数读取 `CSquareData +0x10` 的地形编号，并用固定 `switch` 返回 `0..7` 的族编号。程序中尚未发现这 8 个族编号对应的正式中文名称表。

下表的“原作地形名”来自本地消息表语义目录，并已由原版编辑器 `Square` 地形下拉框对照；“研究层概括名”只为便于讨论，不得登记为原作正式名称。完整语义目录含原版批量文本，只保存在被忽略的 `local/generated/research-catalogs/`，不进入 Git。

| 地形族编号 | 原作地形编号与名称 | 研究层概括名（非原作名称） |
| ---: | --- | --- |
| `0` | `0` 森 | 森林族 |
| `1` | `2` 土、`4` 土草、`7` 土湿 | 土地族 |
| `2` | `3` 草、`5` 草森、`8` 草湿 | 草地族 |
| `3` | `6` 湿地 | 湿地族 |
| `4` | `10` 水、`15` 水贰、`16` 水参 | 水域族 |
| `5` | `11` 桥 | 桥梁族 |
| `6` | `1` 岩、`13` 岩草、`14` 岩土 | 岩地族 |
| `7` | `9` 浅濑、`12` 水畔 | 水岸族 |

对有效地形编号 `0..16`，上表覆盖全部输入。函数的 `default` 分支也返回族 `2`，但这不把范围外编号确认为有效地形。由此可知，寻路中的“地形族为 4”明确指原作地形 `水/水贰/水参`，不是地形编号 `4` 的“土草”；“土草”映射到地形族 `1`。

### 2. 方格坐标与底图绘制

主绘制刷新 `redraw_map_scene` 调用 `draw_visible_square_tiles/redraw_visible_square_tile_pairs`，两者枚举可见区域并交给 `draw_square_tile_records -> draw_square_tile_texture` 绘制方格。

方格不是直接画成普通屏幕矩形。`square_to_isometric_anchor` 将逻辑坐标映射为等距投影位置，横向步长 64、纵向步长 32；`square_to_isometric_center` 再加上 `(32,16)` 得到方格中心。因此画面上看到的是 64×32 的菱形/等距瓦片几何，200×200 是逻辑方格数量，不是 200×200 个屏幕正方形。

`draw_square_tile_texture` 的资源/像素链为：

```text
方格绘制记录
  -> select_land_tile_resource 计算资源编号
  -> select_land_tile_subresource / select_map_mask_resource 选择方向、边缘和子图编号
  -> get_loaded_resource_by_id(resource_id) 取已加载资源对象
  -> composite_land_tile_pixels 或 composite_mask_tile_pixels 读取像素并合成到目标纹理
```

`get_loaded_resource_by_id` 通过全局资源表 `buf_0[resource_id]` 取资源对象；`composite_land_tile_pixels` 明确读取源图像字节、调色板颜色和目标纹理像素，做透明/颜色混合。`select_land_tile_resource` 内有底图资源编号表，例如 `10006`、`10331`、`10525`、`10717` 等，并根据地形/方向/边缘组合选择最终编号。

### 3. `SQUARE` 字段如何关联图片

- `squa +0x09 -> CSquareData +0x10`：经 `get_square_terrain_family` 转为地形族；`build_minimap_terrain_texture` 用这个地形族选择颜色并生成小地图/概览纹理。它不是一个直接图片编号。
- `squa +0x0A -> CSquareData +0x1C`：`draw_square_mesh_overlays` 把它传给 `create_square_mesh_visual`。后者用六个基础资源号 `[10972,11004,11036,11020,10988,11052]` 加上该字段，得到具体资源编号，再由 `get_map_marker_resource` 创建视觉对象；这是当前找到的最直接的“方格字段 -> 图片资源编号”关系，主要对应 MESH/COV 一类覆盖层。
- `squa +0x0D -> CSquareData +0x36`：用于疆界线的四方向掩码；`build_square_boundary_segments` 依据掩码和 `+0x0A` 选择 `10794..10805`，这些资源由 `G_MapMkr.s9` 提供。
- `squa +0x07 -> CSquareData +0x24`：是八方向几何/邻接掩码；`adjust_square_visual_position` 用其调整瓦片边缘坐标，`build_square_edge_overlays` 用其生成覆盖层。它不是直接的图片编号。

`load_map_visual_resources` 的加载表还显示：`G_LLand.s9` 从 10006 开始，`G_MapMkr.s9` 从 10794 开始，`G_MESH.s9` 从 10972 开始，`G_MapCov.s9` 从 10968 开始；因此资源编号和资源包是代码加载时绑定的，`SQUARE` 中通常存的是选择参数/偏移，而不是文件名。

## 结论与限制

- 事实：实际图搜索是四方向；方格不可通行的直接条件是运行时 `+0x34 & 0x1FE`，其源头是 `squa +0x02`。
- 事实：`get_square_terrain_family` 将 17 个有效地形编号固定归并为 8 个数字族；族 `4` 只包含 `水/水贰/水参`，地形编号 `4`“土草”属于族 `1`。
- 来源边界：地形成员名称来自原作消息表及编辑器对照；8 个中文族名只是研究层概括，目前没有原作正式名称证据。
- 事实：地图用逻辑方格坐标做 64×32 等距投影，底图会从资源表取图像并逐像素合成；不是把 `SQUARE` 直接当作一张 200×200 图片。
- 事实：`+0x0A`、`+0x0D`、`+0x07` 分别参与覆盖资源选择、疆界线掩码、几何边缘修正；它们不是同一个“可通行”字段。
- 推断：`+0x09` 更接近地形类别，底图具体图案由代码表、方向/边缘状态和资源包共同决定。
- 待验证：不同移动类型是否在另一套寻路入口中采用不同的阻塞位段；`10006` 等底图资源号在资源包内对应的具体图案名称仍需继续读取资源目录/资源对象元数据。

## IDA 状态

本轮已将核心寻路、地图绘制、等距坐标和资源选择函数同步到项目主 IDB；未修改程序字节、函数边界或结构类型。

已写入并回读确认的函数命名包括：

- 寻路：`find_square_grid_path`、`init_square_path_graph`、`solve_square_path_graph`、`run_square_graph_search`、`expand_square_neighbors_4way`、`build_square_path_costs`、`square_has_blocking_bits`、`square_has_slow_terrain`；
- 绘制：`redraw_map_scene`、`draw_visible_square_tiles`、`draw_square_tile_records`、`draw_square_tile_texture`、`square_to_isometric_anchor`、`square_to_isometric_center`、`screen_to_square_coords`、`adjust_square_visual_position`、`build_square_edge_overlays`、`refresh_map_square_boundaries`、`build_square_boundary_segments`；
- 资源：`select_land_tile_resource`、`select_land_tile_subresource`、`select_map_mask_resource`、`get_loaded_resource_by_id`、`composite_land_tile_pixels`、`composite_mask_tile_pixels`、`draw_square_mesh_overlays`、`create_square_mesh_visual`、`get_map_marker_resource`、`load_map_visual_resources`、`build_minimap_terrain_texture`。

关键函数入口已追加字段来源、`0x1FE` 阻塞判断、四邻格扩展、64×32 等距投影和资源编号链注释；已保存至 `research/analysis/San9PK-TC-101.i64`，并通过 IDA MCP 回读名称与注释。

关键函数：`find_square_grid_path`、`init_square_path_graph`、`solve_square_path_graph`、`run_square_graph_search`、`expand_square_neighbors_4way`、`build_square_path_costs`、`square_has_blocking_bits`、`draw_visible_square_tiles`、`draw_square_tile_texture`、`select_land_tile_resource`、`composite_land_tile_pixels`、`draw_square_mesh_overlays`、`create_square_mesh_visual`、`build_minimap_terrain_texture`。

### 本次 IDA 同步：出征界面临时覆盖层

2026-08-19 在项目主 IDB `research/analysis/San9PK-TC-101.i64` 中新增以下研究层命名，并在函数入口追加了已确认的数据流注释：

- `COrderShingunDlg_refresh_map_overlay`：出征窗口刷新地图覆盖层；
- `init_square_data_list`、`expand_map_objects_to_squares`、`append_map_object_squares`：把地图对象展开为方格列表；
- `enumerate_map_object_squares`、`get_map_object_rect`、`quantize_map_rect_to_square_grid`：读取并量化地图对象矩形；
- `apply_square_overlay_states`、`set_square_overlay_cell_state`、`get_square_overlay_cell_state`：写入/读取临时的逐格覆盖状态；
- 已有的 `draw_square_mesh_overlays`、`create_square_mesh_visual` 追加了状态值到 MESH/COV 资源族的注释。

同步后通过 IDA MCP 回读确认上述函数名及 `COrderShingunDlg_refresh_map_overlay` 的反编译调用链；`idb_save` 返回成功，保存路径为 `research/analysis/San9PK-TC-101.i64`。本次未修改程序字节、函数边界或结构类型。
