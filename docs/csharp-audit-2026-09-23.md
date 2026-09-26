# C# 复核审计：2026-09-23 阶段报告

审计基线：`27eb8902114c9f45d9ff70697efde293a48eb733`。下列原始复现针对该基线，P1/P2修复及当时验证结果见下一节；这是暂停时的报告，不是当前版本重新审计的结果。

**状态：全量审计尚未完成。** 以下是已经完成代码追踪和运行复现的发现，不是对剩余代码无缺陷的保证。全套测试通过不代表全量人工复核完成。

## P1/P2 修复结果

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
- 已登记的覆盖与未决事项见文末；不能把扫描、测试通过或候选台账闭合视为全量人工审计完成。
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

## 覆盖范围与证据边界

暂停时的候选台账共333项：324项已审计、9项明确排除。台账保存基线和逐文件SHA-256，位于 `local/generated/verification/csharp-audit/coverage.json`；测试文件作为证据，不计入产品源码数量。这表示候选清单已登记，不表示仓库全量审计完成，也不证明当前代码已复审。

| 范围 | 已核对的行为与边界 |
| --- | --- |
| 搜索、历史及政治事件 | 资格与提交分离、一次响应、消费键、账户费用、奖励容量、人物失效、候选续行、保存与录像；Importer的人物引用、模式门及初始历史映射 |
| 经济、人事及治理 | 资源账户和饱和报告、任务占用与返程、登场／迁移／死亡、军团权限和引用转移；最后城市与俘虏遗漏由A01／A02单列 |
| 军事、兵法及计略 | 成员与兵数守恒、友军保护、跨岸改编、回收与建设、状态及连锁；数值反例由A06–A08、A10–A11单列 |
| 导航与规则定义 | 静态缓存依赖、动态占用、确定性排序、内容参数及引用合法性；不以原版容量代替共享规则边界 |
| 会话与持久化 | 命令授权、候选世界事务、失败不发布状态、待答限制、序号及操作水位、玩家载入与精确恢复；当时46个命令和152个事件的注册集合一致，不等同全部字段往返验证 |
| 源档案与语义转换 | 容器边界、字段宽度、哨兵、位域、人物／宝物／部队／地域引用，区分原始值与加载归一化；未知源字段不因代码可读就视为已确认 |
| 内容与图片包 | 跨包身份／坐标／文本引用、资源及帧引用、缓存戳、图集边界、取消与顺序；未执行私有资源重导入或视觉验收 |
| 消息与文本 | 解析、依赖确认、表达式深度、归档导出及诊断；合成上下文不证明真实玩法绑定，A09保留完整导出反例 |
| CLI与启动 | 参数、内容加载、日志／存档／录像接线、诊断输出和伤害加成；基准工具构建问题由A05单列 |
| 共享规划 | 玩家委任与Headless玩家脚本调用的`AutomaticCorpsPlanner`、`DomesticPlanner`、`MilitaryPlanner`、`StrategyPlanner`、`DiplomacyPlanner`均在范围内；不评价非玩家战略质量 |

范围按实际调用职责划分，不能按Godot或`*Planner.cs`文件名排除。表现布局、绘制语义和专属非玩家战略质量排除；混合文件仅审计契约与非视觉接线。未以未发现新问题、测试数量或清单闭合作为无缺陷结论。

已排除的误报与限制：商人投资资格约束实际账户容量，已有极端收益饱和测试；城市经济乘法按合法字段上限核对，不能用超契约输入制造缺陷；仅有无阵时不治疗、保留高昂以及关系100时讹言损失为零均符合既有规格或边界测试。上述结论只针对审计基线。

## 未决事项与后续验收

1. 完成候选清单之外的职责与遗漏核对；基线之后的代码变化需单独复审。
2. 纹理图像写入早于最终清单替换，仍缺中途失败注入和调用方恢复契约验证；不能宣称整个目录原子更新，也尚未据此登记缺陷。
3. 合成格式`%10d`正常，`%2147483648d`抛`OverflowException`，`%4294967296d`返回未解析／未确认及诊断，错误路径不统一；无已登记原版样本触发证据，未列为普通玩法缺陷。复验：`dotnet run --project local/generated/verification/csharp-audit/Audit.csproj -- message-boundary`。
4. 人口、能力、长表达式、连携和射程的已复现问题分别见A07–A11，不再重复作为待验证算式；其余数值组合边界仍需收口。
5. A01–A05的已验证修复见开头；A06–A11在本报告暂停时尚未修复。后续修复应以共享状态和完整调用链为边界，补持久回归及保存恢复验证。

上文的缺陷输入、观测结果与测试只证明各自注明的基线；修复状态和候选覆盖不能代替后续版本复审。
