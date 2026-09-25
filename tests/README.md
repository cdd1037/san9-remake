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

Godot 历史画面验收脚本也不属于默认测试集，后续开发默认不执行；只有当前任务明确要求复现对应场景时才运行。脚本分类、入口与画面验收分工见[Godot 历史画面验收](../docs/ui/client-verification.md)。

## 运行入口

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

同一版本已通过的验证不因准备提交而重复执行。`G01ScenarioTests` 默认使用公开夹具；设置 `SAN9_G01_SCENARIO` 会切换到指定本地剧本，不能把默认测试通过当作私有内容包已验收。

Headless 默认不运行。修改 CLI 参数解析、退出码、文件产物、存档／命令日志／录制回放协议或其跨进程接线时，单独运行上述 Headless 命令；普通领域规则、界面和导入器改动运行对应测试即可。共享契约和跨层变更仍运行默认解决方案测试，只有涉及这些进程边界时才追加 Headless。修改公共测试配置时，还需单独验证 Headless 项目继承该配置后的构建与测试。

需要验证全部六个项目时，分别执行两个入口。历史全解决方案测试记录反映当时范围，不代表当前默认入口仍包含 Headless。

## 覆盖取舍

删除或合并用例，应明确它防止的错误、替代覆盖所在和是否丢失独有入口或状态；不按数量指标裁剪，不添加只复述实现的断言，也不把不同规则塞进难以定位失败的通用测试框架。同名功能的领域规则测试与会话接线测试承担不同职责。

## 2026-09-25 整理基线

以下数量、耗时和通过情况仅记录当时版本，不作为当前测试结果或持续维护的数量目标。

2026-09-25 整理行为测试前的全量基线如下。用例数包含 Theory 展开；耗时为测试运行器报告的项目执行时间，受并行和机器状态影响，不含完整构建成本，也不能直接相加作为总耗时。

| 项目 | C# 文件数（含辅助文件） | 用例数 | 执行时间（约） |
| --- | ---: | ---: | ---: |
| Core | 165 | 2,385 | 5 秒 |
| Application | 98 | 577 | 4 秒 |
| Importer | 46 | 357 | 1 秒 |
| Client.Godot | 17 | 122 | 0.8 秒 |
| Content | 11 | 75 | 0.8 秒 |
| Headless | 2 | 17 | 27 秒 |
| 合计 | 339 | 3,533 | — |

基线全部通过，无失败或跳过。公共工程配置合并后再次全量通过；行为测试去重和 CLI 输入准备调整后，受影响的 Application 577 个、Headless 17 个用例分别通过，Headless 本轮约 22 秒（单次结果，不作为固定性能保证）。另有 15 个研究 Python 测试文件，本次未运行。C# 测试及辅助文件约 4.2 万行，维护规模值得控制，但目前执行负担主要来自少量真实进程测试，不能用测试数量作为删除标准。

公共协议覆盖按以下位置维护，探索事件测试不重复展开这些失败路径：

| 协议 | 集中覆盖位置 | 事件测试保留的内容 |
| --- | --- | --- |
| 等待响应时禁止保存和结束录制 | `DailyTaskDecisionTests.TwoSameDayDecisionsContinueInMemoryAndReplayWithoutRepeatingDailyWork`；`SaveBoundaryTests` | 进入 `AwaitingDecision`、事件提示与恢复一致 |
| 同一响应重复提交不产生效果 | 上述日任务测试覆盖仍有待办和已恢复稳定两种状态；`PersistenceTests.OperationWatermarkSurvivesSave` 覆盖存档水位 | 首次响应的事件与领域状态变化 |
| 错误任务、错误阶段、公共互斥字段不修改状态 | `DailyTaskDecisionTests.InvalidResponsesAndNormalCommandsCannotMutatePendingDecision`；`StagedDailyTaskTests` 的过期阶段响应 | 投资金额、具体事件选项、事件专有载荷的校验 |

上述去重已应用于 BeastSearch、TigerSearch、MerchantInvestment、Longevity、MysticBook、CommunitySearch、TreasureMonument、SearchConflict、ScholarInsult、ScholarReturn 十组会话测试。各事件的成功/拒绝/失败分支、实际命令接线、提示查询、专有状态、存档往返和回放仍保留；精简的是重复协议断言，用例数不因此减少。

Headless 的损坏日志、未知存档版本、非法命令与路径冲突用例，由 `WriteFailureScenario` 通过应用 API 在各自临时目录准备输入，省去三次仅用于准备数据的 `verify` 进程。实际错误处理仍运行真实 CLI；成功流程中的 `verify`、存档、恢复、录制和回放链路继续保留。

人事命令测试使用独立的小剧本，通过 `GameSession` 和 `CommandLogService` 生成真实奖励、任命命令日志，不再启动 `verify` 提取日志头，也不手写命令 JSON。CLI 执行结果与准备阶段的会话状态比较，再由独立 CLI 进程验证录制回放。Headless 的 17 个用例共启动 33 次 CLI 进程。

其余抽查结论与整理候选：

- `TigerSearchTests` 的资格、阈值、随机消费矩阵，与 `TigerSearchSessionTests` 的提示、命令接线、恢复和回放承担不同职责。同名功能跨层测试不能直接视为重复。
- `TigerSearchSessionTests.ContentValidatesProvinceReferencesAndAllRequiredParameters` 同时校验内容合法性、必需 JSON 字段和会话指纹。后续调整此类测试时，内容契约部分应归 Content，指纹语义留 Application，避免一个测试承担多个层的职责。
- `GameTextStyleTests` 含颜色和样式表的精确值断言。修改表现时应区分有规格依据的映射和单纯复制实现常量的断言；前者仍有回归价值，后者不应为了固定当前实现而不断增加。
- Headless 直接执行构建好的 DLL，没有每个用例重复 `dotnet run` 构建。进一步减少进程前，需要核实是否会丢失进程边界覆盖，不以删除运行很快的 Core 边界用例来优化耗时。
