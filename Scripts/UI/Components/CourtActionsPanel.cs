using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GeneralSystems.Decisions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;

namespace EmpireCraft.Scripts.UI.Components;

// 帝国窗口里的出手按钮(用户 2026-11-15 "游戏窗口里也加上这些")，同手机上的一套规则，只是神力当场就办、不等一个月：
//   · 朝局(天平页「朝局」签)：权臣卡(削权的天平、安抚、削权)，各派一行加恩赏、申饬、罢其首领；
//   · 可议之事(天平页「决议」签，接在 DecisionScalePanel 后)：皇帝办得成的决议，点发起当场立项；
//   · 开源节流、内帑(财报页)。
// 办不成的弹一句缘由(WorldTip)
public static class CourtActionsPanel
{
    private const float CardPadding = 12f;

    private static void Tip(string failedKey, string done = null)
    {
        if (failedKey != null) WorldTip.showNow(LM.Get(failedKey), false, "top", 2.5f);
        else if (!string.IsNullOrEmpty(done)) WorldTip.showNow(done, false, "top", 2.5f);
    }

    // ───────── 朝局 ─────────
    public static void RenderCourt(AutoVertLayoutGroup page, Empire empire, float width, Action refresh)
    {
        if (empire?.CoreKingdom == null) return;
        var intro = page.BeginCard(width, "court_card_title", WideKit.Gold, null, "ui/icons/iconKingdom");
        intro.AddParagraph(width, LM.Get("court_card_note"), 7, WideKit.Soft);
        int crisis = CourtSystem.State(empire)?.crisis_years ?? 0;
        if (crisis > 0) intro.AddParagraph(width, string.Format(LM.Get("court_lbl_crisis"), crisis), 7, WideKit.Red);

        var ministerCard = page.BeginCard(width, "court_lbl_minister", WideKit.Red);
        Actor minister = empire.GetPowerfulMinister();
        if (minister == null) ministerCard.AddParagraph(width, LM.Get("court_lbl_no_minister"), 7, WideKit.Muted);
        else
        {
            var weights = new List<ScaleWeight>();
            float ratio = CourtActions.CurbScale(empire, weights);
            FixedFaction side = CourtActions.MinisterFaction(empire, minister);
            ministerCard.AddValueRow(width, 0, minister.getName(), (side?.Name ?? "") + " · " +
                (empire.GetPowerfulMinisterStatusText() ?? "").Replace("\n", " · "), WideKit.Value);
            ministerCard.AddBarRow(width, 1, LM.Get("court_lbl_minister"), empire.data.powerful_minister_progress / 100f,
                empire.data.powerful_minister_progress + "%", WideKit.Red, WideKit.Red, "#3A3F44");
            string verdict = string.Format(LM.Get(ratio >= CourtActions.CurbHigh ? "court_curb_dismiss" : ratio >= 1f ? "court_curb_ok" : "court_curb_fail"), ratio);
            ministerCard.AddParagraph(width, verdict + "  " + Weights(weights), 6, WideKit.Muted);
            int appeaseWait = CourtActions.Wait(empire, "minister", "appease"), curbWait = CourtActions.Wait(empire, "minister", "curb");
            ministerCard.AddActionRow(width - CardPadding,
                (LM.Get("court_act_appease") + " · " + (appeaseWait > 0 ? string.Format(LM.Get("court_act_wait"), appeaseWait)
                    : string.Format(LM.Get("court_act_cost"), CourtActions.AppeaseCost(empire))), WideKit.Cyan, appeaseWait == 0, () =>
                {
                    Tip(CourtActions.Appease(empire));
                    refresh?.Invoke();
                }),
                (LM.Get("court_act_curb") + (curbWait > 0 ? " · " + string.Format(LM.Get("court_act_wait"), curbWait) : ""), WideKit.Red, curbWait == 0, () =>
                {
                    string failed = CourtActions.Curb(empire, out bool dismissed);
                    Tip(failed, dismissed ? LM.Get("court_curb_dismissed") : null);
                    refresh?.Invoke();
                }));
        }

        var factions = page.BeginCard(width, "court_card_title", WideKit.Cyan, CourtSystem.Factions(empire).Count.ToString());
        int index = 0;
        foreach (FixedFaction faction in CourtSystem.Factions(empire).OrderByDescending(faction => faction.CentralRatio))
        {
            CourtFactionState state = CourtSystem.FactionState(empire, faction);
            string id = faction.GetID();
            Actor leader = faction.GetLeader();
            string line = $"{LM.Get("court_lbl_ratio")} {faction.CentralRatio}% · {LM.Get("court_lbl_leader")} {(leader != null && leader.isAlive() ? leader.getName() : "—")} · " +
                          $"{LM.Get("court_lbl_favor")} {state.favor:0} · {LM.Get("court_lbl_grievance")} {state.grievance:0} · {LM.Get("court_lbl_capital")} {state.capital:0}";
            factions.AddValueRow(width, index++, faction.Name ?? id, line, state.grievance >= 60f ? WideKit.Orange : WideKit.Value,
                labelWidth: (width - CardPadding) * 0.22f);
            int rewardWait = CourtActions.Wait(empire, id, "reward"), rebukeWait = CourtActions.Wait(empire, id, "rebuke"),
                dismissWait = CourtActions.Wait(empire, id, "dismiss");
            bool hasOffice = CourtActions.LeaderOffices(empire, faction).Count > 0;
            factions.AddActionRow(width - CardPadding,
                (LM.Get("court_act_reward") + " · " + (rewardWait > 0 ? string.Format(LM.Get("court_act_wait"), rewardWait)
                    : string.Format(LM.Get("court_act_cost"), CourtActions.RewardCost(empire))), WideKit.Green, rewardWait == 0, () =>
                {
                    Tip(CourtActions.Reward(empire, faction));
                    refresh?.Invoke();
                }),
                (LM.Get("court_act_rebuke") + (rebukeWait > 0 ? " · " + string.Format(LM.Get("court_act_wait"), rebukeWait) : ""), WideKit.Orange,
                    rebukeWait == 0, () =>
                    {
                        Tip(CourtActions.Rebuke(empire, faction));
                        refresh?.Invoke();
                    }),
                (LM.Get("court_act_dismiss") + (!hasOffice ? " · " + LM.Get("court_act_no_office")
                    : dismissWait > 0 ? " · " + string.Format(LM.Get("court_act_wait"), dismissWait) : ""), WideKit.Red,
                    hasOffice && dismissWait == 0, () =>
                    {
                        Tip(CourtActions.DismissLeader(empire, faction));
                        refresh?.Invoke();
                    }));
        }
    }

