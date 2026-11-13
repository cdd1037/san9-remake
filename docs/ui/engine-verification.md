# Godot 引擎交互检查

以下入口按受影响功能选择，需先构建客户端；有内容参数的检查复用现有转换包。它们验证控件、输入和生命周期，不代表画面、试听或完整玩法验收。历史画面脚本见[画面验证](client-verification.md)，通用验证取舍见[测试说明](../../tests/README.md)。

2026-11-10 使用隔离用户目录与本机升级内容包，在 1024×768 客户区打开研究、制造、出征阵形、分队兵种及武将阵形／装备页面，核对名单、状态、提示和逐层返回。研究不可用提示位于屏幕中央；装备操作名单发现底部提示与武将情报提示重叠，已隐藏被覆盖页面的提示，空物品名单增加说明。修复后重启游戏，复核装备名单和操作名单的底部提示不再重叠；客户端测试与装备控件诊断通过。研究开始确认、己方装备变更和常用编队样式仍待实际画面验证。

同日使用隔离存档和已有旧内容包检查普通装备购买：军事菜单可打开购买名单，实际购买长枪后资金从 18976 减至 18576；修复并重启后核对“购买”页签、旧预设的长枪等新名称和居中确认框。上述检查不替代完整装备、个人爵位与阵形教学验收，实际购买仅发生在隔离会话中。

资料编辑器保存刷新可用无资源引擎检查验证，覆盖保存不重建当前表单／不丢失控件输入，以及隐藏页切回时刷新；不代表画面验收：

```powershell
godot --headless --path src/San9.Client.Godot res://Diagnostics/Verification/SharedEditorRefreshVerification.tscn
```

成功输出`SHARED_EDITOR_REFRESH_OK`并退出。

武将阵形页检查设置 `SAN9_OFFICER_FORMATION_CONTENT` 为已准备的剧本内容目录，运行 `godot --headless --path src/San9.Client.Godot res://Diagnostics/Verification/OfficerFormationVerification.tscn`。检查在启用兵种与研究的真实会话中打开武将详情的阵形页，核对阵形名称及熟练度，成功输出 `OFFICER_FORMATION_OK`；这不代表实际画面验收。

武将装备页检查设置 `SAN9_EQUIPMENT_CONTENT` 为已准备的剧本内容目录，运行 `godot --headless --path src/San9.Client.Godot res://Diagnostics/Verification/OfficerEquipmentVerification.tscn`。检查打开武将装备窗口、九个槽位和从隔离会话库存装备长枪的命令；成功输出 `OFFICER_EQUIPMENT_OK`，不代表实际画面验收。

联携关系表检查设置`SAN9_TACTIC_LINK_CONTENT`为现有内容根目录，运行`godot --headless --path src/San9.Client.Godot res://Diagnostics/Verification/TacticLinkEditorVerification.tscn`，可验证联携关系表的初次显示、按人物筛选、选中修改、删除及搜索选人后添加。仅改变内存草稿，不发布玩家资料；成功输出`TACTIC_LINK_EDITOR_OK`，不代表画面验收。

涉及工作流窗口或地图回调生命周期时，可在客户端编译后运行无资源依赖的引擎检查（将 `godot` 替换为本机 Godot .NET 可执行文件）：

```powershell
godot --headless --path src/San9.Client.Godot res://Diagnostics/Verification/WorkflowLifecycleVerification.tscn
```

成功输出 `WORKFLOW_LIFECYCLE_OK` 并以 0 退出；覆盖窗口锁、退出顺序、地图回调所有权，以及模态窗口重开、嵌套、初始化期间创建子窗口、动态控件、原始输入隔离、Tab焦点约束、关闭时排除退出控件的焦点恢复和祖先返回路由。`COMPANION_DRAFT_OK`另验证伴侣面板待提交数值、隐藏页预设与重置、无变化不写入及保存失败后的草稿保留；不代表画面或完整命令交互验收。

设置`SAN9_OPENING_CONTENT`为已有转换内容根目录（含281年剧本），运行同一headless入口可单独检查真实开局控件：用未完成的读取任务核对首帧禁用状态，再检查悬停转移焦点、禁用控件不抢焦点及数据就绪后的部署状态。成功输出`OPENING_CONTROLS_OK`并退出；该模式不运行其他工作流检查，不创建游戏世界或操作桌面窗口。例如PowerShell：

```powershell
$env:SAN9_OPENING_CONTENT = (Resolve-Path local/generated/content).Path
godot --headless --path src/San9.Client.Godot res://Diagnostics/Verification/WorkflowLifecycleVerification.tscn
Remove-Item Env:SAN9_OPENING_CONTENT
```

