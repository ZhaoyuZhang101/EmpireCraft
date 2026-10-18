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
// 苏丹架空哈里发(用户 2026-10-19 选"补……苏丹架空哈里发""改用权臣机制")：本文化施行"苏丹官僚"后，哈里发照旧在位、
// 成为傀儡，苏丹以权臣掌实权、在本家承袭(同幕府的征夷大将军，见 MilitaryRegency)；离开阿拉伯政体时不再有苏丹。
public static class EmirateSystem
{
    public const int WeakLegitimacy = 50, StrongLegitimacy = 60;
    // 各帝国上次结算的时间(不存档：读档、换世界时清掉，重新计满一年)
    private static readonly Dictionary<long, double> LastYear = new();

    public static void ResetWorldState() => LastYear.Clear();

    public static bool Applies(Empire empire) => empire?.CoreKingdom?.GetRegime()?.type == RegimeType.Arabic;

    public static bool IsSultanate(Empire empire) =>
        Applies(empire) && MilitaryRegency.KindOf(empire) == MilitaryRegency.Sultan;

    // 离开阿拉伯政体：不再有苏丹、哈里发之分
    public static void OnRegimeChanged(Empire empire, RegimeType? previous, RegimeType current)
    {
        if (empire?.data == null || previous != RegimeType.Arabic || current == RegimeType.Arabic) return;
        if (MilitaryRegency.KindOf(empire) == MilitaryRegency.Sultan) MilitaryRegency.Dissolve(empire, null);
    }

    // 每年一次(由 Empire.update 调用)
    public static void Tick(Empire empire)
    {
        if (empire?.data == null || World.world == null || !Applies(empire)) return;
        double now = World.world.getCurWorldTime();
        if (LastYear.TryGetValue(empire.id, out double last) && now >= last && Date.getYearsSince(last) < 1) return;
        LastYear[empire.id] = now;
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
