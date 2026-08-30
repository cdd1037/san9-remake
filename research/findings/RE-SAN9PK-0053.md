# RE-SAN9PK-0053：SQUARE 静态底图渲染链与场景对照

- 状态：部分确认（字段桥接、资源选择、遮罩合成、排序、缓存、季节调色板和两处场景对照已闭合；截图视口的逐像素配准仍待完成）
- 日期：2026-08-22
- 分析者：Codex
- 结论类型：IDA 事实、私有样本只读事实、实验对照、受限推断与待验证项分开记录
- 置信度：高（字段桥接、调用顺序、资源参数、排序、像素混合和缓存链）；中高（资源变体的美术语义）；中（截图到具体方格的配准）
- 关联来源：[SRC-SAN9PK-TC-101](../sources/SRC-SAN9PK-TC-101.md)
- 前置记录：[RE-SAN9PK-0034](RE-SAN9PK-0034.md)、[RE-SAN9PK-0051](RE-SAN9PK-0051.md)、[RE-SAN9PK-0052](RE-SAN9PK-0052.md)
- 关联规格：[BEH-IMPORT-0014](../../specs/behavior/BEH-IMPORT-0014-scenario-square-records.md)

## 1. 调查问题

本记录合并 2026-08-20 至 2026-08-22 的连续底图调查，回答四个问题：

1. `squa` 的 `+0x06` 与 `+0x0B` 在运行时方格对象和底图绘制链中究竟走到哪里；
2. 从可见方格筛选到底图、边缘、桥、疆界和 MESH/COV 覆盖，实际调用顺序是什么；
3. `G_LLand/P_Land_*` 与 `G_MASK/P_MASK` 如何完成源图循环取样、当前格裁切和边缘混合；
4. 虎牢关与云南截图差异中，哪些来自场景数据、季节调色板或实验渲染器错误。

调查使用项目主 IDA 数据库 `research/analysis/San9PK-TC-101.i64` 的反编译、反汇编、交叉引用和组件调用图，并只读对照工作区私有的 `D_Sce002.S9`、虎牢关与云南截图及编辑器截图。证据形成后同步了函数名和注释；没有修改程序字节、函数边界或结构类型。

## 2. 字段桥接与底图索引

`deserialize_squa_record`（`0x45C9B0`）已经确认的运行时映射为：

| 文件字段 | 运行时位置 | 本轮在绘制链中的观察 |
| --- | --- | --- |
| `+0x06` | `CSquareData +0x18` | 只在通用 getter 的 case 4 和属性格式化路径中闭合；没有进入本轮主地图底图、边缘、桥、疆界或 MESH/COV 组件 |
| `+0x07` | `+0x24` | 展开为 8 个邻接/对角边缘记录，并参与等距边缘位置修正 |
| `+0x08` | `+0x14` | 作为底图资源选择和垂直位置修正的偏移代码 |
| `+0x09` | `+0x10` | 原始地形编号；同时被映射为地形族 |
| `+0x0A` | `+0x1C` | 进入底图预计算索引、疆界片段和 MESH/COV 资源选择 |
| `+0x0B` | `+0x2C` | 进入底图预计算索引，也作为像素合成前的底图资源选择参数 |
| `+0x0C` | `+0x30` | 桥资源编号偏移，桥层使用 `10004 + value` |
| `+0x0D` | `+0x36` | 疆界线低/高四位方向掩码 |

`calculate_square_land_tile_atlas_index`（`0x51E1D0`）把四个字段直接传给 `calculate_land_tile_atlas_index`（`0x5193A0`）：

```text
terrain_code, offset_code, elevation_transition_mask, land_texture_variant
= runtime +0x10, +0x14, +0x1C, +0x2C
```

`calculate_land_tile_atlas_index` 的代码事实如下。设原始地形编号为 `t`、偏移为 `o`、高差掩码为 `e`、纹理变体为 `v`：

