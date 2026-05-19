<h1 align="center">
  <img src="icon.png" alt="EmpireCraft Logo" width="150" />
  <br/>
  EmpireCraft
</h1>

# EmpireCraft 帝国盒子

---
## 项目简介

**EmpireCraft** 是一个面向 **WorldBox** 的大型政治与历史模拟模组。  
它围绕 **帝国、正统、法理、派系、官僚、地方治理、战争占领、历史叙事** 等主题，构建出一套比原版更复杂、更长期、更具政治张力的国家演化系统。

---
## 核心功能

### 1. 帝国系统

- 支持帝国建立、继承、瓦解、修复与重建
- 引入正统值、中央与地方关系、皇帝更替、帝国历史
- 支持帝国内部王国、附庸、岁币国、朝贡国等关系

### 2. 王国法理与帝国法理

- 王国可围绕主法理、首都法理、祖籍法理进行命名与合法性运作
- 帝国法理可包含多个王国法理
- 帝国法理会影响称帝资格、统一战争目标、历史归属和地图图层展示
- 支持帝国法理编辑窗口、历史合集、名称修改与调试按钮

### 3. 派系与宫廷政治

- 固定派系、派系领袖、派系成员与派系支持者
- 派系可推动诉求、积累影响力、制造叛乱
- 派系之间存在关系、竞争和吞并空间

### 4. 官僚与地方治理

- 中央与地方均可拥有官职体系
- 国家、城市、军队、后宫支持不同类别的官位配置
- 政体、文化、制度和官制可联动运行
- 配套提供独立的政体编辑器与本地化配置工具

### 5. 诉求系统

- 诉求可由中央或地方推动
- 推动会受到政治身份、影响力、支持者、派系和条件限制影响
- 已实现多类诉求：削藩、税制、宗教政策、官制调整、统一战争、撤换官员等

### 6. 法律、犯罪与腐败

- 官员犯罪、罪行曝光、处罚、撤换、政治反扑
- 腐败与犯罪概率、治理能力、地方稳定度直接联动
- 包含暴君值、暴虐值、官员执法与政治清洗等机制

### 7. 战争、前线与占领

- 支持原版占领与自定义区块占领模式
- 支持前线识别、战士推进、区块扩张、区块夺回
- 引入领主/国王/皇帝被俘事件与连锁战争后果

### 8. 历史与可视化

- 帝国历史、皇帝历史、帝国法理历史合集
- 地图图层、工具提示、铭牌、窗口浏览和调试按钮
- 强调“可追踪的政治变化”和“可阅读的历史叙事”

---
## 技术

### 系统设计

- 将政治、战争、历史、法律、UI、AI 扩展整合为统一框架
- 各模块通过扩展方法、管理器、补丁与辅助系统解耦

### 数据建模

- 对 `Kingdom`、`City`、`Actor`、`Empire`、`Title`、`Faction` 等对象进行了大量运行时扩展
- 管理复杂的历史、状态、归属、关系和地图表现

### 数据驱动与工具化

- 支持政体配置、官职定义、本地化文本、文化绑定
- 自带独立 `RegimeEditor` 用于配置生成与编辑

### UI / UX 扩展

- 自定义窗口、图层、铭牌、提示框、按钮和调试工具
- 为复杂机制设计了可读的浏览界面，而不是只停留在底层逻辑

### AI 与规则系统

- 扩展王国 AI、帝国 AI、战士前线行为、占领判定、诉求推进逻辑
- 让复杂制度不仅能“存在”，还能“运行”

---
## 仓库结构

- [Scripts](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/Scripts)
  核心代码，包含 AI、UI、Layer、GamePatches、GodPowers、系统逻辑和扩展方法

- [Locales](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/Locales)
  多语言本地化文本

- [RegimeEditor](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/RegimeEditor)
  独立政体编辑器及配套工具

- [CultureRulesConfig.json](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/CultureRulesConfig.json)
  文化与政体绑定配置

- [mod.json](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/mod.json)
  模组元数据

---
## 技术栈

- **语言**: C#
- **运行环境**: Unity / WorldBox Modding Environment
- **框架与依赖**:
  - NeoModLoader
  - Harmony
  - Newtonsoft.Json
  - DOTween

---
## 安装说明

1. 安装 **WorldBox**
2. 确保模组环境支持 **NeoModLoader**
3. 将本项目放入 WorldBox 的 `Mods` 目录
4. 启动游戏并启用模组

---
## 当前状态

- 项目仍在持续开发中
- 功能覆盖面已经较大，但仍在不断平衡玩法、优化性能、修复边缘行为与补完 UI

---
## 相关链接

- 仓库地址: [EmpireCraft](https://github.com/ZhaoyuZhang101/EmpireCraft)
- 许可证: [LICENSE](/D:/Application/Steam/steamapps/common/worldbox/Mods/EmpireCraft/LICENSE)
