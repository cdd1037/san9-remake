# 研究脚本

- `build_source_catalog.py`：从本地源码和文档生成统一来源候选。
- `reconcile_ida_catalogs.py`：合并来源分类、旧 IDB 和当前 IDB 导出。
- `build_ida_module_batches.py`：把统一候选按六个既定应用阶段拆成 dry-run 清单，并检查无静默遗漏。
- `ida/export_idb_catalog.py`：在 IDA 中只读导出数据库目录。
- `ida/health_baseline.py`：在 IDA 中只读生成可重复的样本、段、函数、交叉引用、类型、字符串和异常候选健康报告；输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/export_runtime_classes.py`：按 24 字节 CRuntimeClass 形态只读扫描类名字符串前置记录，生成 MFC/运行时类 dry-run 目录；输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/export_vtable_candidates.py`：只读导出 IDA 中已有虚表/函数表候选的原始槽位、目标段、函数归属和交叉引用；不推断边界或写入 IDB，输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/export_message_maps.py`：只读扫描 MFC 风格 8 字节消息映射头、24 字节 handler 表项和零项哨兵，生成消息映射 dry-run 目录；不写入 IDB，输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/export_global_roots.py`：按代码到 `.data/.rdata` 的引用只读汇总高传播全局/静态数据候选，记录边界、类型、来源函数和中性分类；不写入 IDB，输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/classify_source_catalog.py`：在目标 IDB 中只读分类来源候选。
- `ida/apply_core_records.py`：默认 dry-run；仅带 `--apply` 时写入七类核心记录名称和类型。
- `parse_pk_text.py`：解压并解析 PK 消息/人物列传文本表；可用 `--simplified` 调用 OpenCC `t2s` 生成简体 UTF-8 输出，并用 `--json-output-dir` 分别生成引擎可读取的 `messages.json` 与 `biographies.json`。`--format bbcode` 默认生成源语义的 BBCode：显式字体控制转换为颜色标签，对象值只生成占位符；保留实际换行，并用 `--metadata-report-dir` 输出未静态展开的控制流、未知字节和 BBCode 无法表达的样式效果报告。需要模拟原版对象格式化器颜色时，显式添加 `--bbcode-runtime-effects`。
- `export_g_still_treasure.py`：按已确认的模式 3 索引，从 `G_Still.s9` 导出内置宝物 `image_id=0..99` 的原始 RGB PNG 和预览拼图；生成物放在 `artifacts/image-research/` 等 Git 忽略目录。
- `export_g_face_palette0.py`：只导出 `G_FaceL/S.s9` 的全部基础头像，使用 `P_Face[0]`，以 `L_000.png`/`S_000.png` 形式平铺输出。

IDA 导出放在被 Git 忽略的 `artifacts/ida/`，图像研究输出放在 `artifacts/image-research/`，本地转换内容放在 `local/generated/`。执行和证据说明见 [`RE-SAN9PK-0002`](../findings/RE-SAN9PK-0002.md)、[`RE-SAN9PK-0003`](../findings/RE-SAN9PK-0003.md)、[`RE-SAN9PK-0026`](../findings/RE-SAN9PK-0026.md)与 [`RE-SAN9PK-0035`](../findings/RE-SAN9PK-0035.md)。

完整人物姓名、剧本标题、宝物名等由原版资源批量派生的目录只保存在 `local/generated/research-catalogs/`，不得提交。`research/catalogs/` 仅保存支撑字段语义所必需的短枚举摘要。

PK 文本示例：

```text
python research/scripts/parse_pk_text.py \
  private/s9数据文件/M_MsgPK.s9 private/s9数据文件/M_RtdnPK.s9 \
  --simplified --json-output-dir local/generated/san9pk-tc-101-text
```

JSON 文件本身就是数组，每项只有 `index` 和 `content` 两个字段。`index` 是原文件的 0 起始序号，空记录也保留；控制字节以 `<0xNN>` 直接写进 `content`，原始 `0A` 换行保留，暂不做自动换行归一化。

BBCode 投影示例：

```text
python research/scripts/parse_pk_text.py \
  private/s9数据文件/M_MsgPK.s9 private/s9数据文件/M_RtdnPK.s9 \
  --simplified --format bbcode \
  --json-output-dir local/generated/san9pk-tc-101-text-bbcode \
  --metadata-report-dir local/generated/san9pk-tc-101-text-bbcode
```

BBCode JSON 仍只包含 `index` 和 `content`。已确认的动态值使用普通占位符，例如 `{{person[0].name}}` 和 `{{unit[0].related_location}}`；原始 `0A` 保留为实际换行，不转成 `[br]`。只有显式样式控制才使用 `[color=#RRGGBB]`；对象格式化器附加的颜色默认不写入模板。若使用 `--bbcode-runtime-effects`，则会把已确认的原版对象颜色作为可选投影包在占位符外。没有运行时上下文的宏、条件、表达式和未识别属性使用 `{{meta...}}` 或 `{{raw...}}` 保留，报告文件按类别汇总并附最多 10 条样本。
