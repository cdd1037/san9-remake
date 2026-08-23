# RE-SAN9PK-0062：窗口模式固定画布呈现边界与可缩放注入原型

- 状态：已确认核心边界；体验项待回归
- 日期：2026-08-23
- 分析者：Codex
- 结论类型：IDA 静态事实、原型实验与玩家运行时回归
- 置信度：高（固定画布、呈现边界、主输入链与地图边缘滚动）；待验证项单列
- 关联来源：`SRC-SAN9PK-TC-101`
- 关联工具：本地独立子仓库 `tools/san9-toolkit`
- 关联规格：无

## 调查问题

确认原作窗口模式的尺寸约束，并验证能否在不改变内部 1024×768 UI、地图和输入语义的前提下，通过进程内注入把最终画面等比缩放到可变客户区。

## 已确认事实

### 固定逻辑画布与标题

- `0x432C52` 调用图形配置入口时传入 `1024、768、16`。
- `0x432DF1` 把主绘图矩形设为 `(0, 0, 1024, 768)`，随后创建同尺寸的 16 位 DIB 后缓冲。
- `0x5C3640` 使用主绘图矩形创建 DIB section，不读取当前窗口客户区尺寸。
- `0x604D64` 的字节串按 Big5 解码为 `三國志ⅨPK`。注入层以该 Unicode 文本调用 `SetWindowTextW`，避免当前机器 ANSI 代码页造成乱码。

### 最终呈现边界

- `present_full_game_frame_gdi (0x5C3540)` 从共享 DIB DC 向主窗口 DC 提交完整 1024×768 帧。
- `present_dirty_game_rect_gdi (0x5C3B60)` 从同一后缓冲提交脏矩形。
- 两条路径都在 `BitBlt` 后立即调用 `ReleaseDC`，中间没有分支；安全的替代呈现时点是目标窗口 DC 释放之后。
- 后缓冲为 1024×768、16 位、`BI_RGB` DIB。按 Windows 定义，其像素布局为 X1R5G5B5。
- 主模块通过 IAT 导入 `BitBlt` 和 `ReleaseDC`，因此可以在不复制或改写原作绘制代码的情况下截获最终提交。

### 主输入链与地图边缘滚动

- `pump_window_messages (0x5CADA0)` 从 `PeekMessageA` 取消息，经 `normalize_window_message (0x5CC0B0)` 规范化，再由 `dispatch_window_message (0x5CAAF0)` 进入内部路由。
- 内部 handler 可以绕过 Win32 子类窗口过程，因此硬件鼠标坐标和工具快捷键必须在规范化入口处理。
- `CSan9MapView_on_mouse_move (0x534000)` 经 `0x5208C0`、`0x51E400` 调用 `select_map_edge_scroll_cursor (0x51E2A0)`；末级函数以视图四边 32 像素范围判定八方向滚动。
- `movie_mouse_getmessage_hook (0x5C9560)` 只服务影片窗口，不是主游戏输入入口。
- 主模块对 `GetCursorPos` 的两处调用都位于 `get_game_cursor_position (0x433310)`。窗口分支减去的是原无边框窗口矩形 `left/top`，新增标题栏后不能把它当作客户区原点；主动 hover 轮询仍需独立处理该入口。
- `dword_1232718 == 1` 同时控制全屏分支和显示模式参数，不是 hover 状态。
- 硬件鼠标消息可由 `MSG.pt` 与 `lParam` 转为屏幕坐标后的一致性识别；原作内部投递的逻辑消息不得再次缩放。

### 地图视图移动入口

- `scroll_map_view_direction (0x51D930)` 是原作八方向边缘卷动入口。它按方向生成横纵
  `±160` 像素的目标中心，处理等距地图边界后调用 `center_map_view_at_isometric_point
  (0x519B60)`。
- `center_map_view_at_isometric_point` 接收等距地图像素中心，先把越界中心夹取到有效地图
  方格，再构造以该点为中心的 1024×768 视图矩形，经 `0x5B8BB0` 写入地图视图
  `+0x38..+0x44` 并触发虚表刷新。其后两个参数分别控制保存最终中心和刷新光标下地图状态。
- 因而中键拖动不应伪造左键或反复触发边缘卷动；可以直接以“当前视图中心减去本次逻辑
  鼠标位移”调用该中心移动入口，同时复用原作的地图边界和刷新行为。

### 地图卷动模式与快捷键

