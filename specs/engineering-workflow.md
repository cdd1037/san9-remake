# 工程与交付流程

状态：已确认

## Git 基线

- 使用 Git 追踪规范、研究记录和产品源代码。
- 提交标题与说明使用英文，标题采用 `type(scope): summary`，summary 使用简短祈使语气且不加句号。
- 常用 type：`feat`、`fix`、`docs`、`refactor`、`test`、`build`、`chore`。
- scope 对应实际变化的模块，如 `content`、`importer`、`core`、`application`、`ui`、`specs`、`research`；不限定枚举。
- 一个提交只包含一个逻辑变化；规格、实现与测试可在同一功能提交中共同演进。

示例：

```text
docs(specs): define evidence workflow
feat(core): implement confirmed search resolution
test(importer): cover officer record boundaries
```

## 变更流程

任务范围、自主推进和验证要求统一见 [AGENTS.md](../AGENTS.md)，规格维护见[规格索引](README.md#规格维护)。针对当前改动定位代码、约定与必要证据，完成实现和相关验证后交付；不把研究、建规格、建决策、全量验收串成每个任务的固定流程。

评审聚焦本次变化：状态所有权和层间依赖是否正确，行为与数据边界是否明确，测试是否证明需求，以及差异中是否混入无关改动或私有产物。涉及已知原版缺陷或版本冲突时，再核对相关证据和修正履历。

IDA 调查、主库标注和输出边界见[逆向工程规范](reverse-engineering.md)，无需在工程流程中重复登记。
