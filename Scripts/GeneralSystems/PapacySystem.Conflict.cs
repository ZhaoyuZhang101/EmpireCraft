using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 教廷与世俗帝国的冲突(每年随 PapacySystem.Yearly 结算)：
//   · 叙任权之争：施行了世俗等级会议(或中央集权君主制)、仍是教会叙任的帝国，每年有 15% 可能争君主叙任。
//     之争期间每年：教权越高，教宗越可能绝罚(概率 (教权-40)/60)；教权越低，皇帝越可能赢(概率 (60-教权)/60，赢了改为君主叙任，教权 -5)；
//   · 绝罚：与教廷交战(教权/100 的概率)或在叙任权之争中被绝罚。绝罚期间正统 -20、不缴什一税；
//     帝国的教皇党成员国每年有一成可能脱离帝国另立(地方独立)；
//     解除：君主换人、教权跌破 30 自动失效，或悔罪——绝罚满一年、正统不到 45 或已有成员国倒戈时，
//     每年有一半可能赴教廷悔罪(献上年度经常收入的一半，教权 +10，放弃叙任权之争)；
//   · 教皇党与皇帝党：成员国按离帝都远近(远的亲教廷)、本身是不是教皇国、统治者有没有宗教特质与一点随机偏好划分，
//     每年重算，有滞后(不轻易换边)。
public static partial class PapacySystem
{
    public const int PartyNone = 0, PartyPope = 1, PartyEmperor = 2;

    private const float ContestChance = 0.15f;
    private const float DefectChance = 0.1f;
    private const float PenanceChance = 0.5f;
    private const float PenanceCost = 0.5f;
    private const int PenanceLegitimacy = 45;
    private const float AuthorityLapse = 30f;
    private const float PartyDistance = 60f;

    public static bool IsSecularPath(Empire empire) =>
        InstitutionSystem.IsEnacted(empire, NodeSecular) || InstitutionSystem.IsEnacted(empire, "western_centralized_monarchy");

    public static bool InContest(Empire empire) => State(empire)?.investiture_contest_at >= 0d;

    private static void Conflict(Empire empire, PapalRelationState state, Religion religion)
    {
        Kingdom core = empire.CoreKingdom, papacy = Papacy(religion);
        Actor emperor = empire.Emperor;
        float authority = Authority(religion);
        // 绝罚自然失效：换了君主、教权跌破 30
        if (IsExcommunicated(empire) &&
            (emperor == null || emperor.id != state.excommunicated_ruler_id || authority < AuthorityLapse))
        {
            Lift(empire, state, LM.Get(authority < AuthorityLapse ? "papal_ban_lapsed_history" : "papal_ban_ruler_gone_history"));
        }
        if (papacy == null || papacy == core || papacy.GetEmpire() == empire || BrokeWithRome(empire)) return;
        if (IsExcommunicated(empire))
        {
            Defections(empire, religion);
            TryPenance(empire, state, religion, papacy);
            return;
        }
        // 教宗受挟：与挟持者交战的遭绝罚
        if (CaptorBan(empire, state, religion)) return;
        // 与教廷交战
        if (papacy.isInWarWith(core) && UnityEngine.Random.value < authority / 100f)
        {
            Excommunicate(empire, state, "papal_ban_reason_war");
            return;
        }
        // 叙任权之争
        if (!InContest(empire))
        {
            if (IsSecularPath(empire) && Investiture(empire) != InvestitureCrown && UnityEngine.Random.value < ContestChance)
            {
                state.investiture_contest_at = World.world.getCurWorldTime();
                Record(empire, string.Format(LM.Get("papal_contest_started_history"), empire.GetEmpireFullName(),
                    Pope(religion)?.getName() ?? LM.Get("label_none")));
            }
            return;
        }
        if (UnityEngine.Random.value < (authority - 40f) / 60f)
        {
            Excommunicate(empire, state, "papal_ban_reason_investiture");
            return;
        }
        if (UnityEngine.Random.value < (60f - authority) / 60f)
        {
            state.investiture_contest_at = -1d;
            state.investiture_won = true;
            AddAuthority(religion, -5f);
            Record(empire, string.Format(LM.Get("papal_contest_won_history"), empire.GetEmpireFullName()));
        }
    }

    public static void Excommunicate(Empire empire, PapalRelationState state, string reasonKey)
    {
        Actor emperor = empire.Emperor;
        if (emperor == null || IsExcommunicated(empire)) return;
        Religion religion = ReligionOf(empire);
        state.excommunicated_at = World.world.getCurWorldTime();
        state.excommunicated_ruler_id = emperor.id;
        state.excommunication_reason = reasonKey;
        Record(empire, string.Format(LM.Get("papal_ban_history"), Pope(religion)?.getName() ?? LM.Get("label_none"),
            emperor.getName(), empire.GetEmpireFullName(), LM.Get(reasonKey)));
        emperor.RecordPersonalHistory(string.Format(LM.Get("personal_history_excommunicated"), LM.Get(reasonKey)));
        MonarchyLegitimacy.Invalidate(empire);
    }

