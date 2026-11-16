# 彻底独立：模组对原版的依赖清单(2026-11-16)

目的：模组做成彻底独立的新游戏以前，先摸清模组的玩法代码碰了原版多少、碰在哪儿，按好拆的程度排先后。
口径见文末「怎么数的」。数字是 11-16 工作区的，之后每拆一块重跑一遍对账。

用户定了：独立游戏另开一个新的 git 仓库写，不在模组里原地拆。所以下面的分档当"往新仓库搬什么、怎么搬"用：
第 0 档整份拷过去；第 1 档拷过去后把原版的国、城、人换成新仓库自己的实体；第 2 档在新仓库的引擎里直接实现那些世界操作；
第 3 档按新游戏自己的地图模型重写；第 4 档在新仓库里另立自己的表。替身层、补丁、从原版抽出来的表与编号一概不进新仓库。
第四节"建议顺序"是按原地拆写的，另起仓库时只取它的先后：先实体层，再搬系统。

## 一、总账

| 部分 | 行数 | 原版对象引用 | 说明 |
|---|---:|---:|---|
| 玩法核心(独立服务器编进去的那些) | 25.4 万 | 22,821 | 要拆的主体 |
| 界面(UI、神力、诊断) | 6.0 万 | 2,060 | 不搬：新游戏的客户端重写 |
| 补丁(GamePatches) | 2.4 万 | 2,115 | 不搬：1,650 处补丁标记，挂在 281 个原版成员上；里头的逻辑要迁走 |

核心那 22,821 处按领域分：

| 领域 | 引用 | 主要类型 |
|---|---:|---|
| 政权组织 | 15,586 | Kingdom、City、War、Religion、Culture、Clan、Alliance、Plot |
| 地图地块 | 7,365 | World/MapBox、TileZone、WorldTile、Building、TileType |
| 人物 | 3,590 | Actor、Subspecies、Family |
| 原版资产表 | 2,372 | PlotAsset、MetaTypeAsset、WorldLawAsset、BuildingAsset、NameplateAsset |
| 运行、存档、日志 | 1,781 | Date、WorldLog*、Config |
| AI 行为 | 455 | Beh*、CityBehBuild |

(界面类型、工具类型另算；Mathf、颜色、LM、LogService 在服务器里已经是手写的替身，换掉不费事，不计入。)

## 二、依赖分四种，难度不同

1. **当"身份"用**(最多，最机械)：拿原版的国、城、人当钥匙，状态记在模组自己的扩展数据里(GetOrCreate 1,470 处)。
   读的大多是 `isRekt` 3,419、`.data`(名字、颜色、编号)5,969、`capital` 997、`cities` 806、`king` 805、`isAlive` 598、`getName` 593。
   换成自己的实体编号与字段就行，规则不用动。
2. **借原版的运行机制**(要先在自己的引擎里实现)：模组让原版去做"改世界"的事。按调用次数：
   城换主 `joinAnotherKingdom` 76、人入城 `joinCity` 67、开战 `startWar`/`newWar` 77、停战 `endWar` 42、自立 `makeOwnKingdom` 31、
   立君/废君 `setKing`/`removeKing` 50、参战 `joinAttackers`/`joinDefenders` 51、退盟 `leave` 24、城领地 `addZone`/`removeZone` 31、
   迁都 `setCapital` 20、立教 `newReligion` 10、起计谋 `newPlot` 12、改地块 `setTileTypes` 8、人死 `die` 8。
   还有原版每帧自己跑、模组靠补丁改的：人口生死、城扩张与边界、建筑、军队移动、外交意见、忠诚。
3. **用原版的资产表**：计谋表(90 处)、神力表(86)、铭牌(41)、资源(40)、世界法则(37)、单位种类(26)、建筑(25)、顶层地块(18)、元类型(18)……
   数值和规则早就是模组自己定的，只是挂在原版的表上；资源表是从原版资源库抽的，独立时要换成自己的。
4. **界面与补丁**：新游戏不要；补丁里挂的玩法逻辑(按目标：City 45 个成员、Actor 38、ZoneCalculator 11、MapBox 11、Kingdom 10、城市建造 AI 9、War 7……)要改成自己引擎里的直接调用。

## 三、按好拆程度排的清单

### 第 0 档：已经独立，直接搬(218 个系统，2.3 万行)
196 个系统一处原版对象都不碰，另 22 个每千行不到 15 处。主要是：
- 规则与数值：奏议/诉求规则(ClaimRules、CourtIssues、CourtDoctrine)、官品(OfficialRankRules)、王朝周期(DynasticCycleRules)、制度定义与配置(InstitutionDefinitionRegistry、InstitutionConfig)、国债、族群等第、帝国权威、选帝、草原规则、人口数学、城稳定数学、宪法与议会法案、党派财政与党际关系、舆论……
- 自己的数据类：Data/ 下的存档结构(宪政经济、朝局、制度、教廷、神罗、军团、名人……)
- 终端与联机：TerminalServer、TerminalRelay(大厅)、TerminalMdns、TerminalQr、管理员密码、核心接手(CoreHost)、轻量主机
- 帧预算与调度：FrameJobs、SimulationFrameBudget、帧率稳定

