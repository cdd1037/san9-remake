# Godot鼠标限制方案复核

- 日期：2026-09-27；作者：Codex。
- 范围：F9锁定实际4:3画布、保留任意窗口比例、排除黑边与标题栏。此次仅研究，不修改运行代码。
- 证据：本地GodotSharp 4.7.2公开API/XML；官方文档；上游源码固定提交 `6210a2fd88ed3f512b3093d0be74e2541f463b84`。上游源码不冒充本机引擎可执行文件的精确构建；本轮未运行鼠标实验。

## 已确认能力

1. `Input.MouseMode = Confined`保持系统光标可见并限制到窗口；公开API没有可指定矩形的鼠标限制参数。本地绑定与[Input文档](https://docs.godotengine.org/en/stable/classes/class_input.html)、[DisplayServer文档](https://docs.godotengine.org/en/stable/classes/class_displayserver.html)一致。
2. [Windows实现](https://github.com/godotengine/godot/blob/6210a2fd88ed3f512b3093d0be74e2541f463b84/platform/windows/display_server_windows.cpp)的`_set_mouse_mode_impl`用GetClientRect、ClientToScreen及ClipCursor限制客户区，不知道其中4:3画布的黑边。故原Confined方案的黑边现象符合API语义，并非换算一个参数就能解决。
3. [Viewport.WarpMouse](https://docs.godotengine.org/en/stable/classes/class_viewport.html#class-viewport-method-warp-mouse)可以重定位光标。对应[实现](https://github.com/godotengine/godot/blob/6210a2fd88ed3f512b3093d0be74e2541f463b84/scene/main/viewport.cpp)使用视口到窗口的变换，适合在逻辑画布坐标处理重定位。它不是操作系统的持续矩形限制。
4. `Captured`隐藏系统光标并将它锁在窗口中心，移动用相对位移读取。[ScreenRelative](https://docs.godotengine.org/en/stable/classes/class_inputeventmousemotion.html)不受内容缩放影响，可由客户端明确换算到逻辑单位；自绘光标与GUI位置需要由应用统一管理。

## 对前次实现的纠正

调用user32本身不是错误，Godot的Windows后端也会调用；问题是应用新增了独立的限制状态和每帧原生调用，并把功能变成Windows专用。它是平台适配选择，不能称作普通跨平台Godot接入。

顶部“跳到标题栏”的根因尚未查明：上游Confined实现已经排除了标题栏，不能把该现象直接归因于窗口原点用错。需区分实际系统鼠标越界、MouseExited清空了本地导航状态、焦点／鼠标模式变化，以及画布坐标越界。之前坐标测试通过只能证明矩形计算，不能证明顶部原生行为被修复。

## 方案比较

| 方案 | 满足程度 | 代价与限制 |
| --- | --- | --- |
| Confined＋Viewport.WarpMouse | 窗口由引擎限制，进入黑边后重定位回内容边缘 | 可保留系统光标和现有GUI，接入较小；是事后纠正，快速移动或卡顿时可能短暂进入黑边，不能承诺严格不越界 |
| Captured＋虚拟光标 | 系统指针停在中心，逻辑光标始终裁剪到画布，适合严格的内容区限制 | 需要统一鼠标事件与位置查询，软件光标有渲染延迟；原生弹出窗口需明确释放／恢复策略 |
| 平台原生矩形限制 | 保留硬件光标并严格限制实际内容区 | Windows可用ClipCursor，其他桌面后端需单独设计；必须有明确平台边界和生命周期，而非在地图循环中零散调用 |
| 强制窗口4:3 | Confined限制范围与内容一致 | 改变用户的窗口行为，且非4:3全屏仍有黑边，不符合当前任意窗口比例目标 |

## 建议

若坚持“实际画布不越界、任意窗口比例、不在产品代码直调平台API”三个目标，优先验证Captured＋虚拟光标。不能声称只替换MouseMode即可完成：地图导航、悬停、按钮、小地图、滚动条、滚轮缩放和弹窗必须读取同一逻辑指针。

建议结构：客户端窗口输入控制器拥有F9、捕获状态和逻辑指针；游戏UI层显示唯一的软件光标；原始相对运动只用于更新该位置，经过标准化的事件再进入游戏视口的Godot GUI分发，禁止原始中心事件与转换事件重复投递。现有直接读取窗口鼠标位置的消费者需纳入这个边界。实际采用哪种视口接线方式必须经小原型验证，不先大改整个UI。

F8通知是普通Control模态，不必放开捕获；原生PopupMenu/Window、失焦及退出需单独处理。解除F9时使用引擎重定位让系统光标落到虚拟光标位置，避免突然跳到中心；失焦只释放，不主动把指针搬回游戏。

若软件光标的延迟或GUI接入代价不可接受，应明确改选平台适配方案，或接受Confined＋WarpMouse的软边界；不可把软边界包装成严格锁定。

## 下一步验证边界

- 独立窗口、4:3／宽屏／上下黑边、不同缩放和DPI，边缘坐标与实际绘制一致。
- 低帧率和快速甩动；顶部异常同时记录焦点、引擎MouseMode、原始坐标、逻辑坐标和退出事件。
- 关闭消息框、鼠标拖动滚动条、小地图点击、右键原生菜单、中键平移、滚轮缩放无重复／错位事件。
- F9解锁、Alt+Tab、最小化、恢复、退出；相对位移和光标位置不积累欠账。

研究阶段未修改Win32实现，也未把候选方案写成已通过的产品规格。后续用户决定先恢复Godot的Confined整窗口限制；已撤除本次新增的原生矩形锁定及其专用测试，保留上述研究供后续决策。当前不实施虚拟光标或软边界纠正。
