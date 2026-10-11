# 更新内容 (Update Features)

2026-10-12 · 0.4.4Beta12

2026-10-12 军团与战线、春秋战国、朝堂与华夏官制、卷册宽窗口：
2026-10-12 Corps and fronts, Spring and Autumn and Warring States, court politics and Chinese bureaucracy, scroll-style wide windows:

- **军团与战线（借鉴钢铁雄心）**：无小人模式开启「帝国占领模式」时，征召兵不再一团一个实体兵：各军镇按兵额每八个旧制军团合编一支军，兵力只是数字（从各城人口征召、退伍还回、阵亡扣减）。战线每月结算：各国按上月敌军攻势分兵守城，其余集中进攻一到三段战线；攻方兵力×组织度×火力胜过守方（守方 ×1.25 再加堑壕，无军驻守时由团练抵抗）就沿战线连片占领地块，占满全城即陷落；组织度归零的军溃退回驻地休整。军团每秒行军两块地块，统领阵亡全军动摇。原版计兵数把军团折算进去，战争 AI、投降与叛乱判定照常；起义、革命的义军临时编军，仗打完就地解散。
  **Corps and fronts (inspired by Hearts of Iron)**: With No Commoners Mode and EmpireCraft Occupation Mode on, levies are no longer one entity per unit. Each military post forms corps worth eight legacy legions, and strength is only a number (drafted from city populations, returned on demobilization, deducted on death). Fronts resolve monthly: each realm garrisons its cities against last month's enemy pressure and concentrates the rest on one to three front sectors. If attacker strength × organization × firepower beats the defender (×1.25 plus entrenchment, or local militia when no corps holds the city), the attacker takes contiguous tiles along the front, and the city falls once every tile is taken; corps at zero organization rout home to recover. Corps march two tiles per second, and a commander's death shakes the whole corps. Vanilla soldier counts include corps, so war AI, surrender and rebellion checks still work; rebel and revolutionary militias form temporary corps that disband when the war ends.

- **军人实体开关**：新世界法则「军团统领实体」。打开时每支军最多一名统领实体跟着兵牌走；关闭时地图上不留任何军人实体：城市治安守兵只是人数（照常镇压治安），不再组建原版城市军队，被拉去带兵的名人恢复文职；WarBox 坦克、飞机等载具只记在城里账面，照样给本国军团加火力、随伤亡折损、随城易主。城主、刺史等文官不受影响。
  **Military unit toggle**: New world law "Corps Commander Units". When on, each corps has at most one commander unit that follows its counter. When off, no military units remain on the map: city guards are only numbers (still suppressing unrest), no vanilla city armies are formed, and notables drafted to command return to civil life. WarBox tanks, planes and other vehicles are kept only as city tallies that still add firepower, are lost with casualties and change hands with the city. Governors and other civil officials are unaffected.

- **地图兵牌与交战标记**：交战地块边画金色战线。兵牌用帝国卷册小框，边色表示任务（进攻朱漆、防守石青、溃退铁灰、驻守青铜）；国色印面上一枚棋子表示军种（中央禁军为后、军区为车、军府为马、义军为兵），旁边是兵力与兵力条、组织度条；同一地块同一国的几支军合并成一块。交战标记两侧是攻守双方国色，中间是本月攻方胜算。鼠标悬停时放大、换金边并显示详情（番号、任务、兵力、组织度、统领、驻地；交战的攻守双方、胜算、兵力对比、参战军数、已占地块）；点击直接打开军事页，点进来的那几支军和那处交战排到最前并高亮。
  **Map counters and battle markers**: Gold front lines are drawn along contested tile edges. Counters use small imperial scroll frames whose edge color shows the task (lacquer red attacking, azure defending, iron grey routing, bronze holding). A chess piece on a plate in the realm's color shows the corps type (queen for the central guard, rook for district armies, knight for prefecture garrisons, pawn for militia), beside strength with strength and organization bars; several corps of one realm on the same tile merge into one counter. Battle markers show both sides' colors and this month's attack odds. Hovering enlarges a counter or marker with a gold frame and shows details (designation, task, strength, organization, commander, home city; for battles the attacker, defender, odds, strength comparison, corps engaged and tiles taken). Clicking opens the military page with the clicked corps or battle listed first and highlighted.

- **军团页与军事窗口**：帝国窗口新增「军团」页签：主指标、各国军团表、战线一览与军团纪事。不属于帝国的王国有自己的「军事」窗口，原版王国窗口新增「军事」页签可直接打开。
  **Armies page and military window**: The empire window gains an "Armies" tab with key figures, a corps table for each realm, active fronts and a war log. Realms outside an empire get their own "Military" window, also opened from a new "Military" tab in the vanilla kingdom window.

