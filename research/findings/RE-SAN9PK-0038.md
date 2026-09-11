# RE-SAN9PK-0038：SRC-SAN9PK-TC-101 IDA 批次 B–F 铺底（第一轮）

> **当前主库定义回退修复（2026-09-01）**：本记录使用的 `0x4C9220/0x4C9740` 曾在后续主库状态中退回为非代码；[RE-SAN9PK-0154](RE-SAN9PK-0154.md) 已按消息映射、原始控制流和返回边界补回，分别确认为 `WM_LBUTTONUP` 与 `WM_MOUSEMOVE` 处理函数。本记录的消息映射归属结论保持不变。

- 状态：草案
- 日期：2026-08-19
- 分析者：Codex
- 结论类型：事实与高置信度推断；未决项单独列出
- 置信度：高（入口链局部语义、运行时类记录布局、写入回读）；中（MFC 兼容命名与若干 CRT 阶段名称）
- 关联来源：`SRC-SAN9PK-TC-101`
- 关联记录：`RE-SAN9PK-0037`
- 关联实验：无
- 关联规格：无

## 样本、数据库与工具

- 样本：三国志 IX PK 繁体中文版 1.0.1.0，文件大小 2,636,800 字节。
- 样本 SHA-256：`D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`。
- 项目主 IDB：`research/analysis/San9PK-TC-101.i64`，未提交数据库文件。
- IDA/Hex-Rays/MCP：IDA 9.3，Hex-Rays 可用，MCP server health 为 `ok`。
- 机器报告：`local/generated/research/ida/San9PK-TC-101/runtime-classes.json`（Git 忽略）。
- 虚表机器报告：`local/generated/research/ida/San9PK-TC-101/vtable-candidates.json`（Git 忽略）。
- 消息映射机器报告：`local/generated/research/ida/San9PK-TC-101/message-maps.json`（Git 忽略）。
- 全局根对象机器报告：`local/generated/research/ida/San9PK-TC-101/global-roots.json`（Git 忽略）。
- 生成脚本：`research/scripts/ida/export_runtime_classes.py`。
- 生成脚本：`research/scripts/ida/export_message_maps.py`、`research/scripts/ida/export_global_roots.py`。

## 调查问题

在批次 A 健康基线之后，只读收窄入口链、反编译失败点和 MFC/运行时类候选；对有交叉证据的函数和统一运行时记录布局进行最小化 IDA 标注，保存后逐项回读，形成后续虚表、消息映射和公共类型研究的铺底。

## 只读证据

### 入口链

- `0x5D4A29 start`：函数大小 `0x1D2`，149 条指令，41 个基本块，21 个直接被调用目标，无调用者。反编译显示其先完成 PE/环境和 CRT 初始化，最后调用 `0x5DE9A0` 后退出。
- `0x5DE9A0`：原型为 `int __stdcall(HMODULE, int, int, int)`，大小 `0x45`，由 `start` 调用。函数读取 `0x5E0D20` 返回的线程/应用状态，写入实例字段，执行多个虚调用，并通过 `0x5CC5D0` 处理消息范围 `0x600..0x601`。
- `0x62693C` 的运行时记录为 `CWinApp`，基类记录为 `0x6269F4 CWinThread`；后者基类为 `0x626A70 CCmdTarget`，再基类为 `0x6262F4 CObject`。`CWinApp`、`CWinThread` 字符串及相邻记录交叉引用支持其 MFC 应用入口语义。
- `0x5D718A`：调用 `_mtinitlocks`、`TlsAlloc`、`TlsSetValue`，建立 136 字节 TLS 块并保存线程 ID；这是运行库线程状态初始化的事实依据。
- `0x5D97FE`：解析命令行、建立参数数组，并写入 `dword_1B40DE8` 与 `dword_1B40DEC`；命名为 `__setargv`。
- `0x5D9562`：处理带引号或不带引号的可执行文件名，并返回命令行中程序名之后的位置；命名为 `get_command_line_tail`。
- `0x5D3B62`：调用 `_cfltcvt_init_0`、`_ms_p5_mp_test_fdiv` 和 `_setdefaultprecision`；命名为 `initialize_floating_point`。
- `0x5D9BC0` 与 `0x5D6197` 均操作函数指针表；前者的表边界和后者的两组 `atexit` 表已确认，具体 CRT 阶段名称仍待验证。

