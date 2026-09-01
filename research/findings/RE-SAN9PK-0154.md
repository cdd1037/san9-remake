# RE-SAN9PK-0154：误标代码恢复方法与初始基线

- 状态：方法记录已压缩；恢复结果以 [RE-SAN9PK-0155](RE-SAN9PK-0155.md) 和规范区间表为准
- 日期：2026-09-01；样本：`SRC-SAN9PK-TC-101`
- 主数据库：`research/analysis/San9PK-TC-101.i64`

## 初始调查

只读扫描 `.text` 段的 IDA 定义、段权限、数据引用、代码 xref 与首条指令解码，得到 701 个非执行数据指针候选、76 个已定义代码到非代码目标，合并 776 个恢复种子。它们只是证据上界，不等于函数入口：跳转表、嵌入数据、对齐填充和指令内部地址都可能命中。

准入条件是来源类型（虚表/MFC 表/真实回调表）、入口指令、完整控制流闭合、返回与相邻边界、反编译质量及写后回读；不凭序言、对齐或“位于 `.text`”批量建函数。间接跳转按可枚举表、固定虚槽/导入尾调用、动态不可枚举数据流和伪引用分别留痕。

## 恢复范围与结果入口

三处既有研究所需的定义回退（`0x4C9220`、`0x4C9740`、`0x5236A0`）已补回。随后完成 UI/命令、`.rdata`/`.data` 表入口、直接调用、尾跳、虚分派和 switch 闭包恢复。当前规范集合为 835 个函数，合计 113,870 字节；每个函数均可反编译。逐函数区间见 [`San9PK-TC-101-recovered-code-intervals.csv`](../analysis/San9PK-TC-101-recovered-code-intervals.csv)，最终写入回读见 [`San9PK-TC-101-final-code-recovery-readback.json`](../analysis/San9PK-TC-101-final-code-recovery-readback.json)。

最终 `.text` 基线为代码 1,889,244、数据 156,564、未定义 43,152、非代码 199,716 字节。原始启发式剩 22 项，均已裁决为指令中部或嵌入数据；未裁决恢复种子为 0。程序字节未改。

## 影响审计入口

恢复后对既有研究的重新核对集中记录于 [`San9PK-TC-101-misclassified-text-research-impact.csv`](../analysis/San9PK-TC-101-misclassified-text-research-impact.csv)。其中 RE-SAN9PK-0148 的第二调用者已归属 `sub_51E7C0`；RE-SAN9PK-0071/0069 的电脑强度 UI 读写链已确认；其余列出的负面结论均在 835 个函数上重跑后保持，残余风险仅是未类型化的算术式字段访问。

本记录不再保存候选快照、分批计划或逐批叙述；这些中间产物已由规范区间表、最终回读和影响审计替代。
