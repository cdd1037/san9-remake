# C# 复核审计：2026-09-23 阶段报告

审计基线：`27eb8902114c9f45d9ff70697efde293a48eb733`。审计开始时工作区干净；下列原始复现描述针对该基线。用户暂停审计后另行授权修复 P1/P2，当前修复状态见下一节。审计文档先于修复代码单独提交。

**状态：全量审计尚未完成。** 以下是已经完成代码追踪和运行复现的发现，不是对剩余代码无缺陷的保证。全套测试通过不代表全量人工复核完成。

## P1/P2 修复状态（审计暂停后的独立修复）

用户要求优先处理 P1/P2，已修复 A01–A05，P3（A06–A11）未改。全量审计仍暂停。

| 问题 | 修复与验证 |
| --- | --- |
| A01 | 登用带城纳入既有势力存续清理、友军命令清理及统一检查；回归覆盖最后城市与仍有另一城，断言旧控制席、军团、任务和一次灭亡事件，并比对恢复后相同推进事件/状态。 |
| A02 | 提取据点自发转属原有的俘虏离开处理供登用带城共用，保留无目的地的既有处理规则；回归检查本方俘虏恢复、原属旧势力俘虏离开。 |
| A03 | 命令日志头新增必填 `ComputerDamageBonusPercent`，写入录制起点设置，回放先比较设置，Headless 新建会话使用日志值；真实设施战斗日志回放状态一致，错配在第 1 行拒绝且不改状态，缺失字段拒绝，新增 CLI 进程回归。 |
| A04 | 活跃人物缺失不再绕过已开始事件的完成判断；未开始的事件仍受资格限制。公开搜索/处置命令分别令继任者、发起者退隐，断言失败收尾只发生一次，保存恢复及完整录制回放一致。 |
| A05 | 基准工具移除过时 `MemberOrder`，继续由人物 `UnitId` 与现有队列/排数契约表达成员。独立项目构建成功，未运行私有剧本性能基准。 |

实际验证：

- 定向 Core 测试 32 项通过；定向 Application 测试 43 项通过。
- `dotnet test San9Remake.slnx --nologo --verbosity minimal`：退出 0，**3381 项通过**（Core 2383、Application 466、Content 74、Importer 343、Client 98、Headless 17），无失败或跳过。
- `dotnet build tools/scenario-benchmark/ScenarioBenchmark.csproj --nologo --verbosity minimal`：退出 0，0 警告、0 错误。
- `git diff --check` 通过；未启动游戏或进行视觉验收。

兼容性：规则版本更新为 `san9-core-g10-v60`、日志格式为 85；保存结构仍为 82。旧规则保存／录像、旧命令日志明确拒绝，内容 schema 未变，现有内容包无需重转。审计覆盖台账仍保留原始基线哈希，不把修复后的文件伪装成已完成新一轮全量审计。

## 范围与方法

- 纳入 Core 共享领域规则、Application 非表现用例与持久化、Content 语义契约、版本 Importer 数据转换，以及 Headless/Tools 的相关入口。
- 排除表现行为与专用于非玩家势力的决策规划，不按 Godot 或 Planner 文件名整文件排除。客户端非视觉启动配置、玩家委任和脚本验证使用的共享路径纳入；共享战斗、外交、治理规则不因电脑势力也调用而排除。
- 已重点追踪命令事务、状态不变量、存档与录制恢复、人事转属/返程、军团和势力存续、单位终止、经济资源、移动、建设与主要战斗路径；内容初始状态、场景编译与包读写也已检查关键链路。
- 搜索、历史及政治事件执行链和对应定义已在续审中检查并补入台账（见后文）；部分外交和战斗兵法、剩余领域定义、源档案解码及工具分支尚未全部逐项复核。相关文件的扫描或测试通过不能替代人工复核，故本报告不能作为用户要求的全量审计收尾。
- 只列可复现的行为缺陷；未把包多文件替换的异常原子性、输入防御性等尚未完成验证的疑点列为已确认问题。

## 已确认发现

### A10 · P3：极端合法连携倍率溢出，实际战斗由必定连携变为不连携

位置：[TacticLinkRules.cs](../src/San9.Core/Domain/Military/TacticLinkRules.cs) 的 `Chance`；`MilitaryRulebook` 对 `LinkMultiplier` 仅要求正值；`TacticCombat.ActivateMartial` 将结果直接交给 `Roll`。

正式规则构造接受 `LinkMultiplier=int.MaxValue`，同相性连携算式的 int 乘法溢出，机会返回 -25（宽位结果 53,687,091,175）。完整战斗探针以两个满足连携条件的成员、相同种子 22 执行公开 `OrderUnitCommand` 和 `AdvanceXun`：倍率 4 的首个兵法事件参与人数为 2；极端倍率的参与人数反降为 1，两者均推进到 190-01-11。`Roll` 对至少 100 的机会必定接受、对负数拒绝，因此不是显示误差，而是实际连携结果改变。

影响仅证明极端独立创作参数，未证明原版倍率受影响；没有崩溃或部分提交。修复应在概率计算中使用足够宽的表示并依概率语义处理超过 100 的值，不应以原版倍率范围限制共享定义。

### A11 · P3：极端合法设施射程加成使正常旬推进抛出溢出异常

位置：[MilitaryRulebook.cs](../src/San9.Core/Domain/Military/MilitaryRulebook.cs)，327 行 `FacilityAttack` 的基本射程与加成 checked 相加。

设施基本射程 1、先天加成 `int.MaxValue` 均通过规则构造，世界创建和模拟引擎初始化成功。双方正常结束控制后调用 `AdvanceXun`，在 `MilitaryEngagements.FindFacilityTarget` → `FacilityAttack` 抛出 `OverflowException`；加成 2 的对照则顺利推进到 190-01-11，产生 10 次设施攻击。异常后稳定状态哈希与推进前相同（`unchanged=True`），**不是存档损坏或部分提交**，但该合法配置不能继续推进。

仅证明极端自定义参数，不声称普通射程受影响。需统一射程组合的可表示范围、规则校验与运行时算术；不能简单 unchecked 回绕，也不能以原作射程范围代替通用内容约束。

两项完整执行复现均在忽略目录 `CombatBoundaryProbe.cs`，夹具根据现有 `TacticCombatTests` / `FacilityAttackTests` 的世界和规则构造提取；命令 `dotnet run --project local/generated/verification/csharp-audit/Audit.csproj -- combat-boundary` 退出 0，表示探针捕获并报告异常，不是修复后的测试通过。

### A09 · P3：长消息表达式被确认可执行，却使归档诊断导出整体中断

位置：[OriginalMessageParser.cs](../src/San9.Importer.San9PkTc101/Text/OriginalMessageParser.cs) 的 `Arithmetic`/`Boolean` 左结合建树与 `Atom` 深度计数；[MessageArchiveCompiler.cs](../src/San9.Importer.San9PkTc101/Text/MessageArchiveCompiler.cs) 的 `Compile`/`Export`；[MessageGraph.cs](../src/San9.Content/Text/MessageGraph.cs) 的表达式深度检查。

完整合成 LS11 消息归档复现：一个条件包含 64 次连续加法，语法、偏移和终止符合法。`Compile` 返回 FullyParsed、SemanticsConfirmed、DependenciesConfirmed 全为 true，Issues 为空，但生成的左结合树超过共享内容深度限制；`Export` 在序列化第一个消息目录时抛出 `JsonException`，连对应 `messages-audit.json` 都没有生成。2 次及 20 次加法的对照归档均导出 8 个文件成功。此前独立调用 `MessageGraph.Validate` 同样已确认 64 次加法的树被拒绝。

根因是解析器限制递归调用深度，却未限制循环构造出的表达式树深度；确认依赖图不等于验证生成的内容树，序列化又先于诊断写出。影响限异常复杂消息的导入和审计诊断：单条记录可中断全部导出，而非返回逐条未就绪诊断。**没有已登记原版样本触发证据，不声称正常游戏消息已受影响；没有绕过最终内容校验或执行恶意文本。** 格式宽度溢出的诊断不一致仍作为相邻鲁棒性边界，不与此混算为第二项已确认发现。