- 基础项为 `dword_65E728[t] << 4`，偏移项为 `o × 272`。`dword_65E728` 对 `t=0..16` 的值为 `[12,14,7,9,8,11,5,6,10,4,3,4,4,15,13,2,1]`；这只是索引表事实，不把它直接等同于资源文件名。
- 地形 `0..5、13、14` 使用高差掩码的置位数生成边缘子变体：置位数 `0/1/2/3/4+` 分别对应加项 `0/4/8/4/0`；当置位数为 2 且 `e` 为 `5` 或 `9` 时，加项改为 `12`。
- 地形 `9、10、15、16` 对 `v` 做特殊重排：`0→3`、`3→0`、`4→1`、`5→2`；`1、2` 保持不变，其他值走默认值。
- 结果是一个预计算的底图图集索引。记录构造函数把它写入 `record+0x0C`，并立即折入 `record+0x20 = x + right × (y + atlas_index × bottom)`；`sort_square_tile_draw_records` 按 `record+0x20` 排序。因此该索引的已确认绘制用途是决定记录覆盖顺序。`draw_square_tile_texture` 不会再从 `record+0x0C` 选择像素资源；最终资源对象仍由 `select_land_tile_resource` / `select_land_tile_subresource` 根据记录字段另一路选择。

`get_square_terrain_family`（`0x45C820`）对原始 `terrain_code` 的地形族映射为：

| 原始编号 | 地形族 |
| --- | ---: |
| `0` | `0` |
| `1、13、14` | `6` |
| `2、4、7` | `1` |
| `6` | `3` |
| `9、12` | `7` |
| `10、15、16` | `4` |
| `11` | `5` |
| 其他 | `2` |

这个族值用于慢速地形和小地图颜色等路径；它不是 `G_LLand.s9` 的直接图片编号。

## 3. 可见方格的底图记录

`build_square_edge_overlays`（`0x519D30`）读取 `CSquareData +0x24`，把命中的 `+0x07` 位映射到邻格与边缘子资源：

| 文件 `+0x07` | 邻格 | 子资源参数 |
| ---: | --- | ---: |
| `0x80` | 北 `(x,y-1)` | `1` |
| `0x40` | 南 `(x,y+1)` | `4` |
| `0x20` | 西 `(x-1,y)` | `2` |
| `0x10` | 东 `(x+1,y)` | `3` |
| `0x01` | 西北 `(x-1,y-1)` | `5` |
| `0x02` | 东北 `(x+1,y-1)` | `6` |
| `0x04` | 东南 `(x+1,y+1)` | `8` |
| `0x08` | 西南 `(x-1,y+1)` | `7` |

每个命中位调用 `append_square_edge_tile_record`（`0x520ED0`）建立一个边缘记录，最后调用 `append_current_square_tile_record`（`0x520E30`）建立当前方格的 type `9` 记录。两类记录都会携带地形、偏移、`+0x0A` 和 `+0x0B`。`+0x07` 不写入记录，而是在上游决定建立哪些邻接/对角记录。两类记录的 atlas 预计算结果都会立即折入 `record+0x20` 排序键；`record+0x0C` 同时保留该结果。

`draw_square_tile_texture`（`0x51BF50`）对绘制记录按 type 分派：

- type `9` 进入 `composite_mask_tile_pixels`（`0x51BD00`）；
- 其他 type 进入 `composite_land_tile_pixels`（`0x519860`）。

两条像素合成路径都会把地形、偏移、`+0x0A`、`+0x0B` 和记录的边缘类型传给资源选择/子资源选择函数，随后从等距中心和偏移量计算目标像素矩形。当前记录的 `record[6]` 来自 `CSquareData +0x1C`（文件 `+0x0A`）；文件 `+0x07` 则在 `build_square_edge_overlays` 中决定是否展开邻接/对角边缘记录。`load_map_visual_resources`（`0x51B940`）的注册表把相关资源组绑定为：`G_LHashi.s9` 从 `10004`、`G_LLand.s9` 从 `10006`、`G_LCloud.s9` 从 `10777`、`G_LMask.s9` 从 `10000`、`G_MapMkr.s9` 从 `10794`、`G_MASK.s9` 从 `10806`、`G_MapCov.s9` 从 `10968`、`G_MESH.s9` 从 `10972`；`get_loaded_resource_by_id`（`0x5BDD50`）再从已加载表取得资源对象。本轮实际像素/覆盖消费者已闭合的编号区间仍以桥 `10004 + value`、`G_MapMkr.s9` 的 `10794..10805`、以及 MESH/COV 的基础组为准。

