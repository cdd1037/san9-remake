# G04 人事与治理推进记录

2026-09-12：G04进行中，尚未达到阶段退出条件。已接入搜索人物、普通登用、多人调动、军师任命与褒美；宝物、放逐、俘虏扣押与忠诚周期、负责人带动、一般官职和军团治理仍待完成。

## 已有运行行为

人物按势力ID保存0–255忠诚、义理和独立褒美旬标志；现属忠诚为派生值。`ForceState.AdviserId`与军师身份双向校验，任免比较当前有效智力，保留旧军师忠诚的条件随机反馈，不改一般官职。搜索和登用的特殊政治补偿由内容属性表达，不在Core中引用原版人物编号。

本轮审计修正褒美扣费为命令势力资金，移除军师任命对通常在职身份军团长的额外排除；两类即时命令保留当前人事出行。发现转在野补置已行动，发现和登用均记录身份起始年`StatusStartYear`，不把该字段误称为第一次仕官年份。Importer、内容、运行时、存档和在职年数查询已同步更名。依据为[RE-0112审计复核](../research/findings/RE-SAN9PK-0112.md)，未启动原游戏。

`SearchPersonsCommand`、`RecruitOfficerCommand`、`MoveOfficersCommand`通过现有持续任务内核执行；`AppointAdviserCommand`和`RewardOfficerCommand`即时生效。`PersonnelTransitions`统一处理发现、归顺和调动，并清理执行者及指向该人物的相关任务。首个登用成功会取消其他针对同一人物的任务，不能重复兑现。

任务明确区分服务驻地和出行目的地。驻地承担薪俸与军团关系，出行由任务记录拥有；只读`OfficerJourneyView`返回目的地、目标人物、到达日期和进度，情报查询显示出行状态。调动到达后才改写驻地，失效保留来源。旅行的坐标尺度与倒计时公式已通过IDA对齐，搜索忠诚预增及失败设49已修正；完整往返与事件生命周期仍未闭合。见[地图坐标约定](map-coordinates.md)与[实现依据审计](g04-implementation-audit.md)。

普通登用在提交时保存随机值，到期重算当前政治、忠诚、信望和相性。成功设置新归属与忠诚，清旧任务和官职，并重算受宝物/人物归属影响的信望。失败保持身份/归属/位置，并按DEC-PERSON-0001分别更新招募忠诚和原属忠诚。当前明确拒绝俘虏、负责人、军团长及正在旅行的登用目标，等待相应完整跨域分支。

人物搜索只处理人物发现与即时登用，不能替代原作完整探索事件调度器；宝物、资金和历史探索事件尚未接入。当前未新增菜单或主动人事AI。军团长跨军团调动明确拒绝，待治理规则接入；军师属于势力职务，可在势力内部正常调动。

## 内容与研究证据

190年源数据与当前包核对：700名保留人物、25势力、17,500项忠诚、18名军师；特殊招募政治属性映射到人物621。250个源地点记录中187个有有效坐标，其中94个已建设据点、93个未建设地域；63个双无效坐标槽（源ID187–249）在Importer入口排除，不生成地域名称、所属城市映射或旅行地点，保留对象不重新编号。人物、120项宝物状态和40,000个地图格均未引用这些槽，文本目录无残留地域键；审计见`local/generated/verification/roadmap-g04/region-filter/region-audit.json`。所有已保留人物的独立地域引用均有坐标，地点身份集合和坐标逐项一致。建筑是否可驻人由内容能力表达，与名单显示标记分离。

源忠诚、义理、特殊属性、军师和坐标审计以及状态/事件比较见`local/generated/verification/roadmap-g04/personnel-190/comparison.json`。普通公式与搜索阈值由项目主IDA库只读复核，见[RE-0067](../research/findings/RE-SAN9PK-0067.md)、[RE-0110](../research/findings/RE-SAN9PK-0110.md)和[RE-0111](../research/findings/RE-SAN9PK-0111.md)。没有新增原游戏动态实验。

