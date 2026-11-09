using System;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NeoModLoader.General;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 阿拉伯一线的教权(用户 2026-10-19 选"罗马、阿拉伯两线，阿拉伯补教权")：没有西方那一套叙任权之争、绝罚、教皇党与皇帝党、
// 加冕远征——
//   · 册封：新君每年四成可能遣使所奉的哈里发(献礼为年度经常收入的一成)，受其册封，正统同教宗加冕(+8)；哈里发座在本帝国手里的
//     就地受封；即位三年仍未受封正统 −5；
//   · 天课：每年把年度经常收入的 6% 交给所奉的哈里发(哈里发座在本帝国手里、与之交战时不交)；
//   · 吉哈德：成员参加圣战的集结机会同西方 +10%(见 PapacySystem.Crusade)。
// 所奉的哈里发(用户 2026-11-06 选"伊斯兰分裂更多样")：平常是圣地之主；本教有对立哈里发时各哈里发国择一而奉(见 IslamicSchismSystem)，
//   奉对立哈里发的向他求册封、交天课，册封记作"受对立哈里发册封"，不加本教教权；对立哈里发本国的新君就地嗣位。
//   只奉对立哈里发而本教圣地已失的帝国照样在册封、天课之列。
// 法教传统(印度，文化特性 dharmic)不在此列，也不发起圣战。
public static partial class PapacySystem
{
    public const string DharmicTrait = "dharmic";

    public static bool IsCaliphal(Empire empire)
    {
        if (empire?.data == null) return false;
        // 宗教亲和照风俗文化(见 InstitutionSystem.GetCustomsCulture)
        string culture = InstitutionSystem.GetCustomsCulture(empire);
        return InstitutionSystem.GetCultureLine(culture) == "Arabic" && !culture.HasPoliticalTrait(DharmicTrait);
    }

    // 有圣战传统的文化：罗马一线、阿拉伯一线(法教传统的除外)
    public static bool WagesHolyWar(string culture)
    {
        if (!CultureService.IsValidCulture(culture)) return false;
        string line = InstitutionSystem.GetCultureLine(culture);
        return line == Line || line == "Arabic" && !culture.HasPoliticalTrait(DharmicTrait);
    }

    public static bool AppliesCaliphal(Empire empire) =>
        empire?.data != null && !empire.isRekt() && !empire.IsArchived() && empire.CoreKingdom != null &&
        !empire.CoreKingdom.isRekt() && IsCaliphal(empire) && !RepublicSystem.IsRepublic(empire) &&
        RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type) &&
        (HolyCity(ReligionOf(empire)) != null || IslamicSchismSystem.ObedienceOf(empire, ReligionOf(empire)) != null) &&
        ReligionFaith.IsCaliphal(ReligionOf(empire)) && HasSee(ReligionOf(empire));

    private static void CaliphalYear(Empire empire, Religion religion)
    {
        PapalRelationState state = State(empire);
        Actor ruler = empire.Emperor;
        Kingdom core = empire.CoreKingdom;
        // 所奉的哈里发：对立哈里发(见 IslamicSchismSystem)或圣地之主
        RivalCaliphateData rival = IslamicSchismSystem.ObedienceOf(empire, religion);
        Kingdom seat = rival != null ? IslamicSchismSystem.SeatFor(empire, religion) : Papacy(religion);
        if (ruler != null && ruler.isAlive() && !IsCrowned(empire) && seat != null && !seat.isInWarWith(core))
        {
            bool home = seat == core || seat.GetEmpire() == empire;
            if (home || UnityEngine.Random.value < CoronationChance)
            {
                int gift = home ? 0 : (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * CoronationGift));
                if (gift > 0)
                {
                    core.SubMoney(gift, TreasuryCategory.Tithe);
                    seat.AddMoney(gift, TreasuryCategory.Tithe);
                }
                if (rival != null) InvestByRival(empire, state, ruler, seat, home);
                else Crown(empire, state, ruler, self: false, seat);
            }
        }
        // 天课
        double now = World.world.getCurWorldTime();
        if (state.last_tithe_at >= 0d && now >= state.last_tithe_at && Date.getYearsSince(state.last_tithe_at) < 1) return;
        state.last_tithe_at = now;
        state.tithe_paid = 0;
        if (seat == null || seat == core || seat.GetEmpire() == empire || seat.isInWarWith(core)) return;
        long amount = (long)(StateDebtSystem.AnnualStableIncome(core) * TitheRate);
        amount = Math.Min(amount, Math.Max(0L, (long)core.GetMoney()));
        if (amount <= 0) return;
        core.SubMoney((int)amount, TreasuryCategory.Tithe);
        seat.AddMoney((int)amount, TreasuryCategory.Tithe);
        state.tithe_paid = amount;
    }

    // 受对立哈里发册封(或对立哈里发本国的新君就地嗣位)：同 Crown，但不加本教教权，文字写对立哈里发
    private static void InvestByRival(Empire empire, PapalRelationState state, Actor ruler, Kingdom seat, bool home)
    {
        state.crowned_emperor_id = ruler.id;
        state.self_crowned = false;
        state.crowned_at = World.world.getCurWorldTime();
        Actor caliph = seat.king;
        string text = home
            ? string.Format(LM.Get("rival_caliph_succession_history"), ruler.getName(), empire.GetEmpireFullName())
            : string.Format(LM.Get("rival_caliph_investiture_history"), ruler.getName(), seat.capital?.GetCityName() ?? seat.GetKingdomName(),
                caliph?.data != null && caliph.isAlive() ? caliph.getName() : LM.Get("label_none"), empire.GetEmpireFullName());
        empire.RecordHistory(directContent: text, actorId: ruler.id, kingdomId: empire.CoreKingdom.id);
        TranslateHelper.LogEventMessage(text, empire.CoreKingdom);
        ruler.RecordPersonalHistory(text);
        ReligionChronicle.Record(ReligionOf(empire), ReligionChronicle.KindPapacy, text);
        HolyRomanSystem.OnCrowned(empire, ruler, false);
        MonarchyLegitimacy.Invalidate(empire);
    }
}
