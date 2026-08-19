# RE-SAN9PK-0040：旬命令、地图视图与记录列表导航补充

- 状态：草案
- 日期：2026-08-19
- 分析者：Codex
- 结论类型：事实、推断和待验证项分开记录
- 置信度：高（直接返回访问器与消息表布局）；中（UI 子系统工作分类）
- 关联来源：`SRC-SAN9PK-TC-101`
- 关联记录：`RE-SAN9PK-0038`、`RE-SAN9PK-0039`

## 样本与工具

- 样本：繁体中文版 1.0.1.0，32 位 PE，2,636,800 字节，SHA-256 `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`。
- 工具与数据库：IDA 9.3、Hex-Rays、IDA MCP（health `ok`）；项目主数据库 `research/analysis/San9PK-TC-101.i64`。

## 目的与范围

本记录补充批次 F 的子系统导航，不试图恢复旬推进规则或地图业务字段。目标是把已存在的运行时类记录、消息映射和短访问器连接起来，为后续产品驱动调查提供稳定入口。

本批次只处理直接返回已类型化 `MfcRuntimeClassRecord`/`MfcMessageMap` 地址的 10 个短函数：8 个运行时类访问器和 2 个消息映射访问器。名称先 dry-run 检查冲突，再通过 `diff_before_after` 应用；每个函数追加 `RE-SAN9PK-0040` 证据注释，保存 IDB 后用 IDA Python 回读名称和函数注释。

## 已确认事实

### 记录列表访问器

以下函数的反编译结果均为单一直接返回，返回地址与批次 C 已验证的运行时类记录一致：

- `0x46D760 CCityList_get_runtime_class` -> `runtime_class_CCityList`；记录对象大小 `0x20`，基类记录为 `0x6069A4`。
- `0x46DF60 CForceList_get_runtime_class` -> `runtime_class_CForceList`；记录对象大小 `0x20`，基类记录为 `0x606CA0`。
- `0x470DE0 CPersonList_get_runtime_class` -> `runtime_class_CPersonList`；记录对象大小 `0x20`，基类记录为 `0x606CA0`。
- `0x473CB0 CUnitList_get_runtime_class` -> `runtime_class_CUnitList`；记录对象大小 `0x20`，基类记录为 `0x606C2C`。

这些记录是 MFC 运行时目录中的容器/列表类描述，不等于 `city/person/unit` 全局数组本身；列表对象的字段和生命周期仍待单独闭合。

### 旬命令对话框链

- `runtime_class_COrderTurnDlg` 位于 `0x60FBE0`，对象大小 `0x684`，基类记录为 `runtime_class_COrderDlg`。
- `message_map_COrderDlg` 位于 `0x60A400`，有 3 个 handler entry，基类映射为 `message_map_CSan9Dlg`。
- `message_map_COrderTurnDlg` 位于 `0x60FC40`，有 2 个 handler entry，基类映射为 `message_map_COrderDlg`；entry 数组从 `0x60FBF8` 开始。
- `0x60FBF8` 的消息号为 `0x205`、signature 为 `0x24`，handler 地址为 `0x5D0E70`；`0x60FC10` 的消息号为 `0x101`、signature 为 `0x0B`，handler 地址为 `0x4FB0F0`。这里只记录表布局和分发关系，不把消息号直接解释为“旬推进”。
- `0x4FB060 COrderTurnDlg_get_runtime_class` 和 `0x4FB070 COrderTurnDlg_get_message_map` 分别直接返回上述记录和映射；这是该 UI 分支的可靠导航入口。

### 季节切换动画与地图视图

- `runtime_class_CSeasonChangeAnimeDlg` 位于 `0x618C58`，对象大小 `0x218`；`message_map_CSeasonChangeAnimeDlg` 位于 `0x618CB8`，有 2 个 entry，当前 handler 均为 `nullsub_67`。这支持“季节切换 UI/动画对象”的工作分类，但不证明其驱动日期状态。
- `runtime_class_CSan9MapUnitData` 位于 `0x615E70`，对象大小 `0x90`，基类为 `runtime_class_CSan9MapData`；`0x52F700 CSan9MapUnitData_get_runtime_class` 直接返回该记录。
- `runtime_class_CSan9MapView` 位于 `0x6160D0`，对象大小 `0x164`，创建函数为 `0x534DC0`；`message_map_CSan9MapView` 位于 `0x616160`，有 4 个 entry，且 `0x5332E0 CSan9MapView_get_runtime_class` 直接返回该类记录。
- `0x4CB470 COrderDlg_get_message_map` 直接返回 `message_map_COrderDlg`，补齐了 `COrderTurnDlg` 的基类映射访问链。

