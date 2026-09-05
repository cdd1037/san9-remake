# 大地图导入与运行

MVP 显示190年剧本的初始地图快照，并播放水面、建筑旗帜与守兵动画。四季各771张地貌RGB纹理，布局与遮罩共用；桥、建筑、云使用RGBA。所有资源在本地生成，仓库不包含原版素材。

建筑资源独立于当前场景状态导出：三种城池外观×四个耐久阶段×三个开发档位的城墙、城内建筑及对应阴影全部包含在包中，同时包含其余已确认建筑组合引用的纹理。30项已确认组合去重后共154张建筑PNG，位于包内 `objects/building-*.png`，组合顺序与位置在 `map.json` 的 `compositions` 中。未确认的其他原版组合不在此目录承诺范围内；游戏仍只显示190年初始状态。

2026-09-05实际验收：36种城池状态的全部纹理引用存在，所有资产SHA-256通过；原有60张建筑纹理和其余旧资产逐字节不变，场景及组合定义不变。新增94张，地图包增加993226字节，现为73302260字节（约69.91 MiB）。150项自动测试通过。默认包已更新，上一版保留为 `local/generated/maps/scenario-190-before-catalog`。

## 导入

在仓库根目录运行（替换 EXE 路径）：

```powershell
dotnet run --project src/San9.Tools -- import-map --source-exe "<已登记的San9PK.exe>" --data-dir "private/s9数据文件" --scenario "private/s9数据文件/D_Sce002.S9" --output "local/generated/maps/scenario-190"
```

EXE须通过繁中PK 1.0.1.0指纹校验。输出目录必须不存在，避免覆盖已有内容；重新转换时指定另一个目录。转换先写临时目录，验证通过后才成为可加载包。map.json保存版本、源哈希、图像哈希、方格、顺序化地貌描述、建筑实例与组合。

地貌PNG是原纹理的四季彩色版本，不是预渲染大地图。运行时不读取源EXE、S9文件、调色板或研究脚本。四季地貌PNG约55.5 MiB。

## 动态显示

当前地图包schema为3，旧版须重新导入。`animations`记录片段、帧时长和实例，独立PNG帧位于 `animations/`。四类水面各六帧/200ms，以179/256源亮度加色显示；八方向守兵各八帧/100ms；26组当前场景旗色/旗字各包含春夏四帧与秋冬四帧，均142ms。共7818格水面、247面旗帜、335个守兵站位。无驻兵建筑不显示守兵。

默认 `scenario-190` 已更新为含季节旗帜的schema3包（74333580字节，约70.89 MiB），共3873项PNG。原有动态包重复导出逐字节一致；本次新增秋冬旗帜帧，旧资产及场景实例保持不变。旧包已按用户要求移入回收站。

动画采用独立显示时钟，暂停模拟日期不停止飘旗，切季不重置相位。旗帜已按当前预览季节切换：春夏方向1、秋冬方向5，旗字使用对应源帧，换季保持动画时间和初相位。此处没有确认独立风向系统；其他动态对象的完整全局遮挡排序也尚未接入。城市数值、静态纹理、初始场景实例不变；离线 `render-map`仍是静态基线导出。

用 `--map-time-ms 0` 或 `--map-time-ms 200` 固定截图时间；省略则实时播放。实际GPU截图 `local/generated/maps/verification/animation-0.png` 与 `animation-200.png` 已检查水面、旗帜、守兵三个独立区域都有像素变化。156项自动测试通过，Godot四季/选择/缩放/拖动/边界与120帧平移检查通过（中位6.24ms、P95 6.34ms）。这仍是引擎内测试及GPU截图，不是操作系统鼠标自动化。证据见RE-SAN9PK-0156，行为见BEH-RENDER-0003。

## Godot

使用Godot 4.7.2 Mono和.NET 10打开 `src/San9.Client.Godot/project.godot`，先构建C#工程，再运行主场景。默认读取上述输出位置；其他位置可通过游戏参数指定：

```powershell
& "<Godot Mono可执行文件>" --path "src/San9.Client.Godot" -- --map-package "<地图包绝对路径>"
```

- 右键拖动，滚轮以鼠标为中心缩放，左键选方格。
- 左上季节选择切换四套外观；准备完成后统一替换当前视野。
- 初始定位洛阳；可用 `--map-center 133,3 --map-season winter` 预览乌丸边缘。
- 模拟推进旬的按钮仍保留，但地图建筑和季节不随模拟变化；这是初始快照浏览器。

显示纹理按512×512分块后台合成，可见区外围一圈预取，显示缓存预算256 MiB。通常只加载当前季节，换季准备期间两季可能暂时共存。预算不包含源纹理、托管运行时和Godot本身。

## 验证入口

```powershell
dotnet test San9Remake.slnx
dotnet run --project src/San9.Tools -- render-map --package local/generated/maps/scenario-190 --season spring --x 6880 --y 2610 --width 800 --height 450 --terrain-only true --output local/generated/maps/verification/product-terrain.png
python -B research/scripts/verify_map_package.py "<已登记EXE>" private/s9数据文件 local/generated/maps/scenario-190 local/generated/maps/verification
```

研究比较脚本需要Python和Pillow，仅用于私有样本验收，不参与客户端运行。`render-map` 默认包含静态对象，`--terrain-only true`只输出地貌/底云。完整导出使用 `--width 13056 --height 6656`；离线PNG导出会占用较多内存，不代表客户端分块缓存占用。

