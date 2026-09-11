# RE-SAN9PK-0050：EXE CP950 字符串与统一文本目录

- 状态：已确认（EXE 静态字符串编码、IDA 定义与回读；低置信度候选语义仍待复核）
- 日期：2026-08-19
- 分析者：Codex
- 结论类型：事实、推断和待验证项分开记录
- 置信度：高（编码、863 条已应用字符串边界和回读）；低至中（review 候选）
- 样本：`SRC-SAN9PK-TC-101`
- 关联：`RE-SAN9PK-0026`、`RE-SAN9PK-0037`

## 目的与方法

把 `San9PK.exe` 内的繁体中文 C 字符串纳入消息/列传共用的字节保真研究目录。调查只读取项目主 IDB 已加载的程序字节，不读取工作区外原始 EXE。脚本默认 dry-run，只有显式 apply 才定义 IDA 字符串；任何模式都不修改程序字节。

本记录保留当时的IDA字符串登记事实。旧公共片段转换模型及HKSCS回退方案已被[RE-0166](RE-SAN9PK-0166.md)与[统一消息规格](../../specs/behavior/BEH-UI-0006-unified-messages.md)替代；现在由版本Importer按CP950及精确的其他来源编码解析，EXE地址和字节证据仍不进入产品业务接口。

## 事实

1. 应用前 IDA 默认字符串目录有 1,670 条 ASCII 记录，没有非 ASCII 文本；默认枚举不能代表 EXE 中文字符串全集。
2. 对 `.rdata` 执行 NUL 边界、严格 CP950、至少一个 CJK 字符和现有数据边界检查后得到 2,721 条候选：863 条达到自动应用门槛，1,858 条保留为 `review`，没有写入 IDB。
3. 已确认样本包括 `0x604D64 三国志ⅨPK`、`0x61B358 出征部隊`、`0x61BD74 打倒敵勢‧%s` 和 `0x61CDDC 淮南王‧劉安`。
4. 字节 `A1 45` 在 Python `big5` 中映射为 `U+2022 •`，而 IDA `Big5` 与 Python `cp950` 均映射为 `U+2027 ‧`。四条字符串在初次标准 Big5 校验中因此出现文本差异；切换 EXE 来源配置为 CP950 后，863 条全部与 IDA 原生显示逐条一致。
5. apply 后 863 条记录均为 IDA 原生、NUL 结尾、编码名 `Big5` 的字符串项；重新读取字符串类型、长度与 UTF-8 内容全部成功。最终报告位于被 Git 忽略的 `artifacts/ida/San9PK-TC-101/exe-strings-applied-cp950.json`。
6. IDA 中的 Big5 字符串用于 EXE 地址、显示与交叉引用；统一 JSON 目录另外保存来源 SHA-256、文件偏移、原始十六进制、片段、编码判定和完整 xref。二者互为回读验证，不建立第二套 IDB 字符串真相。

## 推断

- 本繁中 Windows EXE 的窄字符串语义应称为 CP950/Windows Big5，而不是不带实现限定的标准 Big5。该判断由确定字节的 Unicode 映射差异和 IDA 回读共同支持。
- `.rdata` 中达到门槛的 863 条记录形成当前可重复的高置信度静态文本集；`review` 项混有二进制偶然解码结果，不能仅凭可解码性提升为字符串事实。

## 待验证

- 1,858 条 `review` 候选中是否仍有无 xref、单字符或特殊格式的真实文本。
- 是否存在运行时拼接、解密生成或当前区段策略未覆盖的文本。
- EXE 与消息资源之间的重复文本、语义键和未来产品本地化映射；本记录不把虚拟地址直接转成产品键。

## IDA 写入与验证

- 注册/复用 IDA `Big5` 编码，并在项目主数据库中定义 863 条 C 字符串。
- 每条已应用字符串写入 `[RE-SAN9PK-0050]` 证据注释；未给 `review` 候选创建字符串或注释。
- apply 后按 CP950 权威投影逐条回读，结果为 863 成功、0 失败；另对上述四个地址回读字符串类型、编码名和内容。
- 已调用 IDA 原生保存，数据库仍为 `research/analysis/San9PK-TC-101.i64`。未修改程序字节、函数边界、名称或类型。
- 保存后重跑健康基线成功：输入 SHA-256 不变，可执行段未定义字节和非执行段代码项均为 0；报告摘要为 `dd3f5c1323b36e6fdc4cbb450e835acddd45f63fd7b653c2d4ca345eb75d23bc`。

## 现行入口与替代关系

- `research/scripts/executable_strings.py`保留严格CP950候选扫描职责；`ida/configure_big5_strings.py`继续负责IDA dry-run/apply与回读，不承担消息转换。
- `ida/export_text_evidence.py`导出当前登记的原始字节与xref；`San9.Tools import-text --exe-evidence`使用与消息档案相同的解析器生成审计。
- 旧Python消息转换、旧最小JSON和旧消息研究投影已删除，当前产物位于`local/generated/research/unified-text/`。上文863条apply事实及`artifacts/ida/`的独有研究证据保留。
