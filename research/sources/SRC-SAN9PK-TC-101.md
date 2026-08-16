# SRC-SAN9PK-TC-101：三国志 IX PK 繁体中文版 1.0.1.0

- 状态：已确认
- 来源类型：商业游戏二进制
- 作者或发布者：KOEI Co., Ltd.
- 原始位置：项目所有者合法持有的本地安装
- 本地路径：不登记；位于工作区外
- 识别日期：2026-08-15
- 文件版本：1.0.1.0
- 产品名称：三國志ⅨPK
- 语言：繁体中文（中国台湾）
- 文件大小：2,636,800 字节
- SHA-256：`D20794AEFF67301EC2BF8C3BECB1E9944C68C6C0588FBFD4BF04E8597F0E5028`
- 文件格式：32 位 PE，Machine `0x014C`，Optional Header Magic `0x010B`
- PE 链接时间戳：2003-09-04T05:34:52Z
- 许可证：商业软件，项目未取得再分发许可
- 可再分发：否
- 适用游戏版本：繁中 PK 1.0.1.0，仅限上述精确指纹

## 内容与用途

该可执行文件是 `San9.Importer.San9PkTc101` 的唯一初始构建身份门槛，也是后续本项目 IDA 数据库、黑盒实验和规则复验的样本基线。导入器必须同时核对大小与 SHA-256，不得把其他版本自动当作兼容版本。与它同一安装中的剧本、地图、头像和消息文件是后续本地转换输入；各格式和必要指纹分别记录，不因目录相同而自动获得可信语义。

## 可信度与限制

文件身份、版本资源、大小、SHA-256 和 PE 架构已通过只读检查确认。尚未由本项目确认任何内部数据布局、函数语义或游戏规则；这些内容必须分别形成 `EXP-*` 或 `RE-*`。

样本本身、IDA 数据库、反编译输出和由其生成的完整内容包均不得提交。

## 关联记录

- `EXP-*`：无
- `RE-*`：[RE-SAN9PK-0001](../findings/RE-SAN9PK-0001.md)、[RE-SAN9PK-0005](../findings/RE-SAN9PK-0005.md)、[RE-SAN9PK-0006](../findings/RE-SAN9PK-0006.md)、[RE-SAN9PK-0008](../findings/RE-SAN9PK-0008.md)
- `BEH-*`：[BEH-IMPORT-0001](../../specs/behavior/BEH-IMPORT-0001-source-container-validation.md)、[BEH-IMPORT-0002](../../specs/behavior/BEH-IMPORT-0002-scenario-item-records.md)、[BEH-IMPORT-0003](../../specs/behavior/BEH-IMPORT-0003-scenario-city-records.md)
- 导入器：`src/San9.Importer.San9PkTc101/`