设置`SAN9_MODAL_CONTENT`为已有转换包目录后，同一入口另检查真实环境设定的保存失败提示，以及终局标题菜单到存档行的鼠标选择和读取回调，成功时追加`MODAL_CONTENT_OK`。检查使用独立存档目录`local/generated/verification/modal-content/`，不读取用户存档；不创建真实战败事件，也不替代画面验收。例如PowerShell：

```powershell
$env:SAN9_MODAL_CONTENT = (Resolve-Path local/generated/content/scenarios/historical/scenario-0).Path
godot --headless --path src/San9.Client.Godot res://Diagnostics/Verification/WorkflowLifecycleVerification.tscn
Remove-Item Env:SAN9_MODAL_CONTENT
```

设置`SAN9_UNIT_GROUP_CONTENT`为已有转换包目录，运行`WorkflowLifecycleVerification.tscn`可单独检查多选常驻名单：实际鼠标点击刘备行后保留其余行对象，检查名单／滚动视口／条目的边界、独立选择状态、隐藏恢复及重复刷新，并通过原有皮肤的“决定”按钮提交重叠名单临时选择。成功输出`UNIT_GROUP_LIST_OK`；有图形设备时同时保存`local/generated/verification/unit-group-list.png`，无图形设备时仅检查布局与输入。该检查不覆盖完整战略地图下令流程。

设置`SAN9_UI_PREFERENCES_CONTENT`为已有转换包目录后，运行上面的`WorkflowLifecycleVerification.tscn`入口，可检查真实存读档页鼠标翻页／中止共享、情报与都市编辑的地图／一览互通、地图起始时详情候选初始化及仅关闭时统一保存。成功输出`UI_PREFERENCES_OK`；该模式只做本功能的引擎交互检查，不代表画面验收，不读写用户偏好文件。

2026-11-11 使用本机隔离升级内容和仅在内存中构造的虚构爵制，实际检查封爵武将名单、爵级名单、封号输入、确认授爵、名额更新、取消撤爵及不可授予原因。授爵后名单显示所填封号、忠诚由 94 增至 98；取消撤爵后原爵位仍保留。该检查不修改用户存档，不验证历史制度配置、主菜单入口或游戏内完整存读档。

2026-11-11 另用升级后真实内容包、独立用户数据目录，从主菜单进入 211 年曹操剧本并开启兵种与研究，检查许昌任免菜单的封爵入口、武将及爵级名单、名额、爵俸、不可授予原因和封号输入后的取消返回。发现原官职命令仍显示“爵位”，改为“官职”后重新构建并在真实剧本的任免菜单确认显示；该次检查未执行授爵或存读档，也不代表已验收完整历史爵制。

2026-11-11 使用同一隔离内容的 234 年刘禅剧本复验封爵名单：关彝、关索、关统、姜维的内部忠诚超过 100 时均显示 100，低于 100 的武将仍显示实际值。此检查只验证名单显示，不修改内部忠诚或用户存档。

2026-11-11 在独立用户数据目录加载本机升级内容的 234 年剧本，实际打开卑衍的武将情报及装备页，确认主武器短剑、头盔纶巾、铠甲皮甲均为一阶且无基础附加效果，其余槽为空。购买名单对重名的主、副武器附加槽位，初始三件均可直接购买；购买主武器短剑后资金减少 200，隔离存档中有一件归势力库存且配发标记为真，游戏成功重新载入。随后将库存短剑换给陈到、存档并读取后再次存档；两个存档的装备实例总数均为 1116，该短剑实例均由陈到装备。梓潼正常尝试登庸李严遭拒；复制隔离存档并人为提高其对玩家势力的忠诚后，实际登庸成功，三个原先持有但未装备的初始物品均自动装备且待出仕标记消除。制造的完整交互尚未在此局逐项验收。

追加普通装备和坐骑品阶后，用新升级的同一剧本再次打开购买名单，双钩枪显示需要装备锻造；选择后提示三阶、基础武力 70 的要求和本项目设定的加成。该检查未完成研究及实际制造。

2026-11-11 在隔离用户目录的 234 年剧本中，实际选择两名武将进入剧本归属预览：一人显示可匹配的刘禅势力与成都，另一人显示无法对应势力；手动将后者改到曹叡势力的北平后，确认并收到安排成功提示。检查了多选、预览、逐人修改和跨势力提交；未通过该画面验证预约到期、批量修改或存读档。

2026-11-11 在独立用户目录的 234 年剧本中检查新增编辑入口、名单与详情。将刘禅基础信望从 220 预览为 221，当前信望相应从 229 变为 230，取消后恢复。将未发现的七星宝刀转入刘禅库存并提交，存档、载入后装备名单仍显示该库存归属。此轮未在画面中逐项检查所有颜色、关系、装备搭配和等级边界。
