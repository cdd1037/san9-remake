# 客户端内容与初始主界面

历史产物清理说明（2026-09-06）：本页引用的旧`verification/client-history`、`font`、`interface`、`map-history`和`minimap`目录已按用户要求移到回收站；历史验收结论保留，路径不再作为现存文件入口。最新验收位于`local/generated/verification/runtime/`，可复用检查脚本保留在`verification/checks/`。

本页记录当前内容边界、运行入口和主界面验收。纯地图转换、渲染与历史实验见[地图文档](map-import.md)。实现依据为[BEH-UI-0001](../specs/behavior/BEH-UI-0001-initial-strategy-screen.md)，源映射证据为[RE-SAN9PK-0157](../research/findings/RE-SAN9PK-0157.md)和[高一致性复核RE-SAN9PK-0158](../research/findings/RE-SAN9PK-0158.md)。

## 文字样式

主界面文字使用客户端`GameTextStyle`语义样式目录；`GameTextPresenter`将同一配置适配到共享`LabelSettings`、设施行文字控件及地图原生描边绘制。共享设置按样式与项目字体缓存，按钮仅在状态变化时切换设置。效果按[DEC-UI-0002](../specs/decisions/DEC-UI-0002-modern-text-effects.md)作现代近似；完整原作效果表见RE-0158，不把所有白字统一加描边。

## 小地图分层刷新（2026-09-06）

地形底图由导入器预生成，客户端初始化时创建一次纹理，由独立`TextureRect`显示。城市标记与视野框分别保留绘制指令；城市标记来自当前会话视图，在世界版本或小地图尺寸变化时重绘。每帧仅比较相机位置与缩放，变化时请求重绘视野框；小地图尺寸变化时更新各层。窗口继续使用固定逻辑画布，窗口拉伸不改变视野范围。

验收要求：静止时不重新提交小地图绘制指令；平移、缩放只重绘视野框；尺寸变化更新底图、标记与视野框，点击定位及显示样式保持一致。实际Godot绘制信号计数验证通过，临时脚本位于`local/generated/verification/checks/check-minimap-layers.gd`；既有UI_SMOKE_OK、MAP_SMOKE_OK和209项单元测试通过，GPU截图`local/generated/verification/minimap/minimap-layers.png`已检查。未执行操作系统交互验证。

## 文字效果验收（2026-09-06，当前结果）

- 自动测试：`dotnet test San9Remake.slnx`通过204项，含8类角色效果、9类按钮状态配色与原有位移检查。研究脚本未修改。
- Godot引擎自检：实际LabelSettings颜色、描边、阴影及项目字体一致性通过；相同状态复用设置、文字位移不漂移、设施各交互状态保持选中色、导航与输入隔离通过。正常及宽屏日志均有UI_SMOKE_OK和MAP_SMOKE_OK，无ERROR/FAILED。
- GPU截图：`local/generated/verification/interface/text-effects.png`（1024×768）、`text-effects-wide.png`（1280×720窗口、960×720内容区）。每组附`-small`、`-boundaries-off`、`-hover`和`-disabled-hover`变体；悬停变体同时保留洛阳设施选中。日志同名`.log`。
- 已对照原图检查正常/宽屏、缩小、悬停及禁用悬停截图：建筑数字/名称描边与条框无新增重叠，底中黑效果和右下绿描边可见，固定UI字号/对齐保持。0.5倍建筑文字按既定规则缩小，字形可读性随之下降；原作位图字形和现代字体边缘仍有允许的差异。未进行操作系统鼠标自动化，截图不包含窗口黑边。
- 截图验证曾检出LabelSettings未显式绑定主题字体导致的回退，已通过绑定当前主题字体修正，并增加引擎断言；最终截图为修正后重跑结果。

以下原有按钮验收记录为之前阶段；其中“文字颜色和效果暂未调整”等限制已由本轮角色效果表和实现取代。

## 职责与文件

