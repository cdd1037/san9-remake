# RE-SAN9PK-0061：新局势力君主城市的兵役转化与增兵

- 状态：已确认核心转换；直接调度入口待闭合
- 日期：2026-08-22
- 分析者：Codex
- 结论类型：IDA 静态事实、用户黑盒观察、调度阶段推断
- 置信度：高（遍历条件、作用对象、数值公式）；中（直接调度入口）
- 关联来源：`SRC-SAN9PK-TC-101`
- 关联实验：用户观察到进入游戏后，有势力城市的士兵数相对剧本记录增加 10,000
- 关联规格：[BEH-IMPORT-0003](../../specs/behavior/BEH-IMPORT-0003-scenario-city-records.md)

## 样本与工具

- 样本：三国志 IX PK 繁体中文版 1.0.1.0，32 位 PE，2,636,800 字节
- SHA-256：`D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`
- 架构与映像基址：x86，`0x00400000`
- 工具：IDA 9.3、Hex-Rays、IDA MCP
- 项目主数据库：`research/analysis/San9PK-TC-101.i64`（Git 忽略，不提交）

## 调查问题

用户观察到进入游戏后，有势力城市的士兵数比剧本城市记录多 10,000。调查该差值是否来自剧本加载错误、固定加兵，或剧本载入后的独立运行时初始化。

## 只读证据

### 剧本载入保持原始士兵数

`deserialize_city_record`（`0x43A8E0`）读取城市记录 `+0x0C` 的 `u32` 士兵数，经 `sub_4373A0` 以 500,000 为上限写入城市运行时士兵字段。加载器在这一段没有按势力归属增加 10,000；字段边界和样本对照已经由 [RE-SAN9PK-0008](RE-SAN9PK-0008.md) 确认。

因此，剧本文件值与进入游戏后的差值属于载入后的状态转换，不能反向解释为 `city +0x0C` 的编码、导入偏移或默认值。

### 势力与君主城市筛选

`0x456C30` 起的初始化例程遍历 50 项势力运行时记录。每项依次要求：

1. 势力记录有效；
2. `is_primary_force_record` 返回真；
3. 经 `get_force_leader` 取得的势力君主有效；
4. `sub_44C230` 对君主返回真；
5. `resolve_person_current_city` 能解析出君主当前城市。

转换只作用于上述君主当前城市，不遍历该势力所属的全部城市。因此，当剧本中每个势力只有一座城市时，黑盒结果会表现为“所有有势力城市都增加兵力”；多城势力不能据此推断每座城市都会增加。

### 兵役人口与士兵转换公式

城市虚表 `+0xE0` 指向 `get_city_draft_population`，读取运行时 `city +0x1C0`；[RE-SAN9PK-0008](RE-SAN9PK-0008.md) 已确认该字段为兵役人口。初始化例程对君主城市执行：

```text
draft_transfer = floor(draft_population / 10)
draft_population -= draft_transfer
soldier_increase = min(12,000, draft_transfer + 5,000)
soldiers += soldier_increase
```

其中：

- `subtract_city_draft_population` 从兵役人口减去 `draft_transfer`；
- 常量 `0x1388` 是 5,000，常量 `0x2EE0` 是 12,000；
- `add_city_soldiers` 按计算结果增加城市士兵。

兵役人口为 50,000 时，`draft_transfer = 5,000`，因此士兵恰好增加 10,000，同时兵役人口减少 5,000。这与用户观察闭合。该规则不是无条件固定增加 10,000：兵役人口不同会产生不同增量，增量最高为 12,000。

## IDA 写入与回读

- 恢复 `0x456C30..0x456D09` 的完整函数边界，并命名为 `initialize_primary_force_leader_cities`。
- 将 `0x43A360`、`0x43A3B0`、`0x4373D0`、`0x43FDE0` 分别命名为 `get_city_draft_population`、`subtract_city_draft_population`、`add_city_soldiers`、`get_force_leader`，并应用 `San9CityRecord`、`San9ForceRecord`、`San9PersonRecord` 参数和返回类型。
- 将 `San9CityRecord` 的已确认尾部字段收敛到 `+0x1BC..+0x1EC`，保留未确认区域，总大小仍为 `0x1F0`；`resolve_person_current_city` 的返回类型同步为 `San9CityRecord *`。
- 在上述函数及 `sub_44C230` 写入事实和限制注释；后者只确认虚表 `+0x4C` 返回值位于 700–799，不强加业务名称。
- 强制重新反编译后，50 项遍历、筛选链、兵役人口字段、转换公式和城市/势力/人物类型均回读成功；未修改样本程序字节。

## 结论与限制

- 事实：剧本城市加载器先保留 `city +0x0C` 的原始士兵数；10,000 差值不是导入器附加值。
- 事实：后续初始化例程按有效普通势力筛选君主当前城市，并按上述公式转化兵役人口、增加士兵。
- 事实：作用对象是君主当前城市，不是势力拥有的全部城市。
- 推断：该例程属于开始新游戏时的势力状态初始化；代码邻域、状态内容和用户进入游戏前后的观察共同支持这一阶段判断。
- 待验证：当前数据库没有指向 `0x456C30` 的有效直接调用交叉引用，新局调度入口仍未闭合；`sub_44C230` 在此处的精确业务名称也未确认。
- 实现边界：这条转换不属于 `city` chunk 解析。重制版若继承它，应另立新局初始化行为规格和验收样例；在该规格确认前，不得把转换烘焙进导入器或内容数据。
