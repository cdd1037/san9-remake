# RE-SAN9PK-0055：大地图建筑资源、组合与状态选择

- 状态：已确认；动画帧仍按静态快照处理
- 日期：2026-08-30
- 分析者：Codex
- 结论类型：IDA 数据事实、场景数据事实、实验渲染实现
- 置信度：高
- 前置记录：[RE-SAN9PK-0008](RE-SAN9PK-0008.md)、[RE-SAN9PK-0053](RE-SAN9PK-0053.md)

## 1. 资源与调色板

`load_map_visual_resources (0x51B940)` 与资源注册表确认：

| 图像资源 | ID | 调色板文件记录 |
| --- | --- | --- |
| `G_LBUILD.s9` | `5663..5946` | `P_LBUILD.s9[0..5]` |
| `G_LBAdd.s9` | `5947..5974` | `P_BAdd.s9[0..6]` |

`G_LBUILD` 的调色板分段为 `5663..5673 → 0`、`5674..5684 → 1`、`5685..5695 → 2`、`5696..5737 → 3`、`5738..5842 → 4`、`5843..5946 → 5`。资源 ID 足以确定图像流和调色板，不需要根据尺寸或视觉猜测。

## 2. 建筑组合模型

`get_building_composition_id (0x52EA40)` 以固定建筑代码、当前耐久和城市外观代码选择组合：

```text
composition = fixed_base[fixed_code] + damage_stage + 1
city composition += map_city_terrain_to_building_group(code) * 5
```

城市外观映射为 `2 → 0`、`0 → 1`、`1 → 2`。固定建筑代码 `0,2,3,4,5,7,9,12,13,16` 使用四个损坏阶段，其余使用三个阶段。

`compose_building_layers (0x531190)` 的组合记录包含：

```text
dynamic_flag, resource_offset_flag, suppress_flag,
resource_id, offset_x, offset_y, z_offset
```

每条记录独立选择资源并使用显式屏幕偏移；组合顺序是静态绘制顺序。`fbld` 的 `5×5` 城市、`3×3` 港口和 `1×5` 关卡表示地图占地，不表示将单图按占地居中。

样本闭合如下：

| 对象 | 输入 | 组合 | 记录数 |
| --- | --- | ---: | ---: |
| 洛阳 `city[15]` | 外观 `0`，耐久 `440/800` | `9` | 10 |
| 带方港 `bldg[0]` | 固定代码 `1`，耐久 `300/300` | `18` | 2 |
| 虎牢关 `bldg[49]` | 固定代码 `4`，耐久 `900/1000` | `33` | 6 |

城市三种外观的大型投影随耐久组合变化。北方组合 `1..4` 分别使用 `5918/5923/5925/5927`，中原组合 `6..9` 使用 `5907/5912/5914/5916`，南方组合 `11..14` 使用 `5929/5934/5936/5938`。

## 3. 开发度与调色板 Alpha

城市建筑对象从 `CCityData` 取得当前收益、当前收获和外观代码。带 `resource_offset_flag` 的记录按 `max(current_income, current_harvest)` 加资源偏移：

```text
0..300   → +0
301..600 → +1
601+     → +2
```

洛阳的当前收益/收获为 `560/150`，因此组合 9 的 `5682/5908` 选择 `5683/5909`。该选择与耐久阶段相互独立。

带动态标志的当前帧走 `BitBltKBMPPaletteAlpha`。按 [RE-KOEIGRPX-0001](RE-KOEIGRPX-0001.md)，`alpha=0` 保留目标，`1..127` 使用 128 分母混合，`alpha>=128` 复制源色；`P_LBUILD` 第六套调色板的 `0..64` 不归一化为全不透明。实验渲染冻结动画资源帧，但保留这条逐索引 Alpha 路径。

## 4. 固定建筑 footprint

`fbld +0x0C/+0x0E` 加载到固定定义运行时 `+0x38/+0x3C`。`get_region_building_footprint_size` 从对象绑定的固定定义读取这两个值；没有定义时才回退 1×1。`refresh_region_building_footprint_rect` 随后执行：

