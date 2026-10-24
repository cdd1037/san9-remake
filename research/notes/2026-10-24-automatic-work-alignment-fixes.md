# 自动工作26类未对齐修复（2026-10-24）

用户授权修复本对话累计确认的26类实现遗漏。本轮已完成全部26类，保留最初三项已有修复，不扩展未验证候选，也不改B20优待组额外留兵只用于估值、B25输送按服务据点记账等既有采用差异。此前各审计记录描述发现时的实现及诊断，本记录维护本次修复状态；原作研究证据及不确定性不因产品测试通过而升级。

**分类说明（2026-10-24）。**这里26类是重制版遗漏／误用原作规则的实现bug，不是“原版26类bug”的照搬或修复清单。当前证据没有将这26类对应的原作行为认定为原版bug；这也不等于保证原版每项设计均无缺陷。原作已知／疑似缺陷、已明确采用的重制选择和待验证疑点须分别登记，不能只因源程序存在某行为就自动要求继承。后续普攻S1／S2属于另一组重制流程bug，不改变本清单的已修状态，见[专项记录](2026-10-24-normal-attack-scheduling-audit.md)。

**合理性复审与v181替代。**用户明确要求分析并授权R1／R2／R3按合理方式实现后，[三项已完成](2026-10-24-original-ai-rationality-review.md#用户确认后的实现v181)：关隘估值与实际分配统一扣留守兵，高昂返回队计入回援预算，目标变更不清异常状态。原26类的原作取证仍保留，但关的阶段差异及返回预算排除高昂不再是正确产品预期。其他19项合理行为及5项启发式取舍未扩展修改。

**后续修复。**用户1号存档虎牢关全军出征触发[设施分类续核及修复](2026-10-24-automatic-work-sortie-mapping-audit.md)，另确认并修复关隘集合、路线障碍集合、输送保留量三项版本内容映射错误。原26项小地图回归使用显式配置的语义设施引用，没有覆盖版本Importer的目录／归并类别转换；现补齐17种设施的转换回归，45项真实内容行为诊断、6054项默认解决方案测试及20剧本验证全部通过。旧档继续持有保存时的策略，需从更新内容新开局生效；原定26项修复本身不构成所有出兵状态均已穷尽的证据。

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

分粮同根分母是同一兵力范围根因的一部分：建立首个子军团后，下一组仍按同根兵伤计算分母，不能缩成根直属；资金人数分母和零兵城市数量回退按既有规则保留。建设转换另覆盖中途敌建筑截换后只读取实际路线前缀。敌接近循环使用世界变更缓存的实时交战关系，不跨前一单位迎击改令冻结旧结果。

新增参数化回归共89项：Application77、Core8、Importer4；复用现有夹具与真实公共命令。原有编码错误行为的无目标建设断言已改为合法进攻路径转建设；不保留临时失败探针为正式测试。没有新增一套反编译实现、旧版兼容分支或全局补丁式兜底。

## 契约与运行内容

- `ConstructionSite.PreferenceDirections`独立保存横／纵偏好许可，源中心位只由版本Importer转换；可建石兵不再隐含横纵许可。消费者按横优先、否则纵向、无可用偏好回普通设施；规则校验拒绝未知方向位。
- `UnitState.AutonomousReturn`保存自主返回身份，要求主任务为返回；正式新命令清除身份，自主转换成功及任务收尾维护身份，失败转换保留状态。删除目标、联盟改令、转属与到达同步维护，精确恢复保留。
- 新字段均为必填，规则v179、存档126、命令日志132、剧本145／组合227；旧结构明确拒绝，不推测缺失语义。Importer新局不恢复源命令历史，因此不会单凭源命令码给空主任务设置自主返回。
- 当前目录型20个正式剧本已用现有`import-content`入口集中原位更新。工具没有仅重建方向契约的局部入口，本轮实际重建全部20个剧本JSON，复用共享纹理及已准备音频，没有重新转码或转换图片。独立历史诊断包不属于当前目录入口，未批量改写。

## 实际验证

最终`dotnet test San9Remake.slnx --no-restore`完整通过6037项：Core2946、Application1906、Content156、Importer493、Client.Godot536。存档／命令日志结构有变化，另运行`dotnet test tests/San9.Headless.Tests/San9.Headless.Tests.csproj`，41项全部通过。最终逐项目TRX位于忽略目录`local/generated/verification/automatic-work-fixes/final/`；早期夹具和编译错误均已修正，不把失败批次当作最终通过。

20正式剧本的`verify-content`全部通过，核对包、初始世界身份、查询与开局规则，未丢失各剧本的势力、据点或700名支持人物。产品及测试差异、文档链接、行尾和`git diff --check`作为交付收尾检查。本轮没有重新进行IDA标注或原游戏动态实验，没有运行Godot画面验收或长期对局，不把编译及内容查询当作交互验收。
