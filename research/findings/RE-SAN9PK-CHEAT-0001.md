# RE-SAN9PK-CHEAT-0001：新武将编辑器妖术 20 次秘籍

- 状态：已确认
- 样本：`SRC-SAN9PK-TC-101`（`San9PK.exe`）
- 项目主数据库：`research/analysis/San9PK-TC-101.i64`
- 证据类型：IDA 静态反编译与指令回读

## 结论

新武将编辑器对妖术兵法事件累计计数；妖术兵法索引为 `30`，对应事件号 `0x41F`。事件处理路径在 `0x554C30..0x554C5D` 对编辑器字段 `this+0x39AC` 执行递增，并以 `0x14`（20）作阈值比较。达到阈值后调用 `activate_new_person_tactic_cheat(0x554910)`，写入全局 `new_person_tactic_cheat_flag(0x1A5E4B0)=1`。

秘籍标志的效果由以下闭合数据流确认：

- `edit_person_ability_with_dynamic_max(0x552680)` 将四维编辑上限设为 `(flag != 0) + 99`，即从 99 变为 100。
- `calculate_new_person_tactic_points(0x552440)` 将兵法点预算基数从正常的 35 改为 10000。
- `render_new_person_tactic_points(0x553810)` 对显示值执行 `min(value, 99)` 并格式化为 `%2dP`，因此秘籍开启后界面保持显示 `99P`；内部预算即使被兵法选择消耗，也不会低于显示上限。

“选中 10 次、取消 10 次”是用户操作层面的配方；当前静态代码直接闭合的是妖术事件累计达到 20 次，未拆分出两个独立计数器。

## IDA 同步

已在项目主数据库中写入以下名称与 `RE-SAN9PK-CHEAT-0001` 证据注释：

`new_person_tactic_cheat_flag`、`edit_person_ability_with_dynamic_max`、`calculate_new_person_tactic_points`、`render_new_person_tactic_points`、`activate_new_person_tactic_cheat`、`finalize_new_person_record`。

仅修改 IDA 数据库元数据，程序字节未修改。
