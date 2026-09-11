# G02 保存、重放与无界面运行

验收日期：2026-09-12。G01前置提交为`c7df256`，复用其日历、任务、随机、事务和统一命令终验；G02退出条件已成立。行为依据为[BEH-SAVE-0001](../specs/behavior/BEH-SAVE-0001-session-persistence-replay-and-headless.md)。没有启动游戏或扩展表现层。

## 实际能力与所有权

`ScenarioSessionDefinition`从现有Content剧本创建规则身份与世界；`GameSession.Start/Restore`组织新局和恢复。`SaveService`保存完整世界、控制轮次、任务、命名随机、最近128条内政记录、玩家年表和序号水位；完整历史仅在显式录像中记录。恢复不重复新局准备，先验证身份与引用再构造查询。Content/Importer沿用已有完整剧本集合，没有新增导入字段。

`GameSession.Apply`统一命令、结束控制、推进与年表操作，在候选世界上执行并发布当前状态和水位。会话序号从0开始，另有Core命令序号；Core不保存整局已完成命令ID。当前会话内旧序号返回`AlreadyProcessed`，不执行、不返回旧结果，也不验证旧载荷；未来序号拒绝。外部JSONL在文件头核对规则、内容和种子；本地请求不再重复附加流指纹。拒绝不占用序号。JSONL输入首次失败即停止，接受前缀可保存续跑。

当前存档和JSONL日志版本为3，录像版本为2，哈希编码版本为1，规则版本为`san9-core-g02-v3`。此前G02 v1/v2协议文件明确拒绝，不保留兼容分支。规范化UTF-8 JSON按Ordinal排列对象键，稳定排序无序集合，保留控制和玩家记录等有序列表。世界/会话摘要按需计算，仅供诊断和重放比较，不存入普通快照，也不在恢复时逐层复核。每个外部文件只保留一层完整性校验，包含世界提交版本。G01及早期G02历史摘要不能直接与当前协议比较。

普通玩家存档不包含操作或事件历史，也不包含录像。自动内政记录保留最近128条，玩家手写年表保留。`BeginRecording`在当前稳定边界捕获起点，显式记录后续接受操作、世界摘要与事件；`EndRecording`停止并释放会话内部历史。`RecordingService`把起点、操作、最终会话摘要和完整性摘要写入独立文件，重放从起点恢复；支持有任务和已消耗随机流的中途起点。读普通存档不自动恢复录制，重新开启形成新的片段。

解析拒绝缺字段、额外字段、重复键、非法枚举、空集合元素、截断、未知版本、指纹不符、损坏引用及未知任务规则。文件通过同目录临时文件、flush和原子替换写入；替换前故障不会截断旧文件。没有旧版自动迁移，也不额外承诺硬件断电保证。

## CLI 使用

仅需.NET 10；CLI不引用Godot。生成物保存在忽略目录：

```powershell
dotnet run --project src/San9.Headless -- verify --fixture --xuns 36 --output local/generated/verification/roadmap-g02/public
dotnet run --project src/San9.Headless -- verify --scenario local/generated/content/scenario-190-live/scenario.json --xuns 36 --output local/generated/verification/roadmap-g02/private-190
dotnet run --project src/San9.Headless -- run --fixture --log local/generated/verification/roadmap-g02/public/commands.jsonl --report local/generated/verification/roadmap-g02/replay/report.json --save local/generated/verification/roadmap-g02/replay/final.save.json
```

`run`默认不录制，可增加`--load PATH`恢复、`--record PATH`显式输出独立录像；再次输入旧日志按水位跳过。输入可选`--fixture`或`--scenario PATH`。`verify`可设`--seed N`。`run`的种子取日志头，保存和恢复时必须保持规则内容一致。

录像用法（普通存档与录像是两个独立输出）：

```powershell
dotnet run --project src/San9.Headless -- run --fixture --log local/generated/verification/roadmap-g02/public/commands.jsonl --report local/generated/verification/roadmap-g02/record/report.json --save local/generated/verification/roadmap-g02/record/player.save.json --record local/generated/verification/roadmap-g02/record/debug.recording.json
dotnet run --project src/San9.Headless -- replay --fixture --recording local/generated/verification/roadmap-g02/record/debug.recording.json --report local/generated/verification/roadmap-g02/playback/report.json --save local/generated/verification/roadmap-g02/playback/final.save.json
```

中途录制可在`run`同时指定`--load`和`--record`；新录像的起点就是载入状态，日志中的旧前缀跳过不录制。录像已含起点，`replay`无需另传初始存档；裸JSONL只含操作，中途日志需配合正确`--load`。

日志为每行一个JSON对象：首行为`contentFingerprint/formatVersion/hashVersion/rulesVersion/seed`；后续为`id/sequence/controllerId/operation`。例如第0条年表操作：

