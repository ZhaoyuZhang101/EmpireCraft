using System;
using System.Collections.Generic;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Terminal;

namespace EmpireCraft.Scripts.GeneralSystems;

// 编年(用户 2026-11-16 "我作为管理员希望能看到世界历史")：世界上发出的每条世界消息(开战议和、换君、立国亡国、称帝篡位、
// 各系统报的事)按发出时的文字记一笔，随模组存档。终端管理员页的「史」读它(见 TerminalHost.History)。
//   · 文字在发出那一刻定下来(以后改国号、人死了不跟着变)，去掉颜色标记；
//   · 大事 = 别国也听得到的事(ScreenFeedPatch.PublicNews)，管理员页可只看大事；
//   · 同一句话同一个月只记一次(同一件事几处一起报)；
//   · 分类(用户 2026-11-16 "世界历史好歹分一下类啊""都挤在一起我怎么看")：战事、君位、兴亡、邦交、朝政、刑狱(判案拘捕，一年几十条，单列免得淹了朝政)、宗教、文教、民生、灾异、史事(预设剧本的剧情)、其他；
//     原版和模组登记过的消息种类按种类定，模组通用事件按是哪个系统发的(调用栈上第一个模组类型的名字)定，见 Classify；
//   · 至多 Cap 条(一年四五十条)，满了先丢最早的小事，大事留到 MajorCap 条才丢最早的大事——几百年后还查得到开头那些朝代兴亡。
// 只做字符串处理，不扫地图、不碰别的对象。
[Serializable]
public sealed class ChronicleEntry
{
    // 世界时刻(getCurWorldTime)
    public double t;
    public string text = "";
    public bool major;
    public long kingdom = -1L;
    // 类别(WorldChronicle.Kinds 之一；旧档没有的按"其他")
    public string kind;
    // 终端管理员页抄好的那一行(不存档；丢了别的条目重排时不用重算)
    [NonSerialized] internal object View;
}

public static class WorldChronicle
{
    public const int Cap = 4000, MajorCap = 2500;
    private static List<ChronicleEntry> _entries = new();
    private static int _majors;
    // 本月已记过的句子(同月去重)
    private static readonly HashSet<string> ThisMonth = new(StringComparer.Ordinal);
    private static int _month = int.MinValue;

    public static IReadOnlyList<ChronicleEntry> Entries => _entries;
    // 记过多少条(含丢掉的)：管理员页按它判断有没有新的
    public static long Stamp { get; private set; }
    // 丢过几回(丢的可能在中间)：管理员页按它判断要不要整份重抄
    public static long Trims { get; private set; }

    public static void ResetWorldState()
    {
        _entries = new List<ChronicleEntry>();
        ThisMonth.Clear();
        _month = int.MinValue;
        _majors = 0;
        Stamp = Trims = 0;
    }

    public static List<ChronicleEntry> Save() => _entries;

    public static void Load(List<ChronicleEntry> saved)
    {
        ResetWorldState();
        if (saved == null) return;
        foreach (ChronicleEntry entry in saved)
            if (entry != null && !string.IsNullOrWhiteSpace(entry.text))
            {
                _entries.Add(entry);
                if (entry.major) _majors++;
            }
        while (_entries.Count > Cap) Trim();
        Stamp = _entries.Count;
    }

    // 世界消息发出时(ScreenFeedPatch.BeforeNewHistory)
    public static void Note(WorldLogMessage message)
    {
        if (message == null || World.world == null) return;
        try
        {
            string text = TerminalIntel.Strip(message.GetEmpireHistoryFormatedText())?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            int month = Date.getCurrentYear() * 12 + Date.getCurrentMonth();
            if (month != _month)
            {
                _month = month;
                ThisMonth.Clear();
            }
            if (!ThisMonth.Add(text)) return;
            bool modEvent = message.asset_id == EmpireCraftWorldLogLibrary.empirecraft_event_log?.id;
            Kingdom about = modEvent ? ScreenFeedPatch.PendingKingdom ?? message.kingdom : message.kingdom;
            bool major = ScreenFeedPatch.PublicNews(message);
            _entries.Add(new ChronicleEntry
            {
                t = World.world.getCurWorldTime(), text = text, major = major, kingdom = about?.data != null ? about.getID() : -1L,
                kind = modEvent ? KindOfCaller() : KindOfAsset(message.asset_id)
            });
            if (major) _majors++;
            if (_entries.Count > Cap) Trim();
            Stamp++;
        }
        catch (Exception)
        {
            // 这条读不出来就不记
        }
    }

