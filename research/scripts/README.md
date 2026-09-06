# 研究脚本

- `analyze_scenario_persons.py`：只读统计 20 个正式剧本的人物编号、能力、属性、熟练度、忠诚和标志位；原始样本哈希用于来源识别，逐值分布保存在忽略目录，不将库存极值转成产品拒绝规则。运行：`python research/scripts/analyze_scenario_persons.py private/s9数据文件 local/generated/research/audit/person-profile.json`；验证：`python -m unittest discover -s research/scripts -p test_analyze_scenario_persons.py`。

- `build_source_catalog.py`：从本地源码和文档生成统一来源候选。
- `build_program_analysis_close01.py`：汇总路线图、覆盖账本、San9PKHard 候选状态和 `EVT-01` 旧待验证项路由，验证已完成批次不再伪装为当前待办，并把唯一剩余核心项守恒到 `CLOSE-02`。
- `validate_core_domain_contracts.py`：校验 `CLOSE-03` 的已确认行为规格分类、核心领域契约、无环依赖、决策门、实现入口、验收条款覆盖及产品契约逆向细节泄漏。
- `validate_program_analysis_final_acceptance.py`：重放 `CLOSE-04` 历史验收，使用提交 `6cbbfba` 的完整工作树及其中脚本执行。其147份研究/419个符号和在线回读是固定历史快照，不能在当前工作树重跑并宣称当前全量通过；历史路线图现归档，当前复核见[疑点清单](../research-open-questions.md)。
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
- `analyze_scenario_head.py`：审计 20 个正式剧本的 82 字节 `head`，复算日期初始化后的世界事件位，验证 50 字节城市向量与 `forc.ColorCode` 的逐项关系，并输出不含原版文本的 JSON 证据。
- `legacy_text.py`：消息资源与 EXE 共用的字节保真 ASCII/Big5 家族解码、片段转换和 C 字符串扫描组件。
- `export_g_still_treasure.py`：按已确认的模式 3 索引，从 `G_Still.s9` 导出内置宝物 `image_id=0..99` 的原始 RGB PNG 和预览拼图；生成物放在 `artifacts/image-research/` 等 Git 忽略目录。
- `export_g_face_palette0.py`：只导出 `G_FaceL/S.s9` 的全部基础头像，使用 `P_Face[0]`，以 `L_000.png`/`S_000.png` 形式平铺输出。
- `render_square_map.py`：地图转换 CLI；读取 `squa/fbld/bldg/city`，按场景月份选择地貌调色板，导出地貌、桥和静态建筑快照。`--boundaries` 可显式启用 `squa +0x0D` 的地域线/都市线图层，`--mesh-state 0..5` 可启用 MESH 诊断层；`--building-info` 可显式加载名称目录、工作区字体和公共 UI 资源，绘制静态建筑信息面板。水面动画、旗帜和动态单位尚未合成。输出放在 `artifacts/image-research/square-map/`。
- `square_map/`：转换实现。`scenario.py` 解析场景，`graphics.py` 解析图像与调色板，`resources.py` 装载并验证资源集合，`canvas.py` 提供 RGB 像素操作，`terrain.py`、`boundaries.py` 与 `buildings.py` 保存选择规则，`building_compositions.py` 保存已确认的静态建筑组合表，`render_map.py` 与 `render_buildings.py` 分别绘制地图和建筑。行为见 [BEH-RENDER-0001](../../specs/behavior/BEH-RENDER-0001-static-map-snapshot.md)。

IDA 导出放在被 Git 忽略的 `artifacts/ida/`，图像研究输出放在 `artifacts/image-research/`，本地转换内容放在 `local/generated/`。执行和证据说明见 [`RE-SAN9PK-0002`](../findings/RE-SAN9PK-0002.md)、[`RE-SAN9PK-0003`](../findings/RE-SAN9PK-0003.md)、[`RE-SAN9PK-0026`](../findings/RE-SAN9PK-0026.md)、[`RE-SAN9PK-0035`](../findings/RE-SAN9PK-0035.md)与 [`RE-SAN9PK-0055`](../findings/RE-SAN9PK-0055.md)。

人物姓名、语义文本和编辑器短枚举等由原版资源或本地研究批量派生的目录统一保存在 `local/generated/research/catalogs/`，不得提交。

建筑信息面板依赖 Pillow，可通过 `python -m pip install -r research/scripts/requirements.txt` 安装。洛阳附近的局部导出示例：

```text
python research/scripts/render_square_map.py \
  private/s9数据文件/D_Sce002.S9 private/s9数据文件 \
  artifacts/image-research/square-map/d_sce002-luoyang-building-info.png \
  --x 80 --y 55 --width 35 --height 30 --building-info \
  --building-info-font private/LXGWWenKaiMonoGBLite-Medium.ttf \
  --semantic-catalog local/generated/research/catalogs/san9pk-tc-101-semantic.csv \
  --building-morale 50
```