    private static void Lift(Empire empire, PapalRelationState state, string text)
    {
        state.excommunicated_at = -1d;
        state.excommunicated_ruler_id = -1L;
        state.excommunication_reason = "";
        Record(empire, text.Contains("{0}") ? string.Format(text, empire.GetEmpireFullName()) : text);
        MonarchyLegitimacy.Invalidate(empire);
    }

    // 悔罪(卡诺莎)：献上年度经常收入的一半，教权 +10，放弃叙任权之争
    private static void TryPenance(Empire empire, PapalRelationState state, Religion religion, Kingdom papacy)
    {
        if (Date.getYearsSince(state.excommunicated_at) < 1) return;
        bool desperate = empire.Legitimacy < PenanceLegitimacy || PartyMembers(empire, PartyPope).Count == 0 ||
                         State(empire).defections > 0;
        if (!desperate || UnityEngine.Random.value >= PenanceChance) return;
        Penance(empire, state, religion, papacy);
    }

    public static void Penance(Empire empire, PapalRelationState state, Religion religion, Kingdom papacy)
    {
        Kingdom core = empire.CoreKingdom;
        long cost = Math.Min(Math.Max(0L, (long)core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * PenanceCost));
        if (cost > 0 && papacy != null)
        {
            core.SubMoney((int)cost, TreasuryCategory.Tithe);
            papacy.AddMoney((int)cost, TreasuryCategory.Tithe);
        }
        AddAuthority(religion, 10f);
        state.investiture_contest_at = -1d;
        state.investiture_won = false;
        state.defections = 0;
        Lift(empire, state, string.Format(LM.Get("papal_penance_history"), empire.Emperor?.getName() ?? "",
            HolyCity(religion)?.GetCityName() ?? "", empire.GetEmpireFullName()));
    }

    // 绝罚期间教皇党成员国倒戈：离开帝国另立(地方独立)
    private static void Defections(Empire empire, Religion religion)
    {
        Kingdom core = empire.CoreKingdom;
        foreach (Kingdom member in PartyMembers(empire, PartyPope))
        {
            if (member.hasEnemies() || UnityEngine.Random.value >= DefectChance) continue;
            War war = DiplomacyHelpers.wars.newWar(member, core, WarTypeLibrary.normal);
            if (war == null) continue;
            empire.leave(member, true, true);
            war.SetEmpireWarType(EmpireWarType.地方独立);
            State(empire).defections++;
            Record(empire, string.Format(LM.Get("papal_guelph_defection_history"), member.GetKingdomFullName(),
                empire.GetEmpireFullName()));
        }
    }

    // 两党：离帝都越远越亲教廷；教皇国、统治者有宗教特质的亲教廷；有滞后，不轻易换边
    private static void UpdateParties(Empire empire)
    {
        Kingdom core = empire.CoreKingdom;
        City seat = core.capital;
        if (seat == null) return;
        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member?.data == null || member.isRekt() || member == core || member.capital == null) continue;
            var data = member.GetOrCreate();
            float distance = Vector2.Distance(member.capital.city_center, seat.city_center) / 8f;
            float lean = Mathf.Clamp01(distance / PartyDistance) * 0.5f;
            if (member.GetKingdomType() == KingdomType.Feudalism_papal_state) lean += 0.6f;
            Actor king = member.king;
            if (king != null && (king.hasTrait("pious") || king.hasTrait("religious") || king.hasTrait("devout"))) lean += 0.25f;
            lean += ((member.id * 7919L) % 100) / 100f * 0.3f;
            int party = data.papal_party;
            if (party == PartyPope && lean < 0.4f) party = PartyEmperor;
            else if (party == PartyEmperor && lean > 0.6f) party = PartyPope;
            else if (party == PartyNone) party = lean >= 0.5f ? PartyPope : PartyEmperor;
            data.papal_party = party;
        }
    }

    public static List<Kingdom> PartyMembers(Empire empire, int party) =>
        empire?.kingdoms_list?.Where(kingdom => kingdom?.data != null && !kingdom.isRekt() && kingdom != empire.CoreKingdom &&
                                                kingdom.GetOrCreate().papal_party == party).ToList() ?? new List<Kingdom>();

    public static int Party(Kingdom kingdom)
    {
        Empire empire = kingdom?.GetEmpire();
        return empire == null || !Applies(empire) || kingdom == empire.CoreKingdom ? PartyNone : kingdom.GetOrCreate().papal_party;
    }

    private static void Record(Empire empire, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        empire.RecordHistory(directContent: text, actorId: empire.Emperor?.id ?? -1L, kingdomId: empire.CoreKingdom.id);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
    }
}
