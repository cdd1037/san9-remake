# 三国志 9 重制

使用 Godot 与 C# 重建《三国志 IX》的策略体验。原作提供研究和设计基线，重制版可以采用现代技术并调整玩法；影响规则、数据语义或玩家预期的重要差异通过行为规格与设计决策记录。

## 当前能力与边界

当前处于工程基线与地图 MVP 阶段，使用 Godot 4.7.2 Mono（.NET 版）和 .NET 10。

- 已接入190年剧本初始地图，支持方格选择、拖动、缩放和四季外观切换；相机中心按含高度的地图边界限制，外围延续云层。
- 地图跳转在目标地形准备完整后提交，等待期间保持旧视野；资源并行准备、可见动画按帧变化刷新，计时与验收见[地图性能记录](docs/map-import.md#视野提交与性能验收2026-09-06)。
- 已接入董卓势力初始主界面：原版框架与按钮美术、顶部与底部原版背景、耐久/士气条和城市图标、原版小地图配色、疆界开关、独立设施名单焦点、君主固定首行与镜头定位；右下地域栏随鼠标显示地域与所属城市。
- 主界面文字通过统一样式适配Godot原生描边与阴影，按原作位置及按钮状态配置效果，保留现有简体字体并允许现代渲染差异。
- 地貌保留四套季节纹理，运行时按块合成；水面、旗帜和守兵播放帧动画，旗帜随预览季节改变方向。
- 已导出完整建筑纹理目录，以及全部旗色、旗字和守兵方向；建筑外观按运行时耐久、开发度、驻兵和归属选择。
- 右侧设施/武将栏共用名单控件：君主固定首行、直属军团过滤、独立滚动、点击定位和行动可用性；武将四项能力来自运行态，显示文本在导入时通过导入器统一原版文本入口解码并经OpenCC转简体。详见[武将栏规格](specs/behavior/BEH-UI-0002-officer-roster.md)。
- GameSession持有独立运行时势力、据点和驻留武将；单人商业/开垦命令会扣费、提高开发度并消耗行动，HUD与地图读取同一份世界状态。详见[运行时架构](docs/runtime-state.md)。
- 地图包已有方格数据，尚不能据此认为 Core/Application 已具备完整地图领域模型、寻路或地形代价计算。
- Core已实现[四项内政基础增量](specs/behavior/BEH-DOMESTIC-0001-base-development-effect.md)，具有独立自动测试；多人组合、巡察/修筑命令、周期经济和存档闭环尚未接入；移动部队、全局遮挡和季节随日期联动也属于后续范围。

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
dotnet run --project src/San9.Tools -- import-client --source-exe "<San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --scenario "private/s9数据文件/D_Sce002.S9" --output "local/generated/content/scenario-190-live"
```

输出目录必须不存在。包内只有一个`content.json`，按map、scenario、text、skin分区保存；图片按`images/map/`和`images/interface/`分类。纯地图仍可用`import-map`独立转换；包结构见[客户端内容与主界面](docs/client-content.md)。

### 运行客户端

用 Godot Mono 打开 `src/San9.Client.Godot/project.godot`，构建 C# 后运行主场景。客户端默认读取上述 `scenario-190-live` 包，也可从命令行指定：

```powershell
& "<Godot Mono可执行文件>" --path "src/San9.Client.Godot" -- --content-package "<客户端内容包绝对路径>"
```

右键拖动、滚轮缩放、左键选择设施或方格；点击小地图或右侧设施列表定位（不改变地图命令起点）；名单移入保留焦点、Enter定位，君主设施固定首行，大小按钮选择显示比例，建筑标注暂随地图缩放；疆界按钮切换边界显示。F3打开调试面板，可对选中城市执行单人商业或开垦，并预览四季。正式经营菜单、战斗及“进行”按钮暂不可用。旧包需重新转换；主界面验收见[客户端内容与主界面](docs/client-content.md)，纯地图转换与离线渲染见[大地图导入与运行](docs/map-import.md)。

## 工程结构

| 目录 | 职责 |
| --- | --- |
| `src/San9.Core/` | 不依赖 Godot 的确定性领域核心 |
| `src/San9.Application/` | 游戏会话、用例与策略界面选择状态 |
| `src/San9.Content/` | 地图、剧本初始态、文本、皮肤契约及组合校验 |
| `src/San9.Importer.San9PkTc101/` | 繁中 PK 样本识别、源数据解析与资源转换 |
| `src/San9.Rendering/` | 引擎无关的地貌合成、地图坐标及动画帧选择 |
| `src/San9.Client.Godot/` | 输入、相机、UI 与地图绘制 |
| `src/San9.Tools/` | `import-client` 组合转换、`import-map` 纯地图转换与 `render-map` 静态导出 |
| `tests/` | 自动化测试 |
| `research/` | 来源、实验、逆向证据及独立验证脚本 |
| `specs/` | 行为规格、设计决策与路线图 |
| `local/generated/` | 被 Git 忽略的转换包、缓存与验证产物 |

`local/generated/`按`content/`（当前运行包）、`verification/`（验收截图与报告）、`research/`（本地研究证据）整理，避免临时文件散落根目录。

客户端运行时只读取转换后的内容，不读取源 EXE、S9、IDA 数据库或研究脚本。地图动画由独立显示时钟选帧，再交给 Godot 绘制；显示状态与模拟状态分离。

## 研究与实现依据

```text
原始资料与实验 → 研究证据（RE）→ 已确认行为规格（BEH）→ 独立实现与测试
                                      ↑
                              重制设计决策（DEC）
```

研究结论区分事实、推断与待验证。产品实现以已确认行为规格为依据，不直接翻译反编译代码；原创或现代化设计也须明确目标行为和验收标准。此处是个人项目的证据与实现分层，不代表独立人员洁净室认证。

私有样本、原版资源、IDA 数据库、密钥和生成物均不提交。提交标题与说明使用英文及带具体 scope 的 Conventional Commits，详细协作规则见 [AGENTS.md](AGENTS.md)。

## 文档导航

- [项目规范索引](specs/README.md)与[整体路线图](specs/roadmap.md)
- [行为规格](specs/behavior/README.md)与[设计决策](specs/decisions/README.md)
- [客户端内容与主界面](docs/client-content.md)与[大地图导入与运行](docs/map-import.md)
- [研究区说明](research/README.md)、[研究结论索引](research/findings/README.md)与[来源登记表](research/SOURCES.md)
- [本地私有文件规则](private/README.md)