## 子系统导航图（第一轮）

```text
旬命令 UI
  runtime_class_COrderTurnDlg 0x60FBE0
    -> message_map_COrderTurnDlg 0x60FC40
       -> base message_map_COrderDlg 0x60A400
       -> handler 0x5D0E70 / 0x4FB0F0

季节切换 UI
  runtime_class_CSeasonChangeAnimeDlg 0x618C58
    -> message_map_CSeasonChangeAnimeDlg 0x618CB8
       -> 2 handlers (当前均为 nullsub_67)

地图视图
  runtime_class_CSan9MapView 0x6160D0 (create 0x534DC0)
    -> message_map_CSan9MapView 0x616160
  runtime_class_CSan9MapUnitData 0x615E70

记录列表对象
  CCityList / CForceList / CPersonList / CUnitList
    -> 各自 runtime_class_* 记录
```

这是 MFC 对象和消息分发导航图，不是日期、旬结算或地图渲染的行为规格。日期状态、命令执行和地图业务数据需要继续沿构造者、handler、调用者和全局根闭合。

## 推断

- `COrderTurnDlg` 是旬/回合相关命令 UI 的高置信度入口，因为类名、继承链和消息表均已机械闭合；“该类实际承载哪些命令”仍需 handler 行为证据。
- `CSeasonChangeAnimeDlg` 很可能是季节切换表现层对象；当前表项为 `nullsub_67`，因此不能把它当作日期状态转换函数。
- `CSan9MapView` 与 `CSan9MapUnitData` 是地图显示/单位表现层的可靠入口；它们与 `CSan9MapData` 的关系不自动证明世界状态归属。

## 待验证

- `0x4FB0F0` 以及 `0x5D0E70` 的消息 handler 参数和实际状态变更；尤其不能仅凭消息号 `0x205` 宣称其为旬推进。
- `COrderDlg`/`COrderTurnDlg` 的创建、销毁和命令对象输出链；当前只闭合运行时类与消息映射访问器。
- 季节/日期核心状态的全局根、初始化、递增和存档读写位置。
- `CSan9MapView` 创建函数 `0x534DC0` 与地图状态根、重绘入口和单位表现对象之间的完整生命周期。
- 四个列表类对象的字段布局和它们与 `San9ForceRecord`、`San9PersonRecord`、城市/部队表的边界。

## IDA 写入与验证

- 新增函数名：8 个运行时类访问器、2 个消息映射访问器；没有覆盖已有用户命名。
- 新增函数注释均带 `RE-SAN9PK-0040`；没有修改函数边界、指令字节、调用约定或产品代码。
- `idb_save` 成功写回 `research/analysis/San9PK-TC-101.i64`。
- 保存后 IDA Python 逐项回读 10 个函数名称和函数注释，全部与预期一致。
- `health_baseline.py` 连续两轮稳定：核心摘要 `FF8421B6943002AB6A3A4F8BBA82061F7ECF93029A7329C905638EA50A6D13F5`；函数总数 10,915、命名函数 743、函数注释 316、反编译失败 7、可执行段未定义字节 0。
- 运行时类、消息映射、全局根和虚表候选导出均各连续两轮稳定；对应摘要分别为 `7EF1BC2F9C6A35D999A532EE169B99BF30F10A001EF62CDCE85AD0AFBC351FA0`、`4F26CD11CD0CA91B1260A8E78CA6D52C3B200B86956E4A77587275BB448F4B85`、`0ECB63D9D2B60DD480A9AD43A5F0BCD2508B30FE4D111C3D392FADA5A6D9AA36`、`EFE06740B389D1BB3295DAE1BDAD35D39E0DC7A11D79230434CA28C81EABF900`。

## 限制

本记录只扩充 IDA 导航底座。类名和消息号来自程序中的运行时目录/消息表，只能证明对象分发关系，不能替代产品行为规格；成熟规则仍需进入 `BEH-*` 或设计决策记录后才能作为实现依据。
