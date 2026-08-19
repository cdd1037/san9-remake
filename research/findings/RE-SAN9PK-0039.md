# RE-SAN9PK-0039：SRC-SAN9PK-TC-101 公共全局根与子系统地图（第一轮）

- 状态：草案
- 日期：2026-08-19
- 分析者：Codex
- 结论类型：事实、推断和待验证项分开记录
- 置信度：高（已类型化根与直接引用）；中（子系统归类与生命周期）
- 关联来源：`SRC-SAN9PK-TC-101`
- 关联记录：`RE-SAN9PK-0037`、`RE-SAN9PK-0038`、`RE-SAN9PK-0040`

## 样本与工具

- 样本：繁体中文版 1.0.1.0，32 位 PE，2,636,800 字节，SHA-256 `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`。
- 工具与数据库：IDA 9.3、Hex-Rays、IDA MCP（health `ok`）；项目主数据库 `research/analysis/San9PK-TC-101.i64`。

## 目的与方法

本记录承接批次 E/F 的第一轮铺底。目标是让后续调查可以从高传播全局、公共类型和可靠入口进入调用链，不把代码引用计数误当作对象语义。

`research/scripts/ida/export_global_roots.py` 在当前 IDB 中遍历 `.text` 指令的数据引用，把目标归并到 IDA 数据项头，记录对象边界、类型、代码引用数、来源函数和中性候选类别。脚本只读、不改名、不改类型、不保存 IDB。两次导出核心摘要一致：

- 输入 SHA-256：`D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`
- top-500 候选输出摘要：`0ECB63D9D2B60DD480A9AD43A5F0BCD2508B30FE4D111C3D392FADA5A6D9AA36`
- 报告：`artifacts/ida/San9PK-TC-101/global-roots.json`（Git 忽略）

## 已闭合的公共根

### 窗口消息根

- `0x1B40800 hWnd`：已有类型 `HWND`；50 个代码引用，覆盖窗口消息泵、坐标转换、窗口过程和投递链。`0x5CA8C0` 使用它调用 `ClientToScreen`，`0x5CBC70` 使用它调用 `GetWindowRect`/`ScreenToClient`。这是窗口子系统根对象的事实级导航锚点。
- `0x66F84C wParam`：已有类型 `WPARAM`；52 个代码引用，覆盖消息识别、命中测试、分发和队列辅助路径。名称保留为现有人工名称，未根据单一函数用途改名。
- `0x1B40818 buf_1`：类型已应用为 `WindowMessageQueueNode *`；由 `queue_window_message` 分配/链接的 0x20 字节节点通过该全局进入消费链。
- `0x1B40820 psub_5C6F20`：已有函数指针类型 `int (*)(void)`；`0x5CBC70` 以可选回调判断坐标偏移是否有效。回调实际设置方和生命周期仍待验证。

### 剧本记录根

- `0x1253C38 san9_force_records`：已有类型 `San9ForceRecord[50]`；代码引用进入 `get_force_record (0x453100)`、场景块反序列化和多个读取/写入路径。数组边界由现有类型记录，不在本批次重建字段。
- `0x1258EE0 san9_person_records`：已有类型 `San9PersonRecord[850]`；代码引用进入 `get_person_record (0x4531B0)`、场景块反序列化和多个记录访问路径。与 `san9_force_records` 的共享加载入口构成数据子系统导航锚点。
- `0x1B40650 n255`：36 个代码引用，类型为 `unsigned int`；被字体、位图和多处渲染/资源路径使用。其准确语义不能由名称或数值单独确认，本批次仅列为高传播待验证根。

### MFC 线程/类根

- `0x5E0D20 AfxGetThread`：返回对象被 `AfxWinMain` 用于 `InitInstance`/窗口初始化/运行路径；调用者 47 个。命名和注释已写入并保存回读，但完整 `CWinThread` UDT 尚未建立。
- `0x60A1C0 runtime_class_CCommandMenuDlg`、`0x60A220 message_map_CCommandMenuDlg` 和 `0x60A238 vftable_CCommandMenuDlg` 已在 `RE-SAN9PK-0038` 中通过运行时类、构造/析构、消息映射访问器和连续函数指针表闭合。
- 10 条数据类构造路径已命名：`CAreaData_ctor`、`CFixedBuildingData_ctor`、`CForceData_ctor`、`CHeihouData_ctor`、`CItemData_ctor`、`CJinkeiData_ctor`、`CKanshakuData_ctor`、`CKokugouData_ctor`、`CMapObjectData_ctor`、`CShakuiData_ctor`。共同证据是传入对应 `runtime_class_*` 并写入对象调度表；未将同族的未知析构路径批量命名。
- 400 条高置信度运行时类记录和 67 张消息映射是类体系的机器导航目录，不等于 400 个类的完整方法/字段语义。