```json
{"controllerId":"player","id":"00000000-0000-00be-0000-000000000001","operation":{"text":"Headless scripted verification","type":"chronicle-note"},"sequence":0}
```

操作类型为`command`、`end-control`、`advance-xun`、`chronicle-note`。领域命令的稳定类型和字段见[GameCommand](../src/San9.Core/Simulation/GameCommand.cs)，完整可复放样例由上述`verify`输出。正式命令目前支持wait/develop；内部start-task仅用于具有显式规则注册的测试，CLI不接受未知任务规则。

`verify`是显式调试验证，会录制并验证`verification.recording.json`，同时输出初始/检查点/连续/恢复存档、`commands.jsonl`、`events.jsonl`、`checkpoints.json`、`report.json`。`run`输出报告、事件与尝试记录，可选保存已接受前缀。stdout始终输出机器JSON；报告包含指纹、种子、日志路径、完成旬数、结束原因、最终世界/会话哈希、首个失败与耗时。

| 退出码 | 含义 |
| --- | --- |
| 0 | 成功 |
| 2 | 参数或输出路径冲突 |
| 3 | 内容输入错误 |
| 4 | 存档错误 |
| 5 | 日志或录像解析/重放拒绝（录像差异序号在`failureSequence`） |
| 6 | 文件I/O错误 |
| 7 | 验证不一致 |
| 8 | 未预期异常 |

## 实际验证

`dotnet test San9Remake.slnx --nologo --no-restore`通过460项，0失败：Core86、Content29、Application63、Importer183、Client93、Headless6。客户端仅编译和测试。独立进程测试覆盖CLI实际退出码、日志失败行、重复恢复、接受前缀保存与输入文件保护。

Application磁盘测试覆盖15日持续任务的扣费、跨旬到期和命名随机；第二次任务提交时同时保存已消耗随机流和待执行任务。连续36旬、首次即时提交后、任务跨旬及跨年前恢复，状态与完整事件一致。另覆盖控制中途存档、终局、年表、内政记录、旧序号/乱序、无效引用、缺失执行者、未知任务、文化/集合排列、注入写入失败和执行中保存拒绝。

| CLI场景 | 公开夹具 | 本地190年完整内容 |
| --- | --- | --- |
| 完成旬数 | 36 | 36 |
| 接受操作/重复重放 | 181 / 181 | 1837 / 1837 |
| 内政命令/等待 | 40 / 32 | 900 / 0 |
| 磁盘恢复 | 10 | 10 |
| 连续/恢复存档 | 逐字节相同 | 逐字节相同 |
| 完整日志重放 | 状态与事件相同 | 状态与事件相同 |
| 本机耗时（包含完整录像重放） | 553ms | 67773ms |
| 初始普通存档字节 | 4626 | 417773 |
| 最终普通存档字节 | 9572 | 433909 |
| 独立录像字节 | 204784 | 1444278 |

190年覆盖25势力、94据点、700人物、25军团、120宝物、300关系、0有效初始部队；身份集合完整保留。数据覆盖不意味着新增人物、外交或战争命令。

公开最终会话哈希：`44B5B11824144112BD24490BBA46343D1BCE10130B790AB76D483F04B649A111`。

190年最终会话哈希：`547BF4E89EB584BF13CBE19E1A1D52B0FE04595DAE5C3BBE70AD4D29BE1E0692`。

本地完整报告位于`local/generated/verification/roadmap-g02/public/`和`private-190/`，不提交原版内容或生成物。

默认玩家模式另有40年（1440旬）测试：每旬实际提交等待再推进，存档长度相比初始增加不超过100字节（日期、序号、版本值等位数变化）；无历史ID、操作、事件数组，恢复后继续一致。自动记录超过128条时淘汰最旧记录，玩家年表保持。开启/关闭录像与普通模式使用同请求后的默认存档逐字节一致。该测试验证时间与存档规模，不代表40年经济或自主对局。

哈希复核及保留理由见[哈希与指纹用途](hash-and-fingerprint.md)。

## 剩余边界

持续任务使用真实执行的内部测试规则，生产任务目录仍为空；人事/战争任务由后续阶段接入。CLI是脚本运行器，未实现周期经济、自主AI或完整对局；公开夹具资金耗尽后的显式等待不代表AI验收。后续动态对象创建需扩充当前身份集合恢复校验，新增规则输入需扩充指纹。普通模式已移除历史累计。显式录像目前在内存累计，导出时原子写盘；超长录像的流式分段、自动落盘与录像播放界面尚未实现；以上耗时是测量结果，不是长期对局性能承诺。

领域契约校验仍存在G00已登记的`BEH-UI-0002..0006`未分类问题，归G13；G02保存边界已登记，不以该历史问题隐藏运行验证结果。

真随机以及存读档后的录像回退/追加已登记为[后续待议](../specs/roadmap.md#后续待议随机与录像管理)，初版维持独立片段，不自动回退或追加。
