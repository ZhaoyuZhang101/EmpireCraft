using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 西方大分裂(用户 2026-11-06 "教宗没法分裂"，选"真正的对立教宗")。以前大分裂只是宗教上的一个标记(记日志、教权目标 −20、
// 教宗正统 −15)，没有第二位教宗，也没有谁站哪边。现在：
//   · 起(见 UpdateCaptivity)：教廷之囚满 SchismYears 年，或教宗正统低于 PopeLegitimacy.AntipopeBelow 时每年 PopeLegitimacy.AntipopeChance；
//     上次分裂收场后 SchismCooldownYears 年内不再起。要有扶立者：奉本教、RivalMinCities 城以上、不是教廷所在(挟持者)、不握圣地的国
//     (帝国按中央)里，与教廷交战、遭绝罚、对教廷观感差、离圣地远的优先，再按城数；对立教宗出自扶立者都城与对立一边的主教领
//     (有教士特质的优先，再按威望，见 PapalElection.PickCandidate)，入住扶立者都城、加教士特质——那座都城就是对立教廷(原型阿维尼翁)；
//     扶立不成就不起分裂；
//   · 服从(站队)：奉本教的独立王国与帝国中央各择一位教宗(成员随中央)，每年重算，换边要越过 ±ObedienceSwitch 的滞后。倾向对立教宗：
//     扶立者本身、与教廷交战(+2)、遭原教宗绝罚(+1.5)、与扶立者同盟(+1.5)、对扶立者观感比对教廷好(至多 ±1.5)、离对立教廷比离圣地近
//     (至多 ±1.5)、与扶立者同文化(+0.5)；倾向原教宗：教廷所在(挟持者)本身、与扶立者交战(−2)、与教廷同盟(−1.5)、受原教宗加冕(−0.5)、
//     控制教廷(−2)、与教廷同文化(−0.5)；另有按国号定的一点偏好(±0.3)。帝国与 MajorCities 城以上的大国站队、换边记世界日志与宗教大事记；
//   · 两边各管各的：什一税交给自己那边的教廷(对立一边交扶立者)；加冕由自己那边的教宗(对立一边在对立教廷受冕，不必远征，
//     正统 +AntipopeCrownedLegitimacy，不授普世之名)；绝罚各管各的——对立一边与扶立者交战的遭对立教宗绝罚，原教宗的绝罚管不到对立一边，
//     换了服从，另一位教宗下的绝罚作废；叙任权之争、悔罪对着自己那边的教宗；圣战只由原教宗号召，对立一边不响应；
//     教宗推举(PapalElection)的枢机不含对立一边的主教领；对立教宗死了，由对立一边的枢机在对立教廷另推一位(对立一线历任记在 PapalSchismData.line)；
//     对立教廷所在城失陷，迁到对立一边最强一国的都城，没有就断绝；
//   · 收场：满 CouncilMinYears 年后，教廷脱困、教宗正统(不计对立教宗那一项)回到 PopeLegitimacy.HealedAt 以上、对立一边的城不到全教一半时，
//     每年 CouncilChance 在最强一国(扶立者、教廷所在之外)的都城开公会议，对立教宗退位，复归一统(原型康斯坦茨)；对立一边的城不到全教
//     CollapseShare 时每年 CollapseCouncilChance 同样开公会议；扶立者亡国或改教、对立教廷无处可迁即断绝；
//     满 SeparateYears 年而对立一边的城占全教 SeparateShare 以上(或满 DominantYears 年而占 DominantShare 以上——圣地一边众叛亲离，
//     教宗却是圣地之主废不掉)，每年 SeparateChance 自成一教(记母宗教，圣地在对立教廷，由扶立者兼管)，
//     对立一边各国连城改奉，母宗教 ReligiousPeaceYears 年内不讨伐它；拖满 MaxSchismYears 年必收场(对立一边够大就分立，否则公会议)；
//     公会议收场教权 +5，分立教权 −10；公会议、分立时对立教宗下的绝罚一并作废；
//   · 只有本土的普世教会闹对立教宗，分出的教会(国教会、新教会、皇帝兼管的正教会)不闹；
//   · 旧档只有大分裂标记、没有对立教宗的，头一次年度结算时补扶立(起因记"教会早已分裂")；扶立不成照旧规则(教廷脱困、教宗正统回升)弥合；
//     分出的教会挂着的旧标记直接去掉；
//   · 窗口：宗教窗口左栏与教廷页列两位教宗、两边服从的国与占比、对立一线历任(收场后列上次大分裂)，概况页各国标奉哪位，
//     教廷与帝国页列本国服从与另一位教宗，天下宗教名册的宗教卡多一行对立教宗。
public static partial class PapacySystem
{
    public const int SchismByCaptivity = 1, SchismByWeakPope = 2, SchismByLegacy = 3;
    public const int SchismEndCouncil = 1, SchismEndCollapse = 2, SchismEndSeparated = 3, SchismEndExtinct = 4;
    public const int PapalObedienceNone = 0, PapalObedienceRome = 1, PapalObedienceAntipope = 2;
    public const int AntipopeCrownedLegitimacy = 4;

    private const int SchismCooldownYears = 50;
    private const int RivalMinCities = 3;
    private const int CouncilMinYears = 8, CollapseMinYears = 3;
    private const float CouncilChance = 0.25f, CollapseShare = 0.15f, CollapseCouncilChance = 0.5f;
    private const int SeparateYears = 45, DominantYears = 20, MaxSchismYears = 80;
    private const float SeparateShare = 0.35f, DominantShare = 0.85f, SeparateChance = 0.2f;
    private const float ObedienceSwitch = 0.4f;
    private const int MajorCities = 6;
    private const int RivalLineMax = 20;

    #region 查询

    // 进行中的大分裂(没有对立教宗的旧档标记不算)
    public static PapalSchismData ActiveSchism(Religion religion)
    {
        if (religion?.data == null) return null;
        var data = religion.GetOrCreate();
        PapalSchismData schism = data.PapalSchism;
        return data.Schism && schism != null && schism.started_at >= 0d && schism.ended_at < 0d ? schism : null;
    }

    public static Actor Antipope(Religion religion)
    {
        PapalSchismData schism = ActiveSchism(religion);
        Actor pope = schism != null && schism.pope_id >= 0 ? World.world.units.get(schism.pope_id) : null;
        return pope?.data != null && !pope.isRekt() && pope.isAlive() ? pope : null;
    }

    // 对立教廷(扶立者，帝国按中央)与所在城
    public static Kingdom AntipopeSee(Religion religion)
    {
        PapalSchismData schism = ActiveSchism(religion);
        Kingdom patron = schism != null && schism.patron_id >= 0 ? World.world.kingdoms.get(schism.patron_id) : null;
        return SchismAlive(patron) ? patron : null;
    }

    public static City AntipopeSeeCity(Religion religion)
    {
        PapalSchismData schism = ActiveSchism(religion);
        City city = schism != null && schism.see_city_id >= 0 ? World.world.cities.get(schism.see_city_id) : null;
        return city?.data != null && !city.isRekt() ? city : null;
    }

    // 一国(按所在帝国中央)奉哪位教宗：没有大分裂时 PapalObedienceNone
    public static int PapalObedience(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt() || !kingdom.hasReligion()) return PapalObedienceNone;
        PapalSchismData schism = ActiveSchism(kingdom.religion);
        if (schism == null) return PapalObedienceNone;
        Kingdom realm = Realm(kingdom);
        return schism.obedience.Contains(realm.id) ? PapalObedienceAntipope : PapalObedienceRome;
    }

    public static bool FollowsAntipope(Kingdom kingdom) => PapalObedience(kingdom) == PapalObedienceAntipope;

    public static bool FollowsAntipope(Empire empire) => empire?.CoreKingdom != null && FollowsAntipope(empire.CoreKingdom);

    // 本帝国服从的教廷与教宗(对立一边是扶立者与对立教宗)
    public static Kingdom ObeyedSee(Empire empire, Religion religion) =>
        FollowsAntipope(empire) ? AntipopeSee(religion) : Papacy(religion);

    public static Actor ObeyedPope(Empire empire, Religion religion) =>
        FollowsAntipope(empire) ? Antipope(religion) : Pope(religion);

    // 本帝国服从的教廷所在城(加冕、悔罪的地方)
    public static City ObeyedSeeCity(Empire empire, Religion religion) =>
        FollowsAntipope(empire) ? AntipopeSeeCity(religion) : HolyCity(religion);

    // 奉某教的国(独立王国与帝国中央)按服从分两边
    public static (List<Kingdom> rome, List<Kingdom> antipope) Obediences(Religion religion)
    {
        var rome = new List<Kingdom>();
        var antipope = new List<Kingdom>();
        PapalSchismData schism = ActiveSchism(religion);
        if (schism == null) return (rome, antipope);
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (!SchismAlive(kingdom) || Realm(kingdom) != kingdom || kingdom.religion != religion) continue;
            (schism.obedience.Contains(kingdom.id) ? antipope : rome).Add(kingdom);
        }
        rome.Sort((a, b) => SchismCities(b).CompareTo(SchismCities(a)));
        antipope.Sort((a, b) => SchismCities(b).CompareTo(SchismCities(a)));
        return (rome, antipope);
    }

    // 对立一边的城占全教的比例(各国按所在帝国中央的服从)
    public static float AntipopeShare(Religion religion)
    {
        PapalSchismData schism = ActiveSchism(religion);
        if (schism == null) return 0f;
        int total = 0, rival = 0;
        foreach (Kingdom kingdom in World.world.kingdoms.list)
        {
            if (!SchismAlive(kingdom) || kingdom.religion != religion) continue;
            int cities = kingdom.cities.Count;
            total += cities;
            if (schism.obedience.Contains(Realm(kingdom).id)) rival += cities;
        }
        return total == 0 ? 0f : rival / (float)total;
    }

    // 两帝并立：另一家奉本教、本朝皇帝受(原)教宗加冕的帝国(东西教会大分裂的隔阂之一，见 ReligionSchismSystem)
    public static Empire RivalEmperor(Empire empire, Religion religion)
    {
        foreach (Empire other in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (other == empire || other?.data == null || other.isRekt() || other.IsArchived() || ReligionOf(other) != religion ||
                !IsCrowned(other)) continue;
            PapalRelationState state = State(other);
            if (!state.self_crowned && !state.crowned_by_rival) return other;
        }
        return null;
    }

    // 庇护或控制教廷的国(帝国按中央)：教廷在帝国之内的是帝国中央；保护约期内的保护人(见 HolyRomanSystem.ProtectedBy)；宗主
    public static Kingdom PapalPatron(Kingdom papacy)
    {
        if (papacy?.data == null || papacy.isRekt()) return null;
        Empire empire = papacy.GetEmpire();
        if (empire?.CoreKingdom != null && empire.CoreKingdom != papacy && !empire.CoreKingdom.isRekt()) return empire.CoreKingdom;
        long id = papacy.GetOrCreate().hre_protector_id;
        Kingdom protector = id >= 0 ? World.world.kingdoms.get(id) : null;
        if (SchismAlive(protector) && HolyRomanSystem.ProtectedBy(papacy, protector)) return Realm(protector);
        Kingdom sovereign = EmpireCoreControl.Sovereign(papacy);
        return sovereign != null && sovereign != papacy && SchismAlive(sovereign) ? Realm(sovereign) : null;
    }

    private static bool SchismAlive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    private static int SchismCities(Kingdom realm)
    {
        Empire empire = realm?.GetEmpire();
        return empire != null && empire.CoreKingdom == realm ? empire.countCities() : realm?.cities?.Count ?? 0;
    }

    private static bool SchismAllied(Kingdom a, Kingdom b) =>
        a != null && b != null && a != b && a.hasAlliance() && b.hasAlliance() && a.getAlliance() == b.getAlliance();

    private static bool MajorRealm(Kingdom realm)
    {
        Empire empire = realm.GetEmpire();
        return empire != null && empire.CoreKingdom == realm || realm.cities.Count >= MajorCities;
    }

    #endregion

    #region 起

    // 大分裂起头(UpdateCaptivity 调用；旧档只有标记的也走这里补扶立)：扶立不成返回 false，不起分裂。
    // 只有本土的普世教会闹对立教宗：分出的教会(国教会、新教会、皇帝兼管的正教会)不闹
    private static bool StartSchism(Religion religion, int cause)
    {
        if (!ReligionSphere.IsNative(religion) || religion.GetOrCreate().Caesaropapist) return false;
        var data = religion.GetOrCreate();
        double now = World.world.getCurWorldTime();
        PapalSchismData last = data.PapalSchism;
        if (cause != SchismByLegacy && last != null && last.ended_at >= 0d && now >= last.ended_at &&
            Date.getYearsSince(last.ended_at) < SchismCooldownYears) return false;
        Kingdom papacy = Papacy(religion);
        if (papacy == null) return false;
        Kingdom see = Realm(papacy);
        Kingdom patron = PickPatron(religion, see);
        if (patron?.capital == null) return false;
        var schism = new PapalSchismData
        {
            cause = cause,
            started_at = now,
            see_city_id = patron.capital.data.id,
            see_city_name = patron.capital.GetCityName(),
            patron_id = patron.id
        };
        Actor pope = ElectAntipope(religion, schism, patron);
        if (pope == null) return false;
        data.PapalSchism = schism;
        data.Schism = true;
        SeatAntipope(religion, schism, pope, now);
        string text = string.Format(LM.Get("antipope_schism_log"), religion.name, LM.Get("antipope_cause_" + cause),
            patron.GetKingdomName(), pope.getName(), schism.see_city_name, HolyCity(religion)?.GetCityName() ?? "",
            Pope(religion)?.getName() ?? LM.Get("label_none"));
        TranslateHelper.LogEventMessage(text, patron);
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
        pope.RecordPersonalHistory(text);
        RecordRealm(patron, text, pope);
        UpdateObedience(religion, schism, initial: true);
        LogService.LogInfo($"[EmpireCraft][教廷] {religion.name} 大分裂：{patron.GetKingdomName()} 在 {schism.see_city_name} 扶立对立教宗 " +
                           $"{pope.getName()}(起因 {cause})，对立一边 {schism.obedience.Count} 国");
        return true;
    }

    // 扶立者：奉本教的国(帝国按中央)，不是教廷所在(挟持者)、不握圣地、RivalMinCities 城以上；按积怨与城数挑
    private static Kingdom PickPatron(Religion religion, Kingdom see)
    {
        City holy = HolyCity(religion);
        float mapSize = Mathf.Max(1f, Mathf.Max(MapBox.width, MapBox.height));
        Kingdom best = null;
        float bestScore = 0f;
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!SchismAlive(kingdom) || Realm(kingdom) != kingdom || kingdom == see || kingdom.religion != religion ||
                kingdom.capital == null || kingdom.king?.data == null || !kingdom.king.isAlive() || HoldsHolyCity(kingdom)) continue;
            int cities = SchismCities(kingdom);
            if (cities < RivalMinCities) continue;
            float grievance = 1f;
            if (see != null && kingdom.isInWarWith(see)) grievance += 1.5f;
            Empire empire = kingdom.GetEmpire();
            if (empire != null && empire.CoreKingdom == kingdom && IsExcommunicated(empire)) grievance += 1f;
            if (see != null && DiplomacyLedger.Opinion(kingdom, see) < 0) grievance += 0.5f;
            if (SchismAllied(kingdom, see)) grievance -= 0.8f;
            if (holy != null)
                grievance += 0.5f * Mathf.Min(1f, Vector2.Distance(kingdom.capital.city_center, holy.city_center) / (mapSize * 0.25f));
            float score = cities * Mathf.Max(0.1f, grievance);
            if (score <= bestScore) continue;
            best = kingdom;
            bestScore = score;
        }
        return best;
    }

    // 对立一线的枢机：扶立者都城与对立一边的主教领都城
    private static Actor ElectAntipope(Religion religion, PapalSchismData schism, Kingdom patron)
    {
        var cities = new List<City> { patron.capital };
        foreach (Kingdom kingdom in World.world.kingdoms.list)
            if (SchismAlive(kingdom) && kingdom.religion == religion && kingdom.capital != null && HolyRomanSystem.IsChurchEstate(kingdom) &&
                (Realm(kingdom) == patron || schism.obedience.Contains(Realm(kingdom).id)))
                cities.Add(kingdom.capital);
        Actor rome = Pope(religion);
        Actor pick = PapalElection.PickCandidate(cities, patron.king);
        if (pick == rome) pick = null;
        return pick ?? CityPopulationSystem.SpawnOfficial(patron.capital, patron);
    }

    private static void SeatAntipope(Religion religion, PapalSchismData schism, Actor pope, double now)
    {
        City see = World.world.cities.get(schism.see_city_id);
        if (see?.data != null && !see.isRekt() && pope.city != see) pope.joinCity(see);
        if (!pope.hasTrait("cleric")) pope.addTrait("cleric");
        if (pope.religion != religion) pope.setReligion(religion);
        CloseAntipopeRecord(schism, now);
        schism.pope_id = pope.id;
        schism.line ??= new List<RivalPopeRecord>();
        schism.line.Add(new RivalPopeRecord { actor_id = pope.id, name = pope.getName(), from = now });
        if (schism.line.Count > RivalLineMax) schism.line.RemoveRange(0, schism.line.Count - RivalLineMax);
    }

    private static void CloseAntipopeRecord(PapalSchismData schism, double now)
    {
        RivalPopeRecord last = schism.line?.LastOrDefault();
        if (last == null || last.to >= 0d) return;
        Actor former = World.world.units.get(last.actor_id);
        if (former?.data != null && !former.isRekt() && former.isAlive()) last.name = former.getName();
        last.to = now;
    }

    #endregion

    #region 年度结算

    // 每年(挂在 ReligionSchismSystem.YearlyPass，奉本教的帝国有没有都结)：补扶立旧档、对立教廷迁移、对立一线推举、服从、收场
    public static void SchismYearly(Religion religion)
    {
        try
        {
            if (religion?.data == null || religion.isRekt() || World.world == null) return;
            var data = religion.GetOrCreate();
            if (!data.Schism) return;
            PapalSchismData schism = ActiveSchism(religion);
            if (schism == null)
            {
                // 旧档：只有标记没有对立教宗——补扶立一位；扶立不成照旧按"教廷脱困、教宗正统回升"弥合；
                // 分出的教会(以前国教会圣地在帝都算教廷之囚，满 20 年也会挂上这个标记)直接去掉
                if (!ReligionSphere.IsNative(religion) || data.Caesaropapist) data.Schism = false;
                else if (!StartSchism(religion, SchismByLegacy)) LegacyHeal(religion);
                return;
            }
            double now = World.world.getCurWorldTime();
            if (schism.updated_at >= 0d && now >= schism.updated_at && Date.getYearsSince(schism.updated_at) < 1) return;
            schism.updated_at = now;
            Kingdom patron = KeepAntipopeSee(religion, schism);
            if (patron == null)
            {
                Finish(religion, schism, SchismEndExtinct, schism.see_city_name, ReligionChronicle.KindPapacy,
                    string.Format(LM.Get("antipope_extinct_log"), schism.see_city_name, religion.name, SchismAge(schism)));
                return;
            }
            if (Antipope(religion) == null && !ElectSuccessor(religion, schism, patron))
            {
                Finish(religion, schism, SchismEndExtinct, schism.see_city_name, ReligionChronicle.KindPapacy,
                    string.Format(LM.Get("antipope_extinct_log"), schism.see_city_name, religion.name, SchismAge(schism)));
                return;
            }
            UpdateObedience(religion, schism, initial: false);
            int years = SchismAge(schism);
            float share = AntipopeShare(religion);
            if (years >= MaxSchismYears)
            {
                if (share >= SeparateShare * 0.7f && Separate(religion, schism)) return;
                Council(religion, schism, SchismEndCouncil);
                return;
            }
            if ((years >= SeparateYears && share >= SeparateShare || years >= DominantYears && share >= DominantShare) &&
                UnityEngine.Random.value < SeparateChance && Separate(religion, schism)) return;
            bool recovered = Papacy(religion) != null && !IsCaptive(religion) &&
                             (!PopeLegitimacy.Applies(religion) ||
                              PopeLegitimacy.Get(religion, countSchism: false) >= PopeLegitimacy.HealedAt);
            if (years >= CouncilMinYears && recovered && share < 0.5f && UnityEngine.Random.value < CouncilChance)
            {
                Council(religion, schism, SchismEndCouncil);
                return;
            }
            if (years >= CollapseMinYears && share < CollapseShare && UnityEngine.Random.value < CollapseCouncilChance)
                Council(religion, schism, SchismEndCollapse);
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"[EmpireCraft][教廷] 大分裂结算失败: {exception.Message}");
        }
    }

    private static int SchismAge(PapalSchismData schism) =>
        schism.started_at >= 0d ? Date.getYearsSince(schism.started_at) : 0;

    // 旧档的大分裂标记：教廷脱困、教宗正统(不计对立教宗那一项)回到 HealedAt 以上才弥合(本功能之前的规则)
    private static void LegacyHeal(Religion religion)
    {
        var data = religion.GetOrCreate();
        if (IsCaptive(religion) ||
            PopeLegitimacy.Applies(religion) && PopeLegitimacy.Get(religion, countSchism: false) < PopeLegitimacy.HealedAt) return;
        data.Schism = false;
        string ended = string.Format(LM.Get("papal_schism_ended_log"), religion.name);
        TranslateHelper.LogEventMessage(ended, Papacy(religion));
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, ended);
    }

    // 对立教廷还在不在：所在城仍归扶立者(或对立一边的国)；失陷了迁到对立一边最强一国的都城，没有就返回 null(断绝)
    private static Kingdom KeepAntipopeSee(Religion religion, PapalSchismData schism)
    {
        Kingdom rome = Realm(Papacy(religion));
        City see = World.world.cities.get(schism.see_city_id);
        Kingdom holder = see?.data != null && !see.isRekt() && see.kingdom != null && !see.kingdom.wild ? Realm(see.kingdom) : null;
        if (SchismAlive(holder) && holder.religion == religion && holder != rome && religion.GetCity() != see &&
            (holder.id == schism.patron_id || schism.obedience.Contains(holder.id)))
        {
            schism.patron_id = holder.id;
            if (!schism.obedience.Contains(holder.id)) schism.obedience.Add(holder.id);
            return holder;
        }
        Kingdom next = schism.obedience.Select(id => World.world.kingdoms.get(id))
            .Where(kingdom => SchismAlive(kingdom) && Realm(kingdom) == kingdom && kingdom != rome && kingdom.religion == religion &&
                              kingdom.capital != null && !HoldsHolyCity(kingdom))
            .OrderByDescending(SchismCities).FirstOrDefault();
        if (next == null) return null;
        string from = schism.see_city_name;
        schism.see_city_id = next.capital.data.id;
        schism.see_city_name = next.capital.GetCityName();
        schism.patron_id = next.id;
        Actor pope = Antipope(religion);
        if (pope != null && pope.city != next.capital) pope.joinCity(next.capital);
        string text = string.Format(LM.Get("antipope_see_moved_log"), from, schism.see_city_name, next.GetKingdomName());
        TranslateHelper.LogEventMessage(text, next);
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
        return next;
    }

    // 对立教宗出缺：对立一边的枢机另推一位
    private static bool ElectSuccessor(Religion religion, PapalSchismData schism, Kingdom patron)
    {
        if (patron?.capital == null) return false;
        Actor pick = ElectAntipope(religion, schism, patron);
        if (pick == null) return false;
        double now = World.world.getCurWorldTime();
        SeatAntipope(religion, schism, pick, now);
        string text = string.Format(LM.Get("antipope_elected_log"), patron.GetKingdomName(), schism.see_city_name, pick.getName());
        TranslateHelper.LogEventMessage(text, patron);
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
        pick.RecordPersonalHistory(text);
        return true;
    }

    #endregion

    #region 服从

    private static void UpdateObedience(Religion religion, PapalSchismData schism, bool initial)
    {
        Kingdom rome = Realm(Papacy(religion));
        Kingdom patron = World.world.kingdoms.get(schism.patron_id);
        City holy = HolyCity(religion);
        City rivalSee = World.world.cities.get(schism.see_city_id);
        var before = new HashSet<long>(schism.obedience ?? new List<long>());
        var next = new List<long>();
        var romeMajors = new List<string>();
        var rivalMajors = new List<string>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!SchismAlive(kingdom) || Realm(kingdom) != kingdom || kingdom.religion != religion) continue;
            bool was = before.Contains(kingdom.id);
            bool rival;
            if (kingdom == patron) rival = true;
            else if (kingdom == rome) rival = false;
            else
            {
                float score = ObedienceScore(kingdom, religion, rome, patron, holy, rivalSee);
                rival = initial ? score > 0f : was ? score > -ObedienceSwitch : score > ObedienceSwitch;
            }
            if (rival) next.Add(kingdom.id);
            AnnulForeignBan(kingdom, rival);
            bool major = MajorRealm(kingdom);
            if (initial)
            {
                if (major) (rival ? rivalMajors : romeMajors).Add(kingdom.GetKingdomName());
            }
            else if (rival != was && major) ObedienceChanged(religion, schism, kingdom, rival);
        }
        schism.obedience = next;
        if (!initial) return;
        string summary = string.Format(LM.Get("antipope_obedience_summary"), Pope(religion)?.getName() ?? LM.Get("label_none"),
            romeMajors.Count == 0 ? LM.Get("label_none") : string.Join("、", romeMajors.Take(8)),
            Antipope(religion)?.getName() ?? LM.Get("label_none"),
            rivalMajors.Count == 0 ? LM.Get("label_none") : string.Join("、", rivalMajors.Take(8)));
        TranslateHelper.LogEventMessage(summary, patron);
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, summary);
    }

    private static float ObedienceScore(Kingdom kingdom, Religion religion, Kingdom rome, Kingdom patron, City holy, City rivalSee)
    {
        float score = 0f;
        if (rome != null && kingdom.isInWarWith(rome)) score += 2f;
        if (patron != null && kingdom.isInWarWith(patron)) score -= 2f;
        Empire empire = kingdom.GetEmpire();
        bool core = empire != null && empire.CoreKingdom == kingdom;
        PapalRelationState state = core ? State(empire) : null;
        if (core && IsExcommunicated(empire) && !state.banned_by_rival) score += 1.5f;
        if (SchismAllied(kingdom, patron)) score += 1.5f;
        if (SchismAllied(kingdom, rome)) score -= 1.5f;
        if (rome != null && patron != null)
            score += Mathf.Clamp((DiplomacyLedger.Opinion(kingdom, patron) - DiplomacyLedger.Opinion(kingdom, rome)) / 50f, -1.5f, 1.5f);
        if (kingdom.capital != null && holy != null && rivalSee?.data != null)
        {
            float toHoly = Vector2.Distance(kingdom.capital.city_center, holy.city_center);
            float toRival = Vector2.Distance(kingdom.capital.city_center, rivalSee.city_center);
            score += (toHoly - toRival) / Mathf.Max(1f, Mathf.Max(toHoly, toRival)) * 1.5f;
        }
        if (core && IsCrowned(empire) && !state.self_crowned && !state.crowned_by_rival) score -= 0.5f;
        Kingdom papacy = Papacy(religion);
        if (core && papacy != null && RealmNamingSystem.ControlsPapacy(empire, papacy)) score -= 2f;
        string culture = CultureService.GetRealmCulture(kingdom);
        if (patron != null && culture == CultureService.GetRealmCulture(patron)) score += 0.5f;
        if (rome != null && culture == CultureService.GetRealmCulture(rome)) score -= 0.5f;
        score += (kingdom.id * 7919L % 100L) / 100f * 0.6f - 0.3f;
        return score;
    }

    // 换了服从：另一位教宗下的绝罚作废
    private static void AnnulForeignBan(Kingdom kingdom, bool rival)
    {
        Empire empire = kingdom.GetEmpire();
        if (empire == null || empire.CoreKingdom != kingdom || !IsExcommunicated(empire)) return;
        PapalRelationState state = State(empire);
        if (state.banned_by_rival == rival) return;
        Lift(empire, state, LM.Get("papal_ban_obedience_lifted_history"));
        state.banned_by_rival = false;
    }

    private static void ObedienceChanged(Religion religion, PapalSchismData schism, Kingdom kingdom, bool rival)
    {
        Actor pope = rival ? Antipope(religion) : Pope(religion);
        string place = rival ? schism.see_city_name : HolyCity(religion)?.GetCityName() ?? "";
        string text = string.Format(LM.Get("antipope_obedience_log"), kingdom.GetKingdomName(), pope?.getName() ?? LM.Get("label_none"),
            place);
        TranslateHelper.LogEventMessage(text, kingdom);
        ReligionChronicle.Record(religion, ReligionChronicle.KindPapacy, text);
        RecordRealm(kingdom, text, kingdom.king);
    }

    #endregion

    #region 收场

    // 公会议：在最强一国(扶立者、教廷所在之外)的都城召开，对立教宗退位，复归一统
    private static void Council(Religion religion, PapalSchismData schism, int outcome)
    {
        Kingdom rome = Realm(Papacy(religion));
        Kingdom host = World.world.kingdoms.list
            .Where(kingdom => SchismAlive(kingdom) && Realm(kingdom) == kingdom && kingdom.religion == religion && kingdom.capital != null &&
                              kingdom.id != schism.patron_id && kingdom != rome)
            .OrderByDescending(SchismCities).FirstOrDefault() ?? rome;
        string venue = host?.capital?.GetCityName() ?? HolyCity(religion)?.GetCityName() ?? "";
        Actor antipope = Antipope(religion);
        string name = antipope?.getName() ?? schism.line?.LastOrDefault()?.name ?? LM.Get("label_none");
        string text = string.Format(LM.Get(outcome == SchismEndCollapse ? "antipope_collapse_log" : "antipope_council_log"), venue, name,
            religion.name, SchismAge(schism));
        AddAuthority(religion, 5f);
        antipope?.RecordPersonalHistory(text);
        Finish(religion, schism, outcome, venue, ReligionChronicle.KindPapacy, text);
    }

    // 分立：对立教廷自成一教(记母宗教，圣地在对立教廷，由扶立者兼管)，对立一边各国连城改奉
    private static bool Separate(Religion religion, PapalSchismData schism)
    {
        Kingdom patron = World.world.kingdoms.get(schism.patron_id);
        City see = World.world.cities.get(schism.see_city_id);
        if (!SchismAlive(patron) || see?.data == null || see.isRekt() || see.kingdom == null || Realm(see.kingdom) != patron) return false;
        Actor founder = Antipope(religion) ?? patron.king;
        if (founder?.data == null || !founder.isAlive()) return false;
        Religion church = World.world.religions.newReligion(founder, false);
        if (church?.data == null) return false;
        church.data.name = string.Format(LM.Get("antipope_church_name"), see.GetCityName());
        church.SetCity(see);
        double now = World.world.getCurWorldTime();
        var churchData = church.GetOrCreate();
        churchData.ParentReligionId = religion.data.id;
        churchData.SplitAt = now;
        churchData.Faith = FaithType.Church.ToString();
        churchData.Sphere = ReligionSphere.SphereOf(religion);
        churchData.Caesaropapist = true;
        churchData.SplitOrigin = new SplitOriginData
        {
            kind = OriginRivalLine, founder_id = founder.id, founder_name = founder.getName(), city_id = see.data.id,
            city_name = see.GetCityName(), at = now
        };
        var data = religion.GetOrCreate();
        data.ReligiousPeaceUntil = Math.Max(data.ReligiousPeaceUntil, now + ReligiousPeaceYears * SecondsPerYear);
        var converted = new List<Kingdom>();
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!SchismAlive(kingdom) || kingdom.religion != religion || !schism.obedience.Contains(Realm(kingdom).id)) continue;
            ConvertWhole(kingdom, religion, church);
            Empire empire = kingdom.GetEmpire();
            if (empire != null && empire.CoreKingdom == kingdom) empire.Religion = church;
            if (Realm(kingdom) == kingdom) converted.Add(kingdom);
        }
        if (founder.religion != church) founder.setReligion(church);
        AddAuthority(religion, -10f);
        string names = converted.Count == 0 ? patron.GetKingdomName()
            : string.Join("、", converted.OrderByDescending(SchismCities).Take(4).Select(kingdom => kingdom.GetKingdomName()));
        string text = string.Format(LM.Get("antipope_separated_log"), SchismAge(schism), patron.GetKingdomName(), church.name,
            see.GetCityName(), names, Math.Max(1, converted.Count));
        ReligionChronicle.Clear(church);
        ReligionChronicle.Record(church, ReligionChronicle.KindSchism, text);
        Finish(religion, schism, SchismEndSeparated, see.GetCityName(), ReligionChronicle.KindSchism, text);
        return true;
    }

    private static void Finish(Religion religion, PapalSchismData schism, int outcome, string place, int kind, string text)
    {
        double now = World.world.getCurWorldTime();
        CloseAntipopeRecord(schism, now);
        schism.ended_at = now;
        schism.outcome = outcome;
        schism.outcome_place = place ?? "";
        schism.pope_id = -1L;
        schism.obedience?.Clear();
        religion.GetOrCreate().Schism = false;
        // 对立教宗下的绝罚一并作废
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (empire?.data == null || empire.isRekt() || empire.IsArchived() || empire.CoreKingdom == null) continue;
            PapalRelationState state = State(empire);
            if (!state.banned_by_rival) continue;
            state.banned_by_rival = false;
            if (IsExcommunicated(empire)) Lift(empire, state, LM.Get("papal_ban_council_lifted_history"));
        }
        TranslateHelper.LogEventMessage(text, Papacy(religion));
        ReligionChronicle.Record(religion, kind, text);
        LogService.LogInfo($"[EmpireCraft][教廷] {religion.name} 大分裂收场(结局 {outcome})：{text}");
    }

    #endregion

    #region 对立教宗加冕

    // 对立一边的皇帝在对立教廷受冕：扶立者自家或在本帝国的直接加冕，否则每年 CoronationChance 遣使(献礼同原教宗)，不必远征
    private static void AntipopeCoronation(Empire empire, PapalRelationState state, Religion religion, Actor emperor)
    {
        Kingdom see = AntipopeSee(religion), core = empire.CoreKingdom;
        if (see == null || Antipope(religion) == null) return;
        if (see != core && see.GetEmpire() != empire)
        {
            if (UnityEngine.Random.value >= CoronationChance || see.isInWarWith(core)) return;
            int gift = (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * CoronationGift));
            if (gift > 0)
            {
                core.SubMoney(gift, TreasuryCategory.Tithe);
                see.AddMoney(gift, TreasuryCategory.Tithe);
            }
        }
        Crown(empire, state, emperor, self: false, see, antipope: true);
    }

    #endregion

    // 帝国史书记一笔(不在帝国里的国只记世界日志)
    private static void RecordRealm(Kingdom kingdom, string text, Actor actor)
    {
        Empire empire = kingdom?.GetEmpire();
        if (empire?.data == null || empire.isRekt()) return;
        empire.RecordHistory(directContent: text, actorId: actor?.id ?? -1L, kingdomId: kingdom.id);
    }
}