| 模块 | 职责 | 输出 |
| --- | --- | --- |
| Content.Maps / MapImporter | 方格、地貌、建筑、云、动画、疆界精灵和小地图底图 | `content.json.map`，地图schema8 |
| Content.Scenarios / Scenarios.ScenarioContentCompiler | 初始日期、势力资源、据点归属/坐标/显示数值、图标、驻留武将初始状态和地域所属城市 | `content.json.scenario`，schema5 |
| Content.Text / Text.ScenarioTextReader | 原作名称索引、类型后缀与简体投影；运行时按键解析 | `content.json.text`，schema1 |
| Content.Interface / Interface.InterfaceSkinCompiler | 框架、三段背景、季节、按钮、状态条、图标和原版显示颜色 | `content.json.skin`，schema6 |
| Content.Packages / Packages.ClientContentImporter | 组合入口、跨包ID/坐标/文本引用校验、来源清单 | 唯一`content.json`，顶层schema7 |
| Content.Images / Importer.Images | 通用PNG、索引图解码和透明边缘处理 | 由使用方选择输出目录 |
| Importer.Containers.Ls11Archive | LS11容器解码，不承担名称或地图语义 | 不直接输出文件 |

地图内容不引用名称、势力摘要或皮肤。剧本和文本可独立于地图校验；组合层检查建筑稳定ID与据点一一对应、坐标一致以及文本键存在。`SourceText`保存繁体显示原文（设施已组合类型后缀）；基础地域名另有文本项。它不是原消息字节的替代归档，原文件以SHA-256追踪。

客户端由`Main`装配各内容和节点。`Application.Presentation.StrategySelection`负责唯一选择状态与玩家设施过滤；`MapWorld`发布拾取事件，接受高亮坐标和相机定位，消费会话视图派生建筑，不自行维护势力或HUD真值。`MapAnnotations`显示标注，`StrategyHud`显示固定界面，`StrategyMinimap`组合地形和据点标记。`Diagnostics`单列F3面板、地图自检和HUD视口事件验收。通用图像加载不再放在TerrainRenderer中。

当前磁盘包只有一个`content.json`，图片位于`images/map/`与`images/interface/`，所有路径相对包根；代码中的四类契约继续分离。旧版包重新转换，不提供兼容路径。初始内容由GameSession复制为独立Core运行态，显示数据由当前世界派生；完整开局结算尚未接入，详见[运行时架构](runtime-state.md)。

## 转换与运行

从仓库根目录执行，输出目录必须不存在：

```powershell
dotnet run --project src/San9.Tools -- import-client --source-exe "<已登记San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --scenario "private/s9数据文件/D_Sce002.S9" --output "local/generated/content/scenario-190-live"
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --content-package "<客户端内容包绝对路径>"
```

客户端默认读取`local/generated/content/scenario-190-live`，只加载转换结果。单独地图仍使用`import-map`；`render-map --content-package local/generated/content/scenario-190-live`也可直接消费组合包内的地图部分。源资源及生成包均被忽略，不提交。

## 主界面行为与数据边界

默认190年1月上旬、春、董卓、洛阳(89,64)、1倍视角。G_Main的顶部框、雷达框、侧栏、季节图和按钮帧由正式转换器导出，动态文字使用现有`assets/LXGWWenKaiMonoGBLite-Medium.ttf`。该字体通过`project.godot`的`gui/theme/custom_font`设为项目全局默认字体，地图标注从控件主题读取相同字体。验收要求：编辑器与运行时控件继承项目字体，主界面文字样式及地图标注保持一致。

顶部信望100、资金9520、兵粮73200来自剧本。原截图资金9076，差444已由RE-0145/0157确认是普通旬俸禄；当前不执行完整周期经济。洛阳32000、孟津港1000、虎牢关30000；董卓设施共13项。耐久条与士气条、商人/训练/皇帝图标已恢复。初始士气由原作初始化链派生，三个主要据点均为50。建筑名称、兵力、条和图标暂随地图缩放；后续修改只需调整独立标注层。