Godot图形模式验收参数：`--map-smoke --map-screenshot <绝对PNG路径> --map-quit`。它验证坐标选择、鼠标中心缩放、拖动、相机边界、四季切换并记录连续平移帧时间；截图需要图形渲染，不能用无图形的headless模式替代。额外季节和边缘截图可使用 `--map-season`、`--map-center`。

来源见RE-SAN9PK-0053/0055/0056/0148，首次验收边界见下文历史记录。标准透明混合的细微色差由DEC-RENDER-0002批准；原作截图中的RGB555量化也不在重制渲染范围内。

### 透明边缘回归（2026-09-05）

事实：旧包 `building-5683-key` 的24255个透明像素保留 `(0,248,0,0)`，在1.728倍双线性采样中可复现城市轮廓绿边。按用户确认的资源流程，转换器现在将邻近可见颜色扩展到完全透明像素，只改变隐藏RGB。导出的PNG直接使用Godot默认材质；客户端没有预乘或边缘修复。旧包需要重新转换，不能仅更新客户端。

实际重新转换并逐像素对照：74张RGBA共994435个完全透明像素的隐藏RGB变化；所有Alpha、可见像素、3084张四季地貌、其他图像以及布局数据不变。150项自动测试通过，包含透明边缘的普通插值、PNG往返、全透明图和可见颜色保持。

复现截图参数为 `--map-zoom 1.728 --map-screenshot <绝对PNG路径> --map-quit`，本地前后截图为 `local/generated/maps/verification/green-before.png` 和 `border-export.png`；转换后的PNG在默认材质下，该视野的建筑、栅栏和阴影绿色轮廓消失。默认 `scenario-190` 已更新，旧包保留为同目录 `scenario-190-before-border`。此验证是Godot实际GPU渲染及截图检查，不是操作系统鼠标自动化；`--map-smoke`也通过程序直接调用输入处理，不能代替完整桌面交互验收。

季节旗帜补充验收：新增104张秋冬PNG；26组旗帜四季引用齐全，春夏帧和其余旧资产逐字节不变。157项自动测试通过，包括换季相位保持和季节帧组校验。实际GPU春冬对照见 `verification/season-flags-spring.png`、`verification/season-flags-winter.png`，换季交互测试记录见 `verification/season-flags-smoke.log`。旧包按用户要求移入回收站，默认目录只保留当前包。

## 首次静态地图接入验收（2026-09-05）

以下为首次静态包的历史实验数据，包大小、资源数及测试数不代表当前版本。当时未验证的水面、旗帜和守兵现已由 RE-SAN9PK-0156 补充验证；其余限制保留。来源为 RE-SAN9PK-0053/0055/0056/0148 与独立转换实验。


- C# 转换产物：40000格、96489条地貌绘制描述、16桥、94建筑、220前景云，所有引用闭合。
- 四季地貌PNG合计55.502 MiB；完整地图包72,223,287字节（约68.877 MiB），3483个图像资源。
- 两次独立导入 map.json 字节一致，清单SHA-256为 `8B7E2D286A6F70ADE9B1A7847667319D540829C9491FE423194E2C4BD5DAA354`，资源哈希一致。
- `verify_map_package.py` 用研究侧全图插入排序后裁剪，比较世界画布 `(6880,2610,800,450)`，360000个地貌像素零差异。若先裁方格再排序，会因同键历史不同而出现22个单色阶差异；该做法不适合作为全图分块精确基线。
- 完整冬季图 `13056×6656` 已导出并目视检查；Godot 洛阳春季与乌丸冬季边缘截图已检查。
- Godot 4.7.2 Mono、RTX 5070 环境：首次准备约1.2秒；连续切换季节约0.7秒。此为单机观察，不外推其他硬件。
- 1280×720窗口追加自检：首次1.352秒，四次换季0.736–0.800秒，连续120帧平移中位6.24ms、P95 6.48ms，进程峰值436 MiB，显示缓存估算77 MiB。主循环帧间隔为实测指标，不等同于GPU独立耗时。
- 两侧逐行云边界与独立参考逐值一致；220个前景云的资源选择与位置逐项一致。
- 同一800×450区域透明混合对照（两侧均保留RGB888，只比较标准Alpha与原128权重式）：11361个像素有差异，平均通道差0.037937/255，最大4/255，23个像素超过满量程1%。验证脚本在存在 product-composite.png 时复算。此值不包含RGB555量化差异，不外推所有场景。
- .NET 148项测试、原研究渲染48项测试通过；Godot图形模式的选择、鼠标中心缩放、拖动、缩放上下界、黑边输入防护及四季切换自检通过。
- 自动夹具不需要私有样本；实际源图与运行截图位于被忽略的 local/generated/maps/，不提交。
- 未验证：其他19个正式剧本、组合31、动态水面/建筑/部队、完整原版动态对象遮挡。未进行全图原作截屏逐像素配准。

复现：按 README 的 import-map 生成包，然后运行 `python -B research/scripts/verify_map_package.py <已识别EXE> <数据目录> <地图包> <验证输出目录>`；参考色差比较前先用 San9.Tools render-map 导出同坐标 product-terrain.png。

这里的 Godot 自检通过程序调用输入处理并检查实际 GPU 截图，未进行操作系统鼠标自动化。