- **周制春秋战国**：周制帝国按历史分西周、春秋、战国、统一四个时代。西周王室强盛，诸侯不得擅自开战；王畿兵力不及最强诸侯、天命低于 60 或王畿失陷（东迁）时进入春秋。春秋诸侯可以互相征伐，攻下都城即灭其国，但都尊奉天子、共同攘夷：不得攻王畿，犯天子者霸主与诸侯共讨，夷狄攻打诸侯时天子与霸主来援。会盟立最强且尊王者为霸主，诸侯每年以赋税一成纳贡，交不出或不肯交的记为“不朝”，霸主讨伐接壤的不朝之国；大国可能被权臣瓜分（三家分晋）或篡位（田氏代齐），夷狄大国可能僭越称王。诸侯兼并到七国及以下（且春秋以来有国被灭）、发生权臣瓜分或天命低于 30 时进入战国：大国相继称王，霸主废止；变法增加兵额与赋税但旧贵族怨望；一国独大时其余合纵，弱小邻国可能连横依附于它；攻下王畿者取周而代之。直辖天下三分之二的城即称皇帝，改行郡县、设三公九卿。
  **Zhou: Spring and Autumn, Warring States**: Zhou realms follow four historical eras: Western Zhou, Spring and Autumn, Warring States and unification. In Western Zhou the royal house is strong and vassals may not start wars. Spring and Autumn begins once the royal domain's army falls behind the strongest vassal, the Mandate drops below 60, or the royal capital is lost (the eastward move). Vassals may then fight one another, and taking a capital annexes that state, yet all still honor the king and repel outsiders together: the royal domain may not be attacked, anyone who does faces the hegemon and every vassal, and the king and hegemon come to the aid of vassals invaded by foreign peoples. Conferences name the strongest loyal vassal hegemon. Vassals pay a tenth of their taxes as tribute each year; those who cannot or will not are marked defiant, and the hegemon punishes defiant neighbors. Great states may be partitioned by their ministers (the Partition of Jin) or usurped (the Tian usurpation of Qi), and large foreign-ruled states may usurp the royal title. The Warring States begin when seven or fewer vassals remain after at least one has been annexed, when a partition occurs, or when the Mandate drops below 30: great states proclaim themselves kings and the hegemony ends; reforms raise levies and taxes but anger the old nobility; when one state dominates, the rest form a vertical alliance against it while weak neighbors may join it in a horizontal alliance; whoever takes the royal domain supplants Zhou. A ruler who directly holds two thirds of the realm's cities becomes emperor and adopts commanderies and counties with the Three Excellencies and Nine Ministers.

- **天下大势卡与神力**：帝国主页新增「天下大势」卡：时代、霸主（战国时为合纵所抗之国）、诸侯数、天命，列国爵位、国力、城与兵、与周室的关系（霸主、称王、变法、合纵连横、不朝、卿族坐大、征伐与攘夷），以及列国纪事。神力栏新增召集会盟、赐胙（立一诸侯为霸主）、推进时代三个神力，卡片标题条与列国表里也有对应按钮。周制诸侯各用各的颜色，帝国仍用天子的颜色。
  **State of the Realm card and god powers**: The empire home page gains a "State of the Realm" card showing the era, the hegemon (or, in the Warring States, the state the vertical alliance opposes), the number of vassals and the Mandate, plus each state's rank, power, cities and troops, its standing with Zhou (hegemon, king, reforms, vertical or horizontal alliance, defiant, overmighty ministers, wars and campaigns against outsiders) and a chronicle of the states. Three new god powers join the power bar: Call a Conference, Bestow Sacrificial Meat (make a vassal hegemon) and Advance the Era, with matching buttons in the card's title bar and state list. Zhou vassals keep their own colors while the empire keeps the king's color.

- **朝堂博弈**：没有议会的帝国，派系诉求改走奏议五步：上奏 → 票拟 → 廷议 → 批红 → 施行，由皇帝拍板（幼主由权臣或摄政代批，神力可直接准奏、驳回或留中）。议题分各派都能上奏的公共议题（赋税、战和、取士、册立储君、整顿吏治、赈济灾荒等）与派系自带的独特议题，各派按阶层利益与本派主张表态。每派有圣眷、怨望与政治资本，可用于弹劾（失败反坐）、掣肘、邀宠、结盟与拉拢；怨望过高会罢朝，失宠且怨望极高时兵谏。皇帝每年定方略（制衡、偏宠、打压、亲政、摄政），并按才干、圣眷与方略任免内阁。帝国主页有朝局卡（奏议进度、廷议表态、批红预估、各派形势与纪事），行政页有内阁卡与选帝侯卡。
  **Court politics**: In empires without a parliament, faction claims go through a five-step memorial process — submission, drafting, court debate, imperial rescript and implementation — with the emperor deciding (a regent or powerful minister decides for a child ruler, and god powers can approve, reject or shelve directly). Issues are either public (taxes, war and peace, examinations, naming an heir, cleaning up officialdom, famine relief and more), which any faction may raise, or unique to a faction type; each faction takes a stance from its class interests and agenda. Every faction has imperial favor, resentment and political capital to spend on impeachment (which backfires on failure), obstruction, currying favor, alliances and winning others over; high resentment leads to boycotting court, and an out-of-favor faction with extreme resentment may stage an armed remonstrance. Each year the emperor sets a policy (balance, favor, suppress, personal rule or regency) and appoints the cabinet by ability, favor and that policy. The empire home page has a court card (memorial progress, debate stances, rescript odds, faction standings and chronicle), and the administration page has cabinet and prince-elector cards.

- **华夏朝廷与官制演进**：华夏帝国的朝廷随制度演进：周制三公卿士 → 郡县制后三公九卿（丞相定议程）→ 三省六部制（中书起草、门下封驳、尚书施行）→ 内阁制（首辅票拟、皇帝批红）。制度树新增三省六部制与内阁制：三省六部制与科举取士并列（都以郡县制为前置），内阁制需两者皆备。行政区划与官名依历代实制：周制城称邑；郡县后京畿称司州、行政区称州（刺史）、城称郡（太守）；三省六部后京畿道、道（观察使）、县（县令）；内阁后直隶省、省（布政使）、县（知县）；军府、都护府、羁縻地与禁军镇戍的官称同步变化，明代中央改设都察院等。节度使、都护府世袭后仍称“某某军”，不再变成封国。
  **Evolution of the Chinese court and offices**: The Chinese imperial court now evolves with its institutions: the Zhou Three Dukes and ministers, then the Three Excellencies and Nine Ministers after commanderies and counties (the chancellor sets the agenda), then the Three Departments and Six Ministries (the Secretariat drafts, the Chancellery can veto, the Department of State Affairs implements), then the Grand Secretariat (the senior grand secretary drafts, the emperor approves in vermilion). The institution tree adds the Three Departments and Six Ministries and the Grand Secretariat: the former sits alongside the imperial examinations (both require commanderies and counties), and the latter requires both. Administrative divisions and titles follow each dynasty's actual practice: in Zhou cities are towns (yi); after commanderies and counties the capital region is the Metropolitan Province, regions are provinces (inspectors) and cities are commanderies (grand administrators); after the Three Departments the capital region is the Capital Circuit, regions are circuits (surveillance commissioners) and cities are counties (magistrates); after the Grand Secretariat the capital region is the Directly Governed Province, regions are provinces (administration commissioners) and cities are counties. Titles for military commands, protectorates, loose-rein areas and garrison posts change with them, and the Ming court adds the Censorate and other offices. Hereditary military governors and protectorates remain "X Army" instead of turning into fiefs.