### 第 1 档：只拿原版对象当身份，换成自己的实体就行(量最大、最机械)
政治、宗教、朝堂、外交这一大片。规则全是自己的，读原版对象只为拿名字、颜色、首都、君主、归属。代表(行数 / 原版引用)：
- 宗教：教廷 PapacySystem(2,955 / 483)、伊斯兰分裂(1,140 / 199)、宗教分裂、宗教圈、融合、本土信仰、背景宗教
- 帝国与天下：神罗 HolyRomanSystem(3,962 / 507)、Empire 本体(4,991 / 476)、天下、周制(1,264 / 207)、统一(1,423 / 209)、帝国分治、复合帝国、帝国成型
- 朝堂与政体：制度 InstitutionSystem(4,405 / 211)、朝堂、共和、议会、政党、官职、临时派系、量刑律法(3,719 / 296)、王室
- 外交：议和台 PeaceTable(958 / 142)、外交账、外交行动、附庸、殖民、流亡政府、军阀
- 人物线：族谱(1,555 / 107)、名人、世家、人物故事、意识形态人口

这些要等「自己的实体层」立起来再改(见第四节①)，改法统一：`Kingdom`→自己的政权、`City`→自己的城、`Actor`→自己的人，`isRekt`/`data.*` 换成自己的字段。

### 第 2 档：借了原版的运行机制，要先有自己的那套操作
- 归属与生灭：城换主、自立、立国撤国、立君废君、迁都——统一、叛乱、帝国分治、部落分迁、南明结成、流亡政府都靠这些
- 战争：开战、参战、停战、联盟——战争目标 WarGoalSystem(977 / 183)、战争目标 WarObjective、议和、劫掠联盟、军阀统一
- 计谋：原版的计谋框架 AI/EmpireCraftPlotsAddition(2,945 / 327，其中原版计谋表 173)、决议 Decisions
- 人口生死与年龄：CityPopulationSystem(3,029 / 323)、真实年龄、继承
- 先把这些操作包成模组自己的接口(城归属、开战停战、立君)，玩法代码全改调接口；接口里先照旧调原版，独立时换成自己的实现。这一步做完，大部分补丁就能删。

### 第 3 档：地图地块，要先定自己的地图模型
地图引用 7,365 处，几乎都压在少数系统上：
- 军事战线 GeneralSystems/Warfare(14,989 行 / 2,123，其中地图 873)：战线按分区推进、合围、补给
- 世界地图与剧本 GeneralSystems/WorldMap(15,347 / 826)
- 平滑国界 SmoothBorders(4,129 / 435)：一半是画法(归客户端)，一半是归属
- 无人区三件(WildernessSystem 2,978 / 359、无人区计谋、无人区开发)、城地碎块、城界
- 农田、城区规划、村镇、营建、关城与长城、前线判定、军队移动 AI
- 用到的原版地图概念：分区 TileZone(2,471 处)、地块 WorldTile(1,673)、地块类型、建筑、河流与山脉判定
- 新游戏的地图不必照原版：模组在纯净地图模式下只用分区归属、地形、河流、道路这几层，可以按这几层设计自己的格网

### 第 4 档：原版资产表，换成自己的表
- 计谋表、世界法则表(EmpireCraftWorldLawLibrary)、元类型表(MetaTypeLibrary)、铭牌表、意见表、特质表、科技回退里的建筑表、城市账本里的建筑表
- 资源表(从原版资源库抽的名字与数值)：独立时重定自己的物资表
- 起名库：原版的起名模板(Onomastics)要换成自己的

### 不搬：新客户端里重写
- 界面 UI(5.3 万行)、神力(2,934)、诊断(3,841)、铭牌与贴图库(NamePlate、QuantumSprite)、平滑国界的画法
- 已有的手机终端网页可以直接当新客户端的底子(舆图、席位、史书、管理页)

## 四、建议顺序

1. **立自己的实体层**：政权、城、人、分区各一套编号与核心字段(名字、颜色、首都、君主、归属、存亡)，原版对象只在一层薄适配里出现；先双写，存档里一起带。
2. **迁第 1 档**：量大但机械，每迁一个系统都还能进游戏、在核心上照测。
3. **做自己的世界操作接口(第 2 档)**：城归属、开战停战参战、联盟、立君废君、迁都、立教、人口生死；玩法代码改调接口，补丁跟着删。
4. **定自己的地图模型(第 3 档)**：分区、地块、地形、河流、道路，战线与无人区改用它。
5. **换掉原版资产表(第 4 档)**。
6. **新客户端**：Unity 只画画面、管操作，接核心的接口；手机终端照旧。

进度量尺(每拆一块重跑对账)：
- 核心里的原版对象引用：22,821 → 0
- 补丁挂的原版成员：281 → 0
- 服务器替身层：生成 6,429 行 + 手写 5,254 行 → 0(那时核心就是新游戏的服务器)

