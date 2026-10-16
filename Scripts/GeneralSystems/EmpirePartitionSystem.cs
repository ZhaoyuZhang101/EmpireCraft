using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using Newtonsoft.Json;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 帝国分裂为两朝(东西或南北，按两朝都城的相对方位)：
//   ① 遗诏分治(东西罗马式)：疆域辽阔的大帝国，年过五十的皇帝有两个以上成年儿子时，可能诏分天下为二——
//      储君留守京城这一半，另一子在另一半的大城(受封的皇子优先用自己的封国)称帝。两朝同一国号加方位、同一宗室，
//      起初是兄弟之国：互不征伐，一方遭外敌攻打另一方出兵相助；四十年后(或一方被外姓篡夺)情谊渐疏，互争正统，
//      强的一方可以发动统一之战，胜者并吞另一朝；一方皇族绝嗣，群臣可以迎立另一朝的皇帝兼承大统，天下复归一统。
//   ② 权臣分立(东西魏式)：权臣挟制皇帝，远方另有手握重兵、不与权臣同派的强藩——或皇帝不堪挟制出奔强藩、
//      权臣在京城另立宗室；或强藩在自己的地盘拥立一位宗室。两朝同一国号加方位，各由权臣把持，互争正统、开战。
//   ③ 宗室另立(篡位后)：外姓篡位时，先朝宗室有一定可能(看原来的法统)在离篡位者都城最远的那一半起兵称帝，
//      延续旧国号加方位；已经叛离篡位者的宗室诸王归附。互争正统、开战。下个月起事。
// 划分：以两朝都城为两极，成员国按自己的都城离哪边近整国划走，京畿(核心王国)的城逐城划分。
// 玩家反馈(2026-10-16)："并立的概率调低点，下调大概一半""并立之后很容易分不清""分裂出来的那个就全靠他一个人，
//   按理来说会有一部分宗室追随他……很容易断代，然后直接被吞并"：三种分立的概率都减半；另一朝换一个和原朝相差最大的颜色；
//   宗室随新君而去(见 Followers)——新君的妻妾子孙全随行，兄弟一家各一半，留在原朝京城的宗室三成投奔，
//   在新朝疆域里的宗室本就归新朝。
// 玩家反馈："小皇帝跑路了就自己找出来一个皇叔……那个皇叔也是乖乖的把大权拱手相让""小皇帝跑到地方了以后也会立刻找一个
//   有能力的大臣让他当权臣""做成皇帝成年以后再出奔吧""出奔弄三种随机结果：成功跑路(并立)、反杀权臣(还政)、被废黜"：
//   · 皇帝年满二十才谋出奔；出奔三种结局(见 FleeOutcome)：成功出奔两朝并立、反杀权臣亲政、事泄被废(宗室无人可立就天下散伙)；
//   · 出奔成功，强藩起初只是辅政(控制三成)，庸弱昏聩之君才一来就被挟制；
//   · 权臣立的傀儡先挑京城里现成的宗室，再挑年幼的(五到十五岁)，不再随手从族谱里拉出一位成年皇叔。
public static class EmpirePartitionSystem
{
    public const string KindWill = "will", KindMinister = "minister", KindRestoration = "restoration";
    private const int AmityYears = 40, CooldownYears = 30, WillMinCities = 14, WillMinAge = 50, PendingMonths = 1;
    private const float WillBaseChance = 0.01f, MinisterChance = 0.06f, WillSpanShare = 0.3f, StrongmanDistanceShare = 0.3f,
        StrongmanMightShare = 0.15f;

    // 篡位后待起事的先朝宗室(帝国 id → 先朝宗族、旧国号、篡位时间)
    private static readonly Dictionary<long, (long clan, string name, double at)> Restorations = new();

    #region 查询

    public static Empire Twin(Empire empire)
    {
        long id = empire?.data?.partition_twin_id ?? -1L;
        if (id <= 0 || ModClass.EMPIRE_MANAGER == null) return null;
        Empire twin = ModClass.EMPIRE_MANAGER.get(id);
        return twin?.data == null || twin.isRekt() || twin.IsArchived() || twin.data.partition_twin_id != empire.data.id ? null : twin;
    }

    private static Empire RealmOf(Kingdom kingdom) =>
        kingdom == null ? null : kingdom.IsEmpire() || kingdom.IsInEmpire() ? kingdom.GetEmpire() : null;

    // 兄弟之国(遗诏分治后的四十年)：两朝之间不开战
    public static bool InAmity(Kingdom first, Kingdom second)
    {
        Empire a = RealmOf(first), b = RealmOf(second);
        return a != null && b != null && a != b && a.data.partition_amity && Twin(a) == b;
    }

