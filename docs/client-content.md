# 客户端内容管线

本页维护内容组织、创作／导入边界、转换、校验和发布。正常游戏只读取转换包；发行版内容准备入口可调用Importer读取玩家选择的原版文件。Core、Application及普通客户端构建不解释EXE、S9或IDA数据。

## 编辑入口与生效范围

| 入口 | 持有与发布内容 | 详细约定 |
| --- | --- | --- |
| 标题菜单“编辑游戏资料”或`--shared-data-editor` | 各页共用草稿；保存完整`definitions.player.json`及`officer-defaults.player.json`，基础文件保留。加载优先玩家副本，再应用剧本差异；新局生效，不修改已有对局，副本不自动继承基础更新 | [资料编辑器](shared-data-editor.md)、[共享创作](../specs/behavior/BEH-CONTENT-0001-shared-scenario-authoring.md) |
| 标题菜单“登录新武将”或`--officer-editor` | 用户目录的`created-officers.json`；只保存创作资料与头像引用，不创建世界、不读地图／音频或参考剧本。通过`catalog.json`读取共享资料并优先玩家副本 | [新武将规格](../specs/behavior/BEH-EDITOR-0002-created-officers.md) |
| 主菜单“编辑头像” | 新增用户头像，供相同选择器引用；人物库不内嵌图片，缺头像不阻止其他资料编辑 | [头像编辑器](shared-data-editor.md#头像编辑器) |

用户目录可用`--user-data-root`隔离。共享人物的新建／复制／删除只改共享资料，不自动部署；新局部署消费独立人物库。可空`PortraitKey`和身份高水位`nextOfficerId`随人物资料维护，删除仅限自建项并检查全部引用。

## 剧本目录与共享内容

内容根为`local/generated/content`：`catalog.json`只索引剧本ID与包路径；`shared/`拥有规则、人物、地理、纹理、消息、媒体和开局资源；剧本位于`scenarios/historical/`或`scenarios/fictional/`。TC101的20个正式剧本完整转换，一般／if分类由定义声明，不由客户端猜源编号。

每个`content.json`提供轻量`preview`，显式引用`definitions`、`officerDefaults`、`geography`及剧本地图、初始状态、文本、消息、插图和表现数据。浏览目录不解析全部世界；预览只保存静态年月、标题、简介和初始城市颜色，不保存势力、兵力、人口、账户、候选或经济预测等派生查询。开局读取及失败取消见[新游戏](../specs/behavior/BEH-UI-0011-new-game.md)。

| 文件／定义 | 所有权与约束 |
| --- | --- |
| `shared/definitions.json`、`shared/officer-defaults.json` | 规则／目录与人物基础资料；名称、说明、姓名、字、列传归有效定义，静态文本不再复制 |
| `shared/maps/china/geography.json` | 领域地理；不依赖皮肤、名称或势力摘要 |
| 剧本`scenario.json` | `ScenarioDocument`创作契约，保存初始状态、额外人物及类型化差异，组合为`ScenarioInitialState`。四维逐项继承，未覆盖、显式null和空集合分别表达；共享库成员不自动进入世界 |
| 剧本`map.json` | 引用共享地图定义，仅保存建筑稳定身份、位置与固定类型；共享定义保存方格、地形、桥／云／水面／边界及建筑组合 |
| `shared/opening.json`与入口`preview` | 共用城市中心／逐行区间分区，绘图和命中同源；页面只声明投影和美术，不维护第二套城市范围。资源角色见[开局显示契约](ui/presentation-content.md#开局与目录资源) |
| `resourceRoot`及纹理依赖 | 资源根必填；数据／音频相对根，纹理依赖相对包目录，路径不得逃出边界。公共文件显式归shared，不按哈希或引用次数自动搬迁 |

`import-content`发布共享内容与全部正式剧本，`import-client`转换显式独立包。`PlayerForceId`必填可空，没有默认190年或董卓。Importer只为TC101初始内容按公共规则重建源大将标记，独立作者的显式任命和存档恢复不重算。

## 现行版本与历史记录

版本只在下列权威定义集中维护；内容不匹配按对应格式／结构／引用校验拒绝，旧包按实际失效范围原位重转，不做运行时兼容补值。规则版本差异与格式不兼容分开处理，规则、存档和命令脚本策略以SessionProtocol为准。录像已延期撤下，旧记录中的录像验收仅是历史事实。

| 契约 | 权威定义 |
| --- | --- |
| 剧本创作／共享定义／地理 | [ScenarioDocument／ScenarioDefinitions／ScenarioGeography](../src/San9.Content/Scenarios/ScenarioAuthoring.cs) |
| 人物默认资料 | [OfficerDefaultsCatalog](../src/San9.Content/Packages/ScenarioDataFiles.cs) |
| 组合后的会话初始状态 | [ScenarioInitialState.CurrentSchemaVersion](../src/San9.Content/Scenarios/ScenarioInitialState.cs) |
| 组合包／共享地图定义 | [ScenarioContent／MapDefinition](../src/San9.Content/Packages/ScenarioContent.cs) |
| 地图资源／文本／皮肤 | [MapPackage](../src/San9.Content/Maps/MapPackage.cs)、[TextCatalog](../src/San9.Content/Text/TextCatalog.cs)、[InterfaceSkin](../src/San9.Content/Interface/InterfaceSkin.cs) |
| 完整纹理转换 | [TexturePackageImporter.ConversionVersion](../src/San9.Importer.San9PkTc101/Packages/TexturePackageImporter.cs)；定向重建保留原包全局版本 |
| 会话规则／存档／日志 | [SessionProtocol](../src/San9.Application/SessionProtocol.cs) |

不按某个历史包号判断今天是否需要转换。新增必需字段、引用或转换规则后，依[转换与运行](#转换与运行)选择实际受影响单元；仅消费者／文档变化复用已有包。阶段内容核对见[已有批次](#已有批次核对记录)和[G10验收](history/stages/g10-verification.md#2026-09-21阶段收口)。

## 职责与文件

| 模块 | 职责 |
| --- | --- |
| Content.Maps／MapImporter | 地图定义和剧本建筑实例，不含图片 |
| Content.Scenarios／ScenarioContentCompiler | 共享规则、人物资料、领域地理与剧本差异，组合会话输入 |
| Content.Text／ScenarioTextReader／PublicationMessageCatalog | 静态文本及按用途分类、保留原键的消息库 |
| Content.Interface／InterfaceSkinCompiler | 皮肤语义角色、颜色和装饰 |
| Content.Packages／ClientContentCompiler／ClientContentImporter | 编译与文件发布分离，入口、依赖和组合校验 |
| TexturePackageImporter | 独立共享纹理／图集／地图资源，物理文件集中到`files`；导入来源以SHA-256和长度登记 |
| Importer.Containers.Ls11Archive | 仅解码LS11，不解释名称或地图语义 |

共享创作不继承原版身份区间、槽位、姓名长度或15州上限。城市资格由`SettlementKind.City`决定，`RegionCityIds`引用真实城市，名称从据点定义读取；源分组与编号转换留Importer。基础版和PK合并为统一类别与稳定引用，运行时不建立来源专属集合或`isPk`行为分支。来源身份可登记追踪，但不决定可见性和可用性，支持范围仍须明确。

TC101地域记录双坐标均为无效哨兵时不生成对象；单轴无效或越界直接报错。有效未建设地域完整保留且稳定身份不重排，不通过丢弃引用目标掩盖错误。完整游戏地图必须覆盖所有可到达中心方格的地域映射并可解析人物地点；底层可空字段不保证此内容可用于完整游戏，见[地域前提](../specs/behavior/BEH-WAR-0002-building-defeat-and-capture.md#人物安置的地域前提)。

## 领域输入与派生数据

| 输入 | 作者／转换职责与运行边界 |
| --- | --- |
| 联盟与关系 | 必填可空`Alliance`持有目标、盟主、有序成员和剩余旬数；源成员／期限矛盾拒绝。世界从初始值独立演进，恢复不重新扣旬 |
| 人物身份与忠诚 | `StatusStartYear`为当前身份起始年，在野也可有值；发现／登用时更新。共享`DefaultLoyalty`只保存一次（TC101为50），`ForceLoyalties`仅保存差异，显式0保留；默认值不复制进存档，缺项按当前内容规则解释 |
| 阵形与部队 | `Positions`保存列和`BattleRank`的完整默认位置，人物映射到具体位置，行位由此派生；旧行位不能补算缺失的列。计略减半、免疫、基础船和固定用途通过属性及引用表达，不读源阵形编号 |
| 地域与州 | `RegionIds`按格引用地域，`RegionCityIds`另保存地域关联城市，回收走“格→地域→城市”，不按当地建筑现归属改选；`CityProvinceIds`可省略某城市的州 |
| 地理连接 | `TerritoryExits`按行优先保存每格北／东／南／西出口。非空必须覆盖全图，空明确表示未提供；Core由不可变地图边及当前对象状态派生资格，作者不维护逐势力邻接表 |
| 军事交通 | 不保存军事城市邻接字段；会话按当前物理据点、地图及固定参考移动条件建立60日有向静态网。几何变化重建，归属变化只更新策略解释；不进入存档，见[交通设计](../specs/original-ai/transport-network-design.md) |
| 军事与感知 | 导入完整地形、阻断、移动预算、footprint；`FormationRule.Vision`及`VisionSizeBands`分别声明基础与兵力附加感知，独立于显示布局及物理占地 |
| AI输入 | 调度标识、家族／君主偏好、工作门、阵形角色及船／设施／地形引用由Importer转换，动态势力分配并保存调度标识；算法变化不自动触发贴图重建 |
| 陷阱地形 | `TerrainIds`与行优先`Elevations`覆盖地图；`TrapTerrains`声明可空`BaseTacticId`及`ProvidesHigherGround`，可空`HigherGroundTrap`声明兵法和`RadiusCells`。地形规则覆盖所有地形，效果引用具体陷阱；不维护逐格派生结果 |
| 武力接触与捕获 | 可空正整数`ContactRange`只用于武力兵法，将来源占地两轴外扩后与目标严格相交，边缘相碰不满足；TC101六种步／骑兵法为5，其余空。`DoublesCapture`为阵形显式加倍属性，TC101仅鹤翼启用 |
| 水面近战接触 | `requiresWaterContact`按建筑实际占地及水侧边界判定，不按港口类型或显示资源推断；规则与几何见[普通战斗](../specs/behavior/BEH-WAR-0004-normal-combat.md#接敌距离与移动) |
| 异常与兵法机会 | 可空`UnformedFormationId`引用具有Combat的阵形，null不替换；TC101指定异常配置，Core统一消费攻防／削墙／伤兵参数。`ConfusionResistanceChance`与`BypassesPassiveCounters`独立于可空主动`Tactics`，无主动机会不抹去被动防御 |
| 个人／自动机会 | `OpportunityChance`与可空`AutomaticOpportunityChance`分开，非空先试自动、失败才个人；自动集合非空须声明机会，机会可配空集合。`defensiveTacticChance`统一部队／设施；TC101个人15%、先行船10%、防守50%，建设／输送／水形态不生成主动配置 |
| 兵力占地与运行历史 | `SoldierFootprints`为升序兵力档，基础Footprint用于未命中和固定尺寸；TC101陆4～10七档，水按船4／6／8／10。`CommandedThisXun`、`HasEnteredDailyCycle`、等待、中继、前瞻和接触／交战分别持有，显示资源不能替代它们 |

共享纹理只保存已支持的最终资源，剧本引用目录和逻辑ID，不带重复`images`目录；不保留运行时未消费的索引图／原始调色板副本。图片、图集、缓存和租约见[纹理资源](texture-resources.md)。静态`text.entries`直接映射键到显示字符串，设施名保存组合后缀后的最终正文，地域基础名独立；源字节和偏移只留源档案与研究。

陷阱实时查询优先选择范围内更高且有高处资格的邻格，否则取目标地形基础陷阱；距离为向下取整欧氏格距离不超过半径，即平方距离严格小于`(RadiusCells+1)²`。高度共用尺度，不按原地形或兵法编号判断。制作工具可复用查询预览；内容修改后重新加载，无游戏中地形编辑。规则细节见[兵法规格](../specs/behavior/BEH-WAR-0005-tactics.md)。

## 接入前的数据核对

新增运行对象或改变导入／筛选范围时，明确受影响版本、集合及依赖，区分有效对象、未启用槽和未支持扩展：

| 层次 | 核对内容 |
| --- | --- |
| 源数据 | 定义与槽数、稳定身份、状态分布、持有人／地点引用、完整文本／图片来源 |
| 转换包 | 身份与资源完整、初始状态保留、缺失／排除原因明确、引用闭合 |
| 世界 | 建局对象仍存在，初始态独立复制，当前归属／可见性／可用性所有者明确 |
| 查询 | 按当前快照筛选；出土、登场、易主后能消费已有定义与资源 |

比较身份集合和关键字段，不只比较总数或首屏。输入及相关规则未变的已通过核对复用，包号递增本身不使未受影响集合失效；历史批次结果按各验收记录的日期及边界解释。

### 已有批次核对记录

开局输入见[开局专项](../research/topics/scenario-startup.md)，经济／军团见[G03](history/stages/g03-verification.md)，兵法和战后见[G07](history/stages/g07-verification.md)，外交／地域见[G08](history/stages/g08-verification.md)，人物与单挑见[G09](history/stages/g09-verification.md)，事件见[覆盖表](history/stages/g10-event-coverage.md)和[G10](history/stages/g10-verification.md)。不在本页复制历史包号、测试数量或生成物清单。

## 导入编译与发布边界

一次导入由`San9ImportSource`持有只读源字节、剧本解析节和消息编译结果；全剧本共用EXE、消息、纹理清单和音频索引，剧本消息绑定逐剧本完成。每剧本暂存后释放编译世界／解析节，不建立跨进程或长期源缓存。

公共入口持有输出写锁，父目录与资源子目录转换互斥；协调入口显式授权内部资源阶段，玩家完整转换持锁至最终校验。`ContentImportProgress`传递阶段与正文，界面不解析日志前缀；源消息与剧本转换支持协作取消。

`ClientContentCompiler`生成并校验结果，`ClientContentImporter`与`ContentPublication`负责整批暂存和发布。编译先校验全部依赖并暂存输出，再逐文件替换、最后发布清单；失败恢复已替换文件，不声称整个目录原子更新。旧资源仅在成功提交后清理；纹理和媒体是独立发布阶段，完整玩家转换不构成跨阶段整目录事务。纹理输出不比较旧PNG、不备份或整批回滚；文件逐个替换、清单最后发布，失败后通过重试转换恢复。禁止同目录并发导入或客户端热加载。数据JSON保留完整字段名，子文件紧凑、清单缩进；仅省略消息表达式／参数中默认null的可选字段，必填nullable状态仍显式保存。

定向纹理重建只更新所选单元，保留整包`Sources`与转换版本；不得用局部读取的新来源戳替整包背书。普通导入按源内容SHA-256、长度和转换版本识别失效，不以修改时间判定；旧纹理包缺少源指纹时需完整重建，局部重建不得为保留单元补造指纹。内容变化的最小范围如下；具体工具选项见下一节：

| 变化 | 重建范围 |
| --- | --- |
| 定义、人物默认资料、世界初始态或地理契约 | `import-content --rebuild worlds`；现有工具不能定向处理的世界变化才完整转换，复用纹理 |
| 开局预览、城市范围、头像／旗字目录或开局资源引用 | `import-content --rebuild opening`，不重建未变世界 |
| 命令界面词条 `ui.commands.*` | `import-content --rebuild command-text`，只更新现有同格式包的静态文本；不编译世界、消息图、表现或纹理 |
| 其他消息、表现绑定或静态文本 | 更新受影响数据及共享消息；未变图片、音频、影片复用 |
| 皮肤、地图或指定素材图集 | 对应`import-textures --rebuild`单元；语义引用随契约需要更新数据 |
| 消费者逻辑、文档或版本号变化但产物内容未失效 | 不转换 |

## 校验职责

`verify-content`、`verify-startup`、`verify-textures`、`verify-client`四个手动命令默认不调用。仅在用户明确要求，或已有具体异常、失败证据确有必要排查时，选择与问题相符的入口和最小范围；不作为开发、转换、提交或交付后的固定步骤，不为“更放心”追加执行。转换器内部的契约校验、纹理发布／复用检查及玩家完整导入末尾的包检查仍按产品流程执行，不因此再手动运行一遍。

| 手动入口 | 用户未明确要求时，确有必要排查的情形 |
| --- | --- |
| `verify-content` | 已有目录包加载失败，需定位清单、依赖或跨文件引用问题 |
| `verify-startup` | 出现开局失败、特定势力／模式的初始化或开局介绍异常；优先用`--scenario`限定问题剧本 |
| `verify-textures` | 图片解码失败，或有具体证据怀疑PNG损坏、格式／尺寸与清单不符 |
| `verify-client` | 单个独立剧本包的加载或图片依赖异常，需要联合检查该包及其引用图片 |

转换器在发布前检查编译结果的结构、契约及引用，独立 `verify-content` 回读落盘包，确认清单、依赖、预览、音视频引用及纹理文件存在，不创建游戏会话。共享音频目录在一次校验内只检查一次。纹理Importer复用及发布时只检查文件存在且非空，转换后不解码PNG；`verify-textures`和`verify-client`提供手动排查用的图片检查。玩家内容准备流程调用相同的包检查入口，不把全部势力开局作为转换成功条件。

`verify-startup` 是显式运行验证：创建会话核对初始身份及目录查询，并遍历所选剧本的所有可选势力与史实／假想模式，检查开局介绍；它不再由普通内容转换或包校验隐式调用。交通网在AI首次实际查询时完整构建并随成功操作保留，不在开场介绍、读档或普通设置操作中无条件构建。

`--rebuild command-text`只转换公共命令词条，复用EXE／消息文件中的原作标签、提示及重制词条输入，替换`ui.commands.*`范围，保留其他静态文本、人物资料和消息标签。相关文本全批暂存后发布，任一包失败则不写回，内容未变化时不重写文件；不读取世界、图片、音频或消息图，不自动追加完整包或开局回归。

## 转换与运行

普通`import-content`、`import-client`及其局部重建默认不复制或转换BGM、音效和视频，只生成声音引用并复用最终包已有媒体。声音使用显式`import-audio --game-dir <原版目录> --output <内容根目录> --group bgm|effects|all`，默认all；BGM由用户环境PATH中的FFmpeg转换为Vorbis Q5，音效按原格式复制。找不到FFmpeg时跳过BGM和视频转码，音效复制不受影响；未安装声音可静音运行；已登记声音仍须通过资源校验。详细约定见[声音导入](audio-preparation.md)。

电影采用独立可选的`shared/movies/catalog.json`（schema1），当前接入14段事件电影、20个剧本开场及theme／op_pk两段启动影片，不含logo。显式`import-movies --game-dir <原版目录> --output <内容根目录> --group events|scenarios|startup|all`直接转码选定组，默认all，保留其他绑定。转换后的媒体直接进入最终内容包；源／参数／输出哈希记录用于复用，临时文件自动清理，不保留独立中间视频库。剧本通过自己的`presentation.json`中可选的`openingMovie`显式引用开场电影角色，未配置／null则不播放；客户端不由剧本ID推导，多个剧本可复用同一影片。仅新游戏播放一次，读档／控制交接不重播。参数、设置与手动验收见[电影准备与接入](movie-preparation.md)。

转换入口统一`--game-dir`，目录须直接含San9PK.exe及原版S9；`--scenario`只接受该目录中的剧本文件名，如D_Sce002.S9，不接受另一路径。San9SourceDirectory负责同版本身份与缺文件检查，不支持旧`--source-exe`／`--data-dir`。私有输入为`private/game-files`，目录内DLL仅研究、不参与导入或运行加载。

以下是按需转换与运行入口，不是每次开发或交付的执行清单。先按[工程流程](development/engineering-workflow.md#变更流程)判断已有产物是否失效；相关内容改动收敛后集中更新，纯消费者代码和文档改动复用已有包。

只改变剧本世界规则或创作契约时，可用`import-content --game-dir "private/game-files" --output "local/generated/content" --rebuild worlds`从源重编目录中的剧本世界，共享定义／人物默认资料／领域地理和全部scenario.json一起暂存、校验并发布，文件替换失败回滚整批，最后更新入口版本。地图显示、文本、表现及纹理资源复用原文件；该入口不启动游戏。共享基准由TC101 Importer显式选择剧本0，各剧本差异完整保留。版本以[现行版本](#现行版本与历史记录)的权威定义为准；世界必填`OfficerGrowth.MaximumBaseAbility`和`OfficerGrowth.TacticProficiencyCap`，TC101导入值分别为100和1000；熟练度上限为九类共用的正整数，由游戏资料编辑器“基础规则”页编辑，所有熟练度成长和武将编辑共同消费，已有超限值保留；宿主提供的021数值配置作为当前公共上限，普通／强制成长和武将编辑共同消费。军事定义新增必填可空的`CompanionTactics`，显式声明041设施兵法、042舰船后备兵法和043舰船禁用项；原始定义保持不变。蔡琰的能力资格上界、祢衡的固定智力分流阈值已从事件契约移除；20个正式剧本世界已重建，地图和表现资源复用。优化伴侣的独立战斗预设由版本Importer解释阵型与技术身份，契约及本次接入范围见[兵法规则](../specs/behavior/BEH-COMPANION-0003-tactic-rules.md#内容与协议)。

情报定义必填`Information.ShowOfficerAges`独立声明年龄可见性：TC101模式1为false，其余为true；武将一览与详情关闭时显示`**`且不提供年龄数值排序键，不影响出生年、登场、自然死亡或伴侣“无限寿命”。20个正式剧本已原位重建，逐项核对源模式与该字段，并确认人物集合和出生年保持不变；报告`local/generated/verification/officer-age-display.json`。采用行为见[情报规格](../specs/behavior/BEH-UI-0004-information-browser.md#状态与不变量)。

从仓库根运行、写回原目录；只有独立对照才留副本。import-content负责目录型发布，支持opening、worlds和command-text三个定向重建单元；import-client会重新生成一个剧本包全部JSON并复制已准备音频，没有局部／增量入口；两者都只引用共享纹理。原位不等于增量，相关改动收敛后集中执行一次，不为未变化产物反复转换。

```powershell
dotnet run --project src/San9.Tools -- import-textures --game-dir "private/game-files" --output "local/generated/content/shared/textures"
dotnet run --project src/San9.Tools -- import-content --game-dir "private/game-files" --output "local/generated/content"
# 仅更新命令界面词条（需现有包格式匹配；不用于升级世界／表现格式）。
dotnet run --project src/San9.Tools -- import-content --game-dir "private/game-files" --output "local/generated/content" --rebuild command-text
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot
# 显式直接进入某个剧本及玩家势力：
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --scenario 4 --player-force 6
```
独立包（不发布目录）示例：

```powershell
dotnet run --project src/San9.Tools -- import-client --game-dir "private/game-files" --scenario D_Sce002.S9 --texture-package "local/generated/content/shared/textures" --output "local/generated/content/scenario-190-live"
```

以下手动排查示例默认不执行，仅按[校验职责](#校验职责)中的触发条件选择所需命令，不串联全跑：

```powershell
dotnet run --project src/San9.Tools -- verify-content --content-root "local/generated/content"
dotnet run --project src/San9.Tools -- verify-startup --content-root "local/generated/content" --scenario 0
dotnet run --project src/San9.Tools -- verify-textures --texture-package "local/generated/content/shared/textures"
dotnet run --project src/San9.Tools -- verify-client --content-package "local/generated/content/scenario-190-live"
```

修正解析规则或补齐遗漏资源时，给 `import-textures` 加 `--rebuild`，直接按当前代码重转指定单元，不以原始文件变化为触发条件，也不创建持久中间缓存：

```powershell
# 只重转光标；其他可选值见下表。
dotnet run --project src/San9.Tools -- import-textures --game-dir "private/game-files" --output "local/generated/content/shared/textures" --rebuild cursors
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

`library/g_start2`包含新势力按钮及头像框的调色板透明度；其重打包会改变开局内嵌皮肤的图片引用，随后用`import-content --rebuild opening`刷新这些引用。无需重转世界；已运行的客户端须重新启动以载入新纹理清单。

定向重建先从源生成选定单元，再反序列化组合，旧选定单元必需字段不是输入，未选单元仍严格读取；全包校验后发布。保留其他单元定义／图片，用images/rebuilt/<单元>/固定路径重打包该目录仍被引用图片并改引用；清单发布后清无引用图片和临时.sources/，不按像素合并不同规则。保留整包Sources／ConversionVersion，局部来源新戳不能替全包背书；全局格式／解码升级才用all。图片／皮肤修复不要求重转世界，数据／文本／引用生成变化才重转剧本包。

新局可追加`--computer-damage-bonus 100`指定电脑对玩家直接兵损+100%，默认0。设置随存档和录像保存，加载时不覆盖已有值；此参数不修改内容包。

客户端通过Godot的`template`特性区分开发与导出运行：开发读取`local/generated/content`；Windows／Linux导出版读取可执行文件旁的`content/`；macOS导出版读取系统“文稿”目录下的`San9Remake/content/`，通常为`~/Documents/San9Remake/content/`，不依赖`.app`的位置。无参数先进入开局菜单；`--content-root`指定其他目录，`--scenario ID`按目录定位明确剧本，`--content-package PATH`直接指定包，两种剧本选择互斥。发布包不附带转换内容，但内置转换器。启动加载失败时显示实际路径，并提供“选择目录”“开始转换”“打开目录”和“重新加载内容”；转换校验成功后可直接进入游戏，无需重启。也可手动复制完整内容目录，保留`catalog.json`、`shared/`和`scenarios/`的相对位置，再重新加载。存档位置见[用户数据目录](user-settings.md#用户数据目录)。离线地图使用`render-map --content-package <剧本包目录>`。正常游戏加载只读取转换结果；源资源与生成物不提交。

### 发行版内置转换入口

内容准备页的“开始转换”直接调用导出时引用的 Importer，在后台任务中执行，与游戏共用一份 .NET 运行时，不启动额外转换器进程，也不附带 `importer/` 目录。源格式解释仍封装在 Importer，正常游戏加载仍只消费转换包。当前仅支持繁体中文 PK 1.0.1.0 的已登记构建，原版目录须直接包含 `San9PK.exe` 和 S9，内容目录与原版目录不得相同或互相包含；始终写入界面显示的内容目录，保留源文件不变。

界面先选择原版目录、确认音频／影片选项，再点击“开始转换”；内容路径可选中复制，加载错误与转换日志分别折叠显示，成功后显示就绪状态。转换器先检查版本，再依次转换纹理、所选音频、全部剧本和所选影片，最后复用 `verify-content` 检查落盘包的结构与引用，不执行游戏开局回归或重复解码PNG。音频开关同时控制背景音乐与音效；关闭后不处理音频，保留已有文件，首次准备也允许不安装音频。影片可选，关闭后保留已有影片。至少选中一项媒体时才检查 FFmpeg：从系统 PATH 查找，也可在界面选择；要求 Vorbis 编码器，选中影片时还要求 Theora。两项都关闭不检查 FFmpeg；发行包不捆绑或下载 FFmpeg，需要转码却缺失或编码器不满足时报错，不跳过后宣告成功。

转换期间显示日志并禁用重复转换和内容加载，允许取消；关闭客户端也会请求取消转换任务及活动转码。各阶段原位写入，目录型剧本发布为统一批次，纹理和媒体分别提交；失败或取消可能保留此前已完成阶段的产物，允许重试，不清空目录或删除玩家文件。完整校验通过且任务正常结束才显示“进入游戏”。所有公共导入入口对输出目录及其资源子目录互斥；玩家入口持锁至最终校验结束。

普通 Debug／Release 构建不引用 Importer，界面不启用内置转换，继续使用现有开发命令，也可显式调用完整流程：

```powershell
dotnet run --project src/San9.Tools -- prepare-content --game-dir "private/game-files" --output "local/generated/content" --audio true --movies true
# PATH 中没有 FFmpeg 时，追加 --ffmpeg "<ffmpeg 可执行文件绝对路径>"
```

`--audio` 和 `--movies` 均默认 `true`，分别控制音频（音乐与音效）和影片，传 `false` 保留已有媒体而不转换；命令行失败返回 1，取消返回 130，可用 Ctrl+C。界面直接传递结构化阶段进度与取消令牌，不使用标准输入／输出进程协议。显式选择的 FFmpeg 路径直接交给媒体导入器，不修改客户端的 PATH。

## 桌面导出与自动发布

本地导出使用 PowerShell 7，在对应平台的机器上先准备依赖，再导出指定平台：

```powershell
$godotPath = ./tools/setup-release.ps1 -Platform windows
./tools/export-client.ps1 -Platform windows -GodotPath $godotPath
```

`-Platform` 支持 `windows`、`linux`、`macos`。编辑器固定为 Godot 4.7.2 .NET，字体固定为霞鹜文楷等宽 GB Lite v1.522；导出模板每次准备时解析 [godot-template 最新正式 Release](https://github.com/cdd1037/godot-template/releases/latest)，按该 Release 资产的 SHA256 digest 校验并更新 `templates/current/`。自动发布在 prepare 阶段统一解析 Release ID，三个平台使用同一 Release，并在日志中记录版本与来源；本地可用 `-TemplateReleaseId` 指定同一 Release。所有下载均校验 SHA256；编辑器与模板放在忽略的 `local/generated/release-tools/`，字体放入已有忽略路径。macOS 自定义模板保持 ZIP 原样。Windows 同时下载并保留 `.console.exe` 模板包装器，但不导出额外控制台包装器。

全新检出没有字体导入缓存，而 Godot 在导入开始前会初始化全局字体。导出脚本先使用不加载全局字体的工程配置完成资源导入，再按原配置导出；导入成功或失败都按原始字节恢复 `project.godot`，保留本次构建版本号及作者设置。导入与导出均检查退出码及错误日志，不依赖已有 `.godot/` 缓存。

Windows 构建直接启动编辑器主 EXE，输出重定向到日志；不使用 `_console.exe` 编辑器包装器，因为包装器会等待整个子进程组，导致常驻 MSBuild 工作进程阻止导出步骤结束。

Godot 项目名称统一为 `San9R`。输出分别为 `local/generated/export/windows-x64/San9R.exe`、`linux-x64/San9R.x86_64` 与 `macos-arm64/San9R.zip`，macOS ZIP 内为 `San9R.app`，主程序名也为 `San9R`；日志保存在对应目录。Windows／Linux 是 x64；macOS 模板仅支持 Apple Silicon ARM64，不包含 Intel，最低系统版本按 [.NET 10 要求](https://learn.microsoft.com/en-us/dotnet/core/install/macos)设为 macOS 14。macOS ZIP 包含完整 `.app`，采用 ad-hoc 签名，不进行 Apple Developer ID 签名或公证。项目启用 ETC2 ASTC 纹理导入，满足 Godot 的 ARM64 导出检查。

Windows／Linux 将 PCK 和 .NET 发布产物嵌入可执行文件；macOS 按 Godot 标准布局将发布产物置于 `.app` 内。不启用 `.NET PublishSingleFile`（Godot C# 工程是类库），也不启用 Native AOT。`ExportRelease` 使用 `PublishTrimmed=true` 与 `TrimMode=partial`，保留游戏程序集、GodotSharp 和 JSON 反射所需的 `System.Collections.Immutable`，并启用原生宿主入口及 JSON 反射。裁剪告警保留在 Godot 的 MSBuild 日志中；当前配置不表示所有代码均可安全进行 full trimming。

脚本不检查、复制或打包转换资源。客户端仅在 `ExportRelease` 配置下引用 `San9.Importer.San9PkTc101`，将转换器及 Opencc 依赖与游戏一起发布，共用已有运行时；不单独发布 `San9.Tools`，也不附带第二份 .NET 框架文件。转换器程序集及其 Opencc 依赖保留，避免部分裁剪丢失反射所需代码。Windows／Linux 随游戏一起嵌入主程序，macOS 随标准发布产物置于 `.app` 内。运行时按上述默认目录放置资源，或通过 `San9R.exe -- --content-package "<剧本包绝对路径>"` 指定现有包。仅构建成功不能替代运行验证，headless 启动也不代表画面或完整玩法验收。

GitHub Actions 的 [Desktop release](../.github/workflows/release.yml) 在推送 `v*` 标签时自动构建，也可在 Actions 页面手动运行并填写 `version`（例如 `v0.1.0` 或 `v0.1.0-rc.1`）。手动指定已有标签时构建该标签的精确提交；标签不存在时构建所选分支／提交，三端成功后由发布任务创建标签和 Release。示例：

```powershell
git tag v0.1.0
git push origin v0.1.0
```

三端分别使用 Windows、Ubuntu、macOS 托管 runner。三端全部成功才创建或更新 GitHub Release；失败保留各端日志和已成功的构建产物，不发布半份版本。发布附件为 `San9R-<版本>-windows-x64.zip`、`San9R-<版本>-linux-x64.tar.gz`、`San9R-<版本>-macos-arm64.zip`，包含内置转换器的客户端及字体／Godot 许可说明，不含原版素材、转换内容或玩家存档。Linux tar 包保留执行权限，macOS 解压 Godot 生成的 ZIP，使用系统 `/usr/bin/codesign` 重建 ad-hoc 签名及 DER 权限数据，保留原有标识、权限和运行时标志后重新打包；重复运行同一版本会替换该版本的三个同名附件。`-rc` 等带连字符后缀的版本创建为预发布。

macOS 发布前重新解压最终 ZIP，执行严格签名校验、权限读取和导出程序的 `--headless --help` 启动检查；任一步失败或超时都会阻止发布，相关日志随导出日志上传。该检查不依赖私有转换内容，只验证程序入口能启动，不代表内容加载、画面或完整玩法验收。签名处理位于工作流内，手动重建已有标签时同样生效；仍不进行 Apple Developer ID 签名或公证。

工作流只在发布任务授予 `contents: write`，使用内置 `GITHUB_TOKEN`，不需要额外配置密钥。工作流文件须先进入默认分支，Actions 页面才会显示手动运行入口；实际托管 runner 构建结果以 Actions 日志为准。

## 原版文本统一入口

Importer将已登记的消息档案与可选EXE证据编译为静态文本、运行消息和诊断；解码、繁简转换、姓名与词汇校订以[文字规范](../specs/text-and-translation.md)为准。图标、调用／尾转移、样式和动态查询由统一消息图表达，运行时只消费语义引用及显式参数，不解释源地址或控制字节。普通参数转义，富文本组合通过消息引用；未知预设、未闭合样式或缺图拒绝。

`Resolve`严格读取纯文本，`ResolveRich`用于提示、列传和说明；`MessageEvaluator`由显式上下文生成可保留的正文，重绘不重新求值。完整契约与绑定见[消息编写](ui/messages.md)，不在内容管线复制节点和控件实现。

```powershell
dotnet run --project src/San9.Tools -- import-text --game-dir "private/game-files" --output "local/generated/research/unified-text" --exe-evidence "local/generated/research/unified-text/exe-evidence.json"
python research/scripts/verify_message_export.py "private/game-files" local/generated/research/unified-text
```

EXE证据由主IDA库中的`research/scripts/ida/export_text_evidence.py`只读生成；未提供时仅审计四个消息档案。原始字节、偏移、哈希、未确认属性和异常尾部留诊断，严格原文不因运行时适配而改写。`coverage.json`登记所需绑定／查询，`synthetic-evaluation.json`只证明合成求值，不证明真实玩法已接线。历史`--text-smoke`按[画面验证](ui/client-verification.md)按需使用，不属于日常转换步骤。

## 开局新增定义与恢复

选择势力页读取人物库快照，`NewGameDraft`持有部署草稿，`NewGameComposer`基于固定剧本、库和部署生成查询与建局共用结果；成功修改时整体替换，失败保留旧草稿。仅纳入已部署人物、君主及必要库内亲代和血缘根，未选人物不随机进入世界。

`SessionDefinitions`只补充本局新增显示身份；能力、兵法、亲缘、归属、位置及资源只保存于世界。不保存作者Guid、整个人物库、属性初值或部署输入。恢复直接组合当前世界与新增定义，不重新建局；同剧本及新增定义可复用显示，定义变化重建。明确null的`MessagePeople.PortraitKey`可无头像，其他非空引用必须闭合。

命令脚本只声明新增定义，有新增对象时经`--restore`／`--load`提供起点存档，不嵌入第二份世界或重读人物库；格式版本以`SessionProtocol`为准。部署、年龄、资源及选择流程见[新游戏规格](../specs/behavior/BEH-UI-0011-new-game.md#新武将部署与新势力)。

## 验证与本地产物

转换契约变化按受影响范围独立核对源字段、文本和图像，不把同一转换器自检当作独立证据。目录验收核对20项预览与真实年月／城市颜色、包可建局、身份进入世界及查询、共享文件归属和无参菜单，不以数量一致代替身份核对。

开发内容根为`local/generated/content`；`scenario-190-live`只用于显式独立包和历史实验，不是默认剧本。研究及验证输出分别在`local/generated/research/`、`local/generated/verification/`，生成物和私有样本不提交。验证范围与视觉分工见[测试说明](../tests/README.md)。

## 领域契约导航

本页只维护转换和引用边界；任务、命令、结算及恢复不变量由对应规格维护。

| 内容 | 作者与Importer职责 | 规则入口 |
| --- | --- | --- |
| 部队方针与成员 | `UnitPolicies`声明四项许可，`PolicyRadius`使用细坐标；人物`UnitId`持有成员归属，部队保存`CommanderId`和`MemberPositions`，不另存成员顺序。TC101源位只在Importer解释 | [移动与命令](../specs/behavior/BEH-WAR-0001-movement-and-command-lifecycle.md) |
| 固定用途与跨岸 | `ConstructionFormations`／`TransportFormations`声明固定陆水配对；战斗基础船由`BasicNavalFormationId`声明，阵型不复制船型或用途状态 | [固定用途](../specs/behavior/BEH-WAR-0001-movement-and-command-lifecycle.md#固定用途部队) |
| 外交与军团 | `AttackRequests`保存有效双方、目标及剩余旬数；TC101剧本无请求元组，输出空集合。`Delegation`为带类型和引用的联合值，导入默认为一般；不支持原版存档导入 | [军团委任](../specs/behavior/BEH-GOVERNANCE-0002-delegation-control-and-automatic-scheduling.md)、[G08验收](history/stages/g08-verification.md) |
| 人物生命周期 | 作者声明寿命档、继任优先、登场与死亡开关；`FreeOfficerMigrationEnabled`独立控制在野迁移。版本特定列传排除由Importer转换为属性 | [人物生命周期](../specs/behavior/BEH-PERSON-LIFECYCLE-0002-appearance-injury-and-death.md) |
| 季度课程 | `TacticCourses`保存城市／兵法引用，校验城市资格、熟练类别及重复项；显示修行城市也由该集合派生。TC101九组配对完整保留，特殊模式输出空集合 | [成长与学习](../specs/behavior/BEH-PERSON-LIFECYCLE-0001-growth-and-learning.md)、[G09验收](history/stages/g09-verification.md) |
| 事件与归附 | `SearchEvents`声明角色、地点、资格、奖励及消费；世界消费键与势力`EventHistory`分开。`SettlementDefectionsEnabled`、`UnitDefectionsEnabled`及城市自立开关分别控制独立规则，TC101模式限制留在Importer | [事件选择与提交](../specs/behavior/BEH-EVENT-0002-event-selection-and-commit.md)、[G10验收](history/stages/g10-verification.md) |

## 界面与演出契约

皮肤、部队外观、命令／消息／事件表现及媒体角色统一见[界面与演出内容](ui/presentation-content.md)，交互入口见[UI索引](ui/README.md)。