### 反编译失败点

7 个失败点均只增加证据注释，没有改变函数边界或字节：

- `0x452001`：反编译在 `0x452018` 失败；该处之后进入 `0x452019..0x45202C` 共享尾部，附近存在 `0x452094` 表项，边界归属待验证。
- `0x4C3D50`、`0x4C3D80`、`0x4C3DB0`：分别处理消息 ID `0x17E2`、`0x17E4`、`0x17E3`，调用 `format_text_message` 和 `sub_5CE5F0`，随后汇入共享尾部 `0x4C3D10`。
- `0x53B2F0`、`0x53B320`：首参数为空时直接返回，否则分别以 selector 3、4 调用 `0x53B200`，均以 `retn 0x10` 返回；调用约定的完整类型仍待验证。
- `0x5BB310`：读取 `p_n255`，以三个栈参数调用 `render_font_glyphs`；现有类型为 `_DWORD __stdcall(LONG, LONG, RECT *)`，失败位于该调用附近。

### 运行时类记录

只读脚本同时执行类名字符串关联、`.rdata` 直接字段扫描和由基类/next 指针进入 `.data` 的引用扫描，并要求名称指针、对象大小、schema、创建函数指针、基类指针和 next 指针全部通过校验。稳定结果如下：

- 类名候选 420；记录候选 659；可解析记录 400；高置信度记录 400；记录大小 24 字节。
- 样本 SHA-256 与批次 A 一致；核心摘要哈希为 `7EF1BC2F9C6A35D999A532EE169B99BF30F10A001EF62CDCE85AD0AFBC351FA0`。
- 初始字符串前置扫描只得到 173 条；随后发现 `0x60A1C0 CCommandMenuDlg` 的类名指针位于记录内 `+0x68` 位置而非固定的 `string-0x18` 关系，因此改为直接扫描每个对齐的 `.rdata` 字段 0，并沿已验证的 base/next 指针追踪 `.data`。最终 399 条来自 `.rdata` 直接扫描，另有 1 条 `.data` 引用记录进入高置信度目录。
- `0x6262F4 CObject`、`0x605CA8 CGameData`、`0x605F04 CKanshakuData`、`0x6062D8 CShakuiData`、`0x60634C CSquareData` 和 MFC 应用链记录均通过相同布局验证。
- 含非空创建函数的记录包括：`CFlagRelayPointSprite -> 0x406420`、`CMapBuildingVO -> 0x522B40`、`CIkkiUnitEffectVO -> 0x54BBA0`、`CEditPlateSprite -> 0x59B0F0`、`CPhasedSprite2 -> 0x5C6BB0`。这是字段事实，不等同于已确认的构造函数语义。
- `0x66F768 CSprite` 的 `.data` 记录通过 base/next 关联进入高置信度集合；`0x66F960 CObList` 保留为中等置信度，因为其 create 指针 `0x5D2920` 尚未成为 IDA 函数入口，未将该不确定性抹平。

当前采用的布局声明为：

```c
typedef struct MfcRuntimeClassRecord {
  const char *class_name;          // +0x00
  int object_size;                 // +0x04
  unsigned int schema;             // +0x08
  void *create_object;             // +0x0C
  struct MfcRuntimeClassRecord *base_class; // +0x10
  struct MfcRuntimeClassRecord *next_class; // +0x14
} MfcRuntimeClassRecord;
```

