# 客户端内容管线

本页维护内容转换、契约与校验入口，不累计界面实现说明。界面开发统一从[UI 文档](ui/README.md)进入；地图与运行态见[地图文档](map-import.md)和[运行时架构](runtime-state.md)。

## 现行版本与历史记录

现行契约以代码校验为准：剧本 [ScenarioInitialState](../src/San9.Content/Scenarios/ScenarioInitialState.cs) 为 124，组合包 [ScenarioContent](../src/San9.Content/Packages/ScenarioContent.cs) 为 128，界面皮肤 [InterfaceSkin](../src/San9.Content/Interface/InterfaceSkin.cs) 为 24，纹理转换 [TexturePackageImporter](../src/San9.Importer.San9PkTc101/Packages/TexturePackageImporter.cs) 为 22。规则为 `san9-core-g10-v74`，保存90／日志94／录像6；协议以 [SessionProtocol](../src/San9.Application/SessionProtocol.cs) 为准。

旧内容包按现行契约原位重转，旧规则存档、日志和录像拒绝加载。是否重转纹理取决于纹理转换版本与源输入。历史验收只证明对应基线，不代表当前版本重新验证；阶段收口见[G10 验收](g10-verification.md#2026-09-21阶段收口)。

开局初始化见[开局专项](scenario-startup.md)，军事状态与命令见[移动规格](../specs/behavior/BEH-WAR-0001-movement-and-command-lifecycle.md)。

## 职责与文件

| 模块 | 职责 | 输出 |
| --- | --- | --- |
| Content.Maps / MapImporter | 当前地图方格、地貌及对象位置 | `map.json`（无图片文件） |
| Content.Scenarios / ScenarioContentCompiler | 剧本初始状态及军事规则拓扑 | `scenario.json`，schema124 |
| Content.Text / ScenarioTextReader | 名称与全量消息目录 | `text.json` / `messages.json`，schema3 |
| Content.Interface / InterfaceSkinCompiler | 固定界面角色、颜色、装饰资源 | `textures.json.skin`，schema24 |
| Content.Packages / ClientContentImporter | 剧本、文本、资源 ID 和依赖校验 | `content.json`，schema128 |
| Content.Packages / TexturePackageImporter | 与剧本无关的完整纹理、图集及地图资源定义 | 独立 `textures.json`，schema5／转换版本22 |
| Importer.Containers.Ls11Archive | LS11容器解码，不承担名称或地图语义 | 不直接输出文件 |

共享剧本契约不按原版编号区间限制城市或人物身份。城市图标资格由`SettlementKind.City`决定；`RegionCityIds`必须引用真实城市，其城市名从据点定义读取，不要求城市ID同时存在于地域名称表。数量与名称不继承源格式槽位和字节长度；玩法数值上限及显示资源范围独立校验。

初始联盟使用必填可空`Alliance`：目标、盟主、排序后的成员身份集合与剩余旬数。Importer把源联盟旗标及头部期限转换为同一个语义对象，缺失成员或矛盾状态明确拒绝。190年开局目标5、盟主7、13个成员及36旬完整保留；运行态从初始值独立演进，快照查询当前联盟，恢复不重新导入或重新扣旬。

阵型以属性表达计略减半与扰乱免疫。版本Importer将方圆和不可扰乱阵型转换为属性，Core不比较源编号。任务保存野战目的地细坐标、地域身份和类型化计略载荷；去返程快照使用同一坐标尺度，旧任务布局明确拒绝。

地理契约包含必填`CityProvinceIds`和`MilitaryDefinition.TerritoryExits`。前者是城市到州的基础分组，可不为某城市指定州，不继承15州上限；后者以行优先顺序保存每格北、东、南、西出口语义，与现有地域、地形及完全阻断格共同推导地域边。非空出口数组必须覆盖整幅地图，空数组明确表示未提供地域邻接。作者不维护逐势力邻接或成员候选表；Core只缓存不可变地图边，按当前据点位置、归属、存续及港口属性生成资格。城市州分组进入内容指纹并由会话独立持有。190年包已原位更新并逐项核对50城、40000格及94据点；具体数据链和736条有向边见G08验证记录。

地图内容不引用名称、势力摘要或皮肤。剧本和文本可独立于地图校验；组合层检查建筑稳定ID与据点一一对应、坐标一致以及文本键存在。`SourceText`保存繁体显示原文（设施已组合类型后缀）；基础地域名另有文本项。它不是原消息字节的替代归档，原文件以SHA-256追踪。

共享纹理默认位于 `local/generated/content/textures-san9pk-tc101`。剧本包只引用共享包的目录和资源 ID，没有 `images` 目录。43个已确认非头像档案及836张正常色大头像转换为最终图片；未确认项只记录研究边界，不保留索引图和原始调色板副本。具体覆盖与透明规则见[纹理资源](texture-resources.md)。

图片契约、静态图集、GPU缓存和资源租约见[纹理资源文档](texture-resources.md)。头像上传后不保留CPU像素，窗口与地图按实际使用异步申请。

清单保留缩进；五个数据文件使用紧凑 JSON，保留完整字段名和内容。仅省略消息表达式及参数中默认值为 null 的可选字段，必填 nullable 状态字段仍显式保存。数据子文件必须位于包内，共享纹理目录允许相对引用包外目录。导入先校验、暂存全部文件，再替换数据并最后发布清单；不支持导入过程中并发读取。

`MilitaryContentCompiler`将完整地形、硬阻挡、阵型移动预算和据点footprint导入剧本，Core直接消费该规范化规则，不读取`map.json`的显示资源。G06增加普通战斗参数、阵位、据点销毁能力及名马标记；部队成员、伤兵和当前命令随世界查询变化。数据核对见[G05](g05-verification.md)与[G06验收](g06-verification.md)，完整兵法、设施建设及水陆转换见[G07验收](g07-verification.md)。

## 来源边界与统一内容

基础版与 PK 追加是原作的来源组织方式，不作为重制版的内容分类。此约定适用于人物、头像、宝物、剧本、建筑、地图、文本、界面和其他内容，不限于显示资源。

- 版本 Importer 处理源文件分组、编号分段、记录布局及版本识别，将支持范围内的数据合并为各自类别的统一定义与稳定 ID 引用。源 ID 可直接保留时不为统一而重新编号；有冲突时在导入阶段明确映射并闭合引用。
- Content、Core、Application、Rendering 和客户端不因内容来自基础版或 PK 而建立两套集合、加载路径或行为分支，不添加仅表达来源分组的 `isPk` 等产品字段。
- 来源文件、版本和原始编号可保留在导入追踪信息与研究记录中，不用于决定内容的可见性或可用性。对象类别、能力、规则和剧本初始状态等实际产品语义仍须明确表达；统一来源不等于抹平玩法差异或把不同剧本的初始状态混合。
- 当前支持范围和缺失内容仍须如实标明。此约定不代表已完成全部 PK 内容导入，也不改变已选定的原作版本基线。后续 Mod 同样转换为统一契约，源格式差异留在导入阶段。

TC101剧本导入以源坐标识别地域配置：双坐标均为无效哨兵的槽不生成地域、名称文本或城市归属映射；单轴无效或越界坐标直接报错。有效但尚未建造的地域完整保留，稳定ID不重排。人物和宝物引用由转换后的契约校验，不能通过丢弃引用对象掩盖错误。

## 接入前的数据核对

新增运行时对象或改变导入范围、对象筛选边界时，明确支持的版本与内容范围，并核对以下受影响层次；区分有效对象、未启用槽和未支持扩展。

| 层次 | 核对内容 |
| --- | --- |
| 原版源数据 | 定义和槽位数量、稳定编号、状态分布、持有人/地点等引用，以及完整范围的文本和图像来源 |
| 转换包 | 定义与资源是否完整，初始状态是否保留，缺失和排除是否有明确原因，引用是否闭合 |
| 世界状态 | 会话启动后对象是否仍存在，初始状态是否独立复制，当前归属、可见性和可用性由谁持有 |
| 界面查询 | 筛选是否读取当前快照；对象出土、登场或易主后能否使用已有定义和资源展示 |

比较身份集合和关键字段，不能只比较总数；文本和资源检查须覆盖支持范围，不能只用首屏样本。

### 已有批次核对记录

验收按专题保留，普通字段增删、版本递增和逐次测试过程由 Git 追溯。这里仅汇总数据链入口；各报告中的日期、基线和限制仍有效。

| 专题 | 契约与验收入口 |
| --- | --- |
| 开局年龄、异民族身份与初始驻兵 | [开局专项](scenario-startup.md) |
| 军团账户、经济与商人买卖 | [运行时状态](runtime-state.md)、[G03验收](g03-verification.md) |
| 兵法、狙击、俘虏解救及排位保护 | [兵法规格](../specs/behavior/BEH-WAR-0005-tactics.md)、[G07验收](g07-verification.md)、[G10补充验收](g10-verification.md) |
| 联盟、外交宝物与地域邻接 | [G08验收](g08-verification.md) |
| 登场、寿命、课程及单挑特性 | [G09验收](g09-verification.md) |
| 探索、政治、势力事件与终局 | [事件规格](../specs/behavior/BEH-EVENT-0002-event-selection-and-commit.md)、[事件覆盖表](g10-event-coverage.md)、[G10验收](g10-verification.md) |

以下为已完成的本地数据核对快照，不是新的验证结果；原报告位于忽略目录，其他检出可按转换命令重建输入。

| 批次 | 独立核对结果与报告 |
| --- | --- |
| 开局初始化 | 25名人物中5名开局年龄34、20名19；电脑异民族四城各300000兵，休眠倭及玩家势力保留基础驻兵。`local/generated/startup-audit/report.json` |
| 战后规则 | 28项源阵型逐项一致，12／13／14／24／25／27关闭排位保护；25势力／94据点／700人物未删减。`local/generated/verification/post-g10-audit/data-check.json` |
| 军团账户，2026-09-26 | 25个账户与源表、开局收支、势力汇总及保存恢复一致。`local/generated/corps-account-migration/audit.json` |
| 固定用途阵型，2026-09-26 | 剧本122／组合126、规则v72；25势力／94据点／700人物／0初始野战队。公开命令生成建设13／正船24、输送14／输船25，恢复保留配对，50份选阵查询排除固定阵型。`local/generated/fixed-formations-audit/report.json`；3716项默认测试、17项Headless通过，图像校验28664项／5286实体文件通过。未启动游戏，画面待验收 |

## 陷阱地形内容

陷阱选择读取独立内容定义：MilitaryDefinition.TerrainIds 引用地形，Elevations 按同一行优先顺序保存每格高度；TrapTerrains 为每种地形声明 BaseTacticId（可空）和 ProvidesHigherGround；HigherGroundTrap 可为空，否则声明 TacticId 与 RadiusCells。高度使用相同尺度比较，地形ID与兵法ID无需沿用原作编号。存在陷阱选择时，高度须覆盖完整地图，地形规则须覆盖全部地形，效果引用须指向具体陷阱。

兵法的 ContactRange 为可空的正整数，仅武力兵法可配置。非空时，将来源占地矩形沿两轴外扩该值后与目标部队占地严格相交，边缘相碰不满足；空值表示没有额外近战门。原版导入器为步兵、骑兵六种兵法配置5，其余为空，普通选择与智力后续连锁共用该资格查询。

阵型的 DoublesCapture 为显式捕获加倍能力，TC101仅鹤翼映射true，28阵型完整保留并逐项验证。战败返回的起点、原军团与倒计时由运行状态保存；结算见[G07验收](g07-verification.md)。

`MilitaryDefinition.UnformedFormationId`声明异常时的有效阵型引用；TC101完整保留28项阵型，指定27为异常时有效Combat配置，源攻防参数原值导入。Core使用攻击2K/25、防御2D/15及混乱8／溃败6，通过统一有效配置查询消费攻防、削墙及伤兵比例。自制内容可显式为null表示不替换，否则校验引用存在且具有Combat；不需要重复维护一份无阵属性。

Core按目标格实时选择：范围内更高且具备高处资格的邻格优先触发高处陷阱，否则使用目标地形的基础陷阱。距离定义为向下取整的欧氏格距离不超过 RadiusCells，实际用平方距离小于 (RadiusCells+1)² 比较。Mod只编辑基础属性和规则，不保存或维护逐格选择结果；制作工具可复用查询进行预览。当前修改内容后需要重新加载，尚无游戏中地形编辑功能。旧内容包须重新转换。

## 转换与运行

从仓库根目录执行；已有目录按来源清单原位增量更新。复用未变化资源，同步清单并清理失效文件；必要的全量重建仍优先使用原目录。只有明确需要独立对照时保留副本，不为旧包引入兼容分支：

```powershell
dotnet run --project src/San9.Tools -- import-textures --source-exe "<已登记San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --output "local/generated/content/textures-san9pk-tc101"
dotnet run --project src/San9.Tools -- import-client --source-exe "<已登记San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --scenario "private/s9数据文件/D_Sce002.S9" --texture-package "local/generated/content/textures-san9pk-tc101" --output "local/generated/content/scenario-190-live"
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --content-package "<客户端内容包绝对路径>"
```

新局可追加`--computer-damage-bonus 100`指定电脑对玩家直接兵损+100%，默认0。设置随存档和录像保存，加载时不覆盖已有值；此参数不修改内容包。

客户端默认读取`local/generated/content/scenario-190-live`，只加载转换结果。`render-map --content-package local/generated/content/scenario-190-live`也可直接消费组合包内的地图部分。源资源及生成包均被忽略，不提交。

## 原版文本统一入口

编写控制节点、绑定参数和显示图片的具体用法见[消息约定](ui/messages.md)。

现行契约见[统一消息规格](../specs/behavior/BEH-UI-0006-unified-messages.md)，原作证据与覆盖边界见[RE-0166](../research/findings/RE-SAN9PK-0166.md)。唯一语法实现为`OriginalMessageParser`；`OriginalTextConverter`只调用该解析器读取静态片段。`import-client`将四个消息/列传档案合入同一个`TextCatalog.Messages`，保留未就绪记录；`import-text`使用同一实现导出全量诊断和合成上下文报告。

静态内容使用Godot原生BBCode，逻辑图片路径示例为`[img]icons/city[/img]洛阳`。运行时`MessageEvaluator`接收显式值、查询和随机源，产生可保留的BBCode结果。普通参数始终转义；富文本组合用消息引用。`01J`转换为尾部转移，`01C`为返回调用。样式切换只保存明确BBCode开闭片段，支持跨分支/引用的显示状态，不维护重复样式树。节点中不保存原始地址、控制码或资源偏移。

`Resolve`是严格纯文本读取，遇到图标/格式标签拒绝，供名称排序和查询使用；`ResolveRich`供提示、列传和说明使用。侧栏城市名称由通用消息模板求值，再交给`GameRichTextLabel`；控件不判断城市前缀。图片由客户端按逻辑标识选择转换包中的最近有效尺寸、同距优先较大图并保持比例，Godot负责行内排版。缺图明确报错。

FA40..FA45依次映射伷/璝/寗/勑/𩅦/儁；不使用HKSCS猜测。CP950是来源编码，繁简转换仅作用于文字片段。四十条日文占位记录按索引和精确原始字节签名识别CP932，不做宽松编码回退。未知字形、未确认属性及异常尾部保留在诊断，并阻止消息执行；所有引用和依赖确认度另行统计。璝按OpenCC词典显示为𪻺，见[DEC-UI-0005](../specs/decisions/DEC-UI-0005-officer-name-normalization.md)。

```powershell
dotnet run --project src/San9.Tools -- import-text --source-exe "<已登记San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --output "local/generated/research/unified-text" --exe-evidence "local/generated/research/unified-text/exe-evidence.json"
python research/scripts/verify_message_export.py "private/s9数据文件" local/generated/research/unified-text
```

历史字体验收另使用`--text-smoke`，后续开发和日常内容转换默认不执行；仅在当前任务明确要求复现时按[Godot历史画面验收](ui/client-verification.md)单独运行。

EXE证据先在项目主IDA库运行`research/scripts/ida/export_text_evidence.py`只读生成；未提供该文件时只审计四个档案。EXE字符串登记和格式识别不等于玩法调用已绑定，必须检查诊断中的边界声明。字节哈希、偏移、控制码、未确认原因只在导入报告中。未来玩法提供的具名绑定与查询清单由`coverage.json`生成；`synthetic-evaluation.json`只证明合成上下文求值，不冒充真实玩法验证。

界面状态、交互、文字与验收统一见[UI 文档](ui/README.md)。

## 验证与本地产物

通用验证要求见[协作规则](../AGENTS.md#验证与交付)。转换契约变化须核对受影响范围的源字段、文本和图像，不以调用同一转换器作为独立证据。当前实际画面由用户验收，不自动启动Godot。

当前运行包为`local/generated/content/scenario-190-live`。研究资料位于`local/generated/research`，仍有用途的运行态对照、字体研究和检查工具位于`local/generated/verification`。局部问题解决后清理对应临时截图与日志，不保留多轮重复验收记录。

G03经济内容保留基础信望、相性、精通类别、军团账户、兵力容量与粮耗资格；组合表映射为规范化人物ID对，官职俸禄进入Core规则目录。数据覆盖与运行验证见[G03验收](g03-verification.md)。

坐标单位和人事计时统一见[地图坐标约定](map-coordinates.md)。未建设地域及部队使用精细Position，据点X/Y保持格锚点；所有转换共用格中心公式，不支持原作存档导入。人物`StatusStartYear`表示当前身份起始年，导入源身份年份；在野也可有值，不以是否在职丢弃。发现与登用时更新为当前年，查询仅对在职人物派生仕官年数。

军事规则的`RegionIds`按地图格保存规范化地域引用，允许无地域；有效引用须存在于地域名称表。地域关联都市由`RegionCityIds`单独保存，运行时按“格子→地域→都市”查询，不在格子展开保存都市或势力。兵力回收使用这条地理关系，不以当地建筑的当前势力改选都市。

## 领域契约导航

本页只维护转换和引用边界；任务、命令、结算及恢复不变量由对应规格维护。

| 内容 | 作者与Importer职责 | 规则入口 |
| --- | --- | --- |
| 部队方针与成员 | `UnitPolicies`声明四项许可，`PolicyRadius`使用细坐标；人物`UnitId`持有成员归属，部队保存`CommanderId`和`MemberRanks`，不另存成员顺序。TC101源位只在Importer解释 | [移动与命令](../specs/behavior/BEH-WAR-0001-movement-and-command-lifecycle.md) |
| 固定用途与跨岸 | `ConstructionFormations`／`TransportFormations`声明固定陆水配对；战斗基础船由`BasicNavalFormationId`声明，阵型不复制船型或用途状态 | [固定用途](../specs/behavior/BEH-WAR-0001-movement-and-command-lifecycle.md#固定用途部队) |
| 外交与军团 | `AttackRequests`保存有效双方、目标及剩余旬数；TC101剧本无请求元组，输出空集合。`Delegation`为带类型和引用的联合值，导入默认为一般；不支持原版存档导入 | [军团委任](../specs/behavior/BEH-GOVERNANCE-0002-delegation-control-and-automatic-scheduling.md)、[G08验收](g08-verification.md) |
| 人物生命周期 | 作者声明寿命档、继任优先、登场与死亡开关；`FreeOfficerMigrationEnabled`独立控制在野迁移。版本特定列传排除由Importer转换为属性 | [人物生命周期](../specs/behavior/BEH-PERSON-LIFECYCLE-0002-appearance-injury-and-death.md) |
| 季度课程 | `TacticCourses`保存城市／兵法引用，校验城市资格、熟练类别及重复项；显示修行城市也由该集合派生。TC101九组配对完整保留，特殊模式输出空集合 | [成长与学习](../specs/behavior/BEH-PERSON-LIFECYCLE-0001-growth-and-learning.md)、[G09验收](g09-verification.md) |
| 事件与归附 | `SearchEvents`声明角色、地点、资格、奖励及消费；世界消费键与势力`EventHistory`分开。`SettlementDefectionsEnabled`、`UnitDefectionsEnabled`及城市自立开关分别控制独立规则，TC101模式限制留在Importer | [事件选择与提交](../specs/behavior/BEH-EVENT-0002-event-selection-and-commit.md)、[G10验收](g10-verification.md) |

## 情报显示元数据

情报契约包含必填ProvinceNameKeys及官爵IsUnranked、可空DescriptionKey。州名映射允许作者省略，缺失显示“—”；非空文本引用由组合包校验，无官爵标记至多一个。Importer将源州消息和官爵说明转换为语义文本键，运行时不依赖原版州／官爵编号；旧包须重转，不提供兼容分支。这些显示元数据不额外加入规则字段投影。

都市初始兵法包含剧本位域与TC101新局初始化追加的兵法，后者由CityTacticContentCompiler转换，合并去重且保留落石／射台。城市编号映射仅存在于Importer；共享内容继续使用EnabledTacticIds。修正前生成包须重转；数据进入既有内容指纹，旧会话须重新开局，不能只刷新情报窗口。

界面底图、按钮四态、纹饰及情报地图标记由皮肤和纹理包声明，细节见[情报界面](ui/information-browser.md)与[纹理资源](texture-resources.md)。都市区域上传时生成CPU位遮罩供命中使用，不保留额外CPU整图或读取源档案。

## 势力能力目录

目录包含必填`Information.Tactics`与`Information.Formations`目录项（`Id`引用军事规则，`NameKey`引用文本）。作者维护名称及展示范围，不维护按势力生成的派生可用表；校验器检查身份唯一、规则和文本引用。目录变化须重新转换剧本包并重新加载会话。

TC101完整保留41项兵法和28项阵形的展示元数据，名称与说明来自消息资源；目录保存引用，不缓存势力可用性。实际展示按当前世界和规则筛选，完整定义不等于全部可选；具体条件见[阵型候选与兵法条件](#阵型候选与兵法条件)。

190年包已原位重转并通过完整链接加载校验。源初始状态仍为25势力／94据点／700人物；开局查询为24活跃势力／92有效据点／205可见人物，休眠及未登场对象不因菜单功能被删除。离线审计遍历全部92个据点菜单及24个势力的四种能力目录，核对详情ID、武将项可用性、表格列数和浏览前后世界哈希；记录在忽略目录`local/generated/context-menu-audit/report.json`。该批仅验收查询，未验收游戏画面。

### 爵位一览说明文本

爵位包含必填可空`DescriptionKey`，非空时校验文本引用；TC101导入76项原版说明。忠诚上升由共享任官规则的俸禄整数折半值提供，不另设展示公式。

### 兵法一览说明与筛选

军事目录项包含必填可空 `DescriptionKey`，非空时校验文本引用。TC101为全部41项兵法导入名称与说明（消息1221+3×ID）；一览仍仅展示当前势力可用且 `Selectable` 的22项候选，不包括水军、攻城及建设技术。展示元数据完整保留不会使自动兵法变为可选兵法。

### 野外部队动画资源

纹理schema5／转换版本22、地图schema11保存部队完整规模布局，每档同时定义兵群与地利光效；身体包含可选的皇帝势力替代资源。人物定义提供旗字，部队采用大将旗字，君主领军用帅旗。需要依次运行`import-textures`和`import-client`，见[地图导入](map-import.md)。覆盖28项军事阵型、20类身体的八方向待机、五类状态及四种地利片段。190年初始野战队仍为0，不扩大初始对象筛选或新增军事命令；显示规模与领域容量独立，实际画面由用户出征验收。

部队旗帜独立保存八方向片段，当前朝向驱动旗面、旗字和旗杆遮挡；建筑旗帜继续按季节切换。资源变化须依次原位重转纹理与组合包。客户端只读取转换片段。


### 水陆阵型接入

部队保存独立水阵，军事规则声明统一基础船；陆阵定义不绑定船型，异族不特殊化，建设／输送使用固定用途配对。出征、整编、地形查询、跨岸寻路、战斗与地图显示共用有效阵型，详见[水陆规格](../specs/behavior/BEH-WAR-0001-movement-and-command-lifecycle.md#水陆阵型与跨岸移动)。水阵选择仍通过公共命令，未新增出征表单。

初次接入批次为剧本121／组合125、规则v69、保存87／日志90；地图11、纹理5／转换16不变。该批默认解决方案3684项、Headless17项通过，190年包保留25势力／94据点／700人物／0初始野战队及28阵型。核对默认船20、20／21／22／23／26五种水阵的八方向资源、上岸恢复，以及查询不改变世界哈希；报告为 `local/generated/amphibious-audit/report.json`。实际水陆切换画面待用户验收。

### 阵型候选与兵法条件

`IsCommon` 与势力 `AdditionalFormationIds` 按并集去重，再排除固定建设／输送配对；候选资格不依赖异族身份或控制者。TC101通用阵型为0至11、20至23，国号4至8分别补充陆阵15至19及水阵26，基础船仍为20。完整定义保留，查询再按陆水类型和 `RequiredTacticId` 筛选；玩家与AI共享校验，陆阵和水阵分别检查兵法条件。额外候选进入世界、保存及回放。

船目录效果由 `AutomaticTactics` 及兵法名称派生，说明使用 `DescriptionKey`；自动兵法名称引用必须闭合，缺失时拒绝加载。旧目录未包含全部名称／说明的包须重新执行 `import-client`，无需重转纹理。

2026-09-26目录批次核对：源41项兵法名称、28项阵形说明、转换引用、国号4至8的额外候选及25势力50份目录一致；船三页共用候选，查询哈希不变，保存往返和基础船出征通过。报告为 `local/generated/formation-catalog-audit/report.json` 与上述跨岸报告。该批解决方案3704项、Headless17项通过，未启动游戏，画面待用户验收。

部队的 `CommandedThisXun` 为必填领域状态，表示本旬已成功接收指令；剧本123／组合127和保存90开始包含此字段。原版剧本转换以新指令阶段初始化为 false，不推测源运行时位；重制存档精确保存与恢复。旧包需重新转换，旧存档不做兼容补值。

部队详情新增 `FormationCatalogDefinition.Presentation`（必填可空），包含展示网格、按默认人员顺序排列的可用阵位、视野半径／角度及底图角色；剧本124／组合128。内容校验检查几何范围、重复阵位及图像引用。TC101完整保留28项阵形的展示元数据，并引用已有 `G_Jinkei` 转换资源；不新增纹理解码，也不将视野字段解释为隐藏的索敌规则。新包已原位转换，仍为25势力／94据点／700人物／0初始野战队；阵形展示定义28项及资源引用闭合。

### 地图命令菜单资源

皮肤schema22、纹理转换版本20加入`command-*`语义角色：六组一级／二级三段四态按钮、上中下拼接框、45个入口的插图角色、条件卡、三类对象摘要底板和摘要图标；`text.json`对应45个`ui.command.*`静态分类／条件说明。原版资源、调色板与布局证据见[RE-SAN9PK-0073](../research/findings/RE-SAN9PK-0073.md#主地图左键命令菜单补充2026-09-26)。须依次原位运行`import-textures`与`import-client`；客户端只读取组合包及纹理依赖。菜单按当前设施类型、拆除属性与部队用途查询，当前只提供导航和说明，不接入领域命令。主界面临时部队预览不改变190年原始剧本的野战部队覆盖。

## 战略面与进行面资源

皮肤 schema24／纹理转换版本22提供14个阶段角色／帧及9个公共弹窗角色／帧：阶段字图8帧、静止按钮4帧、进度框／填充，以及 `dialog-frame`、`dialog-yes-*`／`dialog-no-*`。原版九块确认框在转换阶段无缩放拼为216×136的九宫格底图，透明像素和逐色Alpha完整保留；不按具体弹窗尺寸生成图片。客户端切边为左右各96、上下各56，运行时平铺边与中心。转换器校验资源／调色板绑定，拼接器拒绝行列尺寸不匹配的切块。证据见[RE-0158](../research/findings/RE-SAN9PK-0158.md#战略面与进行面资源2026-09-26)。依次原位运行 `import-textures` 与 `import-client` 更新依赖；旧的 `turn-dialog-*` 九块角色已移除，不设兼容分支。
