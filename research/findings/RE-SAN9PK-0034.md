# RE-SAN9PK-0034：SQUARE 未决字段与运行时标志位继续收敛

- 状态：已确认当前程序中的结构语义与消费边界；部分正式编辑器名称仍未知
- 日期：2026-08-30
- 分析者：Codex
- 结论类型：事实、受限推断、待验证分开记录
- 关联证据：[RE-SAN9PK-0024](RE-SAN9PK-0024.md)、[RE-SAN9PK-0032](RE-SAN9PK-0032.md)、[RE-SAN9PK-0033](RE-SAN9PK-0033.md)
- 关联规格：[BEH-IMPORT-0014](../../specs/behavior/BEH-IMPORT-0014-scenario-square-records.md)

## 结论摘要

本轮继续沿寻路、地图绘制和地图对象标记三条调用链检查 `CSquareData`。结论是：

1. 文件 `squa +0x02` 加载到运行时 `CSquareData +0x34` 后确实是复合标志字，不应整体命名为“可通行”。`0x1FE` 仍是已确认的方格图寻路阻塞位段。
2. `+0x02` 的 bit10、bit12、bit13、bit14 已分别收敛为地域对象计数排除、3×1/1×3 设施形状资格和地域对象核心格；bit11/15 则由载入后流程重建或写入。
3. 文件 `+0x0A` 加载到运行时 `+0x1C`，除 MESH/COV 和疆界资源变体外，还参与地图边缘锚点的垂直步进计算。
4. 文件 `+0x0D` 的低四位和高四位都按同一组四个地图方向槽位排列：`bit0=(x,y-1)`、`bit1=(x+1,y)`、`bit2=(x,y+1)`、`bit3=(x-1,y)`。两组位的线型/颜色语义仍未确认。
5. `+0x06` 的非 `0xFF` 值只分布于直接水面 terrain `9/10/15/16`，范围严格为 `0..7`。对方格基址、全部已知访问器调用者和 `CSquareData` RTTI 引用完成程序级负向审计后，只确认加载及通用 getter/格式化出口，未发现专用玩法或绘制消费者；因此命名为 `water_terrain_auxiliary_code`，保留原始字节，但不臆断成水流方向或动画编号。`+0x0B`、`+0x0C` 仍只确认加载、通用 getter 和 `+0x0C` 的归一化行为。

## `squa +0x02` / 运行时 `+0x34` 的部分位表

下表只记录已在目标代码中看到的直接读取或写入。没有列出的位不代表一定无效。

| 位 | 掩码 | 当前证据 | 当前研究名 | 结论边界 |
| ---: | ---: | --- | --- | --- |
| 0 | `0x0001` | Zone/城市对象条件链；部分移动模式直接测试该位 | `zone_condition_bit0` | 与 Zone/城市对象条件有关；不是寻路阻塞位 |
| 1..8 | `0x01FE` | `square_has_blocking_bits`；由 `build_square_path_costs` 转成阻塞节点 | `path_blocking_bits` | 已确认非零时该方格在这条方格图寻路链中不可进入 |
| 9 | `0x0200` | `square_has_overlay_marker_bit`；地图对象类型 11 的标记刷新，以及特殊对象邻格检查 | `overlay_marker_bit9` | 是地图标记资格/邻格条件，不等于通行位 |
| 10 | `0x0400` | 地域对象方格派生流程：置位格不计入对象方格总数 | `excluded_from_region_square_count` | 不清除 Zone 关联，也不直接抑制疆界绘制 |
| 11 | `0x0800` | `update_runtime_flags_34` 设置；清理函数遍历 40,000 格清除 | `transient_runtime_bit11` | 当前证据支持这是运行时临时标记，不是文件字段的独立业务含义 |
| 12 | `0x1000` | `square_allows_facility_shape_3x1`；设施代码 11/13/15，代码 16 的 3×3 同时接受两位 | `allows_facility_shape_3x1` | 静态地图模板中的设施形状资格 |
| 13 | `0x2000` | `square_allows_facility_shape_1x3`；设施代码 10/12/14，代码 16 的 3×3 同时接受两位 | `allows_facility_shape_1x3` | 静态地图模板中的设施形状资格 |
| 14 | `0x4000` | `derive_region_building_square_membership` 把置位格追加到地域对象核心格列表 | `region_building_core_square` | 静态地图模板中的地域对象核心格标记 |
| 15 | `0x8000` | `solve_square_path_graph` 结束后的城市相关列表统一设置 | `transient_city_footprint_bit15` | 当前证据支持这是城市路径/覆盖范围临时标记 |

### 位 12、13 的设施形状边界