`type_inspect` 回读大小 24，成员偏移为 `0x00/0x04/0x08/0x0C/0x10/0x14`。所有 400 条高置信度记录均通过字段校验；399 条为 `.rdata` 直接记录，1 条为 `.data` 引用记录。基类链抽查支持该布局。

## IDA 写入

### 函数名称、原型与注释

先以 `rename` dry-run 验证 5 个名称，再通过 `diff_before_after` 应用并回读：

| 地址 | 写入名称或类型 | 证据摘要 |
| --- | --- | --- |
| `0x5DE9A0` | `AfxWinMain`；`int __stdcall(HMODULE, HMODULE, LPSTR, int)` | `start` 调用、CWinApp/CWinThread 记录链、窗口消息处理形态 |
| `0x5D718A` | `_mtinit` | TLS 分配、136 字节线程块、线程 ID 初始化 |
| `0x5D97FE` | `__setargv` | 命令行解析、argv/argc 全局写入 |
| `0x5D9562` | `get_command_line_tail` | 处理引号和程序名尾部 |
| `0x5D3B62` | `initialize_floating_point` | 浮点转换、FPU 检测和默认精度初始化 |

另外在 `0x5D6197`、`0x5D9BC0` 以及 7 个反编译失败点追加了证据注释；所有新增注释均标注 `RE-SAN9PK-0038`。未修改函数边界、指令字节或产品代码。

### 运行时类目录

先声明并检查 `MfcRuntimeClassRecord`，再对 8 个体系锚点调用 `make_data`，随后对其余 165 条高置信度记录分五批调用 `make_data`。每条记录均使用 `delete_existing=true`、类型 `MfcRuntimeClassRecord` 和 `runtime_class_<ClassName>` 名称；没有覆盖既有用户命名记录。

首批锚点为：

`runtime_class_CObject` (`0x6262F4`)、`runtime_class_CGameData` (`0x605CA8`)、`runtime_class_CKanshakuData` (`0x605F04`)、`runtime_class_CShakuiData` (`0x6062D8`)、`runtime_class_CSquareData` (`0x60634C`)、`runtime_class_CWinApp` (`0x62693C`)、`runtime_class_CWinThread` (`0x6269F4`)、`runtime_class_CCmdTarget` (`0x626A70`)。

对 10 条同形态构造路径追加稳定名称：`CAreaData_ctor`、`CFixedBuildingData_ctor`、`CForceData_ctor`、`CHeihouData_ctor`、`CItemData_ctor`、`CJinkeiData_ctor`、`CKanshakuData_ctor`、`CKokugouData_ctor`、`CMapObjectData_ctor`、`CShakuiData_ctor`。每条路径均直接把对应 `runtime_class_*` 地址传给公共初始化 helper，并写入对象调度表；均先 dry-run、再用 `diff_before_after` 回读。

### 虚表、构造路径与消息分发铺底

只读核查了三个已有数据类虚表、探索事件 type56 候选和 MSVC `type_info` 运行库对象。三个数据类虚表均表现为 19 个连续槽位，且能与相邻运行时记录或字符串边界闭合：

- `0x605F30 vftable_CKanshakuData`：槽位 `0..18`；包含 `initialize_kanshaku_data`、`get_kanshaku_value`、`format_kanshaku_value`。
- `0x606300 vftable_CShakuiData`：槽位 `0..18`；包含 `initialize_shakui_data`、`get_shakui_value`、`format_shakui_value`。
- `0x606370 vftable_CSquareData`：槽位 `0..18`；包含 `get_csquare_value`、`format_square_property_value`。
- `0x620DD4 vtable_exploration_event_type56`：保留为候选，包含 `exploration_event_type56_execute`，槽位边界和 RTTI 关系未闭合。
- `0x626CB0 ??_7type_info@@6B@`：确认属于 MSVC `type_info` 运行库对象，不纳入产品类虚表目录。
- `0x60A238 vftable_CCommandMenuDlg`：由 `0x4C9150 CCommandMenuDlg_ctor` 和 `0x4C9200 CCommandMenuDlg_dtor` 两条路径写入；连续函数指针表候选边界为 65 个槽位，仍保留“候选”措辞。
- `0x606C8C vftable_CPersonData`：由 `CPersonData_ctor`/`CPersonData_dtor` 两条路径写入；与 `runtime_class_CPersonData` 直接关联，槽位边界仍以机器扫描结果为准。
- `0x4C9140 CCommandMenuDlg_get_runtime_class` 返回 `runtime_class_CCommandMenuDlg`；`0x4C9210 CCommandMenuDlg_get_message_map` 返回 `message_map_CCommandMenuDlg`。两者均为直接数据交叉引用。

