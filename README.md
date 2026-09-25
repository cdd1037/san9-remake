# 三国志 9 重制

使用 Godot 与 C# 重建《三国志 IX》的策略体验。原作提供研究和设计基线，重制版可以采用现代技术并调整玩法；目标行为与验收标准记录在行为规格中，需要独立解释的重要长期取舍另记设计决策。

## 当前能力与边界

[核心玩法路线图](specs/roadmap.md)的 G00–G10 已完成阶段验收，后续为 G11 完整战略 AI。当前客户端仍以地图与情报浏览为主，核心命令接入不代表对应菜单和画面已交付。人才提拔培养（小兵培养）为按需启动的独立支线，不纳入主线。

| 范围 | 已有能力 | 详细说明与验证 |
| --- | --- | --- |
| 时间与会话 | 日／旬／月／季／年调度、任务占用、确定性恢复、存档、命令日志及验证用录像 | [运行时状态](docs/runtime-state.md)、[G01](docs/g01-verification.md)、[G02](docs/g02-verification.md) |
| 经营与人事 | 内政、兵粮收支、粮食买卖、探索登用、奖惩与宝物、军团治理及人员归属迁移 | [G03](docs/g03-verification.md)、[G04](docs/g04-verification.md)、[人事审计](docs/g04-implementation-audit.md) |
| 地图与军事 | 领域拓扑和寻路、编队行军／输送、普通战斗、兵法、攻城占领、设施建设、俘虏与战后清理 | [G05](docs/g05-verification.md)、[G06](docs/g06-verification.md)、[G07](docs/g07-verification.md)、[军事外交边界](docs/military-diplomacy-verification.md) |
| 外交与计略 | 联盟、谈判、赠与、俘虏返还、劝降、八类计略、军团方针及有限自动规划 | [G08](docs/g08-verification.md) |
| 人物生命周期 | 登场、成长、学习、伤势与死亡、在野迁移、修行、继任及普通战斗触发的单挑 | [G09](docs/g09-verification.md) |
| 事件与结局 | 38 项搜索对象、支持范围内的周期／历史事件、实时选择、势力变动、终局及八类结局 | [事件覆盖](docs/g10-event-coverage.md)、[G10](docs/g10-verification.md) |
| 地图显示 | 190 年地图、拖动缩放、四季预览、地貌合成、旗帜／水面／守兵动画、建筑当前状态和悬停 | [地图](docs/map-import.md)、[纹理资源](docs/texture-resources.md) |
| 界面与文本 | 战略主界面、小地图／全图、设施／武将名单、十类情报目录与详情、统一 BBCode 消息和文字样式 | [UI 文档](docs/ui/README.md)、[情报界面](docs/ui/information-browser.md)、[文本管线](docs/client-content.md#原版文本统一入口) |

阶段验证包含公开夹具、190 年内容包、保存恢复与独立 CLI 录像重放；具体范围以链接记录为准。当前限制：

- 正式经营、买卖、战斗及事件选择等菜单和表现尚未完整接入，主界面“进行”按钮暂不可用；新增画面按任务单独验收。
- 完整战略 AI、全局遮挡和季节随模拟日期联动仍属后续范围，已有自动规划仅覆盖指定行为。
- 录像当前用于确定性验证；面向玩家的全程回看、跳转与接手分支尚未交付，见[录像交付边界](specs/behavior/BEH-SAVE-0001-session-persistence-replay-and-headless.md#面向玩家交付前的后续要求)。玩家读档与精确恢复的随机语义见[随机与 SL 约定](docs/randomness.md)。
- 旧内容包须按当前契约重新转换，旧规则存档／日志／录像明确拒绝加载。

原版素材、转换包与界面字体不随仓库分发。运行前须完成本地转换，并将 `LXGWWenKaiMonoGBLite-Medium.ttf` 放入 `src/San9.Client.Godot/assets/`；仓库保留 Godot 导入配置和 OFL 许可证。

## 快速开始

在仓库根目录执行。准备 .NET 10 SDK 和 Godot 4.7.2 Mono；仅运行研究侧图像脚本时需要 Python 与 Pillow。

### 构建与测试

```powershell
dotnet build San9Remake.slnx
dotnet test San9Remake.slnx
```

默认解决方案测试不包含 Headless 进程测试。涉及 CLI、退出码、文件产物、持久化／录像协议、跨进程接线或公共测试配置时，单独运行：

```powershell
dotnet test tests/San9.Headless.Tests/San9.Headless.Tests.csproj
```

测试项目职责、局部运行命令与精简原则见[测试说明](tests/README.md)。

后续开发默认不执行以前的 Godot 画面验收脚本；只有当前任务明确要求复现对应场景时才按需运行。分类与入口见[Godot 历史画面验收](docs/ui/client-verification.md)。

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

右键拖动、滚轮缩放、左键选择设施或方格；点击小地图或右侧设施列表定位（不改变地图命令起点）；名单移入保留焦点、Enter定位，君主设施固定首行，「小」打开带势力/设施/部队图层与排行榜的战略全图，「大」返回详细地图（保持缩放），建筑标注暂随地图缩放；疆界按钮切换边界显示。F3打开调试面板，可对选中城市执行单人商业或开垦，并预览四季。正式经营菜单、战斗及“进行”按钮暂不可用。旧包需重新转换；主界面与验收见[UI 文档](docs/ui/README.md)，纯地图转换与离线渲染见[大地图导入与运行](docs/map-import.md)。

### 无界面运行

```powershell
dotnet run --project src/San9.Headless -- verify --fixture --xuns 36 --output local/generated/verification/roadmap-g02/public
```

支持玩家读档、精确恢复、JSONL 命令重放、验证用录像和机器报告，参数与失败码见[G02 使用说明](docs/g02-verification.md)。

## 工程结构

| 目录 | 职责 |
| --- | --- |
| `src/San9.Core/` | 不依赖 Godot 的确定性领域核心 |
| `src/San9.Application/` | 游戏会话、用例与策略界面选择状态 |
| `src/San9.Content/` | 地图、剧本初始态、文本、皮肤契约及组合校验 |
| `src/San9.Importer.San9PkTc101/` | 繁中 PK 样本识别、源数据解析与资源转换 |
| `src/San9.Rendering/` | 引擎无关的地貌合成、地图坐标及动画帧选择 |
| `src/San9.Client.Godot/` | 输入、相机、UI 与地图绘制 |
| `src/San9.Headless/` | 无界面会话、命令日志与录像验证 |
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
- [UI 文档](docs/ui/README.md)：战略界面、情报、布局、文字、消息与画面验证的统一入口
- [客户端内容管线](docs/client-content.md)与[大地图导入与运行](docs/map-import.md)
- [研究区说明](research/README.md)、[研究结论索引](research/findings/README.md)与[来源登记表](research/SOURCES.md)
- [本地私有文件规则](private/README.md)
