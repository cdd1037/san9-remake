# 主项目已提交代码审计

审计日期：2026-10-23。基线：`f988e0977550a309591114fb5550dcbca1a57c98`。

整改状态（2026-10-23）：**第二轮 A11–A15 已修复、验证并提交**。下文问题描述与行号保留审计基线含义；当前实现、验证结果与限制见文末“第二轮整改结果”。

本报告记录该提交的实现问题，不代表后续版本仍存在这些问题。代码定位中的行号均属于这一基线；工作区有未提交的结局展示、内容契约与转换器改动，本次未将其纳入结论，也未修改这些文件。

## 结论与优先级

项目的基本分层有效：Core 不引用引擎或 Importer；客户端通过转换包消费内容；会话计算使用隔离分支；领域随机、导航随机、显示随机已有区分；存档使用严格反序列化和单文件替换。没有发现需要推倒现有层次结构的依据。

首轮主要风险集中在**提交与通知的边界、内容包发布与缓存有效性、输入校验以及显示文字承担机器身份**。首轮基线测试全部通过，但五个反例仍成立，说明正常路径与已有规则矩阵不能代替边界故障验证。第二轮聚焦 Core 与 Application，另确认了事件语义、事务缓存依赖、长局分配、事务初始化成本与规则组装方面的 5 项问题或改进点，详见文末专项章节。

优先级定义：P1 为可能造成状态与调用结果分裂、损坏既有可用产物的问题；P2 为明确触发条件下的功能、诊断或资源边界问题；P3 为已确认的维护／扩展成本，不表示当前支持范围内功能失效。这里的优先级是整改顺序，不是安全漏洞评级。

| 编号 | 优先级 | 问题 | 证据状态 |
| --- | --- | --- | --- |
| A01 | P1 | 状态已提交后，订阅者异常仍使会话操作报失败 | 已复现；客户端重试后果经调用链核查 |
| A02 | P1 | 内容包原位更新失败会留下混合版本 | 剧本包已复现；纹理／音频经静态核查 |
| A03 | P2 | 定向重建用单元来源戳为整包背书 | 静态确认；输入指纹建议已按既有决策撤回 |
| A04 | P2 | 编辑器以显示文字反查字段，合法名称碰撞导致异常 | 已复现 |
| A05 | P2 | 存档接受未定义的终局原因 | 已复现 |
| A06 | P2 | 内容集合中的 null 元素绕过反序列化约束并变成内部异常 | Headless 已复现 |
| A07 | P2 | 会话计算把任意 InvalidOperationException 归类为 NotStable | 静态确认 |
| A08 | P2 | 存档目录预览没有头部读取大小上限 | 静态确认；未做大内存压力实验 |
| A09 | P3 | StrategyHud 的 partial 拆分未形成独立职责边界 | 结构核查 |
| A10 | P3 | 地图显示契约与算法多处固化原版尺寸和投影常数 | 结构核查；属于当前扩展限制 |
| A11 | P2 | 投降解散部队的领域事件误标为战败，消费者通过同批事件补充判断 | 领域路径已复现；显示层已有规避逻辑 |
| A12 | P3 | 设施兵法变化没有使交战缓存失效 | 事务级已复现；未确认现有玩家操作链触发 |
| A13 | P3 | 历史追加在完全去重时仍复制全历史，长局成本持续增长 | 分配量实测；未推断实际帧耗时 |
| A14 | P3 | 每个新事务重新扫描全部势力、据点与武将，查询候选也承担该成本 | 静态确认复杂度；未做真实规模计时 |
| A15 | P3 | 历史事件配置通过长参数列表逐层传递，规则组装存在多处同步点 | 结构核查；不否定编译式规则设计 |

## 范围、方法与限制

范围为 `San9Remake.slnx` 中八个产品工程、六个相关测试工程、公共构建配置及 Windows 导出入口。基线 `src/` 下有 837 个手写 C# 文件、74,722 行，包括客户端诊断代码，不含 `obj`、`bin`、`.godot` 生成文件。数量只用于交代范围，不作为质量指标。

本次进行了全工程清单与依赖核查、跨项目风险模式扫描，以及关键状态和文件生命周期的调用链审阅；重点代码做逐段阅读。**这不是对 837 个文件所有分支的穷尽证明**。单项未列出问题，表示本轮没有形成足够证据，不表示无缺陷。

| 工程／范围 | 实际检查重点 | 结论关联 |
| --- | --- | --- |
| Core：276 文件、22,753 行 | 世界／mutation／快照所有权，模拟入口、命令提交、决策恢复、终局、不变量、随机；经济、人事、战斗与导航重点调用 | A05、A11、A12、A14、A15；未据此声称所有玩法公式已与原作对齐 |
| Application：163 文件、15,493 行 | 会话同步／后台操作、发布、存档与恢复、AI 推进、查询投影、编辑器、历史与规则组装 | A01、A04、A05、A07、A11、A13–A15 |
| Content：56 文件、3,359 行 | 包读写、跨资源引用、JSON 设置、剧本校验、地图与图片契约 | A02、A06、A10 |
| Importer：93 文件、10,007 行 | 源识别、整包与定向重建、纹理打包、来源记录、音频写入；抽查资源编译入口 | A02、A03 |
| Rendering：52 文件、3,002 行 | 后台队列、租约、预算与缓存回收，地图投影、相机边界及图像解码边界 | A10；内存压力与帧耗时未实测 |
| Client.Godot：191 文件、19,352 行 | 启动、会话切换、旬推进呈现、保存／退出、设置、编辑器和 HUD 生命周期 | A01、A04、A08、A09 |
| Headless：4 文件、603 行 | 参数、输入输出冲突、错误分类、报告落盘、恢复和命令脚本接线 | A06；现有进程测试通过 |
| Tools：2 文件、153 行 | 转换／校验／渲染入口、参数和异常边界 | A02、A03 对其转换命令同样适用；补充构建通过 |
| 测试与构建 | 工程引用、共享测试配置、默认／独立测试入口、SDK 与导出脚本 | 结果见验证章节 |

