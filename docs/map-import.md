# 大地图导入与运行

本页维护当前地图转换、合成、视野提交与调试入口。精确资源契约见[纹理资源](texture-resources.md)和[界面与演出内容](ui/presentation-content.md)，早期接入及性能测量见[历史记录](history/rendering/map-rendering-verification.md)。

## 导入

地图资源由独立`import-textures`构建，`textures.json.map`保存四季地貌、遮罩、建筑组合与动画。`import-client`读取共享资源定义并编译剧本实例；`map.json`引用版本 2 的地图基础定义并保存剧本据点显示位置；地貌绘制记录、高差、疆界、水面及地图外缘由公共构建从基础格和资源主题生成。小地图底图由客户端按当前方格合成，资源范围不受首屏和190年初始可见性限制。

常规完整准备使用[内容管线](client-content.md#转换与运行)；独立转换与离线地图导出示例：

```powershell
dotnet run --project src/San9.Tools -- import-textures --game-dir "private/game-files" --output "local/generated/content/shared/textures"
dotnet run --project src/San9.Tools -- import-client --game-dir "private/game-files" --scenario "D_Sce002.S9" --texture-package "local/generated/content/shared/textures" --output "local/generated/content/scenario-190-live"
dotnet run --project src/San9.Tools -- render-map --content-package local/generated/content/scenario-190-live --output local/generated/verification/map.png --terrain-only true
```

Importer识别已登记的繁中PK 1.0.1.0样本；客户端运行时不读取源EXE、S9、调色板或研究脚本。纹理构建负责复用及失效清理，剧本导入不重写共享纹理。只有契约或资源实际失效时才重新转换，不按旧验收步骤固定重建。

## 动态显示

显示时钟独立于模拟日期；水面、守兵、旗面与旗字按资源声明选帧。建筑部件由当前开发度、耐久、归属和驻兵派生，无驻兵建筑不显示守兵，静态资源不替代领域状态。

新局／读档和跨季推进按日期选择季节，调试预览可独立覆盖；换季保留时钟和相位，资源就绪后统一切换。季节动画与具体帧组以[地图动画规格](../specs/behavior/BEH-RENDER-0003-map-ambient-animation.md)为准。

地图方格、显示投影、拾取和兵群坐标见[地图坐标](architecture/map-coordinates.md)。地图动画和表现状态不改变领域寻路与战斗规则。

## Godot

使用Godot 4.7.2 Mono与.NET 10打开`src/San9.Client.Godot/project.godot`并构建C#。无参进入开局菜单；指定组合包可直接进入对应世界：

```powershell
& "<Godot Mono可执行文件>" --path "src/San9.Client.Godot" -- --content-package "<客户端内容包绝对路径>"
```

中键拖动、滚轮以鼠标为中心缩放；地图选择、右键情报、WASD和小地图导航按[战略界面](ui/strategy-ui.md)执行。F3提供地图状态、资源统计及季节预览，内政和旬推进使用正式菜单与“进行”。

## 视野与资源所有权

请求视野与已呈现视野分离。地形块和对象／动画纹理就绪后统一提交位置、缩放与可见集合；准备期间保留旧帧，小地图白框、拾取和标注读取同一已呈现相机。换季先准备完整季节帧，资源失败保留原画面并允许重试。

地形按512×512块后台合成，外围一圈预取；相同地形源由多视图共享，各视图独立维护相机和可见集合。水面及对象的显示索引只服务裁剪，不参与领域规则。逻辑图片、物理图集、CPU缓存与GPU缓存的所有权和预算统一见[纹理资源](texture-resources.md)。

## 验证入口

验证按[测试说明](../tests/README.md)选择受影响范围。离线`render-map --content-package`可按剧本初始态绘制静态对象；独立`--package`缺少据点运行值，必须显式指定`--terrain-only true`。完整导出尺寸为13056×6656，离线PNG内存占用不代表客户端缓存占用。

研究侧`research/scripts/verify_map_package.py`需要Python与Pillow，仅用于原版样本比较，不参与产品运行。历史Godot画面场景只在任务明确要求复现时使用，参数、限制与分工见[画面验证](ui/client-verification.md)。旧截图和日志路径不保证仍存在，不能把历史性能数字当作当前版本重新通过。
