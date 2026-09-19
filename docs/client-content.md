# 客户端内容与主界面

G09人物生命周期新增必填`lifespanTier`、`successionPriority`及场景`naturalDeathsEnabled`。寿命档与继任优先值为非负数；优先值由作者直接维护，TC101仅在Importer根据列传资源号转换，运行时不读取原作编号。字段进入内容指纹与保存状态，旧包须重转；当前schema以代码常量及[G09验证](g09-verification.md)为准。

本页维护当前能力与使用入口，不累计已解决问题的阶段验收。原版依据见[主界面规格](../specs/behavior/BEH-UI-0001-initial-strategy-screen.md)，地图与运行态见[地图文档](map-import.md)和[运行时架构](runtime-state.md)。

## 职责与文件

| 模块 | 职责 | 输出 |
| --- | --- | --- |
| Content.Maps / MapImporter | 当前地图方格、地貌及对象位置 | `map.json`（无图片文件） |
| Content.Scenarios / ScenarioContentCompiler | 剧本初始状态及军事规则拓扑 | `scenario.json`，schema54 |
| Content.Text / ScenarioTextReader | 名称与全量消息目录 | `text.json` / `messages.json`，schema3 |
| Content.Interface / InterfaceSkinCompiler | 固定界面角色、颜色、装饰资源 | `textures.json.skin`，schema16 |
| Content.Packages / ClientContentImporter | 剧本、文本、资源 ID 和依赖校验 | `content.json`，schema58 |
| Content.Packages / TexturePackageImporter | 与剧本无关的完整纹理、图集及地图资源定义 | 独立 `textures.json`，schema3／转换版本8 |
| Importer.Containers.Ls11Archive | LS11容器解码，不承担名称或地图语义 | 不直接输出文件 |

共享剧本契约不按原版编号区间限制城市或人物身份。城市图标资格由`SettlementKind.City`决定；`RegionCityIds`必须引用真实城市，其城市名从据点定义读取，不要求城市ID同时存在于地域名称表。数量与名称不继承源格式槽位和字节长度；玩法数值上限及显示资源范围独立校验。

G08 的 schema50/54增加必填可空`Alliance`：目标、盟主、排序后的成员身份集合与剩余旬数。Importer把源联盟旗标及头部期限转换为同一个语义对象，缺失成员或矛盾状态明确拒绝。190年开局目标5、盟主7、13个成员及36旬完整保留；运行态从初始值独立演进，快照查询当前联盟，恢复不重新导入或重新扣旬。当前规则`san9-core-g08-v23`，存档/日志41；旧包须原位重转，旧规则存档拒绝加载。

G08的schema52/55为阵型增加计略减半与扰乱免疫语义。版本Importer将方圆和不可扰乱阵型转换为属性，Core不比较源编号。任务保存野战目的地细坐标、地域身份和类型化计略载荷；去返程快照使用同一坐标尺度，旧任务布局明确拒绝。

G08的schema53/57增加必填`CityProvinceIds`和`MilitaryDefinition.TerritoryExits`。前者是城市到州的基础分组，可不为某城市指定州，不继承15州上限；后者以行优先顺序保存每格北、东、南、西出口语义，与现有地域、地形及完全阻断格共同推导地域边。非空出口数组必须覆盖整幅地图，空数组明确表示未提供地域邻接。作者不维护逐势力邻接或成员候选表；Core只缓存不可变地图边，按当前据点位置、归属、存续及港口属性生成资格。城市州分组进入内容指纹并由会话独立持有。190年包已原位更新并逐项核对50城、40000格及94据点；具体数据链和736条有向边见G08验证记录。

地图内容不引用名称、势力摘要或皮肤。剧本和文本可独立于地图校验；组合层检查建筑稳定ID与据点一一对应、坐标一致以及文本键存在。`SourceText`保存繁体显示原文（设施已组合类型后缀）；基础地域名另有文本项。它不是原消息字节的替代归档，原文件以SHA-256追踪。

客户端由`Main`装配各内容和节点。`Application.Presentation.StrategySelection`负责唯一选择状态与玩家设施过滤；`MapWorld`发布拾取事件，接受高亮坐标和相机定位，消费会话视图派生建筑，不自行维护势力或HUD真值。`MapAnnotations`显示标注，`StrategyHud`显示固定界面，`StrategyMinimap`组合地形和据点标记。`Diagnostics`单列F3面板、地图自检和HUD视口事件验收。通用图像加载不再放在TerrainRenderer中。

