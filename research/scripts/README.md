# 研究脚本

- `analyze_scenario_persons.py`：只读统计 20 个正式剧本的人物编号、能力、属性、熟练度、忠诚和标志位；原始样本哈希用于来源识别，逐值分布保存在忽略目录，不将库存极值转成产品拒绝规则。运行：`python research/scripts/analyze_scenario_persons.py private/s9数据文件 local/generated/research/audit/person-profile.json`；验证：`python -m unittest discover -s research/scripts -p test_analyze_scenario_persons.py`。

- `build_source_catalog.py`：从本地源码和文档生成统一来源候选。
- `build_program_analysis_close01.py`：汇总路线图、覆盖账本、San9PKHard 候选状态和 `EVT-01` 旧待验证项路由，验证已完成批次不再伪装为当前待办，并把唯一剩余核心项守恒到 `CLOSE-02`。
- `validate_core_domain_contracts.py`：校验 `CLOSE-03` 的已确认行为规格分类、核心领域契约、无环依赖、决策门、实现入口、验收条款覆盖及产品契约逆向细节泄漏。
- `validate_program_analysis_final_acceptance.py`：重放 `CLOSE-04` 历史验收，使用提交 `6cbbfba` 的完整工作树及其中脚本执行。其147份研究/419个符号和在线回读是固定历史快照，不能在当前工作树重跑并宣称当前全量通过；历史路线图现归档，当前复核见[疑点清单](../research-open-questions.md)。
- `build_formula_catalog.py`：把公式文档的 0–29 项原版主张、独立的优化主张、电子书交叉来源和明确映射的 San9PKHard 补丁点汇总为语义目录；可通过审阅后的状态文件合并当前 IDA 地址、目标函数、证据和待实验项，输出放在 `local/generated/research/ida/`。
- `verify_patch_points.py`：把声明的旧字节按 PE32 映射回精确哈希样本，只读输出直接匹配与冲突；不加载或执行目标程序。
- `build_san9pkhard_catalog.py`：线性扫描 San9PKHard 的地址符号/数组、`ASM_PATCH` 注册、代码与旧字节数组、`WriteProcessMemory` 调用、DDX 配置绑定和参数位域；完整账本输出到 `tmp/`，仓库只保存带分段哈希与问题清单的 `patch-points/san9pkhard-catalog-status.json`。可选 `--image` 只读校验静态旧字节。
- `reconcile_ida_catalogs.py`：合并来源分类、旧 IDB 和当前 IDB 导出。
- `build_ida_module_batches.py`：把统一候选按六个既定应用阶段拆成 dry-run 清单，并检查无静默遗漏。
- `ida/export_idb_catalog.py`：在 IDA 中只读导出数据库目录。
- `ida/health_baseline.py`：在 IDA 中只读生成可重复的样本、段、函数、交叉引用、类型、字符串和异常候选健康报告；输出放在被 Git 忽略的 `local/generated/research/ida/`。
- `ida/export_runtime_classes.py`：按 24 字节 CRuntimeClass 形态只读扫描类名字符串前置记录，生成 MFC/运行时类 dry-run 目录；输出放在被 Git 忽略的 `local/generated/research/ida/`。
- `ida/export_vtable_candidates.py`：只读导出 IDA 中已有虚表/函数表候选的原始槽位、目标段、函数归属和交叉引用；不推断边界或写入 IDB，输出放在被 Git 忽略的 `local/generated/research/ida/`。
- `ida/export_message_maps.py`：只读扫描 MFC 风格 8 字节消息映射头、24 字节 handler 表项和零项哨兵，生成消息映射 dry-run 目录；不写入 IDB，输出放在被 Git 忽略的 `local/generated/research/ida/`。
- `ida/export_global_roots.py`：按代码到 `.data/.rdata` 的引用只读汇总高传播全局/静态数据候选，记录边界、类型、来源函数和中性分类；不写入 IDB，输出放在被 Git 忽略的 `local/generated/research/ida/`。
- `ida/configure_big5_strings.py`：从项目主 IDB 的 `.rdata` 发现严格 CP950（IDA 显示为 Big5）C 字符串并导出统一研究目录；默认 dry-run，设置 `SAN9_IDA_BIG5_APPLY=1` 后才定义字符串并逐条回读，不修改程序字节。
- `ida/classify_source_catalog.py`：在目标 IDB 中只读分类来源候选。
- `ida/apply_core_records.py`：默认 dry-run；仅带 `--apply` 时写入七类核心记录名称和类型。
- `analyze_scenario_head.py`：审计 20 个正式剧本的 82 字节 `head`，复算日期初始化后的世界事件位，验证 50 字节城市向量与 `forc.ColorCode` 的逐项关系，并输出不含原版文本的 JSON 证据。
- `export_g_still_treasure.py`：按已确认的模式 3 索引，从 `G_Still.s9` 导出内置宝物 `image_id=0..99` 的原始 RGB PNG 和预览拼图；生成物放在 `local/generated/research/images/` 等 Git 忽略目录。
- `export_g_face_palette0.py`：只导出 `G_FaceL/S.s9` 的全部基础头像，使用 `P_Face[0]`，以 `L_000.png`/`S_000.png` 形式平铺输出。
- `render_square_map.py`：地图转换 CLI；读取 `squa/fbld/bldg/city`，按场景月份选择地貌调色板，导出地貌、桥和静态建筑快照。`--boundaries` 可显式启用 `squa +0x0D` 的地域线/都市线图层，`--mesh-state 0..5` 可启用 MESH 诊断层；`--building-info` 可显式加载名称目录、工作区字体和公共 UI 资源，绘制静态建筑信息面板。水面动画、旗帜和动态单位尚未合成。输出放在 `local/generated/research/images/square-map/`。
- `square_map/`：转换实现。`scenario.py` 解析场景，`graphics.py` 解析图像与调色板，`resources.py` 装载并验证资源集合，`canvas.py` 提供 RGB 像素操作，`terrain.py`、`boundaries.py` 与 `buildings.py` 保存选择规则，`building_compositions.py` 保存已确认的静态建筑组合表，`render_map.py` 与 `render_buildings.py` 分别绘制地图和建筑。行为见 [BEH-RENDER-0001](../../specs/behavior/BEH-RENDER-0001-static-map-snapshot.md)。