    // ───────── 分类 ─────────
    public const string War = "war", Throne = "throne", Realm = "realm", Diplomacy = "dipl", Court = "court", Law = "law", Faith = "faith",
        Culture = "culture", Economy = "econ", Disaster = "disaster", Story = "story", Other = "other";
    public static readonly string[] Kinds = { War, Throne, Realm, Diplomacy, Court, Law, Faith, Culture, Economy, Disaster, Story, Other };

    // 原版与模组登记的消息种类：按种类名里的字眼，先到先得(宗教战争算宗教、王族换支算君位)
    private static readonly (string word, string kind)[] AssetWords =
    {
        ("disaster", Disaster), ("religion", Faith), ("war", War), ("attack", War), ("occupation", War), ("surrender", War),
        ("king_", Throne), ("heir", Throne), ("posthumous", Throne), ("year_name", Throne), ("minister", Throne), ("empress", Throne),
        ("royal_clan", Throne), ("new_emperor", Throne),
        ("alliance", Diplomacy), ("tribut", Diplomacy), ("朝贡", Diplomacy), ("peerage", Diplomacy),
        ("culture", Culture), ("cultural", Culture), ("ideology", Culture), ("regime", Culture), ("composite", Culture),
        ("faction", Court), ("officer", Court), ("law", Law), ("arrest", Law), ("罪行", Law), ("派系", Court), ("品级", Court), ("clan", Court),
        ("jingshi", Court), ("greater_general", Court),
        ("kingdom", Realm), ("city", Realm), ("empire", Realm), ("title", Realm), ("become", Realm), ("restore", Realm),
        ("combine", Realm), ("province", Realm), ("pianan", Realm), ("crash", Realm), ("division", Realm), ("enfeoff", Realm),
        ("fief", Realm), ("capital", Realm), ("land", Realm), ("controlled", Realm), ("cotrolled", Realm),
    };

    // 模组系统：按类型名里的字眼，先到先得(帝国律令算朝政、军事政变算君位，都排在"帝国""军"前面)
    private static readonly (string word, string kind)[] TypeWords =
    {
        ("ScenarioEvent", Story), ("Famine", Disaster), ("Disaster", Disaster), ("Plague", Disaster),
        ("Religio", Faith), ("Papa", Faith), ("Papal", Faith), ("Caliph", Faith), ("Islam", Faith), ("Faith", Faith), ("Syncretism", Faith),
        ("Persecution", Faith), ("Crusade", Faith),
        ("Succession", Throne), ("Interregnum", Throne), ("Harem", Throne), ("Prince", Throne), ("Posthumous", Throne),
        ("RoyalHouse", Throne), ("Regency", Throne), ("Coup", Throne), ("ConsortKin", Throne), ("PalaceArmy", Throne), ("CheckKing", Throne),
        ("Heir", Throne), ("Throne", Throne),
        ("EmpireLaw", Law), ("Sentenc", Law), ("Crime", Law), ("Justice", Law), ("Faction", Court), ("Clique", Court), ("Party", Court), ("Republic", Court), ("Constitutional", Court),
        ("Gentry", Court), ("Clan", Court), ("Office", Court), ("Institution", Court), ("Purge", Court), ("Grace", Court),
        ("PublicOpinion", Court), ("Guoye", Court), ("Slavery", Court), ("EthnicRank", Court), ("Talent", Court), ("Bakufu", Court),
        ("Court", Court), ("Decision", Court), ("Claim", Court), ("Character", Court), ("Favourite", Court), ("Favorite", Court),
        ("Diplomacy", Diplomacy), ("Tribut", Diplomacy), ("Jimi", Diplomacy), ("Vassal", Diplomacy), ("Marriage", Diplomacy),
        ("Frontier", Diplomacy), ("GreatPower", Diplomacy), ("RoadTreaty", Diplomacy), ("Submission", Diplomacy), ("Alliance", Diplomacy),
        ("MilitaryAccess", Diplomacy),
        ("War", War), ("Corps", War), ("Front", War), ("Encirclement", War), ("Massacre", War), ("ScorchedEarth", War), ("Capture", War),
        ("Occupation", War), ("Logistics", War), ("Nuclear", War), ("NativeDefense", War), ("Raid", War), ("Peace", War),
        ("Conquest", War), ("Feud", War), ("Banner", War), ("Rebellion", War), ("Custody", War), ("Reparation", War),
        ("Culture", Culture), ("Ideolog", Culture), ("School", Culture), ("Tech", Culture), ("Landmark", Culture), ("Legacy", Culture),
        ("Moderniz", Culture),
        ("ShuiTu", Economy), ("HarshRule", Economy), ("Market", Economy), ("Commodity", Economy), ("Economy", Economy), ("Mineral", Economy), ("Wilderness", Economy),
        ("CityPlacement", Economy), ("Settlement", Economy), ("Fiscal", Economy), ("Treasury", Economy), ("Building", Economy),
        ("DynasticCycle", Realm), ("Empire", Realm), ("Unification", Realm), ("Tianxia", Realm), ("Seal", Realm), ("Legitimacy", Realm), ("Naming", Realm),
        ("Retreat", Realm), ("Garrison", Realm), ("Exile", Realm), ("Puppet", Realm), ("Zhou", Realm), ("StateFormation", Realm),
        ("HolyRoman", Realm), ("Nomad", Realm), ("LateImperial", Realm), ("Emirate", Realm), ("Steppe", Realm), ("Boundary", Realm),
        ("Exclave", Realm), ("Colony", Realm), ("Feudal", Realm), ("City", Realm), ("Kingdom", Realm), ("Realm", Realm),
    };

