using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using Random = UnityEngine.Random;

namespace EmpireCraft.Scripts.GeneralSystems;

// 羁縻朝贡(用户 2026-10-17 选"羁縻朝贡事件")：施行羁縻制度的帝国每年过一遍各羁縻政区(都护府、羁縻州，见 JimiSystem)：
//   · 册封：首领更替(新首领头一回见到)，朝廷遣使册封(花 10)；国库出不起就记着没册封，叛去的机会大一些，来年再封；
//   · 入朝：三年没来过的，每年三成机会首领入朝进贡(本地国库的二十分之一，至少 5)，朝廷厚往薄来、回赐一倍半(国库够才给)，
//     天命 +1(每年至多一次)；
//   · 叛：每年 1% 底，天命低于 40、朝廷正在打仗、首领没受册封、十年没入朝各再加，十年内加恩安抚过的只剩三成；
//     开国雄主在位时不叛。要叛时朝廷看形势：国库够(安抚费两倍)且正在打仗、或羁縻政区国力不弱于朝廷一半、或一半机会——
//     遣使安抚，厚赐金帛(每城 25，至少 40)，记为加恩；否则首领叛去自立，朝廷发兵征讨(战名"某国征讨叛服之某某")；
//   · 服：叛去的羁縻政区仍与朝廷交战一年以上、国力不到朝廷一半，每年三成机会请降复服(已经议和自立的，朝廷国力三倍时一成)：
//     重新并入帝国，朝廷加恩安抚、赦其罪；叛去三十年仍未复服、被灭或另投别的帝国的不再记着。
public static class JimiTributeSystem
{
    private const int InvestCost = 10, TributeYears = 3, FavorYears = 10, ForgetYears = 30, PopupsPerYear = 2;
    private const float TributeChance = 0.3f, ReturnGift = 1.5f;
    private const float RevoltBase = 0.01f, RevoltWeak = 0.02f, RevoltWar = 0.02f, RevoltUninvested = 0.03f,
        RevoltNeglected = 0.02f, FavorFactor = 0.3f;
    private const float SubmitChance = 0.3f, LateSubmitChance = 0.1f;

    private static bool Due(double at, int years) => at < 0d || Date.getYearsSince(at) >= years;

    private static double Get(Dictionary<long, double> map, long id) => map != null && map.TryGetValue(id, out double at) ? at : -1d;