两位不是抽象的“城市条件门”。自动建设与相关选择链分别通过两个资格 helper 检查它们，随后由构造代码集合确定设施形状：代码 `11/13/15` 是 3×1，代码 `10/12/14` 是 1×3，代码 `16` 是 3×3 并同时接受两位。静态样本的连通分量也分别以横向 3 格、纵向 3 格为基本形状；相邻候选可合并成更大矩形，因此不能把每个连通分量直接等同于单个设施。完整证据见 [RE-SAN9PK-0019](RE-SAN9PK-0019.md)。

### 位 11、15 不是源文件业务位的直接证据

目标代码通过通用 setter 在运行时设置 bit11 和 bit15，并分别存在整图清除或城市路径收集后的批量设置流程。它们说明运行时对象会复用 `+0x34` 保存临时状态；这不能反推 `squa +0x02` 的原始位在剧本编辑器中就是同名勾选项。

## `squa +0x0A` / 运行时 `+0x1C` 的新增消费者

已有证据确认：

- `draw_square_mesh_overlays` 将运行时 `+0x1C` 传给 `create_square_mesh_visual`；后者以该值作为六组基础 MESH/COV 资源编号的偏移/选择参数。
- `build_square_boundary_segments` 也使用该值参与 `10794..10805` 疆界片段变体选择。

本轮新增的直接证据来自 `calculate_square_edge_visual_position`：它把当前方格的 `+0x1C` 读成 `v5`，对传入的四方向边掩码 `a4` 做 `(v5 & a4)` 测试；命中时把该边的垂直步进增加一格。也就是说，`+0x0A` 不是单纯的图片编号，至少还参与边缘视觉的锚点/高度调整。

因此 `map_visual_variant` 仍是比 `resource_offset` 更稳妥的研究名，但正式业务名称和各数值的编码表仍未确认。

## `squa +0x0D` / 运行时 `+0x36` 的方向顺序

`refresh_map_square_boundaries` 的邻格检查表实际使用以下四个坐标偏移：

| `k` | 邻格坐标偏移 | 对应低位 | 对应高位 |
| ---: | --- | ---: | ---: |
| 0 | `(0,-1)` | `0x01` | `0x10` |
| 1 | `(+1,0)` | `0x02` | `0x20` |
| 2 | `(0,+1)` | `0x04` | `0x40` |
| 3 | `(-1,0)` | `0x08` | `0x80` |

在采用 `x` 向右、`y` 向下的普通行列坐标表示时，这就是上、右、下、左。这里确认的是位与邻格槽位的对应关系，不是两组掩码各自代表的正式疆界类型。最终是否绘制还要经过邻格 Zone/城市对象检查、`+0x08` 偏移代码、`+0x0A` 变体和疆界资源表共同决定。

## `+0x06`、`+0x0B`、`+0x0C` 的当前结论

`deserialize_squa_record` 和 `get_csquare_value` 确认了它们的宽度与运行时落点：

- `+0x06 -> CSquareData +0x18`，通用 getter case 4；`CSquareData` vtable 的 slot `+0x24` 指向 `format_square_property_value`，其 `case 4` 调用同一 getter，并按 `0x%08x` 格式输出；
- `+0x0B -> CSquareData +0x2C`，通用 getter case 8，加载时 `0xFF` 归一为 `-1`；
- `+0x0C -> CSquareData +0x30`，通用 getter case 9，加载时 `0xFF` 归一为 `0`。

`+0x06` 已额外完成静态样本分布和程序级访问审计；`+0x0B/+0x0C` 尚不继承这一结论。

### `+0x06` 的静态分布

现有大地图逆向脚本解析出的 40,000 格分布如下：

| 值 | 方格数 |
| ---: | ---: |
| `0xFF` | 32,219 |
| `0` | 1,553 |
| `1` | 1,141 |
| `2` | 2,488 |
| `3` | 623 |
| `4` | 1,138 |
| `5` | 215 |
| `6` | 353 |
| `7` | 270 |

所有非 `0xFF` 值只出现在 terrain `9/10/15/16`；这四类 terrain 已由水面创建链确认为直接水面地形。四类水面地形中只有 5 格仍为 `0xFF`，而 terrain `15/16` 没有例外。这个分布足以把字段收敛为“水域地形辅助代码”，但不足以命名为“水流方向”“岸线方向”或某种动画帧。

### `+0x06` 的程序级负向审计

审计覆盖：

- `runtime_class_CSquareData` 的 53 个引用点，逐个检查对象字段 `+0x18`；
- 全部已知方格索引/坐标访问器的直接调用者；
- 方格静态基址的直接引用、载入写入点、通用 getter 和格式化出口；
- 地图底图、MESH/COV、疆界、水面动画、地图标记和寻路链。

检出的其他 `+0x18` 均属于单位数据、绘制记录、视觉资源或 sprite 虚表槽，不是 `CSquareData +0x18`。载入器是唯一确认的写入者，通用 getter/格式化接口是唯一确认的读取出口；没有找到专用玩法或地图绘制消费者。该结论限定于当前目标程序，不等同于证明历代编辑器或其他版本从不解释它。

