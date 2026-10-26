# 自动工作26类未对齐修复（2026-10-24）

用户授权的26类重制实现遗漏已修复，原作证据及不确定性不因产品测试升级。它们不是“原版26类bug”；B20优待组额外留兵仅用于估值、B25按服务据点记账等既有采用差异保留。

后续替代及续修分别维护：

- [合理性复审v181](2026-10-24-original-ai-rationality-review.md#用户确认后的实现v181)：R1统一城市／关估值与实际基础留兵，R2返回预算加入高昂，R3目标变化保留异常；替代下表相关原作对齐预期。另19项合理规则及5项启发式未扩展修改。
- [设施分类续修](2026-10-24-automatic-work-sortie-mapping-audit.md)：用户1号档暴露关隘、路线障碍、输送保留三项版本映射错误。原小地图夹具未覆盖Importer类别转换；续修新增17类转换回归，45项真实内容诊断、6054项默认测试及20剧本验证通过。旧档持有旧策略，需更新内容新开局。
- [普攻S1／S2](2026-10-24-normal-attack-scheduling-audit.md)为另一组流程问题；26类已修不证明全部出兵状态穷尽。

## 覆盖与回归

| 范围 | 已修复根因 | 产品／回归入口 |
| --- | --- | --- |
| 战略2类 | 分组计入驻军；模式2低关系更新域不复用选敌门 | `OriginalAiStrategy`；[战略回归](../../tests/San9.Application.Tests/OriginalAiStrategyRegressionTests.cs) |
| 进攻／履约4类 | 指定设施提前；保留弱来源负贡献；关估值基础留兵在v181改为与实际分配一致；500钱门只用于普通进攻 | `OriginalAiMilitary.Attack`；[进攻回归](../../tests/San9.Application.Tests/OriginalAiMilitaryAttackAlignmentTests.cs) |
| 建设4类 | 路线首尾消费、候选与概率；无目标不独立建设；首目标计划失败不重选同城市；独立方向许可契约及消费者 | 同上；`ConstructionSite`／转换器／规则校验；[转换回归](../../tests/San9.Importer.San9PkTc101.Tests/ConstructionContentCompilerTests.cs) |
| 选人与单人后备3类 | 9／59按取主将后人数计算补员预算；零熟练度合法；单人后备固定基础船 | `OriginalAiMilitary.Attack/Equipment`；进攻回归及[军事支援回归](../../tests/San9.Application.Tests/OriginalAiMilitarySupportAlignmentTests.cs) |
| 58迎敌／供援3类 | 非城市／关按人物容量派兵；成功后可同配对续派；来源计入完整来袭压力 | `OriginalAiMilitary.Support`；军事支援回归另覆盖公共拒绝不改资源及尝试预算终止 |
| 57旬级撤回3类 | 异族位置进军可召回；返回预算限定普通／普通交战，v181明确加入高昂；建筑解析独立回退主建筑 | `OriginalAiMilitary.Support`；军事支援回归 |
| 28救援3类 | 后方基础留兵为0；重点区域预筛不误用立即前线；返回／建设目标使用真实交战门 | `OriginalAiActions.Targets`；[救援回归](../../tests/San9.Application.Tests/OriginalAiActions.ReliefTests.cs) |
| 12组织2类 | 区域规划覆盖既有子军团城市；分粮分子计辖区驻军与服务部队，分母保持同根兵伤 | `OriginalAiStaffing`；[组织回归](../../tests/San9.Application.Tests/OriginalAiPersonnel.OrganizationTests.cs) |
| 公共响应2类 | 普通返城／自主返回身份持久化；敌接近按实时交战关系跳过已交战队伍 | `UnitState/MilitaryOrders/MilitaryPolicies`；[响应回归](../../tests/San9.Core.Tests/UnitPolicyAlignmentTests.cs)及现有[会话保存恢复](../../tests/San9.Application.Tests/MilitarySessionTests.cs) |

分粮分母保持同根兵伤；资金人数分母及零兵城市回退不变。建设转换覆盖中途截换后的实际路线前缀；敌接近读取实时交战缓存。新增参数化回归89项（Application77、Core8、Importer4），复用现有夹具和公共命令。

## 契约与运行内容

- `ConstructionSite.PreferenceDirections`独立保存横／纵偏好许可，源中心位只由版本Importer转换；可建石兵不再隐含横纵许可。消费者按横优先、否则纵向、无可用偏好回普通设施；规则校验拒绝未知方向位。
- `UnitState.AutonomousReturn`保存自主返回身份，要求主任务为返回；正式新命令清除身份，自主转换成功及任务收尾维护身份，失败转换保留状态。删除目标、联盟改令、转属与到达同步维护，精确恢复保留。
- 新字段均为必填，规则v179、存档126、命令日志132、剧本145／组合227；旧结构明确拒绝，不推测缺失语义。Importer新局不恢复源命令历史，因此不会单凭源命令码给空主任务设置自主返回。
- 当前目录型20个正式剧本已用现有`import-content`入口集中原位更新。工具没有仅重建方向契约的局部入口，本轮实际重建全部20个剧本JSON，复用共享纹理及已准备音频，没有重新转码或转换图片。独立历史诊断包不属于当前目录入口，未批量改写。

## 实际验证

本次26类修复的历史验证：`dotnet test San9Remake.slnx --no-restore`通过6037项（Core2946、Application1906、Content156、Importer493、Client.Godot536），Headless另41项通过。最终TRX在`local/generated/verification/automatic-work-fixes/final/`，不含无效早期批次。

20正式剧本`verify-content`通过，覆盖包、世界身份、查询及开局规则，各剧本势力、据点及700名支持人物保留。差异／链接／行尾检查通过；无新增IDA标注、原游戏动态、Godot画面或长期对局验收。