- **派系诉求全面调整**：每个诉求都有阶层利弊，相反议题补齐；派系清单重分（旧存档自动迁移）。新增推行三省六部、设立内阁、册立储君、整顿吏治、赈济灾荒。施行时长按轻重分 24/48/72 个月；准奏时从国库支付经费，不足先留中、再不足作罢，未推进的退款；岁币真正付给对方；每个诉求完成时写入史书与朝局纪事。原先只有名字的诉求落地：移民国策（开放迁入或驱逐异族平民）、金融国策（公营贸易收入增减）、输出革命、扶持革命党、划地给教廷。
  **Faction claims overhaul**: Every claim now has class winners and losers, and opposing-issue pairs are complete; faction agendas were reshuffled (old saves migrate automatically). New claims: adopt the Three Departments and Six Ministries, establish the Grand Secretariat, name an heir, clean up officialdom and famine relief. Implementation takes 24, 48 or 72 months depending on weight; approved claims are paid from the treasury, shelved if funds run short and dropped if they still cannot be paid, with refunds for claims that never progressed; tribute is actually paid to the other side; every completed claim is recorded in the history and the court chronicle. Claims that existed only in name now work: migration policy (open immigration or expelling minorities), financial policy (state trade income up or down), exporting revolution, backing revolutionary parties and ceding land to the papacy.

- **古典共和元老院**：施行「元老院」的古典共和有真正的元老名单：各城邦按人口出 1~4 席，城邦之主居首席、上届元老留任、贵族按威望补缺；执政官出缺时元老按派系投票。行政页画元老院席位图与推举次序；未施行元老院时画城邦同盟与接任次序。
  **Classical republic senate**: Classical republics with the Senate institution keep a real roll of senators: each city-state holds one to four seats by population, its ruler takes the first seat, sitting senators stay on and nobles fill vacancies by renown; when a consul is needed, senators vote by faction. The administration page draws the senate seats and the order of candidates; without a senate it shows the city-state league and the succession order.

- **立法机构按体制分版式**：代表大会制、议会内阁制共和国、总统制、君主立宪四种画法，机构名取本国实际机构；下接各省卡（地方选举、代表团或选区议席），点省份打开该省政体。
  **Legislatures by system**: Representative congresses, parliamentary republics, presidential systems and constitutional monarchies each have their own layout, named after the realm's actual body, followed by a provinces card (local elections, delegations or constituency seats); clicking a province opens its government.

- **卷册宽窗口**：帝国窗口改为横屏应用式布局：左栏国家档案固定不随滚动（国号纪年、元首与要人、国势数据），右侧按卡片与两栏、三栏排版；主页派系改为占比扇形图加派系卡（悬停看详情、点击打开），“增设派系”改为空席格；财政页新增近 12 个月收支图（悬停看当月明细）。法理、帝国核心、历史、制度、派系详情、政体等窗口同样改为宽窗口；卡片框重画为帝国风格（青铜边、金回纹角、朱红印点）；原版返回按钮移到左上角。修复左栏输入框不显示文字、右对齐文字尾字被裁、宽窗口中间的红色竖线与半透明遮罩。
  **Scroll-style wide windows**: The empire window uses a landscape app-style layout: a fixed left rail with the realm's profile (name and era, ruler and key figures, state indicators) and cards in two or three columns on the right. Factions on the home page become a share pie chart plus faction cards (hover for details, click to open), and "Add Faction" is now an empty seat. The finance page adds a 12-month income and spending chart (hover a month for details). The de jure, empire core, history, institution, faction detail and government windows use the same wide layout; card frames are redrawn in an imperial style (bronze edges, gold meander corners, a cinnabar seal dot); the vanilla back button sits at the top left. Fixed rail inputs not showing text, the last characters of right-aligned text being cut off, and a red vertical line and dark overlay in the middle of wide windows.

- **能力栏、地图与行政区**：能力栏拆成「帝国」与「文化与规划」两个标签页；帝国图层展开时京畿写首都城名并带都城星标；海上商路只在城市图层画。行政区划界改按王国法理，不再随意划给边界最长的邻居（每年每帝国最多两座）。
  **Power bar, map and administrative regions**: The power bar is split into "Empire" and "Culture and Planning" tabs. When the empire layer is expanded, the capital region shows the capital's name with the capital star; sea trade routes are drawn only on the city layer. Administrative boundaries now follow kingdom de jure titles instead of handing cities to whichever neighbor has the longest border (at most two cities per empire per year).

- **WarBox 兼容**：帝国中央与没有叛乱的行政区共用一个帝国总参谋部；修复战略矿产接入从未生效（火药厂、坦克、飞机、核打击的资源消耗）；补齐七个外交性格特质图标。
  **WarBox compatibility**: The imperial center and loyal administrative regions share one imperial general staff; strategic resource hooks (gunpowder factories, tanks, planes, nuclear strikes), which never took effect, now work; icons were added for seven diplomatic personality traits.

