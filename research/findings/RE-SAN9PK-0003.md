# RE-SAN9PK-0003：核心全局记录与索引访问器

- 状态：已写入项目 IDB
- 日期：2026-08-16
- 结论类型：事实与来源支持型工作结论
- 置信度：高
- 关联来源：`SRC-SAN9PK-TC-101`、`SRC-SAN9PKHARD`
- 前置记录：[`RE-SAN9PK-0002`](RE-SAN9PK-0002.md)
- 符号摘要：[`san9pk-tc-101.csv`](../symbols/san9pk-tc-101.csv)
- 类型摘要：[`san9pk-tc-101-types.csv`](../symbols/san9pk-tc-101-types.csv)
- 关联规格：无

## 证据

San9PKHard 的 `San9PKHardDlg.h` 同时给出七张表的基址、记录数和步长；各配置窗口又按相同步长读取整表，并在固定偏移读取字段。目标样本自身存在七个短小索引访问器，直接执行“索引 × 步长 + 基址”，边界常量与来源记录数一致。

目标样本中七个基址还分别出现 6–29 次直接字节引用。初始化/析构循环再次使用相同基址、记录数和步长，例如 `0x453EB4` 以 `0x1258EE0`、`0x352` 项和 `0x128` 步长遍历武将记录。来源和目标代码形成独立交叉验证。

## 应用结果

| 表 | 基址 | 数量 | 步长 | 类型 | 访问器 |
| --- | ---: | ---: | ---: | --- | --- |
| Zone | `0x1238078` | 200 | `0x1BC` | `San9ZoneRecord` | `get_zone_record` (`0x452F10`) |
| City | `0x124DB58` | 50 | `0x1F0` | `San9CityRecord` | `get_city_record` (`0x452F70`) |
| Force | `0x1253C38` | 50 | `0xD4` | `San9ForceRecord` | `get_force_record` (`0x453100`) |
| Person | `0x1258EE0` | 850 | `0x128` | `San9PersonRecord` | `get_person_record` (`0x4531B0`) |
| Building | `0x14FD4A0` | 17 | `0x50` | `San9BuildingRecord` | `get_building_record_or_first` (`0x453740`) |
| Killer | `0x14FDAD0` | 41 | `0x8C` | `San9KillerRecord` | `get_killer_record` (`0x453720`) |
| Array | `0x1500B70` | 28 | `0x88` | `San9ArrayRecord` | `get_array_record_or_first` (`0x453870`); `get_array_record_or_null` (`0x453890`) |

`Array` 和 `Killer` 是来源术语。本轮保留它们，不强行映射为 Army、Formation 或 Tactic。

## 边界行为

- Zone、City、Force、Person 和 Killer 访问器先检查索引上下界，非法时返回空指针。
- Building 和 `get_array_record_or_first` 的目标代码在非法索引路径把索引清零后继续计算，因此返回第 0 项而不是空指针。严格访问器 `get_array_record_or_null`（`0x453890`）在非法索引时返回空指针；`unit +0x22` 的加载路径使用严格访问器，详见 [RE-SAN9PK-0022](RE-SAN9PK-0022.md)。
- 这些边界行为来自完整目标函数体，可作为后续调查事实使用；是否原样进入重制规则仍需单独的行为规格决策。

## 类型骨架

IDA 大小检查结果：Person `0x128`、Force `0xD4`、City `0x1F0`、Zone `0x1BC`、Array `0x88`、Building `0x50`、Killer `0x8C`，全部与来源步长一致。

已命名字段仅覆盖配置工具明确读取的位置，包括武将姓名和四维、势力标志与指针、城市名称和上限、Array 的配置字节，以及 Killer 的熟练门槛/威力/间隔。其余区域用带偏移的 `unknown_*` 数组占位，保持布局精确。没有依据的位含义未展开为结构字段。

## 写入与验证

- 全局表 dry-run：7 项可写入、0 冲突、0 覆盖；原名均为对应基址的 `unk_*`。
- 访问器重命名 dry-run：7 项成功、0 失败；随后应用 7 个函数名和 7 个明确的 `__cdecl(int index)` 返回类型。
- IDA `type_inspect` 检查七个结构体总大小及所有已知字段偏移，无偏移漂移。
- 抽查 `get_person_record` 和 `get_city_record`：目标函数中的数量、乘法步长和基址均与类型一致。
- 抽查 `0x453EB4` 的武将循环：850 项、步长 `0x128`，与访问器和来源一致。
- 前一轮未修改任何程序字节或既有函数边界；本轮新增的 `0x460FF0` 函数定义见下文。
- 项目 IDB 已原位保存。

## 后续 IDA 写入与回读

2026-08-18 在项目主 IDB `research/analysis/San9PK-TC-101.i64` 中将严格访问器 `0x453890` 重命名为 `get_array_record_or_null`，并定义、命名 `0x460FF0` 为 `deserialize_unit_record`。回读确认前者在 `index >= 0x1C` 时返回空指针，否则返回 `san9_array_records[index]`；后者从 `unit +0x22` 读取索引并调用该访问器。本次未修改程序字节，详细写入清单和 `jnki` 顺序载入验证见 [RE-SAN9PK-0022](RE-SAN9PK-0022.md)。

## 限制

- 字段业务名是可信源码支持的工作事实，但尚未通过黑盒编辑器逐字段复验。
- Zone 和 Building 当前只有总大小骨架；不凭推测填字段。
- 本记录服务逆向工作，不自动升级为正式游戏数据格式或行为规格。
