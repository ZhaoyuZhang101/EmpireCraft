using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;

namespace EmpireCraft.Scripts.GeneralSystems;

// 现代国家的法理一律归中央：
//   · 成员国(省、州、自治州)不持有王国法理，只受托管理其都城所在的法理(行政授权，见 KingdomExtension.SetAdministrativeTitle)，
//     国名用该法理的省份名——无论这个成员国原先有没有法理；
//   · 成员国原来的主法理、其首领名下的法理，全部转到中央元首名下；元首更替时随之转给新元首；
//   · 成员国脱离(独立、叛乱)后不再是行政区，授权随之失效；它要取得那片法理，得按常规法理规则实际控制该地区。
// 成员国在国内不能再取得主法理(SetMainTitle 拦下，见 BlocksMemberMainTitle)。
// 每年由 ConstitutionalEconomySystem 的年度结算调用。
public static class ModernTitleCentralization
{
    private static readonly HashSet<KingdomType> ModernDivisionTypes = new()
        { KingdomType.Modern_province, KingdomType.Modern_state, KingdomType.Modern_autonomous_prefecture };

    public static bool IsModernState(Empire empire) =>
        empire?.CoreKingdom != null && !empire.isRekt() && !empire.IsArchived() &&
        empire.CoreKingdom.GetRegime()?.type == RegimeType.Modern;

    // 现代国家的成员国：王国法理归中央，自己只受托管理
    public static bool IsModernMember(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt() || kingdom.IsEmpire() || !kingdom.IsInEmpire()) return false;
        return IsModernState(kingdom.GetEmpire());
    }

    // 省、州、自治州是现代国家的行政区(与律令制的道、节度使同类：受托管理法理，用法理的省份名)
    public static bool IsModernDivisionType(Kingdom kingdom) =>
        IsModernMember(kingdom) && ModernDivisionTypes.Contains(kingdom.GetKingdomType());

    public static bool BlocksMemberMainTitle(Kingdom kingdom) => IsModernMember(kingdom);

    public static void Update(Empire empire)
    {
        if (!IsModernState(empire)) return;
        Kingdom core = empire.CoreKingdom;
        Actor head = empire.Emperor ?? core.king;
        if (head == null || head.isRekt()) return;
        var centralTitles = new HashSet<KingdomTitle>();
        KingdomTitle coreTitle = core.GetMainTitle();
        if (coreTitle != null) centralTitles.Add(coreTitle);

        foreach (Kingdom member in empire.kingdoms_list.ToList())
        {
            if (member == null || member.isRekt() || member == core) continue;
            // 成员国的主法理收归中央，改为受托管理
            KingdomTitle held = member.GetMainTitle();
            if (held != null && !held.isRekt())
            {
                member.RemoveMainTitle();
                centralTitles.Add(held);
                member.SetAdministrativeTitle(held);
            }
            // 成员国首领名下的其他法理也归中央
            Actor ruler = member.king;
            List<long> owned = ruler?.GetOwnedTitle();
            if (owned != null)
                foreach (long id in owned.ToList())
                {
                    KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(id);
                    if (title != null && !title.isRekt()) centralTitles.Add(title);
                }
            // 没有法理的成员国也受托管理都城所在的法理，国名用它的省份名
            if (member.GetMainTitle() == null && member.GetAdministrativeTitle() == null)
            {
                KingdomTitle capitalTitle = member.GetCapitalDeJureTitle();
                if (capitalTitle != null) member.SetAdministrativeTitle(capitalTitle);
            }
            KingdomTitle administered = member.GetAdministrativeTitle();
            if (administered != null) centralTitles.Add(administered);
        }
        // 前任元首(仍在中央)名下的法理转给现任
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER.list.ToList())
        {
            if (title == null || title.isRekt() || title.owner == null || title.owner == head) continue;
            if (!title.owner.isRekt() && title.owner.kingdom == core) centralTitles.Add(title);
        }
        foreach (KingdomTitle title in centralTitles) GiveToHead(title, head);
    }

    // 只挪归属名单，不走 Actor.removeTitle(那会连带改动旧主人所在王国的主法理、甚至脱离帝国)
    private static void GiveToHead(KingdomTitle title, Actor head)
    {
        if (title == null || title.data == null || title.owner == head) return;
        title.owner?.GetOwnedTitle()?.Remove(title.data.id);
        head.AddOwnedTitle(title);
    }
}