- **虚拟人口实体控制**：修正旧存档转入无小人模式后实体小人不断繁殖（文明种族只有名人以实体生育，积压多时加快并入）；修正开战、称帝、分封时族人成批落成为实体（以前会把不合格的孩子一并落成）。日志新增「实体构成」，按留存原因与生成来源分类。
  **Abstract population entity control**: Fixed units multiplying on the map after an old save switches to No Commoners Mode (only notables of civilized races are born as units, and backlogs are merged faster), and fixed relatives being created as units in batches during wars, coronations and enfeoffments (ineligible children used to be created too). A new "entity breakdown" log line groups map units by why they remain and where they came from.

- **大地图帧数**：野外建筑清单最多每 2 秒重建一次（原版一棵树生灭就整图重扫）；地图图层每秒最多刷新 20 次，小地图每秒 5 次；建筑超过十万的大地图上树木植物每秒最多更新 30 次（生长速度不变）；帧耗时日志细分到各类精灵与清单重建。
  **Large-map frame rate**: The wild building list is rebuilt at most every 2 seconds (vanilla rescanned the whole map whenever a tree grew or died); map layers refresh at most 20 times a second and the minimap 5 times; on maps with over 100,000 buildings, trees and plants update at most 30 times a second (growth speed unchanged); the frame-time log breaks costs down by sprite type and list rebuild.

- **修复**：读档、新建世界时报错（ArgumentException）；读取用户派系配置出错导致整份配置作废；制度树分层警告；无小人模式月度并入失败的警告补上位置信息。
  **Fixes**: An error when loading saves or creating worlds (ArgumentException); a faction config load error that discarded the whole file; an institution tree level warning; monthly merge warnings in No Commoners Mode now include where they happened.

验证：各项改动均编译通过；军团与战线、兵牌交互、春秋战国、帧数优化尚需在实际存档中继续验证。
Verification: every change builds; corps and fronts, counter interaction, the Spring and Autumn and Warring States simulation and the frame-rate changes still need testing on real saves.


2026-10-11 宽窗口与君主世系修整：

- **宽窗口内容布局**：帝国顶部、财政、宪制、属国、派系与阶层面板采用宽排版；财政标签与金额分栏、长文本换行。法理与核心名录卡片加宽，官署卡片改四列，官职图拓宽；保留现有窗口分类、头像尺寸和像素框架。
- **君主世系加载**：世系与详情每页展示 12 位君主，分帧创建；记事每页 30 条，完整历史仍可翻页查看。历史分组和去重减少重复扫描，头像身份只遍历一次所需人口库，翻页不重复全库查找；切换、关闭时取消过期界面绘制。
- **滚动与卡片资源**：宽滚动区使用矩形裁切，稳定布局不再每帧计算全部内容高度；稳定开窗布局时保留滚动位置，卡片复用边框切片资源。
- **特质图标兼容**：补充原版资源路径大小写、提前缓存缺图和图集同名图标恢复。保留已正确加载的图标；仍无同名资源的路径只记录一次诊断，不替换成其他特质。

验证：编译与资源加载、键值行布局回归检查；实际存档的开窗耗时与视觉效果仍待游戏内验证。


2026-10-11 整体更新计划续作：

