# 三国志 9 重制

使用 Godot 与 C# 重建《三国志 IX》的策略体验。原作提供研究和设计基线；产品采用的行为由规格定义，代码使用独立的现代实现。

## 许可与版权

本仓库自行编写的重制代码、工具、测试及文档采用 GNU GPL v3.0（`GPL-3.0-only`）开源，详见 [LICENSE](LICENSE)；第三方组件与字体遵循各自许可证。开源范围不含原版程序、游戏资源、转换后的原作资源及私有研究样本。

本项目为非官方重制。[《三国志 IX》](https://store.steampowered.com/app/628050/)的名称、商标及原作内容归光荣特库摩（KOEI TECMO）及相关权利人所有，本项目与其无隶属或背书关系。GPL 许可不授予原作内容的使用或再分发权；使用原作资源须自行准备合法取得的游戏副本。

## 当前状态

游戏主要系统和操作流程已经建立，目前重点是持续验证、改善 AI 行为，以及补齐内容创作工具。

| 范围 | 当前情况 |
| --- | --- |
| 主要玩法 | 经营、人事、治理、行军、战斗、外交、计略、人物生命周期、事件与结局已实现。 |
| 玩家操作 | 已接入正式新局、势力与开局设定、地图与情报、主要命令、逐日推进、军团管理、存读档和设置，继续完善交互与信息反馈 |
| 电脑与委任 | 主体基于原版行为，做了局部的缺陷修复和智力改进 |
| 画面与声音 | 战斗、兵法、单挑、外交、剧情、音乐和影片等均接入，帮助、鉴赏等待补齐 |
| 内容创作 | 已有游戏资料、新武将、头像及局内编辑入口，地图、剧本、事件等编辑器待补齐 |
| 发布 | 已有 Windows、Linux、macOS 导出和自动发布流程，移动端导出未支持 |

阶段证据见[历史验收索引](docs/history/README.md)，当前规则见[行为规格](specs/behavior/README.md)。具体进度见[表现层计划](specs/plans/presentation.md)和[原版 AI 计划](specs/original-ai/implementation-plan.md)。

## 后续计划

- **持续验证**：验证多个剧本从新局到结局的完整流程，结合实际游玩、受控场景和长期自动对局，持续修正规则、操作、消息、画面、声音及性能问题；完成长局保存恢复和各平台发布包验证。
- **部分低智 AI 行为修复**：针对资源浪费、无效调动、低效出征、反复改令和重复投入等典型问题，改善经营、人事、军事、输送、计略与外交决策，通过代表局面和长期对局验证效果。
- **编辑器补全**：补齐地图、剧本、事件等编辑器，支持地形与据点布置、剧本初始状态配置，以及事件条件、选择、效果、对白和媒体编辑；完善预览、试运行、保存载入与引用校验，打通自定义内容的创作和运行流程。

正式模组支持、小兵培养、移动端适配、玩家录像、任务途中物理坐标与完整行程流程、事件系统通用化重构另行推进；学习型 AI 作为远期研究。教学／挑战、多人联机、原版存档导入暂不考虑。阶段范围与退出条件见[路线图](specs/plans/roadmap.md)。

## 快速开始

在仓库根目录执行。需要 .NET 10 正式版 SDK、Godot 4.7.2 Mono；研究侧 Python 工具按各自入口准备依赖。

```powershell
dotnet build San9Remake.slnx
dotnet test San9Remake.slnx
```

构建和测试可独立使用；`dotnet test` 自带构建。测试职责、局部入口及独立 Headless 测试的触发条件见[测试说明](tests/README.md)。

### 准备内容

原版素材、转换包及字体不随仓库分发。开发环境将 `LXGWWenKaiMonoGBLite-Medium.ttf` 放入 `src/San9.Client.Godot/assets/`，许可证见[字体说明](src/San9.Client.Godot/assets/README.md)。

选择包含 `San9PK.exe`、S9 数据及剧本的同一个原版目录；当前支持已登记且通过指纹校验的繁中 PK 1.0.1.0。本地样本通常位于 `private/game-files/`：

```powershell
dotnet run --project src/San9.Tools -- import-textures --game-dir "private/game-files" --output "local/generated/content/shared/textures"
dotnet run --project src/San9.Tools -- import-content --game-dir "private/game-files" --output "local/generated/content"
dotnet run --project src/San9.Tools -- verify-content --content-root "local/generated/content"
```

已有旧版重制内容包可离线升级，无需再次读取原版文件：

```powershell
dotnet run --project src/San9.Tools -- upgrade-legacy-content --source "E:/San9R/content" --output "local/generated/upgraded-content" --show-officer-ages true
```

该入口支持旧目录版本 4、剧本版本 243、状态版本 151 和纹理版本 5；输出必须是不存在的新目录。旧内容包未保存剧本的年龄显示开关，`--show-officer-ages` 必须显式指定 `true` 或 `false`，并应用于所有剧本。升级结果包含原有游戏资源，不随仓库分发。

音频和影片另按[音频准备](docs/audio-preparation.md)及[电影准备](docs/movie-preparation.md)处理；完整流程、最小转换范围与发行版内置准备入口见[内容管线](docs/client-content.md)。普通游戏加载只消费转换包。

### 运行客户端

Windows 本机使用 `local/generated` 内的 .NET 10、Godot 和已升级内容包时，可双击 `start-game.cmd` 启动。也可在 PowerShell 中使用 `tools/start-game.ps1`，通过 `-ContentRoot` 和 `-GodotPath` 指定其他内容目录或 Godot 可执行文件；`-CheckOnly` 仅检查启动路径和列出运行时。

用 Godot Mono 打开 `src/San9.Client.Godot/project.godot`，构建 C# 并运行主场景。无参进入开局菜单；直接进入指定剧本和玩家势力：

```powershell
& "<Godot Mono可执行文件>" --path "src/San9.Client.Godot" -- --scenario scenario-4 --player-force 6
```

中键拖动、滚轮缩放，左键选择、右键查看情报；小地图和设施名单可定位，F3 打开调试面板。操作约定见[战略界面](docs/ui/strategy-ui.md)。

### 无界面运行

```powershell
dotnet run --project src/San9.Headless -- verify --fixture --xuns 36 --output local/generated/verification/headless/public
```

玩家读档、精确恢复、JSONL 命令脚本、报告与退出码见[Headless 使用说明](docs/headless.md)。

### 桌面发布

推送 `v*` 标签或手动运行 GitHub Actions 的 **Desktop release**，构建 Windows x64、Linux x64、macOS ARM64 发布包。发布包准备字体并内置内容转换入口，不附带原版素材或转换内容；详见[桌面导出与自动发布](docs/client-content.md#桌面导出与自动发布)。

## 工程结构

| 目录 | 职责 |
| --- | --- |
| `src/San9.Core/` | 确定性领域状态与规则 |
| `src/San9.Application/` | 会话、用例、查询与表现投影 |
| `src/San9.Content/` | 独立内容契约与组合校验 |
| `src/San9.Importer.San9PkTc101/` | 原版识别、解析及转换 |
| `src/San9.Rendering/` | 引擎无关的地图合成、坐标与动画 |
| `src/San9.Client.Godot/` | 输入、UI、声音及绘制 |
| `src/San9.Headless/`、`src/San9.Tools/` | 无界面运行、转换与静态导出 |
| `tests/` | 自动化验证 |
| `local/generated/` | 忽略的转换包、缓存与验证产物 |

Core 与 Application 不依赖 Godot 或原版格式；普通客户端构建不引用 Importer，正式导出的内容准备入口可调用 Importer。分层细节见[架构决策](specs/decisions/DEC-PROJECT-0001-layered-architecture.md)。

## 文档入口

- [文档总览](docs/README.md)：按问题查找使用说明、实现约定、规格与历史记录。
- [协作规则](AGENTS.md)、[工程流程](docs/development/engineering-workflow.md)与[测试说明](tests/README.md)。
- [规格索引](specs/README.md)、[行为规格](specs/behavior/README.md)、[设计决策](specs/decisions/README.md)与[路线图](specs/plans/roadmap.md)。
- [研究入口](research/README.md)、[研究结论](research/findings/README.md)与[来源登记](research/SOURCES.md)：证据与推断不自动成为产品要求。
- [本地私有文件规则](private/README.md)与[本地目录](local/README.md)。
