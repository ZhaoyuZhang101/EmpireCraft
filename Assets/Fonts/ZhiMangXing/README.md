# 钟齐志莽行（Zhi Mang Xing，行书）

来源：Google Fonts 开源字体项目，作者 Wei Zhimang（钟齐志莽），https://github.com/googlefonts/zhimangxing
原字体文件 `ZhiMangXing-Regular.ttf` 取自 https://github.com/google/fonts （ofl/zhimangxing/），
SHA-256 `644e0cae9b40f0b10ab729a01bd32032e3973bac22be3dccae01bf6ae7fde969`。
许可：SIL Open Font License 1.1（见同目录 OFL.txt），可免费商用、可再分发。

模组里放的是 `ZhiMangXing.glyphs`：用 Tools/FontBake/bake_seal_glyphs.py 把上面的字体按 128 号字逐字画好的字形包
（128 号字、16 级灰阶，7012 字：GB2312 简体全收，Big5 常用繁体约六成）。游戏里直接拼这些字形显示，不靠 Unity 读字体文件。
字体里没有的繁体字，显示前用 OpenCC 的简繁对照表（../OpenCC/STCharacters.txt，反查）换成对应的简体字形，
所以繁体国号也能整句用这款行书显示；简繁都没有的字按 `Homophones.txt` 换成字体里有的同音字
(python3 Tools/FontBake/build_homophones.py Assets/Fonts/ZhiMangXing/ZhiMangXing.glyphs Assets/Fonts/ZhiMangXing/Homophones.txt，需要 pypinyin)。
重新生成：python3 Tools/FontBake/bake_seal_glyphs.py ZhiMangXing-Regular.ttf Assets/Fonts/ZhiMangXing/ZhiMangXing.glyphs 128 16

原字体文件 `ZhiMangXing-Regular.ttf` 也一并放在这里：双击它安装到系统(Windows 选"安装")、重启游戏后，模组会自动改用系统里的这款字体
(和本机的隶书、行楷一样是动态字体，按需要的字号现画，任何缩放都清楚、大小与其它字体一致)；没安装就用上面的字形包。