## 五、依赖最重的 30 个核心系统

| 系统 | 行数 | 原版引用 | 每千行 | 主要原版类型 |
|---|---:|---:|---:|---|
| GeneralSystems/Warfare(军事战线) | 14,989 | 2,123 | 142 | Kingdom 756、TileZone 490、City 386、World 249 |
| GeneralSystems/WorldMap(世界地图与剧本) | 15,347 | 826 | 54 | City 199、Kingdom 144、World 108、Actor 96 |
| Terminal/TerminalHost(手机终端) | 8,971 | 574 | 64 | Kingdom 200、World 113、City 112、Actor 85 |
| HolyRomanSystem | 3,962 | 507 | 128 | Kingdom 270、Actor 88、World 69 |
| PapacySystem | 2,955 | 483 | 164 | Kingdom 166、Religion 129、World 84 |
| Layer/Empire | 4,991 | 476 | 95 | Actor 156、Kingdom 131、World 90 |
| SmoothBorders | 4,129 | 435 | 105 | TileZone 137、元对象接口 107、World 58 |
| TransmigrantSystem | 2,276 | 361 | 159 | Kingdom 121、City 86、World 48 |
| WildernessSystem | 2,978 | 359 | 121 | TileZone 102、City 98、Kingdom 66 |
| AI/EmpireCraftPlotsAddition | 2,945 | 327 | 111 | Actor 135、Kingdom 103、PlotAsset 73 |
| GameClassExtensions/KingdomExtension | 4,077 | 324 | 80 | Kingdom 249、World 33 |
| CityPopulationSystem | 3,029 | 323 | 107 | City 131、Actor 67、Kingdom 51 |
| GeneralSystems/EmpireLaw | 3,719 | 296 | 80 | Actor 143、Kingdom 123 |
| GameClassExtensions/CityExtension | 2,804 | 280 | 100 | City 132、Kingdom 71、TileZone 36 |
| GameLibrary/EmpireCraftNamePlateLibrary | 2,529 | 230 | 91 | City 83、Kingdom 52、铭牌表 |
| InstitutionSystem | 4,405 | 211 | 48 | Kingdom 98、World 51、Culture 33 |
| UnificationSystem | 1,423 | 209 | 147 | Kingdom 120、City 37 |
| ZhouEraSystem | 1,264 | 207 | 164 | Kingdom 117、World 46 |
| IslamicSchismSystem | 1,140 | 199 | 175 | Kingdom 81、Religion 69 |
| NomadRaidSystem | 1,127 | 188 | 167 | Kingdom 128、War 16 |
| WarGoalSystem | 977 | 183 | 187 | Kingdom 91、City 45、War 24 |
| GameClassExtensions/ActorExtension | 1,982 | 176 | 89 | Actor 133 |
| ShuiTuSystem(水土) | 1,227 | 167 | 136 | Kingdom 72、City 61 |
| CultureService | 1,939 | 162 | 84 | City 62、Kingdom 36 |
| AI/EmpireCraftActorCheckWarriorMoveAdvanced | 1,287 | 161 | 125 | TileZone 68、Kingdom 40、Actor 28 |
| CityZoningSystem | 829 | 159 | 192 | City 56、Building 49、TileZone 38 |
| AdministrativeControlSystem | 903 | 158 | 175 | Kingdom 55、City 49、TileZone 35 |
| LandEconomySystem | 1,189 | 157 | 132 | Actor 55、City 53 |
| GameLibrary/EmpireCraftMetaTypeLibrary | 1,236 | 151 | 122 | 元类型表、Kingdom 29、City 26 |
| FeudalVassalService | 719 | 150 | 209 | Kingdom 105、World 26 |

按密度分档(核心 701 个系统)：不碰 196 个 / 1.6 万行；稀疏 22 个 / 0.8 万行；中等(每千行 15~60)112 个 / 5.7 万行；
密集(60~120)200 个 / 10.0 万行；很密(≥120)171 个 / 7.3 万行。

## 怎么数的

- 原版类型表：独立服务器的替身生成器从模组核心编出的 dll 里扫出的原版类型(.server/Shim/Generated/shim-report.txt，Assembly-CSharp 的 512 个)，按名字分领域。
- 逐个源文件去掉注释和字符串，数这些类型名出现的次数；系统按文件名(分部类合并)，GeneralSystems 下的子目录各算一个，界面、神力、诊断、补丁各算一块。
- 方法级的次数(isRekt、joinAnotherKingdom……)取自同一份 shim-report(那是编译器认到的引用，比数名字准)。
- "扩展数据"数的是 `.GetOrCreate()`；补丁数的是 Harmony 的特性与 `__instance`；补丁目标取自 patch-targets.txt。
- 只数了名字出现的次数，不分轻重：读一次名字和改一次归属都算一处，所以第二节单列了"改世界"的操作。
- 数脚本在会话的临时目录里，没进仓库；要长期对账，可以放进 .server/tools。
