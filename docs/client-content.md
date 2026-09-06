# 客户端内容与主界面

本页维护当前能力与使用入口，不累计已解决问题的阶段验收。原版依据见[主界面规格](../specs/behavior/BEH-UI-0001-initial-strategy-screen.md)，地图与运行态见[地图文档](map-import.md)和[运行时架构](runtime-state.md)。

## 职责与文件

| 模块 | 职责 | 输出 |
| --- | --- | --- |
| Content.Maps / MapImporter | 方格、地貌、建筑、云、动画、疆界精灵和小地图底图 | `content.json.map`，地图schema8 |
| Content.Scenarios / Scenarios.ScenarioContentCompiler | 初始日期、势力资源、据点归属/坐标/显示数值、图标、驻留武将初始状态和地域所属城市 | `content.json.scenario`，schema9 |
| Content.Text / Text.ScenarioTextReader | 原作名称索引、类型后缀与简体投影；运行时按键解析 | `content.json.text`，schema1 |
| Content.Interface / Interface.InterfaceSkinCompiler | 框架、三段背景、季节、按钮、状态条、图标和原版显示颜色 | `content.json.skin`，schema12 |
| Content.Packages / Packages.ClientContentImporter | 组合入口、跨包ID/坐标/文本引用校验、来源清单 | 唯一`content.json`，顶层schema12 |
| Content.Images / Importer.Images | 通用PNG、索引图解码和透明边缘处理 | 由使用方选择输出目录 |
| Importer.Containers.Ls11Archive | LS11容器解码，不承担名称或地图语义 | 不直接输出文件 |

地图内容不引用名称、势力摘要或皮肤。剧本和文本可独立于地图校验；组合层检查建筑稳定ID与据点一一对应、坐标一致以及文本键存在。`SourceText`保存繁体显示原文（设施已组合类型后缀）；基础地域名另有文本项。它不是原消息字节的替代归档，原文件以SHA-256追踪。

客户端由`Main`装配各内容和节点。`Application.Presentation.StrategySelection`负责唯一选择状态与玩家设施过滤；`MapWorld`发布拾取事件，接受高亮坐标和相机定位，消费会话视图派生建筑，不自行维护势力或HUD真值。`MapAnnotations`显示标注，`StrategyHud`显示固定界面，`StrategyMinimap`组合地形和据点标记。`Diagnostics`单列F3面板、地图自检和HUD视口事件验收。通用图像加载不再放在TerrainRenderer中。

当前磁盘包只有一个`content.json`，图片位于`images/map/`与`images/interface/`，所有路径相对包根；代码中的四类契约继续分离。旧版包重新转换，不提供兼容路径。初始内容由GameSession复制为独立Core运行态，显示数据由当前世界派生；完整开局结算尚未接入，详见[运行时架构](runtime-state.md)。

## 转换与运行

从仓库根目录执行；已有目录按来源清单原位增量更新。复用未变化资源，同步清单并清理失效文件；必要的全量重建仍优先使用原目录。只有明确需要独立对照时保留副本，不为旧包引入兼容分支：

```powershell
dotnet run --project src/San9.Tools -- import-client --source-exe "<已登记San9PK.exe绝对路径>" --data-dir "private/s9数据文件" --scenario "private/s9数据文件/D_Sce002.S9" --output "local/generated/content/scenario-190-live"
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --content-package "<客户端内容包绝对路径>"
```

客户端默认读取`local/generated/content/scenario-190-live`，只加载转换结果。单独地图仍使用`import-map`；`render-map --content-package local/generated/content/scenario-190-live`也可直接消费组合包内的地图部分。源资源及生成包均被忽略，不提交。

## 原版文本统一入口

转换能力保留在 `San9.Importer.San9PkTc101.Text.OriginalTextConverter`。输入原版字节，输出TextEntry：CP950与已确认扩展码位统一解码为正确SourceText，再用OpenccNetLib 1.6.2的T2S配置生成Text。人物、地名、地区建筑及EXE设施后缀共用入口。消息容器只提取字节，JSON序列化输出UTF-8，客户端不依赖OpenCC。

FA40/41/45直接映射伷/璝/儁，与所处姓名或正文无关。未知扩展与脚本/图标控制明确拒绝。璝按所用词典显示为𪻺；异体字归一化仍待议，见[DEC-UI-0005](../specs/decisions/DEC-UI-0005-officer-name-normalization.md)。没有独立文本项目或按整姓名补救逻辑。

状态所有权、侧栏行为、小地图刷新、文字和验证入口详见[战略界面约定](strategy-ui.md)。

## 当前显示与交互

设施与武将侧栏复用名单和滚动条，支持独立滚动、焦点和镜头定位。小地图分层显示地形、城市标记与视野框，相机变化只刷新视野框，世界变化刷新标记。

「小」打开独立战略全图，「大」返回详细地图；全图支持三个图层、定位、视野框和四列排行榜。详见[全图规格](../specs/behavior/BEH-UI-0003-strategy-overview.md)。

武将一览使用通用CatalogView，提供13页列组、排序、滚动及原生64×80中档头像。正文在24像素行内垂直居中，各列保留声明的水平对齐。武将详情使用240×240头像、独立18行名单、四维图、状态/兵法页及列传。字段与其他情报页面范围见[情报界面](information-browser.md)。

项目字体为`assets/LXGWWenKaiMonoGBLite-Medium.ttf`。GameTextPresenter统一Label与Canvas文字，按角色采用八邻域各0.25覆盖度描边或(1,1)单次阴影；源资源保留调色板Alpha。仍应评估更简单的现代文字效果，见[DEC-UI-0002](../specs/decisions/DEC-UI-0002-modern-text-effects.md)。

F3提供四季预览及单人商业、开垦入口，内政修改当前会话世界。完整旬结算、部队出征与战斗生命周期尚未接入。全图三个图层按钮的独立文字效果暂缓；目录标题及关闭按钮文字效果仍待核对。

## 验证与本地产物

C#改动按影响范围运行`dotnet test San9Remake.slnx`。当前实际画面由用户验收，不自动启动Godot；重复实现转换规则的Python界面交叉检查器已移除。

当前运行包为`local/generated/content/scenario-190-live`。研究资料位于`local/generated/research`，仍有用途的运行态对照、字体研究和检查工具位于`local/generated/verification`。局部问题解决后清理对应临时截图与日志，不保留多轮重复验收记录。
