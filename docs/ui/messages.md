# 消息编写与显示约定

本页是重制内容的现行用法。原作控制码只由Importer处理；原始格式调查见[RE-0166](../../research/findings/RE-SAN9PK-0166.md)，生成命令见[内容管线](../client-content.md#原版文本统一入口)。

按命令核对调用顺序、说话人物和音效的实例见[巡察消息与音效流程](patrol-presentation.md)。该索引按原编号串联调用角色与证据；接入范围见该页。

静态文本目录`text.json`的`entries`直接使用字符串值，例如`"city.0": "洛阳"`，不保存`sourceText`或单独的`text`字段；名称、说明和提示仍按原有语义键查询。

## 消息分类与命名

消息身份、文件分组和调用角色分别维护。剧本包schema144的`content.json.messages`列出消息文件的相对路径，加载后合并为统一的`TextCatalog.Messages`；允许跨文件引用，重复键直接报错。

TC101原版消息保留`messages.<原始0基序号>`，列传保留`biographies.<原始0基序号>`。不再全量改成语义名称，也不重新编排组内序号或添加`variant`后缀。原编号只用于内容记录身份与逆向定位，不是领域对象ID，不限制对象数量，也不代表人格、播放顺序或随机规则。客户端按内容引用使用这些不透明键，不解析编号。

仅将已有明确边界的区段独立成文件，文件内仍保持原键及源序号顺序：

| 文件（均位于`messages/`） | 原档案与区段（含两端） |
| --- | --- |
| `messages.json` | `M_MsgPK.s9`中下列区段以外的消息，包含仍在调查的对话、战斗、报告等 |
| `help.json` | 消息8577–9323，帮助正文 |
| `tutorial.json` | 消息9422–9904、10122–10139，教学正文 |
| `names.json` | 消息11455–12612，姓名生成片段；不是全部人物／地点名称 |
| `campaign.json` | 消息72–135、12923–14208，挑战剧本标题、正文与结果 |
| `biographies.json` | `M_RtdnPK.s9`的全部679条列传 |
| `presentation.json` | 两条重制名称显示模板，保留简短语义键 |

`PublicationMessageCatalog`仅维护TC101完整记录覆盖、源顺序与文件分组，不改写消息键及内部引用。导入器向通用写入器传入分组；分组不要求消息键使用相同前缀。独立创作可以自行指定分组；未指定时按键第一段归档，无分段键归入`general`。组名使用以小写字母开头的小写英文、数字和连字符，不允许路径片段。加载器不依赖TC101编号或固定分组。

14,888条档案记录与2条名称模板全部保留；重复正文、空记录、姓名池空白、分支及顺序不改变。完整覆盖和引用校验仍在发布前执行。分类本身不增加调用现场证据；只有`random-choice`节点决定随机语义。

业务用途放在`presentation.json`的角色引用中，例如巡察的`normal`、`result`；地图滚动提示由`notices`的`scroll-hover`、`scroll-click`引用消息8427、8428。用途说明与逆向证据放在文档，不继续堆叠进消息ID。

旧包须重跑`import-client`，无需重转纹理，不保留旧语义键别名。成功写入后清理旧清单引用但新清单不再使用的消息文件；未列在旧清单中的用户文件不删除。

消息键直接提供档案类别与0基序号，不再生成独立编号映射文件；重转成功后清理旧`message-id-map.json`。来源档案及哈希由`content.json.sources`保存。可选消息报告保留`key`和`sourceKey`（原版消息两者相同，重制模板后者为空），`import-text`继续保留原始诊断与字节。

## 显示片段

消息使用有限预设，显示边界再转换为Godot原生BBCode。自由颜色、字号等标准BBCode创作能力留待后续Mod扩展；当前消息不保存任意颜色通道或开闭标签指令。静态显示片段写为：

```text
[preset=green]说明[/preset]
[img]icons/city[/img]洛阳
```

显式换行使用字符串中的换行符，自动换行由控件宽度及`AutowrapMode`决定。`[preset=名称]…[/preset]`必须在同一静态片段内闭合；未知预设和未闭合范围在内容校验时拒绝。普通用户文字或参数中的左方括号用`MessageMarkup.Escape`转义，不能先转义再当成富文本子消息执行。

当前行内图片目录为`icons/`，可用标识是`city`、`arrow-up-right`、`arrow-down-right`、`crossed-arrows`、`empty-square`、`question`、`turn-arrow`、`book`、`waves`、`circle`。标识指向转换包资源，禁止绝对磁盘路径。客户端按基础或当前`font_size`选择最近有效原生尺寸，同距优先大图，默认等比缩放到该字号；显式原生`img`尺寸选项由作者负责。包校验拒绝未提供的图片，控件使用Godot原生行内布局。

## 参数与控制

`messages/*.json`的每个消息键直接对应有序节点数组，不再包装`nodes`属性，例如：

```json
{
  "greeting": [
    "欢迎，",
    { "kind": "value", "expression": { "operation": "binding", "binding": "name" } },
    "。"
  ],
  "empty": []
}
```

值节点只包含表达式和可选数字格式，不保存`prefix`／`suffix`；前后文字由相邻的普通文本节点表达。内存模型仍通过`MessageDefinition.Nodes`持有节点序列。

节点类型：

| 节点 | 用法 |
| --- | --- |
| `MessageLiteral` | JSON中直接使用字符串，保存正文、有限预设及图标标记 |
| `MessageValue` | 独立表达式参数；普通结果自动转义，支持字符串/整数/十六进制及宽度、精度 |
| `MessageConditional` | 显式谓词为真时执行Body |
| `MessageChoice` | 求值Index后执行一个候选 |
| `MessageRandomChoice` | 均匀随机选择一个候选；`random-choice`只保存`choices`，随机范围由候选数量推导 |
| `MessageReference` | `Tail=false`返回后继续当前消息；`Tail=true`转移后结束当前消息 |
| `MessageReturn` | 提前结束当前消息调用；自然结束处无需此节点 |
| `MessageStyled` | `kind=styled`，保存`preset`和`body`，作用域结束或提前返回后恢复外层样式 |

纯静态预设范围合并成普通字符串，例如`"好！\n这下子，就能阻止[preset=blue-outline]刘备[/preset]的援军了！"`；相邻静态片段一并合并。只有范围内含参数、分支或引用时保留`MessageStyled`。新内容不模拟原作持续样式。富文本组合通过明确的`MessageReference`，不能用普通字符串参数注入标签。表达式支持常量、具名绑定、显式查询、随机与算术/比较/逻辑操作；原作算术按32位无符号规则，不能把它当作任意精度公式语言。

例如“城市图标加动态名称”的完整消息定义是：

```csharp
var definition = new MessageDefinition(
    [new MessageLiteral("[img]icons/city[/img]", "[img]icons/city[/img]"),
     new MessageValue(new MessageExpression("binding", "name"))],
    SemanticsConfirmed: true, RequiredBindings: ["name"]);
```

对应的`NameMessages.City`模板已随内容包提供，任意图文控件均可使用。名称含`[b]`时仍作为普通文字显示。

## 消息产生与状态所有权

Application的`MessageEvaluator`接收消息目录及`MessageContext`。Values负责具名值；底层Query允许独立表达式宿主，自定义查询的正确性由宿主负责；游戏内生产使用`GameSession.Messages(texts)`返回的`WorldMessageContext`，查询读取该次捕获的世界快照；RandomIndex接收排他上界，必须返回范围内的索引。Content和Application不加载Godot资源。

消息产生时求值一次并保存带预设标记的显示字符串。重新排版、重绘、鼠标移入或控件刷新使用该结果；只有业务上产生一条新消息才重新求值。当前界面模板没有随机节点。

人物对白和无头像结果通过`GamePagedText`按实际排版行共用消息内分页；`CommandNarration`先翻完当前消息，再推进业务消息。页内换行、跨页富文本、音效和稳定外框约定见[叙述消息内分页](../../specs/behavior/BEH-UI-0006-unified-messages.md#叙述消息内分页)。

未确认的原文或依赖在发布前拒绝；运行时遇到循环/过深引用、缺少实际使用的绑定、非法随机值和超限输出均报错。`MessageGraph.Requirements`返回引用有效性及所需绑定集合，不代表调用现场的玩法语义已验证。未就绪的原作记录只保留在研究导出中，不能通过空参数或显示占位符绕过发布校验。

## 名称与富文本的区别

`TextCatalog.Resolve`用于纯名称、排序、查询；遇到富文本会报错，禁止自行剥标签后静默丢图标。`ResolveRich`读取静态图文；动态内容使用`MessageEvaluator`。客户端统一调用`GameRichTextLabel.Present`，字体来自`GameFont.tres`，字号、主体颜色由`GameTextStyles`提供。纯文字地图绘制共享同一主题和角色配置。描边和阴影使用[统一文字样式](text-styles.md)的原生实现，不要求与原作像素强一致。

发布转换按控制流移除消息自然结束处的`return`、确定返回或尾部转移之后的不可达节点，以及所有候选均结束消息时的不可达后续节点。分支后仍有可执行正文时，分支内的提前返回必须保留；参数／查询求值、随机抽取、追加调用与尾部转移的行为不变。

原始偏移、控制码、解码错误、样式号及调用现场证据保存在导入报告，不能成为新玩法的接口。繁简转换只改变文字片段，运行时只发布转换后的正文；原始字节保留在研究导出中。

原作实际使用的蓝、绿、桃、淡黄描边转换为`blue-outline`、`green-outline`、`peach-outline`、`yellow-outline`。帮助／教学通过Importer的`OriginalMessagePresentation.Help`明确选择同色系主体强调预设`blue`、`green`、`peach`、`yellow`，蓝／绿按RE-0167重映射；不按消息编号猜测用途，也未因此新增帮助窗口。容貌帮助使用`yellow`。原作复位结束当前预设范围，恢复所在控件的正文基线；这属于重制选择。未登记组合保留诊断并拒绝发布。样式传播只在导入器中解析，涉及样式的子调用按执行路径展开，运行时的引用不改变调用方后续样式。完整预设及宿主效果组合见[统一文字样式](text-styles.md)。

对象显示参数的样式也由Importer转换为`MessageStyled`，不是仅处理正文中的显式颜色指令：人名、姓、名、字及君主／军团长姓名使用蓝色描边；地点、地点所属势力、势力名、国号、爵位、宝物名及兵法名使用绿色描边。帮助用途沿既有Help策略转换。名称查询仍返回纯文本，参数仍转义；不在控件按“洛阳”等文字匹配上色，也不改变用于条件判断的数值属性。范围结束恢复外层样式。源属性范围及复核见[RE-0168](../../research/findings/RE-SAN9PK-0168.md#动态名称描边补齐2026-09-28)。

复合名称通过已有条件、选择和样式节点组合：官职带兵上限大于10000时显示绿色官职、普通分隔符`‧`、蓝色姓名，否则只显示蓝色姓名；驻防部队显示绿色地点，野战部队显示蓝色主将姓名及普通后缀“队”。`person.office-command-capacity`、`unit.is-embedded`和`unit.commander-name`读取当前世界与内容定义，重绘不改变状态。纯`person.office-name`与`unit.location-name`仍各自只返回官职和服务地点，不把复合富文本塞进普通字符串查询。

## 文字与翻译

原文解码、繁简转换、姓名与正文词汇校订统一以[文字与翻译规范](../../specs/text-and-translation.md)为准；本页仅维护消息编写和求值用法，不另存姓名替换表或未来校订计划。

## 领域消息与事件参数

`MessageBinding.Reference(MessageEntityKind.Person, id)`等引用区分人物、势力、据点、部队、宝物、兵法、地域和军团；编号不受原版槽位或倒序常量限制。`property`表达式明确读取`person.name`、`person.surname`、`person.given-name`、`person.courtesy-name`、`person.war`、`settlement.name`等属性。引用本身不能直接打印或参加数值运算。姓、名和肖像比较身份由`TextCatalog.MessagePeople`提供，姓名全称仍复用人物定义。

```csharp
var messages = session.Messages(texts); // 固定本次世界快照
var result = messages.Produce("messages.7235", new Dictionary<string, MessageBinding>
{
    ["person.0"] = MessageBinding.Reference(MessageEntityKind.Person, speakerId),
    ["person.1"] = MessageBinding.Reference(MessageEntityKind.Person, recipientId),
    ["building.0"] = MessageBinding.Reference(MessageEntityKind.Settlement, destinationId),
    ["return-kind.0"] = MessageBinding.Return(MessageReturnKind.Automatic)
}, presentationRandom.Next);
// 保存 result，之后显示 result.BbCode；不要在绘制回调里再次 Produce。
```

上例仅展示调用契约，不代表已连接相应业务事件。`Personnel`、`Automatic`、`Ordered`分别表示人事归队、自动撤退和主动返回；由事件生产者在任务／单位被移除前明确提供。产生消息所用快照应仍能解析所引用对象。无部队引用的返回台词只需要返回原因，不依赖一个已经销毁的旧部队。表现随机源必须与模拟器随机流独立。

常用查询包括亲子、同父母、血统、长幼、亲密度距离、同根势力、势力关系、持有宝物／类别、日期，以及`event.occurred`具名历史。它们只读取捕获状态；未记录的双边关系沿用Core的0基值，同势力关系为100。运行时消息统一使用发布正文和当前内容名称，不提供原文切换。导入器把原版列传身份条件转换为当前剧本人物引用，把历史特殊称谓写成显式条件；新内容直接引用人物，无需认识列传编号。

运行时`MessageDefinition`只保存`Nodes`，不保存`semanticsConfirmed`、`requiredBindings`或`unavailableReasons`。参数需求由节点及引用依赖推导；未确认原文、无法转换的表达式、缺失或循环引用在发布转换阶段直接报错，中止发布。严格研究导出仍保留原文诊断；就绪报告的`SourceConfirmed`对经过重制改写的记录保持false，不冒充原文已确认。`import-client`默认不生成两个消息报告；指定`--message-reports true`才输出`message-readiness.json`与`message-adaptations.json`。关闭报告时不构造就绪报告或汇总参数清单，成功转换会清理这两个旧文件，发布校验独立执行。`message-readiness.json`只报告成功发布的消息，`RequiredArguments`列出所有分支可能使用的参数；具体产生时缺少实际访问的参数即报错。剧本包schema144要求重新运行`import-client`，无需重转纹理。

### 容貌帮助与异常尾部的发布策略（2026-09-27）

项目所有者提供原作用语辞典截图并确认：PK8677发布为“男性容貌有70种，女性容貌有30种。”，数量暂时写死，仅70和30使用`#F8F8B0`前景色强调。发布正文使用简体“种”，源文“種”保留在研究证据中。该决定不把`ESC H`普遍解释为颜色指令；转换前核对已审阅原文的完整指纹，遇到不同内容拒绝套用。

PK12629／12630／12631／12712，只移除正常结束分隔符之后没有目标编号的`01 J`两字节尾部。必须匹配指定记录及确切尾部结构，截取后的全部正文仍须通过严格解析；保留性别、头像、随机台词和引用分支，不在第一个分支结束处截断。

`MessageContentPolicy`仅用于游戏组合包发布。`import-text`仍输出原始异常及完整原字节，`message-adaptations.json`单独记录每次内容改写，`message-readiness.json`区分原文确认和发布就绪；未登记的新异常继续拒绝。运行时仅导入`M_MsgPK.s9`和`M_RtdnPK.s9`；基础版仅供`import-text`研究对照，不发布或改写。当前14,888条PK档案消息及2条名称模板均就绪；具体业务调用和画面验收的边界不变。