`export_vtable_candidates.py` 只读探测 7 个已命名候选、每个候选 32 个原始槽位；连续槽位的最终边界仍以类记录、构造函数和交叉引用闭合为准。加入 `vftable_CCommandMenuDlg` 与 `vftable_CPersonData` 后，两次运行的核心摘要哈希均为 `EFE06740B389D1BB3295DAE1BDAD35D39E0DC7A11D79230434CA28C81EABF900`。

消息泵和窗口分发链的只读反编译显示：`pump_window_messages` 从 `PeekMessageA` 取消息，经 `normalize_window_message` 规范化后调用 `dispatch_window_message`；后者按消息号、目标窗口和队列状态分流到 `DispatchMessageA`、`route_window_message` 或 `queue_window_message`。`post_window_message` 负责向主窗口投递或包装 `0x601` 队列消息；`lookup_window_handler` 对 1..40 的窗口/控件索引按 `0x48` 步长查表，`has_window_handler` 是其存在性检查；`dispatch_wndproc_message` 处理窗口过程级消息并调用 `route_window_message`。

消息映射扫描得到 67 个 8 字节 header、183 个 24 字节 handler entry；每张 entry 表均由全零 sentinel 闭合。61 个 header 可与唯一运行时类关联，6 个保留为地址型候选；共 3 个 header 暂无代码 xref。`CCommandMenuDlg` 的闭环为 `runtime_class_CCommandMenuDlg (0x60A1C0) -> message_map_CCommandMenuDlg (0x60A220) -> message_map_entry_CCommandMenuDlg_0/1 (0x60A1D8/0x60A1F0) -> 0x4C9220/0x4C9740`。

上述 10 个消息/窗口函数名称，以及 4 个 `CCommandMenuDlg` 构造/析构/访问器名称均先 dry-run，再以 `diff_before_after` 应用，并追加 `RE-SAN9PK-0038` 证据注释；保存后名称和函数注释回读成功。

### 消息映射公共类型

声明并回读：

```c
typedef struct MfcMessageMapEntry {
  UINT message;       // +0x00
  UINT code;          // +0x04
  UINT first_id;      // +0x08
  UINT last_id;       // +0x0C
  UINT signature;     // +0x10
  void *handler;      // +0x14
} MfcMessageMapEntry;

typedef struct MfcMessageMap {
  struct MfcMessageMap *base_map; // +0x00
  MfcMessageMapEntry *entries;    // +0x04
} MfcMessageMap;
```

`type_inspect` 回读 entry 大小 24、map 大小 8；67 个 header 和 183 个 entry 均已应用对应类型与稳定名称，既有用户命名未被覆盖。header 注释记录 base、entry 数量、近邻类和 sentinel 证据；保存后抽查 `0x60A220`、`0x60A1D8`、`0x615BA8`、`0x6251E8` 等地址通过回读。消息映射机器报告两次核心摘要均为 `4F26CD11CD0CA91B1260A8E78CA6D52C3B200B86956E4A77587275BB448F4B85`。

共享字符串缓冲公共头部声明为 `SharedStringBufferHeader`，由数据指针前 `-0x0C/-0x08/-0x04` 的引用计数、长度、容量访问闭合；`type_inspect` 回读大小 12。该类型目前只进入本地类型库，未套用到可能与哨兵全局重叠的地址。

