# San9 EXE 外部 DLL 契约清单

状态：已确认（当前两个 DLL 精确指纹与 `San9PK.exe` 1.0.1.0）
用途：为 EXE 调用点提供外部函数原型、效果和副作用导航；不是产品实现规格。

## 适用范围与状态含义

- `San9.exe` 与 `San9PK.exe` 静态导入相同的 22 个 SanCalc 函数和 17 个 KOEIGrpX 函数；下表 IAT 地址专属于 `SRC-SAN9PK-TC-101`。
- “已闭合”表示 DLL 函数体、ABI、至少一个 EXE 调用现场和 32 位探针相互一致；地址不得套用于其它 EXE/DLL 指纹。
- `Surface`/`KBMP` 是 [RE-KOEIGRPX-0001](../findings/RE-KOEIGRPX-0001.md) 定义的 7 DWORD 视图；所有图像矩形由 EXE 预先裁剪。

## SanCalc：22 个 EXE 导入

| DLL | San9PK IAT | 装饰符号 / C++ 原型 | 代表调用者 | 输入 / 输出 | 副作用 | 已确认效果 | 证据 | RE | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| SanCalc | `0x5FF2DC` | `game_grand() -> int` | `0x53B350`（5 xrefs） | 无参 / RNG 状态 | 无 | 返回全局 32 位 RNG 状态 | 静态+回读 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF2E0` | `game_srand(int seed) -> void` | `0x53B350`（11） | 种子 / 无 | 覆盖 RNG 状态 | 不预推进，直接赋值 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF2E4` | `limadd(ulong v,ulong d,ulong cap) -> ulong` | `0x531690`（6） | 三个 u32 / u32 | 无 | 回绕加法后做上限比较 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF2E8` | `kajuu(int v1,int w1,int v2,int w2) -> int` | `0x45E040`（4） | 两值两权重 / int | 无 | 整数加权平均；权重和 0 返回 0 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF2EC` | `calc_nisa(int a,int b) -> int` | `0x44CEC0`（2） | 两整数 / int | 无 | `max-min` | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF2F0` | `kakuritsu(int chance,RANDOM_KIND kind,int seed) -> BOOL` | `0x443EDA`（279） | 阈值、模式、种子 / bool | kind 11 推进 RNG | `rnd(100,kind,seed) < chance` | 静态+10k 探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF2F4` | `rmul(int value,int percent) -> int` | `0x43EC69`（2） | 数值、百分数 / int | 无 | `muldiv(value,percent,100)` | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF2F8` | `set_bit_data(ulong&,ulong mask,int x) -> void` | `0x44C900`（20） | u32 引用、mask、值 | 修改引用 | 连续位域清除、下限/容量夹取、写入 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF2FC` | `get_bit_data(ulong value,ulong mask) -> int` | `0x44C8E0`（23） | u32、mask / int | 无 | `(value&mask)/lowbit`；mask 0 返回 0 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF300` | `limsub(ulong value,ulong delta) -> ulong` | `0x53E5D0`（6） | 两个 u32 / u32 | 无 | 饱和减到 0 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF304` | `get_bit_data(ushort value,ushort mask) -> int` | `0x435630`（5） | u16、mask / int | 无 | 16 位位域提取；mask 0 返回 0 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF308` | `min(int a,int b) -> int` | `0x402510`（≥500） | 两整数 / int | 无 | 有符号最小值 | 静态+调用现场 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF30C` | `limadd(int v,int d,int cap) -> int` | `0x435670`（66） | 三整数 / int | 无 | 回绕加法后做上限比较 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF310` | `minmax(int v,int lo,int hi) -> int` | `0x4050A0`（81） | 值、下界、上界 / int | 无 | `min(max(v,lo),hi)` | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF314` | `limsub(int value,int delta) -> int` | `0x41D5A0`（182） | 两整数 / int | 无 | `value>delta ? value-delta : 0` | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF318` | `rnd(int upper,RANDOM_KIND kind,int seed) -> int` | `0x408C40`（245） | 上界、模式、种子 / `[0,upper)` | kind 11 推进 RNG | 15 位 LCG 取模；非 11 临时播种后恢复 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF31C` | `sqr(int value) -> int` | `0x409510`（49） | int / int | 无 | `sqrt(double)` 后转 int；负数为 `INT_MIN` | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF320` | `min(ulong a,ulong b) -> ulong` | `0x414570`（4） | 两个 u32 / u32 | 无 | 无符号最小值 | 静态+调用现场 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF324` | `max(ulong a,ulong b) -> ulong` | `0x414570`（6） | 两个 u32 / u32 | 无 | 无符号最大值 | 静态+调用现场 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF328` | `set_bit_data(ushort&,ushort mask,int x) -> void` | `0x435650`（7） | u16 引用、mask、值 | 修改引用 | 16 位连续位域夹取写入 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF32C` | `muldiv(int a,int b,int divisor) -> int` | `0x41D390`（54） | 三整数 / int | 无 | 32 位乘积后有符号除法；除零返回 32767 | 静态+探针 | SANCALC-0001 | 已闭合 |
| SanCalc | `0x5FF330` | `max(int a,int b) -> int` | `0x4036F0`（411） | 两整数 / int | 无 | 有符号最大值 | 静态+调用现场 | SANCALC-0001 | 已闭合 |

未被 EXE 导入的 18 个导出仅登记：`figures`、`game_rand`、short/ushort 版 `limadd/limsub/min/max`、short/ushort/ulong 版 `minmax`、ushort/ulong 版 `muldiv/rmul`、`rate`。其中 `game_rand` 仅作为已闭合 RNG 共享核心记录。

## KOEIGrpX：17 个 EXE 导入

