# RE-SAN9PK-0065：用户配置注册表边界与进程内虚拟化

- 状态：静态调用链与双 profile Toolkit 修正已确认；修正版目标端到端复测待完成
- 样本：`SRC-SAN9PK-TC-101`
- 关联：[RE-SAN9PK-0062](RE-SAN9PK-0062.md)

## 已确认事实

### API 与作用域

- 主 EXE 导入 `RegOpenKeyExA`、`RegCreateKeyExA`、`RegCloseKey`、`RegQueryValueExA`、
  `RegSetValueExA`、`RegDeleteValueA`、`RegEnumValueA` 和 `RegEnumKeyExA`。
- 这些调用不全是游戏选项：`0x42B230..0x42BAEF` 会枚举系统设备注册表；`0x55E900`
  读取 Windows 卸载信息；`0x5B53A4` 读取一段与系统卷序列关联的数据。因此不能把全部
  注册表访问统一替换为成功或空值。
- MFC 配置路径由 `HKCU\Software`、应用 registry key 和 profile name 逐层构造。样本同时
  包含 `San9 Tc` 与 `San9PK Tc` 两个 profile 名称，不能把全部配置归入单一 profile 根。

### 启动所需安装目录

- `load_install_info_from_profile (0x432970)` 创建/打开 `Install` 子键并两次查询
  `InstallInfo`：第一次取得长度，第二次读取字符串。缺失时该函数返回失败，调用者
  `0x434360` 不继续完成启动初始化。
- 读取结果由 `0x4331A0` 复制，`0x4343D0` 直接追加 `S9Gaiji.tte`。因此 `InstallInfo`
  是资源目录前缀，必须以路径分隔符结尾。

### `Configs` 完整值清单

- `load_game_config_from_profile (0x43C810)` 的主序列执行 19 次 DWORD 查询，对应 18 个
  唯一值名；`FlagData` 被读入两个内部字段，因此出现两次。查询失败时
  `read_game_profile_dword (0x5E1120)` 返回调用者传入的结构初值，即原作编译默认值。
- `sub_43CCE0` 另从同一 `Configs` 节读取 4 个试用剧情 DWORD。合并去重后，当前样本共
  确认 22 个唯一值名：

  | 值名 | 已确认语义或边界 | 结论等级 |
  | --- | --- | --- |
  | `MessageSpeed` | “訊息速度”：`0=快`、`1=普通`、`2=暫停` | 事实 |
  | `GameSpeed` | 默认 `1`，加载时夹取至 `0..2` 并在保存时写回；当前样本未发现运行时消费者 | 事实 |
  | `GameReport` | “進行記錄”：`0=OFF`、`1=要約`、`2=詳細` | 事实 |
  | `GameReportDlg` | 三态报告对话框相关设置，原作夹取至 `0..2`；三档标签未确认 | 事实/待验证 |
  | `JumpList` | 三态列表/地图界面设置，原作夹取至 `0..2`；具体交互语义未确认 | 事实/待验证 |
  | `FullScreen` | 全屏开关，原作夹取至 `0..1` | 事实 |
  | `PlayBGM` | 背景音乐开关，原作夹取至 `0..1` | 事实 |
  | `PlaySound` | 音效开关，原作夹取至 `0..1` | 事实 |
  | `PlayMovie` | 影片播放开关，原作夹取至 `0..1` | 事实 |
  | `SvLdPage` | `0..7` 的存/读档页面索引 | 高置信度推断 |
  | `YNPositionX`、`YNPositionY` | Yes/No 对话框位置 | 高置信度推断 |
  | `KakuninPositionX`、`KakuninPositionY` | 确认对话框位置 | 高置信度推断 |
  | `AppPositionX`、`AppPositionY` | 应用主窗口位置 | 事实 |
  | `FlagData` | 通用打包位集合；已观察到地图显示模式、边缘滚动方式等用途，不是单纯解锁数据 | 事实 |
  | `ArtData` | 由编号映射到 bit 的位集合；内容/鉴赏解锁的逐位映射未闭合 | 事实/推断 |
  | `TrialStory00`、`TrialStory01`、`TrialStoryEvent`、`TrialStoryClear` | 试用剧情状态 DWORD | 事实 |

- `save_game_config_to_profile (0x43CAE0)` 对称保存主序列，`sub_43CD70` 保存四个
  `TrialStory*` 值；这些写入最终均通过 `RegSetValueExA` 保存为 `REG_DWORD`。

### 已闭合的三态标签映射

- 玩家界面截图直接观察到“訊息速度”的 `普通/快/暫停` 和“進行記錄”的
  `詳細/要約/OFF` 六个标签。
- `sub_55DB70` 按界面顺序建立两个三项字符串数组，并分别以置换表 `[1,0,2]` 与
  `[2,1,0]` 从配置值选择当前项；CP950 原始字符串回读与截图标签一致。
- `sub_413D30` 的按钮处理路径对称写回 `config + 1`（`MessageSpeed`）和
  `config + 3`（`GameReport`）。因此上述数值映射不是仅凭界面排列作出的推断。

### 首次目标实测与根因

- 实测环境中的只读注册表核对显示，完整的普通设置（含窗口位置、声音、速度、
  `FlagData`、`ArtData`）位于 `HKCU\Software\KOEI\San9 Tc\Configs`；
  `HKCU\Software\KOEI\San9PK Tc\Configs` 则留有 `TrialStory*` 与 `FlagData`。
