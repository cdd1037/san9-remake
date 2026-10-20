# 测试组织与运行

测试按依赖边界分成六个项目，公共 SDK、xUnit 版本和测试属性由本目录的 `Directory.Build.props` 管理，并显式继承仓库根目录的构建配置。各项目只声明自己的工程依赖。

默认解决方案包含五个测试项目；`San9.Headless.Tests`（宿主与少量进程测试）独立于解决方案，按需单独运行。Headless 产品工程仍在解决方案中，参与日常构建。

## 职责

| 项目 | 主要负责 | 控制重复的原则 |
| --- | --- | --- |
| San9.Core.Tests | 领域规则、边界条件、随机消费、状态不变量 | 在这里展开规则输入矩阵 |
| San9.Application.Tests | 内容到运行态的接线、命令与查询、会话推进、存档和回放 | 保留各功能接线与特殊状态往返，公共会话协议集中验证 |
| San9.Content.Tests | 内容契约、序列化、引用与资源校验 | 与具体原版格式无关的契约放在这里 |
| San9.Importer.San9PkTc101.Tests | 源格式识别、解析、转换与导出 | 验证源数据到共享语义的映射，不重复领域规则矩阵 |
| San9.Client.Godot.Tests | 客户端选取、坐标、显示状态、设置及 Rendering 逻辑 | 自动化逻辑验证不代表实际画面验收 |
| San9.Headless.Tests | 宿主参数、报告、文件产物、恢复与回放接线；少量真实 CLI 进程覆盖入口与标准流 | 领域场景直接调用宿主；进程用例只验证启动、退出码和跨进程接线，不重复规则矩阵 |

Rendering 的测试目前与客户端测试放在一起。仅为目录对称而新增测试程序集不会增加覆盖；后续若需要独立于 Godot 构建和运行，再按依赖需要拆分。

`research/scripts/test_*.py` 验证研究工具，不属于产品 C# 测试集，修改相关 Python 脚本时单独运行对应测试。

Godot 历史画面验收脚本也不属于默认测试集，后续开发默认不执行；只有当前任务明确要求复现对应场景时才运行。脚本分类与入口见[Godot 历史画面验收](../docs/ui/client-verification.md)。

## 运行入口

按实际受影响行为选入口，不按文件数量选择检查：局部C#逻辑改动验证受影响项目，共享契约或跨层行为变更运行默认解决方案；纯视觉调整按下文取舍，跨目录移动、注释或纯文档变更本身不构成跨层行为变更。Headless的触发条件见下文。

`dotnet test`默认包含构建，不在它之前另跑同范围`dotnet build`；独立于该入口的项目或明确需要的引擎构建除外。定向测试用于开发定位，最终所需范围通过后不再重复跑定向测试。失败后只重跑受修复影响的范围，未变范围复用已通过结果。

纯文档检查改动涉及的引用、章节锚点、命令与差异；文件或标题迁移时搜索入链，不默认扫描全仓文档。

在仓库根目录执行：

```powershell
# 局部改动：运行受影响项目，例如领域核心。
dotnet test tests/San9.Core.Tests/San9.Core.Tests.csproj

# 调试单个功能；不代替共享契约或跨层变更的全量验证。
dotnet test tests/San9.Application.Tests/San9.Application.Tests.csproj --filter FullyQualifiedName~TigerSearchSessionTests

# 默认全量：五个测试项目，不含 Headless 进程测试。
dotnet test San9Remake.slnx

# 按需验证 Headless CLI 和跨进程协议。
dotnet test tests/San9.Headless.Tests/San9.Headless.Tests.csproj

# 需要分析耗时时输出机器可读结果。
dotnet test San9Remake.slnx --logger "trx;LogFilePrefix=tests" --results-directory local/generated/verification/tests
```

同一代码、输入及相关配置已通过的验证不因准备提交而重复执行。后续只改文档不使代码测试失效；仅文件时间戳变化时先核对实际差异，不能据此判定必须重跑。`G01ScenarioTests` 默认使用公开夹具；设置 `SAN9_G01_SCENARIO` 会切换到指定本地剧本，不能把默认测试通过当作私有内容包已验收。

Headless 默认不运行。修改 CLI 参数解析、退出码、文件产物、存档／命令日志／录制回放协议或其跨进程接线时，单独运行上述 Headless 命令；普通领域规则、界面和导入器改动运行对应测试即可。共享契约和跨层变更仍运行默认解决方案测试，只有涉及这些进程边界时才追加 Headless。修改公共测试配置时，还需单独验证 Headless 项目继承该配置后的构建与测试。