不包含：未提交代码、独立 `tools/map-maker-experiment` 原型的专项审计、研究脚本与 IDA 库审计、第三方依赖漏洞调查、原作规则重新逆向、实际游戏画面验收、真实私有资源重转、发行包运行验收。`tools/scenario-benchmark` 也未作为独立产品进行行为审计。录像已延期，本报告不将其列为整改任务。

## 具体问题

### A01 — 提交完成与通知失败共用同一异常通道

**定位：** [GameSession.Operations.cs](../src/San9.Application/GameSession.Operations.cs)，`PublishOperation`，177–204 行；[GameSession.Background.cs](../src/San9.Application/GameSession.Background.cs)，`Complete`；[TurnPresentation.cs](../src/San9.Client.Godot/UI/Strategy/TurnPresentation.cs)，`Publish` 233–266 行及 `Confirm` 87–93 行。

`PublishOperation` 先替换模拟状态、增加 `NextOperationSequence`、更新记录并清除执行标志，之后直接调用 `Changed?.Invoke()`。任意订阅者抛异常，会从 `Apply`／后台 `Complete` 逃出，但领域状态已经提交，无法回滚。

**已复现：** 用正常会话提交 `ChronicleOperation("audit")`，为 `Changed` 添加抛出 `InvalidOperationException` 的订阅者。调用报错，但序号已从 0 变成 1，年表已经新增一项。重试相同请求返回 `AlreadyProcessed`。

客户端 `TurnPresentation.Publish` 把该异常显示为失败并保留旧请求；重试后又把非 `Accepted` 的 `AlreadyProcessed` 当作失败。由此存在提交已经成功、呈现结果丢失、重试无法恢复的路径。该客户端后果已从代码接线确认，未进行 Godot 画面复现。

**根因与整改：** 领域提交结果的所有权不应交给显示订阅者。提交必须产生可保留、可读取的完成结果；通知异常走独立的呈现故障通道，并能重新投影／呈现已提交状态。不能仅在 UI 把所有异常吞掉，也不能直接把所有 `AlreadyProcessed` 视为成功而丢失本次事件。

**验收：** 订阅者抛异常时提交结果仍可确定；世界只改变一次；后续订阅者与显示恢复策略明确；后台发布和同步提交遵守同一语义。

### A02 — 单文件暂存不能保证整个内容包一致发布

**定位：** [ScenarioContentFiles.cs](../src/San9.Content/Packages/ScenarioContentFiles.cs)，119–135 行；[TextureAtlasCompiler.cs](../src/San9.Importer.San9PkTc101/Images/TextureAtlasCompiler.cs)，32–46 行；[TexturePackageImporter.cs](../src/San9.Importer.San9PkTc101/Packages/TexturePackageImporter.cs)，76–90 行；[ClientContentImporter.cs](../src/San9.Importer.San9PkTc101/Packages/ClientContentImporter.cs)，84–93 行。

剧本包虽先写 `.pending`，但发布时依次覆盖固定名称的数据文件，最后才换 `content.json`。中间任一步失败，已经替换的数据不会恢复。旧清单仍引用这些固定路径，因此“清单最后替换”不能保护旧包。

**已复现：** 先写入 `ScenarioId=first` 的包；再准备地图和剧本均为 `second` 的包。Windows 下用不允许删除共享的文件句柄打开 `scenario.json`，使第二次发布在替换它时失败。解除占用后重新读取，得到 `Map.ScenarioId=second`、`Scenario.ScenarioId=first`。正式组合包加载会因身份不一致而拒绝，旧可用包已被破坏。

同一发布缺口还存在于纹理和音频：图集使用稳定路径，`File.WriteAllBytes` 直接覆盖图片，完成后才发布纹理清单；音频先写回并删除不再保留的文件，随后才写剧本清单。转换取消、磁盘错误或其他写入失败可能使旧清单对应的产物变更或缺失。这部分为静态确认，未对私有素材执行故障注入。

**根因与整改：** 一致性单元是包，而不是某个 JSON 文件。应让已发布清单引用的文件在新包完成前保持不可变，例如在同一输出根内使用新一代资源路径并原子切换入口，成功后清理不再引用的旧资源；或采用有恢复协议的事务发布机制。仍可原位更新根目录，不要求长期保留完整副本。单纯再加一层 `try/catch` 不能恢复一致性。

**验收：** 在每个发布步骤注入失败／取消，旧包仍完整可加载，或恢复机制能确定性完成／撤销更新；图片、音频、数据与清单使用同一发布约束。

### A03 — 定向重建不能用单元来源戳为整包背书

**定位：** [TexturePackage.cs](../src/San9.Content/Packages/TexturePackage.cs)、[TexturePackageImporter.cs](../src/San9.Importer.San9PkTc101/Packages/TexturePackageImporter.cs)、[TexturePackageRebuilder.cs](../src/San9.Importer.San9PkTc101/Packages/TexturePackageRebuilder.cs)。

定向重建会更新包级 `Sources` 中本次读取的来源戳，但保留其他重建单元的旧产物。例如 `skin` 和 `library/g_common` 共享来源，来源变化后只重建 `skin`，旧 library 仍在，包级戳却已被更新；随后普通导入可能错误复用整包。

**审计纠正：** 大小与修改时间的复用策略是 [DEC-RENDER-0004](../specs/decisions/DEC-RENDER-0004-texture-atlases-and-gpu-cache.md) 已确认的取舍。同大小、同时间戳的替换不被自动识别属于该策略限制，撤回原报告要求改为内容摘要、仅时间戳变化不得转换的建议。本项仅处理局部重建错误推进整包来源记录。

**根因与整改：** 整包来源戳只能由整包转换更新。定向重建保留原戳与包级转换版本；来源实际变化时，后续普通导入仍能识别整包尚未更新，不增加哈希缓存或隐式扩大本次重建范围。

