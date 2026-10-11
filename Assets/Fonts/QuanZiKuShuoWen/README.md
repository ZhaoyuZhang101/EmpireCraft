# 全字库说文解字（小篆）

来源：全字库（CNS11643 中文标准交换码全字库），台湾"国家发展委员会"提供。
官方说明：https://www.cns11643.gov.tw/pageView.jsp?ID=59
原字体文件 `全字庫說文解字.ttf` 取自整理仓库 https://github.com/wordshub/free-font （assets/font/中文/全字库系列/），
SHA-256 `e8501dc9747190936a222576c9b0b6f918ff335afa5696c3fda3acba1a9574e2`。
许可：政府资料开放授权条款－第 1 版（Open Government Data License, version 1.0），可免费商用、可再分发，须注明出处。

模组里放的是 `ShuoWen.glyphs`：用 Tools/FontBake/bake_seal_glyphs.py 把上面的字体按 64 号字逐字画好的字形包
（6729 字，按繁体编码）。游戏里直接拼这些字形显示，不靠 Unity 读字体文件（直接读字体文件在游戏里常常显示成空白）。
显示前用 OpenCC 的简繁对照表（../OpenCC/STCharacters.txt）把简体名字转成繁体再查字。
重新生成：python3 Tools/FontBake/bake_seal_glyphs.py 全字庫說文解字.ttf Assets/Fonts/QuanZiKuShuoWen/ShuoWen.glyphs 64