- **资产随人、随债务人走**：虚拟人口迁移(自然迁徙、焦土逃难、国家移民与安置)按迁出比例带走民间存款，土地、厂房和国库留在原城。城市易主时旧政权的治理、驻军和军费欠款交还旧政权首都继续偿还，旧政权已亡记为违约；城市被毁时国库余额交回本国，欠款交给首都，民间存款随逃难百姓归入首都。
- **炼钢与核打击记账**：钢铁厂原料价值从工业税基扣除，工业税不再按毛产出征收。核打击立项成功后才扣铀，失败不再出现“铀退回、钱没退”。
- **战时中央代管与战后分配**：帝国成员在中央也参战的对外战争中打下的城先交中央暂管，记录打下它的成员和所在战争；该战结束后逐城分配：旧都归中央，法理属于某成员的归该成员，否则交给与它边界最长、治理正常的成员，没有成员接壤则由中央直辖。中央正统低于40时，与之接壤的攻城成员保留战果。代管中的城不参与分封和改划；封国与独立盟国的战果、帝国内战和叛乱不走代管。取代原先“正统高时按概率划归中央”。
- **羁縻制度**：华夏制度树新增“羁縻制度”(朝贡体系之后)。施行前异文化地区按普通道、军府管理；施行后异文化、异族地区自动设都护府、置羁縻州，并可用新神力“羁縻规划”逐区指定自动、羁縻或直辖。羁縻地区由本地首领治理，保留习俗、不参与法理文化转化，只向中央缴一成税，但中央与其接壤之敌开战时必须出兵。
- **附庸颜色与层级**：外部附庸和朝贡国保留自己的底色与旗帜，只把边框换成宗主颜色；独立后恢复。旧档里整个染成宗主色的附庸自动换回原色。附庸最多三层，朝贡国自愿改投他国须满二十年(战争迫使不受限)。
- **国内行政区飞地修正**：受托管理的行政区有城市与本区首府不相连、却被帝国其它成员包围时，每年和平时改隶边界最长的相邻成员；不丢国家领土，首都、王畿、代管城不动。海外飞地仍按原有维护与放弃规则。
- **社会危机分层**：阶层怨气除制度外也受重税、饥荒、无地、贫困推高，轻税和善治可以纾解。新增“组织动员”：怨气过半后逐年积累，正统越低越快；驻军镇压只压动员，不清除怨气。怨气达到起义线且动员达到六成才起事，不再掷正统骰子；无小人模式没有该阶层实体人物时，从该阶层人数最多、能起事的城市推举首领。制度页显示动员、镇压与“为什么还没起事”。现代国家的阶层怨气也推高街头抗争。
- **正统统合**：正统来源新增地方安定/失序(各城平均稳定度)、社会危机(阶层怨气、阶层起义)和欠饷欠俸，读取年度快照，不在读取正统时扫描城市。
- **城市扩张不跨山越水**：大半是山地、水面的区块不再被城市领取。
- **城镇道路**：原版只连20格内最近建筑，无小人模式城市建筑稀疏常规划不出路；规划为空时改为从建筑门口修到市政厅(最长80格)，已连上路网即停止。
- **民间投资、官营产业与财政回流(无小人模式，参考维多利亚3)**：俸禄、军饷(含补发欠饷)和工程款不再凭空消失，而是本城官员、士兵、工匠的收入，按居民税率交工资所得税后进入本城民间资产。民间资产超过一年消费储备的部分逐年转入城市“民间投资池”，优先支付民居升级、民营产业的建造升级和清理废墟，有余钱再出资加快施工；国家没钱民间也能建设。新开工的矿场、伐木场、工坊、牧场、屠宰场由谁出资归谁：投资池出钱或百姓自建为民营，国库出钱为官营；官营产业按占比从工业产值中拿出利润，按税收分成上缴(财政明细“官营产业利润”)，这部分不再进民间、不再征工业税。重商、农商并重时民间投资优先；工业化、重农抑商或施行盐铁专卖时官办优先。民居升级的建材改为百姓私用，不再由国库向百姓买自家房子的材料。城市窗口显示投资池、年投资、官营产业数与利润、俸饷工钱回流。城市被毁时投资池随民间资产归入首都。
- **普通人口保管款接入**：人物并入虚拟人口时收进城市保管的实体钱包现金与战利品，迁民按人数比例带走自己那份，城市被毁时归入首都，从背景人口生成平民时按人均一份放回钱包(不计收入、不超原版上限)。
- **封国国君爵位与谥号**：世袭封国国君的爵位以封国等级为准，与官称一致(周制公侯伯子)；单字国名补“国”(湘国侯)。封国国君去世后按一生定单字谥，仿春秋诸侯“国名+谥+爵”(湘文侯、梁孝王)，本国重谥加“后”(湘后文侯)；人物档案显示谥号并记入个人史。
- **原版王国铭牌闪烁**：铭牌对象池化复用，原版只在模式变化时设底板，王国铭牌会沿用上一帧别的铭牌的底板而在有无边框之间来回闪；现每次按当前模式明确设定。
- **国库预算统筹**：同一国库给多座城付治理、驻军、野战军费不再先到先得。每个付款国家按上月各城的实际缺口定本月预算：首都、与交战敌国接壤的前线、稳定度低于叛乱线的城足额，一般地方按剩余比例拨付，付不上照旧记欠饷；国家窗口显示“地方经费拨付”比例。旧欠款偿还仍只用安全储备以上的钱。
- **民间现金与估值分开(无小人模式)**：真钱记在城市普通人口现金账户，俸饷工钱(税后)、公家买料的货款进现金；民间资产仍是估值(含存货)，不兑换成钱。产值税先用民间现金交(每月最多现金盈余的一半)，不够才按估值折算，国库付出的钱能以税款回流。民间投资只投现金，投资池以近一年实投额两倍为限，用不掉的钱留在现金里。城市窗口显示“民间现金”。
- **国债(无小人模式)**：国库低于安全储备或正对地方限额拨款时，向本国各城百姓的现金与投资池借钱(只借每户底线以上部分的一半，总额不超过年经常收入两倍)；需掌握数学，年息 4%~12%(未掌握银行、正统低、有欠饷更贵)。每月付息给债权城的民间现金，付不起滚入本金并记违约；宽裕时先还最早的一笔。财政明细分列举债所得、国债利息、偿还本金(借还本金不算经常收支)；债务超过年经常收入或一年内违约会损正统；国家窗口显示国债余额与平均年息。
- **小城误判饥荒**：每月要吃不到一份粮的小城，本期没有整份可扣时会把吃饱记录清零，再按“存粮”(粮食入库即被吃掉，常年为 0)判成饥荒，死亡率 9.5%、出生 0，只会越来越小。现在本期无整份可扣时沿用上次记录；吃饱率按月平滑，避免整份颗粒造成“这月 100%、下月 50%”的假饥荒；吃饱率未知时有粮食产出就不算饥荒。
- **城镇修路(再修)**：实测原版连路规划几百次仍为 0 格。改为自己规划：从建筑门口修到 40 格内最近、尚未连上同一片路网的建筑(最长 60 格，跳过水面、阻挡格和建筑占地)；人口诊断行分列“无目标/寻路失败/过长/全过滤”，便于继续定位。
- **帝国窗口顶部信息重复叠加**：顶部信息栏任一项绘制出错时，残缺的信息栏未登记就留在窗口里，每次刷新多叠一块。现在建好即登记、绘制出错只记日志；年号读取在皇帝姓名数据缺失时不再空引用。
- **小城建不起风车**：无小人模式下城市人口按户计(一户约百人)，原版风车要求人口≥6，六百人以下的小城建不起风车、没有农田，只靠每户半份自给口粮长期饥荒、长不大。现在判断风车条件时城里有人即达到人口门槛，建筑数量等其它条件照旧。
- **存档瘦身与耗时**：王族不再随每个王国把整个宗族(含全部族人与生平)再存一遍，只存宗族 ID、运行时取族谱里的那一个(旧档自动转换)；族谱生平省略仍为初始值的字段。实测存档约小 26%，自动存档的停顿相应缩短；日志新增“[存档] 收集/序列化/写盘”耗时。
- **跨洋殖民**：国家建城的海路航程原先最多 192 格，大地图上的新大陆永远到不了。现在工业化后航程 384 格起(港口每级 +64)、船速三倍，电力或机动化后不限远近、船速六倍；近代以前仍按近海 64 格、远洋航海 192 格。
- **无小人模式默认开启**：新世界默认启用无小人模式(世界法则“无小人模式”)。已经存过此法则开关的存档仍按存档里的设置读取；早于此法则的老存档读档后会启用。
- **年度财政诊断与事件统计**：日志每年新增“[财政诊断]”(各国国库合计、月度赤字/负债/违约/限额拨款国家数、欠饷城市与总额、最大几国明细)和“[事件统计]”(战后分配、国家举债、国债违约、阶层起义、官办建城、羁縻出兵、行政区划界、官营开办、失都改称的年度次数)，验收和排查不再需要逐项截图。
- **预算统筹按“余额+预计月收入”拨付**：原先只按月初余额定一般地方的拨付比例，税收月中才到账，地方被拨得过少；国债、预算与分行业税的数学抽成纯规则并建立 53 项离线回归(Tasks/FiscalPhase2.Tests.ps1)，顺带修掉两处回归抓出的真问题：需求累计遇负数账单会回绕成天文数字、大额国债的 float 利率在计息时被精度磨掉利息。
- **法理与帝国核心窗口改宽窗口**：与官僚管理界面同款宽窗口(480×380)；法理窗口的颜色盘、法理网格、城市网格和帝国核心的头衔网格按宽度放宽列数，帝国核心的改名输入框移到标题栏上方。
- **官僚管理界面改宽窗口**：换用既有的宽窗口组件(480×380)，官职金字塔、行政区、后宫、虚封爵位不再挤在窄窗里；顶部信息框随正文滚动，页签照旧。
- **财富图层**：新图层按城市富庶度(经济繁荣+城市国库)由红到绿着色，两种模式(贫富热度连片/逐城描边)；悬停显示富庶度档位、城市国库，无小人模式再列民间资产、民间现金与投资池。
- **附庸神力**：新增“强制附庸”(先点宗主再点目标，无视外交条件；再点宗主自己取消)与“解除附庸”(点附庸脱离宗主，点宗主解散全部直属附庸)。两个神力选中期间画出现有附庸关系的指示线，强制附庸选定宗主后再画一条宗主首都到光标的线。
- **还于旧都**：帝国核心记录建国首都；持有该正统的帝国把旧都纳入势力范围(本帝国任一成员持有)后，中央自动把旧都划归直辖并迁都回去，旧都不必在核心法理范围内。战时代管、被占领、持有者只剩一城或正与中央交战时不迁。
- **军阀铭牌残名**：军阀加入政府后类别已是省、铭牌仍显示“某系军阀”——战争中禁止改名的保护把行政区也挡住了，现在帝国内行政区改名不受战争限制。
- **简化铭牌忽略无主小缺口**：国土中间没有归属的空地不再把领土切成两块——连通判断可跨过最多两个无主区块，国名按整片国土排布；第三方国家(含中立部落)的领土照旧隔断。文字可跨过的无主缺口上限也相应放宽。
- **战争赔款(和约)**：有明确胜负的国战结束时，败方即时赔付国库现款的四分之一，另按月经常收入的两成分 36 个月支付；付不起顺延，收款方灭亡或双方再度开战即撕毁和约。财政明细单列“战争赔款”，国家窗口显示每月赔款与剩余月数。劫掠、迫使朝贡等有自己结算的战争和叛乱、独立类内战不赔款。
- **海上商路(无小人模式)**：建成港口的城市与最近的外国港口城结成商路(每年重新配对)，每月按双方富庶度产生贸易利得并按商业税率就地征关税(从民间现金扣、入城市国库)；两国交战即断航禁运。
- **农田半径随城市扩大**：无小人模式的自动耕作范围从固定 9 格改为每 12 块领土 +1 格、最多 16 格，大城不再被风车半径卡死粮食上限。
- **自动存档提速**：流式序列化直接写盘(不再先攒几 MB 的 JSON 大字符串)，并去掉存档后的阻塞式完整 GC(每次固定添几百毫秒停顿)。
- **帝国窗口改宽窗口**：与法理、帝国核心、官僚界面同款(480×380)；顶部信息框随正文滚动，改名输入框移到标题栏上方。
- **零碎修缮**：修复 5 个书籍模板 CSV 因文案含逗号一直加载失败(受影响的书名模板此前全部缺失)；科技首扫(补齐旧档科技、兼容模组挂补丁)挪进读档流程，进游戏后第一个年度轮转不再突然卡两三秒；保城拦截计入年度事件统计，开关开着却未拦住时日志写明原因。
- **征服王朝制度文化**：采用当地制度的征服王朝，制度树改按制度文化推进，修复阿拉伯王朝治下的华夏帝国被改回哈里发的问题；吸收来的外线制度不再改变政体。

