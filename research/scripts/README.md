# 研究脚本

- `build_source_catalog.py`：从本地源码和文档生成统一来源候选。
- `reconcile_ida_catalogs.py`：合并来源分类、旧 IDB 和当前 IDB 导出。
- `build_ida_module_batches.py`：把统一候选按六个既定应用阶段拆成 dry-run 清单，并检查无静默遗漏。
- `ida/export_idb_catalog.py`：在 IDA 中只读导出数据库目录。
- `ida/classify_source_catalog.py`：在目标 IDB 中只读分类来源候选。
- `ida/apply_core_records.py`：默认 dry-run；仅带 `--apply` 时写入七类核心记录名称和类型。

生成物统一放在被 Git 忽略的 `artifacts/ida/`。执行和证据说明见 [`RE-SAN9PK-0002`](../findings/RE-SAN9PK-0002.md) 与 [`RE-SAN9PK-0003`](../findings/RE-SAN9PK-0003.md)。