共享纹理默认位于 `local/generated/content/textures-san9pk-tc101`。剧本包只引用共享包的目录和资源 ID，没有 `images` 目录。43个已确认非头像档案及836张正常色大头像转换为最终图片；未确认项只记录研究边界，不保留索引图和原始调色板副本。具体覆盖与透明规则见[纹理资源](texture-resources.md)。

图片契约、静态图集、GPU缓存和资源租约见[纹理资源文档](texture-resources.md)。头像上传后不保留CPU像素，窗口与地图按实际使用异步申请。

清单保留缩进；五个数据文件使用紧凑 JSON，保留完整字段名和内容。仅省略消息表达式及参数中默认值为 null 的可选字段，必填 nullable 状态字段仍显式保存。数据子文件必须位于包内，共享纹理目录允许相对引用包外目录。组合包schema52及更早的包须原位重新转换，不提供兼容读取。导入先校验、暂存全部文件，再替换数据并最后发布清单；不支持导入过程中并发读取。

`MilitaryContentCompiler`将完整地形、硬阻挡、阵型移动预算和据点footprint导入剧本，Core直接消费该规范化规则，不读取`map.json`的显示资源。G06增加普通战斗参数、阵位、据点销毁能力及名马标记；部队成员、伤兵和当前命令随世界查询变化。数据核对见[G05](g05-verification.md)与[G06验收](g06-verification.md)，完整兵法、设施建设及水陆转换仍归G07。

## 来源边界与统一内容

基础版与 PK 追加是原作的来源组织方式，不作为重制版的内容分类。此约定适用于人物、头像、宝物、剧本、建筑、地图、文本、界面和其他内容，不限于显示资源。

- 版本 Importer 处理源文件分组、编号分段、记录布局及版本识别，将支持范围内的数据合并为各自类别的统一定义与稳定 ID 引用。源 ID 可直接保留时不为统一而重新编号；有冲突时在导入阶段明确映射并闭合引用。
- Content、Core、Application、Rendering 和客户端不因内容来自基础版或 PK 而建立两套集合、加载路径或行为分支，不添加仅表达来源分组的 `isPk` 等产品字段。
- 来源文件、版本和原始编号可保留在导入追踪信息与研究记录中，不用于决定内容的可见性或可用性。对象类别、能力、规则和剧本初始状态等实际产品语义仍须明确表达；统一来源不等于抹平玩法差异或把不同剧本的初始状态混合。
- 当前支持范围和缺失内容仍须如实标明。此约定不代表已完成全部 PK 内容导入，也不改变已选定的原作版本基线。后续 Mod 同样转换为统一契约，源格式差异留在导入阶段。

TC101剧本导入以源坐标识别地域配置：双坐标均为无效哨兵的槽不生成地域、名称文本或城市归属映射；单轴无效或越界坐标直接报错。有效但尚未建造的地域完整保留，稳定ID不重排。人物和宝物引用由转换后的契约校验，不能通过丢弃引用对象掩盖错误。

## 接入前的数据核对

G10事件内容使用剧本schema68／组合72、规则`san9-core-g10-v4`、保存／日志59。`ConsumedEventKeys`贯通内容、指纹、新局与持久化；版本Importer已将神兽三个地点的持久位转换为独立语义键，其他事件位随对应事件接入继续补齐。`SearchEvents`当前包含华佗疗伤和神兽定义，医者、排除探索者按保留人物身份转换；神兽地点、独立消费键、有序兵法池和模式寿命规则完整进入内容，概率除数及候选优先级随内容进入规则。当前没有改变人物、势力、据点或显示资源范围，公开夹具已核对引用、键映射、查询效果和往返；190年真实包仍待全阶段收口时原位重转，不把旧包视为新契约验收。

G10主动求官继续接入使用剧本schema67／组合71，新增必填`OfficeRequestsEnabled`；当前规则`san9-core-g10-v3`，保存／日志58。字段按模式2／9排除语义转换，身份集合和显示资源范围没有变化；拒绝迁移使用既有领域地图地域邻接，不增加派生图的人工维护字段。代码及公开会话验收已完成，本地190年包仍为上一批版本，阶段收口需原位重转并核对，见[G10验证](g10-verification.md)。