## 批次 E 初始公共类型

窗口消息队列节点的分配和消费路径同时闭合了大小、字段顺序和 next 指针：`queue_window_message` 分配 `0x20` 字节并写入前 7 个 DWORD，随后把第 8 个 DWORD 作为链表 next；`normalize_window_message` 和 `dispatch_window_message` 消费前 `0x1C` 字节。因此声明并应用：

```c
typedef struct WindowMessageQueueNode {
  HWND target_window; // +0x00
  UINT message;       // +0x04
  WPARAM wParam;      // +0x08
  LPARAM lParam;      // +0x0C
  DWORD reserved_10;  // +0x10
  DWORD reserved_14;  // +0x14
  DWORD reserved_18;  // +0x18
  struct WindowMessageQueueNode *next; // +0x1C
} WindowMessageQueueNode;
```

`type_inspect` 回读大小 32、8 个成员和偏移 `0x00/0x04/0x08/0x0C/0x10/0x14/0x18/0x1C`；全局 `0x1B40818 buf_1` 回读类型为 `WindowMessageQueueNode *`。三个 reserved 字段保持未知，没有借助名称猜测用途。

同一消息链还为 `0x1B40804 wParam_0` 应用 `HWND`，为 `0x1B40810 dword_1B40810` 和 `0x1B40814 dword_1B40814` 应用 `BOOL`；三者的证据注释和类型均保存后回读成功。保留已有人工名称，没有批量改名。
- `0x5E0D20` 在 CWinThread/CWinApp 虚调用槽位和 47 个调用者交叉证据下命名为 `AfxGetThread`；未强行把其返回值声明为尚未建立的完整 CWinThread UDT。

## 保存与回读

- `idb_save` 成功，目标为 `research/analysis/San9PK-TC-101.i64`。
- 保存后重新执行 `export_runtime_classes.py` 两次：类名候选 420、记录候选 659、可解析 400、高置信度 400；核心摘要哈希均为 `7EF1BC2F9C6A35D999A532EE169B99BF30F10A001EF62CDCE85AD0AFBC351FA0`。
- 保存后重新执行 `export_message_maps.py` 两次：header 67、entry 183；核心摘要哈希均为 `4F26CD11CD0CA91B1260A8E78CA6D52C3B200B86956E4A77587275BB448F4B85`。
- 保存后重新执行 `export_global_roots.py` 两次：稳定输出 top-500 代码数据引用候选；核心摘要哈希均为 `0ECB63D9D2B60DD480A9AD43A5F0BCD2508B30FE4D111C3D392FADA5A6D9AA36`。
- `read_struct` 抽查 `0x600204 CFlagRelayPointSprite`、`0x60508C CAreaData`、`0x6113CC CMapBuildingVO`、`0x6184D4 CIkkiUnitEffectVO`、`0x625B64 CPhasedSprite2`，均回读 24 字节结构、原始对象大小、schema、创建函数和基类指针。
- `0x5DE9A0` 回读函数名为 `AfxWinMain`，类型为 `int __stdcall(HMODULE, HMODULE, LPSTR, int)`，反编译首行同步显示该名称和原型。
- `0x5CAAF0` 回读反编译首行为 `int __stdcall dispatch_window_message(MSG *lpMsg)`；消息分发链中的新增名称与函数注释均可回读。
- `type_inspect` 回读 `WindowMessageQueueNode` 为 32 字节 UDT；`0x1B40818` 的全局类型和证据注释均回读成功。
- `0x1B40804`、`0x1B40810`、`0x1B40814` 分别回读为 `HWND`、`BOOL`、`BOOL`。

