# 客户端内容管线

本页维护内容目录、共享契约、最小重建范围及校验入口。玩法与界面行为由对应规格维护，源数据证据保留在RE，普通升级流水交给Git；历史验证只证明所记基线，不作为当前包复验。界面从[UI文档](ui/README.md)进入，地图与状态分别见[地图导入](map-import.md)、[运行时架构](runtime-state.md)。客户端只读转换包，不读原版EXE、S9或IDA库。

## 剧本目录与共享内容

正常启动进入开局菜单，不预载世界；选剧本→势力→开局设定后建局，播放介绍并生成首旬自动存档。20个正式剧本均已转换，支持分类／预览、地图／一览、决定／取消及返回。多玩家、电脑补偿、虚拟初始化和保存边界见[新游戏规格](../specs/behavior/BEH-UI-0011-new-game.md)，来源见[RE-0200](../research/findings/RE-SAN9PK-0200.md)、[RE-0201](../research/findings/RE-SAN9PK-0201.md)。直接进世界须显式传`--scenario`或`--content-package`；`--player-force`缺省为观察状态。

```text
content/
  catalog.json
  shared/
    opening.json
    maps/china/map.json
    messages/library.json
    textures/
    music/
    sound-effects/
  scenarios/
    historical/scenario-0/ ... scenario-9/
    fictional/scenario-10/ ... scenario-19/
      content.json
      map.json
      scenario.json
      messages/text.json
      ...
```

