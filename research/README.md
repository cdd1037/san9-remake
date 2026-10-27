# 研究区

保存来源、可复现实验和逆向证据；产品采用范围由 `specs/` 决定。记录要求见[研究规范](../specs/research-method.md)，主库、标注及回读要求见[逆向规范](../specs/reverse-engineering.md)。

## 主要入口

- [原版 AI 权威研究](ai.md)：集中维护 AI 决策事实、裁决和未决项；旧 RE 保留取证身份及标注账本。
- [逆向结论索引](findings/README.md)：按主题查找 `RE-*`。
- [研究疑点与处理结果](research-open-questions.md)：已纠错结论、证据边界和后置实验；历史“待验证”不直接作为当前待办。
- [来源登记](SOURCES.md)、[S9 文件清单](sources/s9-file-inventory.md)、[EXE 外部 DLL 契约](sources/exe-dll-analysis-inventory.md)、[第三方仓库](sources/upstream-repositories.md)。
- [军事邻城与实际进攻范围实验](experiments/EXP-SAN9PK-0002.md)：统一保存校准、简单通道和动态前线方案，以及当前采用与待优化方向。
- [开局专项](../docs/scenario-startup.md)：跨主题证据、重制接入状态及未采用边界。
- [程序分析归档](archive/program-analysis/program-analysis-roadmap.md)、[来源覆盖账本](archive/program-analysis/program-analysis-coverage.md)。

## 目录与维护

| 目录 | 内容 |
| --- | --- |
| `sources/` | `SRC-*` 来源登记及文件／仓库清单 |
| `experiments/`、`findings/` | 按[模板](../specs/templates/README.md)记录的 `EXP-*` 实验、`RE-*` 结论 |
| `notes/` | 分析、冲突与专项核对；含[战略底图生成研究](notes/2026-10-18-procedural-strategy-terrain.md)，当前游戏暂用原版底图 |
| `guides/`、`patch-points/` | [优化伴侣说明](guides/san9pkhard-changes.md)等阅读资料、第三方补丁候选及验证状态；事实仍引用 RE／来源 |
| `archive/` | 已完成路线及证据；`ida-write-records/` 为写入回读账本，`code-recovery/` 为函数恢复证据 |
| `symbols/`、`scripts/` | 可追溯符号摘要、可复现研究脚本；不作为产品运行依赖 |
| `analysis/`、`../private/` | 忽略的项目 IDA 主库、私有样本、截图及第三方资料；统一用仓库相对路径引用 |

先登记来源，区分事实、推断与待验证；共享身份和证据只维护一处，后续记录补充差异并链接基线。大型样本、差分和生成物放入忽略目录，只提交哈希、方法及自行撰写的结论。研究成熟不自动新增产品规格；采用条件见[规格维护](../specs/README.md#规格维护)。
