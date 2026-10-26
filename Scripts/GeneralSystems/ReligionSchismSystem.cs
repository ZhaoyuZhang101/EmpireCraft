using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 宗教分裂(用户 2026-10-26 选"东西教会大分裂""逊尼什叶分裂""宗教改革也看教权与圣战失利")。分出来的教会都记母宗教
// (ParentReligionId)，不算地方教会，不会被并入正教(见 ReligionSphere)：
//   · 东西教会大分裂(每个普世教会一次)：立教满 SchismMinAge 年，奉它的帝国(SchismMinCities 城以上、教廷不在本帝国、
//     帝都离圣地超过地图宽度的 FarShare)与教廷交战、遭绝罚、教廷正闹对立教宗或教权低于 40 时，每年 SchismChance 机会另立正教会：
//     圣地在帝都，皇帝兼管教会(Caesaropapist：不算教廷之囚，加冕在自家)，帝国各国各城改宗；就近(离新圣地比离旧圣地近)、
//     不与它交战的同教独立国各 FollowChance 随之；旧教教权 −10，此后 PeaceYears 年不讨伐这支"异端"；
//   · 逊尼什叶分裂(每个伊斯兰教一次)：哈里发、苏丹更替之乱破局开争位之战时(SchismSystem.Proclaim)，ShiaChance 机会
//     争位者另奉什叶派(圣地在其都城，自有伊玛目——哈里发座)，随攻方的割据成员同改；
//   · 宗教改革提早：普世教会教权低于 EarlyReformAuthority、圣战失利满 EarlyReformLosses 次，仍交什一税的西方帝国
//     每年 EarlyReformChance 机会提前脱离罗马(PapacySystem.BreakWithRome，不必等本文化施行宗教改革)。
public static class ReligionSchismSystem
{
    private const int SchismMinAge = 100;
    private const int SchismMinCities = 8;
    private const float FarShare = 0.3f;
    private const float SchismChance = 0.03f;
    private const float FollowChance = 0.6f;
    private const int PeaceYears = 50;
    private const double SecondsPerYear = 60d;
    private const float ShiaChance = 0.5f;
    private const float EarlyReformAuthority = 35f;
    private const int EarlyReformLosses = 2;
    private const float EarlyReformChance = 0.03f;

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt() && !kingdom.wild;

    private static Kingdom Realm(Kingdom kingdom) => kingdom?.GetEmpire()?.CoreKingdom ?? kingdom;

    public static void YearlyPass()
    {
        if (World.world?.religions == null || ModClass.IS_CLEAR) return;
        foreach (Religion religion in World.world.religions.ToList())
        {
            if (religion?.data == null || religion.isRekt() || !ReligionFaith.IsPapal(religion)) continue;
            TryGreatSchism(religion);
            TryEarlyReformation(religion);
        }
    }

    #region 东西教会大分裂

    private static void TryGreatSchism(Religion religion)
    {
        var data = religion.GetOrCreate();
        if (data.EasternSplit || !ReligionSphere.IsNative(religion) || religion.getAge() < SchismMinAge) return;
        Kingdom papacy = PapacySystem.Papacy(religion);
        City holy = PapacySystem.HolyCity(religion);
        if (papacy == null || holy == null) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (!PapacySystem.Applies(empire) || PapacySystem.ReligionOf(empire) != religion) continue;
            Kingdom core = empire.CoreKingdom;
            if (papacy == core || papacy.GetEmpire() == empire || core.capital == null ||
                empire.countCities() < SchismMinCities ||
                Vector2.Distance(core.capital.city_center, holy.city_center) < Mathf.Max(MapBox.width, MapBox.height) * FarShare) continue;
            bool estranged = core.isInWarWith(Realm(papacy)) || PapacySystem.IsExcommunicated(empire) || data.Schism ||
                             PapacySystem.Authority(religion) < 40f;
            if (!estranged || UnityEngine.Random.value >= SchismChance) continue;
            if (EasternSplit(empire, religion, holy)) return;
        }
    }

    private static bool EasternSplit(Empire empire, Religion old, City oldHoly)
    {
        Kingdom core = empire.CoreKingdom;
        Actor head = empire.Emperor;
        City capital = core.capital;
        if (head == null || capital == null) return false;
        head.joinCity(capital);
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
        // 就近的同教独立国随之
        int followers = 0;
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!Alive(kingdom) || Realm(kingdom) != kingdom || kingdom.religion != old || kingdom.capital == null ||
                kingdom.isInWarWith(core) || PapacySystem.HoldsHolyCity(kingdom)) continue;
            float near = Vector2.Distance(kingdom.capital.city_center, capital.city_center);
            float far = Vector2.Distance(kingdom.capital.city_center, oldHoly.city_center);
            if (near >= far || UnityEngine.Random.value >= FollowChance) continue;
            ConvertKingdom(kingdom, old, church);
            followers++;
        }
        PapacySystem.AddAuthority(old, -10f);
        string text = string.Format(LM.Get("great_schism_log"), empire.GetEmpireFullName(), oldHoly.GetCityName(), church.name);
        TranslateHelper.LogEventMessage(text, core);
        ReligionChronicle.Clear(church);
        ReligionChronicle.Record(old, church, ReligionChronicle.KindSchism, text);
        LogService.LogInfo($"[EmpireCraft][宗教] 东西教会大分裂：{empire.GetEmpireName()} 另立{church.name}，随之 {followers} 国");
        return true;
    }

    #endregion

    #region 逊尼什叶分裂

    // 哈里发、苏丹更替之乱开争位之战时(见 SchismSystem.Proclaim)
    public static void OnSuccessionContest(Empire empire, int kind, Kingdom claimant, List<Kingdom> followers)
    {
        if (kind != SchismSystem.Caliph && kind != SchismSystem.Sultan || !Alive(claimant)) return;
        Religion old = empire?.CoreKingdom?.religion;
        if (old?.data == null || !ReligionFaith.IsCaliphal(old) || !ReligionSphere.IsNative(old)) return;
        var oldData = old.GetOrCreate();
        if (oldData.ShiaSplit || claimant.religion != old || UnityEngine.Random.value >= ShiaChance) return;
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

    private static void TryEarlyReformation(Religion religion)
    {
        var data = religion.GetOrCreate();
        if (PapacySystem.Papacy(religion) == null || PapacySystem.Authority(religion) >= EarlyReformAuthority ||
            data.CrusadeLosses < EarlyReformLosses) return;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER?.ToList() ?? new List<Empire>())
        {
            if (!PapacySystem.Applies(empire) || PapacySystem.ReligionOf(empire) != religion ||
                PapacySystem.Investiture(empire) == PapacySystem.InvestitureCrown || UnityEngine.Random.value >= EarlyReformChance) continue;
            if (PapacySystem.BreakWithRome(empire))
                LogService.LogInfo($"[EmpireCraft][宗教] {empire.GetEmpireName()} 因教权低落({PapacySystem.Authority(religion):0})、" +
                                   $"圣战失利 {data.CrusadeLosses} 次提早脱离{religion.name}");
        }
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