当前剧本schema为16，规则版本`san9-core-g04-v4`，存档/命令日志格式8；录像格式2、摘要版本1。190年包已原位重新转换，旧包与旧规则存档拒绝，不提供兼容分支。当前内容指纹为`7B7834606C50CC1D52791CF808F2913B39C08294858144FD45A5A2F843AC3024`。

## 实际验证

以下结果仅证明当前原型的确定性、约束与保存重放，坐标与倒计时公式另有IDA证据，不证明完整探索或调动往返生命周期已对齐；规则修复后须更新脚本预期与验收证据。

- 完整解决方案552项通过：Core167、Content33、Importer183、Application66、Client94、Headless9。未启动游戏；情报显示改动采用查询测试与客户端编译验证，画面待后续验收。
- 公开场景通过搜索发现→登用→调动→军师任命→褒美，连续一年、12个恢复点、最终存档字节和录像重放一致。
- Core覆盖实时阈值、49/50及99/100忠诚门、当前政治与特殊补偿、有符号除法、失败反馈四种组合、义理及身份排除、随机消费、多人不同来源、目标失效、途中改属、竞争取消和畸形任务恢复拒绝。
- 审计回归覆盖根势力钱不足而军团有钱时拒绝、军团空库而势力够钱时成功、军团长任命保留军团引用、在途任命/褒美保留任务并能恢复到达，以及发现当日已行动和身份起始年。最后同步schema夹具后单独重跑Content33项通过。
- 190年现有CLI运行45项操作、36旬：6项出行任务，人物发现1、普通登用失败2/成功1、调动2、军师任命1、褒美2。保存点在首轮推进前，同时有3个已提交的在途任务；恢复跳过已处理的5项操作后继续40项，最终存档与连续执行、录像重放逐字节一致，事件也一致。最终存档654,701字节。
- 190年连续运行3,784 ms，恢复续跑2,846 ms，录像重放4,487 ms；恢复续跑包含完整36旬。没有欠薪/资源短缺。此脚本不下经营命令，不作为完整战略AI或全部G04验收。
- 领域契约检查仍有既有`BEH-UI-0002..0006`未分类问题（G13范围），不能记为通过。

本地证据位于`local/generated/verification/roadmap-g04/personnel-190/`：`commands.jsonl`、`continuous-report.json`、`checkpoint-report.json`、`restored-report.json`、`replayed-report.json`及相应事件、存档和录像。生成物不入库。

公开复现使用`dotnet test San9Remake.slnx`。已有本地日志的190年复现：

```powershell
dotnet run --project src/San9.Headless -- run --scenario local/generated/content/scenario-190-live/scenario.json --log local/generated/verification/roadmap-g04/personnel-190/commands.jsonl --report local/generated/verification/roadmap-g04/personnel-190/continuous-report.json --save local/generated/verification/roadmap-g04/personnel-190/continuous.save.json --record local/generated/verification/roadmap-g04/personnel-190/continuous.recording.json
dotnet run --project src/San9.Headless -- replay --scenario local/generated/content/scenario-190-live/scenario.json --recording local/generated/verification/roadmap-g04/personnel-190/continuous.recording.json --report local/generated/verification/roadmap-g04/personnel-190/replayed-report.json
```

下一人事批次优先闭合去返程、行动释放与取消后的安置；完整探索交互、宝物转移、放逐与归属清理，以及军团设立、任免、辖区、资源守恒和共享控制权限按依赖分批。具体退出条件见实现审计末节。新增军团时须同步替换当前存档校验中的初始军团身份集合限制；负责人归顺不能只改人物而遗漏据点和军团迁移。

坐标修复后核对187个地点身份及源格坐标；94据点与地图建筑锚点一致，93未建设地域的精细中心逐项满足5*x+2。190年无活动野战部队，部队精细位置保存由公开测试覆盖；记录见local/generated/verification/roadmap-g04/coordinate-alignment/source-audit.json。