需要验证全部六个项目时，分别执行两个入口。历史全解决方案测试记录反映当时范围，不代表当前默认入口仍包含 Headless。

## 覆盖取舍

视觉检查按任务需要和收益安排，AI 可主动查看改动效果并即时调整，无需另行请求用户授权；启动游戏、截图和等待用户验收不是固定交付门槛。用户指定验收分工或要求不启动游戏时遵从。交付如实说明实际检查范围，不把编译或单测通过称为视觉验收通过。

表现层按实际风险选择验证，不按所在目录统一要求单测：

| 变化 | 默认验证 |
| --- | --- |
| 布局、配色、字体、文案、装饰动画及效果参数，且不改变输入、状态或契约 | 可不新增或运行自动测试；编译与测试按实际风险和收益选择，无需为流程凑检查 |
| 输入映射、交互状态、动画连续性与时序、资源生命周期 | 运行受影响逻辑测试；对难以稳定人工复现的边界补充有价值的回归覆盖 |
| 内容契约、命令语义、领域状态或跨层行为 | 按对应项目与跨层验证要求执行，不因最终呈现在界面上而豁免 |

不为测试简单视觉代码额外拆分抽象，不把实现中的尺寸、颜色或补间参数抄成断言。坐标映射、状态切换和时间连续性等可独立判断的行为仍可自动验证；这些测试不能证明最终画面效果。

删除或合并用例，应明确它防止的错误、替代覆盖所在和是否丢失独有入口或状态；不按数量指标裁剪，不添加只复述实现的断言，也不把不同规则塞进难以定位失败的通用测试框架。同名功能的领域规则测试与会话接线测试承担不同职责。

后续开发默认复用现有夹具与覆盖，只为新增规则、独有边界或实际缺陷补最小回归；不逐项把实现细节写成测试，不为每个功能重复保存／回放、确定性或公共失败矩阵。验证按本次风险选择必要入口，通过即交付，不以增加测试数量作为完成标准。

整旬表现采样接口退出后，删除仅约束该接口的`PresentedAdvanceTests`：整旬事务失败回滚保留在`IncrementalAdvanceTests.FailedBatchDiscardsEarlierDaysAndSessionChanges`，跨季／跨年与逐日一致性复用该类已有用例，终局拒绝由`EndingSessionTests`覆盖。待答暂停与禁止继续推进并入`DailyTaskDecisionTests.IncrementalAnswersResumeOnlyCurrentDayAndPreserveReplay`；电脑自动答复后的表现投影改由`CapturedRulerDispositionTests`通过后台逐日入口检查。采样数量和样本合并断言随接口删除，正式演出仍由客户端逐日测试覆盖。

单挑领域测试保留触发、随机短路、逐刀守恒、超出原版名单数量、后果与继任边界；`DuelContestTests`不再重复整场结算或逐刀做JSON往返，真实事件协议与确定性由`DuelSessionTests`的录像回放显式比对事件承担。该会话往返只跑显式继任选择，自动／显式选择矩阵留在`DuelCombatIntegrationTests`，电脑自动响应仍有独立会话入口。选项和自定义单挑特性共用一次保存／命令日志／录像验证。演出保留体力、替补、粒子和对白边界，将体力等待并入逐刀场景、对白局部动画并入暂停／跳过场景，稳定旗帜与粒子复用已有重复计划检查；不追加全套播放排列。

世界事件演出测试聚焦发生时身份、自动／待答输出归属、重复播放抑制及记录正文复用，不逐项固定装饰文案排列或消息总数。地图喊话完成状态并入已有延迟、主动关闭、资源等待和自然结束场景；真实内容包绑定样本独立求值，不把互斥结果拼成领域事件时间线。

## 公共协议与进程覆盖

探索事件共用以下协议测试，各事件保留专有资格、效果、提示、命令接线和状态往返，避免逐个重复公共失败路径。

| 协议 | 集中覆盖位置 | 事件测试保留的内容 |
| --- | --- | --- |
| 等待响应时禁止保存和结束录制 | `DailyTaskDecisionTests.TwoSameDayDecisionsContinueInMemoryAndReplayWithoutRepeatingDailyWork`；`SaveBoundaryTests` | 进入 `AwaitingDecision`、事件提示与恢复一致 |
| 同一响应重复提交不产生效果 | 上述日任务测试覆盖仍有待办和已恢复稳定两种状态；`PersistenceTests.OperationWatermarkSurvivesSave` 覆盖存档水位 | 首次响应的事件与领域状态变化 |
| 错误任务、错误阶段、公共互斥字段不修改状态 | `DailyTaskDecisionTests.InvalidResponsesAndNormalCommandsCannotMutatePendingDecision`；`StagedDailyTaskTests` 的过期阶段响应 | 投资金额、具体事件选项、事件专有载荷的校验 |