验证：编译通过，0错误。真实存档、游戏界面与帧率未实测。

2026-10-10 财政第一阶段：

- **国库余额防溢出**：城市和国家实际现金改用64位整数，保留旧存档字段。原有AI、建造和科研的一次报价继续使用整数接口，完整余额及可用资金单独读取；大额收入不再绕回负数，预留资金按真实余额扣除。
- **历史欠款单独记账**：治理、驻军及野战军欠款偿还分别列示，仍扣除实际现金，但不再推高日常维护费用、安全储备或经常性赤字。只重新分类今后的偿还，不猜测旧账用途。
- **完善财政汇总**：补齐帝国汇总的军费、必要支出、历史还款及现金净变动；匹配划拨抵消后保留净转入或净转出。财政页增加境内实收实付、现金净变动和历史欠款偿还，城市及国家提示显示完整国库金额。
- **沿用原有结构**：现金、账本、地方稳定和建城仍在现有系统中处理，两种人口模式共用。增加通用系统财政文件索引；完整企业工资、利润、投资与新税基尚未接管运行经济。

2026-10-10 补充修复：

- **姓名顺序遗漏入口**：读档后及虚拟族人恢复实体时，按当前人物文化校正已知姓与名的顺序；年度年龄更新也校正识别出的反序姓名。保留手动改名与西方王号，不重新生成姓或名。
- **真实年龄开关**：接入实际自然死亡入口，开启时智慧种族使用60至80岁的固定个人寿数，关闭时恢复原版自然死亡判断。虚拟族谱同步读取开关，保存原有寿命与不朽状态；尊重原版老死法则，不再把年龄风险累加到刑罚风险或添加死亡标记。旧虚拟族谱缺少寿命记录时按物种补读；普通匿名虚拟人口仍按原人口模型结算。