左键选择设施/方格，右键拖动，滚轮连续缩放，大小按钮选择1倍/0.5倍显示。设施名单按直属军团列出，君主所在设施固定首行并从普通13行中去重；移入取得且保留焦点，按下使用原图底纹和(2,2)文字位移，点击或Enter只定位镜头。褐色行仍可点击；无详情提示或黄色选中，不随地图选择强制滚动。原生滚动条按行滚动；右键菜单将未实现动作明确禁用。详见 [DEC-UI-0004](../specs/decisions/DEC-UI-0004-facility-list-interaction.md)。小地图有效菱形区域点击定位，装饰不定位；使用原版八类地形颜色，只标城市，标记色来自根势力颜色与原版调色板。疆界按钮点击切换按下/弹起状态，分别显示/隐藏边界。部队、情报、功能、帮助、进行禁用并附说明。F3打开四季预览与单人商业/开垦入口，后者实际修改当前世界，视图随版本刷新。

## 高一致性验收（2026-09-06）

本次按原图及IDA静态布局修正顶部资源背景、数字对齐、情报名称、方向字、设施城市图标与白/褐色、侧栏与大/小/进行位置及选中态、底中和右下原版背景。新增MinimapCompiler与MapBoundaryCompiler分别负责小地图与疆界，不混入名称或经营规则。

- 自动测试：`dotnet test San9Remake.slnx`通过182项；Python校验器2项通过。新增状态条阈值与整数宽度、类型/高差标注坐标、显示字段不变量、疆界优先级和小地图取样共边测试。
- 私有资源审计：66幅界面资产可见RGB/Alpha逐像素一致，全部图像SHA-256验证；小地图24,964个有效像素按独立IDA参考取样与源调色板逐点校验。94项据点、董卓13项设施、四城及虎牢关可用颜色、洛阳商人+皇帝图标、安定训练图标和三处主要士气/耐久通过断言。
- 引擎自检：实际Godot 4.7.2 Mono/OpenGL/RTX5070运行，列表定位、溢出滚动、地图选择同步、小地图导航、大小按钮状态、疆界往返切换、拖动、相机边界、四季与UI/黑边输入隔离通过。无错误日志。
- GPU截图：`local/generated/verification/interface/ui-fidelity.png`为1024×768；`ui-fidelity-wide.png`为1280×720窗口中的960×720游戏内容；对应`-boundaries-off.png`与`-small.png`验证疆界关闭和标注随0.5倍地图缩放。图像是真实GPU内容截图，原图归一化仅用于对照，未作为产品背景。
- 视觉核对：洛阳、孟津港、虎牢关构图与原图对应；三条耐久为440/800、300/300、900/1000，士气均50/100。固定界面缩放前后保持布局，建筑信息整体缩放。透明资源边缘正常，列表完整显示13项。
- 边界：资金仍为剧本9520，对照图9076差444尚未解释；保留现有简体字体，其字形/度量与原版位图字不同。大/小的1倍/0.5倍为当前重制导航档位，不宣称复现原版小地图模式的全部逻辑。截图不包含OS标题栏或宽屏黑边；没有执行OS鼠标自动化。

## 拆分回归验收（2026-09-06）