G10终局／结局使用剧本schema66、组合70：必填可空`Terminal`与`Ending`声明周期胜利／年份规则及结局所需城市、宝物引用。190年包已原位重转，25势力、94据点、700人物、100宝物、1部队的身份集合不变；45核心都市和20文化宝物引用逐项核对。该批会话规则为`san9-core-g10-v2`、保存／日志57：已选结局保存于Application会话，世界仍冻结；普通搜索移除下令时预设的登用答案，改为发现后响应，内容schema不变。实际验证及阶段剩余项见[G10](g10-verification.md)。

同批schema64／组合68军事契约新增必填`BattleDeathsAllowed`和`DuelCapturesAllowed`，将剧本模式限制转换为语义开关。会话另有默认关闭的`BattleDeathsEnabled`选项，不从原版配置文件导入；通过公开会话启动参数选择并写入保存／录像／命令日志。此字段接入没有改变人物与资源范围，战后领域测试已执行，真实普通攻击链与190年集成已完成，见[G09验证](g09-verification.md)。

G09单挑优待使用剧本schema64／组合68：人物必填`DuelAdvantages`保存可组合的强差看破、强差攻击及强差双倍特性。版本Importer按已核对的列传身份转换，运行时不依赖人物编号；任意作者人物均可配置，未知位拒绝。字段进入内容指纹、世界及保存／录像，当前规则v14／保存及日志55。对象、文本与资源覆盖不变；190年已原位重转，700人物逐身份核对一致，其中3人具有非零单挑优待，见[G09](g09-verification.md)。

