# G04 人事与治理验收

2026-09-12：G04退出条件已满足。搜索、登用、调动、任职、奖励与宝物、放逐/释放、归队、忠诚周期和军团设立/变更/解散/控制通过公共命令闭合；一年真实经营、保存恢复及重放一致。未启动Godot或原游戏。规则依据和明确选择见[实现审计](g04-implementation-audit.md)。

## 退出条件与公开验证

| 退出条件 | 实际覆盖 |
| --- | --- |
| 搜索→登用→调动→任职→奖励 | `PersonnelScenarioTests`公开年度链；`OfficeAssignmentTests`一般官职批量交换/爵位/撤职/派生能力；190年同一发现人物完成全链 |
| 出行中改属/目标失效 | `PersonnelMissionTests`同日竞争、目标位置变更、来源同时破坏、三类占用、归队；`PersonnelReturnTests`释放即时取消登用及归队存读档 |
| 单一归属/位置、宝物唯一 | 统一转换和世界不变量；宝物授予/没收/搜索动态加成及特殊信望、查询/内容/恢复后的派生能力一致；`SearchOutcomeTests`四类终端及随机边界 |
| 负责人和军团离任 | `PersonnelDefectionTests`负责人带对象/依附单位/追随者，拒绝者返回、旧军团合并；`GovernanceDepartureTests`放逐/跨团移动后的接替或解散 |
| 治理资源不复制 | `CreateCorpsTests`、`ChangeCorpsTests`、`CorpsGovernanceTests`完整金粮转移、溢出原子拒绝、根保护、实时都督排序；服务单位分类由Importer及Core夹具验证 |
| 玩家/自动权限一致 | `CorpsControlTests`根/子控制切换、旧上下文拒绝、直属范围、实际自动登用和经营、收回与保存重放 |
| 忠诚演进 | 俘虏旬扣减及49/50/51季度回归；开局30天门、所属项排除、跨年与重放；俘虏登用/释放由公开夹具覆盖 |

完整解决方案`dotnet test San9Remake.slnx --no-restore`通过723项；随后增加3项能力派生测试，Core282项定向重跑通过，其余通过结果复用。最终合计726项：Core282、Content33、Importer225、Application83、Client94、Headless9。新增验证覆盖单人/批量一致、宝物逐项最大加成、同事务状态变化、旧快照隔离及序列化不含派生字段；任免、授予、没收、放逐和读档使用真实定义与持有关系验证。客户端验证是编译和查询测试，不是画面验收。

## 内容与数据核对

当前剧本初始态schema23，规则`san9-core-g04-v16`，存档10、日志9，录像2，摘要1。G04接入一般官职四项能力与爵位要求、军团控制、归队任务及探索选择；同步Importer、Content、Core、Application、查询、存档和日志。本轮移除人物宝物/官职加成副本，由当前状态统一派生有效能力，内容初始态和存档形状同步更新；命令与录像格式不变。旧包/旧规则明确拒绝，不引入兼容分支。

190年正式剧本在`local/generated/content/scenario-190-live/`原位转换，指纹：

`F52AF21CC8560F579F2BB37DB9FDA9DCDA03534C4FD6F7ABAF8589B446472D23`

源→内容→运行时→查询核对700人物、25势力、17,500项忠诚、76可任官职、25根军团。官职能力按原四项掩码独立核算，薪俸/带兵量/爵位要求和人物现职一致；直接从源宝物持有人、能力类别、官职掩码、基础能力及伤势独立计算可见人物能力，与运行时查询逐项核对。原剧本25根记录玩家位均未置位，重制开局配置选择势力5；根军团Direct由势力席位决定人/机，子团控制单独映射，未把源默认位冒充用户选择。结果为`complete-190/input-audit.json`。