### 共享字符串公共容器

`get_empty_string_buffer`、`reset_shared_string`、`ensure_string_capacity`、`release_shared_string_buffer`、`assign_string_bytes`、`assign_c_string`、`append_string_bytes`、`set_string_length` 和 `set_string_byte` 已通过 `diff_before_after` 命名。直接证据显示：

- 共享缓冲头位于数据指针前 12 字节；`-0x0C` 为引用计数，`-0x08` 为容量，`-0x04` 为长度。
- 赋值路径按长度复制并写入 NUL；扩容/共享引用不满足条件时进入释放和重新分配路径。
- 声明 `SharedStringBufferHeader`，大小 12 字节；本轮不把它强行应用到哨兵全局。

## 子系统入口图（第一轮）

以下是稳定入口和状态根的导航图；箭头表示已观察到的静态调用或数据引用，括号内的“推断”表示尚未形成完整生命周期证明。

```text
启动/应用生命周期
  start 0x5D4A29
    -> _mtinit 0x5D718A
    -> __setargv 0x5D97FE
    -> AfxWinMain 0x5DE9A0
    -> AfxGetThread 0x5E0D20
    -> 窗口消息/运行链

窗口消息与 UI 框架
  pump_window_messages 0x5CADA0
    -> normalize_window_message 0x5CC0B0
    -> dispatch_window_message 0x5CAAF0
       -> route_window_message 0x5CA9C0
       -> queue_window_message 0x5CC630
       -> dispatch_wndproc_message 0x5CB150
    roots: hWnd 0x1B40800, wParam 0x66F84C, buf_1 0x1B40818

MFC 类与对象分发
  runtime_class_* (400 records)
    -> message_map_* (67 headers / 183 entries)
    -> handler addresses / vtable candidates
  CCommandMenuDlg: 0x4C9150 / 0x4C9200 / 0x4C9140 / 0x4C9210

剧本与记录数据
  deserialize_scenario_chunks 0x453F60
    -> san9_force_records 0x1253C38
    -> san9_person_records 0x1258EE0
  get_force_record 0x453100 / get_person_record 0x4531B0

资源与字体（入口已定位，状态根仍待闭合）
  load_font_resources 0x4278F0
    -> n255 0x1B40650 (待验证)
  render_font_glyphs 0x5BB310
```

## 仍待验证

- `0x1B40BD0`、`0x1B40B90` 与 `AfxGetThread` 内部线程数据链的完整结构、初始化和销毁路径；当前只记录访问形态。
- `0x1B40650 n255` 的真实对象边界和所属资源/字体状态；不能因跨模块引用多就拆分相邻数据。
- 世界状态、日期/旬推进、命令队列、战斗、外交、计略、事件和 AI 的首个可靠入口与核心状态根；本轮不从类名或字符串直接推导玩法语义。
- 消息映射 entry 的 `signature` 到成员函数参数原型的完整表；当前只确认 entry 布局、ID 字段、handler 地址和 sentinel。
- `0x620DD4` 探索事件候选虚表的运行时类关联和边界。

## IDA 写入与验证

- 写入：`AfxGetThread`、10 条数据类构造函数、9 个共享字符串 API 的函数名；`hWnd`、`wParam` 和相关访问器的证据注释。
- 类型沿用或此前已验证：`HWND`、`WPARAM`、`WindowMessageQueueNode *`、剧本数组类型。
- 新增 `SharedStringBufferHeader`，`type_inspect` 回读大小 12；所有新增函数名通过 `diff_before_after` 回读，IDB 保存成功。
- 全局根脚本两次运行结果一致；运行时类、消息映射、全局根和虚表候选摘要保持不变：运行时类 `7EF1BC2F9C6A35D999A532EE169B99BF30F10A001EF62CDCE85AD0AFBC351FA0`，消息映射 `4F26CD11CD0CA91B1260A8E78CA6D52C3B200B86956E4A77587275BB448F4B85`，全局根 `0ECB63D9D2B60DD480A9AD43A5F0BCD2508B30FE4D111C3D392FADA5A6D9AA36`，健康基线当前摘要 `f7152b15c1f16dda7754d2f399b41d7c963c591b923bdd8fd0bebb3643274b7f`；虚表候选摘要 `EFE06740B389D1BB3295DAE1BDAD35D39E0DC7A11D79230434CA28C81EABF900`。后续写入见 `RE-SAN9PK-0044`、`RE-SAN9PK-0045` 和 `RE-SAN9PK-0046`。

## 限制

本记录是导航底座，不是子系统完整规格。代码引用只说明传播度；命名、类型和生命周期仍需由初始化、消费者、销毁和对照实验闭合。没有把反编译伪代码、汇编或第三方补丁实现写入产品代码。
