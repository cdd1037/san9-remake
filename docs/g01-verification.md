# G01 时间、命令与任务内核验收

验收日期：2026-09-12。G01退出条件已满足；下一阶段为G02。实现复用G00确认的世界候选事务、七类领域记录、内政计算和SplitMix64，不新增显示能力，不启动Godot或原作。

## 目标与证据

采用的行为见[BEH-SIMULATION-0001](../specs/behavior/BEH-SIMULATION-0001-time-progression-and-state-visibility.md)及[单人内政范围](../specs/behavior/BEH-DOMESTIC-0001-base-development-effect.md#首批运行时命令范围)。前置证据复核使用[RE-0145](../research/findings/RE-SAN9PK-0145.md)、[RE-0123](../research/findings/RE-SAN9PK-0123.md)、[RE-0098](../research/findings/RE-SAN9PK-0098.md)已有记录：本旬前置更新在下令前，内政即时提交，日命令前更新先于命令效果，旬行动清理不等于经济全部实现。本阶段没有新增原作事实、原作实验或IDA标注。

日历、控制席位、实际命令序号、任务和命名随机均属于Core世界状态。Application把既有玩家选择转为首席玩家控制，其他势力按ID成为自动席位；现有内政入口转为统一类型化命令。占用是任务的派生查询，未新增人物占用副本。新状态进入候选事务、不变量、哈希、只读快照和`SaveEnvelope.World`，稳定快照恢复跳过新局更新；封套不再另存初始种子，避免与世界随机初态分叉。

## 退出条件与实际验证

| 退出条件 | 运行证据 |
| --- | --- |
| 新局更新一次、恢复不重复 | 首轮前置夹具实际扣7，再恢复和重复建立引擎，扣费计数仍为1；日期不变；恢复保留版本和哈希 |
| 日/旬/月/季/年顺序 | 1→11、11→21、2月21→3月1、3月21→4月1、12月21→次年1月1；每旬10次逐日处理，逐阶段核对日期与次序 |
| 即时内政、共享终验 | 单人商业立即300→307、资金1000→950、人物行动；日期与随机不变；玩家和自动席位执行相同处理器 |
| 拒绝零副作用 | 错误控制者、伪造势力、未知对象/任务、错误序号、重复ID、已行动和重复占用被拒绝；世界快照、版本、事件与随机不变；执行前换属重验 |
| 任务不提前兑现 | 15日夹具任务跨旬后仍占用，16日命令阶段完成一次；10日任务到达11日稳定边界仍未兑现，11日命令阶段才完成 |
| 任务失效与资源变化 | 目标和人物改属后取消一次，释放占用且不产生成功效果；日命令前更新令资金不足时，到期取消不扣费、不消费任务随机 |
| 稳定恢复 | 持续任务、行动占用、实际序号、控制席位和随机经JSON往返恢复后继续，状态与事件一致；未知规则、错误摘要和无效调度状态拒绝 |
| 原子失败与重入 | 任务候选先扣费、消费随机，再因无效开发度提交失败；世界与事件未发布，修正规则后重试等同从原稳定态运行；同世界不同引擎也不能重入或捕获执行中存档 |
| 终局短路 | 首轮终局检查、季度阶段及日地图阶段分别触发终止；不执行后续阶段，取消任务；终局可恢复，后续命令与推进零世界副作用 |

核心场景见[SimulationEngineTests](../tests/San9.Core.Tests/SimulationEngineTests.cs)，已有内政与不变量回归继续保留。夹具任务`fixture-investment`在真实候选事务中扣费、消费命名随机并增加开发值，只存在于测试规则中；正式任务注册表为空，未知任务明确拒绝。没有把该夹具作为正式人事、战争或经济玩法发布。

## 无界面36旬场景

[G01ScenarioTests](../tests/San9.Application.Tests/G01ScenarioTests.cs)从Application新局入口建立世界，以明确脚本轮流提交商业/开垦命令并结束控制席位。它同时推进连续世界和恢复世界，在首条命令之后、控制轮次中途及周期边界共恢复10次，每次比较权威哈希、世界版本与完整结构化事件。每旬从恢复世界重建Application查询，核对日历和资金。不是自主AI对局，也不是G03经济循环验收。

| 内容 | 旬数 | 接受/拒绝命令 | 恢复次数 | 结果 |
| --- | --- | --- | --- | --- |
| 公开两势力夹具 | 36 | 40 / 32 | 10 | 进入191年1月1日；资金不足拒绝仍保持一致 |
| 本地190年转换包 | 36 | 900 / 0 | 10 | 进入191年1月1日；连续与恢复全部一致 |

本地包的转换初始态→会话复制→当前世界核对身份集合：25势力、94据点、700人物、0初始部队、25军团、120宝物、300关系。人物可用性继续从当前状态筛选，未删除未登场或非在职人物。原版日期字段已完整提供年月日，控制配置与任务是重制运行态，不需要新增源字段或修改Content/Importer格式；本次未重做原版源记录全量解码审计，也未声称700人物均可下令。

报告位于忽略目录`local/generated/verification/roadmap-g01/`：`public-report.json`、`private-190-report.json`及对应`*-commands.jsonl`。机器报告含内容指纹、种子190、命令日志路径、旬数/结束原因、最终哈希、首个失败、耗时和覆盖数量。这里的内容指纹覆盖序列化的规范化剧本初始态；生产用规则内容指纹与磁盘存档协议由G02定义。

公开夹具最终哈希：`584295660833F4080CFCBC8E36AFB97E34E8317CEE32693228267ADA4D442181`。

190年包最终哈希：`CE2D4A6F7EA5200188158D3987B1623D8685A02DC718946FE1DBE6E25CD00988`。

复现（仓库根目录，PowerShell；测试失败返回非零退出码）：

```powershell
$env:SAN9_G01_REPORT_DIR = Join-Path (Get-Location) 'local/generated/verification/roadmap-g01'
Remove-Item Env:SAN9_G01_SCENARIO -ErrorAction SilentlyContinue
dotnet test San9Remake.slnx --nologo

$env:SAN9_G01_SCENARIO = Join-Path (Get-Location) 'local/generated/content/scenario-190-live/scenario.json'
dotnet test tests/San9.Application.Tests --nologo --no-build --filter FullyQualifiedName~Headless36XunScenario
Remove-Item Env:SAN9_G01_SCENARIO
Remove-Item Env:SAN9_G01_REPORT_DIR
```

## 检查与剩余边界

全解决方案测试433项通过、0失败、0跳过：Core86、Content29、Application42、Importer183、Client93；本地190年集成场景另执行1项通过。客户端仅编译和既有自动测试，没有启动游戏，也没有视觉验收。

领域机器契约与依赖图已更新模拟状态和验收门。契约验证器仍只报告G00已登记的`BEH-UI-0002..0006`未分类问题，无新增报告项；按路线图留给G13，不能宣称完整契约验证通过。报告为`core-domain-contracts.json`。相关文档本地链接和`git diff --check`通过。

磁盘存档服务、格式/内容版本拒绝、命令日志重放、会话年表恢复和独立headless CLI属于G02；多人经营、俸禄、粮耗与周期收入属于G03；人物/军团治理、真实行程任务、战争、实际胜负规则和自主AI由后续阶段接入。G01已提供可执行且可恢复的调度与任务内核，未用空实现声称这些后续玩法完成。
