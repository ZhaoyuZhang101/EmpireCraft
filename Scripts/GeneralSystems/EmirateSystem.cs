using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 埃米尔坐大(用户 2026-10-17 转群友"游牧太强了几乎不会分封""还有阿拉伯"，选"埃米尔行省世袭坐大")：
//   · 阿拉伯政体的帝国打下的法理设为行省，由朝廷派的埃米尔治理(考选、不世袭、不通外交，见 Empire.update 的领土分封)；
//   · 哈里发正统不到 50 时，每个行省每年有 (50 − 正统)% 的机会埃米尔世袭其位：法理归他所有，改为世袭、通外交、
//     不再支援中央军——正统再掉到 40 以下就能拥兵自立为苏丹国(见 kingdom_allow_independent)；
//   · 正统 60 以上才能削藩把世袭的埃米尔收回(见 TempFac_削藩)。
// 苏丹架空哈里发(用户 2026-10-19 选"补……苏丹架空哈里发")：本文化施行"苏丹官僚"后，帝国里不属哈里发一系、领地最大的
// 成员国君主(苏丹国优先)，没有就取朝中最高的武官，入朝为苏丹掌实权；哈里发一家退为象征(同幕府的天皇：帝国窗口显示，
// 见 BakufuSystem.Emperor)，执政宗族改为苏丹一家，核心君主改称苏丹。离开阿拉伯政体时不再有苏丹、哈里发之分。
public static class EmirateSystem
{
    public const int WeakLegitimacy = 50, StrongLegitimacy = 60;
    // 各帝国上次结算的时间(不存档：读档、换世界时清掉，重新计满一年)
    private static readonly Dictionary<long, double> LastYear = new();

    public static void ResetWorldState() => LastYear.Clear();

    public static bool Applies(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Arabic;

    public static bool IsSultanate(Empire empire) => empire?.data != null && empire.data.court_clan_id >= 0 && Applies(empire);

    private static void TryEstablishSultanate(Empire empire)
    {
        if (empire.data.court_clan_id >= 0 || !InstitutionSystem.HasFeature(empire, InstitutionFeatures.Sultanate)) return;
        Kingdom core = empire.CoreKingdom;
        Actor caliph = core.king;
        SpecificClan court = empire.EmpireSpecificClan;
        if (caliph?.data == null || court == null) return;
        Actor sultan = PickSultan(empire, court);
        if (sultan == null) return;
        core.GetOffice().meta_object = core;
        core.GetOffice().SetActor(sultan);
        if (core.king != sultan) return;
        empire.data.court_clan_id = court.id;
        empire.data.court_emperor_id = caliph.getID();
        if (sultan.GetSpecificClan() is SpecificClan sultanClan) empire.data.empire_specific_clan = sultanClan.id;
        string culture = CultureService.GetRealmCulture(core);
        string content = string.Format(LM.Get(DynastyNaming.CultureVariant("sultanate_established_history", culture) ??
                                              "sultanate_established_history"),
            empire.GetEmpireName(), sultan.getName(), caliph.getName());
        empire.RecordHistory(directContent: content, kingdomId: core.id);
        TranslateHelper.LogEventMessage(content, core);
        LogService.LogInfo("[EmpireCraft][苏丹] " + content);
    }

    // 离开阿拉伯政体：不再有苏丹、哈里发之分
    public static void OnRegimeChanged(Empire empire, RegimeType? previous, RegimeType current)
    {
        if (empire?.data == null || previous != RegimeType.Arabic || current == RegimeType.Arabic) return;
        empire.data.court_clan_id = -1L;
        empire.data.court_emperor_id = -1L;
    }

    // 苏丹人选：不属哈里发一系、领地最大的成员国君主(苏丹国优先)；没有就取朝中最高的武官
    private static Actor PickSultan(Empire empire, SpecificClan court)
    {
        bool Eligible(Actor actor) => actor?.data != null && !actor.isRekt() && actor.isAlive() && actor.isAdult() &&
                                      !SpecificClanManager.SameLineage(actor.GetSpecificClan(), court) &&
                                      actor.CanServeOffice(empire.CoreKingdom);
        Actor lord = empire.kingdoms_list?
            .Where(kingdom => kingdom != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom && Eligible(kingdom.king))
            .OrderByDescending(kingdom => kingdom.GetKingdomType() == KingdomType.Arabic_sultanate)
            .ThenByDescending(kingdom => kingdom.cities?.Count ?? 0)
            .Select(kingdom => kingdom.king).FirstOrDefault();
        if (lord != null) return lord;
        return empire.CoreKingdom.units?
            .Where(actor => Eligible(actor) && actor.HasOfficeIdentity() && actor.GetIdentity()?.officialLevel is >= 9 and <= 12)
            .OrderBy(actor => actor.GetIdentity().officialLevel).ThenByDescending(actor => actor.data?.renown ?? 0)
            .FirstOrDefault();
    }

    // 每年一次(由 Empire.update 调用)
    public static void Tick(Empire empire)
    {
        if (empire?.data == null || World.world == null || !Applies(empire)) return;
        double now = World.world.getCurWorldTime();
        if (LastYear.TryGetValue(empire.id, out double last) && now >= last && Date.getYearsSince(last) < 1) return;
        LastYear[empire.id] = now;
        TryEstablishSultanate(empire);
        int legitimacy = empire.Legitimacy;
        if (legitimacy >= WeakLegitimacy) return;
        foreach (Kingdom kingdom in empire.kingdoms_list.ToList())
        {
            if (kingdom?.data == null || kingdom.isRekt() || kingdom == empire.CoreKingdom || kingdom.king == null) continue;
            Regime regime = kingdom.GetRegime();
            KingdomTitle title = kingdom.GetAdministrativeTitle();
            if (regime?.type != RegimeType.Arabic || title == null || kingdom.GetMainTitle() != null) continue;
            if (UnityEngine.Random.value * 100f >= WeakLegitimacy - legitimacy) continue;
            try
            {
                kingdom.king.AddOwnedTitle(title);
                kingdom.SetMainTitle(title);
                regime.SetLeaderSelectMethod(LeaderSelectMethod.Succession);
                regime.SetAllowDiplomacy(true);
                regime.SetAllowSupportCenterArmy(false);
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
                string content = string.Format(LM.Get("emirate_hereditary"), kingdom.GetKingdomName(), kingdom.king.getName(),
                    empire.GetEmpireName());
                empire.RecordHistory(directContent: content, kingdomId: kingdom.id);
                LogService.LogInfo("[EmpireCraft][埃米尔] " + content);
            }
            catch (global::System.Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][埃米尔] {kingdom.GetKingdomName()} 世袭失败: {exception.Message}");
            }
        }
    }
}
