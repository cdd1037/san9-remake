# 三国志 9 重制

使用 Godot 与 C# 重建《三国志 IX》的策略体验。原作提供研究和设计基线，重制版可以采用现代技术并调整玩法；影响规则、数据语义或玩家预期的重要差异通过行为规格与设计决策记录。

## 当前能力与边界

当前处于工程基线与地图 MVP 阶段，使用 Godot 4.7.2 Mono（.NET 版）和 .NET 10。

- 已接入190年剧本初始地图，支持方格选择、拖动、缩放和四季外观切换。
- 地貌保留四套季节纹理，运行时按块合成；水面、旗帜和守兵播放帧动画，旗帜随预览季节改变方向。
- 已导出三种城池外观、各耐久与开发档位的已确认建筑纹理及阴影；客户端目前显示初始城市状态。
- 地图仍是初始快照浏览器：城市数值变化尚未驱动地图外观，预览季节尚未与模拟日期联动；移动部队及其全局遮挡排序尚未接入。
- 地图包已有方格数据，尚不能据此认为 Core/Application 已具备完整地图领域模型、寻路或地形代价计算。

转换包和原版素材均不随仓库分发。运行地图前，需要在本地完成资源转换。

## 快速开始

在仓库根目录执行。准备 .NET 10 SDK 和 Godot 4.7.2 Mono；仅运行研究侧图像脚本时需要 Python 与 Pillow。

### 构建与测试

```powershell
dotnet build San9Remake.slnx
dotnet test San9Remake.slnx
```

### 转换地图

准备已登记且通过指纹校验的繁中 PK 1.0.1.0 EXE，以及本地 `private/s9数据文件/` 下的源数据：

```powershell
dotnet run --project src/San9.Tools -- import-map --source-exe "<San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --scenario "private/s9数据文件/D_Sce002.S9" --output "local/generated/maps/scenario-190"
```

输出目录必须不存在。重新转换时指定新目录，再通过客户端的 `--map-package` 参数加载。包内 `map.json` 保存方格、绘制描述、建筑组合、动画和资产引用。

### 运行客户端

用 Godot Mono 打开 `src/San9.Client.Godot/project.godot`，构建 C# 后运行主场景。客户端默认读取上述 `scenario-190` 包，也可从命令行指定：

```powershell
& "<Godot Mono可执行文件>" --path "src/San9.Client.Godot" -- --map-package "<地图包绝对路径>"
```

右键拖动、滚轮缩放、左键选择方格，左上角切换季节。转换选项、离线渲染、截图验收及已知限制见 [大地图导入与运行](docs/map-import.md)。

## 工程结构

| 目录 | 职责 |
| --- | --- |
| `src/San9.Core/` | 不依赖 Godot 的确定性领域核心 |
| `src/San9.Application/` | 游戏会话与用例 |
| `src/San9.Content/` | 内容契约、剧本与地图包校验 |
| `src/San9.Importer.San9PkTc101/` | 繁中 PK 样本识别、源数据解析与资源转换 |
| `src/San9.Rendering/` | 引擎无关的地貌合成、地图坐标及动画帧选择 |
| `src/San9.Client.Godot/` | 输入、相机、UI 与地图绘制 |
| `src/San9.Tools/` | `import-map` 转换与 `render-map` 静态导出 |
| `tests/` | 自动化测试 |
| `research/` | 来源、实验、逆向证据及独立验证脚本 |
| `specs/` | 行为规格、设计决策与路线图 |
| `local/generated/` | 被 Git 忽略的转换包、缓存与验证产物 |

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
- [大地图导入与运行](docs/map-import.md)
- [研究区说明](research/README.md)、[研究结论索引](research/findings/README.md)与[来源登记表](research/SOURCES.md)
- [本地私有文件规则](private/README.md)
