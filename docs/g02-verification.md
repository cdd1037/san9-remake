# G02 保存、重放与无界面运行

验收日期：2026-09-12。G01前置提交为`c7df256`，复用其日历、任务、随机、事务和统一命令终验；G02退出条件已成立。行为依据为[BEH-SAVE-0001](../specs/behavior/BEH-SAVE-0001-session-persistence-replay-and-headless.md)。没有启动游戏或扩展表现层。

本文的CLI说明按当前实现维护；实际验证章节保留G02历史证据。2026-10-22录像已撤下，历史录像结果不构成当前功能或日常验收要求。当前契约见[BEH-SAVE-0001](../specs/behavior/BEH-SAVE-0001-session-persistence-replay-and-headless.md)。

## 实际能力与所有权

`ScenarioSessionDefinition`从现有Content剧本创建规则身份与世界；`GameSession.Start/Restore`组织新局和恢复。`SaveService`保存完整世界、控制轮次、任务、命名随机、最近128条内政记录、玩家年表和序号水位；不累计完整操作和事件历史。恢复不重复新局准备，先验证身份与引用再构造查询。Content/Importer沿用已有完整剧本集合，没有新增导入字段。

`GameSession.Apply`统一命令、结束控制、推进与年表操作，在候选世界上执行并发布当前状态和水位。会话序号从0开始，另有Core命令序号；Core不保存整局已完成命令ID。当前会话内旧序号返回`AlreadyProcessed`，不执行、不返回旧结果，也不验证旧载荷；未来序号拒绝。外部JSONL在文件头核对格式、种子和会话配置，规则版本不同仅警告；本地请求不再重复附加流指纹。拒绝不占用序号。JSONL输入首次失败即停止，接受前缀可保存续跑。

当前存档版本122、JSONL命令日志版本129；未知格式拒绝，规则版本不同仅告警后按当前规则继续。普通存档不含操作历史或完整事件历史。显式诊断比较完整状态并返回字段差异，不生成默认状态哈希。

命令脚本的头与请求由调用方显式提供：`CommandLogService.CreateHeader(session)`捕获起始配置，`CommandLogService.Write(path, header, requests)`写入输入集合；GameSession不录制、不导出历史。中途执行需要匹配的精确起点存档。

解析拒绝缺字段、额外字段、重复键、非法枚举、空集合元素、截断、未知版本、损坏引用及未知任务规则。文件通过同目录临时文件、flush和原子替换写入；替换前故障不会截断旧文件。没有旧版自动迁移，也不额外承诺硬件断电保证。

## CLI 使用

仅需.NET 10；CLI不引用Godot。生成物保存在忽略目录：

```powershell
dotnet run --project src/San9.Headless -- verify --fixture --xuns 36 --output local/generated/verification/roadmap-g02/public
dotnet run --project src/San9.Headless -- verify --scenario local/generated/content/scenario-190-live/scenario.json --xuns 36 --output local/generated/verification/roadmap-g02/private-190
dotnet run --project src/San9.Headless -- run --fixture --log local/generated/verification/roadmap-g02/public/commands.jsonl --report local/generated/verification/roadmap-g02/replay/report.json --save local/generated/verification/roadmap-g02/replay/final.save.json
```

`run`执行显式JSONL脚本，可增加`--load PATH`玩家读档（换运行种子），或`--restore PATH`精确恢复；二者互斥。再次输入旧前缀按水位跳过。输入可选`--fixture`或`--scenario PATH`，新局种子取日志头；`verify`可设`--seed N`。`--save PATH`保存已接受前缀，不自动产生存档。

录像的`replay`模式、`--record`和`--recording`已移除，使用会返回退出码2；没有旧录像读取或迁移入口。

日志为每行一个JSON对象：首行为`formatVersion/rulesVersion/seed`及会话设置；后续为`id/sequence/controllerId/operation`。例如第0条年表操作：

```json
{"controllerId":"player","id":"00000003-0000-0000-0000-000000000000","operation":{"text":"Headless scripted verification","type":"chronicle-note"},"sequence":0}
```

操作类型为`command`、`end-control`、`advance-xun`、`chronicle-note`。领域命令的稳定类型和字段见[GameCommand](../src/San9.Core/Simulation/GameCommand.cs)，完整可复放样例由上述`verify`输出。正式命令由当前公共会话协议定义；内部start-task仅用于具有显式规则注册的测试，CLI不接受未知任务规则。

`verify`是显式调试验证，会比较连续／恢复／重放终态、逐步核对实际事件，输出初始/检查点/连续/恢复存档、`commands.jsonl`、`events.jsonl`、`checkpoints.json`、`report.json`。`run`输出报告、事件与尝试记录，可选保存已接受前缀。stdout始终输出机器JSON；报告包含种子、日志路径、完成旬数、结束原因、首个失败与耗时。

| 退出码 | 含义 |
| --- | --- |
| 0 | 成功 |
| 2 | 参数或输出路径冲突 |
| 3 | 内容输入错误 |
| 4 | 存档错误 |
| 5 | 命令脚本解析或执行拒绝（`firstFailure.line`定位输入行） |
| 6 | 文件I/O错误 |
| 7 | 验证不一致 |
| 8 | 未预期异常 |

## 实际验证（历史证据）

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

默认玩家存档通过3旬短流程检查不含历史命令ID、操作或事件数组，并验证恢复后继续一致。自动记录超过128条时精确淘汰最旧记录，玩家年表保持；开启／关闭录像与普通模式使用同请求后的默认存档逐字节一致。原40年推进及存档长度差阈值已移除：当前目标与随机流数量变化不等于历史增长，长度差不能作为可靠的有界性判据。当前测试不宣称覆盖长期经济、自主对局或内存泄漏。

哈希复核及保留理由见[哈希与指纹用途](hash-and-fingerprint.md)。

## 剩余边界

持续任务使用真实执行的内部测试规则，生产任务目录仍为空；人事/战争任务由后续阶段接入。CLI是脚本运行器，未实现周期经济、自主AI或完整对局；公开夹具资金耗尽后的显式等待不代表AI验收。后续动态对象创建需扩充当前身份集合恢复校验，新增权威状态需扩充诊断捕获。普通模式已移除历史累计。显式录像目前在内存累计，导出时原子写盘；超长录像的流式分段、自动落盘与录像播放界面尚未实现；以上耗时是测量结果，不是长期对局性能承诺。

领域契约校验仍存在G00已登记的`BEH-UI-0002..0006`未分类问题，归G13；G02保存边界已登记，不以该历史问题隐藏运行验证结果。

真随机以及存读档后的录像回退/追加已登记为[后续待议](../specs/roadmap.md#后续待议随机与录像管理)，初版维持独立片段，不自动回退或追加。