2026-10-09 科技兼容、姓氏与财政保护：

1. **科技兼容与来源提示**：分别识别 ModernMod、WarBox 和 ModernBox，逐项显示枪械、建筑与单位的提供模组。缺失的具体型号按来源提示，不再因为某个型号缺失就把整个模组显示为“未安装”。兼容唯一的大小写别名、重复引用和资源晚注册，科技详情允许换行。
2. **修复后代串姓**：家庭创建和加入家庭不再覆盖已经确定的个人姓，普通父系、入赘和母系沿用相应继承规则。虚拟族谱保存个人姓与名，生成后代和恢复实体时使用个人姓名；旧档缺字段保留回退，不批量改写已有错姓。
3. **保护财政结余**：当月治理、驻军和野战军费用仍优先支付；历史欠款只用超过安全储备和待建城市预留资金的余额偿还。实际付款才减少欠款，资金不足时继续记录欠额。正常小人模式和虚拟人口模式均适用。
4. **补齐新封国税率**：修复皇族分封和国家建城生成新封国时被自动设为免税的问题，统一使用低税率。已有国家和主动免税政策不批量修改。
5. **补全战时分封限制**：皇族分封执行入口也检查帝国是否仍在战争。战争期间的皇位继承分封延至和平后处理，待办随存档保存，恢复时重新核对现行继承制度，避免重复执行。
6. **动态保护迁都后的王畿**：按法理改划行政区时逐城核对当前首都及首都法理，防止分帧处理中迁都导致旧保护名单失效。保留开国法理，仍按既有帧预算执行。
7. **补充财政显示与性能统计**：城市、国家及帝国财政页显示安全储备、可用资金和包含央地划拨的账期现金净变动。预算查询复用必要支出统计，交易和账期过期时更新，减少重复汇总与临时分配。虚拟人口性能日志新增平均帧耗时、P95/P99及有界采样，继续记录最慢帧；Unity对象操作保留在主线程。
8. **修复文化姓名顺序不同步**：人物获得或切换文化后再读取姓名规则，避免首次赋予文化时误用西方回退。已有完整姓名也会更新排列顺序；姓名拼接时刷新文化规则，不继续沿用旧缓存。缺少城市、宗教及可选后缀配置不再阻止姓名顺序初始化，较短后缀表安全回退。文化编辑器纠正“用所在地名作姓”的误标，并明确人名选项未勾选时姓氏前置。历史日志文本不追溯改写。
9. **修复无依据补入西方文化（10月10日）**：未配置文化的物种不再默认归为西方；族谱创建及更新不再因暂时读不到人物文化而写入西方，已有个人文化记录优先保留。人物命名使用已知人物文化、城市文化或明确物种配置；文化演化继承来源文化的绑定，文化批量注入跳过未配置物种。保留主动配置的西方文化及已有少数文化人口，不批量清除旧档文化。
10. **城市后缀遵循政体配置（10月10日）**：名称读取当前政体 JSON 的城市规则，修复易主、改制后旧类型缓存继续影响后缀的问题。新增普通城市及首府后缀可选配置；华夏原始制与周制为县，律令普通城市为县、行政区首府为府。周制后缀译文同步为县。迁都同步更新新旧首府，保留核心地名；政体编辑器支持保存和导出后缀规则。
11. **开放党禁改为理念竞争驱动（10月10日）**：各类派系均可提出开放党禁。至少一种其他理念的支持人口超过国家主理念时形成改革压力，优势越大推进越快，非主导派系的这项改革不会再因内阁限制立即取消。首次开放仍走制度改革及科技前置；已有政党制度的国家可在持续压力下重新开放，玩家手定的宪法锁定仍保留。两种人口模式沿用同一统计口径，月度派系比较复用人口快照。
12. **修正收入统计与换装退款（10月10日）**：人物年度收入按钱包实际到账统计；换装预付款不再计为个人所得。钱包达到原版上限时退回未到账补贴，打造异常时结清未用预付款；收入计数改为64位统计，避免超过32位上限后清零。商人年度判断复用家庭收入汇总，个人上年收入保留本人金额，不再重复记录家庭总收入。新财政重构仍处于独立模型验证阶段。
13. **补齐其他文化的政体后缀规则（10月10日）**：共享原始制恢复部落，华夏县通过文化专属配置保留；游牧聚落改为帐，古典城邦与阿拉伯聚落改为城，西式封建保留原有堡、教区等规则，现代仍为市。文化专属覆盖和首府覆盖均从政体JSON读取，编辑器支持保存与导出；后缀随实际制度发展，不按全地图年份强行统一。
14. **修正土地交易结算（10月10日）**：卖地款不再计作职业收入；全额到账后才转移土地，钱包上限或付款异常时回滚本次交易。排除死亡、缺失卖家和自家买卖；少于整份的土地按实际份额计价。正常小人及虚拟人口中的实体家户共用修复。
15. **修复虚拟人口市场搬货（10月10日）**：不再把仓库新余额误当成交货量，付款及贸易记录按实际送达数量计算。未送达货物退回原所有者并保存，满仓或入库异常时不丢货；退回物资与新城启动物资分开，关闭虚拟人口模式后仍可使用。修正公有库存计数溢出及超出实物的问题。
16. **修正虚拟生产与消费结算（10月10日）**：旧生产税按实际入库的应税产出计值，满仓、无仓或入库异常造成的未收到产出不再产生应税收入。保存尚未入库的应税零头，与自给及救济物资区分；旧档已经计过产值的零头不会重复征税。粮食、皮革按实际出库计算消费与缺口，修复扣粮异常被误记为全部吃饱的问题，清除无整份皮革需求时残留的旧缺口。完整企业现金财政仍未启用。
17. **保管虚实转换的钱包（10月10日）**：并入虚拟人口前保留实体原有现金与战利品，两者独立记录，不记作新增收入。族谱人物重新落成时恢复钱包，超过原版上限的余额继续保留，正常模式的既有校准也可补回后来空出的额度。保管余额按族谱亲属顺序继承，无人继承时保留遗产并防止自动瘦身删除；宗族销户时转入普通人口保管账户。普通人口按城市汇总已有钱包，尚未接入消费、迁移和新财政。修正人物移除异常时重复生成或遗失人口的交接问题；旧档缺字段不补发历史现金。

