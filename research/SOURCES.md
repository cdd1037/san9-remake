# 来源登记表

本表只登记当前工作区可见来源。登记不表示其结论已经验证，也不表示项目取得再分发权。更详细记录后续可拆分为独立 `SRC-*` 文件。`private/` 下的样本仅供本地研究，路径统一使用仓库相对路径。

| ID | 本地路径 | 内容与用途 | 来源/版本状态 | 许可证与再分发 | 项目可信度 |
| --- | --- | --- | --- | --- | --- |
| `SRC-DOCS` | `private/research/docs/` | 电子书、程序分析、设计公式与配图；用于定位待验证机制 | 文件来源与版本未逐项登记 | 待确认；当前仅本地研究 | 低至中：只能作为线索，须按结论复验 |
| `SRC-SAN9WPK-STEAM-MANUAL` | `private/San9WPK.pdf` | Steam 中文版官方操作指引及原版/PK 手册摘录；[来源记录](sources/SRC-SAN9WPK-STEAM-MANUAL.md) | 用户提供；版权标示 2017，34 页；对应 EXE 构建未识别 | 商业文档；不得提交或再分发 | 官方说明来源；精确条件须与样本交叉验证，军师替换见 `RE-SAN9PK-0112` |
| `SRC-SAN9PKHARD` | `private/research/upstream/San9PKHard/` | 旧 MFC 强化工具源码、配置与注入实现；用于发现候选参数、地址、表布局和业务语义 | 未发现上游 URL、提交或发布版本记录 | 待确认；本项目不复制其实现代码 | 中至高：地址、数量和步长与目标访问器吻合时可作为工作事实；行为规格另行验证 |
| `SRC-KAODATA` | `private/research/upstream/kaodata-main/` | 光荣旧游戏数据研究工具；本地副本已裁剪为 SAN9 头像解析入口及其依赖 | README 标识项目为 `kaodata`，本地快照提交未知 | MIT，版权声明为 2023 Tzeng Yuxio；再分发须保留声明 | 中：代码可审阅，但 SAN9 人物解析未实现，头像格式仍需复验 |
| `SRC-JSLHCL` | `private/research/upstream/jslhcl.github.io/files/` | SAN9PK 多武将探索补丁、辅助脚本和逆向笔记；用于发现候选调用链和业务名称 | README 给出繁中 PK 构建与补丁哈希，但本地快照来源/提交未知 | 待确认；本项目不复制其补丁实现 | 中：目标字节、调用链或 vtable 吻合时可采用工作名称；行为规格另行验证 |
| `SRC-LEGACY-IDB` | `private/ida/legacy/San9PK.exe.idb` | 已归档的前人旧版 IDA 数据库；用于提供函数边界、类型和名称候选，不承载项目新标注 | 所分析输入 SHA-256 为 `893296…FF04`，与项目基线不同；创建者和来源未知 | 待确认；归档文件被 Git 忽略且不得提交 | 中：只在局部字节或结构匹配后采用；不能单独支撑行为规格 |
| `SRC-SAN9PK-TC-101` | `private/游戏文件/` | 统一原版研究与转换基线；包含San9PK.exe、剧本、游戏数据和资源 `.s9` 文件，DLL亦同层存放 | 繁中 PK 1.0.1.0，样本版本与文件清单见 [`s9-file-inventory.md`](sources/s9-file-inventory.md) | 商业游戏数据；不得提交或再分发 | 高：样本身份已确认，具体行为仍需实验或逆向验证 |
| `SRC-SANCALC-SAN9TC-001` | `private/游戏文件/SanCalc.dll` | EXE 使用的数学、随机数、概率与位字段运行库 | 114,688 字节；SHA-256 与独立[来源记录](sources/SRC-SANCALC-SAN9TC-001.md)一致 | 商业游戏二进制；不得提交或再分发 | 高：精确样本已确认，函数契约见 `RE-SANCALC-0001` |
| `SRC-KOEIGRPX-SAN9TC-001` | `private/游戏文件/KOEIGrpX.dll` | EXE 使用的索引图、RGB555、KBMP 与像素混合运行库 | 90,112 字节；SHA-256 与独立[来源记录](sources/SRC-KOEIGRPX-SAN9TC-001.md)一致 | 商业游戏二进制；不得提交或再分发 | 高：精确样本已确认，函数契约见 `RE-KOEIGRPX-0001` |
| `SRC-KOEIWMA-SAN9TC-001` | `private/游戏文件/koeiwma.dll` | 结局停止音乐的渐停参数与等待时间 | 90,112字节；版本1.0.0.0，精确哈希见[来源记录](sources/SRC-KOEIWMA-SAN9TC-001.md) | 商业游戏二进制；不得提交或再分发 | 静态参数已核对；见 `RE-SAN9PK-0181` |
| `SRC-EDITOR-SCREENSHOTS` | `private/剧本编辑器截图/` | 剧本编辑器字段、数值和显示标签的黑盒对照 | 用户提供的本地截图；按文件名逐项引用 | 截图不得提交或再分发 | 中：只能作为界面对照，不能单独支撑文件字段语义 |
| `SRC-SAN9PK-JP-PATCH-HISTORY` | [`research/sources/SRC-SAN9PK-JP-PATCH-HISTORY.md`](sources/SRC-SAN9PK-JP-PATCH-HISTORY.md) | 日本版 PK 2003-09-04 至 2005-08-10 修正履历的中文转译；用于建立跨版本差异和实验候选 | 最终发布日期由 GAMECITY 官方入口确认；逐条内容来自用户提供的日文转录 | 商业游戏官方文档，许可状态待确认；仅登记中文事实性转译 | 中至高：可确认版本修正意图，不能直接证明繁中 PK 1.0.1.0 的当前行为 |

| `SRC-SAN9-RECRUIT-COMMUNITY` | [来源记录](sources/SRC-SAN9-RECRUIT-COMMUNITY.md) | 2003 年媒体评测、Steam 玩家指南与论坛的反复登用说法对照 | 精确构建未知；2026-09-06 检索 | 未取得转载许可；仅自行摘要与链接 | 仅支持说法存在及意图假说，不替代 TC-101 实测 |

## 维护要求

- 新来源必须记录原始 URL/发布者、获取日期、版本或提交、许可证、适用游戏版本与必要哈希。
- “许可证待确认”的资料不得复制进实现，也不得在未来公开仓库中默认分发。
- 来源可信度评价针对其在本项目中的证据作用，不评价原作者能力。
