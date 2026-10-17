# 游戏 AI

本目录保留旧自主AI路线的设计与历史验收记录。原有公平性、信息对称、能力驱动难度和增强规划原则仅供后续重构考虑，不约束当前实现或验收，也不自动成为未来要求。当前生产与交付以[原版AI规格](../original-ai/README.md)为准；旧路线暂停时的状态见[当前进度](current-status.md#当前进度)。

## 阅读入口

| 要回答的问题 | 权威文档 |
| --- | --- |
| 下一步做什么，阶段如何完成 | [实施计划](implementation-plan.md) |
| 已实现什么，还有哪些问题 | [当前实现与问题](current-status.md) |
| 架构、算法和权限遵守什么约束 | [系统设计](system-design.md) |
| 怎样证明行为、性能与强度达标 | [验收标准](acceptance.md) |
| 某次实验测了什么、结果能说明什么 | [性能与验证记录](../../docs/ai-prototype-performance-records.md) |

## 维护边界

- 本目录只维护旧自主AI路线的参考资料，职责按上表划分；不作为当前AI的权威设计与实施计划。
- 当前状态直接更新，不追加逐提交流水；历史测量和阶段证据放在 `docs/ai-prototype-performance-records.md`，其他文档引用同一证据。
- 具体已采用行为见 [BEH-AI](../behavior/BEH-AI-0001-planning-boundary.md)，仍有效的工程边界及供后续重构考虑的旧原则见 [DEC-AI](../decisions/DEC-AI-0001-deterministic-planning-boundary.md)。BEH／DEC 不另建实施路线。
- [整体路线图](../roadmap.md)的 G11／G12 分别负责完整自主运行与长期稳定性；旧AI-07～AI-10的协同、推演、多步对抗和独立强度评测仅在后续重构明确采用时再安排，不是当前原版AI对齐的附加交付要求。
