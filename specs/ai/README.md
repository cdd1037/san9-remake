# 游戏 AI

电脑势力与既有委任军团共用自主规划能力，目标是在相同规则和信息权限下完成经营、战争与持续对抗。完整 AI 尚未交付；2026-10-15 暂停自主设计路线的继续开发，保留 AI-07 未验收原型，后续优先转向原版 AI 决策结构复刻与参考基线，具体状态见[当前进度](current-status.md#当前进度)。

## 阅读入口

| 要回答的问题 | 权威文档 |
| --- | --- |
| 下一步做什么，阶段如何完成 | [实施计划](implementation-plan.md) |
| 已实现什么，还有哪些问题 | [当前实现与问题](current-status.md) |
| 架构、算法和权限遵守什么约束 | [系统设计](system-design.md) |
| 怎样证明行为、性能与强度达标 | [验收标准](acceptance.md) |
| 某次实验测了什么、结果能说明什么 | [性能与验证记录](../../docs/ai-performance-records.md) |

## 维护边界

- 本目录共同组成唯一 AI 设计与实施计划，职责按上表划分，不再在单个文件混写全部内容。
- 当前状态直接更新，不追加逐提交流水；历史测量和阶段证据放在 `docs/ai-performance-records.md`，其他文档引用同一证据。
- 具体已采用行为见 [BEH-AI](../behavior/BEH-AI-0001-planning-boundary.md)，长期确定性与规则平等决策见 [DEC-AI](../decisions/DEC-AI-0001-deterministic-planning-boundary.md)。BEH／DEC 不另建实施路线。
- [整体路线图](../roadmap.md)的 G11／G12 分别负责完整自主运行与长期稳定性；AI-07～AI-10 的协同、推演、多步对抗和独立强度评测仍须单独完成。