- 自动单元测试：`dotnet test San9Remake.slnx`通过166项；Python校验器2项通过。覆盖内部/跨包引用、归属、选择同步、设施过滤、小地图坐标和既有相机/画布边界。
- 私有样本审计：45幅UI图像可见RGB/Alpha与源解码一致，所有PNG哈希一致；94项据点、董卓13项设施及主要数值/名称通过断言。地图方格、绘制、建筑、四季纹理与动画相对schema3基线不变。
- 独立性验证：仅复制地图所需源文件，目录不含M_MsgPK或G_Main，`import-map`成功，产物与组合包内map.json逐字节一致。输出`local/generated/verification/client-history/map-only`。
- 引擎自检：Godot 4.7.2 Mono/OpenGL/RTX5070图形运行；地图拾取、拖动、相机边界、四季切换通过，HUD通过视口分发事件验证列表滚动定位、地图选择、小地图、大小按钮、禁用操作及输入隔离。
- GPU截图：`local/generated/verification/client-history/ui-refactored.png`及`ui-wide.png`，分别为1024×768内容和1280×720窗口下960×720内容；逻辑画布均1024×768。与拆分前`local/generated/verification/map-history/ui-final.png`和`ui-wide.png`逐像素零差异。画面保留原版框架布局、洛阳构图、文字位置与透明边缘。
- 截图仅为GPU游戏内容，不包含OS窗口装饰与宽屏两侧黑边；未执行操作系统鼠标自动化。黑边输入拒绝由画布自动测试和引擎输入检查覆盖，不能把它称为OS验收。

复现：

```powershell
dotnet test San9Remake.slnx
python -B -m unittest discover -s research/scripts -p test_verify_initial_ui.py
python -B research/scripts/verify_initial_ui.py "<已登记EXE>" private/s9数据文件 local/generated/content/scenario-190-live
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot --log-file "<绝对日志路径>" -- --ui-smoke --map-time-ms 0 --map-screenshot "<绝对PNG路径>" --map-quit
```

宽屏在Godot参数（`--`之前）增加`--resolution 1280x720`。`--baseline`仅用于已有历史基线的研究对照，旧包清理后日常验收不再传入此参数。首次主界面接入的165项测试及旧截图保留于RE-0157历史验收。

## 单JSON整理（2026-09-06）

当前默认包改为`local/generated/content/scenario-190-live`。正式导入器直接交付单一content.json，客户端、离线渲染与资源审计同步读取。以上拆分回归中的旧包路径为历史记录；新包验收完成；按用户指示，7个旧包及临时地图副本已移入Windows回收站，逐一验证原路径已不存在。截图、日志和独立研究资料保留。清理明细和本次验收位于`local/generated/verification/interface/`。

高一致性包完成后，旧默认包和两次临时转换包已移入Windows回收站，验证包重命名为默认`local/generated/content/scenario-190-live`；该目录仅保留一个content.json。最终GPU自检与截图均使用默认运行入口。
# 按钮状态补充验收（2026-09-06）

后续文字位移验收：StrategyButton统一管理文字与背景，同一选中状态控制(2,2)逻辑像素绝对偏移；取消选中恢复(0,0)。186项自动测试通过，引擎自检覆盖8种疆界状态的实际Label位置、重复刷新不漂移和大/小选中同步。实际GPU截图`local/generated/verification/interface/button-text.png`与`button-text-wide.png`已检查，窗口分别1024×768和1280×720；另有`-small`及`-boundaries-off`状态截图。文字颜色和效果仍为现有样式，不将本次位移修订表述为原作文字效果全部还原。

主界面按钮使用RE-SAN9PK-0158确认的四帧/六帧映射，由StrategyButton统一解析悬停、按下或选中和不可用状态。已有皮肤包包含全部帧，本次无需修改契约或重新转换。动态文字样式沿用现状，本次调整背景帧选择。

`dotnet test San9Remake.slnx`通过184项，新增测试覆盖两种按钮的全部16种状态组合。实际Godot引擎自检通过疆界的8种状态、不可用按钮第四帧，以及原有导航和输入隔离检查。GPU截图为`local/generated/verification/interface/button-states.png`和`button-states-wide.png`，分别对应1024×768与1280×720窗口；后者内容区960×720。附属`-small`、`-boundaries-off`截图验证保持选中状态。此处为引擎注入输入与GPU输出，不声称操作系统鼠标测试。

## 背景与地域悬停验收（2026-09-06）

