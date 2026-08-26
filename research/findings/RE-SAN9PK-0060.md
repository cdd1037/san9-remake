# RE-SAN9PK-0060：皇帝的剧本定义位置

- 状态：已确认
- 日期：2026-08-22
- 分析者：Codex
- 结论类型：IDA 静态事实、既有格式事实
- 置信度：高
- 关联来源：`SRC-SAN9PK-TC-101`
- 关联规格：[BEH-IMPORT-0001](../../specs/behavior/BEH-IMPORT-0001-source-container-validation.md)、[BEH-IMPORT-0004](../../specs/behavior/BEH-IMPORT-0004-scenario-person-records.md)
- 前置记录：[RE-SAN9PK-0005](RE-SAN9PK-0005.md)、[RE-SAN9PK-0009](RE-SAN9PK-0009.md)、[RE-SAN9PK-0058](RE-SAN9PK-0058.md)

## 调查问题

确认皇帝在剧本容器中的具体定义位置，并纠正 `RE-SAN9PK-0058` 把剧本头 `+0x18` 当作皇帝相关标志来源的错误。

## 剧本头开关

`deserialize_scenario_head`（`0x446B60`）在读取格式版本后，从 `head +0x04` 开始连续读取四个小端 `u32`，经 `set_scenario_world_flag_word`（`0x46A940`）写入场景世界状态的四个标志字。`test_scenario_world_flag_bit`（`0x46A960`）把线性 bit 编号拆为 `dword_index = bit / 32` 和 `mask = 1 << (bit % 32)`。

`get_current_emperor_person`（`0x458540`）首先测试线性 bit 0。因此对皇帝解析链而言，来源精确为：

- `head +0x04` 的 `u32` bit 0 为 1：直接返回空，不解析当前皇帝；
- 该 bit 为 0：继续搜索皇帝候选人物。

`head +0x18` 由同一反序列化函数读取为起始日期编码，并传给场景日期 setter；它不是皇帝开关。这里只确认 bit 0 对皇帝解析链的效果，不把整个 `head +0x04` 字段或该 bit 扩张命名为超出证据的全局业务概念。

## 人物候选定义

候选人物不由城市记录或单独的“皇帝人物编号”字段指定。`get_current_emperor_person` 依次以常量 `563`、`564`、`565` 调用 `find_person_by_biography_resource_code`（`0x455660`）。后者扫描全部 850 个运行时人物，匹配 Person `+0x3C`。

`deserialize_person_record`（`0x44F830`）确认 Person `+0x3C` 来自每条 134 字节 `prsn` 记录的 `+0x10`，该字段是列传资源编号。因而剧本中的候选判定条件是：

```text
prsn record +0x10 == 563, 564, or 565
```

这三个数不是 `prsn` 记录序号，不是运行时人物 ID，也不是 `prsn +0x04` 的 serialized id。程序按 `563 -> 564 -> 565` 的顺序，返回第一个有效且身份代码不为 `8` 的人物。

候选人物的初始地点和身份分别由同一条记录的 `prsn +0x18` 与 `prsn +0x1C` 给出。`get_current_emperor_city`（`0x4585D0`）再通过 `resolve_person_current_city`（`0x44F460`）从人物当前位置解析城市，所以皇帝图标所在城市不是 `city` chunk 中的字段。

## 三个候选的文本身份

对 `D_Sce001.S9` 至 `D_Sce020.S9` 的 `prsn` chunk 逐条扫描，三项列传资源编号始终落在相同人物记录；再与工作区内已生成的人物姓名资源目录交叉核对：

| 列传资源编号 | `prsn` 记录序号 | 游戏姓名 | 通行史名（解释） |
| ---: | ---: | --- | --- |
| `563` | `675` | 灵帝 | 汉灵帝刘宏 |
| `564` | `676` | 少帝 | 汉少帝刘辩 |
| `565` | `677` | 献帝 | 汉献帝刘协 |

前三列是正式剧本与人物姓名资源的直接事实；最后一列是对游戏帝号的通行历史身份解释，并非 `prsn` 中另存的姓名字符串。

这里必须区分“列传资源编号”与解包后列传文本表的零基数组下标。直接读取 `M_RtdnPK.s9` 的第 `563/564/565` 条会得到其他人物的列传，不能据此否定上述人物映射；原程序是先用 `prsn +0x10` 在人物表中定位候选人物，而不是把三个常量直接当作文本表数组下标。

## IDA 标注变更与验证

四个自动名先经不覆盖已有名称的 dry-run 验证，随后写入项目主数据库：

| 地址 | 新名称 |
| --- | --- |
| `0x455660` | `find_person_by_biography_resource_code` |
| `0x46A7D0` | `decode_scenario_world_flag_bit` |
| `0x46A940` | `set_scenario_world_flag_word` |
| `0x46A960` | `test_scenario_world_flag_bit` |

另在 `find_person_by_biography_resource_code`、`test_scenario_world_flag_bit`、`deserialize_scenario_head` 和 `get_current_emperor_person` 写入 `[RE-SAN9PK-0060][FACT]` 注释。保存项目主数据库后完成名称及重新反编译结果回读；未修改程序字节或函数边界。

## 结论与限制

- 事实：皇帝解析开关来自 `head +0x04` bit 0；`head +0x18` 是起始日期编码。
- 事实：候选人物由 `prsn +0x10` 的列传资源编号 `563/564/565` 识别，位置和身份取 `prsn +0x18/+0x1C`。
- 事实：三个候选依次为人物记录 `675/676/677`，游戏姓名为灵帝、少帝、献帝。
- 事实：皇帝所在城市由当前人物位置动态解析，不在 `city` 记录中单独定义。
- 非核心命名项：该世界标志 bit 0 在皇帝解析之外的完整剧情名称。
- 适用范围：繁体中文版 1.0.1.0 当前样本。

> **SCN-02 收口（2026-08-26）**：bit 0 置位会禁用皇帝解析；否则身份 4 的俘虏仍是有效皇帝候选，只有身份 8 使对应人物失效。皇帝身份与势力官爵、君主继任相互独立，见 [RE-SAN9PK-0141](RE-SAN9PK-0141.md)。
