using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 战争窗口(WarInfoWindow)、战争提示框共用的取数与措辞(示意图 Tasks/ui-mockups/war-wide.png ①~⑤、⑨)
    public static class WarDisplay
    {
        // 攻方红、守方蓝(条、字)
        public const string AttackBar = "#D9583F", DefendBar = "#4F86C9", AttackText = "#FF8A73", DefendText = "#8FB8EA";

        public static bool Alive(War war) => war?.data != null;

        public static List<Kingdom> Side(War war, bool attackers) =>
            ((attackers ? war?.getAttackers() : war?.getDefenders()) ?? Enumerable.Empty<Kingdom>())
            .Where(kingdom => kingdom?.data != null && !kingdom.isRekt()).Distinct().ToList();

        // 一边各家按帝国中央归并(帝国记一家)
        public static List<Kingdom> Realms(War war, bool attackers) =>
            Side(war, attackers).Select(FrontSystem.Realm).Where(realm => realm?.data != null).Distinct().ToList();

        public static float Army(War war, bool attackers) => Realms(war, attackers).Sum(WarSituation.SideWarriors);

        public static int Cities(War war, bool attackers) => Side(war, attackers).Sum(kingdom => kingdom.cities?.Count ?? 0);

        public static int CitiesLost(War war, bool attackers)
        {
            Kingdom main = attackers ? war.main_attacker : war.main_defender;
            return main?.data == null ? 0 : Math.Max(0, WarSituation.CitiesLost(war, main));
        }

        public static string Count(float value) => CorpsSystem.ShortStrength(Mathf.Max(0f, value));

        // 战局：目标已达成 / 攻方占优 / 守方占优 / 相持；打完了说谁胜
        public static (string text, string hex) Standing(War war)
        {
            if (war.hasEnded())
                return war.data.winner switch
                {
                    WarWinner.Attackers => (LM.Get("war_standing_won_attackers"), AttackText),
                    WarWinner.Defenders => (LM.Get("war_standing_won_defenders"), DefendText),
                    _ => (LM.Get("war_standing_peace"), WideKit.Soft)
                };
            if (war.GetOrCreate().goal_achieved) return (LM.Get("war_standing_achieved"), WideKit.Green);
            (int attackerGain, int defenderGain) = WarGoalSystem.Gains(war);
            float attack = Army(war, true), defend = Army(war, false);
            if (attackerGain >= defenderGain + 1 || attackerGain == defenderGain && attack >= defend * 1.5f)
                return (LM.Get("war_standing_attackers"), AttackText);
            if (defenderGain >= attackerGain + 1 || attackerGain == defenderGain && defend >= attack * 1.5f)
                return (LM.Get("war_standing_defenders"), DefendText);
            return (LM.Get("war_standing_even"), WideKit.Soft);
        }

        // 若此刻议和：按双方得城判胜负(见 WarGoalSystem.Settle)
        public static string PeacePreview(War war)
        {
            (int attackerGain, int defenderGain) = WarGoalSystem.Gains(war);
            if (attackerGain >= defenderGain + 1) return string.Format(LM.Get("war_peace_preview_attackers"), attackerGain - defenderGain);
            if (defenderGain >= attackerGain + 1) return string.Format(LM.Get("war_peace_preview_defenders"), defenderGain - attackerGain);
            return LM.Get("war_peace_preview_white");
        }

        // 强制停战还剩几年(统一战争、开国雄主之战、法统内战不受限)
        public static string TruceLimit(War war)
        {
            if (WarlordEraSystem.IsCivilWar(war) || RulerTraitSystem.IsFounderUnificationWar(war)) return LM.Get("war_truce_exempt");
            return string.Format(LM.Get("war_truce_years"), Math.Max(0, ModClass.WAR_END_YEAR - war.getDuration()));
        }

        // 这一国怎么卷进来的
        public static string Role(War war, Kingdom kingdom, bool attackers)
        {
            Kingdom main = attackers ? war.main_attacker : war.main_defender;
            if (kingdom == main) return LM.Get(attackers ? "war_role_main_attacker" : "war_role_main_defender");
            if (main?.data == null) return LM.Get("war_role_ally");
            Empire empire = main.GetEmpire();
            if (empire != null && kingdom.GetEmpire() == empire) return LM.Get("war_role_member");
            Kingdom overlord = FeudalVassalService.GetOverlord(kingdom);
            if (overlord != null && FrontSystem.Realm(overlord) == FrontSystem.Realm(main)) return LM.Get("war_role_vassal");
            if (FeudalVassalService.GetOverlord(main) == kingdom) return LM.Get("war_role_overlord");
            if (kingdom.hasAlliance() && kingdom.getAlliance() == main.getAlliance()) return LM.Get("war_role_alliance");
            return LM.Get("war_role_ally");
        }

        // 这场仗的陆战(军团月结，见 FrontSystem)：交战双方分在这场仗的两边
        public static List<FrontSystem.BattleView> Battles(War war)
        {
            var attackers = new HashSet<Kingdom>(Side(war, true));
            var defenders = new HashSet<Kingdom>(Side(war, false));
            var result = new List<FrontSystem.BattleView>();
            foreach (FrontSystem.BattleView battle in FrontSystem.Battles.Values)
            {
                Kingdom a = World.world.kingdoms.get(battle.attacker_id), d = World.world.kingdoms.get(battle.defender_id);
                if (a == null || d == null) continue;
                if (attackers.Contains(a) && defenders.Contains(d) || defenders.Contains(a) && attackers.Contains(d)) result.Add(battle);
            }
            return result.OrderByDescending(battle => battle.time).ToList();
        }

        public static List<NavySystem.BattleView> NavalBattles(War war)
        {
            var attackers = new HashSet<Kingdom>(Side(war, true));
            var defenders = new HashSet<Kingdom>(Side(war, false));
            return NavySystem.Battles.Values.Where(battle =>
            {
                Kingdom a = World.world.kingdoms.get(battle.attacker_id), d = World.world.kingdoms.get(battle.defender_id);
                return a != null && d != null && (attackers.Contains(a) && defenders.Contains(d) || defenders.Contains(a) && attackers.Contains(d));
            }).OrderByDescending(battle => battle.time).ToList();
        }

        // 这场仗攻方一边(true)的城在不在攻方手里：用于战线一行的颜色
        public static bool AttackerSide(War war, Kingdom kingdom) => kingdom != null && Side(war, true).Contains(kingdom);

        // 目标城的进度：已攻下 / 攻方占 62% / 未围
        public static (float share, string text, string hex) Progress(War war, City city)
        {
            float share = WarObjective.HeldShareOf(war, city);
            if (share >= 1f) return (1f, LM.Get("war_progress_taken"), WideKit.Green);
            if (share <= 0f) return (0f, LM.Get("war_progress_none"), WideKit.Soft);
            return (share, string.Format(LM.Get("war_progress_held"), share), share >= 0.5f ? WideKit.Green : WideKit.Orange);
        }

        public static string Losses(War war, bool attackers) => Count(WarChronicle.Losses(war, attackers));

        public static string Started(War war) => string.Format(LM.Get("war_started_years"), war.getYearStarted(), war.getDuration());

        // 战争提示框(原版"war"提示框换成模组的，示意图 ⑤⑨)
        private const string Plain = "#D8D2B8", Bad = "#E07A5F", Good = "#7CD27C";

        private static void Line(Tooltip tooltip, string labelKey, string value, string hex = Plain) =>
            tooltip.addLineText(LM.Get(labelKey), value, hex, pPercent: false, pLocalize: false);

        public static void ShowTooltip(Tooltip tooltip, string type, TooltipData data)
        {
            War war = data?.war;
            if (!Alive(war)) return;
            tooltip.clear();
            tooltip.setTitle(war.data.name, "war", WideKit.Gold);
            var head = new List<string> { WarCatalog.Kind(war).ColorString(WarCatalog.Color(war)) };
            head.AddRange(WarCatalog.Tags(war).Select(tag => tag.ColorString(WideKit.Cyan)));
            head.Add(war.hasEnded() ? string.Format(LM.Get("war_tip_ended"), war.getYearEnded()) : string.Format(LM.Get("war_tip_year"), war.getDuration() + 1));
            tooltip.addLineText(LM.Get("war_tip_kind"), string.Join(" · ", head), Plain, pPercent: false, pLocalize: false);
            List<Kingdom> attackers = Side(war, true), defenders = Side(war, false);
            Line(tooltip, "war_tip_attackers", SideText(war.main_attacker, attackers.Count, Army(war, true)), AttackText);
            Line(tooltip, "war_tip_defenders", SideText(war.main_defender, defenders.Count, Army(war, false)), DefendText);
            WarExtension.WarExtraData extra = war.GetOrCreate();
            City main = WarObjective.Main(extra);
            if (WarGoalSystem.Get(war) != WarGoal.None)
            {
                Line(tooltip, "war_tip_goal", WarCatalog.GoalText(war), WideKit.Gold);
                if (main != null && !war.hasEnded())
                {
                    (_, string text, string hex) = Progress(war, main);
                    Line(tooltip, "war_tip_main_city", $"{main.GetCityName()} · {text}", hex);
                }
            }
            else if (WarObjective.RebelWar(war) && war.main_defender?.capital is City capital && !war.hasEnded())
            {
                (_, string text, string hex) = Progress(war, capital);
                Line(tooltip, "war_tip_capital", $"{capital.GetCityName()} · {text}", hex);
            }
            (string standing, string standingHex) = Standing(war);
            Line(tooltip, "war_tip_standing", string.Format(LM.Get("war_tip_standing_value"), standing, CitiesLost(war, true),
                CitiesLost(war, false)), standingHex);
            Line(tooltip, "war_tip_losses", string.Format(LM.Get("war_tip_losses_value"), Losses(war, true), Losses(war, false)));
            if (!war.hasEnded())
            {
                int fronts = Battles(war).Count;
                string front = string.Format(LM.Get("war_tip_fronts_value"), fronts);
                if (war.main_defender != null && war.main_attacker != null &&
                    FrontCollapse.Collapsing(FrontSystem.Realm(war.main_defender), FrontSystem.Realm(war.main_attacker)))
                    front += " · " + string.Format(LM.Get("war_tip_collapse"), war.main_defender.GetKingdomName());
                else if (war.main_defender != null && war.main_attacker != null &&
                         FrontCollapse.Collapsing(FrontSystem.Realm(war.main_attacker), FrontSystem.Realm(war.main_defender)))
                    front += " · " + string.Format(LM.Get("war_tip_collapse"), war.main_attacker.GetKingdomName());
                Line(tooltip, "war_tip_fronts", front, fronts > 0 ? Bad : Plain);
            }
            string outcome = WarCatalog.Outcome(war);
            tooltip.addBottomDescription((string.IsNullOrEmpty(outcome) ? "" : outcome + "\n") + LM.Get("war_tip_footer"));
        }

        private static string SideText(Kingdom main, int count, float army) =>
            string.Format(LM.Get(count > 1 ? "war_tip_side_many" : "war_tip_side_one"), main?.GetKingdomName() ?? "—", count, Count(army));
    }
}
