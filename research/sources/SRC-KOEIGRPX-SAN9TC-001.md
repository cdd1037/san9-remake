# SRC-KOEIGRPX-SAN9TC-001：KOEIGrpX.dll

- 状态：已确认
- 来源类型：商业游戏二进制
- 作者或发布者：KOEI Co., Ltd.
- 本地样本：`private/游戏文件/KOEIGrpX.dll`
- 识别日期：2026-08-22
- 文件大小：90,112 字节
- SHA-256：`DF682DF708631448A71E44D8D031994424411263597C7552CFC36EA35A2AF5AD`
- 文件格式：原生 32 位 PE，Machine `0x014C`，映像基址 `0x10000000`
- PE 链接时间戳：2003-08-18T02:43:59Z
- 导出：53 个具名 C `__cdecl` 图像函数
- 许可证：商业软件，项目未取得再分发许可
- 可再分发：否

## 身份与用途

该文件由用户指定的繁体中文版安装目录复制到工作区私有样本区；复制前后 SHA-256 一致。`San9.exe` 与 `San9PK.exe` 都静态导入其中相同的 17 个 8 位索引图、RGB555、KBMP、混合、灰度和边框函数。本项目只研究 EXE 实际使用的 17 个入口及其共享像素核心，不把 DLL 链接进重制产品。

本样本对应的唯一项目主数据库是被 Git 忽略的 `research/analysis/KOEIGrpX-SAN9TC-001.i64`。函数契约见 [RE-KOEIGRPX-0001](../findings/RE-KOEIGRPX-0001.md)；EXE 使用矩阵见 [DLL 契约清单](exe-dll-analysis-inventory.md)。

## 限制

文件没有可用版本资源，适用范围由完整哈希限定。像素结论同时区分 DLL 的 MMX 与标量实现；现代 x86 CPU 上原作初始化选择 MMX 路径。