**验收：** 定向替换保留原包来源戳，不能把其他单元旧产物标记为已使用新来源。

### A04 — 编辑器将显示标签当作字段身份

**定位：** [WorldEditorDraft.cs](../src/San9.Application/Presentation/WorldEditorDraft.cs)，`EditorField` 第 9 行、`OfficerTacticFields` 65–69 行、`CityTacticFields` 71–79 行以及 `Fields` 146 行起。

字段没有机器标识。兵法分类通过 `fields.Single(f => f.Label == resolvedName)` 找回字段，城市兵法也用拼接后的标签找回。名称唯一性并不是内容契约；兵法之间可以同名，也可能与“武力”等常规字段重名。

**已复现：** 复用现有 `WorldEditorTests` 的有效夹具，仅把兵法 `tactic` 的显示文字改为“武力”；创建武将编辑草稿后调用 `OfficerTacticFields(1)`，抛出 `InvalidOperationException`。这会影响独立内容／Mod 的合法命名。

**根因与整改：** `EditorField` 应携带字段种类及引用对象 ID，标签仅负责显示。分类、选择和写入按类型／ID 关联；无需禁止重名或给文字追加隐藏后缀。其他文字驱动逻辑也应在同类功能修改时检查，例如 [InformationQuery.cs](../src/San9.Application/Presentation/InformationQuery.cs) 68–70 行按“最大”和标签去重，但本项的已复现结论仅限编辑器。

**验收：** 两个兵法同名、兵法与属性同名、修改翻译，都不会改变字段归属或写入目标。

### A05 — 未定义的终局原因可以进入权威存档状态

**定位：** [WorldInvariants.cs](../src/San9.Core/Simulation/WorldInvariants.cs)，145–156 行；[TerminalEvaluation.cs](../src/San9.Core/Simulation/TerminalEvaluation.cs)，16–18 行；[SessionValidation.cs](../src/San9.Application/SessionValidation.cs)，79–85 行。

终局不变量只要求原因非空；`TerminalEvaluation` 对任意非 `conquest` 字符串直接返回 `ReturnToTitle`；会话验证仅对 `time-limit` 额外检查年份。三处组合使未定义原因也能通过恢复。

**已复现：** 将有效快照的边界设为 `Terminal`、原因设为 `audit-unknown-reason`，用当前版本封套编码后交给 `SaveService.Decode`。恢复成功，终局查询返回该未知原因。

影响是错误／被编辑的存档可被当作有效的不可继续终局，严格恢复校验没有守住语义边界。本项不是要求存档防作弊或增加签名。

**根因与整改：** 以受约束的终局原因类型表达领域状态，在恢复边界拒绝未知值；按不同原因定义必要的不变量。涉及被历史事实锁定的结果时，保留事实，不能简单用当前世界重新计算来覆盖全部终局。

**验收：** 未知原因拒绝；合法征服、时间上限和玩家败北按各自条件恢复；不引入旧字符串的兼容分支。

### A06 — 内容集合元素缺少结构性非空校验

**定位：** [PackageFiles.cs](../src/San9.Content/Packages/PackageFiles.cs)，10–17 行；[ScenarioInitialState.cs](../src/San9.Content/Scenarios/ScenarioInitialState.cs)，93–98 行；[HeadlessHost.cs](../src/San9.Headless/HeadlessHost.cs)，439–450 行及第 58 行。

`RespectNullableAnnotations` 没有替集合元素执行非空检查。反序列化后的 `Officers` 可以包含 `null`，随后校验代码直接访问 `p.State.Id`。内容错误因此逃离 `ContentException` 分类，变成内部 `NullReferenceException`。

**已复现：** 先创建并验证正常剧本，序列化后只把 `officers[0]` 改成 `null`，交给 Headless `verify --scenario`。退出码为 8、报告为 `unexpected`，而非内容失败的退出码 3。

存档路径的 [StrictJson.cs](../src/San9.Application/Persistence/StrictJson.cs) 已显式拒绝 null 数组元素，但内容路径没有对应边界。不能因此简单复制存档的所有严格规则：内容字典和可空字段仍须遵守各自契约。

**根因与整改：** 在内容 DTO 进入语义校验前，校验不可空集合及其元素，并给出路径化错误；之后才验证引用和规则。不要通过捕获所有 `NullReferenceException` 把程序错误伪装成内容错误。

**验收：** 对人员、势力、资源等不可空集合的 null 元素明确拒绝，返回可定位的内容错误；允许的可空属性正常保留。

### A07 — 计算阶段内部异常被错误归类为会话忙碌

**定位：** [GameSession.Operations.cs](../src/San9.Application/GameSession.Operations.cs)，56–173 行，尤其第 173 行。

`CalculateOperation` 的同一个 `try` 覆盖命令执行、自动回答、AI 恢复、年表生成、快照投影和战略提示；末尾把所有 `InvalidOperationException` 变为 `SessionRejection.NotStable`。`Single()` 的数据错误、投影不变量失败等也会使用这种异常。

隔离分支能防止这些未提交状态污染当前世界，但异常原文和栈丢失。真正的算法或内容缺陷会表现为“当前阶段不可操作”，让用户重试相同输入，开发者也缺少诊断依据。本项为静态确认，不声称正常测试场景已经触发此路径。

**根因与整改：** 可预期的阶段拒绝应在入口显式判断或用明确结果类型返回；不可预期的计算异常保留诊断并进入故障处理。不能把整个计算过程的通用异常作为业务分支。此问题发生在提交前，与 A01 的提交后通知失败不同。

**验收：** 不稳定输入返回 `NotStable`；在 AI／投影中注入内部异常时保留原始错误且不提交状态；同步和后台路径一致。

### A08 — 预览读取只限制结构位置，没有限制输入字节数

**定位：** [SlotSaveStore.cs](../src/San9.Client.Godot/Persistence/SlotSaveStore.cs)，117–146 行，调用入口为 `ReadCatalog`／`ReadAutoSaveCatalog`。

