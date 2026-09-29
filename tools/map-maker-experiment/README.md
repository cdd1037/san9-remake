# 山河：Godot 地貌编辑实验

当前入口为独立 Godot .NET 工程 `project.godot`。每格只保存 **类型 `type`、高度 `height`、纹理变体 `variant`**，不包含城市、地域、通行或剧本。预览使用现有转换包的真实地貌与遮罩，自动重建高差衔接、地形过渡、资源选择和绘制顺序。

## 启动

需要 Godot 4.7.2 .NET、.NET 10，以及已经转换好的纹理包。在仓库根目录运行：

```powershell
./tools/map-maker-experiment/start.ps1

# 若 Godot 不在 PATH，或素材包位于其他地方：
./tools/map-maker-experiment/start.ps1 -GodotPath '<Godot .NET 可执行文件>' -TexturePackage '<含 textures.json 的目录>'
```

脚本先构建，再打开独立工具窗口。也可在 Godot .NET 中打开本目录 `project.godot`，构建 C# 后运行。默认素材包为 `local/generated/content/textures-san9pk-tc101`；缺少时界面提示，可点击「素材包…」重新选择。工具只读取转换包，不读取 EXE、S9 或研究数据，不修改游戏客户端。

## 编辑

- 选择类型、高度、变体，左键拖动涂刷。每次按下至松开算一笔，快速拖动插值补格。
- 可选择只刷其中一个字段，其他字段保持不变；目标组合不受素材支持时跳过该格。默认「涂刷三个字段」能直接切换完整组合。
- Ctrl+左键吸取当前可见地貌的三个字段；底部显示格坐标及值。
- 右键或中键拖动，滚轮以指针为中心缩放，「适应视图」查看全图。
- Ctrl+Z / Ctrl+Shift+Z 撤销／重做，最多保留 50 笔；历史不跨工程或重启保存。
- 保存／打开独立 JSON 工程；新建、打开或退出前有未保存修改会询问是否放弃。没有自动保存。
- 「导出预览 PNG」写到 `local/generated/terrain-editor/terrain-preview.png`，重复导出覆盖该文件。

类型暂显示转换包地形编号，不为尚未核对的类别猜中文名称。下拉框限制素材支持的高度／变体，换类型只调整笔刷可选范围，不修改已有格。11/12 仅编辑桥／水岸地表，不生成桥对象。变体 4/5 与部分其他变体可以使用同一基础纹理，但排序输入不同，不应随意合并。

启动时若存在本地 `local/generated/terrain-editor/luoyang-north-bank.terrain.json`，默认打开洛阳北部河流北岸示例；否则展示自行创建的 32×32 地貌。也可用 `start.ps1 -Project '<工程 JSON>'` 指定初始工程。洛阳示例提取自本地 190 年剧本，原图范围 X=74–105、Y=36–67，包含北岸山坡、河流及少量南岸作参照；洛阳中心 (89,64) 对应局部 (15,28)，不显示城市对象。三个字段逐格原样保留；裁剪边缘重新计算衔接，纹理采样采用局部坐标，因此不是原图像素截图。来源哈希、坐标及统计位于同目录 `luoyang-north-bank.source.json`。样本属于忽略的本地生成物，不随代码分发。

可新建 4–64 格的矩形地图。尺寸限制是全画面后台重合成的实验预算，不是正式地图上限。每格只含三个字段；格式版本和宽高作为工程元信息。保存不包含派生遮罩、绘制记录或图片，也不是游戏直接可加载的 `map.json`。

## 即时预览与边界

基础数据由编辑器持有，预览线程使用快照。修改后后台合成整幅地貌；只发布最新版本的完整像素和拾取归属图，过期结果丢弃。连续涂刷速度超过合成速度时，画面会在合成追上最新修改后刷新。固定春季、静态水面；没有四季切换、建筑、装饰和地图外云层。

`Model` 只引用现有 Content 的转换包读取、PNG 解码和图片裁剪，不依赖 Godot 或版本 Importer。地貌配方及实验合成在工具内，适配现有 TC101 转换素材，暂不作为正式通用 Mod 契约。高差和八邻格过渡依据仓库现有独立实现与 [SQUARE 说明](../../docs/square-format.md)，不让作者维护原版位字段。

本目录旧 `index.html` / JS 文件是此前浏览器原型，已不作为本轮入口。旧格式含城市和实验地形名，不能打开为新地貌工程，不添加兼容转换。

## 验证

```powershell
dotnet test tools/map-maker-experiment/Tests/San9.TerrainEditor.Tests.csproj
./tools/map-maker-experiment/start.ps1 -Verify
```

模型测试覆盖高差重建、变体资源映射、非方形地图、派生不修改基础数据、连续笔画与撤销、非法组合和工程往返。`-Verify` 在真实 Godot 渲染器内注入鼠标事件，检查拾取与绘制、像素变化、撤销像素恢复、重做、实际文件往返，并输出 `TERRAIN_EDITOR_VERIFY_OK`、`editor.png` 和地貌 PNG 至忽略的 `local/generated/terrain-editor/`。这是引擎内交互检查，不是操作系统鼠标自动化，也不覆盖原生文件选择窗口。
