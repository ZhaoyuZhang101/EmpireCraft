using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教改革(用户 2026-11-06 选"更合史实的宗教改革")。以前只有两条路：本文化一施行宗教改革，西方帝国头一年就整个改宗另立国教会
// (BreakWithRome)；选帝制帝国由一位诸侯另立新教会、诸侯陆续改宗(HolyRomanSystem.Leagues)。现在从一城一人起，逐城逐国传开：
//   · 发端(TrySpark，每个有教廷的本土普世教会)：奉它的国里，制度文化已施行宗教改革的，每年 SparkChance 有一位改革者；没施行的，
//     教权低于 EarlyReformAuthority 且圣战失利满 2 次(见 ReligionSchismSystem.EarlyReformation)、或对立教宗之争已满 SchismSparkYears 年，
//     每年 EarlySparkChance(原型威克里夫、胡斯)。同一母教两次发端至少隔 SparkGapYears 年(从最近一支分出的教会算，剧本带的也算)，
//     宗教改革分出的(连同剧本里带的新教，即圣地所在文化施行了宗教改革的分支)至多 MaxReformations 支。
//     改革者在一座城(不是圣地，不在教廷所在国、教宗国、主教领；施行了宗教改革的、人口多的、离圣地远的优先)：城里有教士特质的成年人
//     就是他(原型路德)，没有就是城主；都城里没有教士、城主时由君主出面(诸侯改革)。立新教会(记母宗教，圣地在那座城)，那城改奉；
//     诸侯改革的连国一起改奉。母宗教教权 −3；
//   · 传布(Spread，新教会分出 SpreadYears 年内)：
//       - 逐城：挨着新教之城的旧教城每年按 CityChance × 相邻新教城数(至多 3)改奉，国教已是新教会的另加 StateCityChance(不必相邻)；
//         国教仍是旧教的减半(都城 ×0.3)、不宽容的国家再减半；圣地、教廷所在国、教宗国、主教领的城不改；
//         每支每年至多 MaxCitiesPerYear + 已有城数 ×CityCapShare 城；
//       - 诸侯择教(教随国定)：境内有新教之城、或与奉新教的国接壤的旧教君主(独立王国、帝国成员、非西方帝国的中央)，每年按
//         PrinceChance × (境内新教城占比 ×2 + 接壤的新教国 ×0.5，至多 2) × PrinceFactor 改奉。PrinceFactor：所在帝国缴全额什一税 ×1.3、
//         协约 ×1、不缴 ×0.8；遭绝罚 ×2；与教廷交战 ×1.5；帝国成员对仍奉旧教的皇帝观感差 ×1.5、教皇党 ×0.3；君主虔诚或是教士 ×0.5；
//         教宗正统 60 以上 ×0.7、40 以下 ×1.3；对立教宗之争中 ×1.3；本文化施行了宗教改革 ×1.5、施行了教权至上 ×0.5。
//         每支每年至多 MaxPrincesPerYear 国。改奉只改君主、国与都城，其余各城随国教势能慢慢改(记皈依时间：独立王国此后国教势能加倍、
//         50 年不再皈依，见 ReligionConversionSystem.RecentlyConverted)。教宗国、主教领、握圣地的不改；
//         有议会的封建帝国(神罗)的成员归 HolyRomanSystem.PrincelyReformation(同一套几率，见 PrinceReformChance)，结同盟、打宗教战争；
//       - 帝国中央(西方帝国)：君主率国改宗见 TryBreakWithRome(PapacySystem.Events)；
//   · 宗教战争(ReformRevolts)：没有议会的帝国里，皇帝仍奉旧教，改奉新教的成员国每年 RevoltChance(皇帝不宽容 ×1.5)起兵脱离，
//     打成地方独立之战；同一帝国隔 RevoltGapYears 年才再起。
// 窗口：宗教窗口谱系页列各支新教会的改革者、发端之城、城数与国数，教廷与帝国页列本国新教之城。
public static partial class PapacySystem
{
    public const int OriginPriest = 1, OriginPrince = 2, OriginNational = 3, OriginRivalLine = 4;
    public const float EarlyReformAuthority = 35f;
    public const int SchismSparkYears = 15;
    public const float EarlySparkChance = 0.02f;

