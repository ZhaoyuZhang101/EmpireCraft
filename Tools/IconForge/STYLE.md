# EmpireCraft 图标风格规范

> **现行规范(2026-10-05 定稿)：帝国盒子以后所有新图标一律使用"冷峻写实 · 像素输出"风格，由 `imperial_forge.py` 生成。**
> 下文较早的"像素风 / 庄重风格"章节只作历史记录，不再用于新图标。

## 现行规范：冷峻写实 · 像素输出

**流程**：在 256×256 画布上写实绘制(距离场浮雕、渐变、金属高光、材质颗粒) → 冷峻调色 → 缩到 **28×28** → 透明度二值化 → 限 20 色 → **1px 纯黑描边** + 右下投影(黑色 alpha 80)。成品与原版像素界面对齐，最近邻显示。

**配色**：只用黑、骨白、暗血红、枪灰、氧化旧铜(另有旧纸、黑胡桃木、原野灰绿作辅助)。降饱和 25%、对比 1.3。不用亮色、不用卡通形状。只借这种冷峻的配色与氛围，**绝不使用任何真实的法西斯或国家符号**。

**色带与旗帜**：不得排出像任何真实国旗的配色组合(例如绿白红)。

**形制要考据**：华夏器物按史实画，例如官帽为宋代直脚幞头(前低后高、展脚极长)，冕为明定陵衮冕(前低后高的冕板、十二旒五色珠、玉笄、朱缨)。拿不准时先找参考图。

**按用途选形式**：

| 用途 | 形式 | `imperial_forge.py` 里的表 | 输出位置 |
| --- | --- | --- | --- |
| 帝国窗口页签 | 独立器物(不套徽章) | `TABS` | `GameResources/Tab*.png` |
| 谋划 | 器物 + 右下角标 | `PLOTS` | `ui/icons/plots/plot_<id>` |
| godpower 按钮 | 器物 + 右下角标 | `GODPOWERS` | `ui/icons/godpowers/<id>` |
| 身份特质 | 小徽章(旧铜环、血红底、器物) | `TRAITS` | `ui/icons/actor_traits/` |
| 文化 | 纹章盾(旧铜边、底色、浮雕纹章) | `CULTURES` | `ui/icons/cultures/` |
| 法理 / 行政区 / 帝国核心工具 | 器物 + 角标(法理图卷、朱红官印、玉玺) | `TOOLS` | 覆盖原 `ui/icons/iconTool*.png` |
| 民族情绪图层 | 由冷到热的色带旗(黑铁 → 枪灰 → 旧铜 → 暗红 → 血红) | `TOOLS` | `ui/icons/iconNationLayer.png` |
| 理念徽章 | 保留原设计，在原图上调色(降饱和、冷色再去色、金色转旧铜、纯黑描边) | `regrade_badge` | 母版在 `Tools/IconForge/source/` |
| 世界提示 | 直接复用上面的图标，`path_icon` 指过去 | —— | —— |

**角标含义**：骨白 = 增加 / 转移 / 允许 / 互换；血红 = 取消 / 销毁 / 揭发 / 停止；旧铜 = 提升 / 新立；枪灰双剑 = 战争；红印 = 涉及法理；小冕 = 帝国。

**新增图标的步骤**：
1. 能复用现有器物和角标的，直接在对应的表里加一行；没有合适的器物，就照现有 `obj_*` 函数的写法补一个(按部件分材质 `shade()`)。
2. 运行 `python Tools/IconForge/imperial_forge.py --preview`，看 `imperial_preview.png`(4 倍最近邻放大)，确认小尺寸下认得出。
3. 代码里用 `SpriteTextureLoader.getSprite("<相对 GameResources 的路径，不带扩展名>")` 引用。

**稳定提示词**(需要描述或外包图标时用)：

> 帝国盒子图标，28×28 像素图，与 WorldBox 原版像素界面一致。先写实绘制再像素化：金属浮雕、渐变光影、材质颗粒，配色只用黑、骨白、暗血红、枪灰、氧化旧铜，低饱和、高对比，1px 纯黑描边与右下投影。EU4 修正图标式的器物(谋划、按钮可在右下加角标)或 HOI4 式小徽章。华夏器物须合史实形制。不要亮色与卡通造型。主体：____

---

# 历史记录(旧规范)


凡是会出现在 godpower 栏、窗口页签、图层按钮的图标，都按这套规范画，保证和原版 WorldBox 以及模组现有的 godpower 图标(王冠、纹章、"印"字等)放在一起不突兀。