18. **修复宣称与收回法理循环（10月10日）**：天子持有且未绑定地方主法理的封地不再被当作无主法理，地方不能靠占地自动拿回。收回须核对当前地方领袖的有效罪名、现行法律及实际所有权；已处理的罪名清除，核心国与天子本人仍受保护。修正指控决议把罪名记到指控者身上的问题，无罪不记作定罪；新罪名绑定当事领袖，支持枚举值为零的谋反罪。违例数值一次只加一次，并防止溢出。
19. **修正职业文官与部长身份（10月10日）**：责任政府的部长及部门主官属于政治任命，职业文官制不再把他们标成常任文官；任命、补缺和界面共用岗位判断。新增可选岗位属性供事务官与常任副职使用，此类岗位在混合制、职业文官制下不因三年任期自动轮换，分肥制仍可轮换。旧配置默认中央岗位为政治主官，政体编辑器保留该属性，宪法说明同步修正；当前未新增独立副职。

20. **禁止地方指控天子（10月10日）**：地方揭发罪名的自动、手动及执行入口均排除本帝国天子；地方推动的犯罪类诉求也不能以中央目标范围绕过限制。法律权限、旧罪名查询及直接处罚入口复核管辖权，阻止地方以同属一个帝国为由给天子附罪或施刑。地方官员之间的合法指控和既有中央程序保留。

验证：编译通过；两种人口模式各完成和平、战争、资金不足三类24个月离线财政回放。科技、姓氏、财政账本、军费、分封、行政区、附庸、保城、存档与性能队列的相关回归通过。

待验证：428年实际存档的财政平衡、真实游戏的UI布局、大地图实测帧率与月度停顿。离线预算查询基准不是整场游戏的提帧结果。

---

2026-10-09 · 0.4.4Beta11

1. **完善财政收入**：加入土地税、工业税和商业税，分别对应农业及土地交易、工业生产、商业利润；同一笔收入不重复征税。默认基础税率为10%时，三类税率分别为10%、12.5%、15%；零税率仍然免税。正常小人模式按人物钱包征收，虚拟人口模式按背景人口产出及交易结算，保留人物独立缴税。
2. **修正虚拟人口军费口径**：驻军及野战军费用与生产、税收使用相同的经济单位，传统时期按100人、现代时期按1000人折算。实际驻军人数和镇压能力不因此减少；正常小人模式仍按实际人数计费。旧档被高估的虚拟人口军费欠款会一次性折算。
3. **补充国库收支明细**：分别显示各类税收、央地分成、治理、驻军、野战军、科研、建设及腐败等来源。中央收到的土地税、工业税、商业税分成可以单独查看，帝国汇总不会重复计算内部转账。账本滚动记录最近12个月，不追补不存在的历史账目。
4. **修复新封国免税问题**：自动新建封国和分封制征服后的归附国采用低税率，避免居民税因创建流程被意外关闭；原有主动设置的免税政策保留。
5. **调整战时分封**：战争期间暂缓自动划分新封国及行政区，和平后再按法理处理；征服后的皇子改封也延后处理。待办随存档保存，保护王畿、开国法理及现首都法理。
6. **新增按法理整合决议**：皇帝在和平时期、正统高于80时，可整合或拆分本帝国治下行政区及封国。同一法理优先归并，跨法理地区另行划分；保留核心首都及王畿，不夺取外部国家的法理所有权。改划逐城执行，战争或正统不足时暂停，条件恢复后继续。
7. **加强行政区主权限制**：修正征服入口绕过附庸权限检查的问题，受托行政区不能直接成为外来政府的附庸；旧档仍有行政授权记录的地方政府也会接受检查。
8. **修正法理决议判定**：强制发起“获取法理”时先检查资格，避免无效决议开始后立刻取消；帝国核心取得法理不再依赖地方外交权限。已开始的法理决议不会仅因随后开战而取消，失去统治身份或法理资格时仍会停止。
9. **修复皇帝收回自己法理的问题**：收回法理的候选人与执行入口均排除核心王国和皇帝本人，避免旧犯罪记录触发自我处罚。
10. **补齐本地化与图标兼容**：补充财政、制度立场、法理页签、现代国家及特质译文，修正页签重复翻译和部分文化词库格式；兼容原版统计行与模组图标路径的大小写差异。已有图标经静态审计未发现缺失或全透明图片。
11. **控制新增计算开销**：行政区改划最多每帧处理一个城市；虚拟人口财政预算沿用有界工作队列和数值快照多核计算，扣款、易主与Unity操作留在主线程。实际帧数和财政平衡需结合具体存档复测。
12. **修复居民死亡后城市消失**：开启城市保护时，居民死光也保留至少一个区域的空城，等待移民。虚拟人口仍在时不因实体小人死亡而失去城市归属；城主补位失败也不会触发弃城。城市删除与法理、帝国归属清理共用保护检查，正常小人模式同样支持保留空城；换地图时仍正常清理旧世界。