    // 每年(帝国例行检查，见 EmpireCraftEmpireBehCheckMandate)
    public static void Yearly(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.IsArchived()) return;
        Kingdom core = empire.CoreKingdom;
        if (core?.data == null || core.isRekt()) return;
        EmpireData data = empire.data;
        data.jimi_chiefs ??= new Dictionary<long, long>();
        data.jimi_tribute_at ??= new Dictionary<long, double>();
        data.jimi_favor_at ??= new Dictionary<long, double>();
        data.jimi_rebels ??= new Dictionary<long, double>();
        Returns(empire, core);
        if (!JimiSystem.Enacted(empire)) return;
        var regions = empire.kingdoms_list.Where(region => region != null && region != core && JimiSystem.IsJimiAdministration(region) &&
                                                           region.hasKing() && (region.cities?.Count ?? 0) > 0).ToList();
        var alive = new HashSet<long>(regions.Select(region => region.id));
        foreach (long id in data.jimi_chiefs.Keys.Where(id => !alive.Contains(id)).ToList()) data.jimi_chiefs.Remove(id);
        int popups = 0;
        bool honoured = false;
        foreach (Kingdom region in regions)
        {
            try
            {
                Invest(empire, core, region, ref popups);
                if (Revolt(empire, core, region)) continue;
                if (Tribute(empire, core, region, ref popups, ref honoured)) continue;
            }
            catch (Exception exception)
            {
                LogService.LogWarning($"[EmpireCraft][羁縻朝贡] {region.GetKingdomName()} 结算失败: {exception.Message}");
            }
        }
    }

    // 册封：首领更替时遣使册封(头一回见到只记下，不算更替)
    private static void Invest(Empire empire, Kingdom core, Kingdom region, ref int popups)
    {
        EmpireData data = empire.data;
        long chief = region.king.data.id;
        if (!data.jimi_chiefs.TryGetValue(region.id, out long known))
        {
            data.jimi_chiefs[region.id] = chief;
            return;
        }
        if (known == chief) return;
        if (!TreasurySystem.TrySpend(core, InvestCost, TreasuryCategory.Diplomacy, discretionary: false))
        {
            // 没册封：记成负的，来年再封
            data.jimi_chiefs[region.id] = -chief;
            return;
        }
        data.jimi_chiefs[region.id] = chief;
        Announce(string.Format(LM.Get("jimi_invest_log"), core.GetKingdomName(), region.GetKingdomName(), region.king.getName()),
            region, history: false, ref popups);
    }

    private static bool Uninvested(Empire empire, Kingdom region) =>
        empire.data.jimi_chiefs.TryGetValue(region.id, out long chief) && chief != region.king.data.id;

    // 入朝进贡，厚往薄来
    private static bool Tribute(Empire empire, Kingdom core, Kingdom region, ref int popups, ref bool honoured)
    {
        EmpireData data = empire.data;
        if (!Due(Get(data.jimi_tribute_at, region.id), TributeYears) || region.isInWarWith(core) || region.IsLocalRebelling() ||
            region.IsFactionRebelling() || Random.value > TributeChance) return false;
        int tribute = Math.Max(5, region.GetMoney() / 20);
        if (region.GetMoney() < tribute) return false;
        region.SubMoney(tribute, TreasuryCategory.Tribute);
        core.AddMoney(tribute, TreasuryCategory.Tribute);
        int gift = (int)Math.Ceiling(tribute * ReturnGift);
        if (TreasurySystem.TrySpend(core, gift, TreasuryCategory.Gift, discretionary: false))
            region.AddMoney(gift, TreasuryCategory.Gift);
        data.jimi_tribute_at[region.id] = World.world.getCurWorldTime();
        if (!honoured)
        {
            honoured = true;
            empire.AddMandate(1);
        }
        Announce(string.Format(LM.Get("jimi_tribute_log"), core.GetKingdomName(), region.GetKingdomName(), region.king.getName()),
            region, history: false, ref popups);
        return true;
    }

    // 叛：朝廷先看能否安抚，不能就由它叛去、发兵征讨
    private static bool Revolt(Empire empire, Kingdom core, Kingdom region)
    {
        // 新入帝国十年内、神力拉入未满期的不叛去(见 MembershipCooldown)
        if (RulerTraitSystem.FounderShield(region) || region.isInWarWith(core) || region.IsLocalRebelling() ||
            !MembershipCooldown.Settled(region)) return false;
        EmpireData data = empire.data;
        float chance = RevoltBase;
        if (empire.Mandate < 40) chance += RevoltWeak;
        if (core.hasEnemies()) chance += RevoltWar;
        if (Uninvested(empire, region)) chance += RevoltUninvested;
        if (Due(Get(data.jimi_tribute_at, region.id), FavorYears)) chance += RevoltNeglected;
        if (!Due(Get(data.jimi_favor_at, region.id), FavorYears)) chance *= FavorFactor;
        if (Random.value > chance) return false;
        string chief = region.king.getName(), name = region.GetKingdomName();
        int cost = Math.Max(40, region.cities.Count * 25);
        bool pacify = core.GetMoney() >= cost * 2 &&
                      (core.hasEnemies() || ZhouEraSystem.Power(region) * 2 > ZhouEraSystem.Power(core) || Random.value < 0.5f);
        if (pacify && TreasurySystem.TrySpend(core, cost, TreasuryCategory.Gift, discretionary: false))
        {
            region.AddMoney(cost, TreasuryCategory.Gift);
            data.jimi_favor_at[region.id] = World.world.getCurWorldTime();
            int none = 0;
            Announce(string.Format(LM.Get("jimi_pacify_log"), core.GetKingdomName(), name, chief), region, history: true, ref none);
            return true;
        }
        empire.leave(region);
        War war = DiplomacyHelpers.wars.newWar(region, core, WarTypeLibrary.normal);
        if (war?.data != null)
        {
            war.SetEmpireWarType(EmpireWarType.地方独立);
            war.data.name = string.Format(LM.Get("jimi_revolt_war_name"), core.GetKingdomName(), name);
        }
        data.jimi_rebels[region.id] = World.world.getCurWorldTime();
        data.jimi_chiefs.Remove(region.id);
        int count = 0;
        Announce(string.Format(LM.Get("jimi_revolt_log"), core.GetKingdomName(), name, chief), core, history: true, ref count);
        return true;
    }

    // 服：叛去的羁縻政区请降复服
    private static void Returns(Empire empire, Kingdom core)
    {
        EmpireData data = empire.data;
        foreach (long id in data.jimi_rebels.Keys.ToList())
        {
            double since = data.jimi_rebels[id];
            Kingdom rebel = World.world.kingdoms.get(id);
            int none = 0;
            if (rebel?.data == null || rebel.isRekt() || (rebel.cities?.Count ?? 0) == 0)
            {
                data.jimi_rebels.Remove(id);
                continue;
            }
            if (rebel.GetEmpire() != null || Date.getYearsSince(since) >= ForgetYears)
            {
                data.jimi_rebels.Remove(id);
                continue;
            }
            if (!rebel.hasKing()) continue;
            bool atWar = rebel.isInWarWith(core);
            int power = ZhouEraSystem.Power(rebel), royal = ZhouEraSystem.Power(core);
            bool yields = atWar
                ? Date.getYearsSince(since) >= 1 && power * 2 < royal && Random.value < SubmitChance
                : power * 3 < royal && Random.value < LateSubmitChance;
            if (!yields) continue;
            string name = rebel.GetKingdomName(), chief = rebel.king.getName();
            if (atWar) core.EndWarWith(rebel);
            empire.join(rebel, pForce: true);
            if (rebel.GetEmpire() != empire) continue;
            data.jimi_rebels.Remove(id);
            data.jimi_favor_at[id] = World.world.getCurWorldTime();
            data.jimi_chiefs[id] = rebel.king.data.id;
            Announce(string.Format(LM.Get("jimi_submit_log"), core.GetKingdomName(), name, chief), rebel, history: true, ref none);
        }
    }

    // 册封、入朝只弹提示(每国每年至多两条)；叛服连史册一起记
    private static void Announce(string text, Kingdom kingdom, bool history, ref int popups)
    {
        LogService.LogInfo("[EmpireCraft][羁縻朝贡] " + text);
        if (history)
        {
            TranslateHelper.LogEventMessage(text, kingdom);
            return;
        }
        if (popups >= PopupsPerYear) return;
        popups++;
        TranslateHelper.LogEventOnly(text, kingdom);
    }
}
