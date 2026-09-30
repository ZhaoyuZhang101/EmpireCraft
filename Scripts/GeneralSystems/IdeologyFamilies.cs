using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

// 理念的归类与"一个政权信奉什么理念"的统一口径。
// 以前各系统各写一份(三种"社会主义"定义、五种取政权理念的回退顺序)，结果同一个国家在军阀时期、
// 朝贡废除、革命滚雪球、元首称号里被当成不同的理念。
public static class IdeologyFamilies
{
    // 社会主义本体：社会主义、共产主义(统一战线由它领导、立国即一党专政)
    public static bool IsSocialist(PartyIdeology ideology) =>
        ideology is PartyIdeology.Socialism or PartyIdeology.Communism;

    // 工人运动：社会主义本体 + 社会民主主义(工业城市吸引的对象)
    public static bool IsLabourMovement(PartyIdeology ideology) =>
        IsSocialist(ideology) || ideology == PartyIdeology.SocialDemocracy;

    // 左翼：工人运动 + 无政府主义(阶级怨气、农民革命政府的倾向)
    public static bool IsLeft(PartyIdeology ideology) =>
        IsLabourMovement(ideology) || ideology == PartyIdeology.Anarchism;

    // 农民革命：无地农民倒向的激进左翼
    public static bool IsAgrarianRadical(PartyIdeology ideology) =>
        IsSocialist(ideology) || ideology == PartyIdeology.Anarchism;

    // 自由主义：繁荣商业城市的倾向；占优后各种主义都会冒出来
    public static bool IsLiberal(PartyIdeology ideology) => ideology is PartyIdeology.SocialLiberalism or
        PartyIdeology.ConservativeLiberalism or PartyIdeology.Libertarianism or PartyIdeology.Capitalism;

    // 传统理念：繁荣城市里式微
    public static bool IsTraditional(PartyIdeology ideology) => ideology is PartyIdeology.Conservatism or
        PartyIdeology.ReligiousDemocracy or PartyIdeology.Authoritarianism;

    // 乡土传统：有地小农守着的理念
    public static bool IsAgrarianTraditional(PartyIdeology ideology) =>
        ideology is PartyIdeology.Conservatism or PartyIdeology.ReligiousDemocracy;

    // 威权：占满议席即一党专政
    public static bool IsAuthoritarian(PartyIdeology ideology) =>
        ideology is PartyIdeology.Authoritarianism or PartyIdeology.Fascism;

    // 政权信奉的理念，回退顺序：执政党 → 共和国建国理念 → 元首本人的信念 → 首都主流理念 → 中间派
    public static PartyIdeology StateIdeology(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return PartyIdeology.Centrism;
        Empire empire = kingdom.IsEmpire() ? kingdom.GetEmpire() : null;
        FixedFaction party = empire == null ? null : PartySystem.GetGovernmentParty(empire);
        if (party != null) return party.Ideology;
        if (empire != null && RepublicSystem.IsRepublic(empire) && empire.data?.constitutional_economy != null)
            return empire.data.constitutional_economy.republic_ideology;
        if (kingdom.king != null && !kingdom.king.isRekt()) return IdeologyPopulationSystem.Get(kingdom.king);
        return kingdom.capital != null && !kingdom.capital.isRekt()
            ? IdeologyPopulationSystem.GetDominant(kingdom.capital)
            : PartyIdeology.Centrism;
    }

    public static PartyIdeology StateIdeology(Empire empire) => StateIdeology(empire?.CoreKingdom);
}
