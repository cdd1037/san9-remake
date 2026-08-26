# RE-SAN9PK-0048：场景世界状态根与候选对象布局（已由 0072 裁决）

- 状态：历史铺底；存储形式与生命周期已由 [RE-SAN9PK-0069](RE-SAN9PK-0069.md) 纠正并闭合
- 日期：2026-08-19
- 分析者：Codex
- 结论类型：事实、推断和待验证项分开记录
- 置信度：高（全局传参形式与数据项冲突）；中（候选构造族身份）
- 关联来源：`SRC-SAN9PK-TC-101`
- 关联记录：`RE-SAN9PK-0039`、`RE-SAN9PK-0041`、`RE-SAN9PK-0047`

> **后续裁决（2026-08-23）**：本记录把 `0x1232688` 解释为“读取其中保存的值”并倾向指针/句柄，是对反编译表达的误读。指令级复核确认 218 个有效引用直接执行 `mov ecx, offset 0x1232688`；`0x5FAC70` 又以该地址调用构造函数并向 `_atexit` 注册同一实例的析构包装器。该地址是大小 `0x78` 的静态内联 `scenario_world_state` 对象，不是指针或句柄。下文保留当时调查过程；涉及存储形式、对象边界和生命周期的结论一律以 `RE-SAN9PK-0069` 为准。

> **SCN-01 收口（2026-08-26）**：该对象在新局载入中的重置、头部直接写入、载入后派生和读档分支顺序见 [RE-SAN9PK-0140](RE-SAN9PK-0140.md)。

## 样本与工具

- 样本：繁体中文版 1.0.1.0，32 位 PE，2,636,800 字节，SHA-256 `D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`。
- 工具与数据库：IDA 9.3、Hex-Rays、IDA MCP（health `ok`）；项目主数据库 `research/analysis/San9PK-TC-101.i64`。

## 目的与范围

沿 `reset_scenario_runtime_state(0x453DA0)` 收窄 `scenario_world_state_root` 的存储边界，并记录一个候选外层对象构造/析构族与 `CForceData` 的布局关系。这里只恢复导航事实，不把全局根与候选对象实例强行等同。

## 已确认事实

- **历史误判（已作废）**：当时把 `mov ecx, offset 0x1232688` 误读成读取数据项中保存的值，因而命名为 `scenario_world_state_root` 并保留指针/句柄解释。`RE-SAN9PK-0069` 已用指令和静态初始化链确认这里传递的就是地址本身。
- **历史误判（已作废）**：当时把 IDA 的 20 字节数据项切分当成对象边界证据。后续重置函数的 `+0x76` 写入和独立 `config(0x1232700)` 起点共同确认真实对象大小为 `0x78`；中间地址名是字段历史名，不是相邻独立全局。
- `scenario_world_state_ctor(0x46ABC0)` 将对象首字段设为 `vftable_scenario_world_state(0x6065F0)`，在 `this+0x38` 调用 `CForceData_ctor(0x46DF70)`，随后调用 `reset_date_world_state_fields(0x46AB30)`。后续恢复的 `initialize_static_scenario_world_state(0x5FAC70)` 已把该构造函数与 `0x1232688` 静态实例直接连接。
- `scenario_world_state_dtor(0x46A560)` 写回同一 `vftable_scenario_world_state(0x6065F0)`，再在 `this+0x38` 调用 `CForceData_dtor(0x46DF90)`；构造/析构的虚表与成员偏移对称。
- `CForceData_ctor(0x46DF70)` 使用 `runtime_class_CForceData(0x605C2C)` 并写入 `vftable_CForceData(0x606AA4)`；`CForceData_dtor(0x46DF90)` 恢复同一虚表并调用公共 `CGameData` 销毁路径 `0x473050`。
- `reset_date_world_state_fields(0x46AB30)` 访问传入对象的 `this+0x04..0x76` 字段，并重新初始化 `this+0x38`；若该对象与上述构造族相同，`this+0x38` 是构造/析构可观察的 `CForceData` 成员边界，但这个实例映射仍待验证。

## 推断

- 原“更可能保存指针或句柄”的推断已经证伪；`scenario_world_state` 是静态内联对象。
- 构造族的外层类型不等同于 `CForceData`，而是包含一个从 `+0x38` 开始的 `CForceData` 成员；静态初始化器已确认该类型就是 `0x1232688` 实例。
- `vftable_scenario_world_state` 的身份已由构造/析构对称性确认，但它对应的正式类名、虚表完整边界及每个槽位语义仍未机械闭合。
- 该根可作为后续日期、存档、模拟、势力关系和 AI 数据流的共同导航点；它不能单独证明任何具体玩法规则。

## 待验证

- `scenario_world_state` 的存储形式、初始化和释放路径已由 `RE-SAN9PK-0069` 闭合；完整字段读写矩阵仍待建立。
- 候选外层对象的完整大小及 `+0x00..+0x37`、`+0x3C` 之后字段布局，以及它与全局根的实例关系。
- `0x6065F0` 虚表的完整边界、每个槽位的接口签名，以及该类型是否存在独立运行时类记录。
- 候选构造函数的静态或间接调用点；当前 IDA 交叉引用只直接闭合了构造体内的虚表/成员操作。
- `scenario_world_state` 与存档读写、日期推进、模拟任务队列和势力/AI 入口之间的所有权及生命周期关系。

## IDA 写入与验证

- 初次命名 `scenario_world_state`、`vftable_scenario_world_state`、`scenario_world_state_ctor`、`scenario_world_state_dtor`、`vftable_CForceData`、`CForceData_dtor`；所有命名先 dry-run 检查冲突，再写入。构造族名称是待实例关联验证的工作名。
- 复核后将 `0x1232688` 从 `scenario_world_state` 修订为 `scenario_world_state_root`，并写入边界说明；保存和回读成功。
- 在相关全局、虚表和函数处追加 `[RE-SAN9PK-0048]` 证据注释；未修改函数边界、指令字节或产品代码。
- 每次写入后保存并逐地址回读名称/注释，均与预期一致。
- 验收重排时将项目主 IDB 中 9 处本记录注释同步改为 `[RE-SAN9PK-0048]`；无失败项，保存后旧编号回读为 0 处。

## 限制

本记录只收窄世界状态根和候选构造族的边界，不构成世界字段规格、日期结算、AI、外交或战斗行为规格；产品实现仍需等待相应的已确认行为记录。
