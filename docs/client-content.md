# 客户端内容与主界面

本页维护当前能力与使用入口，不累计已解决问题的阶段验收。原版依据见[主界面规格](../specs/behavior/BEH-UI-0001-initial-strategy-screen.md)，地图与运行态见[地图文档](map-import.md)和[运行时架构](runtime-state.md)。

## 职责与文件

| 模块 | 职责 | 输出 |
| --- | --- | --- |
| Content.Maps / MapImporter | 方格、地貌、建筑、云、动画、疆界精灵和小地图底图 | `content.json.map`，地图schema8 |
| Content.Scenarios / Scenarios.ScenarioContentCompiler | 初始日期、势力资源、据点、人物、宝物初始状态和地域所属城市 | `content.json.scenario`，schema11 |
| Content.Text / Text.ScenarioTextReader | 语义名称与全量消息目录、原生BBCode、结构化控制及绑定 | `content.json.text`，schema3 |
| Content.Interface / Interface.InterfaceSkinCompiler | 框架、三段背景、季节、按钮、状态条、图标和原版显示颜色 | `content.json.skin`，schema15 |
| Content.Packages / Packages.ClientContentImporter | 组合入口、跨包ID/坐标/文本引用校验、来源清单 | 唯一`content.json`，顶层schema18 |
| Content.Images / Importer.Images | 通用PNG、索引图解码和透明边缘处理 | 由使用方选择输出目录 |
| Importer.Containers.Ls11Archive | LS11容器解码，不承担名称或地图语义 | 不直接输出文件 |

地图内容不引用名称、势力摘要或皮肤。剧本和文本可独立于地图校验；组合层检查建筑稳定ID与据点一一对应、坐标一致以及文本键存在。`SourceText`保存繁体显示原文（设施已组合类型后缀）；基础地域名另有文本项。它不是原消息字节的替代归档，原文件以SHA-256追踪。

客户端由`Main`装配各内容和节点。`Application.Presentation.StrategySelection`负责唯一选择状态与玩家设施过滤；`MapWorld`发布拾取事件，接受高亮坐标和相机定位，消费会话视图派生建筑，不自行维护势力或HUD真值。`MapAnnotations`显示标注，`StrategyHud`显示固定界面，`StrategyMinimap`组合地形和据点标记。`Diagnostics`单列F3面板、地图自检和HUD视口事件验收。通用图像加载不再放在TerrainRenderer中。

当前磁盘包只有一个`content.json`，图片位于`images/map/`与`images/interface/`，所有路径相对包根；代码中的四类契约继续分离。旧版包重新转换，不提供兼容路径。初始内容由GameSession复制为独立Core运行态，显示数据由当前世界派生；完整开局结算尚未接入，详见[运行时架构](runtime-state.md)。

## 来源边界与统一内容

基础版与 PK 追加是原作的来源组织方式，不作为重制版的内容分类。此约定适用于人物、头像、宝物、剧本、建筑、地图、文本、界面和其他内容，不限于显示资源。

- 版本 Importer 处理源文件分组、编号分段、记录布局及版本识别，将支持范围内的数据合并为各自类别的统一定义与稳定 ID 引用。源 ID 可直接保留时不为统一而重新编号；有冲突时在导入阶段明确映射并闭合引用。
- Content、Core、Application、Rendering 和客户端不因内容来自基础版或 PK 而建立两套集合、加载路径或行为分支，不添加仅表达来源分组的 `isPk` 等产品字段。
- 来源文件、版本和原始编号可保留在导入追踪信息与研究记录中，不用于决定内容的可见性或可用性。对象类别、能力、规则和剧本初始状态等实际产品语义仍须明确表达；统一来源不等于抹平玩法差异或把不同剧本的初始状态混合。
- 当前支持范围和缺失内容仍须如实标明。此约定不代表已完成全部 PK 内容导入，也不改变已选定的原作版本基线。后续 Mod 同样转换为统一契约，源格式差异留在导入阶段。

## 接入前的数据核对

新增运行时对象或改变导入范围、对象筛选边界时，明确支持的版本与内容范围，并核对以下受影响层次；区分有效对象、未启用槽和未支持扩展。

| 层次 | 核对内容 |
| --- | --- |
| 原版源数据 | 定义和槽位数量、稳定编号、状态分布、持有人/地点等引用，以及完整范围的文本和图像来源 |
| 转换包 | 定义与资源是否完整，初始状态是否保留，缺失和排除是否有明确原因，引用是否闭合 |
| 世界状态 | 会话启动后对象是否仍存在，初始状态是否独立复制，当前归属、可见性和可用性由谁持有 |
| 界面查询 | 筛选是否读取当前快照；对象出土、登场或易主后能否使用已有定义和资源展示 |

比较身份集合和关键字段，不能只比较总数；文本和资源检查须覆盖支持范围，不能只用首屏样本。

## 转换与运行

从仓库根目录执行；已有目录按来源清单原位增量更新。复用未变化资源，同步清单并清理失效文件；必要的全量重建仍优先使用原目录。只有明确需要独立对照时保留副本，不为旧包引入兼容分支：

```powershell
dotnet run --project src/San9.Tools -- import-client --source-exe "<已登记San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --scenario "private/s9数据文件/D_Sce002.S9" --output "local/generated/content/scenario-190-live"
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --content-package "<客户端内容包绝对路径>"
```

客户端默认读取`local/generated/content/scenario-190-live`，只加载转换结果。单独地图仍使用`import-map`；`render-map --content-package local/generated/content/scenario-190-live`也可直接消费组合包内的地图部分。源资源及生成包均被忽略，不提交。

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

主题`GameFont.tres`以霞鹜文楷等宽GB Lite为主字体，并按全量缺字检查配置BabelStone Han回退；来源与许可证见[字体说明](../src/San9.Client.Godot/assets/README.md)。纯文字地图绘制与富文本共用主题字体和角色色彩/字号。原生RichTextLabel承担图文排版；描边和阴影强一致仍为未决项，不自建排版引擎。

F3提供四季预览及单人商业、开垦入口，内政修改当前会话世界。完整旬结算、部队出征与战斗生命周期尚未接入。全图三个图层按钮的独立文字效果暂缓；目录标题及关闭按钮文字效果仍待核对。

## 验证与本地产物

通用验证要求见[协作规则](../AGENTS.md#验证与交付)。转换契约变化须核对受影响范围的源字段、文本和图像，不以调用同一转换器作为独立证据。当前实际画面由用户验收，不自动启动Godot。

当前运行包为`local/generated/content/scenario-190-live`。研究资料位于`local/generated/research`，仍有用途的运行态对照、字体研究和检查工具位于`local/generated/verification`。局部问题解决后清理对应临时截图与日志，不保留多轮重复验收记录。

文字角色、状态与原生效果的统一入口见[统一文字样式](text-styles.md)。