    // ───────── 可议之事 ─────────
    // 一国之主办得成的决议(帝国给中央，王国给本国)
    public static void RenderOptions(AutoVertLayoutGroup page, Kingdom realm, float width, Action refresh)
    {
        Actor emperor = realm?.king;
        if (emperor == null || !emperor.isAlive()) return;
        List<DecisionSystem.Advice> all = DecisionSystem.AdviseFor(emperor);
        List<DecisionSystem.Advice> fit = all.Where(advice => advice.blocked == null).ToList();
        var card = page.BeginCard(width, "decision_scale_options", WideKit.Cyan, fit.Count + "/" + all.Count);
        card.AddParagraph(width, LM.Get("decision_scale_options_god"), 6, WideKit.Muted);
        bool busy = emperor.plot != null && emperor.plot.isActive();
        if (busy) card.AddParagraph(width, LM.Get("decision_fail_busy"), 7, WideKit.Orange);
        if (fit.Count == 0) card.AddParagraph(width, LM.Get("decision_scale_none_fit"), 7, WideKit.Muted);
        int index = 0;
        bool advised = false;
        foreach (DecisionSystem.Advice advice in fit)
        {
            string name = LM.Has("plot_" + advice.id) ? LM.Get("plot_" + advice.id) : advice.id;
            string mark = !advised && advice.ratio >= DecisionScale.Threshold ? LM.Get("decision_scale_advice") + " · " : "";
            if (mark.Length > 0) advised = true;
            card.AddValueRow(width, index++, name, mark + string.Format(LM.Get("decision_scale_ratio"), advice.ratio) +
                (advice.cost > 0 ? " · " + string.Format(LM.Get("decision_scale_cost"), advice.cost) : ""), mark.Length > 0 ? WideKit.Gold : WideKit.Value);
            card.AddParagraph(width, Weights(advice.weights), 6, WideKit.Muted);
            string id = advice.id;
            card.AddActionRow(width - CardPadding, (LM.Get("decision_scale_launch") + " · " + name, WideKit.Green, !busy, () =>
            {
                string failed = DecisionSystem.Launch(emperor, id);
                Tip(failed);
                refresh?.Invoke();
            }));
        }
        int unfit = all.Count - fit.Count;
        if (unfit > 0) card.AddParagraph(width, string.Format(LM.Get("decision_scale_unfit_more"), unfit), 6, WideKit.Muted);
    }