面板开关默认关闭；开启时字体和语义目录均为必填输入。静态剧本没有序列化建筑士气，脚本对有兵建筑使用 `--building-morale`，无兵建筑始终显示为零。

PK 文本示例：

```text
python research/scripts/parse_pk_text.py \
  private/s9数据文件/M_MsgPK.s9 private/s9数据文件/M_RtdnPK.s9 \
  --simplified --json-output-dir local/generated/research/san9pk-tc-101-text \
  --research-catalog-output-dir local/generated/research/san9pk-tc-101-text/research
```

JSON 文件本身就是数组，每项只有 `index` 和 `content` 两个字段。`index` 是原文件的 0 起始序号，空记录也保留；控制字节以 `<0xNN>` 直接写进 `content`，原始 `0A` 换行保留，暂不做自动换行归一化。

EXE 字符串在 IDA 内先设置 `SAN9_IDA_BIG5_OUTPUT` 执行 dry-run，审阅 `status=ready` 后再设置 `SAN9_IDA_BIG5_APPLY=1` 重跑并保存主 IDB。EXE 使用 Windows CP950 映射；消息资源继续使用严格 Big5 加显式 Big5-HKSCS 回退，两者共享片段模型但不混淆来源编码，也不合并输出文件。

`--research-catalog-output-dir` 为每个输入分别生成 `M_MsgPK.research.json` 和 `M_RtdnPK.research.json`。EXE 目录始终由 IDA 工具单独生成。


## 界面验证按影响范围运行

`verify_initial_ui.py`是独立原图/初始数据审计器，不是每次客户端修改都要执行的回归入口。原版结论仍需对应RE证据；此脚本检查已确认来源与转换结果的一致性。范围必须显式选择，不默认运行全量，也不使用历史通过缓存跳过检查。

| 改动或目的 | 验证范围 |
| --- | --- |
| 仅修改客户端布局、绘制、交互 | 相关自动测试、Godot输入自检与实际GPU截图；无需重新逐像素核对未改的导出资源 |
| 滚动条资源映射/分帧/转换 | `--scope scrollbar`，含装饰、轨道、滑块、全部箭头状态帧 |
| 主界面框架、季节、导航按钮 | `--scope hud` |
| 战略全图背景、城市着色掩码、图层按钮、排行榜及标记 | `--scope overview`；按RE-0160核对运行时调色板Alpha |
| 名单行背景、城市前缀、可用性文字颜色 | `--scope roster` |
| 消息框、士气/耐久条、状态图标 | 分别为 `--scope messages`、`--scope bars`、`--scope icons` |
| 小地图内容转换 | `--scope minimap` |
| 初始人物、姓名、能力或据点数据转换 | `--scope scenario` |
| 单独检查整个导出包的图片文件与清单指纹一致 | `--scope integrity`；不声称已核对原图像素 |
| 共享解码、调色板/Alpha转换、包契约、审计器变更，或首次建立基线 | `--all` |

资源范围只打开涉及的源图集与导出PNG，检查所选帧的可见RGB、Alpha及输出清单指纹；不扫描整个地图包或解析人物数据。所有模式都会核对EXE样本身份和内容schema。源映射按职责集中在独立审计表中，与产品转换器保持独立；全量皮肤审计按角色集合检查缺项和未覆盖项，不维护固定资源总数。

```powershell
# 局部资源改动
python -B research/scripts/verify_initial_ui.py "<已登记EXE>" private/s9数据文件 local/generated/content/scenario-190-live --scope scrollbar
# 跨范围改动；相同范围重复指定只执行一次
python -B research/scripts/verify_initial_ui.py "<已登记EXE>" private/s9数据文件 local/generated/content/scenario-190-live --scope roster --scope scenario
# 显式全量审计
python -B research/scripts/verify_initial_ui.py "<已登记EXE>" private/s9数据文件 local/generated/content/scenario-190-live --all
python -B -m unittest discover -s research/scripts -p test_verify_initial_ui.py
```

`--baseline`仅与`--all`组合，用于已有历史地图基线比较。报告列出每个实际执行的范围和图片数量；`mode=scoped`不代表未选择的范围通过。增加皮肤资源时应同时增加所属范围的独立来源映射，全量检查会拒绝没有来源覆盖的新角色。

情报增量检查：菜单使用`--scope information-menu`，公共窗口使用`--scope information`，人物头像与宝物图使用`--scope information-art`，人物/官爵/军团/关系等初始数据使用`--scope scenario`。本次更改组合契约，验收使用`--all`。

十项菜单按钮三段四帧可用`--scope information-menu-buttons`单独核对；底部十条普通提示及两条空态原因由`--scope scenario`核对源消息。

通用表格皮肤使用`--scope catalog`，覆盖表头、方向标记、六帧页签、行背景、头像框、帮助、目录滚动条和表格边框。武将新增运行时字段由`--scope scenario`独立核对；本次契约/审计器同时更新，运行`--all`。
