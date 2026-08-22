# RE-SAN9PK-0064：原作字形分档、文本布局与物理分辨率重绘边界

- 状态：静态逆向与像素写入边界已确认；保留式文字命令层进入运行时验证
- 样本：`SRC-SAN9PK-TC-101`
- 关联：[RE-SAN9PK-0026](RE-SAN9PK-0026.md)、[RE-SAN9PK-0063](RE-SAN9PK-0063.md)

## 已确认事实

### 三套原生字形

- 字号类别表 `0x6036A8` 的三个值依次为 `24`、`16`、`12`。`0x427740` 按类别
  `0/1/2` 返回对应值，越界时回退到类别 `0`。
- `0x4276E0` 为每个字号类别选择独立的 `F_Font.s9` 数据区：

  | 类别 | 单元尺寸 | 单字节区偏移 | Big5 全角区偏移 | 4-bit 图集片区 |
  | --- | ---: | ---: | ---: | ---: |
  | `0` | 24 | `0xB410` | `0x14410` | `0 * 294912` |
  | `1` | 16 | `0x668410` | `0x66C410` | `1 * 294912` |
  | `2` | 12 | `0x93C410` | `0x93E810` | `2 * 294912` |

- 单字节字形步长为 `cell_size * cell_size / 4`，对应半角宽度；相邻区间可容纳
  256 个单字节槽。Big5 全角字形步长为 `cell_size * cell_size / 2`；24 与 16 像素
  数据区间均对应 23040 个预留槽。
- `0x5BA7F0` 使用载入的 Big5 码表查找全角字形，未命中时使用第 216 个回退槽；
  非 Big5 首字节走单字节区。
- `0x5BA8E0` 在 `(编码字符, 字号类别)` 缓存未命中时，把所选原生字形解包到对应
  768×768、4-bit 覆盖度图集片区。三个片区各占 `768 * 768 / 2 = 294912` 字节，
  字形按 32 列排列。这里没有从公共母版缩放出 24/16/12 字形。

### 布局记录与绘制边界

- `0x5BB330` 把文本解析为连续的 8 字节记录：

  ```text
  +0  int16   x
  +2  int16   y
  +4  uint16  glyph_id
  +6  uint16  style_flags
  ```

- `x/y` 是相对最终绘制原点的逻辑坐标。全角字符前进一个 `cell_size`，单字节字符和
  空格前进半个 `cell_size`；换行增加 `cell_size + line_spacing`。调色板编号、描边和
  其他效果保存在 `style_flags`。
- 普通字符的 `glyph_id` 指向 1024 项的 `(编码字符, 字号类别, 引用计数)` 缓存；
  `0xFA` 私用字符分支以 `0x8000` 标志区分，直接使用专用字形编号。
- `render_font_glyphs` 使用这些记录加调用者提供的原点和裁剪矩形逐字合成，因此现有
  布局已经足以恢复基线、字距、换行、样式与裁剪，不需要重新猜测排版。
- `render_font_glyphs` 既会写最终 1024×768 后缓冲，也会写临时表面；部分临时表面随后
  被缓存并作为 UI 图像复用。只观察最终后缓冲的四个字形合成调用点不能覆盖全部可见文字。

### 缓存表面与透明合成

- `allocate_surface_pixels`（`0x5BED00`）创建游戏使用的像素表面；`fill_surface`
  （`0x5BEB00`）按表面位深填充全部像素。已确认的缓存文字创建路径
  `0x557F70 → 0x5580A9` 会先把新建的 16 位表面填成 RGB555 `0`，再调用
  `render_font_glyphs`。
- `bind_surface_to_cached_visual`（`0x5B9C60`）把生成的 `KoeiSurface` 保存到
  `CachedSurfaceVisual +0x18`。包装器构造函数 `0x5BB830` 把
  `transparent_rgb555 (+0x34)` 初始化为 `0`。
- `draw_cached_surface_visual`（`0x5BBA50`）有互斥的两种后端：`+0x1C` 非空时走
  `blit_bitmap_resource_to_surface`；否则把 `+0x18` 的 `KoeiSurface` 交给
  `blit_surface_to_surface`（`0x5C25F0`），并原样传入 `+0x34` 的透明键。
- `blit_surface_to_surface` 会根据源表面的 `blit_mode (+0x24)` 选择导入的
  `BitBltAlphablend` 或 `BitBltHighColor`，两条优化路径都接收透明键；不满足优化条件时
  再选择逐行像素转换函数。
- `KoeiSurface` 已确认大小为 56 字节：像素地址 `+0x04`、行方向 `+0x18`、代际
  `+0x1C`、宽高 `+0x1E/+0x20`、位深 `+0x22`、合成模式 `+0x24`、有效边界
  `+0x28..+0x34`。