- `catalog.json`只负责发现：稳定剧本ID、包路径、共享纹理／开局入口；没有默认剧本或190年／第一项回退，也不重复保存来源列表。目录／预览ID与世界ScenarioId一致，TC101使用无前缀源身份字符串；史实／虚拟由categoryId，不按运行时编号区间判断。
- 剧本入口`preview`仅含分类、年月、标题、简介及小地图城市颜色；浏览20项不读取世界。作者维护入口预览，TC101从源头／消息生成；改变其输入后`import-content --rebuild opening`只更新20个已转换入口的预览、目录和共享开局，不重导世界，成功后清旧preview.json。入目录的包必须有预览，独立包可省略。
- 点击剧本先用预览显示势力页，首帧后后台读取所选入口、静态文本／头像和scenario.json中日期、势力、据点、人物、部队、情报、联盟所需字段；不构建事件／AI／军事规则，不读大地图、完整消息或命令表现。Application从真实世界数据及Core经济预测只读派生势力详情，不把人口／兵力／经济派生缓存写进预览，也不建会话／执行首旬或随机初始化。返回取消任务，迟到结果不得发布；失败留页、点击选择势力重试，同页复用准备任务。重新进页会重新派生。
- 确认新局后，地图、世界、文本／表现、消息四组并行读取，共享纹理同步准备，完成结构检查再统一组合校验。消息按声明顺序合并，重复键／覆盖严格检查；失败或取消等全部分支结束，不发布部分内容。后台建局，Godot节点／纹理上传在主线程与转场并行。
- shared/opening.json唯一保存城市中心／逐行区间分区，供绘图和命中共用。剧本页自有底图／补图／色标，只声明整页投影；烘焙标记在制作时配准，不保留第二套城市范围。蓝金势力皮肤、透明槽及标题独立；倭岛保留岛／海／框并排除大陆边角，预览不合回蓝色海框。开局必填quitPrompt，settings及audio声明美术、循环音乐／进入音效和语义文件引用。布局取舍见[RE-0195](../research/findings/RE-SAN9PK-0195.md#2026-10-23开局面板接入与ida布局复核)、[RE-0071](../research/findings/RE-SAN9PK-0071.md#2026-10-23开局设定样式与数值编辑复核)。

每剧本入口引用自身map.json、scenario.json、messages/text.json、私有消息、illustrations.json及presentation.json。必填resourceRoot声明共同根，数据／音频相对根、纹理依赖相对包目录，路径不得逃出相应边界。公共地图／纹理／音频／消息／开局显式归shared，不按哈希或引用次数自动搬迁。

`import-content`转换20个正式剧本并发布共享开局／目录；`import-client`只转换显式独立包。初始PlayerForceId必填可空，不固定190／董卓。TC101重复派生大将标记按现有Core任命规则重建；独立作者显式任命及存档恢复不重算。发布先校验／暂存，逐个替换、最后清单，失败恢复已替换文件；禁止同目录并发导入和客户端热加载。

目录验收须核对20预览与真实年月／城市颜色、只读势力表现、所有包可建局、身份完整进入世界／查询、共享文件独立归属及无参启动菜单，不能以首屏或数量相同代替身份核对。

## 现行版本与历史记录

版本只在下列权威定义集中维护；内容不匹配按对应格式／结构／引用校验拒绝，旧包按实际失效范围原位重转，不做运行时兼容补值。规则版本差异与格式不兼容分开处理，规则、存档和命令脚本策略以SessionProtocol为准。录像已延期撤下，旧记录中的录像验收仅是历史事实。

| 契约 | 权威定义 |
| --- | --- |
| 剧本初始状态 | [ScenarioInitialState.CurrentSchemaVersion](../src/San9.Content/Scenarios/ScenarioInitialState.cs) |
| 组合包／共享地图定义 | [ScenarioContent／MapDefinition](../src/San9.Content/Packages/ScenarioContent.cs) |
| 地图资源／文本／皮肤 | [MapPackage](../src/San9.Content/Maps/MapPackage.cs)、[TextCatalog](../src/San9.Content/Text/TextCatalog.cs)、[InterfaceSkin](../src/San9.Content/Interface/InterfaceSkin.cs) |
| 完整纹理转换 | [TexturePackageImporter.ConversionVersion](../src/San9.Importer.San9PkTc101/Packages/TexturePackageImporter.cs)；定向重建保留原包全局版本 |
| 会话规则／存档／日志 | [SessionProtocol](../src/San9.Application/SessionProtocol.cs) |

不按某个历史包号判断今天是否需要转换。新增必需字段、引用或转换规则后，依[转换与运行](#转换与运行)选择实际受影响单元；仅消费者／文档变化复用已有包。阶段内容核对见[已有批次](#已有批次核对记录)和[G10验收](g10-verification.md#2026-09-21阶段收口)。

## 职责与文件

| 模块 | 职责与输出 |
| --- | --- |
| Content.Maps／MapImporter | 固定shared/maps/china/map.json及剧本map.json建筑布置，无图片 |
| Content.Scenarios／ScenarioContentCompiler | scenario.json初始世界与军事规则拓扑 |
| Content.Text／ScenarioTextReader／PublicationMessageCatalog | 静态messages/text.json及按明确用途分类、保留原键的messages/*.json |
| Content.Interface／InterfaceSkinCompiler | 皮肤角色、颜色、装饰，textures.json.skin |
| Content.Packages／ClientContentImporter | content.json入口、引用与依赖校验 |
| TexturePackageImporter | 独立textures.json完整纹理／图集／地图资源，结构schema5 |
| Importer.Containers.Ls11Archive | 仅LS11解码，不解释名称／地图语义 |

文件名表达逻辑职责，实际路径以content.json和显式resourceRoot为准；独立包可自带资源根。现行版本见上表，不在各模块重复维护。

剧本map.json只存definition路径和buildings身份／位置／固定类型；共享定义保存尺寸、方格、地形绘制、桥／云／边缘／水面／边界及建筑类型。TC101写共享前核对固定数据完全一致，差异不静默复用；独立内容可带自己的map-definition.json。地图身份从剧本、季节从月份派生，不重复保存scenarioId／initialSeason／sources；布局变化无需重建未变纹理。

`content.json.sharedMessages`必填可空，非空时引用公共消息库清单（目录型TC101包为`shared/messages/library.json`）；`messages`只列出本剧本私有消息文件。公共库清单声明自身schema和相对该清单目录的分组文件；剧本清单中的路径仍相对`resourceRoot`。加载时先读取公共消息，再合并私有消息，私有同名键必须列入`messageOverrides`，不声明的重名、无对应公共／私有正文的覆盖声明、缺失引用和循环引用均拒绝。引用闭包在合并后校验，公共消息可引用被本剧本覆盖的键；同一剧本的私有消息不影响其他剧本。独立包可令`sharedMessages`为null并独立携带完整消息。

`messages/text.json`保存静态词条及人物名称引用；`text`和`library`为保留文件名，不可用作消息分组名。公共消息按已确认的源绑定逐步分类：剧本标题及简介放入`scenario-catalog.json`，人物姓名放入`person-names.json`，兵法、阵型、官职、爵位、州名、国号分别放入`tactics.json`、`formations.json`、`offices.json`、`ranks.json`、`geography.json`、`country-titles.json`。消息键与节点不变，文件之间仍可互相引用；未确认用途的列继续留在`messages.json`，不按相邻编号扩大分类范围。已有列传正文保存在公共库的`biographies.json`，人物的`biographyKey`直接引用它，`text.json`不再复制一份列传。静态文本解析支持无运行时参数的消息正文，动态消息仍通过消息求值器处理。剧本私有`messages/text.json.messageTags`提供具名台词条件，公共模板通过`scenario.has-message-tag`在运行时查询。TC101将黄巾起义等剧本开局语境转换为标签；原作DC史实／虚拟条件转换为`game.is-fictional`，读取已保存的世界设定，不再误用剧本模式1的标签。原作剧本编号不进入运行时消息；势力判断保留实体比较，不因初始剧本中缺席就折叠为恒假。

定向纹理重建只更新所选单元，保留整包 `Sources` 与转换版本；共享来源变化后，后续普通整包导入仍按既定大小／修改时间策略识别失效，不以局部读取的新来源戳替整包背书。

共享剧本契约不按原版编号区间限制城市或人物身份。城市图标资格由`SettlementKind.City`决定；`RegionCityIds`必须引用真实城市，其城市名从据点定义读取，不要求城市ID同时存在于地域名称表。数量与名称不继承源格式槽位和字节长度；玩法数值上限及显示资源范围独立校验。

初始联盟使用必填可空`Alliance`：目标、盟主、排序后的成员身份集合与剩余旬数。Importer把源联盟旗标及头部期限转换为同一个语义对象，缺失成员或矛盾状态明确拒绝。190年开局目标5、盟主7、13个成员及36旬完整保留；运行态从初始值独立演进，快照查询当前联盟，恢复不重新导入或重新扣旬。

阵型以属性表达计略减半与扰乱免疫。版本Importer将方圆和不可扰乱阵型转换为属性，Core不比较源编号。任务保存野战目的地细坐标、地域身份和类型化计略载荷；去返程快照使用同一坐标尺度，旧任务布局明确拒绝。

地理契约包含必填`CityProvinceIds`和`MilitaryDefinition.TerritoryExits`。前者是城市到州的基础分组，可不为某城市指定州，不继承15州上限；后者以行优先顺序保存每格北、东、南、西出口语义，与现有地域、地形及完全阻断格共同推导地域边。非空出口数组必须覆盖整幅地图，空数组明确表示未提供地域邻接。作者不维护逐势力邻接或成员候选表；Core只缓存不可变地图边，按当前据点位置、归属、存续及港口属性生成资格。城市州分组进入内容指纹并由会话独立持有。190年包已原位更新并逐项核对50城、40000格及94据点；具体数据链和736条有向边见G08验证记录。

地图不依赖名称、势力摘要或皮肤；剧本／文本可独立校验，组合层核建筑ID与据点一一对应、坐标及文本引用。text.entries直接键→显示字符串，不存text/sourceText双包装；设施名含类型后缀、地域基础名单列。源字节留源文件／研究导出，以SHA追踪，不进客户端。

共享纹理位于 `local/generated/content/shared/textures`。剧本包只引用共享包的目录和资源 ID，没有 `images` 目录。43个已确认非头像档案及836张正常色大头像转换为最终图片；未确认项只记录研究边界，不保留索引图和原始调色板副本。具体覆盖与透明规则见[纹理资源](texture-resources.md)。

图片契约、静态图集、GPU缓存和资源租约见[纹理资源文档](texture-resources.md)。头像上传后不保留CPU像素，窗口与地图按实际使用异步申请。

清单保留缩进；数据子文件使用紧凑 JSON，保留完整字段名和内容。仅省略消息表达式及参数中默认值为 null 的可选字段，必填 nullable 状态字段仍显式保存。数据子文件必须位于显式资源根内，共享纹理目录允许相对依赖引用。导入先校验、暂存全部文件，再替换数据并最后发布清单；不支持导入过程中并发读取。

`MilitaryContentCompiler`将完整地形、硬阻挡、阵型移动预算和据点footprint导入剧本，Core直接消费该规范化规则，不读取`map.json`的显示资源。G06增加普通战斗参数、阵位、据点销毁能力及名马标记；部队成员、伤兵和当前命令随世界查询变化。数据核对见[G05](g05-verification.md)与[G06验收](g06-verification.md)，完整兵法、设施建设及水陆转换见[G07验收](g07-verification.md)。

## 来源边界与统一内容

基础版与 PK 追加是原作的来源组织方式，不作为重制版的内容分类。此约定适用于人物、头像、宝物、剧本、建筑、地图、文本、界面和其他内容，不限于显示资源。

宝物图片统一images/treasures/{id}.png，与portraits并列，普通插图在illustrations；目录或共用解码规则变化按实际消费者重建，影响全包时才用all。

- 版本 Importer 处理源文件分组、编号分段、记录布局及版本识别，将支持范围内的数据合并为各自类别的统一定义与稳定 ID 引用。源 ID 可直接保留时不为统一而重新编号；有冲突时在导入阶段明确映射并闭合引用。
- Content、Core、Application、Rendering 和客户端不因内容来自基础版或 PK 而建立两套集合、加载路径或行为分支，不添加仅表达来源分组的 `isPk` 等产品字段。
- 来源文件、版本和原始编号可保留在导入追踪信息与研究记录中，不用于决定内容的可见性或可用性。对象类别、能力、规则和剧本初始状态等实际产品语义仍须明确表达；统一来源不等于抹平玩法差异或把不同剧本的初始状态混合。
- 当前支持范围和缺失内容仍须如实标明。此约定不代表已完成全部 PK 内容导入，也不改变已选定的原作版本基线。后续 Mod 同样转换为统一契约，源格式差异留在导入阶段。

TC101剧本导入以源坐标识别地域配置：双坐标均为无效哨兵的槽不生成地域、名称文本或城市归属映射；单轴无效或越界坐标直接报错。有效但尚未建造的地域完整保留，稳定ID不重排。人物和宝物引用由转换后的契约校验，不能通过丢弃引用对象掩盖错误。

地图攻击配方声明身体／弹体启动时间对、齐射间隔及动作日阻塞属性，普通身体与弹体寿命分开；同阶段实际准入对象共同等待最长者。新增字段缺失拒绝，不补兼容值；资源规则变化仅重建map，不改地图实例／世界／音频，见[移动与攻击](../specs/behavior/BEH-RENDER-0003-map-ambient-animation.md#旬推进中的移动与攻击)。

## 接入前的数据核对

AI参数独立于显示资源：调度标识、家族／君主偏好、工作门、阵形角色、船型／设施／地形引用由Importer转换。TC101保留25初始势力／94据点／700人物、三组家族加权、七君主偏好及25初始错峰标识；动态势力分配并保存新标识。算法变化本身不触发贴图重建，见[AI控制](../specs/behavior/BEH-AI-0001-planning-boundary.md)及[OAI-03军事闭环](../specs/original-ai/implementation-plan.md#oai-03军事闭环)。

新增运行时对象或改变导入范围、对象筛选边界时，明确支持的版本与内容范围，并核对以下受影响层次；区分有效对象、未启用槽和未支持扩展。

| 层次 | 核对内容 |
| --- | --- |
| 原版源数据 | 定义和槽位数量、稳定编号、状态分布、持有人/地点等引用，以及完整范围的文本和图像来源 |
| 转换包 | 定义与资源是否完整，初始状态是否保留，缺失和排除是否有明确原因，引用是否闭合 |
| 世界状态 | 会话启动后对象是否仍存在，初始状态是否独立复制，当前归属、可见性和可用性由谁持有 |
| 界面查询 | 筛选是否读取当前快照；对象出土、登场或易主后能否使用已有定义和资源展示 |

比较身份集合和关键字段，不能只比较总数；文本和资源检查须覆盖支持范围，不能只用首屏样本。

这里的支持范围指本次新增或改变的数据集合及其依赖，不要求每次接入重做全部已有内容的源数据、资源与世界核对。输入及相关规则未变的已通过核对继续复用；包版本递增本身不使未受影响集合的核对失效。

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

原版音乐的离线转码及音效去角色重复的素材整理见[音频准备](audio-preparation.md)。转码与运行包导入独立，不固定到版本Importer；Importer从私有目录复制已准备的WAV／Ogg Vorbis／MP3并生成语义引用。

转换入口统一--game-dir，目录须直接含San9PK.exe及原版S9；--scenario只接受该目录中的剧本文件名，如D_Sce002.S9，不接受另一路径。San9SourceDirectory负责同版本身份与缺文件检查，不支持旧--source-exe/--data-dir。私有输入为private/游戏文件，目录内DLL仅研究、不参与导入或运行加载。

以下是按需转换与运行入口，不是每次开发或交付的执行清单。先按[工程流程](../specs/engineering-workflow.md#变更流程)判断已有产物是否失效；相关内容改动收敛后集中更新，纯消费者代码和文档改动复用已有包。

从仓库根运行、写回原目录；只有独立对照才留副本。import-content负责目录型全剧本发布，import-client会重新生成一个剧本包全部JSON并复制已准备音频，没有局部／增量入口；两者都只引用共享纹理。原位不等于增量，相关改动收敛后集中执行一次，不为未变化产物反复转换。

```powershell
dotnet run --project src/San9.Tools -- import-textures --game-dir "private/游戏文件" --output "local/generated/content/shared/textures"
dotnet run --project src/San9.Tools -- import-content --game-dir "private/游戏文件" --output "local/generated/content"
dotnet run --project src/San9.Tools -- verify-content --content-root "local/generated/content"
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot
# 显式直接进入某个剧本及玩家势力：
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --scenario 4 --player-force 6
```
独立包（不发布目录）示例：

```powershell
dotnet run --project src/San9.Tools -- import-client --game-dir "private/游戏文件" --scenario D_Sce002.S9 --texture-package "local/generated/content/shared/textures" --output "local/generated/content/scenario-190-live"
dotnet run --project src/San9.Tools -- verify-client --content-package "local/generated/content/scenario-190-live"
```

修正解析规则或补齐遗漏资源时，给 `import-textures` 加 `--rebuild`，直接按当前代码重转指定单元，不以原始文件变化为触发条件，也不创建持久中间缓存：

```powershell
# 只重转光标；其他可选值见下表。
dotnet run --project src/San9.Tools -- import-textures --game-dir "private/游戏文件" --output "local/generated/content/shared/textures" --rebuild cursors
```

| `--rebuild` 值 | 重新转换范围 |
| --- | --- |
| `cursors` | 全部系统光标图片及热点定义 |
| `skin` | 整个界面皮肤，包括按钮、装饰、颜色和光标 |
| `map` | 共享地图资源，包括四季地形、建筑和动画 |
| `portraits` | 全部大头像与锚点 |
| `library/g_common` 等 `library/<档案ID>` | 指定已登记图库档案的全部已支持图片与调色板变体；ID 使用小写 |
| `all` | 强制整包重转，跳过原有整包复用判断 |

一次指定一个单元，按修正规则实际影响范围选择；共用解码规则影响多个消费者时须依次重转相关单元，或使用 `all`。当前不支持单按钮、单头像或单地图动画选择。定向重转要求已有可反序列化的同来源、同包 schema 纹理清单，允许被替换单元在当前校验下尚不完整，替换后对整个包校验；不迁移旧格式。原始输入应保持同一基线，切换源数据用整包转换。

定向重建先从源生成选定单元，再反序列化组合，旧选定单元必需字段不是输入，未选单元仍严格读取；全包校验后发布。保留其他单元定义／图片，用images/rebuilt/<单元>/固定路径重打包该目录仍被引用图片并改引用；清单发布后清无引用图片和临时.sources/，不按像素合并不同规则。保留整包Sources／ConversionVersion，局部来源新戳不能替全包背书；全局格式／解码升级才用all。图片／皮肤修复不要求重转世界，数据／文本／引用生成变化才重转剧本包。

新局可追加`--computer-damage-bonus 100`指定电脑对玩家直接兵损+100%，默认0。设置随存档和录像保存，加载时不覆盖已有值；此参数不修改内容包。

客户端通过Godot的`editor`特性区分开发与导出运行：开发读取`local/generated/content`，导出读取EXE旁的`content/`。无参数先进入开局菜单；`--content-root`指定其他目录，`--scenario ID`按目录定位明确剧本，`--content-package PATH`直接指定包，两种剧本选择互斥。发布时复制整个目录，保留`catalog.json`、`shared/`和`scenarios/`的相对位置。离线地图使用`render-map --content-package <剧本包目录>`。客户端只读取转换结果；源资源与生成物不提交。

## Windows 导出

在仓库根目录运行 `./tools/export-windows.ps1`，可用 `-GodotPath "<Godot 4.7.2 .NET 可执行文件>"` 指定编辑器；默认从 PATH 查找。模板固定使用 `private/template/windows_release_x86_64.exe`，输出为 `local/generated/export/windows-x64/San9.exe`，导入与导出日志保存在同目录。

Godot 将 PCK 和 .NET 发布产物嵌入整体单 EXE；不启用 `.NET PublishSingleFile`（Godot C# 工程是类库），也不启用 Native AOT。`ExportRelease` 使用 `PublishTrimmed=true` 与 `TrimMode=partial`，保留游戏程序集、GodotSharp 和 JSON 反射所需的 `System.Collections.Immutable`，并启用原生宿主入口及 JSON 反射。裁剪告警保留在 Godot 的 MSBuild 日志中；当前配置不表示所有代码均可安全进行 full trimming。

脚本不检查、复制或打包转换资源。运行时按上述默认目录放置资源，或通过 `San9.exe -- --content-package "<剧本包绝对路径>"` 指定现有包。仅构建成功不能替代运行验证，headless 启动也不代表画面或完整玩法验收。

## 原版文本统一入口

CommandText及RemakeCommandText.json集中维护必需语义角色／参数（含军团管理、赠礼金额／空金额格式和ui.commands.label.advice）；Importer从源短标签／格式／静态消息及明确重制说明生成，运行时缺键／参数拒绝，无中文回退。作者可改措辞及参数顺序，动态对白仍走消息图。新词条只需重转剧本包、不建纹理，字段证据见[RE-0182](../research/findings/RE-SAN9PK-0182.md#25-赠与准备页字段与初始状态复核2026-10-22)。

编写控制节点、绑定参数和显示图片的具体用法见[消息约定](ui/messages.md)。

现行契约见[统一消息规格](../specs/behavior/BEH-UI-0006-unified-messages.md)，原作证据与覆盖边界见[RE-0166](../research/findings/RE-SAN9PK-0166.md)。唯一语法实现为`OriginalMessageParser`；`OriginalTextConverter`只调用该解析器读取静态片段。`import-client`仅将`M_MsgPK.s9`和`M_RtdnPK.s9`合入`TextCatalog.Messages`，经`GameplayMessageCompiler`转换为领域引用、属性与事件条件，发布前拒绝未确认语义、无法转换的表达式和无效引用，不保存未就绪记录；`messages/*.json`以消息键直接映射节点数组，普通文本节点直接写成字符串，值节点不保存前后缀字段，默认不生成`message-readiness.json`和`message-adaptations.json`，仅在显式指定`--message-reports true`时输出；关闭报告时不构造就绪报告或汇总参数清单，发布校验独立执行，成功转换后清理输出目录中这两个旧报告。就绪报告列出发布消息的原文确认状态和从节点推导的参数；`import-text`仍对四个档案导出研究诊断和合成上下文报告，基础版不进入运行时包。

静态内容使用有限预设及行内图片标记，逻辑图片路径示例为`[img]icons/city[/img]洛阳`。运行时`MessageEvaluator`接收显式值、查询和随机源，产生可保留的显示字符串，预设由显示边界转换为原生BBCode。普通参数始终转义；富文本组合用消息引用。`01J`转换为尾部转移，`01C`为返回调用。样式使用有限预设：静态范围合并为`[preset=名称]…[/preset]`字符串，动态范围保存`styled/preset/body`。源状态传播在Importer消解，运行时引用不泄漏样式；未知预设及未闭合范围拒绝加载。节点中不保存原始地址、控制码或资源偏移。

`Resolve`是严格纯文本读取，遇到图标/格式标签拒绝，供名称排序和查询使用；`ResolveRich`供提示、列传和说明使用。侧栏城市名称由通用消息模板求值，再交给`GameRichTextLabel`；控件不判断城市前缀。图片由客户端按逻辑标识选择转换包中的最近有效尺寸、同距优先较大图并保持比例，Godot负责行内排版。缺图明确报错。

FA40..FA45依次映射伷/璝/寗/勑/𩅦/儁；不使用HKSCS猜测。CP950是来源编码，繁简转换仅作用于文字片段。四十条日文占位记录按索引和精确原始字节签名识别CP932，不做宽松编码回退。未知字形、未确认属性及异常尾部保留在诊断，并阻止消息执行；所有引用和依赖确认度另行统计。璝按OpenCC词典显示为𪻺，见[DEC-UI-0005](../specs/decisions/DEC-UI-0005-officer-name-normalization.md)。

```powershell
dotnet run --project src/San9.Tools -- import-text --game-dir "private/游戏文件" --output "local/generated/research/unified-text" --exe-evidence "local/generated/research/unified-text/exe-evidence.json"
python research/scripts/verify_message_export.py "private/游戏文件" local/generated/research/unified-text
```

历史字体验收另使用`--text-smoke`，后续开发和日常内容转换默认不执行；仅在当前任务明确要求复现时按[Godot历史画面验收](ui/client-verification.md)单独运行。

EXE证据先在项目主IDA库运行`research/scripts/ida/export_text_evidence.py`只读生成；未提供该文件时只审计四个档案。EXE字符串登记和格式识别不等于玩法调用已绑定，必须检查诊断中的边界声明。字节哈希、偏移、控制码、未确认原因只在导入报告中。未来玩法提供的具名绑定与查询清单由`coverage.json`生成；`synthetic-evaluation.json`只证明合成上下文求值，不冒充真实玩法验证。

界面状态、交互、文字与验收统一见[UI 文档](ui/README.md)。

## 验证与本地产物

通用验证要求见[协作规则](../AGENTS.md#验证与交付)。转换契约变化须核对受影响范围的源字段、文本和图像，不以调用同一转换器作为独立证据。当前实际画面由用户验收，不自动启动Godot。

开发内容根为local/generated/content；独立scenario-190-live仅用于显式独立包和历史实验，不是无参默认剧本。研究输出在local/generated/research，必要运行态对照／字体研究／工具在local/generated/verification；问题闭合后清无用途的临时截图／日志，不累计重复验收。生成物及私有样本不提交。

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

Information.MapContextExcludedFacilityIds为必填设施类型集合，空即无额外排除、未知／重复引用拒绝；TC101仅土垒两方向／石兵，映射留Importer。障碍物标牌／hover从FacilityRule.IsWork派生，不与驻人或右键排除混用。

情报契约包含必填ProvinceNameKeys及官爵IsUnranked、可空DescriptionKey。州名映射允许作者省略，缺失显示“—”；非空文本引用由组合包校验，无官爵标记至多一个。Importer将源州消息和官爵说明转换为语义文本键，运行时不依赖原版州／官爵编号；旧包须重转，不提供兼容分支。这些显示元数据不额外加入规则字段投影。

都市初始兵法包含剧本位域与TC101新局初始化追加的兵法，后者由CityTacticContentCompiler转换，合并去重且保留落石／射台。城市编号映射仅存在于Importer；共享内容继续使用EnabledTacticIds。修正前生成包须重转；数据进入既有内容指纹，旧会话须重新开局，不能只刷新情报窗口。

界面底图、按钮四态、纹饰及情报地图标记由皮肤和纹理包声明，细节见[情报界面](ui/information-browser.md)与[纹理资源](texture-resources.md)。都市区域上传时生成CPU位遮罩供命中使用，不保留额外CPU整图或读取源档案。

## 势力能力目录

目录包含必填`Information.Tactics`与`Information.Formations`目录项（`Id`引用军事规则，`NameKey`引用文本）。作者维护名称及展示范围，不维护按势力生成的派生可用表；校验器检查身份唯一、规则和文本引用。目录变化须重新转换剧本包并重新加载会话。

TC101完整保留41项兵法和28项阵形的展示元数据，名称与说明来自消息资源；目录保存引用，不缓存势力可用性。实际展示按当前世界和规则筛选，完整定义不等于全部可选；具体条件见[阵型候选与兵法条件](#阵型候选与兵法条件)。

历史190包菜单审计：25势力／94据点／700人物定义，查询24活跃势力／92有效据点／205可见人物；92据点菜单、24势力四能力目录的详情ID、武将项、列数及前后世界哈希通过。报告local/generated/context-menu-audit/report.json，仅证明该基线查询，非画面验收，休眠／未登场对象未被删除。

### 爵位一览说明文本

官职AppointmentGroup（文／武）和FillBeforeOrdinaryOffices、君主爵位PreferBalancedOfficeCandidates表达任命语义，顺序用GovernorPriority，不按显示文字或ID推导。TC101转换源指挥门及四最高官职，导入76条官爵说明；DescriptionKey必填可空、非空核引用，忠诚变化共用薪俸整数折半，不另造显示公式。

### 兵法一览说明与筛选

军事目录项包含必填可空 `DescriptionKey`，非空时校验文本引用。TC101为全部41项兵法导入名称与说明（消息1221+3×ID）；一览仍仅展示当前势力可用且 `Selectable` 的22项候选，不包括水军、攻城及建设技术。展示元数据完整保留不会使自动兵法变为可选兵法。

### 野外部队动画资源

地图资源声明28军事阵型／20身体的规模布局、八向待机／攻击、可选皇帝身体、五状态和四档地利。人物旗字独立，君主用帅旗；MapUnitBody.Flags保存每向多挂点及深度、斗舰双旗，部队随持久朝向、建筑按季节。近／远MapUnitAttack独立声明动作候选、弹体／发数，统一变体选择供绘制／时长／真实帧中心发射。显示规模不决定领域容量，不增加190年初始野战队；数据／证据见[RE-0156](../research/findings/RE-SAN9PK-0156.md#同日实现水面地图挂旗与普通攻击)及[地图导入](map-import.md)。

### 部队退场资源

`MapUnitBody.Retirement`声明可空的终止动作八向片段、附加效果及延迟、淡出时长与节拍；`MapBodySounds.Retirement`独立声明初始声音族、延迟及可空收尾声。自制内容可明确关闭退场或声音；缺少必填字段的旧包拒绝加载。TC101保留20种身体的终止资源、4种附加效果及身体族声音，不在客户端按源兵种编号选择。退场资源通过当前可见部队预载，活动对象删除后由有限视觉继续持有，完成后释放；新字段接入须重转共享地图资源和剧本表现内容，其他纹理单元复用。原作证据与采用边界见[RE-0139](../research/findings/RE-SAN9PK-0139.md#2026-10-24部队退场动画喊话与音效复核)。

### 水陆阵型接入

部队独立保存水阵，军事规则BasicNavalFormationId声明共用基础船；陆阵不绑船、不按异族身份强制船型，建设／输送用固定配对。出征／整编／跨岸／战斗／显示共用有效阵型，见[水陆规格](../specs/behavior/BEH-WAR-0001-movement-and-command-lifecycle.md#水陆阵型与跨岸移动)。初始Facing由内容声明，TC101取原作初始化南向，Core按净移动更新、战略待机读取，不再按目标重算；见[朝向证据](../research/findings/RE-SAN9PK-0156.md#2026-10-22进行面返回战略面时船只朝向复核)。

历史跨岸批次：28阵型、默认船20及20/21/22/23/26八向资源／上岸恢复、查询哈希通过，190包25势力／94据点／700人物／0野战队未变。报告local/generated/amphibious-audit/report.json；旧3684项默认／17项Headless不代表当前复验，实际画面由用户验收。

### 阵型候选与兵法条件

`IsCommon` 与势力 `AdditionalFormationIds` 按并集去重，再排除固定建设／输送配对；候选资格不依赖异族身份或控制者。TC101通用阵型为0至11、20至23，国号4至8分别补充陆阵15至19及水阵26，基础船仍为20。完整定义保留，查询再按陆水类型和 `RequiredTacticId` 筛选；玩家与AI共享校验，陆阵和水阵分别检查兵法条件。额外候选进入世界、保存及回放。

船目录效果由 `AutomaticTactics` 及兵法名称派生，说明使用 `DescriptionKey`；自动兵法名称引用必须闭合，缺失时拒绝加载。旧目录未包含全部名称／说明的包须重新执行 `import-client`，无需重转纹理。

2026-09-26目录批次核对：源41项兵法名称、28项阵形说明、转换引用、国号4至8的额外候选及25势力50份目录一致；船三页共用候选，查询哈希不变，保存往返和基础船出征通过。报告为 `local/generated/formation-catalog-audit/report.json` 与上述跨岸报告。该批解决方案3704项、Headless17项通过，未启动游戏，画面待用户验收。

CommandedThisXun必填，初始新指令阶段false，存档精确恢复，不猜源运行位；HasEnteredDailyCycle为独立必填执行历史，公共部署false、首次每日军事执行true，等待和旬内下令不替代它。DepartureDaysRemaining、Waypoint、Lookahead分别保存等待／玩家中继／自动前瞻，Importer对源未提供状态取0/null/null；不恢复旧MovementPlan／阻挡快照／失败重试。接触对象及主动／被动交战初始空／false，设施另保存攻击者身份；运行态资格不能用显示资源代替，见[生命周期](../research/ai.md#military-lifecycle-closure)。

FormationCatalogDefinition.Presentation必填可空，含展示网格、默认顺序阵位、视野半径／角度及底图角色，核几何、重复位和资源引用；TC101保留28项并复用G_Jinkei，不把视野展示字段解释成额外索敌规则。

### 地图命令菜单资源

皮肤command-*提供六组一级／二级三段四态按钮、拼接框、45插图、条件卡、三对象摘要与图标；ui.command.*为静态分类／条件说明，ui.command-hint.*为底部短提示。运行查询以当前对象类型／用途／资格／资源返回可用性及原因，静态文案不等于可执行。资源与完整交互见[RE-0073](../research/findings/RE-SAN9PK-0073.md#主地图左键命令菜单补充2026-09-26)及[UI文档](ui/README.md)，不在此累计菜单实施进度。

## 战略面与进行面资源

皮肤提供阶段字图8帧、静止四态、进度框／填充及dialog-frame／yes／no角色。九片源确认框在Importer无缩放拼成216×136九宫格，保留透明及逐色Alpha，客户端左右切96／上下56后平铺，不按每个窗口导新图；尺寸不一致拒绝，旧turn-dialog-*别名不兼容。证据见[RE-0158](../research/findings/RE-SAN9PK-0158.md#战略面与进行面资源2026-09-26)。

### 动态消息运行时接入（2026-09-27）

动态消息使用人物姓／名／肖像、类型化对象引用、快照属性及具名历史，正文名称与条件数值分开，运行时无需源倒序编号。return-kind.N由MessageBinding.Return显式提供人事37／野战52归队类型，不从当前选中单位推断。

190年组合包已原位更新，无需重新转换纹理。两个PK档案14,888条经已确认的内容取舍后全部具备运行时表达式及依赖支持；另有2条重制名称模板，总计14,890／14,890条就绪。这里的“就绪”要求调用方提供报告所列的有效领域引用和事件参数，并不表示所有原版窗口和事件台词已在客户端触发。PK中原先拒绝的5条已处理：1条容貌帮助固定70／30并着色，4条去除正常结束后的残缺跳转。基础版消息不加载、不改写，也不参与运行时统计。显式开启报告后，`message-adaptations.json`保留5条直接改写的源数据SHA-256、决定与移除控制字节；研究导出的严格原文诊断不变。详情见[消息约定](ui/messages.md)。

### 边缘滚动光标（2026-09-27）

皮肤Cursors目录引用PNG及像素热点，八向scroll-<direction>-idle/active共16项，核引用／尺寸／热点。TC101从EXE RT_GROUP_CURSOR/RT_CURSOR的12000..12015组解8位调色板、倒序行及AND遮罩，拒绝RGBA无法表达的反色像素；48×48产物由共享租约交Godot系统光标，不新加平台调用，源编号留Importer。证据见[RE-0175](../research/findings/RE-SAN9PK-0175.md#真实光标素材接入2026-09-27)。

### 设施出征准备

军事内容字段`DefaultDeploymentFormationId`可为空，非空须引用可选用途的陆阵，不能指定建设、输送或无阵。TC101导入指定长蛇，UI不识别源编号；该偏好只用于准备草案初选，不改变公共出征命令或AI选择。

deployment-*提供外框／字段／设施装饰／陆水标签／按钮／数字键盘，复用布阵／目录。Importer裁源外留白并统一中段平铺；多选勾选背景、绿色决定／金色总括四态、三段标题／滑条／拖标，以及三背景裁切遮罩／原生边条／方针三态独立声明。遮罩转Alpha覆盖，运行时只在纹理就绪且尺寸变化时重建临时背景、退出释放，见[RE-0177](../research/findings/RE-SAN9PK-0177.md)。

历史源包核对洛阳八候选、长蛇／走舸、32000兵及哈希不变；实际交互验收边界见[设施出征准备](../specs/behavior/BEH-UI-0001-initial-strategy-screen.md#设施出征准备2026-09-27)，不因显示接入增加初始部队。

### 出征目标、路线与运行时状态

皮肤schema33提供`target-*`语义资源：标题／底框、四态一览与中止按钮、两组四帧目标标记、八帧`target-route-0..7`路线光点、两季各四帧中继旗，以及160×240部队悬浮卡。`BuildingTargetOffsets`按建筑显示类型提供标记偏移，组合包校验引用完整。黑色与地利红色覆盖由Rendering根据地形四角高差生成共用遮罩，颜色由shader指定，不导出`target-mesh-*`中间贴图。证据见[RE-0178](../research/findings/RE-SAN9PK-0178.md)和[RE-0179](../research/findings/RE-SAN9PK-0179.md)。

路线光点由Importer从`G_Marker`图31／局部调色板4切为八张32×32图片；客户端加色权重192/256，按路径序号错相播放。中继旗从`G_MpMkPK`图0／1切出40×68帧，补透明边为76×68，统一旗杆底端到(38,67)，分别映射`target-waypoint-spring-summer-0..3`及`target-waypoint-autumn-winter-0..3`。客户端以底部中心为锚点、142ms切帧，按季节选组并参与对象深度排序。资源编号、调色板和原版偏移只由Importer解释。

目标／中继／路线／方针只改草稿，最后DeployUnitCommand共用资格校验并原子创建；源中心合法即城内生成，纯水等中心不合法用统一合法入口。历史190包94据点／0初始队，洛阳至孟津／虎牢／长安预览、中继、9日等待及恢复通过，29059逻辑／5270物理是当时基线，见[寻路验收](pathfinding-verification.md)。移动／攻击／箭雨／石块／船水纹内容调度见[表现规格](../specs/behavior/BEH-RENDER-0003-map-ambient-animation.md#旬推进中的移动与攻击)，本页不重复日推进时序或画面验收。

## 巡察命令表现

presentation.json独立声明巡察消息、NPC、地域提示及语义音效，源消息号／列传／地点／文件映射留Importer；发布校验人物／消息／媒体路径。人物对白框／继续动画在skin，命令用途提示与45项源消息逐项核对，动态禁用原因仍由规则决定。效果不消费规则随机，实际操作／消息顺序见[巡察接入](ui/patrol-presentation.md)。

## 进行面消息与任务面板

turn-record-panel／turn-command-panel为G_Main59/61、调色板12/13，保留逐色Alpha，四ui.execution-panel.*词条必需。记录求值一次、左下近十条；任务读当前玩家、最多五行，跨旬“-------”、本旬用剩余日数条。完整历史浏览／映射范围见[消息面板](ui/map-messages.md)，不以此资源说明宣称全事件都已接入。

## 存读档界面皮肤

save-load-*含背景／边框、两种四态槽、页签、史实／虚拟／难度、备注与是／否。文件为原子保存、摘要头可独立读取的客户端栏位容器和Application封套；列表只读摘要、确认后校验正文，保留格式／内容／状态检查，合法手工修改不因完整性哈希拒绝。预览必填scenario.title、头像复用Face；版本／路径与玩家读档语义见[存读档规格](../specs/behavior/BEH-UI-0007-save-load-browser.md)，源资源见[RE-0180](../research/findings/RE-SAN9PK-0180.md)。

catalog-facility-*复用G_Still工作物插图及特性／提示，目录基础费用与建设按人数算实际费用分开。icon-construction／icon-transport在skin，内容引用变化重转剧本、图像变化只重建对应资源。

## 待答事件、终局与媒体接口（2026-10-07）

presentation.decisions声明窗口族、语义选项、消息／角色、前置／结果、确认、NPC及词汇。DecisionDraft只读当前待答，控制者按当前受询势力玩家席位，不固定初始玩家／发起方；费用／报价／候选／结果来自规则及已提交事实，未提交不宣告奖励。24类Prompt、36表现定义（含山阳公国号第二步）与三继任共用目录，结局独立于玩法待答。

横向确认、人物卡／目录／键盘复用；纵选G_Common386..388四态32高、纵步40。大图消息框G_Common426..433／palette57按原生边角／平铺边居中，不能用纸纹正文框包图；时间条G_Common390/palette51、G_Main63/palette14。历史包校验341次消息求值、52新角色及默认4139／Headless17项，仅是该基线引用／逻辑验证，非画面／试听。

ISceneMedia仅接受语义BGM／影片和取消令牌；默认SilentSceneMedia立即完成、不加载媒体，SE按消息及用户开关处理。实际地图／会谈音乐由各自播放所有权管理；电影仍占位。结局四人及方针确认后一次EndingOperation，读取冻结结果且不推进日期，结束可回标题／新局。当前专用ending声明两套地图分层、阶段、插图／遮幅／继任／正文、时间与曲渐停、厂牌／标题资源；BGM_00/BGM_22、G_Ending1/2/7/8/9逐色Alpha，来源／渲染与取舍见[RE-0181](../research/findings/RE-SAN9PK-0181.md#结局专用场景接入与收尾2026-10-23)及[结局规格](../specs/behavior/BEH-SCENARIO-0002-ending-resolution.md#结局演出与返回标题)，替代初期仅图片／消息的限制。

## 人才四命令（2026-10-08）

presentation.personnel提供召来／移动出发、探索／登庸推荐及结果、四命令独立出发声、三顾专用预测、俘虏成功通知、携城插图／声；源SE_C004／SE_G012须存在。PersonnelDraft绑定局部选择／Revision，共用Core资格／行程／阈值查询，不建任务、抽骰或披露隐藏对象。探索／登庸初选有效政治最高者，召来／移动多选；取消不提交，执行原子交整个名单，重复对象不重复意见。

成功后按统辖优先级选一人、整次一次出发发言；普通探索／登庸以发生前快照组织结果，发现走待答、不重复播报。携城G_Still71及SE_G012，普通人物／登庸通知沿无音效渠道；完整历史／官渡／敌方登庸专用剧情不据此宣称接入。中止／撤回、移动／归队及取消赠与载荷共九必需消息角色；取消赠与由返程任务持钱粮、到达报告实际入库／溢出，改道／恢复不重结算，正常拒收仍谈判退款，后置零天归队不重复普通到达。证据见[RE-0110](../research/findings/RE-SAN9PK-0110.md#2026-10-08人才命令界面军师推荐与声音接入)、[RE-0111](../research/findings/RE-SAN9PK-0111.md#2026-10-08登庸准备与结果表现)、[RE-0112](../research/findings/RE-SAN9PK-0112.md#2026-10-23任务中止到达与归队的消息渠道)。

## 军事下令反馈（2026-10-09）

presentation.military声明出阵、增援、建设、输送、攻击、进军、交战撤退及调整方针八角色和transport-redirect；demolition声明确认／对白／结果声。成功后按真实主将／前后事实保留一次消息，SE_C007，撤除SE_M005；取消／拒绝不播。改令保留部队／载荷、不重扣资源，显式重编客户端仍未开放。military-target.*接受真实部队／设施，不造假守军；目标／目的地未变用调整对白，换进军目标按提交前交战查询区分进军／撤退，受话人分别为设施主官／势力君主。

历史真实包135次消息求值覆盖男女、两目标与随机分支；默认4171后审核4184／Headless17项通过，媒体与源哈希一致，未画面／试听验收。源证据见[RE-0137](../research/findings/RE-SAN9PK-0137.md#军事下令对白与音效2026-10-09)，不再在本页累计命令UI修复过程。

### 环境设定接入（2026-10-11）

environment-label-*三片不透明底板、environment-choice-*四态三片逐色Alpha，对应G_Common131..133／118..120、palette13；既有窗口／背景／标题／按钮复用。消息等级仅影响已产生消息的显示，不改模拟／保存，见[用户设置](user-settings.md)、[RE-0065](../research/findings/RE-SAN9PK-0065.md)。

功能菜单提示由消息表转换为 `ui.hint.functions-*`，共 11 项，与已有主按钮 `ui.hint.functions` 共用文本目录；新增键需重新运行 `import-client`，不需要重建贴图。证据见 [RE-0083](../research/findings/RE-SAN9PK-0083.md#2026-10-11功能菜单提示文本复核)。

## 阵形主动兵法与被动反制契约

ConfusionResistanceChance与BypassesPassiveCounters属于FormationRule、均必填；Tactics=null只表示无主动机会，不能抹去被动防御。TC101建设／输送及水形态不生成主动配置，源适配掩码完整保留，公共发动资格另验用途／状态。个人OpportunityChance与可空AutomaticOpportunityChance分开：非空先试自动、失败才个人，自动集合非空须声明机会、机会允许空集合；TC101个人15%、先行船10%，独立支援保持。全局military.defensiveTacticChance统一部队／设施，TC10150%，不保留阵型counterChance。显示／资格见[兵法规格](../specs/behavior/BEH-WAR-0005-tactics.md#阵形资格与装备投影)、[机会链](../specs/behavior/BEH-WAR-0005-tactics.md#已接入的武力机会链)。

### 2026-10-17导航占地契约

SoldierFootprints为必填升序兵力档，基础Footprint用于未命中及固定尺寸。TC101陆4..10细单位七档，水按船固定4/6/8/10，28阵型不裁；Core派生碰撞，不读显示布局。导航架构／随机隔离／保留地形时间见[寻路设计](../specs/navigation-redesign.md)。

### v143 接触与交战状态契约

当前接触及主动／被动交战属于领域状态，Lookahead只存坐标，不存目标几何；新局不猜源未提供的运行命令／历史，单位空接触／未交战、设施无攻击者。保存由当前格式严格读取，具体时序沿移动规格。

### 单挑演出内容接入

Battle.Duel声明男女／左右动作、准备序列、助阵、按地形／季节背景、必杀／胜负、消息／声及必填旗位／局部粒子；Interface为原生面板／头像／文字／血条／三段对白框／染色，局部效果显式Additive/Opacity，label.health必需。Source映射由Importer展开，旗复用地图动画，强击／欢呼／失意复用G_Ikki；正常／跳过／关闭使用同一领域结果。

G_Ikki背景／对白／面板保留逐色Alpha，地图染色#072746、179/256，不加模糊；G_ZoomIn0..57和141/142保留逐色Alpha，其他维持各自混合，特写下地图染#101922、217/256，不垫不透明暗底或把黑像素抠透明。相应纹理变化分别重建library/g_ikki／library/g_zoomin，语义布局／染色变更再转包。历史单挑31424逻辑／5276物理、背景93档Alpha／Importer450项是源转换与静态合成，非Godot验收，见[RE-0194](../research/findings/RE-SAN9PK-0194.md#原作截图后的背景渐隐复核)。

## 进行记录文本页（2026-10-18）

progress-record-side及九片progress-record-*来自G_Common52／323..331，按绑定调色板保留Alpha；复用背景／金边／标题／页签／关闭。布局、筛选与范围见[进行记录](ui/map-messages.md#p2完成范围与使用限制)，记录／喊话的源调用见[RE-0183](../research/findings/RE-SAN9PK-0183.md)，计略现场见[RE-0184](../research/findings/RE-SAN9PK-0184.md)。

## 年表输入按钮资源（2026-10-18）

detail-chronicle-input为G_Common304／palette38四态，当前仅展示禁用态；年表共用九片正文／背景／金边／标题／关闭，按自身宽拼、不拉端帽，输入点击不由本资源接入证明。

## 年表持久化与消息接入（2026-10-18）

presentation.chronicle包含98原始角色、通用继位模板、六底栏提示／两页帮助；年表有独立持久事实，进行记录只属客户端会话，两者边界见[统一消息](../specs/behavior/BEH-UI-0006-unified-messages.md#年表历史与进行记录的边界)。详细级记录含第三方易主、工作物破坏、部队溃灭及activation-other；创建时定等级，客户端筛选。

建设、联盟变化、处斩、请求结束分别保存发生时归属／类型／城市／位置、旧联盟／移除原因／背盟方、身份／执行君主／路径、失效原因；年表依事实顺序入册、来源色独立档案所有者，不追溯改写已存记录。其他恢复／响应／成长／密商／战后反馈的当前角色及边界见[RE-0198](../research/findings/RE-SAN9PK-0198.md#f01f15实施收口2026-10-20)、[RE-0185](../research/findings/RE-SAN9PK-0185.md)。

## 水面近战接触规则（2026-10-19）

requiresWaterContact按建筑实际占地及水侧边界判近战，替代requiresWaterOrHarbor，不靠港口类型；源补丁线索与重制裁决见[普通战斗](../specs/behavior/BEH-WAR-0004-normal-combat.md#接敌距离与移动)。几何缓存随更新，岸线／边缘／阻断角、其他建筑阻挡／拆除及导航／战斗均有回归；这不是可见界面验收。内容字段变更重转剧本、不建纹理。

## 进行面动态音乐（2026-10-19）

mapAudio.executionMusic声明普通／高爵位战斗、普通／高爵位进军、内政五角色，TC101用BGM_12..16，按玩家野战交战／军事活动选择；战略季节／爵位曲独立，见[地图音乐](ui/strategy-ui.md#地图音乐与进行面声音)、[基础声音证据](../research/findings/RE-SAN9PK-0186.md)。event-court-music内部20实为BGM_21、共用audio/music/diplomacy，BGM_20资产仍保留；循环会谈与单次插图必须由消息明确区分，完整证据见[RE-0197](../research/findings/RE-SAN9PK-0197.md)。

## 攻占／失守插图与音乐（2026-10-19）

presentation.capture必填可空，声明胜负及跨势力接收消息、按设施类型的插图／专曲开关、胜负曲和占领声。TC101消息7387/7389及转属7388/7390、G_Still69/70、BGM_24/25及se_m006覆盖17类，源编号只在Importer。跨势力转属消息为必需绑定，旧包需重转剧本内容，复用纹理和音频。按[世界事件](../specs/behavior/BEH-UI-0010-world-event-presentation.md#攻占失守场景2026-10-19)分有主城／空城／其他设施／毁坏／第三方，专曲单次后回地图。历史190包31412逻辑／5271物理及25势力／94据点／700人物、两图／两曲／声引用通过，非实际插图／听感验收。

## 其他表现契约与最小重建范围

| 内容变化 | 契约／最小范围 | 行为与证据 |
| --- | --- | --- |
| 开局地图／设定美术 | library/g_mmap、library/g_start2（212源图）、library/g_strtpk（19源图），再集中import-content --rebuild opening；不导世界 | [新游戏](../specs/behavior/BEH-UI-0011-new-game.md) |
| 地图多旗／普通攻击 | map；实例／世界／音频不变 | [RE-0156](../research/findings/RE-SAN9PK-0156.md#同日实现水面地图挂旗与普通攻击) |
| 外交普通会谈 | presentation.diplomacy及消息，图／阴影逐色Alpha变化建library/g_gaikou，再转包；四答复／条件／接待意见只转包 | [RE-0182](../research/findings/RE-SAN9PK-0182.md#17-外交会谈分层与动作2026-10-22)、[接待意见](../research/findings/RE-SAN9PK-0182.md#23-普通外交会谈全面复核2026-10-22) |
| 楼船与兵法身份绑定 | 转包、不建纹理；双方实际阵型各自选身体，七水阵覆盖；攻心25／幻术26／辱骂27分开 | [楼船纠正](../research/findings/RE-SAN9PK-0192.md#2026-10-22-楼船来源编队遗漏纠正)、[身份纠正](../research/findings/RE-SAN9PK-0192.md#2026-10-20-攻心幻术与辱骂的身份映射纠正) |
| 世界事件／兵法／消息 | presentation.events、battle、missions、progress各语义角色；数字／状态／武将／箭簇按内容声明，消息闭包校验；仅引用／参数变化转包 | [兵法表现](../specs/behavior/BEH-UI-0008-battle-presentation.md)、[世界事件](../specs/behavior/BEH-UI-0010-world-event-presentation.md) |
| 结局美术／布局 | library/g_ending、skin，语义布局变化再转包；其他地图／媒体复用 | [RE-0181](../research/findings/RE-SAN9PK-0181.md#结局专用场景接入与收尾2026-10-23) |
| 编辑器／快捷菜单 | skin图像变化重建；ui.editor.confirm-discard／取消五类状态提示／ui.hint.functions-*等词条变化转包 | [编辑器](../specs/behavior/BEH-EDITOR-0001-world-editors.md)、[RE-0199](../research/findings/RE-SAN9PK-0199.md)、[功能提示](../research/findings/RE-SAN9PK-0083.md#2026-10-11功能菜单提示文本复核) |
| 多玩家／退出托管 | 外交不确定／反要约意见、presentation.exitMenu九消息角色，姓名以人物引用保留样式；不转图／声 | [新游戏](../specs/behavior/BEH-UI-0011-new-game.md)、[用户设置](user-settings.md) |

皮肤还声明minimap-command-pulse／minimap-record-pulse各自五帧及消息／喊话布局与颜色，红蓝目标箭四帧、BuildingArrowOffsets、information-summary／information-map-selected；命令小样式按内容宽<416或高<312选择，大小均背景270、保留透明留白／调色板。光圈来源见[RE-0158](../research/findings/RE-SAN9PK-0158.md#小地图命令来源光圈补证2026-09-27)，资源角色与重建入口见[纹理资源](texture-resources.md)；编译或引用检查不等于交互／画面验收。