依据RE-SAN9PK-0158的只读逆向补证，顶部三块资源框移除额外整体透明度；底中三段背景与右下地域背景使用源调色板覆盖度转换的Alpha。底中背景按逻辑字格调整宽度。地域栏只消费鼠标方格，分别显示地域和所属城市，城市本身也保留所属城市；选择变化不覆盖悬停内容，UI、小地图、黑边和离开窗口时隐藏。地图高度修正拾取仍为已记录的后续差异，不将既有逆投影称为完全还原。

内容契约升级至组合4、地图7、皮肤3，新增必填HideRegionName显示标志并重新转换默认190年包；旧默认包已移入Windows回收站。没有旧包兼容分支。

- 自动测试：`dotnet test San9Remake.slnx`通过209项；相关Python单元测试通过2项。
- 私有资源审计：66幅界面资产可见RGB/Alpha及哈希通过核对，初始数值和名称校验通过。资金仍为9520，与截图9076的差异不强改。
- 引擎输入自检：两个窗口尺寸均通过UI_SMOKE_OK与MAP_SMOKE_OK，覆盖洛阳、虎牢关、孟津港悬停、选择不覆盖、UI/小地图/黑边输入隔离及既有导航。
- 实际GPU截图：`local/generated/verification/interface/shadows.png`与`shadows-wide.png`；窗口分别1024×768、1280×720，宽屏内容区960×720。附属`-small`、`-hover`、`-disabled-hover`、`-region-gate`、`-region-port`截图覆盖缩放和地域状态。已检查顶部背景、底部渐变边缘、文字裁切及建筑条遮挡；保留DEC-UI-0002允许的现代文字渲染差异。

此次为引擎注入输入和GPU渲染验证，没有执行操作系统鼠标交互测试。截图和转换产物均留在忽略目录。

## 设施名单对齐验收（2026-09-06）

现代实现：Godot焦点、原生滚动输入与原图滚动条/箭头和通用弹出菜单，简体文字与标准Alpha。保留原作可见规则；Tab控件顺序、Space激活为明确的现代差异。部队页、详图面板、完整军团和君主移动命令未实现，不声称这些用例已验收。

导出在现有目录原位更新：校验未变化地图来源与图像后复用地图，仅重编译剧本/文本/皮肤；变化PNG才写入，最终替换清单并清理失效图像。重复导出验证确认全部PNG修改时间保持不变，注入的失效导出文件被清除。此次原游戏独占消息文件，使用已有 `private/s9数据文件` 同版本样本导入；原游戏保持打开。

- 自动单元测试：`dotnet test San9Remake.slnx --no-restore` 255项通过；Python校验器2项通过。
- 私有资源审计：78幅UI图像RGB/Alpha与源解码一致，君主引用、董卓13项设施（含107郿城塞）、名称和全部图像指纹通过。
- 引擎输入自检：Godot 4.7.2 Mono/OpenGL/RTX5070，焦点保留/转移、按下位移、拖出取消/移回、褐色点击、Enter、Tab/Shift+Tab、Down不换行、双击、右键菜单及Escape、滚轮幅度归一、箭头、轨道13行和拖动滑块全部通过。溢出使用诊断层合成直属归属夹具，恢复后检查隐藏滚动条；未改会话世界。
- GPU截图：1024×768普通、焦点、移出保留焦点、按下和取消五种状态已逐图检查；君主洛阳首行固定、底板不重复，郿城塞保留。截图及无错误引擎日志在 `local/generated/verification/facility-list/`。
- 操作系统输入：真实重制窗口右键打开菜单、Escape关闭、随后点击长安仍能定位，最后点击君主行恢复洛阳。原游戏保持打开；原版交互证据另见RE-0158，不将引擎派发按住测试冒充操作系统按住截图。

不提交原版资源、生成包或GPU截图。独立宽屏本轮未重跑，既有等比画布规则未变；完整部队页、军团切换及君主移动命令未实现。

## 武将内容与共用名单

