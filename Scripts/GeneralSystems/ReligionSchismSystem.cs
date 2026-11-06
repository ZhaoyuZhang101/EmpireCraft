using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教分裂(用户 2026-10-26 选"东西教会大分裂""逊尼什叶分裂""宗教改革也看教权与圣战失利")。分出来的教会都记母宗教
// (ParentReligionId)，不算地方教会，不会被并入正教(见 ReligionSphere)：
//   · 东西教会大分裂(每个普世教会一次；用户 2026-11-06 "教宗没法分裂""比如君士坦丁堡和罗马")：以前要帝都离圣地超过地图宽高的三成，
//     还要 PapacySystem.Applies(君主制)——世界地图横跨约 186 个经度，三成是 56°，罗马到君士坦丁堡才 16.5°、到莫斯科 25°；
//     剧本里罗马文化的拜占庭又是元首制(古典共和)，根本进不了门，东西从来分不开。现在立教满 SchismMinAge 年后，奉它的帝国
//     (罗马一线或文化属本教天下，不论政体；SchismMinCities 城以上；不是教廷所在、庇护或控制教廷的帝国；本朝皇帝没受教宗加冕)：
//       - 远：帝都离圣地至少 max(SchismMinTiles 格, 地图宽高的 SchismMinShare)，而且与"教廷一边"隔着圣地(从圣地看，帝都与教廷一边各城的
//         重心方向夹角的余弦不到 OppositeCos，约 100° 以上；重心离圣地不到下限的四分之一时不看方向)。
//         教廷一边：教廷所在国、庇护者，在教廷往来之列的帝国(PapacySystem.Applies)与罗马一线的君主国，候选帝国自己不算——
//         拜占庭不交什一税、不受教宗加冕，不在其中；800 年剧本里罗马 → 君士坦丁堡向东，
//         教廷一边(法兰克、威塞克斯、阿斯图里亚斯)的重心在罗马西北，夹角约 137°；基辅约 108°、莫斯科约 104°、保加利亚约 128°；
//         英格兰、西班牙、斯堪的纳维亚与它同侧，波兰、匈牙利约 90°，都分不出去；
//       - 异(至少一项)：文化不同于教廷所在国(有庇护者时按庇护者)的文化、不同于教廷一边各城的主流文化、不在教廷往来之列
//         (元首制、共和等，PapacySystem.Applies 不成立)、帝都离圣地超过地图宽高的 FarShare；
//       - 每年 SchismChance，每多一项隔阂 +SchismEstrangedChance(至多 SchismMaxChance)：与教廷或其庇护者交战、遭绝罚、教廷正闹对立教宗、
//         教权低于 40、另有受教宗加冕的皇帝(两帝并立，原型 800 年查理曼加冕)、法理上声索圣地；
//     另立正教会：圣地在帝都，皇帝兼管教会(Caesaropapist：不算教廷之囚，加冕在自家)，帝国各国各城改宗；就近(离新圣地比离旧圣地近)、
//     不与它交战的同教独立国与帝国中央各 FollowChance 随之(同文化或文化异于教廷的 +FollowKinBonus，受教宗加冕或与教廷同盟的 −FollowLoyalPenalty；
//     帝国中央改宗后成员国陆续随之，见 ReligionConversionSystem.FollowRealm)；旧教教权 −10，此后 PeaceYears 年不讨伐这支"异端"；
//   · 逊尼什叶分裂(每个伊斯兰教一次)：哈里发、苏丹更替之乱破局开争位之战时(SchismSystem.Proclaim)，ShiaChance 机会
//     争位者另奉什叶派(圣地在其都城，自有伊玛目——哈里发座)，随攻方的割据成员同改；已分出过什叶派的，争位者
//     IslamicSchismSystem.ContestJoinShiaChance 投向它；奉什叶派的帝国争位，争位者 ContestIsmailiChance 另立伊斯玛仪派
//     (争位之外的什叶派、对立哈里发、复古运动见 IslamicSchismSystem)；
//   · 宗教改革提早：普世教会教权低于 PapacySystem.EarlyReformAuthority、圣战失利满 EarlyReformLosses 次(或对立教宗之争拖满
//     PapacySystem.SchismSparkYears 年)时，不必等本文化施行宗教改革，每年 PapacySystem.EarlySparkChance 机会有改革者在一城发端
//     (原型威克里夫、胡斯；见 PapacySystem.Reformation)。以前是仍交什一税的西方帝国整个提前脱离罗马；
//   · 西方大分裂(对立教宗)、宗教改革的逐年结算也挂在这里(见 PapacySystem.Antipope、PapacySystem.Reformation)；
//   · 异文明立教(用户 2026-10-27 截图全图基督教 397/404 城："不同文化的帝国应该有自己创建宗教的机会，甚至分裂宗教""为什么没有触发"，
//     选"复归本土或另立教会""约五十年一次")：各文明的本土大教立在最大的城就停在那一城——外来宗教是全境的国教、邻城又同教，
//     势能压得住；君主皈依只看邻国、境内已奉的世界性宗教；大分裂只给奉教廷的西方帝国。现在奉外来宗教(不是本文明的)、
//     SchismMinCities 城以上、没握着它圣地的帝国，每年 NativeTurnChance：本文明已有本土大教的 NativeRevertShare 复归——
//     皇帝率帝国改奉(成员国陆续随之，城里人按国教势能加倍慢慢改，见 ReligionConversionSystem)；否则从外来宗教分出本国教会：
//     圣地在帝都，记母宗教、天下记本文明，帝国各国各城当场改奉，普世教会的由皇帝兼管。皈依、立教后 TurnCooldownYears 年不再判。
public static class ReligionSchismSystem
{
    private const int SchismMinAge = 80;
    private const int SchismMinCities = 8;
    private const float SchismMinTiles = 40f, SchismMinShare = 0.04f, FarShare = 0.25f, OppositeCos = -0.2f;
    private const float SchismChance = 0.01f, SchismEstrangedChance = 0.01f, SchismMaxChance = 0.04f;
    private const float FollowChance = 0.6f, FollowKinBonus = 0.2f, FollowLoyalPenalty = 0.3f;
    private const int PeaceYears = 50;
    private const double SecondsPerYear = 60d;
    private const float ShiaChance = 0.5f;
    private const float NativeTurnChance = 0.02f, NativeRevertShare = 0.6f;
    private const int TurnCooldownYears = 50;

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    public static void YearlyPass()
    {
        if (World.world?.religions == null || ModClass.IS_CLEAR) return;
        foreach (Religion religion in World.world.religions.ToList())
        {
            if (religion?.data == null || religion.isRekt() || !ReligionFaith.IsPapal(religion)) continue;
            TryGreatSchism(religion);
            // 西方大分裂：两边站队、对立一线推举、公会议弥合或分立(见 PapacySystem.Antipope)
            PapacySystem.SchismYearly(religion);
            // 宗教改革：改革者发端(含提早)、逐城传布、诸侯择教、成员国起兵(见 PapacySystem.Reformation)
            PapacySystem.ReformationYearly(religion);
        }
        TryNativeTurns();
    }

