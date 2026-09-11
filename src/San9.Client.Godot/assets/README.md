# 字体资源

`GameFont.tres`统一用于普通控件、地图文字和富文本。主字体仍为霞鹜文楷等宽GB轻量版；主字体缺字时使用BabelStone Han，不修改Unicode或用其他汉字代替。

- 主字体：[LXGW WenKai GB](https://github.com/lxgw/LxgwWenkaiGB)，许可见`OFL.txt`。
- 回退字体：[BabelStone Han](https://www.babelstone.co.uk/Fonts/Han.html)，使用作者16.0.3版原始TTF文件，未裁剪或修改；许可全文见`ARPHICPL.txt`。仅在主字体缺字时使用其宋体字形。

运行`--text-smoke`生成全量文本字形覆盖报告与图文展示页，输出位于`local/generated/verification/text/`。报告包含源文及简体文本，按Unicode标量检查扩展汉字。