1. 取得对象运行时中心；
2. 将两个尺寸分别乘 5，转换为细粒度地图单位；
3. 用 `left = center_x - width/2`、`right = left + width` 及同样的 Y 轴公式构造半开矩形。

因此 `+0x0C` 是 X 轴 `FootprintWidth`，`+0x0E` 是 Y 轴 `FootprintHeight`。这条公共链没有旋转码、按类别交换长宽或不规则格集合。17 类定义为：

| 固定代码 | X×Y |
| ---: | ---: |
| `0` | `5×5` |
| `1` | `3×3` |
| `2` | `1×3` |
| `3` | `3×1` |
| `4` | `1×5` |
| `5` | `5×1` |
| `6..9` | `3×3` |
| `10/12/14` | `1×3` |
| `11/13/15` | `3×1` |
| `16` | `3×3` |

工作区 26 个可解析正式/教学场景的固定定义在尺寸上相同。对其中现存对象，城市 5×5、港口与代码 9 的 3×3，以及代码 2/3 的两个方向均与定义一致。代码 4 的运行时矩形是 1×5，但静态 `squa bit0` 只有中间 3 格置位，直接证明方格模板标志不是运行时 footprint 的替代品；运行时碰撞、到达和路径代价应使用对象矩形。

## 5. 实现与验证

- `square_map/building_compositions.py` 保存组合表，`square_map/buildings.py` 保存耐久、外观与开发度选择规则。
- `square_map/render_buildings.py` 仅负责建筑图层绘制。
- 单元测试覆盖资源偏移阈值、城市/港口/关卡组合、北方四个投影帧和 Alpha 边界。
- `D_Sce002.S9` 洛阳/虎牢关局部实渲染命中 4 个建筑对象、3 个组合，fallback 为 0。

## 6. IDA 标注

项目主数据库中的相关名称保持不变：`map_city_terrain_to_building_group`、`select_city_development_resource_offset`、`get_city_development_resource_offset`、`select_fixed_building_damage_stage`、`select_building_composition_id`、`get_building_composition_id`、`compose_building_layers`。

2026-08-30 的 footprint 调查先只读确认 `0x437310` 与 `0x449D00` 的原始字节和控制流，再修复原先误定义的数据/函数边界，并恢复相邻 `0x437380` 函数。新增保守名称：

- `set_region_building_fixed_definition`
- `get_region_building_footprint_size`
- `refresh_region_building_footprint_rect`
- `scale_size_by_5`
- `set_centered_rect_from_size`

上述函数记录了字段来源、X/Y 轴、乘 5 和轴对齐矩形边界。保存、强制重新反编译并回读确认名称和注释；程序字节未修改。

相关函数、可重复函数和指令注释已统一为当前编号，并保留 `0x5336DA` 的资源注册分段事实。保存和全库回读通过；未修改程序字节、函数边界或结构类型。

## 7. 2026-09-05 转换元数据复核

事实：建筑组合按28字节记录定位，30组完整数据块与已确认记录唯一匹配；组合31与研究抄录不完全匹配，未纳入本次包，190年剧本不引用它。

来源：RE-SAN9PK-0053 第10节登记的 EXE，既有组合表与源文件完整数据块交叉比较。转换验收见 [地图导入文档](../../docs/map-import.md)。

## Godot大地图以来的IDA同步复核（2026-09-06）

按首次地图接入提交`2d63db1`及其后续研究追溯，本记录在`San9PK-TC-101.i64`同步1处有证据的函数/地址备注：`0x531190`。已有正文保留，已被后续证据推翻的描述定点纠正。

完整修改与回读清单见[地图与主界面同步清单](../analysis/ida-write-records/map-ui-sync-2026-09-06.json)。本轮EXE共56处备注（其中4处为已恢复代码旧编号校正）、2个函数改名；53个地址/函数范围的字节哈希保持不变，43个相关函数强制重新反编译成功，原有15处动画/UI证据标签仍在。没有修改程序字节、函数边界或类型；不将反编译成功表述为文本逐字不变。
