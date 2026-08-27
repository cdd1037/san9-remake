# 研究脚本

- `build_source_catalog.py`：从本地源码和文档生成统一来源候选。
- `build_program_analysis_close01.py`：汇总路线图、覆盖账本、San9PKHard 候选状态和 `EVT-01` 旧待验证项路由，验证已完成批次不再伪装为当前待办，并把唯一剩余核心项守恒到 `CLOSE-02`。
- `validate_core_domain_contracts.py`：校验 `CLOSE-03` 的已确认行为规格分类、核心领域契约、无环依赖、决策门、实现入口、验收条款覆盖及产品契约逆向细节泄漏。
- `build_formula_catalog.py`：把公式文档的 0–29 项原版主张、独立的优化主张、电子书交叉来源和明确映射的 San9PKHard 补丁点汇总为语义目录；可通过审阅后的状态文件合并当前 IDA 地址、目标函数、证据和待实验项，输出放在 `artifacts/ida/`。
- `verify_patch_points.py`：把声明的旧字节按 PE32 映射回精确哈希样本，只读输出直接匹配与冲突；不加载或执行目标程序。
- `build_san9pkhard_catalog.py`：线性扫描 San9PKHard 的地址符号/数组、`ASM_PATCH` 注册、代码与旧字节数组、`WriteProcessMemory` 调用、DDX 配置绑定和参数位域；完整账本输出到 `tmp/`，仓库只保存带分段哈希与问题清单的 `patch-points/san9pkhard-catalog-status.json`。可选 `--image` 只读校验静态旧字节。
- `reconcile_ida_catalogs.py`：合并来源分类、旧 IDB 和当前 IDB 导出。
- `build_ida_module_batches.py`：把统一候选按六个既定应用阶段拆成 dry-run 清单，并检查无静默遗漏。
- `ida/export_idb_catalog.py`：在 IDA 中只读导出数据库目录。
- `ida/health_baseline.py`：在 IDA 中只读生成可重复的样本、段、函数、交叉引用、类型、字符串和异常候选健康报告；输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/export_runtime_classes.py`：按 24 字节 CRuntimeClass 形态只读扫描类名字符串前置记录，生成 MFC/运行时类 dry-run 目录；输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/export_vtable_candidates.py`：只读导出 IDA 中已有虚表/函数表候选的原始槽位、目标段、函数归属和交叉引用；不推断边界或写入 IDB，输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/export_message_maps.py`：只读扫描 MFC 风格 8 字节消息映射头、24 字节 handler 表项和零项哨兵，生成消息映射 dry-run 目录；不写入 IDB，输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/export_global_roots.py`：按代码到 `.data/.rdata` 的引用只读汇总高传播全局/静态数据候选，记录边界、类型、来源函数和中性分类；不写入 IDB，输出放在被 Git 忽略的 `artifacts/ida/`。
- `ida/configure_big5_strings.py`：从项目主 IDB 的 `.rdata` 发现严格 CP950（IDA 显示为 Big5）C 字符串并导出统一研究目录；默认 dry-run，设置 `SAN9_IDA_BIG5_APPLY=1` 后才定义字符串并逐条回读，不修改程序字节。
- `ida/classify_source_catalog.py`：在目标 IDB 中只读分类来源候选。
- `ida/apply_core_records.py`：默认 dry-run；仅带 `--apply` 时写入七类核心记录名称和类型。
- `parse_pk_text.py`：解压并解析 PK 消息/人物列传文本表；可用 `--simplified` 调用 OpenCC `t2s` 生成简体 UTF-8 输出，并用 `--json-output-dir` 分别生成 `messages.json` 与 `biographies.json`。控制字节保持原始可见形式；如何在新引擎中解释和渲染消息脚本另行设计。
- `legacy_text.py`：消息资源与 EXE 共用的字节保真 ASCII/Big5 家族解码、片段转换和 C 字符串扫描组件。
- `export_g_still_treasure.py`：按已确认的模式 3 索引，从 `G_Still.s9` 导出内置宝物 `image_id=0..99` 的原始 RGB PNG 和预览拼图；生成物放在 `artifacts/image-research/` 等 Git 忽略目录。
- `export_g_face_palette0.py`：只导出 `G_FaceL/S.s9` 的全部基础头像，使用 `P_Face[0]`，以 `L_000.png`/`S_000.png` 形式平铺输出。
- `render_square_map.py`：地图转换 CLI；读取 `squa/fbld/bldg/city`，按场景月份选择地貌调色板，导出地貌、桥和静态建筑快照。`--boundaries` 可显式启用 `squa +0x0D` 的地域线/都市线图层，`--mesh-state 0..5` 可启用 MESH 诊断层；`--building-info` 可显式加载名称目录、工作区字体和公共 UI 资源，绘制静态建筑信息面板。水面动画、旗帜和动态单位尚未合成。输出放在 `artifacts/image-research/square-map/`。
- `square_map/`：转换实现。`scenario.py` 解析场景，`graphics.py` 解析图像与调色板，`resources.py` 装载并验证资源集合，`canvas.py` 提供 RGB 像素操作，`terrain.py`、`boundaries.py` 与 `buildings.py` 保存选择规则，`building_compositions.py` 保存已确认的静态建筑组合表，`render_map.py` 与 `render_buildings.py` 分别绘制地图和建筑。行为见 [BEH-RENDER-0001](../../specs/behavior/BEH-RENDER-0001-static-map-snapshot.md)。

