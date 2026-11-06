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
    //   页头 → 主指标(教权、加冕、叙任权、什一税) → 两栏(教廷：本国服从的教宗、圣地、教廷所在、状态；大分裂中加服从与另一位教宗 |
    //   本国与教廷：叙任权之争、绝罚、两党、国教会、普世之名、宗教改革——本国新教之城占比与最大一支)
    //   → 圣战卡 → 骑士团卡 → 按钮(加冕、绝罚或解除、号召圣战、创立骑士团)；
    //   阿拉伯一线(见 PapacySystem.Caliphate)另是一页：主指标(教权、册封、天课、对立哈里发) → 哈里发座卡(所奉的哈里发、圣地、所奉哈里发)
    //   → 对立哈里发卡(见 IslamicSchismSystem，点开其国)
    public partial class EmpireWindow
    {
        public void ShowPapacy(WindowMetaTab pArg0)
        {
            Clear();
            Religion religion = PapacySystem.ReligionOf(_empire);
            if (!PapacySystem.IsWestern(_empire) && PapacySystem.AppliesCaliphal(_empire))
            {
                ShowCaliphate(religion);
                return;
            }
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
                : PapacySystem.IsCrowned(_empire)
                    ? LM.Get(state.self_crowned ? "papacy_crown_self" : state.crowned_by_rival ? "papacy_crown_antipope" : "papacy_crown_pope")
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
            // 西方大分裂：头一行是本国服从的那位(奉对立教宗的显示对立教宗与扶立者)，另一位列在下面(见 PapacySystem.Antipope)
            PapalSchismData schism = PapacySystem.ActiveSchism(religion);
            bool antipope = schism != null && PapacySystem.FollowsAntipope(_empire);
            Actor pope = antipope ? PapacySystem.Antipope(religion) : PapacySystem.Pope(religion);
            Kingdom obeyed = antipope ? PapacySystem.AntipopeSee(religion) : papacy;
            see.AddPersonRow(HalfWidth, pope, LM.Get(antipope ? "papacy_antipope" : "papacy_pope"), pope?.getName() ?? LM.Get("label_none"),
                obeyed != null ? obeyed.GetKingdomName() : LM.Get("papacy_see_lost"));
            see.AddListRow(HalfWidth, 0, null, LM.Get("papacy_holy_city"), PapacySystem.HolyCity(religion)?.GetCityName() ?? "", titleWidth: 50f);
            see.AddListRow(HalfWidth, 1, null, LM.Get("papacy_state"), PapacyStatus(religion), titleWidth: 50f);
            if (papacy != null)
                see.AddListRow(HalfWidth, 2, null, LM.Get("papacy_see_treasury"), MoneyDisplay.Format(papacy.GetMoney()), titleWidth: 50f);
            // 教宗本人的正统(见 PopeLegitimacy)
            if (PopeLegitimacy.Applies(religion))
                see.AddListRow(HalfWidth, 3, null, LM.Get("religion_pope_legitimacy"), PopeLegitimacy.Get(religion).ToString(),
                    titleWidth: 50f);
            if (schism != null)
            {
                see.AddListRow(HalfWidth, 4, null, LM.Get("papacy_obedience"),
                    string.Format(LM.Get(antipope ? "papacy_obedience_2" : "papacy_obedience_1"),
                        antipope ? schism.see_city_name : PapacySystem.HolyCity(religion)?.GetCityName() ?? ""), titleWidth: 50f);
                Actor other = antipope ? PapacySystem.Pope(religion) : PapacySystem.Antipope(religion);
                string otherSeat = antipope ? PapacySystem.HolyCity(religion)?.GetCityName() ?? "" : schism.see_city_name;
                see.AddListRow(HalfWidth, 5, null, LM.Get(antipope ? "papacy_other_pope" : "papacy_antipope"),
                    $"{other?.getName() ?? LM.Get("label_none")} · {otherSeat}", titleWidth: 50f);
            }

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
            // 宗教改革：本国奉新教会的城占比与最大一支(见 PapacySystem.Reformation)
            (Religion reformed, float reformedShare) = PapacySystem.ReformedShare(_empire, religion);
            realm.AddListRow(HalfWidth, 6, null, LM.Get("papacy_reformation"),
                reformed != null ? string.Format(LM.Get("papacy_reformation_value"), $"{reformedShare:P0}", reformed.name) : LM.Get("label_none"),
                titleWidth: 56f);

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
            // 普世之名：直接授(须在天下里；天下已有别的普世皇帝时转授，从那家收回)、收回；神圣之名：授予(没由教宗加冕的先加冕)、去掉
            actions.AddButtonRow(MainWidth,
                ("papacy_act_holy", LM.Get(PapacySystem.HasHolyName(_empire) ? "papacy_act_holy_revoke" : "papacy_act_holy"), () =>
                {
                    if (PapacySystem.ActHoly(_empire)) ShowPapacy(pArg0);
                    else ShowPapacyBlocked(PapacySystem.HolyBlocker(_empire));
                }),
                ("papacy_act_universal", LM.Get(RealmNamingSystem.Holder(_empire) != null ? "papacy_act_universal_transfer" : "papacy_act_universal"), () =>
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

        // 阿拉伯一线：所奉的哈里发(圣地之主或对立哈里发)、册封、天课；本教的对立哈里发
        private void ShowCaliphate(Religion religion)
        {
            PapalRelationState state = PapacySystem.State(_empire);
            Kingdom seat = IslamicSchismSystem.SeatFor(_empire, religion);
            List<RivalCaliphateData> rivals = IslamicSchismSystem.ActiveRivals(religion);
            string obedience = IslamicSchismSystem.ObedienceLabel(_empire);
            if (obedience.Length == 0) obedience = LM.Get("caliph_obedience_orthodox");
            var page = BeginPage("empire_papacy", religion.name, LM.Get("religion_hierarchy_caliphate"));
            bool invested = PapacySystem.IsCrowned(_empire);
            page.AddKpiRow(MainWidth,
                (LM.Get("papacy_authority"), $"{PapacySystem.Authority(religion):0}", WideKit.Grade(PapacySystem.Authority(religion), 60, 35)),
                (LM.Get("papacy_caliph_investiture"), LM.Get(invested ? "mandate_caliphal_investiture" : "mandate_uncrowned_caliphal"),
                    invested ? WideKit.Green : WideKit.Orange),
                (LM.Get("papacy_zakat"), MoneyDisplay.Format(state.tithe_paid), WideKit.Gold),
                (LM.Get("religion_card_rival_caliphs"), rivals.Count.ToString(), rivals.Count > 0 ? WideKit.Red : WideKit.Green));

            var see = page.BeginCard(MainWidth, LM.Get("religion_hierarchy_caliphate"), WideKit.Gold);
            Actor caliph = seat?.king;
            see.AddPersonRow(MainWidth, caliph, LM.Get("religion_head_caliph"),
                caliph != null && caliph.isAlive() ? caliph.getName() : LM.Get("label_none"),
                seat != null ? seat.GetKingdomName() : LM.Get("papacy_see_lost"));
            see.AddListRow(MainWidth, 0, null, LM.Get("religion_row_obedience"), obedience, titleWidth: 80f);
            see.AddListRow(MainWidth, 1, null, LM.Get("papacy_holy_city"),
                PapacySystem.HolyCity(religion)?.GetCityName() ?? LM.Get("papacy_see_lost"), titleWidth: 80f);
            string branch = IslamicSchismSystem.BranchLabel(religion);
            if (branch.Length > 0) see.AddListRow(MainWidth, 2, null, LM.Get("religion_row_branch"), branch, titleWidth: 80f);

            var rivalCard = page.BeginCard(MainWidth, LM.Get("religion_card_rival_caliphs"), WideKit.Red, rivals.Count.ToString());
            if (rivals.Count == 0) rivalCard.AddParagraph(MainWidth, LM.Get("religion_rival_caliphs_none"), hex: WideKit.Muted);
            for (int i = 0; i < rivals.Count; i++)
            {
                Kingdom rivalSeat = IslamicSchismSystem.Seat(rivals[i]);
                rivalCard.AddListRow(MainWidth, i, null, IslamicSchismSystem.RivalSummary(rivals[i]), null,
                    rivalSeat != null ? () => KingdomOverviewWindow.Open(rivalSeat) : null, WideKit.Gold, MainWidth - 20f);
            }
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
