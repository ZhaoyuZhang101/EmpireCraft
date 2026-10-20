using System;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.services;

namespace EmpireCraft.Scripts.GeneralSystems;

// 阿拉伯一线的教权(用户 2026-10-19 选"罗马、阿拉伯两线，阿拉伯补教权")：没有西方那一套叙任权之争、绝罚、教皇党与皇帝党、
// 加冕远征——
//   · 册封：新君每年四成可能遣使圣地(献礼为年度经常收入的一成)，受圣地之主册封，正统同教宗加冕(+8)；圣地在本帝国手里的
//     就地受封；即位三年仍未受封正统 −5；
//   · 天课：每年把年度经常收入的 6% 交给圣地之主(圣地在本帝国手里、与圣地之主交战时不交)；
//   · 吉哈德：成员参加圣战的集结机会同西方 +10%(见 PapacySystem.Crusade)。
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
        RegimeManager.IsMonarchy(empire.CoreKingdom.GetRegime()?.type) && HolyCity(ReligionOf(empire)) != null;

    private static void CaliphalYear(Empire empire, Religion religion)
    {
        PapalRelationState state = State(empire);
        Actor ruler = empire.Emperor;
        Kingdom core = empire.CoreKingdom, seat = Papacy(religion);
        if (ruler != null && ruler.isAlive() && !IsCrowned(empire) && seat != null && !seat.isInWarWith(core))
        {
            if (seat == core || seat.GetEmpire() == empire) Crown(empire, state, ruler, self: false, seat);
            else if (UnityEngine.Random.value < CoronationChance)
            {
                int gift = (int)Math.Min(Math.Max(0L, core.GetMoney()), (long)(StateDebtSystem.AnnualStableIncome(core) * CoronationGift));
                if (gift > 0)
                {
                    core.SubMoney(gift, TreasuryCategory.Tithe);
                    seat.AddMoney(gift, TreasuryCategory.Tithe);
                }
                Crown(empire, state, ruler, self: false, seat);
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
}
