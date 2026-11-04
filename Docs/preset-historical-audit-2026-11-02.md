# 七个内置预设的历史与开局数据复查

本轮核查对象为战国（前260）、罗马帝国（117）、三国（230）、盛唐（750）、查理曼（800）、十字军（1190）、临高（1628）七个预设。
修正已有证据支持的年代、都城、城市控制和数据过滤问题；没有修改平滑显示、地形距离或地图单位分区算法。
这不是对所有历史国界逐点完成认证。城市归属与无人区行政范围是两层数据，修正城市并不自动得到精确历史边界。

## 已落实

| 预设 | 修正内容 |
| --- | --- |
| 战国、三国、查理曼 | 修补首都的小地图可用性，清理与现有城表年代不一致的行政区城市引用；未凭猜测新增国界。 |
| 罗马117 | 按用户选择采用图拉真末年极盛时点。君主改为图拉真、配偶改为普洛蒂娜；保留罗马占据泰西封的极盛主题。移除将泰西封、苏萨、巴士拉混在一起的“亚述”行政区，将泰西封列入美索不达米亚。苏萨从卡拉塞尼转入安息势力范围这一粗粒度表示。东汉撤去对整个西域的默认地区声明，补充疏勒、于阗、车师三个绿洲政权。 |
| 盛唐750 | 渤海首都改为中京显德府，补入和龙西古城附近的城点，不再提前使用755年才迁入的上京龙泉府作为首都。 |
| 十字军1190 | 明确为1190年春：皇帝为仍在世的腓特烈一世，亨利列为儿子及继承人，康斯坦丝为亨利之妻。阿卡归阿尤布守军，耶路撒冷王国以提尔为开局据点；塞浦路斯单列为伊萨克控制的独立政权。 |
| 临高1628 | 柬埔寨首都从洛韦改为乌栋，增加乌栋城点。保留澳宋的架空设定。 |

### 最小地图遗漏首都

运行时按城市等级过滤小地图城点；部分预设首都原为等级3，最小地图只保留等级不大于2的城点，可能造成迁都或整个国家缺席。
在既有默认数据生成器中增加“开局有效首都至少为等级2”的规则，补充28处覆盖，不修改运行时筛选：

| 预设 | 新增首都等级覆盖 |
| --- | --- |
| 战国 | chen、guiyang、chifeng、almaliq、sinope、petra |
| 罗马 | treva、gothiscandza、almaliq |
| 三国 | almaliq、gothiscandza |
| 盛唐 | chifeng、cangas、erfurt |
| 查理曼 | benevento、tahert、aberffraw、zhending、chifeng |
| 十字军 | pamplona、sharukan、tarsus、tripoli_lb、onon、kerulen、khovd |
| 临高 | shigatse、sohar |

既有的37处非首都治所等级调整保留。首都被纳入计划不保证地图尺寸不足时每座城都能独立成城；相邻城点仍可能按现有规则合并。

### 年代交界的在位者

同一年可以包含退位者与继位者。原前史逻辑一律按结束年份排除，117年的图拉真会被误当作已经结束在位，1190年的腓特烈也有同类问题。
`WorldMapHistory.ReigningAt` 现在先在有效年份内匹配剧本指定人物编号，再匹配多语言姓名；未匹配时保持原先结束年不包含在内的默认选择。
被选为今上的人不再同时进入已故先帝列表，当前史书只预载开局年之前的大事，防止1190年春的腓特烈已被记载为同年夏天溺亡。年内先后无法从仅有年份的事件表判断，因此不预载今上开局当年的事件。没有将剧本中任意姓名强行匹配到已经过期的在位区间。

### 城表年代的一致性

清理11处行政区对开局无效城点的引用：800年的马格德堡、汉堡；750/800年的昔兰尼；1190/1628年的帕尔米拉；117/230年的佩拉；117年的特莱姆森；前260年的大不里士、克尔曼。
这里遵循的是项目已有城表 `from`/`to`，不代表这些聚落在相关年代必定不存在。运行时此前已经跳过这些引用，本次使默认数据与实际计划一致；城表年代本身仍可据史料另行完善。

## 史料与解释边界

