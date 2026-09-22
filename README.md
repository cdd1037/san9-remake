# 三国志 9 重制

使用 Godot 与 C# 重建《三国志 IX》的策略体验。原作提供研究和设计基线，重制版可以采用现代技术并调整玩法；目标行为与验收标准记录在行为规格中，需要独立解释的重要长期取舍另记设计决策。

## 当前能力与边界

战场支持[混战阵亡](specs/behavior/BEH-WAR-0005-tactics.md#混战阵亡)：双倍武力兵法击破野外部队后，在俘获前判定阵亡；玩家直属、委任与电脑采用相同规则。救援去返程兵员纳入[周期经济统计](specs/behavior/BEH-ECONOMY-0001-domestic-and-economic-cycle.md#在途救援的军事人口)，[途中后勤](specs/behavior/BEH-STRATEGY-0002-unit-status-and-reinforcement.md#在途补给与兵员损耗)按军团账户结算粮耗、断粮损失和伤兵返程。

战场已支持[狙击](specs/behavior/BEH-WAR-0005-tactics.md#2026-09-21-狙击)：部队／设施兵法可造成武将伤势及受规则约束的阵亡，同次击破仍保留伤势。

战场已支持[兵法解救俘虏](specs/behavior/BEH-WAR-0005-tactics.md)及[击破部队后的俘虏释放返程](specs/behavior/BEH-WAR-0003-unit-return-defeat-and-force-cleanup.md)：按原作处理己方与第三方俘虏，获救者独立返程；友好度与联盟关系对第三方处理的影响仅列为未来考虑。

当前按[核心玩法路线图](specs/roadmap.md)推进，G01时间内核、G02保存重放与无界面运行、G03经营与兵员循环、G04人事与治理基础、G05领域地图与出征行军、G06普通战斗与攻城占领、G07完整军事手段、G08外交计略、G09人物生命周期与单挑和G10事件、终局与结局已完成；人才提拔培养（小兵培养）独立为按需开发支线，不纳入重制主线，使用 Godot 4.7.2 Mono（.NET 版）和 .NET 10。

城市商人旬刷新及粮食买进／卖出已补齐，`GetFoodTradeQuote`提供当前报价，`FoodTradeCommand`提交交易；军团账户、共享旬次数、当前商人图标与保存／录像已验证，买卖菜单待界面阶段，见[G03补齐](docs/g03-verification.md#2026-09-20城市商人与粮食买卖补齐)。

G10完整搜索表38项对象已接入，包括最后八项民间事务与地点修学；新增`GetCommunitySearchPrompt()`及`RespondDailyTaskCommand.CommunitySearch`，规则、选择、结算与保存录像贯通，专用菜单和画面待表现阶段。G10已完成玩法阶段验收，190年整包、独立CLI一年运行及跨域集成通过；已接入普通场景胜负／期限、八类结局、责任者与方针选择，以及结局会话保存和录像；普通探索发现人物后可实时选择是否登用，主动求官已支持接受／拒绝与续行，联盟选择复用统一活动事件槽；华佗疗伤及神兽战斗／逃走已接入搜索并在完成后结束本次搜索，电脑势力合并已支持玩家占城触发、无候选重试、完整人员／资产转移及奖励；空城起兵已支持有向邻城支持者、动态势力身份、初始化、查询及保存／录像，城市自立／失败下野已支持独立概率、直属返程、俘虏与旧势力清理，皇帝动态解析与季度前置死亡已支持护持信望、当前图标、保存和录像；季度晋爵／册授、称帝／禅让及国号选择已接入原子会话、动态旗字和保存／录像；二乔历史事件已接入完整资格、寿命／信望／外交奖励及持久消费，并与季度联盟按呈现结果短路；孙尚香已接入跨势力转属、任务／部队／军团清理及关系／忠诚／兵法奖励；汉中王、五虎将及出师表已接入，季度主表首成功短路与独立政治收尾联动已验证；三顾之礼已接入推荐、两次探索、同行归队与一年超时，并覆盖稳定保存／录像；连环计已接入前后段、三种失败收尾、亲族处置和指定继任，以及宝物／军团／联盟清理与保存／录像；赤壁已接入战区兵力资格、九成减兵、退兵及信望／忠诚奖励，并覆盖保存／录像；山阳公已接入探索资格、保护／处斩、分步国号选择及忠诚／外交效果，并覆盖原子提交与保存／录像；蔡琰归还已接入接受／拒绝、能力与兵法奖励、全局消费及势力成功历史，并覆盖保存／录像；封禅已接入君主探索、四能力／信望／在职忠诚奖励及一次性消费；铜雀台已接入宝物／官爵／资金资格、建造选择、扣款／信望及拒绝重试；望蓬莱已接入整组未启用状态、控州／物理地域资格、倭城及港口／五人启用、增兵／信望和保存／录像；官渡已接入登用前资格、保证登用、地域减兵／归还、根账户扣粮与信望／忠诚奖励，覆盖失效取消和保存／录像；皇帝城市转属已接入旬末最终归属结算、拥立／废帝选择与强制分支、忠诚和汉室状态以及保存／录像；女性访问已接入四个地点的兵法指导、独立日期／历史前置和可重复学习；司马徽访问已接入推荐人物对探索势力的登用意愿奖励、重复访问和无奖励收尾；于吉／华佗访问已接入医书唯一转移及当前持有的能力／兵法效果；管辂访问已接入独立势力历史、当前信望门和保存／录像，延命已接入接受／拒绝、军团资金资格与同账户全额扣款及寿命提升；祢衡遭遇已接入无视／处斩失败、智力／信望分支及搜索者伤势，覆盖保存／录像；左慈授书与索还已接入统率概率、军团账户费用、拒绝扣款／永久兵法奖励及宝物循环转移；猛虎讨伐已接入州范围、战斗／拒绝、信望与伤势结果，可重复遭遇且失败不减寿命，并验证与神兽的候选竞争；商人投资已接入八类地点、金额输入、投资收益／失败、拒绝及处斩分类封禁，并接入每旬粮价与保存／录像；要求金钱、名士处罚、恶人、化物退却、贼退却、旅人求助、贼归顺七项已接入，涵盖选择、军团收支、名士离场及保存／录像；其他事件族仍在接入，见[G10验证](docs/g10-verification.md)。公开入口为`GameSession.GetTerminalResult()`、`GetEndingPrompt()`、`EndingOperation`、`Ending`、`GetSearchRecruitmentPrompt()`、`GetBeastSearchPrompt()`、`GetTigerSearchPrompt()`、`GetSearchConflictPrompt()`／`RespondDailyTaskCommand.SearchConflict`、`GetMerchantInvestmentPrompt()`／`MerchantInvestmentResponse`、`GetShanyangGongPrompt()`、`GetScholarReturnPrompt()`、`GetTreasureMonumentPrompt()`、`GetLongevityPrompt()`、`GetScholarInsultPrompt()`、`GetMysticBookOfferPrompt()`、`GetMysticBookReturnPrompt()`／`RespondDailyTaskCommand`、`GetEmperorTransferPrompt()`／`RespondEmperorTransferCommand`、`GetOfficeRequestPrompt()`及`GetCountryTitlePrompt()`／`ChooseCountryTitleCommand`，CLI命令日志使用同一会话协议。

随机判定支持固定条件骰与读档可重抽的运行流，录像始终精确恢复。普通登用和探索发现门不受垫操作影响；CLI用`--load`玩家读档、`--restore`精确恢复，详见[随机与SL约定](docs/randomness.md)。

G04公共命令支持普通探索、登用与归队、多人调动、军师和官职任免、奖惩与宝物、俘虏释放、忠诚周期，以及军团设立/变更/解散和直接/自动控制。190年完整剧本的人事治理与真实经营运行一年，保存恢复、命令日志与录像重放一致，见[验收记录](docs/g04-verification.md)和[实现审计](docs/g04-implementation-audit.md)。当前剧本初始态schema27，旧包需重新转换，旧规则存档明确拒绝；完整战略AI和新增菜单尚未接入。

G05已接入规范化地形/硬阻挡、阵型移动预算、编队出征、逐日行军、输送与返城吸收。武将所在部队、兵力、载荷及当前命令进入同一世界和存档。190年真实地图完成两旬输送、每旬恢复和独立CLI录像重放；有限自动增援已接入，验收证据及后续边界见[G05推进记录](docs/g05-verification.md)。

G06支持公共部队/据点攻击、接敌、每日普通伤害和反击、伤兵与士气、占领、人物与军团清理、势力退出和初步终局。有限军事策略可从出征打到占城，公开小战役与190年普通交战均经过保存/重放验证，见[G06验收](docs/g06-verification.md)。G07支持完整阵型／兵法映射、武力连携与智力／陷阱／设施效果、都市地利和落石范围、石兵受击反作用、建设／重建／撤除、伤兵恢复、拘押出逃、出征四项方针与性格违令、临时迎击恢复、显式整编、水侧归港、增援输送及账户断粮后果。公开场景与190年实际兵法攻城、建设均通过保存恢复和重放，见[G07验收](docs/g07-verification.md)。G08外交计略核心已完成，已接入190年初始联盟、成员自动停战、到期及灭亡清理；八类计略已接公共命令、意见与去返程，地域地理与联盟候选已提供只读查询，季度形成已支持内存中的玩家选择和部队冲突清理，攻击请求生命周期及正式谈判、赠与、俘虏返还、劝降归并与有限自动外交已接入。四类外交按整旬行程在旬边界谈判，日内暂停续行能力保留；军团四种方针及外交目标清理已接入，有限自动计略、公共行为审计、公开八计略CLI与190年36旬保存／重放验收已完成，见[G08验收](docs/g08-verification.md)。表现与画面验收仍按路线图后置。

- 已接入190年剧本初始地图，支持方格选择、拖动、缩放和四季外观切换；相机中心按含高度的地图边界限制，外围延续云层。
- 地图跳转在目标地形和对象纹理准备完整后提交，等待期间保持旧视野；资源并行准备、可见动画按帧变化刷新，计时与验收见[地图性能记录](docs/map-import.md#视野提交与性能验收2026-09-06)。
- G09已完成：人物登场、成长与兵法学习、旬伤势恢复、季度自然死亡、在野迁移、90日修行、处决及继任已接入；单挑由真实普通战斗触发，玩家继任选择后继续同日结算。公开会话、独立CLI、完整录像和190年一年场景通过，见[G09验收](docs/g09-verification.md)。
- 转换包按类别生成静态纹理图集，转换已确认资源并明确未决边界；头像和宝物只保留单份大图；显示资源使用统一GPU缓存，头像按需加载且不保留CPU图片副本；缓存预算、资源生命周期与画面验收见[纹理资源](docs/texture-resources.md)。
- 已接入董卓势力初始主界面：原版框架与按钮美术、顶部与底部原版背景、耐久/士气条和城市图标、原版小地图配色、疆界开关、独立设施名单焦点、君主固定首行与镜头定位；右下地域栏随鼠标显示地域与所属城市。
- 滚动条使用已核对的原版分段纹理与四帧箭头，输入使用Godot原生控件；独立皮肤组件供后续列表复用。
- 大地图城市与普通建筑支持悬停高亮和详情卡，按归属与外交关系使用三种原版背景；字段来自当前会话，详见[悬停规格](specs/behavior/BEH-UI-0005-map-building-hover.md)。
- 游戏文字通过统一样式适配八邻域文字副本与单向阴影，按原作位置及按钮状态配置效果，保留现有简体字体并允许现代渲染差异。
- 地貌保留四套季节纹理，运行时按块合成；水面、旗帜和守兵播放帧动画，旗帜随预览季节改变方向。
- 已导出完整建筑纹理目录，以及全部旗色、旗字和守兵方向；建筑外观按运行时耐久、开发度、驻兵和归属选择。
- 右侧设施/武将栏共用名单控件：君主固定首行、直属军团过滤、独立滚动、点击定位和行动可用性；武将四项能力来自运行态，显示文本在导入时通过导入器统一原版文本入口解码并经OpenCC转简体。详见[武将栏规格](specs/behavior/BEH-UI-0002-officer-roster.md)。
- GameSession持有独立运行时势力、据点、人物和宝物等状态；1–5人商业、开垦、巡察、修筑、征兵和训练命令统一校验账户与行动，HUD与地图读取同一份世界状态。详见[运行时架构](docs/runtime-state.md)。
- Core具备日级日历、旬/月/季/年串行调度、控制轮次与统一命令校验、持续任务占用/到期重验、命名随机及稳定快照恢复；新局前置更新一次，恢复不重复。公开夹具与本地190年包的36旬脚本运行均通过连续/恢复一致性验证；实际人事任务已在G04接入，G05行军命令复用日级流水线。见[G01验收](docs/g01-verification.md)。
- 情报接入十类目录及详情浏览，武将和宝物表格支持排序、滚动及详情返回；宝物出土/转移、人物登场等命令尚未实现。详见[情报界面](docs/information-browser.md)。
- Core地图使用独立领域拓扑，支持细坐标矩形、地形代价、陆水方式和四向寻路，不以图片决定通行；显示地图仍由Rendering负责。
- Core已实现多人内政组合、征兵训练、俸禄、日粮耗、季度人口/收入与七月收获；公开场景与190年内容通过一年经营、保存恢复和重放，见[G03验收](docs/g03-verification.md)。简单策略经公共命令下令；完整战略AI、全局遮挡和季节随日期联动仍属后续范围。

转换包和原版素材均不随仓库分发。运行地图前，需要在本地完成资源转换。

界面字体文件也不随仓库分发。运行前请将 `LXGWWenKaiMonoGBLite-Medium.ttf` 放入 `src/San9.Client.Godot/assets/`；仓库保留其 Godot 导入配置及 OFL 许可证。

## 快速开始

在仓库根目录执行。准备 .NET 10 SDK 和 Godot 4.7.2 Mono；仅运行研究侧图像脚本时需要 Python 与 Pillow。

### 构建与测试

```powershell
dotnet build San9Remake.slnx
dotnet test San9Remake.slnx
```

### 转换客户端内容

准备已登记且通过指纹校验的繁中 PK 1.0.1.0 EXE，以及本地 `private/s9数据文件/` 下的源数据：

```powershell
dotnet run --project src/San9.Tools -- import-textures --source-exe "<San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --output "local/generated/content/textures-san9pk-tc101"
dotnet run --project src/San9.Tools -- import-client --source-exe "<San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --scenario "private/s9数据文件/D_Sce002.S9" --texture-package "local/generated/content/textures-san9pk-tc101" --output "local/generated/content/scenario-190-live"
```

纹理由 `import-textures` 独立构建，按类别存入共享纹理包；`import-client` 只生成剧本、文本、地图实例及资源引用。增量复用与失效产物清理归纹理构建所有。入口见[内容管线](docs/client-content.md)。

### 运行客户端

用 Godot Mono 打开 `src/San9.Client.Godot/project.godot`，构建 C# 后运行主场景。客户端默认读取上述 `scenario-190-live` 包，也可从命令行指定：

```powershell
& "<Godot Mono可执行文件>" --path "src/San9.Client.Godot" -- --content-package "<客户端内容包绝对路径>"
```

右键拖动、滚轮缩放、左键选择设施或方格；点击小地图或右侧设施列表定位（不改变地图命令起点）；名单移入保留焦点、Enter定位，君主设施固定首行，「小」打开带势力/设施/部队图层与排行榜的战略全图，「大」返回详细地图（保持缩放），建筑标注暂随地图缩放；疆界按钮切换边界显示。F3打开调试面板，可对选中城市执行单人商业或开垦，并预览四季。正式经营菜单、战斗及“进行”按钮暂不可用。旧包需重新转换；主界面验收见[客户端内容与主界面](docs/client-content.md)，纯地图转换与离线渲染见[大地图导入与运行](docs/map-import.md)。

## 工程结构

| 目录 | 职责 |
| --- | --- |
| `src/San9.Core/` | 不依赖 Godot 的确定性领域核心 |
| `src/San9.Application/` | 游戏会话、用例与策略界面选择状态 |
| `src/San9.Content/` | 地图、剧本初始态、文本、皮肤契约及组合校验 |
| `src/San9.Importer.San9PkTc101/` | 繁中 PK 样本识别、源数据解析与资源转换 |
| `src/San9.Rendering/` | 引擎无关的地貌合成、地图坐标及动画帧选择 |
| `src/San9.Client.Godot/` | 输入、相机、UI 与地图绘制 |
| `src/San9.Tools/` | `import-textures` 独立纹理、`import-client` 剧本转换与 `render-map` 静态导出 |
| `tests/` | 自动化测试 |
| `research/` | 来源、实验、逆向证据及独立验证脚本 |
| `specs/` | 行为规格、设计决策与路线图 |
| `local/generated/` | 被 Git 忽略的转换包、缓存与验证产物 |

`local/generated/`按`content/`（当前运行包）、`verification/`（验收截图与报告）、`research/`（本地研究证据）整理，避免临时文件散落根目录。

客户端运行时只读取转换后的内容，不读取源 EXE、S9、IDA 数据库或研究脚本。地图动画由独立显示时钟选帧，再交给 Godot 绘制；显示状态与模拟状态分离。

## 协作与设计依据

原作研究提供证据，行为规格描述采用的目标，产品代码独立实现。研究中的未知项不等于产品待办，规格已采用也不代表功能已实现；当前能力以本页和对应模块文档为准。

修改项目从 [AGENTS.md](AGENTS.md) 开始；BEH/DEC 的维护与状态含义见[规格索引](specs/README.md)，原作研究见[研究区说明](research/README.md)。

## 文档导航

- [项目规范索引](specs/README.md)与[整体路线图](specs/roadmap.md)
- [行为规格](specs/behavior/README.md)与[设计决策](specs/decisions/README.md)
- [客户端内容与主界面](docs/client-content.md)与[大地图导入与运行](docs/map-import.md)
- [研究区说明](research/README.md)、[研究结论索引](research/findings/README.md)与[来源登记表](research/SOURCES.md)
- [本地私有文件规则](private/README.md)

原作文本已统一为原生Godot BBCode及结构化消息目录；转换、全量诊断、字体和图标入口见[内容管线](docs/client-content.md#原版文本统一入口)，覆盖及未确认语义见[RE-0166](research/findings/RE-SAN9PK-0166.md)。

文字显示通过[统一文字样式](docs/text-styles.md)复用角色与状态，消息正文遵循[原生 BBCode 约定](docs/messages.md)。

普通登用已支持城市／设施大将带据点归顺，以及野外大将带部队归顺；普通部队成员只迁移本人，追随／拒绝、返程及保存录像均按显式归属处理，见[人事规格](specs/behavior/BEH-PERSON-0001-discovery-recruitment-and-affiliation.md#野外部队登用)。

旬开始已支持有人据点自行归附、无人无兵设施随关联城市改属及[被围野外部队自行归附](docs/g10-verification.md#2026-09-21野外部队自行归附)，包含人员、金粮及旧军团清理；野外归附明确修复原作将自身计为援军的阻断bug。条件和验证见[G10验收](docs/g10-verification.md#2026-09-21设施自行归附补齐)。

独立无界面入口：`dotnet run --project src/San9.Headless -- verify --fixture --xuns 36 --output local/generated/verification/roadmap-g02/public`。支持轻量玩家存档、严格恢复、序号水位和当前用于验证的独立录像、JSONL命令重放与机器报告；用法、失败码和190年验证见[G02验收与使用说明](docs/g02-verification.md)。

本轮先对齐已有军事规则：强攻确认与设施宣战/背盟、野战实战关系下降、自动选敌与反击分离、普通设施外交通行及固定步进费用；A*优化和确定性速度保留。公共接口与验证边界见[军事外交补齐验收](docs/military-diplomacy-verification.md)。

录像面向玩家的目标是回看全程游戏、跳转及从可操作时间点接手新分支；当前实现先用于确定性验证，这些玩家能力尚未交付，须在正式提供给玩家前完成，见[录像交付边界](specs/behavior/BEH-SAVE-0001-session-persistence-replay-and-headless.md#面向玩家交付前的后续要求)。