忽略目录复现：`MessageExportProbe.cs`，`dotnet run --project local/generated/verification/csharp-audit/Audit.csproj -- message-export`，退出 0（捕获异常的探针正常结束）。修复方向是以共享契约及实际可序列化表示约束生成树，无法支持的记录应保留原始数据与明确诊断，不应只提升序列化深度或把确认标志直接当作可装载保证。

### A08 · P3：合法极端基础能力在授予能力宝物时抛出未转换的溢出异常

位置：[OfficerAbilityRules.cs](../src/San9.Core/Domain/Governance/OfficerAbilityRules.cs)，26–30 行；公开入口为 `GameSession.Execute(GrantTreasureCommand)`。

基础能力及宝物加成各自只要求非负，未限制二者有效合计；`Current` 使用 checked int 相加。独立创作初始内容中，收宝武将武力为 `int.MaxValue`，武力 +10 的宝物初始在普通能力君主处：内容 Validate、会话创建和初始查询均成功。正常授宝命令在提交前维护领导派生状态时抛出 `OverflowException`（`OfficerAbilityRules` → `LeadershipMaintenance` → `SimulationEngine.Evaluate` → `GameSession.Apply`），不是结构化命令拒绝。对照基础武力 100 时授宝接受、查询为 110、存档恢复正常。

本次极端输入复现确认 `unchanged=True`，异常后原会话查询及保存恢复仍成功；**不是部分提交或存档损坏，也没有证据表明原版普通数值触发**。忽略目录探针：`AbilityProbe.cs`；命令 `dotnet run --project local/generated/verification/csharp-audit/Audit.csproj -- ability`，进程退出 0 表示探针捕获并输出预期异常，不表示缺陷消失。修复需统一有效能力的可表示范围及运行时加成策略，不应简单恢复原作能力上限或 unchecked 回绕。

### A01 · P1：登用带走最后一城没有执行势力灭亡清理

位置：[PersonnelDefection.cs](../src/San9.Core/Simulation/Personnel/PersonnelDefection.cs)，73–78 行；据点转属发生于 52–69 行。

`Recruit` 仅在被登用者原先具有 `UnitId` 时执行 `BattleAftermath.Reconcile`、清友军目标、维护领导和检查统一；据点大将带城分支仅修复军团。旧君主位于别处时，登用可以正常成功，却绕过无城市灭亡结算。

复现：势力 2 仅有城市 2 和设施 3，君主驻设施 3；势力 1 登用城市 2 的大将。命令接受且登用成功，推进整旬后：

```text
formerCities=0; eliminated=False; controlRetained=True
```

影响：无城市的势力、君主和控制席位继续存在，灭亡及统一判定没有在该次转属中发生。不符合 [BEH-WAR-0003](../specs/behavior/BEH-WAR-0003-unit-return-defeat-and-force-cleanup.md) 第 28、32、111 行明确要求的“人员登用带城同次清理”。

修复方向：把据点所有权转移后的存续清理纳入共享事务，而非依赖下一旬补扫或仅扩大某个条件。回归应覆盖最后一城、仍有另一城、君主在设施/野外、子军团，以及存档和重放。

### A02 · P2：登用带城遗漏原有俘虏，本方武将仍被本方城池拘押

位置：[PersonnelDefection.cs](../src/San9.Core/Simulation/Personnel/PersonnelDefection.cs)，57–69 行。

据点转属只处理原所有者的在职人物，既不枚举原有俘虏，也不对收归本方的俘虏执行释放/恢复。该据点成为势力 1 所有后，原属于势力 1 的俘虏仍保留 `Captive` 身份。

复现：在 A01 场景中放入势力 1 的俘虏 30。登用成功并推进整旬后：

```text
captiveStatus=Captive; captiveOwner=1; prisonOwner=1
```

额外对照：让原势力保留另一座城市，结果仍相同。因此不是 A01 未灭亡的附带结果。错误状态通过当前世界校验；该人物不能正常作为在职武将使用，后续俘虏周期仍按俘虏处理。

依据：[BEH-WAR-0003](../specs/behavior/BEH-WAR-0003-unit-return-defeat-and-force-cleanup.md) 第 46–48 行要求敌方拘押位置；其他转属入口已有原有俘虏的处理，例如 [SettlementAffiliation.cs](../src/San9.Core/Simulation/Governance/SettlementAffiliation.cs) 和 [SurrenderMerge.cs](../src/San9.Core/Simulation/Diplomacy/SurrenderMerge.cs)。

修复方向：据点交接须明确处理原有俘虏，至少本方俘虏必须恢复/返程；第三方俘虏按既定政策处置。同时补足据点拘押关系的不变量校验。仅修复 A01 的势力清理不足以修复本问题。

### A03 · P2：命令日志遗漏电脑伤害加成，回放静默产生不同战斗结果

位置：[CommandLogService.cs](../src/San9.Application/Persistence/CommandLogService.cs)，8–10、26–29、49–52 行；[HeadlessHost.cs](../src/San9.Headless/HeadlessHost.cs)，232–237 行。

会话可通过 `GameSession.Start(definition, seed, computerDamageBonusPercent)` 设置电脑伤害加成。日志头只保存初始种子及 `BattleDeathsEnabled`，未保存或校验 `ComputerDamageBonusPercent`。Headless 从日志启动也使用默认加成。

复现：使用现有 `FacilityCombatSessionTests` 的公开合成场景，以种子 42、100% 加成开始录制，出征并推进一旬，导出命令日志；以相同内容、种子和默认 0% 加成回放：

```text
firstFailure=null; accepted=2
originalBonus=100; replayBonus=0; hashEqual=False
originalSoldiers=9130; replaySoldiers=9565
```

影响：日志报告所有操作成功，却无法重现原战斗；不能作为可靠的复现证据。普通存档和完整 `RecordingService` 路径保留该参数，本发现不泛指所有持久化路径。虽然参数涉及电脑伤害，它是共享战斗配置及回放协议，不属于本次排除的 AI 决策。

修复方向：日志协议应包含并校验影响规则执行的全部开局配置，Headless 按日志重建相同配置；协议升级遵循项目约定，不静默兼容缺失字段。回归要验证相同配置的事件和最终状态一致，以及不同配置在执行首条操作之前被拒绝。

### A04 · P2：已启动连环计遇到参与者退隐后，不进入失效结算

位置：[ChainedPlot.cs](../src/San9.Core/Simulation/Events/Historical/ChainedPlot.cs)，14–16 行。

入口在读取 `StartedKey` 之前，要求君主、继任者和谋划者都能从活动人物集合 `world.Officers` 查到。名士处罚通过 `PersonnelWithdrawal.Resolve` 使人物退隐并标为 `IsDormant`；身份仍保留在完整世界中，但从活动集合移除。于是已启动事件也直接返回 false，永远到不了后面的 `ConspiratorsUnavailable`/`RulerUnavailable` 和完成键提交。

复现使用可通过 `ScenarioInitialState.Validate` 的独立内容：沿现有连环计会话测试的合成场景，将谋划者 13 同时配置为一个名士处罚搜索事件的人物。正常推进到连环计前段，再由武将 14 搜索，通过公开 `RespondDailyTaskCommand` 选择处斩（名士处罚的实际效果为退隐）。响应接受，人物 13 退隐；该旬结算及再下一旬都没有连环计结果，也没有消费完成键：

```text
prelude=Prelude; searchAccepted=True; responseAccepted=True
withdrawn=True; completed=False; subsequentEvents=0
restoredSame=True; replaySame=True
```

影响边界：已证实有效 Mod/独立内容中的事件组合异常；没有证据表明当前原版导入的固定角色配置会触发同样组合，不扩大为原版场景必现问题。异常不阻塞整个游戏推进，但使已开始的剧情链无法按规定完成失效结算，后续仍重复检查，并被稳定保存和回放保留。