像素调色板不是按原始 terrain 编号直接猜测的。静态资源注册表 `0x65E7D0` 以 `(G_LLand resource_id, palette_slot)` 绑定底图资源；`sub_519310`（`0x519310`）会按当前季节把 `P_Land_S/N/A/W.s9` 之一加载到同一组全局槽位 `91..103`。因此当前测试中，水域族 `terrain 9/10/12/15/16` 对应的 `G_LLand` 资源 `10315/10319/10323/10327` 使用活动调色板的第 `4..7` 组（资源变体决定具体组），而不是依据 terrain 编号套用第 `9..10` 组。该资源槽位事实解释了旧测试版水体偏土色的直接原因；固定使用 `P_Land_N.s9` 也不等于所有日期下的原作色调。

`select_map_mask_resource`（`0x518F60`）在 `+0x0A != 0` 时按高差值选择 `G_MASK.s9` 的 16 个 9 张子资源组；在 `+0x0A == 0` 时，还会按边缘记录字段 `+0x24` 的 flag bit0/bit1 选择 `10950..10958` 或 `10959..10967` 两组专用遮罩，`select_land_tile_subresource` 再按边缘记录 type `1..8` 选择组内子图。`sub_520ED0`（`0x520ED0`）确认 flags 由当前格 terrain、邻格 terrain 和邻格 offset 条件生成，并非直接读取 terrain 低位。`draw_square_tile_texture` 的当前方格 type `9` 走 `composite_mask_tile_pixels`，边缘记录走 `composite_land_tile_pixels`；这两条路径的遮罩、源图和调色板合成不是可省略的装饰层。局部 Python 测试已按 `0x610C20/0x610C24` 的 16×9 目标位置表、`0x6110A0` 的高差修正、`+0x07` 八方向展开和 `G_MASK/P_MASK`（含两组边缘 flag 专用遮罩）重画，水陆交界从硬直角块变为资源定义的过渡形状。

这里形成一个需要分层记录的结论：当前像素合成函数的直接四字段选择参数是 `terrain_code +0x09`、`offset_code +0x08`、`elevation_transition_mask +0x0A`、`land_texture_variant +0x0B`。`terrain_transition_mask +0x07` 的职责是在上游展开八方向边缘记录；它不作为这四个资源选择参数之一。`+0x06` 没有进入当前主地图像素路径。

## 4. 主地图绘制顺序

`redraw_map_scene`（`0x522040`）按脏矩形选择全量或局部路径；两者最终都经过同一组底图和覆盖函数。对一个可见矩形，已确认的顺序是：

```text
redraw_map_scene
  ├─ draw_visible_square_tiles / redraw_visible_square_tile_pairs
  │    ├─ screen_to_square_coords
  │    ├─ 收集扩大可见范围内的 CSquareData
  │    ├─ draw_square_tile_records
  │    │    └─ draw_square_tile_texture
  │    │         └─ composite_mask_tile_pixels 或 composite_land_tile_pixels
  │    ├─ create_square_water_surface_animation：逐格水面动画检查
  │    └─ draw_visible_bridge_overlays
  ├─ draw_map_edge_clouds：菱形地图外部底云与四边前景云（见 RE-SAN9PK-0148）
  ├─ refresh_map_square_boundaries
  ├─ draw_square_mesh_overlays
  └─ 单位、建筑、季节、地图对象等更高层视觉对象
```

### 4.1 可见方格与桥

`draw_visible_square_tiles`（`0x51C0E0`）先把屏幕矩形反算为逻辑坐标，再用 `square_to_isometric_center`（`0x51C930`）筛选扩大矩形中的方格对象；扩大范围是为了覆盖 64×32 等距瓦片以及边缘绘制所需的外扩像素。底图记录在 `build_or_load_land_tile_draw_cache`（`0x520FE0`）中按全图 `y/x` 生成，`build_square_edge_overlays` 为每格先追加边缘记录再追加当前记录；随后 `sort_square_tile_draw_records`（`0x518370`）按记录 `+0x20` 的 `x + right * (y + atlas_index * bottom)` 排序，最后才由 `draw_square_tile_records` 绘制。仅按局部 `(y,x)` 直接画会破坏山顶/山壁过渡。绘制完方格记录后，它遍历覆盖范围调用 `create_square_water_surface_animation`，再调用 `draw_visible_bridge_overlays`（`0x5194D0`）。