- 图拉真116—117年攻取泰西封、设置东方行省，以及117年哈德良撤军：大都会博物馆图录 [The World between Empires](https://resources.metmuseum.org/resources/metpublications/pdf/The_World_between_Empires_Art_and_Identity_in_the_Ancient_Middle_East.pdf)。图拉真生于53年、在位98—117年：[圣路易斯艺术博物馆](https://www.slam.org/accessibility/ancient-splendor-roman-art-in-the-time-of-trajan/)。
- 普洛蒂娜为图拉真之妻：[Getty Roman Portraits](https://www.getty.edu/publications/resources/virtuallibrary/0866590048.pdf)。人物年龄仍为近似值，尤其普洛蒂娜的出生年没有在本轮确证；47岁不作为精确史实承诺。
- “亚述行省”的短期设置存在争议：[Encyclopaedia Iranica: Trajan](https://www.iranicaonline.org/articles/trajan/)。苏萨与埃利迈斯、安息的关系也不能简化成始终由中央直接管理：[Elymais](https://www.iranicaonline.org/articles/elymais/)、[Susa, Hellenistic and Parthian Periods](https://www.iranicaonline.org/articles/susa-iv-hellenistic-parthian-periods/)。本次仅撤销明显不妥的卡拉塞尼归属及混合行政区，没有宣称“罗马从未设置亚述行省”。
- 东汉107年撤销西域都护、123年班勇出屯并恢复经营：《后汉书》西域传 [华盛顿大学所载英译](https://depts.washington.edu/silkroad/texts/hhshu/houhanshutextonly.html)。117年不能继续将整个西域当作稳定的汉朝直辖地区。三个绿洲政权只是现有城点尺度下的表示，没有完整复原塔里木所有国家及其边界。
- 中京显德府约748年成为渤海都城、755年迁上京：[吉林省地方志编纂委员会，西古城](https://dfz.jl.gov.cn/ybjl/201106/t20110607_5217093.html)。新增城点坐标为遗址附近的近似值。
- 腓特烈一世在位至1190年：[MGH皇帝文书目录](https://data.mgh.de/databases/abbildungsverzeichnis/abbver_db_search.xql?group=friedrich-i-barbarossa)。其死亡发生在1190年夏：[美因茨州立博物馆](https://www.landesmuseum-mainz.de/angebote-programm/online-ausstellung/sektion-41)。春季快照使用腓特烈，避免将亨利的后续身份提前。
- 阿卡在1191年才被攻取：[Fordham所载中世纪纪事](https://sourcebooks.web.fordham.edu/source/1191acre.asp)。1190年的提尔实际由蒙费拉的康拉德掌握，居伊在围攻阿卡；游戏仍用一个耶路撒冷王国表示这一阵营，没有完整模拟内部权力分裂。
- 塞浦路斯在理查1191年征服前由伊萨克控制：[尼科西亚市政府](https://www.nicosia.org.cy/en-GB/discover/nicosia/nicosia/richard/)。单列国家表示实际独立控制，未完整表示其自称皇帝的头衔。
- 乌栋的早17世纪首都地位及坐标：[柬埔寨提交UNESCO的乌栋暂定名录资料](https://whc.unesco.org/en/tentativelists/6459)。材料采用1618年起，其他研究对迁都时点有差异，但不影响1628年应采用乌栋。

## 尚需独立补充的历史地块数据

1. **750/800年的朝鲜半岛北部。**新罗的大地区声明与渤海的盖马无人区指定仍过于粗糙，不能把全部 `korea_n` 简单整块送给任一方。应增加按年代划分的默认边界和缓冲地带，结合平壤等城点的归属研究。参考 [韩国国家地图集](https://nationalatlas.ngii.go.kr/pages/page_1267.php) 与 [韩国国立中央博物馆渤海展厅](https://www.museum.go.kr/ENG/contents/E0201031000.do?showHallId=760&showroomCode=DM0010)；不同年代不能照搬渤海极盛图。
2. **唐代道界与边疆控制。**原始算法在缺少历史多边形时仍按治所等默认信息划区，关内、河西、安西及唐—吐蕃—回鹘边缘仍可能很粗。需要补可靠边界资料，不应通过扭曲平滑算法假装已经得到历史疆界。
3. **1628年的葡萄牙与西班牙。**当前以同一王冠下的一个势力和海外行政区近似伊比利亚联合；没有表现葡萄牙独立的王国及行政体系。适合后续按游戏的共主关系能力单独建模，不能仅为配色改成互不相关的国家。
4. **750年的法兰克实际统治者与名义国王。**丕平当时是掌权宫相，751年才称王。当前国主栏采用实际掌权者，却仍挂墨洛温王朝；需要先区分“国家掌权者”和“名义君主”的表现方式，避免机械换名后仍丢失一方。

本轮没有足够证据逐点改写上述边界，也没有把缺少城点的土地随意划成无人区。先修明确错误，后续继续补默认数据。

用户随后提出参考CK3。已直接对照本机原版的省份图、不可通行区域、荒地走廊、法理层级和开局省份设置，详见 [CK3无人区对照记录](ck3-wilderness-reference-2026-11-02.md)。完整调用链核查更正了“全部最终归首都”的简化判断，并已落实地块中立和45处明确地方归属；详见 [无人区完善记录](wilderness-refinement-2026-11-02.md)。

## 维护与验证

`historical_presets_source.py` 在其他城表、人物、行政区生成器之后运行，定点覆盖本轮选择；`consorts_source.py` 中相应配偶来源已同步。
若新增首都，再运行默认等级生成器。以下命令从模组根目录执行：

```powershell
python -X utf8 Tools/WorldMap/historical_presets_source.py .
python -X utf8 Tools/WorldMap/administrative_defaults_source.py .
python -X utf8 Tools/WorldMap/historical_presets_source.py . --check
python -X utf8 Tools/WorldMap/administrative_defaults_source.py . --check
python -X utf8 Tools/WorldMap/check_historical_presets.py .
python -X utf8 Tools/WorldMap/check_administrative_boundaries.py .
Get-ChildItem WorldMaps/eras/*.json | ForEach-Object { python -X utf8 Tools/WorldMap/check_scenario.py . $_.FullName }
dotnet build EmpireCraft.csproj --no-restore -v minimal
```

验证结果：七个剧本结构检查均无错误、无提示；1085项城市控制、首都、日期及最小地图断言通过；35处既有历史行政范围校验通过；本地专项测试直接提取生产方法，13项在位者、年界及当前事件截止检查通过；编译0错误。完整编译15条既有警告，无代码改动时的增量编译0警告。
1085项主要是遍历数据产生的结构断言数量，不代表核实了1085条历史事实。

离线默认地块预览覆盖七个最小地图及罗马、盛唐、十字军、临高中尺寸地图。预览采用地球采样地形与原分区实现，可检查默认数据，但不是游戏最终平滑显示截图，也不能替代实际开局游玩验证。
上述开局城点、国主、领土修改需要重新生成预设世界才体现；不会迁移已有存档的城市或重写其历史。
