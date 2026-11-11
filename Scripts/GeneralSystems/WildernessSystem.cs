using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 无人区(用户 2026-10-29 "之前的沙漠和冻土什么的地块扩展就算了，我想改成另外一种，那就是利用城市这个实体作出无人区地块，
// 可以有多个类型比如山脉冻土沙漠，将连片的沙漠或者山脉全覆盖并且依据最近的文化进行命名……主要是为了吧地图填满，
// 同时能更有效地阻隔国家军队和更容易计算控制力"；问答选"专门的无人区层""荒地块收回，城心村镇留"，
// 类型山脉、沙漠、冻土雪原、丛林、沼泽)：
//   · 归类：按地块里陆地格的构成判(见 Classify)，门槛严——是整片荒地，不是有点山、有片沙；水多(过四分之一)的不算；
//   · 连片：同类型四邻相连的地块连成一片，至少 MinZones 块才算无人区；太大的不切(一整片撒哈拉就是一片)。
//     城心那块、有村镇(村、镇、关，废墟也算)与厂区的地块留给城，区域在它们周围断开；
//   · 与城互斥(用户 2026-10-29 "无人区层基本和城市层级同级……他一旦变动了也会去挤压或者导致隔壁的城市地块变形的")：
//     一块地要么归城、要么归无人区。每次划区，划进来的地块被城占着就当场从城里划出来(邻城因此变形是预期的)，
//     不再算无人区的放回无主，城照常可以再扩进去；
//     把地给城的路子(扩张、村镇划地、改隶、迁城、边界画笔、建城)碰到无人区都挡住，统一拦在 CityExpansionPatch.CanAddZone
//     (原版 addZone 都经它)。只放行神力手动立村设镇筑关、放厂区(见 Settle)、建城与新城落脚的头一块地
//     (用户 2026-10-20 "手动放置建筑时候不要有任何限制")：那块地归城、当即退出无人区(见 OnZoneTaken)，停手一两秒后重划时区域在那里断开；
//   · 不是城：不进人口、赋税、战争目标、外交、统计这些按城算的系统；
//   · 法理(用户 2026-10-29 "这些无人地块一般会被纳入有实体城市的法理。一旦任何一个国家获取了这个法理，
//     那么这个无人地块就会自动被纳入国家范围，主要是模拟西藏俄罗斯之类的准备的")：每块地挂到离它最近(按地块走、不隔海，
//     不限远近；隔海走不到的岛按直线)的城所在的那个法理上，一大片可以分给几个法理；持有这个法理的国家
//     (KingdomExtension.FindRealmTitleHolder，同法理之争、继承法理的口径)就算这块地的主人(OwnerOf)，地块仍不归任何城。
//     同城的地一个样(用户 10-30 "所有的包含逻辑都和城市等同""唯一的区别就是它可以被分割归属，而城市只能整块纳入")：
//     每块地跟着一座城(OriginOf)，那座城在持有国或同一帝国的省里就归那座城所在的国(省)；最近的城没有法理的跟着那座城，归它的国；
//     法理与承载城市随存档；预设剧本只修补新地块或消失的法理，新增城市不重分仍有效的辖境；随机世界仍按城与法理变化核对；
//     沙漠、冻原归游牧(用户 10-30 看长城以北"这部分无人区建议给游牧"，问答定"沙漠和冻原""只挂游牧城的法理")：
//     这两类先挂走得到的最近的游牧国(文化是游牧的国家，见 FrontierRelations.IsNomadRealm)的城的法理(只穿过无人区、
//     无主地与游牧国的地，不穿过农耕国的城)，游牧的城都走不到才照旧挂最近的城的；谁取得这个法理就归谁(农耕国打下草原的法理也算)。剧本写了史实归属的仍照史实；
//   · 起名：新的一片才起(WildernessNaming，按离区域中心最近的城的主流文化，没有城按全图最常见的；种子用区域编号)，
//     重划时按与旧区域重叠最多的沿用旧编号与名字；起名时全图还没有文化的，等有了城再按文化起；
//   · 何时划(用户 2026-10-29 "无人区一般会在地图加载的时候会被立马被识别""如果地形什么的变了这些区层也要更着变动")：
//     读档、新开地图、换世界后(剧本开局连同进世界安排都做完)头一帧当即划，读档先按存档恢复编号、名字、法理再按地形校一遍；
//     设地面格、顶层格(见 MapWallPatch)记下那几块，停手 DebounceSeconds 秒后重判这几块，类型真变了才重划
//     (神力刷一片也只在停手后划一次)；村镇、厂区变了(RuralSettlementSystem.Revision、FactorySystem.Revision)至多每年重划一次；
//   · 开发(见 WildernessDevelopmentSystem)：雨林、大泽的地块按开发度一批批改成草地；开发中的照开发时的类型留在无人区(Reclassify)，
//     开满的留给城(同城心、村镇，见 Regroup 的 anchored)，城照常扩张领进去；山脉、沙漠、冻原、熔岩固定不开发；
//     雨林、大泽按地形预划县址(见 WildernessPlotSystem，每次划区后维护)，开满的只归这一县的县城；
//   · 模组设置「无人区」(默认开)：关掉时没有无人区(Regions 为空、IsWild 全为假)；开着时从城里划出来的地块当场还给原来那座城，
//     划区时交给别城的城地碎块也还回去(用户 2026-11-12 转群友"关掉中立区之后一瞬间恢复成我画的图了，然后就变成这种情况了"：
//     以前划出来的不还，原版扩张又不进山，山地永远空着成了一片灰；问答选"还给原来的城""碎块一起还""旧档按挂接的城补一次")。
//     哪块从哪座城划出的随存档(WildRegionData.carved)；旧档没记的，仍空着、不是永久中立的按跟着的城补记一次(见 CarveRule)；
//     世界地图预设剧本(真实地球，见 WorldMap.WorldMapWilderness.ForcesWild，随存档)不看这个设置，一直开着——剧本的史实归属、地名、
//     各时代开发度都靠无人区；玩家自己的图按设置开关(用户 2026-10-30 "预设剧本是强制开的，但是玩家自己的图可以选择开关")，
//     地球地图上的自定剧本(手做的、从当前世界导入的)也算自己的图(用户 2026-11-04 选"自定剧本也听设置")；
//   · 熔岩(用户 2026-11-03 "还有无人区你怎么没把岩浆算进去""原版都算进去了你没算")：以前岩浆格同水面一起算，一片岩浆、
//     一座火山因"水多过四分之一"判不成无人区，城也立不住、用不上，成了地图上的空地；现在岩浆格算陆地、另记一类(见 Classify)，
//     成片的熔岩原、火山划成熔岩无人区：谁都过不去(游牧、阿拉伯也不例外，见 WildernessPassage)，固定不开发，起名用 WildLava 词库。
//     岩浆会流、会冷却，格子变了照常停手后重判(喷发时这一带隔几秒重划一回，冷却后退回山脉或无主地，城照常再扩回去)。
//   · 模组设置「雨林全开发」(默认开，见 JungleDeveloped)：玩家自己的图上雨林不划无人区；世界地图剧本同样不受它管。
// 全图归类只在读档与开关打开时做(每块地 64 格扫一遍)；连片、对旧区域、收地只看地块，几万块地几毫秒。
// 高原(用户 2026-11-10)：高寒高原格(见 PlateauTerrain)占三成五、连同冻土雪地山地丘陵够八成五的划高原无人区(青藏、帕米尔的高原面
// 夹着雪山)；黄土高原是农耕之地，不划。法理挂走得到的最近的城(不先给游牧，用户问琼结为何比藏巴多认领那么多无人区后定)，
// 隔着外缘山脊走不进来(见 Spread)；游牧之国的城能扩进挨着的高原无人区("高原游牧不算荒地"，见 HerdsOnPlateau)。
// 类型按整数存档(WildRegionData.kind、开发度、县址)：只往后加，不改已有的编号
public enum WildKind { None = 0, Mountain = 1, Desert = 2, Tundra = 3, Jungle = 4, Swamp = 5, Lava = 6, Plateau = 7 }

public sealed class WildRegion
{
    public int Id;                       // 稳定编号(存档里沿用)
    public WildKind Kind;
    public string Name;                  // 全名，如"撒哈拉大沙漠"(由 WildernessNaming 起)
    public bool CustomName;              // 名字是在无人区窗口里改的(见 WildernessSystem.Rename)：自动起名不再动它
    public string Culture;               // 起名所依的文化 id
    public readonly List<int> Zones = new(); // 地块 id
    public TileZone Center;              // 标名字的位置(区域里最靠中间的一块)
    public int MinX, MinY, MaxX, MaxY;   // 地块坐标外框
    // 这一片的底色(显示用)：按类型定色系，按编号微调，挨着的两片不至于一个颜色；编号不变颜色就不变
    public Color32 Color;
    // 整片均中立的汇总；实际每块的中立状态由 WildernessSystem.IsNeutral 查询。
    public bool Neutral;
    // 周边的城全属于同一个国家时就是它(见 OwnerOf)，否则为 null；随归属一起隔几秒核一次
    public Kingdom Surround;
}

// 存档里的一片无人区(见 SaveData.wild_regions)
public class WildRegionData
{
    public int id;
    public int kind;
    public string name;
    // 名字是改过的(见 WildRegion.CustomName)
    public bool custom_name;
    public bool ShouldSerializecustom_name() => custom_name;
    public string culture;
    public List<int> zones = new List<int>();
    // 与 zones 一一对应：各块挂的法理编号(没挂为 -1)
    public List<long> titles = new List<long>();
    // 与 zones 一一对应的行政承载城市；旧档缺失时按剧本锚点或原法理补齐。
    public List<long> origins;
    public bool ShouldSerializeorigins() => origins is { Count: > 0 };
    // 永久中立区
    public bool neutral;
    public bool ShouldSerializeneutral() => neutral;
    // 部分中立时只保存命中的地块；旧版 neutral=true 仍表示整片。
    public List<int> neutralZones;
    public bool ShouldSerializeneutralZones() => !neutral && neutralZones is { Count: > 0 };
    // 权能笔刷划过法理的地块(见 WildernessSystem.Paint)：自动重挂不动它们
    public List<int> painted;
    public bool ShouldSerializepainted() => painted is { Count: > 0 };
    // 按第几版分地规矩挂的(见 WildernessSystem.AttachRule)；旧档 0，读档后按新规矩重分一次
    public int rule;
    public bool ShouldSerializerule() => rule > 0;
    // 与 zones 一一对应：这块是从哪座城里划出来的(城编号，本来就不归城的为 -1)；关掉设置时还给那座城(见 WildernessSystem.SetEnabled)
    public List<long> carved;
    public bool ShouldSerializecarved() => carved != null && carved.Exists(id => id >= 0);
    // 记划出来源的版本(见 WildernessSystem.CarveRule)；旧档 0：没记过，仍空着的地块按跟着的城补记一次
    public int carve;
    public bool ShouldSerializecarve() => carve > 0;
}

public static class WildernessSystem
{
    // 归类门槛(占地块陆地格的比例)：
    //   · 水面过四分之一的不算(海岸、湖、河网不是荒地，海另有海区)；陆地不到 MinLand 格的不算。
    //     岩浆不算水(同 SmoothBorders.IsWater、LandKinds)，算陆地，另记岩浆格；
    //   · 熔岩(先于山脉判，火山归熔岩不归山脉)：岩浆格占三成(熔岩原)，或一成五以上且连山地山巅占六成(火山：山口、山坡上淌着岩浆，
    //     余下是山体；同山脉"两肩"的口径，岩浆少一半、连山的总量低一档)。门槛比别的类型低得多：岩浆格不能走、不能建、会流会烧，
    //     同水面过四分之一就不算荒地一个分量——三成岩浆的地块余下的也多是烧焦冷却的地，城立不住，人走不过；
    //   · 山脉：山地、山巅格(原版的阻挡格)过半，或三成五以上且连丘陵、石地占到四分之三——
    //     战线地形的"山地"只要两成五、城市扩张的"山地区块"两成(TerrainSystem)，那是"有点山"；
    //     不可通行要阻挡格占四分之三或山脊拦腰截断，过半已是山腹，三成五加满坡丘陵是山脉的两肩；
    //     够不上熔岩的零星岩浆格也算山地(山里的火山口是山的一部分；以前岩浆当水不进分母，山里淌着几格岩浆的地块照旧归山脉)；
    //   · 沙漠、冻土雪原：沙地荒漠或冻土雪地(连雪丘)占八成五——同剧本铺辖地的"整块荒漠"(WorldMapZones)，
    //     比纯沙漠纯冻土(BarrenLandSystem，一格不差)松一点，绿洲边上几格草不至于把大漠戳成筛子；两样混着的按多的那样算；
    //   · 沼泽七成五、丛林八成五(丛林宜居度不低，城常扩进去，门槛更严)；两样混着够八成五的按多的那样算；
    //   · 开过的田不算荒地(路、城墙铺在上面的看底下的地面，同 TerrainSystem)；
    //   · 已是无人区的那一类门槛降一成(Hysteresis)：草木蔓生、刷几格地，边上的地块不至于今天划进明天划出
    private const float MaxWater = 0.25f;
    private const int MinLand = 16;
    private const float MountainCore = 0.5f, MountainFlank = 0.35f, MountainRugged = 0.75f;
    private const float LavaCore = 0.3f, LavaFlank = 0.15f, LavaRugged = 0.6f;
    // 高原无人区：高寒高原格至少占陆地这么多(其余是雪山、丘陵、冻土)
    private const float PlateauCore = 0.35f;
    private const float BarrenShare = 0.85f, SwampShare = 0.75f, JungleShare = 0.85f;
    private const float Hysteresis = 0.1f;
    // 至少几块连成一片才算无人区
    public const int MinZones = 4;
    // 外沿放宽(用户 2026-10-29 "你这无人区也没覆盖完全啊")：核心按上面的严门槛定，再一圈圈往外收这一类地形过半的地块
    // (山脉连同丘陵石地、零星岩浆，沙漠冻原连同两样，雨林大泽连同两样)，直到收不动——山脚的丘陵带、沙漠丛林的外沿都并进来；
    // 平滑国界下只涂这一类地形的格子(见 WildernessShape)，收进来的地块里别的格交给邻城、露地形；
    // 已是这一片的地块门槛再降一成(同 Hysteresis)
    // 山脉的外沿门槛高一档(六成)：五成时山西的太原、临汾、大同盆地被并进太行，安纳托利亚、伊朗、巴尔干的河谷也被收进去
    // (按真实地形预演，见 Tools/WorldMap/preview_wilderness.py)
    // 熔岩的外沿只看岩浆格、一成五(核心三成的一半，同别的类型外沿约为核心的六成上下)：熔岩原边上淌进来的岩浆舌头并进来；
    // 光是山石的交给山脉收(要是同山脉一样连山石按五成算，平地上熔岩原的外沿得岩浆过半，比核心还严，等于不放宽)。
    // 山脉与熔岩还互收对方零星的核心(不到 MinZones 块，见 Absorbs)
    private const float EdgeShare = 0.5f, MountainEdgeShare = 0.6f, LavaEdgeShare = 0.15f;
    // 改地形停手几秒后重判；一直有格子在变(草木蔓生)时至多等几秒；法理挂接与主人几秒核对一次(真实时间)
    private const float DebounceSeconds = 1.5f, MaxWaitSeconds = 10f, RefreshSeconds = 2f, RetrySeconds = 30f;
    // 村镇、厂区变了至多隔几年重划一次
    private const int AnchorYears = 1;
    // 归游牧：从游牧国的城的地块往外走，至多几块(不隔海)；再远的照最近的城挂(最近的城不限远近，见 Attach)
    private const int MaxAttachSteps = 64;

    // 模组设置；眼下实际开着没有(设置开着，或这一局是世界地图预设剧本；变了由 Apply 清掉重划)
    private static bool _setting = true, _enabled = true;
    private static List<WildRegion> _regions = new();
    private static readonly Dictionary<int, WildRegion> IdIndex = new();
    // 按地块编号：所在区域在 _regions 里的下标(-1 不是)、地形判的类型(没算过、不是都为 0)、挂的法理
    private static int[] _regionOf = new int[0];
    private static byte[] _kind = new byte[0];
    private static KingdomTitle[] _titles = new KingdomTitle[0];
    // 权能笔刷划过法理的地块(见 Paint)；有擦掉的，下一核补挂
    private static bool[] _painted = new bool[0];
    private static bool[] _neutralZones = new bool[0];
    // 各块跟着的城；随无人区存档，避免重载后从邻城重新推算行政归属。
    private static City[] _parents = new City[0];
    // 各块是从哪座城里划出来的(见 Regroup 收地；本来就不归城的为 null)：关掉设置时还给它(见 SetEnabled)，随无人区存档
    private static City[] _carved = new City[0];
    // 压着长城(城墙格)、不归城的地块：挂法理往外走、连城都到此为止，长城那边的无人区不从这边挂(见 WallZones)
    private static bool[] _walled = new bool[0];
    private static bool _attachPending;
    private static TileZone[] _zones = new TileZone[0];
    private static bool _built;
    private static int _nextId = 1;
    // 读档读进来、还没用上的(头一回划区时按它沿用编号名字法理)；关掉开关时也把现有的存在这里
    private static List<WildRegionData> _saved;
    // 改过格子、等着重判的地块；有地块被手动放行给了城(见 OnZoneTaken)时类型没变也要重划(_regroup)；
    // 有事等着(_pending)：头一件、最近一件的时间(真实时间，去抖)
    private static readonly HashSet<int> Dirty = new();
    private static bool _regroup, _pending;
    private static float _firstDirtyAt, _lastDirtyAt, _nextRefresh, _retryAt;
    private static double _builtAt = -1d;
    private static int _settlementRevision, _factoryRevision;
    private static long _titleSignature;
    private static readonly Dictionary<KingdomTitle, Kingdom> Owners = new();
    private static int _settling;

    // 开着没有：设置开着，或世界地图预设剧本(强制开，见类头)。内部按 Apply 对上的 _enabled，换局后头一帧对上
    public static bool Enabled => _setting || WorldMap.WorldMapWilderness.ForcesWild;
    // 只看设置本身(剧本编辑器预测用：还没进世界，ForcesWild 是上一局的；编辑器只编自定剧本，都照设置，见 WorldMap.ScenarioWilderness)
    public static bool EnabledSetting => _setting;

    // 区域每变一次 +1(显示、通行据此重算)；挂接或归属变了也 +1
    public static int Version { get; private set; }

    // 每重新连片一次 +1(只算划区，挂接、归属、起名不算)：县址据此维护、补划(见 WildernessPlotSystem)
    public static int Builds { get; private set; }

    public static IReadOnlyList<WildRegion> Regions => _regions;

    public static int NextId => _nextId;

    public static WildRegion RegionOf(TileZone zone)
    {
        int index = Index(zone);
        return index >= 0 ? _regions[index] : null;
    }

    public static bool IsWild(TileZone zone) => Index(zone) >= 0;

    public static bool IsWildAt(int x, int y) => _regions.Count > 0 && IsWild(World.world?.GetTile(x, y)?.zone);

    public static WildKind KindOf(TileZone zone)
    {
        int index = Index(zone);
        return index >= 0 ? _regions[index].Kind : WildKind.None;
    }

    public static WildRegion ById(int id) => IdIndex.TryGetValue(id, out WildRegion region) ? region : null;

    // 这块无人区挂在哪个法理上(没有为 null)
    public static KingdomTitle TitleOf(TileZone zone)
    {
        if (Index(zone) < 0 || zone.id >= _titles.Length) return null;
        KingdomTitle title = _titles[zone.id];
        return title == null || title.isRekt() ? null : title;
    }

    // 这块无人区归哪国(用户 2026-10-29 "关于并进哪个国家我建议用法理判断, 如果他属于一个法理那么一旦有国家获得这个法理
    // 那么这个地会被百分百划入这个国家, 如果周边的几个城市全属于同一个国家这个无人区也会被划入, 否则他是中立的.
    // 也可以手动将他设置成永久中立区")：
    //   · 永久中立区(神力设的，见 ToggleNeutral)：不归任何国家；
    //   · 挂了法理、法理有持有国(KingdomExtension.FindRealmTitleHolder)：归持有国——跟着的城(OriginOf)在持有国或同一帝国的省里
    //     就归那座城所在的国(省)，同城的地一个样(用户 10-30 "所有的包含逻辑都和城市等同"：安西都护府的城挂在核心持有的龟兹法理上，
    //     以前城外的无人区算核心京畿道的，"整个西域的无人区都给归入京畿道了")；
    //   · 法理没人取得、没挂上法理(最近的城没有法理)：归跟着的城所在的国(城在没人取得的法理里也照样归它的国)；
    //   · 否则这一片周边的城(与区域相邻的地块上的城)全属于同一个国家：归那个国家(见 RefreshOwners)；
    //   · 都不是：中立(null)
    //   · 太小的碎块(不到 MinZones 块、不挨着本国的地，或没有归属)并进四周挨得最多的那一国(见 RefreshFragments)
    public static Kingdom OwnerOf(TileZone zone) => Resolve(zone, out _);

    private static Kingdom Resolve(TileZone zone, out bool byTitle)
    {
        Kingdom owner = BaseResolve(zone, out byTitle);
        if (Merged.Count == 0 || Index(zone) < 0 || IsNeutral(zone) ||
            !Merged.TryGetValue(zone.id, out (Kingdom from, Kingdom into) merge) || merge.from != owner ||
            merge.into?.data == null || merge.into.isRekt()) return owner;
        byTitle = false;
        return merge.into;
    }

    // 这块无人区归 owner(OwnerOf 的结果)是因太小并进来的(见 RefreshFragments)：显示按 owner 画，不按跟着的城所在的省
    public static bool MergedInto(TileZone zone, Kingdom owner) =>
        owner != null && Merged.Count > 0 && zone != null && Merged.TryGetValue(zone.id, out (Kingdom from, Kingdom into) merge) &&
        merge.into == owner;

    // 不算碎块合并的归属(见 OwnerOf)；离那国的城太远的不算(见 RefreshReach)
    private static Kingdom BaseResolve(TileZone zone, out bool byTitle)
    {
        Kingdom owner = Candidate(zone, out byTitle);
        if (owner == null || zone.id < 0 || zone.id >= _far.Length || !_far[zone.id]) return owner;
        byTitle = false;
        return null;
    }

    // 不看远近时这块无人区归谁
    private static Kingdom Candidate(TileZone zone, out bool byTitle)
    {
        byTitle = false;
        int index = Index(zone);
        if (index < 0) return null;
        WildRegion region = _regions[index];
        if (IsNeutral(zone)) return null;
        Kingdom local = OriginOf(zone)?.kingdom;
        if (local != null && (local.data == null || local.isRekt() || local.wild)) local = null;
        // 城归谁，它辖下的无人区就归谁(用户 2026-11-04 "一叛乱就是飞地, 你至少带着中间的无人区一起啊")：
        // 以前按法理持有国，叛军拿了城、城外的无人区还留给旧朝，叛军的城成了一个个孤岛
        if (local != null)
        {
            byTitle = TitleOf(zone) != null;
            return local;
        }
        Kingdom holder = HolderOf(zone);
        if (holder != null)
        {
            byTitle = true;
            return holder;
        }
        Kingdom around = region.Surround;
        return around != null && !around.isRekt() ? around : null;
    }

    // 同一国，或同一帝国(帝国的省)
    private static bool SameRealm(Kingdom a, Kingdom b)
    {
        if (a == b) return true;
        if (a == null || b == null) return false;
        Empire empire = a.GetEmpire();
        return empire != null && empire == b.GetEmpire();
    }

    // 取得这块无人区所挂法理的国家(不管永久中立、周边的城；悬停提示里写明法理归谁，看得出为什么没并进国土)
    public static Kingdom HolderOf(TileZone zone)
    {
        KingdomTitle title = TitleOf(zone);
        return title != null && Owners.TryGetValue(title, out Kingdom holder) && holder != null && !holder.isRekt() ? holder : null;
    }

    // 这块无人区是因所挂法理而归属的(不是因周边的城同属一国)：悬停提示里写明是哪个法理
    public static bool OwnedByTitle(TileZone zone) => Resolve(zone, out bool byTitle) != null && byTitle;

    // 神力「中立区」：点一片无人区设为永久中立区，再点一次取消；返回那一片(不是无人区为 null)
    public static WildRegion ToggleNeutral(TileZone zone)
    {
        WildRegion region = RegionOf(zone);
        if (region == null) return null;
        SetNeutral(region, !IsNeutral(zone));
        return region;
    }

    // 剧本编辑器刷的永久中立区(开局头一回划区挂法理时设上，见 WorldMap.WorldMapWilderness.BeginPaint)：之后同神力设的一样随存档
    public static void SetNeutral(WildRegion region, bool neutral)
    {
        if (region == null) return;
        SetNeutralZones(region.Zones, neutral);
    }

    public static bool IsNeutral(TileZone zone) => zone != null && IsNeutral(zone.id);

    public static bool IsNeutral(int id) => _enabled && id >= 0 && id < _regionOf.Length && _regionOf[id] >= 0 &&
        (id < _neutralZones.Length ? _neutralZones[id] : _regions[_regionOf[id]].Neutral);

    // 剧本笔刷按地块生效；神力的整片开关仍通过 SetNeutral 设置整片。
    internal static int SetNeutralZones(IEnumerable<int> zones, bool neutral)
    {
        if (zones == null) return 0;
        if (_neutralZones.Length != _regionOf.Length)
        {
            int previous = _neutralZones.Length;
            Array.Resize(ref _neutralZones, _regionOf.Length);
            foreach (WildRegion region in _regions.Where(region => region.Neutral))
                foreach (int id in region.Zones)
                    if (id >= previous && id < _neutralZones.Length) _neutralZones[id] = true;
        }
        var touched = new HashSet<int>();
        int changed = 0;
        foreach (int id in zones)
        {
            if (id < 0 || id >= _regionOf.Length || _regionOf[id] < 0 || _neutralZones[id] == neutral) continue;
            _neutralZones[id] = neutral;
            touched.Add(_regionOf[id]);
            changed++;
        }
        foreach (int index in touched)
            _regions[index].Neutral = _regions[index].Zones.All(id => _neutralZones[id]);
        if (changed > 0) Version++;
        return changed;
    }

    // 一块地归城就不算无人区(两者互斥)：放行给城的那一刻到下次重划之间也照这个算
    private static int Index(TileZone zone)
    {
        if (!_enabled || zone == null) return -1;
        int id = zone.id;
        if (id < 0 || id >= _regionOf.Length) return -1;
        int index = _regionOf[id];
        return index >= 0 && index < _regions.Count && zone.city == null ? index : -1;
    }

    #region 放行、改地形

    // using (WildernessSystem.Settle()) { city.addZone(...); }：神力手动立村设镇筑关、放厂区时那块无人区放行给城(见 CanTake)
    public static SettleScope Settle()
    {
        _settling++;
        return default;
    }

    public readonly struct SettleScope : IDisposable
    {
        public void Dispose()
        {
            if (_settling > 0) _settling--;
        }
    }

    // 无人区的地能不能给这座城(CityExpansionPatch.CanAddZone 里问)：神力手动营建(Settle)、手动建城(CityPlacementSystem.Placing)、
    // 新城落脚的头一块地(城心总归城；剧本、殖民、穿越者降临选址都先避开无人区，见 CityExpansionPatch.AfterCanStartCityHere)
    public static bool CanTake(City city) =>
        _settling > 0 || CityPlacementSystem.Placing || city?.zones == null || city.zones.Count == 0;

    // 高原游牧不算荒地(用户 2026-11-10)：游牧之国的城能扩进挨着的高原无人区(扩进去的地当即退出无人区，同神力手动划地)
    public static bool HerdsOnPlateau(City city, TileZone zone) =>
        city?.kingdom != null && KindOf(zone) == WildKind.Plateau && FrontierRelations.IsNomadRealm(city.kingdom);

    // 地块归了城(City.addZone 之后，见 CityExpansionPatch.AfterAddZone)：当即退出无人区，停手后重划
    public static void OnZoneTaken(TileZone zone)
    {
        if (!_built || zone == null || zone.id < 0 || zone.id >= _regionOf.Length) return;
        int index = _regionOf[zone.id];
        if (index < 0 || index >= _regions.Count) return;
        _regionOf[zone.id] = -1;
        _regions[index].Zones.Remove(zone.id);
        if (zone.id < _titles.Length) _titles[zone.id] = null;
        if (zone.id < _painted.Length) _painted[zone.id] = false;
        if (zone.id < _carved.Length) _carved[zone.id] = null;
        _regroup = true;
        Pending();
        Version++;
    }

    // 当场整图重判重划(剧本开局按时代改过地形后，见 WorldMap.WorldMapDevelopment)：不等进世界安排做完、不等停手，划过的也重划
    public static void BuildNow()
    {
        Apply();
        if (!_enabled || World.world?.zone_calculator?.zones == null || World.world.cities == null) return;
        using (FrameProfiler.Measure("无人区·划区")) Build(true, true);
    }

    // 没有历史雨林改造的剧本也要在加载收尾完成首次划分；已由开发度流程划好时不重复整图重建。
    public static void EnsureBuilt()
    {
        if (!_built) BuildNow();
    }

    // 一块地开发满了(或开满的又改回去，见 WildernessDevelopmentSystem)：停手后重划，开满的留给城
    public static void OnDeveloped(TileZone zone)
    {
        if (!_built || zone == null || zone.id < 0) return;
        Dirty.Add(zone.id);
        _regroup = true;
        Pending();
    }

    // 设地面格、顶层格之后(见 MapWallPatch)：记下这块地，停手后重判(加载、建图时设的格不管，那时还没划区)
    public static void OnTileChanged(TileZone zone)
    {
        if (!_built || zone == null || zone.id < 0) return;
        Dirty.Add(zone.id);
        Pending();
    }

    private static void Pending()
    {
        float now = Time.realtimeSinceStartup;
        if (!_pending)
        {
            _pending = true;
            _firstDirtyAt = now;
        }
        _lastDirtyAt = now;
    }

    #endregion

    #region 开关、存读档

    // 模组设置「无人区」(见 ConfigActions.WildernessCallBack)：世界地图预设剧本不受它管
    // 关掉时(设置管得着的图，见 ForcesWild)开着时从城里划出来的地块当场还给原来那座城，城地碎块还回原城(见类头)
    public static void SetEnabled(bool on)
    {
        bool releasing = !on && _enabled && _built && !WorldMap.WorldMapWilderness.ForcesWild && InWorld();
        _setting = on;
        List<(TileZone zone, City city)> back = releasing ? CarvedBack() : null;
        Apply();
        if (back == null) return;
        // 存起来的(Apply 里刚存的)已还过：再读这一局时不再还(见 ReleaseSaved)
        if (_saved != null) MarkReleased(_saved);
        try
        {
            int zones = GiveBack(back);
            int fragments = CityFragmentSystem.RestoreAll();
            LogService.LogInfo($"[EmpireCraft][无人区] 关掉无人区：{zones} 块还给原来的城，{fragments} 块城地碎块还回原城");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][无人区] 关掉时还地出错: {exception}");
        }
    }

    // 世界加载完、不在建图开局里(还地、补记都只在这时做)
    private static bool InWorld()
    {
        MapBox world = World.world;
        return world?.zone_calculator?.zones != null && world.cities != null && Config.game_loaded && !SmoothLoader.isLoading() &&
               !ModClass.IS_CLEAR && !WorldMap.WorldMapGenerator.Generating && !WorldMap.WorldMapScenario.AfterLoadPending;
    }

    // 眼下的无人区里记着划出来源的地块
    private static List<(TileZone zone, City city)> CarvedBack()
    {
        var back = new List<(TileZone zone, City city)>();
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
                if (id >= 0 && id < _carved.Length && _carved[id] != null && id < _zones.Length && _zones[id] != null)
                    back.Add((_zones[id], _carved[id]));
        return back;
    }

    // 还地：地块仍空着、那座城还在才还(按神力手动划地，不受每城上限、边界固定挡)；返回还了几块
    private static int GiveBack(List<(TileZone zone, City city)> back)
    {
        var touched = new HashSet<City>();
        int count = 0;
        foreach ((TileZone zone, City city) in back)
        {
            if (!ShouldGiveBack(zone?.city != null, city?.data != null && !city.isRekt())) continue;
            using (GamePatches.CityExpansionPatch.Manual()) city.addZone(zone);
            if (zone.city != city) continue;
            touched.Add(city);
            count++;
        }
        if (touched.Count == 0) return 0;
        CityDistricts.Reset();
        foreach (City city in touched) PureMapPatch.Redraw(city);
        World.world.zone_calculator?.setDrawnZonesDirty();
        return count;
    }

    // 还不还：地块又归了城的(城照常扩进去了)、原城没了的不还
    internal static bool ShouldGiveBack(bool zoneHasCity, bool cityAlive) => !zoneHasCity && cityAlive;

    // 划区后这块记的划出来源：这回从城里划出来的记那座城；本来就空着的沿用上回记的(那座城还在)；
    // 旧档没记过的(legacy，见 CarveRule)、不是永久中立的记跟着的城；都不是为 null(本来就不归城，关掉时照旧空着)
    internal static T CarvedSource<T>(T carvedNow, T before, bool beforeAlive, bool legacy, bool neutral, T origin) where T : class =>
        carvedNow ?? (before != null && beforeAlive ? before : legacy && !neutral ? origin : null);

    // 关着无人区读进来的档里还记着没还的(开着时存的、或在别处关掉设置后才读的档没经过 SetEnabled；旧档没记来源，见 CarveRule)：
    // 记着的地块仍空着就还给划出它的城，旧档的、不是永久中立的还给跟着的城；交出去的城地碎块还回原城。还过的记号清掉，一档一次
    private static void ReleaseSaved()
    {
        _releasePending = false;
        if (_saved == null || _saved.Count == 0)
        {
            CityFragmentSystem.RestoreAll();
            return;
        }
        ZoneCalculator calc = World.world.zone_calculator;
        var back = new List<(TileZone zone, City city)>();
        foreach (WildRegionData data in _saved)
        {
            bool legacy = data.carve < CarveRule;
            var neutral = data.neutralZones == null ? null : new HashSet<int>(data.neutralZones);
            for (int k = 0; k < data.zones.Count; k++)
            {
                int id = data.zones[k];
                long carved = data.carved != null && k < data.carved.Count ? data.carved[k] : -1L;
                long origin = data.origins != null && k < data.origins.Count ? data.origins[k] : -1L;
                long source = SavedSource(carved, legacy, data.neutral || neutral?.Contains(id) == true, origin);
                if (source < 0) continue;
                TileZone zone = calc.getZoneByID(id);
                City city = World.world.cities.get(source);
                if (zone != null && city != null) back.Add((zone, city));
            }
        }
        MarkReleased(_saved);
        try
        {
            int zones = GiveBack(back);
            int fragments = CityFragmentSystem.RestoreAll();
            if (zones > 0 || fragments > 0)
                LogService.LogInfo($"[EmpireCraft][无人区] 关着无人区读档：{zones} 块开着时划出去的地还给原来的城，{fragments} 块城地碎块还回原城");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][无人区] 读档还地出错: {exception}");
        }
    }

    // 存档里一块地该还给哪座城(城编号，-1 不还)：记了划出来源的还它；旧档没记的、不是永久中立的还跟着的城
    internal static long SavedSource(long carved, bool legacy, bool neutral, long origin) =>
        carved >= 0 ? carved : legacy && !neutral ? origin : -1L;

    // 已还过：清掉划出来源，记成新版(再读不再还；再打开时照常重划重记)
    private static void MarkReleased(List<WildRegionData> saved)
    {
        foreach (WildRegionData data in saved)
        {
            data.carved = null;
            data.carve = CarveRule;
        }
    }

    // 模组设置「雨林全开发」(默认开，见 ConfigActions.WildernessJungleDevelopedCallBack；用户 2026-11-03 "雨林地形百分百开发的开关，
    // 默认开启""剧情模式不受这个开关影响")：雨林算开发完毕——不划无人区，城能直接扩进去，住人、种地同平地(见 TerrainSystem.Livability)；
    // 格子不改，还是丛林，出产、行军、交战照旧。世界地图剧本(连同开局铺设的时候)照旧按开发度走
    private static bool _jungleDeveloped = true;

    public static bool JungleDeveloped => _jungleDeveloped && !WorldMap.WorldMapWilderness.OnEarth &&
                                          !WorldMap.WorldMapGenerator.Generating && !WorldMap.WorldMapScenario.AfterLoadPending;

    public static bool JungleDevelopedSetting => _jungleDeveloped;

    public static void SetJungleDeveloped(bool on)
    {
        if (_jungleDeveloped == on) return;
        _jungleDeveloped = on;
        // 已经划好的图全部重判一遍(走停手重划，同改了地形)
        if (!_built || WorldMap.WorldMapWilderness.OnEarth) return;
        foreach (TileZone zone in _zones)
            if (zone != null && zone.id >= 0) Dirty.Add(zone.id);
        _regroup = true;
        Pending();
    }

    // 实际开关对上设置与这一局是不是预设剧本(每帧、当场划区前调)
    private static void Apply()
    {
        bool on = Enabled;
        if (on == _enabled) return;
        // 关掉：编号、名字、法理存起来，再打开时沿用
        if (!on && _built) _saved = Save();
        _enabled = on;
        _regions = new List<WildRegion>();
        IdIndex.Clear();
        _regionOf = new int[0];
        _titles = new KingdomTitle[0];
        _painted = new bool[0];
        _far = new bool[0];
        _cutOff = new bool[0];
        _neutralZones = new bool[0];
        _parents = new City[0];
        _carved = new City[0];
        Owners.Clear();
        Merged.Clear();
        Dirty.Clear();
        _regroup = _pending = false;
        _built = false;
        Version++;
    }

    public static void ResetWorldState()
    {
        _regions = new List<WildRegion>();
        IdIndex.Clear();
        _regionOf = new int[0];
        _kind = new byte[0];
        _titles = new KingdomTitle[0];
        _painted = new bool[0];
        _far = new bool[0];
        _cutOff = new bool[0];
        _neutralZones = new bool[0];
        _parents = new City[0];
        _carved = new City[0];
        _zones = new TileZone[0];
        _built = false;
        _nextId = 1;
        _saved = null;
        _releasePending = false;
        Dirty.Clear();
        _firstDirtyAt = _lastDirtyAt = _nextRefresh = _retryAt = 0f;
        _regroup = _pending = _attachPending = false;
        _builtAt = -1d;
        _titleSignature = 0L;
        Owners.Clear();
        Merged.Clear();
        _settling = 0;
        ReachCache.Clear();
        _reachPrimed = false;
        _fragmentBuilds = -1;
        _fragmentAt = 0f;
        Version++;
    }

    // 分地规矩的版本：1 = 换地形、下河有坎(见 Seam)。存档里的比这个旧，读档后自动挂的地块重分一次(权能笔刷划的、剧本照史实的不动)
    private const int AttachRule = 1;
    private static bool _ruleOutdated;
    // 记划出来源的版本：1 = 记了哪块从哪座城划出(WildRegionData.carved)。存档里比这个旧的没记过：开着读档时仍空着、不是永久中立的
    // 按跟着的城补记(见 Regroup)，关着读档时直接还给跟着的城(见 ReleaseSaved)，一档只做一次
    private const int CarveRule = 1;
    // 关着无人区读的档里有没还的地块(见 ReleaseSaved)；开着读档时划区就接管，不用再还
    private static bool _releasePending;

    public static void Load(List<WildRegionData> saved, int nextId)
    {
        _saved = new List<WildRegionData>();
        _nextId = Math.Max(1, nextId);
        _ruleOutdated = saved != null && saved.Any(data => data != null && data.rule < AttachRule);
        _releasePending = false;
        if (saved == null) return;
        foreach (WildRegionData data in saved)
        {
            if (data?.zones == null || data.zones.Count == 0 || data.kind <= 0 || data.kind > (int)WildKind.Plateau) continue;
            data.titles ??= new List<long>();
            _saved.Add(data);
            _nextId = Math.Max(_nextId, data.id + 1);
            if (data.carve < CarveRule || data.carved?.Exists(id => id >= 0) == true) _releasePending = true;
        }
    }

    public static List<WildRegionData> Save()
    {
        if (!_built || !_enabled) return _saved ?? new List<WildRegionData>();
        var result = new List<WildRegionData>(_regions.Count);
        foreach (WildRegion region in _regions)
        {
            var data = new WildRegionData { id = region.Id, kind = (int)region.Kind, name = region.Name, culture = region.Culture,
                custom_name = region.CustomName && !string.IsNullOrEmpty(region.Name), neutral = region.Neutral,
                origins = new List<long>(region.Zones.Count), rule = AttachRule,
                carved = new List<long>(region.Zones.Count), carve = CarveRule };
            foreach (int id in region.Zones)
            {
                City origin = OriginOf(_zones[id]);
                KingdomTitle title = id < _titles.Length ? _titles[id] : null;
                data.zones.Add(id);
                data.titles.Add(title == null || title.isRekt() ? -1L : title.id);
                data.origins.Add(origin?.data == null || origin.isRekt() ? -1L : origin.id);
                City carved = id < _carved.Length ? _carved[id] : null;
                data.carved.Add(carved?.data == null || carved.isRekt() ? -1L : carved.id);
                WorldMap.WorldMapWilderness.RememberTerritory(_zones[id], title, origin, IsNeutral(id),
                    IsPainted(_zones[id]) || WorldMap.WorldMapWilderness.PaintedTitle(_zones[id]) != null);
                if (id < _painted.Length && _painted[id] && title != null && !title.isRekt()) (data.painted ??= new List<int>()).Add(id);
                if (!region.Neutral && IsNeutral(id)) (data.neutralZones ??= new List<int>()).Add(id);
            }
            result.Add(data);
        }
        return result;
    }

    #endregion

    #region 锁定刷给城的地块

    // 城市边界固定时锁定玩家刷好的行政区(用户 2026-11-12 转群友"导入存档之后就变成这样""全是无人区整出来的坑""画了一晚上的"，
    // "玩家编辑好的行政区把地形锁定""如果开了固定城市或者地形的话")：剧本编辑器里刷给城(c:)、本剧本开无人区时开局划成那座城法理无人区的地块
    // (见 WorldMap.WorldMapWilderness.CityClaim)，法则开着就还给那座城，之后划区也不再划走(见 Regroup 的 anchored)。
    // 法则开着读档、中途打开法则各还一次；关掉法则不动(下次划区照常按地形划走)。永久中立的、那座城没了的、地块已归了别的城的不还
    private static bool _claimsLocked;

    private static bool RestoreClaimsDue()
    {
        bool locked = CityTerritoryProtection.BordersFixed && WorldMap.WorldMapWilderness.HasCityClaims;
        if (locked == _claimsLocked) return false;
        _claimsLocked = locked;
        return locked;
    }

    private static int RestoreClaims()
    {
        var touched = new HashSet<City>();
        int count = 0;
        foreach (KeyValuePair<int, long> claim in WorldMap.WorldMapWilderness.CityClaims.ToList())
        {
            int id = claim.Key;
            TileZone zone = id >= 0 && id < _zones.Length ? _zones[id] : null;
            if (zone == null || zone.city != null || Index(zone) < 0 || IsNeutral(zone)) continue;
            City city = World.world.cities.get(claim.Value);
            if (city?.data == null || city.isRekt()) continue;
            using (GamePatches.CityExpansionPatch.Manual()) city.addZone(zone);
            if (zone.city != city) continue;
            touched.Add(city);
            count++;
        }
        if (count == 0) return 0;
        CityDistricts.Reset();
        foreach (City city in touched) PureMapPatch.Redraw(city);
        World.world.zone_calculator?.setDrawnZonesDirty();
        LogService.LogInfo($"[EmpireCraft][无人区] 城市边界固定：剧本刷给城的 {count} 块还给 {touched.Count} 座城");
        return count;
    }

    #endregion

    #region 每帧

    // 每帧(ModClass.Update)：加载完头一帧划区；改过地形的停手后重判；村镇厂区变了一年一划；法理挂接与主人几秒一核
    public static void Tick()
    {
        Apply();
        if (!_enabled)
        {
            // 关着无人区读进来、档里还记着没还的地块：还给原来的城(一档一次，见 ReleaseSaved)
            if (_releasePending && InWorld()) ReleaseSaved();
            return;
        }
        MapBox world = World.world;
        if (world?.zone_calculator?.zones == null || world.cities == null || !Config.game_loaded || SmoothLoader.isLoading() ||
            ModClass.IS_CLEAR || WorldMap.WorldMapGenerator.Generating || WorldMap.WorldMapScenario.AfterLoadPending) return;
        float now = Time.realtimeSinceStartup;
        if (now < _retryAt) return;
        try
        {
            if (!_built)
            {
                // 旧档的世界地图：大漠腹地的沙地先改成荒漠(沙地只算沙滩，见 WorldMap.EarthBeaches)
                if (WorldMap.WorldMapWilderness.OnEarth)
                {
                    WorldMap.WorldMapWilderness.RepairRiverSurfaces();
                    WorldMap.EarthBeaches.Migrate();
                }
                using (FrameProfiler.Measure("无人区·划区")) Build(true, true);
                _claimsLocked = false;
                return;
            }
            // 城市边界固定开着：剧本里刷给城、已划成无人区的地块还给那座城(读档、打开法则各一次，见 RestoreClaims)
            if (RestoreClaimsDue())
            {
                using (FrameProfiler.Measure("无人区·还刷给城的地")) RestoreClaims();
                return;
            }
            if (_pending && (now - _lastDirtyAt >= DebounceSeconds || now - _firstDirtyAt >= MaxWaitSeconds))
            {
                using (FrameProfiler.Measure("无人区·重划")) Build(false, _regroup);
                return;
            }
            if (!WorldMap.WorldMapWilderness.FixedLayout &&
                (RuralSettlementSystem.Revision != _settlementRevision || FactorySystem.Revision != _factoryRevision) && YearPassed())
            {
                using (FrameProfiler.Measure("无人区·重划")) Build(false, true);
                return;
            }
            using (FrameProfiler.Measure("无人区·城地碎块")) CityFragmentSystem.Tick();
            if (now >= _nextRefresh)
            {
                _nextRefresh = now + RefreshSeconds;
                RefreshTitles();
            }
            // 附近有了文明的无名荒地起名(见 NameAll)
            if (now >= _nameAt)
            {
                _nameAt = now + NameSeconds;
                var used = new HashSet<string>(_regions.Where(region => !string.IsNullOrEmpty(region.Name)).Select(region => region.Name));
                if (NameAll(_regions, used)) Version++;
            }
            // 雨林、大泽的开发(每年一推、每帧改一批格子；出错自己兜着，不连累划区)
            WildernessDevelopmentSystem.Tick();
        }
        catch (Exception exception)
        {
            // 划区失败过一会儿再整个重来
            _built = false;
            _retryAt = now + RetrySeconds;
            LogService.LogWarning($"[EmpireCraft][无人区] 划区失败: {exception}");
        }
    }

    private static bool YearPassed()
    {
        double now = World.world.getCurWorldTime();
        return _builtAt < 0d || now < _builtAt || Date.getYearsSince(_builtAt) >= AnchorYears;
    }

    #endregion

    #region 归类

    // 格子种类的标记(按 TileTypeBase.index_id 缓存，同 TerrainSystem.Marks)
    private const int MarkWater = 1, MarkRock = 2, MarkHills = 4, MarkFrozen = 8, MarkSand = 16, MarkJungle = 32, MarkSwamp = 64,
        MarkFarm = 128, MarkPaved = 256, MarkLava = 512, MarkPlateau = 1024;
    private static int[] _marks = new int[0];
    private static TileTypeBase[] _markTypes = new TileTypeBase[0];

    private static int Marks(TileTypeBase type)
    {
        int index = type.index_id;
        if (index < 0) return ComputeMarks(type);
        if (index >= _marks.Length)
        {
            int size = Math.Max(index + 1, 256);
            Array.Resize(ref _marks, size);
            Array.Resize(ref _markTypes, size);
        }
        if (ReferenceEquals(_markTypes[index], type)) return _marks[index];
        int marks = ComputeMarks(type);
        _marks[index] = marks;
        _markTypes[index] = type;
        return marks;
    }

    private static int ComputeMarks(TileTypeBase type)
    {
        int marks = 0;
        // 岩浆不算水(同 SmoothBorders.IsWater、LandKinds)：另记，归熔岩(以前同水面一起算，一片岩浆判不成无人区)。
        // 原版的岩浆格也算液体(各处判水面都写 liquid && !lava，见 SmoothBorders.IsWater)，先看 lava
        if (type.lava) marks |= MarkLava;
        else if (type.liquid) marks |= MarkWater;
        // 高寒高原(地面格)；黄土高原不算荒地
        if (PlateauTerrain.Alpine != null && ReferenceEquals(type, PlateauTerrain.Alpine)) marks |= MarkPlateau;
        if (type.mountains || type.summit) marks |= MarkRock;
        string id = type.id ?? "";
        if (id == "hills" || type.rocks) marks |= MarkHills;
        if (type.farm_field) marks |= MarkFarm;
        if (type.road || type.wall) marks |= MarkPaved;
        // 冻土雪地：永冻土生物群系、雪地雪丘、冻住的格(同 TerrainSystem 的雪原)
        if (type.biome_id == "biome_permafrost" || id.StartsWith("snow", StringComparison.Ordinal) ||
            id.StartsWith("frozen", StringComparison.Ordinal)) marks |= MarkFrozen;
        else
            switch (type.biome_id)
            {
                // 只认荒漠(biome_desert 的地表)：沙地(biome_sand，没有地表)是海边湖边的沙滩，不是沙漠(用户 2026-11-02 "沙滩要区分一下
                // 不然都没法建港口了""沙滩不是沙漠")——以前算进去，海岸一条沙滩连着荒漠划成无人区，沿海的城没了海岸、建不了港口。
                // 世界地图以前把大漠腹地画成成片沙地，已改成荒漠(见 Tools/WorldMap/build_earth.py 的 beaches)
                case "biome_desert": marks |= MarkSand; break;
                case "biome_jungle": marks |= MarkJungle; break;
                case "biome_swamp": marks |= MarkSwamp; break;
            }
        return marks;
    }

    // 一格的地形(平滑国界下无人区按格贴着地形画，见 UI.Components.WildernessShape)：口径同 Classify 的格子标记——
    // 水面、开过的田什么都不算；岩浆格为熔岩(不算山地、不看地面)；路、城墙看底下的地面；rock 山地山巅，hills 丘陵石地(不是山地的)；
    // 返回地面是哪一类荒地：岩浆为熔岩，冻土雪地为冻原，沙地荒漠为沙漠，丛林为雨林，沼泽为大泽，别的为 None(雪山既是 rock 又是冻原)
    public static WildKind GroundKind(WorldTile tile, out bool rock, out bool hills)
    {
        rock = hills = false;
        TileTypeBase type = tile?.Type;
        if (type == null) return WildKind.None;
        int marks = Marks(type);
        TileTypeBase main = tile.main_type;
        int under = main != null ? Marks(main) : 0;
        if (((marks | under) & MarkWater) != 0 || (marks & MarkFarm) != 0) return WildKind.None;
        // 以前岩浆同水面一样什么都不算，熔岩、火山口那几格平滑国界下涂不上
        if (((marks | under) & MarkLava) != 0) return WildKind.Lava;
        rock = ((marks | under) & MarkRock) != 0;
        hills = !rock && ((marks | under) & MarkHills) != 0;
        // 高寒高原格冻上了也是高原
        if (!rock && (under & MarkPlateau) != 0) return WildKind.Plateau;
        int ground = (marks & MarkPaved) != 0 && main != null ? under : marks;
        if ((ground & MarkFrozen) != 0) return WildKind.Tundra;
        if ((ground & MarkSand) != 0) return WildKind.Desert;
        if ((ground & MarkJungle) != 0) return WildKind.Jungle;
        return (ground & MarkSwamp) != 0 ? WildKind.Swamp : WildKind.None;
    }

    // 剧本开局铺辖地(WorldMapZones)用：这块地按地形会不会是无人区、哪一类——两边同一套门槛，开局留空的地进世界后正好划成无人区，
    // 不会留下既不归城、也不是无人区的空地
    internal static WildKind ClassifyFresh(TileZone zone) => Classify(zone, WildKind.None, scenario: true);

    // 这块地按地形算哪一类无人区(门槛见类头的常量)；previous 是上回判的，已是那一类的门槛降一成
    private struct Counts
    {
        public int Total, Water, Rock, Hills, Frozen, Sand, Jungle, Swamp, Lava;
        // 高寒高原格；山地、丘陵、冻土雪地里的格(每格只记一次，高原无人区的门槛用)
        public int Plateau, Harsh;
        // 陆地连同岩浆格
        public int Land => Total - Water;
    }

    // 一块地各类格子数(水、岩浆、山地山巅、丘陵石地、冻土雪地、荒漠(沙滩不算)、丛林、沼泽)
    private static Counts Count(TileZone zone)
    {
        int total = 0, water = 0, rock = 0, hills = 0, frozen = 0, sand = 0, jungle = 0, swamp = 0, lava = 0, plateau = 0, harsh = 0;
        foreach (WorldTile tile in zone.tiles)
        {
            TileTypeBase type = tile?.Type;
            if (type == null) continue;
            total++;
            int marks = Marks(type);
            TileTypeBase main = tile.main_type;
            int under = main != null ? Marks(main) : 0;
            // 冻住的湖(冰面盖在水上)也算水
            if (((marks | under) & MarkWater) != 0)
            {
                water++;
                continue;
            }
            // 岩浆算陆地、另记(不算山地，不看地面)
            if (((marks | under) & MarkLava) != 0)
            {
                lava++;
                continue;
            }
            // 开过的田不算荒地
            if ((marks & MarkFarm) != 0) continue;
            bool rocky = ((marks | under) & MarkRock) != 0, hilly = !rocky && ((marks | under) & MarkHills) != 0;
            if (rocky) rock++;
            else if (hilly) hills++;
            // 高寒高原格(冻上了也是高原)
            if (!rocky && (under & MarkPlateau) != 0)
            {
                plateau++;
                continue;
            }
            // 路、城墙铺在上面的看底下的地面
            int ground = (marks & MarkPaved) != 0 && main != null ? under : marks;
            if (rocky || hilly || (ground & MarkFrozen) != 0) harsh++;
            if ((ground & MarkFrozen) != 0) frozen++;
            else if ((ground & MarkSand) != 0) sand++;
            else if ((ground & MarkJungle) != 0) jungle++;
            else if ((ground & MarkSwamp) != 0) swamp++;
        }
        return new Counts
        {
            Total = total, Water = water, Rock = rock, Hills = hills, Frozen = frozen, Sand = sand, Jungle = jungle, Swamp = swamp,
            Lava = lava, Plateau = plateau, Harsh = harsh
        };
    }

    private static WildKind Classify(TileZone zone, WildKind previous, bool scenario = false)
    {
        if (zone?.tiles == null) return WildKind.None;
        Counts c = Count(zone);
        int total = c.Total, water = c.Water, rock = c.Rock, hills = c.Hills, frozen = c.Frozen, sand = c.Sand,
            jungle = c.Jungle, swamp = c.Swamp, lava = c.Lava;
        // 雨林全开发：丛林格当普通的地(见 JungleDeveloped)，沼泽够门槛的照旧是大泽
        if (!scenario && JungleDeveloped) jungle = 0;
        int land = total - water;
        if (land < MinLand || water > total * MaxWater) return WildKind.None;
        float Need(bool was, float share) => land * (was ? share - Hysteresis : share);
        // 熔岩先判：熔岩原(岩浆三成)、火山(岩浆一成五且连山地六成)；已是熔岩的门槛同样降一成，岩浆冷却一半还留着
        bool wasLava = previous == WildKind.Lava;
        if (lava >= Need(wasLava, LavaCore) || lava >= Need(wasLava, LavaFlank) && lava + rock >= Need(wasLava, LavaRugged))
            return WildKind.Lava;
        // 够不上熔岩的零星岩浆格算山地(见类头)
        bool wasMountain = previous == WildKind.Mountain;
        int peak = rock + lava;
        if (peak >= Need(wasMountain, MountainCore) ||
            peak >= Need(wasMountain, MountainFlank) && peak + hills >= Need(wasMountain, MountainRugged))
            return WildKind.Mountain;
        // 高原：高寒高原格三成五以上，连同雪山、丘陵、冻土够荒地的门槛(青藏的高原面夹着雪山)
        bool wasPlateau = previous == WildKind.Plateau;
        if (c.Plateau >= Need(wasPlateau, PlateauCore) && c.Plateau + c.Harsh >= Need(wasPlateau, BarrenShare)) return WildKind.Plateau;
        if (frozen + sand >= Need(previous is WildKind.Desert or WildKind.Tundra, BarrenShare))
            return frozen > sand ? WildKind.Tundra : WildKind.Desert;
        bool wet = previous is WildKind.Jungle or WildKind.Swamp;
        if (swamp >= Need(wet, SwampShare)) return WildKind.Swamp;
        if (jungle + swamp >= Need(wet, JungleShare)) return jungle >= swamp ? WildKind.Jungle : WildKind.Swamp;
        return WildKind.None;
    }

    private static bool Reclassify(TileZone zone)
    {
        if (zone == null || zone.id < 0 || zone.id >= _kind.Length) return false;
        WildKind previous = (WildKind)_kind[zone.id];
        byte kind = (byte)Classify(zone, previous);
        // 开发中的雨林大泽(见 WildernessDevelopmentSystem)：格子一批批改成草地，按地形早够不上门槛，没开满不掉出无人区——
        // 照原来的类型(没判过的按开发时记的)留着；已归了城的(那一片小得不成无人区后城扩进去的)不留，免得又从城里划出来
        WildKind held = kind == 0 && zone.city == null ? WildernessDevelopmentSystem.HeldKind(zone.id) : WildKind.None;
        if (held != WildKind.None) kind = (byte)(previous is WildKind.Jungle or WildKind.Swamp ? previous : held);
        // 旧档里开发到一半的雨林：开关开着也算开发完了
        if (kind == (byte)WildKind.Jungle && JungleDeveloped) kind = 0;
        if (kind == _kind[zone.id]) return false;
        _kind[zone.id] = kind;
        return true;
    }

    #endregion

    #region 划区

    // reclassifyAll：全图重判(读档、换世界、开关打开)，否则只重判改过格子的地块；force：类型没变也重新连片(村镇厂区变了、地块放行给了城)
    private static void Build(bool reclassifyAll, bool force)
    {
        var watch = global::System.Diagnostics.Stopwatch.StartNew();
        List<TileZone> all = World.world.zone_calculator.zones;
        int size = all.Count;
        foreach (TileZone zone in all)
            if (zone != null && zone.id >= size) size = zone.id + 1;
        if (_zones.Length != size)
        {
            _zones = new TileZone[size];
            _kind = new byte[size];
            _regionOf = new int[0];
            _titles = new KingdomTitle[0];
            _painted = new bool[0];
            _far = new bool[0];
        _cutOff = new bool[0];
            _neutralZones = new bool[0];
            _parents = new City[0];
            _carved = new City[0];
            reclassifyAll = force = true;
        }
        foreach (TileZone zone in all)
            if (zone != null && zone.id >= 0) _zones[zone.id] = zone;
        bool fromSave = !_built && _saved != null;
        // 读档(或开关重开)：存档里的类型当作上回判的(门槛留余量)，边上的地块不至于一读档就跳出去
        if (fromSave)
            foreach (WildRegionData data in _saved)
            foreach (int id in data.zones)
                if (id >= 0 && id < size) _kind[id] = (byte)data.kind;
        bool changed = false;
        if (reclassifyAll)
            for (int id = 0; id < size; id++) changed |= Reclassify(_zones[id]);
        else
            foreach (int id in Dirty)
                if (id >= 0 && id < size) changed |= Reclassify(_zones[id]);
        var terrainChanges = new HashSet<int>(Dirty);
        Dirty.Clear();
        bool regroup = _regroup;
        _regroup = _pending = false;
        _built = true;
        _builtAt = World.world.getCurWorldTime();
        _settlementRevision = RuralSettlementSystem.Revision;
        _factoryRevision = FactorySystem.Revision;
        // 预设剧本照史实固定分区：改过格子的地块真会进出区域才重划(见 PresetAffected)，单是类型标记变了不算
        bool preset = WorldMap.WorldMapWilderness.FixedLayout && _regions.Count > 0;
        if (!force && !regroup && (preset ? terrainChanges.Count == 0 || !PresetAffected(terrainChanges) : !changed)) return;
        int carved = Regroup(fromSave, terrainChanges);
        _saved = null;
        Version++;
        Builds++;
        LogService.LogInfo($"[EmpireCraft][无人区] 划了 {_regions.Count} 片 {_regions.Sum(region => region.Zones.Count)} 块，" +
                           $"从城里划出 {carved} 块，用时 {watch.Elapsed.TotalMilliseconds:0} ms");
        // 划完看城地碎块：陷在别国地里的小块城地交给四周的城(见 CityFragmentSystem)
        CityFragmentSystem.AfterBuild();
    }

    // 预设剧本(区域照史实固定)里改了地形的地块会不会让区域变(同 Regroup 的 retain 与 PresetGroups)：片里的地块够不上原来的类型就退出；
    // 片外的够得上无人区、凑够 MinZones 块才可能新添一片。都不会就不重划——草木蔓生、林火之类时时改格子，以前停手十秒就整图重划一次
    // (世界地图上一次七八十到上百毫秒，偶尔七百多，进图后隔几秒卡一下)
    private static bool PresetAffected(HashSet<int> terrainChanges)
    {
        int fresh = 0;
        foreach (int id in terrainChanges)
        {
            if (id < 0 || id >= _zones.Length || _zones[id] == null) continue;
            int index = id < _regionOf.Length ? _regionOf[id] : -1;
            if (index >= 0 && index < _regions.Count)
            {
                if (!KeepsPresetKind(_zones[id], _regions[index].Kind)) return true;
            }
            else if (id < _kind.Length && _kind[id] != 0 && ++fresh >= MinZones) return true;
        }
        return false;
    }

    private sealed class OldRegion
    {
        public int Id;
        public WildKind Kind;
        public string Name, Culture;
        public bool CustomName;
    }

    // 外沿放宽的门槛(见类头的常量)：山脉六成、熔岩一成五(只看岩浆格)，别的五成
    private static float EdgeOf(WildKind kind) =>
        kind == WildKind.Mountain ? MountainEdgeShare : kind == WildKind.Lava ? LavaEdgeShare : EdgeShare;

    // 往外长的一片(kind)长到别的核心(other，count 块，还没长过)时整片并不并进来：同类的并；山脉与熔岩互收对方零星的核心
    // (不到 MinZones 块)——山里一两块的火山口按岩浆判成熔岩、山腰几块光秃的山判成山脉，自成一片又因太小被去掉，
    // 会在山脉、熔岩地中间留下窟窿(以前岩浆当水，山里的火山口多半照样归山脉)；成片的各归各的。剧本编辑器的预演同用(ScenarioWilderness)
    internal static bool Absorbs(WildKind kind, WildKind other, int count) =>
        other == kind || count < MinZones &&
        (kind == WildKind.Mountain && other == WildKind.Lava || kind == WildKind.Lava && other == WildKind.Mountain);

    // 连片、对上旧区域(沿用编号名字)、起名、收地、挂法理；返回从城里划出的块数
    private static int Regroup(bool fromSave, HashSet<int> terrainChanges = null)
    {
        int size = _zones.Length;
        // 旧区域：读档后头一回按存档，平时按现有的；各块原先属哪片、挂哪个法理
        var olds = new List<OldRegion>();
        var oldOf = new int[size];
        var oldTitles = new KingdomTitle[size];
        var oldParents = new City[size];
        var oldPainted = new bool[size];
        var oldNeutral = new bool[size];
        // 各块原先记的划出来源(见 _carved)；旧档没记过的(见 CarveRule)
        var oldCarved = new City[size];
        var legacy = new bool[size];
        for (int i = 0; i < size; i++) oldOf[i] = -1;
        if (fromSave)
        {
            _releasePending = false;
            foreach (WildRegionData data in _saved)
            {
                int index = olds.Count;
                olds.Add(new OldRegion { Id = data.id, Kind = (WildKind)data.kind, Name = data.name, Culture = data.culture,
                    CustomName = data.custom_name && !string.IsNullOrEmpty(data.name) });
                var neutral = data.neutralZones == null ? null : new HashSet<int>(data.neutralZones);
                for (int k = 0; k < data.zones.Count; k++)
                {
                    int id = data.zones[k];
                    if (id < 0 || id >= size) continue;
                    oldOf[id] = index;
                    oldNeutral[id] = data.neutral || neutral?.Contains(id) == true;
                    long titleId = k < data.titles.Count ? data.titles[k] : -1L;
                    KingdomTitle title = titleId >= 0 ? ModClass.KINGDOM_TITLE_MANAGER?.get(titleId) : null;
                    oldTitles[id] = title == null || title.isRekt() ? null : title;
                    long originId = data.origins != null && k < data.origins.Count ? data.origins[k] : -1L;
                    oldParents[id] = originId >= 0 ? World.world.cities.get(originId) : null;
                    long carvedId = data.carved != null && k < data.carved.Count ? data.carved[k] : -1L;
                    oldCarved[id] = carvedId >= 0 ? World.world.cities.get(carvedId) : null;
                    legacy[id] = data.carve < CarveRule;
                    WorldMap.WorldMapWilderness.RememberTerritory(_zones[id], oldTitles[id], oldParents[id], oldNeutral[id],
                        data.painted?.Contains(id) == true || WorldMap.WorldMapWilderness.PaintedTitle(_zones[id]) != null);
                }
                if (data.painted != null)
                    foreach (int id in data.painted)
                        if (id >= 0 && id < size) oldPainted[id] = true;
            }
        }
        else
        {
            for (int index = 0; index < _regions.Count; index++)
            {
                WildRegion region = _regions[index];
                olds.Add(new OldRegion { Id = region.Id, Kind = region.Kind, Name = region.Name, Culture = region.Culture,
                    CustomName = region.CustomName });
                foreach (int id in region.Zones)
                {
                    if (id < 0 || id >= size) continue;
                    oldOf[id] = index;
                    oldNeutral[id] = IsNeutral(id);
                    oldParents[id] = OriginOf(_zones[id]);
                    if (id < _titles.Length) oldTitles[id] = _titles[id];
                    if (id < _carved.Length) oldCarved[id] = _carved[id];
                    WorldMap.WorldMapWilderness.RememberTerritory(_zones[id], oldTitles[id], oldParents[id], oldNeutral[id],
                        IsPainted(_zones[id]) || WorldMap.WorldMapWilderness.PaintedTitle(_zones[id]) != null);
                    if (id < _painted.Length) oldPainted[id] = _painted[id];
                }
            }
        }

        // 留给城的地块：城心那块、有村镇(含废墟、关城)的、厂区占着的、开发满了的；古战争兼容模组的城整座不动
        var anchored = new bool[size];
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            if (AncientWarfareCompatibility.OwnsObject(city))
            {
                foreach (TileZone zone in city.zones)
                    if (zone != null && zone.id >= 0 && zone.id < size) anchored[zone.id] = true;
                continue;
            }
            TileZone seat = city.getTile()?.zone;
            if (seat != null && seat.id >= 0 && seat.id < size) anchored[seat.id] = true;
        }
        foreach (int id in RuralSettlementSystem.SettledZones())
            if (id >= 0 && id < size) anchored[id] = true;
        foreach (int id in FactorySystem.CoveredZones())
            if (id >= 0 && id < size) anchored[id] = true;
        // 开发满了的雨林大泽(见 WildernessDevelopmentSystem)：退出无人区，草木再蔓生回来也不再划进来
        foreach (int id in WildernessDevelopmentSystem.DevelopedZones())
            if (id >= 0 && id < size) anchored[id] = true;
        // 照原图分地的剧本(从当前世界导入的)里归城的地块：城还领着就留给城(见 WorldMap.WorldMapWilderness.Keep)
        foreach (int id in WorldMap.WorldMapWilderness.KeptZones)
            if (id >= 0 && id < size && _zones[id]?.city != null) anchored[id] = true;
        // 城市边界固定：城领着的地不划走；预设剧本开局定好要转成法理无人区的州府锚点除外(见 WorldMapWilderness.SetAnchors)。
        // 玩家在剧本编辑器里刷给城的(本剧本开无人区，见 WorldMapWilderness.CityClaim)也锁定、不划走(见 RestoreClaims)
        if (CityTerritoryProtection.BordersFixed)
            for (int id = 0; id < size && id < _zones.Length; id++)
                if (_zones[id]?.city != null && (!WorldMap.WorldMapWilderness.BoundedAnchor(_zones[id]) ||
                                                 WorldMap.WorldMapWilderness.CityClaim(_zones[id]))) anchored[id] = true;

        // 连片：同类型四邻相连
        var piece = new int[size];
        for (int i = 0; i < size; i++) piece[i] = -1;
        var groups = new List<(List<int> zones, WildKind kind)>();
        var queue = new Queue<int>();
        for (int start = 0; start < size; start++)
        {
            if (piece[start] >= 0 || _kind[start] == 0 || anchored[start] || _zones[start] == null) continue;
            byte kind = _kind[start];
            var zones = new List<int>();
            int mark = groups.Count;
            piece[start] = mark;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                zones.Add(id);
                TileZone[] neighbours = _zones[id].neighbours;
                if (neighbours == null) continue;
                foreach (TileZone next in neighbours)
                {
                    if (next == null || next.id < 0 || next.id >= size) continue;
                    int nid = next.id;
                    if (piece[nid] >= 0 || _kind[nid] != kind || anchored[nid]) continue;
                    piece[nid] = mark;
                    queue.Enqueue(nid);
                }
            }
            groups.Add((zones, (WildKind)kind));
        }

        // 外沿放宽：大的先长，同一块地只归先长到的那片；长到同类、还没长过的小片就把它整片并进来(核心之间隔着外沿的)，
        // 山脉、熔岩长到对方零星的核心也并进来(见 Absorbs)；长完再按 MinZones 去掉太小的
        var counted = new Dictionary<int, Counts>();
        var grown = new bool[groups.Count];
        foreach (int g in Enumerable.Range(0, groups.Count).OrderByDescending(index => groups[index].zones.Count).ToList())
        {
            if (groups[g].zones.Count == 0) continue;
            grown[g] = true;
            WildKind kind = groups[g].kind;
            List<int> zones = groups[g].zones;
            queue.Clear();
            foreach (int id in zones) queue.Enqueue(id);
            while (queue.Count > 0)
            {
                TileZone[] neighbours = _zones[queue.Dequeue()].neighbours;
                if (neighbours == null) continue;
                foreach (TileZone next in neighbours)
                {
                    if (next?.tiles == null || next.id < 0 || next.id >= size) continue;
                    int nid = next.id;
                    int other = piece[nid];
                    if (other >= 0)
                    {
                        if (other == g || grown[other] || !Absorbs(kind, groups[other].kind, groups[other].zones.Count)) continue;
                        foreach (int id in groups[other].zones)
                        {
                            piece[id] = g;
                            zones.Add(id);
                            queue.Enqueue(id);
                        }
                        groups[other].zones.Clear();
                        grown[other] = true;
                        continue;
                    }
                    if (anchored[nid]) continue;
                    if (!counted.TryGetValue(nid, out Counts c))
                    {
                        c = Count(next);
                        counted[nid] = c;
                    }
                    if (c.Land < MinLand) continue;
                    int share = kind switch
                    {
                        // 山脉连同零星的岩浆格(同 Classify：够不上熔岩的岩浆算山地)
                        WildKind.Mountain => c.Rock + c.Hills + c.Lava,
                        WildKind.Desert or WildKind.Tundra => c.Sand + c.Frozen,
                        WildKind.Lava => c.Lava,
                        _ => c.Jungle + c.Swamp
                    };
                    bool was = oldOf[nid] >= 0 && olds[oldOf[nid]].Kind == kind;
                    float edge = EdgeOf(kind);
                    if (share < c.Land * (was ? edge - Hysteresis : edge)) continue;
                    piece[nid] = g;
                    zones.Add(nid);
                    queue.Enqueue(nid);
                }
            }
        }

        // 预设里的地理分区以已保存的成员关系为准。普通刷新和读档不重新切片；
        // 营建／开发只退出占用的块，实际改地形只影响变过的块，不牵动其他山脉、大漠。
        bool fixedLayout = WorldMap.WorldMapWilderness.FixedLayout && olds.Count > 0;
        if (fixedLayout)
        {
            var retain = new bool[size];
            for (int id = 0; id < size; id++)
                retain[id] = _zones[id] != null && !anchored[id] &&
                    (terrainChanges?.Contains(id) != true || oldOf[id] >= 0 && KeepsPresetKind(_zones[id], olds[oldOf[id]].Kind));
            groups = PresetGroups(groups, olds, oldOf, retain, terrainChanges);
        }

        // 对旧区域：重叠最多的先配，一片旧区域只配一片(分成几片的大的那片沿用，并起来的沿用重叠多的那片)
        var overlap = new Dictionary<long, int>();
        var kept = new List<int>();
        for (int g = 0; g < groups.Count; g++)
        {
            if (groups[g].zones.Count < MinZones && !(fixedLayout && groups[g].zones.Any(id => oldOf[id] >= 0))) continue;
            kept.Add(g);
            foreach (int id in groups[g].zones)
            {
                int old = oldOf[id];
                if (old < 0 || olds[old].Kind != groups[g].kind) continue;
                long key = (long)g << 32 | (uint)old;
                overlap.TryGetValue(key, out int count);
                overlap[key] = count + 1;
            }
        }
        var matchOf = new Dictionary<int, int>();
        var taken = new HashSet<int>();
        foreach (KeyValuePair<long, int> pair in overlap.OrderByDescending(pair => pair.Value)
                     .ThenBy(pair => olds[(int)(pair.Key & 0xffffffffL)].Id).ThenBy(pair => pair.Key >> 32))
        {
            int g = (int)(pair.Key >> 32), old = (int)(pair.Key & 0xffffffffL);
            if (matchOf.ContainsKey(g) || taken.Contains(old)) continue;
            matchOf[g] = old;
            taken.Add(old);
        }

        var regions = new List<WildRegion>();
        var used = new HashSet<string>();
        foreach (int g in kept)
        {
            var region = new WildRegion { Kind = groups[g].kind };
            if (matchOf.TryGetValue(g, out int old))
            {
                region.Id = olds[old].Id;
                region.Name = olds[old].Name;
                region.Culture = olds[old].Culture;
                region.CustomName = olds[old].CustomName;
                if (!string.IsNullOrEmpty(region.Name)) used.Add(region.Name);
            }
            else region.Id = _nextId++;
            List<int> zones = groups[g].zones;
            zones.Sort();
            region.Zones.AddRange(zones);
            regions.Add(region);
        }
        regions.Sort((a, b) => a.Id.CompareTo(b.Id));

        // 收地：划进无人区的地块被城占着就从城里划出来(城心、村镇厂区所在的早已留给城，见 anchored)
        var touched = new HashSet<City>();
        int carved = 0;
        // 这回从哪座城划出来的(关掉设置时还给它，见 _carved)
        var carvedNow = new City[size];
        foreach (WildRegion region in regions)
        {
            region.Zones.RemoveAll(id =>
            {
                TileZone zone = _zones[id];
                City city = zone.city;
                if (city == null) return false;
                if (city.data == null) return true;
                // 被敌军占着的：占领记在原城名下，划出来就对不上，先清掉(同村镇改隶，见 RuralSettlementSystem.Assign)
                if (city.GetTileZoneOccupier(zone) != null) city.RemoveOccupiedTileZoneFromAll(zone);
                using (GamePatches.CityExpansionPatch.Manual()) city.removeZone(zone);
                // 划不出来(那座城只剩这一块、受保护)的留给城
                if (zone.city != null) return true;
                touched.Add(city);
                carvedNow[id] = city;
                carved++;
                return false;
            });
        }
        regions.RemoveAll(region => region.Zones.Count == 0);
        foreach (WildRegion region in regions) region.Neutral = region.Zones.All(id => oldNeutral[id]);
        var regionOf = new int[size];
        for (int i = 0; i < size; i++) regionOf[i] = -1;
        for (int index = 0; index < regions.Count; index++)
            foreach (int id in regions[index].Zones) regionOf[id] = index;
        if (touched.Count > 0)
        {
            CityDistricts.Reset();
            foreach (City city in touched) PureMapPatch.Redraw(city);
            World.world.zone_calculator?.setDrawnZonesDirty();
        }

        _regions = regions;
        _regionOf = regionOf;
        _neutralZones = oldNeutral;
        IdIndex.Clear();
        foreach (WildRegion region in regions) IdIndex[region.Id] = region;
        Shape(regions, regionOf);

        // 起名(见 NameAll)
        NameAll(regions, used);

        // 挂法理：沿用原先挂的与承载城市；变化时核对，剧本中的有效辖境不重分。
        var titles = new KingdomTitle[size];
        var painted = new bool[size];
        bool missing = false;
        foreach (WildRegion region in regions)
            foreach (int id in region.Zones)
            {
                KingdomTitle title = oldTitles[id];
                if (title != null && !title.isRekt())
                {
                    titles[id] = title;
                    painted[id] = oldPainted[id];
                }
                else missing = true;
            }
        _titles = titles;
        _parents = oldParents;
        _painted = painted;
        long signature = TitleSignature();
        // 旧档按旧规矩分的(直边、不顺地形)：读档后重分一次(用户 2026-11-10 "自然的地形占领也好直""而且也没顺着地形走")
        bool outdated = fromSave && _ruleOutdated && !WorldMap.WorldMapWilderness.OnEarth;
        _ruleOutdated = false;
        if (outdated) LogService.LogInfo("[EmpireCraft][无人区] 旧档的无人区按顺着地形的新规矩重分一次");
        if (!fromSave && signature != _titleSignature || outdated) Attach(true);
        else if (missing || _attachPending) Attach(false);
        _attachPending = false;
        _titleSignature = signature;
        // 划出来源(见 CarvedSource)：设置管不着的图(预设剧本、本剧本开无人区)关不掉，不记
        var carvedFrom = new City[size];
        if (!WorldMap.WorldMapWilderness.ForcesWild)
            foreach (WildRegion region in regions)
                foreach (int id in region.Zones)
                {
                    City before = oldCarved[id];
                    carvedFrom[id] = CarvedSource(carvedNow[id], before, before?.data != null && !before.isRekt(), legacy[id],
                        IsNeutral(id), legacy[id] && carvedNow[id] == null ? OriginOf(_zones[id]) : null);
                }
        _carved = carvedFrom;
        RefreshOwners();
        return carved;
    }

    // 不重新把旧区分成几个几何连通分量：城镇把一片山脉隔开，地名与编号仍是同一片山脉。
    private static List<(List<int> zones, WildKind kind)> PresetGroups(List<(List<int> zones, WildKind kind)> inferred,
        List<OldRegion> olds, int[] oldOf, bool[] retain, HashSet<int> terrainChanges)
    {
        var result = new List<(List<int> zones, WildKind kind)>();
        var members = olds.Select(_ => new List<int>()).ToArray();
        var assigned = new bool[oldOf.Length];
        for (int id = 0; id < oldOf.Length; id++)
        {
            int old = oldOf[id];
            if (old < 0 || !retain[id]) continue;
            members[old].Add(id);
            assigned[id] = true;
        }
        for (int old = 0; old < olds.Count; old++)
            if (members[old].Count > 0) result.Add((members[old], olds[old].Kind));
        // 新地形才可新增区域；全图重采样推出来的边缘不能趁读档、建厂时自行长出去。
        foreach ((List<int> zones, WildKind kind) in inferred)
        {
            List<int> added = zones.Where(id => !assigned[id] && terrainChanges?.Contains(id) == true &&
                (oldOf[id] < 0 || !retain[id])).ToList();
            if (added.Count < MinZones) continue;
            result.Add((added, kind));
            foreach (int id in added) assigned[id] = true;
        }
        return result;
    }

    private static bool KeepsPresetKind(TileZone zone, WildKind kind)
    {
        Counts c = Count(zone);
        if (c.Land < MinLand || c.Water > c.Total * MaxWater) return false;
        // 固定地理身份不能把新喷发的岩浆继续当成普通山地。
        if (kind != WildKind.Lava && (c.Lava >= c.Land * LavaCore ||
            c.Lava >= c.Land * LavaFlank && c.Lava + c.Rock >= c.Land * LavaRugged)) return false;
        int share = kind switch
        {
            WildKind.Mountain => c.Rock + c.Hills + c.Lava,
            WildKind.Desert or WildKind.Tundra => c.Sand + c.Frozen,
            WildKind.Lava => c.Lava,
            _ => c.Jungle + c.Swamp
        };
        return share >= c.Land * (EdgeOf(kind) - Hysteresis);
    }

    // 标名字的块(区域里离边最远的一块，一样远的取离各块平均位置最近的)、外框、底色
    private static void Shape(List<WildRegion> regions, int[] regionOf)
    {
        int size = _zones.Length;
        var depth = new int[size];
        for (int i = 0; i < size; i++) depth[i] = -1;
        var queue = new Queue<int>();
        for (int index = 0; index < regions.Count; index++)
        {
            WildRegion region = regions[index];
            region.Color = ColorOf(region.Kind, region.Id);
            region.MinX = region.MinY = int.MaxValue;
            region.MaxX = region.MaxY = int.MinValue;
            float cx = 0f, cy = 0f;
            foreach (int id in region.Zones)
            {
                TileZone zone = _zones[id];
                region.MinX = Math.Min(region.MinX, zone.x);
                region.MinY = Math.Min(region.MinY, zone.y);
                region.MaxX = Math.Max(region.MaxX, zone.x);
                region.MaxY = Math.Max(region.MaxY, zone.y);
                cx += zone.x;
                cy += zone.y;
                bool edge = zone.neighbours == null || zone.neighbours.Length < 4;
                if (!edge)
                    foreach (TileZone next in zone.neighbours)
                        if (next == null || next.id < 0 || next.id >= size || regionOf[next.id] != index)
                        {
                            edge = true;
                            break;
                        }
                if (!edge) continue;
                depth[id] = 0;
                queue.Enqueue(id);
            }
            cx /= region.Zones.Count;
            cy /= region.Zones.Count;
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                TileZone[] neighbours = _zones[id].neighbours;
                if (neighbours == null) continue;
                foreach (TileZone next in neighbours)
                {
                    if (next == null || next.id < 0 || next.id >= size || regionOf[next.id] != index || depth[next.id] >= 0) continue;
                    depth[next.id] = depth[id] + 1;
                    queue.Enqueue(next.id);
                }
            }
            TileZone best = null;
            int bestDepth = -1;
            float bestDistance = float.MaxValue;
            foreach (int id in region.Zones)
            {
                TileZone zone = _zones[id];
                float distance = (zone.x - cx) * (zone.x - cx) + (zone.y - cy) * (zone.y - cy);
                if (depth[id] < bestDepth || depth[id] == bestDepth && distance >= bestDistance) continue;
                best = zone;
                bestDepth = depth[id];
                bestDistance = distance;
            }
            region.Center = best;
        }
    }

    private static Color32 ColorOf(WildKind kind, int id)
    {
        (int r, int g, int b) = kind switch
        {
            WildKind.Mountain => (128, 112, 96),
            WildKind.Desert => (214, 190, 132),
            WildKind.Tundra => (200, 214, 224),
            WildKind.Jungle => (70, 112, 66),
            WildKind.Swamp => (104, 116, 82),
            // 熔岩暗红(偏褐，同山脉一样压得住地形浮雕；纹样另用亮橙，见 WildernessOverlay)
            WildKind.Lava => (156, 66, 44),
            // 高原：高寒草甸的灰黄
            WildKind.Plateau => (162, 152, 112),
            _ => (160, 160, 160)
        };
        uint hash = unchecked((uint)id * 2654435761u);
        int shade = (int)(hash % 25u) - 12;
        int tint = (int)(hash / 25u % 9u) - 4;
        byte Clamp(int value) => (byte)Mathf.Clamp(value, 0, 255);
        return new Color32(Clamp(r + shade + tint), Clamp(g + shade), Clamp(b + shade - tint), 255);
    }

    #endregion

    #region 起名

    // 各城城心与主流文化；全图最常见的文化(没有城为 null)
    private static List<(int x, int y, string culture)> Seats(out string common)
    {
        var seats = new List<(int x, int y, string culture)>();
        var counts = new Dictionary<string, int>();
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            WorldTile seat = city.getTile();
            string culture = CultureService.GetMainCulture(city, initialize: false);
            if (seat == null || string.IsNullOrEmpty(culture)) continue;
            seats.Add((seat.x, seat.y, culture));
            counts.TryGetValue(culture, out int count);
            counts[culture] = count + 1;
        }
        common = counts.Count == 0 ? null
            : counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).First().Key;
        return seats;
    }

    // 起名要有文明(用户 2026-10-29 "这些初始名称应该是没有, 只显示地形类别, 等有文明了才会取名")：区域中心 NameReach 块以内
    // 有城(有主流文化)才按最近那座城的文化起名；没有就不起名，地图上只标类型(山脉、沙漠……，见 DisplayName)，等附近有了城再起
    // (隔 NameSeconds 秒看一次，见 Refresh)。以前没有城时按通用词库先起了名，有了城再按文化改——读档时这样的名一并去掉。
    // 世界地图(真实地球)开局就有文明，真实地名照起(见 WorldMap.WorldMapWilderness)
    private const int NameReach = 48;
    private const float NameSeconds = 10f;
    private static float _nameAt;

    // 标在地图上、提示里的名字：起了名的用名字，还没有文明起名的只写类型
    public static string DisplayName(WildRegion region) =>
        region == null ? "" : !string.IsNullOrEmpty(region.Name) ? region.Name : WildernessNaming.KindName(region.Kind) ?? "";

    // 改名(用户 2026-11-08 "无人区没法自定义命名")：改过的名字随存档、重划时跟着这一片走，附近有没有文明都不再自动改；
    // 留空恢复自动起名(附近有文明的当场按文化重起，没有的只标类型)。名字太长截到 MaxNameLength 字。有变化返回 true
    public const int MaxNameLength = 24;

    public static bool Rename(WildRegion region, string name)
    {
        if (region == null) return false;
        name = (name ?? "").Trim();
        if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
        if (name.Length == 0)
        {
            if (!region.CustomName) return false;
            region.CustomName = false;
            region.Name = null;
            region.Culture = null;
            var used = new HashSet<string>(_regions.Where(other => other != region && !string.IsNullOrEmpty(other.Name))
                .Select(other => other.Name));
            NameAll(new List<WildRegion> { region }, used);
        }
        else
        {
            if (region.CustomName && region.Name == name) return false;
            region.Name = name;
            region.CustomName = true;
        }
        Version++;
        return true;
    }

    // 给没起名(或以前没有文化时按通用词库起的)的各片起名：大的先挑；附近没有文明的不起(名字去掉)。有变化返回 true
    private static bool NameAll(List<WildRegion> regions, HashSet<string> used)
    {
        List<WildRegion> pending = regions.Where(region => !region.CustomName &&
                                                           (string.IsNullOrEmpty(region.Name) || string.IsNullOrEmpty(region.Culture)))
            .OrderByDescending(region => region.Zones.Count).ThenBy(region => region.Id).ToList();
        if (pending.Count == 0) return false;
        bool earth = WorldMap.WorldMapWilderness.OnEarth;
        List<(int x, int y, string culture)> seats = Seats(out string common);
        float reach = earth ? float.MaxValue : NameReach * Config.CITY_ZONE_SIZE;
        bool changed = false;
        foreach (WildRegion region in pending)
        {
            string culture = CultureNear(region, seats, earth ? common : null, reach);
            if (culture == null && !earth)
            {
                if (string.IsNullOrEmpty(region.Name)) continue;
                used.Remove(region.Name);
                region.Name = null;
                region.Culture = null;
                changed = true;
                continue;
            }
            if (!string.IsNullOrEmpty(region.Name))
            {
                // 世界地图上没有文化也照起的真实地名，文化还是没有就不必重起
                if (culture == null) continue;
                used.Remove(region.Name);
            }
            region.Culture = culture;
            region.Name = NameOf(region, used);
            used.Add(region.Name);
            changed = true;
        }
        return changed;
    }

    // 离区域中心最近(reach 格以内)的城的主流文化；没有为 fallback
    private static string CultureNear(WildRegion region, List<(int x, int y, string culture)> seats, string fallback, float reach)
    {
        WorldTile center = region.Center?.centerTile;
        if (center == null || seats.Count == 0) return fallback;
        string best = fallback;
        float bestDistance = reach >= float.MaxValue ? float.MaxValue : reach * reach;
        foreach ((int x, int y, string culture) in seats)
        {
            float distance = (float)(x - center.x) * (x - center.x) + (float)(y - center.y) * (y - center.y);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = culture;
        }
        return best;
    }

    private static string NameOf(WildRegion region, HashSet<string> used)
    {
        string name = null;
        try
        {
            // 世界地图先用真实地名(见 WorldMap.WorldMapWilderness；随机地图、没对上的为 null)
            name = WorldMap.WorldMapWilderness.Name(region, used) ??
                   WildernessNaming.Name(region.Kind, region.Culture ?? "", region.Id, used, region.Zones.Count);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][无人区] 起名失败({region.Kind}, {region.Culture}): {exception.Message}");
        }
        if (!string.IsNullOrEmpty(name)) return name;
        // 起名出错时只标类型(重名的后面加编号)
        name = LM.Get("wilderness_fallback_" + region.Kind.ToString().ToLowerInvariant());
        return used.Contains(name) ? name + region.Id : name;
    }

    #endregion

    #region 法理

    // 城与法理、行政成员关系变没变：每城分别混合再累加，顺序无关。没有法理的城也算(见 Attach)。
    private static long TitleSignature()
    {
        long sum = 1_469_598_103_934_665_603L;
        var nomads = new Dictionary<Kingdom, bool>();
        foreach (City city in World.world.cities)
        {
            if (city?.data == null || city.isRekt()) continue;
            KingdomTitle title = city.GetTitle();
            long titleId = title == null || title.isRekt() ? 0L : title.id;
            // 同法理下改隶省份、脱离帝国也要刷新；按城混合，避免两座城互换法理后总和不变。
            bool nomad = Nomad(city, nomads);
            unchecked
            {
                long entry = (1_469_598_103_934_665_603L ^ city.id) * 1_099_511_628_211L;
                entry = (entry ^ titleId) * 1_099_511_628_211L;
                entry = (entry ^ (city.kingdom?.id ?? -1L)) * 1_099_511_628_211L;
                entry = (entry ^ (city.kingdom?.GetEmpire()?.id ?? -1L)) * 1_099_511_628_211L;
                entry = (entry ^ (nomad ? 1L : 0L)) * 1_099_511_628_211L;
                sum += entry ^ (entry >> 32);
            }
        }
        return sum;
    }

    private static bool Nomad(City city, Dictionary<Kingdom, bool> cache)
    {
        Kingdom kingdom = city?.kingdom;
        if (kingdom == null || kingdom.isRekt() || kingdom.wild) return false;
        if (!cache.TryGetValue(kingdom, out bool nomad)) cache[kingdom] = nomad = FrontierRelations.IsNomadRealm(kingdom);
        return nomad;
    }

    // 归游牧的无人区类型(熔岩不归游牧：游牧也过不去，照最近的城挂)
    private static bool NomadKind(WildKind kind) => kind is WildKind.Desert or WildKind.Tundra;

    // 从各城的地块往外按地块走(八方、不过海、不限远近)，每块无人区挂最先走到它的那座城的法理(那座城没有法理就跟着那座城，
    // 见 OriginOf)；隔海走不到的(没有城的岛)挂直线最近的城的。沙漠、冻原先看游牧国的城走不走得到(见 NomadKind)。
    // 以前至多走 MaxAttachSteps，青藏高原、西伯利亚腹地挂不上任何法理，法理图层上空着(用户 10-30 "我要的是地形被包含进法理图层")。
    // full 核对城市/法理变化；预设剧本即便 full 也保留有效辖境，随机世界照旧重新挂接。
    private static void Attach(bool full)
    {
        // 剧本笔刷可能晚于首次划区加载；先应用明确指定，再走保留旧归属的快速路径。
        WorldMap.WorldMapWilderness.PreparePaint();
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
            {
                if (id < _painted.Length && _painted[id] && _titles[id] != null && !_titles[id].isRekt()) continue;
                // 地区设置是开局默认值，已有来源城市后跟随该城；编辑器笔刷仍可明确覆盖。
                City parent = id < _parents.Length ? _parents[id] : null;
                bool established = parent?.data != null && !parent.isRekt();
                KingdomTitle painted = WorldMap.WorldMapWilderness.PaintedTitle(_zones[id]) ??
                    (established ? null : WorldMap.WorldMapWilderness.FixedTitle(_zones[id]));
                if (painted == null)
                {
                    OriginOf(_zones[id]);
                    continue;
                }
                if (painted == _titles[id]) continue;
                _titles[id] = painted;
                if (id < _parents.Length) _parents[id] = null;
            }
        bool scenario = WorldMap.WorldMapWilderness.OnEarth;
        if (scenario && WorldMap.WorldMapWilderness.NeedsMountainRepair)
        {
            int repaired = RepairOverMountains();
            WorldMap.WorldMapWilderness.MarkMountainRepaired();
            if (repaired > 0) LogService.LogInfo($"[EmpireCraft][无人区] 旧档里隔着山挂上的 {repaired} 块无人区改挂不翻山走得到的城");
        }
        // 剧本里新增一城、别处换法理，不会推翻仍有效的辖境。只修补新地块或已经消失的法理。
        if (scenario && !WorldMap.WorldMapWilderness.NeedsBoundaryRepair && !_regions.Any(region => region.Zones.Any(id =>
                (_titles[id] == null || _titles[id].isRekt()) && !UntitledOrigin(id) && !CutOff(id))))
        {
            foreach (WildRegion region in _regions)
                foreach (int id in region.Zones) OriginOf(_zones[id]);
            return;
        }
        var titleOf = new Dictionary<City, KingdomTitle>();
        _walled = WallZones();
        KingdomTitle[] from = Spread(titleOf, null, float.PositiveInfinity, out bool[] reached, out City[] fromCity);
        KingdomTitle[] nomadFrom = null;
        bool[] nomadReached = null;
        if (_regions.Any(region => NomadKind(region.Kind)))
        {
            var nomads = new Dictionary<Kingdom, bool>();
            nomadFrom = Spread(titleOf, city => Nomad(city, nomads), MaxAttachSteps, out nomadReached, out _);
        }
        var realms = new Dictionary<Kingdom, KingdomTitle[]>();
        var walk = new Nearest(titleOf, from, reached, fromCity);
        if (_parents.Length != _titles.Length) _parents = new City[_titles.Length];
        // 走得到的最近的城与它的法理(隔海走不到的按直线)：连不上所挂法理的地块改挂这个(见 Connect)
        var nearestCity = new City[_titles.Length];
        var nearestTitle = new KingdomTitle[_titles.Length];
        foreach (WildRegion region in _regions)
        {
            bool nomadKind = nomadFrom != null && NomadKind(region.Kind);
            foreach (int id in region.Zones)
            {
                KingdomTitle old = _titles[id];
                bool valid = old != null && !old.isRekt();
                // 权能笔刷划的不动(见 Paint)
                if (valid && (id < _painted.Length && _painted[id] || !full || scenario) || scenario && UntitledOrigin(id)) continue;
                City parent = walk.Of(id, out KingdomTitle nearest);
                nearestCity[id] = parent;
                nearestTitle[id] = nearest;
                // 剧本：先照州府锚点定的城(行政区)，再照时代文件的史实归属，再照游牧、最近的城
                _titles[id] = AnchorTitle(_zones[id]) ?? HistoricalTitle(_zones[id], titleOf, from, fromCity, realms) ??
                              (nomadKind && nomadReached[id] ? nomadFrom[id] : nearest);
                _parents[id] = _titles[id] == null ? parent : null;
                MarkCutOff(id, _titles[id] == null && parent == null);
            }
        }
        Connect(titleOf, nearestCity, nearestTitle);
        WorldMap.WorldMapWilderness.MarkBoundaryRepaired();
    }

    // 不成飞地(用户 10-30 截图狼居胥山一块块挂在萧汗国名下、插在回纥国土中间："出现这种飞地也很不合理")：剧本锚点、史实归属挂的法理，
    // 城后来换了主、分了家，或者隔得远，无人区就成了插在别国国土里的一块块。同城的地一个样，每块地得连得上所挂法理的一座城——
    // 挨着那座城的地块，或经同一法理的无人区地块连过去(长城不过，见 _walled)；连得上的记下跟着哪座城(OriginOf)，
    // 连不上的并给四周连得上的地块或城(用户 2026-11-04 截图一块块翔国插在别国无人区里："这种飞地问题还是没有解决吗")：一圈圈往外并，
    // 并完每块地都连得上所挂法理的城，不会再成飞地；州府锚点、上次跟的城只定跟哪座城，不算连上(以前算，剧本里锚点划出的地全成飞地)。
    // 四周都没有的(隔海的岛)才改挂走得到的最近的城。权能笔刷划的不动
    private static void Connect(Dictionary<City, KingdomTitle> titleOf, City[] nearestCity, KingdomTitle[] nearestTitle)
    {
        int size = _titles.Length;
        var linked = new bool[size];
        var anchored = new bool[size];
        var queue = new Queue<int>();
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
            {
                KingdomTitle title = _titles[id];
                TileZone zone = _zones[id];
                if (title == null || zone?.neighbours_all == null) continue;
                // 权能笔刷按城刷的跟取的那座城(见 Paint)；明确的剧本锚点先于邻城；同一法理下的几个行政区不能被连通扫描互相覆盖。
                City origin = IsPainted(zone) && id < _parents.Length ? _parents[id] : null;
                if (origin?.data == null || origin.isRekt() || origin.GetTitle() != title) origin = WorldMap.WorldMapWilderness.AnchorCity(zone);
                if (origin == null || origin.GetTitle() != title)
                    origin = WorldMap.WorldMapWilderness.OnEarth && id < _parents.Length ? _parents[id] : null;
                if (origin?.data != null && !origin.isRekt() && origin.GetTitle() == title)
                {
                    anchored[id] = true;
                    _parents[id] = origin;
                }
                foreach (TileZone next in zone.neighbours_all)
                {
                    City city = next?.city;
                    if (city?.data == null || city.isRekt() || TitleOfCity(city, titleOf) != title ||
                        anchored[id] && city.kingdom != _parents[id]?.kingdom) continue;
                    linked[id] = true;
                    if (!anchored[id]) _parents[id] = city;
                    queue.Enqueue(id);
                    break;
                }
            }
        bool[] followed = FollowSpread(titleOf, nearestCity, anchored);
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            if (id < _walled.Length && _walled[id]) continue;
            foreach (TileZone next in _zones[id].neighbours_all)
            {
                if (next == null || next.id < 0 || next.id >= size || linked[next.id] || _titles[next.id] != _titles[id] ||
                    next.id >= _regionOf.Length || _regionOf[next.id] < 0 ||
                    anchored[next.id] && _parents[next.id]?.kingdom != _parents[id]?.kingdom) continue;
                linked[next.id] = true;
                if (!anchored[next.id] && !followed[next.id]) _parents[next.id] = _parents[id];
                queue.Enqueue(next.id);
            }
        }
        int moved = Absorb(linked, titleOf);
        Nearest walk = null;
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
            {
                if (_titles[id] == null || linked[id]) continue;
                // 修补别处失效法理时，不把仍有效的历史辖境一并重划。
                if (WorldMap.WorldMapWilderness.OnEarth && !_titles[id].isRekt())
                {
                    _parents[id] = OriginOf(_zones[id]);
                    continue;
                }
                if (id < _painted.Length && _painted[id] || WorldMap.WorldMapWilderness.PaintedTitle(_zones[id]) != null)
                {
                    // 权能笔刷取的城还在这个法理里就跟着它，不和它的地连着也一样
                    City kept = IsPainted(_zones[id]) ? _parents[id] : null;
                    if (kept?.data == null || kept.isRekt() || kept.GetTitle() != _titles[id]) _parents[id] = null;
                    continue;
                }
                City parent = nearestCity[id];
                KingdomTitle title = nearestTitle[id];
                if (parent == null) parent = (walk ??= new Nearest(titleOf)).Of(id, out title);
                if (title != _titles[id]) moved++;
                _titles[id] = title;
                _parents[id] = parent;
                MarkCutOff(id, parent == null);
            }
        if (moved > 0) LogService.LogInfo($"[EmpireCraft][无人区] {moved} 块连不上所挂法理的城(飞地)，并给四周或最近的城");
    }

    // 同一法理里几座城的分界也照走到谁归谁(见 Spread)：Connect 那趟一圈圈连过去只管连得上，分界是横平竖直的直线，
    // 城分了家这条线就成了国界。从挨着最近那座城的地块起，沿最近的城相同、法理相同的地块走：走得到的跟最近的城；
    // 走不到的(中间隔着别的法理)仍由 Connect 连，不成飞地。州府锚点定的不动，长城不过
    private static bool[] FollowSpread(Dictionary<City, KingdomTitle> titleOf, City[] nearestCity, bool[] anchored)
    {
        int size = _titles.Length;
        var followed = new bool[size];
        var queue = new Queue<int>();
        bool Follows(int id) => id >= 0 && id < size && !followed[id] && !anchored[id] && nearestCity[id] != null && _titles[id] != null &&
                                id < _regionOf.Length && _regionOf[id] >= 0 && TitleOfCity(nearestCity[id], titleOf) == _titles[id];
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
            {
                if (!Follows(id) || _zones[id]?.neighbours_all == null) continue;
                foreach (TileZone next in _zones[id].neighbours_all)
                {
                    if (next?.city != nearestCity[id]) continue;
                    followed[id] = true;
                    _parents[id] = nearestCity[id];
                    queue.Enqueue(id);
                    break;
                }
            }
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            if (OnWall(id)) continue;
            foreach (TileZone next in _zones[id].neighbours_all)
            {
                if (next == null || !Follows(next.id) || nearestCity[next.id] != nearestCity[id]) continue;
                followed[next.id] = true;
                _parents[next.id] = nearestCity[id];
                queue.Enqueue(next.id);
            }
        }
        return followed;
    }

    private static bool PinnedTitle(int id) =>
        id < _painted.Length && _painted[id] || WorldMap.WorldMapWilderness.PaintedTitle(_zones[id]) != null ||
        WorldMap.WorldMapWilderness.BoundedAnchor(_zones[id]) ||
        WorldMap.WorldMapWilderness.FixedLayout && !WorldMap.WorldMapWilderness.NeedsBoundaryRepair &&
        id < _parents.Length && _parents[id]?.data != null && !_parents[id].isRekt() &&
        _titles[id] != null && !_titles[id].isRekt() && _parents[id].GetTitle() == _titles[id];

    // 连不上所挂法理的地块从四周连得上的地块、城一圈圈并过去(长城不过)，返回并了多少块
    private static int Absorb(bool[] linked, Dictionary<City, KingdomTitle> titleOf)
    {
        int moved = 0;
        var queue = new Queue<int>();
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
            {
                if (linked[id])
                {
                    queue.Enqueue(id);
                    continue;
                }
                if (_titles[id] == null || _zones[id]?.neighbours_all == null || PinnedTitle(id)) continue;
                foreach (TileZone next in _zones[id].neighbours_all)
                {
                    KingdomTitle around = TitleOfCity(next?.city, titleOf);
                    if (around == null) continue;
                    if (around != _titles[id]) moved++;
                    _titles[id] = around;
                    _parents[id] = next.city;
                    WorldMap.WorldMapWilderness.FollowAnchor(_zones[id], next.city);
                    linked[id] = true;
                    queue.Enqueue(id);
                    break;
                }
            }
        while (queue.Count > 0)
        {
            int id = queue.Dequeue();
            if (id < _walled.Length && _walled[id] || _titles[id] == null) continue;
            foreach (TileZone next in _zones[id].neighbours_all)
            {
                if (next == null || next.id < 0 || next.id >= linked.Length || linked[next.id] || _titles[next.id] == null ||
                    next.id >= _regionOf.Length || _regionOf[next.id] < 0 || PinnedTitle(next.id)) continue;
                if (_titles[next.id] != _titles[id]) moved++;
                _titles[next.id] = _titles[id];
                _parents[next.id] = _parents[id];
                WorldMap.WorldMapWilderness.FollowAnchor(next, _parents[id]);
                linked[next.id] = true;
                queue.Enqueue(next.id);
            }
        }
        return moved;
    }

    // 压着城墙格、不归城的地块
    private static bool[] WallZones()
    {
        var walled = new bool[_zones.Length];
        WorldTile[] tiles = World.world?.tiles_list;
        if (tiles == null) return walled;
        foreach (int tileId in MapWallSystem.Walls.Keys)
        {
            TileZone zone = tileId >= 0 && tileId < tiles.Length ? tiles[tileId]?.zone : null;
            if (zone != null && zone.id >= 0 && zone.id < walled.Length && zone.city == null) walled[zone.id] = true;
        }
        return walled;
    }

    private static KingdomTitle TitleOfCity(City city, Dictionary<City, KingdomTitle> titleOf)
    {
        if (city?.data == null || city.isRekt()) return null;
        if (titleOf.TryGetValue(city, out KingdomTitle title)) return title;
        title = city.GetTitle();
        if (title != null && title.isRekt()) title = null;
        return titleOf[city] = title;
    }

    // 州府锚点定的城的王国法理(见 WorldMap.WorldMapWilderness.AnchorCity；不是剧本、没定的为 null)
    private static KingdomTitle AnchorTitle(TileZone zone)
    {
        KingdomTitle painted = WorldMap.WorldMapWilderness.FixedTitle(zone);
        if (painted != null) return painted;
        KingdomTitle title = WorldMap.WorldMapWilderness.AnchorCity(zone)?.GetTitle();
        if (title != null && !WorldMap.WorldMapWilderness.BoundedAnchor(zone))
        {
            // 治所的推算范围不能盖过另一政权明确的史实范围；同一帝国内再按行政锚点分省。
            KingdomTitle historical = WorldMap.WorldMapWilderness.TitleFor(zone);
            Kingdom historyRealm = historical == null ? null : RealmOf(historical.FindRealmTitleHolder());
            Kingdom anchorRealm = RealmOf(WorldMap.WorldMapWilderness.AnchorCity(zone)?.kingdom);
            if (historyRealm != null && anchorRealm != historyRealm) return null;
        }
        return title == null || title.isRekt() ? null : title;
    }

    // 时代文件写了史实归属的(见 WorldMap.WorldMapWilderness.TitleFor)：挂那个政权(连同它帝国里的省)按地块走最近的城的王国法理——
    // 以前挂都城那块法理，塔克拉玛干算进了京畿；后来挂直线最近的城，几座城之间的分界是一条条直线(用户 10-30 截图瀚海里横平竖直的线)。
    // 走得到的最近的城就是这个政权的照它，否则只从这个政权的城走一遍(一次挂接每个政权至多一遍)；那个政权没城了才挂原来那个
    private static KingdomTitle HistoricalTitle(TileZone zone, Dictionary<City, KingdomTitle> titleOf, KingdomTitle[] from,
        City[] fromCity, Dictionary<Kingdom, KingdomTitle[]> realms)
    {
        KingdomTitle title = WorldMap.WorldMapWilderness.TitleFor(zone);
        if (title == null) return null;
        Kingdom realm = RealmOf(title.FindRealmTitleHolder());
        if (realm == null) return title;
        City near = fromCity[zone.id];
        if (from[zone.id] != null && near?.data != null && !near.isRekt() && RealmOf(near.kingdom) == realm) return from[zone.id];
        if (!realms.TryGetValue(realm, out KingdomTitle[] own))
            realms[realm] = own = Spread(titleOf, city => RealmOf(city.kingdom) == realm, float.PositiveInfinity, out _, out _, false);
        return own[zone.id] ?? title;
    }

    // 一国所在的政权：帝国的省算帝国核心
    private static Kingdom RealmOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        return kingdom.GetEmpire()?.CoreKingdom ?? kingdom;
    }

    private static City NearestCity(IEnumerable<City> cities, TileZone zone)
    {
        WorldTile at = zone?.centerTile;
        if (at == null || cities == null) return null;
        City best = null;
        long bestDistance = long.MaxValue;
        foreach (City city in cities)
        {
            WorldTile tile = city?.data == null || city.isRekt() ? null : city.getTile();
            if (tile == null) continue;
            long dx = tile.x - at.x, dy = tile.y - at.y, distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = city;
        }
        return best;
    }

    // 这块无人区跟着哪座城(显示行政区用，见 WildernessTerritory)：州府锚点定的城(还在同一个法理里)，否则所挂法理里离它最近的城
    // 挂接时连上的那座城(见 Connect)还在这个法理里就是它；没挂上法理的(最近的城没有法理)就是那座城
    public static City OriginOf(TileZone zone)
    {
        KingdomTitle title = TitleOf(zone);
        City parent = Index(zone) >= 0 && zone.id < _parents.Length ? _parents[zone.id] : null;
        if (parent?.data == null || parent.isRekt()) parent = null;
        // 权能笔刷按城刷的跟取的那座城(见 Paint)，州府锚点也不改
        if (parent != null && title != null && IsPainted(zone) && parent.GetTitle() == title) return parent;
        City anchor = WorldMap.WorldMapWilderness.AnchorCity(zone);
        if (title != null && anchor != null && anchor.GetTitle() == title)
        {
            if (zone.id < _parents.Length) _parents[zone.id] = anchor;
            return anchor;
        }
        if (parent != null && !IsPainted(zone) && WorldMap.WorldMapWilderness.PaintedTitle(zone) == null)
        {
            KingdomTitle current = parent.GetTitle();
            if (current == null || !current.isRekt())
            {
                // 自动辖境跟随原城市换法理；先到旧法理里另找城市会留下旧辖境飞地。
                if (_titles[zone.id] != current)
                {
                    _titles[zone.id] = current;
                    Version++;
                }
                return parent;
            }
        }
        if (title == null) return parent;
        if (parent != null && parent.GetTitle() == title) return parent;
        // 法理里的城隔着长城分在两个行政区时，跟长城这一侧的城(见 WorldMap.FrontierWalls)
        City replacement = NearestCity(title.getCities().Where(city => !WorldMap.FrontierWalls.Behind(city, zone)), zone) ??
                           NearestCity(title.getCities(), zone);
        if (zone.id < _parents.Length) _parents[zone.id] = replacement;
        return replacement;
    }

    private static bool UntitledOrigin(int id) => id < _parents.Length && _parents[id]?.data != null &&
        !_parents[id].isRekt() && _parents[id].GetTitle() == null &&
        !(id < _painted.Length && _painted[id]) && WorldMap.WorldMapWilderness.PaintedTitle(_zones[id]) == null;

    // 权能「无人区法理」笔刷(用户 10-30 "给个划分无人区法理归属的权能""可以允许多个法理归属同一个无人区，但是类似于笔刷上色那种"
    // "一个法理拿一部分")：把这块无人区挂到给定的法理上，之后自动重挂不再动它；title 为 null 擦掉，下一核按自动的规矩补挂。
    // city 为取的那座城：地块跟着它、归它所在的国(以前只挂法理，跟法理里最近的城，法理分在几国时归了别国，同一法理的还刷不动)；
    // 不给或那城不在这个法理里，跟法理里最近的城(见 OriginOf)。返回改没改
    public static bool Paint(TileZone zone, KingdomTitle title, City city = null)
    {
        if (!_built || !_enabled || Index(zone) < 0 || zone.id >= _titles.Length || title != null && title.isRekt()) return false;
        if (_painted.Length != _titles.Length) Array.Resize(ref _painted, _titles.Length);
        if (_parents.Length != _titles.Length) Array.Resize(ref _parents, _titles.Length);
        if (title == null)
        {
            if (!_painted[zone.id]) return false;
            _painted[zone.id] = false;
            _titles[zone.id] = null;
            _attachPending = true;
        }
        else
        {
            bool follow = city?.data != null && !city.isRekt() && city.GetTitle() == title;
            if (_painted[zone.id] && _titles[zone.id] == title && (!follow || _parents[zone.id] == city)) return false;
            _painted[zone.id] = true;
            _titles[zone.id] = title;
            if (follow) _parents[zone.id] = city;
            // 刷了就不受远近限制(见 Assigned)：当场算近，提示不等下一轮核对就写对归属
            if (zone.id < _far.Length) _far[zone.id] = false;
            if (!Owners.ContainsKey(title))
            {
                Kingdom holder = title.FindRealmTitleHolder();
                Owners[title] = holder != null && holder.isRekt() ? null : holder;
            }
        }
        Version++;
        return true;
    }

    public static bool IsPainted(TileZone zone) => zone != null && zone.id >= 0 && zone.id < _painted.Length && _painted[zone.id];

    // 从城的地块往外走(starts 为空时所有的城都起步，不为空时只从这些城起步；block 时也不穿过别的城的地)：
    // 每块地记最先走到它的那座城(cities)与它的法理；reached 记走到了没有。八方向走，每块地的代价各自不同(见 TerritoryGrain)
    // (用户 10-30 "你至少让版图平滑点"：以前四方向一步一格，两座城之间的分界是横平竖直、45 度的长直线，并进国土后一块块方的)，
    // 至多走 limit。隔山不领(见 Step)：按"在山里连走了几块"分开记，山里走得深的那条路不挡山外绕过来的路；overMountains 时不管山
    private static KingdomTitle[] Spread(Dictionary<City, KingdomTitle> titleOf, Func<City, bool> starts, float limit, out bool[] reached,
        out City[] cities, bool block = true, bool overMountains = false)
    {
        int size = _zones.Length;
        int layers = overMountains ? 1 : MountainDepth + 1;
        var stateFrom = new KingdomTitle[size * layers];
        var stateCity = new City[size * layers];
        var cost = new float[size * layers];
        // travel：不算地形的坎的代价，limit(游牧挂接走多远)只量它，坎只管谁先领到(见 Seam)
        var travel = new float[size * layers];
        for (int i = 0; i < cost.Length; i++) cost[i] = float.PositiveInfinity;
        var heap = new WorldMap.WorldMapZones.MinHeap();
        for (int id = 0; id < size; id++)
        {
            City city = _zones[id]?.city;
            if (city?.data == null || city.isRekt() || starts != null && !starts(city)) continue;
            if (!titleOf.TryGetValue(city, out KingdomTitle title))
            {
                title = city.GetTitle();
                if (title != null && title.isRekt()) title = null;
                titleOf[city] = title;
            }
            // 没有法理的城也从这里起步：离它最近的无人区不挂(由周边的城定归属)，不让远处有法理的城越过它挂过来——
            // 以前只从有法理的城起步，紧挨着祭部落(没有法理)的雨林挂到了北边远处的顾法理上(用户 2026-10-29 截图)
            int s0 = id * layers;
            stateFrom[s0] = title;
            stateCity[s0] = city;
            cost[s0] = 0f;
            travel[s0] = 0f;
            heap.Push(0f, s0);
        }
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int state);
            if (at > cost[state]) continue;
            int id = state / layers, depth = state % layers;
            // 长城上不归城的地块到此为止：长城那边的无人区不从这边挂过去
            if (id < _walled.Length && _walled[id] && _zones[id].city == null) continue;
            TileZone zone = _zones[id];
            TileZone[] neighbours = zone.neighbours_all;
            if (neighbours == null) continue;
            foreach (TileZone next in neighbours)
            {
                if (next == null || next.id < 0 || next.id >= size || !Land(next)) continue;
                // 只从部分城起步时(游牧)不穿过别的城的地：农耕国腹地里的沙漠走不到，照旧挂最近的城
                if (block && starts != null && next.city?.data != null && !next.city.isRekt() && !starts(next.city)) continue;
                float step = (next.x != zone.x && next.y != zone.y ? 1.414f : 1f) * Roughness(next);
                float walked = travel[state] + step;
                if (walked > limit) continue;
                float total = at + step + Seam(zone, next);
                int nextDepth = overMountains ? 0 : Step(zone, depth, next);
                if (nextDepth < 0) continue;
                int target = next.id * layers + nextDepth;
                if (total >= cost[target]) continue;
                cost[target] = total;
                travel[target] = walked;
                stateFrom[target] = stateFrom[state];
                stateCity[target] = stateCity[state];
                heap.Push(total, target);
            }
        }
        var from = new KingdomTitle[size];
        cities = new City[size];
        reached = new bool[size];
        for (int id = 0; id < size; id++)
        {
            float best = float.PositiveInfinity;
            for (int d = 0; d < layers; d++)
            {
                int state = id * layers + d;
                if (cost[state] >= best) continue;
                best = cost[state];
                from[id] = stateFrom[state];
                cities[id] = stateCity[state];
            }
            reached[id] = !float.IsPositiveInfinity(best);
        }
        return from;
    }

    private static bool OnWall(int id) => id < _walled.Length && _walled[id];

    // 换地形、下河的坎(用户 2026-11-10 截图韦部落："自然的地形占领也好直""而且也没顺着地形走")：每块地的代价互不相干(TerritoryGrain)，
    // 走远了正负抵消，两城之间的分界在大尺度上还是两城连线的中垂线，画成平滑轮廓、磨掉碎齿就成了一条长直边；地形自己连成片，
    // 从一种地形走进另一种(平原进林子、草原进沙漠、平地上丘陵)多花 TerrainSeam，从岸上下河多花 RiverSeam(上岸不收)，
    // 分界就落在林缘、沙漠边、河上、山脚下。城池那几块不算换地形(哪座城出城都一样)。只管谁先领到，不算进走多远(见 Spread 的 travel)。
    // 斜着走过河的拐角(河只有一块宽时八方向走能从拐角跨过去)也算下河。按同一套走法在格子上模拟过两城之间：
    // 斜着的林缘，分界离林缘平均从 4.9 块近到 1.7 块；绕着中垂线左右摆 7 块的河，从 5.4 块近到 0.7 块
    private const float TerrainSeam = 5f, RiverSeam = 10f;

    private static float Seam(TileZone zone, TileZone next)
    {
        FrontTerrain from = TerrainSystem.Of(zone), to = TerrainSystem.Of(next);
        if (from == FrontTerrain.River) return 0f;
        if (to == FrontTerrain.River || CutsRiverCorner(zone, next)) return RiverSeam;
        if (from == to || from == FrontTerrain.Urban || to == FrontTerrain.Urban) return 0f;
        return TerrainSeam;
    }

    // 斜走一步时，旁边两块里有河：从河的拐角跨了过去
    private static bool CutsRiverCorner(TileZone zone, TileZone next)
    {
        if (next.x == zone.x || next.y == zone.y) return false;
        ZoneCalculator calculator = World.world?.zone_calculator;
        if (calculator == null) return false;
        TileZone a = calculator.getZone(zone.x, next.y), b = calculator.getZone(next.x, zone.y);
        return a != null && TerrainSystem.Of(a) == FrontTerrain.River || b != null && TerrainSystem.Of(b) == FrontTerrain.River;
    }

    // 隔山不领(用户 2026-11-10 截图犍陀罗、迦湿弥罗挂走了兴都库什、喀喇昆仑山里山梁那边的无人区："山地对面的无人区也不准自动领取"；
    // 问答定"至多 2 块""法理也不挂，留作无主")：挂法理往外走时，成片的山(地块里走不通，见 TerrainSystem.ImpassableGround)
    // 至多连着走进 MountainDepth 块(约 16 格的山脚)，从山里下不到别处走得通的地；走得通的地块之间，交界上也要走得通
    // (见 TerrainSystem.Crossable，城心、修了路的照通)。所以山那边的地只能经山口、河谷走过去领，大山深处谁都不领；高原外缘同理
    private const int MountainDepth = 2;

    // 从 zone(已在山里连走了 depth 块)走到 next 之后，在山里连走了几块；走不过去为 -1
    private static int Step(TileZone zone, int depth, TileZone next)
    {
        if (TerrainSystem.ImpassableGround(next)) return depth < MountainDepth ? depth + 1 : -1;
        if (TerrainSystem.ImpassableGround(zone) || !TerrainSystem.Crossable(zone, next)) return -1;
        return 0;
    }

    // 不翻山走得到的最近的城与它的法理(见 Spread)。走不到的：翻山走得到(大山深处、四面是山的谷地)的谁都不挂，留作无主；
    // 翻山也走不到的(隔海、没有城的岛)照旧挂直线最近的城
    private sealed class Nearest
    {
        private readonly Dictionary<City, KingdomTitle> _titleOf;
        private KingdomTitle[] _from;
        private bool[] _reached, _overReached;
        private City[] _city;

        public Nearest(Dictionary<City, KingdomTitle> titleOf, KingdomTitle[] from = null, bool[] reached = null, City[] city = null)
        {
            _titleOf = titleOf;
            _from = from;
            _reached = reached;
            _city = city;
        }

        // 隔着山谁都不领的
        public bool CutOff(int id)
        {
            _from ??= Spread(_titleOf, null, float.PositiveInfinity, out _reached, out _city);
            if (id < 0 || id >= _reached.Length || _reached[id]) return false;
            if (_overReached == null) Spread(_titleOf, null, float.PositiveInfinity, out _overReached, out _, overMountains: true);
            return _overReached[id];
        }

        public City Of(int id, out KingdomTitle title)
        {
            title = null;
            if (CutOff(id)) return null;
            if (id >= 0 && id < _reached.Length && _reached[id])
            {
                title = _from[id];
                return _city[id];
            }
            City near = NearestCity(World.world.cities.list, _zones[id]);
            title = TitleOfCity(near, _titleOf);
            return near;
        }
    }

    // 隔着山谁都不领、空着的(见 Nearest)：剧本存档不为它们每回都整个重挂(见 Attach)
    private static bool[] _cutOff = new bool[0];

    private static bool CutOff(int id) => id >= 0 && id < _cutOff.Length && _cutOff[id];

    private static void MarkCutOff(int id, bool cut)
    {
        if (id < 0) return;
        if (_cutOff.Length < _titles.Length) Array.Resize(ref _cutOff, _titles.Length);
        if (id < _cutOff.Length) _cutOff[id] = cut;
    }

    // 旧档里翻山挂上的无人区改挂(剧本存档的辖境照存档留着，不会自己重挂，见 Attach)：从各城沿同一法理的无人区按隔山的走法走
    // (见 Step)，走不到的、又不是剧本定的(州府锚点、史实归属、剧本或权能刷的)改挂不翻山走得到的最近的城，谁都走不到的空出来。
    // 读旧档后做一次(见 WorldMap.WorldMapWilderness.NeedsMountainRepair)，返回改了几块
    private static int RepairOverMountains()
    {
        int size = _titles.Length;
        if (size == 0) return 0;
        var titleOf = new Dictionary<City, KingdomTitle>();
        _walled = WallZones();
        int layers = MountainDepth + 1;
        var seen = new bool[size * layers];
        var legit = new bool[size];
        var queue = new Queue<int>();
        void Reach(int id, int depth)
        {
            int state = id * layers + depth;
            if (seen[state]) return;
            seen[state] = true;
            legit[id] = true;
            queue.Enqueue(state);
        }
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
            {
                KingdomTitle title = _titles[id];
                TileZone zone = _zones[id];
                if (title == null || title.isRekt() || zone?.neighbours_all == null) continue;
                foreach (TileZone next in zone.neighbours_all)
                {
                    City city = next?.city;
                    if (city?.data == null || city.isRekt() || TitleOfCity(city, titleOf) != title) continue;
                    int depth = Step(next, 0, zone);
                    if (depth >= 0) Reach(id, depth);
                }
            }
        while (queue.Count > 0)
        {
            int state = queue.Dequeue(), id = state / layers, depth = state % layers;
            if (OnWall(id)) continue;
            foreach (TileZone next in _zones[id].neighbours_all)
            {
                if (next == null || next.id < 0 || next.id >= size || _titles[next.id] != _titles[id] ||
                    next.id >= _regionOf.Length || _regionOf[next.id] < 0) continue;
                int nextDepth = Step(_zones[id], depth, next);
                if (nextDepth >= 0) Reach(next.id, nextDepth);
            }
        }
        var walk = new Nearest(titleOf);
        int moved = 0;
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
            {
                KingdomTitle title = _titles[id];
                TileZone zone = _zones[id];
                if (legit[id] || title == null || title.isRekt() || zone == null || IsPainted(zone) ||
                    AnchorTitle(zone) != null || WorldMap.WorldMapWilderness.TitleFor(zone) != null) continue;
                City city = walk.Of(id, out KingdomTitle nearest);
                if (nearest == title) continue;
                _titles[id] = nearest;
                _parents[id] = city;
                MarkCutOff(id, city == null);
                moved++;
            }
        return moved;
    }

    // 走进这块地的代价(每块地各自不同，同一块地每次一样，见 TerritoryGrain)
    private static float Roughness(TileZone zone) =>
        Mathf.Max(1f, TerrainResistance.TerrainStep(zone)) * TerritoryGrain.At(zone.x, zone.y, 4049);

    private static bool Land(TileZone zone) => zone.tiles_with_ground > 0 && !SeaRegionSystem.IsSea(zone);

    // 几秒一核：城与法理的对应变了修补挂接；各法理的持有国变了记下。
    internal static void RequestReattach() => _attachPending = true;

    private static void RefreshTitles()
    {
        if (_regions.Count == 0) return;
        long signature = TitleSignature();
        bool changed = false;
        if (signature != _titleSignature)
        {
            _titleSignature = signature;
            using (FrameProfiler.Measure("无人区·挂法理")) Attach(true);
            changed = true;
        }
        // 笔刷擦掉的地块补挂
        else if (_attachPending)
        {
            using (FrameProfiler.Measure("无人区·挂法理")) Attach(false);
            changed = true;
        }
        _attachPending = false;
        if (RefreshOwners()) changed = true;
        if (changed) Version++;
    }

    // 碎块核对(见 RefreshFragments)：归属、远近有变或重新划过区当场核，否则至多隔几秒(真实时间)核一次——城扩张、易手改了四邻也在这时补上。
    // 以前同挂法理一起每 2 秒整片扫一遍
    private const float FragmentSeconds = 10f;
    private static float _fragmentAt;
    private static int _fragmentBuilds = -1;

    // 各法理的持有国(KingdomExtension.FindRealmTitleHolder)；有变化返回 true。
    // 每 2 秒一核，各段分开计时(进图后一卡一卡时看性能日志里「无人区·」开头的几段)
    private static bool RefreshOwners()
    {
        bool changed = false;
        using (FrameProfiler.Measure("无人区·法理持有"))
        {
            var seen = new HashSet<KingdomTitle>();
            foreach (KingdomTitle title in _titles)
                if (title != null) seen.Add(title);
            foreach (KingdomTitle title in seen)
            {
                Kingdom holder = title.isRekt() ? null : title.FindRealmTitleHolder();
                if (holder != null && holder.isRekt()) holder = null;
                if (Owners.TryGetValue(title, out Kingdom old) && old == holder) continue;
                Owners[title] = holder;
                changed = true;
            }
            foreach (KingdomTitle stale in Owners.Keys.Where(title => !seen.Contains(title)).ToList()) Owners.Remove(stale);
        }
        using (FrameProfiler.Measure("无人区·周边归属"))
            if (RefreshSurround()) changed = true;
        using (FrameProfiler.Measure("无人区·版图远近"))
            if (RefreshReach()) changed = true;
        float now = Time.realtimeSinceStartup;
        if (changed || Builds != _fragmentBuilds || now >= _fragmentAt)
        {
            _fragmentAt = now + FragmentSeconds;
            _fragmentBuilds = Builds;
            using (FrameProfiler.Measure("无人区·碎块并国"))
                if (RefreshFragments()) changed = true;
        }
        return changed;
    }

    // 远处的无人区不算版图(用户 2026-11-10 转群友截图：图上只放了一个部落，整片大陆的无人区都挂到它唯一的城上、全归了它，
    // "放个部落全归属了"；问答定"法理照挂，版图只算附近""按时代放宽")：法理照旧不限远近挂最近的城(地形都进法理图层)，
    // 可离本来该归的那国的城(城心连同城的大小)超过 ReachOf 块的地块不算它的版图，显示为无主无人区，城多了、近了自然归入。
    // 石器时代 ReachStone 块，之后每进一个时代多 ReachPerEra 块；科技树关着按 ReachDefault 块。世界地图预设剧本照史实不变。
    // 每次核归属时算好记在 _far，OwnerOf 只看这个。
    // 远近顺着地形量(用户 2026-11-10 截图韦部落："自然的地形占领也好直""而且也没顺着地形走")：以前量直线(城心到地块，加上城的大小)，版图边是一段段圆弧，
    // 隔着林子、沙漠、河也一样远。现在从这国各城的地块起步按地形阻力往外走(TerrainResistance.TerrainStep：平原 1、森林 1.3、
    // 丘陵 1.6、沙漠雪地 2、沼泽丛林 2.5、山地高原 4，斜走 ×1.414，下河 +2)，花得不到 ReachOf 的才算近；近岸的岛隔着海一块按 SeaReachStep
    private const int ReachStone = 6, ReachPerEra = 3, ReachDefault = 10;
    private const float SeaReachStep = 2f;
    private static bool[] _far = new bool[0];

    public static int ReachOf(Kingdom kingdom)
    {
        string culture = CultureService.GetRealmCulture(kingdom);
        if (!TechnologySystem.IsActiveFor(culture)) return ReachDefault;
        return ReachStone + ReachPerEra * (Math.Max(1, TechnologySystem.GetEraTier(culture)) - 1);
    }

    // 远近按国缓存：一国的城、城的地块与远近(时代)都没变就沿用上回走出的范围；地形改了靠 ReachEpochSeconds 一轮全部重走。
    // 每次核对至多重算 ReachBudgetMs 毫秒，没轮到的照上回的(还没算过的那几块远近先不动)；进图后头一轮一次算齐。
    // 以前每 2 秒(真实时间)把每个国家从它所有城的地块起步整片按地形走一遍，地图大、国家多时一次几十到上百毫秒，进图就一卡一卡
    private sealed class ReachEntry
    {
        public long Key;
        public HashSet<int> Near;
    }

    private static readonly Dictionary<Kingdom, ReachEntry> ReachCache = new();
    private const double ReachBudgetMs = 4d;
    private const float ReachEpochSeconds = 60f;
    private static float _reachEpochAt;
    private static int _reachEpoch;
    private static bool _reachPrimed;

    // 指定了归属的地块不限远近(用户 2026-11-12 转群友"用无人区法理那个画笔画的时候画不动，显示无主""导入存档之后……全是无人区整出来的坑")：
    // 远近只管自动挂的；神力笔刷刷过的(Paint)、剧本编辑器刷了归属的(PaintedTitle)、刷给城后开局划成无人区的(CityClaim)照刷的归。
    // 以前这些离城远(石器时代往山里只算得进一块半)也算无主，刷了只见"归 无主"，导入的图在山里沙漠里一片片空着
    private static bool Assigned(int id) =>
        id < _painted.Length && _painted[id] || WorldMap.WorldMapWilderness.PaintedTitle(_zones[id]) != null ||
        WorldMap.WorldMapWilderness.CityClaim(_zones[id]);

    private static bool RefreshReach()
    {
        int length = _titles.Length;
        if (_far.Length != length) _far = new bool[length];
        bool exempt = WorldMap.WorldMapWilderness.FixedLayout;
        float now = Time.realtimeSinceStartup;
        if (now >= _reachEpochAt)
        {
            _reachEpoch++;
            _reachEpochAt = now + ReachEpochSeconds;
        }
        var keys = new Dictionary<Kingdom, long>();
        var watch = global::System.Diagnostics.Stopwatch.StartNew();
        double budget = _reachPrimed ? ReachBudgetMs : double.PositiveInfinity;
        bool changed = false, complete = true;
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
            {
                if (id < 0 || id >= length || id >= _zones.Length || _zones[id] == null) continue;
                bool far = false;
                if (!exempt && !Assigned(id))
                {
                    Kingdom owner = Candidate(_zones[id], out _);
                    if (owner != null)
                    {
                        HashSet<int> near = NearOf(owner, keys, watch, budget, ref complete);
                        // 还没轮到算的国：这块的远近照旧
                        if (near == null) continue;
                        // 没有城的国(还在筹备建城)不领无人区：一块都没走到
                        far = !near.Contains(id);
                    }
                }
                if (_far[id] == far) continue;
                _far[id] = far;
                changed = true;
            }
        if (complete) _reachPrimed = true;
        if (ReachCache.Count > keys.Count + 32)
            foreach (Kingdom stale in ReachCache.Keys.Where(kingdom => kingdom == null || kingdom.isRekt() || !keys.ContainsKey(kingdom)).ToList())
                ReachCache.Remove(stale);
        return changed;
    }

    // 这国走得到的范围：缓存对得上就沿用；对不上且这轮还有余量就重走，没余量先用过期的(没有过就是 null)
    private static HashSet<int> NearOf(Kingdom owner, Dictionary<Kingdom, long> keys, global::System.Diagnostics.Stopwatch watch,
        double budget, ref bool complete)
    {
        if (!keys.TryGetValue(owner, out long key)) keys[owner] = key = ReachKey(owner);
        ReachCache.TryGetValue(owner, out ReachEntry entry);
        if (entry != null && entry.Key == key) return entry.Near;
        if (watch.Elapsed.TotalMilliseconds >= budget)
        {
            complete = false;
            return entry?.Near;
        }
        HashSet<int> near = Near(owner, ReachOf(owner));
        ReachCache[owner] = new ReachEntry { Key = key, Near = near };
        return near;
    }

    // 一国的远近、各城的地块(不计次序)与地形轮次合成的签名
    private static long ReachKey(Kingdom owner)
    {
        long count = 0L, sum = 0L, squares = 0L, cities = 0L;
        foreach (City city in owner.cities ?? new List<City>())
        {
            if (city?.data == null || city.isRekt()) continue;
            cities += city.id;
            foreach (TileZone part in city.zones ?? new List<TileZone>())
            {
                if (part == null || part.id < 0) continue;
                count++;
                sum += part.id;
                squares += (long)part.id * part.id;
            }
        }
        unchecked
        {
            long key = ReachOf(owner) * 1_000_003L + _reachEpoch;
            key = key * 1_000_003L + count;
            key = key * 1_000_003L + sum;
            key = key * 1_000_003L + squares;
            return key * 1_000_003L + cities;
        }
    }

    // 从这国各城的地块起步，按地形阻力走得到、花不到 budget 的地块(不含起步的城地块；记全，无人区重划后照样查得到)。
    // 只从挨着外面的城地块起步：里面的城地块代价同为 0，从那儿出发绕不出更近的路(结果同从全部城地块起步)；代价按地块编号放数组、带戳记
    private static float[] _nearCost = new float[0];
    private static int[] _nearMark = new int[0];
    private static int _nearStamp;

    private static HashSet<int> Near(Kingdom owner, float budget)
    {
        var zones = World.world?.zone_calculator?.zones;
        int size = Math.Max(_zones.Length, zones?.Count ?? 0);
        if (_nearCost.Length < size)
        {
            _nearCost = new float[size];
            _nearMark = new int[size];
            _nearStamp = 0;
        }
        if (++_nearStamp == int.MaxValue)
        {
            Array.Clear(_nearMark, 0, _nearMark.Length);
            _nearStamp = 1;
        }
        int stamp = _nearStamp;
        var result = new HashSet<int>();
        var seeds = new List<TileZone>();
        foreach (City city in owner.cities ?? new List<City>())
        {
            if (city?.data == null || city.isRekt()) continue;
            foreach (TileZone part in city.zones ?? new List<TileZone>())
            {
                if (part == null || part.id < 0 || part.id >= size || _nearMark[part.id] == stamp) continue;
                _nearMark[part.id] = stamp;
                _nearCost[part.id] = 0f;
                seeds.Add(part);
            }
        }
        var heap = new WorldMap.WorldMapZones.MinHeap();
        foreach (TileZone part in seeds)
        {
            bool edge = part.neighbours_all == null;
            if (!edge)
                foreach (TileZone next in part.neighbours_all)
                    if (next == null || next.id < 0 || next.id >= size || _nearMark[next.id] != stamp)
                    {
                        edge = true;
                        break;
                    }
            if (edge) heap.Push(0f, part.id);
        }
        while (heap.Count > 0)
        {
            heap.Pop(out float at, out int id);
            if (at > _nearCost[id]) continue;
            TileZone zone = zones != null && id < zones.Count ? zones[id] : id < _zones.Length ? _zones[id] : null;
            if (zone?.neighbours_all == null) continue;
            bool fromRiver = TerrainSystem.Of(zone) == FrontTerrain.River;
            foreach (TileZone next in zone.neighbours_all)
            {
                if (next == null || next.id < 0 || next.id >= size) continue;
                float step = SeaRegionSystem.IsSea(next) ? SeaReachStep : TerrainResistance.TerrainStep(next);
                if (step < 0f) continue;
                if (!fromRiver && (TerrainSystem.Of(next) == FrontTerrain.River || CutsRiverCorner(zone, next)))
                    step += TerrainResistance.RiverCrossing;
                float total = at + (next.x != zone.x && next.y != zone.y ? 1.414f : 1f) * step;
                if (total > budget || _nearMark[next.id] == stamp && total >= _nearCost[next.id]) continue;
                _nearMark[next.id] = stamp;
                _nearCost[next.id] = total;
                result.Add(next.id);
                heap.Push(total, next.id);
            }
        }
        return result;
    }

    // 各片周边的城(与区域相邻的地块上的城)是不是全属于同一个国家；有变化返回 true。
    // 只看地块四邻八邻上的城，几万块地也只是几毫秒；周边一座城都没有的算中立
    // 周边的城同属一家(用户 2026-11-02 转群友"秋海棠叶要完整 至少得是大清那种 这种算什么 残风败叶?")：以前要周边的城全是同一个国家，
    // 一个帝国的几个行政区、封国夹着的山脉沙漠成了中立，帝国版图上一块块窟窿。现在按所在帝国比(不在帝国里的按本国)：
    // 周边的城都在同一个帝国里就归这个帝国，画给挨着这片的城最多的那个成员(一样多的先核心王国、再编号小的)
    private static readonly Dictionary<Kingdom, int> SurroundCounts = new();

    private static bool RefreshSurround()
    {
        bool changed = false;
        foreach (WildRegion region in _regions)
        {
            object realm = null;
            bool mixed = false;
            SurroundCounts.Clear();
            foreach (int id in region.Zones)
            {
                TileZone zone = id >= 0 && id < _zones.Length ? _zones[id] : null;
                if (zone?.neighbours_all == null) continue;
                foreach (TileZone next in zone.neighbours_all)
                {
                    City city = next?.city;
                    if (city?.data == null || city.isRekt()) continue;
                    Kingdom kingdom = city.kingdom;
                    if (kingdom == null || kingdom.isRekt() || kingdom.wild)
                    {
                        mixed = true;
                        break;
                    }
                    Empire empire = kingdom.GetEmpire();
                    object key = empire != null && !empire.isRekt() && !empire.IsArchived() ? empire : kingdom;
                    if (realm == null) realm = key;
                    else if (!ReferenceEquals(realm, key))
                    {
                        mixed = true;
                        break;
                    }
                    SurroundCounts.TryGetValue(kingdom, out int count);
                    SurroundCounts[kingdom] = count + 1;
                }
                if (mixed) break;
            }
            Kingdom around = null;
            if (!mixed && realm != null)
            {
                Kingdom core = (realm as Empire)?.CoreKingdom;
                int most = -1;
                foreach (KeyValuePair<Kingdom, int> pair in SurroundCounts)
                {
                    bool better = pair.Value > most || pair.Value == most &&
                        (pair.Key == core || around != core && pair.Key.id < around.id);
                    if (!better) continue;
                    most = pair.Value;
                    around = pair.Key;
                }
            }
            SurroundCounts.Clear();
            if (region.Surround == around) continue;
            region.Surround = around;
            changed = true;
        }
        return changed;
    }

    // 太小的碎块并进最近的大国(用户 2026-11-06 "特别小的土地就别填补国家了, 直接并进最近的大国不就好了"，又说"一旦城市变更政权
    // 对应的无人区也要变更, 不然会导致飞地")：按本来的归属(BaseResolve)把无人区地块连成片——四邻相连、同一归属，跨片的无人区也连
    // (行政上分给谁跟哪片山脉沙漠无关)；不到 MinZones 块的一片，有归属却不挨着本国的地(城的地块、本国的剧本史实领土)，
    // 或没有归属(永久中立的不算)，改归四周挨得最多的那一国(城的地块、别的无人区、剧本史实领土都算，按挨着的块数；一样多的编号小的先)；
    // 连成片、挨着本国都只算东西南北四邻(用户 11-08 截图沙州一带还有碎块)：画布按 3×3 块平滑，两块只斜着碰一个角，
    // 角上四块各占一份、两国打平，画出来是两个分开的小岛；以前按八邻算，斜角碰着本国就当连着、不并，画出来照样是飞地；
    // 四周没有国家的照旧。记成(本来归属 → 并进哪国)：本来归属变了(城换了主)当场不再算，等下一核(RefreshOwners)重算
    private static readonly Dictionary<int, (Kingdom from, Kingdom into)> Merged = new(), FreshMerged = new();
    private static Kingdom[] _baseOwner = new Kingdom[0];
    private static int[] _fragmentMark = new int[0];
    private static readonly List<int> FragmentZones = new();
    private static readonly Dictionary<Kingdom, int> FragmentNeighbours = new();

    private static bool RefreshFragments()
    {
        int size = _zones.Length;
        if (_baseOwner.Length != size)
        {
            _baseOwner = new Kingdom[size];
            _fragmentMark = new int[size];
        }
        else Array.Clear(_fragmentMark, 0, size);
        FreshMerged.Clear();
        foreach (WildRegion region in _regions)
            foreach (int id in region.Zones)
                if (id >= 0 && id < size && _zones[id] != null) _baseOwner[id] = BaseResolve(_zones[id], out _);
        int mark = 0;
        foreach (WildRegion region in _regions)
            foreach (int start in region.Zones)
            {
                if (!Fragmentable(start, size) || _fragmentMark[start] != 0) continue;
                Kingdom owner = _baseOwner[start];
                mark++;
                FragmentZones.Clear();
                FragmentNeighbours.Clear();
                bool home = false;
                _fragmentMark[start] = mark;
                FragmentZones.Add(start);
                for (int i = 0; i < FragmentZones.Count; i++)
                {
                    TileZone zone = _zones[FragmentZones[i]];
                    if (zone?.neighbours == null) continue;
                    foreach (TileZone next in zone.neighbours)
                    {
                        if (next == null || next.id < 0 || next.id >= size) continue;
                        if (Fragmentable(next.id, size) && _baseOwner[next.id] == owner)
                        {
                            if (_fragmentMark[next.id] == mark) continue;
                            _fragmentMark[next.id] = mark;
                            FragmentZones.Add(next.id);
                            continue;
                        }
                        Kingdom other = NeighbourOwner(next, size);
                        if (other == null) continue;
                        if (other == owner)
                        {
                            home = true;
                            continue;
                        }
                        FragmentNeighbours.TryGetValue(other, out int count);
                        FragmentNeighbours[other] = count + 1;
                    }
                }
                if (FragmentZones.Count >= MinZones || owner != null && home || FragmentNeighbours.Count == 0) continue;
                Kingdom into = null;
                int most = 0;
                foreach (KeyValuePair<Kingdom, int> pair in FragmentNeighbours)
                {
                    if (pair.Value < most || pair.Value == most && into != null && pair.Key.id > into.id) continue;
                    most = pair.Value;
                    into = pair.Key;
                }
                if (into == null) continue;
                foreach (int id in FragmentZones) FreshMerged[id] = (owner, into);
            }
        FragmentZones.Clear();
        FragmentNeighbours.Clear();
        bool changed = FreshMerged.Count != Merged.Count;
        if (!changed)
            foreach (KeyValuePair<int, (Kingdom from, Kingdom into)> pair in FreshMerged)
                if (!Merged.TryGetValue(pair.Key, out (Kingdom from, Kingdom into) old) || old.from != pair.Value.from ||
                    old.into != pair.Value.into)
                {
                    changed = true;
                    break;
                }
        if (!changed)
        {
            FreshMerged.Clear();
            return false;
        }
        Merged.Clear();
        foreach (KeyValuePair<int, (Kingdom from, Kingdom into)> pair in FreshMerged) Merged[pair.Key] = pair.Value;
        FreshMerged.Clear();
        return true;
    }

    // 参与碎块合并的无人区地块(永久中立的不并)
    private static bool Fragmentable(int id, int size) =>
        id >= 0 && id < size && _zones[id] != null && Index(_zones[id]) >= 0 && !IsNeutral(id);

    // 挨着的那块地归哪国：城的地块按城，别的无人区按本来的归属，剧本史实领土按史实；都不是为 null
    private static Kingdom NeighbourOwner(TileZone next, int size)
    {
        Kingdom kingdom;
        City city = next.city;
        if (city != null) kingdom = city.data != null && !city.isRekt() ? city.kingdom : null;
        else if (Index(next) >= 0) kingdom = IsNeutral(next.id) ? null : _baseOwner[next.id];
        else kingdom = WorldMap.WorldMapWilderness.KnownOwner(next);
        return kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild && !kingdom.isNeutral() ? kingdom : null;
    }

    #endregion
}
