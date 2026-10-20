using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 教廷与帝国页(西方，见 PapacySystem)：
    //   页头 → 主指标(教权、加冕、叙任权、什一税) → 两栏(教廷：教宗、圣地、教廷所在、状态 | 本国与教廷：叙任权之争、绝罚、两党)
    //   → 圣战卡 → 骑士团卡 → 按钮(加冕、绝罚或解除、号召圣战、创立骑士团)
    public partial class EmpireWindow
    {
        public void ShowPapacy(WindowMetaTab pArg0)
        {
            Clear();
            Religion religion = PapacySystem.ReligionOf(_empire);
            if (!PapacySystem.IsWestern(_empire) || religion == null || PapacySystem.HolyCity(religion) == null)
            {
                var none = BeginPage("empire_papacy");
                none.AddEmptyCard(MainWidth, "papacy_none_title", LM.Get("papacy_none_hint"));
                StartCoroutine(RefreshScrollLayout(resetToTop: true));
                return;
            }
            PapalRelationState state = PapacySystem.State(_empire);
            Kingdom papacy = PapacySystem.Papacy(religion);
            float authority = PapacySystem.Authority(religion);
            var data = religion.GetOrCreate();
            var page = BeginPage("empire_papacy", religion.name, PapacyStatus(religion));

            // 非君主政体(古典共和等)不在教廷加冕之列：不显示"未加冕"，免得像是缺了什么
            bool crownable = PapacySystem.Applies(_empire);
            string crown = !crownable ? LM.Get("papacy_crown_na")
                : PapacySystem.IsHolySee(_empire) ? LM.Get("papacy_crown_holy_see")
                : PapacySystem.IsExcommunicated(_empire) ? LM.Get("papacy_crown_banned")
                : PapacySystem.IsCrowned(_empire) ? LM.Get(state.self_crowned ? "papacy_crown_self" : "papacy_crown_pope")
                : LM.Get("papacy_crown_none");
            string crownHex = !crownable ? WideKit.Muted
                : PapacySystem.IsExcommunicated(_empire) ? WideKit.Red
                : PapacySystem.IsCrowned(_empire) || PapacySystem.IsHolySee(_empire) ? WideKit.Green : WideKit.Orange;
            page.AddKpiRow(MainWidth,
                (LM.Get("papacy_authority"), $"{authority:0}", WideKit.Grade(authority, 60, 35)),
                (LM.Get("papacy_coronation"), crown, crownHex),
                (LM.Get("papacy_investiture"), LM.Get("papacy_investiture_" + PapacySystem.Investiture(_empire)), WideKit.Cyan),
                (LM.Get("papacy_tithe"), MoneyDisplay.Format(state.tithe_paid), WideKit.Gold));

            var columns = page.BeginColumns(MainWidth);
            var see = columns.BeginColumnCard(HalfWidth, "papacy_see_title", WideKit.Gold, null, "TabPapacy");
            Actor pope = PapacySystem.Pope(religion);
            see.AddPersonRow(HalfWidth, pope, LM.Get("papacy_pope"), pope?.getName() ?? LM.Get("label_none"),
                papacy != null ? papacy.GetKingdomName() : LM.Get("papacy_see_lost"));
            see.AddListRow(HalfWidth, 0, null, LM.Get("papacy_holy_city"), PapacySystem.HolyCity(religion)?.GetCityName() ?? "", titleWidth: 50f);
            see.AddListRow(HalfWidth, 1, null, LM.Get("papacy_state"), PapacyStatus(religion), titleWidth: 50f);
            if (papacy != null)
                see.AddListRow(HalfWidth, 2, null, LM.Get("papacy_see_treasury"), MoneyDisplay.Format(papacy.GetMoney()), titleWidth: 50f);

            var realm = columns.BeginColumnCard(HalfWidth, "papacy_realm_title", WideKit.Cyan);
            string contest = PapacySystem.InContest(_empire)
                ? string.Format(LM.Get("papacy_contest_years"), Date.getYearsSince(state.investiture_contest_at))
                : LM.Get("label_none");
            realm.AddListRow(HalfWidth, 0, null, LM.Get("papacy_contest"), contest, titleWidth: 56f);
            string ban = PapacySystem.IsExcommunicated(_empire)
                ? string.Format(LM.Get("papacy_ban_detail"), LM.Get(state.excommunication_reason),
                    Date.getYearsSince(state.excommunicated_at))
                : LM.Get("label_none");
            realm.AddListRow(HalfWidth, 1, null, LM.Get("papacy_ban"), ban, titleWidth: 56f);
            List<Kingdom> guelphs = PapacySystem.PartyMembers(_empire, PapacySystem.PartyPope);
            List<Kingdom> ghibellines = PapacySystem.PartyMembers(_empire, PapacySystem.PartyEmperor);
            realm.AddListRow(HalfWidth, 2, null, LM.Get("papacy_party_pope"), Names(guelphs), titleWidth: 56f);
            realm.AddListRow(HalfWidth, 3, null, LM.Get("papacy_party_emperor"), Names(ghibellines), titleWidth: 56f);
            if (state.national_church_id >= 0)
                realm.AddListRow(HalfWidth, 4, null, LM.Get("papacy_national_church"), religion.name, titleWidth: 56f);
            // 普世之名(见 RealmNamingSystem.Universal)
            realm.AddListRow(HalfWidth, 5, null, LM.Get("papacy_universal_name"), RealmNamingSystem.UniversalStatus(_empire), titleWidth: 56f);

            War crusade = PapacySystem.ActiveCrusade(religion);
            var holyWar = page.BeginCard(MainWidth, "papacy_crusade_title", WideKit.Red,
                string.Format(LM.Get("papacy_crusade_record"), data.CrusadeCount, data.CrusadeLosses));
            if (crusade != null)
            {
                holyWar.AddListRow(MainWidth, 0, null, crusade.data.name,
                    $"{crusade.main_attacker?.GetKingdomName()} → {crusade.main_defender?.GetKingdomName()}", titleWidth: 110f);
                holyWar.AddListRow(MainWidth, 1, null, LM.Get("papacy_crusade_sides"),
                    string.Format(LM.Get("papacy_crusade_sides_value"), crusade._list_attackers.Count, crusade._list_defenders.Count),
                    titleWidth: 110f);
            }
            else
            {
                double now = World.world.getCurWorldTime();
                string idle = now < data.CrusadeCooldownUntil
                    ? string.Format(LM.Get("papacy_crusade_cooldown"), Mathf.CeilToInt((float)((data.CrusadeCooldownUntil - now) / 60d)))
                    : LM.Get("papacy_crusade_idle");
                holyWar.AddListRow(MainWidth, 0, null, LM.Get("label_none"), idle, titleWidth: 110f);
            }

            List<KnightOrderData> orders = PapacySystem.Orders(religion).ToList();
            var orderCard = page.BeginCard(MainWidth, "papacy_orders_title", WideKit.Gold, orders.Count.ToString());
            if (orders.Count == 0)
                orderCard.AddListRow(MainWidth, 0, null, LM.Get("label_none"), LM.Get("papacy_orders_hint"), titleWidth: 110f);
            for (int i = 0; i < orders.Count; i++)
            {
                KnightOrderData order = orders[i];
                List<CorpsData> corps = PapacySystem.OrderCorps(order);
                string detail = string.Format(LM.Get("papacy_order_detail"),
                    PapacySystem.GrandMaster(order)?.getName() ?? LM.Get("label_none"),
                    PapacySystem.Host(order)?.GetKingdomName() ?? LM.Get("label_none"),
                    corps.Count, UIHelper.ShortCount(corps.Sum(item => item.strength)), MoneyDisplay.Format(order.treasury));
                orderCard.AddListRow(MainWidth, i, null, order.name, detail, titleWidth: 110f);
            }

            var actions = page.BeginCard(MainWidth, "papacy_actions_title", WideKit.Muted);
            actions.AddButtonRow(MainWidth,
                ("papacy_act_crown", LM.Get("papacy_act_crown"), () =>
                {
                    if (PapacySystem.ActCrown(_empire)) ShowPapacy(pArg0);
                    else ShowPapacyBlocked(PapacySystem.CrownBlocker(_empire));
                }),
                ("papacy_act_ban", LM.Get(PapacySystem.IsExcommunicated(_empire) ? "papacy_act_lift" : "papacy_act_ban"), () =>
                {
                    if (PapacySystem.ActToggleBan(_empire)) ShowPapacy(pArg0);
                    else ShowPapacyBlocked(PapacySystem.BanBlocker(_empire));
                }),
                ("papacy_act_crusade", LM.Get("papacy_act_crusade"), () => { if (PapacySystem.ActCrusade(_empire)) ShowPapacy(pArg0); }),
                ("papacy_act_order", LM.Get("papacy_act_order"), () => { if (PapacySystem.ActFoundOrder(_empire)) ShowPapacy(pArg0); }));
            // 普世之名：直接授(须在天下里、天下没有别的普世皇帝)、收回
            actions.AddButtonRow(MainWidth,
                ("papacy_act_universal", LM.Get("papacy_act_universal"), () =>
                {
                    if (RealmNamingSystem.DecreeUniversal(_empire)) ShowPapacy(pArg0);
                    else ShowPapacyBlocked("papacy_universal_blocked");
                }),
                ("papacy_act_universal_revoke", LM.Get("papacy_act_universal_revoke"), () =>
                {
                    RealmNamingSystem.DecreeRevoke(_empire);
                    ShowPapacy(pArg0);
                }));
            StartCoroutine(RefreshScrollLayout(resetToTop: true));
        }

        // 干预做不成时说明缘由(以前点了毫无动静)
        private void ShowPapacyBlocked(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            WorldTip.showNow(string.Format(LM.Get(key), _empire.GetEmpireFullName()), false, "top", 3f);
        }

        private static string Names(List<Kingdom> kingdoms) => kingdoms.Count == 0
            ? LM.Get("label_none")
            : string.Join("、", kingdoms.Select(kingdom => kingdom.GetKingdomName()));

        private static string PapacyStatus(Religion religion)
        {
            var data = religion.GetOrCreate();
            if (data.Schism) return LM.Get("papacy_state_schism");
            if (PapacySystem.IsCaptive(religion)) return LM.Get("papacy_state_captive");
            if (PapacySystem.Papacy(religion) == null) return LM.Get("papacy_see_lost");
            return LM.Get("papacy_state_free");
        }
    }
}
