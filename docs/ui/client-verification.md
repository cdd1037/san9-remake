# Godot 历史画面验收

这些脚本用于复现已有画面和引擎交互场景，不属于日常自动测试，也不是后续开发的默认交付步骤。后续开发默认不执行以前的画面验收脚本；只有当前任务明确要求复现相应场景时才按需运行，不因修改相关模块或执行全量测试而自动回跑。

日常改动按风险编译客户端、运行受影响的 C# 测试。当前功能需要画面验收时，针对本次变化验收；用户负责画面验收时，交付说明哪些画面待确认，不以运行历史脚本或自行截图作为前置条件。引擎输入自检、GPU 截图检查和操作系统交互是三种不同证据，不能互相替代。

## 分类与实现

历史脚本集中在 `src/San9.Client.Godot/Diagnostics/Verification/`，通过客户端启动参数显式启用。无验收参数时不创建验收节点。`Diagnostics/DebugPanel.cs` 是日常 F3 调试面板，与历史验收分开维护。

| 类别 | 参数 | 范围与实现 |
| --- | --- | --- |
| 地图 | `--map-smoke` | `MapVerification.cs`：选点、缩放、拖动、边界、换季、导航和帧耗时采样 |
| 主界面 | `--ui-smoke` | `StrategyHudVerification.cs`：按钮、名单焦点、滚动、图层、输入阻挡；`InterfaceVerification.cs`：界面状态截图 |
| 情报 | `--information-smoke` | `InterfaceVerification.cs`：菜单、十类页面、键盘详情与返回、只读状态检查和截图 |
| 运行态接线 | `--runtime-smoke` | `ClientVerification.cs`：一次经营命令及其在地图、HUD 的刷新，重复命令拒绝 |
| 字体与样式 | `--text-smoke` | `TextVerification.cs`、`TextStyleSheet.cs`：字体覆盖、富文本图标、不同字号及文字适配器比较页 |
| 单张截图 | `--map-screenshot <PNG路径>` | 不启用其他检查时，只等待画面准备完成并截图 |

`ClientVerification` 统一编排世界场景、异常和退出；`VerificationSupport` 合并帧等待、视口就绪、纹理就绪及 PNG 保存，保存失败会报错。情报检查与主界面截图流程放在同一文件；HUD 私有控件的检查仍保留为 `StrategyHud` 的 partial，避免为测试公开控件内部状态。

`--ui-smoke` 只运行主界面检查，不再隐式运行地图检查和性能采样。需要二者时显式传入 `--map-smoke --ui-smoke`。地图、主界面、情报和运行态参数可组合；字体样本是独立画面，`--text-smoke` 必须单独运行，不能与世界检查或地图截图参数混用，以免静默忽略请求。

## 按需复现

在仓库根目录执行，使用 Godot Mono 和已经转换的客户端内容包。下列命令仅作为按需入口，不是开发检查清单：

```powershell
# 单独复现主界面；不会附带地图检查。
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --ui-smoke --map-time-ms 0 --map-screenshot "<绝对输出目录>/ui.png" --map-quit

# 地图与主界面同时复现时显式选择两类。
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --map-smoke --ui-smoke --map-time-ms 0 --map-screenshot "<绝对输出目录>/map-ui.png" --map-quit

# 字体样本独立运行，完成后自动退出。
& "<Godot Mono可执行文件>" --path src/San9.Client.Godot -- --text-smoke
```

将第一条中的 `--ui-smoke` 换为 `--information-smoke` 或 `--runtime-smoke` 可复现对应场景。截图需要图形渲染；无图形模式不能作为 GPU 画面验收。字体入口在无图形模式下仅做逻辑检查，不输出 PNG。`--map-quit` 让世界场景完成所有检查与截图后退出。

输出统一放在忽略的 `local/generated/verification/` 下，原位覆盖，不为每次开发保留副本。默认包下，字体输出位于 `verification/text/`；情报未指定截图路径时输出至 `verification/information/`，不写入工作目录。指定截图路径时，界面变体、导航、运行态前后及情报页面图片写在其同级目录。

## 使用边界

这些是历史场景脚本，包含 1024×768 逻辑坐标、默认 190 年包中的人物／设施和资源假设，不能当作任意 Mod 的通用验收器。运行态场景仍使用势力 5、据点 15 的受控经营夹具，只调整内存初始输入，不修改磁盘内容包。复现前按当前任务核对所需包和场景；无关历史假设不阻塞日常开发。

历史成功日志和截图只说明当时结果。脚本重构后的编译与 C# 测试不代表重新完成 GPU 画面验收。
