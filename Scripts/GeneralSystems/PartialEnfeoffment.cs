using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 行省为主、也有封授(用户 2026-11-18："罗马为什么长期都是辖区而不是分封""游牧几百年都不分封一直都是直辖"；
// 问答选"按体制各有分寸""宗王分兀鲁思")。以前两头一刀切：元首制罗马、西方中央集权君主制打下的地一律设行省/辖区；
// 游牧政体的配置不划分打下的地(partition_conquests=false)，汗国打下的地永远留在汗庭直辖，只在换汗时(8 城以上、有在世兄弟)分一次。
//   · 元首制：打下的异族边地(法理文化不同于本朝、挨着帝国以外的国)有机会立附庸王国，本地首领为王、政体随当地文化，
//     附庸王国连同别的非行省成员合计不超过 ClientShare 的城(罗马的附庸王国、盟邦)；
//   · 西方中央集权君主制：打下的地设辖区时，成年亲王(君主的儿子、兄弟，不含储君)有一位就改封给他一块封地，
//     封地连同别的非辖区成员合计不超过 ApanageShare 的城，每年至多一块(法兰西式亲王封地)；
//   · 游牧汗国(真游牧，见 KingdomExtension.IsTrueNomad)：汗庭直辖的法理每年至多一块分给成年宗王(汗子、汗弟)做兀鲁思，
//     一位宗王一块；都城法理、开国法理、本族老家不分(见 Empire.GetProtectedDirectCityIds)，汗庭至少留 KhanShare 的城；
//     没有宗王的地留汗庭直辖。施行了行省制度的汗国打下的地照旧设行省，设行省时同集权君主制一样有宗王就改封他做兀鲁思，
//     兀鲁思连同别的非行省成员合计不超过 ProvincialUlusShare 的城、每年至多一块(元朝的宗王出镇)。
public static class PartialEnfeoffment
{
    public const float ClientShare = 0.2f, ClientChance = 0.5f;
    public const float ApanageShare = 0.15f, ProvincialUlusShare = 0.3f;
    public const float KhanShare = 0.5f;
    public const int UlusMinRealmCities = 3, UlusMandate = 3;
    public const float GrantIntervalYears = 1f;

    // ───── 规则(纯函数，回归测这几样) ─────

    // 封出去之后，封授的城(已有的非行省成员 + 这一片)不超过全境的 share
    public static bool WithinShare(int grantedCities, int regionCities, int realmCities, float share) =>
        realmCities > 0 && regionCities > 0 && grantedCities + regionCities <= realmCities * share;

    // 分出这块之后汗庭直辖的城至少还占全境的 KhanShare
    public static bool KhanKeepsHalf(int coreCities, int grantCities, int realmCities) =>
        grantCities > 0 && coreCities - grantCities >= Math.Ceiling(realmCities * KhanShare);

    // 每年至多一块：没封过、或离上回满一年(世界时间倒退了也算满)
    public static bool GrantDue(double lastAt, double now, double yearsSince) =>
        lastAt < 0d || now < lastAt || yearsSince >= GrantIntervalYears;

    public static bool Foreign(string titleCulture, string realmCulture) =>
        CultureService.IsValidCulture(titleCulture) && CultureService.IsValidCulture(realmCulture) &&
        !string.Equals(titleCulture, realmCulture, StringComparison.Ordinal);

    // 附庸王国的政体：随当地文化，城邦共和(会一城一国地拆开)、近代、原始、穿越、殖民的一律按封建王国
    public static bool ClientRegimeAllowed(RegimeType type) =>
        !RegimeManager.IsPrimitive(type) && type is not (RegimeType.ClassicalRepublic or RegimeType.Modern or RegimeType.Republic or
            RegimeType.Transmigrant or RegimeType.Colonial or RegimeType.Company);

    // ───── 设行省/辖区的那一刻改封(Empire.AutoEstablishTerritorialDivisions) ─────

    // 返回 true：这片法理不设行省/辖区，改封 grantee；附庸王国连政体一起换成当地的
    public static bool TryPick(Empire empire, KingdomTitle title, List<City> region, ref Regime regime, out Actor grantee)
    {
        grantee = null;
        Kingdom core = empire?.CoreKingdom;
        if (title == null || title.isRekt() || region == null || region.Count == 0 || core == null || World.world == null) return false;
        int realm = RealmCities(empire);
        int granted = GrantedCities(empire);
        if (empire.IsPrincipate())
        {
            if (!WithinShare(granted, region.Count, realm, ClientShare)) return false;
            string culture = CultureService.GetEffectiveTitleCulture(title);
            if (!Foreign(culture, CultureService.GetRealmCulture(core)) || !Frontier(empire, region)) return false;
            if (UnityEngine.Random.value >= ClientChance) return false;
            Actor local = LocalRuler(empire, title, region);
            Regime clientRegime = ClientRegime(culture);
            if (local == null || clientRegime == null) return false;
            regime = clientRegime;
            grantee = local;
            return true;
        }
        // 施行了行省制度的汗国设行省时，宗王照样分兀鲁思(元朝的宗王出镇)，只是封顶 ProvincialUlusShare
        bool nomadProvincial = empire.IsNomadProvincial() && TrueNomad(core);
        if (empire.IsWesternCentralized() || nomadProvincial)
        {
            if (!WithinShare(granted, region.Count, realm, nomadProvincial ? ProvincialUlusShare : ApanageShare) ||
                !GrantDue(empire.data.partial_grant_at, World.world.getCurWorldTime(), YearsSince(empire.data.partial_grant_at))) return false;
            grantee = Princes(empire).FirstOrDefault();
            return grantee != null;
        }
        return false;
    }

    // 改封成了(Empire 建国成功后调)：记下时间，亲王封地每年至多一块；记下这是封授的封地，国王绝嗣就并为行省/辖区
    public static void MarkGranted(Empire empire, Kingdom fief = null)
    {
        if (empire?.data != null && World.world != null) empire.data.partial_grant_at = World.world.getCurWorldTime();
        if (fief?.data != null && !fief.isRekt()) MarkFief(fief);
    }

    private static void MarkFief(Kingdom fief) => fief.GetOrCreate().partial_grant = true;

    // 绝嗣并为行省/辖区(用户 2026-11-18："罗马改为国王绝嗣后吞并, 和亲王一个逻辑""但是这仅限罗马, 神罗却不是这样子的"，
    // 问答选"按体制分")：元首制的附庸王国、集权君主制(不是选帝制)的亲王封地，国王死时没有能承袭的儿子，城收归核心、
    // 整片改设行省/辖区(GraceEdictService.TryHandleVacancy 里同郡国并行一起处理)；有儿子照常承袭。
    // 封建制帝国、选帝制的神罗(就算施行了集权君主制)、已经封建化的罗马文化帝国：诸侯绝嗣照常继承。兀鲁思照游牧旧制，不收
    public static bool RevertsOnExtinction(bool tagged, bool principate, bool centralized, bool elective) =>
        tagged && (principate || centralized && !elective);

    public static bool RevertsOnExtinction(Empire empire, Kingdom fief) =>
        fief?.data != null && empire != null &&
        RevertsOnExtinction(fief.GetOrCreate().partial_grant, empire.IsPrincipate(), empire.IsWesternCentralized(),
            HolyRomanSystem.IsElective(empire));

    // ───── 游牧汗国宗王分兀鲁思(Empire.update，每月看一回) ─────

    private static readonly Dictionary<long, double> LastCheck = new();
    private static object _world;

    public static void TickUlus(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt() || empire.data == null || World.world == null) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            LastCheck.Clear();
        }
        if (core.GetRegime()?.type != RegimeType.YouMu || !TrueNomad(core) ||
            AdministrationReorganizationSystem.IsRunning(empire)) return;
        double now = World.world.getCurWorldTime();
        if (LastCheck.TryGetValue(empire.id, out double last) && now >= last && Date.getMonthsSince(last) < 1) return;
        LastCheck[empire.id] = now;
        if (!GrantDue(empire.data.partial_grant_at, now, YearsSince(empire.data.partial_grant_at))) return;
        int realm = RealmCities(empire);
        if (realm < UlusMinRealmCities) return;
        int coreCities = core.cities.Count(city => city != null && !city.isRekt() && city.kingdom == core);
        HashSet<long> protectedCities = empire.GetProtectedDirectCityIds();
        foreach (KingdomTitle title in EnfeoffmentHelper.FindEnfeoffableTitles(empire))
        {
            List<City> cities = title.getCities().Where(city => city != null && !city.isRekt() && city.kingdom == core).ToList();
            if (cities.Count == 0 || cities.Any(city => protectedCities.Contains(city.id)) ||
                !KhanKeepsHalf(coreCities, cities.Count, realm)) continue;
            Actor prince = Princes(empire).FirstOrDefault();
            if (prince == null) return;
            if (!EnfeoffmentHelper.GrantTitle(empire, prince, title, UlusMandate)) return;
            MarkGranted(empire);
            LogService.LogInfo($"[EmpireCraft][宗王兀鲁思] {empire.GetEmpireName()} 把 {title.data?.name}({cities.Count}城) 分给 {prince.getName()}，汗庭剩 {core.cities.Count}/{realm} 城");
            return;
        }
    }

    // ───── 人选与计数 ─────

    // 成年宗王：君主的儿子(年长的在前)，其次兄弟；同宗、不是君主、不是储君、还没受封。
    // 无小人模式下地图上没有合格的，按长幼落成至多两位虚拟族人再挑(同 EnfeoffmentHelper.FindEnfeoffmentCandidate)
    public static List<Actor> Princes(Empire empire)
    {
        var result = new List<Actor>();
        PersonalClanIdentity ruler = empire?.Emperor?.GetPersonalIdentity();
        if (ruler == null) return result;
        var seen = new HashSet<long>();
        List<PersonalClanIdentity> kin = SpecificClanManager.getChildren(ruler).Select(pair => pair.Item2)
            .Where(identity => identity != null).OrderBy(identity => identity.rank)
            .Concat(SpecificClanManager.GetSiblingsWithRelation(ruler).Select(pair => pair.Item2)
                .Where(identity => identity != null).OrderBy(identity => identity.rank))
            .Where(identity => seen.Add(identity.id) && identity.is_alive && identity.CanHeir(ruler) &&
                               SpecificClanManager.SameLineage(identity._specificClan, empire.EmpireSpecificClan))
            .ToList();
        foreach (PersonalClanIdentity identity in kin)
            if (IsEligiblePrince(identity._actor, empire)) result.Add(identity._actor);
        if (result.Count > 0 || !CityPopulationSystem.AbstractPopulationEnabled) return result;
        foreach (PersonalClanIdentity identity in kin.Where(identity => identity.is_virtual).Take(2))
        {
            Actor realized = identity.Realize();
            if (IsEligiblePrince(realized, empire)) result.Add(realized);
        }
        return result;
    }

    public static bool IsEligiblePrince(Actor actor, Empire empire)
    {
        if (actor?.data == null || actor.isRekt() || !actor.isAlive() || actor.isKing() || empire?.CoreKingdom == null) return false;
        if (actor == empire.Emperor || RealisticAgeSystem.IsMinor(actor) || !actor.isUnitFitToRule()) return false;
        Kingdom core = empire.CoreKingdom;
        if (actor.id == (core.GetHeir()?.id ?? -1L) || core.GetOrCreate()?.scripted_heirs?.Contains(actor.id) == true) return false;
        return !actor.HasVirtualEnfeoff(empire) && actor.kingdom?.GetEmpire() == empire;
    }

    // 附庸王国的王：法理首府(没有就头一座城)的城主，其次城里的成年人；不要宗室、不要别国君主
    private static Actor LocalRuler(Empire empire, KingdomTitle title, List<City> region)
    {
        City seat = title.title_capital != null && region.Contains(title.title_capital) ? title.title_capital : region[0];
        SpecificClan royal = empire.EmpireSpecificClan;
        long heir = empire.CoreKingdom?.GetHeir()?.id ?? -1L;
        bool Fit(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                 !actor.isKing() && actor.id != heir && actor.isUnitFitToRule() &&
                                 (royal == null || !SpecificClanManager.SameLineage(actor.GetSpecificClan(), royal));
        if (seat.hasLeader() && Fit(seat.leader)) return seat.leader;
        return seat.getUnits()?.FirstOrDefault(Fit);
    }

    private static Regime ClientRegime(string culture)
    {
        if (RegimeManager.regimes == null) return null;
        if (CompositeEmpireService.TryGetCultureRegime(culture, out RegimeType type, out Regime regime) &&
            regime != null && ClientRegimeAllowed(type)) return regime;
        return RegimeManager.regimes.TryGetValue(RegimeType.Feudalism, out Regime feudal) ? feudal : null;
    }

    // 边地：这片有城挨着帝国以外的国(无人区不算)
    private static bool Frontier(Empire empire, List<City> region) =>
        region.Any(city => city.neighbours_cities?.Any(other =>
            other?.kingdom != null && !other.isRekt() && !other.kingdom.wild && !other.kingdom.isRekt() &&
            other.kingdom.GetEmpire() != empire) == true);

    public static int RealmCities(Empire empire) =>
        empire?.kingdoms_list?.Where(kingdom => kingdom != null && !kingdom.isRekt())
            .Sum(kingdom => kingdom.cities?.Count ?? 0) ?? 0;

    // 已封出去的城：不是行省/辖区的成员(封国、附庸王国、自治城邦、自由市……)
    public static int GrantedCities(Empire empire) =>
        empire?.kingdoms_list?.Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom &&
                                                !kingdom.IsAdministrativeKingdomType())
            .Sum(kingdom => kingdom.cities?.Count ?? 0) ?? 0;

    // 真游牧(阿兹特克这类挂在游牧线上、并不游牧的不算，见 KingdomExtension.IsTrueNomad)
    private static bool TrueNomad(Kingdom kingdom) => KingdomExtension.IsTrueNomad(kingdom);

    private static double YearsSince(double timestamp) => timestamp < 0d ? double.MaxValue : Date.getYearsSince(timestamp);
}