G09登场接入将剧本schema提升为59、组合63：人物必填`AppearanceYear`和可空`BloodlineId`，后者是无源编号上限的家族标识；场景必填`AnnualAppearancesEnabled`。190年包已原位重转，700名支持人物逐身份核对源字段、转换和世界，397名未登场者完整保留，初始可见查询210人；没有改变对象、文本和显示资源覆盖范围。具体证据见[RE-0133](../research/findings/RE-SAN9PK-0133.md#2026-09-18g09登场分支与数据核对)，当前验证见[G09](g09-verification.md)。

当前G08宝物保留类别契约为剧本schema56、组合60：必填`RetentionGroup`为其他、武力、智力、政治、统率之一。即使相应能力增量为零也保存明确类别，多个能力加成的独立创作宝物可指定其保留类别；报价比较值直接读取该能力或忠诚增量。190年包已原位更新，源100件宝物按类别逐项核对一致；规则版本与存档格式仍为v20／38。

G08宝物外交输入将剧本契约提升至schema55、组合包59。宝物定义必填`AutomaticDiplomacyOfferAllowed`与`FavoredOwnerIds`，后者为去重的人物身份引用；未知人物、重复引用或缺字段拒绝。两个字段进入内容指纹，偏好集合顺序不影响指纹。版本Importer把EXE的宝物／列传配对转换为保留人物引用，Core不读取源表。190年运行包已原位更新：100项宝物定义、700个人物，38条源配对转换后仍为38条，3项禁止自动报价；身份与字段已全量逐项比对。赠与资格未因此改变，报价消费者仍待接入；规则v20、存档／日志38不变，旧内容存档由指纹拒绝。

新增运行时对象或改变导入范围、对象筛选边界时，明确支持的版本与内容范围，并核对以下受影响层次；区分有效对象、未启用槽和未支持扩展。

| 层次 | 核对内容 |
| --- | --- |
| 原版源数据 | 定义和槽位数量、稳定编号、状态分布、持有人/地点等引用，以及完整范围的文本和图像来源 |
| 转换包 | 定义与资源是否完整，初始状态是否保留，缺失和排除是否有明确原因，引用是否闭合 |
| 世界状态 | 会话启动后对象是否仍存在，初始状态是否独立复制，当前归属、可见性和可用性由谁持有 |
| 界面查询 | 筛选是否读取当前快照；对象出土、登场或易主后能否使用已有定义和资源展示 |

比较身份集合和关键字段，不能只比较总数；文本和资源检查须覆盖支持范围，不能只用首屏样本。

## 陷阱地形内容

陷阱选择读取独立内容定义：MilitaryDefinition.TerrainIds 引用地形，Elevations 按同一行优先顺序保存每格高度；TrapTerrains 为每种地形声明 BaseTacticId（可空）和 ProvidesHigherGround；HigherGroundTrap 可为空，否则声明 TacticId 与 RadiusCells。高度使用相同尺度比较，地形ID与兵法ID无需沿用原作编号。存在陷阱选择时，高度须覆盖完整地图，地形规则须覆盖全部地形，效果引用须指向具体陷阱。

兵法的 ContactRange 为可空的正整数，仅武力兵法可配置。非空时，将来源占地矩形沿两轴外扩该值后与目标部队占地严格相交，边缘相碰不满足；空值表示没有额外近战门。原版导入器为步兵、骑兵六种兵法配置5，其余为空，普通选择与智力后续连锁共用该资格查询。

阵型的 DoublesCapture 为显式捕获加倍能力，TC101仅鹤翼映射true，28阵型完整保留并逐项验证。组合包schema53要求此字段；存档／日志24保存战败返回的起点坐标、原军团与倒计时，规则g07-v31使用动态交战程度、周期士气和独立都市俘虏门。旧包原位重转，旧规则存档明确拒绝。

组合包schema53新增`MilitaryDefinition.UnformedFormationId`引用；TC101完整保留28项阵型，指定27为异常时有效Combat配置，源攻防参数原值导入。Core使用攻击2K/25、防御2D/15及混乱8／溃败6，通过统一有效配置查询消费攻防、削墙及伤兵比例。自制内容可显式为null表示不替换，否则校验引用存在且具有Combat；不需要重复维护一份无阵属性。规则g07-v31同时采用每日断粮／溃败损失、守军回气和邻城建筑池安置，存档与日志布局为24，规则版本拒绝旧语义。

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

编写控制节点、绑定参数和显示图片的具体用法见[消息约定](messages.md)。

现行契约见[统一消息规格](../specs/behavior/BEH-UI-0006-unified-messages.md)，原作证据与覆盖边界见[RE-0166](../research/findings/RE-SAN9PK-0166.md)。唯一语法实现为`OriginalMessageParser`；`OriginalTextConverter`只调用该解析器读取静态片段。`import-client`将四个消息/列传档案合入同一个`TextCatalog.Messages`，保留未就绪记录；`import-text`使用同一实现导出全量诊断和合成上下文报告。

静态内容使用Godot原生BBCode，逻辑图片路径示例为`[img]icons/city[/img]洛阳`。运行时`MessageEvaluator`接收显式值、查询和随机源，产生可保留的BBCode结果。普通参数始终转义；富文本组合用消息引用。`01J`转换为尾部转移，`01C`为返回调用。样式切换只保存明确BBCode开闭片段，支持跨分支/引用的显示状态，不维护重复样式树。节点中不保存原始地址、控制码或资源偏移。

`Resolve`是严格纯文本读取，遇到图标/格式标签拒绝，供名称排序和查询使用；`ResolveRich`供提示、列传和说明使用。侧栏城市名称由通用消息模板求值，再交给`GameRichTextLabel`；控件不判断城市前缀。图片由客户端按逻辑标识选择转换包中的最近有效尺寸、同距优先较大图并保持比例，Godot负责行内排版。缺图明确报错。

FA40..FA45依次映射伷/璝/寗/勑/𩅦/儁；不使用HKSCS猜测。CP950是来源编码，繁简转换仅作用于文字片段。四十条日文占位记录按索引和精确原始字节签名识别CP932，不做宽松编码回退。未知字形、未确认属性及异常尾部保留在诊断，并阻止消息执行；所有引用和依赖确认度另行统计。璝按OpenCC词典显示为𪻺，见[DEC-UI-0005](../specs/decisions/DEC-UI-0005-officer-name-normalization.md)。

```powershell
dotnet run --project src/San9.Tools -- import-text --source-exe "<已登记San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --output "local/generated/research/unified-text" --exe-evidence "local/generated/research/unified-text/exe-evidence.json"
python research/scripts/verify_message_export.py "private/s9数据文件" local/generated/research/unified-text
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --text-smoke --map-quit
```

EXE证据先在项目主IDA库运行`research/scripts/ida/export_text_evidence.py`只读生成；未提供该文件时只审计四个档案。EXE字符串登记和格式识别不等于玩法调用已绑定，必须检查诊断中的边界声明。字节哈希、偏移、控制码、未确认原因只在导入报告中。未来玩法提供的具名绑定与查询清单由`coverage.json`生成；`synthetic-evaluation.json`只证明合成上下文求值，不冒充真实玩法验证。

状态所有权、侧栏行为、小地图刷新、文字和验证入口详见[战略界面约定](strategy-ui.md)。

## 当前显示与交互

设施与武将侧栏复用名单和滚动条，支持独立滚动、焦点和镜头定位。小地图分层显示地形、城市标记与视野框，相机变化只刷新视野框，世界变化刷新标记。

「小」打开独立战略全图，「大」返回详细地图；全图支持三个图层、定位、视野框和四列排行榜。详见[全图规格](../specs/behavior/BEH-UI-0003-strategy-overview.md)。

武将一览使用通用CatalogView，提供13页列组、排序、滚动及原生64×80中档头像。正文在24像素行内垂直居中，各列保留声明的水平对齐。武将详情使用240×240头像、独立18行名单、四维图、状态/兵法页及列传。字段与其他情报页面范围见[情报界面](information-browser.md)。

主题`GameFont.tres`只使用霞鹜文楷等宽GB Lite，按用户决定不增补字体并明确接受四个缺字；来源与许可证见[字体说明](../src/San9.Client.Godot/assets/README.md)。纯文字地图绘制与富文本共用主题字体和角色色彩/字号。原生RichTextLabel承担图文排版；描边和阴影强一致仍为未决项，不自建排版引擎。

F3提供四季预览及单人商业、开垦入口，内政修改当前会话世界。核心旬结算和G05出征/行军已通过无界面API接入，正式界面尚无出征菜单，G06普通战斗与占领已通过无界面API验收。全图三个图层按钮的独立文字效果暂缓；目录标题及关闭按钮文字效果仍待核对。

## 验证与本地产物

通用验证要求见[协作规则](../AGENTS.md#验证与交付)。转换契约变化须核对受影响范围的源字段、文本和图像，不以调用同一转换器作为独立证据。当前实际画面由用户验收，不自动启动Godot。

当前运行包为`local/generated/content/scenario-190-live`。研究资料位于`local/generated/research`，仍有用途的运行态对照、字体研究和检查工具位于`local/generated/verification`。局部问题解决后清理对应临时截图与日志，不保留多轮重复验收记录。

文字角色、状态与原生效果的统一入口见[统一文字样式](text-styles.md)。

G03经济内容保留基础信望、相性、精通类别、军团账户、兵力容量与粮耗资格；组合表映射为规范化人物ID对，官职俸禄进入Core规则目录。数据覆盖与运行验证见[G03验收](g03-verification.md)。

坐标单位和人事计时统一见[地图坐标约定](map-coordinates.md)。剧本schema16的未建设地域及部队使用精细Position，据点X/Y保持格锚点；所有转换共用格中心公式，不支持原作存档导入。人物`StatusStartYear`表示当前身份起始年，导入源身份年份；在野也可有值，不以是否在职丢弃。发现与登用时更新为当前年，查询仅对在职人物派生仕官年数。

军事规则的`RegionIds`按地图格保存规范化地域引用，允许无地域；有效引用须存在于地域名称表。地域关联都市由`RegionCityIds`单独保存，运行时按“格子→地域→都市”查询，不在格子展开保存都市或势力。兵力回收使用这条地理关系，不以当地建筑的当前势力改选都市。

### 部队方针数据

G07的`UnitPolicies`保留迎击、追击、事后攻击和自主撤退四项语义字段，源位12至15只在TC101 Importer解释；`MilitaryDefinition.PolicyRadius`以领域细坐标定义邻敌范围，TC101转换10格为50细单位。初始活动单位筛选不变，190年没有初始野战队，因此源四位的独立映射以构造记录测试验证，不能把0活动队误记为实战覆盖。源初始在队人物及其主将/原任务恢复仍不属于当前190年导入范围。

出征和建设命令传入方针，世界部队持有主将、原任务、临时交战目标与一次性事后状态；部队情报方针页读取当前快照。公共改方针命令遵守当前军团权限。存档24、操作日志24和规则g07-v31保存这些字段；初始态schema49、组合包schema53，旧包须原位重新转换。

人物的`UnitId`是成员归属权威；部队独立保存`CommanderId`和`MemberRanks`，不保存`MemberOrder`。出征／整编按身份、官职指挥量级、适用官职优先级及当前统率选任，同分按人物ID升序；初始声明的主将保留，缺省不补选。声明的主将须是本队同势力在职成员。阵位由命令输入绑定，批量成长及均匀随机候选统一按ID升序。剧本schema65／组合69移除成员顺序契约，旧内容须重新转换；保存／日志55、规则v14明确拒绝旧版本，不增加兼容初始化。190年初始野战队仍为0，不扩大源初始在队人物导入范围。

战争规则内容补充：阵型显式保存`OrdinaryAttack.RangedSiege`及`GainsDailyCombatProficiency`；TC101仅发石允许远距削墙，13／14不产生交战逐日熟练度。水战关联学习由阵型可空的`AutomaticTacticLearningId`引用技术，TC101仅21／22／23分别映射12／13／14，须验证引用存在及熟练类别。设施保存可空的`DestructionGrowthTacticId`，引用要学习的技术定义，不从设施名称推断。部队`HighSpiritsMoralePenaltyPending`独立保存高昂到期副作用；正日数高昂导入为true，治疗清除后不能在恢复存档时重新推导。2026-09-13运行包已按schema49／53原位重转，28阵型、41兵法、17设施、94据点、700人物保持完整。

### G08内容与运行契约

当前规则`san9-core-g08-v30`，保存／日志45，剧本schema58／组合62。旧规则存档和日志拒绝加载，不提供兼容分支；运行任务和事件变更不要求重新转换当前内容包。

`FacilityRule.IsWork`和可空的`FormationRule.MovementStepCost`均须显式存在。TC101定义10–16为工作物，28阵型步进费用均为10。工作物与驻人能力独立，地形选路费用与每日步进扣费独立；源编号只留在Importer。

`AttackRequests`为必填集合：作者填写受请求方、请求方、目标据点与正的剩余旬数；建局根据初始据点归属推导签约条件。缺字段、重复持有人、无效身份、无主或属于签约方的目标均拒绝。集合进入内容指纹，运行态独立演进，恢复不重新应用初始约定。TC101剧本没有请求元组，导入输出空集合；不支持原版存档导入。

人物必填`Ambition`（0..15）和`AutomaticSurrenderAllowed`通过OfficerState进入初始世界、保存与内容指纹。Importer把版本特定列传排除转换为语义布尔，Core不保留列传排除表。190年700人物已逐项与源记录核对，未扩大人物筛选或视觉资源范围。

`CorpsState.Delegation`为必填联合值，一般、内政、打倒势力、攻略据点分别保存类型及对应身份引用；不另存可与类型矛盾的目标字段，根军团仅一般。TC101剧本没有运行时委任方针，导入为一般；源50条军团记录中的25条有效记录与转换集合、根归属、领导者及据点引用逐项一致。独立内容可创作四种方针，进入内容指纹；运行时通过公共命令修改，查询读取当前快照。

运行时任务、路径及事件的保存边界见[运行时状态](runtime-state.md#g08任务与结算契约)，数据覆盖和验证见[G08验收](g08-verification.md)。

### G09在野迁移开关

剧本schema62／组合66新增必填`FreeOfficerMigrationEnabled`，用于独立创作剧本控制在野周期迁移，与年度登场和自然死亡开关独立。TC101的模式2／9转换为关闭，其余模式开启。该字段进入内容指纹、初始世界、保存及恢复校验；人物地点在运行时迁移，查询读取当前快照，定义与显示资源集合不变。当前190年包已按schema65／组合69原位重转并核对开关，详细验证见[G09实施记录](g09-verification.md)。

### G09季度课程

剧本schema63／组合67新增必填`TacticCourses`集合，每项保存城市与兵法引用；不存在城市、非城市对象、未知／无熟练类别兵法及重复城市均拒绝，集合进入内容指纹。Importer将TC101九组配对转换为共享引用，显示training城市集合从同一表推导，Core不读取图标或源编号。人物、地图地点、文本和图像支持范围保持不变。原版特殊模式关闭课程时输出空集合；独立作者可任意配置有效课程。当前190年运行包已原位重转，九组课程引用与源表一致。

报名通过`GetTacticStudyPrompt`和`ChooseTacticStudentCommand`消费；修行保存为人员任务及`StudyTacticId`，使用现有人员占用、取消和查询链，协议v9／保存及日志50。报名等待、90日修行、到期直接授予、失效取消与录像验证见[G09](g09-verification.md)。