能用脚本就用脚本：在 [icon_forge.py](icon_forge.py) 里加一个 `@icon(...)` 函数，只画平涂形状，受光、描边、投影交给流水线统一处理。

```bash
python Tools/IconForge/icon_forge.py --preview
```

## 硬性规则

| 项目 | 规定 |
| --- | --- |
| 画布 | 28×28 透明 PNG；主体约占 22–24px，四周留出描边和投影的位置 |
| 描边 | 主体外一圈 1px 深褐 `#2a1410`，不用纯黑 |
| 投影 | 描边整体向右下平移 1px，颜色为黑色、alpha 70 |
| 光源 | 固定在左上：区域的上、左边缘用亮色，下、右边缘用暗色 |
| 色阶 | 每种材质 3–4 阶(高光 / 本色 / 暗部 / 深色)，见下表；不做渐变、不做抗锯齿、不加半透明内部像素 |
| 色彩 | 暖色为主：金、朱红、米黄纸色；蓝、玉绿只作点缀或用于区分 |
| 造型 | 一个可识别的主体，最多一个附加角标(加号、减号、箭头圆章)；轮廓要能在 22px 下一眼认出 |
| 题材 | 优先用东方、帝制意象(官署、典籍、竹简、玉玺、挂轴)；西方或现代题材按需要换材质，但规则不变 |
| 禁止 | 纯平涂无明暗、单色剪影(白色剪影只用于主标签页 `TabEmpire`)、照片感、渐变、文字(数字角标除外) |

## 材质色板

| 材质 | 高光 | 本色 | 暗部 | 深色 |
| --- | --- | --- | --- | --- |
| 金 gold | `#fff2ae` | `#f2bd3c` | `#c98d22` | `#9c6a14` |
| 朱红 red | `#ff7466` | `#d95050` | `#a0242e` | `#5c0e18` |
| 纸 paper | `#fffbe6` | `#ffe9c4` | `#e5cb83` | `#b89a5e` |
| 木 wood | `#d9a066` | `#a8693c` | `#7a4526` | `#4a2716` |
| 玉绿 jade | `#9be08a` | `#47af4d` | `#207729` | `#145a1c` |
| 青蓝 blue | `#b8e0ff` | `#4a86d0` | `#1b4486` | `#0f2a5a` |
| 石/铁 steel | `#ffffff` | `#dfd8cb` | `#b7ad9c` | `#7a7266` |
| 竹 bamboo | `#fff2ae` | `#e5cb83` | `#b89a5e` | `#7a5a2e` |
| 墨 ink | `#5a4a44` | `#3a2c28` | `#26201c` | `#140c0a` |

角标沿用现有 godpower 图标的样式：圆形金边徽章，绿底加号表示添加，红底减号表示移除，蓝底箭头表示转移或编辑，红底叉号表示销毁。

## 稳定提示词

需要请 AI 生成或描述图标时，把下面这段原样贴上，再在最后补一句主体描述。

> WorldBox 风格 28×28 像素图标，透明背景。主体居中，约 22px。外轮廓为 1px 深褐色描边 #2a1410，描边向右下偏移 1px，形成半透明黑色投影。光源在左上，每种材质只用 3–4 个色阶，上、左边缘提亮，下、右边缘压暗。不用渐变，不做抗锯齿。配色以金 #f2bd3c、朱红 #d95050、米黄纸色 #ffe9c4 为主，蓝、玉绿作点缀。造型饱满、轮廓清晰，缩到 22px 也能一眼认出。最多带一个圆形金边角标。不出现文字。主体：____

English version:

