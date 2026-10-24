# 自动工作组合与状态刷新续核（2026-10-24）

**结果。**本分工未新增确认的遗漏分支或条件。完成跨工作组织变化后的缓存对照，并排除了按控制器字符串判断玩家身份的初步疑点。这个结论仅限下述审查和6项定向配置，不表示全部组合、恢复和回滚都已穷尽。

**范围与身份。**作者Codex，基线`258d757ffee276bc7f2c158ef513daf77e9f2f91`及当前工作区；读取[原版AI权威研究](../ai.md#lifecycle)的调度、组织变化、失败消费和[缓存生命周期](../ai.md#analysis-state-effects)，对照B01／B10～B14／B18／B30已采用行为。仅研究和诊断，没有修改产品、正式测试、主研究正文或提交。本轮没有独立调用IDA、原游戏或转换器，原作事实复用既有研究；不把阅读既有用例算作本轮运行证据。

## 实际定向验证

### 组织变化后的人物与经营缓存：2项通过

复用`OriginalAiPersonnelTests.OrganizationFixture`，设置省1城市6／7的政治100人物106／107，根辖区城市8的政治80人物108，其余己方人物政治30，并在城8放置一名未发现人物。测试复用同一个`OriginalAiPersonnel`和`OriginalAiProjection`实例，先以零尝试搜索准备及`Domestic()`读取建立缓存，再通过真实公共军团命令改变辖区。

| 公共状态变化 | 后续实际搜索执行者 | 缓存对照 |
| --- | --- | --- |
| 把城6／7新设给以106为都督的子军团 | 108；没有继续使用已归子军团的106／107 | 原经营投影不再含城6／7，与当前世界新建的冷投影完整规范JSON相等 |
| 先有上述子军团，再公开解散它 | 106；根军团重新纳入解散后归还的来源 | 原经营投影重新包含城6／7，与当前世界新建的冷投影完整规范JSON相等 |

新设、解散及最终搜索全部实际经`SimulationEngine.Execute`接受。此处验证的是军团结构／资产变化后已存在的人物与经营投影实例，不是新建实例天然读到新世界。人物选择由完整`Run(Search)`完成，未直接调用私有选择器。零尝试搜索只用于建立候选读取缓存，没有提交或消费人物。

### 控制器身份疑点：4项通过，排除

`OriginalAiStrategy.PrepareGlobal`和`PrepareCorps`中的玩家判断使用`ControllerId == "player"`，初读时怀疑合法Player席位换名会被当作电脑参与分组／战略分类。进一步沿真实会话入口核验：

- `SimulationEngine.SetForceControl`将Player席位统一写为`"player"`，电脑席位写为`"ai:{ForceId}"`。
- [SessionValidation.cs](../../src/San9.Application/SessionValidation.cs)验证保存恢复时强制同一映射。Core的世界不变量允许非空字符串，不等于Application接受任意控制器名的正式会话。
- 四普通势力分组夹具中，正常`"player"`恢复后经公共`EndControlOperation`被接受，玩家组保持`Unassigned`；只将其改为`"custom-player"`时，恢复直接拒绝，尚未进入自动工作。
- 异民族面对关系40的玩家夹具中，正常恢复并结束双方控制后，异民族按玩家分支选择`TargetForce`；改名输入同样在恢复边界拒绝。

后两项异常输入用`ArgumentException`及`control configuration mismatch`明确断言。没有把绕过会话验证后直接构造Core世界得到的静态差别计为正常产品缺陷，也不将原来的多玩家设计误解成每个玩家必须具有不同控制器名。

## 静态对照与已采用差异

以下是本轮代码／研究对照，不是新增动态复现：

- [OriginalAiTurnRunner](../../src/San9.Application/AI/OriginalAiTurnRunner.cs)在席位内先根军团后子军团，符合B01的重制选择；不把未采用的原作跨势力信望统一排序再次报告为遗漏。工作12成功后重算当前配额，按当前有效军团移除解散项并追加未处理的新军团，随后`Reconcile`策略状态。B10／B12对组织变化重算配额及同旬追加的采用规则优先于原作旧配额时点。
- Geography只缓存所有权／拓扑关系，军团归属变化不必改变它；组织工作后的显式配额更新及人物／经营投影自身对Corps集合变化的失效负责直属范围刷新。上述两项新探针验证了后两者，不能因Geography实例复用本身判定失效遗漏。
- 续行记录保存当前席位、有序待处理／已处理军团、工作游标、规划随机状态、配额和俘虏处置进度。恢复现有游标时跳过全局重新准备及当前军团的策略重抽样，从处置进度继续；不把暂停前的当前工作重新当作全新任务。续行不进入稳定存档协议；原作读档保留的其他元数据不直接成为重制恢复要求。
- `OriginalAiPersonnel.Refresh`按人物、军团、据点及任务集合更新人物范围、当前能力、直属地点和忙碌集合；经营投影还重新读取账户、驻军／野战粮耗和俸禄。军事规划器按人物／宝物更新能力，按单位／据点／外交变化清路线、目标投影和占用缓存。未从这些依赖中发现本轮可用正常命令证明的新缺口。
- `OriginalAiTerrainRoutes`的缓存键包含不可变地图实例、起终点／目标占地、形状、行军模式和进入范围方式，计算不带动态占用；完整军事预测缓存仍由各规划器持有。没有把该地形缓存复用等同于跨世界复用完整决策结果。
- `GameSession.CalculateOperation`对候选引擎执行，只有形成`OperationCommit`才发布世界、OriginalAi、报告和命令／操作身份；规划随机对象和地形路线缓存位于本次计算内。B13的尝试／候选消费与B14的资源扣账区分维护，不能把拒绝后不回填候选一律判错。

已有`OriginalAiPersonnelTests`的同旬新增／解散队列、处斩待答恢复、组织后外层异常回滚用例，以及`OriginalAiCycleTests`的经营复合操作回滚用例，本轮只读审阅其职责和断言，没有重跑，不将其历史结果附会成本轮通过。

## 产物、失败记录与限制

最终`composition-audit-final.trx`记录6项通过，完整探针`CompositionAuditProbeTests.cs`已移入忽略目录`local/generated/verification/automatic-work-audit/`，没有保留在正式测试目录。运行命令为`dotnet test tests/San9.Application.Tests/San9.Application.Tests.csproj --filter FullyQualifiedName~CompositionAuditProbeTests --logger "trx;LogFileName=composition-audit-final.trx" --results-directory local/generated/verification/automatic-work-audit --no-restore`，Windows、.NET10.0。

首轮`composition-audit.trx`为2通过／4失败：两项组织诊断误用不支持军团命令的`GetPreparationEligibility`查询，尚未实际提交，不证明产品拒绝军团命令；另两项是自定义控制器输入被正式恢复验证拒绝，促成上述候选排除。最终诊断改以真实军团命令接受验证组织变化，并将恢复拒绝明确写为预期。首轮未作为有效缺陷证据。初次文件编译还曾误用不存在的`OfficerState.Abilities`，已改为真实字段`BaseAbilities`；这同样是探针错误，不是产品问题。

没有专门执行任意势力转属发生于暂停应答中的场景、所有拒绝组合、跨旬全部缓存冷热矩阵、全解决方案、Headless、原游戏动态实验或长期对局；没有新增主库标注。前轮已确认的人事、军事、资源和候选消费问题均未重复计数。
