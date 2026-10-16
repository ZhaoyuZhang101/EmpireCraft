using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.GameClassExtensions;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 华夏原始阶段的氏族(用户 2026-10-16："华夏缺少原始体制""早期是母系社会？部落联合还是什么"，选定：完整五段、随母入氏、女字旁古姓)：
//   制度线 母系氏族 → 父系氏族 → 部落联盟(禅让) → 家天下(夏商方国) → 宗法(周)，前四段用原始政体，宗法起才是周制分封；
//   母系氏族阶段(制度特性 matrilineal_descent)：孩子入母亲的氏族、随母姓(见 SpecificClan.Matrilineal、SurnameInheritanceSystem)；
//     新立的氏族取姬、姜、姒、嬴这类带女字旁的古姓；进入父系氏族后改为随父，已有的古姓照旧传下去。
//
// 姓氏的演变(用户 2026-10-16："早期如何体现出姓氏的发展"，选定：完整演变、姓 = 宗族、氏 = 支脉、无姓只显示名)：
//   · 原始群(制度特性 no_surname)：无姓，人只有名；族谱里仍记着所属氏族(取古姓)，到有姓的时代显示出来；
//   · 母系氏族：有了"姓"(姬、姜、姒、嬴……)，人人称姓；
//   · 父系氏族至家天下：贵族从姓里分出"氏"——宗族分支(受封以国为氏、迁居以邑为氏、五世以王父之名为氏)
//     记下自己的氏，分支里的男子称氏、女子称姓，其余仍称姓；
//   · 宗法(周制)：男子称氏、女子称姓；没有氏的平民(贵族、官吏、君主、城主以外)只称名；
//   · 郡县(郡国并行或郡县官僚，制度特性 surname_merged)：姓氏合一，氏就成了姓，百姓皆有姓。
// 只改人物显示的名字，族谱里的姓(宗族)不变；时代变了每年把本文化在世的人重新起一次名。
public static class PrehistoricClanSystem
{
    public const string MatrilinealFeature = "matrilineal_descent";
    public const string NoSurnameFeature = "no_surname";
    public const string SurnameMergedFeature = "surname_merged";
    private const float CacheSeconds = 5f;
    private static readonly Dictionary<string, (float until, bool value)> Cache = new();
    private static readonly Dictionary<string, (float until, SurnameEra era)> EraCache = new();
    private static readonly Dictionary<string, SurnameEra> LastEra = new();

    public enum SurnameEra { None, Xing, ShiEmerging, ZhouShi, Merged }

    private static readonly Dictionary<string, List<string>> AncientPools = new();

