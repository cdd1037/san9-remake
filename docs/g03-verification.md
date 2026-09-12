# G03 经营与兵员循环验收

验收日期：2026-09-12。G03a、G03b退出条件已成立，下一阶段为G04人事与治理。行为依据为[BEH-ECONOMY-0001](../specs/behavior/BEH-ECONOMY-0001-domestic-and-economic-cycle.md)，基础计算沿用[BEH-DOMESTIC-0001](../specs/behavior/BEH-DOMESTIC-0001-base-development-effect.md)。本轮没有启动游戏，正式经营菜单仍待后续界面阶段。特殊对白也按用户确认延后至UI接入时处理，触发时机已记入[RE-0095](../research/findings/RE-SAN9PK-0095.md)，本轮不扩展对白事件或表现。

## 实际行为与边界

商业、开垦、巡察、修筑、征兵、训练支持1–5名参与者，共享资格、占用和行动校验；多人组合使用规范化人物对与独立随机流。征兵在兵员、资金和驻军容量之间取可执行数量，训练更新士气与兵法熟练度。势力与独立军团的金粮账户统一路由，资源事件记录请求量、实际量、短缺和上限损失。

旬前置更新发放俸禄，每日扣粮；经过至少30日后，季度结算人口、预备兵员、信望和资金，七月结算收获。收入使用人口平方根，信望使用超过300的民心与人口乘积，两者公式不同。开局、30日门、七月、跨年、零资源、上限和多人随机均有针对性验证。

审计后恢复原作俸禄双向概率忠诚和异族季度/七月补给，取消欠薪必减1、支付不增忠诚及统一普通经济的选择；内政排序改为身份优先、能力与ID分离。当前规则与验证见[审计修复验收](g01-g04-audit-verification.md)。粮食不足扣至零并报告，饥饿伤亡仍属于战争后勤范围。简单策略仅选择可执行经营命令，尚不是完整经营或战略AI；出现欠薪不能把确定性验收解释为策略质量达标。

## 内容链路与协议

源数据、转换包、会话复制与查询核对覆盖700名保留人物（源800槽）、25势力、25军团、94据点、76官职；核对相性、习得兵法、官职、人物位置、基础信望、驻军容量与粮耗资格。组合表为巡察12、商业7、开垦5、修筑5、训练12对，使用实际人物ID。190年初始无野战部队，不能据此声称已验收真实部队后勤。粮耗排除集合为据点107；初始总俸禄1232，董卓势力首次扣款444。

研究依据及本轮静态核对、IDA注释回读记录见[RE-0096](../research/findings/RE-SAN9PK-0096.md)与[RE-0098](../research/findings/RE-SAN9PK-0098.md)。没有新增原作动态实验。审计报告在忽略目录`local/generated/verification/roadmap-g03/input-audit.json`。

内容schema为12，规则版本为`san9-core-g03-v1`，存档/操作日志格式为4；录像格式仍为2，摘要版本仍为1。新经济定义进入内容指纹，新世界字段进入保存恢复；旧内容包须重新转换，旧规则存档明确拒绝，不提供迁移分支。

## 实际验证

完整解决方案480项通过（Core104、Content29、Importer183、Application63、Client93、Headless8）；随后新增3项组合测试并运行Core，107项全部通过。Python领域契约验证器测试3项通过。纯文档链接与差异另作检查。

固定种子190、36旬，以下四种运行全部成功；每种均比较10个恢复点、最终存档字节、状态与事件，并完成操作日志和录像重放。公开场景两种策略均覆盖六类命令。

| 内容与策略 | 内政命令 | 等待 | 欠薪金额 | 恢复点 | 验收总耗时 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 公开夹具，脚本 | 72 | 0 | 0 | 10 | 674 ms |
| 公开夹具，简单策略 | 72 | 0 | 0 | 10 | 694 ms |
| 190年，脚本 | 884 | 16 | 1224 | 10 | 69937 ms |
| 190年，简单策略 | 874 | 26 | 1840 | 10 | 70304 ms |

本次短缺全部为俸禄，粮食短缺与资源上限损失均为零。耗时包含恢复/重放对照及报告写入，不是游戏单次推进耗时或性能门槛。公开夹具指纹为`B6F81E64A57AFF2578F274A49F4BCE9FCCDC65D3FC3A8A48528869B304EB96FA`，190年为`456691863684DEC8E27B2F396C3A2FA94D7E85D679F89064ADF6B735F609A8D7`。

复现命令（生成物均不入库；190年包先按[内容管线](client-content.md)重新转换）：

```powershell
dotnet run --project src/San9.Headless -- verify --fixture --xuns 36 --output local/generated/verification/roadmap-g03/public-scripted
dotnet run --project src/San9.Headless -- verify --fixture --strategy simple --xuns 36 --output local/generated/verification/roadmap-g03/public-policy
dotnet run --project src/San9.Headless -- verify --scenario local/generated/content/scenario-190-live/scenario.json --xuns 36 --output local/generated/verification/roadmap-g03/private-scripted
dotnet run --project src/San9.Headless -- verify --scenario local/generated/content/scenario-190-live/scenario.json --strategy simple --xuns 36 --output local/generated/verification/roadmap-g03/private-policy
```

每个输出目录保留`report.json`、命令/事件日志、录像与恢复对照产物。领域契约检查仍仅报告既有`BEH-UI-0002..0006`未分类问题（归G13），没有新增G03问题；不能将该检查描述为通过，报告为`local/generated/verification/roadmap-g03/core-domain-contracts.json`。