    // ───────── 开源节流(财报页) ─────────
    // 帝国的中央才有统一的基础税率可调；王国的税率随政体，只能裁军
    private static Empire CoreEmpire(Kingdom realm)
    {
        Empire empire = realm?.GetEmpire();
        return empire != null && !empire.isRekt() && empire.CoreKingdom == realm ? empire : null;
    }

    public static void RenderLevers(AutoVertLayoutGroup page, Kingdom core, float width, Action refresh)
    {
        if (core?.data == null) return;
        Empire empire = CoreEmpire(core);
        var card = page.BeginCard(width, "fiscal_lever_title", WideKit.Gold);
        if (empire == null) card.AddParagraph(width, LM.Get("fiscal_tax_kingdom"), 6, WideKit.Muted);
        bool canTax = empire != null && ConstitutionalEconomySystem.CanChangeTax(empire);
        if (empire != null)
        {
        card.AddValueRow(width, 0, string.Format(LM.Get("fiscal_tax_rate"), Math.Round(empire.data.TaxRate * 100f)),
            canTax ? "" : LM.Get("fiscal_tax_locked"), canTax ? WideKit.Value : WideKit.Orange);
        card.AddActionRow(width - CardPadding,
            (LM.Get("fiscal_tax_up") + " · " + LM.Get("fiscal_tax_up_note"), WideKit.Orange, canTax, () =>
            {
                empire.AddTaxRate(0.05f);
                refresh?.Invoke();
            }),
            (LM.Get("fiscal_tax_down") + " · " + LM.Get("fiscal_tax_down_note"), WideKit.Green, canTax, () =>
            {
                empire.SubTaxRate(0.05f);
                refresh?.Invoke();
            }));
        }
        card.AddParagraph(width, LM.Get("fiscal_disband_title"), 7, WideKit.Soft);
        List<CorpsData> idle = CorpsSystem.Of(core).Where(corps => corps != null && corps.task is not ("attack" or "defend"))
            .OrderByDescending(corps => corps.strength).Take(8).ToList();
        if (idle.Count == 0) card.AddParagraph(width, LM.Get("fiscal_disband_none"), 6, WideKit.Muted);
        foreach (CorpsData corps in idle)
        {
            City home = World.world.cities.get(corps.home_city_id);
            CorpsData target = corps;
            card.AddActionRow(width - CardPadding, (LM.Get("fiscal_disband") + " · " + CorpsSystem.Name(corps) + " · " +
                string.Format(LM.Get("fiscal_corps_line"), (int)corps.strength, home?.data?.name ?? ""), WideKit.Orange, true, () =>
            {
                if (CorpsSystem.Of(core).Contains(target) && target.task is not ("attack" or "defend")) CorpsSystem.Disband(core, target);
                else Tip("fiscal_disband_busy");
                refresh?.Invoke();
            }));
        }
    }

