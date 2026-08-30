# RE-SAN9PK-0019：`squa` 地图方格记录

- 状态：已确认区块边界、字段读取布局、建设选格资格及 `square_flags_02` 的静态/运行时分层；其余辅助字节按清单继续研究
- 日期：2026-08-30
- 结论类型：事实、反编译器修正与截图/样本对照
- 置信度：高（边界、字段宽度、目标运行时偏移、已观察字段）；中（字段业务名称）
- 关联来源：`SRC-SAN9PK-TC-101`、`SRC-SAN9PKHARD`、样本目录 `private/s9数据文件/`、编辑器截图 `private/剧本编辑器截图/square.png`、`private/剧本编辑器截图/地形.png`
- 前置记录：[`RE-SAN9PK-0018`](RE-SAN9PK-0018.md)
- 深入分析：[`RE-SAN9PK-0024`](RE-SAN9PK-0024.md)
- 关联规格：[`BEH-IMPORT-0014`](../../specs/behavior/BEH-IMPORT-0014-scenario-square-records.md)

## 区块与读取边界

`squa` 位于文件偏移 `0x1406`，大小 `560000` 字节，即 40000 条、每条 14 字节的方格记录。目标程序的 `deserialize_squa_record`（IDA `0x45C9B0`）把记录加载到 `CSquareData` 运行时对象；总加载器使用运行时基址 `0x12965B0`、步长 56 字节并循环 40000 次。

文件布局按原始字节列出如下：

| 偏移 | 大小 | 当前结论 | 运行时目标 | 归一化行为 |
| ---: | ---: | --- | ---: | --- |
| `+0x00` | `u16` | 序列化编号；`SerializedId` | 不写入对象 | 读取后丢弃，运行时编号使用循环序号 |
| `+0x02` | `u16` | 可变复合标志；`SquareFlags` | `+0x34`（有效字） | bit10/12/13/14 的模板语义及 bit11/15 的运行时复用边界已闭合；其它位按原始值保留 |
| `+0x04` | `u16` | 所属地域/Zone 编号；`ZoneCode` | `+0x28`（有符号运行时值） | `0xFFFF` 变为 `-1`，其他值保持 |
| `+0x06` | `byte` | 未确认字段 | `+0x18` | IDA 伪代码比较 `u8` 与 `0xFFFF`，条件不可达；按汇编原样保留 `0..255` |
| `+0x07` | `byte` | 未确认字段 | `+0x24` | 同上，`0xFF` 不被该函数归一化 |
| `+0x08` | `byte` | 偏移代码；`OffsetCode` | `+0x14` | `0xFF` 变为 `-1` |
| `+0x09` | `byte` | 地形编号；`TerrainCode` | `+0x10` | `0xFF` 变为 `-1` |
| `+0x0A` | `byte` | 未确认字段 | `+0x1C` | `0xFF` 变为 `-1` |
| `+0x0B` | `byte` | 未确认字段 | `+0x2C` | `0xFF` 变为 `-1` |
| `+0x0C` | `byte` | 未确认字段 | `+0x30` | `0xFF` 变为 `0` |
| `+0x0D` | `byte` | 未确认字段 | `+0x36` | 原样写入；通用 `CSquareData` getter 未覆盖该偏移 |

`+0x00`、`+0x02` 和 `+0x04` 是完整的 16 位小端字段；`+0x06..+0x0D` 必须按单字节读取，不能按 16 位对齐重组。

## 截图和样本对照

`D_Sce001.S9` 第 0 条的 14 个原始字节为：

```text
00 00 C6 03 76 00 FF 00 04 02 00 02 00 00
```

因此：

- `+0x02 = 966`，与 `square.png` 未知栏中显示的 0002 值一致；
- `+0x04 = 118`，目标 getter 将其作为所属地域引用，截图显示该方格属于“酒泉”；授权样本中的 `ZoneCode` 最大值为 186，和 200 条 Zone 表的范围相容；
- `+0x08 = 4`，与截图“偏移 4”一致；
- `+0x09 = 2`，与 `地形.png` 中编号 2 的“土”一致；
- `+0x06 = 0xFF` 是仍未命名的原始字节，不能因为截图未知栏的显示方式而改成其他宽度。

