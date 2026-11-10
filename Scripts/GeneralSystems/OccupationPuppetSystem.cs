using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 近代占领民族国家(用户 2026-11-10 "关于近代占领民族国家, 不是直接占领土地因为民族意识很强, 需要先设置傀儡政府, 比如汪精卫的
// 汪伪政府, 伪满洲国之类的""开战的目标中砝码里面民族主义建议也是筹码的一部分, 然后傀儡吞并之间所需要的砝码数值也不同"
// "比如大国的占领成本和小国的也明显不一样")：
//   · 他国本土(NationalLand)：城里的主流文化有了民族意识(本文化有民族主义科技，或城中民族情绪 ≥ NationalSentimentFloor)，
//     又与占领者不是同族；占领者是近代国家(StateModernization.IsModernPower)。殖民地式的征服(没有民族意识的地方)不算；
//   · 军管：打下来的他国本土先由占领者直接管着，打仗时按城价 × 民族情绪 × 大小之比记进占领方的疲敝(OccupationCost，
//     见 PeaceTermRules.WearOf)，被占的国家越大、民族情绪越高越想停；
//   · 立傀儡：军管满 SettleMonths 个月的他国本土，按帝国法理分片(满洲、燕、吴……)交给这一片的傀儡政府；
//     这一片还没有傀儡的，在其中最大的城(片里有法理首府先取首府)从当地人里扶一个(城里的本族名人，没有就推举一位读书人)立傀儡政府，
//     国号"某某自治政府"(剧情事件立的伪满、华北临时政府在这一片的照样接收)；
//   · 议和：割他国本土可以写"割地立傀儡"(PeaceTermKind.Puppet)，城交给战胜国这一片的傀儡；民族意识的砝码见 PeaceTermRules。
public static class OccupationPuppetSystem
{
    public const int SettleMonths = 12;
    // 本文化没有民族主义科技时，城中民族情绪到这么多也算有了民族意识
    public const float NationalSentimentFloor = 30f;

    private static string RulingCulture(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire != null && !empire.isRekt() ? CompositeEmpireService.GetRulingCulture(empire) : CultureService.GetRealmCulture(kingdom);
    }

    // 城里的民族情绪(0~1)：本文化有民族主义的至少按 PeaceTermRules.NationFloor
    public static float Sentiment(City city)
    {
        string culture = CultureService.GetMainCulture(city, initialize: false);
        float value = NationalSentimentSystem.GetCity(city) / 100f;
        if (CultureService.IsValidCulture(culture) && TechnologySystem.HasTech(culture, "nationalism"))
            value = Math.Max(value, PeaceTermRules.NationFloor);
        return Math.Max(0f, Math.Min(1f, value));
    }

    // 这座城对占领者 occupier 是不是"他国本土"：有民族意识、不同族；占领者是近代国家
    public static bool ForeignNationalCity(City city, Kingdom occupier)
    {
        if (city?.data == null || city.isRekt() || occupier?.data == null || !StateModernization.IsModernPower(occupier)) return false;
        string culture = CultureService.GetMainCulture(city, initialize: false), ruler = RulingCulture(occupier);
        if (!CultureService.IsValidCulture(culture) || !CultureService.IsValidCulture(ruler) || CultureService.SameKin(culture, ruler)) return false;
        return TechnologySystem.HasTech(culture, "nationalism") || NationalSentimentSystem.GetCity(city) >= NationalSentimentFloor;
    }

    // 议和割城：giver 出的这座城是 giver 本族之地、对收的一方 receiver 是他国本土(要另记民族意识，能写"割地立傀儡")
    public static bool NationalLand(City city, Kingdom giver, Kingdom receiver)
    {
        if (!ForeignNationalCity(city, receiver) || PuppetStateSystem.IsPuppet(receiver) || ExileGovernmentSystem.IsExiled(receiver)) return false;
        string culture = CultureService.GetMainCulture(city, initialize: false), own = RulingCulture(giver);
        return CultureService.IsValidCulture(own) && CultureService.SameKin(culture, own);
    }

    // 民族意识的砝码(失地一方另记)：城价 × 民族情绪 × 大小之比，吞并按全数、立傀儡按四成
    public static int Resistance(City city, Kingdom giver, Kingdom receiver, bool puppet)
    {
        if (!NationalLand(city, giver, receiver)) return 0;
        float size = PeaceTermRules.SizeFactor(PeaceTermRules.NationValue(giver), PeaceTermRules.NationValue(receiver));
        return PeaceTermRules.NationResistance(PeaceTermRules.CityValue(city), Sentiment(city), size, puppet);
    }

    // 一国整个立为保护国的民族意识：他国本土各城的城价 × 民族情绪，乘大小之比与倍数(PeaceTermRules.ProtectorateNationFactor)
    public static int RealmResistance(Kingdom giver, Kingdom receiver, double factor)
    {
        if (giver?.cities == null || receiver?.data == null) return 0;
        double weighted = 0d;
        foreach (City city in giver.cities)
            if (NationalLand(city, giver, receiver)) weighted += PeaceTermRules.CityValue(city) * Sentiment(city);
        if (weighted <= 0d) return 0;
        float size = PeaceTermRules.SizeFactor(PeaceTermRules.NationValue(giver), PeaceTermRules.NationValue(receiver));
        return (int)Math.Round(weighted * size * factor);
    }

    // 占领成本(打仗时记进占领方的疲敝)：realm 一方(连同帝国成员)直接管着的他国本土；大小之比按被占的一方(foe)连同已丢的城
    public static (int wear, int cities) OccupationCost(Kingdom realm, Kingdom foe)
    {
        if (realm?.data == null || realm.isRekt() || !StateModernization.IsModernPower(realm)) return (0, 0);
        int occupied = 0, resistance = 0;
        double weighted = 0d;
        foreach (City city in DirectCities(realm))
        {
            if (!ForeignNationalCity(city, realm)) continue;
            int value = PeaceTermRules.CityValue(city);
            occupied += value;
            weighted += value * Sentiment(city);
            resistance++;
        }
        if (resistance == 0) return (0, 0);
        int nation = PeaceTermRules.NationValue(foe) + occupied;
        float size = PeaceTermRules.SizeFactor(nation, PeaceTermRules.NationValue(realm));
        return (PeaceTermRules.OccupationWear((int)Math.Round(weighted * size), PeaceTermRules.NationValue(realm)), resistance);
    }

    // 一方直接管着的城：本国与帝国成员的(傀儡、殖民地的不算)
    private static IEnumerable<City> DirectCities(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        IEnumerable<City> cities = empire?.CoreKingdom == realm && empire.cities_list != null ? empire.cities_list : realm.cities;
        return (cities ?? new List<City>()).Where(city => city?.data != null && !city.isRekt() && city.kingdom != null &&
                                                          !PuppetStateSystem.IsPuppet(city.kingdom) && !ColonySystem.IsColony(city.kingdom))
            .ToList();
    }

    #region 交给傀儡

    private static readonly List<(long city, long occupier)> Pending = new();

    // 议和"割地立傀儡"：几帧之内逐城交给战胜国这一片的傀儡(不在原版结束战争的同一帧里改归属，同 WarGoalSystem.QueueCession)
    public static void QueueCession(City city, Kingdom occupier)
    {
        if (city?.data == null || city.isRekt() || occupier?.data == null || occupier.isRekt()) return;
        Pending.Add((city.id, occupier.id));
    }

    // 这座城所在一片(帝国法理；没有的按王国法理)
    private static long Sphere(City city)
    {
        EmpireCore core = city?.GetEmpireCore();
        if (core != null) return core.id;
        KingdomTitle title = city?.GetTitle();
        return title?.data != null ? -1000000L - title.data.id : -1L;
    }

    // 占领者在这一片的傀儡(治所在这一片的)
    private static Kingdom PuppetIn(Kingdom occupier, long sphere) =>
        sphere == -1L ? null : PuppetStateSystem.PuppetsOf(occupier)
            .FirstOrDefault(puppet => puppet.capital != null && !puppet.capital.isRekt() && Sphere(puppet.capital) == sphere);

    // 把这些城交给占领者这一片的傀儡；这一片还没有的立一个。返回交出去几座
    public static int Hand(Kingdom occupier, List<City> cities)
    {
        cities = cities.Where(city => city?.data != null && !city.isRekt() && city.kingdom != null &&
                                      !PuppetStateSystem.IsPuppet(city.kingdom) && city != occupier.capital).ToList();
        if (cities.Count == 0) return 0;
        int handed = 0;
        foreach (IGrouping<long, City> group in cities.GroupBy(Sphere).ToList())
        {
            List<City> part = group.ToList();
            Kingdom puppet = PuppetIn(occupier, group.Key);
            if (puppet == null)
            {
                puppet = Create(occupier, part);
                if (puppet == null) continue;
                handed += part.Count(city => city.kingdom == puppet);
                continue;
            }
            foreach (City city in part)
            {
                city.joinAnotherKingdom(puppet);
                if (city.kingdom == puppet) handed++;
            }
        }
        return handed;
    }

    // 立这一片的傀儡：最大的城(片里有帝国法理首府先取首府)作治所，从当地人里扶一个领头
    private static Kingdom Create(Kingdom occupier, List<City> cities)
    {
        EmpireCore core = cities[0].GetEmpireCore();
        City seat = cities.FirstOrDefault(city => core != null && core.GetCoreCapital() == city) ??
                    cities.OrderByDescending(PeaceTermRules.CityValue).First();
        Actor leader = Collaborator(seat, occupier);
        if (leader == null) return null;
        string region = seat.GetTitle()?.data?.name;
        if (string.IsNullOrWhiteSpace(region)) region = seat.GetCityName();
        string name = string.Format(LM.Get("occupation_puppet_name"), region);
        Kingdom puppet = PuppetStateSystem.Found(occupier, seat, cities, leader, name);
        if (puppet == null) return null;
        string text = string.Format(LM.Get("occupation_puppet_history"), occupier.GetKingdomName(), leader.getName(), seat.GetCityName(),
            puppet.GetKingdomName());
        EventRecorder.Record(occupier, text, leader);
        TranslateHelper.LogEventMessage(text, occupier);
        LogService.LogInfo($"[EmpireCraft][占领] {occupier.GetKingdomName()} 在 {seat.GetCityName()} 立傀儡 {puppet.GetKingdomName()}，{cities.Count} 城");
        return puppet;
    }

    // 领头的本地人：城里本族的成年名人，奉行主国执政理念的先(见 PuppetStateSystem.MasterIdeology)，再按声望；
    // 没有就从城里推举一位读书人(无小人模式)。立了傀儡以后领头人的理念照主国改(见 PuppetStateSystem.SyncIdeology)
    private static Actor Collaborator(City seat, Kingdom occupier)
    {
        string culture = CultureService.GetMainCulture(seat, initialize: false);
        PartyIdeology ideology = PuppetStateSystem.MasterIdeology(occupier);
        Actor local = (seat.units ?? new List<Actor>())
            .Where(actor => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() && !actor.isKing() &&
                            actor.CanFoundCivKingdom() &&
                            CultureService.SameKin(CultureService.GetActorCulture(actor), culture))
            .OrderByDescending(actor => IdeologyPopulationSystem.Get(actor) == ideology ? 1 : 0)
            .ThenByDescending(actor => CityPopulationSystem.IsNotable(actor) ? 1 : 0).ThenByDescending(actor => actor.renown)
            .FirstOrDefault();
        return local ?? (CityPopulationSystem.AbstractPopulationEnabled ? CityPopulationSystem.SpawnScholar(seat) : null);
    }

    #endregion

    #region 每帧、每月

    private static object _world;
    private static double _lastMonth = -1d;

    public static void ResetWorldState()
    {
        _world = null;
        _lastMonth = -1d;
        Pending.Clear();
    }

    public static void Tick()
    {
        MapBox world = World.world;
        if (world?.kingdoms == null || ModClass.IS_CLEAR) return;
        if (!ReferenceEquals(_world, world))
        {
            ResetWorldState();
            _world = world;
        }
        // 议和割给傀儡的城：每帧一座
        if (Pending.Count > 0)
        {
            (long cityId, long occupierId) = Pending[0];
            Pending.RemoveAt(0);
            City city = world.cities.get(cityId);
            Kingdom occupier = world.kingdoms.get(occupierId);
            if (city?.data != null && !city.isRekt() && occupier?.data != null && !occupier.isRekt())
                try
                {
                    if (Hand(occupier, new List<City> { city }) == 0 && city.kingdom != occupier) city.joinAnotherKingdom(occupier);
                }
                catch (Exception exception)
                {
                    LogService.LogWarning($"[EmpireCraft][占领] 割地立傀儡失败: {exception.Message}");
                }
        }
        double now = world.getCurWorldTime();
        if (_lastMonth >= 0d && now >= _lastMonth && Date.getMonthsSince(_lastMonth) < 1) return;
        _lastMonth = now;
        foreach (Kingdom kingdom in world.kingdoms.list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom.wild || !kingdom.isCiv()) continue;
            // 只看一方的头(帝国中央或独立国)；傀儡、殖民地、流亡政府不扶傀儡
            if (kingdom.IsInEmpire() && !kingdom.IsEmpire() || PuppetStateSystem.IsPuppet(kingdom) || ColonySystem.IsColony(kingdom) ||
                ExileGovernmentSystem.IsExiled(kingdom)) continue;
            try
            {
                Settle(kingdom);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][占领] {kingdom.data.name} 立傀儡失败: {exception.Message}");
            }
        }
    }

    // 军管满 SettleMonths 个月的他国本土(这一方打下来的)：交给这一片的傀儡
    private static void Settle(Kingdom realm)
    {
        if (!StateModernization.IsModernPower(realm)) return;
        long realmId = realm.data.id;
        List<City> settled = DirectCities(realm).Where(city =>
        {
            CityExtension.CityExtraData data = city.GetOrCreate();
            return data.conquered_at >= 0d && data.conquered_by == realmId && Date.getMonthsSince(data.conquered_at) >= SettleMonths &&
                   ForeignNationalCity(city, realm);
        }).ToList();
        if (settled.Count > 0) Hand(realm, settled);
    }

    #endregion
}