IDA 导出放在被 Git 忽略的 `local/generated/research/ida/`，图像研究输出放在 `local/generated/research/images/`。执行和证据说明见 [`RE-SAN9PK-0002`](../findings/RE-SAN9PK-0002.md)、[`RE-SAN9PK-0003`](../findings/RE-SAN9PK-0003.md)、[`RE-SAN9PK-0026`](../findings/RE-SAN9PK-0026.md)、[`RE-SAN9PK-0035`](../findings/RE-SAN9PK-0035.md)与 [`RE-SAN9PK-0055`](../findings/RE-SAN9PK-0055.md)。

人物姓名、语义文本和编辑器短枚举等由原版资源或本地研究批量派生的目录统一保存在 `local/generated/research/catalogs/`，不得提交。

建筑信息面板依赖 Pillow，可通过 `python -m pip install -r research/scripts/requirements.txt` 安装。洛阳附近的局部导出示例：

```text
python research/scripts/render_square_map.py \
  private/s9数据文件/D_Sce002.S9 private/s9数据文件 \
  local/generated/research/images/square-map/d_sce002-luoyang-building-info.png \
  --x 80 --y 55 --width 35 --height 30 --building-info \
  --building-info-font private/LXGWWenKaiMonoGBLite-Medium.ttf \
  --semantic-catalog local/generated/research/catalogs/san9pk-tc-101-semantic.csv \
  --building-morale 50
```

面板开关默认关闭；开启时字体和语义目录均为必填输入。静态剧本没有序列化建筑士气，脚本对有兵建筑使用 `--building-morale`，无兵建筑始终显示为零。

## 统一消息转换

现行语法只有C# `OriginalMessageParser`一份实现，入口为`San9.Tools import-text`与`import-client`，命令见[内容管线](../../docs/client-content.md#原版文本统一入口)。此前Python消息转换入口、可见控制字节投影及旧JSON格式已由此替代。

- `ida/export_text_evidence.py`：只读导出已登记EXE字符串的原始字节和交叉引用，不解码消息，不更改IDA。
- `executable_strings.py`：供`ida/configure_big5_strings.py`进行严格CP950候选扫描和IDA边界核对；不生成产品消息。
- `ls11_archive.py`：独立的容器解压和记录切片，供字库研究及原始字节交叉核对；不解释控制码。
- `verify_message_export.py`：独立复核全部原始字节、容器尾字节与定距纯文本抽样，生成`coverage.json`，不复制消息脚本语法。
- `audit_font_extensions.py`：完整字库及有效绑定审计，输出到`local/generated/research/unified-text/font-audit`。

诊断和当前导出统一写入`local/generated/research/unified-text/`。消息JSON为键到结构化消息定义的目录；原字节与偏移在单独审计文件，不再输出控制码占位文字。IDB历史写入事实和独有证据保留，见[RE-0050](../findings/RE-SAN9PK-0050.md)、[RE-0166](../findings/RE-SAN9PK-0166.md)。

## 界面转换交叉检查已移除

按用户2026-09-06决定，删除Python界面转换结果交叉检查器及其专用测试，不再维护与C#导入器使用相同规则来源的另一套像素、映射和字段验收实现。历史运行结果仅表示当时两套实现一致，不能独立证明原版行为。原版研究记录、字体研究工具及C#测试保留；实际画面由用户验收。
