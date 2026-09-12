# G06 普通战斗与攻城占领验收

日期：2026-09-12。状态：G06完成；下一阶段G07尚未启动。

## 运行闭环

公共`DeployUnitCommand`与`OrderUnitCommand`支持明确的部队/据点攻击目标。日地图阶段先行军，再按稳定顺序处理普通攻击及反击，共用每日机会、矩形距离和同一交战前快照。`CombatMath`使用精确整数计算兵力、耐久及伤兵损失；`MilitaryCombat`应用损失，`BattleAftermath`统筹占领、人物、单位终止与治理。全部修改在候选世界内验证后原子发布。

零兵、零士气的部队被统一终止；据点兵力、士气或耐久任一归零进入失守。显式销毁能力决定移除战场占用或保留并重建；同一日竞争者不能再次占领或攻击新所有者。人员处理覆盖阵位、附近参与者、实际名马、可达退路、俘虏携带与释放、双败安置、在途任务归队和隐藏身份保留。军团负责人和账户统一修复，末资产/末在职人员失去后势力退出。普通和异民族人员/宝物清理有独立场景。最小场景终止消费退出事件，停止日期、任务与财政更新。

规则与重制选择见[普通战斗](../specs/behavior/BEH-WAR-0004-normal-combat.md)、[占领](../specs/behavior/BEH-WAR-0002-building-defeat-and-capture.md)及[终止清理](../specs/behavior/BEH-WAR-0003-unit-return-defeat-and-force-cleanup.md)。守军系数、普通近战距离、顺序接收和无阵位舰船的中排系数均为明确重制选择，不宣称复刻原作完整兵法/阵型差异。

有限军事策略经公共验证选择进攻或返城，保留守军和行政人员、避免重复派往同一目标，并在静态无路时等待；不读取敌方兵力或隐藏野战位置。公开会话完成经营→征兵→策略出征→占城→终局，另一个场景由自动势力主动进攻直至玩家退出。两者均验证录像，前者额外逐旬对照存档恢复；终局保存恢复后继续推进没有副作用。

## 内容、状态与查询

当前初始态schema26、组合包schema27、规则`san9-core-g06-v2`、存档14、日志12、录像3、摘要1。旧规则/缺少必填字段的保存与内容明确拒绝，需原位重新转换。

TC101实际190年包已重转并逐项核对：28项阵型四类战斗系数及五个阵位、94据点显式守军参数及销毁能力（7处可销毁）、100项内置宝物定义中的6匹名马。源800单位槽均未启用，初始保留0活动单位；不能用此证明非零单位伤兵和成员数据，相关状态以公开夹具验证。军事拓扑仍为40,000格、17地形及14,791硬阻挡。原始核对位于忽略的`local/generated/verification/roadmap-g06/battle-190/input-audit.json`，驱动器为`local/generated/g06-audit/Program.cs`。

伤兵、成员阵位、类型化目标、势力消亡和据点毁坏是权威世界状态，进入保存与摘要；名马能力和军事定义进入内容指纹。只读视图提供据点伤兵、毁坏和势力消亡状态。对象静态定义及身份继续保留，可用性读取当前世界；客户端运行时不读取原版文件。

## 实际验证

完整`dotnet test San9Remake.slnx --nologo`：843项通过（Core380、Content35、Importer228、Application96、Client94、Headless10）。核心战役场景覆盖三种失守门、反击机会、双败、退路/返城点丢失、第三方争夺、军团解散、名马、在途人物、隐藏身份、异民族和宝物；原有伤害/概率/资源算术、保存与行军测试一并通过。

实际190年通过公共命令从99号据点向10号据点派27,000兵；全剧本自动控制同时推进。一旬记录9次出征、48次日移动、8次输送/返城吸收、14条普通伤害结算，零资源短缺和容量损失；本次没有占领或势力消亡，不能将真实地图接入检查当作190年完整战争或长期性能验收。连续运行、磁盘恢复、录像和独立Headless重放一致，最终存档文件SHA256相同。连续/恢复/录像合计5,326 ms，独立CLI重放2,327 ms。

内容指纹：`A443EB2D56276442E201E9C55479BC34AC6CE1C2CADAD8CD7B1C57F36A25AE9B`。最终会话摘要：`65158D81B0EA0627649C21A5DEB7B532E8F37755C133E6B0F51C8A5DD1BF3F29`。原始事件、存档、录像和CLI报告在忽略的`local/generated/verification/roadmap-g06/battle-190/`。

```powershell
dotnet run --project src/San9.Headless -- replay --scenario local/generated/content/scenario-190-live/scenario.json --recording local/generated/verification/roadmap-g06/battle-190/battle.recording.json --report local/generated/verification/roadmap-g06/battle-190/cli-report.json --save local/generated/verification/roadmap-g06/battle-190/cli.save.json
```

契约JSON与运行入口同步。全局契约检查仍有G05记录的人物章节、培养草案和UI分类既有问题；不记为通过，不借战争任务修改无关规格。未启动游戏，客户端仅完成必要编译与查询回归，画面和交互留待表现层验收。

## 后续边界

G07负责完整阵型/兵法差异、设施建设及未完成建设物清理、陆水转换、补给、伤兵恢复和俘虏周期；G08接入联盟与外交禁战，G09接入完整人物继承，G10完善胜负/结局，G11完善战略AI，G12验收长期对局。当前普通攻击距离及有限策略不是这些阶段的完成证明。进入G07前按roadmap确认阶段提交完成且Git干净。
