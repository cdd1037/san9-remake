# R0–R3 核心程序分析验收

- 验收结论：通过
- 适用样本：`SRC-SAN9PK-TC-101`
- 验收日期：2026-08-24
- 验收范围：[`program-analysis-roadmap.md`](program-analysis-roadmap.md) 的 `COV-01` 至 `DUEL-04`

## 交付核对

- `COV-01..03` 已形成来源覆盖账本、San9PKHard 全量候选目录、当前样本 IDA MCP 审计及可重复队列；队列共 2635 项，期望数与实际数一致，校验无问题项。
- `DOM-01..04` 已由 `RE-SAN9PK-0095..0098` 闭合内政八指令、即时写回、旬级清除、商人/市价刷新和季度人口周期。
- `TAC-01..04` 已由 `RE-SAN9PK-0099..0102` 闭合兵法机会、陆战分配、水战/设施分支、最小间隔和每日状态机。
- `DUEL-01..04` 已由 `RE-SAN9PK-0103..0108` 闭合单挑触发、逐刀结算、战后状态、人物身份域和动态复核，并形成三份已确认 `BEH-DUEL-*`。
- 12 份主批次状态文件均为 `completed`；人物身份专项审计亦为 `completed`。路线图、结论索引、状态文件和提交历史相互对应。

## 样本与 IDA 验证

- IDA MCP 当前打开 `research/analysis/San9PK-TC-101.i64`，模块为 `San9PK.exe`，自动分析与 Hex-Rays 均可用。
- IDA 回读 SHA-256 为 `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`，与来源记录一致。
- 依赖当前样本机器事实的 `RE-SAN9PK-0092..0108` 均留有 IDA MCP 调查或写入/回读记录；纯来源编目的 `RE-SAN9PK-0091` 明确声明未执行程序逆向。
- 本轮提交范围未包含 `.i64/.idb/.exe/.dll/.s9`、`private/`、`artifacts/` 或 `local/generated/` 内容。

## 自动验证

- `python -m unittest discover research/scripts -p "test_*.py"`：60 项通过。
- `git diff --check 1be31e8..137b03e`：通过。
- San9PKHard 队列重建：2635/2635，`validation.valid=true`，无静默遗漏。
- Git 工作树在验收前无已跟踪改动。

## 验收修正与剩余边界

- 验收发现 `research/docs/三国志9电子书.md` 中 6 个 R1 校核链接少写一级父目录；已在本地资料中改为 `../findings/RE-SAN9PK-0095..0098.md` 并回读有效。`research/docs/` 按仓库策略受 Git 忽略，因此该修正不进入提交。
- SQUARE 文档的 `%20` 图片路径经 URL 解码后有效，不是失效链接。
- `inferred`、`conflict` 与 `experiment-required` 项仍只表示已登记的证据边界；本次通过不把它们提升为已确认行为。
- 本次验收只确认 R0–R3 的研究目标完成，不表示人物、计略、外交、军团、事件或结局系统已经闭合。

下一轮从人物与势力治理的实现依赖继续，范围和完成门槛仍以当前 [`program-analysis-roadmap.md`](program-analysis-roadmap.md) 为准。