桥层只收集 `terrain_code == 11` 且 `square_flags_02 bit9` 置位的方格，以 `CSquareData +0x30`（文件 `+0x0C`）加 `10004` 取得桥资源，并以组 `31` 的地图视觉对象加入。当前代码路径把 bit9 当作桥锚点资格，但 [RE-SAN9PK-0052](RE-SAN9PK-0052.md) 已证明该位在都市/港口 footprint 中普遍存在，所以它不能被整体命名为桥字段。

### 4.2 疆界与 MESH/COV

`refresh_map_square_boundaries`（`0x521370`）在脏区域内逐格检查邻格、Zone 对象和城市条件，之后把 `+0x0D` 的低/高四位合并为两组 N/E/S/W 掩码，再调用 `build_square_boundary_segments`（`0x5191D0`）。该函数按 `+0x0A`、偏移及两组掩码选择 `G_MapMkr.s9` 的 `10794..10805` 资源变体，并创建组 `41` 的疆界片段。

`draw_square_mesh_overlays`（`0x51F140`）在底图和疆界刷新之后，读取每格的临时 overlay state 与 `CSquareData +0x1C`（文件 `+0x0A`），由 `create_square_mesh_visual`（`0x51E1F0`）在六组 MESH/COV 基础资源 `[10972,11004,11036,11020,10988,11052]` 上加上 `+0x0A`，再以组 `25` 插入可见对象。这个消费者是当前 `+0x0A` 最清晰的直接绘制证据。

`create_square_mesh_visual` 还把状态表 `[50,25,20,22,20,20]` 编码为 `((alpha << 8) | 2)` 写入资源对象 `+0x14`。`sub_5C7FB0`（`0x5C7FB0`）将其解释为混合模式 `2` 和源 alpha，随后进入 `BitBltAlphablend`；因此这组数不是像素尺寸的缩放系数。绘制坐标由 `square_to_isometric_anchor`（`0x51C8C0`）给出，横向锚点为等距中心减 `32`，纵向锚点为等距中心再减 `10 × CSquareData +0x14`。此前实验把 MESH 像素按 100% 不透明绘制且漏掉横向 `-32`，生成的实心红色结果不能作为原版外观依据。

这里的“临时 overlay state”不是 `squa` 字段：`get_square_overlay_cell_state`（`0x51CC70`）从地图对象的运行时覆盖网格（`this + 163`）读取状态，越界时返回 `6`；`apply_square_overlay_states`（`0x520CF0`）才把一个 `CSquareData` 列表和状态值写入该网格，然后重画 MESH/COV。`COrderShingunDlg_refresh_map_overlay`（`0x4F51D0`）已直接观察到普通对象覆盖使用状态 `0`，城市/港口路线覆盖使用状态 `5`。因此 MESH/COV 不是裸地图上按 `+0x0A` 自动铺满的底图层；`+0x0A` 只负责在已激活的状态组内选择 16 个高差变体。

资源实验也已闭合：工作区私有样本中的 `G_MESH.s9` 有 `96` 个 `64×44` 图像，资源号连续覆盖 `10972..11067`；六个状态组的基础资源号正是上述数组，每组再加 `+0x0A`。`P_MESH.s9` 有一个调色板，索引 `0` 是透明键色，图像索引 `1/249/250/252/253/255` 是实际覆盖颜色。`G_MapCov.s9` 另有 `4` 个 `1024×768` 大图，配套注册的是 `P_MapEtc.s9`；图像观察呈灰度云雾/遮罩形态，当前调用链没有把它接入 `create_square_mesh_visual`，不能与这 96 个 MESH 子图混为一层，也不能据此确认它是高差覆盖。

Python 渲染器已增加显式诊断参数 `--mesh-state 0..5`，并支持 `--mesh-elevated-only` 只把 `squa +0x0A != 0` 的方格作为测试输入；现已按状态 alpha 表和原版横向 `-32` 锚点绘制。该模式是“资源/坐标/高差变体”验证，不是原版裸地图状态的复原；省略 `--mesh-state` 时仍不绘制 MESH/COV。

## 5. 像素合成、资源边界与缓存

### 5.1 当前格与边缘格使用不同路径

`composite_mask_tile_pixels` 对当前格 G_MASK 的每一行寻找首个与最后一个非零像素，再连续复制两者之间的源图横段；它不是逐点复制所有非零遮罩像素。边缘记录才由 `composite_land_tile_pixels` 逐像素测试遮罩，并以 P_MASK 红通道作为目标保留权重：