- 全局设置 `dword_1232744` 的 `0x1000` 位经取反写入地图视图 `+0x1A70`。
- `+0x1A70 == 0` 时，普通鼠标移动可触发边缘卷动；`+0x1A70 == 1` 时，边缘卷动要求 `MK_LBUTTON`。
- 玩家重复操作确认 F8 切换原作地图卷动模式，F10 为原作暂停键；注入工具不得占用二者。
- F8 消息到设置位或视图字段的完整写入链尚未闭合，因此两种提示文本与 `+0x1A70` 取值的对应关系仍是高置信度推断。

## 可缩放注入原型

独立工具仓库 `tools/san9-toolkit/` 实现版本限定的启动器与运行时 DLL：

- 启动器校验目标 EXE SHA-256，未指定路径时选择同目录 `San9PK.exe`。
- 构建脚本把 DLL 及其长度、SHA-256 附加到启动器；启动器校验载荷后释放到临时缓存。
- 启动器挂起目标进程，在恢复主线程前设置 `SYSTEM_AWARE` DPI 感知并注入 DLL；关键步骤失败时不继续启动。
- Runtime 内部选项 `scaleInitialWindowForSystemDpi` 默认开启；初始客户区按窗口取得的
  system DPI 计算为 `1024*dpi/96 × 768*dpi/96`，外框使用同一 DPI。该选项暂不暴露到
  INI 或 GUI；关闭时客户区仍为物理 1024×768，但外框继续按实际窗口 DPI 计算。
- DLL 以首次来自已确认 1024×768×16 位 DIB 的提交选定目标窗口，再把它改为可缩放窗口，避免启动阶段误选同类临时窗口。
- 客户区保持 4:3；非 4:3 状态使用居中黑边。鼠标硬件坐标在主规范化入口反变换为逻辑坐标。
- F12 在主规范化入口切换鼠标视口锁定，并吞掉自身按键重复及对应按键消息。状态提示由不可激活、鼠标穿透的独立透明覆盖窗口显示，不修改原作后缓冲。
- 曾实验以独立状态机实现中键拖动：第一次进入 `CMainmapView` 的 `WM_MOUSEMOVE` handler
  后绑定地图对象，再按逻辑坐标增量调用 `center_map_view_at_isometric_point`。该实验存在
  偶发中断且原因未闭合，已从 Toolkit 产品代码移除；滚轮缩放也未实现。
- 原版 EXE 不被写入，退出目标进程即撤销运行时修改。

### D3D11 呈现模型

原作继续完成全部 GDI 自绘。注入层在最终提交边界保存后缓冲，在对应 `ReleaseDC` 完成后上传并呈现：

1. `BitBlt` 代理验证 `BITMAP` 的尺寸、位深、stride 和 `bmBits`，登记最新完整帧并抑制原可见提交。
2. `ReleaseDC` 代理先释放原作窗口 DC，再把 X1R5G5B5 数据复制到 `DXGI_FORMAT_B5G5R5A1_UNORM` 动态纹理。
3. D3D11 shader 翻转倒置 DIB 的 Y 坐标，在 4:3 viewport 中绘制；其余 render target 清黑，像素输出强制 `Alpha=1`。
4. 尺寸变化时先 `ClearState` 解除 immediate context 的资源绑定，再释放 view、`Flush` 并执行 `ResizeBuffers`。
5. 缩放使用单 Pass Catmull–Rom 4×4 采样；1:1 视口保持源、目标像素中心对应。

实现要求 feature level 10.0 及以上，着色器使用 `vs_4_0`、`ps_4_0`。初始化同时要求目标格式支持 `TEXTURE2D` 与 `SHADER_SAMPLE`，不提供未经验证的格式回退。原作整帧和局部提交都会触发完整纹理上传，以保持既有提交语义。

## 验证结果

- Release/Win32 在 `/W4 /WX` 下构建为 0 警告、0 错误；两种 shader 均由运行时同版 `d3dcompiler_47` 离线编译成功。
- DPI 初始尺寸配置加入后，单文件与 loose Release/Win32 均在 `/W4 /WX` 下构建通过；
  Runtime 导入表回读确认 `GetDpiForWindow` 与 `AdjustWindowRectExForDpi` 均已进入产物。
  96/144 DPI 的目标尺寸为代码验收值，仍待目标窗口运行复测。