| DLL | San9PK IAT | 符号 / C 原型摘要 | 代表调用者 | 输入 / 输出 | 副作用 | 已确认效果 | 证据 | RE | 状态 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| KOEIGrpX | `0x5FF258` | `FrameBlt2(Surface*,Surface*,w,h,a,key,rgb,copy)` | `0x5BD960`（3） | RGB555 邻域 / 目标像素 | 写目标 | 透明状态边界着色，可复制到当前像素 | 静态+3×3 探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF25C` | `KBMPFrameBlt2(Surface*,KBMP*,w,h,a,key,rgb,copy)` | `0x5BD960`（4） | KBMP 邻域 / 目标像素 | 写目标 | KBMP 调色板颜色判透明的边界着色 | 静态+共享探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF260` | `IsMMXSupported() -> BOOL` | `0x5C5D40`（4） | 无参 / bool | 无 | CPUID(1) EDX bit23 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF264` | `BitBltHighColor(Surface*,Surface*,w,h,key)` | `blit_bitmap_resource_to_surface`（6） | RGB555 / RGB555 | 写目标 | 清 bit15 后透明键复制 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF268` | `BitBlt256Color(Surface*,Surface*,w,h,key)` | `0x5BCC90`（16） | 8 位索引 / 索引 | 写目标 | 透明索引复制 | 静态+保护字节探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF26C` | `BitBltKBMPGrayScale(Surface*,KBMP*,w,h,scale,offset,key)` | `0x5BCA90`（6） | 调色板 RGB / RGB555 | 写目标 | 76/151/28 灰度、缩放和偏移 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF270` | `BitBltGrayScale(Surface*,Surface*,w,h,scale,offset,key)` | `0x5BCA90`（8） | RGB555 / RGB555 | 写目标 | MMX 展开 RGB555 后灰度；标量公式不同 | 静态+双路径探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF274` | `BitBltKBMPScreenblend(Surface*,KBMP*,w,h,key)` | `0x5BC9D0`（8） | KBMP / RGB555 | 写目标 | 分母 256 滤色混合 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF278` | `BitBltScreenblend(Surface*,Surface*,w,h,key)` | `0x5BC9D0`（6） | RGB555 / RGB555 | 写目标 | 分母 256 滤色混合 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF27C` | `BitBltKBMPColorAlphablend(...,tint,srcA,tintA,key)` | `0x5BC8C0`（4） | KBMP、染色参数 / RGB555 | 写目标 | 先染色、再统一 Alpha | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF280` | `BitBltColorAlphablend(...,tint,srcA,tintA,key)` | `0x5BC8C0`（4） | RGB555、染色参数 / RGB555 | 写目标 | 先染色、再统一 Alpha | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF284` | `BitBltKBMPAddblendAlpha(...,key,alpha)` | `0x5BC800`（4） | KBMP、Alpha / RGB555 | 写目标 | 源乘 Alpha 后饱和加色 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF288` | `BitBltKBMPHighColor(Surface*,KBMP*,w,h,key)` | `0x5BAC20`（6） | KBMP / RGB555 | 写目标 | 调色板量化复制，按颜色 DWORD 判透明 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF28C` | `BitBltAlphablend(...,alpha,key)` | `0x5BC740`（12） | RGB555、Alpha / RGB555 | 写目标 | `S*A+D*(255-A)` / 256 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF290` | `BitBltKBMPAlphablend(...,alpha,key)` | `0x5BC740`（4） | KBMP、Alpha / RGB555 | 写目标 | KBMP 统一 Alpha；MMX 先量化源色 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF294` | `BitBltAddblendAlpha(...,key,alpha)` | `0x5BC800`（4） | RGB555、Alpha / RGB555 | 写目标 | 源乘 Alpha 后饱和加色 | 静态+探针 | KOEIGRPX-0001 | 已闭合 |
| KOEIGrpX | `0x5FF298` | `BitBltKBMPPaletteAlpha(Surface*,KBMP*,w,h,key)` | `0x5BAB80`（8） | KBMP 每色 Alpha / RGB555 | 写目标 | MMX：0 保留、1..127 按 127-A/128 混合、128+复制 | 静态+Alpha 边界探针 | KOEIGRPX-0001 | 已闭合 |

未被 EXE 导入的 36 个导出仅登记：`BitBltAddblend`、`BitBltAlpha`、`BitBltColorAddblend`、`BitBltColorMulblend`、`BitBltColorScreenblend`、三种 Fading、`BitBltIndexAlphablend`、`BitBltKBMPAddblend`、`BitBltKBMPAlpha`、`BitBltKBMPColorAddblend`、`BitBltKBMPColorMulblend`、`BitBltKBMPColorScreenblend`、三种 KBMP Fading、`BitBltKBMPIndexAlphablend`、`BitBltKBMPMulblend`、`BitBltKBMPMulblendAlpha`、三种 KBMP Phasing、`BitBltKBMPReversePhasingAlphablend`、`BitBltKBMPScreenblendAlpha`、`BitBltMulblend`、`BitBltMulblendAlpha`、三种 Phasing、`BitBltReversePhasingAlphablend`、`BitBltScreenblendAlpha`、`Fogblend`、`FrameBlt`、`KBMPFogblend`、`KBMPFrameBlt`。

## EXE 分析使用原则

1. 先用 IAT 行定位调用点，再按本表原型恢复参数；不要把 DLL 返回寄存器命名为成功状态。
2. 对图形调用先追 EXE 的裁剪、源/目标视图构造和 packed mode 解码，再应用 DLL 像素公式。
3. `kakuritsu/rnd` 的业务名称来自上游调用语境；DLL 只确定随机契约，不替调用者定义玩法规则。
4. 需要进入产品实现的行为必须另写已确认 `BEH-*`；本清单和地址只服务逆向导航。