- `render_font_glyphs` 同时携带两套颜色索引：最终主体字形取
  `(style_flags & 0x1F)`，`0x2000/0x4000` 的偏移或描边层才取
  `((style_flags >> 8) & 0x1F)`。此前把高位索引用于整个字形是错误的。
  `KOEIGrpX.dll` 的 `BitBltKBMPPaletteAlpha` 进一步确认调色板低三字节为 RGB、第四字节
  以 `0x80` 为满覆盖；`0x4000` 的八方向效果层把其自身颜色的覆盖度右移 2 位，等价于
  1/4 覆盖，随后主体仍用低位索引对应的颜色和原覆盖度绘制。

### 运行时验证结果

- 动态计数确认缓存文字会经 `blit_surface_to_surface` 进入最终画面；进入新游戏界面后，
  已观察到 194 次携带文字元数据的表面复制。
- 仅跟踪 `blit_surface_to_surface` 的无字影子不闭合。`draw_cached_surface_visual`
  在 `bitmap_resource (+0x1C)` 非空时改走 `blit_bitmap_resource_to_surface (0x5BD650)`；
  菜单高亮、叙事装饰等动态视觉可由这条路径在文字之后继续写最终表面。
- 以跨帧“期望像素”推断遮挡已被运行时反证：菜单渐变、高亮动画和水面等动态背景本身会
  改变像素，即使文字生命周期未变也会造成错误裁剪，表现为黑洞、碎片和原字回退。遮挡与
  无字背景同步必须由实际合成操作的顺序和本次写入范围决定，不能由最终帧与历史帧的像素差
  反推。
- `initialize_narration_dialog (0x41A900)` 通过
  `allocate_narration_content_surface (0x41E7F0)` 创建并清空内嵌内容表面，随后
  `build_narration_text_layout (0x41A710)` 为所选页建立文字缓存。
  `draw_narration_panel (0x41F0F0)` 在 `0x41F1D9` 先把该内容表面复制到最终表面；框体和
  动画视觉随后绘制。因此对话尾部异常不是漏解析字符串，而是后续位图资源合成未进入文字
  遮挡生命周期。
- `destroy_memory_surface`（`0x5BE310`）是 `CMemDIB` 基类析构入口：在
  `ownsPixels (+0x08)` 有效时释放 `pixels (+0x04)`，并释放其余位图资源；派生表面析构链
  会调用该入口。它是清理文字跟踪状态、防止像素地址复用后继承旧元数据的生命周期边界。
- 字体替换最初晚于原字出现的原因不在游戏绘制顺序：启动器等待远程 `LoadLibraryW`
  返回后立即恢复主线程，而 Runtime 的实际钩子安装位于 `DllMain` 创建的异步线程中，
  两者存在确定的启动竞态。

### 已否定实现实验

- 第一版按字形绘制结果保存历史像素指纹，并在 Present 时用最终像素差推断文字遮挡。
  菜单渐变、水面和叙事动画会持续改变背景，导致文字记录被错误裁碎；该模型已否定。
- 第二版把文字记录附着到 `KoeiSurface`，并同时代理 `blit_surface_to_surface` 与
  `blit_bitmap_resource_to_surface`，尝试按两条已知合成入口的本次覆盖范围传播或裁剪记录。
  运行时仍可复现叙事尾部原字残留和动态区域清理不完整，证明这两个入口并未闭合所有可见
  写入、缓存更新与图层重建路径。继续增加入口或像素判据只会把不完整状态机固化为产品代码。
- 因此字体加载、字形捕获、影子表面、遮挡裁剪和 DirectWrite 最终文字层已整体从 Toolkit
  删除；D3D11 RGB555 上传与 Catmull–Rom 画面缩放不受影响。以下静态逆向事实仍保留，
  但不得再直接作为影子表面方案的充分实现依据。

### 保留式文字命令原型

- 新原型不恢复上述影子表面模型。`render_font_glyphs` 每次调用前直接复制本次目标字形区域，
  调用原作以取得确切的字形覆盖事实，随后在同一调用栈内恢复调用前像素，并登记文字语义命令。
  该背景只存在于一次绘制事务中，不跨帧保存，也不参与 Present 阶段的最终像素差推断。
- 文字命令和逐行所有权附着于 `KoeiSurface::pixels`。表面复制按源/目标坐标传播命令；位图资源
  合成按本次实际写入覆盖裁剪命令；Present 只消费最终后缓冲仍拥有的命令，并在 Catmull–Rom
  缩放后用 Direct2D/DirectWrite 绘制。