- 中键拖动首版已通过 Release/Win32 `/W4 /WX` 构建，但玩家实测无效果。该版本曾把
  规范化消息的路由目标与 `CMainmapView + 0x04` 读取的窗口句柄视为同一对象，并以此作为
  手势激活条件；两者相等没有逆向证据，因此这是当前失败原因的推断，不是已确认结论。
  修正版删除该句柄读取、相等判断和 handler `flags` 的重复门槛，只以消息入口保持的捕获/
  中键状态及实际进入 `CMainmapView::WM_MOUSEMOVE` 作为对象确认。玩家复测确认拖动已经生效，
  但偶尔会中断；推断是绑定后仍要求每个移动消息继续路由到同一地图 handler。连续性修正版仅用
  第一次 handler 调用确认地图对象，后续移动在统一规范化入口直接调用已确认的原作地图移动函数，
  不再依赖逐帧路由；玩家复测仍观察到偶发中断。按当前产品范围停止该功能并移除实现，保留
  入口语义、失败现象和未闭合原因作为后续研究依据。
- 本机 Win32 硬件设备以 feature level 11.0 支持 `B5G5R5A1_UNORM` 的纹理与采样用途，并成功创建动态纹理和 shader-resource view。
- 运行时检查确认窗口物理尺寸变化时，DPI 虚拟化后的客户区与 swap-chain back buffer 同步变化。
- 玩家确认当前版本可正常显示，白屏、持续闪烁、明显模糊和初始黑屏已消失；800×600 客户区显示完整 4:3 画面，无白色客户区或裁切。
- 玩家确认按钮命中与地图边缘滚动位置恢复正确；目标目录下单文件与非单文件版本均可无参数启动。

这些结果确认目标版本的呈现和主输入边界可被该方案接管，不确认所有设备、DPI、窗口状态和长期运行体验。

## 排除路径与可复用诊断

以下失败实验不属于当前实现，但保留其否定结论可避免后续重复采用已证伪的边界。

### DPI 感知必须在目标主线程恢复前设置

系统缩放为 150% 时，旧版窗口捕获尺寸与进程内逻辑尺寸出现 1.5 倍差异，同时伴随模糊和鼠标 hover 偶发偏移。这说明 GDI 缩放结果和部分坐标来源受到 DPI 虚拟化影响。在 DLL 初始化阶段设置 DPI 感知已经太晚；必须由启动器在挂起目标进程中、注入 DLL 和恢复主线程之前调用 `SetProcessDpiAwarenessContext(SYSTEM_AWARE)`，失败时终止启动流程。

玩家随后观察到原版直接启动存在 Windows DPI 放大，而 Toolkit 启动后的初始客户区保持
物理 1024×768。该差异反向确认进程级 DPI 感知已经生效，不构成撤销它的证据。目标行为
改为保留 `SYSTEM_AWARE` 的坐标不变量，同时由 Toolkit 主动按 system DPI 设置初始客户区；
在 96 DPI 下应保持 1024×768，在 144 DPI 下应为 1536×1152，内部画布仍固定 1024×768。

### D3D11 最低能力边界

使用 `vs_4_0_level_9_3` 编译依赖 `SV_VertexID` 的全屏三角形顶点着色器会返回 `E_FAIL`，而对应像素着色器可成功编译。该路径不能通过保留 feature level 9.3 名义兼容来修复；当前实现要求 feature level 10.0 及以上，并统一使用 `vs_4_0`、`ps_4_0`。

### 捕获与呈现不能发生在同一次 `BitBlt` 调用栈内

把 DXGI `Present` 直接放入 `BitBlt` 代理时，原作仍持有同一 HWND 的目标 HDC，返回代理后才执行 `ReleaseDC`；运行时表现为进程正常、DIB 像素非空，但主窗口持续黑屏。因此 `BitBlt` 成功或 `Present` 返回成功不能证明画面最终可见。

两种消息延迟方案也被运行时结果否定：

- 在首次 `BitBlt` 线程创建 message-only window 并投递请求，错误地假定该线程负责泵消息；
- 向主窗口投递请求并在 `normalize_window_message` 中呈现，仍未形成可靠的 DC 生命周期边界。

确定边界来自两条原作提交函数共同具备的 `BitBlt → ReleaseDC` 相邻调用。当前代理只在 `BitBlt` 登记帧，在匹配的 `ReleaseDC` 调用原函数之后执行上传和 `Present`；`WM_PAINT` 补帧则在原窗口过程返回后提交。

### 初始黑屏与 resize 是两个独立问题

运行时检查曾确认 D3D device、context、swap chain、纹理、shader 和 render target 均已创建，帧登记计数也持续增长，但上传函数在绘制前返回失败。根因是上传器在已经由 `GetObject(..., BITMAP)` 确认尺寸、位深和像素地址后，又要求 `GetObject(..., DIBSECTION)` 返回完整扩展结构。这比原作已确认的提交契约更强，且在目标环境不成立。上传路径改为只依赖 `BITMAP` 提供的宽高、stride、位深和非空 `bmBits` 后，玩家确认黑屏消失。

