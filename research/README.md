# 研究区

`research/` 保存资料来源、实验和逆向过程。这里的内容是证据或线索，不自动成为实现需求。新功能、核心规则与数据语义由产品规格明确采用；已有行为内的修复无需为转述研究结论额外新增规格。

## 当前目录

- [`research-open-questions.md`](research-open-questions.md)：当前疑点复核、已纠错结论、保留边界，以及集中决策和实验清单；历史“待验证”不直接作为当前待办。

- `analysis/`：项目主 IDA 数据库、说明和已回读的 IDA 写入账本。新研究按对应样本版本同步有证据的标注；旧库原件归档到被忽略的 `private/ida/legacy/`，只作历史线索，可再生工作副本放入 `local/generated/research/ida/legacy/`。
- `archive/`：已完成且仅供历史追溯的研究路线、验收与机器状态，不作为当前排期入口。
- `experiments/`：新增黑盒实验记录；每个实验使用 `EXP-*`。
- `findings/`：可追溯的逆向结论；每项结论使用 `RE-*`，地址只保留在这里；按主题浏览见[结论索引](findings/README.md)。
- `guides/`：面向阅读的机制说明和攻略资料；涉及原作事实时仍以对应 `RE-*` 与来源记录为准。
  - [优化伴侣改动汇总](guides/san9pkhard-changes.md)：按隐式改动、可配置规则、操作辅助整理，并区分 2019 年版本差异与证据状态。
- `notes/`：研究笔记、冲突整理和证据链演练。
- `patch-points/`：第三方补丁候选的可提交摘要与验证状态。
- `prototypes/`：不进入产品的可复现研究预览；不得作为行为规格或运行时依赖。
- `scripts/`：未来可复现的研究辅助脚本，不得成为产品运行依赖。
- `sources/`：来源说明以及 `.s9`、外部 DLL 和第三方仓库清单。
- `symbols/`：可追溯的符号摘要，不保存 IDB。
- `../private/`：本地游戏样本、黑盒截图、旧 IDB 原件和许可未确认的第三方研究资料；研究记录统一引用仓库相对路径，不记录机器绝对路径。

## 使用规则

1. 先在 [来源登记表](SOURCES.md) 分配 `SRC-*`。
2. 使用 [研究模板](../specs/templates/README.md)记录实验或逆向结论。
3. 把事实、推断和待验证事项分开。可信资料与目标样本的地址、字节或结构吻合时，可以作为 IDA 工作事实；进入正式行为规格时再执行更严格验证。
4. 大型样本、差分产物和工具输出放在被 Git 忽略的目录，只提交哈希和生成说明。
5. 产品采用研究结论时，按[规格维护规则](../specs/README.md#规格维护)更新相关 BEH；研究成熟本身不要求新增产品规格。
6. 逆向任务产生可靠命名、类型或注释时，按需写入对应版本的项目主 IDA 数据库并回读验证；只读调查无需记录“数据库未修改”。

主要入口为[`.s9` 文件清单](sources/s9-file-inventory.md)、[来源登记表](SOURCES.md)、[逆向结论索引](findings/README.md)、[EXE 外部 DLL 契约清单](sources/exe-dll-analysis-inventory.md)、[程序分析归档](archive/program-analysis/program-analysis-roadmap.md)、[来源覆盖账本](archive/program-analysis/program-analysis-coverage.md)、[第三方仓库简介](sources/upstream-repositories.md)和[证据链纸面演练](notes/0001-source-trace-example.md)。