    private const float SparkChance = 0.08f;
    private const int SparkGapYears = 30, MaxReformations = 3, SpreadYears = 120;
    private const float CityChance = 0.04f, StateCityChance = 0.06f, CityCapShare = 0.06f;
    private const int MaxCitiesPerYear = 3;
    private const float PrinceChance = 0.03f;
    private const int MaxPrincesPerYear = 2;
    private const float RealmBreakChance = 0.04f, RealmShareFactor = 0.3f, PeopleBreakChance = 0.06f, AdoptShare = 0.25f;
    private const float RevoltChance = 0.04f;
    private const int RevoltGapYears = 30;

    #region 查询

    // 宗教改革分出的新教会(改革者、诸侯、国教会)
    public static bool IsReformedChurch(Religion religion)
    {
        SplitOriginData origin = religion?.data == null ? null : religion.GetOrCreate().SplitOrigin;
        return origin != null && origin.kind is OriginPriest or OriginPrince or OriginNational;
    }

    public static SplitOriginData SplitOrigin(Religion religion) => religion?.data == null ? null : religion.GetOrCreate().SplitOrigin;

    // 从某教分出、还在的教会
    private static List<Religion> ChildChurches(Religion old) =>
        old?.data == null ? new List<Religion>()
            : World.world.religions.Where(religion => religion?.data != null && !religion.isRekt() && religion != old &&
                                                      religion.GetOrCreate().ParentReligionId == old.data.id).ToList();

    // 宗教改革分出的教会(按分出先后)：本功能起头的(改革者、诸侯、国教会)，加上剧本带的新教(圣地所在文化施行了宗教改革)
    public static List<Religion> Reformations(Religion old) =>
        ChildChurches(old).Where(child => IsReformedChurch(child) || LegacyProtestant(child))
            .OrderBy(child => child.GetOrCreate().SplitAt).ToList();

    private static bool LegacyProtestant(Religion religion)
    {
        var data = religion.GetOrCreate();
        if (data.SplitOrigin != null || data.Caesaropapist || !ReligionFaith.IsPapal(religion)) return false;
        City holy = religion.GetCity();
        return holy?.data != null && !holy.isRekt() &&
               InstitutionSystem.IsEnacted(CultureService.GetMainCulture(holy, initialize: false), NodeReformation);
    }

