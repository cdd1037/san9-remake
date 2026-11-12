# 项目文档

当前能力与启动步骤见[根README](../README.md)。本目录维护使用、实现和开发说明；采用行为归[规格](../specs/README.md)，原作事实归[研究](../research/README.md)，各主题只维护一份正文。

## 使用与内容

| 需求 | 入口 |
| --- | --- |
| 转换、校验、准备与导出内容 | [内容管线](client-content.md) |
| 命令脚本、保存恢复与退出码 | [Headless](headless.md) |
| 界面、交互与显示约定 | [UI索引](ui/README.md) |
| 音频、影片资源准备 | [音频](audio-preparation.md)、[影片](movie-preparation.md) |
| 本地设置与跨局资料 | [用户设置](user-settings.md)、[玩家档案](player-profile.md) |
| 独立公共资料编辑 | [资料编辑器](shared-data-editor.md) |
| 剧本初始状态编辑 | [剧本编辑器](scenario-editor.md) |
| 地貌、地域与据点布局编辑 | [地图编辑器](map-editor.md) |

## 架构与开发

| 主题 | 入口 |
| --- | --- |
| 状态所有权、事务、快照与查询 | [运行时架构](architecture/runtime-state.md) |
| 地图逻辑位置与显示投影 | [坐标](architecture/map-coordinates.md)、[导入与运行](map-import.md) |
| 图像分类、缓存与生命周期 | [纹理资源](texture-resources.md) |
| 协作、文档、工程与研究流程 | [开发规范](development/README.md) |
| 诊断比较、任务身份与资源指纹 | [状态诊断](development/state-diagnostics.md) |
| 验证范围与入口 | [测试说明](../tests/README.md)、[引擎检查](ui/engine-verification.md) |
| 旬推进及AI测量 | [性能记录](ai-performance-records.md) |

## 规格、研究与历史

- [行为规格与决策](../specs/README.md)：查已采用行为、边界和验收；[计划目录](../specs/plans/README.md)维护阶段与未来工作。
- [研究入口](../research/README.md)：查原作来源、格式、实验与证据；开局、AI等研究不维护产品接入状态副本。
- [历史资料](history/README.md)：保存有复现价值的阶段验收、专项审计、旧设计与性能测量；普通修复过程由Git追溯。
