# 兵种系统及旧版内容包升级版权核查

- 核查日期：2026-11-09；基线：`9f6954a0383d0e3020c9901238355b02c28b0018`。
- 范围：下列 99 个拟提交文件的 Git 暂存差异；本记录自身不计入摘要。`git diff --cached --binary` 的 SHA-256：`e7fb256355d349aee2a88525907d6009c86ec0d10bcce2a148ed9801882ce4ce`。
- 方法：检查暂存路径、文件类型、UTF-8、依赖变化、版权标记及新增文本，并核对迁移实现所使用的资源来源。临时验证目录及本机输入均未纳入暂存。

## 结果与限制

- 暂存文件仅包含 C# 和 Markdown；没有原版程序、图片、音频、字体、剧本／转换包、IDA 数据库、密钥或第三方依赖。测试内容包保留在忽略目录，不随 PR 分发。
- 新增规则、工具及说明为本次编写，沿用仓库已有接口和 TC101 数据解释；升级工具在用户本机读取已转换包并生成副本，不附带原作资源。旧包内的素材及生成副本仍归相关权利人所有。
- 仓库 [README](../../../README.md#许可与版权) 将项目自行编写部分标为 GPL-3.0-only，并明确不授予原作素材许可。未发现本次暂存内容中可确认的未经许可第三方素材或大段反编译代码；这不是全网相似性鉴定，也未审核仓库历史或发行物。
- 未取得或核实光荣特库摩对本项目的授权；名称、商标、作品改编及各地适用法律的问题不能凭本次扫描排除。[美国版权局的游戏说明](https://www.copyright.gov/register/tx-games.html)区分游戏规则与可能受保护的文字、美术表达；具体案件和适用法仍需独立判断。本记录不保证零法律风险。

## 核查文件清单

```text
README.md
research/notes/legacy-remake-content-migration.md
specs/behavior/BEH-WAR-0007-troop-formations-research.md
specs/behavior/README.md
src/San9.Application/AI/MixedDeploymentSelection.cs
src/San9.Application/AI/OriginalAiMilitary.Equipment.cs
src/San9.Application/AI/OriginalAiMilitary.cs
src/San9.Application/AI/OriginalAiResearch.cs
src/San9.Application/AI/OriginalAiTurnRunner.cs
src/San9.Application/DeploymentPreset.cs
src/San9.Application/GameSession.cs
src/San9.Application/Persistence/StrictJson.cs
src/San9.Application/Presentation/DeploymentDraft.Presets.cs
src/San9.Application/Presentation/DeploymentDraft.Tactics.cs
src/San9.Application/Presentation/DeploymentDraft.cs
src/San9.Application/Presentation/DeploymentTargets.cs
src/San9.Application/Presentation/MilitaryTargets.cs
src/San9.Application/Presentation/OccurrenceFeedback.cs
src/San9.Application/Presentation/ResearchDisplay.cs
src/San9.Application/ScenarioSessionDefinition.cs
src/San9.Application/SessionProtocol.cs
src/San9.Client.Godot/UI/Information/FormationBoardView.cs
src/San9.Client.Godot/UI/Start/OpeningSettingsView.cs
src/San9.Client.Godot/UI/Strategy/DeploymentDetachmentsView.cs
src/San9.Client.Godot/UI/Strategy/DeploymentView.cs
src/San9.Client.Godot/UI/System/FunctionsMenuView.cs
src/San9.Client.Godot/UI/System/ResearchView.cs
src/San9.Client.Godot/UI/System/SystemMenuPanel.cs
src/San9.Content/Packages/LegacyImageTableMigration.cs
src/San9.Content/Scenarios/MixedTroopPreset.cs
src/San9.Content/Scenarios/ScenarioAuthoring.cs
src/San9.Core/Domain/Military/DetachmentState.cs
src/San9.Core/Domain/Military/MilitaryRulebook.cs
src/San9.Core/Domain/Military/NavalTrainingDefinition.cs
src/San9.Core/Domain/Military/ResearchProjectDefinition.cs
src/San9.Core/Domain/Military/SiegeEquipmentDefinition.cs
src/San9.Core/Domain/Military/TacticMath.cs
src/San9.Core/Domain/Military/TacticRule.cs
src/San9.Core/Domain/Military/TroopTypeDefinition.cs
src/San9.Core/Domain/Military/UnitFormations.cs
src/San9.Core/Domain/Military/UnitState.cs
src/San9.Core/Domain/NewGameSettings.cs
src/San9.Core/Domain/RuntimeState.cs
src/San9.Core/Simulation/Combat/BattleAftermath.cs
src/San9.Core/Simulation/Combat/CombatExchangeDamage.cs
src/San9.Core/Simulation/Combat/MilitaryCombat.cs
src/San9.Core/Simulation/Combat/MilitaryEngagements.cs
src/San9.Core/Simulation/Combat/MixedTroopCombat.cs
src/San9.Core/Simulation/Combat/TacticCombat.cs
src/San9.Core/Simulation/Construction/FacilityConstruction.cs
src/San9.Core/Simulation/Construction/FacilityDemolition.cs
src/San9.Core/Simulation/DomainEvent.cs
src/San9.Core/Simulation/Events/Historical/SunMarriage.cs
src/San9.Core/Simulation/GameCommand.cs
src/San9.Core/Simulation/Governance/CorpsControl.cs
src/San9.Core/Simulation/Governance/ForceCreation.cs
src/San9.Core/Simulation/Governance/ForceTransfer.cs
src/San9.Core/Simulation/Governance/SettlementAffiliation.cs
src/San9.Core/Simulation/Military/FormationEligibility.cs
src/San9.Core/Simulation/Military/MilitaryOrders.cs
src/San9.Core/Simulation/Military/MilitaryResearch.cs
src/San9.Core/Simulation/Movement/MilitaryArrivalForecast.cs
src/San9.Core/Simulation/Movement/MilitaryMovement.cs
src/San9.Core/Simulation/Movement/MilitaryNavigation.cs
src/San9.Core/Simulation/Movement/MilitaryOrderQuery.cs
src/San9.Core/Simulation/Personnel/PersonnelDefection.cs
src/San9.Core/Simulation/Personnel/PersonnelTransitions.cs
src/San9.Core/Simulation/ResolutionPipeline.cs
src/San9.Core/Simulation/SimulationEngine.cs
src/San9.Core/Simulation/WorldInvariants.cs
src/San9.Core/Simulation/WorldMutation.cs
src/San9.Importer.San9PkTc101/Packages/LegacyOpeningUpgrade.cs
src/San9.Importer.San9PkTc101/Packages/LegacyPresentationUpgrade.cs
src/San9.Importer.San9PkTc101/Packages/LegacyScenarioDataUpgrade.cs
src/San9.Importer.San9PkTc101/Packages/LegacyScenarioStateUpgrade.cs
src/San9.Importer.San9PkTc101/Packages/LegacyTextureSkinUpgrade.cs
src/San9.Importer.San9PkTc101/Packages/TexturePackageImporter.cs
src/San9.Importer.San9PkTc101/Scenarios/ConstructionContentCompiler.cs
src/San9.Importer.San9PkTc101/Scenarios/MilitaryContentCompiler.cs
src/San9.Importer.San9PkTc101/Scenarios/ScenarioContentCompiler.cs
src/San9.Tools/LegacyContentUpgrade.cs
src/San9.Tools/Program.cs
tests/San9.Application.Tests/DeploymentTacticConfigurationTests.cs
tests/San9.Application.Tests/OriginalAiActions.ResearchTests.cs
tests/San9.Application.Tests/OriginalAiMilitary.MixedDeploymentTests.cs
tests/San9.Content.Tests/LegacyImageTableMigrationTests.cs
tests/San9.Core.Tests/DetachmentAllocationTests.cs
tests/San9.Core.Tests/FacilityDemolitionTests.cs
tests/San9.Core.Tests/MilitaryCombatTests.cs
tests/San9.Core.Tests/MilitaryMovementTests.cs
tests/San9.Core.Tests/ResearchProjectTests.cs
tests/San9.Core.Tests/SunMarriageTests.cs
tests/San9.Core.Tests/TacticCombatTests.cs
tests/San9.Importer.San9PkTc101.Tests/ConstructionContentCompilerTests.cs
tests/San9.Importer.San9PkTc101.Tests/ImportOperationTests.cs
tests/San9.Importer.San9PkTc101.Tests/LegacyPresentationUpgradeTests.cs
tests/San9.Importer.San9PkTc101.Tests/LegacyScenarioStateUpgradeTests.cs
tests/San9.Importer.San9PkTc101.Tests/LegacyTextureSkinUpgradeTests.cs
tests/San9.Importer.San9PkTc101.Tests/MilitaryContentCompilerTests.cs
```

## 运行验证后的追加变更（2026-11-09）

追加修改仅涉及 `DeploymentDetachmentsView.cs`、`DeploymentTacticMenu.cs` 与 `DeploymentView.cs`：调整自有界面样式、类别名称和帮助文字，沿用仓库现有主题与控件；未引入图片、字体、游戏资源、第三方代码或依赖。运行时临时下载的 Godot 编辑器与字体、测试存档和内容包均不进入提交。此前原版权利和授权不确定性的说明继续适用。

## 阵形列表提示与预览补充（2026-11-09）

后续变更仅增加自有阵位格子绘制、解锁提示及相关测试，使用已存在的阵位数据，不引入图片或其他第三方素材。验证使用隔离的本地升级内容包与存档，均不包含在提交中；原版权利和授权的限制仍同前述。

## 武将阵形页与说明补充（2026-11-09）

新增阵形说明由本次编写并依据项目实际规则描述；武将阵形页和引擎检查复用仓库现有控件及玩家本机转换内容，不包含原作图像、文本或测试内容包。原版权利与授权的限制继续适用。

## 装备系统工作区复核（2026-11-09）

检查当前装备改动及新增未跟踪文件：代码、中文规格和 Godot 文本场景均为本次编写；测试使用本机旧内容包，输入与生成包均位于提交范围之外。新装备名称、数值和界面由本项目定义，参考作品仅用于玩法构思，未引入参考作品的程序、图片、音频、字体或原文。当前清单和暂存状态仍可能变化，提交前须重新检查暂存差异及文件类型；原版权利人授权、商标与改编问题仍未核实，不能据此保证零法律风险。

2026-11-10 补核：[光荣特库摩公告](https://www.koeitecmo.co.jp/news/docs/news_20250627.pdf)说明《三國志曹操伝》相关新作品采用 IP 许可。该公告不能证明本项目已获授权，也不能单独决定本项目的法律性质；公开 PR 前仍需确认权利与项目维护者的接受条件。旧内容包和游戏运行截图可能包含受保护素材，仅用于本机验证，不加入 Git。

装备系统当前暂存范围共 97 个文件，包含 92 个 C# 文件、4 个 Markdown 文件和 1 个 Godot 文本场景；未发现二进制文件、内容包或截图。排除本审核记录后的 `git diff --cached --binary` SHA-256 为 `db4bceeb7509f8d0fae7947aba9adf626269b90ad61c61a000301daa56fdfdea`。这是路径与差异范围核对，不是对原版权利或全部源码相似性的法律鉴定；后续更改须重新核对。

后续装备迁移修正仅涉及类别校验、冲突拒绝、升级报告、自编测试和使用说明；本机验证内容及产生的报告仍位于忽略目录。该修正不引入外部素材或依赖，前述授权限制仍然适用。

玉玺信望修正仅调整仓库现有计算与装备状态的联动，并补充自编测试和行为规格；未引入原作文本、素材或第三方程序。该核查不改变上述权利与授权限制。

装备兵法引用的后续校验及其测试为本项目自编代码，不包含测试内容包或第三方素材；原版权利人授权仍未核实。

非服役武将的旧装备迁移修正及对应测试和规格为本项目自编；验证使用的旧内容包、生成包和游戏画面不纳入提交。前述授权限制仍然适用。

坐骑及宝物固定等级的状态校验和测试为本项目自编，不引入外部文本、素材或依赖；上述原版权利与授权限制不变。

旧宝物兵法引用校验和主副武器不兼容提示测试均为本项目自编，测试数据为虚构内容，不包含本机内容包或第三方素材；原版权利与授权限制不变。

## PR 当前完整差异范围复核（2026-11-10）

以 PR 基线 `9f6954a0383d0e3020c9901238355b02c28b0018` 和本地提交 `0db6910b` 对照，共 186 个变更文件：177 个 C# 文件、7 个 Markdown 文件、2 个 Godot 文本场景；`git diff --numstat` 未显示二进制差异，也没有依赖清单变化。排除此审核记录后的完整二进制差异 SHA-256 为 `72c1e4741d0ce0d51332eda581acc2405cfa62c1ba4e5b97718dc81f3f7d084f`。这项复核确认了待提交路径和文件类型，不能证明外部权利授权，也不替代对历史代码或全部相似内容的法律鉴定；后续修改须重新复核。

后续搭配冲突提示为本项目编写的短句，动态显示当前已装备物品名称；测试使用虚构物品，不附带内容包或第三方素材。此前差异摘要仅对应上述提交，最终推送前须重新计算。

补充复核至本地提交 `3b7058c0`：变更仍为 186 个文本文件，未发现二进制差异；排除本记录的完整差异 SHA-256 为 `546c6e883eb24f6bc50e6b58bd5f5d991d22c444f1e4aff5339aac5b8960bd1c`。本结论仍仅覆盖文件范围，不改变授权限制。

装备入口合并及兵符、暗器、书籍槽的新增代码、规则、配置和测试为本项目编写，未引入外部素材或依赖；本机内容包和运行画面不纳入提交。权利人授权仍未核实。

普通装备命名参考《霸王的大陆 GD 版》的基础装备分类、[《三国志曹操传》的普通武具分类](https://bkso.baidu.com/item/%E4%B8%89%E5%9B%BD%E5%BF%97%E6%9B%B9%E6%93%8D%E4%BC%A0/5353432)及《全面战争：三国》的武器和护甲分类，使用长枪、短剑、皮铠等通用名称；不复制游戏说明、素材或数值。参考资料不构成对相关作品的授权，最终提交仍须复核差异与权利风险。

本次装备品阶及基础属性门槛参考 GD 版公开介绍中的装备使用条件思路；品阶、属性门槛、效果、价格和程序均为本项目独立设定，未引入参考作品的代码、素材或说明原文。尚无权利人授权的事实不变，不能将本记录视为版权或改编授权结论。

## 个人爵位工作区检查（2026-11-11）

个人爵位的规则、界面、迁移及测试由本项目编写；历史初值仅记录人物、封号、保守适用日期及史料链接，不复制史料正文。使用本机内容包升级后的数据和运行画面保留在忽略目录，不纳入提交。与基线 `9f6954a0383d0e3020c9901238355b02c28b0018` 比较，已跟踪变更均为 C#、Markdown 或 Godot 文本场景；未跟踪文件及最终暂存范围仍须在提交前单独复核。该路径检查不能排除表达相似或改编权利问题；用户确认没有已知权利人授权，不能认定项目已获得许可。

后续初始装备规则与数值为本项目编写；短剑、纶巾及普通刀剑枪戟名称参考公开装备分类，未引入作品代码、文字说明或素材。参考资料链接和本项目选择见[装备规格](../../../specs/behavior/BEH-PERSON-0004-equipment.md)。工作区仍在开发，最终差异及授权风险须在推送前另行复核。

## 本次推送范围复核（2026-11-11）

本次游戏功能暂存范围为 102 个文本文件：95 个 C# 文件和 7 个 Markdown 文件；另有独立启动脚本提交，包含 1 个批处理文件、1 个 PowerShell 文件和 README 更新。检查暂存路径和二进制差异，未发现本机转换包、截图、原版程序或资源、私有样本与密钥。武将登场依据从玩家本地转换包提取，本次提交的是读取、升级和校验代码，不含提取的数据包。

上述检查是提交范围及来源记录，不能证明全部代码不存在相似表达或确认原作、名称及改编的权利；用户已说明没有已知权利人授权，故不宣称已获授权或不存在版权风险。

## 阵形依据工作区检查（2026-11-12）

个人阵形记录、校验、升级报告和教学测试为本项目编写；首批人物经历采用《三国志》相关篇目的链接及简短概述，游戏阵形对应和数值明确标为本项目推定。验证使用的本机旧内容包、升级产物和游戏画面保存在忽略目录，不纳入提交。此记录只说明本次材料来源及提交边界，用户没有已知原作品权利人授权的情况不变，不能由此断言整个项目不存在版权或改编权风险。

汉、魏、蜀汉和孙吴的游戏爵制及战功规则为本项目独立设定；时期依据与赵云爵位仅记录公开史籍出处和必要事实，不复制第三方作品的界面、素材或说明。相关本机升级数据不纳入仓库。上述原作品及改编授权风险不因本项核查而消除。

个人爵位分期数据引用《三国志》及地方沿革资料，记录人物、封号、保守适用日期和出处；实现、测试与升级报告为本项目编写。本机剧本和转换包不提交，此项来源核查不构成原作品授权或法律结论。

政绩累计、封爵排序、显示及兼容测试由本项目编写，测试使用虚构数据；验证使用的本机内容包不纳入提交。用户没有已知原作品权利人授权，原作品及改编授权风险仍未解决。

张辽的初始阵形补充仅引用《三国志·张辽传》的史事并标明游戏推定，不复制第三方游戏文字、代码或素材；本机内容及生成的升级包不提交。此核查不改变原作品和改编授权风险。

张辽爵位分期仅记录史籍所载爵号、保守适用年份及出处，不复制参考游戏的表达或素材；年份留空不代表推断精确转封日期。权利人授权情况不变。

## 军械重构及图标样稿（2026-11-12）

本阶段的军械命名参考[中国国家博物馆有关汉代军械的研究](https://www.chnmuseum.cn/yj/xscg/xslw/201812/t20181224_36339.shtml)及用户提出的历史题材要求，费用、攻防数值、库存与战损规则由本项目设定。代码、配置和测试为本项目编写；本机旧包、升级包、游戏截图和隔离存档均位于本地，不纳入提交。

新增40件装备的名称、属性数值、门槛和城市投放由本项目编写，统一标记为项目自创；参考作品用于分类和设计方向，不复制其具体物品描述、图像或代码。部分物品使用本项目设定的部队攻防及兵法效果，其余仍以能力加成为主。新增装备图集和长戟样稿均使用内置 imagegen 从文字生成，未输入第三方游戏图片；提示词和素材状态见[图标来源记录](../../development/equipment-art-provenance.md)。图集已接入新增物品名单和详情，旧名物逐件图标尚未制作；最终提交前仍须核对来源和差异。

用户明确表示没有已知原作品权利人授权。上述来源记录和独立素材不能消除项目整体的改编权、商标或其他权利风险，也不能作为无版权问题的结论。

后续新增赤兔马、方天画戟和流星镖图标均由内置 imagegen 根据文字生成，未使用参考游戏画面作为输入；提示词要点及实机检查状态见[图标来源记录](../../development/equipment-art-provenance.md)。本次提交仅包含项目代码、规格、测试和自制图像，不包含本机转换包、原版资源或隔离存档。授权情况和项目整体权利风险不变。

2026-11-13 补充的青龙偃月刀、蛇矛图标同样由文字生成，依据演义名物作游戏化构图，未输入参考游戏画面；不将其造型视为汉末实物复原。相关图像来源及验收范围见[图标来源记录](../../development/equipment-art-provenance.md)。

2026-11-13 补充的的卢、绝影图标由文字生成，未输入参考游戏画面；马匹的毛色与马具是项目的视觉设计，不作为历史个体的复原。图标来源与实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。上述素材来源记录不构成对原作品改编的授权，目前仍无已知权利人授权。

同日补充的爪黄飞电图标也由文字生成，未输入参考游戏画面；毛色及马具为视觉设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的倚天剑、青釭剑图标分别由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的七星宝刀、雌雄一对剑、古锭刀图标由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的铁脊蛇矛、三尖刀、双铁戟和大斧图标由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的铁鞭、铁蒺藜骨朵、流星锤和眉尖刀图标由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的大宛马、四轮车、铜锤和铁锁图标由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的飞刀、短戟、手戟和袖箭图标由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的养由基弓与李广弓图标由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的孙子兵法、兵法二十四篇、六韬和三略图标由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。

同日补充的司马法、吴子、孙膑兵法和魏公子兵法图标由文字生成，未输入参考游戏画面；图像为独立设计，来源和实机验收状态见[图标来源记录](../../development/equipment-art-provenance.md)。原作品授权情况不变。