当前scenario5以 `OfficerInitialState(NameKey, State)` 分离姓名定义与武将运行态，四项当前能力从基础值、伤势和逐能力加成计算。皮肤6使用 `roster-row-*`、`roster-available/unavailable` 共用语义。武将页已启用，与设施共用固定君主行、普通13行、分页独立滚动和相机定位；本轮不做视觉验收，由用户检查。原版基线与范围见 [BEH-UI-0002](../specs/behavior/BEH-UI-0002-officer-roster.md)，三名私有字形姓名的现代显示映射见 [DEC-UI-0005](../specs/decisions/DEC-UI-0005-officer-name-normalization.md)。

## 原版文本统一入口

转换能力保留在 `San9.Importer.San9PkTc101.Text.OriginalTextConverter`。输入原版字节，输出TextEntry：CP950与已确认扩展码位统一解码为正确SourceText，再用OpenccNetLib 1.6.2的T2S配置生成Text。人物、地名、地区建筑及EXE设施后缀共用入口。消息容器只提取字节，JSON序列化输出UTF-8，客户端不依赖OpenCC。

FA40/41/45直接映射伷/璝/儁，与所处姓名或正文无关。未知扩展与脚本/图标控制明确拒绝。璝按所用词典显示为𪻺；异体字归一化仍待议，见[DEC-UI-0005](../specs/decisions/DEC-UI-0005-officer-name-normalization.md)。没有独立文本项目或按整姓名补救逻辑。

## 当前名单与文本验证

- 262项C#测试通过，覆盖武将四维能力、会话隔离、行动后可用性、两页独立滚动和统一字节转换。
- UI审计脚本2项、字体审计脚本3项测试通过。私有样本审计核对195名驻留武将与78幅UI资产。
- Godot无窗口输入自检验证真实武将页切换、定位、滚轮、轨道、拖动和跨页滚动保留；运行态自检验证内政成功与重复命令拒绝。
- 统一入口重导出后514条显示值不变，4条SourceText纠正为原字（孔伷势力名及三个人名）。默认包原地更新，未提交源资源或生成物。
- 本轮武将与文本视觉检查由用户负责；上文GPU记录属于此前设施及主界面验收，不代表本轮视觉验收。

依赖资料：[OpenCC](https://github.com/BYVoid/OpenCC)、[OpenccNetLib](https://github.com/laisuk/OpenccNet)。依赖版本固定，不声称移植版词典与上游master实时一致。


### 滚动条原图与组件复用验收（2026-09-06）

原版证据见[RE-SAN9PK-0158](../research/findings/RE-SAN9PK-0158.md)滚动条复核，重制契约见[DEC-UI-0004](../specs/decisions/DEC-UI-0004-facility-list-interaction.md)。皮肤schema6区分`roster-scroll-decoration`与`scroll-track-*`、`scroll-thumb-*`、`button-scroll-up/down`四帧；导入器、校验器与客户端同步更新，原目录增量导出并清理旧scroll-track.png，复用地图资源。

`SkinnedScrollBar`只维护原生范围、箭头及绘制，`ScrollBarSkin`传入分段图和布局；名单自身保留行模型。未来人物界面可选择自己的皮肤与页大小，尚未实现这些界面的业务。

验证：262项.NET测试、2项Python解析测试通过；私有样本独立脚本核对92项原图可见RGB/Alpha全部通过。Godot无窗口输入自检与1024×768、1280×720图形运行均取得UI_SMOKE_OK/MAP_SMOKE_OK；GPU截图`local/generated/verification/scrollbar-scroll-top.png`、`scrollbar-scroll-bottom.png`及`scrollbar-wide-scroll-bottom.png`已检查，含顶部/底部禁用箭头及中间滚动状态。图形测试移至屏幕外运行以隔离桌面指针影响；首次屏幕内执行曾在既有设施行按下断言失败，隔离后完整通过。上述为引擎派发输入和GPU输出，未做操作系统鼠标交互验收。