```text
result = (source × (256 - mask_red) + target × mask_red) >> 8
```

两条路径都只对每条扫描线的**源起点**按 `image_width - 64` 取模，随后让源指针连续前进，最多复制 64 像素；扫描线内部的每个像素不能再次取模。当前格的起点是 `(global_source_x + first_nonzero_x) % (image_width - 64)`，边缘格的起点是 `global_source_x % (image_width - 64)`。南皮 `(134,30)` 城下与河内港花丛对照暴露了实验渲染器原先的逐像素回卷：花丛扫描线跨过周期末端时会在格内跳回图库左侧，形成错块、拉伸或倾斜感；未跨界的花丛则近似正确。渲染器现已改为一次取模后连续取样，并以 `288` 像素宽图库、`224` 像素起点周期和跨周期的 64 像素扫描线测试锁定该不变量。

原作在 RGB555 目标画面上执行该式。研究脚本保留 8 位 RGB 输出，按 [DEC-RENDER-0001](../../specs/decisions/DEC-RENDER-0001-high-color-map-rendering.md) 不把 RGB555 写回量化带入正式重制渲染；这不会改变资源、遮罩或 256 权重规则本身。

图库循环取样使用减去目标缓存原点之前的全图等距坐标。局部导出窗口只影响目标画布位置，不能改变同一逻辑方格在图库中的取样相位。源位图行地址按 `base + y × stride + ceil(x × bpp / 8)` 计算，没有源图纵向翻转；目标 surface 的可选翻转只是底向上存储映射。

### 5.2 地形资源合法范围

`select_land_tile_resource` 使用 17 项地形下界/上界表验证 `offset_code`。若偏移非法，函数保留该地形资源基址，不把偏移钳制到下界。terrain 6 的合法范围是 `1..1`，基址为 `10523`、纹理变体步长为 `1`，所以变体 `0/1` 只能选择 `10523/10524`。

早期实验把 terrain 6 下界误记为 `0`，导致合法 `offset=1` 跨族选到 terrain 2 的 `10525/10526`，是云南左下区块误呈土色的直接原因。该表项、非法偏移行为和边缘 256 权重均已在脚本中修正并由单元测试锁定。修正后仍存在的土色带来自实际 `terrain_code=2`，当前证据不支持把土格强制改为草地或虚构通用植被覆盖层。

### 5.3 绘制缓存

`build_or_load_land_tile_draw_cache` 先尝试读取 `D_LandNN.s9`；缓存不可用时遍历 200×200 方格，由 `build_square_edge_overlays` 构造边缘与当前格记录，排序后序列化缓存并安装记录数组/列表。已确认的记录和缓存辅助函数包括：

- `initialize_square_tile_draw_record`、`set_square_tile_draw_record_coords`、`set_square_tile_draw_record_mask_flag`；
- `allocate_square_tile_draw_record`、`sort_square_tile_draw_records`；
- `select_current_tile_mask_resource`、`append_current_square_tile_record`、`append_square_edge_tile_record`；
- `build_or_load_land_tile_draw_cache`；
- `get_bitmap_pixel_pointer`、`get_surface_pixel_pointer_checked`、`blit_bitmap_resource_to_surface`、`decode_blit_mode_and_alpha`。

`sort_square_tile_draw_records` 并非稳定排序。它先按原顺序取出全部记录，再逐条插回有序链表；若新键小于当前首尾键的整数中点，则从头查找并插在第一个 `>= new_key` 的节点前，同键记录因而前插；否则从尾查找并插在最后一个 `<= new_key` 的节点后，同键记录因而后插。最终键序仍是升序，差异只存在于同键组内。研究脚本用 `key -> deque` 复刻前插/后插规则，再统一按 key 展开，避免照搬原链表的 O(n²) 扫描。以 `D_Sce002.S9` 生成的 96,489 条记录对照工作区 `D_Land00.s9`，字段及顺序 96,489/96,489 完全一致；因此无需把缓存文件作为渲染输入依赖。

## 6. 虎牢关与季节调色板对照