    private static bool Eligible(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt() || core.capital == null || !SuccessionCouncilSystem.Applies(core)) return false;
        // 开国气象(统一战争期间、统一后三十年)不分治、不分立(见 UnificationSystem)
        if (UnificationSystem.InGrace(core)) return false;
        if (Twin(empire) != null) return false;
        double since = empire.data.partition_time;
        return since < 0d || Date.getYearsSince(since) >= CooldownYears;
    }

    private static float Span(IEnumerable<City> cities, out bool horizontal)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (City city in cities)
        {
            if (city?.data == null) continue;
            Vector2 at = city.city_center;
            minX = Mathf.Min(minX, at.x);
            maxX = Mathf.Max(maxX, at.x);
            minY = Mathf.Min(minY, at.y);
            maxY = Mathf.Max(maxY, at.y);
        }
        horizontal = maxX - minX >= maxY - minY;
        return minX > maxX ? 0f : Mathf.Max(maxX - minX, maxY - minY);
    }

    private static List<City> Cities(Empire empire) =>
        empire.kingdoms_list.Where(kingdom => kingdom != null && !kingdom.isRekt())
            .SelectMany(kingdom => kingdom.cities ?? new List<City>())
            .Where(city => city?.data != null && !city.isRekt()).Distinct().ToList();

    private static float Might(Kingdom kingdom) =>
        kingdom == null || kingdom.isRekt() ? 0f
            : kingdom.countTotalWarriors() + (kingdom.cities ?? new List<City>()).Sum(city => CorpsSystem.EquivalentAt(city));

    // 两朝都城的相对方位：东西相距远就分东西，南北相距远就分南北
    private static string Side(Vector2 seat, Vector2 capital)
    {
        float dx = seat.x - capital.x, dy = seat.y - capital.y;
        if (Mathf.Abs(dx) >= Mathf.Abs(dy)) return dx > 0f ? "Eastern" : "Western";
        return dy > 0f ? "Northern" : "Southern";
    }

    private static string Opposite(string side) => side switch
    {
        "Eastern" => "Western",
        "Western" => "Eastern",
        "Northern" => "Southern",
        _ => "Northern"
    };

    #endregion

    #region 划分

    private sealed class Plan
    {
        public List<Kingdom> Members = new();
        public List<City> CoreCities = new();
        public int Moved, Total;
        public float Share => Total <= 0 ? 0f : (float)Moved / Total;
    }

    // 以京城、新都为两极：离新都近的成员国整国划走，京畿的城逐城划分
    private static Plan Divide(Empire source, City capital, City seat)
    {
        var plan = new Plan();
        Kingdom core = source.CoreKingdom;
        Vector2 a = capital.city_center, b = seat.city_center;
        bool Nearer(City city) => city != null && (city == seat ||
                                                   (city.city_center - b).sqrMagnitude < (city.city_center - a).sqrMagnitude);
        foreach (Kingdom kingdom in source.kingdoms_list.ToList())
        {
            if (kingdom == null || kingdom.isRekt() || kingdom == core) continue;
            int count = kingdom.cities?.Count ?? 0;
            plan.Total += count;
            City anchor = kingdom.capital ?? kingdom.cities?.FirstOrDefault();
            if (anchor == null || !Nearer(anchor)) continue;
            plan.Members.Add(kingdom);
            plan.Moved += count;
        }
        foreach (City city in core.cities?.ToList() ?? new List<City>())
        {
            if (city?.data == null || city.isRekt()) continue;
            plan.Total++;
            if (city == capital || !Nearer(city)) continue;
            plan.CoreCities.Add(city);
            plan.Moved++;
        }
        if (seat.kingdom != null && seat.kingdom != core && !plan.Members.Contains(seat.kingdom)) plan.Moved++;
        return plan;
    }

    // 新都：离京城最远的那一部分城里最大的(地块最多)，且划分后新朝占的比例在给定范围内
    private static City FarSeat(Empire source, Vector2 from, float minShare, float maxShare, out Plan plan)
    {
        plan = null;
        City capital = source.CoreKingdom.capital;
        List<City> cities = Cities(source).Where(city => city != capital).ToList();
        if (cities.Count < 3) return null;
        foreach (City seat in cities.OrderByDescending(city => (city.city_center - from).sqrMagnitude)
                     .Take(Mathf.Max(3, cities.Count / 2))
                     .OrderByDescending(city => city.zones?.Count ?? 0))
        {
            Plan candidate = Divide(source, capital, seat);
            if (candidate.Share < minShare || candidate.Share > maxShare) continue;
            plan = candidate;
            return seat;
        }
        return null;
    }

    #endregion

    #region 分立

    // 在 seat 立 ruler 为另一朝皇帝；ownKingdom 不为空时(受封皇子、宗室诸王)以他自己的王国为新朝核心
    private static Empire Split(Empire source, Actor ruler, City seat, Plan plan, string kind, string note,
        string dynastyName, bool renameSource, Kingdom ownKingdom = null)
    {
        Kingdom sourceCore = source.CoreKingdom;
        City capital = sourceCore?.capital;
        if (ruler?.data == null || ruler.isRekt() || !ruler.isAlive() || seat?.data == null || capital == null) return null;
        string side = Side(seat.city_center, capital.city_center);
        Kingdom newCore;
        if (ownKingdom != null)
        {
            newCore = ownKingdom;
            plan.Members.Remove(newCore);
            if (newCore.IsInEmpire()) newCore.GetEmpire()?.leave(newCore, isLeave: false);
        }
        else
        {
            // 原来是别国君主、城主的先卸下
            if (ruler.isKing() && ruler.kingdom != null) ruler.kingdom.removeKing();
            if (ruler.isCityLeader() && ruler.city != null) ruler.city.removeLeader();
            ruler.removeFromArmy();
            // 新都所在的成员国：新都那座城自立为新朝核心，其余的城仍按划分随成员国走
            plan.CoreCities.Remove(seat);
            newCore = seat.makeOwnKingdom(ruler);
            if (newCore == null) return null;
            newCore.setCapital(seat);
            ruler.joinCity(seat);
        }
        newCore.SetRegimeType(sourceCore.GetOrCreate().regimeType);
        newCore.LoadRegime();
        newCore.SetSuccessionLaw(sourceCore.GetSuccessionLaw());
        PrinceStruggleSystem.Note(newCore, ruler, note);
        Empire twin = ModClass.EMPIRE_MANAGER.NewEmpire(newCore, isSplit: true, allowCultureRival: true, forceNewCore: true,
            suppressFoundingLog: true);
        if (twin?.data == null)
        {
            LogService.LogWarning($"[EmpireCraft][分裂] {source.GetEmpireFullName()} 分立失败：{newCore.name} 未能称帝");
            return null;
        }
        Inherit(source, twin, ruler);
        foreach (Kingdom kingdom in plan.Members)
            if (kingdom != null && !kingdom.isRekt() && kingdom.GetEmpire() == source)
                twin.join(kingdom, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        foreach (City city in plan.CoreCities)
            if (city?.data != null && !city.isRekt() && city.kingdom == sourceCore) city.joinAnotherKingdom(newCore);
        // 国号：同一国号，各加方位
        string name = string.IsNullOrWhiteSpace(dynastyName) ? source.data.core_name : dynastyName;
        twin.data.directPre = side;
        twin.SetEmpireName(name);
        if (renameSource)
        {
            source.data.directPre = Opposite(side);
            source.SetEmpireName(source.data.core_name);
        }
        double now = World.world.getCurWorldTime();
        twin.data.partition_twin_id = source.data.id;
        source.data.partition_twin_id = twin.data.id;
        twin.data.partition_kind = source.data.partition_kind = kind;
        twin.data.partition_time = source.data.partition_time = now;
        twin.data.partition_claimant = true;
        source.data.partition_claimant = false;
        DistinctColor(source, twin);
        Followers(source, twin, ruler, seat);
        // 世家择主(见 GentrySystem)
        GentrySystem.ChooseSides(new[] { source, twin });
        twin.recalculate();
        source.recalculate();
        World.world.zone_calculator?.dirtyAndClear();
        return twin;
    }

    // 另一朝换一个和原朝相差最大的颜色(以前新朝多半沿用受封王国的颜色，跟原朝同一色系，地图上分不清)
    private static void DistinctColor(Empire source, Empire twin)
    {
        try
        {
            List<ColorAsset> colors = AssetManager.kingdom_colors_library?.list;
            ColorAsset own = source.getColor();
            Kingdom core = twin.CoreKingdom;
            if (colors == null || colors.Count == 0 || own == null || core == null) return;
            Color main = own.getColorMain();
            var taken = new HashSet<ColorAsset>(ModClass.EMPIRE_MANAGER.Where(empire => empire?.data != null && empire != twin &&
                !empire.isRekt()).Select(empire => empire.getColor()).Where(color => color != null));
            float Distance(ColorAsset color)
            {
                Color other = color.getColorMain();
                return Mathf.Abs(other.r - main.r) + Mathf.Abs(other.g - main.g) + Mathf.Abs(other.b - main.b);
            }
            ColorAsset best = colors.Where(color => color != null && !taken.Contains(color)).OrderByDescending(Distance)
                .FirstOrDefault() ?? colors.Where(color => color != null).OrderByDescending(Distance).First();
            core.updateColor(best);
            twin.updateColor(best);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][分裂] 换色失败: {exception.Message}");
        }
    }

    private const float SiblingFollowShare = 0.5f, CourtFollowShare = 0.3f;

    // 宗室随新君而去：妻妾子孙全随行，兄弟一家各一半，留在原朝京城的宗室三成；在新朝疆域里的本就归新朝
    private static void Followers(Empire source, Empire twin, Actor ruler, City seat)
    {
        PersonalClanIdentity self = ruler?.GetPersonalIdentity();
        SpecificClan clan = ruler?.GetSpecificClan() ?? source.EmpireSpecificClan;
        if (self == null || clan == null || seat?.data == null) return;
        Kingdom newCore = twin.CoreKingdom;
        City court = source.CoreKingdom?.capital;
        var family = new HashSet<long>();
        void AddLine(PersonalClanIdentity person, int depth)
        {
            if (person == null || depth > 3 || !family.Add(person.id)) return;
            foreach (long child in person.children ?? new List<long>()) AddLine(SpecificClanManager.getPerson(child), depth + 1);
        }
        AddLine(self, 0);
        if (self.lover.identity > 0) family.Add(self.lover.identity);
        foreach ((long _, long concubine) in self.concubines ?? new List<(long, long)>()) family.Add(concubine);
        int moved = 0;
        foreach (PersonalClanIdentity person in clan.SnapshotPeople())
        {
            if (person == null || !person.is_alive || person.id == self.id) continue;
            Actor actor = person.is_virtual ? null : person._actor;
            if (actor != null && (actor.isKing() || actor.IsEmperor() || actor.isCityLeader() || actor.IsOnOffice())) continue;
            bool sibling = self.father > 0 && person.father == self.father || self.mother > 0 && person.mother == self.mother;
            long cityId = actor?.city?.data?.id ?? person.virtual_city_id;
            bool atCourt = court?.data != null && cityId == court.data.id;
            bool follows = family.Contains(person.id) ||
                           sibling && UnityEngine.Random.value < SiblingFollowShare ||
                           atCourt && UnityEngine.Random.value < CourtFollowShare;
            if (!follows) continue;
            if (sibling)
                foreach (long child in person.children ?? new List<long>()) family.Add(child);
            if (actor != null)
            {
                actor.joinCity(seat);
                actor.setKingdom(newCore);
            }
            else person.virtual_city_id = seat.data.id;
            moved++;
        }
        if (moved <= 0) return;
        string text = string.Format(LM.Get("partition_followers_history"), moved, ruler.getName(), seat.GetCityName());
        twin.RecordHistory(directContent: text, actorId: ruler.id);
        LogService.LogInfo("[EmpireCraft][分裂] " + text);
    }

    // 新朝承袭：史书、王号序数、制度、税率、国教、开国之君、法统
    private static void Inherit(Empire source, Empire twin, Actor ruler)
    {
        twin.data.history.InsertRange(0, source.data.history ?? new List<EmpireCraftHistory>());
        twin.data.regnal_records.InsertRange(0, source.data.regnal_records ?? new List<RegnalRecord>());
        RegnalNameService.OnEmperorCrowned(twin, ruler, recompute: true);
        twin.data.dynasty_founder_actor_id = source.data.dynasty_founder_actor_id;
        twin.data.founder_actor_id = source.data.founder_actor_id;
        twin.data.founder_actor_name = source.data.founder_actor_name;
        twin.data.western_ordinal = source.data.western_ordinal;
        twin.data.TaxRate = source.data.TaxRate;
        twin.data.Religion = source.data.Religion;
        twin.data.heir_type = source.data.heir_type;
        twin.data.long_posthumous = source.data.long_posthumous;
        twin.data.show_long_posthumous = source.data.show_long_posthumous;
        twin.data.ruling_culture = source.data.ruling_culture;
        twin.data.institutional_culture = source.data.institutional_culture;
        twin.data.Mandate = Mathf.Max(30, source.data.Mandate - 15);
        try
        {
            twin.data.institution_state = Clone(source.data.institution_state) ?? twin.data.institution_state;
            twin.data.constitutional_economy = Clone(source.data.constitutional_economy) ?? twin.data.constitutional_economy;
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][分裂] 承袭制度失败: {exception.Message}");
        }
    }

    private static T Clone<T>(T value) where T : class =>
        value == null ? null : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));

    private static void Publish(Empire first, Empire second, string text, Actor actor)
    {
        first?.RecordHistory(directContent: text, actorId: actor?.id ?? -1L);
        second?.RecordHistory(directContent: text, actorId: actor?.id ?? -1L);
        TranslateHelper.LogEventMessage(text, (first ?? second)?.CoreKingdom);
    }

    #endregion

    #region 每年、每月(RoyalHouseSystem.Tick)

    public static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived()) return;
        Empire twin = Twin(empire);
        if (twin == null)
        {
            if (empire.data.partition_twin_id > 0)
            {
                empire.data.partition_twin_id = -1L;
                empire.data.partition_amity = false;
            }
        }
        else if (empire.data.partition_amity) CheckAmity(empire, twin);
        TryWill(empire);
        TryMinister(empire);
    }

    public static void Monthly()
    {
        if (Restorations.Count == 0 || World.world == null) return;
        foreach (var pair in Restorations.ToList())
        {
            if (Date.getMonthsSince(pair.Value.at) < PendingMonths) continue;
            Restorations.Remove(pair.Key);
            try
            {
                Restore(ModClass.EMPIRE_MANAGER?.get(pair.Key), pair.Value.clan, pair.Value.name);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][分裂] 宗室另立失败: {exception}");
            }
        }
    }

    #endregion

    #region ① 遗诏分治

    private static void TryWill(Empire empire)
    {
        if (!Eligible(empire)) return;
        Actor emperor = empire.Emperor;
        Kingdom core = empire.CoreKingdom;
        if (emperor?.data == null || !emperor.isAlive() || !emperor.isAdult() || emperor.getAge() < WillMinAge ||
            empire.data.is_been_controlled) return;
        List<City> cities = Cities(empire);
        if (cities.Count < WillMinCities) return;
        float span = Span(cities, out _);
        float mapSize = Mathf.Max(MapBox.width, MapBox.height);
        if (mapSize <= 0f || span < mapSize * WillSpanShare) return;
        Actor heir = core.HasHeir() ? core.GetHeir() : null;
        bool allowFemale = PersonalUnionService.AllowsFemaleSuccession(core);
        List<Actor> sons = (emperor.getChildren(false) ?? Enumerable.Empty<Actor>())
            .Where(child => child?.data != null && !child.isRekt() && child.isAlive() && child.isAdult() &&
                            (allowFemale || child.isSexMale()) && !child.IsEmperor() && RealmOf(child.kingdom) == empire)
            .ToList();
        if (sons.Count < 2) return;
        heir ??= sons.OrderByDescending(son => son.getAge()).First();
        float chance = WillBaseChance;
        RegimeType? regime = core.GetRegime()?.type;
        if (regime == RegimeType.LvLing) chance *= 0.5f;
        else if (regime == RegimeType.YouMu) chance *= 2f;
        else if (regime == RegimeType.Feudalism) chance *= 1.5f;
        if (cities.Count >= 24) chance *= 1.5f;
        if (span >= mapSize * 0.45f) chance *= 1.5f;
        chance *= RulerQualitySystem.Of(emperor) switch
        {
            RulerQualitySystem.Grade.Wise => 0.5f,
            RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous => 1.5f,
            _ => 1f
        };
        if (UnityEngine.Random.value >= chance) return;

        City capital = core.capital;
        Actor ruler = null;
        City seat = null;
        Plan plan = null;
        Kingdom ownKingdom = null;
        // 受封在远方的皇子以自己的封国称帝
        foreach (Actor son in sons.Where(son => son != heir).OrderByDescending(son => son.renown))
        {
            Kingdom fief = son.isKing() ? son.kingdom : null;
            if (fief?.capital == null || fief.GetEmpire() != empire || fief == core) continue;
            Plan candidate = Divide(empire, capital, fief.capital);
            if (candidate.Share < 0.25f || candidate.Share > 0.6f) continue;
            (ruler, seat, plan, ownKingdom) = (son, fief.capital, candidate, fief);
            break;
        }
        if (ruler == null)
        {
            ruler = sons.Where(son => son != heir && !son.isKing()).OrderByDescending(son => son.renown).FirstOrDefault();
            seat = ruler == null ? null : FarSeat(empire, capital.city_center, 0.25f, 0.6f, out plan);
        }
        if (ruler == null || seat == null || plan == null) return;
        string emperorName = emperor.getName(), heirName = heir.getName(), seatName = seat.GetCityName();
        Empire twin = Split(empire, ruler, seat, plan, KindWill, PrinceStruggleSystem.NotePartition, empire.data.core_name,
            renameSource: true, ownKingdom);
        if (twin == null) return;
        empire.data.partition_amity = twin.data.partition_amity = true;
        Publish(empire, twin, string.Format(LM.Get("partition_will_history"), emperorName, heirName, ruler.getName(), seatName,
            empire.GetEmpireFullName(), twin.GetEmpireFullName()), emperor);
    }

    // 兄弟之国到期(四十年)，或一方已被外姓篡夺：情谊渐疏，互争正统
    private static void CheckAmity(Empire empire, Empire twin)
    {
        bool expired = empire.data.partition_time >= 0d && Date.getYearsSince(empire.data.partition_time) >= AmityYears;
        bool sameHouse = SpecificClanManager.SameLineage(empire.EmpireSpecificClan, twin.EmpireSpecificClan);
        if (!expired && sameHouse) return;
        empire.data.partition_amity = twin.data.partition_amity = false;
        Empire claimant = empire.data.partition_claimant ? empire : twin;
        Empire incumbent = claimant == empire ? twin : empire;
        ImperialLegitimacyChallengeService.DeclareDynasticRivals(claimant, incumbent, war: false);
        Publish(empire, twin, string.Format(LM.Get("partition_amity_end_history"), empire.GetEmpireFullName(),
            twin.GetEmpireFullName()), null);
    }

    // 兄弟之国遭外敌攻打：另一朝出兵相助(WarPatch.new_war)
    public static void OnWarStarted(War war)
    {
        Kingdom defender = war?.main_defender, attacker = war?.main_attacker;
        Empire realm = RealmOf(defender);
        if (realm == null || !realm.data.partition_amity) return;
        Empire twin = Twin(realm);
        Kingdom ally = twin?.CoreKingdom;
        if (ally == null || ally.isRekt() || RealmOf(attacker) == twin || RealmOf(attacker) == realm ||
            war.hasKingdom(ally) || ally.isInWarWith(attacker)) return;
        war.joinDefenders(ally);
        string text = string.Format(LM.Get("partition_aid_history"), twin.GetEmpireFullName(), realm.GetEmpireFullName());
        twin.RecordHistory(directContent: text);
        TranslateHelper.LogEventOnly(text, ally);
    }

    #endregion

    #region ② 权臣分立

    private static void TryMinister(Empire empire)
    {
        if (!Eligible(empire)) return;
        Actor emperor = empire.Emperor;
        Actor minister = empire.GetPowerfulMinister();
        Kingdom core = empire.CoreKingdom;
        if (emperor?.data == null || !emperor.isAlive() || minister?.data == null || !minister.isAlive() ||
            !empire.data.is_been_controlled && empire.data.powerful_minister_progress < 50) return;
        List<City> cities = Cities(empire);
        if (cities.Count < 8) return;
        float span = Span(cities, out _);
        float total = empire.kingdoms_list.Sum(Might);
        City capital = core.capital;
        FixedFaction ministerFaction = minister.GetFaction();
        // 强藩：远方、兵多(或带兵的统帅、节度使、都护)、不与权臣同派的成员国君主
        Kingdom bastion = empire.kingdoms_list
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != core && kingdom.capital != null &&
                              kingdom.king?.data != null && kingdom.king.isAlive() && kingdom.king.isAdult() &&
                              kingdom.king != minister && (ministerFaction == null || kingdom.king.GetFaction() != ministerFaction) &&
                              Vector2.Distance(kingdom.capital.city_center, capital.city_center) >= span * StrongmanDistanceShare &&
                              (Might(kingdom) >= total * StrongmanMightShare || CorpsSystem.IsGeneral(kingdom.king) ||
                               kingdom.GetKingdomType() is KingdomType.LvLing_jiedushi or KingdomType.LvLing_duhufu))
            .OrderByDescending(Might).FirstOrDefault();
        if (bastion == null || UnityEngine.Random.value >= MinisterChance) return;
        Plan plan = Divide(empire, capital, bastion.capital);
        if (plan.Share < 0.15f || plan.Share > 0.7f) return;
        Actor strongman = bastion.king;
        City seat = bastion.capital;
        bool flee = emperor.getAge() >= FleeMinAge && UnityEngine.Random.value < 0.5f;
        if (flee)
        {
            switch (FleeOutcome(empire, emperor))
            {
                case Flee.Counter:
                    Counter(empire, emperor, minister);
                    return;
                case Flee.Deposed:
                    Depose(empire, emperor, minister);
                    return;
            }
            // 皇帝出奔：权臣先在京城另立宗室幼主，皇帝在强藩处另立朝廷
            Actor puppet = PickRoyal(empire, emperor, preferYoung: true);
            if (puppet != null)
            {
                string emperorName = emperor.getName();
                core.removeKing();
                Install(empire, puppet, PrinceStruggleSystem.NoteEnthroned);
                Empire twin = Split(empire, emperor, seat, plan, KindMinister, PrinceStruggleSystem.NoteFled,
                    empire.data.core_name, renameSource: true);
                if (twin != null)
                {
                    AfterMinisterSplit(empire, twin, strongman, emperor,
                        RulerQualitySystem.Of(emperor) is RulerQualitySystem.Grade.Weak or RulerQualitySystem.Grade.Fatuous
                            ? ControlledProgress : HostProgress);
                    Publish(empire, twin, string.Format(LM.Get("partition_flee_history"), emperorName, minister.getName(),
                        seat.GetCityName(), strongman.getName(), puppet.getName(), empire.GetEmpireFullName(),
                        twin.GetEmpireFullName()), emperor);
                }
                return;
            }
        }
        // 强藩拥立：在自己的地盘立一位宗室为帝
        Actor royal = PickRoyal(empire, emperor, preferYoung: true);
        if (royal == null) return;
        Empire rival = Split(empire, royal, seat, plan, KindMinister, PrinceStruggleSystem.NoteRaised,
            empire.data.core_name, renameSource: true);
        if (rival == null) return;
        AfterMinisterSplit(empire, rival, strongman, royal, ControlledProgress);
        Publish(empire, rival, string.Format(LM.Get("partition_raise_history"), strongman.getName(), seat.GetCityName(),
            royal.getName(), minister.getName(), empire.GetEmpireFullName(), rival.GetEmpireFullName()), royal);
    }

    // 强藩把持新朝(权臣)；两朝互争正统、开战
    private const int FleeMinAge = 20, ControlledProgress = 75, HostProgress = 30;

    private enum Flee { Escaped, Counter, Deposed }

    // 出奔的结局：成功出奔四成、反杀权臣三成、事泄被废三成；明君更能反杀，庸弱昏聩更易被废，权臣挟制已深的更易被废
    private static Flee FleeOutcome(Empire empire, Actor emperor)
    {
        float escaped = 0.4f, counter = 0.3f, deposed = 0.3f;
        switch (RulerQualitySystem.Of(emperor))
        {
            case RulerQualitySystem.Grade.Wise:
                counter += 0.2f;
                break;
            case RulerQualitySystem.Grade.Weak:
            case RulerQualitySystem.Grade.Fatuous:
                deposed += 0.2f;
                counter = Mathf.Max(0.05f, counter - 0.15f);
                break;
        }
        if (empire.data.is_been_controlled) deposed += 0.1f;
        float roll = UnityEngine.Random.value * (escaped + counter + deposed);
        return roll < escaped ? Flee.Escaped : roll < escaped + counter ? Flee.Counter : Flee.Deposed;
    }

    // 反杀：皇帝与近臣谋诛权臣，抄没家产，亲政
    private static void Counter(Empire empire, Actor emperor, Actor minister)
    {
        Kingdom core = empire.CoreKingdom;
        string ministerName = minister.getName();
        int fortune = Mathf.Max(0, minister.money);
        if (fortune > 0)
        {
            minister.data.money = 0;
            core.AddMoney(fortune, TreasuryCategory.RecoveredFunds);
        }
        empire.ResetPowerfulMinister();
        empire.AddMandate(5);
        string text = string.Format(LM.Get("partition_counter_history"), emperor.getName(), ministerName);
        emperor.RecordPersonalHistory(text, relatedActorId: minister.id);
        minister.RecordPersonalHistory(text, relatedActorId: emperor.id);
        minister.die(true, AttackType.Other, false, false);
        empire.RecordHistory(directContent: text, actorId: emperor.id);
        TranslateHelper.LogEventMessage(text, core);
    }

    // 事泄被废：权臣废帝，另立宗室幼主；宗室无人可立就天下散伙(见 InterregnumSystem)
    private static void Depose(Empire empire, Actor emperor, Actor minister)
    {
        Kingdom core = empire.CoreKingdom;
        string emperorName = emperor.getName(), ministerName = minister.getName();
        Actor puppet = PickRoyal(empire, emperor, preferYoung: true);
        if (puppet == null)
        {
            InterregnumSystem.TryFracture(empire, emperor.GetPersonalIdentity(), minister,
                string.Format(LM.Get("interregnum_deposed_opening"), emperorName, ministerName));
            return;
        }
        core.removeKing();
        Install(empire, puppet, PrinceStruggleSystem.NoteEnthroned);
        empire.data.powerful_minister_progress = Mathf.Max(empire.data.powerful_minister_progress, ControlledProgress);
        empire.data.powerful_minister_emperor_id = puppet.id;
        empire.data.is_been_controlled = true;
        string text = string.Format(LM.Get("partition_deposed_history"), emperorName, ministerName, puppet.getName());
        emperor.RecordPersonalHistory(text, relatedActorId: minister.id);
        puppet.RecordPersonalHistory(text, relatedActorId: minister.id);
        empire.RecordHistory(directContent: text, actorId: puppet.id);
        TranslateHelper.LogEventMessage(text, core);
    }

    private static void AfterMinisterSplit(Empire source, Empire twin, Actor strongman, Actor ruler, int control)
    {
        if (strongman?.data != null && strongman.isAlive())
        {
            twin.data.powerful_minister_id = strongman.id;
            twin.data.powerful_minister_progress = control;
            twin.data.powerful_minister_emperor_id = ruler?.id ?? -1L;
            twin.data.last_powerful_minister_timestamp = World.world.getCurWorldTime();
            twin.data.is_been_controlled = control >= ControlledProgress;
        }
        ImperialLegitimacyChallengeService.DeclareDynasticRivals(twin, source, war: true);
    }

    // 宗室里挑一位(权臣、强藩要立的多是年少的)，从族谱落成实体
    private static Actor PickRoyal(Empire empire, Actor emperor, bool preferYoung)
    {
        PersonalClanIdentity parent = emperor?.GetPersonalIdentity();
        if (parent == null) return null;
        var pool = EmpireCraftKingdomBehCheckHeir.CollateralPool(empire.CoreKingdom, parent);
        // 先挑京城里现成的宗室(实体)，再挑年幼的；不随手从族谱里拉出一位成年皇叔
        IEnumerable<PersonalClanIdentity> ordered = (preferYoung
                ? pool.OrderByDescending(item => !item.person.is_virtual && item.person._actor?.data != null &&
                                                 item.person._actor.city == empire.CoreKingdom.capital)
                    .ThenByDescending(item => item.person.age >= 5 && item.person.age < 16)
                    .ThenBy(item => item.kinship).ThenBy(item => Mathf.Abs(item.person.age - 10))
                : pool.OrderBy(item => item.kinship).ThenByDescending(item => item.person.age >= 18).ThenByDescending(item => item.person.age))
            .Select(item => item.person);
        return VirtualGenealogySystem.RealizeFirst(ordered,
            actor => actor?.data != null && actor.isAlive() && actor != emperor && !actor.isKing());
    }

    private static void Install(Empire empire, Actor successor, string note)
    {
        Kingdom core = empire.CoreKingdom;
        PrinceStruggleSystem.Note(core, successor, note);
        if (successor.isCityLeader() && successor.city != null) successor.city.removeLeader();
        successor.removeFromArmy();
        OfficeObject office = core.GetOffice();
        if (office != null)
        {
            office.meta_object = core;
            office.SetActor(successor);
        }
        if (core.king != successor) core.setKing(successor);
    }

    #endregion

    #region ③ 宗室另立(篡位后)

    // 外姓篡位时由 Empire.NewEmperor 调用：按原来的法统定先朝宗室起兵的可能，下个月起事
    public static void QueueRestoration(Empire empire, SpecificClan previousClan, string previousName, int mandate,
        bool ministerUsurpation)
    {
        if (empire?.data == null || previousClan == null || World.world == null) return;
        float chance = Mathf.Clamp01((mandate - 30f) / 60f) * 0.425f * (ministerUsurpation ? 0.5f : 1f);
        if (UnityEngine.Random.value >= chance) return;
        Restorations[empire.data.id] = (previousClan.id, previousName ?? "", World.world.getCurWorldTime());
    }

    private static void Restore(Empire usurper, long clanId, string oldName)
    {
        if (usurper?.data == null || usurper.isRekt() || usurper.IsArchived() || Twin(usurper) != null) return;
        Kingdom core = usurper.CoreKingdom;
        if (UnificationSystem.InGrace(core)) return;
        City capital = core?.capital;
        SpecificClan clan = SpecificClanManager.Get(clanId);
        if (capital == null || clan == null || SpecificClanManager.SameLineage(usurper.EmpireSpecificClan, clan)) return;
        bool Royal(Actor actor) => actor?.data != null && actor.isAlive() &&
                                   SpecificClanManager.SameLineage(actor.GetSpecificClan(), clan);
        // 已经叛离篡位者的宗室诸王：最强的一位以自己的王国称帝，其余归附
        List<Kingdom> princes = World.world.kingdoms.list
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom.capital != null && !kingdom.IsEmpire() &&
                              !kingdom.IsInEmpire() && Royal(kingdom.king))
            .ToList();
        Kingdom leader = princes.OrderByDescending(Might).FirstOrDefault();
        Actor claimant;
        City seat;
        Plan plan;
        if (leader != null)
        {
            claimant = leader.king;
            seat = leader.capital;
            plan = Divide(usurper, capital, seat);
            if (plan.Share > 0.5f) plan = new Plan();
        }
        else
        {
            seat = FarSeat(usurper, capital.city_center, 0.15f, 0.5f, out plan);
            EmpireCraftHistory lastReign = usurper.data.history?.LastOrDefault(history =>
                history != null && history.id > 0 && history.royal_surname == clan.name);
            PersonalClanIdentity last = lastReign == null ? null
                : SpecificClanManager._actorToPersonLookup.TryGetValue(lastReign.id, out var identity) ? identity : null;
            IEnumerable<PersonalClanIdentity> pool = last != null
                ? EmpireCraftKingdomBehCheckHeir.CollateralPool(core, last).OrderBy(item => item.kinship)
                    .ThenByDescending(item => item.person.age >= 18).ThenByDescending(item => item.person.age)
                    .Select(item => item.person)
                : clan.SnapshotPeople().Where(person => person != null && person.is_alive)
                    .OrderByDescending(person => person.age >= 18).ThenByDescending(person => person.age);
            claimant = seat == null ? null : VirtualGenealogySystem.RealizeFirst(pool,
                actor => actor?.data != null && actor.isAlive() && !actor.isKing() && actor.isAdult());
        }
        if (claimant == null || seat == null || plan == null) return;
        string name = string.IsNullOrWhiteSpace(oldName) ? clan.name : oldName;
        Empire loyal = Split(usurper, claimant, seat, plan, KindRestoration, PrinceStruggleSystem.NoteRestoration, name,
            renameSource: false, ownKingdom: leader);
        if (loyal == null) return;
        foreach (Kingdom prince in princes)
            if (prince != leader && !prince.isRekt() && !prince.IsInEmpire())
                loyal.join(prince, pForce: true, pLegitimacyTransfer: true);
        ImperialLegitimacyChallengeService.DeclareDynasticRivals(loyal, usurper, war: true);
        Publish(usurper, loyal, string.Format(LM.Get("partition_restoration_history"), usurper.Emperor?.getName() ?? "",
            claimant.getName(), seat.GetCityName(), name, loyal.GetEmpireFullName()), claimant);
    }

    #endregion

    #region 复归一统

    // 一朝皇族绝嗣、群臣迎立另一朝的皇帝兼承大统(SuccessionCouncilSystem)：并入另一朝
    public static bool CanUnite(Empire vacant, out Empire heir)
    {
        heir = Twin(vacant);
        Actor emperor = heir?.Emperor;
        return heir != null && emperor?.data != null && emperor.isAlive() &&
               SpecificClanManager.SameLineage(heir.EmpireSpecificClan, vacant.EmpireSpecificClan);
    }

    public static bool Reunite(Empire vacant, Empire heir)
    {
        if (vacant?.data == null || heir?.data == null || heir.isRekt() || heir.IsArchived()) return false;
        string vacantName = vacant.GetEmpireFullName();
        Kingdom oldCore = vacant.CoreKingdom;
        foreach (Kingdom kingdom in vacant.kingdoms_list.ToList())
            if (kingdom != null && kingdom != oldCore && !kingdom.isRekt())
                heir.join(kingdom, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        List<City> cities = oldCore?.cities?.ToList() ?? new List<City>();
        if (!vacant.IsArchived()) ModClass.EMPIRE_MANAGER.dissolveEmpire(vacant);
        foreach (City city in cities)
            if (city?.data != null && !city.isRekt() && heir.CoreKingdom != null) city.joinAnotherKingdom(heir.CoreKingdom);
        heir.data.partition_twin_id = -1L;
        heir.data.partition_amity = false;
        heir.data.directPre = "";
        heir.SetEmpireName(heir.data.core_name);
        heir.recalculate();
        string text = string.Format(LM.Get("partition_reunite_history"), heir.Emperor?.getName() ?? "", vacantName,
            heir.GetEmpireFullName());
        heir.RecordHistory(directContent: text, actorId: heir.Emperor?.id ?? -1L);
        TranslateHelper.LogEventOnly(text, heir.CoreKingdom);
        World.world.zone_calculator?.dirtyAndClear();
        return true;
    }

    #endregion
}