按计划重新运行 `health_baseline.py`；在新增数据类构造器、共享字符串 API、`SharedStringBufferHeader`、10 个子系统访问器、日期链、按日更新入口、部队周期入口、两个模拟状态机、势力关系处理链和旬命令路由边界标注后得到当前核心摘要 `f7152b15c1f16dda7754d2f399b41d7c963c591b923bdd8fd0bebb3643274b7f`；函数总数 10,918、命名函数 785、自动命名函数 10,133、库函数 465、thunk 136、函数注释 355、命名实体 7,430、命名数据实体 6,268、本地类型 185、反编译失败 7、可执行段未定义字节 0、非执行段代码项 0、执行段数据项 15,472。函数/ thunk 计数稳定；相关写入未修改程序字节。

验收整理完成后再次在项目主 IDB 上实跑全部五个导出器：健康基线连续两次得到摘要 `b9cf1108caef89c4ed7588943ff84e3a58188754c66254a03e9e060ac2be02d4`，函数 10,919、用户命名函数 810、函数注释 393、反编译失败 7、可执行段未定义字节 0；运行时类仍为 400 条高置信度记录，消息映射仍为 67 张/183 项，全局根仍为 top-500。随后新增的已命名虚表使当前候选数增至 13，连续两次摘要为 `ae9837746fa17a45b25e6f37954b8cf3932dd3e68b56b7e64e7f8cbf12ddc63f`；这是 IDB 标注增长，不是脚本不稳定。所有验收报告仍位于被 Git 忽略的 `local/generated/research/ida/San9PK-TC-101/`。

## 结论、推断与待验证

### 已确认事实

- 入口链中 5 个运行库/应用交接函数已获得稳定的证据化名称，其中 `AfxWinMain` 的参数类型完成回读。
- 7 个反编译失败点已分类为共享尾部、调用约定或类型问题候选，当前没有为了消除错误而改边界。
- 400 条高置信度 MFC/运行时类记录共享 24 字节布局；名称、对象大小、schema、创建函数、基类和 next 字段均已机械校验并完成 IDA 类型化。
- 67 个消息映射 header 与 183 个 handler entry 已按零项 sentinel 机械闭合并完成 IDA 类型化；61 个 header 可唯一关联运行时类。
- `AfxGetThread`、`CCommandMenuDlg` 构造/析构/运行时类访问器/消息映射访问器和 `vftable_CCommandMenuDlg` 已在交叉证据闭环后写入并回读。
- 10 条数据类构造路径和 9 个共享字符串缓冲 API 名称已在直接行为证据下写入并回读；`SharedStringBufferHeader` 类型大小已回读。

### 高置信度推断

- `0x5DE9A0` 是 MFC 风格的应用入口交接函数，`AfxWinMain` 是适合后续研究的工作名称；其虚函数槽位和应用对象初始化顺序仍需独立验证。
- 运行时类记录目录可以作为后续构造函数、虚表和对象工厂分析的索引，但类名与记录字段不自动赋予业务语义。
- 全局根扫描已把窗口消息根、剧本记录表和运行时类目录按代码引用传播度列入机器目录；引用数只表示导航优先级，不自动证明生命周期或字段语义。

### 待验证

- `0x5D9BC0`、`0x5D6197` 的 CRT 阶段精确名称及表边界。
- `0x5E0D20` 的 MFC 线程/应用状态访问器语义，以及 `AfxWinMain` 的虚表槽位映射。
- 7 个反编译失败点的最终函数边界、共享尾部归属和完整原型。
- 5 个非空 `create_object` 指针是否可分别与对应类构造路径建立一一对应关系。
- `0x620DD4` 探索事件候选虚表与运行时类记录之间的映射。
- 67 张消息映射中 handler signature 与具体 C++ 成员原型的完整对应关系；当前只确认表布局、ID 字段和 handler 地址。
- `0x1B40BD0`/`0x1B40B90` 线程状态根的完整对象布局与生命周期。

## 限制

本记录仅登记 IDA 标注和研究证据；不把反编译伪代码、汇编片段或第三方实现复制到产品代码。IDB 是被忽略的本地研究产物，未提交；机器 JSON 报告同样未提交。
