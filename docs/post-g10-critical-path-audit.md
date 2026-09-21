# G10 后关键路径核查与修复（2026-09-21）

核查基线为 `5e56b8e`，G10 收口为 `f35303d`。三处跨阶段遗漏均已修复，并加入正式回归测试。用户已有的治理规格修改保持原样。

## 控制轮次与动态势力

原问题：季首起兵／自立追加控制席位后，`ActiveControl` 仍指向旧集合末尾，后续修学、联盟或国号选择无法通过待答状态校验，阻断旬推进。

[ControlRotation](../src/San9.Core/Simulation/ControlRotation.cs) 统一维护创建、激活与退出势力时的控制游标。已结束的轮次继续保持结束；尚在下令时保留下一个未完成的存续席位；删除当前或此前席位不会跳过下一势力。新一轮仍由前置流水线完成后统一开启，不放宽事件等待校验。

回归覆盖实际空城起兵／城市自立后修学报名、创建势力后的联盟和国号选择、回答续行、稳定恢复及退出当前／此前／此后席位。测试入口为 `ForceEmergenceTests`、`AllianceFormationTests`、`PoliticalEventsTests`、`ControlRotationTests`。

## 灭亡势力的野外人员安置

原问题：灭亡清理先清空在队武将的 `UnitId`，随后删除部队时已经无法找回成员的地理位置，产生无法迁移、求官或被探索的无位置在野人物。

[BattleAftermath](../src/San9.Core/Simulation/Combat/BattleAftermath.cs) 在清除部队引用前，通过共享战败安置链处理野外成员：从部队实际地域选择公共安置目的地；候选为空时保留原地域，更新在野身份起始年。部队中剩余俘虏走共享部队终止链，按自身归属返程或安置，不再统一抹掉其归属。原势力被敌方拘押的俘虏仍保留拘押位置及身份年份，只清旧归属。在野人物没有任何地理位置时不允许提交世界状态。

回归覆盖君主与普通成员、有／无邻接安置地点、服务据点与实际地域不同、敌方拘押与己方携带俘虏、最后城市占领／归附及保存恢复。测试入口为 `SettlementDefectionTests`、`MilitaryCombatTests`、`ControlRotationTests`。

## 旬后置行动恢复

原问题：普通旬更新过早清除人物行动标记，后续求官接受、起兵／自立又写入已行动，进入下令阶段后额外禁用一旬。

[XunRecovery](../src/San9.Core/Simulation/XunRecovery.cs) 分开据点使用次数和人物行动／褒美恢复：前者仍在普通旬更新，后者在 `XunCleanup`。持续任务仍是跨旬占用的权威来源，清行动标记不会释放旅行或修学任务。季度修学报名读取本季驻留与任务资格，不使用尚待清除的上一旬行动标记；与 [RE-0131 的驻留及教学资格](../research/findings/RE-SAN9PK-0131.md#quarterly-study-enrollment) 一致。

回归覆盖旬初求官后立即提交探索、起兵／自立成员行动恢复、上一旬已行动者进入新季修学、报名后持续任务阻止再次下令。测试入口为 `OfficeRequestTests`、`ForceEmergenceTests`，既有人事返程、修学中断和保存／录像测试同时通过。

## 验证与交付边界

执行 `dotnet test San9Remake.slnx --no-restore --verbosity minimal`：3198 项全部通过，分别为 Core 2223、Application 444、Content 74、Importer 343、Godot 98、Headless 16。完整输出位于忽略目录 `local/generated/xun-fixes-solution.log`。正式回归测试不依赖私有样本或审计临时探针。

规则版本为 `san9-core-g10-v47`；保存与内容结构未变，旧规则存档／录像明确拒绝，现有内容包无需重转。未启动游戏，未进行新一轮原版动态实验；此轮是领域逻辑修复，无新增界面。

核查另确认：据点／野外归附已接入旬更新；城市普通攻击内政损失已有生产入口；空城自然衰减不属于已采用原版规则；月事件空表属于内容事实；完整 AI／自动工作表及投降设计讨论仍按既定阶段与范围处理。
