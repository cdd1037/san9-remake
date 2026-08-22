# RE-SAN9PK-0056：大地图非地貌图层坐标与桥资源绑定

- 状态：已确认
- 日期：2026-08-22
- 分析者：Codex
- 结论类型：IDA 数据事实、静态资源表事实、实验渲染实现
- 置信度：高
- 前置记录：[RE-SAN9PK-0053](RE-SAN9PK-0053.md)、[RE-SAN9PK-0055](RE-SAN9PK-0055.md)

## 1. 坐标不变量

逻辑方格 `(x,y)` 的中心与锚点为：

```text
center_x = 32 * (x + map_width - y)
center_y = 16 * (x + y + 1)
anchor   = center + (-32, -16)
```

各图层使用不同基准：

| 图层 | 位置 |
| --- | --- |
| 地貌目标 | 中心 + mask 位置与高度修正 - 局部裁剪原点 |
| 地貌源纹理 | 全地图中心 + mask 位置与高度修正 |
| 桥 | `anchor + (-64, 0)` |
| MESH | `anchor + (0, -10 * squa[+0x08])` |
| 建筑 | `center + (0, -10 * (squa[+0x08] - 1)) + 组合偏移` |

局部导出只改变目标裁剪，不能改变地貌源纹理相位。`D_Sce002.S9` 的 94 个活动建筑锚点中，`squa +0x08` 分布为 `1:76, 2:12, 3:4, 4:2`，建筑高度修正不能简化为常量。

## 2. 桥的资格、方向与调色板

`draw_visible_bridge_overlays (0x5194D0)` 仅绘制满足以下条件的方格：

- `terrain == 11`；
- `squa +0x02` 的 bit 9 置位；
- `squa +0x0C` 选择 `G_LHashi.s9` 的 `10004/10005` 方向资源。

静态 `(resource_id, palette_slot)` 表 `0x65E7D0` 给出：

| 图像资源 | 调色板 |
| --- | --- |
| `G_LHashi 10004/10005` | `P_LndAdd[0]`，slot 89 |
| `G_LMask 10000..10003` | `P_LndAdd[1]`，slot 90 |
| `G_MapCov 10968..10971` | `P_MapEtc[1]`，slot 113 |

因此桥不使用 `P_MapEtc[1]`；该调色板属于 `G_MapCov`。

## 3. 实现与验证

- `square_map/terrain.py` 定义中心、锚点、建筑基点、桥资格和资源选择。
- `square_map/render_map.py` 绘制地貌、桥与可选 MESH；建筑由 `render_buildings.py` 单独绘制。
- 单元测试覆盖中心/锚点差值、全地图纹理相位、建筑高度和桥资格。
- 桥区域实渲染命中资源 `10005`；使用 `P_LndAdd[0]` 呈木质棕色，地貌 fallback 为 0。
- 全图重构回归为 `13056×6656`、86,900,736 像素；与重构前基线的差异像素、差异通道和最大通道差均为 0。PNG 压缩流不同，但解压扫描线 SHA-256 同为 `c6200f7e68cab8eeb9384d8ab1459d907694f599babeb6b7cebf878662c4154d`。

## 4. IDA 标注

相关命名包括 `square_coords_to_isometric_anchor`、`square_point_to_isometric_center`、`square_coords_to_isometric_center_in_place`、`adjust_building_visual_position_for_elevation`、`get_building_visual_base`、`register_resource_palette_table`、`load_image_records_by_resource_id`、`load_palette_records_to_slots`、`get_palette_for_resource_id` 和 `map_visual_resource_palette_registry`。

相关函数、可重复函数和指令注释已统一为当前编号。保存后回读确认名称、坐标注释、桥绑定注释和静态表首两项 `(10004,89)/(10005,89)`；未修改程序字节、函数边界或结构类型。
