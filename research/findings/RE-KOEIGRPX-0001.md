# RE-KOEIGRPX-0001：KOEIGrpX EXE 导入像素契约

- 状态：已确认（17 个 EXE 导入、描述符、RGB555/KBMP 与 MMX/标量分支）
- 日期：2026-08-22
- 分析者：Codex
- 结论类型：DLL 代码事实、EXE 调用事实与 32 位合成缓冲区探针
- 置信度：高
- 关联来源：[SRC-KOEIGRPX-SAN9TC-001](../sources/SRC-KOEIGRPX-SAN9TC-001.md)、[SRC-SAN9PK-TC-101](../sources/SRC-SAN9PK-TC-101.md)
- 关联记录：[RE-SAN9PK-0026](RE-SAN9PK-0026.md)、[RE-SAN9PK-0053](RE-SAN9PK-0053.md)、[RE-SAN9PK-0055](RE-SAN9PK-0055.md)

## 样本与范围

分析样本为 90,112 字节、SHA-256 `DF682DF708631448A71E44D8D031994424411263597C7552CFC36EA35A2AF5AD` 的 32 位 `KOEIGrpX.dll`，映像基址 `0x10000000`。工具为 IDA 9.3、Hex-Rays、批处理 IDAPython 和 MSVC x86 合成缓冲区探针；项目主数据库为 `research/analysis/KOEIGrpX-SAN9TC-001.i64`。

`San9.exe` 与 `San9PK.exe` 导入相同的 17 个入口。本文只闭合这些入口和它们共享的像素核心；其余 36 个导出只作为目录项保留。

## 描述符与像素不变量

EXE 在栈上构造固定 7 个 32 位字段的视图传给 DLL：

```text
SurfaceView = { base, width, height, pixels, reverse_rows, x, y }
KbmpView    = { kbmp_base, row_stride, height, indices, reserved, x, y }
```

- 8 位普通表面的行步长是 `(width+3)&~3`；RGB555 表面是 `(2*width+3)&~3`。
- `reverse_rows` 决定普通表面的起始行与行步长符号；KBMP 索引始终从 `indices + x + stride*(y+height-1)` 开始并按负步长遍历。
- KBMP 调色板从 `kbmp_base+40` 开始，共 256 个小端 `R,G,B,A` DWORD；透明参数为 `-1` 时禁用，否则先取指定索引的完整调色板 DWORD，再按颜色值而不是索引值比较。因此重复颜色也会被视作透明。
- 高彩色源像素先与 `0x7FFF`；工作颜色为 RGB555，展开通道是 `R5/G5/B5 × 8`。MMX 个别路径会留下 bit15，但 EXE 的颜色比较和 RGB 语义均忽略该位。
- DLL 不负责矩形裁剪或参数校验；EXE 在调用前完成裁剪。宽高必须为正，指针、步长和坐标必须有效。返回寄存器是循环残值，EXE 不使用，不能解释为成功码。

## 17 个导入入口

### 复制

- `BitBlt256Color`：8 位索引逐像素复制，命中透明索引时保留目标。
- `BitBltHighColor`：RGB555 逐像素复制，源先清 bit15，命中透明色时保留目标。
- `BitBltKBMPHighColor`：索引查 `R,G,B,A` 调色板并量化为 RGB555；透明比较使用完整调色板 DWORD。

### Alpha、加色、染色和滤色

设 `D`、`S` 为展开到 0..248 的目标/源通道，`A` 为调用参数：

- `BitBlt[KBMP]Alphablend`：`floor((S*A + D*(255-A))/256)` 后量化为 RGB555。`A=0` 仍可能因 255/256 与量化轻微变暗；有效调用域是 0..255。
- `BitBlt[KBMP]AddblendAlpha`：`saturate(D + floor(S*A/256))` 后量化；参数顺序是透明键在前、Alpha 在后。
- `BitBlt[KBMP]ColorAlphablend`：先以 `tint_alpha` 对源色和调用方提供的 `tint_rgb` 做一次分母 256 的染色，再以 `source_alpha` 和目标做一次分母 256 的 Alpha 混合。
- `BitBlt[KBMP]Screenblend`：每通道执行 `255-floor((255-S)*(255-D)/256)` 后量化。

