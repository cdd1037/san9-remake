# 三国志 9 重制

使用 Godot 与 C# 重建《三国志 IX》的策略体验。原作提供研究和设计基线，重制版可以采用现代技术并调整玩法；目标行为与验收标准记录在行为规格中，需要独立解释的重要长期取舍另记设计决策。

## 当前能力与边界

当前按[核心玩法路线图](specs/roadmap.md)推进，G01时间内核、G02保存重放与无界面运行、G03经营与兵员循环、G04人事与治理基础已完成，下一阶段为G05领域地图与出征行军；人才提拔培养（小兵培养）独立为按需开发支线，不纳入重制主线，使用 Godot 4.7.2 Mono（.NET 版）和 .NET 10。

随机判定支持固定条件骰与读档可重抽的运行流，录像始终精确恢复。普通登用和探索发现门不受垫操作影响；CLI用`--load`玩家读档、`--restore`精确恢复，详见[随机与SL约定](docs/randomness.md)。

G04公共命令支持普通探索、登用与归队、多人调动、军师和官职任免、奖惩与宝物、俘虏释放、忠诚周期，以及军团设立/变更/解散和直接/自动控制。190年完整剧本的人事治理与真实经营运行一年，保存恢复、命令日志与录像重放一致，见[验收记录](docs/g04-verification.md)和[实现审计](docs/g04-implementation-audit.md)。当前剧本初始态schema23，旧包需重新转换，旧规则存档明确拒绝；战争、完整战略AI和新增菜单尚未接入。

- 已接入190年剧本初始地图，支持方格选择、拖动、缩放和四季外观切换；相机中心按含高度的地图边界限制，外围延续云层。
- 地图跳转在目标地形和对象纹理准备完整后提交，等待期间保持旧视野；资源并行准备、可见动画按帧变化刷新，计时与验收见[地图性能记录](docs/map-import.md#视野提交与性能验收2026-09-06)。
- 转换包按类别生成静态纹理图集，转换已确认资源并明确未决边界；头像和宝物只保留单份大图；显示资源使用统一GPU缓存，头像按需加载且不保留CPU图片副本；缓存预算、资源生命周期与画面验收见[纹理资源](docs/texture-resources.md)。
- 已接入董卓势力初始主界面：原版框架与按钮美术、顶部与底部原版背景、耐久/士气条和城市图标、原版小地图配色、疆界开关、独立设施名单焦点、君主固定首行与镜头定位；右下地域栏随鼠标显示地域与所属城市。
- 滚动条使用已核对的原版分段纹理与四帧箭头，输入使用Godot原生控件；独立皮肤组件供后续列表复用。
- 大地图城市与普通建筑支持悬停高亮和详情卡，按归属与外交关系使用三种原版背景；字段来自当前会话，详见[悬停规格](specs/behavior/BEH-UI-0005-map-building-hover.md)。
- 游戏文字通过统一样式适配八邻域文字副本与单向阴影，按原作位置及按钮状态配置效果，保留现有简体字体并允许现代渲染差异。
- 地貌保留四套季节纹理，运行时按块合成；水面、旗帜和守兵播放帧动画，旗帜随预览季节改变方向。
- 已导出完整建筑纹理目录，以及全部旗色、旗字和守兵方向；建筑外观按运行时耐久、开发度、驻兵和归属选择。
- 右侧设施/武将栏共用名单控件：君主固定首行、直属军团过滤、独立滚动、点击定位和行动可用性；武将四项能力来自运行态，显示文本在导入时通过导入器统一原版文本入口解码并经OpenCC转简体。详见[武将栏规格](specs/behavior/BEH-UI-0002-officer-roster.md)。
- GameSession持有独立运行时势力、据点、人物和宝物等状态；1–5人商业、开垦、巡察、修筑、征兵和训练命令统一校验账户与行动，HUD与地图读取同一份世界状态。详见[运行时架构](docs/runtime-state.md)。
- Core具备日级日历、旬/月/季/年串行调度、控制轮次与统一命令校验、持续任务占用/到期重验、命名随机及稳定快照恢复；新局前置更新一次，恢复不重复。公开夹具与本地190年包的36旬脚本运行均通过连续/恢复一致性验证；任务规则使用内核夹具，实际人事任务已在G04接入，战争任务仍待后续阶段。见[G01验收](docs/g01-verification.md)。
- 情报接入十类目录及详情浏览，武将和宝物表格支持排序、滚动及详情返回；宝物出土/转移、人物登场等命令尚未实现。详见[情报界面](docs/information-browser.md)。
- 地图包已有方格数据，尚不能据此认为 Core/Application 已具备完整地图领域模型、寻路或地形代价计算。
- Core已实现多人内政组合、征兵训练、俸禄、日粮耗、季度人口/收入与七月收获；公开场景与190年内容通过一年经营、保存恢复和重放，见[G03验收](docs/g03-verification.md)。简单策略经公共命令下令；完整战略AI、移动部队、全局遮挡和季节随日期联动仍属后续范围。

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

独立无界面入口：`dotnet run --project src/San9.Headless -- verify --fixture --xuns 36 --output local/generated/verification/roadmap-g02/public`。支持轻量玩家存档、严格恢复、序号水位和独立调试录像、JSONL命令重放与机器报告；用法、失败码和190年验证见[G02验收与使用说明](docs/g02-verification.md)。