预览从 1 KiB 缓冲开始，未读到完整头部就倍增，没有头部大小上限。损坏文件若以 `header` 开头并含有很长的未闭合字符串，可以让“读取目录预览”持续读入整个文件并增长缓冲。极大但合法的备注也没有在这个读取边界受限。

正常文件确实无需读取完整 save 正文；问题在于这不等于内存使用有上界。现有长备注预览测试证明可变头部受支持，没有建立损坏输入的资源预算。本轮没有进行 OOM 或巨大文件压力实验，不能据此报告实际峰值。

**整改：** 为预览头部定义独立字节／字段长度预算，超限将该槽位标记为格式错误并继续列举其他槽位；写入端与读取端采用同一约束。不要给整个正常存档套用头部的小尺寸限制。

**验收：** 超长／未闭合头部在预算内终止，正常中文备注与大型存档正文不受影响，其他槽位仍可显示。

### A09 — HUD 的文件拆分没有消除共享可变状态耦合

**定位：** [StrategyHud.cs](../src/San9.Client.Godot/UI/Strategy/StrategyHud.cs) 及同目录 `StrategyHud.*.cs`；[SessionUiHost.cs](../src/San9.Client.Godot/UI/Strategy/SessionUiHost.cs)，`CompleteSwitch`；[StrategyHud.Decisions.cs](../src/San9.Client.Godot/UI/Strategy/StrategyHud.Decisions.cs)。

`UI/Strategy` 下该 partial 类型共有 16 个文件、1,864 行。它同时负责布局、地图输入阻挡、命令草稿、目标选择、保存／设置入口、决策和结果演出、资源租约以及退出清理。`SessionUiHost` 还要通过多个委托读写这一组合对象的状态。

问题不在行数，而在共享状态和生命周期没有相应边界：新增一条交互流程时，必须同时理解输入门禁、当前面板、呈现推进和清理路径；`partial` 仅分开文件，不能限制相互访问。已有 `TurnPresentation`、`StrategyOverlayHost`、草稿类和租约机制是可继续利用的分离点，不应被另起一套框架替换。

**建议：** 按实际工作流逐步让命令、决策／结果呈现各自拥有草稿与生命周期；HUD 留下布局和组合。统一暴露工作流的占用／完成状态，由一个宿主决定输入门禁。只在相应功能变更时抽离，不为审计一次性重写整个 UI。

**验收方向：** 新工作流无需访问其他流程私有字段；关闭、切换会话、异常重试有单一清理入口；保留实际交互验收，不以编译通过代替。

### A10 — 原版地图显示假设分散在共享契约和算法中

**定位：** [MapPackage.cs](../src/San9.Content/Maps/MapPackage.cs)，77–78 行；[MapGeometry.cs](../src/San9.Rendering/MapGeometry.cs)，7–24 行；[MapCameraBoundary.cs](../src/San9.Rendering/MapCameraBoundary.cs)，`Clamp` 与 `Perimeter`。

地图契约虽携带宽高和画布尺寸，校验却固定接受 `200×200`／`13056×6656`；投影、反投影、相机索引和周界遍历分别写死 200／199、32／16、96／112 等常数。Core 的 `BattlefieldMap` 则已经按传入尺寸建立领域拓扑。

这是当前显示范围的明确限制，不能把它描述成现有 200×200 原版地图运行错误。但将来支持独立地图时，只放开内容校验会使后续索引和坐标计算仍按旧尺寸工作，改动需要跨多处同步，存在隐含耦合。

**建议：** 若进入自创地图支持阶段，由内容定义地图显示度量并构造共享投影对象；坐标转换、相机边界和渲染共同消费它。若暂不扩展尺寸，至少集中表达当前支持的显示度量，避免分散复制。此项不要求现在承诺任意地图尺寸。

**验收方向：** 支持范围内正反投影和边界共享同一套参数；未来加入非方形／非 200 地图时，不依赖修改多组常量。

## 验证结果与反例复现

所有命令均在隔离的基线 worktree 中运行：`local/generated/code-audit-f988e09/`。产品源码保持基线未改，反例仅添加到该隔离目录的现有测试工程，没有创建额外审计工程，也未加入主工作区测试集。

| 检查 | 实际结果 |
| --- | --- |
| `dotnet test San9Remake.slnx --nologo` | 5,786 通过，0 失败，0 跳过；Core 2,911，Application 1,775，Content 128，Importer 475，Client 497 |
| `dotnet test tests/San9.Headless.Tests/San9.Headless.Tests.csproj --nologo` | 39 通过，0 失败，0 跳过 |
| `dotnet build src/San9.Tools/San9.Tools.csproj --nologo --no-restore` | 成功，0 警告、0 错误 |
| A01／A05 的 Application 定向反例 | 2 项通过，断言的是报告中的错误行为确实存在 |
| A02 的 Content 发布故障反例 | 1 项通过；Windows 文件共享占用触发替换失败 |
| A04 的编辑器名称碰撞反例 | 1 项通过 |
| A06 的 Headless null 元素反例 | 1 项通过 |

“反例通过”不表示已修复。五个反例分别执行以下步骤，可用基线已有夹具重建：

1. **A01：** `G01ScenarioTests.Fixture()` → `GameSession.Start` → 添加抛异常的 `Changed` 订阅者 → 提交年表操作 → 断言调用抛异常、序号增加、记录已存在、同请求重试为 `AlreadyProcessed`。
2. **A02：** `ScenarioContentFilesTests.Fixture()` → 写旧包 → 将地图与剧本 ID 同时改为 `second` → 用 `FileShare.Read` 持有 `scenario.json` → 写新包并观察 `UnauthorizedAccessException` → 释放句柄后加载，断言地图新 ID、剧本旧 ID。
3. **A04：** `WorldEditorTests.Fixture()`／`Texts()` → 将 `texts.Entries["tactic"]` 设为“武力” → 创建武将草稿 → `OfficerTacticFields(1)` 抛 `InvalidOperationException`。
4. **A05：** 有效 G01 会话 `Capture()` → 只将 `World.Simulation.Boundary` 设为 `Terminal`、`TerminalReason` 设为未知非空字符串 → 当前版本 `SaveEnvelope`／`CanonicalJson.Encode` → `SaveService.Decode` 接受 → 终局查询保留未知原因。
5. **A06：** 有效的单势力、单城市、单武将及根军团剧本先执行 `Validate()` → 序列化后把 `officers[0]` 改为 JSON null → Headless `verify --scenario <文件> --output <独立目录>` → 退出码 8、`unexpected`。

