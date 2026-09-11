# RE-SANCALC-0001：SanCalc EXE 导入函数契约

- 状态：已确认（22 个 EXE 导入、共享 RNG 核心、边界与副作用）
- 日期：2026-08-22
- 分析者：Codex
- 结论类型：DLL 代码事实、EXE 导入/调用事实与 32 位探针复验
- 置信度：高
- 关联来源：[SRC-SANCALC-SAN9TC-001](../sources/SRC-SANCALC-SAN9TC-001.md)、[SRC-SAN9PK-TC-101](../sources/SRC-SAN9PK-TC-101.md)
- 关联记录：[RE-SAN9PK-0027](RE-SAN9PK-0027.md)

## 样本与范围

分析样本为 114,688 字节、SHA-256 `E6F51781694B151EDB9181F7F1E06A91078D18062B159147CDF91A36A98B3279` 的 32 位 `SanCalc.dll`，映像基址 `0x10000000`。工具为 IDA 9.3、Hex-Rays、批处理 IDAPython 和 MSVC x86 合成输入探针；项目主数据库为 `research/analysis/SanCalc-SAN9TC-001.i64`。

`San9.exe` 与 `San9PK.exe` 导入相同的 22 个函数。本文恢复这些入口及其直接共享的 `game_rand`/RNG 核心；其余未被 EXE 导入的重载不在闭合范围。

## 随机数与概率

### 全局序列

`advance_game_rng(state)` 使用 32 位回绕运算：

```text
state = state * 1103515245 + 12345
result = (state >> 16) & 0x7FFF
```

`game_srand(seed)` 直接覆盖全局有符号 32 位状态，`game_grand()` 返回该状态，`game_rand()` 推进一步并返回上述 15 位结果。探针从种子 1 得到 `38, 58, 13, 15, 51`，并回读到推进后的状态 `-112468174`。

### `rnd` 与 `kakuritsu`

`rnd(upper, random_kind, seed)` 的行为为：

- `upper < 2` 时返回 0，不消费全局序列；
- `random_kind == 11` 时返回 `game_rand() % upper`，并推进全局状态一次；
- 其它所有 `random_kind` 值走同一临时模式：保存全局状态，把 `seed` 下限夹到 1，对局部种子先推进一次并设为临时全局状态，再推进一次取模，最后恢复原全局状态；相同 `seed/upper` 可重复得到相同结果。

`kakuritsu(chance, kind, seed)` 严格返回 `rnd(100, kind, seed) < chance`。因此 `chance <= 0` 恒假、`chance >= 100` 恒真；中间值受 `15-bit % 100` 的轻微模偏差影响，不是浮点百分比。10,000 次固定种子流探针得到 `chance=1/50/99` 的命中数 `109/5028/9880`，与该比较和模运算相容。

这闭合了 `RE-SAN9PK-0027` 的外部概率缺口：EXE 传入的第一个参数就是阈值，不会在 DLL 内再次反转、缩放或夹到 0–100。

## 数学与位字段契约

| 函数族 | 已确认效果 | 边界与副作用 |
| --- | --- | --- |
| `muldiv(a,b,d)` | 先做 32 位 `a*b`，再做有符号整数除法 | `d==0` 返回 `32767`；商向零截断；乘积可回绕 |
| `rmul(value,percent)` | `muldiv(value, percent, 100)` | 继承 32 位回绕与向零截断 |
| `kajuu(v1,w1,v2,w2)` | `(v1*w1 + v2*w2) / (w1+w2)` | 权重和为 0 返回 0；中间量为 32 位有符号运算 |
| `calc_nisa(a,b)` | `max(a,b)-min(a,b)` | 是整数绝对差，但极值输入可回绕 |
| `sqr(value)` | `int(sqrt(double(value)))` | 正数向下截断；探针确认负数转换为 `INT_MIN` |
| `limadd(value,delta,cap)` | `sum=value+delta; sum>=cap ? cap : sum` | 不检测加法溢出；有符号/无符号回绕可能绕过上限 |
| `limsub(value,delta)` | `value>delta ? value-delta : 0` | 下限固定为 0 |
| `min/max` | 普通二元比较 | 分有符号 `int` 与无符号 32 位重载 |
| `minmax(value,lo,hi)` | `min(max(value,lo),hi)` | `lo>hi` 时结果为 `hi`，不交换边界 |
| `get_bit_data(value,mask)` | `mask==0 ? 0 : (value&mask)/lowbit(mask)` | 16 位与 32 位版本相同 |
| `set_bit_data(ref,mask,x)` | 清除 mask 后写入 `lowbit(mask)*clamp(x,0,mask/lowbit)` | 假定 mask 是连续位域；非连续 mask 可能把乘积写到 mask 外 |

探针实例包括：`muldiv(7,5,3)=11`、`muldiv(-7,5,3)=-11`、`rmul(123,45)=55`、`kajuu(10,2,20,3)=16`、`get_bit_data(0xB0,0xF0)=11`。这些结果与 DLL 代码及 EXE 的 C++ `__cdecl` ABI 一致。

## 对重制与 EXE 分析的约束

- EXE 调用点可把 `kakuritsu` 第一个参数直接解释为百分阈值；仍应在各业务调用点分别确认阈值的上游计算。
- 重制不需要复用原版 LCG 序列；若保留原作概率语义，应保留严格 `< chance`、离散整数抽样和边界行为，随机状态则使用项目自己的确定性 RNG。
- 原版的溢出和非法 mask 行为是 DLL 事实，不自动成为产品要求；现代实现若改变它们，应由对应玩法规格明确输入不变量或差异决策。

## IDA 写入与回读

- 为 22 个 EXE 导入入口应用了含参数名、调用约定和有符号性的函数类型，并追加 `[RE-SANCALC-0001]` 注释。
- 将 `0x10001200` 命名为 `advance_game_rng`，将 `0x10016080` 命名为 `game_rng_state`，并记录精确状态公式和 `random_kind == 11` 边界。
- 保存 `SanCalc-SAN9TC-001.i64` 后批量回读 24 个相关对象：24 个类型和 24 个证据注释均存在，无失败项。
- 将同一组 22 个函数指针类型同步到 `San9PK-TC-101.i64` 的 IAT `0x5FF2DC..0x5FF330`，并在 `0x580EBF` 记录概率阈值不再缩放的跨模块事实；保存后 22 个类型和注释全部回读存在。
- 未修改 DLL 字节；合成探针及反编译导出只保存在 Git 忽略的 `tmp/`。

## 地图与主界面消费者同步（2026-09-06）

在对应DLL项目主库的`0x10001180`保留既有入口契约，追加带本记录及RE-SAN9PK-0158标签的跨模块备注，记录EXE初始士气的平方根消费者（EXE 0x43F830及城市/设施初始化链）。先只读核对样本SHA-256、已有注释和类型，再保存并重新打开主库回读；注释一致，入口字节哈希和类型未变，重新反编译成功。调用者地址明确属于EXE，不混入DLL地址语义。详见[同步清单](../analysis/ida-write-records/map-ui-sync-2026-09-06.json)。