依据：[BEH-EVENT-0002 连环计](../specs/behavior/BEH-EVENT-0002-event-selection-and-commit.md#连环计) 明确要求参与者失效时消费完成键、报告原因，不无限重试；工程边界要求支持独立内容组合。

修复方向：区分“前段资格不足”与“已启动后参与者不再活动”；以保留的内容身份报告失效，并保持后段规定的分支优先级。不能用禁止事件角色重叠或删除内容身份规避生命周期问题。回归需覆盖君主、继任者和谋划者退隐、失效只提交一次、稳定保存及事件回放。

### A05 · P2：已记录的场景基准工具无法构建

位置：[tools/scenario-benchmark/Program.cs](../tools/scenario-benchmark/Program.cs)，109 行。

构造压力场景部队时仍初始化 `MemberOrder`，而 `UnitState` 已删除此成员，人物的 `UnitId` 才是成员归属权威。当前基线执行：

```powershell
dotnet build tools/scenario-benchmark/ScenarioBenchmark.csproj --nologo --verbosity minimal
```

退出码 1，编译器报告：

```text
Program.cs(109,79): error CS0117: “UnitState”未包含“MemberOrder”的定义
0 个警告，1 个错误
```

影响：[寻路验证](pathfinding-verification.md)和[场景性能验证](scenario-performance-verification.md)记录的 `dotnet run --project ...` 入口均无法重新执行，不限于某个运行模式或私有样本。失败发生在编译阶段，复现无需原版资源。

根因与验证缺口：单位成员契约迁移未同步这个消费者；`ScenarioBenchmark.csproj` 不在 `San9Remake.slnx`，因此前述解决方案测试全部通过并不能证明基准工具可构建。修复应将压力场景构造同步至当前成员/主将/阵位契约，并把文档仍推荐的独立工具构建纳入相关契约变更验证；不应为了旧工具重新引入 `MemberOrder` 兼容字段。

### A06 · P3：攻心副效果在合法极大熟练度下溢出为负持续天数

位置：[IntelligenceTactics.cs](../src/San9.Core/Simulation/Combat/IntelligenceTactics.cs)，`ApplySecondary` 中 `int quality = (proficiency + 100) / 250 + 1` 及后续无阵持续天数计算。

`OfficerState.IsValid` 接受非负熟练度，不把 1000 的成长上限当作内容输入上限；`OfficerTacticGrowth` 也明确保留已有超限值。主效果公式 `TacticMath.Quality` 已使用 `BigInteger`，副效果却先以 `int` 计算加法。在 `proficiency > int.MaxValue - 100` 时溢出，`Math.Min(5, ...)` 只限制上界，不能阻止负持续期。

复现沿现有 `IntelligenceTacticTests` 的合成战斗，通过 Core 正常选兵法、下攻击命令、结束控制和推进一旬。设置目标无地域都市和服务据点，使攻心超额兵员留在目标、保证其存活进入副效果；比较除熟练度外相同的两个初始世界：

```text
value=1000       -> NoFormation DaysRemaining=5，0190-01-06 恢复 Normal
value=2147483647 -> NoFormation DaysRemaining=-8589613，0190-01-02 当日即恢复 Normal
```

极大值世界能正常创建，所有命令接受；不需要修改运行中的快照。负值是在正常推进内产生并出现在领域事件中，随后每日状态周期清零；本复现没有发生崩溃，不将其描述为推进异常或永久坏档。

影响仅确认于极端自定义数值，按低优先级记录。修复方向是让副效果使用与主公式一致的宽整数/饱和运算，并验证最终持续期非负；不要把原作输入范围强加为共享内容上限。回归应比较正常、超成长上限及整型边界的主/副效果，防止更高熟练度反而消除不利状态。

### A07 · P3：合法极大兵役人口令拆除设施抛出溢出异常

位置：[Demobilization.cs](../src/San9.Core/Simulation/Military/Demobilization.cs) 第 22 行；公开触发入口为 [FacilityDemolition.cs](../src/San9.Core/Simulation/Construction/FacilityDemolition.cs) 第 30 行。

兵役人口使用 uint，内容契约和世界不变量没有把它限制在自然增长上限；回收逻辑也明确要求保留超过增长上限的人口。然而 `Demobilization.Return` 将现有人口与返回兵数相加后 checked 转回 uint，没有为表示范围不足定义行为。

复现使用 `FacilityDemolitionTests` 的合法初始世界结构，仅在创建前设置接收城市的 `DraftPopulation`，设施含 100 士兵和 20 伤兵，经公开 `SimulationEngine.Execute` 拆除，不修改运行中快照：

```text
draft=500000     -> accepted=True; after=500120
draft=4294967295 -> OverflowException; unchanged=True
```

异常栈为 `Demobilization.Return → FacilityDemolition.Apply → SimulationEngine.Evaluate → Execute`。世界和引擎初始化均成功；失败时原稳定状态哈希不变，故不声称存档损坏或已提交部分人口变化。确认范围仅为极端自定义内容，不声称通常原版剧本可以短期累积至该数值。

修复方向：协调人口表示范围与回收守恒规则，定义无法容纳时的领域行为，不能仅去掉 checked 使其回绕，也不应以截断丢兵掩盖矛盾。回归需覆盖设施拆除、部队吸收和其他共用回收入口的边界行为。

## 实际验证

`dotnet test San9Remake.slnx --nologo --verbosity minimal`：退出码 0，全部 3,376 项通过：Core 2,381、Application 464、Content 74、Importer 343、Client 98、Headless 16。Client 测试随整套运行，不表示已人工审计表现层。

续审额外执行的独立 `ScenarioBenchmark.csproj` 构建失败，见 A05；不得把上面的解决方案结果概括成“仓库所有 C# 项目均可构建”。

本机独立诊断程序位于忽略目录 `local/generated/verification/csharp-audit/`，使用合成内容，不需要私有样本或启动游戏：

```powershell
dotnet run --project local/generated/verification/csharp-audit/Audit.csproj
```

程序引用当前产品项目；借助现有测试友元程序集访问 Core 内部接口。登用及副效果复现通过实际命令及推进接口，回放和退隐复现通过 Application 公开接口，不通过事后篡改快照制造异常。`Program.cs` 包含 A01/A02 及保留另一城的对照，`LogProbe.cs` 包含 A03，`PlotProbe.cs` 包含 A04，`ProficiencyProbe.cs` 包含 A06，`results.txt` 保存输出。诊断程序退出码 0 表示复现执行完毕，不表示这些缺陷已修复。该目录被 Git 忽略，未纳入提交；上述场景和观测结果保存在本报告中。

补充诊断 `DemobilizationProbe.cs` 通过公开拆除命令复现 A07，并带正常人口对照；最新诊断运行退出码 0，异常由探针捕获并输出，不能解读为该命令成功。

## 后续验收

1. 完成上面明确列出的剩余人工复核范围，再出全量结论；不以现有测试数代替覆盖证明。
2. 修复时以据点交接共享后处理和完整回放配置为根因边界，避免每条转属入口分别补遗漏。
3. 将已确认诊断转换为持久回归测试，并验证存档恢复、确定性事件和最终状态；本轮未实施产品修复。

## 续审记录：搜索事件链

基线仍为上述提交；产品代码未变。此次新增逐文件复核记录 32 个文件：

- `src/San9.Core/Simulation/Events/Search/` 下全部 17 个文件。
- `src/San9.Core/Simulation/Events/EventSelection.cs`、`EventSession.cs`。
- `src/San9.Core/Domain/Governance/PersonnelRulebook.cs`。
- `src/San9.Core/Domain/Events/` 下 12 个搜索定义文件：`SearchEventDefinition.cs`、`CommunitySearchEventDefinition.cs`、`ConflictSearchEventDefinition.cs`、`MerchantInvestmentDefinition.cs`、`DivinerVisitSearchEventDefinition.cs`、`LongevitySearchEventDefinition.cs`、`MysticBookSearchEventDefinitions.cs`、`PhysicianVisitSearchEventDefinition.cs`、`SageVisitSearchEventDefinition.cs`、`ScholarInsultSearchEventDefinition.cs`、`TigerSearchEventDefinition.cs`、`WomenVisitSearchEventDefinition.cs`。

另外重新追踪 `PersonnelMissions` 的搜索启动、选择载荷、响应分派、资格复验和返程调用，以及 `ScenarioInitialState` 中搜索定义和休眠势力激活的引用校验。本节不把上述两个文件的局部复查算作此次新增的完整文件覆盖。

检查结果与证据：

| 检查点 | 本轮证据与结论 |
| --- | --- |
| 单次选择与重复响应 | `PersonnelTaskRule.AcceptsResponse` 校验事件类型、选择、金额、互斥载荷；既有 `SearchDecisionTests` 检验错误任务/步骤/控制者与同日任务顺序。未确认新增缺陷。 |
| 资格变化与返程 | 事件 `CanResolve` 不再抽资格骰；响应经各专用结算入口后调用公共返程。`SearchConflictSessionTests` 检验公开响应、幂等、存档和完整录制回放。 |
| 账户与费用 | 社区活动和固定费用遭遇要求所有选项可全额付款；商人投资独立校验金额。账户从当前出发据点军团解析，与 BEH-EVENT-0002 的统一探索账户约定一致。 |
| 大数与饱和 | 商人资格要求钱粮有接收容量，因此投资基数受到实际账户上限约束；现有 `LargeAuthoredReturnSaturatesAccountWithoutOverflow` 覆盖极大收益与粮价并断言资源事件饱和。最初的收益溢出疑点没有形成可确认缺陷。 |
| 消费范围 | 全局地点键、势力访问/封禁历史、无消费事件分别检查；纪念活动拒绝不消费，商人处斩仅封禁本势力对应类别。通用消费器区分呈现完成与玩法成功。 |
| 内容参数与引用 | 检查概率除数、地点唯一性、有序奖励池、人物/兵法/宝物引用、休眠激活组及城市容量校验。搜索相关 Importer 参数映射仍需在后续转换器批次逐项核对。 |

本轮未新增已确认问题，也不将“未确认”写成无缺陷保证。既有测试已在同一产品代码版本的全套测试中通过，本轮仅修改审计文档，未重复运行全套测试。

本机保守覆盖台账：`local/generated/verification/csharp-audit/coverage.json`。库存包含 326 个候选源文件，并保存基线及 SHA-256；本轮 32 个条目标记为 `reviewed-search-pass`，其余标记为 `not-yet-individually-ledgered`。后者只表示尚未逐文件补入本台账，不表示此前没有检查，也不能直接换算整体审计完成比例。库存包含需按职责区分的内容/导入/工具混合文件；不把显示语义分支扩入本次审计。测试文件作为验证证据，不混入产品源码覆盖数。

## 续审记录：历史与政治事件

补入台账 22 个文件：`Simulation/Events/Historical/` 全部 8 个、`Simulation/Events/Politics/` 两个，对应的 8 个历史定义文件，以及 `PoliticalRulebook.cs`、`ImperialCourtDefinition.cs`、`Simulation/Governance/ImperialCourt.cs`、`PersonnelWithdrawal.cs`。历史执行文件合并此前源码追踪记录与此次定义/交接复查，基线和文件哈希未变化；不是声称此前未阅读过这些文件。

重点核对：季度首个成功事件停止选择、三顾前段/首访/超时状态、连环计死亡继任与失效、战区兵力损失与共享单位终止、皇帝城市多次转属后的最终归属、国号候选去重占用、政治待答资格复验。连环计与名士退隐的交叉路径产生 A04，其公开复现同时验证了稳定保存恢复和完整录制回放。现有政治/皇帝会话测试的覆盖入口已核对；同一产品基线的全套测试结果继续有效，不重复运行。

台账此时有 32 个 `reviewed-search-pass` 和 22 个 `reviewed-history-politics-pass`，其余仍保守标为未逐文件入账。下一批继续未完成的外交任务、战斗兵法及领域定义，不以当前 54 个条目作为全量覆盖证明。

## 续审记录：外交共享结算与基准工具

本批合并此前礼物、劝降等路径阅读记录，补齐 `Simulation/Diplomacy/` 全部 14 个文件及 `Domain/Diplomacy/` 4 个文件的逐文件台账。范围是公开任务资格、旅程、待答、实际报价合法性、交割、关系/同盟/出兵义务状态；不对非玩家策略的选择质量作审计结论。

已检查两阶段俘虏交还和请求出兵在报价/接受时重新校验归属、可付款与接纳地；共享交割按当前实际账户饱和并记录损失；义务重复建立不续期，目标改属清理不罚款，双向违约/到期逐条处理；劝降清理旧任务、军团、控制席位，并恢复新领地中的本方俘虏。核对 `PrisonerReturnTests` 双玩家待答、错误步骤/控制者、资金失效取消与饱和交割断言，以及 `AttackRequestTests` 的到期、违约、履约和目标改属断言；本批没有新增已确认外交缺陷。

交叉核对成员归属消费者时，完整检查 `tools/scenario-benchmark/Program.cs`、`NavigationBenchmark.cs`，并实际构建独立项目，发现 A05。该工具的运行时性能结果未重新测量，不能在构建失败的基线上声称已验证其运行行为。

新增 18 个 `reviewed-diplomacy-pass`、2 个 `reviewed-benchmark-pass`；台账累计 74 个逐文件记录。基线未改，既有诊断和测试不重复运行，独立工具构建作为新的验证入口已明确记录失败。产品代码仍未修改。

## 续审记录：战斗兵法与算术

补齐并归并 `Simulation/Combat/` 全部 24 个执行文件的源码记录；此前完整阅读过的普通交战、战后清理、单挑和狙击链按未变化哈希合并，此次重点新增智力兵法、武力连锁、支援、成长、人物去向和战利品分配。另逐项检查 `Domain/Military/` 的 `CombatMath.cs`、`TacticMath.cs`、`DuelStrike.cs`、`BattleCaptureRules.cs`、`TacticLinkRules.cs`、`TacticRule.cs`、`UnitState.cs`。

已核对无将据点不免疫、主目标最后结算和一次连锁、超额攻心兵役回收/无接纳地保留、治疗伤兵不凭空创造、保留高昂并免除到期士气扣减、学习和成长重新筛选仍在原位置的存活成员。治疗只存在无阵而无其他伤病时不入候选，以及保留高昂的行为均有规格明确，不作为缺陷报告。城市经济损失乘法的疑点经内容字段上限核对后排除，未用超出支持契约的城市值制造发现。

大数检查发现 A06：主公式使用宽整数并不代表执行层重复计算也安全。新增两组实际推进对照；诊断程序重新运行并更新输出，已有 A01–A04 仍复现。未改变产品代码，故不重复全套测试。

新增 31 个 `reviewed-combat-pass`，台账累计 105 个逐文件记录。下一步继续剩余军事命令、导航/军事规则书及人员周期，然后补足内容转换与独立工具范围；当前仍不是全量审计完成结论。

## 续审记录：军事周期与敌对计略

本批完整检查 13 个文件：`WoundedRecovery`、`UnitReformation`、`MilitaryMorale`、`MilitaryConditions`、`DailyRations`、`Demobilization`、`SoldierTotals`、`FormationEligibility`，以及放火、夺取、离间、讹言的执行文件和 `StrategyMath`。逐项核对成员集合与阵型资格、改编支付与命令替换、异常状态到期、高昂延期扣减、伤兵恢复容量与加权士气、任务携兵计数、提交时固定随机数和到达时重新检查目标。

交叉阅读 `StrategyDiplomacyTests` 中出兵义务双向保护与途中建立义务后取消返程的实际断言；核对 `UnitReformationTests` 的渡水改编、成员/伤兵/货物保留和错误命令原子拒绝，以及 `DemobilizationTests` 的超过自然增长上限仍保留回收兵役、缺少地理归属时保留部队与总兵数守恒。讹言在关系值 100 时损失为零有现有明确边界测试，未仅凭直觉列为缺陷。

本批未新增已确认缺陷。保留一个待核实边界：`Demobilization.Return` 使用 checked 转换写回 uint 兵役人口，需进一步确认接近 uint 上限的合法初始内容能否经公开命令触发溢出；目前只发现算术风险，尚不能作为已确认运行缺陷。其他军事领域定义、人员周期及转换链仍未全部补齐。

新增 13 个 `reviewed-military-cycle-pass`，逐文件台账累计 118 个。已核对各文件 SHA-256 与基线一致；未修改产品代码、未重复运行同一版本已通过的全套测试。本记录属于阶段审计，不代表全量完成。

## 续审记录：人口边界复现与人员周期

上轮兵役人口疑点已经完成公开入口的对照复现，升级为 A07。补读设施拆除完整实现，并检查 `OfficerInjuryRecovery`、`OfficerAbilityGrowth`、`NaturalMortality` 全文及它们的周期调用入口：伤势按共享字段恢复，普通成长不降低作者设定的超上限能力，自然死亡跳过豁免人物、逐人结算并在终局后停止。相关测试入口已定位，不将测试名称本身当作行为证明。

三个新增人员文件与此前本轮完整复核的设施拆除文件按基线哈希纳入台账，累计 122 个逐文件记录。人员迁移/登场、领域定义及导入器等仍需继续；全量审计未完成。产品代码未修改，既有全套测试不重复运行。

## 续审记录：人员迁移、登场与待答

完整检查 `FreeOfficerMigration`、`OfficerAppearance`、`PersonnelLoyaltyCycle`、`CapturedRulerDisposition`、`OfficeRequests`、`TacticStudy` 六个文件。重点是迁移的有向地理候选与任务取消，亲属登场对父母/同族身份的区别，非所属势力忠诚逐季趋向中值，以及俘虏君主、请求出仕、兵法学习的待答资格和恢复边界。混合文件中的自动选择分支仅用于理解共享状态衔接，不据此对非玩家策略质量作结论。

实际阅读测试断言：`OfficerAppearanceTests` 初始化不重演、年度恢复一致、父母身份和普通同族只提供位置；`FreeOfficerMigrationTests` 30 天/概率严格边界、有向候选与无候选随机消耗；`OfficeRequestTests` 拒绝迁移和待答对象失效后继续队列；`TacticStudyInterruptionTests` 死亡、俘虏、城市失去后取消学习且不授予兵法。学习任务同时依赖共享 `TaskReferencesValid` 检查当前归属和驻地，不能仅看其自身 `Validate` 判断缺少资格复验。本批未新增确认问题。

另将此前已完整阅读的经济执行 7 个文件、经济领域定义 4 个文件按当前相同基线及 SHA-256 归并入账，并未冒称本轮重新逐行阅读或重跑测试。新增 6 个人员记录、11 个经济归并记录，逐文件台账累计 139 个；仍有 187 个候选文件尚未逐文件入账。该数量不是全量完成比例，也不能代替剩余源码检查。产品代码和测试基线不变。

## 续审记录：治理、人事历史记录归并与军事定义补读

本轮完整阅读 `CorpsGovernance`、`DefectionPressure`、`UnitPolicies`，补齐 `MilitaryRulebook` 第 201 行至末尾（前段此前已完整检查）。检查军团调整钱粮差额、溢出拒绝、主军团保留城市、驻地关联单位的归属更新，以及解散后资源和身份交接；`CorpsGovernanceTests` 的实际断言覆盖资金超过日常上限仍守恒、引用迁移、快照恢复和 uint 溢出原子拒绝。叛变压力检查围攻对象、支援排除和确定性候选排序。单位自主策略属于玩家亦可设置的共享部队规则，不扩展到非玩家势力规划质量审计。

军事定义后段检查地形陷阱、城市影响反向搜索、设施实例与静态定义选择、攻击条件及命令联合字段合法性。极端内容中设施射程叠加与连锁倍率的算术边界仍有后续核实价值，当前没有新增运行复现，不列为确认缺陷。

按当前哈希归并此前完整检查的人事转属、返程、俘虏、任官、奖惩、继任和治理生命周期记录。此次共补入 35 个条目（17 个治理、16 个人事、2 个军事定义），其中上述新读/补读与历史归并明确区分。`PersonnelMissions` 仍暂不入账，留待最终补齐长文件阅读边界；不是将目录整体一概标记完成。

台账累计 174 个逐文件记录，152 个候选条目尚未入账；已有 A01–A07 结论不变。仅更新审计产物，未改产品代码或提交，也未重复运行基线已通过的全套测试。全量审计继续。

## 续审记录：导航派生缓存与建设契约

新增完整阅读 8 个文件：`BattlefieldRegions`、`BattlefieldTopologies`、`PathSearchDiagnostics`、`ConstructionLocations`、`ConstructionPlacement`、`FacilityRule`、`MilitaryGeography`、`TerritoryConnections`。地图拓扑按足迹和旅行模式派生，分区只连接真实可通行相邻节点；目标区域数组在调用侧去重排序，区域引导缓存不保存动态占用，地图级缓存加锁、搜索工作区按线程隔离。建设查询仅说明位置可用，不替代支付、兵法、人员与授权检查。

交叉阅读路径搜索的通行性前置条件与目标收集调用，以及 `BattlefieldPathSearchTests` 独立加权距离对照、动态障碍撤销不污染缓存、并发查询一致性的实际断言。领土边界缓存只保存静态水陆条件，港口集合每次由当前世界计算；`TerritoryConnectionTests` 明确断言港口销毁即时断连、易主不改变地理连通性。本轮未新增确认缺陷，不把有限地图测试扩写为所有规模的性能保证。

另按相同哈希归并此前完整审阅的 12 个文件：地图、寻路、坐标、移动计划，军事移动与占用、设施建设、日循环、部队政策、增援运输、部队计略和终止服务。新增阅读与历史归并合计 20 项，台账累计 194 项，仍有 132 项候选未逐文件入账。表现层仍排除；本批不重复运行产品基线已通过的测试，不修改产品代码。

## 续审记录：会话基础设施与终局统计

完整阅读 `CanonicalJson`、`CommandAdvice`、`XunRecovery`、`EndingSnapshotFactory`，并补齐 `WorldInvariants` 此前未完整显示的搜索/学习任务载荷校验段。规范编码按序排列对象属性、保留数组顺序并拒绝重复字段及非整型数值；军师建议使用条件随机而非消耗推进随机流；旬恢复遍历不可变字典的原视图，替换当前字段不会使原枚举失效。终局入口已先回收任务携兵并清空任务，不能仅凭终局快照工厂不直接累加任务载荷就认定漏算。

检查 `EndingSnapshotTests` 关于休眠对象排除、资产账户计数、当前归属、宝物转移和不修改输入快照的实际断言；没有据此扩大为全部终局规则已经完成审计。`AssemblyInfo` 的友元声明亦完整读取。

按相同基线/hash 归并此前完整检查的 Application 会话定义/协议/校验/终局入口、6 个持久化文件，以及世界状态/事务、时间、随机、控制轮转、任务取消、终局判定和结算管线。新增/补读与历史归并合计 27 项，台账累计 221 项，105 项候选仍未逐文件入账。`GameSession`、`SimulationEngine` 等长文件仍保守留待补齐，不以本次归并宣称所有会话代码完成。已有 7 项确认问题保留；产品代码未改，未重复运行基线测试。

## 续审记录：剧本容器与基础记录解码

完整阅读剧本 RIFF 容器、版本检测器和势力/城市/地区/国号记录解码六个文件。检查流可读可定位与起始位置、声明长度、分块大小和对齐、已知分块顺序、记录实际填满分块、48 位标志拼接、源哨兵引用与身份字段。检测器校验注册大小及 SHA-256；解码器的固定记录数、编号和文本索引属于版本源格式，不据此限制共享内容契约。

实际检查 `San9ScenarioArchiveTests` 的分块偏移、头字段/模式位拆分、日期换算、长度改变与截断拒绝断言；重读国号转换器核对按源表顺序覆盖反向省份映射。本轮没有进行私有原版样本重新导入，也没有新增逆向事实结论，合成测试不能作为真实源数据覆盖证明。

同时将此前完整审阅的 LS11 容器、客户端包导入编排和八个剧本转换器按未变化 SHA-256 归并。合计新增 16 个台账项，累计 237 项，89 项候选未入账。未新增确认缺陷；其他记录解码、事件转换、内容包及长会话文件仍需继续。产品代码不变，既有全套测试不重复运行。

## 续审记录：官爵与共享语义转换

完整阅读 9 个文件：官职、爵位记录解码，以及爵位、外交、兵法学习、兵法连锁、皇室、终局、内政组合的转换器。核对无官职哨兵、最低爵位作为真实定义、能力位掩码、同盟角色矛盾拒绝、人物传记键向保留人物身份展开、兄弟加成排除与特殊加成合并、源地理表仅存在版本 Importer 等边界。

实际读取 `TacticLinkContentCompilerTests` 关于重复传记映射为多个保留人物、过滤人物、关系关闭和损坏表拒绝的断言，以及 `DiplomacyContentCompilerTests` 关于不连续势力身份、丢失同盟成员和矛盾持续期的断言。核对剧本编译调用处传入保留人物集合；内政转换取前 700 个源人物，与当前人物编译器保留范围一致，配置了扩展人物会明确拒绝，而非静默导入后遗漏组合。此处是当前版本实现边界核对，不宣称重制共享契约应继承 700 人上限。

本轮未新增确认问题，也未重新验证硬编码表的原版逆向事实或私有输入覆盖。台账新增 9 项，累计 246 项、80 项候选待补齐；文件数量覆盖约 75%，不等于总工作量完成率。仅更新审计产物，产品代码与测试基线不变。

## 续审记录：地图格、区域与设施源记录

完整检查 `San9ScenarioStrategyArchive`、`San9ScenarioTerrainArchive`、`San9ScenarioZoneArchive`、`San9ScenarioSquareArchive`、`San9ScenarioFixedBuildingArchive`。核对源记录宽度、字节/位字段、48 位兵法标志、源编号到区域编号的偏移、坐标成对存在、重复位置拒绝和城市/区域在格子上的归属一致性；没有将保留的未知源字段自行解释为玩法规则。

实际阅读地图格测试的行优先坐标、有效拓扑、区域码错配和越界拒绝断言。全库定位确认跨记录 `ValidateKnownTopology` 在 `MapImporter` 调用，而非所有解码函数自行执行；剧本编译器另检查配置区域坐标，军事转换器检查目录连续性、兵法引用和缺失地形。这里记录实际校验边界，不声称某个局部解析入口独自完成全部输入验证。

本轮新增 5 项，累计 251 项、75 项候选待补齐；未新增确认缺陷，未运行私有样本导入。产品代码与测试基线保持不变，全量审计继续。

## 续审记录：人物、部队、兵法与宝物源记录

完整阅读 `San9ScenarioFormationArchive`、`San9ScenarioTacticArchive`、`San9ScenarioItemArchive`、`San9ScenarioUnitArchive`、`San9ScenarioPersonArchive`。检查各字段读取宽度与记录总长、阵位及兼容阵型位掩码、宝物发现状态和持有人哨兵、部队目标类型的高低半字拆分、人物年值偏移、忠诚数组长度、九类熟练度和 48 位已学兵法。原始数值保留与 `Loaded*` 源加载归一化属性分开，不把源部队人数截断规则误记成 Core 定义。

实际阅读 `San9ScenarioUnitArchiveTests` 的合成字节记录及断言，核对原始人数保留、加载人数截断、目标对象、指令、位置和自主行为标志。解码结果后续还要通过版本转换器与内容契约；本轮不声称所有未知源字段均有已验证语义，也不把读取函数无业务校验直接当作已确认缺陷。

新增 5 个文件记录，累计 256 项、70 项候选待补齐。未新增确认问题；未修改产品代码或重跑相同基线全套测试。余下长会话文件、事件转换、内容与工具范围继续审计。

## 续审记录：终局规则与能力契约

完整阅读终局定义/纯结算/胜利条件三个文件，以及 `OfficerAbilityRules`、`TreasureRulebook`、`CorpsDelegation`、`InformationState`。核对终局四名官员互异且不能选君主、少于五人分支、继承稳定性与第四阶段独立、关隘仅替代第三阶段条件；实际读取 `EndingResolutionTests` 的对应阈值和短路断言。宝物同能力取最大加成而非累加，伤势先作用基础值再加官职/宝物，批量求值不持有可变世界缓存。

能力派生使用 checked int 加法，合法单字段极大值的组合可能超过表示范围；尚未完成其初始化可达性和公开变更入口验证，留作疑点，不列为新增确认问题。检查过的源码仍需后续跨层边界复核，台账状态不表示完全排除缺陷。

另按相同哈希归并此前完整阅读的 `GovernanceRulebook`、`OfficerState`、`RuntimeState` 三个文件。本批共 10 项，累计 266 项、60 项候选未入账。产品代码不变，原有 7 项确认发现保留，全量审计继续。

## 续审记录：内容基础契约与文本引用图

完整检查来源标识、国号定义、文本目录、消息图、消息节点定义及宝物文本键六个文件；关注引用存在性、环与深度防护、表达式参数个数、静态/纯文本访问对动态或富文本的拒绝，不评价表现层排版。`MessageGraph.Validate` 与 `Requirements` 职责不同：前者检查节点和直接引用，后者汇总跨消息绑定、环和可执行性，不能将前者称为独立的完整可执行性证明。

按当前相同哈希归并此前完整审阅的包路径、场景包及读写、出兵请求初始状态、信息定义、人物初始状态和场景初始状态七个文件。另完整阅读 `NameMessages` 和 `StrategyHintText` 后确认它们仅为表现模板/提示键清单，标记范围排除，不计作玩法审计完成数。

本批新增 13 个审计记录、2 个范围排除。当前候选库存仍为 326，实际审计记录 279、明确排除 2、待处理 45；不把排除项冒算成已审计文件。未新增确认问题，产品代码未改，未重复运行相同基线测试。剩余混合表现/基础设施文件还需继续逐项判定范围。

## 续审记录：Headless 验证入口

补齐 `HeadlessHost` 第 226 行至文件末尾，与此前前段完整阅读合并；完整检查 `HeadlessReport`、`Program`、`ScenarioFixture`。核对互斥输入、输出路径去重、回放失败传播、恢复时初始事件不重演、连续/恢复状态与事件对照、重复日志前缀幂等检查。验证器中待答自动选择是显式验证策略，不等于正常会话默认替玩家作答；本轮不评价所调用非玩家规划器的策略质量。

Headless 命令日志启动参数仍存在 A03 已确认的伤害倍率遗漏，不重复编号。本批属于源码覆盖补齐，未重新运行既有同版本通过的 Headless 测试或私有剧本。新增 4 项，当前实际审计记录 283、明确排除 2、待处理 41。产品代码未改，全量审计尚未完成。

## 续审记录：图片包基础设施边界

完整检查 `TexturePackage`、`TextureLibrary`、`ImageAsset`、`PngImage` 的数据完整性职责：物理文件元数据一致性、区域边界/重叠、重复标识加载拒绝、包内路径解析、PNG 分块长度/CRC、受限格式和解压像素精确长度。实际读取 `MapImageTests` 的逐字节往返与损坏/截断拒绝断言；不将这些检查称为图像显示验收。PNG 是项目明确限定的包格式，不以不支持通用 PNG 全功能作为缺陷。

另完整读取头像裁剪和纹理分组两个文件后，确认属于纯表现职责，明确排除。本批新增 4 个审计记录、2 个排除，累计实际审计 287、明确排除 4、待处理 35。当前没有新增确认问题；源码完整性边界仍需结合剩余包消费者收尾，未修改产品代码、未重跑相同基线测试。

## 续审记录：文本导入基础设施

完整检查 `OriginalTextConverter`、`San9MessageTable`、`MessageArchiveCompiler`、`ExecutableTextCompiler`。核对原始编码错误严格拒绝、扩展字形显式映射、原文保留、消息表偏移单调与容器尾字节、纯文本终止符、引用图依赖汇总，以及 EXE 证据 SHA-256 和对应 PE 地址字节的一致性。编译审计记录明确区分语法可解析、语义已确认、依赖完整和玩法绑定，不将前几项等同于最后一项。

实际阅读编码转换测试的未知字形/截断/控制码拒绝，以及消息表损坏输入测试；不涉及显示效果验收，也未重新读取私有 EXE。主消息解析器与场景文本读取器仍留在待审清单，不能因编排器已检查就视为整体解析链完成。

新增 4 项，累计实际审计 291、明确排除 4、待处理 31。未新增确认缺陷，产品代码与既有测试基线不变。

## 续审记录：消息解析与场景文本索引

完整阅读 `OriginalMessageParser` 和 `ScenarioTextReader`。检查重复调用的状态重置、字节读取边界、分支与归档终止符区别、二进制引用参数、未知属性保留诊断并禁止执行、显式格式参数绑定，以及场景姓名/传记/设施后缀索引和 PE 字符串终止检查。表现样式仅检查语法消费和未支持语义标记，不验收视觉效果。

交叉阅读主解析器测试中的人物关系、部队状态、势力关系、保留选择器和属性映射的实际断言。另记录待核实边界：解析递归层数与最终生成的左结合表达式树深度并不相同，需要进一步对照 `MessageGraph` 的深度限制和导出行为；格式宽度的 checked int 转换也需确认异常诊断边界。目前没有公开链路复现，不新增缺陷编号。

新增 2 项，累计实际审计 293、明确排除 4、待处理 29；产品代码未改，仍未完成全量审计。

## 续审记录：索引图像与头像档案边界

完整读取 `IndexedImageArchive`、`San9FaceArchive`、`TextureSourcePath`，审计范围为文件格式和资源身份基础设施。检查连续偏移、记录大小与像素长度一致、尾随数据拒绝、头像逐记录元数据一致、调色板头长和文件边界、源身份路径不依赖像素内容；裁剪及透明像素处理仅核对索引和数组约束，不评价显示效果。

实际阅读 `TextureArchiveTests` 的每个图像索引/边界保留，以及偏移、重叠、维度畸变拒绝断言。当前调用链仍需结合剩余图集/包编排器检查，不把底层读取器检查完成等同整条资源管线完成，也没有运行私有样本或视觉验收。

新增 3 项，累计实际审计 296、明确排除 4、待处理 26。未新增确认缺陷；产品代码和测试基线不变，全量审计继续。

## 续审记录：混合地图导入职责判定

完整读取头像编译器、地图导入编排器和 Core 友元声明。头像部分检查源图数量/尺寸、锚点表长度和身份连接；地图部分核对剧本识别、城市/区域/格子拓扑交叉校验、资源引用及输出结构。绘图坐标、美术组合和画面效果不在此次结论范围，不因文件位于 Importer 就扩大视觉审计范围。

另完整读取云层编译、边界美术编译、地图悬停框合成后，确认三个文件仅生成表现数据，明确排除。该范围判定有源码依据，不是为了减少剩余工作而按目录一概排除；仍有包编排、图集文件和地图契约等基础设施待审。

本批新增 3 个审计记录、3 个排除。累计实际审计 299、明确排除 7、待处理 20。未新增确认问题，未修改产品代码或重复测试；全量审计继续。

## 续审记录：纹理包与图集基础设施

完整读取 `TexturePackageImporter`、`TextureAtlasCompiler` 和 `TextureLibraryCompiler`，检查源文件清单和缓存戳、逻辑资源身份与共享文件映射、固定尺寸图集的放置/分割边界、编号冲突、位图头及像素范围、取消检查和输出顺序。实际阅读 `TextureAtlasTests` 的稳定编号、冲突拒绝、资源独立、反序输入确定性、分页尺寸和回读断言；不评价显示效果。

注意图像文件写入早于最终清单替换，不能据清单使用 pending 文件就宣称整个目录事务性更新。尚未完成中途失败注入及调用方恢复契约核实，因此此项仅记录验证边界，不列为已确认缺陷。未执行私有资源导入，也未重复运行未变化的测试。

本批新增 3 项，累计实际审计 302、明确排除 7、待处理 17。确认问题仍为 A01–A07；产品代码未改，全量审计尚未完成。

## 续审记录：会话入口与多态命令/事件契约

完整逐段重读 `GameSession`（包括此前未单独登记的提示转发和操作分支）、`GameCommand`、`DomainEvent`。核对会话操作序号与领域命令序号分离、重复水位处理、候选世界提交、失败后稳定状态、记录起止、缓存失效、玩家载入重置随机流与精确恢复的区别。规划器调用只检查事务边界，不审计其非玩家决策逻辑；表现投影不作视觉语义验收。

实际阅读 `PersistenceTests` 中操作水位、复合推进失败不发布状态、执行中存档拒绝、近期记录上限、玩家笔记保留和中途开始录制/回放的断言。另对当前 Core 源码的派生 record 声明与 JSON 注册作静态集合比较：46 个命令、152 个领域事件，未见遗漏、多余项或重复 discriminator。该检查不等同每个类型所有字段的运行时往返验证；既有测试基线没有重跑。

本批新增 3 项，累计实际审计 305、明确排除 7、待处理 14。没有新增确认缺陷，产品代码未修改；`SimulationEngine`、人事任务和事件编译器等剩余文件仍须完成，不能以契约登记齐全代替行为审计。

## 续审记录：模拟引擎与人事任务完整补审

分段完整重读 `SimulationEngine` 852 行和 `PersonnelMissions` 458 行，补齐此前重复转发和长分支未完整登记的部分。引擎核对初始化/恢复差异、待决任务和规则引用、所有提示查询、命令信封授权、响应类型及步骤匹配、验证与执行共用候选 mutation、提交前不变量、旬推进与恢复续段。静态比较 46 个已注册命令与执行 switch，全部有对应分支；这只证明分发集合一致，不证明每种业务规则正确。

人事任务核对批量移动先验证再建任务、稳定排序与任务身份、出发时固定条件随机数、旅程期限、响应字段互斥、到达时重新检查目标/归属、搜索发现与回程。实际阅读引擎测试中的只读验证、执行时重验、失败后随机状态及事件不发布、同一世界跨引擎重入保护，以及人事测试中的多出发地移动、目的地失守取消、忙碌人员拒绝不消耗随机/序号和搜索前重新检查母城归属。没有把测试名称或既有通过数量单独当作上述行为的覆盖证明。

招募最后城市的消灭流程和俘虏迁移遗漏仍由 A01/A02 记录，本轮未新增确认缺陷。新增 2 项，累计实际审计 307、明确排除 7、待处理 12。未修改产品代码、未重复测试；全量审计继续。

## 续审记录：事件内容编译与命令行验证入口

完整阅读 `EventContentCompiler`，核对以传记资源身份绑定保留人物、缺失/重复身份拒绝、模式 2/9 排除、模式 1 与历史功能开关区别、事件优先级和消费键、势力历史位与跨 uint 边界的地点历史位转换、开局日期补设历史，以及蓬莱势力休眠初始化保留对象和资源。固定编号及源标记留在版本导入器；没有因此给共享契约增添原版容量限制。实际阅读相关测试中的身份重排、缺失/重复拒绝、蓬莱多成员保留、日期边界、无关位忽略和地点消费键逐项对应断言。本轮没有重新逆向原作或读取私有样本，故不把源码常量与测试一致冒充新增原作证据。

完整阅读 Tools 的 `Program` 和 `MessageVerification`，范围限参数检查、导入/包验证编排及诊断输出；render-map 画面行为与消息表现求值器内部不在范围。合成消息检查只对可执行依赖图使用三个上下文，收集失败到报告，不能当作全分支或真实玩法绑定验证；这一边界与当前内容管线文档一致，不因 CLI 正常退出就宣称所有消息验证成功。

新增 3 项，累计实际审计 310、明确排除 7、待处理 9。没有新增确认缺陷、产品代码修改或重复测试；剩余文件及待核实边界仍需继续处理。

## 续审记录：剩余地图及界面资源契约

完整读取剩余 9 个文件。`ClientContentPackage`、`MapPackage`、`InterfaceSkin`、`InformationArtCompiler`、`InterfaceSkinCompiler`、`MapResourceCompiler`、`MapAnimationCompiler` 的结论限于跨包身份/坐标/文本引用、资源目录与帧引用、导入索引与边界基础设施。地图尺寸、图形编号、色板及固定美术目录的视觉能力限制不作此次玩法限制结论，也不宣称画面效果已验收。`OfficerDetailSkin` 只列界面角色名，`MapTerrainCompiler` 只生成绘制印章及色板选择，完整阅读后归为表现层排除。

实际阅读 `ClientContentPackageTests` 中缺失角色、身份/坐标不符、逐个删除文本引用、未被势力使用的国号定义仍需名称、宝物说明与引用完整性的断言。`ValidateLinks` 负责组合引用，加载入口另调用地图完整校验；不能因单独链接检查接受简化测试地图就断言实际加载跳过地图校验。

本批 7 项基础设施审计、2 项范围排除。初始 326 项候选清单现为实际审计 317、明确排除 9、未登记项 0；重新核对所有清单文件 SHA-256，没有基线漂移。**这只是候选清单闭合，不代表全量审计已经完成**：仍需检查清单之外的代码是否正确排除，并对前述未确认疑点作复现或结论收口。确认缺陷仍为 A01–A07，产品代码未改，目标保持进行中。

## 范围纠正：玩家势力委任军团的共享规划路径

清单外复查发现此前按 `*Planner.cs` 排除的规则过宽。`AutomaticCorpsPlanner.Run` 不只处理非玩家势力，也在玩家控制席结束时处理该势力的自动军团；因此玩家委任执行路径不能被“非玩家势力 AI”排除。撤回此前对此类整文件排除的完整性假设，将 `AutomaticCorpsPlanner` 及其共享 `DomesticPlanner`、`MilitaryPlanner`、`StrategyPlanner` 纳入候选清单。本轮完整读取这四个文件，检查军团权限筛选、行动前公共验证、任务占用、确定性排序和命令身份、玩家委任目标传递及路径缓存输入；不评价非玩家势力的战略质量。

实际阅读 `CorpsControlTests`：玩家交出军团直辖权限后旧上下文拒绝、军团上下文不能操作外部人员/解散军团、推进产生真实招募与内政事件、保存恢复及完整录制回放一致。这些断言支持玩家委任路径确实在原目标范围内，不能用文件名规避。未重复运行既有测试。

修正后清单为 330 项：实际审计 321、明确排除 9。该数量变化是修正范围遗漏，不是产品变更；尚须继续检查其他清单外文件及此前未核实疑点，不能宣称全量完成。确认缺陷仍为 7 项。

## 续审记录：启动基础设施与能力溢出疑点复现

完整读取客户端 `ClientOptions` 和 `Main`，只纳入非视觉内容加载、电脑伤害加成参数传递、诊断用内存初始状态与正式内容隔离；绘制、布局及视觉生命周期仍排除。另发现 Headless 脚本验证对包括玩家在内的所有控制席调用 `DiplomacyPlanner`，将该文件加入待补审，撤回专属非玩家 AI 的排除假设。

此前能力加成疑点现已用合法初始内容与公开授宝命令复现，新增 A08（极端自定义数值，P3），未改变产品代码或运行完整测试集。当前清单 333 项：323 已审计、9 明确排除、1 待补审；已确认问题增至 8 项。其余疑点与清单外范围判定仍须继续，审计未完成。

## 续审记录：共享外交规划与消息边界实测

完整读取 `DiplomacyPlanner`，核对使者任务/外交回程占用、候选稳定排序、公开建议和命令验证、礼物资金预留。实际阅读测试中的同一输入重复规划不改状态、俘虏赎回后不派第二使者、礼物预算临界值；范围是玩家脚本验证所依赖的共享执行安全，不评价非玩家战略质量。清单现为 333 项：324 已审计、9 明确排除。

新增忽略目录 `MessageBoundaryProbe.cs`，运行 `dotnet run --project local/generated/verification/csharp-audit/Audit.csproj -- message-boundary`（退出 0），将此前两个消息疑点从算式推测推进到直接复现：

- 原生格式 `%10d` 正常确认；`%2147483648d` 抛 `OverflowException`；更大的 `%4294967296d` 反而返回未完全解析、未确认、一个诊断。可确认格式宽度的错误诊断路径不统一，但该宽度不可执行，且尚无已登记原版样本触发证据，不作普通玩法缺陷结论。
- 条件中的左结合算术链：63 次加法时解析和图校验均通过；64/80 次加法时解析返回 FullyParsed、SemanticsConfirmed、Requirements.Executable 均为 true，但 `MessageGraph.Validate` 拒绝“Message expression too deep”。这证实“解析递归深度”与“生成表达式树深度”不同，确认标志不能单独作为最终可装载证明；仍需核实完整归档导出/诊断保留影响后决定独立缺陷分级。

上述消息探针为合成输入，没有读取私有样本、执行消息表现求值器或修改产品代码。确认缺陷仍为 A01–A08；包写入中途失败、其他数值边界及清单外排除核实尚未全部收口，目标保持进行中。

## 续审记录：长表达式归档导出链路复现

本轮构造完整 LS11 容器、单记录偏移表与四个消息文件，直接调用 `MessageArchiveCompiler.Compile/Export`，不再只测试解析器。64 次加法的记录被无诊断确认，但序列化失败导致首个消息文件及对应审计文件均未生成；2/20 次加法对照成功。此前疑点现升级为 A09（P3），已确认问题共 9 项。所有输出位于忽略的审计验证目录，产品代码未改，未重跑未变化的完整测试集。

候选清单数量未变（324 已审计、9 明确排除）；其余未核实风险和最终范围复核仍在进行，不能因新增复现或清单无未登记项就宣布审计完成。

## 续审记录：正式规则校验后的连携与射程边界

新增 `RuleBoundaryProbe.cs`，使用 `MilitaryRulebook` 正式构造校验而非直接跳过定义校验。运行 `dotnet run --project local/generated/verification/csharp-audit/Audit.csproj -- rule-boundary`，退出 0，确认两项此前静态疑点确实可到达公开领域查询：

- `FormationTactics.LinkMultiplier=2` 时同相性人物的连携机会为 50；`int.MaxValue` 同样通过定义校验，却返回 -25，而宽位算式结果为 53,687,091,175。执行调用点 `TacticCombat.ActivateMartial` 直接把该结果交给 `Roll`；后者对非正数拒绝、对至少 100 的机会必定接受。故溢出会改变概率方向，并非仅显示超大数字。
- 设施基本射程 1，先天 `RangeBonus=2` 返回 3；`RangeBonus=int.MaxValue` 同样通过规则构造，但 `FacilityAttack` 的 checked 相加抛 `OverflowException`。尚未在本轮补足整场战斗推进的复现，不能据此声称存档已损坏或普通设施会触发。

这两项已从未验证算式推进到有效定义的领域入口复现，后续需补齐完整执行影响与独立问题分级。全部是极端独立创作参数，无原版常规数值触发证据；本轮不增加正式缺陷编号，确认问题仍为 9 项。产品代码未改、未重复完整测试，审计继续。

## 续审记录：连携与设施射程完整执行影响

本轮补齐公开命令/旬推进链路：相同种子的双人兵法从倍率 4 的两人连携变为极端倍率的一人发动；设施极端射程加成在首次日战斗目标构建时中止整旬，对照正常完成 10 次设施攻击，异常前后稳定状态哈希一致。两项现分别登记 A10/A11（P3），已确认问题共 11 项。未修改产品代码，也没有重跑未变化的全套测试。

候选文件覆盖数量未变；尚需收口包更新异常恢复、其他记录的边界限制及最终范围/证据核验，全量审计仍未完成。