    // 本文化的古姓词库(文化配置 Clan.ancient_group，华夏是姬姜姒嬴这类女字旁古姓)；没配为空
    private static List<string> AncientPool(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return new List<string>();
        if (AncientPools.TryGetValue(culture, out List<string> cached)) return cached;
        string group = OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) ? setting?.Clan?.ancient_group : null;
        List<string> pool = string.IsNullOrWhiteSpace(group) ? new List<string>() : PartySystem.GetCultureWordPool(culture, group);
        AncientPools[culture] = pool;
        return pool;
    }

    // 这个文化是否走姓氏演变：华夏线、且文化配置开了 surname_evolution
    private static bool Evolves(string culture) =>
        InstitutionSystem.GetCultureLine(culture) == "Huaxia" &&
        OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting) && setting?.surname_evolution == true;

    public static bool IsMatrilineal(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return false;
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(culture, out var hit) && hit.until > now) return hit.value;
        bool value;
        try
        {
            value = CultureService.IsValidCulture(culture) && InstitutionSystem.GetFeature(culture, MatrilinealFeature) > 0f;
        }
        catch
        {
            value = false;
        }
        Cache[culture] = (now + CacheSeconds, value);
        return value;
    }

    public static void ResetWorldState()
    {
        Cache.Clear();
        EraCache.Clear();
        LastEra.Clear();
    }

    public static SurnameEra EraOf(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return SurnameEra.Merged;
        float now = Time.unscaledTime;
        if (EraCache.TryGetValue(culture, out var hit) && hit.until > now) return hit.era;
        SurnameEra era;
        try
        {
            era = !CultureService.IsValidCulture(culture) || !Evolves(culture) ||
                  InstitutionSystem.GetFeature(culture, SurnameMergedFeature) > 0f ? SurnameEra.Merged
                : InstitutionSystem.IsEnacted(culture, "huaxia_patriarchal_clan") ? SurnameEra.ZhouShi
                : InstitutionSystem.GetFeature(culture, NoSurnameFeature) > 0f ? SurnameEra.None
                : InstitutionSystem.GetFeature(culture, MatrilinealFeature) > 0f ? SurnameEra.Xing
                : SurnameEra.ShiEmerging;
        }
        catch
        {
            era = SurnameEra.Merged;
        }
        EraCache[culture] = (now + CacheSeconds, era);
        return era;
    }

    // 新立的氏族取古姓：原始群、母系氏族阶段，且本文化配了古姓词库
    public static bool AncientNaming(string culture) =>
        EraOf(culture) is SurnameEra.None or SurnameEra.Xing && AncientPool(culture).Count > 0;

    // 分支的氏还能不能新起(姓氏合一以后，分支沿用宗族的姓)
    public static bool ShiForming(string culture) => EraOf(culture) is SurnameEra.ShiEmerging or SurnameEra.ZhouShi;

    // 由分支的来历取氏：受封的国名、所居的城名、房祖的名，取首字
    public static string ShiFrom(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";
        string trimmed = source.Trim();
        return char.IsSurrogate(trimmed[0]) && trimmed.Length > 1 ? trimmed.Substring(0, 2) : trimmed.Substring(0, 1);
    }

    // 族谱修正用：名字上显示的不是族谱的姓，是不是姓氏演变造成的(称氏、无姓)，是就不算错
    public static bool ShownByEra(PersonalClanIdentity person, string shown) =>
        person != null && (EraOf(person.culture) != SurnameEra.Merged || shown == ShiOf(person._specificClan));

    // 本人(或所在分支的上一层分支)的氏；没有返回 null
    private static string ShiOf(Actor actor) => ShiOf(actor?.GetPersonalIdentity()?._specificClan);

    private static string ShiOf(SpecificClan clan)
    {
        for (int guard = 0; clan != null && guard < 8; guard++)
        {
            if (!string.IsNullOrEmpty(clan.shi)) return clan.shi;
            clan = clan.parent_clan_id > 0 ? SpecificClanManager.Get(clan.parent_clan_id) : null;
        }
        return null;
    }

    private static bool Noble(Actor actor)
    {
        if (actor.isKing() || actor.isCityLeader()) return true;
        SocialClass socialClass = actor.GetOrCreate().socialClass;
        return socialClass is SocialClass.Noble or SocialClass.Officer;
    }

    // 名字上显示的姓(或氏)；返回空串表示只称名
    public static string DisplaySurname(Actor actor, string surname)
    {
        if (actor?.data == null || string.IsNullOrEmpty(surname)) return surname;
        switch (EraOf(TechnologySystem.GetCultureOf(actor)))
        {
            case SurnameEra.None:
                return "";
            case SurnameEra.Xing:
                return surname;
            case SurnameEra.ShiEmerging:
                return actor.isSexMale() && ShiOf(actor) is string emerging ? emerging : surname;
            case SurnameEra.ZhouShi:
                if (actor.isSexMale() && ShiOf(actor) is string shi) return shi;
                return !actor.isSexMale() || Noble(actor) ? surname : "";
            default:
                return ShiOf(actor) ?? surname;
        }
    }

    // 每年(科技年度结算时)看本文化的姓氏时代变没变，变了就把本文化在世的人重新起一次名
    public static void YearlyCheck(string culture)
    {
        if (string.IsNullOrEmpty(culture) || World.world?.units == null) return;
        SurnameEra era = EraOf(culture);
        if (!LastEra.TryGetValue(culture, out SurnameEra last))
        {
            LastEra[culture] = era;
            if (era == SurnameEra.Merged) return;
        }
        else if (last == era) return;
        LastEra[culture] = era;
        int renamed = 0;
        foreach (Actor actor in World.world.units.ToList())
        {
            if (actor?.data == null || actor.isRekt() || !actor.isAlive() || TechnologySystem.GetCultureOf(actor) != culture) continue;
            Name name = actor.GetModName();
            if (name == null || !name.hasFamilyName(actor)) continue;
            name.SetName(actor);
            renamed++;
        }
        if (renamed > 0)
            NeoModLoader.services.LogService.LogInfo($"[EmpireCraft][姓氏演变] {culture} 进入 {era}，重新起名 {renamed} 人");
    }

    // 新氏族的古姓：从本文化的古姓词库里取，优先取还没被用掉的
    public static string PickAncientSurname(string culture)
    {
        List<string> all = AncientPool(culture);
        if (all.Count == 0) return null;
        HashSet<string> used = SpecificClanManager.ClanNames();
        List<string> free = all.Where(name => !used.Contains(name)).ToList();
        List<string> pool = free.Count > 0 ? free : all;
        return pool[Random.Range(0, pool.Count)];
    }
}
