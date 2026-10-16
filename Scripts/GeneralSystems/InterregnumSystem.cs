using System;
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

// 绝嗣裂土(用户 2026-10-16："皇帝绝嗣直接裂开算了，然后可能会有人自称是后代然后多方称帝，都说自己是正统。
// 或者说自己有传国玉玺……然后再次统一这样")：皇帝崩、皇族已无可立之人(议立开列不出宗室，见 SuccessionCouncilSystem.Open)，
// 疆域够大(至少十城)、有强藩(至少三城、有成年君主的成员国)的帝国不再劝进外姓，而是天下无主、群雄并起——
//   · 京城：群臣推权臣、重臣即皇帝位(直接即位，朝廷易代)；
//   · 强藩：最强的至多三家各据其地称帝，离开帝国自立为帝国(称帝不成的就地自立)：
//       一家称得传国玉玺(玉玺一半留在京城)，法统 +15；一家自称先帝之后，法统 +10；其余拥兵称帝；
//   · 其余成员国各归都城离得最近的一方(京城或称帝的强藩)；
//   · 此后各方互争正统，天下大势按乱世→统一战争走(见 UnificationSystem)，同文化帝国上限不拦这一回。
// 传国玉玺(记在文化上，见 CultureInstitutionState)：持有者每年法统 +2；持有的帝国亡了，玉玺归占了它最后都城的那一家。
// 两朝分治、可以迎立另一朝兼承大统的不裂土；周制天下、原始部落联盟也不裂土，照旧议立。
public static class InterregnumSystem
{
    private const int MinCities = 10, MinClaimantCities = 3, MaxClaimants = 3, SealMandate = 15, DescentMandate = 10,
        SealYearlyMandate = 2;