    // 本帝国奉宗教改革分出的新教会(从 old 分出，含剧本带的新教)的城最多的一支与它的占比
    public static (Religion church, float share) ReformedShare(Empire empire, Religion old)
    {
        int total = 0;
        if (old?.data == null) return (null, 0f);
        var counts = new Dictionary<Religion, int>();
        var reformed = new Dictionary<Religion, bool>();
        foreach (Kingdom kingdom in empire?.kingdoms_list ?? new List<Kingdom>())
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt()) continue;
                total++;
                Religion faith = city.hasReligion() ? city.religion : null;
                if (faith?.data == null || faith == old) continue;
                if (!reformed.TryGetValue(faith, out bool counted))
                    reformed[faith] = counted = faith.GetOrCreate().ParentReligionId == old.data.id &&
                                                (IsReformedChurch(faith) || LegacyProtestant(faith));
                if (!counted) continue;
                counts.TryGetValue(faith, out int count);
                counts[faith] = count + 1;
            }
        }
        if (total == 0 || counts.Count == 0) return (null, 0f);
        KeyValuePair<Religion, int> top = counts.OrderByDescending(pair => pair.Value).First();
        return (top.Key, top.Value / (float)total);
    }

    #endregion

    // 每年(挂在 ReligionSchismSystem.YearlyPass)：有教廷的本土普世教会的改革者发端、各支新教会传布、成员国起兵
    public static void ReformationYearly(Religion religion)
    {
        try
        {
            if (religion?.data == null || religion.isRekt() || World.world == null || !ReligionFaith.IsPapal(religion) ||
                !ReligionSphere.IsNative(religion) || ReligionSphere.IsLocalChurch(religion) || Papacy(religion) == null) return;
            TrySpark(religion);
            foreach (Religion church in ChildChurches(religion))
                if (SplitOrigin(church)?.kind is OriginPriest or OriginPrince) Spread(church, religion);
            ReformRevolts(religion);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][宗教改革] {religion?.name} 结算失败: {exception.Message}");
        }
    }

    #region 发端

    private static void TrySpark(Religion religion)
    {
        List<Religion> reformations = Reformations(religion);
        if (reformations.Count >= MaxReformations) return;
        double latest = ChildChurches(religion).Select(child => child.GetOrCreate().SplitAt).DefaultIfEmpty(-1d).Max();
        if (latest >= 0d && World.world.getCurWorldTime() >= latest && Date.getYearsSince(latest) < SparkGapYears) return;
        PapalSchismData schism = ActiveSchism(religion);
        bool early = ReligionSchismSystem.EarlyReformation(religion) ||
                     schism != null && Date.getYearsSince(schism.started_at) >= SchismSparkYears;
        Kingdom see = Realm(Papacy(religion));
        City holy = HolyCity(religion);
        float mapSize = Mathf.Max(1f, Mathf.Max(MapBox.width, MapBox.height));
        City best = null;
        float bestScore = 0f;
        bool anyEnacted = false;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (!SchismAlive(kingdom) || kingdom.religion != religion || Realm(kingdom) == see || !ReformableRealm(kingdom)) continue;
            bool enacted = InstitutionSystem.IsEnacted(CultureService.GetInstitutionCulture(kingdom), NodeReformation);
            if (!enacted && !early) continue;
            anyEnacted |= enacted;
            foreach (City city in kingdom.cities)
            {
                if (city?.data == null || city.isRekt() || city.religion != religion || city == holy) continue;
                float far = holy != null ? Vector2.Distance(city.city_center, holy.city_center) / mapSize * 2f : 0f;
                float score = Mathf.Sqrt(Mathf.Max(1, city.CountLivingPopulation())) * (enacted ? 2f : 1f) * (1f + far) *
                              UnityEngine.Random.Range(0.5f, 1.5f);
                if (score <= bestScore) continue;
                best = city;
                bestScore = score;
            }
        }
        if (best == null || UnityEngine.Random.value >= (anyEnacted ? SparkChance : EarlySparkChance)) return;
        Kingdom host = best.kingdom;
        int kind = OriginPriest;
        Actor reformer = (best.units ?? new List<Actor>()).ToList()
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.isKing() &&
                            actor.hasTrait("cleric"))
            .OrderByDescending(actor => actor.data.renown).FirstOrDefault();
        // 西方帝国的皇帝不在此出面(君主率国改宗另见 TryBreakWithRome)
        Empire realm = host.GetEmpire();
        bool princeMay = realm == null || realm.CoreKingdom != host || !IsWestern(realm);
        Actor leader = best.leader;
        if (reformer == null && leader?.data != null && !leader.isRekt() && leader.isAlive() && (princeMay || !leader.isKing()))
            reformer = leader;
        if (reformer == null && princeMay && best == host.capital && host.king?.data != null && host.king.isAlive()) reformer = host.king;
        reformer ??= CityPopulationSystem.SpawnOfficial(best, host);
        if (reformer?.data == null) return;
        if (reformer.isKing() && reformer.kingdom == host) kind = OriginPrince;
        FoundReformedChurch(religion, best, reformer, kind);
    }

    // 能起改革、改宗的国：不是教宗国、主教领，不握圣地
    private static bool ReformableRealm(Kingdom kingdom) =>
        kingdom.GetKingdomType() != KingdomType.Feudalism_papal_state && !HolyRomanSystem.IsChurchEstate(kingdom) && !HoldsHolyCity(kingdom);

    // 立新教会：改革者在一城张贴论纲，或诸侯在本国另立(HolyRomanSystem.SeedReformation 也走这里)
    public static Religion FoundReformedChurch(Religion old, City city, Actor founder, int kind)
    {
        if (old?.data == null || city?.data == null || city.isRekt() || founder?.data == null || !founder.isAlive()) return null;
        if (founder.city != city && !founder.isKing()) founder.joinCity(city);
        Religion church = World.world.religions.newReligion(founder, false);
        if (church?.data == null) return null;
        church.data.name = string.Format(LM.Get("hre_reformed_church_name"), city.GetCityName());
        church.SetCity(city);
        double now = World.world.getCurWorldTime();
        var data = church.GetOrCreate();
        data.ParentReligionId = old.data.id;
        data.SplitAt = now;
        data.Faith = FaithType.Church.ToString();
        data.Sphere = ReligionSphere.SphereOf(old);
        data.SplitOrigin = new SplitOriginData
        {
            kind = kind, founder_id = founder.id, founder_name = founder.getName(), city_id = city.data.id,
            city_name = city.GetCityName(), at = now
        };
        if (city.religion != church) BackgroundReligionSystem.Convert(city, church, city.religion);
        if (founder.religion != church) founder.setReligion(church);
        Kingdom host = city.kingdom;
        if (kind == OriginPrince && host?.data != null && host.religion == old) ConvertState(host, church);
        AddAuthority(old, -3f);
        string text = kind == OriginPrince
            ? string.Format(LM.Get("hre_reformation_founded_history"), host?.GetKingdomName() ?? "", church.name, old.name)
            : string.Format(LM.Get("reformation_sparked_log"), founder.getName(), city.GetCityName(), old.name, church.name);
        TranslateHelper.LogEventMessage(text, host);
        RecordRealm(host, text, founder);
        founder.RecordPersonalHistory(text);
        ReligionChronicle.Clear(church);
        ReligionChronicle.Record(old, church, ReligionChronicle.KindSchism, text);
        LogService.LogInfo($"[EmpireCraft][宗教改革] {founder.getName()}({(kind == OriginPrince ? "诸侯" : "教士")})在 {city.GetCityName()} " +
                           $"立{church.name}，分自{old.name}");
        return church;
    }

    #endregion

    #region 传布

    private static void Spread(Religion church, Religion old)
    {
        var data = church.GetOrCreate();
        if (data.SplitAt < 0d || World.world.getCurWorldTime() < data.SplitAt || Date.getYearsSince(data.SplitAt) > SpreadYears) return;
        Kingdom see = Realm(Papacy(old));
        City holy = HolyCity(old);
        // 逐城
        int own = 0;
        var candidates = new List<(City city, float chance)>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt()) continue;
            if (city.religion == church)
            {
                own++;
                continue;
            }
            Kingdom owner = city.kingdom;
            if (city.religion != old || city == holy || !SchismAlive(owner) || Realm(owner) == see || !ReformableRealm(owner)) continue;
            int reformed = city.neighbours_cities?.Count(neighbour => neighbour?.data != null && neighbour.religion == church) ?? 0;
            bool state = owner.religion == church;
            if (reformed == 0 && !state) continue;
            float chance = CityChance * Mathf.Min(3, reformed) + (state ? StateCityChance : 0f);
            if (owner.religion == old)
            {
                chance *= city.isCapitalCity() ? 0.3f : 0.5f;
                if (ReligiousTension.PolicyOf(owner) == ReligiousTension.Policy.Intolerant) chance *= 0.5f;
            }
            candidates.Add((city, chance));
        }
        int cap = MaxCitiesPerYear + (int)(own * CityCapShare), converted = 0;
        foreach ((City city, float chance) in candidates.OrderBy(_ => UnityEngine.Random.value))
        {
            if (converted >= cap) break;
            if (UnityEngine.Random.value >= chance) continue;
            BackgroundReligionSystem.Convert(city, church, old);
            converted++;
        }
        // 诸侯择教
        var reformedRealms = new HashSet<Kingdom>(World.world.kingdoms.list.Where(kingdom => SchismAlive(kingdom) && kingdom.religion == church));
        int princes = 0;
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (princes >= MaxPrincesPerYear) break;
            if (!PrinceMayChoose(kingdom, old, see)) continue;
            float chance = PrinceReformChance(kingdom, old, church, reformedRealms);
            if (chance <= 0f || UnityEngine.Random.value >= chance) continue;
            AdoptReformation(kingdom, church, old);
            reformedRealms.Add(kingdom);
            princes++;
        }
        if (converted > 0 || princes > 0)
            LogService.LogInfo($"[EmpireCraft][宗教改革] {church.name} 本年改奉 {converted} 城、{princes} 国(已有 {own} 城)");
    }

    // 由诸侯择教的国：奉旧教、有君、不是教宗国、主教领、不握圣地、不在教廷所在国；西方帝国的中央另走 TryBreakWithRome，
    // 有议会的封建帝国的成员另走 HolyRomanSystem.PrincelyReformation
    private static bool PrinceMayChoose(Kingdom kingdom, Religion old, Kingdom see)
    {
        if (!SchismAlive(kingdom) || kingdom.religion != old || kingdom.capital == null || kingdom.king?.data == null ||
            !kingdom.king.isAlive() || Realm(kingdom) == see || !ReformableRealm(kingdom)) return false;
        Empire empire = kingdom.GetEmpire();
        if (empire == null) return true;
        if (empire.CoreKingdom == kingdom) return !IsWestern(empire);
        return !HolyRomanSystem.HasDiet(empire);
    }

    // 诸侯改奉某支新教会的年几率(神罗诸侯同用，见 HolyRomanSystem.PrincelyReformation)
    public static float PrinceReformChance(Kingdom kingdom, Religion old, Religion church, ICollection<Kingdom> reformedRealms = null)
    {
        if (kingdom?.data == null || kingdom.cities.Count == 0) return 0f;
        float share = kingdom.cities.Count(city => city?.data != null && city.religion == church) / (float)kingdom.cities.Count;
        int neighbours = reformedRealms != null
            ? reformedRealms.Count(other => other != kingdom && other.IsNeighbourWith(kingdom))
            : World.world.kingdoms.list.Count(other => SchismAlive(other) && other != kingdom && other.religion == church &&
                                                       other.IsNeighbourWith(kingdom));
        float weight = Mathf.Min(2f, share * 2f + neighbours * 0.5f);
        return weight <= 0f ? 0f : PrinceChance * weight * PrinceFactor(kingdom, old);
    }

    private static float PrinceFactor(Kingdom kingdom, Religion old)
    {
        float factor = 1f;
        Kingdom realm = Realm(kingdom);
        Empire empire = kingdom.GetEmpire();
        if (empire != null && Applies(empire))
        {
            factor *= Investiture(empire) switch
            {
                InvestitureChurch => 1.3f,
                InvestitureConcordat => 1f,
                _ => 0.8f
            };
            if (IsExcommunicated(empire)) factor *= 2f;
        }
        Kingdom see = Realm(Papacy(old));
        if (see != null && realm.isInWarWith(see)) factor *= 1.5f;
        if (empire != null && empire.CoreKingdom != kingdom)
        {
            if (empire.CoreKingdom.religion == old && DiplomacyLedger.Opinion(kingdom, empire.CoreKingdom) < 0) factor *= 1.5f;
            if (Party(kingdom) == PartyPope) factor *= 0.3f;
        }
        Actor king = kingdom.king;
        if (king != null && (king.hasTrait("pious") || king.hasTrait("religious") || king.hasTrait("devout") || king.hasTrait("cleric")))
            factor *= 0.5f;
        if (PopeLegitimacy.Applies(old))
        {
            int legitimacy = PopeLegitimacy.Get(old);
            if (legitimacy >= 60) factor *= 0.7f;
            else if (legitimacy < 40) factor *= 1.3f;
        }
        if (ActiveSchism(old) != null) factor *= 1.3f;
        string culture = CultureService.GetInstitutionCulture(kingdom);
        if (InstitutionSystem.IsEnacted(culture, NodeReformation)) factor *= 1.5f;
        if (InstitutionSystem.IsEnacted(culture, NodeClerical)) factor *= 0.5f;
        return factor;
    }

    // 诸侯改奉新教会(教随国定)：君主、国、都城改奉，其余各城随国教势能慢慢改；记皈依时间(见 ReligionConversionSystem.RecentlyConverted)
    public static void AdoptReformation(Kingdom kingdom, Religion church, Religion old)
    {
        ConvertState(kingdom, church);
        string text = string.Format(LM.Get("hre_prince_converted_history"), kingdom.GetKingdomName(), church.name, old.name);
        TranslateHelper.LogEventMessage(text, kingdom);
        RecordRealm(kingdom, text, kingdom.king);
        ReligionChronicle.Record(church, old, ReligionChronicle.KindConversion, text);
    }

    private static void ConvertState(Kingdom kingdom, Religion church)
    {
        Actor king = kingdom.king;
        if (king?.data != null && king.isAlive() && king.religion != church) king.setReligion(church);
        kingdom.setReligion(church);
        City capital = kingdom.capital;
        if (capital?.data != null && !capital.isRekt() && capital.religion != church)
            BackgroundReligionSystem.Convert(capital, church, capital.religion);
        kingdom.GetOrCreate().religion_converted_at = World.world.getCurWorldTime();
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.CoreKingdom != kingdom) return;
        empire.Religion = church;
        State(empire).national_church_id = church.data.id;
    }

    // 帝国中央改奉境内已占上风的新教会(TryBreakWithRome)：成员国与各城随之慢慢改(见 ReligionConversionSystem.FollowRealm)，没收教产
    private static void AdoptRealmReformation(Empire empire, Religion church, Religion old)
    {
        Kingdom core = empire.CoreKingdom;
        ConvertState(core, church);
        long lands = (long)(StateDebtSystem.AnnualStableIncome(core) * ChurchLandsYears);
        if (lands > 0) core.AddMoney((int)Math.Min(int.MaxValue, lands), TreasuryCategory.RecoveredFunds);
        AddAuthority(old, -6f);
        string text = string.Format(LM.Get("papal_realm_reformation_history"), empire.Emperor?.getName() ?? core.king?.getName() ?? "",
            empire.GetEmpireFullName(), old.name, church.name, MoneyDisplay.Format(lands));
        Record(empire, text);
        empire.Emperor?.RecordPersonalHistory(text);
        ReligionChronicle.Record(old, church, ReligionChronicle.KindConversion, text);
    }

    // 一国连城改奉(对立一线分立用)：国、君主、奉旧教的城(连城中实体)
    private static void ConvertWhole(Kingdom kingdom, Religion old, Religion church)
    {
        kingdom.setReligion(church);
        if (kingdom.king?.data != null && kingdom.king.isAlive() && kingdom.king.religion == old) kingdom.king.setReligion(church);
        foreach (City city in kingdom.cities.ToList())
            if (city?.data != null && !city.isRekt() && city.religion == old) BackgroundReligionSystem.Convert(city, church, old);
    }

    #endregion

    #region 宗教战争

    // 没有议会的帝国：皇帝仍奉旧教，改奉新教的成员国起兵脱离(有议会的走诸侯同盟，见 HolyRomanSystem.Leagues)
    private static void ReformRevolts(Religion old)
    {
        double now = World.world.getCurWorldTime();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            Kingdom core = empire?.CoreKingdom;
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || !SchismAlive(core) || core.religion != old ||
                HolyRomanSystem.HasDiet(empire)) continue;
            PapalRelationState state = State(empire);
            if (state.reform_revolt_at >= 0d && now >= state.reform_revolt_at && Date.getYearsSince(state.reform_revolt_at) < RevoltGapYears)
                continue;
            Kingdom rebel = empire.kingdoms_list.ToList()
                .Where(member => SchismAlive(member) && member != core && member.hasReligion() && IsReformedChurch(member.religion) &&
                                 member.religion.GetOrCreate().ParentReligionId == old.data.id && MembershipCooldown.Settled(member) &&
                                 !member.hasEnemies())
                .OrderByDescending(member => member.cities.Count).FirstOrDefault();
            if (rebel == null) continue;
            float chance = RevoltChance * (ReligiousTension.PolicyOf(core) == ReligiousTension.Policy.Intolerant ? 1.5f : 1f);
            if (UnityEngine.Random.value >= chance) continue;
            War war = DiplomacyHelpers.wars.newWar(rebel, core, WarTypeLibrary.normal);
            if (war == null) continue;
            empire.leave(rebel, true, true);
            war.SetEmpireWarType(EmpireWarType.地方独立);
            state.reform_revolt_at = now;
            string text = string.Format(LM.Get("reformation_revolt_log"), rebel.GetKingdomName(), rebel.religion.name,
                empire.GetEmpireFullName(), old.name);
            Record(empire, text);
            ReligionChronicle.Record(rebel.religion, old, ReligionChronicle.KindHolyWar, text);
        }
    }

    #endregion
}