    #region 异文明立教

    private static void TryNativeTurns()
    {
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            Kingdom core = empire?.CoreKingdom;
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || !Alive(core) || !core.hasReligion() ||
                core.capital == null || empire.Emperor?.data == null || !empire.Emperor.isAlive() ||
                Compatibility.AncientWarfareCompatibility.Owns(core) || empire.countCities() < SchismMinCities) continue;
            Religion current = core.religion;
            string sphere = ReligionSphere.SphereOfCulture(CultureService.GetRealmCulture(core));
            if (sphere.Length == 0 || ReligionSphere.SphereOf(current) == sphere) continue;
            // 握着本教圣地的不改
            City holy = current.GetCity();
            if (holy?.kingdom != null && Realm(holy.kingdom) == core) continue;
            double converted = core.GetOrCreate().religion_converted_at;
            if (converted >= 0d && World.world.getCurWorldTime() >= converted && Date.getYearsSince(converted) < TurnCooldownYears) continue;
            if (UnityEngine.Random.value >= NativeTurnChance) continue;
            Religion native = ReligionSphere.OrthodoxOf(sphere);
            // 哈里发国只复归伊斯兰，否则照分本国教派(见 CaliphateFaith)
            if (native != null && native != current && CaliphateFaith.MayAdopt(core, native) && UnityEngine.Random.value < NativeRevertShare)
            {
                ReligionConversionSystem.Convert(core, native, current, "native_revert_log");
                continue;
            }
            NationalChurch(empire, current, sphere);
        }
    }

    // 从外来宗教分出本国教会：圣地在帝都，天下记本文明，帝国各国各城当场改奉
    private static void NationalChurch(Empire empire, Religion old, string sphere)
    {
        Kingdom core = empire.CoreKingdom;
        Actor head = empire.Emperor;
        City capital = core.capital;
        if (head.city != capital) head.joinCity(capital);
        Religion church = World.world.religions.newReligion(head, false);
        if (church?.data == null) return;
        string culture = CultureService.GetRealmCulture(core);
        string people = CultureService.IsValidCulture(culture) ? culture.GetCultureTranslate() : core.GetKingdomName();
        FaithType faith = ReligionFaith.Of(old);
        church.data.name = faith == FaithType.Church
            ? string.Format(LM.Get("national_church_name_church"), people)
            : string.Format(LM.Get("national_branch_name"), people, old.name);
        church.SetCity(capital);
        double now = World.world.getCurWorldTime();
        var data = church.GetOrCreate();
        data.ParentReligionId = old.data.id;
        data.SplitAt = now;
        data.Caesaropapist = faith == FaithType.Church;
        data.Faith = faith.ToString();
        data.Sphere = sphere;
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
            if (Alive(kingdom) && kingdom.religion == old) ConvertKingdom(kingdom, old, church);
        if (core.religion != church) ConvertKingdom(core, core.religion, church);
        empire.Religion = church;
        core.GetOrCreate().religion_converted_at = now;
        if (PapacySystem.Papacy(old) != null) PapacySystem.AddAuthority(old, -5f);
        string text = string.Format(LM.Get("national_church_log"), empire.GetEmpireFullName(), old.name, capital.GetCityName(),
            church.name);
        EventRecorder.Record(empire, text, head);
        ReligionChronicle.Clear(church);
        ReligionChronicle.Record(old, church, ReligionChronicle.KindSchism, text);
        LogService.LogInfo($"[EmpireCraft][宗教] {empire.GetEmpireName()}(天下 {sphere})不奉外来的{old.name}，另立{church.name}");
    }

    #endregion

    #region 东西教会大分裂

    private static void TryGreatSchism(Religion religion)
    {
        var data = religion.GetOrCreate();
        if (data.EasternSplit || !ReligionSphere.IsNative(religion) || ReligionSphere.IsLocalChurch(religion) ||
            religion.getAge() < SchismMinAge) return;
        Kingdom papacy = PapacySystem.Papacy(religion);
        City holy = PapacySystem.HolyCity(religion);
        if (papacy == null || holy == null) return;
        Kingdom see = Realm(papacy);
        Kingdom patron = PapacySystem.PapalPatron(papacy);
        string reference = CultureService.GetRealmCulture(patron ?? see);
        float mapSize = Mathf.Max(MapBox.width, MapBox.height);
        float floor = Mathf.Max(SchismMinTiles, mapSize * SchismMinShare);
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (!EasternCandidate(empire, religion, papacy, patron)) continue;
            Kingdom core = empire.CoreKingdom;
            Vector2 toCapital = core.capital.city_center - holy.city_center;
            float distance = toCapital.magnitude;
            if (distance < floor) continue;
            (Vector2 centroid, int blocCities, string blocCulture) = PapalBloc(religion, core, see, patron);
            Vector2 toBloc = blocCities > 0 ? centroid - holy.city_center : Vector2.zero;
            if (toBloc.magnitude >= floor * 0.25f && Vector2.Dot(toBloc.normalized, toCapital.normalized) > OppositeCos) continue;
            string culture = CultureService.GetRealmCulture(core);
            int divergence = (culture != reference ? 1 : 0) + (culture != blocCulture ? 1 : 0) +
                             (!PapacySystem.Applies(empire) ? 1 : 0) + (distance >= mapSize * FarShare ? 1 : 0);
            if (divergence == 0) continue;
            int estranged = Estrangement(empire, religion, see, patron, holy);
            float chance = Mathf.Min(SchismMaxChance, SchismChance + SchismEstrangedChance * estranged);
            if (UnityEngine.Random.value >= chance) continue;
            LogService.LogInfo($"[EmpireCraft][宗教] 东西教会大分裂：{empire.GetEmpireName()} 离圣地 {distance:0} 格(下限 {floor:0})，" +
                               $"相异 {divergence} 项、隔阂 {estranged} 项，本年几率 {chance:P1}");
            if (EasternSplit(empire, religion, holy, reference)) return;
        }
    }

    // 候选：奉本教的同天下帝国(罗马一线或文化属本教天下，不论政体——拜占庭是元首制)，够大，不握也不护教廷，本朝皇帝没受教宗加冕
    private static bool EasternCandidate(Empire empire, Religion religion, Kingdom papacy, Kingdom patron)
    {
        Kingdom core = empire?.CoreKingdom;
        if (empire?.data == null || empire.isRekt() || empire.IsArchived() || !Alive(core) || core.capital == null ||
            empire.Emperor?.data == null && core.king?.data == null ||
            PapacySystem.ReligionOf(empire) != religion || empire.countCities() < SchismMinCities) return false;
        if (!PapacySystem.IsWestern(empire) &&
            ReligionSphere.SphereOfCulture(CultureService.GetRealmCulture(core)) != ReligionSphere.SphereOf(religion)) return false;
        if (papacy == core || papacy.GetEmpire() == empire || patron == core || RealmNamingSystem.ControlsPapacy(empire, papacy))
            return false;
        return !PapacySystem.IsCrowned(empire) || PapacySystem.State(empire).self_crowned;
    }

    // 教廷一边(候选帝国自己不算)奉本教各城的重心、城数与主流文化
    private static (Vector2 centroid, int cities, string culture) PapalBloc(Religion religion, Kingdom core, Kingdom see, Kingdom patron)
    {
        Vector2 sum = Vector2.zero;
        int count = 0;
        var members = new Dictionary<Kingdom, bool>();
        var cultures = new Dictionary<string, int>();
        foreach (City city in World.world.cities.list)
        {
            if (city?.data == null || city.isRekt() || city.religion != religion || !Alive(city.kingdom)) continue;
            Kingdom realm = Realm(city.kingdom);
            if (realm == core) continue;
            if (!members.TryGetValue(realm, out bool member)) members[realm] = member = InPapalBloc(realm, see, patron);
            if (!member) continue;
            sum += city.city_center;
            count++;
            string culture = CultureService.GetMainCulture(city, initialize: false);
            if (!CultureService.IsValidCulture(culture)) continue;
            cultures.TryGetValue(culture, out int cultureCount);
            cultures[culture] = cultureCount + 1;
        }
        string top = cultures.Count == 0 ? "" : cultures.OrderByDescending(pair => pair.Value).First().Key;
        return (count > 0 ? sum / count : Vector2.zero, count, top);
    }

    // 教廷一边：教廷所在国、庇护者；在教廷往来之列的帝国；罗马一线的君主国(独立王国)
    private static bool InPapalBloc(Kingdom realm, Kingdom see, Kingdom patron)
    {
        if (realm == see || realm == patron) return true;
        Empire empire = realm.GetEmpire();
        if (empire != null && empire.CoreKingdom == realm) return PapacySystem.Applies(empire);
        return InstitutionSystem.GetCultureLine(CultureService.GetRealmCulture(realm)) == PapacySystem.Line &&
               RegimeManager.IsMonarchy(realm.GetRegime()?.type);
    }

    // 隔阂几项：与教廷或其庇护者交战、遭绝罚、对立教宗、教权低落、两帝并立、法理上声索圣地
    private static int Estrangement(Empire empire, Religion religion, Kingdom see, Kingdom patron, City holy)
    {
        Kingdom core = empire.CoreKingdom;
        int count = 0;
        if (core.isInWarWith(see) || patron != null && patron != core && core.isInWarWith(patron)) count++;
        if (PapacySystem.IsExcommunicated(empire)) count++;
        if (religion.GetOrCreate().Schism) count++;
        if (PapacySystem.Authority(religion) < 40f) count++;
        if (PapacySystem.RivalEmperor(empire, religion) != null) count++;
        if (WarGoalSystem.Claimed(core, new[] { holy }).Count > 0) count++;
        return count;
    }

    private static bool EasternSplit(Empire empire, Religion old, City oldHoly, string reference)
    {
        Kingdom core = empire.CoreKingdom;
        Actor head = empire.Emperor ?? core.king;
        City capital = core.capital;
        if (head?.data == null || !head.isAlive() || capital == null) return false;
        if (head.city != capital) head.joinCity(capital);
        Religion church = World.world.religions.newReligion(head, false);
        if (church?.data == null) return false;
        church.data.name = ReligionFaith.PickName("orthodox", 3) ?? church.data.name;
        church.SetCity(capital);
        double now = World.world.getCurWorldTime();
        var churchData = church.GetOrCreate();
        churchData.ParentReligionId = old.data.id;
        churchData.SplitAt = now;
        churchData.Caesaropapist = true;
        churchData.Faith = FaithType.Church.ToString();
        churchData.Sphere = ReligionSphere.SphereOf(old);
        var oldData = old.GetOrCreate();
        oldData.EasternSplit = true;
        oldData.ReligiousPeaceUntil = now + PeaceYears * SecondsPerYear;
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
            if (Alive(kingdom)) ConvertKingdom(kingdom, old, church);
        empire.Religion = church;
        if (head.religion != church) head.setReligion(church);
        // 就近的同教独立国、帝国中央随之(帝国中央改宗后成员国陆续随之，见 ReligionConversionSystem.FollowRealm)
        string culture = CultureService.GetRealmCulture(core);
        Kingdom see = Realm(PapacySystem.Papacy(old));
        var followers = new List<Kingdom>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!Alive(kingdom) || Realm(kingdom) != kingdom || kingdom == core || kingdom.religion != old || kingdom.capital == null ||
                kingdom.isInWarWith(core) || PapacySystem.HoldsHolyCity(kingdom) || kingdom == see) continue;
            float near = Vector2.Distance(kingdom.capital.city_center, capital.city_center);
            float far = Vector2.Distance(kingdom.capital.city_center, oldHoly.city_center);
            if (near >= far) continue;
            float chance = FollowChance;
            string own = CultureService.GetRealmCulture(kingdom);
            if (own == culture || own != reference) chance += FollowKinBonus;
            Empire realm = kingdom.GetEmpire();
            if (realm != null && PapacySystem.IsCrowned(realm) && !PapacySystem.State(realm).self_crowned ||
                see != null && kingdom.hasAlliance() && see.hasAlliance() && kingdom.getAlliance() == see.getAlliance())
                chance -= FollowLoyalPenalty;
            if (UnityEngine.Random.value >= chance) continue;
            ConvertKingdom(kingdom, old, church);
            kingdom.GetOrCreate().religion_converted_at = now;
            if (realm != null && realm.CoreKingdom == kingdom) realm.Religion = church;
            followers.Add(kingdom);
        }
        PapacySystem.AddAuthority(old, -10f);
        string text = string.Format(LM.Get("great_schism_log"), empire.GetEmpireFullName(), oldHoly.GetCityName(), church.name);
        TranslateHelper.LogEventMessage(text, core);
        ReligionChronicle.Clear(church);
        ReligionChronicle.Record(old, church, ReligionChronicle.KindSchism, text);
        empire.RecordHistory(directContent: text, actorId: head.id, kingdomId: core.id);
        if (followers.Count > 0)
        {
            string names = string.Join("、", followers.Take(4).Select(kingdom => kingdom.GetKingdomName()));
            string follow = string.Format(LM.Get("great_schism_followers_log"), names, followers.Count, church.name);
            TranslateHelper.LogEventMessage(follow, followers[0]);
            ReligionChronicle.Record(old, church, ReligionChronicle.KindSchism, follow);
        }
        LogService.LogInfo($"[EmpireCraft][宗教] 东西教会大分裂：{empire.GetEmpireName()} 以 {capital.GetCityName()} 为圣地另立{church.name}，" +
                           $"随之 {followers.Count} 国");
        return true;
    }

    #endregion

    #region 逊尼什叶分裂

    // 哈里发、苏丹更替之乱开争位之战时(见 SchismSystem.Proclaim)。用户 2026-11-06 选"伊斯兰分裂更多样"，另加两种(机会见 IslamicSchismSystem)：
    //   · 本教已分出过什叶派、它还在：争位者 ContestJoinShiaChance 投向它(连随攻方的割据成员)；
    //   · 帝国本就奉什叶派(伊玛目之位相争，原型 765 年)：争位者 ContestIsmailiChance 另立伊斯玛仪派(每个什叶派一次)。
    // 争位分出的什叶派记下分支(IslamicSchismSystem.MarkShia)，此后同一母宗教不再另立什叶派(争位之外的立、改奉见 IslamicSchismSystem)
    public static void OnSuccessionContest(Empire empire, int kind, Kingdom claimant, List<Kingdom> followers)
    {
        if (kind != SchismSystem.Caliph && kind != SchismSystem.Sultan || !Alive(claimant)) return;
        Religion old = empire?.CoreKingdom?.religion;
        if (old?.data == null || !ReligionFaith.IsCaliphal(old) || claimant.religion != old) return;
        if (IslamicSchismSystem.IsShia(old))
        {
            Religion ismaili = IslamicSchismSystem.OnShiaContest(old, claimant, followers, out string split);
            if (ismaili != null && split != null && empire.data != null)
                empire.RecordHistory(directContent: split, actorId: claimant.king?.id ?? -1L, kingdomId: claimant.id);
            return;
        }
        if (!ReligionSphere.IsNative(old)) return;
        var oldData = old.GetOrCreate();
        if (oldData.ShiaSplit)
        {
            Religion existing = IslamicSchismSystem.ShiaOf(old);
            if (existing == null || UnityEngine.Random.value >= IslamicSchismSystem.ContestJoinShiaChance) return;
            ConvertKingdom(claimant, old, existing);
            foreach (Kingdom follower in followers ?? new List<Kingdom>())
                if (Alive(follower) && follower.religion == old) ConvertKingdom(follower, old, existing);
            string joined = string.Format(LM.Get("shia_contest_join_log"), claimant.GetKingdomName(), existing.name, old.name);
            TranslateHelper.LogEventMessage(joined, claimant);
            ReligionChronicle.Record(old, existing, ReligionChronicle.KindSchism, joined);
            if (empire.data != null) empire.RecordHistory(directContent: joined, actorId: claimant.king?.id ?? -1L, kingdomId: claimant.id);
            LogService.LogInfo($"[EmpireCraft][宗教] {claimant.GetKingdomName()} 争位，投向{existing.name}");
            return;
        }
        if (UnityEngine.Random.value >= ShiaChance) return;
        Actor head = claimant.king;
        City capital = claimant.capital;
        if (head?.data == null || !head.isAlive() || capital == null) return;
        if (head.city != capital) head.joinCity(capital);
        Religion shia = World.world.religions.newReligion(head, false);
        if (shia?.data == null) return;
        shia.data.name = ReligionFaith.PickName("shia", 1) ?? shia.data.name;
        shia.SetCity(capital);
        var shiaData = shia.GetOrCreate();
        shiaData.ParentReligionId = old.data.id;
        shiaData.SplitAt = World.world.getCurWorldTime();
        shiaData.Faith = FaithType.Islam.ToString();
        shiaData.Sphere = ReligionSphere.SphereOf(old);
        oldData.ShiaSplit = true;
        IslamicSchismSystem.MarkShia(old, shia);
        ConvertKingdom(claimant, old, shia);
        foreach (Kingdom follower in followers ?? new List<Kingdom>())
            if (Alive(follower) && follower.religion == old) ConvertKingdom(follower, old, shia);
        string text = string.Format(LM.Get("shia_split_log"), claimant.GetKingdomName(), shia.name, old.name);
        TranslateHelper.LogEventMessage(text, claimant);
        ReligionChronicle.Clear(shia);
        ReligionChronicle.Record(old, shia, ReligionChronicle.KindSchism, text);
        if (empire.data != null) empire.RecordHistory(directContent: text, actorId: head.id, kingdomId: claimant.id);
        LogService.LogInfo($"[EmpireCraft][宗教] {claimant.GetKingdomName()} 争位，另奉{shia.name}(分自{old.name})");
    }

    #endregion

    #region 宗教改革提早

    // 教权低落、圣战屡败：改革者提早发端(见 PapacySystem.TrySpark)
    private const int EarlyReformLosses = 2;

    public static bool EarlyReformation(Religion religion)
    {
        var data = religion?.GetOrCreate();
        return data != null && PapacySystem.Papacy(religion) != null &&
               PapacySystem.Authority(religion) < PapacySystem.EarlyReformAuthority && data.CrusadeLosses >= EarlyReformLosses;
    }

    #endregion

    // 一国改奉分出来的教会：国、君主、奉旧教的城(连城中实体)
    private static void ConvertKingdom(Kingdom kingdom, Religion old, Religion church)
    {
        kingdom.setReligion(church);
        if (kingdom.king?.data != null && kingdom.king.isAlive() && kingdom.king.religion == old) kingdom.king.setReligion(church);
        foreach (City city in kingdom.cities.ToList())
            if (city?.data != null && !city.isRekt() && city.religion == old) BackgroundReligionSystem.Convert(city, church, old);
    }
}
