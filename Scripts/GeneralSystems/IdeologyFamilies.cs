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

    // 古代理念(诸子百家与古代政治立场，见 PartySystem.PartyIdeology)
    public static bool IsAncient(PartyIdeology ideology) => ideology >= PartyIdeology.Confucianism;

    // 诸子百家(只有华夏有)
    public static bool IsSchool(PartyIdeology ideology) =>
        ideology is >= PartyIdeology.Confucianism and <= PartyIdeology.YinYang;

    // 身份立场(用户 2026-10-17："要不要把皇权和诸侯派信的东西改一改，应该是他们本身代表这些阶级")：
    // 皇权(宗室与寒门近臣)、门阀(贵族与世家)、外戚、宦官、宗亲、军功(将领与武职)——不是民间流传的学说，派系按人的身份招人(见 CliqueSystem.IdentityStance)
    public static bool IsIdentityStance(PartyIdeology ideology) =>
        ideology is PartyIdeology.ImperialPower or PartyIdeology.GreatClans || IsCourtOnly(ideology);

    // 派系卡片第二行：身份立场写"代表某某"，诸子百家写"宗某之学"(学说渊源)，其余写"倾向某理念"
    public static string FactionLeaning(PartyIdeology ideology)
    {
        if (IsIdentityStance(ideology)) return NeoModLoader.General.LM.Get("faction_represents_" + ideology);
        string key = IsSchool(ideology) ? "faction_school_doctrine" : "faction_ideology_leaning";
        return string.Format(NeoModLoader.General.LM.Get(key), PartySystem.GetIdeologyName(ideology));
    }

    // 只在朝堂里的立场(外戚、宦官、宗亲、军功)：不在民间流传
    public static bool IsCourtOnly(PartyIdeology ideology) =>
        ideology is PartyIdeology.ConsortKin or PartyIdeology.Eunuchs or PartyIdeology.RoyalKin or PartyIdeology.MilitaryMerit;

    // 传统理念：繁荣城市里式微；古代理念都算传统(不靠印行的书报传播)
    public static bool IsTraditional(PartyIdeology ideology) => ideology is PartyIdeology.Conservatism or
        PartyIdeology.ReligiousDemocracy or PartyIdeology.Authoritarianism || IsAncient(ideology);

    // 乡土传统：有地小农守着的理念
    public static bool IsAgrarianTraditional(PartyIdeology ideology) =>
        ideology is PartyIdeology.Conservatism or PartyIdeology.ReligiousDemocracy or PartyIdeology.Agrarianism or
            PartyIdeology.HuangLao;

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