### `+0x06` 的通用属性接口证据

`format_square_property_value` 是 `CSquareData` vtable slot `+0x24` 的实现。它对属性编号 `1、3、4、5、6、7、8、9、10、12` 统一调用 vtable slot `+0x18` 的 getter，再用 `0x%08x` 写入文本缓冲区；其中属性编号 `4` 正好对应 `get_csquare_value` 的 `case 4`，读取 runtime `+0x18`，也就是文件 `squa +0x06`。因此可以确认：

- **事实**：`+0x06` 有通用属性读取和十六进制格式化出口；
- **事实**：当前目标程序未找到它参与寻路代价、阻塞、底图、MESH/COV、疆界、水面动画或地图标记的专用计算；
- **事实**：静态样本把非哨兵值限定在直接水面地形，值域为 `0..7`；
- **受限推断**：`water_terrain_auxiliary_code` 是当前证据允许的最窄结构名，具体编码表仍未知。

这里的“可见”只表示进入通用对象属性接口，不等于已经确认它会改变游戏规则或地图图片。

### 继续追通用属性接口的上层调用

本轮又沿虚表调用继续向上追了两层：

- `sub_527140` 是通用对象属性面板的文本/布局函数，参数中有“对象指针”和“属性编号”；它先通过 vtable slot `+0x28` 取得显示宽度，再通过 slot `+0x24` 取得属性文本。
- 当前两个直接调用点位于 `sub_527FC0`，该函数在进入面板前明确用 RTTI 筛选 `CForceData`，因此这条已确认是势力对象属性面板，不是 `CSquareData` 方格字段面板。
- 另一条势力对象信息函数 `sub_525DA0` 虽然也调用 vtable slot `+0x24`，但其上层 `sub_52C280/sub_533600` 明确传入 `force_record`，并连续请求势力对象属性 `12、160、168`；`160/168` 也超出 `CSquareData` 格式化函数支持的 `0..12`，所以不能把它解释成方格属性显示。
- 对方格专用调用链的直接字段扫描仍未发现 `CSquareData +0x18` 被拿去做寻路、底图、MESH/COV、疆界或地图标记计算；地图绘制中看到的 `+0x18` 调用属于新建的渲染记录对象，不是方格对象字段。

因此当前更准确的结论是：`+0x06` 是水域地形辅助元数据，当前程序只保留并通过通用对象属性接口暴露它；没有证据支持它会改变地图或通行规则。导入器使用 `WaterTerrainAuxiliaryCode` 保存原始字节，不增加未经证实的枚举或归一化。

## IDA 写入与验证

基于本记录的直接消费者证据，项目主数据库 `research/analysis/San9PK-TC-101.i64` 写入了以下保守函数名：

- `calculate_square_edge_visual_position`
- `square_has_overlay_marker_bit`
- `update_runtime_flags_34`（后续 [RE-SAN9PK-0142](RE-SAN9PK-0142.md) 证明该 helper 也作用于场景世界状态，故更正原方格专用旧名）
- `square_allows_facility_shape_3x1`
- `square_allows_facility_shape_1x3`
- `is_constructible_facility_shape_3x1_or_3x3`
- `is_constructible_facility_shape_1x3_or_3x3`
- `increment_region_building_counted_square_count`
- `append_region_building_core_square`
- `derive_region_building_square_membership`
- `format_square_property_value`（原 `sub_45CB70`，CSquareData vtable slot `+0x24`）

其中两个设施形状判定函数和一个地域对象辅助函数原先被误定义为数据或边界不完整，本轮先只读确认原始字节与控制流后修复函数定义。另在 `deserialize_squa_record` 的 `+0x18` 写入点和 `get_csquare_value` 的 case 4 读取点记录水域分布与负向审计边界；未修改程序字节。

已回读确认名称和相关注释，数据库保存成功。

## 待验证

> **终态路由（2026-08-31）**：本节是方格字段调查中期留下的历史清单。`SQUA-01..05` 已由 `RE-SAN9PK-0051/0053/0054/0056/0136` 与[统一研究清单](../scenario-field-research-checklist.md#已关闭字段队列)关闭：缺少正式编辑器标签但无当前消费者的值按原始字段保留，不再作为玩法语义待办。编辑器保存行为统一归 `EDITOR-01`。

- bit0、bit9 对应的正式玩法/编辑器字段名称；
- bit11、bit15 的运行时标记是否会影响未覆盖的地图或指令流程；
- `+0x0A` 的数值编码表，以及它与 MESH/COV、疆界片段和边缘锚点的组合规则；
- `+0x0B`、`+0x0C` 的专用消费者；
- `+0x0D` 两组四方向掩码各自代表的疆界样式。
