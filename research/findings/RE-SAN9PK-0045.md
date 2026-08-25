# RE-SAN9PK-0045：势力关系状态与模拟侧势力对处理链

- 状态：草案
- 日期：2026-08-19
- 分析者：Codex
- 结论类型：事实、推断和待验证项分开记录
- 置信度：高（flag 访问、筛选与调用链）；中（外交关系工作分类）
- 关联来源：`SRC-SAN9PK-TC-101`
- 关联记录：`RE-SAN9PK-0021`、`RE-SAN9PK-0044`

## 样本与工具

- 样本：繁体中文版 1.0.1.0，32 位 PE，2,636,800 字节，SHA-256 `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`。
- 工具与数据库：IDA 9.3、Hex-Rays、IDA MCP（health `ok`）；项目主数据库 `research/analysis/San9PK-TC-101.i64`。

## 目的与范围

沿 `san9_force_records` 的直接引用收窄势力状态筛选、关系通知和模拟侧势力对处理。记录只建立导航事实，不把 flag 4/5/6 直接定名为盟主、盟友或目标，也不把消息资源当作完整外交规则。

## 已确认事实

### 势力记录结构访问

- `is_primary_force_record(0x43E510)` 当且仅当 `*(this+0xB8) == this` 时返回真；调用者用它筛选自根/主记录。
- `get_effective_force_id(0x43ED00)` 在主记录上读取自身 `+0x10`，否则读取关联记录 `+0xB8` 的 `+0x10`。
- `get_force_flag_bit_4/5/6/12`（`0x43FA70`、`0x43FAA0`、`0x43FAD0`、`0x43FB40`）分别从主记录或关联记录的 flag 字读取对应位。

### 50 项筛选和清理

- `find_primary_force_with_flag_5(0x4550A0)`、`find_primary_force_with_flag_6(0x4550F0)` 均扫描 50 条势力记录，返回第一条有效且为主记录、对应 flag 已置位的记录。
- `count_primary_forces_with_flag_4(0x4551C0)` 统计有效主记录中 flag 4 置位的数量。
- `clear_primary_force_flags_4_5_6(0x455140)` 遍历 50 条有效主记录，通过写入器清除 flag 6、5、4，并重置一处日期/场景侧状态。它是关系状态清理路径，但触发时机仍待验证。

### 关系通知和势力对处理

- `dispatch_force_relation_notice(0x458A60)` 按 flag 5、6、4 选择通知分支；flag 4 分支还要求 `count_primary_forces_with_flag_4() <= 2`。
- `emit_force_relation_notice(0x4587A0)` 根据 0–5 分支生成消息资源 9397–9402，并发出事件代码 `0x35..0x3A`，之后调用关系状态清理。
- `emit_force_pair_relation_notice(0x458B10)` 在全局门控和两个势力对象有效时生成势力对消息，发出事件代码 `0x31`，随后清理关系 flag 并刷新表现。
- `process_force_pair_state_change(0x458D70)` 处理有效势力对，生成关系变化消息，遍历其它主势力进行状态调整，并处理相关人物状态；其最终 flag 分类和消息语义仍待验证。
- `process_force_pair_update(0x50A3A0)` 是模拟侧入口：验证目标对象后处理势力/人物影响，调用 `process_force_pair_state_change`，并在另一关系谓词成立时继续调用后续关系动作。它被 `process_simulation_state_machine(0x50A760)` 的单位阶段调用。

## 推断

- 这组函数形成了一条独立的“势力 flag 筛选 → 关系通知/势力对处理 → 状态清理”的高置信度导航链，并且已有模拟状态机到 `0x50A3A0` 的调用连接。
- `RE-SAN9PK-0021` 已确认 flag 4/5/6 与势力关系/联盟区域存在运行时关联；本记录进一步确认它们参与通知分派和势力对处理。后续 `RE-SAN9PK-0119` 已从形成、期限和清理链闭合 bit 4=成员、bit 5=盟主、bit 6=目标。
- `process_force_pair_update` 更像模拟/运行时状态处理入口，不足以单独作为外交行为规格或旬结算根。

## 待验证

- flag 4/5/6 的逐位领域标签、设置/清除时机及与 50 项外交数组的精确关系。
- `0x4587A0` 的六个消息资源和事件代码的最终文案/行为分类。
- `0x50A3A0` 的触发条件、对象类型 3/4 的数值规则、人物影响和 `0x4464F0` 后续动作。
- 这条链与玩家外交命令、AI 决策、旬推进的汇合点。

## IDA 写入与验证

- 新增 15 个保守函数名，覆盖 4 个 flag 访问器、主记录筛选/计数/清理、关系通知和势力对处理入口；rename dry-run 和写入均成功。
- 15 个函数追加 `[RE-SAN9PK-0045]` 函数注释；保存后 IDA Python 回读名称、函数边界和注释标记，全部与预期一致。
- `idb_save` 成功写回 `research/analysis/San9PK-TC-101.i64`。
- `health_baseline.py` 执行成功：摘要 `818e786c4495f590ae09316f796e6c9922bdf034d26b7ce828e3e5d36753d6b4`；函数总数 10,917、命名函数 784、函数注释 354、反编译失败 7、可执行段未定义字节 0。
- 未修改产品代码、指令字节或数据值。

## 限制

本记录建立的是势力关系处理导航，不是外交系统规格。具体联盟、外交值、AI 或平衡规则必须在 flag 写入、关系数组读写和重复实验闭合后再进入产品规格。
