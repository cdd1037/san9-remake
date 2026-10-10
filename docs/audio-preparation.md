# 音乐与音效离线整理

本页说明原版音频的离线准备步骤；转码不固定到版本Importer中，不作为每次开发或交付的自动前置。Importer只识别已准备音频，按语义重命名并复制到运行包。

## 目录与命名

- 导入输入：忽略的`private/游戏文件/BGM`与`private/游戏文件/SE`。已准备音乐保留原作基名，如`BGM_04.ogg`或`BGM_04.mp3`；原始WMA可保留，但不作为可播放音频导入。
- 音乐输出：`local/generated/content/scenario-190-live/audio/music/`。
- 音效输出：`local/generated/content/scenario-190-live/audio/sound-effects/`，原样复制，不转码。
- 本批离线对应清单：`local/generated/content/scenario-190-live/audio/manifest.json`，记录原始名称、新路径及含义是否确认；这是本批准备记录，不是Importer输入或运行时绑定，实际路径以`presentation.json`为准。

音乐和音效统一支持WAV、Ogg Vorbis与MP3，识别实际扩展名，语义名称独立于编码。把同基名输入从Ogg替换为MP3后再导入，会自动复制成对应`.mp3`并更新绑定；客户端按文件格式加载。一个源基名只能存在一种可导入格式，多格式冲突报错，不按扩展名优先级选择。现有Ogg只是本批音乐的格式，音效本批保持WAV。所有已准备音频都复制一次，多个角色共享同一物理路径；导入完成清理音频目录中不再对应当前输入的旧音频，保留非音频准备记录。

本批27首音乐与170个音效已全部采用英文语义名，例如`map-spring.ogg`、`map-winter-high-rank.ogg`、`diplomacy.ogg`、`construction.wav`和`execution-title.wav`。完整对应唯一维护在[AudioAssetCompiler](../src/San9.Importer.San9PkTc101/Interface/AudioAssetCompiler.cs)。共享声音采用共同用途命名，不按每个调用角色复制；不同角色可引用同一个文件。第三方支持型名称与本项目独立确认的行为分开记录；尚不确定的细分条件采用宽用途名称，不凭听感命名。未来新增但未登记用途的输入保留小写源名。来源及原表纠正见[完整音频命名复核](../research/findings/RE-SAN9PK-0187.md)。

## 转换命令与限制

源音乐27首均为ASF容器、WMA v2编码、44.1kHz双声道，音频码率约160kbps。使用PATH上的FFmpeg及`libvorbis`编码器，把每个输入转换一次；下面命令用于单个已选定素材，不要求重复转换整个目录：

```powershell
ffmpeg -nostdin -hide_banner -loglevel error -n -i "private/游戏文件/BGM/BGM_04.WMA" -map 0:a:0 -vn -af "asetpts=N/SR/TB" -c:a libvorbis -q:a 5 -ar 44100 -ac 2 -map_metadata -1 "private/游戏文件/BGM/BGM_04.ogg"
```

- `.ogg`必须实际为Ogg容器中的Vorbis音频，不能只改扩展名，也不能以Ogg Opus代替本次约定。
- `-q:a 5`为Vorbis可变码率质量设置，不保证固定160kbps或与原始WMA同体积。WMA已经有损，二次有损转换不能提升音质；听感需实际试听验收。
- 保留44.1kHz双声道，不自动提高采样率、混成单声道、调整响度、截断静音或修改时长。`-map_metadata -1`丢弃源元数据；当前流程不转换或推导循环点，不能据此认为首尾循环已无缝。
- 部分源音乐时间戳不连续，直接沿用会使转换后的解码输出出现非单调DTS错误。`asetpts=N/SR/TB`按实际解码采样数重建连续时间戳，不增删采样；时长核对以解码音频为准，容器声明时长允许编码延迟的微小差异。不能仅凭FFmpeg退出码为0判定通过，错误输出也必须检查。
- `-n`拒绝覆盖已有目标。只在源素材或转换设置实际变化、目标缺失／损坏时重做受影响文件；不以消费者代码变化或交付为触发条件。覆盖前明确核对目标和来源，不通过自动删除整个目录重转。
- 整理完成后用`ffprobe`核对全部输出的容器、编码、采样率、声道和时长，并进行解码错误检查。音效复制按字节对照确认，保持原有编码与格式。
- 输出、清单和原版素材均留在忽略目录，不提交。准备完受影响输入后集中执行一次`import-client`更新资源和绑定，不手改运行包；音频变化不需要`import-textures`。导入不运行FFmpeg、不调整音频参数、不按播放角色复制同一源文件；缺失已准备素材直接报错，不自动回退到WMA转码。