Headless 的领域场景由`HeadlessHostTests`直接调用生产宿主，并以独立`TextWriter`捕获输出，不修改全局Console。文件产物、退出码、恢复、重放、警告和失败前缀断言保留。`HeadlessProcessTests`只执行两个入口用例、共四次真实进程启动，覆盖三种命令模式、标准输出／错误、成功／失败退出码及跨进程存档／日志／录像。失败输入由应用API准备，不用另一个CLI进程造夹具。

内容合法性与必需字段归 Content，会话指纹和接线归 Application，规则矩阵归 Core。表格样式中的精确数值须有规格依据；不能仅为固定实现常量增加断言。数量和耗时不是覆盖目标，历史整理基线由 Git 保留。

状态一致性断言使用共享的`StateAssert.Equal`／`NotEqual`，直接传入不可变世界或会话快照。共享状态和相等记录跳过序列化，跨快照按分块使用源生成诊断元数据比较语义内容；`NotEqual`使用`AreEqual`，不定位差异，`Equal`只在失败时定位字段差异，不计算SHA-256、不生成规范化JSON字符串。只读界面验收使用`Revision`。测试中的存档／录像序列化仍覆盖真实产品协议，不另建替代存档实现。

事件一致性使用`EventAssert.Equal`逐项比较，保留类型、顺序、长度和字段语义；相等记录跳过JSON，仅不相等的事件使用源生成元数据比较。玩法测试以快照断言比较恢复／重放状态，实际往返仍使用产品存档协议；字节稳定性、格式和录制隔离集中在`PersistenceTests`验证。失败诊断仅在断言失败时序列化。固定分支夹具使用已知种子或`TestSeeds`随机向量，并核对要求的随机值；随机矩阵继续覆盖原有输入范围，不运行时搜索分支种子。

默认存档不累计操作／事件历史由3旬短流程、文件字段检查和恢复后继续一致性验证；记录队列通过`AutomaticRecordsAreBoundedWhilePlayerNotesArePreserved`越过128条上限并验证精确淘汰顺序。删除40年推进及存档长度差阈值，后者随当前AI目标、随机流和日期位数变化，不能证明历史有界。这些用例不承担长期对局或内存泄漏验收；有具体长期故障时按实际状态／资源增长补回归。

## 粗细两级导航覆盖

地图预览是粗路线意图，不能与逐日细步或攻城最终停点直接比较。`BattlefieldPathSearchTests`、`PlanningRouteCostTests`检查粗格连接、预算与工作区复用；`MovementNavigationTests`、`CombatApproachTests`、`AmphibiousMovementTests`检查实际细步、完整对象碰撞、中心地形扣费及跨岸。旧A*最优路径、整队地形净空和精细预览相等断言已迁移，局部受阻不要求升级为全图细搜索。

`NavigationQueryConsistencyTests`覆盖正式移动日期下的ETA首次到达、等待／混乱、预测状态隔离和占用缓存；`MilitaryMovementTests`检查非法途经点的命令拒绝与恢复，`MilitaryRoutePreviewTests`检查界面粗路线、中继和旧接触不污染改令预览。现有保存／录像及Headless覆盖继续承担协议职责。

## 原型AI退出后的覆盖

2026-10-16删除共同分配、经济目标、战役搜索／配队／选阵、机会冷却、预测缓存与拒绝集合的专属测试，所约束实现已退出，不移为另一套生产回退。公共部署预检、途经点保存及实际行军、显式导航对照保留在`DeploymentPreparationTests`；军事、计略、军团权限及终局测试以显式公共命令继续验证实际效果和回放。经营脚本选人及独立规划随机测试仍保留。OAI-01新增`OriginalDomesticWorkTests`、`OriginalAiCycleTests`覆盖原版经营规则与生产接线，`AiProductionBaselineTests`、`DomesticPlanningBoundaryTests`与Headless同步验证实际自动经营、控制权限、推进、恢复及协议；必要电脑响应沿原有响应专项。后续基线能力按原版AI规格另行验收。