本地证据日志为隔离目录内的 `audit-tests.log`、`audit-headless.log`、`audit-tools-build.log` 以及 `audit-probes-application.log`、`audit-probes-content.log`、`audit-probes-editor.log`、`audit-probes-headless.log`。这些属于忽略的审计产物；报告不依赖提交日志文件。定向探针名含 `AuditEvidence`，可以在对应现有测试工程用 `--filter FullyQualifiedName~AuditEvidence` 运行。普通全量测试在加入探针前执行，未把探针计入 5,825 项基线测试。

## 整改顺序与交付边界

1. 先处理 A01、A02，分别明确会话完成结果与包发布的一致性单元；这两项涉及数据／状态正确性。
2. 处理 A03–A07，以输入身份、字段身份、终局类型和错误分类作为边界；补入针对触发条件的回归测试，不扩成全仓重构。
3. A08 补齐读取预算；A09、A10 随相关功能演进处理，避免为消除表面代码规模而增加抽象层。

审计阶段仅交付文档；后续按用户要求完成的第二轮代码整改见文末。动态画面、发行裁剪配置、真实数据规模下性能，以及未深入验证的玩法分支仍属于未决覆盖，不能从现有测试通过推导为已验收。

## 第二轮：Core 与 Application 深入审计

本轮仍以同一提交为基线，新增 A11–A15，不重复计入 A01、A04、A05、A07。除正确性与代码质量外，检查了事务与派生状态的所有权、领域事件契约、确定性边界、长局内存分配、数据规模扩展成本、规则组装，以及测试遗漏的故障类型。未修改产品代码。

### A11 — 投降解散部队被领域事件默认标为战败

**定位：** [SurrenderMerge.cs](../src/San9.Core/Simulation/Diplomacy/SurrenderMerge.cs)，89–90 行；[UnitTerminationService.cs](../src/San9.Core/Simulation/Military/UnitTerminationService.cs)，5–11 行；[ProgressMessages.cs](../src/San9.Application/Presentation/ProgressMessages.cs)，84–86 行。

投降合并时，如果原部队无人继续加入，`SurrenderMerge` 解散该部队并发出 `UnitTerminatedEvent`，但没有填写 `Reason`。枚举默认值为 `Defeated`；`JsonRequired` 只约束反序列化时字段存在，不要求 C# 构造时显式赋值。因此，成功投降后的解散在领域输出中被声明为战败。

**已复现：** 复用 `SurrenderDiplomacyTests.AcceptanceAtomicallyMergesCorpsAssetsPeopleAndDetentionAndRestoresExactly` 的有部队／无人加入场景，正常提交劝降并接受。产生 `ForceSurrenderedEvent`，部队正确移除，但对应 `UnitTerminatedEvent.Reason == Defeated`。原测试只检查终止事件存在，未检查原因；补入观察断言后通过。

**影响与边界：** 当前 `ProgressMessages` 已通过扫描同一批次中相同势力、相同日期的投降事件来排除“部队损失”消息，因此本项不声称当前界面必然误报。问题是事件本身语义错误，消费者必须依赖批次完整性和其他事件来修正含义；未来事件筛选、统计、诊断与分段呈现容易产生不一致。这种补充判断也让 Application 承担了本应由 Core 给出的终止原因。

**整改：** 由 Core 在终止发生时明确区分战败、士气崩溃、势力灭亡及投降解散等实际原因；原因在构造时必须显式提供。统一定义资源处置与终止事件的责任，避免各条解散路径各自构造不完整事件。Application 按明确原因显示，不再靠邻近事件猜测。

**验收：** 投降解散、战败、士气崩溃、势力灭亡分别断言原因、资源去向及目标身份；单独消费终止事件时也不会把投降解散判断成战败。

### A12 — 交战缓存遗漏设施兵法这一依赖

**定位：** [WorldMutation.cs](../src/San9.Core/Simulation/WorldMutation.cs)，46–53、237–253 行；[MilitaryRulebook.cs](../src/San9.Core/Domain/Military/MilitaryRulebook.cs)，358–380 行；[MilitaryEngagements.cs](../src/San9.Core/Simulation/Combat/MilitaryEngagements.cs)，`FindFacilityTarget`。

设施可攻击目标取决于 `FacilityAttack`，其射程由 `EnabledTacticIds` 中的兵法修正。`WorldMutation.Replace(SettlementState)` 手工比较一组字段以清除 `_engagements`，却未包含 `EnabledTacticIds`。同一事务中先读取交战关系，再修改兵法，后续交战查询会保留旧射程下的目标。

**已复现：** 使用 `FacilityAttackTests` 规则，在基础射程 1 外、加成后射程 3 内放置敌军。首次查询没有目标；替换据点状态，启用增加 2 射程的兵法。此时 `FacilityAttack` 返回射程 3，缓存查询仍无目标，而直接 `MilitaryEngagements.Build` 已找到目标。

**证据边界与优先级：** 这是已确认的内部缓存一致性缺口，但目前生产代码主要由世界编辑操作改变这组兵法，而编辑操作建立新事务；尚未证明其会在既有玩家流程中先构建再修改该缓存。因此列为 P3，不把事务级反例扩大成已发生的实战错误。

**整改：** 明确交战关系依赖的状态投影及失效规则，使查询所读依赖与 mutation 的变更分类有统一维护位置。不能只追加一个字段后就假定其他依赖已经完整，也不宜为规避依赖管理而在每次查询重建全图。