    private static bool Applies(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt() || RepublicSystem.IsRepublic(empire) || ShanrangSystem.Elective(empire)) return false;
        RegimeType? type = core.GetRegime()?.type;
        if (type == null || type == RegimeType.ZhouFeudalism || RegimeManager.IsPrimitive(type) || !RegimeManager.IsMonarchy(type))
            return false;
        return !EmpirePartitionSystem.CanUnite(empire, out _) &&
               EmpireMembershipService.EnumerateCities(empire).Count() >= MinCities;
    }

    private static double Might(Kingdom kingdom)
    {
        try
        {
            return kingdom.GetNationalPower();
        }
        catch
        {
            return kingdom.cities?.Count ?? 0;
        }
    }

    // 议立开列不出宗室时调用(SuccessionCouncilSystem.Open)：裂土成了返回真(京城已立新君，议立作罢)
    public static bool TryFracture(Empire empire, PersonalClanIdentity predecessor, Actor court)
    {
        if (!Applies(empire) || court?.data == null || court.isRekt() || !court.isAlive()) return false;
        Kingdom core = empire.CoreKingdom;
        List<Kingdom> regions = empire.kingdoms_list
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom != core && kingdom.capital != null &&
                              (kingdom.cities?.Count ?? 0) >= MinClaimantCities && kingdom.king?.data != null &&
                              kingdom.king.isAlive() && kingdom.king.isAdult() && kingdom.king != court)
            .OrderByDescending(Might).Take(MaxClaimants).ToList();
        if (regions.Count == 0) return false;
        string fallen = predecessor?.name ?? LM.Get("label_none");
        City capital = core.capital;
        if (!empire.InstallHeadOfState(court))
        {
            LogService.LogWarning($"[EmpireCraft][绝嗣裂土] {empire.GetEmpireFullName()} 京城立 {court.getName()} 不成，照旧议立");
            return false;
        }
        string culture = CultureService.GetEmpireDefaultCulture(empire);
        // 玉玺一半留在京城，一半流落到某家强藩手里
        int sealAt = UnityEngine.Random.value < 0.5f ? -1 : UnityEngine.Random.Range(0, regions.Count);
        int descentAt = regions.Count > 1 || sealAt < 0 ? Enumerable.Range(0, regions.Count).FirstOrDefault(i => i != sealAt) : -1;
        var texts = new List<string> { string.Format(LM.Get("interregnum_fracture_history"), fallen) };
        texts.Add(string.Format(LM.Get(sealAt < 0 ? "interregnum_court_seal_history" : "interregnum_court_history"),
            court.getName(), capital?.GetCityName() ?? ""));
        if (sealAt < 0) Seal(culture, empire, announce: false);
        var claimants = new List<(Kingdom seat, Empire realm)> { (core, empire) };
        for (int i = 0; i < regions.Count; i++)
        {
            Kingdom region = regions[i];
            string ruler = region.king.getName(), seatName = region.capital.GetCityName();
            empire.leave(region, isLeave: false);
            Empire rival = null;
            try
            {
                rival = ModClass.EMPIRE_MANAGER.NewEmpire(region, allowCultureRival: true, forceNewCore: true);
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][绝嗣裂土] {region.name} 称帝失败: {exception.Message}");
            }
            if (rival?.data == null)
            {
                texts.Add(string.Format(LM.Get("interregnum_claim_independent_history"), ruler, seatName));
                claimants.Add((region, null));
                continue;
            }
            string key = i == sealAt ? "interregnum_claim_seal_history"
                : i == descentAt ? "interregnum_claim_descent_history" : "interregnum_claim_army_history";
            if (i == sealAt)
            {
                rival.AddMandate(SealMandate);
                Seal(culture, rival, announce: false);
            }
            else if (i == descentAt) rival.AddMandate(DescentMandate);
            texts.Add(string.Format(LM.Get(key), ruler, seatName, rival.GetEmpireFullName()));
            claimants.Add((region, rival));
        }
        // 其余成员国各归都城离得最近的一方
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == core || kingdom.capital == null) continue;
            (Kingdom seat, Empire realm) nearest = claimants
                .Where(claimant => claimant.seat?.capital != null && !claimant.seat.isRekt())
                .OrderBy(claimant => (claimant.seat.capital.city_center - kingdom.capital.city_center).sqrMagnitude)
                .FirstOrDefault();
            if (nearest.seat == null || nearest.realm == empire || nearest.realm == null) continue;
            empire.leave(kingdom, isLeave: false);
            nearest.realm.join(kingdom, pRecalc: false, pForce: true, pLegitimacyTransfer: true);
        }
        foreach ((Kingdom _, Empire realm) in claimants)
            realm?.recalculate();
        World.world.zone_calculator?.dirtyAndClear();
        string content = string.Join("", texts);
        LogService.LogInfo("[EmpireCraft][绝嗣裂土] " + content);
        foreach ((Kingdom seat, Empire realm) in claimants)
            realm?.RecordHistory(directContent: content, kingdomId: seat?.id ?? -1L);
        TranslateHelper.LogEventMessage(content, core);
        return true;
    }

    #region 传国玉玺

    private static void Seal(string culture, Empire holder, bool announce)
    {
        CultureInstitutionState state = CultureService.IsValidCulture(culture) ? InstitutionSystem.GetOrCreateCultureState(culture) : null;
        if (state == null || holder?.data == null) return;
        state.imperial_seal_empire_id = holder.data.id;
        state.imperial_seal_city_id = holder.CoreKingdom?.capital?.data?.id ?? -1L;
        if (!announce) return;
        string content = string.Format(LM.Get("imperial_seal_taken_history"), holder.GetEmpireFullName());
        LogService.LogInfo("[EmpireCraft][传国玉玺] " + content);
        holder.RecordHistory(directContent: content);
        TranslateHelper.LogEventMessage(content, holder.CoreKingdom);
    }

    public static bool HoldsSeal(Empire empire)
    {
        string culture = CultureService.GetEmpireDefaultCulture(empire);
        CultureInstitutionState state = CultureService.IsValidCulture(culture) ? InstitutionSystem.GetOrCreateCultureState(culture) : null;
        return state != null && empire?.data != null && state.imperial_seal_empire_id == empire.data.id;
    }

    // 每年(帝国例行检查，见 EmpireCraftEmpireBehCheckMandate)：持有者得法统、玉玺随都城；持有的帝国亡了，玉玺易手
    public static void Yearly(Empire empire)
    {
        string culture = CultureService.GetEmpireDefaultCulture(empire);
        if (!CultureService.IsValidCulture(culture)) return;
        CultureInstitutionState state = InstitutionSystem.GetOrCreateCultureState(culture);
        if (state == null || state.imperial_seal_empire_id <= 0) return;
        if (state.imperial_seal_empire_id == empire.data.id)
        {
            empire.AddMandate(SealYearlyMandate);
            state.imperial_seal_city_id = empire.CoreKingdom?.capital?.data?.id ?? state.imperial_seal_city_id;
            return;
        }
        Empire holder = ModClass.EMPIRE_MANAGER?.get(state.imperial_seal_empire_id);
        if (holder?.data != null && !holder.isRekt() && !holder.IsArchived()) return;
        // 持有的帝国亡了：玉玺归占了它最后都城的那一家
        City city = World.world.cities.get(state.imperial_seal_city_id);
        Kingdom owner = city?.kingdom;
        Empire next = owner == null ? null : owner.IsEmpire() || owner.IsInEmpire() ? owner.GetEmpire() : null;
        if (next == empire) Seal(culture, empire, announce: true);
    }

    #endregion
}