    // 同一个系统里分得出来的几样：按这个类型在栈上的几层方法名(草原诸部"争草场"是 SteppeOrderSystem.DeclareFeud 发的；战时拜将、拜粮官是 WarCommandSystem.Appoint，算任免)
    private static readonly (string word, string kind)[] MethodWords =
    {
        ("Appoint", Court), ("Feud", War), ("Raid", War), ("Crusade", Faith), ("Marriage", Diplomacy), ("Tribute", Diplomacy)
    };

    // 转手发消息的(不是事情本身的出处)
    private static readonly HashSet<string> Relays = new(StringComparer.Ordinal)
    {
        nameof(WorldChronicle), nameof(ScreenFeedPatch), nameof(TranslateHelper), nameof(EventRecorder)
    };

    private static readonly Dictionary<string, string> AssetKinds = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> TypeKinds = new();

    public static string KindOfAsset(string id)
    {
        if (string.IsNullOrEmpty(id)) return Other;
        if (AssetKinds.TryGetValue(id, out string kind)) return kind;
        kind = Other;
        foreach (var (word, to) in AssetWords)
            if (id.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = to;
                break;
            }
        if (AssetKinds.Count < 1024) AssetKinds[id] = kind;
        return kind;
    }

    // 模组通用事件：调用栈上第一个不是转手的模组类型(嵌套的、编译器生成的取最外层)。一年几十条，取一次栈不碍事
    private static string KindOfCaller()
    {
        try
        {
            var frames = new global::System.Diagnostics.StackTrace(1, false).GetFrames();
            if (frames == null) return Other;
            Type source = null;
            foreach (var frame in frames)
            {
                var method = frame?.GetMethod();
                Type type = method?.DeclaringType;
                while (type?.DeclaringType != null) type = type.DeclaringType;
                if (source != null)
                {
                    if (type != source) break;
                }
                else if (type == null || type.Namespace == null || !type.Namespace.StartsWith("EmpireCraft", StringComparison.Ordinal) ||
                         Relays.Contains(type.Name)) continue;
                source = type;
                string name = method.Name;
                foreach (var (word, to) in MethodWords)
                    if (name.IndexOf(word, StringComparison.Ordinal) >= 0)
                        return to;
            }
            if (source != null) return KindOfType(source);
        }
        catch (Exception)
        {
            // 取不到栈就算其他
        }
        return Other;
    }

    public static string KindOfType(Type type)
    {
        if (TypeKinds.TryGetValue(type, out string kind)) return kind;
        kind = KindOfTypeName(type.Name);
        TypeKinds[type] = kind;
        return kind;
    }

    public static string KindOfTypeName(string name)
    {
        foreach (var (word, to) in TypeWords)
            if (name.IndexOf(word, StringComparison.Ordinal) >= 0)
                return to;
        return Other;
    }

    // 丢一条：大事还没超额就丢最早的小事，否则丢最早的一条
    private static void Trim()
    {
        int at = 0;
        if (_majors <= MajorCap)
            for (int i = 0; i < _entries.Count; i++)
                if (!_entries[i].major)
                {
                    at = i;
                    break;
                }
        if (_entries[at].major) _majors--;
        _entries.RemoveAt(at);
        Trims++;
    }
}