- 只代理两条表面复制路径仍不闭合。静态回读确认 `fill_surface (0x5BEB00)` 以及
  `0x5C2810`、`0x5C29D0`、`0x5C2AB0`、`0x5C2CA0`、`0x5C3250`、`0x5C3310`、
  `0x5C33D0` 会直接写 `KoeiSurface::pixels`，绕过 KOEIGrpX 的 BitBlt 导入族；新原型把
  这些全表面填充、像素、横纵线、矩形和棋盘填充入口纳入同一所有权失效边界。
- 启动器与 Runtime 增加预恢复握手：字体及其像素写入钩子安装完成后才允许游戏主线程开始，
  消除启动首帧先显示原字体、异步安装后才切换的竞态。
- 当前结论仍标记为“待运行时验证”：编译和入口字节校验不能替代动态水面、菜单遮挡、叙事
  动画和缓存 UI 的实际闭合测试。

## 实现推断

- 原作资源最高只有 24×24 原生字形。继续使用 `F_Font.s9` 可以准确复现原作，但无法在
  2×、3×窗口中产生新的笔画细节；强行把 12/16 像素文本换成 24 像素字形还会改变布局。
- 真正的物理分辨率重绘需要新的可缩放字源（例如 DirectWrite 字体或另建高分辨率字形
  资源），同时复用原作生成的逐字坐标、前进量、样式与裁剪。
- 难点不在 D3D11 绘制成本，而在原文字的可靠移除和离屏文本的生命周期。文字元数据应附着
  到其目标表面；`blit_surface_to_surface` 负责在表面间传播无字像素和字形记录，
  `blit_bitmap_resource_to_surface` 负责把位图资源产生的本次写入同步到无字影子并裁剪其下方
  字形。两个入口都必须按原作调用顺序更新状态，不能在 Present 阶段猜测历史背景。
- 在尚未追完临时表面的最终合成链之前，直接跳过所有原字形或仅在最终帧叠加新字体都会分别
  导致缺字、重复字或缓存 UI 行为不一致，不应进入发布实现。

## 后续研究边界

- 若再次研究字体替换，必须先证明一个完整、单一的文字语义边界，例如在原作字形进入各类
  缓存前替换字形资源，或重建独立于最终帧像素的 UI/文字场景图；不得恢复历史像素指纹、
  Present 阶段清字或枚举部分合成入口的方案。
- 选择候选繁体中文字体后，对 12/16/24 三档分别校准字面、基线和笔画粗细；这属于视觉
  决策，不能由静态逆向单独确认。

## IDA 写入与回读

- 新增 `FontSizeClass`、`FontGlyphLayoutRecord`、`FontGlyphCacheEntry` 类型。
- 命名并恢复类型：`get_font_cell_size`、`resolve_font_size_resources`、
  `resolve_font_glyph_bitmap`、`cache_font_glyph_coverage`、
  `copy_fullwidth_glyph_to_atlas`、`copy_singlebyte_glyph_to_atlas`、
  `release_text_layout_glyph_refs`、`build_text_layout`。
- 命名数据：`font_cell_sizes`、`font_singlebyte_data_offsets`、
  `font_fullwidth_data_offsets`、`font_glyph_atlas_4bpp`；并为字号选择、图集片区和布局字段
  写入证据注释。
- 保存项目主 IDB 后，已回读上述关键名称、函数原型、结构化布局访问和注释；未修改样本程序
  字节。
- 新增 `KoeiSurface`、`CachedSurfaceVisual` 类型；命名并恢复类型
  `allocate_surface_pixels`、`fill_surface`、`bind_surface_to_cached_visual`、
  `draw_cached_surface_visual`、`blit_surface_to_surface`、`destroy_memory_surface`。在透明底
  填充、缓存绑定、双后端、透明键/alpha 调用点及表面析构清理边界写入事实注释。保存主 IDB
  后已回读名称、类型、字段访问和注释。
- 命名 `fill_surface_horizontal_span_rgb`、`write_surface_pixel`、
  `draw_surface_horizontal_line`、`draw_surface_vertical_line`、
  `fill_surface_rectangle_rgb`、`fill_surface_checker_rgb`、
  `draw_surface_vertical_span_rgb`；在上述七个直写入口及 `fill_surface` 写入绕过导入合成器的
  事实注释。保存主 IDB 后已回读全部名称。
- 补定义此前被当作未探索字节的 `CNarrationDlg` 入口并命名
  `initialize_narration_dialog`、`build_narration_text_layout`、
  `allocate_narration_content_surface`、`draw_narration_panel`；在叙事内容表面复制点、缓存视觉
  的两条互斥合成后端以及 `CBGMDlg` 保留式文字项创建点写入证据注释，保存后回读验证。