IDA 导出放在被 Git 忽略的 `artifacts/ida/`，图像研究输出放在 `artifacts/image-research/`，本地转换内容放在 `local/generated/`。执行和证据说明见 [`RE-SAN9PK-0002`](../findings/RE-SAN9PK-0002.md)、[`RE-SAN9PK-0003`](../findings/RE-SAN9PK-0003.md)、[`RE-SAN9PK-0026`](../findings/RE-SAN9PK-0026.md)、[`RE-SAN9PK-0035`](../findings/RE-SAN9PK-0035.md)与 [`RE-SAN9PK-0055`](../findings/RE-SAN9PK-0055.md)。

人物姓名、语义文本和编辑器短枚举等由原版资源或本地研究批量派生的目录统一保存在 `local/generated/research-catalogs/`，不得提交。

建筑信息面板依赖 Pillow，可通过 `python -m pip install -r research/scripts/requirements.txt` 安装。洛阳附近的局部导出示例：

```text
python research/scripts/render_square_map.py \
  private/s9数据文件/D_Sce002.S9 private/s9数据文件 \
  artifacts/image-research/square-map/d_sce002-luoyang-building-info.png \
  --x 80 --y 55 --width 35 --height 30 --building-info \
  --building-info-font private/LXGWWenKaiMonoGBLite-Medium.ttf \
  --semantic-catalog local/generated/research-catalogs/san9pk-tc-101-semantic.csv \
  --building-morale 50
```

面板开关默认关闭；开启时字体和语义目录均为必填输入。静态剧本没有序列化建筑士气，脚本对有兵建筑使用 `--building-morale`，无兵建筑始终显示为零。

PK 文本示例：

```text
python research/scripts/parse_pk_text.py \
  private/s9数据文件/M_MsgPK.s9 private/s9数据文件/M_RtdnPK.s9 \
  --simplified --json-output-dir local/generated/san9pk-tc-101-text \
  --research-catalog-output-dir local/generated/san9pk-tc-101-text/research
```

JSON 文件本身就是数组，每项只有 `index` 和 `content` 两个字段。`index` 是原文件的 0 起始序号，空记录也保留；控制字节以 `<0xNN>` 直接写进 `content`，原始 `0A` 换行保留，暂不做自动换行归一化。

EXE 字符串在 IDA 内先设置 `SAN9_IDA_BIG5_OUTPUT` 执行 dry-run，审阅 `status=ready` 后再设置 `SAN9_IDA_BIG5_APPLY=1` 重跑并保存主 IDB。EXE 使用 Windows CP950 映射；消息资源继续使用严格 Big5 加显式 Big5-HKSCS 回退，两者共享片段模型但不混淆来源编码，也不合并输出文件。

`--research-catalog-output-dir` 为每个输入分别生成 `M_MsgPK.research.json` 和 `M_RtdnPK.research.json`。EXE 目录始终由 IDA 工具单独生成。
