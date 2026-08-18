# 改良决策目录

本目录保存 `DEC-*`。核心玩法、平衡、数据语义、存档或玩家预期的重要调整必须先形成决策记录，再更新行为规格。现代代码实现和局部易用性优化不需要仅因不同于原版而创建决策。

新增决策使用[改良决策模板](../templates/improvement-decision.md)。决策被替代时保留原文件并标为“已废弃”，同时链接替代决策。

## 已确认决策

- [DEC-PROJECT-0001：Godot 表现层与纯 C# 模拟核心分离](DEC-PROJECT-0001-layered-architecture.md)
- [DEC-PROJECT-0002：选择性继承核心体验与现代重组](DEC-PROJECT-0002-selective-inheritance.md)
- [DEC-DATA-0001：JSON 内容源包与本地生成缓存](DEC-DATA-0001-content-pipeline.md)
- [DEC-SIM-0001：确定性命令结算与显式存档迁移](DEC-SIM-0001-determinism-and-saves.md)
