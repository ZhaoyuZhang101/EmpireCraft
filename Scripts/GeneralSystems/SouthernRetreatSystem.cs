using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems.WorldMap;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 南明结成(用户 2026-11-08 "一旦满清占下北京或者三分之一的天下南边都自动组成南明""而不是不起民变"
// "要达到那种外族入侵然后原来的农民起义什么的都自主加入同一个旗帜下抗清的那种符合历史的感觉")：
//   华夏王朝的京师(帝国法理首都)落到异族手里，或异族占了本天下三分之一的城：朝廷还有人承统、还有 MinCities 城的，结成一次
//   (两次至少隔 CooldownYears 年)——
//   · 京师已失、朝廷在别处的，当场冠方位(南明、南宋这样，见 CapitalLossNamingSystem.ApplyNow)，收复京师照旧去掉；
//   · 南方反过本朝、从本朝分出去的同族政权(起义军、割据、自立的行省)当场罢兵来投；此后 ShelterYears 年里新起的，每年一半机会来投；
//   · 正统保底：当年 45，逐年降到正统崩坏线(见 MonarchyLegitimacy)。民变照常起，积弊照涨(积弊只能延缓，不能削减)。
//   以前是京师一破就"偏安十五年不起满额民变"，明朝末年反倒没有民变，南明还能反攻收复关外。
public static class SouthernRetreatSystem
{
    private static readonly Dictionary<long, double> Checked = new(), Rallied = new();
    private static object _world;

    public static bool Sheltered(Empire empire)
    {
        double at = empire?.data?.southern_retreat_at ?? -1d;
        return at >= 0d && World.world != null && SouthernRetreatRules.Sheltered(YearsSince(at));
    }

    // 正统一项(见 MonarchyLegitimacy)：别的各项加起来不到保底的补齐
    public static int LegitimacyBonus(Empire empire, int others)
    {
        double at = empire?.data?.southern_retreat_at ?? -1d;
        return at < 0d || World.world == null ? 0
            : SouthernRetreatRules.LegitimacyBonus(others, YearsSince(at), DynasticCycleRules.CollapseLegitimacy);
    }

    private static int YearsSince(double at) => World.world.getCurWorldTime() < at ? 0 : Date.getYearsSince(at);

    private static bool Alive(Kingdom kingdom) => kingdom?.data != null && !kingdom.isRekt();

    // 异族：本国(帝国按核心)的文化与朝廷的不同族
    private static bool Alien(Kingdom realm, string culture)
    {
        if (!Alive(realm)) return false;
        string other = CultureService.GetRealmCulture(realm);
        return CultureService.IsValidCulture(other) && !CultureService.SameKin(other, culture);
    }

    // 每月看一次(见 Empire.update)
    public static void Check(Empire empire)
    {
        if (empire?.data == null || World.world == null || empire.isRekt() || empire.IsArchived()) return;
        if (!ReferenceEquals(_world, World.world))
        {
            _world = World.world;
            Checked.Clear();
            Rallied.Clear();
        }
        double now = World.world.getCurWorldTime();
        if (Checked.TryGetValue(empire.id, out double last) && now >= last && Date.getMonthsSince(last) < 1) return;
        Checked[empire.id] = now;
        Kingdom core = empire.CoreKingdom;
        if (!Alive(core) || core.capital == null || core.capital.isRekt()) return;
        string culture = CultureService.GetRealmCulture(core);
        if (!DynasticCycleSystem.Applies(empire) || InstitutionSystem.GetCultureLine(culture) != "Huaxia") return;
        EmpireData data = empire.data;
        int since = data.southern_retreat_at < 0d ? -1 : YearsSince(data.southern_retreat_at);
        // 已结成：期内新起的同族义军陆续来投(一年看一次)
        if (SouthernRetreatRules.Sheltered(since))
        {
            if (Rallied.TryGetValue(empire.id, out double rallied) && now >= rallied && Date.getYearsSince(rallied) < 1) return;
            Rallied[empire.id] = now;
            Kingdom former = World.world.kingdoms.get(data.southern_ming_invader);
            if (Alive(former)) Rally(empire, FrontSystem.Realm(former) ?? former, SouthernRetreatRules.LateRallyChance);
            return;
        }
        City capital = EmpireCoreManager.Get(empire)?.GetCoreCapital();
        Kingdom invader = null;
        bool holdsCapital = false, holdsThird = false;
        if (capital?.kingdom != null && !capital.isRekt() && capital.kingdom.GetEmpire() != empire)
        {
            Kingdom holder = FrontSystem.Realm(capital.kingdom);
            if (Alien(holder, culture))
            {
                invader = holder;
                holdsCapital = true;
            }
        }
        EmpireCore lead = TianxiaSystem.Lead(EmpireCoreManager.Get(empire));
        if (invader == null && lead != null)
        {
            (Dictionary<Kingdom, int> held, int total) = TianxiaSystem.Holdings(lead);
            foreach (KeyValuePair<Kingdom, int> pair in held)
            {
                if (!Alien(pair.Key, culture) || !SouthernRetreatRules.HoldsThird(pair.Value, total)) continue;
                invader = pair.Key;
                holdsThird = true;
                break;
            }
        }
        if (invader == null) return;
        int cities = empire.kingdoms_list.Where(Alive).Sum(kingdom => kingdom.cities?.Count ?? 0);
        bool successor = empire.Emperor != null && !empire.Emperor.isRekt() || core.HasHeir();
        if (!SouthernRetreatRules.Triggers(holdsCapital, holdsThird, successor, cities, since)) return;
        data.southern_retreat_at = now;
        data.southern_ming_invader = invader.id;
        Rallied[empire.id] = now;
        MonarchyLegitimacy.Invalidate(empire);
        CapitalLossNamingSystem.ApplyNow(empire);
        string text = holdsCapital
            ? string.Format(LM.Get("southern_retreat_history"), invader.GetKingdomName(), capital.GetCityName(), empire.GetEmpireFullName())
            : string.Format(LM.Get("southern_retreat_third"), invader.GetKingdomName(), empire.GetEmpireFullName());
        EventRecorder.Record(empire, text, empire.Emperor, core);
        TranslateHelper.LogEventMessage(text, core);
        List<Kingdom> joined = Rally(empire, invader, 1f);
        LogService.LogInfo($"[EmpireCraft][南明] {empire.GetEmpireFullName()}：{invader.GetKingdomName()} " +
                           $"{(holdsCapital ? "据京师 " + capital.GetCityName() : "据天下三分之一")}，朝廷在 {core.capital.GetCityName()}，" +
                           $"{cities} 城，正统 {empire.Legitimacy}，积弊 {DynasticCycleSystem.GetPressure(empire):0}；来投 " +
                           (joined.Count == 0 ? "无" : string.Join("、", joined.Select(kingdom => kingdom.GetKingdomName()))));
    }

    // 南方反过本朝、从本朝分出去的同族政权来投：都城在入侵者都城以南，不是入侵者一边的(成员、朝贡、附庸、同盟)
    private static List<Kingdom> Rally(Empire court, Kingdom invader, float chance)
    {
        var joined = new List<Kingdom>();
        Kingdom courtCore = court.CoreKingdom;
        if (!Alive(courtCore) || !Alive(invader)) return joined;
        string culture = CultureService.GetRealmCulture(courtCore);
        Empire invaderEmpire = invader.GetEmpire();
        Alliance alliance = invader.hasAlliance() ? invader.getAlliance() : null;
        float? invaderY = invader.capital != null && !invader.capital.isRekt() ? invader.capital.city_center.y : null;
        foreach (Kingdom kingdom in World.world.kingdoms.list.ToList())
        {
            if (!Alive(kingdom) || !kingdom.isCiv() || kingdom == courtCore || kingdom == invader || kingdom.capital == null) continue;
            if (FrontSystem.Realm(kingdom) != kingdom || kingdom.GetEmpire() == court) continue;
            if (invaderEmpire != null && (kingdom.GetEmpire() == invaderEmpire || kingdom.GetTakenAllianceEmpire() == invaderEmpire ||
                                          kingdom.GetGivenAllianceEmpire() == invaderEmpire)) continue;
            if (alliance != null && kingdom.hasAlliance() && kingdom.getAlliance() == alliance) continue;
            if (!CultureService.SameKin(CultureService.GetRealmCulture(kingdom), culture)) continue;
            if (!kingdom.HasRebelledAgainst(court) && kingdom.GetOrCreate().left_empire_id != court.id) continue;
            if (!SouthernRetreatRules.South(kingdom.capital.city_center.y, invaderY)) continue;
            if (chance < 1f && UnityEngine.Random.value >= chance) continue;
            if (ScenarioEventSystem.Submit(kingdom, invader, courtCore, court, 0f)) joined.Add(kingdom);
        }
        return joined;
    }
}