MMX 路径会先把 KBMP 的 8 位调色板色量化到 5 位精度，并使用打包饱和指令；标量路径多处直接使用 8 位调色板色，因此边界取整并不完全相同。DLL 初始化以 CPUID EDX bit23 设置 `koeigrpx_mmx_enabled`，当前探针机器确认走 MMX；像素级重现应以 MMX 结果为基线，同时保留标量差异记录。

### 灰度

`BitBlt[KBMP]GrayScale(dst,src,w,h,scale,offset,key)` 的 MMX 主路径先计算：

```text
luma = floor((76*R + 151*G + 28*B) / 256)
gray = uint8(offset + floor(scale*luma/256))
```

然后把 `gray>>3` 写入 RGB555 三个通道。普通 RGB555 的标量备用路径直接按打包像素的三个字节参与系数运算，与 MMX 结果可能明显不同；这是 DLL 的实际分支差异，不应把两者合并成一个“等价优化”。

### 调色板 Alpha

`BitBltKBMPPaletteAlpha` 不接收统一 Alpha，而读取每个调色板 DWORD 的第四字节：

- MMX 主路径：`A==0` 保留目标；`1..127` 使用 `floor((S*A + D*(127-A))/128)`；`A>=128` 直接复制源色。
- 标量备用路径：使用 `floor((S*A + D*(128-A))/128)`，没有 MMX 的 0/128 快捷边界。

因此 `P_LBUILD` 第六套调色板中实测的 `0..64` 只覆盖到约半透明，不能归一化成 `0..255`。研究渲染器已按 MMX 的 `127-A` 权重、128 分母和 RGB555 量化修正。

### FrameBlt2

`FrameBlt2` 与 `KBMPFrameBlt2` 比较当前源像素和左/上一源像素的透明状态。在透明—非透明状态发生变化时，以 `frame_rgb` 和 `alpha` 染目标边界像素；`copy_frame_to_current!=0` 时还把生成的边界色复制到当前目标像素。KBMP 版本仍以完整调色板 DWORD判透明。`alpha` 只做上限 255 夹取，调用方须保证非负。

### MMX 检测

`IsMMXSupported()` 执行 `CPUID(1)` 并返回 EDX bit23。它没有操作系统能力探测或后备配置；DLL 初始化结果直接控制上述 MMX/标量分支。

## 动态复验

- 32 位探针在 1×1 普通表面和 KBMP 上覆盖 Alpha `0/1/32/64/128/255`，并在内存两侧放置保护字节；8 位复制后保护值 `AA/BB` 与源保护值 `CC/DD` 均未改变。
- 对 `dst=0x18C3`、调色板色 `(200,100,40,A)`，MMX 调色板 Alpha 在 `A=0/32/64/128` 得到 `0x18C3/0x28E3/0x3D03/0x6585`，与上述边界和公式一致。
- 3×3 单像素透明边界探针分别执行标量和 MMX `FrameBlt2`，两者都只改变透明状态交界处；具体值因 MMX 取整不同而不同，确认 `copy_frame_to_current` 与邻接传播路径实际生效。
- 动态探针只加载 `SanCalc.dll` 和 `KOEIGrpX.dll` 的工作副本；未加载驱动、音视频 DLL，也未修改样本字节。

## IDA 写入与回读

- 建立 `KoeiSurfaceView`、`KoeiKbmpView` 类型，为 17 个 EXE 导入入口应用完整 `__cdecl` 原型并追加 `[RE-KOEIGRPX-0001]` 注释。
- 命名 `pack_rgb555_saturating`、`is_mmx_supported_cpuid`、`blit_kbmp_palette_alpha_mmx` 和 `koeigrpx_mmx_enabled`，并记录 MMX 调色板 Alpha 的精确边界。
- 保存 `KOEIGrpX-SAN9TC-001.i64` 后批量回读 21 个相关对象：21 个类型均存在，17 个导入入口及两个关键内部对象的证据注释存在，无失败项。
- 将两种视图类型和 17 个函数指针原型同步到 `San9PK-TC-101.i64` 的 IAT `0x5FF258..0x5FF298`；回读确认类型和注释完整，`blit_bitmap_resource_to_surface` 的反编译已显示 `dst/src`、宽高和 KBMP 视图，不再是 `_DWORD` 参数占位。
- 未修改 DLL 字节；合成探针及反编译导出只保存在 Git 忽略的 `tmp/`。