当前包94据点、93有效未建设地域，保留187项地域归城与208条有向城市邻接；双无效坐标槽在Importer排除，人物/宝物位置不重排。此前地理源核对见`personnel-190/geography-audit.json`。源800单位槽均未启用，当前导出0单位；服务位置及单位类型/命令/异常状态的迁移资格由225项Importer测试及公开单位夹具覆盖，不能以190年无单位声称实战部队已验收。地图建设改变港口时更新邻接属于G05/G07。

## 190年年度验收

产物均为忽略的本地生成物：`local/generated/verification/roadmap-g04/complete-190/`。场景驱动器`local/generated/g04-audit/Program.cs`只使用现有GameSession和保存/日志/录像API，无私有运行时或旁路状态修改。种子190，190-01-01至191-01-01，共36旬。

- 人物526由搜索发现，独立登用后调到17号城市、任一般官职并褒美；搜索预声明拒绝即时登用，确实经过独立招募命令。另有宝物授予/没收与放逐。
- 创建动态军团50，变更辖区及资金，自动工作后解散；根军团移交两旬并收回。25个原势力保持，最终25军团，无身份槽复用假设。
- 1,849项会话操作，928项领域命令；894次经营：商业147、开垦148、巡察301、征兵4、修筑147、训练147。实际自动军团也执行登用和经营，不以等待冒充动作。
- 登用6次（成功3、失败3），18项出行任务、10次归队；14次磁盘保存恢复。连续/恢复存档逐字节一致，所有步骤状态和事件一致，录像与命令日志重放一致；重复前缀1,849项不产生事件、不改状态。
- 独立Headless `run`接受全部1,849项，报告无首个失败。1,192资金短缺全部是90笔薪俸未足额支付，按既有G03规则记账；收入饱和损失0。16次等待和资源不足保留为真实受限结果，有限策略不承诺每个势力始终富足。
- 年度驱动、连续/恢复及两种重放合计113,208 ms；独立CLI命令回放32,580 ms，独立CLI录像回放27,036 ms；两者事件日志及四份最终存档逐字节一致。该时间是本机测量，不是G12性能门槛。
- 普通存档573,526字节；命令日志396,001字节；显式录像5,159,803字节、事件日志4,018,585字节。普通存档不累计完整操作历史。

最终会话哈希：`F2A3827103769F602DFA165AA0DFEFDCE9FCB89453CDA28A79038A4986EB7134`。

主要机器证据：`verification.json`、`input-audit.json`、`checkpoints.json`、`commands.jsonl`、`verification.recording.json`、`continuous.save.json`、`restored.save.json`、`cli-report.json`、`cli-report.events.jsonl`、`cli.save.json`、`cli-replay-report.json`及`cli-replay.save.json`。重放所需私有剧本及源资源不入库。

复现现有日志的入口：

```powershell
dotnet run --project src/San9.Headless -- run --scenario local/generated/content/scenario-190-live/scenario.json --log local/generated/verification/roadmap-g04/complete-190/commands.jsonl --report local/generated/verification/roadmap-g04/complete-190/cli-report.json --save local/generated/verification/roadmap-g04/complete-190/cli.save.json --record local/generated/verification/roadmap-g04/complete-190/cli.recording.json
dotnet run --project src/San9.Headless -- replay --scenario local/generated/content/scenario-190-live/scenario.json --recording local/generated/verification/roadmap-g04/complete-190/cli.recording.json --report local/generated/verification/roadmap-g04/complete-190/cli-replay-report.json --save local/generated/verification/roadmap-g04/complete-190/cli-replay.save.json
```

## 后续边界

G04是无界面经营底座，不是战争可玩。G05/G06接部队寻路、出征、俘获、势力退出及相关君主安置；G07接拘押期间的旬周期出逃（当前未实现，区别于主动释放和战败逃脱）；G09/G10接自立/新势力忠诚初始化、历史探索和实时选项会话；G11扩充定向委任、自动调配和完整战略AI。已保存的基础单位分类、权限和人事任务在后续阶段复用。UI菜单、动态表现和视觉验收尚未进行。