`CSquareData` 的通用 getter（IDA `0x45CFC0`）明确读出运行时 `+0x10,+0x14,+0x18,+0x1C,+0x24,+0x28,+0x2C,+0x30,+0x34`，所以这些来源字段都不能标记为完全未使用。运行时 `+0x36` 虽被加载，但没有被该通用 getter 覆盖；在没有更多消费者证据前仍保留原始字节，暂记为“加载但用途未确认”。产品字段 `SquareFlags` 保留完整 `u16`，局部已确认位只通过派生视图暴露。

全量样本进一步收窄了用途边界：正式 `D_Sce001..020` 与教学 `D_Ttrl00..05` 的 `squa` 区块逐字节相同，说明它是共享地图模板数据。`+0x02` 在城市/据点坐标及其 3×3 footprint 上出现 `0x4201、0x5201、0x6201` 等结构值，属于地图块/占用描述候选；`+0x0D` 的 52 个值则在地形边界和地图对象附近形成重复半字节模式，属于边缘/连接/图形选择码候选。`+0x0C=1` 仅覆盖 144 个地形 11 格中的 72 格，不能直接命名为桥标志。详细统计与优先级见 [`RE-SAN9PK-0024`](RE-SAN9PK-0024.md)。

## 实现与验证

- 新增 `San9ScenarioSquareArchive.ReadKnownRecords`，严格检查 `squa` 大小为 `40000 × 14`。
- 原始 3 个 `u16` 与 8 个字节全部保留，同时仅对 IDA 汇编已确认的哨兵行为提供派生属性。
- 新增单元测试覆盖逐字节布局、`0xFFFF/0xFF` 处理、地形/Zone/偏移样本和异常区块大小。
- 尚未把未知字节映射为重制规则；应通过编辑器单变量修改或更多运行时消费者交叉引用继续确认。
- 当前可暂缓：`+0x06/+0x07`、`+0x0A/+0x0B`、`+0x0C`、`+0x0D` 继续按各自清单研究；`+0x02` 的 `SQUA-01` 局部位义与静态/运行时边界已经关闭，未确认位仍原样保留。

## SQUA-01：`square_flags_02` 静态模板位与运行时复用

### bit0：普通设施建设选格

玩家建设动作按固定设施代码 `6..16` 建立对应的地图选格模式；代码 6“阵”对应模式 40。`is_construction_target_square_selectable(0x4F4A90)` 是鼠标启用态与实际点击共同复用的资格谓词：它以 `mode - 34` 还原固定设施代码，调用 `collect_fixed_building_footprint_squares(0x4738C0)` 从候选中心生成该设施的方格集合，并逐格验证。

对阵、砦、箭楼、城塞（代码 `6..9`，模式 `40..43`），生成集合均为中心 3×3 九格；候选必须不在地图最外圈，九格必须全部解析到同一个地域建筑对象，并且每格 `square_flags_02 bit0` 都为 1。该地域对象还必须存在于进入选格界面前准备好的允许列表中。`update_construction_target_hover_state(0x4F4E90)` 把谓词结果直接传给地图启用/禁用反馈，`handle_construction_target_map_input(0x4F4CB0)` 在接受点击前再次调用同一谓词，因此亮暗显示与实际可选条件没有两套分支。

这把 bit0 的语义从“已有建筑占用格”修正为更准确的“工作物格模板位”：已存在对象和预留的空建设点都可在静态模板中置位；当前是否已有设施则由 `ZoneCode` 解析出的地域对象运行时状态另行判断。`get_square_region_building(0x45C8A0)` 读取 `squa +0x04`，通过统一地域编码返回 `0..49` 的城市或 `50..249` 的 `bldg` 对象，旧名 `get_zone_city_record` 错误暗示它只返回城市。

方向化设施使用不同的逐格门：模式 `44/46/48`（代码 `10/12/14`）要求 bit13，模式 `45/47/49`（代码 `11/13/15`）要求 bit12；代码 16 的玩家模式 50 使用 3×3 与 bit0。自动设施候选筛选仍允许代码 16 同时通过 bit12 或 bit13 的偏好过滤，这与玩家选格时的模式 50 条件属于不同阶段，不能合并。