`D_Sce002.S9` 中洛阳是 `city[15]`、中心 `(89,64)`；东侧 `bldg[49]` 位于 `(96,69)`，所属城市为 15，`fixed_building_code=4`，与编辑器的“关 / 雄关（南北）”及用户截图中的虎牢关相符。周围方格的 `ZoneCode=99`。截图山壁属于底图合成链，不是建筑本体、MESH 红色覆盖或 `G_MapCov` 全屏图。

虎牢关窗口同时使用多种图库：

| `terrain_code` | 编辑器名称 | `G_LLand` 主要资源区间 | 活动调色板记录 |
| ---: | --- | --- | ---: |
| `0` | 森 | `10331..10521` | `8` |
| `1` | 岩 | `10006..10196` | `0` |
| `2` | 土 | `10525..10715` | `10` |
| `3` | 草 | `10198..10253` | `1` |
| `4` | 土草 | `10717..10772` | `11` |

山顶、山壁和山脚来自“图库资源 + 全图源坐标循环取样 + G_MASK 裁切/混合 + `-10 × offset` 等距锚点”，不是运行时生成的 3D 高度网格。`StretchDIBits` 在已确认路径中使用相同的源/目标宽高，没有非等比拉伸证据。

`sub_519310` 按季节把 `P_Land_S/N/A/W.s9` 之一加载到相同的全局槽位 `91..103`。传入季节值 4 时由当前日期计算索引；`D_Sce002.S9` 起始于 190 年 1 月，应选 `P_Land_S.s9`。所以相同资源索引也会随季节改变色调。诊断渲染器的 `--land-palette auto` 已按场景起始月份选择调色板，也允许显式指定 `S/N/A/W`。

## 7. 云南对照与实验验证

`D_Sce002.S9` 的 `city[44]` 位于 `(69,191)`，都市 footprint 为 terrain 3 草地；周边同时包含 terrain `0/1/2/4/5/6/8`。这要求先完成截图视口到逻辑方格的配准，不能仅凭颜色把诊断图中的 terrain 2 判为错误。

云南对照先后确认并修正了三项实验偏差：当前格应按扫描线连续复制、图库取样应使用全图坐标、terrain 6 只能选择 `10523/10524`。修正后没有发现土格或草格上的通用“点缀层”；`create_square_water_surface_animation` 只处理水域与桥邻接例外，其完整动画证据见 [RE-SAN9PK-0054](RE-SAN9PK-0054.md)。

本轮验证结果：

- `python -m unittest test_render_square_map.py`：2 项通过；
- `python -m py_compile render_square_map.py`：通过；
- 重新导出的 `x=0/y=140/w=60/h=60` 与云南 `x=52/y=174/w=34/h=26` 均使用自动春季调色板且 `fallback_squares=0`，SHA-256 分别为 `80E2412D9C9F1897549A807F5C246FCF17C187AB2CC6FFF99C0B73068C3B471F`、`D6216B06AD1014DEF5EF5E2389EBE04C5CFE0D29FD7B738F2D961AA1632F04C2`；
- 虎牢关 `x=80..114/y=55..84` 已生成无建筑底图用于检查图库、遮罩、排序和季节调色板；生成物位于 Git 忽略目录。

## 8. 事实、推断与待验证

### 事实

- `+0x06` 的运行时槽位是 `CSquareData +0x18`；在本轮主地图绘制组件中未找到它作为底图、桥、疆界或 MESH/COV 资源输入。
- `+0x0B` 通过 `+0x2C` 进入底图预计算索引，并在像素合成前的 `select_land_tile_resource` 参数中直接出现。
- `+0x07` 的八方向位序、边缘记录建立和可见方格绘制顺序由代码直接闭合。
- `+0x0C` 的桥资源选择、`+0x0D` 的疆界两组方向掩码以及 `+0x0A` 的 MESH/COV 消费者由代码直接闭合。
- `G_LLand` 资源与活动 `P_Land_*` 调色板槽位的静态绑定由 `0x65E7D0` 的 `(resource_id, palette_slot)` 表直接给出；水域资源基址 `10315/10319/10323/10327` 的基准组使用调色板记录 `4`，其变体使用记录 `5..7`。
- `G_MASK/P_MASK` 的 16 个高差组、两组 `elevation_mask=0` 的边缘 flag 专用组、每组 9 个当前/边缘子图，以及 `0x610C20/0x610C24/0x6110A0` 的目标位置/垂直修正已闭合。
- 当前格扫描线连续复制、边缘格 256 权重混合、全图源坐标循环取样、terrain 6 资源边界与非法偏移回退已由代码和实验交叉闭合。
- 当前格与边缘格的横向循环只作用于扫描线起点；`0x51BE52/0x51BF2C/0x5199FE/0x519A4F` 共同确认连续复制期间源指针不再次回卷。
- `G_MESH.s9` 的 96 个资源、六组基础编号、`+0x0A` 变体偏移、64×44 尺寸和组 25 插入点已闭合；其是否出现仍取决于运行时覆盖网格，而不是 `squa` 单独决定。