> WorldBox-style 28×28 pixel-art icon, transparent background. One centered subject about 22px wide. 1px dark-brown outline (#2a1410) with a 1px black drop shadow (alpha 70) offset to the bottom-right. Light from top-left; each material uses only 3–4 flat tones (highlight on top/left edges, shade on bottom/right). No gradients, no anti-aliasing. Warm palette led by gold #f2bd3c, vermilion #d95050 and parchment #ffe9c4, with blue and jade as accents. Chunky, readable silhouette at 22px. At most one round gold-rimmed corner badge. No text. Subject: ____

## 现有图标

| 文件 | 主体 |
| --- | --- |
| `GameResources/TabBureau.png` | 官署：金瓦飞檐、红柱、蓝匾、石台 |
| `GameResources/TabConstitution.png` | 宪法：红皮金饰典籍、金印 |
| `GameResources/TabDynasty.png` | 先帝录：挂轴帝王像 |
| `GameResources/TabSetting.png` | 设置：金齿轮、红宝石 |
| `GameResources/TabInstitutions.png` | 制度：竹简、红绳编连 |
| `GameResources/ui/icons/iconNationLayer.png` | 民族情绪图层：五色热度旗 |
| `GameResources/TabColor.png` | 颜色与旗帜：木调色板、朱漆毛笔 |
| `GameResources/ui/icons/actor_traits/iconFounderRuler.png` | 开国雄主：金冠压剑 |
| `GameResources/ui/icons/actor_traits/iconRestorerRuler.png` | 中兴之主：旭日出云 |
| `GameResources/ui/icons/cultures/<文化>.png` | 文化图标(21 个文化 + `Default` 素白旗)；文化文件夹里放 `icon.png` 可覆盖 |
| `GameResources/ui/icons/plots/plot_<谋划id>.png` | 模组谋划(42 个)：主体 + 右下角圆形角标，见下 |

## 谋划图标

主体讲"对什么"，角标讲"做什么"，组合方式与 godpower 的加减号徽章一致。新增谋划时在 `icon_forge.py` 的 `PLOT_ICONS` 里加一行 `"谋划id": (主体, 角标)`，再把谋划的 `path_icon` 写成 `"ui/icons/plots/plot_<谋划id>"`。

| 主体 | 含义 | 主体 | 含义 |
| --- | --- | --- | --- |
| `m_crown` 冕旒 | 帝位、称帝、继承 | `m_seal` 玉玺 | 天命、正统 |
| `m_title` 法理印 | 法理 | `m_scroll` 诏书 | 政体诏令 |
| `m_bamboo` 竹简 | 制度 | `m_chain` 锁链 / 断链 | 附庸 / 独立 |
| `m_palace` 宫殿 | 都城 | `m_brush` 笔砚 | 文化 |
| `m_minister` 乌纱帽 | 权臣 | `m_axe` 斧钺 | 九锡 |
| `m_phoenix` 凤冠 | 太后 | `m_tablet` 牌位 | 追封 |
| `m_people` 人群 | 派系 | `m_temple` 神庙 | 宗教 |
| `m_scales` 天平 | 揭发、断案 | `m_calendar` 历书 | 年号 |
| `m_shield` 盾 / `m_sword` 剑 | 军队 | `m_envoy` 节杖 | 外交 |
| `m_city` 城池 | 城市 | `m_tribute` 贡箱 | 朝贡 |
| `m_banners` 双旗 | 合并国家 | `m_whiteflag` 白旗 | 停战 |

| 角标 | 底色 | 含义 |
| --- | --- | --- |
| `plus` 加号 | 绿 | 获取、加入、吞并 |
| `check` 对勾 | 绿 | 允许、履行 |
| `minus` 减号 | 红 | 停止 |
| `cross` 叉号 | 红 | 销毁 |
| `alert` 感叹号 | 红 | 揭发、诉求 |
| `seal` 印章 | 红 | 涉及法理 |
| `war` 双剑 | 深色 | 战争 |
| `arrow` 箭头 | 蓝 | 转移、加入、施加影响 |
| `back` 回转箭头 | 蓝 | 收回、恢复、还都 |
| `swap` 互换 | 蓝 | 更替、转化 |
| `star` 星 | 蓝 | 新立、追封 |
| `up` 向上 | 金 | 提升、称帝、请封 |

## 庄重风格(默认)

玩家反馈初版"太卡通"。帝国窗口页签、谋划图标等代表朝廷权威的图标一律用庄重风格：

- **色板**：暗金 `agold`、深朱 `crimson`、黑漆 `lacquer`、古铜 `bronze`、苍玉 `djade`、靛青 `indigo`、旧绢 `silk`、旧竹 `oldbamboo`、旧石 `stone`。不用明黄、粉红、亮绿。
- **造型**：对称、端正，优先画礼器与法器(牌位、令牌、玉玺、笏板、斧钺、重檐殿宇)；不用齿轮、小人、圆滚滚的卡通形状。
- **明暗**：高光收敛，暗部压深；轮廓更硬。
- 脚本里给 `Canvas` 传 `remap=REGAL`(或在图标函数里设 `c.remap = REGAL`)，就能把原色板一键换成庄重色板，形状不用重画。

庄重风格的稳定提示词(在通用提示词后追加)：

> 庄重、权威，不要卡通感。配色压暗：暗金 #c9a046、深朱 #8e2420、黑漆 #2e2220、古铜 #8a5f33，高光收敛、暗部更深。造型对称端正，画礼器法器(牌位、令牌、玉玺、笏板、重檐殿宇)，不要圆滚滚的卡通形状和小人。

### 写实加细

庄重风格的图标同时打开写实渲染(`c.detail = True`，谋划图标自动打开)：

- **体积明暗**：按每个像素到区域上左、下右边缘的距离分层，交界处用棋盘抖动过渡；
- **投影遮挡**：正上方压着别的材质时(屋檐下、兽钮下)本行压暗；
- **金属高光**：金、铜、石、玉的受光面点出零星反光；
- **材质颗粒**：木、漆、竹、绢、朱面加细微纹理。

造型上要有结构细节：殿宇画瓦垄、斗拱彩画、门钉；器物画錾花、回纹、卷草边、须弥座线脚；不要只用一两块平涂色块拼出轮廓。

### 身份特质图标(庄重风格)

| 文件 | 主体 | 依据 |
| --- | --- | --- |
| `iconJuren.png` 举人 | 桂枝 | 乡试放榜正值桂花开，称桂榜 |
| `iconGongshi.png` 贡士 | 杏花枝 | 会试杏花时节放榜，称杏榜 |
| `iconJingshi.png` 进士 | 金榜 | 殿试黄纸金字，金榜题名 |
| `iconEmpireOfficer.png` 帝国官员 | 乌纱帽 | |
| `iconOfficerLeave.png` 告老还乡(神职人员共用) | 鸠杖 | 汉代赐老人鸠首杖 |
| `iconEmpireArmy.png` 帝国军人(革命者共用) | 铁兜鍪、红缨 | |
| `iconEmpireEliteArmy.png` 军府军人 | 鎏金兜鍪、护颈、翎羽 | |
| `iconFounderRuler.png` 开国雄主 | 冕旒冠压剑 | |
| `iconRestorerRuler.png` 中兴之主 | 旭日出云 | |

文化图标与上面两个君主特质都经 `_regal` 包装，统一套庄重色板与写实渲染。十三个理念徽章(`ideology_*.png`)本是正式纹章风格，保持原样。

## 冷峻写实风格(现行，2026-10-05 起)

玩家要的是 EU4 修正图标 / HOI4 国策徽章那种威严感，配色是"黑、骨白、暗血红、枪灰、氧化旧铜"。
帝国页签、谋划、身份特质、文化图标已全部改由 **`imperial_forge.py`** 生成(56×56，平滑绘制)，上面的像素风规范只留给仍是像素风的少数图标(颜色页签、民族情绪图层)。

```bash
python Tools/IconForge/imperial_forge.py --preview
```

| 类别 | 形式 | 定义位置 |
| --- | --- | --- |
| 帝国窗口页签 | 国徽式徽章：旧铜环 + 黑/血红底 + 浮雕主体 + 麦穗绶带 | `TABS` |
| 谋划 | EU4 式写实器物 + 右下角标(骨白=增加/转移/允许，血红=取消/揭发，旧铜=提升/新立，枪灰双剑=战争) | `PLOTS` |
| 身份特质 | 小徽章：旧铜环 + 血红底 + 器物 | `TRAITS` |
| 文化 | 纹章盾：旧铜边 + 底色 + 浮雕纹章 | `CULTURES` |

- 材质渲染在 `object_forge.py`(距离场浮雕、渐变、颗粒、冷峻调色 `grade`、粗黑描边)，徽章构件在 `emblem_forge.py`。
- 游戏里由 `Scripts/UI/Components/HiResIconFilter.cs` 把这些 ≥48px 的图标改成平滑过滤；新增目录要加进它的 `Folders`。
- `icon_forge.py` 不带参数运行时会跳过这些图标，不会覆盖。

稳定提示词(冷峻写实)：

> 威严的帝国图标，EU4 修正图标 / HOI4 国策徽章风格，56×56 平滑写实绘制。配色只用黑、骨白、暗血红、枪灰、氧化旧铜，低饱和、高对比、重阴影，粗黑外描边，右下投影。器物有金属浮雕与材质颗粒；徽章为旧铜环、黑或血红底、浮雕主体。不要亮色、不要卡通形状。主体：____

### 最终输出：像素化(2026-10-05 修订)

平滑的 56×56 图标放进原版像素界面太突兀(玩家反馈"格格不入")，现在 `imperial_forge.py` 一律经 `pixelize()` 输出 **28×28 像素图**：
256 画布写实绘制 → 冷峻调色(降饱和 25%、对比 1.3) → 缩到 28 → 透明度二值化 → 限 20 色 → 1px 深色描边 + 右下投影。
页签不再套圆形徽章，改为独立器物(牌位、宫门、令牌、法典、竹简)，与原版页签一致。`HiResIconFilter` 对 28px 图自动跳过，保持最近邻清晰显示。
