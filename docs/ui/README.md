# UI 文档

界面开发从本页进入。`docs/ui/` 集中维护当前界面实现与通用约定；行为要求仍由 BEH 保存，重要取舍由 DEC 保存，原作证据由 RE 保存。本页提供统一导航，不复制这些正文。

剩余接入顺序、进入战略阶段时的情境发言、IDA 核对与验收边界见[表现层后续接入规划](../../specs/presentation-implementation-plan.md)。BGM 和视频继续保持占位。

## 实现与通用约定

| 文档 | 查阅内容 |
| --- | --- |
| [战略界面](strategy-ui.md) | 状态所有权、输入、窗口缩放、侧栏、小地图、建筑悬停 |
| [情报界面](information-browser.md) | 目录、详情、导航、行政查询、情报地图及当前边界 |
| [坐标对齐](ui-layout-alignment.md) | 截图标定、透明留白、共同锚点与布局量测 |
| [文字样式](text-styles.md) | 字体、角色与状态、描边阴影、样式覆盖 |
| [部队外观](../../specs/behavior/BEH-RENDER-0003-map-ambient-animation.md#阵型身体数量与站位) | 阵型身体、三类兵群数量与坐标、兵器护卫及固定船型 |
| [消息编写](messages.md) | BBCode、图标、参数绑定、消息求值与词汇校订计划 |
| [地图消息测试](map-messages.md) | 首批计略喊话与左下记录接线、第40号本地测试存档及后续范围 |
| [画面验证](client-verification.md) | 验收分工、历史脚本分类、按需复现命令与限制 |

## 行为与设计依据

P1 已接通的计略、主动外交与军团管理见[操作与验证说明](mission-corps-commands.md)；实际画面由用户验收。

| 主题 | 行为规格 | 重要取舍 |
| --- | --- | --- |
| 主界面与布局 | [初始战略界面](../../specs/behavior/BEH-UI-0001-initial-strategy-screen.md) | [桌面宽屏与固定内容区域布局](../../specs/decisions/DEC-UI-0008-desktop-widescreen-layout.md)（动态视口、公共宿主、HUD分组与确认框scene已接入） |
| 侧栏名单 | [武将名单](../../specs/behavior/BEH-UI-0002-officer-roster.md) | [设施名单交互](../../specs/decisions/DEC-UI-0004-facility-list-interaction.md) |
| 战略全图 | [全图面板](../../specs/behavior/BEH-UI-0003-strategy-overview.md) | [导航与悬停](../../specs/decisions/DEC-UI-0006-overview-navigation.md)、[相机边界](../../specs/decisions/DEC-UI-0003-continuous-map-camera-boundary.md) |
| 情报浏览 | [目录与详情](../../specs/behavior/BEH-UI-0004-information-browser.md) | [运行态与交互](../../specs/decisions/DEC-UI-0007-information-browser.md)、[程序四维图](../../specs/decisions/DEC-RENDER-0003-procedural-officer-radar.md) |
| 建筑悬停 | [悬停卡](../../specs/behavior/BEH-UI-0005-map-building-hover.md) | — |
| 存读档界面 | [功能菜单、栏位与确认](../../specs/behavior/BEH-UI-0007-save-load-browser.md) | [原作证据](../../research/findings/RE-SAN9PK-0180.md) |
| 文字与消息 | [统一消息](../../specs/behavior/BEH-UI-0006-unified-messages.md) | [现代文字效果](../../specs/decisions/DEC-UI-0002-modern-text-effects.md)、[姓名规范化](../../specs/decisions/DEC-UI-0005-officer-name-normalization.md) |
| 世界事件演出 | [事件行为与播放](../../specs/behavior/BEH-UI-0010-world-event-presentation.md) | [IDA证据及资源](../../research/findings/RE-SAN9PK-0196.md) |

原作调查入口：[主界面 RE-0158](../../research/findings/RE-SAN9PK-0158.md)、[目录 RE-0151](../../research/findings/RE-SAN9PK-0151.md)、[情报 RE-0161](../../research/findings/RE-SAN9PK-0161.md)、[消息 RE-0166](../../research/findings/RE-SAN9PK-0166.md)、[文字样式 RE-0167](../../research/findings/RE-SAN9PK-0167.md)。各专题中的精确证据链接继续保留。

## 相邻模块

- [内容管线](../client-content.md)：转换命令、包契约、数据覆盖与校验。
- [纹理资源](../texture-resources.md)与[字体资源](../../src/San9.Client.Godot/assets/README.md)：资源准备、生命周期与许可证。
- [地图导入与运行](../map-import.md)及[地图坐标](../map-coordinates.md)：地图渲染和领域坐标。
- [运行时架构](../runtime-state.md)：会话、领域状态与查询边界。

## 维护方式

新增界面说明放在本目录，优先更新已有专题，并补充本页入口。同一约定只保留一份正文，其他文档使用链接；内容管线只记录转换和契约，不再重复界面交互。普通修复经过、临时截图和重复验收命令不累计到实现文档。规格与研究按[规格维护规则](../../specs/README.md#规格维护)分别更新，不将研究结论自动作为产品要求。
