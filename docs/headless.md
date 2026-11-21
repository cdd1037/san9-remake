# 无界面运行与命令脚本

本页维护当前 CLI 使用与持久化接线；历史阶段结果见[G02 验收](history/stages/g02-verification.md)。领域行为见[存档规格](../specs/behavior/BEH-SAVE-0001-session-persistence-replay-and-headless.md)，随机语义见[随机与 SL](../specs/behavior/BEH-SIMULATION-0002-randomness.md)。

## 实际能力与所有权

`ScenarioSessionDefinition`从现有Content剧本创建规则身份与世界；`GameSession.Start/Restore`组织新局和恢复。`SaveService`保存完整世界、控制轮次、任务、命名随机、最近128条内政记录、玩家年表和序号水位；不累计完整操作和事件历史。恢复不重复新局准备，先验证身份与引用再构造查询。资料集标识、入局人物和新势力引用与存档一起校验，不在恢复时重新执行新局初始化。

`GameSession.Apply`统一命令、结束控制、推进与年表操作，在候选世界上执行并发布当前状态和水位。会话序号从0开始，另有Core命令序号；Core不保存整局已完成命令ID。当前会话内旧序号返回`AlreadyProcessed`，不执行、不返回旧结果，也不验证旧载荷；未来序号拒绝。外部JSONL在文件头核对格式、种子和会话配置，规则版本不同仅警告；本地请求不再重复附加流指纹。拒绝不占用序号。JSONL输入首次失败即停止，接受前缀可保存续跑。

存档和命令日志版本以[SessionProtocol](../src/San9.Application/SessionProtocol.cs)为准；未知格式拒绝，规则版本不同仅告警后按当前规则继续。普通存档包含部队实际朝向，不含操作历史或完整事件历史。显式诊断比较完整状态并返回字段差异，不生成默认状态哈希。

命令脚本的头与请求由调用方显式提供：`CommandLogService.CreateHeader(session)`捕获起始配置，`CommandLogService.Write(path, header, requests)`写入输入集合；GameSession不录制、不导出历史。中途执行需要匹配的精确起点存档。

解析拒绝缺字段、额外字段、重复键、非法枚举、空集合元素、截断、未知版本、损坏引用及未知任务规则。文件通过同目录临时文件、flush和原子替换写入；替换前故障不会截断旧文件。没有旧版自动迁移，也不额外承诺硬件断电保证。

## CLI 使用

仅需.NET 10；CLI不引用Godot。生成物保存在忽略目录：

```powershell
dotnet run --project src/San9.Headless -- verify --fixture --xuns 36 --output local/generated/verification/headless/public
dotnet run --project src/San9.Headless -- verify --content-package local/generated/content/scenarios/historical/scenario-1 --player-force 5 --xuns 36 --output local/generated/verification/headless/private-190
dotnet run --project src/San9.Headless -- run --fixture --log local/generated/verification/headless/public/commands.jsonl --report local/generated/verification/headless/replay/report.json --save local/generated/verification/headless/replay/final.save.json
```

`run`执行显式JSONL脚本，可增加`--load PATH`玩家读档（换运行种子），或`--restore PATH`精确恢复；二者互斥。再次输入旧前缀按水位跳过。输入三选一：`--fixture`、独立领域状态`--scenario PATH`或带共享引用的已发布剧本`--content-package DIR`；`--player-force ID`显式选择玩家势力，目录包不默认选玩家，新局种子取日志头；`verify`可设`--seed N`。`--save PATH`保存已接受前缀，不自动产生存档。

录像的`replay`模式、`--record`和`--recording`已移除，使用会返回退出码2；没有旧录像读取或迁移入口。

日志为每行一个JSON对象：首行为`formatVersion/rulesVersion/contentSetId/seed`及会话设置；后续为`id/sequence/controllerId/operation`。例如第0条年表操作：

```json
{"controllerId":"player","id":"00000003-0000-0000-0000-000000000000","operation":{"text":"Headless scripted verification","type":"chronicle-note"},"sequence":0}
```

操作的稳定类型与字段由[SessionProtocol](../src/San9.Application/SessionProtocol.cs)定义，包含命令、控制、推进、待答与年表等操作；不在手册复制完整清单。领域命令的稳定类型和字段见[GameCommand](../src/San9.Core/Simulation/GameCommand.cs)，完整可复放样例由上述`verify`输出。正式命令由当前公共会话协议定义；内部start-task仅用于具有显式规则注册的测试，CLI不接受未知任务规则。

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
