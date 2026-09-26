# 统一文字样式

界面通过 `GameTextStyles` 选择用途和状态，通过 `GameTextPresenter` 或 `GameRichTextLabel.Present` 应用。字体统一来自 `assets/GameFont.tres`。不在各个界面重新实现描边、不从原作调色板号直接构造业务样式。消息编写见[消息约定](messages.md)。

## 现行绘制

原作是颜色、信息层次和状态组合的依据。重制使用现代字体及 Godot 原生效果：描边宽度 1 个逻辑像素、效果色 alpha 乘 0.65；阴影偏移 `(1,1)`、保留效果色覆盖度；无效果角色关闭两者。

- `Label`：缓存共享的不可变 `LabelSettings`，使用 `OutlineSize/OutlineColor` 和 `ShadowOffset/ShadowColor`。
- `RichTextLabel`：使用对应原生主题属性。图标由原生 `[img]` 参与排版，不对图标复制文字效果。
- 地图：`DrawStringOutline` 加主体绘制，或一次偏移阴影加主体绘制。

Godot 的堆叠轮廓、堆叠阴影不需要用于这批已确认的单一效果组合；原作同时打开两个标志时，实际阴影优先，并不是双层效果。当前不使用八邻域文字副本，不引入 TextServer 排版。原生轮廓的粗细与原作位图不同属于明确的重制选择。

## 复用目录

| 入口 | 用途及组合 | 依据 |
|---|---|---|
| `For(Header)` | 24，白字，无效果 | RE-0158 顶栏 |
| `For(Resource)` | 16，白字，无效果 | RE-0158 资源 |
| `For(Building)` / `Building(highlighted)` | 16，白／黄色主体，黑描边 | RE-0158、RE-0163 |
| `Message(enabled)` | 16，白／红主体，黑阴影 | RE-0158 消息栏 |
| `For(Region)` | 16，淡黄主体，深绿描边 | RE-0158 地域栏 |
| `For(Spring)` | 16，白字，暗红描边 | RE-0158 季节 |
| `For(Compass)` | 16，棕色主体，黑描边 | RE-0158 方位 |
| `For(Roster)` | 16，无效果；可用性主体色来自内容包 | RE-0158 名单 |
| `Button(role, hovered, selected, disabled)` | 九种主界面按钮，完整八状态 | RE-0158 实际前景／描边表 |
| `InformationMenu(...)` | 情报菜单及地图一、二级命令菜单共用：白／黄／灰／黑主体，黑／绿／白描边，无阴影 | RE-0161、RE-0073 |
| `CatalogHeader(...)` / `CatalogTab(...)` | 列头和页签状态 | RE-0162；统一保留现有配置 |
| `HelpTopic(..., current)` | 当前／非当前主题，完整八状态 | RE-0167 的帮助、教学页面成对状态表 |
| `CatalogRow` / `For(DetailBody)` | 16，白字黑描边 | 当前详情页统一基线；未逐项认定为所有原作详情页事实 |
| `For(HelpBody)` | 16，白字黑描边 | 重制正文基线；源帮助正文还需要下述特殊转换策略 |
| `For(DialogTitle)` | 18，纯白无效果 | 重制已有弹窗选择，不冒充原作字号 |

布局所需字号可以 `with { FontSize = size }` 调整。新业务状态应增加有名称的入口，不能在界面里分散复制前景／效果色条件。重制默认和原作已确认配置分别注明依据。

## 消息内样式

`MessageFormat.Channel` 分别保存 `foreground` 和 `effect`；两个通道均使用原生 BBCode，切换时统一闭合并重新嵌套。状态跟随实际执行的分支与子消息，不跟随导入器的字节扫描状态。

普通原作 `b` 控制转换为 `[outline_size=1][outline_color=#RRGGBBA6]`，不改变主体色；`m` 是主体色；恢复基础状态同时显式使用 `[outline_size=0]`，避免错误继承宿主描边。源文和简体共用同一格式节点。

原生富文本没有局部阴影标签。遇到原作 `s` 转换时，记录精确偏移、完整消费字节，但消息不得执行。若宿主使用整段阴影，消息又显式切换轮廓，客户端报出不支持的组合；不会把两者偷偷叠加。帮助／教学原作另有把 `b/s` 转成强调主体色的调用链，不能套用到普通消息，目前该专用正文转换尚未接入。

## 覆盖与复核入口

已对共享设置／布局函数的全程序直接引用建立清单：883 个调用点、209 个函数、853 条反编译观察；归并得到 47 种实际引用的单通道状态表。它们不等于 47 种已确认完整组合。另有 102 条动态／上下文观察和 47 个未匹配调用点，均保留定位，不能据此宣称全游戏样式已经闭合。研究见 [RE-0167](../../research/findings/RE-SAN9PK-0167.md)。

在对应 IDA 主库执行 `research/scripts/ida/export_text_style_usage.py`，再运行：

```powershell
python research/scripts/summarize_text_style_usage.py local/generated/research/text-styles/usage.json
```

得到 `catalog.json` 与 `catalog.md`，包括去重状态表、每个实际调用的证据及未闭合项。生成物只写入 `local/generated/`。以后优先查该目录和研究记录，未闭合的对象生命周期仍需要研究，不能直接交叉组合调色板值。

历史验收入口 `--text-smoke` 会生成 12／16／24 字号、99 个角色／状态样本的三入口比较页 `local/generated/verification/text/styles-*.png`，并检查普通标签与富文本效果参数一致。后续开发默认不运行，只有当前任务明确要求复现文字场景时单独执行，见[Godot历史画面验收](client-verification.md)。实际截图还须检查可读性、图标、裁切；自检通过不代替画面检查。

字体覆盖取舍：按 2026-09-11 用户决定不增补字体，撤除 BabelStone 回退，明确接受四个已知缺字。缺字仍保留在报告，其他新增缺字不自动豁免；见[字体说明](../../src/San9.Client.Godot/assets/README.md)。
