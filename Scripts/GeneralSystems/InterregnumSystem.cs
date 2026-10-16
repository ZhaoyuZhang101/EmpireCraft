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
// 不再劝进外姓，天下无主、各地散伙(用户："皇族绝嗣也弄一下，别搞外姓了，直接各地散伙得了")——
//   · 京城：权臣、重臣(没有就京城官员)据京城即皇帝位(直接即位，朝廷易代)；
//   · 强藩：成员国全部离开帝国；最强的至多三家(至少三城、有成年君主)各据其地称帝(称帝不成的就地自立)，其余各自自立：
//       华夏帝国原来佩着传国玉玺的(见 ImperialSealSystem)，玉玺一半留在京城、一半流落到一家强藩手里，那一家法统 +15；
//       一家自称先帝之后，法统 +10；其余拥兵称帝；
//   · 此后各方互争正统，天下大势按乱世→统一战争走(见 UnificationSystem)，同文化帝国上限不拦这一回。
// 两朝分治、可以迎立另一朝兼承大统的不裂土；周制天下、原始部落联盟也不裂土，照旧议立。
public static class InterregnumSystem
{
    private const int MinClaimantCities = 3, MaxClaimants = 3, SealMandate = 15, DescentMandate = 10;

    private static bool Applies(Empire empire)
    {
        Kingdom core = empire?.CoreKingdom;
        if (core == null || core.isRekt() || RepublicSystem.IsRepublic(empire) || ShanrangSystem.Elective(empire)) return false;
        RegimeType? type = core.GetRegime()?.type;
        if (type == null || type == RegimeType.ZhouFeudalism || RegimeManager.IsPrimitive(type) || !RegimeManager.IsMonarchy(type))
            return false;
        return !EmpirePartitionSystem.CanUnite(empire, out _);
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

    // 议立开列不出宗室时调用(SuccessionCouncilSystem.Open)，或权臣废帝而宗室无人可立(EmpirePartitionSystem)：
    // 裂土成了返回真(京城已立新君，议立作罢)；opening 为空时史册开头写"某崩，皇族绝嗣"
    public static bool TryFracture(Empire empire, PersonalClanIdentity predecessor, Actor court, string opening = null)
    {
        if (!Applies(empire) || court?.data == null || court.isRekt() || !court.isAlive()) return false;
        Kingdom core = empire.CoreKingdom;
        List<Kingdom> regions = empire.kingdoms_list
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom != core && kingdom.capital != null &&
                              (kingdom.cities?.Count ?? 0) >= MinClaimantCities && kingdom.king?.data != null &&
                              kingdom.king.isAlive() && kingdom.king.isAdult() && kingdom.king != court)
            .OrderByDescending(Might).Take(MaxClaimants).ToList();
        string fallen = predecessor?.name ?? LM.Get("label_none");
        City capital = core.capital;
        bool hasSeal = ImperialSealSystem.HeldBy(empire);
        if (!empire.InstallHeadOfState(court))
        {
            LogService.LogWarning($"[EmpireCraft][绝嗣裂土] {empire.GetEmpireFullName()} 京城立 {court.getName()} 不成，照旧议立");
            return false;
        }
        // 玉玺(只有华夏有)一半留在京城，一半流落到某家强藩手里；没有玉玺的不分
        int sealAt = !hasSeal ? -2 : regions.Count == 0 || UnityEngine.Random.value < 0.5f ? -1 : UnityEngine.Random.Range(0, regions.Count);
        int descentAt = regions.Count > 1 || sealAt < 0 ? Enumerable.Range(0, regions.Count).FirstOrDefault(i => i != sealAt) : -1;
        var texts = new List<string> { opening ?? string.Format(LM.Get("interregnum_fracture_history"), fallen) };
        texts.Add(string.Format(LM.Get(sealAt == -1 ? "interregnum_court_seal_history" : "interregnum_court_history"),
            court.getName(), capital?.GetCityName() ?? ""));
        if (sealAt == -1) ImperialSealSystem.Give(empire, announce: false);
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
                ImperialSealSystem.Give(rival, announce: false);
            }
            else if (i == descentAt) rival.AddMandate(DescentMandate);
            texts.Add(string.Format(LM.Get(key), ruler, seatName, rival.GetEmpireFullName()));
            claimants.Add((region, rival));
        }
        // 其余成员国各自散伙自立
        int scattered = 0;
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == core) continue;
            empire.leave(kingdom, isLeave: false);
            scattered++;
        }
        if (scattered > 0) texts.Add(string.Format(LM.Get("interregnum_scatter_history"), scattered));
        // 京畿的甲等世家据城自守，其余世家各附郡望所在的一方(见 GentrySystem)
        GentrySystem.OnFracture(empire);
        GentrySystem.ChooseSides(claimants.Select(claimant => claimant.realm));
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
}