- 首版 Runtime 只纳管 `San9PK Tc`，因此 `San9 Tc` 下的普通设置绕过覆盖层，继续读取原生
  注册表且不会写回 `San9Toolkit.ini`。玩家使用正确路径、正确文件名及已核对哈希的构建
  仍复现“既不读也不写”，排除了旧构建与 INI 放置错误。
- 修正后的领域边界是两个明确的本作 profile 根，而不是扩大到整个 `KOEI` 树；二者的
  `Configs` 统一映射到同一 Toolkit INI，二者的 `InstallInfo` 统一由目标 EXE 目录生成。

## Toolkit 实现边界

- Runtime 只为 `HKCU\Software\KOEI\San9 Tc`、`HKCU\Software\KOEI\San9PK Tc` 及其
  祖先句柄建立虚拟视图；两个目标配置根之外的设备、卸载信息和其他安装/授权访问继续
  调用原生 API。
- `Install\InstallInfo` 从当前目标 EXE 所在目录生成，并保证尾随反斜杠。
- `Configs` DWORD 从启动器同目录的 `San9Toolkit.ini` 读取，游戏写入时同步写回同一文件；
  缺值时返回 `ERROR_FILE_NOT_FOUND`，让原作采用自身默认值。
- `FullScreen` 是唯一 Toolkit 强制默认值：INI 未指定时返回 `0`，保持现有可缩放窗口实现
  的产品边界。其余值不由 Toolkit 猜测。
- 虚拟配置不会创建或修改目标游戏的用户注册表键。非目标路径保持原生行为。

## 实现验证

- 独立 Win32 宿主安装与 Runtime 相同的八个 IAT hook，未读取既有游戏配置根；逐层打开
  `Software`，并创建 `KOEI/San9 Tc` 与 `KOEI/San9PK Tc` 两套虚拟句柄成功。
- `InstallInfo` 长度探测与数据读取均成功，返回路径含尾随反斜杠；空 INI 下
  `FullScreen` 返回 DWORD `0`，预写入的 `GameSpeed=2` 可读取。
- `Configs` DWORD 的设置、同会话读取、INI 写回、删除，以及虚拟配置枚举结束语义均通过。
- Toolkit 的 loose 与单文件 Release/Win32 构建均在 `/W4 /WX` 下为 0 警告、0 错误。
- 上述宿主验证 API 契约，不替代目标游戏在真实启动时对 profile name、资源载入和退出保存
  的端到端验证。

## 事实、推断与待验证

- 事实：`InstallInfo` 缺失会使 `0x434360` 的启动初始化失败；该值随后用作资源路径前缀。
- 事实：普通配置查询失败会回退到调用者提供的默认值，已列字段具有明确夹取范围。
- 事实：首次目标实测的 Toolkit 路径、INI 路径和发布 EXE 哈希均正确；原生注册表中的完整
  普通设置位于未被首版纳管的 `San9 Tc` 根，首版失败可由该路径遗漏解释。
- 待验证：修正版是否从 INI 覆盖普通设置、保存时写入所有普通设置、在两个本作 profile 根
  均为空时完成启动，以及第二次启动是否恢复位置和声音等状态。

## IDA 写入与回读

| 地址 | 名称 | 证据 |
| --- | --- | --- |
| `0x432970` | `load_install_info_from_profile` | `Install/InstallInfo` 两阶段查询及启动返回值 |
| `0x43C810` | `load_game_config_from_profile` | `Configs` 19 项读取及范围夹取 |
| `0x43CAE0` | `save_game_config_to_profile` | 对称 DWORD 写回 |
| `0x5E0930` | `write_game_profile_dword` | section 创建与 `REG_DWORD` 写入 |
| `0x5E1010` | `open_game_profile_root` | `HKCU\Software` 下两层应用根构造 |
| `0x5E10C0` | `open_game_profile_section` | profile section 创建 |
| `0x5E1120` | `read_game_profile_dword` | 查询失败回退默认值 |

以上名称、函数类型和事实注释已写入当前主数据库
`research/analysis/San9PK-TC-101.i64`；回读反编译确认 `InstallInfo`、`Configs` 与默认值链
使用新名称，数据库已原位保存。未修改样本程序字节或函数边界。

本次标签映射补充在只读核对后，向 `0x4139E0`、`0x413D30`、`0x55DB70` 追加
`MessageSpeed` 与 `GameReport` 的映射事实注释并原位保存数据库；通过 IDA API 回读三个
函数注释，确认 `0/1/2` 映射及两个置换表文本均已持久化。未修改名称、类型、程序字节或
函数边界。

## `GameSpeed` 消费边界补充

- 配置对象构造函数 `0x43BFB0` 将 `config + 2` 初始化为 `1`；
  `load_game_config_from_profile` 读取 `GameSpeed` 后夹取到 `0..2`，
  `save_game_config_to_profile` 会把该字段写回。
- 对 `config (0x1232700)` 的全部静态交叉引用按调用目标复核后，除构造、加载和保存外，
  没有被调用函数读取 `config + 2`；写入同一偏移的通用 setter `0x46B9A0` 也没有以
  `config` 为对象的调用点。
- 因此当前样本中 `GameSpeed` 是可持久化但未消费的遗留字段，不能据键名推断它会改变
  地图推进、动画或事件速度。
