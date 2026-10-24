# RE-SAN9PK-0090：每日撤退/追击调度与运行时命令实验

状态：行为正文已并入[原版AI权威研究](../ai.md#source-0090)，本编号只保留取证身份及历史标注验证，不再维护AI规则或待办。合并日期：2026-10-15。

本次合并不改变原样本或原有IDA证据；当前结论与未决项只以权威研究为准。

<a id="调查结论"></a>
<a id="自动撤退没有概率或冷却"></a>
<a id="返回事件的精确转换"></a>
<a id="运行时命令实验"></a>
<a id="人物字段顺序更正"></a>
<a id="黑盒观察与待验证"></a>
<a id="ida-写入与回读验证"></a>

## 历史取证与标注回读

以下仅证明当时实际操作与回读，不是当前行为定义或新版本通过声明。

## IDA 写入与回读验证

- 将错误数据项 `0x50A168..0x50A23F` 撤销定义，并按真实 `ret` 建立 `0x50A170..0x50A238` 函数；原始程序字节未修改。
- 命名 `process_simulation_stage_task_state(0x50A170)`、`try_set_unit_return_event(0x4642B0)`、`unit_return_event_ctor(0x46BF20)`、`clear_unit_command(0x462670)`、`set_unit_command_event(0x461E40)` 和 `get_unit_command_event_type(0x461C00)`。
- 以临时唯一名过渡后，把三个错误 setter 更正为 `set_person_righteousness(0x46A770)`、`set_person_ambition(0x45E200)`、`set_person_personality(0x45E350)`；`San9PersonRecord +0x70/+0x74/+0x78` 同步改为 `righteousness/ambition/personality`，总大小保持 `0x128`。
- 在调度入口、两条直接调用、两个扫描入口、返回转换、事件构造和人物加载入口写入 `RE-SAN9PK-0090` 注释。保存主 IDB 后回读确认函数边界、名称、结构偏移及反编译调用边均已持久化。
- 回读结果：函数边界 `0x50A170..0x50A238`，两条直接引用分别来自 `0x50A1A6/0x50A1B2`，函数名 `9/9`、注释 `8/8`、结构成员 `6/6` 一致；该函数范围 SHA-256 为 `390bcbead1960b42a6cc2523424089e54ccecbe69be64e286d58ab61b95d8084`，补丁字节计数为 0。
- 导入器把四个字段改为 `Affinity/Righteousness/Ambition/Personality`，相应 134 字节夹具新增逐项断言；`San9.Importer.San9PkTc101.Tests` 共 101 项全部通过，完整解决方案共 112 项测试全部通过。
- 未修改程序字节、原版资源或玩法平衡；IDA 数据库仍位于 Git 忽略的研究分析目录。

## 2026-10-24返回身份续核标注

沿用登记TC101样本与主库，在`4642B0`保留原注释并追加自主返回身份的解释性说明；全文回读、重新反编译去注释函数体比较和字节对盘均通过，原位保存成功。未更名、修改类型／边界或程序字节。当前实现差异与公开诊断见[公共响应续核](../notes/2026-10-24-automatic-work-response-audit.md)，本次实际标注及合并对盘范围见[主库账本](../archive/ida-write-records/automatic-work-remaining-20261024.json)；未重启原作动态实验。