### bit10/12/13/14 与运行时复用

`derive_region_building_square_membership(0x459830)` 在 40,000 格载入并完成 Zone 关联后逐格执行两条独立派生：bit14 置位时，把方格加入对应城市/据点对象的核心格列表；bit10 **未**置位时，把该对象的计数格总数增加 1。因此 bit10 的直接语义是“排除于地域对象计数”，而不是“无 `ZoneCode`”。正式共享地图中 4,987 个 bit10 方格全部仍有合法 `ZoneCode`，其中 547 格还带非零 `boundary_edge_mask`；`refresh_map_square_boundaries` 也不读取 bit10。旧候选“无所属/不画疆域线”据此收窄为 `IsExcludedFromRegionSquareCount`，不得用它清空地域关联或抑制疆界层。

bit12/13 的代码和样本关系已经闭合：

| 模板位 | 方格访问器 | 自动建设筛选的 `fbld` 代码 | 记录形状 | 样本连通分量 |
| --- | --- | --- | --- | --- |
| bit12 / `0x1000` | `square_allows_facility_shape_3x1(0x45C8C0)` | 11、13、15、16 | 3×1 或 3×3 | 基础分量为横向 3×1；相邻候选可合并为 3×2、3×3、3×4 |
| bit13 / `0x2000` | `square_allows_facility_shape_1x3(0x45C8D0)` | 10、12、14、16 | 1×3 或 3×3 | 基础分量为纵向 1×3；相邻候选可合并为 2×3、3×3、4×3 |

对应设施代码 10/11、12/13、14/15 是栅栏、土砂、土垒的两种 1×3/3×1 记录；代码 16 的石兵为 3×3，所以两位都可接受。`select_automatic_construction_facility(0x4B3D00)` 根据方格 bit12/13 分别使用 `is_constructible_facility_shape_3x1_or_3x3(0x46DD90)` 和 `is_constructible_facility_shape_1x3_or_3x3(0x46DE00)` 过滤候选；另一组选格模式 44/46/48 与 45/47/49 也分别测试 bit13 与 bit12。这些位表示地图模板中的定向设施建设资格，不是现存建筑 footprint 的即时方向字段。

运行时复用边界同样可以明确划分：场景反序列化把完整 `u16` 复制到 `CSquareData +0x34`；当前所有已定位的通用位更新调用中，载入后重建会先对全图清除 bit11，再按地域对象计算重新设置，路径图流程另行设置 bit15。没有发现 bit10/12/13/14 的运行时生产者；它们由静态模板提供并被派生逻辑消费。因此产品层保留完整 `SquareFlags`，提供 bit10、bit12、bit13、bit14 的只读派生视图，不复刻 bit11/15 的原作临时状态。

## SQUA-01 的 IDA 同步与回读

2026-08-30 在项目主数据库中完成只读审计后：

- 修复并定义此前误作数据的 `0x46DD90`、`0x46DE00` 两个设施候选谓词，以及误判函数边界的 `0x4359C0` 核心格列表写入器；
- 将 `0x45C8C0/0x45C8D0` 重命名为 `square_allows_facility_shape_3x1` / `square_allows_facility_shape_1x3`；
- 将 `0x46DD90/0x46DE00` 重命名为 `is_constructible_facility_shape_3x1_or_3x3` / `is_constructible_facility_shape_1x3_or_3x3`；
- 将 `0x4358A0`、`0x4359C0`、`0x459830` 分别重命名为 `increment_region_building_counted_square_count`、`append_region_building_core_square`、`derive_region_building_square_membership`；
- 在 bit10/14 派生点、bit12/13 访问器和候选谓词、bit11 全图清除点及 bit15 路径标记点写入事实注释。

2026-08-30 又将 `0x45C8A0`、`0x4738C0`、`0x4F4A90`、`0x4F4E90`、`0x4F4CB0` 分别重命名为 `get_square_region_building`、`collect_fixed_building_footprint_squares`、`is_construction_target_square_selectable`、`update_construction_target_hover_state`、`handle_construction_target_map_input`，并在函数入口记录统一地域解析、3×3 bit0 资格及悬停/点击复用关系。

保存后回读确认上述函数边界、代码集合与位测试均保持；未修改程序字节。