**验收：** 针对真正影响目标、射程、敌对关系与占地的变化，验证“先查询→修改→再次查询”与冷计算一致；测试比较语义结果，不固定缓存对象的实现细节。

### A13 — 历史去重仍产生与历史长度成比例的分配

**定位：** [ChronicleHistory.cs](../src/San9.Application/ChronicleHistory.cs)，28–45 行；[GameSession.Operations.cs](../src/San9.Application/GameSession.Operations.cs)，154 行；[ChroniclePresentation.cs](../src/San9.Application/Presentation/ChroniclePresentation.cs)，18–22 行。

有新增候选事实时，`Append` 在确认候选是否全部重复之前就调用 `history.ToBuilder()`，结束时再 `ToImmutable()`。因此，即使所有候选被去重，内容完全没变化，仍返回另一份全量数组。正常追加同样要复制已有历史；各势力共享一个不断增长的历史数组。

**实测：** 在 .NET 10 Debug 测试进程中，先预热，再以 `GC.GetAllocatedBytesForCurrentThread()` 只测一次 `Append`：1,000 条历史、追加一个与末项完全相同的事实，分配 **16,880 字节**；100,000 条时分配 **1,600,880 字节**。两次结果长度都不变，但数组身份改变。大输入由重复同一事实构造，仅用于隔离复制成本，不表示真实剧本会生成该分布。

**影响与边界：** 单次追加有 O(H) 历史复制成本，固定小批次持续追加时，累计复制量可达 O(H²)。年表打开时还会筛选、渲染并排序完整历史。这里确认的是规模增长特征和分配量，没有测出具体玩家卡顿，也未设定未经需求确认的历史条数上限；不要以删除历史的方式替代结构改进。

**整改：** 全部重复时保持原历史；需要保留完整历史时，评估不可变分块或持久化追加结构，让会话分支共享旧块。存档边界可以按协议展平，展示按势力和可见区间读取。是否改变存储格式由实际规模预算决定，不为优化引入另一套权威历史。

**验收：** 无变化追加不复制已有历史；使用增长序列测追加与快照分配，而不只测最终结果；真实长局样本补充保存、恢复及年表打开耗时，再决定进一步优化范围。

### A14 — 新事务存在与规则实际访问对象无关的全量扫描

**定位：** [WorldMutation.cs](../src/San9.Core/Simulation/WorldMutation.cs)，112、154–157 行；[SimulationEngine.cs](../src/San9.Core/Simulation/SimulationEngine.cs)，381–384、503–512 行；[OriginalAiTurnRunner.cs](../src/San9.Application/AI/OriginalAiTurnRunner.cs)，139–140 行。

世界状态使用不可变集合，`Fork` 可以共享根，但每次 `BeginMutation` 的构造函数都会调用 `RebuildActiveRecords`，扫描完整势力、据点和武将集合以排除 dormant 对象。即使没有任何 dormant 对象、调用仅查询一次粮食报价，也先支付这三个集合的遍历成本。`Validate`／`Execute` 的事务及 AI 粮食交易候选报价都会进入这些路径。

**影响：** 单个事务的前置成本至少为 O(F + S + P)；K 次独立候选报价的这部分成本为 O(K × (F + S + P))。支持更多自创／Mod 武将时，未参与该命令的对象也增加开销。不能把不可变根共享等同于所有事务初始化都是常数成本。

**证据边界：** 遍历路径和复杂度已经静态确认；没有在真实资源包上测量其占总 AI 耗时的比例，也未声称它是当前最大瓶颈。原有 preparation revision 缓存已经减少部分查询开销，但 `GetFoodTradeQuote` 直接建事务，不走该缓存。

**整改：** 活跃索引由世界版本持有并随激活／休眠变化更新，事务继承相同版本的索引；或为真正只读的报价建立可复用查询上下文。索引仍是权威状态的派生结果，必须明确失效与恢复行为，不能复制成另一套可独立修改的状态。

**验收：** 在对象数与候选数分别增长的矩阵中测量事务初始化和报价；同时验证激活、撤离、势力灭亡及存档恢复后索引与完整注册表一致。

### A15 — 历史事件配置组装使新增规则横跨多个中心文件

**定位：** [SimulationEngine.cs](../src/San9.Core/Simulation/SimulationEngine.cs)，43–49、80–111 行；[ResolutionPipeline.cs](../src/San9.Core/Simulation/ResolutionPipeline.cs)，21–40 行及阶段调度；[ScenarioRules.cs](../src/San9.Application/ScenarioRules.cs)，38–42 行。

Core 已有内部 `SimulationRules` 配置对象，但公开引擎构造函数仍将十种具名历史事件配置逐一列为可选参数，Application 再通过一条长位置参数调用逐一转发；引擎构造体分别校验各事件引用，阶段调度再具名调用。新增同类事件需要同步事件定义、规则容器、构造签名、Application 转发、校验与调度等接点。

**影响与边界：** 独立事件的扩展会修改核心组装入口，维护者需要同时理解与其无关的其他事件参数及初始化顺序。类型不同可以捕获部分错传，却不能证明遗漏的可选配置、初始化和校验已经接齐。这里不把具体三国历史规则本身视为错误，也不要求将规则改成动态脚本；编译式规则与确定的阶段顺序是合理边界。

**整改：** 让公开入口接收有明确职责的不可变规则配置，将场景转换与 Core 校验的责任集中；同类历史事件的配置与校验按模块组织。只有实际新增事件反复要求同类接线时，再抽取受控模块注册；保留显式阶段顺序，避免设计一个全能事件框架。

**验收：** 选一个同类事件作为扩展样例，确认不再新增跨层位置参数、遗漏必需定义会在场景加载时失败；共享配置的 Fork 不得引入可变对象或不确定的回调顺序。

### 本轮覆盖与未升级为问题的观察

