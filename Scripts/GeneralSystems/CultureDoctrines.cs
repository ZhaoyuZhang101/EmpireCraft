using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Layer;
using EmpireCraft.Scripts.Regimes;
using NeoModLoader.General;
using UnityEngine;

namespace EmpireCraft.Scripts.GeneralSystems;

// 各文化线的古代学说(用户 2026-10-20 转玩家截图：西方帝国的皇权派、封建派、神权派都写"宗儒家之学"——"派系的保守主义理念应该依据
// 不同的文化做出适配，不是所有文化都有华夏的那些"；问答定"每条文化线一套古代学说")：
//   · 机制不变：仍是 PartyIdeology 的古代理念(阶层基础、光谱坐标、诉求、派系类型照旧)，只是名字、说明按文化线换。
//     如儒家那一格(官僚、地主，守秩序、开科取士)在西方是经院正统、阿拉伯是教法派、草原是祖训派；
//   · 谱系：华夏线是诸子百家(原来的键)；罗马线(西方各族)、阿拉伯线(阿拉伯、埃及、波斯)、日本线各一套，
//     阿拉伯线里的印度、游牧线里的阿兹特克与林地部族(奥吉布瓦)另有一套，游牧线其余的(草原)用游牧那套；
//   · 某谱系没有的学说就不出现(西方、草原、日本没有宦官，林地部族没有法令、匠作与宗亲外戚)：选理念只列本谱系有的，
//     旧档里派系记着本谱系没有的就近换成本谱系有的(只换派系记的理念，成员、占比、诉求不动)；
//   · 文字键：<键>_<文化小写>(个别文化，如维京、波斯) → <键>_<谱系> → <键>_other(非华夏通用格式) → <键>(华夏原名)；
//   · 民间信众照旧用原名：民间流传诸子百家的只有华夏线(见 IdeologyPopulationSystem.AddPremodern)，信儒家的人就是儒家
public static class CultureDoctrines
{
    public const string Huaxia = "huaxia";

    // 各谱系没有的古代理念(未列的谱系样样都有)
    private static readonly Dictionary<string, HashSet<PartyIdeology>> Missing = new()
    {
        ["roma"] = new() { PartyIdeology.Eunuchs },
        ["japan"] = new() { PartyIdeology.Eunuchs },
        ["youmu"] = new() { PartyIdeology.Eunuchs },
        ["india"] = new() { PartyIdeology.Eunuchs },
        ["aztec"] = new() { PartyIdeology.Eunuchs },
        ["ojibwe"] = new()
        {
            PartyIdeology.Legalism, PartyIdeology.Mohism, PartyIdeology.Eunuchs, PartyIdeology.ConsortKin,
            PartyIdeology.RoyalKin
        },
        ["arabic"] = new()
    };

    // 谱系的显示顺序(理念图鉴里"别的文化怎么叫")
    public static readonly string[] Sets = { "roma", "arabic", "india", "youmu", "aztec", "ojibwe", "japan" };

    // 同一条制度线里另有一套的文化(印度走阿拉伯线，阿兹特克、奥吉布瓦走游牧线)
    private static readonly HashSet<string> CultureSets = new(StringComparer.Ordinal) { "india", "aztec", "ojibwe" };

    private static readonly Dictionary<string, string> SetCache = new(StringComparer.Ordinal);

    // 文化 → 谱系：文化本身另有一套的用自己的，否则按制度线；取不到文化当华夏(原名)
    public static string SetOf(string culture)
    {
        if (string.IsNullOrEmpty(culture)) return Huaxia;
        if (SetCache.TryGetValue(culture, out string cached)) return cached;
        if (!CultureService.IsValidCulture(culture)) return Huaxia;
        string set;
        string own = culture.ToLowerInvariant();
        if (CultureSets.Contains(own)) set = own;
        else
        {
            string line;
            try
            {
                line = InstitutionSystem.GetCultureLine(culture);
            }
            catch
            {
                line = "";
            }
            set = line switch
            {
                "Huaxia" => Huaxia,
                "Arabic" => "arabic",
                "Youmu" => "youmu",
                "Japan" => "japan",
                _ => "roma"
            };
        }
        SetCache[culture] = set;
        return set;
    }

    public static bool IsHuaxia(string culture) => SetOf(culture) == Huaxia;

    // 这一谱系有没有这个古代理念(近代理念都有)
    public static bool Has(string set, PartyIdeology ideology) =>
        !IdeologyFamilies.IsAncient(ideology) || !Missing.TryGetValue(set ?? Huaxia, out HashSet<PartyIdeology> missing) ||
        !missing.Contains(ideology);

    public static bool HasIn(string culture, PartyIdeology ideology) => Has(SetOf(culture), ideology);

    // 学说是否已在本文化出现：随科技(TechTree.json 的 ideology:<理念>)陆续出现；文化取不到时不拦
    public static bool Emerged(string culture, PartyIdeology ideology) =>
        !IdeologyFamilies.IsSchool(ideology) || !CultureService.IsValidCulture(culture) ||
        PartySystem.IsResearched(culture, ideology) ||
        TechnologySystem.AreFeatureTechsMet(culture, IdeologySpreadSystem.FeatureKey(ideology));

    #region 取文化