### 推断

- `+0x0B` 是底图资源/变体选择值；黑盒中“同地形颜色浓度变化”应视为资源变体的表现，不宜直接建模为线性色彩系数。
- `+0x0A` 同时服务于预计算索引、疆界片段和 MESH/COV，说明它更接近跨覆盖层共享的高差/边缘状态，而不是单一图片编号。
- `record+0x0C` 没有在像素绘制函数中再次读取，但同一预计算值已在记录构造时折入 `record+0x20` 排序键；产品实现需要保留排序语义，不要求复刻冗余的独立槽位。

### 待验证

- `select_land_tile_resource` 内部表的每个 terrain/offset/`+0x0A`/`+0x0B` 组合与 `G_LLand.s9` 具体图案的视觉语义对应；资源号到调色板槽位本身已经由静态注册表闭合。
- `+0x0B` 的所有有效值与视觉变体的稳定映射，不能只凭少量颜色浓度观测外推。
- `+0x06` 在水域类地形、编辑器保存或其他非主地图系统中的专用消费者。
- `create_square_water_surface_animation` 的 terrain 12 桥邻接例外已闭合；各水域 terrain 的完整玩法名称仍待确认，动画链见 [RE-SAN9PK-0054](RE-SAN9PK-0054.md)。
- `G_MapCov.s9` 的四张大图在其他地图覆盖路径中的具体消费者与显示条件；目前只能确认资源形态和注册关系，不能把文件名直接解释为玩法语义。
- 虎牢关与云南截图的视口、建筑锚点和屏幕坐标到具体方格的逐像素配准。

## 9. IDA 写入与回读

项目主数据库为以下地貌函数保留 `[RE-SAN9PK-0053]` 证据注释：

- 字段与索引：`get_csquare_value`、`calculate_land_tile_atlas_index`、`calculate_square_land_tile_atlas_index`；
- 方格记录与像素合成：`append_current_square_tile_record`、`append_square_edge_tile_record`、`build_square_edge_overlays`、`draw_square_tile_texture`、`draw_visible_square_tiles`；
- 覆盖层与刷新入口：`draw_visible_bridge_overlays`、`refresh_map_square_boundaries`、`draw_square_mesh_overlays`、`apply_square_overlay_states`、`redraw_map_scene`。
- 记录、缓存与像素辅助：`initialize_square_tile_draw_record`、`set_square_tile_draw_record_coords`、`set_square_tile_draw_record_mask_flag`、`allocate_square_tile_draw_record`、`sort_square_tile_draw_records`、`select_current_tile_mask_resource`、`build_or_load_land_tile_draw_cache`、`get_bitmap_pixel_pointer`、`get_surface_pixel_pointer_checked`、`blit_bitmap_resource_to_surface`、`decode_blit_mode_and_alpha`。

`0x51BE52/0x51BF2C/0x5199FE/0x519A4F` 记录扫描线起点取模与连续源指针，`0x5183E5/0x518408/0x51842E` 记录同键前插/后插排序。保存并回读确认；水面动画注释归 [RE-SAN9PK-0054](RE-SAN9PK-0054.md)。未修改程序字节、函数边界或结构类型。

### 9.1 2026-08-22 表述更正

重新回读 `append_current_square_tile_record`、`append_square_edge_tile_record` 与 `draw_square_tile_texture` 后，已更正早期把 `+0x07` 写成记录内资源参数的表述：记录内 `record[6]` 实际来自文件 `+0x0A`；文件 `+0x07` 只在 `build_square_edge_overlays` 上游决定八方向记录展开。同时确认 atlas 预计算值在构造时已折入 `record+0x20` 排序键，不再作为“消费者未知”的槽位处理。

项目主数据库已替换上述三个函数的旧注释并回读确认；未修改程序字节、函数边界或结构类型。
