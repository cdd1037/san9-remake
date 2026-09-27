# 测试组织与运行

测试按依赖边界分成六个项目，公共 SDK、xUnit 版本和测试属性由本目录的 `Directory.Build.props` 管理，并显式继承仓库根目录的构建配置。各项目只声明自己的工程依赖。

默认解决方案包含五个测试项目；`San9.Headless.Tests` 独立于解决方案，按需单独运行。Headless 产品工程仍在解决方案中，参与日常构建。

## 职责

| 项目 | 主要负责 | 控制重复的原则 |
| --- | --- | --- |
| San9.Core.Tests | 领域规则、边界条件、随机消费、状态不变量 | 在这里展开规则输入矩阵 |
| San9.Application.Tests | 内容到运行态的接线、命令与查询、会话推进、存档和回放 | 保留各功能接线与特殊状态往返，公共会话协议集中验证 |
| San9.Content.Tests | 内容契约、序列化、引用与资源校验 | 与具体原版格式无关的契约放在这里 |
| San9.Importer.San9PkTc101.Tests | 源格式识别、解析、转换与导出 | 验证源数据到共享语义的映射，不重复领域规则矩阵 |
| San9.Client.Godot.Tests | 客户端选取、坐标、显示状态、设置及 Rendering 逻辑 | 自动化逻辑验证不代表实际画面验收 |
| San9.Headless.Tests | 真实 CLI 进程、退出码、文件产物、恢复与回放链路 | 保留进程边界覆盖，不在 CLI 再展开所有规则分支 |

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

## 公共协议与进程覆盖

探索事件共用以下协议测试，各事件保留专有资格、效果、提示、命令接线和状态往返，避免逐个重复公共失败路径。

| 协议 | 集中覆盖位置 | 事件测试保留的内容 |
| --- | --- | --- |
| 等待响应时禁止保存和结束录制 | `DailyTaskDecisionTests.TwoSameDayDecisionsContinueInMemoryAndReplayWithoutRepeatingDailyWork`；`SaveBoundaryTests` | 进入 `AwaitingDecision`、事件提示与恢复一致 |
| 同一响应重复提交不产生效果 | 上述日任务测试覆盖仍有待办和已恢复稳定两种状态；`PersistenceTests.OperationWatermarkSurvivesSave` 覆盖存档水位 | 首次响应的事件与领域状态变化 |
| 错误任务、错误阶段、公共互斥字段不修改状态 | `DailyTaskDecisionTests.InvalidResponsesAndNormalCommandsCannotMutatePendingDecision`；`StagedDailyTaskTests` 的过期阶段响应 | 投资金额、具体事件选项、事件专有载荷的校验 |

Headless 直接执行已构建的 DLL。失败输入由 `WriteFailureScenario` 通过应用 API 准备；人事日志通过 `GameSession`／`CommandLogService` 生成，实际错误处理、保存恢复和录像重放仍交给独立 CLI 进程。精简准备进程时不得丢失被验证的进程边界。

内容合法性与必需字段归 Content，会话指纹和接线归 Application，规则矩阵归 Core。表格样式中的精确数值须有规格依据；不能仅为固定实现常量增加断言。数量和耗时不是覆盖目标，历史整理基线由 Git 保留。