| 维度 | 已检查与结论边界 |
| --- | --- |
| 状态所有权与恢复 | 检查世界根共享、mutation、会话计算／发布、后台句柄、快照与存档验证。A01、A05、A07 仍适用，不重复编号。后台结果必须由调用方显式完成，未据此宣称当前客户端存在锁死。 |
| 确定性与身份 | 检查命令序号、随机流、AI 恢复状态、排序及路线缓存。旧序号返回 AlreadyProcessed 是已有明确协议，不将“不比较历史请求正文”列为新缺陷；未要求录像或完整重放。 |
| 领域规则与事件 | 深入投降合并、人事迁移／任务取消、部队终止、战利品分配、设施攻击与交战关系。形成 A11、A12；其余疑点没有足够证据，未列为缺陷。 |
| 规模与长局 | 检查历史存储、事务初始化、查询投影和 AI 候选循环，形成 A13、A14。投影还有按势力遍历军团、按城市查势力的成本，但未单独夸大成性能事故。 |
| 扩展与内容作者边界 | 检查规则配置、编辑器、任务分派及历史事件组装，形成 A15；字段显示名承担身份仍归 A04。 |
| 测试有效性 | 区分状态终值、事件语义、热缓存失效与规模增长。现有测试覆盖正常行为较多，但“冷缓存正确”“事件存在”“数组内容相同”分别不能证明上述三个边界正确。 |

### 本轮验证与整改顺序

产品提交、输入及配置未变，沿用上一轮全量检查结果，未重复运行全套测试。隔离 worktree 中本轮有 **5 个定向测试用例通过**：投降场景原理论测试 3 例（其中空部队场景新增原因断言）、交战缓存探针 1 例、历史分配探针 1 例。断言的是当前问题及成本确实存在，不表示已修复。

交战探针首次构造夹具时遗漏根军团，被世界不变量正确拒绝；补全夹具后通过。这个夹具错误不计为产品缺陷，也没有因此重复运行已经通过的投降用例。证据文件为隔离目录内 `audit-core-round2.log`（含首次夹具失败与投降 3 例通过）、`audit-core-cache-round2.log`、`audit-app-round2.log`；分配数据在 Application 测试输出目录的 `audit-history-allocation.txt`。没有把临时探针写入主工作区。

后续优先完善 A11 的事件语义，并在修改交战／设施规则时处理 A12 的依赖所有权。A13、A14 先据真实长局与扩展规模建立预算，再选择数据结构；A15 随下一次历史事件扩展收敛组装接口。此前 A01、A02 的一致性问题仍优先于本轮 P3 改进。本轮未执行 Godot 视觉验收、原版规则重新考证或真实资源性能压测。

## 第二轮整改结果

本节记录用户要求修复后的当前工作区状态，取代上一节对 A11–A15 的待办安排。

| 项目 | 已完成的根因修复 | 回归依据 |
| --- | --- | --- |
| A11 | `UnitTerminatedEvent.Reason` 必须在构造时明确提供；增加 `Surrendered`。投降解散直接给出该原因，消息投影不再扫描同批投降事件补充判断。 | 劝降合并的空部队分支检查原因与身份；投降解散事件单独消费不报战败，同批真实战败仍报告；战败、士气崩溃与灭亡遣散各有原因断言。 |
| A12 | `MilitaryEngagements` 集中维护据点、部队的交战依赖变化判断，包含设施兵法；mutation 按该判断失效缓存。部队判断使用实际生效命令、发起攻击资格及占地。 | 增加兵法启用／撤销的热缓存反例；已有移动、目标、外交、士气、兵力、混乱、出发等待、设施与恢复测试通过。 |
| A13 | 历史改用 `ImmutableList` 持久化树，追加共享旧节点，完全重复时返回原根；会话分支、检查点和存档快照共享该根。JSON 仍是原顺序数组。 | 1,000／100,000 条历史追加分配、去重身份、旧检查点不变、JSON 数组等价及会话保存恢复验证。 |
| A14 | 活跃势力／据点／武将索引由已提交世界版本持有，事务和 Fork 共享；单对象变更按身份更新索引，激活变化失效相关查询；创建和外部恢复时从完整集合重建。 | dormant 激活／撤离、分支隔离、提交与恢复测试；对象数×候选数矩阵确认初始化分配不随世界规模增长。 |
| A15 | `SimulationRules` 成为公开、仅初始化赋值的规则配置；Application 编译一次具名配置并复用；删除十个具名历史事件构造参数，事件引用校验归入规则模块。 | 应用层实际入口使用配置构造；错误历史事件引用在初始化前拒绝；合法事件触发、Fork、既有历史事件和确定性测试通过。所有引擎入口统一校验官职／宝物引用。 |

性能探针为 .NET 10 Debug、小型合成输入，测量区间不含夹具构造，不代表真实游戏整旬耗时：

- 历史新增一条：1,000 条历史分配 **1,104 字节**；100,000 条分配 **1,440 字节**。完全重复的批次保持原集合身份，不再复制全历史。去重仍可能向前查找相关势力的末项；完整年表渲染和文件序列化仍需遍历对应数据，本次没有裁剪历史或声称这些操作为常数成本。
- 含一半 dormant 武将的世界：100 与 10,000 名武将两种规模，创建 100 个候选事务均分配 **35,200 字节**，1,000 个均分配 **352,000 字节**，即每个事务 **352 字节**。该矩阵仅覆盖事务初始化，报价自身的规则计算仍有其成本。

**验证：** 在当前主工作区运行 `dotnet test San9Remake.slnx --no-restore --nologo`，产品代码构建成功；首轮 5,796 项通过，4 项因测试夹具缺少官职／宝物定义失败。补全夹具、保持统一校验后，4 项全部定向复测通过。后续增加／调整的历史规模、索引规模、规则引用与终止原因用例均定向通过；最终覆盖 Core 2,919、Application 1,784、Content 129、Importer 475、Client 499，共 5,806 个用例，无未解决失败。该计数合并全量与受影响项复测结果，不表示再次全量运行。另运行独立 Headless 测试，**39 项全部通过**。文档引用与 `git diff --check` 通过。