    // 帝国：制度文化(同制度树)
    public static string CultureOf(Empire empire)
    {
        if (empire?.data == null || empire.isRekt() || empire.CoreKingdom == null) return null;
        try
        {
            return InstitutionSystem.GetPrimaryCulture(empire);
        }
        catch
        {
            return null;
        }
    }

    // 国家：帝国核心按帝国的制度文化，其余按本国文化(同 FactionEditor 的朝堂)
    public static string CultureOf(Kingdom kingdom)
    {
        if (kingdom?.data == null || kingdom.isRekt()) return null;
        Empire empire = FactionEditor.EmpireOf(kingdom);
        return empire != null ? CultureOf(empire) : CultureService.GetRealmCulture(kingdom);
    }

    // 派系：所在帝国(不挂帝国的取不到，按原名)
    public static string CultureOf(FixedFaction faction)
    {
        if (faction == null || faction.EmpireId < 0) return null;
        Empire empire = faction.Empire;
        return empire == null || empire.IsArchived() ? null : CultureOf(empire);
    }

    #endregion

    #region 文字

    // 文字键按文化 → 谱系 → 非华夏通用 → 原键依次找
    public static string Key(string baseKey, string culture)
    {
        if (string.IsNullOrEmpty(baseKey) || string.IsNullOrEmpty(culture)) return baseKey;
        string set = SetOf(culture);
        string own = baseKey + "_" + culture.ToLowerInvariant();
        if (LM.Has(own)) return own;
        if (set == Huaxia) return baseKey;
        string bySet = baseKey + "_" + set;
        if (LM.Has(bySet)) return bySet;
        string other = baseKey + "_other";
        return LM.Has(other) ? other : baseKey;
    }

    // 学说名：古代理念按文化换说法，近代理念照旧
    public static string Name(PartyIdeology ideology, string culture) =>
        LM.Get(IdeologyFamilies.IsAncient(ideology) ? Key($"party_ideology_{ideology}", culture) : $"party_ideology_{ideology}");

    // 派系默认名(史书称号)：华夏是清流、变法派……；别的谱系有专门称号的用称号，没有就用学说名
    public static string Label(PartyIdeology ideology, string culture)
    {
        string baseKey = "clique_label_" + ideology;
        string key = Key(baseKey, culture);
        if (key == baseKey && !IsHuaxia(culture) && IdeologyFamilies.IsAncient(ideology)) return Name(ideology, culture);
        return LM.Get(key);
    }

    // 说明(理念图鉴、派系的理念选择)
    public static string Description(PartyIdeology ideology, string culture)
    {
        string snake = string.Concat(ideology.ToString().Select((ch, index) =>
            index > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
        string baseKey = $"institution_ideology_{snake}_desc";
        string key = IdeologyFamilies.IsAncient(ideology) ? Key(baseKey, culture) : baseKey;
        string text = LM.Get(key);
        return string.IsNullOrWhiteSpace(text) || text == key ? "" : text;
    }

    // 理念图鉴：这一格在别的谱系叫什么(华夏的原名不列；某谱系没有的不列)
    public static string OtherNames(PartyIdeology ideology)
    {
        if (!IdeologyFamilies.IsAncient(ideology) || ideology == PartyIdeology.Animism) return "";
        var parts = new List<string>();
        foreach (string set in Sets)
        {
            if (!Has(set, ideology)) continue;
            string key = $"party_ideology_{ideology}_{set}";
            if (!LM.Has(key)) continue;
            parts.Add($"{LM.Get("doctrine_set_" + set)}·{LM.Get(key)}");
        }
        return string.Join("、", parts);
    }

    #endregion

    #region 旧档

    // 手选了理念的派系记着本谱系没有的古代理念(旧档里西方派系选了宦官、派系库里存的派系加进林地部族……)：按阶层坐标就近
    // 换成本谱系有的，优先同类(学说换学说、身份立场换身份立场)。只换派系记的理念，成员、占比、诉求不动。返回是否换了。
    // 华夏朋党由 CliqueSystem 按它自己的规矩管(阉党、后党是常设的阶级派系)，这里不碰
    public static bool Normalize(FixedFaction faction)
    {
        if (faction == null || faction.IsParty || faction.IsClique || !faction.IdeologyChosen ||
            !IdeologyFamilies.IsAncient(faction.Ideology)) return false;
        string culture = CultureOf(faction);
        if (string.IsNullOrEmpty(culture)) return false;
        string set = SetOf(culture);
        if (Has(set, faction.Ideology)) return false;
        PartyIdeology old = faction.Ideology;
        Vector2 position = PartySystem.ClassCenter(faction) ?? PartySystem.GetPosition(old);
        bool stance = IdeologyFamilies.IsIdentityStance(old);
        bool school = IdeologyFamilies.IsSchool(old);
        List<PartyIdeology> candidates = FactionEditor.Ancient.Where(ideology => Has(set, ideology) &&
            IdeologyFamilies.IsIdentityStance(ideology) == stance && IdeologyFamilies.IsSchool(ideology) == school).ToList();
        if (candidates.Count == 0) candidates = FactionEditor.Ancient.Where(ideology => Has(set, ideology)).ToList();
        if (candidates.Count == 0) return false;
        faction.Ideology = candidates.OrderBy(ideology => Vector2.Distance(PartySystem.GetPosition(ideology), position)).First();
        return true;
    }

    #endregion
}