    // ───────── 内帑(财报页) ─────────
    public static void RenderPurse(AutoVertLayoutGroup page, Kingdom core, float width, Action refresh)
    {
        Empire empire = CoreEmpire(core);
        Actor king = core?.king;
        if (king == null || !king.isAlive()) return;
        PrivyPurseData book = PrivyPurse.Book(core);
        var card = page.BeginCard(width, "purse_title", WideKit.Gold, MoneyDisplay.Format(king.money));
        string Ledger(Dictionary<string, long> sums) => string.Join(" · ",
            new[] { PrivyPurse.Allowance, PrivyPurse.Estates, PrivyPurse.Offices, PrivyPurse.Taken, PrivyPurse.Graft }
                .Where(key => sums.TryGetValue(key, out long v) && v > 0).Select(key => LM.Get("purse_src_" + key) + " " + MoneyDisplay.Format(sums[key])));
        string now = Ledger(book.thisYear), before = Ledger(book.lastYear);
        card.AddValueRow(width, 0, LM.Get("purse_this_year"), now.Length == 0 ? LM.Get("purse_none") : now, WideKit.Value, labelWidth: (width - CardPadding) * 0.2f);
        if (before.Length > 0) card.AddValueRow(width, 1, LM.Get("purse_last_year"), before, WideKit.Soft, labelWidth: (width - CardPadding) * 0.2f);

        card.AddParagraph(width, LM.Get("purse_tier_title") + " · " + LM.Get("purse_tier_help"), 6, WideKit.Muted);
        var tiers = new (string, string, bool, UnityEngine.Events.UnityAction)[PrivyPurse.TierShare.Length];
        for (int tier = 0; tier < tiers.Length; tier++)
        {
            int pick = tier;
            string label = LM.Get("purse_tier_" + tier) + (tier == 0 ? "" : " · " + string.Format(LM.Get("purse_tier_note"),
                Math.Round(PrivyPurse.TierShare[tier] * 100f), PrivyPurse.AllowanceOf(core, tier)));
            tiers[tier] = (label, book.tier == tier ? WideKit.Gold : WideKit.Cyan, book.tier != tier, () =>
            {
                Tip(PrivyPurse.SetTier(core, pick));
                refresh?.Invoke();
            });
        }
        card.AddActionRow(width - CardPadding, tiers);

        float land = PrivyPurse.RoyalLand(core, out List<(City city, float share)> holdings);
        card.AddParagraph(width, LM.Get("purse_land_title") + " · " + string.Format(LM.Get("purse_land_line"), Math.Round(land, 1),
            Math.Round(PrivyPurse.MonthlyRent(core), 1)) + (holdings.Count == 0 ? "" : "  " +
            string.Join(" · ", holdings.Take(6).Select(item => item.city.data.name + " " + item.share.ToString("0.#")))), 6, WideKit.Soft);
        List<City> buyable = core.cities.Where(city => PrivyPurse.CanBuyLand(core, city))
            .OrderByDescending(city => city == core.capital).ThenByDescending(city => city.getPopulationPeople()).Take(4).ToList();
        if (buyable.Count == 0) card.AddParagraph(width, LM.Get("purse_land_closed"), 6, WideKit.Muted);
        else
            card.AddActionRow(width - CardPadding, buyable.Select(city => (city.data.name + " · " +
                string.Format(LM.Get("purse_land_buy"), PrivyPurse.BuyPoints, PrivyPurse.LandPrice(city)), WideKit.Green, king.money >= PrivyPurse.LandPrice(city),
                (UnityEngine.Events.UnityAction)(() =>
                {
                    Tip(PrivyPurse.BuyLand(core, city));
                    refresh?.Invoke();
                }))).ToArray());

        card.AddParagraph(width, LM.Get("purse_office_title") + " · " + LM.Get("purse_office_help"), 6, WideKit.Muted);
        List<(int slot, OfficeObject office)> vacant = empire == null ? new List<(int, OfficeObject)>() : PrivyPurse.VacantOffices(empire).Take(4).ToList();
        if (vacant.Count == 0) card.AddParagraph(width, LM.Get(empire == null ? "purse_office_kingdom" : "purse_office_none"), 6, WideKit.Muted);
        else
            card.AddActionRow(width - CardPadding, vacant.Select(item => ((item.office.GetOfficeName(null) ?? "") + " · " +
                string.Format(LM.Get("purse_sell"), PrivyPurse.OfficePrice(core)), WideKit.Orange, true, (UnityEngine.Events.UnityAction)(() =>
                {
                    string failed = PrivyPurse.SellOffice(empire, item.slot, out string buyer);
                    Tip(failed, buyer == null ? null : string.Format(LM.Get("purse_sold_to"), buyer));
                    refresh?.Invoke();
                }))).ToArray());

        card.AddParagraph(width, LM.Get("purse_take_title") + " · " + LM.Get("purse_take_help"), 6, WideKit.Orange);
        (string, string, bool, UnityEngine.Events.UnityAction) Take(float share) =>
            (string.Format(LM.Get("purse_take"), PrivyPurse.TakeAmount(core, share)), WideKit.Red, PrivyPurse.TakeAmount(core, share) > 0, () =>
            {
                string failed = PrivyPurse.Embezzle(core, share, out bool exposed, out int taken);
                Tip(failed, string.Format(LM.Get(exposed ? "purse_take_exposed" : "purse_take_quiet"), taken));
                refresh?.Invoke();
            });
        card.AddActionRow(width - CardPadding, Take(PrivyPurse.SmallTake), Take(PrivyPurse.BigTake));
    }

    private static string Weights(List<ScaleWeight> weights)
    {
        if (weights == null || weights.Count == 0) return "";
        string Side(bool push) => string.Join("、", weights.Where(weight => weight.push == push).OrderByDescending(weight => weight.factor)
            .Select(weight => $"{LM.Get(weight.key)}×{weight.factor:0.00}"));
        string pushes = Side(true), pulls = Side(false);
        return (LM.Get("decision_scale_push") + "：" + (pushes.Length == 0 ? LM.Get("label_none") : pushes)).ColorString(WideKit.Green) + "  " +
               (LM.Get("decision_scale_pull") + "：" + (pulls.Length == 0 ? LM.Get("label_none") : pulls)).ColorString(WideKit.Red);
    }
}