本次验证日志在主工作区 `local/generated/audit-round2-tests.log`、`audit-round2-scaling.log`、`audit-round2-application-final.log`、`audit-round2-core-final.log`、`audit-round2-headless.log`。用户已有的结局展示等未提交改动保留；验证阶段未做内容转换或 Godot 画面验收，后续提交见下节。存档 JSON 结构与规则状态语义不变，未增加旧格式兼容路径。

## 第一轮后续整改结果

第二轮 A11–A15 已提交，提交为 `1cc513e3`、`2ae0493a`、`a8b2a0cc`、`d4a559fc`、`b4a2e635`。随后继续处理第一轮问题，曾按用户要求暂停；恢复后补充 A09 的工作流边界整改。下列代码与测试已按逻辑分组提交：`6217a113`（会话操作）、`321393ae`（终局原因）、`ee604703`（编辑器身份）、`8a8c3057`（预览预算）、`c29c2d99`（内容结构校验）、`9722faea`（包发布与来源记录）、`6b0cca16`（地图显示度量）、`b11e5ccf`（人事工作流）。原有结局展示等改动未纳入这些提交。

| 项目 | 当前结果 |
| --- | --- |
| A01 | 删除会话提交后的外部 `Changed` 回调。信息面板按已提交操作序号刷新，后台计算仍由所有者显式完成；UI 刷新不再介入操作结果。 |
| A02 | 剧本数据写入独立生成目录，最后替换 `content.json`；失败清理本次数据，不覆盖旧清单引用。贴图打包为每次生成选择独立路径；音频同样先写新路径、发布清单后清理旧资源。锁住清单导致发布失败时，旧包全文件及加载结果保持；连续重建同一贴图单元不会覆盖前一代像素。 |
| A03 | 定向重建保留整包来源戳；撤回与既有决策冲突的内容哈希建议。 |
| A04 | `EditorField.TacticId` 承担战法身份，显示名称只用于展示。武将字段与基础属性重名、城市战法相互重名均可正常操作。 |
| A05 | 领域终局校验只接受 `conquest`、`time-limit`、`player-defeated`。未知原因经存档入口返回 `InvalidState`；测试中的虚构终局原因改为已定义原因。 |
| A06 | 内容入口增加结构校验，按声明的可空性检查属性和集合元素，错误携带字段路径；保留合法可空元素，未初始化不可变数组也归为内容错误。Headless 的 null 人物元素反例返回退出码 3／`content`。 |
| A07 | 结束控制、逐日推进在入口显式检查阶段条件；删除捕获整个计算过程 `InvalidOperationException` 的分支。内部异常保留诊断，失败分支不发布，操作 guard 释放。 |
| A08 | 预览读取和标准编码共用 64 KiB 头部预算；超限槽位独立报错，后续槽位仍可浏览，正文不计入头部预算。完整载入先检查同一预算。 |
| A09 | 按逐步拆分建议，将人事命令抽为 `PersonnelCommandFlow`，独立持有窗口、草稿、建议选择及地图目标回调；HUD 仅读取工作流状态、转发操作和连接现有演出。关闭、草稿失效、提交异常、节点退出共用释放路径，释放时不覆盖其他流程已接管的回调。其他工作流继续按相关功能演进拆分，不进行全 HUD 重写。 |
| A10 | 将当前支持的地图尺寸、画布、投影及高差常数集中到 `MapDisplayMetrics`，地图校验、坐标投影和相机边界共用。仍只支持当前显示规格，没有扩成任意尺寸地图。 |

**最终验证：** `dotnet test San9Remake.slnx --no-restore` 全部通过：Core 2,919、Application 1,786、Content 131、Importer 475、Client 500，共 **5,811** 项；实际 Godot 项目构建通过。另运行 `dotnet test tests/San9.Headless.Tests/San9.Headless.Tests.csproj --no-restore`，**40** 项通过。首次测试的两个默认不可变数组错误分类失败已修复；Windows 清单占用测试改为接受实际的 IO／访问拒绝异常，最终全量覆盖通过。日志位于 `local/generated/audit-remaining-final.log` 与 `audit-remaining-headless.log`。

没有执行私有资源整包转换、Godot 画面验收或真实资源性能压测。发布保证以单个剧本包或纹理包为单位，不支持并发写入同一输出目录及运行中热更新；进程中断可能留下未引用资源，不能据此声称具备崩溃后的垃圾回收。原有结局展示等用户改动继续保留。

### A09 恢复后的验证与边界

[PersonnelCommandFlow](../src/San9.Client.Godot/UI/Strategy/PersonnelCommandFlow.cs) 不访问 HUD 或其他命令的私有字段。状态由工作流持有，现有 `StrategyOverlayHost` 继续持有窗口的输入锁，HUD 统一使用 `Stage`、`IsTargeting`、`Source` 决定门禁、目标栏和地图提示。窗口不再自行检查草稿失效，统一由工作流控制；准备窗口显示异常、提交拒绝或异常均释放当前窗口和目标选择。Overlay 先退出或工作流先退出都能释放引用，清理只撤销本流程安装的地图回调。

本次只修改客户端职责与生命周期，运行 `dotnet test tests/San9.Client.Godot.Tests/San9.Client.Godot.Tests.csproj --no-restore`，实际 Godot 工程编译成功、**500 项通过**；`git diff --check` 通过。日志为 `local/generated/audit-hud-workflow.log`。复用此前未变的跨层及 Headless 检查，不重复全量执行。现有单测不覆盖 Godot 节点退出顺序及人工点击，尚未执行人事窗口、地图选点、取消和会话切换的实际交互验收；这里不将自动测试结果称为画面／交互通过。此次落实了审计建议的一个独立工作流边界，未声称全部 HUD 职责已经拆完。

本报告继续保留，作为已修复项的证据索引与 A09 后续职责演进、实际交互验收的入口；前文基线问题不应直接当作当前版本仍存在的缺陷。