另一个独立问题发生在 `ResizeBuffers`：旧 render-target view 虽已从工具对象释放，仍可能绑定在 immediate context 上，间接持有 swap-chain back buffer。尺寸变化时必须先 `ClearState`，再释放 view、`Flush` 并调用 `ResizeBuffers`。后续运行时检查确认客户区与 back buffer 已能同步变化；该修正有效，但不是初始黑屏的根因。

### 输入入口不能依赖窗口子类或影片钩子

`movie_mouse_getmessage_hook (0x5C9560)` 的生命周期与 MCI/MPEG 播放绑定，只处理影片窗口鼠标消息。主窗口已登记内部 handler 时，`dispatch_window_message` 可以直接进入自定义路由而不调用 `DispatchMessageA`，所以 Win32 子类过程既不能覆盖全部鼠标消息，也收不到用于工具功能的 F12。更换虚拟键不会改变这一事实；坐标反变换和 F12 检测必须放在主规范化入口。

### 工具提示不能与游戏帧分步写入同一窗口

在每次游戏帧提交后追加 GDI 提示，会在连续地图滚动时产生缩放帧与提示互相覆盖的闪烁。提示应由归属主窗口的独立透明覆盖窗口承载，并具备不可激活、鼠标穿透属性；它只在文本或宿主几何变化时重绘，不参与原作逐帧提交。

## IDA 写入与回读

项目主 IDB 完成以下命名与类型收敛：

| 地址 | 名称 | 证据摘要 |
| --- | --- | --- |
| `0x433310` | `get_game_cursor_position` | 窗口/全屏分支统一读取鼠标位置 |
| `0x51E2A0` | `select_map_edge_scroll_cursor` | 四边 32 像素阈值和可滚动范围决定八方向光标 |
| `0x51E400` | `update_map_edge_scroll_cursor` | 调用前者并在结果变化时设置光标 |
| `0x519B60` | `center_map_view_at_isometric_point` | 夹取等距地图中心、更新 1024×768 视图矩形并触发刷新 |
| `0x51D930` | `scroll_map_view_direction` | 八方向生成 ±160 像素目标中心后调用地图中心移动入口 |
| `0x534000` | `CSan9MapView_on_mouse_move` | `CSan9MapView` 的 `WM_MOUSEMOVE` handler |
| `0x5C3540` | `present_full_game_frame_gdi` | 完整帧 `BitBlt → ReleaseDC` 提交路径 |
| `0x5C3640` | `create_game_backbuffer_dib` | 创建固定尺寸的 16 位 `BI_RGB` 共享后缓冲 |
| `0x5C3B60` | `present_dirty_game_rect_gdi` | 脏矩形 `BitBlt → ReleaseDC` 提交路径 |
| `0x5C9560` | `movie_mouse_getmessage_hook` | 影片生命周期限定的 `WH_GETMESSAGE` 回调 |
| `0x5CF020` | `map_screen_point_to_target_client` | 根窗口与自定义窗口的坐标转换 |
| `0x5CF180` | `post_window_message_to_target` | 根窗口与内部窗口的消息投递 |

`normalize_window_message` 的原型收敛为 `int __cdecl(MSG *normalized_message, MSG *source_message)`。固定画布配置、共享后缓冲、Big5 窗口标题、呈现边界、输入路由和卷动模式字段的证据注释均已写入并回读；未修改样本程序字节或既有函数边界。

## 事实、推断与待验证项

- 事实：原作使用固定 1024×768×16 位逻辑画布，最终通过两条 GDI 路径提交到主窗口。
- 事实：主游戏输入经过统一规范化入口；地图边缘滚动使用逻辑视图四边 32 像素阈值。
- 事实：当前原型已在目标版本上完成可缩放呈现、鼠标坐标反变换和基本启动回归。
- 推断：F8 的两种运行时提示分别对应 `+0x1A70` 的悬停与按住左键模式；直接写入链仍待闭合。
- 待验证：透明提示在连续地图滚动时是否完全无闪烁；Catmull–Rom 的实际画质与 GPU 帧耗时；长时间动态画面和设备丢失恢复。
- 已停止：中键拖动原型存在未闭合的偶发中断，当前 Toolkit 不提供中键拖动或滚轮缩放。
- 待验证：最大化、最小化、Alt+Tab、多种 DPI 和交互式 resize。
- 限制：第一版仅支持从窗口模式启动；不承诺全屏切换、其他语言版本、补丁版本或所有硬件。

该原型是版本限定研究工具，不进入重制版产品运行时。
