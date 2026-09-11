# 消息编写与显示约定

本页是重制内容的现行用法。原作控制码只由Importer处理；原始格式调查见[RE-0166](../research/findings/RE-SAN9PK-0166.md)，生成命令见[内容管线](client-content.md#原版文本统一入口)。

## 显示片段

采用[Godot原生BBCode](https://docs.godotengine.org/en/stable/tutorials/ui/bbcode_in_richtextlabel.html)，已在Godot 4.7.2 Mono验证颜色、字号、对齐、图片和换行。新内容的静态显示片段直接写为：

```text
[color=#88F880]说明[/color]
[font_size=24]标题[/font_size]
[img]icons/city[/img]洛阳
```

显式换行使用字符串中的换行符，自动换行由控件宽度及`AutowrapMode`决定。不要发明`[icon]`或`[style]`标签。普通用户文字或参数中的左方括号用`MessageMarkup.Escape`转义，不能先转义再当成富文本子消息执行。

当前行内图片目录为`icons/`，可用标识是`city`、`arrow-up-right`、`arrow-down-right`、`crossed-arrows`、`empty-square`、`question`、`turn-arrow`、`book`、`waves`、`circle`。标识指向转换包资源，禁止绝对磁盘路径。客户端按基础或当前`font_size`选择最近有效原生尺寸，同距优先大图，默认等比缩放到该字号；显式原生`img`尺寸选项由作者负责。包校验拒绝未提供的图片，控件使用Godot原生行内布局。

## 参数与控制

`MessageDefinition.Nodes`是有序节点数组：

| 节点 | 用法 |
| --- | --- |
| `MessageLiteral` | 保存简体与源文的BBCode片段 |
| `MessageValue` | 独立表达式参数；普通结果自动转义，支持字符串/整数/十六进制及宽度、精度 |
| `MessageConditional` | 显式谓词为真时执行Body |
| `MessageChoice` | 求值Index后执行一个候选 |
| `MessageReference` | `Tail=false`返回后继续当前消息；`Tail=true`转移后结束当前消息 |
| `MessageReturn` | 结束当前消息调用 |
| `MessageFormat` | 导入原作的持续样式切换；Channel区分foreground/effect，Open/Close仍是原生BBCode片段 |

新编写内容一般使用自包含的静态BBCode片段，不需要模拟原作持续样式。富文本组合通过明确的`MessageReference`，不能用普通字符串参数注入标签。表达式支持常量、具名绑定、显式查询、随机与算术/比较/逻辑操作；原作算术按32位无符号规则，不能把它当作任意精度公式语言。

例如“城市图标加动态名称”的完整消息定义是：

```csharp
var definition = new MessageDefinition(
    [new MessageLiteral("[img]icons/city[/img]", "[img]icons/city[/img]"),
     new MessageValue(new MessageExpression("binding", "name"))],
    SemanticsConfirmed: true, RequiredBindings: ["name"]);
```

对应的`NameMessages.City`模板已随内容包提供，任意图文控件均可使用。名称含`[b]`时仍作为普通文字显示。

## 消息产生与状态所有权

Application的`MessageEvaluator`接收消息目录及`MessageContext`。Values负责具名值；Query接收查询名和已求值参数，由玩法适配当前世界；RandomIndex接收排他上界，必须返回范围内的索引。Content和Application不加载Godot资源。

消息产生时求值一次并保存返回的BBCode字符串。重新排版、重绘、鼠标移入或控件刷新使用该结果；只有业务上产生一条新消息才重新求值。当前界面模板没有随机节点。

未确认的消息或依赖、循环/过深引用、缺少实际使用的绑定、非法随机值和超限输出均报错。读取`MessageGraph.Requirements`可获得完整依赖确认状态和所需绑定集合，但不能仅据绑定名存在就宣称玩法语义已经验证。未就绪的原作记录完整保留在转换目录，不能通过设置空参数或显示占位符绕过门槛。

## 名称与富文本的区别

`TextCatalog.Resolve`用于纯名称、排序、查询；遇到富文本会报错，禁止自行剥标签后静默丢图标。`ResolveRich`读取静态图文；动态内容使用`MessageEvaluator`。客户端统一调用`GameRichTextLabel.Present`，字体来自`GameFont.tres`，字号、主体颜色由`GameTextStyles`提供。纯文字地图绘制共享同一主题和角色配置。描边和阴影使用[统一文字样式](text-styles.md)的原生实现，不要求与原作像素强一致。

原始偏移、控制码、解码错误、样式号及调用现场证据保存在导入报告，不能成为新玩法的接口。繁简转换只改变文字片段，源文与简体共用控制结构、参数及图片标识。

普通原作 `b` 转成轮廓而非主体色；`m` 改主体色；格式通道独立嵌套。原生局部阴影和带宿主阴影的行内轮廓切换明确报错。帮助／教学正文的额外强调色转换属于专用调用策略，尚未接入，不能与普通消息规则混用。

## 后续词汇校订计划（2026-09-11）

以下为项目所有者确认记录的后续校订目标，本次不修改转换结果，也不作为历史姓名考证结论。原始文本和缺字出处见 [RE-0166](../research/findings/RE-SAN9PK-0166.md#缺字出处核对与不增补字体决定2026-09-11)。

| 对象 | 原文／当前转换结果 | 后续处理 | 状态 |
|---|---|---|---|
| 秦宓的字，人物 292 | 子勑／子𠡠 | 校订显示为「子敕」 | 目标已记录，待接入 |
| 孙𩅦，人物 368 | 孫𩅦／孙𩅦 | 保留现状；以后考虑定制字形，或采用显示替代名「孙湾」 | 方案待决；不构成本次增补字体授权 |
| 刘璝，人物 602 | 劉璝／刘𪻺 | 校订显示为「刘瑰」 | 目标已记录，待接入 |
| 战斗台词用词 | 驃駻／骠𫘣 | 对整个词校订为「剽悍」 | 目标已记录，待接入 |

未来在繁简转换之后接入统一词汇校订层，保留原文对照，不在字体或渲染控件中替换。人物规则绑定稳定人物 ID 和字段，名称结果供列表、详情、动态消息及检索共用；台词规则绑定消息 ID 和预期原词，不能全局按单字替换。PK 台词索引为 7122、7123，基础版为 7032、7033。

每条规则记录原文、转换结果、校订结果、理由与确认状态；原词不匹配时报告，防止资源变更后误改。「孙湾」若日后采用，须明确属于显示替代名，保留原作姓名。当前仍执行四个已知缺字明确接受的决定。
